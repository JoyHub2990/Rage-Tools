using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class RpfExplorer
    {
        public readonly struct DiskHit_V35
        {
            public readonly string FullPath;
            public readonly RpfFileEntry Entry;
            public DiskHit_V35(string p, RpfFileEntry e) { FullPath = p; Entry = e; }
        }

        public const int DiskSearchScanCap_V35 = 400000;

        public int SearchDisk_V35(string folder, string query, string extension, List<DiskHit_V35> into, int max = 500)
        {
            into.Clear();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;

            query = (query ?? "").Trim().ToLowerInvariant();
            extension = (extension ?? "").Trim().ToLowerInvariant();
            bool anyExt = string.IsNullOrEmpty(extension) || extension == "*";
            if (query.Length == 0 && anyExt) return 0;

            int total = 0, scanned = 0;
            var archives = new List<string>();

            foreach (var path in SafeFiles_V35(folder))
            {
                if (++scanned > DiskSearchScanCap_V35) break;
                var name = Path.GetFileName(path);
                var lower = name.ToLowerInvariant();

                if (lower.EndsWith(".rpf", StringComparison.Ordinal)) { archives.Add(path); continue; }

                if (!anyExt && !lower.EndsWith(extension, StringComparison.Ordinal)) continue;
                if (query.Length > 0 && lower.IndexOf(query, StringComparison.Ordinal) < 0) continue;
                total++;
                if (into.Count < max) into.Add(new DiskHit_V35(path, null));
            }

            foreach (var arch in archives)
            {
                if (into.Count >= max && total > max * 4) break;
                foreach (var fe in ArchiveFiles_V35(arch))
                {
                    var lower = (fe.Name ?? "").ToLowerInvariant();
                    if (!anyExt && !lower.EndsWith(extension, StringComparison.Ordinal)) continue;
                    if (query.Length > 0 && lower.IndexOf(query, StringComparison.Ordinal) < 0) continue;
                    total++;
                    if (into.Count < max) into.Add(new DiskHit_V35(arch, fe));
                }
            }
            return total;
        }

        private static IEnumerable<string> SafeFiles_V35(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] subs = Array.Empty<string>(), files = Array.Empty<string>();
                try { subs = Directory.GetDirectories(dir); } catch { }
                try { files = Directory.GetFiles(dir); } catch { }
                foreach (var s in subs) stack.Push(s);
                foreach (var f in files) yield return f;
            }
        }

        private readonly Dictionary<string, List<RpfFileEntry>> rpfCache_V35 =
            new Dictionary<string, List<RpfFileEntry>>(StringComparer.OrdinalIgnoreCase);

        private List<RpfFileEntry> ArchiveFiles_V35(string path)
        {
            if (rpfCache_V35.TryGetValue(path, out var cached)) return cached;
            var list = new List<RpfFileEntry>();
            try
            {
                var rpf = new RpfFile(path, Path.GetFileName(path));
                rpf.ScanStructure(null, null);
                if (rpf.AllEntries != null)
                    foreach (var e in rpf.AllEntries)
                        if (e is RpfFileEntry fe) list.Add(fe);
            }
            catch { }
            rpfCache_V35[path] = list;
            return list;
        }

        public void ClearDiskSearchCache_V35() => rpfCache_V35.Clear();

        public IEnumerable<string> OutsideFolders_V35()
        {
            var root = GameRoot?.FsPath?.TrimEnd('\\', '/');
            foreach (var n in Roots)
            {
                if (n == null || !n.IsFs || string.IsNullOrEmpty(n.FsPath)) continue;
                if (root != null && n.FsPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                yield return n.FsPath;
            }
        }

        public string DiskBranchToSearch_V35()
        {
            var n = Current;
            if (n == null || !n.IsFs || string.IsNullOrEmpty(n.FsPath)) return null;
            return CurrentBranchPrefix_V23() == null ? n.FsPath : null;
        }

        public void ShowDiskSearchResults_V35(IEnumerable<DiskHit_V35> hits)
        {
            rows.Clear();
            RowTotal = 0;
            foreach (var h in hits)
            {
                RowTotal++;
                if (h.Entry != null) rows.Add(RowFor(h.Entry));
                else
                {
                    var fi = new FileInfo(h.FullPath);
                    long len = 0; try { len = fi.Length; } catch { }
                    rows.Add(new Row(null, null, fi.Name, TypeNameForExt_V35(fi.Extension),
                                     h.FullPath, len, len, false, false, false, "", true));
                }
            }
            SortRows();
            listDirty = false;
            lastNode = Current;
            lastFilter = Filter;
            lastSort = SortColumn;
            lastAsc = SortAscending;
        }

        private static string TypeNameForExt_V35(string ext)
        {
            ext = (ext ?? "").TrimStart('.').ToLowerInvariant();
            return string.IsNullOrEmpty(ext) ? "File" : ext.ToUpperInvariant();
        }
    }
}

