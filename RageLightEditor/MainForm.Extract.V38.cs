using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool extractDone_V38;

        partial void OnWorldTick_Extract_V38()
        {
            if (extractDone_V38) return;
            var want = Environment.GetEnvironmentVariable("RLE_EXTRACT");
            if (string.IsNullOrWhiteSpace(want)) return;
            var c = gameFiles?.Cache;
            if (c == null || !gameFiles.Ready) return;
            extractDone_V38 = true;

            var dir = Environment.GetEnvironmentVariable("RLE_EXTRACTDIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Path.GetTempPath(), "rle_extract");
            try { Directory.CreateDirectory(dir); } catch { }

            foreach (var raw in want.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var nm = raw.Trim();
                var stem = Path.GetFileNameWithoutExtension(nm).ToLowerInvariant();
                var ext = Path.GetExtension(nm).ToLowerInvariant();
                RpfFileEntry fe = null;
                uint h = JenkHash.GenHash(stem);
                switch (ext)
                {
                    case ".ydd": c.YddDict?.TryGetValue(h, out fe); break;
                    case ".ydr": c.YdrDict?.TryGetValue(h, out fe); break;
                    case ".yft": c.YftDict?.TryGetValue(h, out fe); break;
                    case ".ytd": c.YtdDict?.TryGetValue(h, out fe); break;
                    case ".ybn": c.YbnDict?.TryGetValue(h, out fe); break;
                    case ".ymap": c.YmapDict?.TryGetValue(h, out fe); break;
                }
                if (fe == null) { Console.WriteLine($"EXTRACT {nm}: not in the archives"); continue; }
                try
                {
                    var data = ArchiveBrowser.ExtractForDisk(fe);
                    var outPath = Path.Combine(dir, fe.Name);
                    File.WriteAllBytes(outPath, data);
                    Console.WriteLine($"EXTRACT wrote {outPath} ({data.Length:N0} bytes)");
                }
                catch (Exception ex) { Console.WriteLine($"EXTRACT {nm} failed: {ex.Message}"); }
            }
        }
    }
}

