using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {

        public sealed class NavDoc
        {
            public YnvFile Ynv;
            public string FilePath;
            public string Name = "navmesh.ynv";
            public string Source = "";
            public bool Visible = true;
            public BoundingBox Bounds;
            public BoundingBox[] PolyBounds = Array.Empty<BoundingBox>();
            public int Version = 1;
            public int PolyCountOnLoad_V15 = -1;
            public readonly HashSet<YnvPoly> Disabled_V15 = new HashSet<YnvPoly>();
            public bool Dirty
            {
                get => Ynv != null && Ynv.HasChanged;
                set { if (Ynv != null) Ynv.HasChanged = value; }
            }
            public int PolyCount => Ynv?.Polys?.Count ?? 0;
            public int PointCount => Ynv?.Points?.Count ?? 0;
            public int PortalCount => Ynv?.Portals?.Count ?? 0;
            public override string ToString() => Name + (Dirty ? " *" : "");
        }

        public readonly List<NavDoc> Docs = new List<NavDoc>();
        public NavDoc Active;

        public readonly List<YnvPoly> SelectedPolys = new List<YnvPoly>();
        public YnvPoly SelectedPoly => SelectedPolys.Count > 0 ? SelectedPolys[SelectedPolys.Count - 1] : null;
        public int SelectedVertex = -1;
        public YnvPoint SelectedPoint;
        public YnvPortal SelectedPortal;

        public void ClearSelection()
        {
            SelectedPolys.Clear();
            SelectedVertex = -1;
            SelectedPoint = null;
            SelectedPortal = null;
        }

        public void SelectPoly(YnvPoly p, bool add)
        {
            if (p == null) { ClearSelection(); return; }
            SelectedPoint = null; SelectedPortal = null;
            if (add)
            {
                if (!SelectedPolys.Remove(p)) SelectedPolys.Add(p);
            }
            else
            {
                SelectedPolys.Clear();
                SelectedPolys.Add(p);
            }
            SelectedVertex = -1;
        }

        public NavDoc DocOf(YnvFile ynv)
        {
            if (ynv == null) return null;
            foreach (var d in Docs) if (ReferenceEquals(d.Ynv, ynv)) return d;
            return null;
        }

        public enum NavCat { Walk = 0, Pavement, Road, Water, Shallow, Steep, Underground, Interior, Train, Isolated }

        public static readonly string[] CatNames =
        {
            "Walkable", "Pavement / footpath", "Road", "Water", "Shallow water",
            "Steep slope", "Underground", "Interior", "Train track", "Isolated (no links)"
        };

        public static readonly Vector4[] CatColours =
        {
            new Vector4(0.40f, 0.56f, 0.34f, 1f),
            new Vector4(0.20f, 0.84f, 0.32f, 1f),
            new Vector4(0.36f, 0.44f, 0.60f, 1f),
            new Vector4(0.12f, 0.35f, 0.95f, 1f),
            new Vector4(0.13f, 0.74f, 0.92f, 1f),
            new Vector4(0.93f, 0.18f, 0.14f, 1f),
            new Vector4(0.58f, 0.24f, 0.92f, 1f),
            new Vector4(0.98f, 0.53f, 0.08f, 1f),
            new Vector4(0.92f, 0.85f, 0.10f, 1f),
            new Vector4(1.00f, 0.10f, 0.70f, 1f),
        };

        public readonly bool[] CatVisible = Enumerable.Repeat(true, CatNames.Length).ToArray();

        public bool ShowFills = true;
        public bool ShowEdges = true;
        public bool ShowLinks;
        public bool ShowPortals = true;
        public bool ShowPoints = true;
        public bool ShowLegend = true;
        public bool HighlightIsolated = true;
        public float FillAlpha = 0.55f;
        public int LayerVersion = 1;

        public int LayerStamp()
        {
            int h = 17 + LayerVersion * 7919;
            for (int i = 0; i < CatVisible.Length; i++) h = h * 31 + (CatVisible[i] ? 1 : 0);
            h = h * 31 + (ShowFills ? 1 : 0);
            h = h * 31 + (ShowEdges ? 2 : 0);
            h = h * 31 + (ShowLinks ? 4 : 0);
            h = h * 31 + (ShowPortals ? 8 : 0);
            h = h * 31 + (ShowPoints ? 16 : 0);
            h = h * 31 + (HighlightIsolated ? 32 : 0);
            h = h * 31 + (int)(FillAlpha * 255.0f);
            return h;
        }

        public static NavCat Categorise(YnvPoly p)
        {
            if (p == null) return NavCat.Walk;
            if (p.B07_IsWater) return NavCat.Water;
            if (p.B21_IsShallowWater) return NavCat.Shallow;
            if (p.B20_IsTrainTrack) return NavCat.Train;
            if (p.B03_IsUnderground) return NavCat.Underground;
            if (p.B14_IsInterior) return NavCat.Interior;
            if (p.B06_SteepSlope) return NavCat.Steep;
            if (p.B02_IsFootpath) return NavCat.Pavement;
            if (p.B18_IsRoad) return NavCat.Road;
            return NavCat.Walk;
        }

        public static bool IsIsolated(YnvPoly p)
        {
            if (p?.Edges == null || p.Edges.Length == 0) return true;
            foreach (var e in p.Edges)
            {
                if (e == null) continue;
                if (e.PolyID1 != NoPoly || e.PolyID2 != NoPoly) return false;
            }
            return true;
        }

        public const uint NoPoly = 0x3FFF;

        public NavCat CategoryOf(YnvPoly p) => Categorise(p);

        public bool ShowsAsIsolated(YnvPoly p) => HighlightIsolated && IsIsolated(p);

        public bool PolyVisible(YnvPoly p) =>
            ShowsAsIsolated(p)
                ? CatVisible[(int)NavCat.Isolated] && CatVisible[(int)CategoryOf(p)]
                : CatVisible[(int)CategoryOf(p)];

        public sealed class PolyFlag
        {
            public readonly string Name;
            public readonly string Tip;
            public readonly Func<YnvPoly, bool> Get;
            public readonly Action<YnvPoly, bool> Set;
            public PolyFlag(string name, string tip, Func<YnvPoly, bool> get, Action<YnvPoly, bool> set)
            { Name = name; Tip = tip; Get = get; Set = set; }
        }

        public static readonly PolyFlag[] Flags =
        {
            new PolyFlag("00 Avoid (unknown)",   "Peds avoid this poly. Exact meaning unconfirmed.",       p => p.B00_AvoidUnk, (p, v) => p.B00_AvoidUnk = v),
            new PolyFlag("01 Avoid (unknown)",   "Second avoid bit. Exact meaning unconfirmed.",           p => p.B01_AvoidUnk, (p, v) => p.B01_AvoidUnk = v),
            new PolyFlag("02 Footpath",          "Pavement. Peds walking down a street use these.",        p => p.B02_IsFootpath, (p, v) => p.B02_IsFootpath = v),
            new PolyFlag("03 Underground",       "Under something - tunnels, subways, car parks.",         p => p.B03_IsUnderground, (p, v) => p.B03_IsUnderground = v),
            new PolyFlag("04 Unused",            "Not used by the game.",                                  p => p.B04_Unused, (p, v) => p.B04_Unused = v),
            new PolyFlag("05 Unused",            "Not used by the game.",                                  p => p.B05_Unused, (p, v) => p.B05_Unused = v),
            new PolyFlag("06 Steep slope",       "Too steep to walk. Peds refuse it.",                     p => p.B06_SteepSlope, (p, v) => p.B06_SteepSlope = v),
            new PolyFlag("07 Water",             "Deep water - swimming, not walking.",                    p => p.B07_IsWater, (p, v) => p.B07_IsWater = v),
            new PolyFlag("08 Underground unk 0", "Underground detail bit 0.",                              p => p.B08_UndergroundUnk0, (p, v) => p.B08_UndergroundUnk0 = v),
            new PolyFlag("09 Underground unk 1", "Underground detail bit 1.",                              p => p.B09_UndergroundUnk1, (p, v) => p.B09_UndergroundUnk1 = v),
            new PolyFlag("10 Underground unk 2", "Underground detail bit 2.",                              p => p.B10_UndergroundUnk2, (p, v) => p.B10_UndergroundUnk2 = v),
            new PolyFlag("11 Underground unk 3", "Underground detail bit 3.",                              p => p.B11_UndergroundUnk3, (p, v) => p.B11_UndergroundUnk3 = v),
            new PolyFlag("12 Unused",            "Not used by the game.",                                  p => p.B12_Unused, (p, v) => p.B12_Unused = v),
            new PolyFlag("13 Has path node",     "A vehicle path node sits over this poly.",               p => p.B13_HasPathNode, (p, v) => p.B13_HasPathNode = v),
            new PolyFlag("14 Interior",          "Inside an MLO. Peds treat it as interior space.",        p => p.B14_IsInterior, (p, v) => p.B14_IsInterior = v),
            new PolyFlag("15 Interaction unk",   "Something interactive here - vents, ladders, workers.",  p => p.B15_InteractionUnk, (p, v) => p.B15_InteractionUnk = v),
            new PolyFlag("16 Unused",            "Not used by the game.",                                  p => p.B16_Unused, (p, v) => p.B16_Unused = v),
            new PolyFlag("17 Flat ground",       "Flat enough to stand and wander on.",                    p => p.B17_IsFlatGround, (p, v) => p.B17_IsFlatGround = v),
            new PolyFlag("18 Road",              "Carriageway. Peds cross it rather than walk it.",        p => p.B18_IsRoad, (p, v) => p.B18_IsRoad = v),
            new PolyFlag("19 Cell edge",         "Poly lies on the edge of its 150 m navmesh cell.",       p => p.B19_IsCellEdge, (p, v) => p.B19_IsCellEdge = v),
            new PolyFlag("20 Train track",       "Rail. Peds keep off.",                                   p => p.B20_IsTrainTrack, (p, v) => p.B20_IsTrainTrack = v),
            new PolyFlag("21 Shallow water",     "Wadeable / moving water.",                               p => p.B21_IsShallowWater, (p, v) => p.B21_IsShallowWater = v),
            new PolyFlag("22 Footpath unk 1",    "Footpath variant - beaches and the like.",               p => p.B22_FootpathUnk1, (p, v) => p.B22_FootpathUnk1 = v),
            new PolyFlag("23 Footpath unk 2",    "Footpath variant - special areas.",                      p => p.B23_FootpathUnk2, (p, v) => p.B23_FootpathUnk2 = v),
            new PolyFlag("24 Footpath mall",     "Mall / plaza footpath (Vinewood Blvd, the mall).",       p => p.B24_FootpathMall, (p, v) => p.B24_FootpathMall = v),
            new PolyFlag("25 Slope south",       "Slope faces -Y.",                                        p => p.B25_SlopeSouth, (p, v) => p.B25_SlopeSouth = v),
            new PolyFlag("26 Slope south-east",  "Slope faces +X,-Y.",                                     p => p.B26_SlopeSouthEast, (p, v) => p.B26_SlopeSouthEast = v),
            new PolyFlag("27 Slope east",        "Slope faces +X.",                                        p => p.B27_SlopeEast, (p, v) => p.B27_SlopeEast = v),
            new PolyFlag("28 Slope north-east",  "Slope faces +X,+Y.",                                     p => p.B28_SlopeNorthEast, (p, v) => p.B28_SlopeNorthEast = v),
            new PolyFlag("29 Slope north",       "Slope faces +Y.",                                        p => p.B29_SlopeNorth, (p, v) => p.B29_SlopeNorth = v),
            new PolyFlag("30 Slope north-west",  "Slope faces -X,+Y.",                                     p => p.B30_SlopeNorthWest, (p, v) => p.B30_SlopeNorthWest = v),
            new PolyFlag("31 Slope west",        "Slope faces -X.",                                        p => p.B31_SlopeWest, (p, v) => p.B31_SlopeWest = v),
            new PolyFlag("32 Slope south-west",  "Slope faces -X,-Y.",                                     p => p.B32_SlopeSouthWest, (p, v) => p.B32_SlopeSouthWest = v),
        };

        public static int FlagIndex(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            name = name.Trim();
            if (int.TryParse(name, out int n) && n >= 0 && n < Flags.Length) return n;
            var key = name.Replace(" ", "").Replace("_", "").Replace("-", "");
            for (int i = 0; i < Flags.Length; i++)
            {
                var lab = Flags[i].Name.Replace(" ", "").Replace("_", "").Replace("-", "");
                if (lab.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }

        public string Status = "";
        public bool RequestOpenFile, RequestSave, RequestSaveAs, RequestAddToProject;
        public bool RequestLoadAroundCamera, RequestCloseActive, RequestGenerate, RequestNewFile;
        public string RequestOpenByName;
        public bool RequestFrameSelection;
        public readonly List<Vector3> PendingPoly = new List<Vector3>();
        public bool PlacingPoly;
        public bool RequestClosePoly, RequestDeleteSelection;
        public bool HardDeletePolys_V15;
        public bool RequestAddPoint, RequestAddPortal;
        public string Filter = "";
        public float LoadRadius = 750.0f;
        public float GenSlopeLimit = 40.0f;
        public float GenDensity = 1.0f;
        public bool GenUseArea = true;
        public bool GenInterior;

        public NavDoc Add(YnvFile ynv, string path, string source)
        {
            if (ynv == null) return null;
            var doc = new NavDoc
            {
                Ynv = ynv,
                FilePath = path,
                Name = !string.IsNullOrEmpty(ynv.Name) ? ynv.Name
                     : !string.IsNullOrEmpty(path) ? Path.GetFileName(path) : "navmesh.ynv",
                Source = source ?? "",
                PolyCountOnLoad_V15 = ynv.Polys?.Count ?? 0,
            };
            ResolveLinks_V16(ynv);
            SeedDisabled_V15(doc);
            if (string.IsNullOrEmpty(ynv.Name)) ynv.Name = doc.Name;
            var same = Docs.FirstOrDefault(d =>
                (path != null && string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)) ||
                (path == null && string.Equals(d.Name, doc.Name, StringComparison.OrdinalIgnoreCase) && d.FilePath == null));
            if (same != null) Docs.Remove(same);
            Docs.Add(doc);
            Active = doc;
            Reindex(ynv);
            RecomputeBounds(doc);
            ClearSelection();
            return doc;
        }

        public void Close(NavDoc doc)
        {
            if (doc == null) return;
            Docs.Remove(doc);
            if (ReferenceEquals(Active, doc)) Active = Docs.Count > 0 ? Docs[Docs.Count - 1] : null;
            ClearSelection();
        }

        public static YnvFile NewFile(int cellX, int cellY, Vector3 cellMin, Vector3 cellMax)
        {
            var ynv = new YnvFile();
            ynv.Name = $"navmesh[{cellX}][{cellY}].ynv";
            ynv.Nav = new NavMesh();
            ynv.Nav.SetDefaults(false);
            var size = cellMax - cellMin;
            ynv.Nav.AABBSize = new Vector3(Math.Max(size.X, 1.0f), Math.Max(size.Y, 1.0f), Math.Max(size.Z, 1.0f));
            ynv.Nav.AABBMin = cellMin;
            ynv.Nav.AABBMax = cellMax;
            ynv.Nav.SectorTree = new NavMeshSector();
            ynv.Nav.SectorTree.AABBMin = new Vector4(cellMin, 0.0f);
            ynv.Nav.SectorTree.AABBMax = new Vector4(cellMax, 0.0f);
            ynv.AreaID = cellX + cellY * 100;
            ynv.Polys = new List<YnvPoly>();
            ynv.Portals = new List<YnvPortal>();
            ynv.Points = new List<YnvPoint>();
            ynv.Vertices = new List<Vector3>();
            ynv.Indices = new List<ushort>();
            ynv.Edges = new List<YnvEdge>();
            ynv.HasChanged = true;
            ynv.Loaded = true;
            return ynv;
        }

        public static void RecomputeBounds(NavDoc doc)
        {
            var polys = doc?.Ynv?.Polys;
            if (polys == null) { if (doc != null) { doc.PolyBounds = Array.Empty<BoundingBox>(); doc.Bounds = new BoundingBox(); } return; }
            var arr = new BoundingBox[polys.Count];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < polys.Count; i++)
            {
                var p = polys[i];
                var pmin = new Vector3(float.MaxValue);
                var pmax = new Vector3(float.MinValue);
                var vs = p?.Vertices;
                if (vs != null)
                    for (int v = 0; v < vs.Length; v++) { pmin = Vector3.Min(pmin, vs[v]); pmax = Vector3.Max(pmax, vs[v]); }
                if (pmin.X > pmax.X) { pmin = Vector3.Zero; pmax = Vector3.Zero; }
                arr[i] = new BoundingBox(pmin, pmax);
                min = Vector3.Min(min, pmin); max = Vector3.Max(max, pmax);
            }
            if (min.X > max.X) { min = Vector3.Zero; max = Vector3.Zero; }
            doc.PolyBounds = arr;
            doc.Bounds = new BoundingBox(min, max);
        }

        public void Touch(NavDoc doc, bool structural)
        {
            if (doc?.Ynv == null) return;
            if (structural) { Reindex(doc.Ynv); RelinkStructural_V16(doc.Ynv, doc.Disabled_V15); SyncVertexLists(doc.Ynv); }
            var polys = doc.Ynv.Polys;
            if (polys != null)
                foreach (var p in polys) { p.CalculatePosition(); p.CalculateAABB(); }
            RecomputeBounds(doc);
            doc.Version++;
            doc.Dirty = true;
        }

        public static void Reindex(YnvFile ynv)
        {
            var polys = ynv?.Polys;
            if (polys == null) return;
            for (int i = 0; i < polys.Count; i++) { polys[i].Index = i; polys[i].Ynv = ynv; }
        }

        private static (Vector3 a, Vector3 b) EdgeKey(Vector3 a, Vector3 b)
        {
            bool swap = a.X != b.X ? a.X > b.X : a.Y != b.Y ? a.Y > b.Y : a.Z > b.Z;
            return swap ? (b, a) : (a, b);
        }

        [Obsolete("WS-V16: re-deriving adjacency from shared vertex pairs CUTS real links on any " +
                  "shipped navmesh (T-junctions share no vertex pair). Use RelinkStructural_V16. " +
                  "Kept because the measurement in MainForm.NavAudit.V16.cs is what proves it.")]
        public static void RebuildAdjacency(YnvFile ynv, ISet<YnvPoly> disabled = null)
        {
            var polys = ynv?.Polys;
            if (polys == null) return;
            uint areaId = (uint)ynv.AreaID;
            var map = new Dictionary<(Vector3, Vector3), List<(YnvPoly poly, int edge)>>();
            for (int i = 0; i < polys.Count; i++)
            {
                var p = polys[i];
                var vs = p?.Vertices;
                if (vs == null || vs.Length < 3) continue;
                EnsureEdgeArray(p);
                for (int e = 0; e < vs.Length; e++)
                {
                    var key = EdgeKey(vs[e], vs[(e + 1) % vs.Length]);
                    if (!map.TryGetValue(key, out var list)) map[key] = list = new List<(YnvPoly, int)>();
                    list.Add((p, e));
                }
            }
            foreach (var kv in map)
            {
                var list = kv.Value;
                bool anyDisabled = disabled != null && disabled.Count > 0 &&
                                   list.Any(x => disabled.Contains(x.poly));
                if (list.Count == 2 && !anyDisabled)
                {
                    Link(list[0].poly, list[0].edge, list[1].poly, areaId);
                    Link(list[1].poly, list[1].edge, list[0].poly, areaId);
                }
                else
                {
                    foreach (var (poly, edge) in list)
                        if (!IsForeignLink(poly.Edges[edge], areaId)) Link(poly, edge, null, areaId);
                }
            }
        }

        private static bool IsForeignLink(YnvEdge e, uint areaId)
        {
            if (e == null) return false;
            bool f1 = e.AreaID1 != areaId && e.AreaID1 != NoPoly && e.PolyID1 != NoPoly;
            bool f2 = e.AreaID2 != areaId && e.AreaID2 != NoPoly && e.PolyID2 != NoPoly;
            return f1 || f2;
        }

        private static void EnsureEdgeArray(YnvPoly p)
        {
            int n = p.Vertices?.Length ?? 0;
            if (p.Edges != null && p.Edges.Length == n)
            {
                for (int i = 0; i < n; i++) if (p.Edges[i] == null) p.Edges[i] = NewEmptyEdge_V16();
                return;
            }
            var old = p.Edges;
            var arr = new YnvEdge[n];
            for (int i = 0; i < n; i++) arr[i] = (old != null && i < old.Length && old[i] != null) ? old[i] : NewEmptyEdge_V16();
            p.Edges = arr;
        }

        private static void Link(YnvPoly poly, int edge, YnvPoly other, uint areaId)
        {
            var e = poly.Edges[edge];
            e.Ynv = poly.Ynv;
            e.Poly1 = other; e.Poly2 = other;
            e.AreaID1 = other != null ? areaId : NoPoly;
            e.AreaID2 = other != null ? areaId : NoPoly;
            e.PolyID1 = other != null ? (uint)other.Index : NoPoly;
            e.PolyID2 = other != null ? (uint)other.Index : NoPoly;
        }

        public static void SyncVertexLists(YnvFile ynv)
        {
            var polys = ynv?.Polys;
            if (polys == null) return;
            var verts = new List<Vector3>();
            var inds = new List<ushort>();
            var edges = new List<YnvEdge>();
            var dict = new Dictionary<Vector3, ushort>();
            foreach (var p in polys)
            {
                var vs = p?.Vertices;
                if (vs == null) continue;
                EnsureEdgeArray(p);
                p._RawData.IndexID = (ushort)inds.Count;
                p._RawData.IndexCount = vs.Length;
                if (p.Indices == null || p.Indices.Length != vs.Length) p.Indices = new ushort[vs.Length];
                for (int i = 0; i < vs.Length; i++)
                {
                    if (!dict.TryGetValue(vs[i], out ushort ind))
                    {
                        ind = (ushort)Math.Min(verts.Count, ushort.MaxValue);
                        if (verts.Count <= ushort.MaxValue) { dict[vs[i]] = ind; verts.Add(vs[i]); }
                    }
                    p.Indices[i] = ind;
                    inds.Add(ind);
                    edges.Add(p.Edges[i]);
                }
            }
            ynv.Vertices = verts;
            ynv.Indices = inds;
            ynv.Edges = edges;
        }

        public int SetFlag(EditHistory hist, IList<YnvPoly> polys, int flagIndex, bool value)
        {
            if (polys == null || flagIndex < 0 || flagIndex >= Flags.Length) return 0;
            var f = Flags[flagIndex];
            var changed = polys.Where(p => p != null && f.Get(p) != value).ToArray();
            if (changed.Length == 0) return 0;
            var docs = changed.Select(p => DocOf(p.Ynv)).Where(d => d != null).Distinct().ToArray();
            void Apply(bool v)
            {
                foreach (var p in changed) f.Set(p, v);
                foreach (var d in docs) { d.Version++; d.Dirty = true; }
            }
            Apply(value);
            hist?.Push(new DelegateCommand($"{(value ? "Set" : "Clear")} {f.Name} on {changed.Length} poly" + (changed.Length == 1 ? "" : "s"),
                () => Apply(value), () => Apply(!value)));
            Status = $"{f.Name} {(value ? "set" : "cleared")} on {changed.Length} poly" + (changed.Length == 1 ? "" : "s");
            return changed.Length;
        }

        public int MoveVertex(NavDoc doc, Vector3 from, Vector3 to)
        {
            var polys = doc?.Ynv?.Polys;
            if (polys == null) return 0;
            int n = 0;
            foreach (var p in polys)
            {
                var vs = p?.Vertices;
                if (vs == null) continue;
                for (int i = 0; i < vs.Length; i++)
                    if (vs[i] == from) { vs[i] = to; n++; }
            }
            if (n > 0) Touch(doc, structural: false);
            return n;
        }

        public void MovePolys(NavDoc doc, IEnumerable<YnvPoly> polys, Vector3 delta)
        {
            if (polys == null || delta == Vector3.Zero) return;
            var pts = new HashSet<Vector3>();
            foreach (var p in polys)
            {
                var vs = p?.Vertices;
                if (vs == null) continue;
                foreach (var v in vs) pts.Add(v);
            }
            foreach (var v in pts) MoveVertex(doc, v, v + delta);
        }

        public static Vector3 SelectionCentre(IEnumerable<YnvPoly> polys)
        {
            var pts = new HashSet<Vector3>();
            if (polys != null)
                foreach (var p in polys)
                {
                    var vs = p?.Vertices;
                    if (vs == null) continue;
                    foreach (var v in vs) pts.Add(v);
                }
            if (pts.Count == 0) return Vector3.Zero;
            var sum = Vector3.Zero;
            foreach (var v in pts) sum += v;
            return sum / pts.Count;
        }

        public YnvPoly AddPoly(EditHistory hist, NavDoc doc, IList<Vector3> pts, YnvPoly template)
        {
            var poly = BuildPoly(doc?.Ynv, pts, template);
            if (poly == null) return null;
            doc.Ynv.Polys ??= new List<YnvPoly>();
            void Apply() { doc.Ynv.Polys.Add(poly); Touch(doc, structural: true); }
            void Undo() { doc.Ynv.Polys.Remove(poly); SelectedPolys.Remove(poly); Touch(doc, structural: true); }
            Apply();
            hist?.Push(new DelegateCommand("Add nav poly", Apply, Undo));
            Status = $"poly {poly.Index} added to {doc.Name} ({poly.Vertices.Length} corners)";
            return poly;
        }

        public int AddPolys(EditHistory hist, NavDoc doc, IList<YnvPoly> polys, string what)
        {
            if (doc?.Ynv == null || polys == null || polys.Count == 0) return 0;
            var arr = polys.Where(p => p?.Vertices != null && p.Vertices.Length >= 3).ToArray();
            if (arr.Length == 0) return 0;
            doc.Ynv.Polys ??= new List<YnvPoly>();
            void Apply() { foreach (var p in arr) { p.Ynv = doc.Ynv; doc.Ynv.Polys.Add(p); } Touch(doc, structural: true); }
            void Undo() { foreach (var p in arr) { doc.Ynv.Polys.Remove(p); SelectedPolys.Remove(p); } Touch(doc, structural: true); }
            Apply();
            hist?.Push(new DelegateCommand(what ?? $"Add {arr.Length} nav polys", Apply, Undo));
            return arr.Length;
        }

        public static YnvPoly BuildPoly(YnvFile ynv, IList<Vector3> pts, YnvPoly template)
        {
            if (ynv == null || pts == null || pts.Count < 3) return null;
            var verts = pts.ToArray();
            if (SignedAreaXY(verts) < 0) Array.Reverse(verts);

            var poly = new YnvPoly { Ynv = ynv, Vertices = verts, Indices = new ushort[verts.Length] };
            if (template != null) poly.RawData = template.RawData;
            else { poly.B17_IsFlatGround = true; poly.B02_IsFootpath = true; }
            poly.AreaID = (ushort)ynv.AreaID;
            poly._RawData.PortalLinkCount = 0;
            poly._RawData.PortalLinkID = 0;
            poly.PortalLinks = null;
            EnsureEdgeArray(poly);
            poly.CalculatePosition();
            poly.CalculateAABB();
            return poly;
        }

        public static float SignedAreaXY(IList<Vector3> v)
        {
            float a = 0;
            for (int i = 0; i < v.Count; i++)
            {
                var p = v[i]; var q = v[(i + 1) % v.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5f;
        }

        public int DeletePolys(EditHistory hist, NavDoc doc, IList<YnvPoly> polys)
        {
            var list = doc?.Ynv?.Polys;
            if (list == null || polys == null) return 0;
            var gone = polys.Where(p => p != null && list.Contains(p)).ToArray();
            if (gone.Length == 0) return 0;
            var at = gone.Select(p => list.IndexOf(p)).ToArray();
            Array.Sort(at, gone);
            void Apply()
            {
                foreach (var p in gone) list.Remove(p);
                foreach (var p in gone) SelectedPolys.Remove(p);
                Touch(doc, structural: true);
            }
            void Undo()
            {
                for (int i = 0; i < gone.Length; i++) list.Insert(Math.Min(at[i], list.Count), gone[i]);
                Touch(doc, structural: true);
            }
            Apply();
            hist?.Push(new DelegateCommand($"Delete {gone.Length} nav poly" + (gone.Length == 1 ? "" : "s"), Apply, Undo));
            Status = $"{gone.Length} poly" + (gone.Length == 1 ? "" : "s") + " deleted from " + doc.Name;
            return gone.Length;
        }

        public YnvPoint AddPoint(EditHistory hist, NavDoc doc, Vector3 pos, byte type)
        {
            if (doc?.Ynv == null) return null;
            doc.Ynv.Points ??= new List<YnvPoint>();
            var pt = new YnvPoint { Ynv = doc.Ynv, Position = pos, Type = type };
            void Apply() { doc.Ynv.Points.Add(pt); RenumberPoints(doc.Ynv); Bump(doc); }
            void Undo() { doc.Ynv.Points.Remove(pt); if (ReferenceEquals(SelectedPoint, pt)) SelectedPoint = null; RenumberPoints(doc.Ynv); Bump(doc); }
            Apply();
            hist?.Push(new DelegateCommand("Add nav point", Apply, Undo));
            Status = $"point {pt.Index} added to {doc.Name}";
            return pt;
        }

        public bool DeletePoint(EditHistory hist, NavDoc doc, YnvPoint pt)
        {
            var list = doc?.Ynv?.Points;
            if (list == null || pt == null || !list.Contains(pt)) return false;
            int at = list.IndexOf(pt);
            void Apply() { list.Remove(pt); if (ReferenceEquals(SelectedPoint, pt)) SelectedPoint = null; RenumberPoints(doc.Ynv); Bump(doc); }
            void Undo() { list.Insert(Math.Min(at, list.Count), pt); RenumberPoints(doc.Ynv); Bump(doc); }
            Apply();
            hist?.Push(new DelegateCommand("Delete nav point", Apply, Undo));
            Status = "point deleted from " + doc.Name;
            return true;
        }

        public YnvPortal AddPortal(EditHistory hist, NavDoc doc, Vector3 from, Vector3 to)
        {
            if (doc?.Ynv == null) return null;
            doc.Ynv.Portals ??= new List<YnvPortal>();
            var po = new YnvPortal { Ynv = doc.Ynv, PositionFrom = from, PositionTo = to, Type = 1 };
            po.AreaIDFrom = po.AreaIDTo = (ushort)doc.Ynv.AreaID;
            void Apply() { doc.Ynv.Portals.Add(po); RenumberPortals(doc.Ynv); Bump(doc); }
            void Undo() { doc.Ynv.Portals.Remove(po); if (ReferenceEquals(SelectedPortal, po)) SelectedPortal = null; RenumberPortals(doc.Ynv); Bump(doc); }
            Apply();
            hist?.Push(new DelegateCommand("Add nav portal", Apply, Undo));
            Status = $"portal {po.Index} added to {doc.Name}";
            return po;
        }

        public bool DeletePortal(EditHistory hist, NavDoc doc, YnvPortal po)
        {
            var list = doc?.Ynv?.Portals;
            if (list == null || po == null || !list.Contains(po)) return false;
            int at = list.IndexOf(po);
            void Apply() { list.Remove(po); if (ReferenceEquals(SelectedPortal, po)) SelectedPortal = null; RenumberPortals(doc.Ynv); Bump(doc); }
            void Undo() { list.Insert(Math.Min(at, list.Count), po); RenumberPortals(doc.Ynv); Bump(doc); }
            Apply();
            hist?.Push(new DelegateCommand("Delete nav portal", Apply, Undo));
            Status = "portal deleted from " + doc.Name;
            return true;
        }

        private static void RenumberPoints(YnvFile ynv)
        {
            var l = ynv?.Points; if (l == null) return;
            for (int i = 0; i < l.Count; i++) { l[i].Index = i; l[i].Ynv = ynv; }
        }
        private static void RenumberPortals(YnvFile ynv)
        {
            var l = ynv?.Portals; if (l == null) return;
            for (int i = 0; i < l.Count; i++) { l[i].Index = i; l[i].Ynv = ynv; }
        }

        private static void Bump(NavDoc doc)
        {
            if (doc == null) return;
            doc.Version++;
            doc.Dirty = true;
        }

        public void SetField<T>(EditHistory hist, NavDoc doc, object owner, string field, T from, T to, Action<T> set)
        {
            if (Equals(from, to)) return;
            set(to);
            Bump(doc);
            var cmd = new NavFieldCommand<T>(field, owner, from, to, v => { set(v); Bump(doc); });
            hist?.Push(cmd);
        }

        private sealed class NavFieldCommand<T> : IEditCommand, IMergeableCommand
        {
            private readonly object owner;
            private readonly string field;
            private readonly T before;
            private T after;
            private readonly Action<T> set;
            public string Name { get; }
            public NavFieldCommand(string field, object owner, T before, T after, Action<T> set)
            {
                this.owner = owner; this.field = field; this.before = before; this.after = after; this.set = set;
                Name = field;
            }
            public void Do() => set(after);
            public void Undo() => set(before);
            public bool TryMerge(IEditCommand newer, TimeSpan since)
            {
                if (!(newer is NavFieldCommand<T> n)) return false;
                if (!ReferenceEquals(owner, n.owner) || field != n.field) return false;
                if (since > TimeSpan.FromMilliseconds(500)) return false;
                after = n.after;
                return true;
            }
        }

        public delegate bool GroundSampler(float x, float y, out float z);

        public static List<YnvPoly> Generate(YnvFile ynv, Func<float, float, bool> insideXY,
                                             BoundingBox box, float spacing, float slopeLimitDeg,
                                             bool interior, GroundSampler sample, out string report)
        {
            var result = new List<YnvPoly>();
            report = "";
            if (ynv == null || sample == null) { report = "no file"; return result; }
            spacing = Math.Clamp(spacing, 0.25f, 8.0f);
            int nx = (int)Math.Floor((box.Maximum.X - box.Minimum.X) / spacing);
            int ny = (int)Math.Floor((box.Maximum.Y - box.Minimum.Y) / spacing);
            if (nx < 1 || ny < 1) { report = "the area is smaller than one grid cell"; return result; }
            long cells = (long)nx * ny;
            if (cells > 250000) { report = $"{cells:N0} cells - widen the grid or shrink the area"; return result; }

            int gw = nx + 1, gh = ny + 1;
            var hits = new bool[gw * gh];
            var zs = new float[gw * gh];
            int found = 0;
            for (int iy = 0; iy < gh; iy++)
                for (int ix = 0; ix < gw; ix++)
                {
                    float x = box.Minimum.X + ix * spacing;
                    float y = box.Minimum.Y + iy * spacing;
                    int k = iy * gw + ix;
                    if (insideXY != null && !insideXY(x, y)) continue;
                    if (sample(x, y, out float z)) { hits[k] = true; zs[k] = z; found++; }
                }
            if (found == 0) { report = "no collision under the area - turn Collision on in the World options and let it load"; return result; }

            float cosLimit = (float)Math.Cos(Math.Clamp(slopeLimitDeg, 1.0f, 89.0f) * Math.PI / 180.0);
            float stepLimit = Math.Max(0.6f, spacing * 1.5f);
            int steep = 0, stepped = 0;
            var template = new YnvPoly();
            template.B17_IsFlatGround = true;
            template.B02_IsFootpath = !interior;
            template.B14_IsInterior = interior;

            var okCell = new bool[nx * ny];
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int k00 = iy * gw + ix, k10 = k00 + 1, k01 = k00 + gw, k11 = k01 + 1;
                    if (!hits[k00] || !hits[k10] || !hits[k01] || !hits[k11]) continue;
                    float zmin = Math.Min(Math.Min(zs[k00], zs[k10]), Math.Min(zs[k01], zs[k11]));
                    float zmax = Math.Max(Math.Max(zs[k00], zs[k10]), Math.Max(zs[k01], zs[k11]));
                    if (zmax - zmin > stepLimit) { stepped++; continue; }

                    float x0c = box.Minimum.X + ix * spacing, x1c = x0c + spacing;
                    float y0c = box.Minimum.Y + iy * spacing, y1c = y0c + spacing;
                    var q0 = new Vector3(x0c, y0c, zs[k00]);
                    var q1 = new Vector3(x1c, y0c, zs[k10]);
                    var q2 = new Vector3(x1c, y1c, zs[k11]);
                    var q3 = new Vector3(x0c, y1c, zs[k01]);
                    var nrm = Vector3.Cross(q1 - q0, q3 - q0) + Vector3.Cross(q3 - q2, q1 - q2);
                    if (nrm.LengthSquared() < 1e-9f) continue;
                    nrm.Normalize();
                    if (Math.Abs(nrm.Z) < cosLimit) { steep++; continue; }
                    okCell[iy * nx + ix] = true;
                }

            const float lift = 0.05f;
            var blocks = MergeCells_V20(okCell, zs, nx, ny);
            int cellsUsed = 0;
            foreach (var b in blocks)
            {
                cellsUsed += b.W * b.H;
                var quad = BlockQuad_V20(b, box.Minimum, spacing, zs, nx, lift);
                var poly = BuildPoly(ynv, quad, template);
                if (poly != null) result.Add(poly);
            }

            report = $"{result.Count} polygon(s) from {cellsUsed:N0} usable cells of {found:N0} ground samples" +
                     (cellsUsed > result.Count ? $" (merged {cellsUsed - result.Count} away)" : "") +
                     (steep > 0 ? $"; {steep} too steep" : "") + (stepped > 0 ? $"; {stepped} on a step" : "");
            return result;
        }

        public static float GenArea(IEnumerable<YnvPoly> polys)
        {
            float total = 0.0f;
            foreach (var p in polys ?? Enumerable.Empty<YnvPoly>())
            {
                var v = p?.Vertices;
                if (v == null || v.Length < 3) continue;
                total += Math.Abs(SignedAreaXY(v));
            }
            return total;
        }

        public YnvPoly Pick(Ray ray, out NavDoc hitDoc, out float hitDist)
        {
            hitDoc = null; hitDist = float.MaxValue;
            YnvPoly best = null;
            foreach (var doc in Docs)
            {
                if (!doc.Visible || doc.Ynv?.Polys == null) continue;
                var db = doc.Bounds;
                if (!ray.Intersects(ref db, out float _)) continue;
                var polys = doc.Ynv.Polys;
                for (int i = 0; i < polys.Count; i++)
                {
                    var p = polys[i];
                    if (p?.Vertices == null || p.Vertices.Length < 3) continue;
                    if (!PolyVisible(p)) continue;
                    if (i < doc.PolyBounds.Length)
                    {
                        var bb = doc.PolyBounds[i];
                        if (!ray.Intersects(ref bb, out float _)) continue;
                    }
                    var v = p.Vertices;
                    for (int t = 0; t < v.Length - 2; t++)
                    {
                        if ((ray.Intersects(ref v[0], ref v[t + 1], ref v[t + 2], out float d) ||
                             ray.Intersects(ref v[0], ref v[t + 2], ref v[t + 1], out d)) && d < hitDist)
                        { hitDist = d; best = p; hitDoc = doc; break; }
                    }
                }
            }
            return best;
        }

        public object PickNode(Ray ray, float radius, out NavDoc hitDoc, out float hitDist)
        {
            hitDoc = null; hitDist = float.MaxValue;
            object best = null;
            foreach (var doc in Docs)
            {
                if (!doc.Visible || doc.Ynv == null) continue;
                if (ShowPoints && doc.Ynv.Points != null)
                    foreach (var pt in doc.Ynv.Points)
                    {
                        var s = new BoundingSphere(pt.Position, radius);
                        if (ray.Intersects(ref s, out float d) && d < hitDist) { hitDist = d; best = pt; hitDoc = doc; }
                    }
                if (ShowPortals && doc.Ynv.Portals != null)
                    foreach (var po in doc.Ynv.Portals)
                    {
                        var s = new BoundingSphere(po.PositionFrom, radius);
                        if (ray.Intersects(ref s, out float d) && d < hitDist) { hitDist = d; best = po; hitDoc = doc; }
                    }
            }
            return best;
        }

        public static int NearestVertex(YnvPoly p, Vector3 pos)
        {
            if (p?.Vertices == null || p.Vertices.Length == 0) return -1;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < p.Vertices.Length; i++)
            {
                float d = (p.Vertices[i] - pos).LengthSquared();
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public IEnumerable<YnvPoly> FilteredPolys(NavMeshEditor.NavDoc doc, int max)
        {
            var polys = doc?.Ynv?.Polys;
            if (polys == null) yield break;
            var f = Filter?.Trim();
            bool hasFilter = !string.IsNullOrEmpty(f);
            int n = 0;
            for (int i = 0; i < polys.Count && n < max; i++)
            {
                var p = polys[i];
                if (p == null) continue;
                if (!PolyVisible(p)) continue;
                if (hasFilter && !MatchesFilter(p, f)) continue;
                n++;
                yield return p;
            }
        }

        public bool MatchesFilter(YnvPoly p, string f)
        {
            if (p.Index.ToString().Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
            if (CatNames[(int)CategoryOf(p)].Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < Flags.Length; i++)
                if (Flags[i].Get(p) && Flags[i].Name.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public int TotalPolys => Docs.Sum(d => d.PolyCount);
        public int TotalPoints => Docs.Sum(d => d.PointCount);
        public int TotalPortals => Docs.Sum(d => d.PortalCount);
        public bool AnyDirty => Docs.Any(d => d.Dirty);

        public static int SelfTest()
        {
            int fails = 0;
            void Check(string what, bool ok, string detail = "")
            {
                if (!ok) { fails++; Console.WriteLine($"  NAVMESH FAIL: {what}   {detail}"); }
                else Console.WriteLine($"  navmesh ok: {what}");
            }

            var ed = new NavMeshEditor();
            var hist = new EditHistory();
            var ynv = NewFile(50, 20, new Vector3(0, 0, -20), new Vector3(150, 150, 130));
            var doc = ed.Add(ynv, null, "self-test");

            var a = ed.AddPoly(hist, doc, new[]
            {
                new Vector3(10, 10, 0), new Vector3(20, 10, 0), new Vector3(20, 20, 0), new Vector3(10, 20, 0)
            }, null);
            var b = ed.AddPoly(hist, doc, new[]
            {
                new Vector3(20, 10, 0), new Vector3(30, 10, 0), new Vector3(30, 20, 0), new Vector3(20, 20, 0)
            }, null);
            Check("two polys added", a != null && b != null && ynv.Polys.Count == 2, $"{ynv.Polys.Count}");
            Check("both are wound anticlockwise from above", a != null && SignedAreaXY(a.Vertices) > 0 && b != null && SignedAreaXY(b.Vertices) > 0);
            Check("the shared edge links them", a != null && b != null &&
                  a.Edges.Any(e => e.PolyID1 == (uint)b.Index) && b.Edges.Any(e => e.PolyID1 == (uint)a.Index));
            Check("neither reads as isolated", a != null && !IsIsolated(a) && b != null && !IsIsolated(b));

            var border = a.Edges[0];
            border.AreaID1 = border.AreaID2 = (uint)(ynv.AreaID + 1);
            border.PolyID1 = border.PolyID2 = 77;
            var c = ed.AddPoly(hist, doc, new[]
            {
                new Vector3(10, 30, 0), new Vector3(20, 30, 0), new Vector3(20, 40, 0)
            }, null);
            ed.DeletePolys(hist, doc, new[] { c });
            Check("a cross-cell edge survives an add and a delete",
                  border.PolyID1 == 77 && border.AreaID1 == (uint)(ynv.AreaID + 1),
                  $"poly {border.PolyID1} area {border.AreaID1}");

            int moved = ed.MoveVertex(doc, new Vector3(20, 10, 0), new Vector3(20, 10, 2));
            Check("moving a shared corner moves both polys' copies of it", moved == 2, $"{moved} vertices");

            int flagIdx = FlagIndex("water");
            Check("a flag can be found by name", flagIdx == 7, $"index {flagIdx}");
            ed.SetFlag(hist, new[] { a, b }, flagIdx, true);
            Check("the flag is set on both", a.B07_IsWater && b.B07_IsWater);
            Check("a water poly categorises as water", ed.CategoryOf(a) == NavCat.Water, ed.CategoryOf(a).ToString());
            hist.Undo();
            Check("undo clears it again", !a.B07_IsWater && !b.B07_IsWater);
            hist.Redo();
            Check("redo sets it back", a.B07_IsWater && b.B07_IsWater);

            ed.Filter = "water";
            Check("the filter finds the water polys", ed.FilteredPolys(doc, 100).Count() == 2);
            ed.Filter = "";

            int before = ynv.Polys.Count;
            ed.DeletePolys(hist, doc, new[] { b });
            Check("delete removes the poly", ynv.Polys.Count == before - 1, $"{ynv.Polys.Count}");
            hist.Undo();
            Check("undo puts it back", ynv.Polys.Count == before && ynv.Polys.Contains(b), $"{ynv.Polys.Count}");
            Check("and the link with it", a.Edges.Any(e => e.PolyID1 == (uint)b.Index));

            var pt = ed.AddPoint(hist, doc, new Vector3(15, 15, 1), 3);
            pt.Direction = 1.0f;
            var po = ed.AddPortal(hist, doc, new Vector3(15, 15, 1), new Vector3(15, 18, 1));
            Check("a point and a portal were added", ynv.Points.Count == 1 && ynv.Portals.Count == 1);

            try
            {
                ynv.UpdateContentFlags(false);
                var bytes = ynv.Save();
                Check("the file writes", bytes != null && bytes.Length > 0, $"{bytes?.Length ?? 0} bytes");
                var re = new YnvFile();
                re.Load(bytes);
                int rp = re.Polys?.Count ?? 0;
                Check("it reads back with the same polygon count", rp == ynv.Polys.Count, $"{rp} vs {ynv.Polys.Count}");
                if (rp == ynv.Polys.Count)
                {
                    Check("the flag survived the round trip", re.Polys[0].B07_IsWater, "poly 0 water bit");
                    var v0 = re.Polys[0].Vertices?[0] ?? Vector3.Zero;
                    Check("the geometry survived", (v0 - ynv.Polys[0].Vertices[0]).Length() < 0.05f,
                          $"{v0} vs {ynv.Polys[0].Vertices[0]}");
                }
                Check("the point survived", (re.Points?.Count ?? 0) == 1, $"{re.Points?.Count ?? 0}");
                Check("the portal survived", (re.Portals?.Count ?? 0) == 1, $"{re.Portals?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                fails++;
                Console.WriteLine("  NAVMESH FAIL: round trip threw  " + ex.Message);
            }

            {
                var gynv = NewFile(50, 20, new Vector3(0, 0, -20), new Vector3(150, 150, 130));
                bool Flat(float x, float y, out float z) { z = 3.0f; return true; }
                var gbox = new BoundingBox(new Vector3(0, 0, 0), new Vector3(10, 10, 10));
                var gen = Generate(gynv, null, gbox, 1.0f, 40.0f, false, Flat, out var rep);
                Check("the generator paves a flat plane", gen.Count > 0 && GenArea(gen) > 99.0f,
                      $"{gen.Count} polygon(s) covering {GenArea(gen):0.#} of 100 m2 - {rep}");
                Check("...and merges it instead of leaving one quad per grid cell", gen.Count < 20,
                      $"{gen.Count} polygon(s) where the grid had 100 cells");
                Check("its polygons are wound the way the game reads them",
                      gen.Count > 0 && SignedAreaXY(gen[0].Vertices) > 0);
                bool Ramp(float x, float y, out float z) { z = x * 0.577f; return true; }
                var gen1 = Generate(gynv, null, gbox, 1.0f, 40.0f, false, Ramp, out var rep1);
                Check("it paves a 30 degree ramp", gen1.Count > 0 && GenArea(gen1) > 99.0f,
                      $"{gen1.Count} polygon(s) covering {GenArea(gen1):0.#} of 100 m2 - {rep1}");
                bool Steep(float x, float y, out float z) { z = x * 1.7f; return true; }
                var gen2 = Generate(gynv, null, gbox, 1.0f, 40.0f, false, Steep, out var rep2);
                Check("and refuses a 60 degree slope at a 40 degree limit", gen2.Count == 0, $"{gen2.Count} polys - {rep2}");
                bool Holed(float x, float y, out float z) { z = 2.0f; return !(x > 3.5f && x < 6.5f && y > 3.5f && y < 6.5f); }
                var gen3 = Generate(gynv, null, gbox, 1.0f, 40.0f, false, Holed, out var rep3);
                Check("a gap in the collision is left as a gap",
                      GenArea(gen3) > 80.0f && GenArea(gen3) < 99.0f,
                      $"{gen3.Count} polygon(s) covering {GenArea(gen3):0.#} m2 of 100 - {rep3}");
            }

            fails += SelfTestQ2();

            Console.WriteLine(fails == 0 ? "NAVMESH SELF-TEST PASSED" : $"NAVMESH SELF-TEST FAILED ({fails})");
            return fails;
        }
    }

    public sealed class NavVertexTarget : IWorldGizmoTarget
    {
        private readonly NavMeshEditor editor;
        private readonly NavMeshEditor.NavDoc doc;
        public readonly YnvPoly Poly;
        public readonly int Vertex;
        public NavVertexTarget(NavMeshEditor editor, NavMeshEditor.NavDoc doc, YnvPoly poly, int vertex)
        { this.editor = editor; this.doc = doc; Poly = poly; Vertex = vertex; }
        public object Key => (Poly, Vertex);
        public Vector3 Position => Poly?.Vertices != null && Vertex >= 0 && Vertex < Poly.Vertices.Length ? Poly.Vertices[Vertex] : Vector3.Zero;
        public Quaternion Orientation => Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.None;
        public bool ScaleLockXY => true;
        public bool CanScale => false;
        public void SetPosition(Vector3 p) { var from = Position; if (from != p) editor.MoveVertex(doc, from, p); }
        public void SetOrientation(Quaternion q) { }
        public void SetScale(Vector3 s) { }
    }

    public sealed class NavSelectionTarget : IWorldGizmoTarget
    {
        private readonly NavMeshEditor editor;
        private readonly NavMeshEditor.NavDoc doc;
        public readonly YnvPoly[] Polys;
        public NavSelectionTarget(NavMeshEditor editor, NavMeshEditor.NavDoc doc, YnvPoly[] polys)
        { this.editor = editor; this.doc = doc; Polys = polys ?? Array.Empty<YnvPoly>(); }
        public object Key => Polys.Length > 0 ? Polys[Polys.Length - 1] : (object)this;
        public Vector3 Position => NavMeshEditor.SelectionCentre(Polys);
        public Quaternion Orientation => Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.None;
        public bool ScaleLockXY => true;
        public bool CanScale => false;
        public void SetPosition(Vector3 p) => editor.MovePolys(doc, Polys, p - Position);
        public void SetOrientation(Quaternion q) { }
        public void SetScale(Vector3 s) { }
    }

    public sealed class NavPointTarget : IWorldGizmoTarget
    {
        private readonly NavMeshEditor.NavDoc doc;
        public readonly YnvPoint Point;
        public NavPointTarget(NavMeshEditor.NavDoc doc, YnvPoint pt) { this.doc = doc; Point = pt; }
        public object Key => Point;
        public Vector3 Position => Point?.Position ?? Vector3.Zero;
        public Quaternion Orientation => Point?.Orientation ?? Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.Z;
        public bool ScaleLockXY => true;
        public bool CanScale => false;
        public void SetPosition(Vector3 p) { if (Point != null) { Point.Position = p; Bump(); } }
        public void SetOrientation(Quaternion q) { if (Point != null) { Point.Orientation = q; Bump(); } }
        public void SetScale(Vector3 s) { }
        private void Bump() { if (doc != null) { doc.Version++; doc.Dirty = true; } }
    }

    public sealed class NavPortalTarget : IWorldGizmoTarget
    {
        private readonly NavMeshEditor.NavDoc doc;
        public readonly YnvPortal Portal;
        public NavPortalTarget(NavMeshEditor.NavDoc doc, YnvPortal po) { this.doc = doc; Portal = po; }
        public object Key => Portal;
        public Vector3 Position => Portal?.PositionFrom ?? Vector3.Zero;
        public Quaternion Orientation => Portal?.Orientation ?? Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.Z;
        public bool ScaleLockXY => true;
        public bool CanScale => false;
        public void SetPosition(Vector3 p) { if (Portal != null) { Portal.SetPosition(p); Bump(); } }
        public void SetOrientation(Quaternion q) { if (Portal != null) { Portal.Orientation = q; Bump(); } }
        public void SetScale(Vector3 s) { }
        private void Bump() { if (doc != null) { doc.Version++; doc.Dirty = true; } }
    }
}

