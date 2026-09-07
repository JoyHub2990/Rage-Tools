using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer : IDisposable
    {
        public class MapNode
        {
            public uint Hash;
            public string Name;
            public Vector3 Min, Max;
            public YmapFile Ymap;
            public volatile bool Queued;
            public volatile bool RawLoaded;
            public bool Prepared;
            public volatile bool LoadFailed;
            public string FailReason;
            public uint ParentHash;
            public uint ContentFlags;
            public float RangeScale = 1.0f;
            public YmapFile PendingParent;
            public MapNode ParentNode;
            public int LastWantedTick;
            public float MaxReach;

            public float DistanceTo(Vector3 p) => DistanceTo(p, 1.0f);

            public float DistanceTo(Vector3 p, float heightWeight)
            {
                var c = Vector3.Clamp(p, Min, Max);
                float dx = c.X - p.X, dy = c.Y - p.Y, dz = c.Z - p.Z;
                if (dz < 0.0f) dz *= heightWeight;
                return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        private readonly Dictionary<uint, MapNode> nodes = new Dictionary<uint, MapNode>();
        private GameFileCache cache;
        private GameFileManager files;

        public bool Ready { get; private set; }
        public int NodeCount => nodes.Count;
        public IEnumerable<MapNode> Nodes => nodes.Values;
        public IEnumerable<YmapFile> ResidentYmaps
        {
            get { foreach (var n in nodes.Values) if (n.Ymap != null && n.Prepared) yield return n.Ymap; }
        }

        public float StreamRadius = 500.0f;
        public float LodScale = 1.0f;
        public int MaxEntities = 40000;

        public bool Truncated { get; private set; }
        public float EvictFactor = 2.0f;

        public readonly List<YmapEntityDef> Visible = new List<YmapEntityDef>();
        public readonly List<float> Fade = new List<float>();
        public int YmapsOpen { get; private set; }
        public int YmapsWanted { get; private set; }
        public int YmapsWalked { get; private set; }

        public void Build(GameFileManager manager)
        {
            files = manager;
            var gameCache = manager?.Cache;
            nodes.Clear();
            Ready = false;
            cache = gameCache;
            if (cache?.YmapHierarchyDict == null) return;

            foreach (var kv in cache.YmapHierarchyDict)
            {
                var n = kv.Value;
                if (n == null) continue;
                var min = n.streamingExtentsMin;
                var max = n.streamingExtentsMax;
                if (!(max.X > min.X) || !(max.Y > min.Y)) continue;
                if (IsDlcInvalidated(kv.Key)) continue;

                nodes[kv.Key] = new MapNode
                {
                    Hash = kv.Key,
                    Name = n.Name.ToString(),
                    Min = new Vector3(min.X, min.Y, min.Z),
                    Max = new Vector3(max.X, max.Y, max.Z),
                    ParentHash = n.ParentName.Hash,
                    ContentFlags = n.ContentFlags,
                    RangeScale = RangeScaleFor(n.ContentFlags),
                };
            }
            Ready = nodes.Count > 0;
            InitManifestData();
            ReportDlcFiles();
            BeginUncachedScan();
        }

        private readonly Dictionary<uint, uint> ymapTimes = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, MetaHash[]> ymapWeathers = new Dictionary<uint, MetaHash[]>();
        public int TimedYmapCount => ymapTimes.Count;
        public int WeatherYmapCount => ymapWeathers.Count;

        private void InitManifestData()
        {
            ymapTimes.Clear(); ymapWeathers.Clear();
            var manifests = cache?.AllManifests;
            if (manifests == null) return;
            foreach (var manifest in manifests)
            {
                var groups = manifest?.MapDataGroups;
                if (groups == null) continue;
                foreach (var mapgroup in groups)
                {
                    if (mapgroup == null) continue;
                    if (mapgroup.HoursOnOff != 0) ymapTimes[mapgroup.Name] = mapgroup.HoursOnOff;
                    if (mapgroup.WeatherTypes != null) ymapWeathers[mapgroup.Name] = mapgroup.WeatherTypes;
                }
            }
        }

        public bool IsYmapAvailable(uint ymapHash, int hour, uint weather)
        {
            if (hour >= 0 && hour <= 23 && ymapTimes.TryGetValue(ymapHash, out uint ymaptime))
            {
                uint mask = 1u << hour;
                if ((ymaptime & mask) == 0) return false;
            }
            if (weather != 0 && ymapWeathers.TryGetValue(ymapHash, out var weathers))
            {
                for (int i = 0; i < weathers.Length; i++) if (weathers[i].Hash == weather) return true;
                return false;
            }
            return true;
        }
        public bool IsScheduledYmap(uint ymapHash) => ymapTimes.ContainsKey(ymapHash) || ymapWeathers.ContainsKey(ymapHash);

        public bool YmapHourFilter
        {
            get => ymapHourFilter;
            set { if (value != ymapHourFilter) { ymapHourFilter = value; residentVersion++; worldChanged = true; } }
        }
        private bool ymapHourFilter = true;
        public bool YmapWeatherFilter
        {
            get => ymapWeatherFilter;
            set { if (value != ymapWeatherFilter) { ymapWeatherFilter = value; residentVersion++; worldChanged = true; } }
        }
        private bool ymapWeatherFilter = true;
        public uint WeatherHash
        {
            get => weatherHash;
            set { if (value != weatherHash) { weatherHash = value; residentVersion++; worldChanged = true; } }
        }
        private uint weatherHash;
        public float Hour
        {
            get => hour;
            set
            {
                if ((int)value != (int)hour) { residentVersion++; worldChanged = true; }
                hour = value;
            }
        }
        private float hour = 12.0f;
        public int HourFilteredYmaps { get; private set; }

        private System.Threading.Tasks.Task uncachedScan;
        private readonly System.Collections.Concurrent.ConcurrentQueue<MapNode> uncachedFound =
            new System.Collections.Concurrent.ConcurrentQueue<MapNode>();
        public int UncachedYmaps { get; private set; }
        public bool UncachedScanDone { get; private set; }

        private void BeginUncachedScan()
        {
            if (cache?.ActiveMapRpfFiles == null || cache.RpfMan == null) { UncachedScanDone = true; return; }
            var known = new HashSet<uint>(nodes.Keys);
            var rpfs = cache.ActiveMapRpfFiles.Values.ToList();
            var rpfMan = cache.RpfMan;
            UncachedScanDone = false;
            uncachedScan = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var seen = new HashSet<uint>();
                    foreach (var rpf in rpfs)
                    {
                        if (rpf?.AllEntries == null) continue;
                        foreach (var entry in rpf.AllEntries)
                        {
                            if (!(entry is RpfFileEntry fe)) continue;
                            if (!entry.NameLower.EndsWith(".ymap")) continue;
                            uint h = fe.ShortNameHash;
                            if (known.Contains(h) || !seen.Add(h)) continue;
                            if (IsDlcInvalidated(h)) continue;
                            YmapFile ymap = null;
                            try { ymap = rpfMan.GetFile<YmapFile>(entry); } catch { }
                            if (ymap == null) continue;
                            var dsn = new MapDataStoreNode(ymap);
                            if (dsn.Name == 0) continue;
                            var min = dsn.streamingExtentsMin; var max = dsn.streamingExtentsMax;
                            if (!(max.X > min.X) || !(max.Y > min.Y)) continue;
                            uncachedFound.Enqueue(new MapNode
                            {
                                Hash = dsn.Name,
                                Name = dsn.Name.ToString(),
                                Min = new Vector3(min.X, min.Y, min.Z),
                                Max = new Vector3(max.X, max.Y, max.Z),
                                ParentHash = dsn.ParentName.Hash,
                                ContentFlags = dsn.ContentFlags,
                                RangeScale = RangeScaleFor(dsn.ContentFlags),
                            });
                        }
                    }
                }
                catch { }
                finally { UncachedScanDone = true; }
            });
        }

        private void TakeUncachedNodes()
        {
            int added = 0;
            while (uncachedFound.TryDequeue(out var n))
            {
                if (nodes.ContainsKey(n.Hash)) continue;
                nodes[n.Hash] = n;
                added++;
            }
            if (added > 0)
            {
                UncachedYmaps += added;
                near = null;
                worldChanged = true;
            }
        }

        public static float RangeScaleFor(uint contentFlags)
        {
            if ((contentFlags & (4u | 16u)) != 0) return 24.0f;
            if ((contentFlags & 2u) != 0) return 6.0f;
            return 1.0f;
        }

        public List<MapNode> NodesNear(Vector3 p)
        {
            var list = new List<MapNode>();
            float r = EffectiveRadius > 0.0f ? EffectiveRadius : StreamRadius;
            foreach (var n in nodes.Values)
                if (n.DistanceTo(p) <= r * n.RangeScale) list.Add(n);
            list.Sort((a, b) => a.DistanceTo(p).CompareTo(b.DistanceTo(p)));
            return list;
        }

        private readonly BlockingCollection<MapNode> queue =
            new BlockingCollection<MapNode>(new ConcurrentQueue<MapNode>());
        private Thread loader;
        private volatile bool stopping;

        public int LoadsPending => queue.Count;
        public int YmapsResident { get; private set; }

        private void StartLoader()
        {
            if (loader != null) return;
            stopping = false;
            loader = new Thread(LoaderProc)
            {
                IsBackground = true,
                Name = "WorldStreamer",
                Priority = ThreadPriority.Normal,
            };
            loader.Start();
        }

        public string LoaderStatus =>
            $"ymaps thread {(loader == null ? "not started" : loader.IsAlive ? "alive" : "DEAD")} queue {queue.Count} inFlight {InFlight} loaderErrors {LoaderErrors} lastError [{LastLoaderError}]";
        public int LoaderErrors { get; private set; }
        public string LastLoaderError { get; private set; } = "";
        private int InFlight { get { int c = 0; foreach (var n in nodes.Values) if (n.Queued) c++; return c; } }

        private void LoaderProc()
        {
            while (!stopping)
            {
                MapNode n;
                try { n = queue.Take(); }
                catch { return; }
                if (n == null || stopping) continue;
                try { LoadOne(n); }
                catch (Exception ex) { LoaderErrors++; LastLoaderError = ex.GetType().Name + ": " + ex.Message; n.LoadFailed = true; n.Queued = false; }
            }
        }

        private void LoadOne(MapNode n)
        {
            {
                try
                {
                    if (n.Ymap == null || !files.EnsureLoaded(n.Ymap)) { n.LoadFailed = true; n.FailReason = n.Ymap == null ? "loader: no ymap object" : "loader: EnsureLoaded false (LoadFile failed)"; return; }

                    if (n.Ymap.Parent == null && n.ParentHash != 0)
                    {
                        var pnode = n.ParentNode;
                        var pm = (pnode != null && pnode.Ymap != null && pnode.Ymap.Loaded) ? pnode.Ymap : cache.GetYmap(n.ParentHash);
                        if (pm != null && files.EnsureLoaded(pm)) n.PendingParent = pm;
                    }
                    n.RawLoaded = true;
                }
                catch (Exception ex) { n.LoadFailed = true; n.FailReason = "loader " + ex.GetType().Name + ": " + ex.Message; LoaderErrors++; LastLoaderError = n.FailReason; }
                finally { n.Queued = false; }
            }
        }

        private int tick;
        private readonly List<MapNode> open = new List<MapNode>();
        private List<MapNode> near;
        private Vector3 lastSelectPos = new Vector3(float.MaxValue);

        public float ReselectDistance = 12.0f;
        public float AltitudeRadiusGain = 1.5f;
        public float GroundZ = 30.0f;
        public float EffectiveRadius { get; private set; }
        private float lastRadius = -1.0f;

        public float RewalkDistance = 0.75f;
        private Vector3 lastWalkPos = new Vector3(float.MaxValue);
        private float lastWalkLodScale = -1.0f;
        private bool worldChanged = true;
        public void Invalidate() => worldChanged = true;
        public int WalksSkipped { get; private set; }
        public Func<YmapFile, bool> IsPinned;

        public Dictionary<uint, YmapFile> ProjectOverrides;
        public bool HideGameMap;

        public bool ShowNorthYankton
        {
            get => showNorthYankton;
            set { if (value != showNorthYankton) { showNorthYankton = value; residentVersion++; worldChanged = true; } }
        }
        private bool showNorthYankton;
        private bool IsHiddenByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("shadowmesh", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return showNorthYankton
                ? name.StartsWith("h4_", StringComparison.OrdinalIgnoreCase)
                : name.StartsWith("prologue", StringComparison.OrdinalIgnoreCase);
        }

        public bool ShowScriptedYmaps
        {
            get => showScriptedYmaps;
            set { if (value != showScriptedYmaps) { showScriptedYmaps = value; residentVersion++; worldChanged = true; InvalidateVariantCache(); } }
        }
        private bool showScriptedYmaps = true;

        public bool ShowScriptedVariants
        {
            get => showScriptedVariants;
            set { if (value != showScriptedVariants) { showScriptedVariants = value; residentVersion++; worldChanged = true; InvalidateVariantCache(); } }
        }
        private bool showScriptedVariants;
        public int HiddenScriptedVariants { get; private set; }

        private struct BigEnt { public Vector3 Pos; public float R; public int Lod; public Vector3 Min, Max; public YmapEntityDef Ent; }
        private sealed class BigSet
        {
            public BigEnt[] Ents = Array.Empty<BigEnt>();
            public Vector3 Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue);
            public float MaxR;
            public bool Any => Ents.Length > 0;
            public int LowerBound(float x)
            {
                int lo = 0, hi = Ents.Length;
                while (lo < hi) { int mid = (lo + hi) >> 1; if (Ents[mid].Pos.X < x) lo = mid + 1; else hi = mid; }
                return lo;
            }
        }
        private readonly Dictionary<YmapFile, BigSet> bigEnts = new Dictionary<YmapFile, BigSet>();
        private static readonly BigSet NoBig = new BigSet();
        private const float BigEntRadius = 6.0f;
        public float VariantIoU = 0.6f;
        public float SamePivotIoU = 0.15f;
        public bool VariantsIncludeDlc = true;
        private static int LodClass(rage__eLodType t)
        {
            switch (t)
            {
                case rage__eLodType.LODTYPES_DEPTH_HD:
                case rage__eLodType.LODTYPES_DEPTH_ORPHANHD: return 0;
                case rage__eLodType.LODTYPES_DEPTH_LOD: return 1;
                case rage__eLodType.LODTYPES_DEPTH_SLOD1: return 2;
                case rage__eLodType.LODTYPES_DEPTH_SLOD2: return 3;
                case rage__eLodType.LODTYPES_DEPTH_SLOD3: return 4;
                default: return 5;
            }
        }
        private BigSet BigEntsOf(YmapFile y)
        {
            if (bigEnts.TryGetValue(y, out var set)) return set;
            var list = new List<BigEnt>();
            var all = y.AllEntities;
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            if (all != null)
                foreach (var e in all)
                {
                    if (e == null || e.BSRadius < BigEntRadius) continue;
                    list.Add(new BigEnt { Pos = e.Position, R = e.BSRadius, Lod = LodClass(e._CEntityDef.lodLevel), Min = e.BBMin, Max = e.BBMax, Ent = e });
                    mn = Vector3.Min(mn, e.BBMin); mx = Vector3.Max(mx, e.BBMax);
                }
            if (list.Count == 0) set = NoBig;
            else
            {
                list.Sort((a, b) => a.Pos.X.CompareTo(b.Pos.X));
                float maxR = 0; foreach (var b in list) if (b.R > maxR) maxR = b.R;
                set = new BigSet { Ents = list.ToArray(), Min = mn, Max = mx, MaxR = maxR };
            }
            bigEnts[y] = set;
            return set;
        }
        private bool BigOverlaps(YmapFile a, YmapFile b) => BigOverlaps(BigEntsOf(a), BigEntsOf(b));
        private static bool BigOverlaps(BigSet sa, BigSet sb)
        {
            if (!sa.Any || !sb.Any) return false;
            const float slack = 1.0f;
            return sa.Min.X <= sb.Max.X + slack && sa.Max.X + slack >= sb.Min.X &&
                   sa.Min.Y <= sb.Max.Y + slack && sa.Max.Y + slack >= sb.Min.Y &&
                   sa.Min.Z <= sb.Max.Z + slack && sa.Max.Z + slack >= sb.Min.Z;
        }
        public static bool SameLayer(YmapFile a, YmapFile b)
        {
            if (a == null || b == null || a.IsScripted || b.IsScripted) return false;
            var pa = a.RpfFileEntry?.Path; var pb = b.RpfFileEntry?.Path;
            if (string.IsNullOrEmpty(pa) || string.IsNullOrEmpty(pb)) return false;
            int ia = pa.LastIndexOf('\\'), ib = pb.LastIndexOf('\\');
            if (ia < 0 || ib < 0 || ia != ib) return false;
            return string.Compare(pa, 0, pb, 0, ia, StringComparison.OrdinalIgnoreCase) == 0;
        }
        private static bool IsDlcYmap(YmapFile y)
        {
            var p = y?.RpfFileEntry?.Path;
            if (string.IsNullOrEmpty(p)) return false;
            return p.IndexOf("dlcpacks", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   p.IndexOf("dlc_patch", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static float BoxIoU(in Vector3 amn, in Vector3 amx, in Vector3 bmn, in Vector3 bmx)
        {
            float ix = Math.Min(amx.X, bmx.X) - Math.Max(amn.X, bmn.X); if (ix <= 0) return 0;
            float iy = Math.Min(amx.Y, bmx.Y) - Math.Max(amn.Y, bmn.Y); if (iy <= 0) return 0;
            float iz = Math.Min(amx.Z, bmx.Z) - Math.Max(amn.Z, bmn.Z); if (iz <= 0) return 0;
            float inter = ix * iy * iz;
            var ad = amx - amn; var bd = bmx - bmn;
            float va = Math.Max(ad.X, 0) * Math.Max(ad.Y, 0) * Math.Max(ad.Z, 0);
            float vb = Math.Max(bd.X, 0) * Math.Max(bd.Y, 0) * Math.Max(bd.Z, 0);
            float uni = va + vb - inter;
            return uni > 1e-6f ? inter / uni : 0;
        }
        private static bool Overlaps(YmapFile a, YmapFile b)
        {
            var amn = a._CMapData.streamingExtentsMin; var amx = a._CMapData.streamingExtentsMax;
            var bmn = b._CMapData.streamingExtentsMin; var bmx = b._CMapData.streamingExtentsMax;
            return amn.X <= bmx.X && amx.X >= bmn.X && amn.Y <= bmx.Y && amx.Y >= bmn.Y;
        }
        public long CoincidentCalls, CoincidentInner, CoincidentMine;
        public double VariantSetupMs, VariantBaseMs, VariantKeptMs, VariantIncMs, VariantLeftMs;
        private int Coincident(BigEnt[] mine, BigSet theirSet, bool[] hit)
        {
            int n = 0;
            float iou = VariantIoU;
            var theirs = theirSet.Ents;
            CoincidentCalls++; CoincidentMine += mine.Length;
            for (int i = 0; i < mine.Length; i++)
            {
                if (hit[i]) { n++; continue; }
                var m = mine[i];
                float win = 2.6f * m.R + 1.0f;
                int j0 = theirSet.LowerBound(m.Pos.X - win);
                float xEnd = m.Pos.X + win;
                for (int j = j0; j < theirs.Length; j++)
                {
                    var t = theirs[j];
                    if (t.Pos.X > xEnd) break;
                    CoincidentInner++;
                    if (t.Lod != m.Lod) continue;
                    if (Math.Abs(t.R - m.R) > Math.Max(t.R, m.R) * 0.35f) continue;
                    float d2 = Vector3.DistanceSquared(t.Pos, m.Pos);
                    if (d2 > 0.75f * 0.75f)
                    {
                        float rr = t.R + m.R;
                        if (d2 > rr * rr) continue;
                        if (BoxIoU(t.Min, t.Max, m.Min, m.Max) < iou) continue;
                    }
                    else if (BoxIoU(t.Min, t.Max, m.Min, m.Max) < SamePivotIoU) continue;
                    hit[i] = true; n++; break;
                }
            }
            return n;
        }

        private readonly List<YmapFile> baseYmaps = new List<YmapFile>();
        private readonly List<KeyValuePair<uint, YmapFile>> variantYmaps = new List<KeyValuePair<uint, YmapFile>>();
        private readonly List<YmapFile> keptVariants = new List<YmapFile>();

        private sealed class VariantDecision { public bool[] Hit; public int Hits; public YmapEntityDef[] Hidden; }
        private readonly Dictionary<uint, VariantDecision> variantDecision = new Dictionary<uint, VariantDecision>();
        private readonly Dictionary<uint, YmapFile> variantPrevCandidates = new Dictionary<uint, YmapFile>();
        private readonly List<YmapFile> variantArrived = new List<YmapFile>();
        private readonly List<YmapFile> variantLeft = new List<YmapFile>();
        private readonly HashSet<YmapFile> baseSet = new HashSet<YmapFile>();
        private readonly HashSet<YmapFile> keptSet = new HashSet<YmapFile>();
        private readonly HashSet<YmapEntityDef> variantHiddenEnts = new HashSet<YmapEntityDef>();
        public int VariantEvaluations { get; private set; }
        public int VariantIncrementals { get; private set; }
        public int VariantHiddenEntities => variantHiddenEnts.Count;
        public bool IsVariantHiddenEnt(YmapEntityDef e) => e != null && variantHiddenEnts.Contains(e);
        private void InvalidateVariantCache() { variantDecision.Clear(); variantPrevCandidates.Clear(); bigGrid.Clear(); }
        public int VariantEvalBudget = 6;
        public int VariantEvalEntityBudget = 400;
        public int VariantPending { get; private set; }

        private const float FineCell = 16.0f, CoarseCell = 160.0f, SplitR = 30.0f;
        private struct GridEnt { public BigEnt E; public YmapFile Y; }
        private sealed class GridLevel
        {
            public readonly float Cell;
            public readonly List<GridEnt> Ents = new List<GridEnt>();
            public readonly List<int> Next = new List<int>();
            public readonly Dictionary<long, int> Head = new Dictionary<long, int>();
            public GridLevel(float cell) { Cell = cell; }
            public static long CellKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
            public void Clear() { Ents.Clear(); Next.Clear(); Head.Clear(); }
            public int Add(BigEnt e, YmapFile y)
            {
                int idx = Ents.Count;
                Ents.Add(new GridEnt { E = e, Y = y });
                long key = CellKey((int)Math.Floor(e.Pos.X / Cell), (int)Math.Floor(e.Pos.Y / Cell));
                Head.TryGetValue(key, out int head);
                Next.Add(head);
                Head[key] = idx + 1;
                return idx;
            }
        }
        private sealed class BigGrid
        {
            public readonly GridLevel Fine = new GridLevel(FineCell), Coarse = new GridLevel(CoarseCell);
            public readonly Dictionary<YmapFile, int[]> IndicesOf = new Dictionary<YmapFile, int[]>();
            public int Dead;
            public int Count => Fine.Ents.Count + Coarse.Ents.Count;
            public void Clear() { Fine.Clear(); Coarse.Clear(); IndicesOf.Clear(); Dead = 0; }
            public bool Contains(YmapFile y) => IndicesOf.ContainsKey(y);
            public void Add(BigSet set, YmapFile y)
            {
                if (IndicesOf.ContainsKey(y)) return;
                var ents = set.Ents;
                var idxs = new int[ents.Length];
                for (int i = 0; i < ents.Length; i++)
                    idxs[i] = ents[i].R < SplitR ? Fine.Add(ents[i], y) * 2 : Coarse.Add(ents[i], y) * 2 + 1;
                IndicesOf[y] = idxs;
            }
            public void Remove(YmapFile y)
            {
                if (!IndicesOf.TryGetValue(y, out var idxs)) return;
                foreach (var i in idxs) { if ((i & 1) == 0) Fine.Ents[i >> 1] = default; else Coarse.Ents[i >> 1] = default; }
                Dead += idxs.Length;
                IndicesOf.Remove(y);
            }
            public void Compact(Func<YmapFile, BigSet> setOf)
            {
                if (Dead < 2000 || Dead * 2 < Count) return;
                var keep = new List<YmapFile>(IndicesOf.Keys);
                Clear();
                foreach (var y in keep) Add(setOf(y), y);
            }
        }
        private readonly BigGrid bigGrid = new BigGrid();
        private readonly BigGrid arrivalGrid = new BigGrid();
        private readonly Dictionary<YmapFile, string> layerOf = new Dictionary<YmapFile, string>();
        private string LayerOf(YmapFile y)
        {
            if (layerOf.TryGetValue(y, out var l)) return l;
            var p = y.RpfFileEntry?.Path;
            int i = string.IsNullOrEmpty(p) ? -1 : p.LastIndexOf('\\');
            l = i < 0 ? "" : p.Substring(0, i).ToLowerInvariant();
            layerOf[y] = l;
            return l;
        }
        private readonly Dictionary<YmapFile, int> orderOf = new Dictionary<YmapFile, int>();
        private readonly Dictionary<YmapFile, bool> pairExcluded = new Dictionary<YmapFile, bool>();
        private bool PairExcluded(YmapFile b, YmapFile y, int myOrder, uint parent, uint self, string myLayer)
        {
            if (pairExcluded.TryGetValue(b, out bool ex)) return ex;
            ex = !orderOf.TryGetValue(b, out int bo) || bo >= myOrder ||
                 (bo > 0 && IsFullyHiddenVariant(b)) ||
                 (b.RpfFileEntry != null && b.RpfFileEntry.ShortNameHash == parent) ||
                 (b._CMapData.parent != 0 && b._CMapData.parent == self) ||
                 (!b.IsScripted && !y.IsScripted && myLayer.Length > 0 && LayerOf(b) == myLayer);
            pairExcluded[b] = ex;
            return ex;
        }
        private bool IsFullyHiddenVariant(YmapFile b)
        {
            uint h = b.RpfFileEntry?.ShortNameHash ?? 0;
            return h != 0 && variantDecision.TryGetValue(h, out var d) && d?.Hit != null && d.Hits >= d.Hit.Length;
        }
        private int CoincidentGrid(BigGrid grid, YmapFile y, int myOrder, BigEnt[] mine, uint parent, uint self, bool[] hit)
        {
            int n = 0;
            pairExcluded.Clear();
            string myLayer = y.IsScripted ? "" : LayerOf(y);
            CoincidentCalls++; CoincidentMine += mine.Length;
            for (int i = 0; i < mine.Length; i++)
            {
                if (hit[i]) { n++; continue; }
                var m = mine[i];
                bool found = false;
                if (m.R * 1.54f < SplitR) found = QueryLevel(grid.Fine, y, myOrder, m, parent, self, myLayer);
                else if (m.R * 0.65f >= SplitR) found = QueryLevel(grid.Coarse, y, myOrder, m, parent, self, myLayer);
                else found = QueryLevel(grid.Fine, y, myOrder, m, parent, self, myLayer) || QueryLevel(grid.Coarse, y, myOrder, m, parent, self, myLayer);
                if (found) { hit[i] = true; n++; }
            }
            return n;
        }
        private bool QueryLevel(GridLevel lvl, YmapFile y, int myOrder, in BigEnt m, uint parent, uint self, string myLayer)
        {
            float iou = VariantIoU;
            var head = lvl.Head; var ents = lvl.Ents; var next = lvl.Next; float cell = lvl.Cell;
            float win = 2.6f * m.R + 1.0f;
            int cx0 = (int)Math.Floor((m.Pos.X - win) / cell), cx1 = (int)Math.Floor((m.Pos.X + win) / cell);
            int cy0 = (int)Math.Floor((m.Pos.Y - win) / cell), cy1 = (int)Math.Floor((m.Pos.Y + win) / cell);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    if (!head.TryGetValue(GridLevel.CellKey(cx, cy), out int idx)) continue;
                    for (; idx != 0; idx = next[idx - 1])
                    {
                        CoincidentInner++;
                        var g = ents[idx - 1];
                        if (g.Y == null) continue;
                        var t = g.E;
                        if (t.Lod != m.Lod) continue;
                        if (Math.Abs(t.R - m.R) > Math.Max(t.R, m.R) * 0.35f) continue;
                        float d2 = Vector3.DistanceSquared(t.Pos, m.Pos);
                        if (d2 > 0.75f * 0.75f)
                        {
                            float rr = t.R + m.R;
                            if (d2 > rr * rr) continue;
                            if (BoxIoU(t.Min, t.Max, m.Min, m.Max) < iou) continue;
                        }
                        else if (BoxIoU(t.Min, t.Max, m.Min, m.Max) < SamePivotIoU) continue;
                        if (g.Y == y || PairExcluded(g.Y, y, myOrder, parent, self, myLayer)) continue;
                        return true;
                    }
                }
            return false;
        }

        private readonly System.Diagnostics.Stopwatch vsw = new System.Diagnostics.Stopwatch();
        private void HideScriptedVariants()
        {
            vsw.Restart();
            HiddenScriptedVariants = 0;
            VariantPending = 0;
            baseYmaps.Clear(); variantYmaps.Clear(); keptVariants.Clear(); variantArrived.Clear(); variantLeft.Clear();
            baseSet.Clear(); keptSet.Clear(); orderOf.Clear();
            foreach (var kv in lodCandidates)
                if (!variantPrevCandidates.TryGetValue(kv.Key, out var prev) || prev != kv.Value) variantArrived.Add(kv.Value);
            foreach (var kv in variantPrevCandidates)
                if (!lodCandidates.TryGetValue(kv.Key, out var cur) || cur != kv.Value) variantLeft.Add(kv.Value);
            variantPrevCandidates.Clear();
            foreach (var kv in lodCandidates) variantPrevCandidates[kv.Key] = kv.Value;
            if (variantLeft.Count > 0)
            {
                var gone = new List<uint>();
                foreach (var kv in variantDecision) if (!lodCandidates.ContainsKey(kv.Key)) gone.Add(kv.Key);
                foreach (var h in gone) variantDecision.Remove(h);
            }

            foreach (var kv in lodCandidates)
            {
                var y = kv.Value;
                if (y?.AllEntities == null || y.AllEntities.Length == 0) continue;
                if (!stateWinners.Contains(y) && (y.IsScripted || (VariantsIncludeDlc && IsDlcYmap(y)))) variantYmaps.Add(kv); else { baseYmaps.Add(y); baseSet.Add(y); orderOf[y] = 0; }
            }
            variantHiddenEnts.Clear();
            foreach (var c in variantLeft) bigGrid.Remove(c);
            bigGrid.Compact(BigEntsOf);
            arrivalGrid.Clear();
            foreach (var c in variantArrived)
            {
                if (c?.AllEntities == null || c.AllEntities.Length == 0) continue;
                var cs = BigEntsOf(c);
                if (!cs.Any) continue;
                bigGrid.Add(cs, c);
                arrivalGrid.Add(cs, c);
            }
            if (bigEnts.Count > lodCandidates.Count + 256) ForgetBigEnts();
            if (variantYmaps.Count == 0) return;
            variantYmaps.Sort((a, b) =>
            {
                int sa = a.Value.IsScripted ? 1 : 0, sb = b.Value.IsScripted ? 1 : 0;
                if (sa != sb) return sa - sb;
                int oa = IsOriginalStateName(a.Value.Name) ? 1 : 0, ob = IsOriginalStateName(b.Value.Name) ? 1 : 0;
                if (oa != ob) return oa - ob;
                return string.CompareOrdinal(a.Value.Name ?? "", b.Value.Name ?? "");
            });
            for (int i = 0; i < variantYmaps.Count; i++) orderOf[variantYmaps[i].Value] = i + 1;
            VariantSetupMs += vsw.Elapsed.TotalMilliseconds;
            var arrMin = new Vector3(float.MaxValue); var arrMax = new Vector3(float.MinValue);
            foreach (var kv in arrivalGrid.IndicesOf) { var s = BigEntsOf(kv.Key); arrMin = Vector3.Min(arrMin, s.Min); arrMax = Vector3.Max(arrMax, s.Max); }
            bool anyArrivals = arrivalGrid.Count > 0;
            int fullEvals = 0, fullEvalEnts = 0;
            double vb0 = vsw.Elapsed.TotalMilliseconds;
            foreach (var kv in variantYmaps)
            {
                var y = kv.Value;
                var mySet = BigEntsOf(y);
                var mine = mySet.Ents;
                if (mine.Length == 0) { keptVariants.Add(y); continue; }
                uint parent = y._CMapData.parent;
                uint self = y.RpfFileEntry?.ShortNameHash ?? 0;
                int myOrder = orderOf[y];
                variantDecision.TryGetValue(kv.Key, out var dec);
                bool full = dec == null || dec.Hit.Length != mine.Length;
                if (!full && dec.Hits > 0)
                    foreach (var c in variantLeft) { if (c != y && BigOverlaps(BigEntsOf(c), mySet)) { full = true; break; } }
                if (full)
                {
                    if (fullEvals >= VariantEvalBudget || (fullEvals > 0 && fullEvalEnts + mine.Length > VariantEvalEntityBudget))
                    {
                        VariantPending++;
                        variantDecision.Remove(kv.Key);
                        keptVariants.Add(y); keptSet.Add(y);
                        continue;
                    }
                    fullEvals++; fullEvalEnts += mine.Length;
                    VariantEvaluations++;
                    dec = new VariantDecision { Hit = new bool[mine.Length] };
                    dec.Hits = CoincidentGrid(bigGrid, y, myOrder, mine, parent, self, dec.Hit);
                    dec.Hidden = null;
                    variantDecision[kv.Key] = dec;
                }
                else if (anyArrivals && dec.Hits < mine.Length &&
                         mySet.Min.X <= arrMax.X + 1 && mySet.Max.X + 1 >= arrMin.X && mySet.Min.Y <= arrMax.Y + 1 && mySet.Max.Y + 1 >= arrMin.Y &&
                         ArrivalTouches_V66(mySet))
                {
                    int before = dec.Hits;
                    dec.Hits = CoincidentGrid(arrivalGrid, y, myOrder, mine, parent, self, dec.Hit);
                    if (dec.Hits != before) { dec.Hidden = null; VariantIncrementals++; }
                }
                if (dec.Hits > 0 && dec.Hidden == null)
                {
                    dec.Hidden = new YmapEntityDef[dec.Hits];
                    int w = 0;
                    for (int i = 0; i < mine.Length && w < dec.Hits; i++) if (dec.Hit[i]) dec.Hidden[w++] = mine[i].Ent;
                }
                var hidden = dec.Hidden;
                if (hidden != null && hidden.Length > 0)
                {
                    foreach (var e in hidden) variantHiddenEnts.Add(e);
                    HiddenScriptedVariants++;
                    if (hidden.Length < mine.Length) { keptVariants.Add(y); keptSet.Add(y); }
                }
                else { keptVariants.Add(y); keptSet.Add(y); }
            }
            VariantKeptMs += vsw.Elapsed.TotalMilliseconds - vb0;
        }
        private bool ArrivalTouches_V66(BigSet mySet)
        {
            foreach (var c in variantArrived)
            {
                if (c == null) continue;
                var cs = BigEntsOf(c);
                if (cs.Any && BigOverlaps(cs, mySet)) return true;
            }
            return false;
        }

        private void ForgetBigEnts()
        {
            var keep = new HashSet<YmapFile>(lodCandidates.Values);
            var gone = new List<YmapFile>();
            foreach (var kv in bigEnts) if (!keep.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var y in gone) { bigEnts.Remove(y); layerOf.Remove(y); }
        }
        public int BigEntSetsCached => bigEnts.Count;
        public readonly List<YmapEntityDef> InteriorsEmitted = new List<YmapEntityDef>();
        public int InteriorsDuplicated { get; private set; }
        private readonly Dictionary<(int, int, int), YmapEntityDef> interiorOrigins = new Dictionary<(int, int, int), YmapEntityDef>();
        private static (int, int, int) OriginKey(Vector3 p) => ((int)Math.Round(p.X * 2), (int)Math.Round(p.Y * 2), (int)Math.Round(p.Z * 2));
        private void NoteInteriorEmitted(YmapEntityDef ent)
        {
            InteriorsEmitted.Add(ent);
            var k = OriginKey(ent.Position);
            if (interiorOrigins.TryGetValue(k, out var other) && !ReferenceEquals(other, ent)) InteriorsDuplicated++;
            else interiorOrigins[k] = ent;
        }
        private readonly HashSet<YmapEntityDef> interiorShellsHidden = new HashSet<YmapEntityDef>();
        public int InteriorShellsHidden => interiorShellsHidden.Count;
        public int InteriorVariantGroups { get; private set; }
        public bool IsInteriorShellHidden(YmapEntityDef e) => e != null && interiorShellsHidden.Contains(e);
        private bool IsProjectYmap(YmapFile y) =>
            y != null && ProjectOverrides != null && y.RpfFileEntry != null &&
            ProjectOverrides.TryGetValue(y.RpfFileEntry.ShortNameHash, out var p) && ReferenceEquals(p, y);
        private readonly Dictionary<(int, int, int), List<YmapEntityDef>> interiorGroups = new Dictionary<(int, int, int), List<YmapEntityDef>>();
        private void HideInteriorVariants()
        {
            interiorShellsHidden.Clear();
            InteriorVariantGroups = 0;
            interiorGroups.Clear();
            foreach (var kv in lodCandidates)
            {
                var mlos = kv.Value?.MloEntities;
                if (mlos == null) continue;
                foreach (var e in mlos)
                {
                    if (e?.MloInstance == null || e.Archetype == null) continue;
                    var k = OriginKey(e.Position);
                    if (!interiorGroups.TryGetValue(k, out var l)) interiorGroups[k] = l = new List<YmapEntityDef>(2);
                    l.Add(e);
                }
            }
            foreach (var g in interiorGroups.Values)
            {
                if (g.Count < 2) continue;
                g.Sort((a, b) =>
                {
                    int pa = IsProjectYmap(a.Ymap) ? 0 : 1, pb = IsProjectYmap(b.Ymap) ? 0 : 1;
                    if (pa != pb) return pa - pb;
                    int sa = (a.Ymap?.IsScripted ?? false) ? 1 : 0, sb = (b.Ymap?.IsScripted ?? false) ? 1 : 0;
                    if (sa != sb) return sa - sb;
                    int da = IsDlcYmap(a.Ymap) ? 1 : 0, db = IsDlcYmap(b.Ymap) ? 1 : 0;
                    if (da != db) return da - db;
                    int c = string.CompareOrdinal(a.Ymap?.Name ?? "", b.Ymap?.Name ?? "");
                    if (c != 0) return c;
                    return string.CompareOrdinal(a.Archetype?.Name ?? "", b.Archetype?.Name ?? "");
                });
                for (int i = 1; i < g.Count; i++) interiorShellsHidden.Add(g[i]);
                InteriorVariantGroups++;
            }
        }

        public bool IsInLodTree(uint hash) => lodYmaps.ContainsKey(hash);
        public string ExplainCoincident(YmapEntityDef e)
        {
            if (e == null || e.Ymap == null) return "";
            var mine = new[] { new BigEnt { Pos = e.Position, R = e.BSRadius, Lod = LodClass(e._CEntityDef.lodLevel), Min = e.BBMin, Max = e.BBMax, Ent = e } };
            var sb = new System.Text.StringBuilder();
            foreach (var kv in lodCandidates)
            {
                var y = kv.Value;
                if (y == null || y == e.Ymap) continue;
                var bs = BigEntsOf(y);
                if (!bs.Any) continue;
                var hit = new bool[1];
                if (Coincident(mine, bs, hit) == 0) continue;
                string who = "";
                foreach (var t in bs.Ents)
                    if (t.Lod == mine[0].Lod && Math.Abs(t.R - e.BSRadius) <= Math.Max(t.R, e.BSRadius) * 0.35f && (t.Pos - e.Position).Length() <= t.R + e.BSRadius)
                    { who = $"{t.Ent.Archetype?.Name ?? t.Ent._CEntityDef.archetypeName.ToString()}[{t.Ent._CEntityDef.lodLevel}] r{t.R:0} d {(t.Pos - e.Position).Length():0.0}"; break; }
                sb.Append($" {y.Name}(scripted {y.IsScripted} dlc {IsDlcYmap(y)} base {baseSet.Contains(y)} kept {keptSet.Contains(y)} path {y.RpfFileEntry?.Path}): {who};");
            }
            return sb.ToString();
        }
        public string DescribeYmap(YmapFile y, Vector3 camera)
        {
            if (y == null) return "null ymap";
            uint h = y.RpfFileEntry?.ShortNameHash ?? 0;
            var sb = new System.Text.StringBuilder();
            sb.Append($"{y.Name} hash {h} loaded {y.Loaded} ents {(y.AllEntities?.Length ?? 0)} roots {(y.RootEntities?.Length ?? 0)} parent {y._CMapData.parent} parentObj {(y.Parent != null)} scripted {y.IsScripted} ");
            sb.Append($"override {(ProjectOverrides != null && ProjectOverrides.TryGetValue(h, out var o) && ReferenceEquals(o, y))} candidate {(lodCandidates.TryGetValue(h, out var c) && ReferenceEquals(c, y))} inTree {(lodYmaps.TryGetValue(h, out var t) && ReferenceEquals(t, y))} ");
            sb.Append($"treeRoots {(lodRoots.TryGetValue(y, out var r) ? r.Count : -1)} node {(lodNodeOf.TryGetValue(y, out var n) ? n.Name + " reach " + n.MaxReach.ToString("0") : "-")} lodUpdate {y.LodManagerUpdate} truncated {Truncated} visible {Visible.Count}/{MaxEntities} walks {Walks} skipped {WalksSkipped}");
            var all = y.AllEntities;
            if (all != null)
                foreach (var e in all.Take(3))
                    sb.Append($" | ent {e?.Archetype?.Name ?? e?._CEntityDef.archetypeName.ToString()} d {(e.Position - camera).Length():0} lodDist {e.LodDist:0} final {IsFinalRender(e)} variantHidden {variantHiddenEnts.Contains(e)} parentEnt {(e.Parent != null)} visible {Visible.Contains(e)}");
            return sb.ToString();
        }
        public bool IsFinalRenderPublic(YmapEntityDef e) => e != null && IsFinalRender(e);
        public MapNode NodeOf(uint hash) => nodes.TryGetValue(hash, out var n) ? n : null;
        public bool IsCandidate(uint hash) => lodCandidates.ContainsKey(hash);
        public bool IsHiddenVariant(uint hash)
        {
            return variantDecision.TryGetValue(hash, out var d) && d != null && d.Hits > 0;
        }

        public int EvictInterval = 30;
        private int lastEvictTick;

        public void Select(Vector3 camera, int loadBudget = 8, int prepareBudget = 6)
        {
            if (!Ready) { Visible.Clear(); Fade.Clear(); YmapsOpen = 0; return; }
            StartLoader();
            tick++;
            files?.Tick();
            TakeUncachedNodes();

            float walkMoved = (camera - lastWalkPos).Length();
            bool arrivalsDue = pendingResident && (tick - lastArrivalWalkTick >= ArrivalWalkInterval || walkMoved > RewalkDistance);
            bool needWalk = worldChanged || arrivalsDue || walkMoved > RewalkDistance || LodScale != lastWalkLodScale ||
                            VariantPending > 0;
            if (!needWalk)
            {
                ServiceLoads(camera, loadBudget, prepareBudget);
                WalksSkipped++;
                return;
            }
            worldChanged = false;
            if (pendingResident) { pendingResident = false; lastArrivalWalkTick = tick; }
            lastWalkPos = camera;
            lastWalkLodScale = LodScale;
            Walks++;
            prepClock.Restart();

            Visible.Clear();
            Fade.Clear();
            YmapsOpen = 0;
            Truncated = false;
            InteriorsEmitted.Clear();
            interiorOrigins.Clear();
            InteriorsDuplicated = 0;

            float alt = Math.Max(camera.Z - GroundZ, 0.0f);
            EffectiveRadius = StreamRadius + alt * AltitudeRadiusGain;

            float moved = (camera - lastSelectPos).Length();
            if (near == null || moved > ReselectDistance || Math.Abs(EffectiveRadius - lastRadius) > 25.0f)
            {
                near = NodesNear(camera);
                lastSelectPos = camera;
                lastRadius = EffectiveRadius;
            }
            YmapsWanted = near.Count;

            open.Clear();

            int asked = 0, prepared = 0;
            foreach (var n in near)
            {
                n.LastWantedTick = tick;

                if (!n.Prepared)
                {
                    if (n.LoadFailed) continue;

                    if (n.RawLoaded)
                    {
                        if (prepared >= prepareBudget) continue;
                        if (prepared > 0 && prepareBudget < 100 && prepClock.Elapsed.TotalMilliseconds > PrepareBudgetMs) continue;
                        prepared++;
                        try
                        {
                            try { n.Ymap.EnsureChildYmaps(cache); } catch { }
                            ConnectParent(n);
                            try { n.Ymap.Parent?.EnsureChildYmaps(cache); } catch { }
                            n.Ymap.InitYmapEntityArchetypes(cache);
                            n.MaxReach = ReachOf(n.Ymap);
                            n.Prepared = true;
                            NoteArrival();
                        }
                        catch (Exception ex) { n.LoadFailed = true; n.FailReason = ex.GetType().Name + ": " + ex.Message + " @ " + string.Join(" | ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(t => t.Trim())); }
                        if (!n.Prepared) continue;
                    }
                    else
                    {
                        if (n.Queued || asked >= loadBudget) continue;
                        asked++;
                        try
                        {
                            n.Ymap = cache.GetYmap(n.Hash);
                            if (n.Ymap == null) { n.LoadFailed = true; continue; }
                            if (n.ParentHash != 0 && n.ParentNode == null) nodes.TryGetValue(n.ParentHash, out n.ParentNode);
                            n.Queued = true;
                            queue.Add(n);
                        }
                        catch { n.LoadFailed = true; }
                        continue;
                    }
                }

                YmapsOpen++;
                open.Add(n);
            }
            YmapsWalked = open.Count;
            LastPrepareMs = prepClock.Elapsed.TotalMilliseconds;
            PrepareMsTotal += LastPrepareMs;

            LodTreeSync();
            walkClock.Restart();
            OrderRoots(camera);
            requiredParents.Clear();
            WalkStarting?.Invoke();
            for (int ri = 0; ri < lodRootsOrdered.Count; ri++)
            {
                var kv = lodRootsOrdered[ri];
                if (lodNodeOf.TryGetValue(kv.Key, out var node) && node.DistanceTo(camera, HeightLodWeight) > node.MaxReach * LodScale) continue;
                foreach (var ent in kv.Value)
                {
                    ent.Distance = LodDistanceTo(ent.Position, camera);
                    if (ent.Distance <= ent.LodDist * LodScale) RecurseAddVisibleLeaves(ent, camera);
                    if (Visible.Count >= MaxEntities) { Truncated = true; break; }
                }
                if (Truncated) break;
            }
            LastWalkMs = walkClock.Elapsed.TotalMilliseconds;
            WalkMsTotal += LastWalkMs;

            LastEvictMs = 0;
            if (tick - lastEvictTick >= EvictInterval)
            {
                var ec = System.Diagnostics.Stopwatch.StartNew();
                Evict(camera); lastEvictTick = tick;
                LastEvictMs = ec.Elapsed.TotalMilliseconds; EvictMsTotal += LastEvictMs;
            }
        }
        public double LastEvictMs { get; private set; }
        public double EvictMsTotal { get; private set; }

        public int ArrivalWalkInterval = 6;
        private bool pendingResident;
        private int lastArrivalWalkTick;
        private void NoteArrival() { residentVersion++; pendingResident = true; }

        private void ConnectParent(MapNode n)
        {
            if (n.Ymap == null) return;
            var pn = n.ParentNode;
            var pobj = (pn != null && pn.Ymap != null && pn.Ymap.Loaded) ? pn.Ymap : n.PendingParent;
            if (n.Ymap.Parent == null && pobj != null) n.Ymap.ConnectToParent(pobj);
            n.PendingParent = null;
        }

        public int YmapsFailed { get { int c = 0; foreach (var n in nodes.Values) if (n.LoadFailed) c++; return c; } }

        private static float ReachOf(YmapFile y)
        {
            var all = y?.AllEntities;
            if (all == null || all.Length == 0) return 0.0f;
            float max = 0.0f;
            foreach (var e in all)
            {
                if (e == null) continue;
                if (e.LodDist <= 0.0f) return float.MaxValue;
                float reach = e.LodDist + e.BSRadius;
                if (reach > max) max = reach;
            }
            return max;
        }

        public double PrepareBudgetMs = 4.0;

        private void ServiceLoads(Vector3 camera, int loadBudget, int prepareBudget)
        {
            if (near == null) return;
            int asked = 0, prepared = 0;
            prepClock.Restart();
            foreach (var n in near)
            {
                if (n.Prepared || n.LoadFailed) continue;
                if (n.RawLoaded)
                {
                    if (prepared >= prepareBudget) continue;
                    if (prepared > 0 && prepareBudget < 100 && prepClock.Elapsed.TotalMilliseconds > PrepareBudgetMs) continue;
                    prepared++;
                    try
                    {
                        try { n.Ymap.EnsureChildYmaps(cache); } catch { }
                        ConnectParent(n);
                        try { n.Ymap.Parent?.EnsureChildYmaps(cache); } catch { }
                        n.Ymap.InitYmapEntityArchetypes(cache);
                        n.MaxReach = ReachOf(n.Ymap);
                        n.Prepared = true;
                        NoteArrival();
                    }
                    catch (Exception ex) { n.LoadFailed = true; n.FailReason = ex.GetType().Name + ": " + ex.Message + " @ " + string.Join(" | ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(t => t.Trim())); }
                }
                else if (!n.Queued && asked < loadBudget)
                {
                    asked++;
                    try
                    {
                        n.Ymap = cache.GetYmap(n.Hash);
                        if (n.Ymap == null) { n.LoadFailed = true; continue; }
                        if (n.ParentHash != 0 && n.ParentNode == null) nodes.TryGetValue(n.ParentHash, out n.ParentNode);
                        n.Queued = true;
                        queue.Add(n);
                    }
                    catch { n.LoadFailed = true; }
                }
            }
        }

        private void Evict(Vector3 camera)
        {
            YmapsResident = 0;
            foreach (var n in nodes.Values)
            {
                if (n.Ymap == null) continue;
                if (n.Queued) { YmapsResident++; continue; }

                if (IsPinned != null && IsPinned(n.Ymap)) { YmapsResident++; continue; }
                float far = (EffectiveRadius > 0.0f ? EffectiveRadius : StreamRadius) * n.RangeScale * EvictFactor;
                if (n.DistanceTo(camera) <= far) { YmapsResident++; continue; }

                n.Ymap = null;
                n.PendingParent = null;
                n.RawLoaded = false;
                n.Prepared = false;
                n.LoadFailed = false;
                NoteArrival();
            }
        }

        private readonly Dictionary<uint, YmapFile> lodYmaps = new Dictionary<uint, YmapFile>();
        private readonly Dictionary<uint, YmapFile> lodCandidates = new Dictionary<uint, YmapFile>();
        private readonly List<uint> lodRemove = new List<uint>();
        private readonly Dictionary<YmapFile, List<YmapEntityDef>> lodRoots = new Dictionary<YmapFile, List<YmapEntityDef>>();
        private readonly Dictionary<YmapFile, MapNode> lodNodeOf = new Dictionary<YmapFile, MapNode>();
        private readonly HashSet<YmapEntityDef> requiredParents = new HashSet<YmapEntityDef>();
        private int residentVersion = 1, syncedResidentVersion;
        private readonly System.Diagnostics.Stopwatch syncPace = new System.Diagnostics.Stopwatch();
        public static readonly int SyncPaceMs =
            int.TryParse(Environment.GetEnvironmentVariable("RLE_SYNCPACE"), out var sp) && sp >= 0 ? sp : 120;
        private Dictionary<uint, YmapFile> syncedOverrides;
        private bool syncedHideGameMap;

        public Func<Archetype, bool> IsRenderableReady;
        public Action WalkStarting;
        public bool WaitForChildrenToLoad = true;
        public bool TimedEntitiesAlways;
        public bool RenderProxies;
        public int LodTreeYmaps => lodYmaps.Count;

        private void LodTreeSync()
        {
            bool residentChanged = syncedResidentVersion != residentVersion;
            bool structural = !ReferenceEquals(syncedOverrides, ProjectOverrides) || syncedHideGameMap != HideGameMap;
            bool projectEdit = false;
            if (!residentChanged && !structural && ProjectOverrides != null)
                foreach (var kv in ProjectOverrides) if (kv.Value != null && kv.Value.LodManagerUpdate) { projectEdit = true; break; }
            bool pendingDrain = VariantPending > 0 && !showScriptedVariants && !HideGameMap;
            if (!residentChanged && !structural && !projectEdit && !pendingDrain) return;
            if (!structural && !projectEdit && syncPace.IsRunning)
            {
                long pace = pendingDrain && !residentChanged ? 40 : SyncPaceMs;
                if (syncPace.ElapsedMilliseconds < pace) return;
            }
            syncPace.Restart();
            syncedResidentVersion = residentVersion;
            syncedOverrides = ProjectOverrides;
            syncedHideGameMap = HideGameMap;

            syncClock.Restart();
            Syncs++;
            lodCandidates.Clear();
            HourFilteredYmaps = 0;
            if (!HideGameMap)
            {
                int h = (int)hour;
                if (!ymapHourFilter || TimedEntitiesAlways) h = -1;
                uint w = ymapWeatherFilter ? weatherHash : 0u;
                foreach (var n in nodes.Values)
                {
                    if (n.Ymap == null || !n.Prepared) continue;
                    if (ProjectOverrides != null && ProjectOverrides.ContainsKey(n.Hash)) continue;
                    if (IsHiddenByName(n.Name)) continue;
                    if (!showScriptedYmaps && n.Ymap.IsScripted) continue;
                    if ((h >= 0 || w != 0) && !IsYmapAvailable(n.Hash, h, w)) { HourFilteredYmaps++; continue; }
                    lodCandidates[n.Hash] = n.Ymap;
                    if (!lodNodeOf.TryGetValue(n.Ymap, out var had) || had != n) lodNodeOf[n.Ymap] = n;
                }
                if (!showScriptedVariants) HideScriptStates();
                else if (stateHiddenYmaps.Count > 0) { stateHiddenYmaps.Clear(); stateWinners.Clear(); StateReport = ""; }
                if (lodParentChain.Count > 0) lodParentChain.Clear();
                foreach (var kv in lodCandidates)
                {
                    var p = kv.Value.Parent;
                    while (p != null && p.Loaded && p.RpfFileEntry != null)
                    {
                        uint ph = p.RpfFileEntry.ShortNameHash;
                        if (lodCandidates.ContainsKey(ph) || lodParentChain.ContainsKey(ph)) break;
                        if (IsHiddenByName(p.Name)) break;
                        lodParentChain[ph] = p;
                        p = p.Parent;
                    }
                }
                foreach (var kv in lodParentChain)
                {
                    lodCandidates[kv.Key] = kv.Value;
                    AdoptParentNode(kv.Key, kv.Value);
                }
                variantClock.Restart();
                if (!showScriptedVariants) HideScriptedVariants();
                else if (variantHiddenEnts.Count > 0) { variantHiddenEnts.Clear(); HiddenScriptedVariants = 0; }
                LastVariantMs = variantClock.Elapsed.TotalMilliseconds;
                VariantMsTotal += LastVariantMs;
            }
            else if (variantHiddenEnts.Count > 0) { variantHiddenEnts.Clear(); HiddenScriptedVariants = 0; }
            if (ProjectOverrides != null)
                foreach (var kv in ProjectOverrides)
                    if (kv.Value != null) lodCandidates[kv.Key] = kv.Value;
            if (!showScriptedVariants) HideInteriorVariants();
            else if (interiorShellsHidden.Count > 0) interiorShellsHidden.Clear();

            foreach (var kv in lodCandidates)
            {
                var ymap = kv.Value;
                if (ymap._CMapData.parent != 0)
                {
                    lodCandidates.TryGetValue(ymap._CMapData.parent, out var pymap);
                    if (pymap == null) continue;
                    if (ymap.Parent != pymap) ymap.ConnectToParent(pymap);
                }
            }

            lodRemove.Clear();
            foreach (var kv in lodYmaps)
            {
                if (!lodCandidates.TryGetValue(kv.Key, out var ymap) || ymap != kv.Value || ymap.LodManagerUpdate)
                    lodRemove.Add(kv.Key);
            }
            if (lodRemove.Count > 0)
            {
                var removing = new HashSet<uint>(lodRemove);
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var kv in lodYmaps)
                    {
                        if (removing.Contains(kv.Key)) continue;
                        uint ph = kv.Value._CMapData.parent;
                        if (ph != 0 && removing.Contains(ph)) { removing.Add(kv.Key); lodRemove.Add(kv.Key); grew = true; }
                    }
                }
            }
            foreach (var h in lodRemove)
            {
                var ymap = lodYmaps[h];
                lodYmaps.Remove(h);
                lodRoots.Remove(ymap);
                var remEnts = ymap.LodManagerOldEntities ?? ymap.AllEntities;
                if (remEnts != null)
                {
                    foreach (var ent in remEnts)
                    {
                        if (ent == null) continue;
                        ent.LodManagerChildren?.Clear();
                        ent.LodManagerChildren = null;
                        ent.LodManagerRenderable = null;
                        if (ent.Parent != null && ent.Parent.Ymap != ymap)
                            ent.Parent.LodManagerRemoveChild(ent);
                    }
                }
                ymap.LodManagerUpdate = false;
                ymap.LodManagerOldEntities = null;
                worldChanged = true;
            }
            foreach (var kv in lodCandidates)
            {
                var ymap = kv.Value;
                if (ymap._CMapData.parent != 0 && (ymap.Parent == null || !lodCandidates.ContainsKey(ymap._CMapData.parent))) continue;
                if (lodYmaps.ContainsKey(kv.Key)) continue;
                lodYmaps.Add(kv.Key, ymap);
                var ents = ymap.AllEntities;
                if (ents != null)
                {
                    List<YmapEntityDef> roots = null;
                    foreach (var ent in ents)
                    {
                        if (ent == null) continue;
                        if (ent.Parent != null) ent.Parent.LodManagerAddChild(ent);
                        else { roots ??= new List<YmapEntityDef>(); roots.Add(ent); }
                    }
                    if (roots != null) lodRoots[ymap] = roots;
                }
                worldChanged = true;
                lodRootsDirty = true;
            }
            if (lodRemove.Count > 0) lodRootsDirty = true;
            LastSyncMs = syncClock.Elapsed.TotalMilliseconds;
            SyncMsTotal += LastSyncMs;
        }

        private readonly Dictionary<uint, YmapFile> lodParentChain = new Dictionary<uint, YmapFile>();
        private void AdoptParentNode(uint hash, YmapFile ymap)
        {
            if (ymap == null) return;
            if (IsDlcInvalidated(hash)) return;
            if (!nodes.TryGetValue(hash, out var n))
            {
                MapDataStoreNode dsn;
                try { dsn = new MapDataStoreNode(ymap); } catch { return; }
                if (dsn.Name == 0) return;
                var min = dsn.streamingExtentsMin; var max = dsn.streamingExtentsMax;
                if (!(max.X > min.X) || !(max.Y > min.Y)) return;
                n = new MapNode
                {
                    Hash = hash,
                    Name = ymap.Name ?? dsn.Name.ToString(),
                    Min = new Vector3(min.X, min.Y, min.Z),
                    Max = new Vector3(max.X, max.Y, max.Z),
                    ParentHash = dsn.ParentName.Hash,
                    ContentFlags = dsn.ContentFlags,
                    RangeScale = RangeScaleFor(dsn.ContentFlags),
                };
                nodes[hash] = n;
                AdoptedParentNodes++;
                near = null;
            }
            if (n.Prepared && n.Ymap == ymap) return;
            if (n.Queued) return;
            n.Ymap = ymap;
            n.RawLoaded = true;
            n.LoadFailed = false;
            try
            {
                try { ymap.EnsureChildYmaps(cache); } catch { }
                if (n.ParentHash != 0 && n.ParentNode == null) nodes.TryGetValue(n.ParentHash, out n.ParentNode);
                ConnectParent(n);
                ymap.InitYmapEntityArchetypes(cache);
                n.MaxReach = ReachOf(ymap);
                n.Prepared = true;
                lodNodeOf[ymap] = n;
            }
            catch (Exception ex) { n.LoadFailed = true; n.FailReason = "adopt " + ex.GetType().Name + ": " + ex.Message; }
        }
        public int AdoptedParentNodes { get; private set; }

        private readonly System.Diagnostics.Stopwatch syncClock = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch walkClock = new System.Diagnostics.Stopwatch();
        public double LastSyncMs { get; private set; }
        public double LastWalkMs { get; private set; }
        public double SyncMsTotal { get; private set; }
        public double WalkMsTotal { get; private set; }
        public double LastPrepareMs { get; private set; }
        public double PrepareMsTotal { get; private set; }
        public double LastVariantMs { get; private set; }
        public double VariantMsTotal { get; private set; }
        private readonly System.Diagnostics.Stopwatch prepClock = new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch variantClock = new System.Diagnostics.Stopwatch();
        public int Syncs { get; private set; }
        public int Walks { get; private set; }
        public int ResidentVersion => residentVersion;

        private readonly List<KeyValuePair<YmapFile, List<YmapEntityDef>>> lodRootsOrdered = new List<KeyValuePair<YmapFile, List<YmapEntityDef>>>();
        private bool lodRootsDirty = true;
        private Vector3 lodRootsOrderedAt = new Vector3(float.MaxValue);
        private void OrderRoots(Vector3 camera)
        {
            if (!lodRootsDirty && (camera - lodRootsOrderedAt).Length() <= ReselectDistance) return;
            lodRootsOrdered.Clear();
            foreach (var kv in lodRoots) lodRootsOrdered.Add(kv);
            lodRootsOrdered.Sort((a, b) =>
            {
                float da = lodNodeOf.TryGetValue(a.Key, out var na) ? na.DistanceTo(camera) : 0.0f;
                float db = lodNodeOf.TryGetValue(b.Key, out var nb) ? nb.DistanceTo(camera) : 0.0f;
                return da.CompareTo(db);
            });
            lodRootsDirty = false;
            lodRootsOrderedAt = camera;
        }

        private void RecurseAddVisibleLeaves(YmapEntityDef ent, Vector3 camera)
        {
            var clist = GetEntityChildren(ent, camera);
            if (clist != null)
            {
                for (var cnode = clist.First; cnode != null; cnode = cnode.Next)
                    RecurseAddVisibleLeaves(cnode.Value, camera);
            }
            else
            {
                AddLeaf(ent, camera);
            }
        }

        public float HeightLodWeight = 0.30f;

        public float LodDistanceTo(Vector3 pos, Vector3 camera)
        {
            float dx = pos.X - camera.X, dy = pos.Y - camera.Y, dz = pos.Z - camera.Z;
            if (dz < 0.0f) dz *= HeightLodWeight;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private LinkedList<YmapEntityDef> GetEntityChildren(YmapEntityDef ent, Vector3 camera)
        {
            var clist = ent.LodManagerChildren;
            if ((clist != null) && (clist.Count >= ent._CEntityDef.numChildren))
            {
                if (ent.Parent != null)
                    ent.Distance = LodDistanceTo(ent.Position, camera);
                if (ent.Distance <= (ent.ChildLodDist * LodScale))
                    return clist;
                for (var cnode = clist.First; cnode != null; cnode = cnode.Next)
                {
                    var child = cnode.Value;
                    child.Distance = LodDistanceTo(child.Position, camera);
                    if (child.Distance <= (child.LodDist * LodScale))
                        return clist;
                }
            }
            return null;
        }

        private void AddLeaf(YmapEntityDef ent, Vector3 camera)
        {
            if (Visible.Count >= MaxEntities) { Truncated = true; return; }
            if (!IsFinalRender(ent)) return;
            if (variantHiddenEnts.Count > 0 && variantHiddenEnts.Contains(ent)) return;

            if (ent.MloInstance != null)
            {
                if (interiorShellsHidden.Count > 0 && interiorShellsHidden.Contains(ent)) return;
                NoteInteriorEmitted(ent);
                EmitInterior(ent.MloInstance, camera);
                return;
            }

            ent.IsVisible = true;
            Visible.Add(ent);
            Fade.Add(1.0f);

            var pent = ent.Parent;
            if (WaitForChildrenToLoad && pent != null && IsRenderableReady != null && !requiredParents.Contains(pent))
            {
                requiredParents.Add(pent);
                bool allok = true;
                var pc = pent.LodManagerChildren;
                if (pc != null)
                {
                    for (var n = pc.First; n != null; n = n.Next)
                    {
                        var c = n.Value;
                        if (c.Archetype == null || c.MloInstance != null || !IsFinalRender(c)) continue;
                        if (variantHiddenEnts.Count > 0 && variantHiddenEnts.Contains(c)) continue;
                        if (!IsRenderableReady(c.Archetype)) allok = false;
                    }
                }
                if (!allok && pent.Archetype != null && pent.MloInstance == null && IsFinalRender(pent) &&
                    Visible.Count < MaxEntities)
                {
                    pent.IsVisible = true;
                    Visible.Add(pent);
                    Fade.Add(1.0f);
                }
            }
        }

        private bool IsFinalRender(YmapEntityDef ent)
        {
            var arch = ent.Archetype;
            if (arch == null) return false;
            uint archflags = arch._BaseArchetypeDef.flags;
            if (arch.Type == MetaName.CTimeArchetypeDef)
            {
                if (!(TimedEntitiesAlways || arch.IsActive(Hour))) return false;
            }
            bool isshadowproxy = (archflags & 2048) > 0;
            if (!isshadowproxy && !RenderProxies && IsProxyByName(arch)) isshadowproxy = true;
            bool isreflproxy = false;
            if ((ent._CEntityDef.flags & FlagsOnlyOtherPasses) != 0) isreflproxy = true;
            switch (ent._CEntityDef.flags)
            {
                case 135790592:
                case 135790593:
                case 672661504:
                case 536870912:
                case 35127296:
                case 39321602:
                    isreflproxy = true; break;
            }
            ReflectionProxy_T5(ent, ref isreflproxy);
            if (isshadowproxy || isreflproxy) return RenderProxies;
            return true;
        }

        partial void ReflectionProxy_T5(YmapEntityDef ent, ref bool isreflproxy);

        private readonly Dictionary<uint, bool> proxyByName = new Dictionary<uint, bool>();
        private bool IsProxyByName(Archetype arch)
        {
            if (!proxyByName.TryGetValue(arch.Hash, out bool p))
            {
                var nm = arch.Name ?? "";
                p = nm.IndexOf("exshadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nm.EndsWith("_shadowproxy", StringComparison.OrdinalIgnoreCase) ||
                    nm.IndexOf("shadowlightproxy", StringComparison.OrdinalIgnoreCase) >= 0;
                proxyByName[arch.Hash] = p;
            }
            return p;
        }

        private const uint FlagOnlyInReflections = 0x2000000;
        public const uint FlagsOnlyOtherPasses = 0x800000u | 0x2000000u | 0x8000000u | 0x20000000u;

        private void EmitInterior(MloInstanceData inst, Vector3 camera)
        {
            var ents = inst.Entities;
            if (ents != null)
            {
                foreach (var ie in ents) EmitInteriorEntity(ie, camera);
            }
            var sets = inst.EntitySets;
            if (sets != null)
            {
                foreach (var set in sets)
                {
                    if (set == null || set.Entities == null) continue;
                    if (!set.Visible && !(set.EntitySet?.ForceVisible ?? false) && !IsInteriorSetAutoVisible(inst, set)) continue;
                    foreach (var ie in set.Entities) EmitInteriorEntity(ie, camera);
                }
            }
        }

        private void EmitInteriorEntity(YmapEntityDef ie, Vector3 camera)
        {
            if (ie?.Archetype == null || Visible.Count >= MaxEntities) return;
            if ((ie._CEntityDef.flags & FlagOnlyInReflections) != 0) return;
            if (!IsFinalRender(ie) && !MirrorOnlyKeep_N1(ie)) return;

            ie.IsVisible = true;
            Visible.Add(ie);
            Fade.Add(1.0f);
        }

        public (int entities, int interiors) ReresolveArchetypes()
        {
            if (cache == null) return (0, 0);
            int ents = 0, ints = 0;
            foreach (var n in nodes.Values)
            {
                if (n.Ymap == null || !n.Prepared) continue;
                var r = ProjectController.ReresolveArchetypes(n.Ymap, cache);
                if (r.entities == 0 && r.interiors == 0) continue;
                ents += r.entities; ints += r.interiors;
                n.MaxReach = ReachOf(n.Ymap);
            }
            if (ents > 0 || ints > 0) { residentVersion++; worldChanged = true; }
            return (ents, ints);
        }

        public void UnloadAll()
        {
            foreach (var n in nodes.Values)
            {
                n.Ymap = null;
                n.PendingParent = null;
                n.RawLoaded = false;
                n.Prepared = false;
                n.LoadFailed = false;
            }
            residentVersion++;
            Visible.Clear();
            Fade.Clear();
            YmapsOpen = 0;
            YmapsResident = 0;
            near = null;
            lastSelectPos = new Vector3(float.MaxValue);
            worldChanged = true;
        }

        public void Drain(Vector3 camera, int timeoutMs = 60000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int settled = 0;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                worldChanged = true;
                Select(camera, 512, 512);
                bool busy = queue.Count > 0 || AnyInFlight();
                settled = busy ? 0 : settled + 1;
                if (settled >= 2) break;
                Thread.Sleep(2);
            }
            for (int guard = 0; VariantPending > 0 && guard < 200; guard++) { worldChanged = true; Select(camera, 512, 512); }
        }

        private bool AnyInFlight()
        {
            foreach (var n in nodes.Values) if (n.Queued) return true;
            return false;
        }

        public void Dispose()
        {
            stopping = true;
            try { queue.CompleteAdding(); } catch { }
            try { loader?.Join(500); } catch { }
            loader = null;
        }

        public static readonly Vector3 DowntownLosSantos = new Vector3(-270.0f, -960.0f, 120.0f);
    }
}

