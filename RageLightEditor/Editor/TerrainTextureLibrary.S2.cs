using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public sealed class TerrainTextureLibrary
    {
        public sealed class Entry
        {
            public string Name;
            public string Ytd;
            public string Group;
            public uint NameHash;
            public ushort Width, Height;
            public IntPtr Thumb;
            public bool ThumbTried;
        }

        public const int Version = 3;

        public static readonly string[] Groups =
            { "All", "Grass", "Dirt", "Sand", "Rock", "Gravel", "Road", "Snow", "Concrete", "Other" };

        private static readonly string[] YtdHints =
        {
            "terrain", "_lyr", "4lyr", "ground", "grass", "gras_", "dirt", "sand", "gravel", "rock",
            "mud", "soil", "snow", "cliff", "beach", "road", "tarmac", "asphalt", "mapdetail",
            "detail", "im_", "rsn_", "gnd", "floor_", "concrete",
        };

        private static readonly string[] StrongYtdHints = { "terrain", "_lyr", "4lyr", "mapdetail" };

        private static readonly string[] NotALayer =
        {
            "_norm", "_nrm", "_nm", "_bump", "_bmp", "_spec", "_specular", "_gloss", "_mask",
            "_lod", "slod", "decal", "_alpha", "_ao", "_blend", "_tm", "_dm",
        };

        private static readonly (string Word, string Group)[] NameHints =
        {
            ("grass", "Grass"), ("gras", "Grass"), ("lawn", "Grass"), ("turf", "Grass"), ("moss", "Grass"),
            ("dirt", "Dirt"), ("mud", "Dirt"), ("soil", "Dirt"), ("earth", "Dirt"), ("dust", "Dirt"),
            ("sand", "Sand"), ("beach", "Sand"), ("desert", "Sand"), ("dune", "Sand"),
            ("rock", "Rock"), ("cliff", "Rock"), ("stone", "Rock"), ("slate", "Rock"), ("shale", "Rock"),
            ("gravel", "Gravel"), ("pebble", "Gravel"), ("scree", "Gravel"), ("rubble", "Gravel"),
            ("road", "Road"), ("tarmac", "Road"), ("asphalt", "Road"), ("kerb", "Road"), ("pave", "Road"),
            ("snow", "Snow"), ("ice", "Snow"),
            ("concrete", "Concrete"), ("cement", "Concrete"), ("tile", "Concrete"), ("floor", "Concrete"),
            ("terrain", "Other"), ("ground", "Other"), ("gnd", "Other"),
        };

        private readonly List<Entry> entries = new List<Entry>();
        private readonly object gate = new object();

        public volatile bool Ready;
        public volatile bool Running;
        public volatile bool FromCache;
        public volatile int Done, Total;
        public string Status = "not started";

        public int Count { get { lock (gate) return entries.Count; } }

        public void Begin(GameFileManager game)
        {
            if (Running || Ready || game == null || !game.Ready || game.Cache == null) return;
            Running = true;
            Status = "looking for the ground textures...";
            var folder = game.Folder;
            Task.Run(() =>
            {
                try
                {
                    if (Load(CachePath(folder)))
                    {
                        FromCache = true;
                        Status = $"{Count:N0} ground textures";
                        return;
                    }
                    var t0 = DateTime.UtcNow;
                    Sweep(game);
                    Save(CachePath(folder));
                    Status = $"{Count:N0} ground textures";
                    Console.WriteLine($"TERRAINLIB swept {Done:N0} of {Total:N0} dictionaries in " +
                                      $"{(DateTime.UtcNow - t0).TotalSeconds:0.0} s -> {Count:N0} ground textures");
                }
                catch (Exception ex)
                {
                    Status = "the sweep failed: " + ex.Message;
                    Console.WriteLine("TERRAINLIB sweep failed: " + ex);
                }
                finally { Ready = true; Running = false; }
            });
        }

        private void Sweep(GameFileManager game)
        {
            var candidates = new List<RpfFileEntry>();
            foreach (var rpf in game.Cache.AllRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var e in rpf.AllEntries)
                {
                    if (!(e is RpfFileEntry fe)) continue;
                    var n = fe.NameLower ?? "";
                    if (!n.EndsWith(".ytd", StringComparison.Ordinal)) continue;
                    if (!Looks(n, YtdHints)) continue;
                    candidates.Add(fe);
                }
            }
            Total = candidates.Count;

            var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
            Parallel.ForEach(candidates, opts, fe =>
            {
                try
                {
                    var lower = fe.NameLower ?? "";
                    if (Looks(lower, NotALayer)) return;
                    bool strong = Looks(lower, StrongYtdHints);
                    var ytd = new YtdFile(fe);
                    if (!game.Cache.LoadFile(ytd)) return;
                    var texs = ytd.TextureDict?.Textures?.data_items;
                    if (texs == null) return;
                    var mine = new List<Entry>();
                    foreach (var t in texs)
                    {
                        if (t == null || string.IsNullOrEmpty(t.Name)) continue;
                        var tn = t.Name.ToLowerInvariant();
                        if (tn.EndsWith("_n") || tn.EndsWith("_s") || tn.EndsWith("_b")) continue;
                        if (Looks(tn, NotALayer)) continue;
                        if (t.Width < 128 || t.Height < 128) continue;
                        string group = GroupOf(tn);
                        if (group == null)
                        {
                            if (!strong) continue;
                            group = "Other";
                        }
                        mine.Add(new Entry
                        {
                            Name = t.Name,
                            Ytd = Path.GetFileNameWithoutExtension(fe.Name ?? ""),
                            Group = group,
                            NameHash = t.NameHash,
                            Width = t.Width,
                            Height = t.Height,
                        });
                    }
                    if (mine.Count > 0)
                        lock (gate) foreach (var m in mine) entries.Add(m);
                }
                catch { }
                finally { System.Threading.Interlocked.Increment(ref doneCount); Done = doneCount; }
            });

            Dedupe();
        }

        private int doneCount;

        private void Dedupe()
        {
            lock (gate)
            {
                var seen = new HashSet<uint>();
                var keep = new List<Entry>(entries.Count);
                entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var e in entries) if (seen.Add(e.NameHash)) keep.Add(e);
                entries.Clear();
                entries.AddRange(keep);
            }
        }

        private static bool Looks(string s, string[] hints)
        {
            foreach (var h in hints) if (s.IndexOf(h, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string GroupOf(string lowerName)
        {
            foreach (var (word, group) in NameHints)
                if (lowerName.IndexOf(word, StringComparison.Ordinal) >= 0) return group;
            return null;
        }

        public int Search(string query, string group, List<Entry> into, int max = 400)
        {
            into.Clear();
            query = (query ?? "").Trim().ToLowerInvariant();
            bool anyGroup = string.IsNullOrEmpty(group) || group == "All";
            int total = 0;
            lock (gate)
            {
                foreach (var e in entries)
                {
                    if (!anyGroup && e.Group != group) continue;
                    if (query.Length > 0 &&
                        e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (e.Ytd ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    total++;
                    if (into.Count < max) into.Add(e);
                }
            }
            return total;
        }

        public Dictionary<string, int> GroupCounts()
        {
            var d = new Dictionary<string, int>();
            lock (gate)
                foreach (var e in entries)
                {
                    d.TryGetValue(e.Group, out int n);
                    d[e.Group] = n + 1;
                }
            return d;
        }

        private static string CachePath(string folder)
        {
            uint h = 2166136261u;
            foreach (var c in (folder ?? "").ToLowerInvariant()) { h ^= c; h *= 16777619u; }
            return Path.Combine(AppContext.BaseDirectory, "terraintex_" + h.ToString("X8") + ".bin");
        }

        private bool Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using var fs = File.OpenRead(path);
                using var r = new BinaryReader(fs);
                if (r.ReadInt32() != Version) return false;
                int n = r.ReadInt32();
                if (n <= 0 || n > 500000) return false;
                lock (gate)
                {
                    entries.Clear();
                    for (int i = 0; i < n; i++)
                        entries.Add(new Entry
                        {
                            Name = r.ReadString(),
                            Ytd = r.ReadString(),
                            Group = r.ReadString(),
                            NameHash = r.ReadUInt32(),
                            Width = r.ReadUInt16(),
                            Height = r.ReadUInt16(),
                        });
                }
                Total = Done = n;
                return true;
            }
            catch { return false; }
        }

        private void Save(string path)
        {
            try
            {
                using var fs = File.Create(path);
                using var w = new BinaryWriter(fs);
                w.Write(Version);
                lock (gate)
                {
                    w.Write(entries.Count);
                    foreach (var e in entries)
                    {
                        w.Write(e.Name ?? "");
                        w.Write(e.Ytd ?? "");
                        w.Write(e.Group ?? "Other");
                        w.Write(e.NameHash);
                        w.Write(e.Width);
                        w.Write(e.Height);
                    }
                }
            }
            catch { }
        }

        public List<Entry> All()
        {
            lock (gate) return new List<Entry>(entries);
        }
    }
}

