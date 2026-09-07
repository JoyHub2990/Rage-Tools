using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {
        public static void ResolveLinks_V16(YnvFile ynv)
        {
            var polys = ynv?.Polys;
            if (polys == null) return;
            uint area = (uint)ynv.AreaID;
            for (int i = 0; i < polys.Count; i++)
            {
                var p = polys[i];
                if (p == null) continue;
                p.Index = i;
                foreach (var e in p.Edges ?? Array.Empty<YnvEdge>())
                {
                    if (e == null) continue;
                    e.Poly1 = ResolveOne_V16(polys, area, e.AreaID1, e.PolyID1);
                    e.Poly2 = ResolveOne_V16(polys, area, e.AreaID2, e.PolyID2);
                }
            }
        }

        private static YnvPoly ResolveOne_V16(IList<YnvPoly> polys, uint area, uint edgeArea, uint id)
        {
            if (id == NoPoly_V15) return null;
            if (edgeArea != area) return null;
            return id < polys.Count ? polys[(int)id] : null;
        }

        public static void RelinkStructural_V16(YnvFile ynv, ISet<YnvPoly> disabled = null)
        {
            var polys = ynv?.Polys;
            if (polys == null) return;
            uint area = (uint)ynv.AreaID;

            var at = new Dictionary<YnvPoly, int>(polys.Count);
            for (int i = 0; i < polys.Count; i++) if (polys[i] != null) at[polys[i]] = i;

            bool Out(YnvPoly p) => p == null || !at.ContainsKey(p) || (disabled != null && disabled.Contains(p));

            foreach (var p in polys)
            {
                if (p == null) continue;
                EnsureEdgeArray_V16(p);
                bool pOut = disabled != null && disabled.Contains(p);
                foreach (var e in p.Edges)
                {
                    if (e == null) continue;

                    if (e.Poly1 != null)
                    {
                        if (pOut || Out(e.Poly1)) { e.Poly1 = null; e.PolyID1 = NoPoly_V15; e.AreaID1 = NoPoly_V15; }
                        else { e.PolyID1 = (uint)at[e.Poly1]; e.AreaID1 = area; }
                    }
                    else if (e.PolyID1 != NoPoly_V15 && e.AreaID1 == area)
                    {
                        e.PolyID1 = NoPoly_V15; e.AreaID1 = NoPoly_V15;
                    }
                    else if (pOut && e.PolyID1 != NoPoly_V15)
                    {
                        e.PolyID1 = NoPoly_V15; e.AreaID1 = NoPoly_V15;
                    }

                    if (e.Poly2 != null)
                    {
                        if (pOut || Out(e.Poly2)) { e.Poly2 = null; e.PolyID2 = NoPoly_V15; e.AreaID2 = NoPoly_V15; }
                        else { e.PolyID2 = (uint)at[e.Poly2]; e.AreaID2 = area; }
                    }
                    else if (e.PolyID2 != NoPoly_V15 && e.AreaID2 == area)
                    {
                        e.PolyID2 = NoPoly_V15; e.AreaID2 = NoPoly_V15;
                    }
                    else if (pOut && e.PolyID2 != NoPoly_V15)
                    {
                        e.PolyID2 = NoPoly_V15; e.AreaID2 = NoPoly_V15;
                    }
                }
            }

            FillNewPolyLinks_V16(ynv, at, area, disabled);
        }

        private static void FillNewPolyLinks_V16(YnvFile ynv, Dictionary<YnvPoly, int> at, uint area,
                                                 ISet<YnvPoly> disabled)
        {
            var polys = ynv.Polys;
            var map = new Dictionary<(Vector3, Vector3), List<(YnvPoly poly, int edge)>>();
            foreach (var p in polys)
            {
                var vs = p?.Vertices;
                if (vs == null || vs.Length < 3) continue;
                if (disabled != null && disabled.Contains(p)) continue;
                for (int i = 0; i < vs.Length; i++)
                {
                    var key = EdgeKey_V16(vs[i], vs[(i + 1) % vs.Length]);
                    if (!map.TryGetValue(key, out var l)) map[key] = l = new List<(YnvPoly, int)>();
                    l.Add((p, i));
                }
            }

            foreach (var kv in map)
            {
                var l = kv.Value;
                if (l.Count != 2) continue;
                var (pa, ea) = l[0];
                var (pb, eb) = l[1];
                if (ea >= (pa.Edges?.Length ?? 0) || eb >= (pb.Edges?.Length ?? 0)) continue;
                var a = pa.Edges[ea];
                var b = pb.Edges[eb];
                if (a == null || b == null) continue;
                bool aFree = a.PolyID1 == NoPoly_V15 && a.PolyID2 == NoPoly_V15;
                bool bFree = b.PolyID1 == NoPoly_V15 && b.PolyID2 == NoPoly_V15;
                bool aNamesB = a.Poly1 == pb || a.Poly2 == pb;
                bool bNamesA = b.Poly1 == pa || b.Poly2 == pa;

                void LinkA() { a.Poly1 = a.Poly2 = pb; a.PolyID1 = a.PolyID2 = (uint)at[pb]; a.AreaID1 = a.AreaID2 = area; }
                void LinkB() { b.Poly1 = b.Poly2 = pa; b.PolyID1 = b.PolyID2 = (uint)at[pa]; b.AreaID1 = b.AreaID2 = area; }

                if (aFree && bFree)
                {
                    LinkA(); LinkB();
                }
                else if (aFree && bNamesA)
                {
                    LinkA();
                }
                else if (bFree && aNamesB)
                {
                    LinkB();
                }
            }
        }

        private static (Vector3 a, Vector3 b) EdgeKey_V16(Vector3 a, Vector3 b)
        {
            bool swap = a.X != b.X ? a.X > b.X : a.Y != b.Y ? a.Y > b.Y : a.Z > b.Z;
            return swap ? (b, a) : (a, b);
        }

        public static YnvEdge NewEmptyEdge_V16()
        {
            var e = new YnvEdge();
            e.PolyID1 = e.PolyID2 = NoPoly_V15;
            e.AreaID1 = e.AreaID2 = NoPoly_V15;
            return e;
        }

        private static void EnsureEdgeArray_V16(YnvPoly p)
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

        public static int RelinkSelfTest_V16(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string detail)
            { if (!ok) fails++; check(what, ok, detail); }

            var ynv = NewFile(4321, 20, new Vector3(0, 0, -20), new Vector3(150, 150, 130));
            var ed = new NavMeshEditor();
            var doc = ed.Add(ynv, null, "v16");

            var made = new List<YnvPoly>();
            for (int i = 0; i < 3; i++)
            {
                float x = i * 4f;
                made.Add(BuildPoly(ynv, new[]
                {
                    new Vector3(x, 0, 0), new Vector3(x + 4, 0, 0),
                    new Vector3(x + 4, 4, 0), new Vector3(x, 4, 0),
                }, null));
            }
            ed.AddPolys(null, doc, made, "v16 fixture");

            var t = made[0].Edges[2];
            t.Poly1 = made[2]; t.Poly2 = made[2];
            t.PolyID1 = t.PolyID2 = (uint)ynv.Polys.IndexOf(made[2]);
            t.AreaID1 = t.AreaID2 = (uint)ynv.AreaID;

            var foreign = made[1].Edges[0];
            foreign.Poly1 = null; foreign.Poly2 = null;
            foreign.PolyID1 = foreign.PolyID2 = 77;
            foreign.AreaID1 = foreign.AreaID2 = 9999;

            RelinkStructural_V16(ynv, doc.Disabled_V15);
            Chk("v16: a link the geometry cannot explain SURVIVES a structural pass",
                t.PolyID1 == (uint)ynv.Polys.IndexOf(made[2]) && t.Poly1 == made[2],
                $"still names poly {t.PolyID1}");
            Chk("v16: ...and a link into another cell is left exactly as it was",
                foreign.PolyID1 == 77 && foreign.AreaID1 == 9999,
                $"area {foreign.AreaID1}, poly {foreign.PolyID1}");

            ynv.Polys.Remove(made[2]);
            ynv.Polys.Insert(0, made[2]);
            RelinkStructural_V16(ynv, doc.Disabled_V15);
            Chk("v16: a link follows its neighbour to a new index",
                t.PolyID1 == (uint)ynv.Polys.IndexOf(made[2]) && t.PolyID1 == 0,
                $"now names poly {t.PolyID1}");

            ynv.Polys.Remove(made[2]);
            RelinkStructural_V16(ynv, doc.Disabled_V15);
            Chk("v16: ...and a deleted neighbour becomes NO neighbour, never a stale index",
                t.PolyID1 == NoPoly_V15 && t.Poly1 == null, $"names {t.PolyID1}");

            return fails;
        }
    }
}

