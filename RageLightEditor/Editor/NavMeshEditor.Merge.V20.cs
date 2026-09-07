using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {
        public const float MergeFlat_V20 = 0.05f;

        public struct Block_V20
        {
            public int X, Y, W, H;
            public float Z;
            public bool Flat;
        }

        public static List<Block_V20> MergeCells_V20(bool[] ok, float[] zs, int nx, int ny)
        {
            var outp = new List<Block_V20>();
            if (ok == null || zs == null || nx < 1 || ny < 1) return outp;
            int gw = nx + 1;
            var used = new bool[nx * ny];

            float Corner(int cx, int cy) => zs[cy * gw + cx];

            bool CellFlat(int cx, int cy, out float z)
            {
                float a = Corner(cx, cy), b = Corner(cx + 1, cy);
                float c = Corner(cx, cy + 1), d = Corner(cx + 1, cy + 1);
                float mn = Math.Min(Math.Min(a, b), Math.Min(c, d));
                float mx = Math.Max(Math.Max(a, b), Math.Max(c, d));
                z = (mn + mx) * 0.5f;
                return (mx - mn) <= MergeFlat_V20;
            }

            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int k = y * nx + x;
                    if (used[k] || !ok[k]) continue;

                    if (!CellFlat(x, y, out float z0))
                    {
                        used[k] = true;
                        outp.Add(new Block_V20 { X = x, Y = y, W = 1, H = 1, Z = z0, Flat = false });
                        continue;
                    }

                    int w = 1;
                    while (x + w < nx)
                    {
                        int kk = y * nx + (x + w);
                        if (used[kk] || !ok[kk]) break;
                        if (!CellFlat(x + w, y, out float z1) || Math.Abs(z1 - z0) > MergeFlat_V20) break;
                        w++;
                    }

                    int h = 1;
                    while (y + h < ny)
                    {
                        bool rowOk = true;
                        for (int i = 0; i < w && rowOk; i++)
                        {
                            int kk = (y + h) * nx + (x + i);
                            if (used[kk] || !ok[kk]) { rowOk = false; break; }
                            if (!CellFlat(x + i, y + h, out float z1) || Math.Abs(z1 - z0) > MergeFlat_V20) rowOk = false;
                        }
                        if (!rowOk) break;
                        h++;
                    }

                    for (int j = 0; j < h; j++)
                        for (int i = 0; i < w; i++)
                            used[(y + j) * nx + (x + i)] = true;

                    outp.Add(new Block_V20 { X = x, Y = y, W = w, H = h, Z = z0, Flat = true });
                }

            return outp;
        }

        public static Vector3[] BlockQuad_V20(in Block_V20 b, Vector3 origin, float spacing, float[] zs, int nx, float lift)
        {
            int gw = nx + 1;
            float x0 = origin.X + b.X * spacing, x1 = origin.X + (b.X + b.W) * spacing;
            float y0 = origin.Y + b.Y * spacing, y1 = origin.Y + (b.Y + b.H) * spacing;
            if (b.Flat)
                return new[]
                {
                    new Vector3(x0, y0, b.Z + lift), new Vector3(x1, y0, b.Z + lift),
                    new Vector3(x1, y1, b.Z + lift), new Vector3(x0, y1, b.Z + lift),
                };
            float c00 = zs[b.Y * gw + b.X], c10 = zs[b.Y * gw + b.X + 1];
            float c11 = zs[(b.Y + 1) * gw + b.X + 1], c01 = zs[(b.Y + 1) * gw + b.X];
            return new[]
            {
                new Vector3(x0, y0, c00 + lift), new Vector3(x1, y0, c10 + lift),
                new Vector3(x1, y1, c11 + lift), new Vector3(x0, y1, c01 + lift),
            };
        }

        public static int MergeSelfTest_V20(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            int n = 8, gw = n + 1;
            var ok8 = new bool[n * n];
            var z8 = new float[gw * gw];
            for (int i = 0; i < ok8.Length; i++) ok8[i] = true;
            var blocks = MergeCells_V20(ok8, z8, n, n);
            Chk("v20 nav: a flat 8x8 field merges into one polygon",
                blocks.Count == 1 && blocks[0].W == 8 && blocks[0].H == 8,
                blocks.Count + " block(s)" + (blocks.Count > 0 ? $", first {blocks[0].W}x{blocks[0].H}" : ""));

            var okHole = new bool[n * n];
            for (int i = 0; i < okHole.Length; i++) okHole[i] = true;
            okHole[3 * n + 3] = false;
            var bh = MergeCells_V20(okHole, z8, n, n);
            Chk("v20 nav: a hole splits the field without shattering it",
                bh.Count > 1 && bh.Count <= 6, bh.Count + " block(s) for 63 cells");

            var z2 = new float[gw * gw];
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                    z2[y * gw + x] = y >= 4 ? 3.0f : 0.0f;
            var b2 = MergeCells_V20(ok8, z2, n, n);
            bool spans = false;
            foreach (var b in b2) if (b.Y < 3 && b.Y + b.H > 4) spans = true;
            Chk("v20 nav: two levels never merge into one polygon through the step",
                !spans, b2.Count + " block(s), none spanning the step");

            var zSlope = new float[gw * gw];
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                    zSlope[y * gw + x] = y * 1.0f;
            var bs = MergeCells_V20(ok8, zSlope, n, n);
            bool allSingle = true;
            foreach (var b in bs) if (b.Flat) allSingle = false;
            Chk("v20 nav: a slope is not flattened - every cell keeps its own quad",
                allSingle && bs.Count == n * n, bs.Count + " block(s), none flat");

            Chk("v20 nav: merging is what keeps a big area inside the format's 16,383 polys",
                blocks.Count < n * n, $"{n * n} cells -> {blocks.Count} polygon(s)");
            return fails;
        }
    }
}

