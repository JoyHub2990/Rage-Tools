using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldWater
    {
        public const float WeldZEpsilon_U9 = 0.02f;

        public readonly struct QuadRect_U9
        {
            public readonly float X0, X1, Y0, Y1, Z;
            public QuadRect_U9(float x0, float x1, float y0, float y1, float z) { X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Z = z; }
        }

        public static List<Vector2>[] SplitPointsFor_U9(IReadOnlyList<QuadRect_U9> quads)
        {
            var result = new List<Vector2>[quads.Count];
            for (int i = 0; i < quads.Count; i++) result[i] = new List<Vector2>();
            for (int i = 0; i < quads.Count; i++)
            {
                var a = quads[i];
                for (int j = 0; j < quads.Count; j++)
                {
                    if (i == j) continue;
                    var b = quads[j];
                    if (Math.Abs(a.Z - b.Z) > WeldZEpsilon_U9) continue;
                    if (b.X1 < a.X0 || b.X0 > a.X1 || b.Y1 < a.Y0 || b.Y0 > a.Y1) continue;
                    Consider(result[i], a, b.X0, b.Y0);
                    Consider(result[i], a, b.X1, b.Y0);
                    Consider(result[i], a, b.X1, b.Y1);
                    Consider(result[i], a, b.X0, b.Y1);
                }
            }
            return result;
        }

        private static void Consider(List<Vector2> into, in QuadRect_U9 a, float x, float y)
        {
            bool onLeft = x == a.X0, onRight = x == a.X1, onBottom = y == a.Y0, onTop = y == a.Y1;
            bool insideX = x > a.X0 && x < a.X1, insideY = y > a.Y0 && y < a.Y1;
            if (!((onLeft || onRight) && insideY) && !((onBottom || onTop) && insideX)) return;
            foreach (var p in into) if (p.X == x && p.Y == y) return;
            into.Add(new Vector2(x, y));
        }

        public static List<Vector2> Perimeter_U9(in QuadRect_U9 q, int type, List<Vector2> splits)
        {
            var corners = new List<Vector2>();
            switch (type)
            {
                case 1: corners.Add(new Vector2(q.X0, q.Y0)); corners.Add(new Vector2(q.X1, q.Y0)); corners.Add(new Vector2(q.X0, q.Y1)); break;
                case 2: corners.Add(new Vector2(q.X0, q.Y0)); corners.Add(new Vector2(q.X1, q.Y1)); corners.Add(new Vector2(q.X0, q.Y1)); break;
                case 3: corners.Add(new Vector2(q.X1, q.Y0)); corners.Add(new Vector2(q.X1, q.Y1)); corners.Add(new Vector2(q.X0, q.Y1)); break;
                case 4: corners.Add(new Vector2(q.X0, q.Y0)); corners.Add(new Vector2(q.X1, q.Y0)); corners.Add(new Vector2(q.X1, q.Y1)); break;
                default: corners.Add(new Vector2(q.X0, q.Y0)); corners.Add(new Vector2(q.X1, q.Y0)); corners.Add(new Vector2(q.X1, q.Y1)); corners.Add(new Vector2(q.X0, q.Y1)); break;
            }

            var ring = new List<Vector2>();
            for (int i = 0; i < corners.Count; i++)
            {
                var c0 = corners[i];
                var c1 = corners[(i + 1) % corners.Count];
                ring.Add(c0);
                if (splits == null || splits.Count == 0) continue;
                bool axisEdge = c0.X == c1.X || c0.Y == c1.Y;
                if (!axisEdge) continue;
                var onEdge = new List<Vector2>();
                foreach (var s in splits)
                {
                    if (c0.X == c1.X && s.X == c0.X && s.Y > Math.Min(c0.Y, c1.Y) && s.Y < Math.Max(c0.Y, c1.Y)) onEdge.Add(s);
                    else if (c0.Y == c1.Y && s.Y == c0.Y && s.X > Math.Min(c0.X, c1.X) && s.X < Math.Max(c0.X, c1.X)) onEdge.Add(s);
                }
                onEdge.Sort((p, r) => (p - c0).LengthSquared().CompareTo((r - c0).LengthSquared()));
                ring.AddRange(onEdge);
            }
            return ring;
        }

        public static float CornerAlphaAt_U9(in QuadRect_U9 q, float a1, float a2, float a3, float a4, Vector2 p)
        {
            float w = Math.Max(q.X1 - q.X0, 1e-6f), h = Math.Max(q.Y1 - q.Y0, 1e-6f);
            float u = MathUtil.Clamp((p.X - q.X0) / w, 0f, 1f), v = MathUtil.Clamp((p.Y - q.Y0) / h, 0f, 1f);
            float bottom = a1 + (a2 - a1) * u;
            float top = a4 + (a3 - a4) * u;
            return bottom + (top - bottom) * v;
        }

        public static ushort[] FanIndices_U9(int ringCount)
        {
            if (ringCount < 3) return Array.Empty<ushort>();
            var idx = new ushort[ringCount * 3];
            int centre = ringCount;
            for (int i = 0; i < ringCount; i++)
            {
                idx[i * 3] = (ushort)centre;
                idx[i * 3 + 1] = (ushort)i;
                idx[i * 3 + 2] = (ushort)((i + 1) % ringCount);
            }
            return idx;
        }

        public const float GridCell_U9 = 64.0f;
        public const int GridMaxLines_U9 = 250;

        public static List<float> GridAxis_U9(float lo, float hi, IEnumerable<float> musts)
        {
            var set = new SortedSet<float> { lo, hi };
            if (musts != null) foreach (var m in musts) if (m > lo && m < hi) set.Add(m);
            float span = hi - lo;
            int cells = Math.Max(1, (int)Math.Ceiling(span / GridCell_U9));
            if (cells > GridMaxLines_U9 - 2) cells = GridMaxLines_U9 - 2;
            for (int k = 1; k < cells; k++) set.Add(lo + span * k / cells);
            var axis = new List<float>(set);
            while (axis.Count > GridMaxLines_U9)
            {
                int drop = -1;
                float best = float.MaxValue;
                for (int i = 1; i < axis.Count - 1; i++)
                {
                    float gap = axis[i + 1] - axis[i - 1];
                    if (gap < best) { best = gap; drop = i; }
                }
                if (drop < 0) break;
                axis.RemoveAt(drop);
            }
            return axis;
        }

        public static ushort[] GridIndices_U9(int nx, int ny)
        {
            if (nx < 2 || ny < 2) return Array.Empty<ushort>();
            var idx = new ushort[(nx - 1) * (ny - 1) * 6];
            int n = 0;
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int v0 = j * nx + i, v1 = v0 + 1, v2 = v0 + nx, v3 = v2 + 1;
                    idx[n++] = (ushort)v0; idx[n++] = (ushort)v2; idx[n++] = (ushort)v1;
                    idx[n++] = (ushort)v1; idx[n++] = (ushort)v2; idx[n++] = (ushort)v3;
                }
            return idx;
        }

        public static int SelfTestWeld_U9(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var big = new QuadRect_U9(0, 2, 0, 2, 0);
            var smallLow = new QuadRect_U9(2, 3, 0, 1, 0);
            var smallHigh = new QuadRect_U9(2, 3, 1, 2, 0);
            var far = new QuadRect_U9(10, 12, 10, 12, 0);
            var lifted = new QuadRect_U9(0, 1, 2, 3, 5);
            var splits = SplitPointsFor_U9(new[] { big, smallLow, smallHigh, far, lifted });

            Chk("u9 weld: two small quads on the big one's edge leave it one split point, at the T",
                splits[0].Count == 1 && splits[0][0] == new Vector2(2, 1),
                splits[0].Count + " split(s)" + (splits[0].Count > 0 ? " first " + splits[0][0] : ""));

            Chk("u9 weld: a quad at another height does not weld, and a far quad does not either",
                splits[3].Count == 0 && splits[4].Count == 0 &&
                !splits[0].Exists(p => p.Y == 2 && p.X == 1),
                $"far {splits[3].Count}, lifted {splits[4].Count}");

            var ring = Perimeter_U9(big, 0, splits[0]);
            Chk("u9 weld: the ring walks the four corners with the T inserted on the right edge",
                ring.Count == 5 && ring[1] == new Vector2(2, 0) && ring[2] == new Vector2(2, 1) && ring[3] == new Vector2(2, 2),
                string.Join(" ", ring));

            var fan = FanIndices_U9(ring.Count);
            Chk("u9 weld: a five-point ring fans into five triangles around a centre vertex",
                fan.Length == 15 && fan[0] == 5 && fan[14] == 0,
                fan.Length / 3 + " triangles");

            var tri = Perimeter_U9(big, 1, new List<Vector2> { new Vector2(1, 0), new Vector2(2, 1) });
            Chk("u9 weld: a cut-corner quad keeps three corners, takes splits on its edges and ignores the cut one",
                tri.Count == 4 && tri[0] == new Vector2(0, 0) && tri[1] == new Vector2(1, 0) &&
                tri[2] == new Vector2(2, 0) && tri[3] == new Vector2(0, 2),
                string.Join(" ", tri));

            var axis = GridAxis_U9(-3500f, -1904f, new[] { -2000f, -5000f });
            bool hasEnds = axis[0] == -3500f && axis[axis.Count - 1] == -1904f;
            bool hasSplit = axis.Contains(-2000f) && !axis.Contains(-5000f);
            bool smallCells = true;
            for (int i = 1; i < axis.Count; i++) if (axis[i] - axis[i - 1] > GridCell_U9 + 0.01f) smallCells = false;
            Chk("u9 grid: a 1.6 km edge becomes cells no wider than 64 m, keeping its ends and the T split",
                hasEnds && hasSplit && smallCells && axis.Count <= GridMaxLines_U9,
                $"{axis.Count} lines, widest gap {axis.Zip(axis.Skip(1), (p, r) => r - p).Max():0.#} m");

            var gi = GridIndices_U9(3, 2);
            Chk("u9 grid: a 3x2 lattice makes two cells, four triangles, and stays in range",
                gi.Length == 12 && gi.Max() == 5, gi.Length / 3 + " triangles");

            float mid = CornerAlphaAt_U9(big, 0f, 1f, 1f, 0f, new Vector2(1, 1));
            Chk("u9 weld: a split point's alpha is the blend of the corners it sits between",
                Math.Abs(mid - 0.5f) < 0.001f && Math.Abs(CornerAlphaAt_U9(big, 0f, 1f, 1f, 0f, new Vector2(2, 1)) - 1f) < 0.001f,
                $"centre {mid:0.##}, right edge {CornerAlphaAt_U9(big, 0f, 1f, 1f, 0f, new Vector2(2, 1)):0.##}");
            return fails;
        }
    }
}
