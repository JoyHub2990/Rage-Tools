using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class RpfExplorer
    {
        public sealed class Node
        {
            public string FsPath;
            public bool IsFs;
            public bool IsRoot;

            public string Label;
            public RpfDirectoryEntry Dir;
            public RpfFile Archive;
            public Node Parent;
            public int Depth;
            public bool Expanded;
            public List<Node> Children;

            public string Path => Dir?.Path ?? Archive?.Path ?? FsPath ?? Label;
        }

        public readonly struct Row
        {
            public readonly RpfEntry Entry;
            public readonly RpfDirectoryEntry EnterDir;
            public readonly string Name, Type, Path;
            public readonly long Size;
            public readonly long Packed;
            public readonly bool IsFolder, IsResource, IsEncrypted;
            public readonly string Attr;
            public readonly bool IsFs;

            public Row(RpfEntry e, RpfDirectoryEntry enterDir, string name, string type,
                       string path, long size, long packed, bool folder, bool res, bool enc,
                       string attr = "", bool isFs = false)
            {
                Entry = e; EnterDir = enterDir; Name = name; Type = type; Path = path;
                Size = size; Packed = packed; IsFolder = folder; IsResource = res; IsEncrypted = enc;
                Attr = attr ?? ""; IsFs = isFs;
            }
        }

        public readonly List<Node> Roots = new List<Node>();
        public Node Current;
        public RpfEntry Selected;
        public bool Ready { get; private set; }
        public int ArchiveCount => Roots.Count;

        private readonly List<Node> back = new List<Node>();
        private readonly List<Node> forward = new List<Node>();
        public bool CanGoBack => back.Count > 0;
        public bool CanGoForward => forward.Count > 0;

        public void Build(ArchiveBrowser browser)
        {
            Roots.Clear();
            back.Clear();
            forward.Clear();
            Current = null;
            Selected = null;
            Ready = false;
            if (browser == null || !browser.Ready) return;

            foreach (var rpf in browser.Roots)
            {
                if (rpf?.Root == null) continue;
                Roots.Add(new Node
                {
                    Label = ArchiveBrowser.RootLabel(rpf),
                    Dir = rpf.Root,
                    Archive = rpf,
                    Depth = 0,
                });
            }
            Ready = Roots.Count > 0;
            listDirty = true;
        }

        public List<Node> ChildrenOf(Node n)
        {
            if (n == null) return null;
            if (n.Children != null) return n.Children;
            if (n.IsFs) return FsChildrenOf_O1(n);
            var list = new List<Node>();
            var dir = n.Dir;
            if (dir != null)
            {
                if (dir.Directories != null)
                {
                    foreach (var d in dir.Directories.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                        list.Add(new Node { Label = d.Name, Dir = d, Parent = n, Depth = n.Depth + 1 });
                }
                if (dir.Files != null)
                {
                    foreach (var f in dir.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var inner = NestedArchive(f);
                        if (inner?.Root == null) continue;
                        list.Add(new Node
                        {
                            Label = f.Name,
                            Dir = inner.Root,
                            Archive = inner,
                            Parent = n,
                            Depth = n.Depth + 1,
                        });
                    }
                }
            }
            n.Children = list;
            return list;
        }

        public static RpfFile NestedArchive(RpfFileEntry f)
        {
            if (f == null) return null;
            if (!(f.NameLower ?? f.Name ?? "").EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) return null;
            var kids = f.File?.Children;
            if (kids == null) return null;
            foreach (var c in kids)
                if (string.Equals(c.Path, f.Path, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        public void Go(Node n)
        {
            if (ReferenceEquals(n, Current)) return;
            if (Current != null) back.Add(Current);
            forward.Clear();
            Current = n;
            Selected = null;
            listDirty = true;
            for (var p = n?.Parent; p != null; p = p.Parent) p.Expanded = true;
        }

        public void GoBack()
        {
            if (back.Count == 0) return;
            var t = back[back.Count - 1];
            back.RemoveAt(back.Count - 1);
            if (Current != null) forward.Add(Current);
            Current = t;
            Selected = null;
            listDirty = true;
        }

        public void GoForward()
        {
            if (forward.Count == 0) return;
            var t = forward[forward.Count - 1];
            forward.RemoveAt(forward.Count - 1);
            if (Current != null) back.Add(Current);
            Current = t;
            Selected = null;
            listDirty = true;
        }

        public void GoUp()
        {
            if (Current == null) return;
            Go(Current.Parent);
        }

        public List<Node> CrumbTrail()
        {
            var trail = new List<Node>();
            for (var n = Current; n != null; n = n.Parent) trail.Add(n);
            trail.Reverse();
            return trail;
        }

        public bool Reveal(RpfEntry entry)
        {
            var dir = entry as RpfDirectoryEntry ?? entry?.Parent;
            if (dir == null) return false;

            var chain = new List<RpfDirectoryEntry>();
            var d = dir;
            for (int guard = 0; d != null && guard < 256; guard++)
            {
                chain.Add(d);
                if (d.Parent != null) { d = d.Parent; continue; }
                d = d.File?.ParentFileEntry?.Parent;
            }
            chain.Reverse();
            if (chain.Count == 0) return false;

            var node = Roots.FirstOrDefault(r => ReferenceEquals(r.Dir, chain[0])) ?? FindArchiveNode_O1(chain[0]);
            if (node == null) return false;
            for (int i = 1; i < chain.Count; i++)
            {
                var kids = ChildrenOf(node);
                var next = kids.FirstOrDefault(k => ReferenceEquals(k.Dir, chain[i]));
                if (next == null) break;
                node.Expanded = true;
                node = next;
            }
            Go(node);
            Selected = entry;
            return true;
        }

        public bool GoToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var parts = path.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            if (GoToPathFs_O1(path)) return true;

            Node node = null;
            foreach (var r in Roots)
            {
                var name = System.IO.Path.GetFileName(r.Archive?.Path ?? r.Label ?? "");
                if (string.Equals(name, parts[0], StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.Label, parts[0], StringComparison.OrdinalIgnoreCase))
                {
                    node = r;
                    break;
                }
            }
            if (node == null) return false;

            for (int i = 1; i < parts.Length; i++)
            {
                var kids = ChildrenOf(node);
                Node next = null;
                foreach (var k in kids)
                    if (string.Equals(k.Label, parts[i], StringComparison.OrdinalIgnoreCase)) { next = k; break; }
                if (next == null) break;
                node.Expanded = true;
                node = next;
            }
            Go(node);
            return true;
        }

        public string Filter = "";
        public int SortColumn;
        public bool SortAscending = true;

        private readonly List<Row> rows = new List<Row>();
        private bool listDirty = true;
        private string lastFilter = "";
        private int lastSort = -1;
        private bool lastAsc;
        private Node lastNode;

        public IReadOnlyList<Row> Rows => rows;
        public int RowTotal { get; private set; }

        public void Invalidate() => listDirty = true;

        public void EnsureList()
        {
            if (!listDirty && ReferenceEquals(lastNode, Current) && lastFilter == Filter
                && lastSort == SortColumn && lastAsc == SortAscending) return;
            listDirty = false;
            lastNode = Current;
            lastFilter = Filter;
            lastSort = SortColumn;
            lastAsc = SortAscending;
            BuildList();
        }

        private void BuildList()
        {
            rows.Clear();
            RowTotal = 0;
            var f = (Filter ?? "").Trim();

            if (BuildListFs_O1(f)) { SortRows(); return; }

            if (Current == null)
            {
                foreach (var r in Roots)
                {
                    RowTotal++;
                    if (f.Length > 0 && r.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    rows.Add(new Row(r.Dir, r.Dir, r.Label, "Rage Package File", r.Archive?.Path ?? "",
                                     (long)(r.Archive?.FileSize ?? 0), (long)(r.Archive?.FileSize ?? 0),
                                     true, false, false, AttrOf_O1(r.Archive), false));
                }
            }
            else
            {
                var dir = Current.Dir;
                if (dir?.Directories != null)
                {
                    foreach (var d in dir.Directories)
                    {
                        RowTotal++;
                        if (f.Length > 0 && (d.Name ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        rows.Add(new Row(d, d, d.Name, "Folder", d.Path, 0, 0, true, false, false,
                                         ItemCountText_O1(d), false));
                    }
                }
                if (dir?.Files != null)
                {
                    foreach (var fe in dir.Files)
                    {
                        RowTotal++;
                        if (f.Length > 0 && (fe.Name ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        rows.Add(RowFor(fe));
                    }
                }
            }
            SortRows();
        }

        public void ShowSearchResults(IEnumerable<ArchiveBrowser.Entry> hits)
        {
            rows.Clear();
            RowTotal = 0;
            foreach (var h in hits)
            {
                RowTotal++;
                rows.Add(RowFor(h.File));
            }
            SortRows();
            listDirty = false;
            lastNode = Current;
            lastFilter = Filter;
            lastSort = SortColumn;
            lastAsc = SortAscending;
        }

        private static Row RowFor(RpfFileEntry fe)
        {
            var inner = NestedArchive(fe);
            long packed = fe.FileSize;
            if (packed == 0 && fe is RpfBinaryFileEntry bfe) packed = bfe.FileUncompressedSize;
            return new Row(fe, inner?.Root, fe.Name, TypeNameOf(fe), fe.Path,
                           fe.GetFileSize(), packed, inner != null,
                           fe is RpfResourceFileEntry, fe.IsEncrypted,
                           inner != null ? AttrOf_O1(inner) : AttrOf_O1(fe), false);
        }

        private void SortRows()
        {
            int dir = SortAscending ? 1 : -1;
            rows.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                int c;
                switch (SortColumn)
                {
                    case 1: c = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase); break;
                    case 2: c = a.Size.CompareTo(b.Size); break;
                    case 3: c = string.Compare(a.Attr, b.Attr, StringComparison.OrdinalIgnoreCase); break;
                    case 4: c = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
                    default: c = 0; break;
                }
                if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c * dir;
            });
        }

        private static readonly Dictionary<string, string> TypeNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".rpf", "Rage Package File" }, { ".dat", "Data File" }, { ".cab", "CAB File" },
            { ".txt", "Text File" }, { ".gxt2", "Global Text Table" }, { ".log", "LOG File" },
            { ".ini", "Config Text" }, { ".vdf", "Steam Script File" }, { ".sps", "Shader Preset" },
            { ".ugc", "User-Generated Content" }, { ".xml", "XML File" }, { ".meta", "Metadata (XML)" },
            { ".ymt", "Metadata (Binary)" }, { ".pso", "Metadata (PSO)" }, { ".gfx", "Scaleform Flash" },
            { ".ynd", "Path Nodes" }, { ".ynv", "Nav Mesh" }, { ".yvr", "Vehicle Record" },
            { ".ywr", "Waypoint Record" }, { ".fxc", "Compiled Shaders" }, { ".yed", "Expression Dictionary" },
            { ".yld", "Cloth Dictionary" }, { ".yfd", "Frame Filter Dictionary" }, { ".asi", "ASI Plugin" },
            { ".dll", "Dynamic Link Library" }, { ".exe", "Executable" }, { ".yft", "Fragment" },
            { ".ydr", "Drawable" }, { ".ydd", "Drawable Dictionary" }, { ".cut", "Cutscene" },
            { ".ysc", "Script" }, { ".ymf", "Manifest" }, { ".bik", "Bink Video" },
            { ".jpg", "JPEG Image" }, { ".jpeg", "JPEG Image" }, { ".gif", "GIF Image" },
            { ".png", "Portable Network Graphics" }, { ".dds", "DirectDraw Surface" },
            { ".ytd", "Texture Dictionary" }, { ".mrf", "Move Network File" }, { ".ycd", "Clip Dictionary" },
            { ".ypt", "Particle Effect" }, { ".ybn", "Static Collisions" }, { ".ide", "Item Definitions" },
            { ".ytyp", "Archetype Definitions" }, { ".ymap", "Map Data" }, { ".ipl", "Item Placements" },
            { ".awc", "Audio Wave Container" }, { ".rel", "Audio Data (REL)" },
            { ".nametable", "Name Table" }, { ".ypdb", "Pose Matcher Database" },
            { ".lua", "Lua Script" }, { ".cfg", "Config Text" },
        };

        public static string TypeNameOf(RpfEntry e)
        {
            if (e is RpfDirectoryEntry) return "Folder";
            var n = e?.NameLower ?? e?.Name?.ToLowerInvariant() ?? "";
            int dot = n.LastIndexOf('.');
            if (dot < 0) return "File";
            var ext = n.Substring(dot);
            return TypeNames.TryGetValue(ext, out var t) ? t : ext.TrimStart('.').ToUpperInvariant() + " File";
        }

        public static string ViewKindOf(RpfEntry e)
        {
            switch (ArchiveBrowser.KindOf(e))
            {
                case "ydr": case "yft": case "ydd": return "model";
                case "ytd": return "textures";
                case "ypt": return "particles";
                case "ybn": return "collision";
                case "ymap": case "ytyp": case "ymt": case "ynv": case "ymf": case "ynd":
                case "ycd": case "yed": case "yld": case "ypdb": case "rel": case "awc":
                case "cut": case "yvr": case "ywr": case "yfd": case "pso":
                    return "xml";
                case "xml": case "meta": case "lua": case "txt": case "ini": case "cfg":
                case "sps": case "log": case "vdf": case "ugc": case "ide": case "ipl":
                    return "text";
                default: return null;
            }
        }

        public static string SizeText(long b)
        {
            if (b <= 0) return "";
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / (1024.0 * 1024.0)).ToString("0.#") + " MB";
            return (b / (1024.0 * 1024.0 * 1024.0)).ToString("0.##") + " GB";
        }

        public static long ExtractTo(RpfFileEntry e, string folder)
        {
            if (e == null || string.IsNullOrEmpty(folder)) return 0;
            var data = ArchiveBrowser.ExtractForDisk(e);
            if (data == null || data.Length == 0) return 0;
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, SafeName(e.Name)), data);
            return data.Length;
        }

        public static void ExtractFolder(RpfDirectoryEntry dir, string folder,
                                         ref int files, ref long bytes, ref int failed, int depth = 0)
        {
            if (dir == null || depth > 24) return;
            Directory.CreateDirectory(folder);
            if (dir.Files != null)
            {
                foreach (var fe in dir.Files)
                {
                    var inner = NestedArchive(fe);
                    if (inner?.Root != null)
                    {
                        ExtractFolder(inner.Root, Path.Combine(folder, SafeName(fe.Name)),
                                      ref files, ref bytes, ref failed, depth + 1);
                        continue;
                    }
                    try
                    {
                        var n = ExtractTo(fe, folder);
                        if (n > 0) { files++; bytes += n; } else failed++;
                    }
                    catch { failed++; }
                }
            }
            if (dir.Directories != null)
            {
                foreach (var d in dir.Directories)
                    ExtractFolder(d, Path.Combine(folder, SafeName(d.Name)),
                                  ref files, ref bytes, ref failed, depth + 1);
            }
        }

        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "file";
            var bad = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }
}

