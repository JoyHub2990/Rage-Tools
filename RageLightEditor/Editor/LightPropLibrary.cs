using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public class LightPropEntry
    {
        public string Name { get; set; } = "";
        public uint Hash { get; set; }

        public string Path { get; set; } = "";
        public bool FromArchive { get; set; }
        public bool IsYft { get; set; }

        public int LightCount { get; set; }
        public string Types { get; set; } = "";

        public override string ToString() => Name;
    }

    public class LightPropLibrary
    {
        private readonly List<LightPropEntry> entries = new List<LightPropEntry>();
        private readonly object gate = new object();

        public volatile bool Scanning;
        public volatile bool ArchiveScanned;
        public string Status = "";
        public volatile int Scanned;
        public volatile int Total;

        private CancellationTokenSource cancel;

        public int ArchiveLimit;

        public int Count { get { lock (gate) return entries.Count; } }

        private static string CachePath =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "lightprops.json");

        public List<LightPropEntry> Snapshot()
        {
            lock (gate) return new List<LightPropEntry>(entries);
        }

        public List<LightPropEntry> Search(string query)
        {
            var all = Snapshot();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                all = all.Where(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            all.Sort((a, b) =>
            {
                if (a.FromArchive != b.FromArchive) return a.FromArchive ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return all;
        }

        public void Cancel() => cancel?.Cancel();

        public void BeginScan(GameFileManager game, IEnumerable<string> localFolders, bool includeArchives)
        {
            if (Scanning) return;
            Scanning = true;
            Scanned = 0;
            Total = 0;
            Status = "Starting...";
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            var folders = localFolders?.ToList() ?? new List<string>();

            Task.Run(() =>
            {
                try
                {
                    lock (gate) entries.RemoveAll(e => !e.FromArchive);
                    ScanLocal(folders, token);

                    if (includeArchives && game != null && game.Ready)
                    {
                        lock (gate) entries.RemoveAll(e => e.FromArchive);
                        ScanArchives(game, token);
                        ArchiveScanned = !token.IsCancellationRequested;
                    }
                    Save();
                    Status = token.IsCancellationRequested
                        ? $"Cancelled - {Count} light props found so far."
                        : $"{Count} light props.";
                }
                catch (Exception ex)
                {
                    Status = "Scan failed: " + ex.Message;
                }
                finally
                {
                    Scanning = false;
                }
            }, token);
        }

        private void ScanLocal(List<string> folders, CancellationToken token)
        {
            var files = new List<string>();
            foreach (var root in folders)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                foreach (var f in EnumerateSafe(root))
                {
                    var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".ydr" || ext == ".yft") files.Add(f);
                }
            }
            Total = files.Count;
            Status = $"Reading {files.Count} local models...";

            for (int i = 0; i < files.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                Scanned = i + 1;
                var path = files[i];
                try
                {
                    var data = File.ReadAllBytes(path);
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    bool yft = System.IO.Path.GetExtension(path).Equals(".yft", StringComparison.OrdinalIgnoreCase);
                    var lights = ReadLights(data, yft);
                    if (lights == null || lights.Length == 0) continue;
                    Add(new LightPropEntry
                    {
                        Name = name,
                        Hash = JenkHash.GenHash(name.ToLowerInvariant()),
                        Path = path,
                        FromArchive = false,
                        IsYft = yft,
                        LightCount = lights.Length,
                        Types = Describe(lights),
                    });
                }
                catch { }
            }
        }

        private void ScanArchives(GameFileManager game, CancellationToken token)
        {
            var rpfman = game.Cache?.RpfMan;
            if (rpfman?.EntryDict == null) return;

            var candidates = new List<RpfFileEntry>();
            foreach (var kv in rpfman.EntryDict)
            {
                if (!(kv.Value is RpfFileEntry fe)) continue;
                var n = fe.Name;
                if (n == null) continue;
                if (n.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith(".yft", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(fe);
            }

            Total = ArchiveLimit > 0 ? Math.Min(ArchiveLimit, candidates.Count) : candidates.Count;
            Status = $"Scanning {Total} game models...";

            for (int i = 0; i < Total; i++)
            {
                if (token.IsCancellationRequested) return;
                Scanned = i + 1;
                if ((i & 255) == 0) Status = $"Scanning game models {i}/{Total} ({Count} with lights)...";

                var fe = candidates[i];
                try
                {
                    bool yft = fe.Name.EndsWith(".yft", StringComparison.OrdinalIgnoreCase);
                    var lights = ReadArchiveLights(rpfman, fe, yft);
                    if (lights == null || lights.Length == 0) continue;
                    var name = System.IO.Path.GetFileNameWithoutExtension(fe.Name);
                    Add(new LightPropEntry
                    {
                        Name = name,
                        Hash = JenkHash.GenHash(name.ToLowerInvariant()),
                        Path = fe.Path,
                        FromArchive = true,
                        IsYft = yft,
                        LightCount = lights.Length,
                        Types = Describe(lights),
                    });
                }
                catch { }
            }
        }

        private static LightAttributes[] ReadArchiveLights(RpfManager rpfman, RpfFileEntry fe, bool isYft)
        {
            if (isYft)
            {
                var yft = rpfman.GetFile<YftFile>(fe);
                return yft?.Fragment?.LightAttributes?.data_items;
            }
            var ydr = rpfman.GetFile<YdrFile>(fe);
            return ydr?.Drawable?.LightAttributes?.data_items;
        }

        private static LightAttributes[] ReadLights(byte[] data, bool isYft)
        {
            if (isYft)
            {
                var yft = new YftFile();
                yft.Load(data);
                return yft.Fragment?.LightAttributes?.data_items;
            }
            var ydr = new YdrFile();
            ydr.Load(data);
            return ydr.Drawable?.LightAttributes?.data_items;
        }

        private static string Describe(LightAttributes[] lights)
        {
            int pt = 0, sp = 0, cap = 0;
            foreach (var l in lights)
            {
                switch ((byte)l.Type)
                {
                    case 1: pt++; break;
                    case 2: sp++; break;
                    case 4: cap++; break;
                }
            }
            var parts = new List<string>();
            if (pt > 0) parts.Add($"{pt} point");
            if (sp > 0) parts.Add($"{sp} spot");
            if (cap > 0) parts.Add($"{cap} capsule");
            return string.Join(", ", parts);
        }

        private void Add(LightPropEntry e)
        {
            lock (gate)
            {
                if (entries.Any(x => x.Hash == e.Hash && x.FromArchive == e.FromArchive)) return;
                entries.Add(e);
            }
        }

        private static IEnumerable<string> EnumerateSafe(string root)
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

        private class CacheFile
        {
            public bool ArchiveScanned { get; set; }
            public List<LightPropEntry> Entries { get; set; } = new List<LightPropEntry>();
        }

        public void Save()
        {
            try
            {
                var c = new CacheFile { ArchiveScanned = ArchiveScanned, Entries = Snapshot() };
                File.WriteAllText(CachePath, JsonSerializer.Serialize(c));
            }
            catch { }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
                if (c?.Entries == null) return;
                lock (gate)
                {
                    entries.Clear();
                    entries.AddRange(c.Entries);
                }
                ArchiveScanned = c.ArchiveScanned;
                Status = $"{c.Entries.Count} light props (cached).";
            }
            catch { }
        }
    }
}

