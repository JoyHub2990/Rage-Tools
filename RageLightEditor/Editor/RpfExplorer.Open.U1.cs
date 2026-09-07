using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class RpfExplorer
    {
        public sealed class OpenJob_U1
        {
            public string Folder = "";
            public string[] Files = Array.Empty<string>();
            public long TotalBytes;

            public volatile int Done;
            public long DoneBytes;
            public long Entries;
            public volatile string Current = "";
            public volatile bool Finished;
            public volatile bool Failed;

            public readonly Dictionary<string, RpfFile> Results =
                new Dictionary<string, RpfFile>(StringComparer.OrdinalIgnoreCase);

            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public double Seconds => Clock.Elapsed.TotalSeconds;
            public int Total => Files.Length;
            public float Fraction => TotalBytes > 0
                ? (float)Math.Clamp(DoneBytes / (double)TotalBytes, 0.0, 1.0)
                : (Files.Length > 0 ? Math.Clamp(Done / (float)Files.Length, 0.0f, 1.0f) : 1.0f);
        }

        public OpenJob_U1 OpenJob_U1Current { get; private set; }

        public OpenJob_U1 OpenJob_U1Last { get; private set; }

        private const double MaxDeferSeconds = 8.0;

        public static int JobsRun_U1, ArchivesOpened_U1;

        public bool BackgroundOpen_U1;

        internal void ArchiveFolderGate_U1(string folder, ref bool wait)
        {
            if (!BackgroundOpen_U1 || string.IsNullOrEmpty(folder)) return;

            var job = OpenJob_U1Current;
            if (job != null)
            {
                if (job.Finished)
                {
                    foreach (var kv in job.Results)
                    {
                        scannedRpfs[kv.Key] = kv.Value;
                        if (kv.Value != null) ArchivesOpened_U1++;
                    }
                    job.Results.Clear();
                    OpenJob_U1Last = job;
                    OpenJob_U1Current = null;
                    Invalidate();
                    Console.WriteLine($"RPFOPEN {Path.GetFileName(job.Folder)}: {job.Done}/{job.Total} archive(s), " +
                                      $"{job.DoneBytes / 1048576.0:0.#} MB, {job.Entries:N0} entries in {job.Seconds:0.000}s");
                    return;
                }
                if (job.Seconds > MaxDeferSeconds) return;
                wait = true;
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(folder, "*.rpf"); }
            catch { return; }
            if (files.Length == 0) return;

            List<string> todo = null;
            long bytes = 0;
            foreach (var f in files)
            {
                if (knownRpfs.ContainsKey(f) || scannedRpfs.ContainsKey(f)) continue;
                (todo ??= new List<string>()).Add(f);
                try { bytes += new FileInfo(f).Length; } catch { }
            }
            if (todo == null) return;

            var started = new OpenJob_U1
            {
                Folder = folder,
                Files = todo.ToArray(),
                TotalBytes = bytes,
                Current = Path.GetFileName(todo[0]),
            };
            OpenJob_U1Current = started;
            JobsRun_U1++;
            wait = true;

            var t = new Thread(() => RunOpenJob_U1(started))
            { IsBackground = true, Name = "RAGE Tools rpf open" };
            t.Start();
        }

        private void RunOpenJob_U1(OpenJob_U1 job)
        {
            try
            {
                foreach (var f in job.Files)
                {
                    job.Current = Path.GetFileName(f);
                    long len = 0;
                    try { len = new FileInfo(f).Length; } catch { }
                    RpfFile rpf = null;
                    try
                    {
                        var rel = GameFolder.Length > 0 &&
                                  f.StartsWith(GameFolder + "\\", StringComparison.OrdinalIgnoreCase)
                                  ? f.Substring(GameFolder.Length + 1) : f;
                        rpf = new RpfFile(f, rel);
                        rpf.ScanStructure(null, null);
                        if (rpf.LastException != null || rpf.Root == null) rpf = null;
                    }
                    catch { rpf = null; }

                    job.Results[f] = rpf;
                    job.Entries += rpf?.AllEntries?.Count ?? 0;
                    job.DoneBytes += len;
                    job.Done++;
                }
            }
            catch { job.Failed = true; }
            finally { job.Finished = true; }
        }

        public void PumpOpenJob_U1()
        {
            var job = OpenJob_U1Current;
            if (job == null || !job.Finished) return;
            bool was = BackgroundOpen_U1;
            BackgroundOpen_U1 = true;
            bool ignored = false;
            ArchiveFolderGate_U1(job.Folder, ref ignored);
            BackgroundOpen_U1 = was;
        }

        public string OpenJobText_U1()
        {
            var j = OpenJob_U1Current;
            if (j == null) return "";
            string mb = j.TotalBytes > 0
                ? $"{j.DoneBytes / 1048576.0:0.#} of {j.TotalBytes / 1048576.0:0.#} MB"
                : $"{j.DoneBytes / 1048576.0:0.#} MB";
            return $"Opening {j.Current} - {j.Done} of {j.Total} archive{(j.Total == 1 ? "" : "s")}, " +
                   $"{mb}, {j.Entries:N0} entries read";
        }

        public bool IsOpening_U1(string fsPath)
        {
            var j = OpenJob_U1Current;
            if (j == null || string.IsNullOrEmpty(fsPath)) return false;
            return string.Equals(j.Folder, fsPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}

