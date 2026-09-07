using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private readonly List<RpfExplorer.DiskHit_V35> rpfDiskHits_V35 = new List<RpfExplorer.DiskHit_V35>();

        private int RunRpfDiskFind_V35(string query, string folder, string branchLabel)
        {
            var sw = Stopwatch.StartNew();
            int total = Rpf.SearchDisk_V35(folder, query, null, rpfDiskHits_V35, 2000);
            Rpf.ClearDiskSearchCache_V35();
            sw.Stop();
            rpfSearchMs_S3 = (int)sw.ElapsedMilliseconds;

            var q = (query ?? "").Trim();
            string where = "under " + (branchLabel ?? Path.GetFileName(folder.TrimEnd('\\', '/')));
            rpfSearchNote_S3 = total == 0
                ? $"nothing {where} is called \"{q}\" (searched the folder on disk, {rpfSearchMs_S3} ms)"
                : $"\"{q}\": {total:N0} file(s) {where}" +
                  (total > rpfDiskHits_V35.Count ? $" - showing the first {rpfDiskHits_V35.Count:N0}" : "") +
                  $" (on disk, {rpfSearchMs_S3} ms)";
            RpfStatus = rpfSearchNote_S3;
            return total;
        }

        private void AppendOutsideFolderHits_V35(string query)
        {
            var folders = Rpf.OutsideFolders_V35().ToList();
            if (folders.Count == 0) return;

            int added = 0;
            var extra = new List<RpfExplorer.DiskHit_V35>();
            var sw = Stopwatch.StartNew();
            foreach (var f in folders)
            {
                int n = Rpf.SearchDisk_V35(f, query, null, extra, 400);
                if (n <= 0) continue;
                added += n;
                foreach (var h in extra)
                    if (h.Entry != null && rpfHits.Count < 2000)
                        rpfHits.Add(new ArchiveBrowser.Entry(h.Entry));
            }
            Rpf.ClearDiskSearchCache_V35();
            sw.Stop();
            if (added <= 0) return;

            rpfSearchNote_S3 += $"  +  {added:N0} in {folders.Count} added folder(s) ({sw.ElapsedMilliseconds} ms)";
            RpfStatus = rpfSearchNote_S3;
        }
    }
}

