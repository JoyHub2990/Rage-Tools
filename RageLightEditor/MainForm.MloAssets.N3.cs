using System;
using System.Collections.Generic;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool mloArchivesWereReady_N3;
        private int mloGameModelCount_N3 = -1;
        private static readonly string[] ModelExts_N3 = { ".ydr", ".yft" };
        private const string EmptyQueryPrefix_N3 = "prop_";

        private void TickArchiveIndex_N3(MloAssetLibrary lib)
        {
            bool ready = panel.Archive != null && panel.Archive.Ready;
            if (ready && !mloArchivesWereReady_N3)
            {
                mloGameModelCount_N3 = panel.Archive.Find("", ModelExts_N3, new List<ArchiveBrowser.Entry>(), 0);
                lib.Dirty = true;
                Console.WriteLine($"MLOASSETS archive index ready: {panel.Archive.FileCount:N0} files, {mloGameModelCount_N3:N0} models (.ydr/.yft)");
            }
            mloArchivesWereReady_N3 = ready;

            lib.ArchivesReady = ready;
            lib.GameModels = ready ? Math.Max(mloGameModelCount_N3, 0) : 0;
            lib.ArchiveIndexing = !ready;
            if (ready) lib.ArchiveStatus = $"{lib.GameModels:N0} game models";
            else if (gameFiles == null || (!gameFiles.Ready && !gameFiles.Initialising))
                lib.ArchiveStatus = "no GTA V folder open - only your own prop folders can answer";
            else if (gameFiles.Initialising)
                lib.ArchiveStatus = "opening the game archives... (the game's props appear here by themselves)";
            else
                lib.ArchiveStatus = "indexing the game archives... (the game's props appear here by themselves)";
        }

        private int SearchArchiveAssets_N3(MloAssetLibrary lib, IReadOnlyList<string> exts, List<MloAssetItem> into, int max)
        {
            if (panel.Archive == null || !panel.Archive.Ready) return 0;
            bool empty = string.IsNullOrWhiteSpace(lib.Query);
            var hits = new List<ArchiveBrowser.Entry>();
            int total = panel.Archive.Find(empty ? EmptyQueryPrefix_N3 : lib.Query, exts, hits, max);
            MloAssetLibrary.FromArchive(hits, into, max);
            if (empty && hits.Count == 0)
            {
                total = panel.Archive.Find("", exts, hits, max);
                MloAssetLibrary.FromArchive(hits, into, max);
            }
            return total;
        }

        private static void OrderMloAssets_N3(List<MloAssetItem> items, MloAssetLibrary lib)
        {
            if (!string.IsNullOrWhiteSpace(lib.Query) || !lib.ArchivesReady)
            {
                MloAssetLibrary.Sort(items, lib.Query);
                return;
            }
            items.Sort((a, b) =>
            {
                if (a.FromArchive != b.FromArchive) return a.FromArchive ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void MloAssetsSearchTest_N3(Action<string, bool, string> check)
        {
            var lib = Creator.Assets;
            TickArchiveIndex_N3(lib);
            if (!lib.ArchivesReady)
            {
                check("mloassets: without the archives the header says why", lib.ArchiveIndexing && !string.IsNullOrEmpty(lib.ArchiveStatus), lib.ArchiveStatus ?? "");
                lib.Query = ""; lib.SourceFilter = 0;
                RunMloAssetSearch_L3(lib);
                check("mloassets: an empty query still lists the folders", lib.Results.Count >= 0, $"{lib.Results.Count} results");
                return;
            }
            int before = lib.Results.Count;
            lib.Query = ""; lib.SourceFilter = 1; lib.KindFilter = 0;
            RunMloAssetSearch_L3(lib);
            check("mloassets: an empty query lists the game's props", lib.Results.Count > 100 && lib.Results.Count(r => r.FromArchive) > 100,
                  $"{lib.Results.Count} results, {lib.Results.Count(r => r.FromArchive)} from the archives");
            check("mloassets: and they are prop_ models", lib.Results.Count(r => r.FromArchive && r.Name.Contains("prop_")) > 50, $"{lib.Results.Count(r => r.FromArchive && r.Name.Contains("prop_"))} prop_ names");
            lib.Query = "chair"; lib.SourceFilter = 0;
            RunMloAssetSearch_L3(lib);
            check("mloassets: 'chair' finds game models", lib.Results.Any(r => r.FromArchive && r.Name.Contains("chair")), $"{lib.Results.Count} results, {lib.Results.Count(r => r.FromArchive)} from the archives");
            check("mloassets: the count is models, not every file", lib.GameModels > 0 && lib.GameModels < panel.Archive.FileCount, $"{lib.GameModels:N0} of {panel.Archive.FileCount:N0} files");
            lib.Query = ""; lib.Dirty = true;
        }
    }
}

