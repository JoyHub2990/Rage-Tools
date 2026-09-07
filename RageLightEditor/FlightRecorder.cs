using System;
using System.IO;
using System.Text;

namespace RageLightEditor
{
    internal static class FlightRecorder
    {
        private static StreamWriter writer;
        private static string path;
        private static double lastFlush;
        private static readonly object sync = new object();

        public static string PreviousCrashReport { get; private set; }

        public static void Start()
        {
            try
            {
                path = Path.Combine(AppContext.BaseDirectory, "session.log");

                if (File.Exists(path))
                {
                    string prev = null;
                    try { prev = File.ReadAllText(path); } catch { }
                    if (!string.IsNullOrEmpty(prev) && !prev.Contains("CLEAN EXIT") &&
                        prev.Split('\n').Length > 2)
                    {
                        var lines = prev.Split('\n');
                        int take = Math.Min(lines.Length, 25);
                        var sb = new StringBuilder();
                        sb.AppendLine("The previous session ended without closing cleanly. Its last lines:");
                        for (int i = lines.Length - take; i < lines.Length; i++)
                            if (i >= 0 && !string.IsNullOrWhiteSpace(lines[i])) sb.AppendLine("  " + lines[i].TrimEnd());
                        PreviousCrashReport = sb.ToString();
                    }
                }

                writer = new StreamWriter(path, false) { AutoFlush = false };
                Line($"start  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Environment.OSVersion}");
            }
            catch { writer = null; }
        }

        public static void Line(string s)
        {
            lock (sync)
            {
                if (writer == null) return;
                try { writer.WriteLine(s); writer.Flush(); } catch { }
            }
        }

        public static void Frame(double now, string state)
        {
            if (writer == null) return;
            if (now - lastFlush < 0.5) return;
            lastFlush = now;
            Line(state);
        }

        public static void CleanExit()
        {
            Line("CLEAN EXIT");
            lock (sync)
            {
                try { writer?.Flush(); writer?.Dispose(); } catch { }
                writer = null;
            }
        }
    }
}

