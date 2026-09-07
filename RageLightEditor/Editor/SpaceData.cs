using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class SpaceData
    {
        private readonly GameFileManager gameFiles;

        public SpaceData(GameFileManager gameFiles)
        {
            this.gameFiles = gameFiles;
        }

        internal GameFileCache Cache => gameFiles?.Cache;
        internal bool CacheReady => gameFiles != null && gameFiles.Ready && gameFiles.Cache != null;

        public volatile string Status = "";
        public volatile string Error = "";

        public volatile bool PathsReady;
        public volatile bool PathsLoading;
        public double PathsLoadMs;
        public SpaceNodeGrid NodeGrid;
        public Dictionary<uint, YndFile> AllYnds = new Dictionary<uint, YndFile>();
        public Space PathSpace;
        public int PathNodeCount;

        public void EnsurePaths()
        {
            if (PathsReady || PathsLoading || !CacheReady) return;
            PathsLoading = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Status = "Loading paths...";
                    LoadPaths();
                    PathsLoadMs = sw.Elapsed.TotalMilliseconds;
                    Status = $"Paths loaded: {AllYnds.Count} ynds, {PathNodeCount:N0} nodes in {PathsLoadMs:0} ms";
                    Console.WriteLine("SPACEDATA " + Status);
                    PathsReady = true;
                }
                catch (Exception ex)
                {
                    Error = "paths: " + ex.Message;
                    Status = "Paths failed: " + ex.Message;
                    Console.WriteLine("SPACEDATA paths failed: " + ex);
                }
                finally { PathsLoading = false; }
            });
        }

        private void LoadPaths()
        {
            var cache = Cache;
            var rpfman = cache.RpfMan;
            var grid = new SpaceNodeGrid();
            var all = new Dictionary<uint, YndFile>();

            var yndentries = new Dictionary<uint, RpfFileEntry>();
            void AddRpfYnds(RpfFile rpffile)
            {
                if (rpffile?.AllEntries == null) return;
                foreach (var entry in rpffile.AllEntries)
                {
                    if (entry is RpfFileEntry fentry && entry.NameLower.EndsWith(".ynd"))
                        yndentries[entry.NameHash] = fentry;
                }
            }
            foreach (var rpffile in cache.BaseRpfs) AddRpfYnds(rpffile);
            if (cache.EnableDlc)
            {
                var updrpf = rpfman.FindRpfFile("update\\update.rpf");
                if (updrpf?.Children != null)
                    foreach (var rpffile in updrpf.Children) AddRpfYnds(rpffile);
                foreach (var dlcrpf in cache.DlcActiveRpfs)
                {
                    if (dlcrpf.Path.StartsWith("x64")) continue;
                    if (dlcrpf.Children == null) continue;
                    foreach (var rpffile in dlcrpf.Children) AddRpfYnds(rpffile);
                }
            }

            var corner = new Vector3(-8192, -8192, -2048);
            var cellsize = new Vector3(512, 512, 4096);
            int nodes = 0;
            for (int x = 0; x < grid.CellCountX; x++)
            {
                for (int y = 0; y < grid.CellCountY; y++)
                {
                    var cell = grid.Cells[x, y];
                    string fname = "nodes" + cell.ID + ".ynd";
                    uint fnhash = JenkHash.GenHash(fname);
                    if (!yndentries.TryGetValue(fnhash, out var fentry)) continue;
                    YndFile ynd = null;
                    try { ynd = rpfman.GetFile<YndFile>(fentry); }
                    catch (Exception ex) { Console.WriteLine($"SPACEDATA {fname}: {ex.Message}"); }
                    if (ynd == null) continue;
                    ynd.BBMin = corner + (cellsize * new Vector3(x, y, 0));
                    ynd.BBMax = ynd.BBMin + cellsize;
                    ynd.CellX = x;
                    ynd.CellY = y;
                    ynd.Loaded = true;
                    cell.Ynd = ynd;
                    all[fnhash] = ynd;
                    nodes += ynd.Nodes?.Length ?? 0;
                }
                Status = $"Loading paths... {all.Count} ynds";
            }

            var space = new Space { NodeGrid = grid };
            var tverts = new List<EditorVertex>();
            var tlinks = new List<YndLink>();
            var nlinks = new List<YndLink>();
            foreach (var ynd in all.Values)
            {
                try { space.BuildYndData(ynd, tverts, tlinks, nlinks); }
                catch (Exception ex) { Console.WriteLine($"SPACEDATA {ynd.Name} links: {ex.Message}"); }
            }

            NodeGrid = grid;
            AllYnds = all;
            PathSpace = space;
            PathNodeCount = nodes;
        }

        public void GetYndsNear(Vector3 pos, float range, List<YndFile> ynds)
        {
            ynds.Clear();
            if (!PathsReady || NodeGrid == null) return;
            foreach (var ynd in AllYnds.Values)
            {
                if (ynd == null) continue;
                float dx = Math.Max(Math.Max(ynd.BBMin.X - pos.X, 0.0f), pos.X - ynd.BBMax.X);
                float dy = Math.Max(Math.Max(ynd.BBMin.Y - pos.Y, 0.0f), pos.Y - ynd.BBMax.Y);
                if (dx * dx + dy * dy <= range * range) ynds.Add(ynd);
            }
        }

        public void PathNodeMoved(YndNode node)
        {
            var ynd = node?.Ynd;
            if (ynd == null || PathSpace == null) return;
            try
            {
                PathSpace.BuildYndVerts(ynd, null);
                ynd.UpdateAllNodePositions();
                ynd.BuildBVH();
                ynd.HasChanged = true;
                if (node.Links != null)
                {
                    var seen = new HashSet<YndFile> { ynd };
                    foreach (var l in node.Links)
                    {
                        var oy = l?.Node2?.Ynd;
                        if (oy == null || !seen.Add(oy)) continue;
                        PathSpace.BuildYndVerts(oy, null);
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("SPACEDATA path node update: " + ex.Message); }
        }

        public volatile bool NavReady;
        public volatile bool NavLoading;
        public double NavLoadMs;
        public SpaceNavGrid NavGrid;
        public int NavCellCount;
        private readonly Dictionary<int, YnvFile> navLoaded = new Dictionary<int, YnvFile>();
        private readonly HashSet<int> navRequested = new HashSet<int>();
        private readonly Queue<int> navQueue = new Queue<int>();
        private readonly object navLock = new object();
        private volatile bool navWorkerRunning;
        public int NavLoadedCount { get { lock (navLock) return navLoaded.Count; } }
        public int NavPending { get { lock (navLock) return navQueue.Count; } }

        public void EnsureNav()
        {
            if (NavReady || NavLoading || !CacheReady) return;
            NavLoading = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Status = "Scanning nav meshes...";
                    LoadNavGrid();
                    NavLoadMs = sw.Elapsed.TotalMilliseconds;
                    Status = $"Nav grid ready: {NavCellCount} cells in {NavLoadMs:0} ms";
                    Console.WriteLine("SPACEDATA " + Status);
                    NavReady = true;
                }
                catch (Exception ex)
                {
                    Error = "nav: " + ex.Message;
                    Status = "Nav meshes failed: " + ex.Message;
                    Console.WriteLine("SPACEDATA nav failed: " + ex);
                }
                finally { NavLoading = false; }
            });
        }

        private void LoadNavGrid()
        {
            var cache = Cache;
            var rpfman = cache.RpfMan;
            var grid = new SpaceNavGrid();
            var ynventries = new Dictionary<uint, RpfFileEntry>();
            void AddRpfYnvs(RpfFile rpffile)
            {
                if (rpffile?.AllEntries == null) return;
                foreach (var entry in rpffile.AllEntries)
                {
                    if (entry is RpfFileEntry fentry && entry.NameLower.EndsWith(".ynv"))
                        ynventries[entry.NameHash] = fentry;
                }
            }
            foreach (var rpffile in cache.BaseRpfs) AddRpfYnvs(rpffile);
            if (cache.EnableDlc)
            {
                var updrpf = rpfman.FindRpfFile("update\\update.rpf");
                if (updrpf?.Children != null)
                    foreach (var rpffile in updrpf.Children) AddRpfYnvs(rpffile);
                foreach (var dlcrpf in cache.DlcActiveRpfs)
                {
                    if (dlcrpf.Children == null) continue;
                    foreach (var rpffile in dlcrpf.Children) AddRpfYnvs(rpffile);
                }
            }
            int cells = 0;
            for (int x = 0; x < grid.CellCountX; x++)
            {
                for (int y = 0; y < grid.CellCountY; y++)
                {
                    var cell = grid.Cells[x, y];
                    string fname = "navmesh[" + cell.FileX + "][" + cell.FileY + "].ynv";
                    uint fnhash = JenkHash.GenHash(fname);
                    if (ynventries.TryGetValue(fnhash, out var fentry))
                    {
                        cell.YnvEntry = fentry as RpfResourceFileEntry;
                        if (cell.YnvEntry != null) cells++;
                    }
                }
            }
            NavGrid = grid;
            NavCellCount = cells;
        }

        public void GetYnvsNear(Vector3 pos, float range, List<YnvFile> ynvs)
        {
            ynvs.Clear();
            if (!NavReady || NavGrid == null) return;
            int gridrange = Math.Max(1, (int)Math.Ceiling(range / NavGrid.CellSize));
            var cp = NavGrid.GetCellPos(pos);
            int minx = Math.Min(Math.Max(cp.X - gridrange, 0), NavGrid.CellCountX - 1);
            int maxx = Math.Min(Math.Max(cp.X + gridrange, 0), NavGrid.CellCountX - 1);
            int miny = Math.Min(Math.Max(cp.Y - gridrange, 0), NavGrid.CellCountY - 1);
            int maxy = Math.Min(Math.Max(cp.Y + gridrange, 0), NavGrid.CellCountY - 1);
            bool kick = false;
            lock (navLock)
            {
                for (int x = minx; x <= maxx; x++)
                {
                    for (int y = miny; y <= maxy; y++)
                    {
                        var cell = NavGrid.Cells[x, y];
                        if (cell?.YnvEntry == null) continue;
                        var cmin = NavGrid.GetCellMin(cell); var cmax = NavGrid.GetCellMax(cell);
                        float dx = Math.Max(Math.Max(cmin.X - pos.X, 0.0f), pos.X - cmax.X);
                        float dy = Math.Max(Math.Max(cmin.Y - pos.Y, 0.0f), pos.Y - cmax.Y);
                        if (dx * dx + dy * dy > range * range) continue;
                        if (navLoaded.TryGetValue(cell.ID, out var ynv)) { if (ynv != null) ynvs.Add(ynv); }
                        else if (navRequested.Add(cell.ID)) { navQueue.Enqueue(cell.ID); kick = true; }
                    }
                }
                if (kick && !navWorkerRunning) { navWorkerRunning = true; Task.Run(NavWorker); }
            }
        }

        private void NavWorker()
        {
            try
            {
                while (true)
                {
                    int id;
                    lock (navLock)
                    {
                        if (navQueue.Count == 0) { navWorkerRunning = false; return; }
                        id = navQueue.Dequeue();
                    }
                    var cell = NavGrid.GetCell(id);
                    YnvFile ynv = null;
                    if (cell?.YnvEntry != null)
                    {
                        try
                        {
                            ynv = Cache.RpfMan.GetFile<YnvFile>(cell.YnvEntry);
                            if (ynv != null) { ynv.Loaded = true; cell.Ynv = ynv; }
                        }
                        catch (Exception ex) { Console.WriteLine($"SPACEDATA {cell.YnvEntry.Name}: {ex.Message}"); }
                    }
                    lock (navLock) navLoaded[id] = ynv;
                    Status = $"Nav meshes: {navLoaded.Count} loaded" + (navQueue.Count > 0 ? $", {navQueue.Count} to go" : "");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SPACEDATA nav worker: " + ex);
                lock (navLock) navWorkerRunning = false;
            }
        }

        public void NavNodeMoved(YnvFile ynv)
        {
            if (ynv == null) return;
            try { ynv.UpdateAllNodePositions(); ynv.BuildBVH(); ynv.HasChanged = true; }
            catch (Exception ex) { Console.WriteLine("SPACEDATA nav node update: " + ex.Message); }
        }

        public volatile bool TrainsReady;
        public volatile bool TrainsLoading;
        public double TrainsLoadMs;
        public Trains Trains;

        public void EnsureTrains()
        {
            if (TrainsReady || TrainsLoading || !CacheReady) return;
            TrainsLoading = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Status = "Loading train tracks...";
                    var t = new Trains();
                    t.Init(Cache, s => { if (!string.IsNullOrEmpty(s)) Status = s; });
                    Trains = t;
                    TrainsLoadMs = sw.Elapsed.TotalMilliseconds;
                    int nodes = 0; foreach (var tr in t.TrainTracks) nodes += tr.Nodes?.Count ?? 0;
                    Status = $"Train tracks loaded: {t.TrainTracks.Count} tracks, {nodes:N0} nodes in {TrainsLoadMs:0} ms";
                    Console.WriteLine("SPACEDATA " + Status);
                    TrainsReady = true;
                }
                catch (Exception ex)
                {
                    Error = "trains: " + ex.Message;
                    Status = "Train tracks failed: " + ex.Message;
                    Console.WriteLine("SPACEDATA trains failed: " + ex);
                }
                finally { TrainsLoading = false; }
            });
        }

        public void TrainNodeMoved(TrainTrackNode node)
        {
            var track = node?.Track;
            if (track == null) return;
            try { track.BuildVertices(); track.UpdateBvhForNode(node); track.HasChanged = true; }
            catch (Exception ex) { Console.WriteLine("SPACEDATA train node update: " + ex.Message); }
        }

        public volatile bool ScenariosReady;
        public volatile bool ScenariosLoading;
        public double ScenariosLoadMs;
        public Scenarios Scenarios;

        public void EnsureScenarios()
        {
            if (ScenariosReady || ScenariosLoading || !CacheReady) return;
            ScenariosLoading = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Status = "Loading scenarios...";
                    var s = new Scenarios();
                    s.Init(Cache, st => { if (!string.IsNullOrEmpty(st)) Status = st; }, null);
                    Scenarios = s;
                    ScenariosLoadMs = sw.Elapsed.TotalMilliseconds;
                    int nodes = 0; foreach (var r in s.ScenarioRegions) nodes += r.ScenarioRegion?.Nodes?.Count ?? 0;
                    Status = $"Scenarios loaded: {s.ScenarioRegions.Count} regions, {nodes:N0} points in {ScenariosLoadMs:0} ms";
                    Console.WriteLine("SPACEDATA " + Status);
                    ScenariosReady = true;
                }
                catch (Exception ex)
                {
                    Error = "scenarios: " + ex.Message;
                    Status = "Scenarios failed: " + ex.Message;
                    Console.WriteLine("SPACEDATA scenarios failed: " + ex);
                }
                finally { ScenariosLoading = false; }
            });
        }

        public void ScenarioNodeMoved(ScenarioNode node)
        {
            var sr = node?.Ymt?.ScenarioRegion;
            if (sr == null) return;
            try { sr.BuildVertices(); sr.BuildBVH(); if (node.Ymt != null) node.Ymt.HasChanged = true; }
            catch (Exception ex) { Console.WriteLine("SPACEDATA scenario node update: " + ex.Message); }
        }

        public volatile bool AudioReady;
        public volatile bool AudioLoading;
        public double AudioLoadMs;
        public AudioZones AudioZones;
        public List<AudioPlacement> AudioPlacements = new List<AudioPlacement>();

        public void EnsureAudio()
        {
            if (AudioReady || AudioLoading || !CacheReady) return;
            AudioLoading = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var cache = Cache;
                    if (cache.AudioDatRelFiles == null || cache.AudioDatRelFiles.Count == 0)
                    {
                        Status = "Loading audio .rel files...";
                        cache.LoadAudio = true;
                        cache.InitAudio();
                    }
                    Status = "Building audio zones...";
                    var az = new AudioZones();
                    az.Init(cache, s => { if (!string.IsNullOrEmpty(s)) Status = s; });
                    var all = new List<AudioPlacement>();
                    az.GetPlacements(cache.AudioDatRelFiles, all);
                    AudioZones = az;
                    AudioPlacements = all;
                    AudioLoadMs = sw.Elapsed.TotalMilliseconds;
                    Status = $"Audio zones loaded: {all.Count:N0} placements from {cache.AudioDatRelFiles.Count} rel files in {AudioLoadMs:0} ms";
                    Console.WriteLine("SPACEDATA " + Status);
                    AudioReady = true;
                }
                catch (Exception ex)
                {
                    Error = "audio: " + ex.Message;
                    Status = "Audio zones failed: " + ex.Message;
                    Console.WriteLine("SPACEDATA audio failed: " + ex);
                }
                finally { AudioLoading = false; }
            });
        }

        public bool Busy => PathsLoading || NavLoading || TrainsLoading || ScenariosLoading || AudioLoading || navWorkerRunning;
    }
}

