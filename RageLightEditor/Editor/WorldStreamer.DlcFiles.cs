using System;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        private void ReportDlcFiles()
        {
            var report = cache?.DlcFileReport;
            if (string.IsNullOrEmpty(report)) return;
            var dump = Environment.GetEnvironmentVariable("RLE_DLCDUMP");
            bool full = !string.IsNullOrEmpty(dump);
            if (full && dump != "1") Console.WriteLine(cache.DescribeDlcChangeSets(dump));
            foreach (var line in report.Split('\n'))
            {
                var l = line.TrimEnd();
                if (full || l.StartsWith("DLCFILES summary", StringComparison.Ordinal) || l.StartsWith("DLCFILES failed", StringComparison.Ordinal))
                    Console.WriteLine(l);
            }
        }

        private bool IsDlcInvalidated(uint ymapHash) => cache != null && cache.IsDlcInvalidatedYmap(ymapHash);
    }
}

