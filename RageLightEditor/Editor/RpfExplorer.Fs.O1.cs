using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class RpfExplorer
    {
        public string GameFolder { get; private set; } = "";
        public Node GameRoot { get; private set; }

        private readonly Dictionary<string, RpfFile> knownRpfs =
            new Dictionary<string, RpfFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RpfFile> scannedRpfs =
            new Dictionary<string, RpfFile>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> extraRoots = new List<string>();

        public void BuildFromGameFolder(string gameFolder, ArchiveBrowser browser)
        {
            Roots.Clear();
            back.Clear();
            forward.Clear();
            Current = null;
            Selected = null;
            GameRoot = null;
            knownRpfs.Clear();
            GameFolder = (gameFolder ?? "").TrimEnd('\\', '/');

            if (browser != null && browser.Ready)
            {
                foreach (var rpf in browser.Roots)
                {
                    var fp = rpf?.FilePath;
                    if (!string.IsNullOrEmpty(fp)) knownRpfs[fp] = rpf;
                }
            }

            if (GameFolder.Length > 0 && Directory.Exists(GameFolder))
            {
                GameRoot = new Node
                {
                    Label = "GTA V",
                    FsPath = GameFolder,
                    IsFs = true,
                    IsRoot = true,
                    Depth = 0,
                    Expanded = true,
                };
                Roots.Add(GameRoot);
            }

            foreach (var extra in extraRoots)
            {
                if (!Directory.Exists(extra)) continue;
                Roots.Add(new Node
                {
                    Label = LabelForFolder_O1(extra),
                    FsPath = extra.TrimEnd('\\', '/'),
                    IsFs = true,
                    IsRoot = true,
                    Depth = 0,
                });
            }

            Ready = Roots.Count > 0;
            Invalidate();
        }

        public bool AddRootFolder(string folder)
        {
            folder = (folder ?? "").Trim().TrimEnd('\\', '/');
            if (folder.Length == 0 || !Directory.Exists(folder)) return false;
            foreach (var r in Roots)
                if (r.IsRoot && string.Equals(r.FsPath, folder, StringComparison.OrdinalIgnoreCase))
                {
                    Go(r);
                    return true;
                }
            if (!extraRoots.Contains(folder, StringComparer.OrdinalIgnoreCase)) extraRoots.Add(folder);

            var node = new Node
            {
                Label = LabelForFolder_O1(folder),
                FsPath = folder,
                IsFs = true,
                IsRoot = true,
                Depth = 0,
                Expanded = true,
            };
            Roots.Add(node);
            Ready = true;
            Go(node);
            return true;
        }

        public bool CloseRootFolder(Node n)
        {
            if (n == null || !n.IsRoot || ReferenceEquals(n, GameRoot)) return false;
            extraRoots.RemoveAll(p => string.Equals(p, n.FsPath, StringComparison.OrdinalIgnoreCase));
            Roots.Remove(n);
            for (var c = Current; c != null; c = c.Parent)
                if (ReferenceEquals(c, n)) { Current = null; break; }
            Invalidate();
            return true;
        }

        private static string LabelForFolder_O1(string folder)
        {
            var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? folder : name;
        }

        private List<Node> FsChildrenOf_O1(Node n)
        {
            { bool waitU1 = false; ArchiveFolderGate_U1(n.FsPath, ref waitU1); if (waitU1) return new List<Node>(); }
            var list = new List<Node>();
            n.Children = list;
            if (string.IsNullOrEmpty(n.FsPath)) return list;

            string[] dirs, files;
            try { dirs = Directory.GetDirectories(n.FsPath); }
            catch { return list; }
            try { files = Directory.GetFiles(n.FsPath); }
            catch { files = Array.Empty<string>(); }

            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var d in dirs)
                list.Add(new Node
                {
                    Label = Path.GetFileName(d),
                    FsPath = d,
                    IsFs = true,
                    Parent = n,
                    Depth = n.Depth + 1,
                });

            foreach (var f in files)
            {
                if (!f.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                var rpf = OpenArchiveOnDisk_O1(f);
                if (rpf?.Root == null) continue;
                list.Add(new Node
                {
                    Label = Path.GetFileName(f),
                    FsPath = f,
                    Dir = rpf.Root,
                    Archive = rpf,
                    Parent = n,
                    Depth = n.Depth + 1,
                });
            }
            return list;
        }

        public RpfFile OpenArchiveOnDisk_O1(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            if (knownRpfs.TryGetValue(fullPath, out var known)) return known;
            if (scannedRpfs.TryGetValue(fullPath, out var cached)) return cached;
            { bool waitU1 = false; ArchiveFolderGate_U1(System.IO.Path.GetDirectoryName(fullPath), ref waitU1); if (waitU1) return null; }
            if (scannedRpfs.TryGetValue(fullPath, out var openedU1)) return openedU1;

            RpfFile rpf = null;
            try
            {
                if (File.Exists(fullPath))
                {
                    var rel = GameFolder.Length > 0 &&
                              fullPath.StartsWith(GameFolder + "\\", StringComparison.OrdinalIgnoreCase)
                              ? fullPath.Substring(GameFolder.Length + 1)
                              : fullPath;
                    rpf = new RpfFile(fullPath, rel);
                    rpf.ScanStructure(null, null);
                    if (rpf.LastException != null || rpf.Root == null) rpf = null;
                }
            }
            catch { rpf = null; }
            scannedRpfs[fullPath] = rpf;
            return rpf;
        }

        public void ForgetArchive_O1(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return;
            scannedRpfs.Remove(fullPath);
            knownRpfs.Remove(fullPath);
        }

        public void RememberArchive_O1(RpfFile rpf)
        {
            var fp = rpf?.FilePath;
            if (!string.IsNullOrEmpty(fp)) scannedRpfs[fp] = rpf;
        }

        private bool BuildListFs_O1(string filter)
        {
            if (Current == null)
            {
                if (Roots.Count == 0 || !Roots[0].IsFs) return false;
                foreach (var r in Roots)
                {
                    RowTotal++;
                    if (filter.Length > 0 && (r.Label ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    rows.Add(new Row(null, null, r.Label, "Folder", r.FsPath ?? "", 0, 0,
                                      true, false, false, "", true));
                }
                return true;
            }
            if (!Current.IsFs) return false;

            string[] dirs, files;
            try { dirs = Directory.GetDirectories(Current.FsPath); }
            catch { return true; }
            try { files = Directory.GetFiles(Current.FsPath); }
            catch { files = Array.Empty<string>(); }

            foreach (var d in dirs)
            {
                RowTotal++;
                var name = Path.GetFileName(d);
                if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(new Row(null, null, name, "Folder", d, 0, 0, true, false, false,
                                  FsItemCountText_O1(d), true));
            }
            foreach (var f in files)
            {
                RowTotal++;
                var name = Path.GetFileName(f);
                if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                long size = 0;
                try { size = new FileInfo(f).Length; } catch { }

                if (f.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    var rpf = OpenArchiveOnDisk_O1(f);
                    rows.Add(new Row(null, rpf?.Root, name, "Rage Package File", f, size, size,
                                      rpf?.Root != null, false, false, AttrOf_O1(rpf), true));
                }
                else
                {
                    rows.Add(new Row(null, null, name, TypeNameForName_O1(name), f, size, size,
                                      false, false, false, "", true));
                }
            }
            return true;
        }

        public static string AttrOf_O1(RpfFile rpf) =>
            rpf == null ? "" : rpf.Encryption.ToString() + " encryption";

        public static string AttrOf_O1(RpfFileEntry fe)
        {
            if (fe == null) return "";
            var s = "";
            if (fe is RpfResourceFileEntry res) s = "Resource [V." + res.Version + "]";
            if (fe.IsEncrypted) s = s.Length > 0 ? s + ", Encrypted" : "Encrypted";
            return s;
        }

        private static string ItemCountText_O1(RpfDirectoryEntry d)
        {
            if (d == null) return "";
            int n = (d.Directories?.Count ?? 0) + (d.Files?.Count ?? 0);
            return n + " item" + (n == 1 ? "" : "s");
        }

        private static string FsItemCountText_O1(string dir)
        {
            try
            {
                int n = Directory.GetFileSystemEntries(dir).Length;
                return n + " item" + (n == 1 ? "" : "s");
            }
            catch { return ""; }
        }

        public string DisplayPath_O1(in Row r)
        {
            var p = r.Path ?? "";
            if (!r.IsFs || GameFolder.Length == 0) return p;
            if (p.StartsWith(GameFolder + "\\", StringComparison.OrdinalIgnoreCase))
                return p.Substring(GameFolder.Length + 1);
            return p;
        }

        public static string TypeNameForName_O1(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            int dot = n.LastIndexOf('.');
            if (dot < 0) return "File";
            var ext = n.Substring(dot);
            return TypeNames.TryGetValue(ext, out var t) ? t : ext.TrimStart('.').ToUpperInvariant() + " File";
        }

        private bool GoToPathFs_O1(string path)
        {
            if (Roots.Count == 0) return false;
            path = (path ?? "").Replace('/', '\\').Trim();
            if (path.Length == 0) return false;

            Node start = null;
            string rest = path;

            if (path.Length > 1 && path[1] == ':')
            {
                foreach (var r in Roots)
                {
                    if (!r.IsFs || string.IsNullOrEmpty(r.FsPath)) continue;
                    if (string.Equals(path.TrimEnd('\\'), r.FsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Go(r);
                        return true;
                    }
                    if (path.StartsWith(r.FsPath + "\\", StringComparison.OrdinalIgnoreCase) &&
                        (start == null || r.FsPath.Length > start.FsPath.Length))
                    {
                        start = r;
                        rest = path.Substring(r.FsPath.Length + 1);
                    }
                }
                if (start == null) return false;
            }
            else
            {
                foreach (var r in Roots)
                {
                    if (!r.IsRoot) continue;
                    if (string.Equals(r.Label, path, StringComparison.OrdinalIgnoreCase)) { Go(r); return true; }
                    if (path.StartsWith(r.Label + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        start = r;
                        rest = path.Substring(r.Label.Length + 1);
                        break;
                    }
                }
                if (start == null) start = GameRoot ?? Roots[0];
            }

            var parts = rest.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var node = start;
            bool all = true;
            foreach (var part in parts)
            {
                Node next = null;
                foreach (var c in ChildrenOf(node))
                    if (string.Equals(c.Label, part, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
                if (next == null) { all = false; break; }
                node.Expanded = true;
                node = next;
            }
            Go(node);
            return all;
        }

        private Node FindArchiveNode_O1(RpfDirectoryEntry archiveRoot)
        {
            var rpf = archiveRoot?.File;
            var fp = rpf?.FilePath;
            if (string.IsNullOrEmpty(fp)) return null;

            Node start = null;
            foreach (var r in Roots)
            {
                if (!r.IsFs || string.IsNullOrEmpty(r.FsPath)) continue;
                if (fp.StartsWith(r.FsPath + "\\", StringComparison.OrdinalIgnoreCase) &&
                    (start == null || r.FsPath.Length > start.FsPath.Length))
                    start = r;
            }
            if (start == null) return null;

            var rest = fp.Substring(start.FsPath.Length + 1);
            var parts = rest.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var node = start;
            foreach (var part in parts)
            {
                Node next = null;
                foreach (var c in ChildrenOf(node))
                    if (string.Equals(c.Label, part, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
                if (next == null) return null;
                node = next;
            }
            return ReferenceEquals(node.Dir, archiveRoot) ? node : null;
        }

        public bool EnterRow_O1(in Row r)
        {
            if (!r.IsFolder) return false;

            if (r.IsFs)
            {
                if (Current != null && Current.IsFs)
                {
                    foreach (var c in ChildrenOf(Current))
                        if (string.Equals(c.FsPath, r.Path, StringComparison.OrdinalIgnoreCase)) { Go(c); return true; }
                }
                else if (Current == null)
                {
                    foreach (var root in Roots)
                        if (string.Equals(root.FsPath, r.Path, StringComparison.OrdinalIgnoreCase)) { Go(root); return true; }
                }
                return GoToPathFs_O1(r.Path);
            }

            if (r.EnterDir == null) return false;
            if (Current != null)
            {
                foreach (var c in ChildrenOf(Current))
                    if (ReferenceEquals(c.Dir, r.EnterDir)) { Go(c); return true; }
            }
            return Reveal(r.EnterDir);
        }

        public void RefreshNode_O1(Node n)
        {
            if (n != null) n.Children = null;
            Invalidate();
        }

        public void RefreshCurrent_O1()
        {
            RefreshNode_O1(Current);
            if (Current?.Parent != null) Current.Parent.Children = null;
        }

        public RpfDirectoryEntry CurrentRpfDir => Current != null && !Current.IsFs ? Current.Dir : null;
        public string CurrentFsFolder => Current != null && Current.IsFs ? Current.FsPath : null;

        public bool CurrentIsInGameFolder
        {
            get
            {
                if (GameFolder.Length == 0 || Current == null) return false;
                var p = CurrentPhysicalPath;
                return !string.IsNullOrEmpty(p) &&
                       (string.Equals(p, GameFolder, StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith(GameFolder + "\\", StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool CurrentIsInModsFolder
        {
            get
            {
                if (GameFolder.Length == 0) return false;
                var p = CurrentPhysicalPath;
                if (string.IsNullOrEmpty(p)) return false;
                var mods = GameFolder + "\\mods";
                return string.Equals(p, mods, StringComparison.OrdinalIgnoreCase) ||
                       p.StartsWith(mods + "\\", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string CurrentPhysicalPath
        {
            get
            {
                if (Current == null) return null;
                if (Current.IsFs) return Current.FsPath;
                return Current.Dir?.File?.GetPhysicalFilePath() ?? Current.Archive?.GetPhysicalFilePath();
            }
        }

        public string CurrentDisplayPath
        {
            get
            {
                if (Current == null) return "";
                if (Current.IsFs) return Current.FsPath ?? "";
                return Current.Dir?.Path ?? Current.Archive?.Path ?? Current.Label ?? "";
            }
        }
    }
}

