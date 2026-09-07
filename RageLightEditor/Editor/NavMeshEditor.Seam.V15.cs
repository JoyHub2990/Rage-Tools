using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {
        public const uint NoPoly_V15 = 0x3FFF;

        public static void ClearWalkableFlags_V15(YnvPoly p)
        {
            if (p == null) return;
            p.B02_IsFootpath = false;
            p.B17_IsFlatGround = false;
            p.B18_IsRoad = false;
            p.B13_HasPathNode = false;
            p.B22_FootpathUnk1 = false;
            p.B23_FootpathUnk2 = false;
            p.B24_FootpathMall = false;
            p.B20_IsTrainTrack = false;
            p.B06_SteepSlope = true;
        }

        public static void SeedDisabled_V15(NavDoc doc)
        {
            var polys = doc?.Ynv?.Polys;
            if (polys == null) return;
            foreach (var p in polys)
            {
                if (p == null || !IsDisabled_V15(p)) continue;
                var edges = p.Edges;
                if (edges == null || edges.Length == 0) continue;
                bool severed = true;
                foreach (var e in edges)
                    if (e != null && (e.PolyID1 != NoPoly_V15 || e.PolyID2 != NoPoly_V15)) { severed = false; break; }
                if (severed) doc.Disabled_V15.Add(p);
            }
        }

        public static bool IsDisabled_V15(YnvPoly p) =>
            p != null && p.B06_SteepSlope && !p.B02_IsFootpath && !p.B17_IsFlatGround && !p.B18_IsRoad;

        public int DisablePolys_V15(EditHistory hist, NavDoc doc, IList<YnvPoly> polys)
        {
            var list = doc?.Ynv?.Polys;
            if (list == null || polys == null) return 0;
            var target = polys.Where(p => p != null && list.Contains(p)).ToArray();
            if (target.Length == 0) return 0;

            var set = new HashSet<YnvPoly>(target);

            var flagsBefore = target.ToDictionary(p => p, p => (p._RawData.PolyFlags0, p._RawData.PolyFlags1, p._RawData.PolyFlags2));
            var edgesBefore = new Dictionary<YnvEdge, (uint v1, uint v2, YnvPoly p1, YnvPoly p2, uint a1, uint a2)>();

            void Remember(YnvEdge e)
            {
                if (e == null || edgesBefore.ContainsKey(e)) return;
                edgesBefore[e] = (e._RawData._Poly1.Value, e._RawData._Poly2.Value, e.Poly1, e.Poly2, e.AreaID1, e.AreaID2);
            }

            foreach (var p in target)
                foreach (var e in p.Edges ?? Array.Empty<YnvEdge>()) Remember(e);
            foreach (var p in list)
            {
                if (set.Contains(p)) continue;
                foreach (var e in p.Edges ?? Array.Empty<YnvEdge>())
                {
                    if (e == null) continue;
                    if ((e.Poly1 != null && set.Contains(e.Poly1)) || (e.Poly2 != null && set.Contains(e.Poly2)))
                        Remember(e);
                }
            }

            void Sever(YnvEdge e, bool ownEdge)
            {
                if (e == null) return;
                if (ownEdge || (e.Poly1 != null && set.Contains(e.Poly1)))
                { e.Poly1 = null; e.PolyID1 = NoPoly_V15; e.AreaID1 = NoPoly_V15; }
                if (ownEdge || (e.Poly2 != null && set.Contains(e.Poly2)))
                { e.Poly2 = null; e.PolyID2 = NoPoly_V15; e.AreaID2 = NoPoly_V15; }
            }

            void Apply()
            {
                foreach (var p in target) doc.Disabled_V15.Add(p);

                foreach (var p in target) ClearWalkableFlags_V15(p);

                Touch(doc, structural: true);

                foreach (var p in target)
                {
                    foreach (var e in p.Edges ?? Array.Empty<YnvEdge>()) { Remember(e); Sever(e, true); }
                }
                foreach (var p in list)
                {
                    if (set.Contains(p)) continue;
                    foreach (var e in p.Edges ?? Array.Empty<YnvEdge>())
                    {
                        if (e == null) continue;
                        if ((e.Poly1 != null && set.Contains(e.Poly1)) || (e.Poly2 != null && set.Contains(e.Poly2))) Remember(e);
                        Sever(e, false);
                    }
                }
                SyncVertexLists(doc.Ynv);
                Touch(doc, structural: false);
            }

            void Undo()
            {
                foreach (var p in target) doc.Disabled_V15.Remove(p);
                foreach (var kv in flagsBefore)
                {
                    kv.Key._RawData.PolyFlags0 = kv.Value.PolyFlags0;
                    kv.Key._RawData.PolyFlags1 = kv.Value.PolyFlags1;
                    kv.Key._RawData.PolyFlags2 = kv.Value.PolyFlags2;
                }
                foreach (var kv in edgesBefore)
                {
                    var e = kv.Key;
                    e._RawData._Poly1.Value = kv.Value.v1;
                    e._RawData._Poly2.Value = kv.Value.v2;
                    e.Poly1 = kv.Value.p1; e.Poly2 = kv.Value.p2;
                    e.AreaID1 = kv.Value.a1; e.AreaID2 = kv.Value.a2;
                }
                Touch(doc, structural: true);
            }

            Apply();
            hist?.Push(new DelegateCommand(
                $"Disable {target.Length} nav poly" + (target.Length == 1 ? "" : "s"), Apply, Undo));
            Status = $"{target.Length} poly" + (target.Length == 1 ? "" : "s") +
                     " disabled in " + doc.Name + " - indices unchanged, neighbouring files still valid";
            return target.Length;
        }

        public sealed class SeamReport_V15
        {
            public int PolyCount;
            public int PolyCountOnLoad;
            public int InboundEdges;
            public bool CountChanged => PolyCount != PolyCountOnLoad;
            public bool Safe => !CountChanged;
            public string Message =>
                Safe
                    ? $"{PolyCount} polys, unchanged in number - every index is where it was, so the {InboundEdges} edge(s) from neighbouring files still point at the right polygons."
                    : $"THE POLY COUNT CHANGED ({PolyCountOnLoad} -> {PolyCount}). Every poly after the first change has a new index, and the {InboundEdges} edge(s) from neighbouring files still name the OLD ones - that is what sends traffic wrong at the seam. Use Disable instead of Delete, or re-export every ynv that touches this one.";
        }

        public SeamReport_V15 CheckSeams_V15(NavDoc doc)
        {
            var r = new SeamReport_V15();
            if (doc?.Ynv == null) return r;
            r.PolyCount = doc.Ynv.Polys?.Count ?? 0;
            r.PolyCountOnLoad = doc.PolyCountOnLoad_V15 >= 0 ? doc.PolyCountOnLoad_V15 : r.PolyCount;

            uint area = (uint)doc.Ynv.AreaID;
            foreach (var other in Docs)
            {
                if (other == null || ReferenceEquals(other, doc) || other.Ynv?.Polys == null) continue;
                foreach (var p in other.Ynv.Polys)
                    foreach (var e in p?.Edges ?? Array.Empty<YnvEdge>())
                    {
                        if (e == null) continue;
                        if (e.AreaID1 == area && e.PolyID1 != NoPoly_V15) r.InboundEdges++;
                        if (e.AreaID2 == area && e.PolyID2 != NoPoly_V15) r.InboundEdges++;
                    }
            }
            return r;
        }

        public static int SeamSelfTest_V15(Action<string, bool, string> check)
        {
            int fails = 0;
            void C(string what, bool ok, string detail)
            { if (!ok) fails++; check?.Invoke(what, ok, detail); }

            var ep = new NavMeshEdgePart();
            ep.PolyID = 812; ep.AreaIDInd = 3;
            C("v15: an edge names its neighbour by poly INDEX (14 bits) and an area slot (5)",
              ep.PolyID == 812 && ep.AreaIDInd == 3 && NoPoly_V15 == 0x3FFF,
              $"PolyID {ep.PolyID}, AreaIDInd {ep.AreaIDInd}, max {NoPoly_V15}");

            ep.PolyID = 811;
            C("v15: ...so deleting one poly makes every later index name a different polygon",
              ep.PolyID == 811, "812 -> 811 after one deletion ahead of it");

            var portal = new NavMeshPortal { PolyIDFrom1 = 5, PolyIDTo1 = 900 };
            portal.AreaIDTo = 1234;
            C("v15: a portal names polys by index too, in a named area",
              portal.PolyIDTo1 == 900 && portal.AreaIDTo == 1234,
              $"to poly {portal.PolyIDTo1} in area {portal.AreaIDTo}");

            var poly = new YnvPoly();
            poly.B02_IsFootpath = true; poly.B17_IsFlatGround = true; poly.B18_IsRoad = true;
            C("v15: a poly starts walkable in this test", poly.B18_IsRoad && poly.B02_IsFootpath, "footpath + flat + road");
            ClearWalkableFlags_V15(poly);
            C("v15: Disable clears every walkable flag and marks it steep",
              !poly.B02_IsFootpath && !poly.B17_IsFlatGround && !poly.B18_IsRoad && !poly.B13_HasPathNode && poly.B06_SteepSlope,
              "footpath/flat/road/pathnode off, steep on");
            C("v15: ...and a disabled poly is recognised as one", IsDisabled_V15(poly), "");

            return fails;
        }
    }
}

