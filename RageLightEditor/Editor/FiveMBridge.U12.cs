using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RageLightEditor.Editor
{
    public class FiveMClient : MloBridgeClient
    {
        public NetworkStream Stream;
        public string Player = "";

        public override void Send(string line)
        {
            if (Closed || Stream == null) return;
            try
            {
                var frame = FiveMBridge.EncodeFrame(line);
                lock (this) { Stream.Write(frame, 0, frame.Length); Stream.Flush(); }
            }
            catch { Closed = true; }
        }
    }

    public class FiveMBridge : IDisposable
    {
        public const int DefaultPort = 27018;
        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public int Port { get; private set; } = DefaultPort;
        public bool Listening => listener != null;
        public bool AnyInterface { get; private set; }
        public string Error = "";
        public int Received, Sent;
        public DateTime LastAt;

        private TcpListener listener;
        private readonly List<FiveMClient> clients = new List<FiveMClient>();
        private readonly ConcurrentQueue<MloBridgeMessage> inbox = new ConcurrentQueue<MloBridgeMessage>();
        private readonly ConcurrentQueue<string> notes = new ConcurrentQueue<string>();

        public int Clients { get { lock (clients) return clients.Count; } }

        public bool Start(int port, bool anyInterface = false)
        {
            Stop();
            Error = "";
            try
            {
                AnyInterface = anyInterface;
                listener = new TcpListener(anyInterface ? IPAddress.Any : IPAddress.Loopback, Math.Max(port, 0));
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var t = new Thread(AcceptLoop) { IsBackground = true, Name = "FiveM link accept" };
                t.Start();
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                listener = null;
                return false;
            }
        }

        public void Stop()
        {
            var l = listener;
            listener = null;
            try { l?.Stop(); } catch { }
            lock (clients)
            {
                foreach (var c in clients) { c.Closed = true; try { c.Tcp?.Close(); } catch { } }
                clients.Clear();
            }
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            var l = listener;
            while (l != null && ReferenceEquals(l, listener))
            {
                TcpClient tcp;
                try { tcp = l.AcceptTcpClient(); }
                catch { break; }
                var t = new Thread(() => ClientLoop(tcp)) { IsBackground = true, Name = "FiveM link client" };
                t.Start();
            }
        }

        private void ClientLoop(TcpClient tcp)
        {
            FiveMClient client = null;
            try
            {
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                stream.ReadTimeout = 5000;
                var head = ReadHead(stream);
                if (head == null) { tcp.Close(); return; }
                var headers = ParseHeaders(head, out string requestLine);
                if (!headers.TryGetValue("sec-websocket-key", out var key) ||
                    !headers.TryGetValue("upgrade", out var up) || !up.Trim().Equals("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    var body = Encoding.UTF8.GetBytes("RAGE Tools FiveM live link is listening here. The game's ragetools_linking resource connects to it by WebSocket.\n");
                    var reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(reply, 0, reply.Length);
                    stream.Write(body, 0, body.Length);
                    stream.Flush();
                    tcp.Close();
                    return;
                }
                var accept = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + AcceptKey(key.Trim()) + "\r\n\r\n");
                stream.Write(accept, 0, accept.Length);
                stream.Flush();
                stream.ReadTimeout = Timeout.Infinite;

                client = new FiveMClient { Tcp = tcp, Stream = stream, Remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?" };
                lock (clients) clients.Add(client);
                notes.Enqueue("game client connected from " + client.Remote);
                FrameLoop(client, stream);
            }
            catch { }
            finally
            {
                if (client != null)
                {
                    client.Closed = true;
                    lock (clients) clients.Remove(client);
                    notes.Enqueue("game client " + client.Remote + " went away");
                }
                try { tcp.Close(); } catch { }
            }
        }

        private static string ReadHead(NetworkStream stream)
        {
            var buf = new List<byte>();
            var one = new byte[1];
            while (buf.Count < 16384)
            {
                int n = stream.Read(one, 0, 1);
                if (n <= 0) return null;
                buf.Add(one[0]);
                int c = buf.Count;
                if (c >= 4 && buf[c - 4] == '\r' && buf[c - 3] == '\n' && buf[c - 2] == '\r' && buf[c - 1] == '\n')
                    return Encoding.ASCII.GetString(buf.ToArray());
            }
            return null;
        }

        public static Dictionary<string, string> ParseHeaders(string head, out string requestLine)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            requestLine = lines.Length > 0 ? lines[0] : "";
            for (int i = 1; i < lines.Length; i++)
            {
                int k = lines[i].IndexOf(':');
                if (k <= 0) continue;
                map[lines[i].Substring(0, k).Trim().ToLowerInvariant()] = lines[i].Substring(k + 1).Trim();
            }
            return map;
        }

        private void FrameLoop(FiveMClient client, NetworkStream stream)
        {
            var buf = new byte[65536];
            int have = 0;
            var text = new MemoryStream();
            while (!client.Closed)
            {
                if (have == buf.Length) Array.Resize(ref buf, buf.Length * 2);
                int n = stream.Read(buf, have, buf.Length - have);
                if (n <= 0) return;
                have += n;
                while (TryDecodeFrame(buf, have, out int opcode, out bool fin, out byte[] payload, out int used))
                {
                    Array.Copy(buf, used, buf, 0, have - used);
                    have -= used;
                    switch (opcode)
                    {
                        case 1:
                        case 0:
                            text.Write(payload, 0, payload.Length);
                            if (fin)
                            {
                                var line = Encoding.UTF8.GetString(text.ToArray());
                                text.SetLength(0);
                                var m = Parse(line);
                                if (m != null) { m.From = client; inbox.Enqueue(m); }
                            }
                            break;
                        case 8:
                            try { var close = EncodeFrame("", 8); stream.Write(close, 0, close.Length); } catch { }
                            return;
                        case 9:
                            try { var pong = EncodeFrame(payload, 10); lock (client) { stream.Write(pong, 0, pong.Length); } } catch { }
                            break;
                    }
                }
            }
        }

        public int Broadcast(string line)
        {
            FiveMClient[] snapshot;
            lock (clients) snapshot = clients.ToArray();
            int n = 0;
            foreach (var c in snapshot) { if (c.Closed) continue; c.Send(line); n++; }
            if (n > 0) Sent++;
            return n;
        }

        public IEnumerable<MloBridgeMessage> Drain()
        {
            while (inbox.TryDequeue(out var m))
            {
                Received++;
                LastAt = DateTime.Now;
                yield return m;
            }
        }

        public IEnumerable<string> DrainNotes()
        {
            while (notes.TryDequeue(out var n)) yield return n;
        }

        public string[] ClientNames()
        {
            lock (clients) return clients.Select(c => c.Remote).ToArray();
        }

        public static string AcceptKey(string key) =>
            Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WsGuid)));

        public static byte[] EncodeFrame(string text, int opcode = 1) => EncodeFrame(Encoding.UTF8.GetBytes(text ?? ""), opcode);

        public static byte[] EncodeFrame(byte[] payload, int opcode)
        {
            int len = payload.Length;
            var head = new List<byte> { (byte)(0x80 | (opcode & 0x0F)) };
            if (len < 126) head.Add((byte)len);
            else if (len <= 0xFFFF) { head.Add(126); head.Add((byte)(len >> 8)); head.Add((byte)len); }
            else
            {
                head.Add(127);
                for (int i = 7; i >= 0; i--) head.Add((byte)((long)len >> (8 * i)));
            }
            var frame = new byte[head.Count + len];
            head.CopyTo(frame, 0);
            Array.Copy(payload, 0, frame, head.Count, len);
            return frame;
        }

        public static byte[] MaskedFrame(string text, byte[] mask, int opcode = 1)
        {
            var payload = Encoding.UTF8.GetBytes(text ?? "");
            var plain = EncodeFrame(payload, opcode);
            int headLen = plain.Length - payload.Length;
            var frame = new byte[headLen + 4 + payload.Length];
            Array.Copy(plain, 0, frame, 0, headLen);
            frame[1] |= 0x80;
            Array.Copy(mask, 0, frame, headLen, 4);
            for (int i = 0; i < payload.Length; i++) frame[headLen + 4 + i] = (byte)(payload[i] ^ mask[i % 4]);
            return frame;
        }

        public static bool TryDecodeFrame(byte[] buf, int have, out int opcode, out bool fin, out byte[] payload, out int used)
        {
            opcode = 0; fin = false; payload = null; used = 0;
            if (have < 2) return false;
            fin = (buf[0] & 0x80) != 0;
            opcode = buf[0] & 0x0F;
            bool masked = (buf[1] & 0x80) != 0;
            long len = buf[1] & 0x7F;
            int idx = 2;
            if (len == 126)
            {
                if (have < 4) return false;
                len = (buf[2] << 8) | buf[3];
                idx = 4;
            }
            else if (len == 127)
            {
                if (have < 10) return false;
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | buf[2 + i];
                idx = 10;
            }
            if (len > int.MaxValue / 2) throw new InvalidDataException("frame too large");
            byte[] mask = null;
            if (masked)
            {
                if (have < idx + 4) return false;
                mask = new[] { buf[idx], buf[idx + 1], buf[idx + 2], buf[idx + 3] };
                idx += 4;
            }
            if (have < idx + len) return false;
            payload = new byte[len];
            Array.Copy(buf, idx, payload, 0, (int)len);
            if (mask != null) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
            used = idx + (int)len;
            return true;
        }

        public static MloBridgeMessage Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (!text.StartsWith("{")) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                string cmd = root.TryGetProperty("type", out var t) ? t.GetString() : (root.TryGetProperty("cmd", out var c) ? c.GetString() : null);
                if (string.IsNullOrEmpty(cmd)) return null;
                return new DccMessage { Command = cmd.ToLowerInvariant(), Raw = text, Json = true, Root = root.Clone() };
            }
            catch { return null; }
        }
    }
}
