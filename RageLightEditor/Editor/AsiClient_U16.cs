using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace RageLightEditor.Editor
{
    public class AsiClient_U16 : IDisposable
    {
        public const string PipeName = "RageToolsLive";
        public volatile bool Connected;
        public volatile bool Wanted;
        public string Status = "off";
        public string LastReply = "";
        public int Sent, Failed;
        public readonly ConcurrentQueue<string> Notes = new ConcurrentQueue<string>();

        private Thread thread;
        private readonly BlockingCollection<string> queue = new BlockingCollection<string>();
        private NamedPipeClientStream pipe;
        private StreamReader reader;
        private StreamWriter writer;
        private DateTime nextTry = DateTime.MinValue;

        public void Start()
        {
            if (thread != null) return;
            var t = new Thread(Loop) { IsBackground = true, Name = "RageToolsLive link" };
            thread = t;
            t.Start();
        }

        public void Dispose()
        {
            Wanted = false;
            thread = null;
            Close();
        }

        public void Send(string line)
        {
            if (Connected && !string.IsNullOrEmpty(line)) queue.Add(line);
        }

        public void SendNow(string line)
        {
            try { if (Connected) Exchange(line); } catch { }
        }

        private void Close()
        {
            try { pipe?.Dispose(); } catch { }
            pipe = null;
            reader = null;
            writer = null;
            Connected = false;
        }

        private void Loop()
        {
            var me = Thread.CurrentThread;
            while (ReferenceEquals(thread, me))
            {
                if (!Wanted)
                {
                    if (Connected) { Exchange("reset"); Close(); Notes.Enqueue("plugin: link closed, game materials restored"); }
                    Status = "off";
                    Thread.Sleep(300);
                    continue;
                }
                if (pipe == null)
                {
                    if (DateTime.UtcNow < nextTry) { Thread.Sleep(200); continue; }
                    nextTry = DateTime.UtcNow.AddSeconds(2);
                    try
                    {
                        var p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                        p.Connect(400);
                        pipe = p;
                        reader = new StreamReader(p, new UTF8Encoding(false));
                        writer = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                        var hello = Exchange("ping");
                        if (hello == null || !hello.StartsWith("ok")) throw new IOException("no answer from the plugin");
                        Connected = true;
                        Status = "connected  (" + hello.Trim() + ")";
                        Notes.Enqueue("plugin: connected - " + hello.Trim());
                    }
                    catch (TimeoutException) { Close(); Status = "plugin not found - game not running, or RageToolsLive.asi not installed"; continue; }
                    catch (Exception ex) { Close(); Status = ex.Message; continue; }
                }
                if (!queue.TryTake(out var line, 300)) continue;
                var reply = Exchange(line);
                if (reply == null)
                {
                    Failed++;
                    Close();
                    Status = "link to the plugin lost";
                    Notes.Enqueue("plugin: link lost");
                    continue;
                }
                Sent++;
                LastReply = reply.Trim();
                if (LastReply.StartsWith("err")) { Failed++; Notes.Enqueue("plugin: " + LastReply); }
            }
        }

        private string Exchange(string line)
        {
            try
            {
                if (writer == null || reader == null) return null;
                writer.WriteLine(line);
                return reader.ReadLine();
            }
            catch { return null; }
        }
    }
}
