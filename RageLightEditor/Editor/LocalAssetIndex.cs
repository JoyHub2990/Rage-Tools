using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public class LocalAssetIndex
    {
        private readonly Dictionary<uint, string> ydrs = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> yfts = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> ydds = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> ytds = new Dictionary<uint, string>();

        private readonly Dictionary<uint, Archetype> archetypes = new Dictionary<uint, Archetype>();

        private readonly Dictionary<string, YddFile> loadedYdds = new Dictionary<string, YddFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, YtdFile> loadedYtds = new Dictionary<uint, YtdFile>();

        public string Root { get; private set; }
        public int FileCount => ydrs.Count + yfts.Count + ydds.Count;
        public int ArchetypeCount => archetypes.Count;

        private const int MaxFiles = 40000;

        public static string FindRoot(string ytypPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(ytypPath));
            var cur = dir;
            for (int i = 0; i < 3 && cur != null; i++)
            {
                if (File.Exists(Path.Combine(cur, "fxmanifest.lua")) ||
                    File.Exists(Path.Combine(cur, "__resource.lua"))) return cur;
                var parent = Path.GetDirectoryName(cur);
                if (parent == null || parent == cur) break;
                cur = parent;
            }
            var leaf = Path.GetFileName(dir)?.Trim('[', ']', '(', ')').ToLowerInvariant() ?? "";
            string[] containers = { "ytyp", "ytyps", "stream", "data", "meta", "metadata", "map", "maps" };
            if (Array.IndexOf(containers, leaf) >= 0)
            {
                var up = Path.GetDirectoryName(dir);
                if (up != null) return up;
            }
            return dir;
        }

        private readonly object sync = new object();

        public void Build(string root, Action<string> progress = null, IEnumerable<string> extraRoots = null)
        {
            Root = root;
            progress?.Invoke("Scanning local resource files...");
            AddRoot(root);
            if (extraRoots != null) foreach (var r in extraRoots) AddRoot(r);
            progress?.Invoke($"Local: {FileCount} models, {archetypes.Count} archetypes.");
        }

        public void AddRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            if (Root == null) Root = root;
            int n = 0;
            lock (sync)
                foreach (var path in EnumerateFilesSafe(root))
                {
                    if (++n > MaxFiles) break;
                    AddFileLocked(path, loadYtyps: true);
                }
        }

        public int AddFolder(string dir, int maxDepth = MaxFolderDepth)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            int n = 0, walked = 0;
            lock (sync)
                foreach (var f in EnumerateFolderFiles(dir, maxDepth))
                {
                    if (++walked > MaxFolderFiles) break;
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".ydr" && ext != ".ydd" && ext != ".yft" && ext != ".ytd") continue;
                    AddFileLocked(f, loadYtyps: false, keepFirst: true);
                    n++;
                }
            return n;
        }

        public const int MaxFolderDepth = 6;
        public const int MaxFolderFiles = 25000;
        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bin", "obj", ".git", ".svn", ".vs", "node_modules", "__pycache__", "$recycle.bin", "system volume information" };

        private static IEnumerable<string> EnumerateFolderFiles(string root, int maxDepth)
        {
            var queue = new Queue<(string dir, int depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();
                string[] files;
                try { files = Directory.GetFiles(dir); } catch { continue; }
                foreach (var f in files) yield return f;
                if (depth >= maxDepth) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(dir); } catch { continue; }
                foreach (var s in subs)
                {
                    var leaf = Path.GetFileName(s) ?? "";
                    if (SkippedFolders.Contains(leaf)) continue;
                    try { if ((File.GetAttributes(s) & FileAttributes.Hidden) != 0) continue; } catch { continue; }
                    queue.Enqueue((s, depth + 1));
                }
            }
        }

        public static bool IsUnder(string dir, string root)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(root)) return false;
            try
            {
                var d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return d.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public string Signature()
        {
            lock (sync)
            {
                var all = ydrs.Values.Concat(yfts.Values).Concat(ydds.Values).Concat(ytds.Values)
                              .Select(p => p.ToLowerInvariant()).OrderBy(p => p, StringComparer.Ordinal);
                return string.Join("|", all);
            }
        }

        public void AddFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            lock (sync) AddFileLocked(path, loadYtyps: false);
        }

        private void AddFileLocked(string path, bool loadYtyps, bool keepFirst = false)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            uint hash = JenkHash.GenHash(name);
            switch (ext)
            {
                case ".ydr": case ".yft": case ".ydd": case ".ytd": case ".ytyp":
                    JenkIndex.Ensure(name); break;
            }
            switch (ext)
            {
                case ".ydr": if (keepFirst && ydrs.ContainsKey(hash)) break; ydrs[hash] = path; break;
                case ".yft": if (keepFirst && yfts.ContainsKey(hash)) break; yfts[hash] = path; break;
                case ".ydd": if (keepFirst && ydds.ContainsKey(hash)) break; ydds[hash] = path; loadedYdds.Remove(path); break;
                case ".ytd": if (keepFirst && ytds.ContainsKey(hash)) break; ytds[hash] = path; loadedYtds.Remove(hash); texCache.Clear(); break;
                case ".ytyp": if (loadYtyps) LoadArchetypes(path); break;
            }
        }

        public bool HasModel(uint hash) => ydrs.ContainsKey(hash) || yfts.ContainsKey(hash) || ydds.ContainsKey(hash);
        public bool HasTextureDict(uint hash) => ytds.ContainsKey(hash);
        public int TextureDictCount => ytds.Count;
        public string ModelPath(uint hash) =>
            ydrs.TryGetValue(hash, out var p) ? p : yfts.TryGetValue(hash, out p) ? p : ydds.TryGetValue(hash, out p) ? p : null;

        private void LoadArchetypes(string path)
        {
            try
            {
                var ytyp = new YtypFile();
                ytyp.Load(File.ReadAllBytes(path));
                if (ytyp.AllArchetypes == null) return;
                foreach (var a in ytyp.AllArchetypes)
                {
                    if (a != null) archetypes[a.Hash] = a;
                }
            }
            catch { }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] subs, files;
                try { subs = Directory.GetDirectories(dir); } catch { continue; }
                try { files = Directory.GetFiles(dir); } catch { continue; }
                foreach (var s in subs) stack.Push(s);
                foreach (var f in files) yield return f;
            }
        }

        public Archetype GetArchetype(uint hash) => archetypes.TryGetValue(hash, out var a) ? a : null;

        public YdrFile LastYdr;
        public YftFile LastYft;
        public string LastPath;

        public DrawableBase GetDrawable(uint archetypeHash, out Archetype archetype)
        {
            lock (sync)
            {
                archetypes.TryGetValue(archetypeHash, out archetype);
                return GetDrawableLocked(archetype, archetypeHash);
            }
        }

        public DrawableBase GetDrawable(Archetype archetype, uint archetypeHash)
        {
            lock (sync) return GetDrawableLocked(archetype, archetypeHash);
        }

        private DrawableBase GetDrawableLocked(Archetype archetype, uint archetypeHash)
        {
            LastYdr = null; LastYft = null; LastPath = null;
            uint assetHash = archetypeHash;
            uint ddHash = 0;
            if (archetype != null)
            {
                var an = archetype.AssetName;
                if (!string.IsNullOrEmpty(an) && !an.StartsWith("hash_", StringComparison.OrdinalIgnoreCase))
                    assetHash = JenkHash.GenHash(an.ToLowerInvariant());
                ddHash = archetype.DrawableDict;
            }

            var d = LoadYdr(assetHash) ?? LoadYdr(archetypeHash);
            if (d != null) return d;

            d = LoadYft(assetHash) ?? LoadYft(archetypeHash);
            if (d != null) return d;

            d = LoadFromYdd(ddHash, assetHash) ?? LoadFromYdd(ddHash, archetypeHash);
            if (d != null) return d;

            foreach (var kv in ydds)
            {
                d = LoadFromYdd(kv.Key, assetHash) ?? LoadFromYdd(kv.Key, archetypeHash);
                if (d != null) return d;
            }
            return null;
        }

        private DrawableBase LoadYdr(uint hash)
        {
            if (hash == 0 || !ydrs.TryGetValue(hash, out var path)) return null;
            try
            {
                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(path));
                if (ydr.Drawable == null) return null;
                LastYdr = ydr; LastPath = path;
                return ydr.Drawable;
            }
            catch { return null; }
        }

        private DrawableBase LoadYft(uint hash)
        {
            if (hash == 0 || !yfts.TryGetValue(hash, out var path)) return null;
            try
            {
                var yft = new YftFile();
                yft.Load(File.ReadAllBytes(path));
                if (yft.Fragment?.Drawable == null) return null;
                LastYft = yft; LastPath = path;
                return yft.Fragment.Drawable;
            }
            catch { return null; }
        }

        private DrawableBase LoadFromYdd(uint dictHash, uint drawableHash)
        {
            if (dictHash == 0 || drawableHash == 0) return null;
            if (!ydds.TryGetValue(dictHash, out var path)) return null;
            if (!loadedYdds.TryGetValue(path, out var ydd))
            {
                try
                {
                    ydd = new YddFile();
                    ydd.Load(File.ReadAllBytes(path));
                }
                catch { ydd = null; }
                loadedYdds[path] = ydd;
            }
            if (ydd?.Dict == null) return null;
            return ydd.Dict.TryGetValue(drawableHash, out var dr) ? dr : null;
        }

        public IEnumerable<KeyValuePair<uint, string>> TextureDictFiles => ytds;

        public YtdFile GetTextureDict(uint hash)
        {
            if (hash == 0) return null;
            lock (sync)
            {
                if (loadedYtds.TryGetValue(hash, out var cached)) return cached;
                YtdFile ytd = null;
                if (ytds.TryGetValue(hash, out var path))
                {
                    try
                    {
                        ytd = new YtdFile();
                        ytd.Load(File.ReadAllBytes(path));
                        if (ytd.TextureDict == null) ytd = null;
                        else
                        {
                            ytd.RpfFileEntry ??= new RpfResourceFileEntry { Name = Path.GetFileName(path), ShortNameHash = hash };
                            ytd.Name ??= Path.GetFileName(path);
                            ytd.Loaded = true;
                        }
                    }
                    catch { ytd = null; }
                }
                loadedYtds[hash] = ytd;
                return ytd;
            }
        }

        private readonly Dictionary<uint, Texture> texCache = new Dictionary<uint, Texture>();

        public Texture FindTexture(uint nameHash)
        {
            if (nameHash == 0) return null;
            lock (sync)
            {
                if (texCache.TryGetValue(nameHash, out var cached)) return cached;

                Texture found = null;
                foreach (var hash in ytds.Keys.ToList())
                {
                    var ytd = GetTextureDict(hash);
                    var t = ytd?.TextureDict?.Lookup(nameHash);
                    if (t?.Data?.FullData != null) { found = t; break; }
                }
                texCache[nameHash] = found;
                return found;
            }
        }
    }
}

