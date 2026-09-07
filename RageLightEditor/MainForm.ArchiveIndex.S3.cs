using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public volatile bool ArchiveIndexWarming_S3;

        private void BuildArchiveIndex_S3(RpfManager rpfMan, bool now)
        {
            var browser = panel?.Archive;
            if (browser == null) return;

            void Build()
            {
                var sw = Stopwatch.StartNew();
                Console.WriteLine($"ARCHIVE indexing {(now ? "now" : "in the background")}...");
                try { browser.Build(rpfMan); }
                catch (Exception ex) { Console.WriteLine("ARCHIVE index failed: " + ex.Message); return; }
                Console.WriteLine($"ARCHIVE indexed {browser.FileCount:N0} files in {browser.Roots.Count} " +
                                  $"archives in {sw.ElapsedMilliseconds} ms" + (now ? "" : " (background)"));
            }

            if (now) { Build(); return; }
            ArchiveIndexWarming_S3 = true;
            Task.Factory.StartNew(() =>
            {
                try { Build(); }
                finally { ArchiveIndexWarming_S3 = false; }
            }, System.Threading.CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}

