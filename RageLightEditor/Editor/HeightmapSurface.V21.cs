using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class HeightmapSurface_V21 : BasePathData
    {
        public bool ShowMax = true, ShowMin = true;

        private EditorVertex[] tris;
        public int TriangleCount => (tris?.Length ?? 0) / 3;
        public int CellCount { get; private set; }

        private static readonly Vector3 SunDir = Vector3.Normalize(new Vector3(0.45f, 0.35f, 0.82f));

        public EditorVertex[] GetTriangleVertices() => tris;
        public EditorVertex[] GetPathVertices() => null;
        public Vector4[] GetNodePositions() => null;

        public void Build(IEnumerable<HeightmapFile> files)
        {
            var vl = new List<EditorVertex>();
            CellCount = 0;
            foreach (var f in files)
            {
                if (f == null || f.Width < 2 || f.Height < 2) continue;
                CellCount += f.Width * f.Height;
                if (ShowMin) BuildLayer(f, f.MinHeights, vl, isMax: false);
                if (ShowMax) BuildLayer(f, f.MaxHeights, vl, isMax: true);
            }
            tris = vl.Count > 0 ? vl.ToArray() : null;
        }

        private static void BuildLayer(HeightmapFile f, byte[] heights, List<EditorVertex> vl, bool isMax)
        {
            if (heights == null || heights.Length < f.Width * f.Height) return;

            int w = f.Width, h = f.Height;
            var min = f.BBMin;
            var size = f.BBMax - f.BBMin;
            var step = new Vector3(size.X / (w - 1), size.Y / (h - 1), size.Z / 255.0f);

            byte lo = 255, hi = 0;
            for (int i = 0; i < w * h; i++) { var v = heights[i]; if (v < lo) lo = v; if (v > hi) hi = v; }
            float span = Math.Max(1, hi - lo);

            var cols = new uint[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int o = y * w + x;
                    int xm = Math.Max(0, x - 1), xp = Math.Min(w - 1, x + 1);
                    int ym = Math.Max(0, y - 1), yp = Math.Min(h - 1, y + 1);
                    float dzdx = (heights[y * w + xp] - heights[y * w + xm]) * step.Z / ((xp - xm) * step.X);
                    float dzdy = (heights[yp * w + x] - heights[ym * w + x]) * step.Z / ((yp - ym) * step.Y);
                    var n = Vector3.Normalize(new Vector3(-dzdx, -dzdy, 1.0f));
                    float diff = 0.35f + 0.65f * Math.Max(0, Vector3.Dot(n, SunDir));

                    float t = (heights[o] - lo) / span;
                    var c = Ramp(t) * diff;
                    if (!isMax) c = new Vector3(c.X * 0.45f, c.Y * 0.55f, c.Z * 0.75f);
                    cols[o] = (uint)new SharpDX.Color(
                        (byte)(Math.Min(1f, c.X) * 255), (byte)(Math.Min(1f, c.Y) * 255),
                        (byte)(Math.Min(1f, c.Z) * 255), (byte)(isMax ? 235 : 120)).ToRgba();
                }
            }

            float lift = isMax ? 0.0f : -0.75f;
            var v1 = new EditorVertex(); var v2 = new EditorVertex();
            var v3 = new EditorVertex(); var v4 = new EditorVertex();
            for (int y = 1; y < h; y++)
            {
                int yo = y - 1;
                for (int x = 1; x < w; x++)
                {
                    int xo = x - 1;
                    int o1 = yo * w + xo, o2 = yo * w + x, o3 = y * w + xo, o4 = y * w + x;
                    v1.Position = min + step * new Vector3(xo, yo, heights[o1]) + new Vector3(0, 0, lift);
                    v2.Position = min + step * new Vector3(x, yo, heights[o2]) + new Vector3(0, 0, lift);
                    v3.Position = min + step * new Vector3(xo, y, heights[o3]) + new Vector3(0, 0, lift);
                    v4.Position = min + step * new Vector3(x, y, heights[o4]) + new Vector3(0, 0, lift);
                    v1.Colour = cols[o1]; v2.Colour = cols[o2]; v3.Colour = cols[o3]; v4.Colour = cols[o4];
                    vl.Add(v1); vl.Add(v2); vl.Add(v3);
                    vl.Add(v3); vl.Add(v2); vl.Add(v4);
                }
            }
        }

        public static Vector3 Ramp(float t)
        {
            t = Math.Min(1, Math.Max(0, t));
            var stops = new[]
            {
                (0.00f, new Vector3(0.13f, 0.30f, 0.42f)),
                (0.12f, new Vector3(0.28f, 0.45f, 0.32f)),
                (0.35f, new Vector3(0.45f, 0.52f, 0.28f)),
                (0.60f, new Vector3(0.52f, 0.42f, 0.28f)),
                (0.82f, new Vector3(0.58f, 0.54f, 0.50f)),
                (1.00f, new Vector3(0.92f, 0.92f, 0.92f)),
            };
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].Item1) continue;
                var (t0, c0) = stops[i - 1];
                var (t1, c1) = stops[i];
                float k = (t - t0) / Math.Max(1e-5f, t1 - t0);
                return Vector3.Lerp(c0, c1, k);
            }
            return stops[stops.Length - 1].Item2;
        }
    }
}

