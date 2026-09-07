using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class MloBridgeMessage
    {
        public string Command = "";
        public Vector3 Point;
        public Vector3[] Corners;
        public string Name = "";
        public Vector3 Min, Max;
        public string Raw = "";
        public bool Json;
        public MloBridgeClient From;
    }

    public class MloBridgeClient
    {
        public TcpClient Tcp;
        public StreamWriter Writer;
        public string Remote = "";
        public volatile bool Closed;
        public virtual void Send(string line)
        {
            if (Closed) return;
            try { lock (this) { Writer.Write(line); Writer.Write('\n'); Writer.Flush(); } }
            catch { Closed = true; }
        }
    }

    public class MloBridge : IDisposable
    {
        public const int DefaultPort = 27017;
        public int Port { get; private set; } = DefaultPort;
        public bool Listening => listener != null;
        public string Error = "";
        public int Clients => clients.Count;
        public int Received;
        public string LastLine = "";
        public DateTime LastAt;

        private TcpListener listener;
        private Thread acceptThread;
        private readonly List<MloBridgeClient> clients = new List<MloBridgeClient>();
        private readonly ConcurrentQueue<MloBridgeMessage> inbox = new ConcurrentQueue<MloBridgeMessage>();

        public bool Start(int port = DefaultPort)
        {
            Stop();
            Error = "";
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Math.Max(port, 0));
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MloBridge accept" };
                acceptThread.Start();
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
                foreach (var c in clients) { c.Closed = true; try { c.Tcp.Close(); } catch { } }
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
                var client = new MloBridgeClient { Tcp = tcp, Remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?" };
                try
                {
                    tcp.NoDelay = true;
                    var stream = tcp.GetStream();
                    client.Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    lock (clients) clients.Add(client);
                    var t = new Thread(() => ReadLoop(client, stream)) { IsBackground = true, Name = "MloBridge client" };
                    t.Start();
                }
                catch { try { tcp.Close(); } catch { } }
            }
        }

        private void ReadLoop(MloBridgeClient client, NetworkStream stream)
        {
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string line;
                while (!client.Closed && (line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    var msg = Parse(line);
                    if (msg == null) { client.Send(line.StartsWith("{") ? "{\"type\":\"error\",\"text\":\"unrecognised\"}" : "error unrecognised"); continue; }
                    msg.From = client;
                    inbox.Enqueue(msg);
                }
            }
            catch { }
            finally
            {
                client.Closed = true;
                lock (clients) clients.Remove(client);
                try { client.Tcp.Close(); } catch { }
            }
        }

        public int Broadcast(string line)
        {
            MloBridgeClient[] snapshot;
            lock (clients) snapshot = clients.ToArray();
            int n = 0;
            foreach (var c in snapshot) { if (c.Closed) continue; c.Send(line); n++; }
            return n;
        }

        public string[] ClientNames()
        {
            lock (clients) return clients.Select(c => c.Remote).ToArray();
        }

        public IEnumerable<MloBridgeMessage> Drain()
        {
            while (inbox.TryDequeue(out var m))
            {
                Received++;
                LastLine = m.Raw;
                LastAt = DateTime.Now;
                yield return m;
            }
        }

        public static Func<string, MloBridgeMessage> ExtraParse = DccBridgeProtocol.Parse;

        public static MloBridgeMessage Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            string cmd = null;
            bool json = line.StartsWith("{");
            if (json)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                        cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() : (root.TryGetProperty("type", out var t) ? t.GetString() : null);
                }
                catch { return null; }
            }
            else
            {
                var parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                cmd = parts.Length > 0 ? parts[0] : null;
            }
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.ToLowerInvariant();
            if (cmd == "ping" || cmd == "hello" || cmd == "bye") return new MloBridgeMessage { Command = cmd, Raw = line, Json = json };
            return ExtraParse?.Invoke(line);
        }

        private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0f;
        private static string N(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static Vector3[] OrderQuadLoop(Vector3[] c)
        {
            if (c == null || c.Length != 4) return c;
            var centre = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            var n = Vector3.Cross(c[2] - c[0], c[3] - c[1]);
            if (n.LengthSquared() < 1e-10f) n = Vector3.Cross(c[1] - c[0], c[2] - c[0]);
            if (n.LengthSquared() < 1e-10f) return c;
            n.Normalize();
            var u = c[0] - centre; u -= n * Vector3.Dot(u, n);
            if (u.LengthSquared() < 1e-10f) return c;
            u.Normalize();
            var v = Vector3.Cross(n, u);
            var order = new List<(float ang, Vector3 p)>();
            foreach (var p in c)
            {
                var d = p - centre;
                order.Add(((float)Math.Atan2(Vector3.Dot(d, v), Vector3.Dot(d, u)), p));
            }
            order.Sort((a, b) => a.ang.CompareTo(b.ang));
            var outp = new Vector3[4];
            int start = order.FindIndex(o => o.p == c[0]);
            for (int i = 0; i < 4; i++) outp[i] = order[(start + i) % 4].p;
            return outp;
        }

        public static string Ack(bool json, string what) => json ? "{\"type\":\"ok\",\"text\":" + JsonSerializer.Serialize(what) + "}" : "ok " + what;
    }
}

