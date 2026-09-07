using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class MloShellAnalysis
    {
        public struct Tri { public Vector3 A, B, C; }

        public sealed class RoomHit
        {
            public BoundingBox Box;
            public int Voxels;
            public float Volume => (Box.Maximum.X - Box.Minimum.X) * (Box.Maximum.Y - Box.Minimum.Y) * (Box.Maximum.Z - Box.Minimum.Z);
        }

        public sealed class PortalHit
        {
            public int RoomA, RoomB = -1;
            public Vector3[] Corners;
            public float Area;
            public int Axis;
            public bool ToOutside => RoomB < 0;
            internal float Plane, U0, U1, V0, V1;
        }

        public readonly List<RoomHit> Rooms = new List<RoomHit>();
        public readonly List<PortalHit> Portals = new List<PortalHit>();
        public BoundingBox Bounds;
        public float VoxelSize;
        public int NX, NY, NZ;
        public int TriangleCount, SolidVoxels, InsideVoxels, SeedVoxels;
        public float RoomCoreUsed;
        public float Dominance;
        public int AutoPasses = 1;
        public double Seconds;
        public string Problem = "";

        public sealed class Options
        {
            public float Voxel = 0.25f;
            public int VoxelBudget = 7_000_000;
            public float RoomCore = 0.75f;
            public float MinRoomVolume = 3.0f;
            public int MaxRooms = 24;
            public bool AutoSplit = true;
            public int MaxAutoPasses = 4;
            public float MinPortalArea = 0.35f;
            public float MinOutsidePortalArea = 1.0f;
            public int MaxPortals = 64;
        }

        public static MloShellAnalysis Run(IReadOnlyList<Tri> tris, Options opt = null, Action<string> progress = null)
        {
            opt = opt ?? new Options();
            var r = new MloShellAnalysis();
            var sw = Stopwatch.StartNew();
            try { r.Build(tris, opt, progress); }
            catch (Exception ex) { r.Problem = ex.Message; }
            r.Seconds = sw.Elapsed.TotalSeconds;
            return r;
        }

        private Vector3 origin;
        private byte[] solid;
        private int[] label;
        private int Idx(int x, int y, int z) => x + NX * (y + NY * z);

        private void Build(IReadOnlyList<Tri> tris, Options opt, Action<string> progress)
        {
            TriangleCount = tris?.Count ?? 0;
            if (TriangleCount < 4) { Problem = "the shell has almost no geometry"; return; }

            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            for (int i = 0; i < TriangleCount; i++)
            {
                var t = tris[i];
                mn = Vector3.Min(mn, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
                mx = Vector3.Max(mx, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
            }
            var size = mx - mn;
            if (!(size.X > 0.1f && size.Y > 0.1f && size.Z > 0.1f)) { Problem = "the shell is flat - there is no volume to divide"; return; }
            Bounds = new BoundingBox(mn, mx);

            float vs = Math.Max(opt.Voxel, 0.02f);
            for (int guard = 0; guard < 12; guard++)
            {
                double n = ((double)size.X / vs + 4) * ((double)size.Y / vs + 4) * ((double)size.Z / vs + 4);
                if (n <= opt.VoxelBudget) break;
                vs *= (float)Math.Max(1.05, Math.Pow(n / opt.VoxelBudget, 1.0 / 3.0));
            }
            VoxelSize = vs;
            NX = Math.Max(3, (int)Math.Ceiling(size.X / vs) + 5);
            NY = Math.Max(3, (int)Math.Ceiling(size.Y / vs) + 5);
            NZ = Math.Max(3, (int)Math.Ceiling(size.Z / vs) + 5);
            origin = mn - new Vector3(2.31f * vs);
            long total = (long)NX * NY * NZ;
            if (total > int.MaxValue / 4) { Problem = "the shell is too big to analyse"; return; }
            progress?.Invoke($"voxelising {TriangleCount} triangles at {vs:0.00} m ({NX}x{NY}x{NZ})...");

            solid = new byte[total];
            Voxelise(tris, vs);
            progress?.Invoke("finding the enclosed volume...");
            var inside = Enclosure();
            if (InsideVoxels == 0)
            {
                Problem = "no enclosed volume: the shell has no space with a floor, a ceiling and walls round it";
                return;
            }
            progress?.Invoke("splitting it into rooms...");
            var dist = Distance(inside);
            AutoSegment(inside, dist, opt, vs, progress);
            if (Rooms.Count == 0) { Problem = "the enclosed volume is smaller than one room"; return; }
            progress?.Invoke("finding the doorways...");
            FindPortals(inside, opt, vs);
        }

        private void Voxelise(IReadOnlyList<Tri> tris, float vs)
        {
            float inv = 1.0f / vs;
            var half = new Vector3(vs * 0.5f);
            var ax = new Vector3[9];
            var lo = new float[9]; var hi = new float[9]; var rad = new float[9];
            var edges = new Vector3[3];
            var units = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                var tmin = Vector3.Min(t.A, Vector3.Min(t.B, t.C));
                var tmax = Vector3.Max(t.A, Vector3.Max(t.B, t.C));
                int x0 = Clamp((int)Math.Floor((tmin.X - origin.X) * inv) - 1, 0, NX - 1);
                int x1 = Clamp((int)Math.Floor((tmax.X - origin.X) * inv) + 1, 0, NX - 1);
                int y0 = Clamp((int)Math.Floor((tmin.Y - origin.Y) * inv) - 1, 0, NY - 1);
                int y1 = Clamp((int)Math.Floor((tmax.Y - origin.Y) * inv) + 1, 0, NY - 1);
                int z0 = Clamp((int)Math.Floor((tmin.Z - origin.Z) * inv) - 1, 0, NZ - 1);
                int z1 = Clamp((int)Math.Floor((tmax.Z - origin.Z) * inv) + 1, 0, NZ - 1);

                var e0 = t.B - t.A; var e1 = t.C - t.B; var e2 = t.A - t.C;
                var nrm = Vector3.Cross(e0, -e2);
                if (nrm.LengthSquared() < 1e-16f) continue;
                float nd = Vector3.Dot(nrm, t.A);
                float nrad = half.X * Math.Abs(nrm.X) + half.Y * Math.Abs(nrm.Y) + half.Z * Math.Abs(nrm.Z);
                int na = 0;
                edges[0] = e0; edges[1] = e1; edges[2] = e2;
                foreach (var e in edges)
                    foreach (var u in units)
                    {
                        var a = Vector3.Cross(u, e);
                        if (a.LengthSquared() < 1e-14f) { a = Vector3.UnitX; }
                        ax[na] = a;
                        float p0 = Vector3.Dot(a, t.A), p1 = Vector3.Dot(a, t.B), p2 = Vector3.Dot(a, t.C);
                        lo[na] = Math.Min(p0, Math.Min(p1, p2));
                        hi[na] = Math.Max(p0, Math.Max(p1, p2));
                        rad[na] = half.X * Math.Abs(a.X) + half.Y * Math.Abs(a.Y) + half.Z * Math.Abs(a.Z);
                        na++;
                    }

                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                    {
                        int row = NX * (y + NY * z);
                        for (int x = x0; x <= x1; x++)
                        {
                            int idx = row + x;
                            if (solid[idx] != 0) continue;
                            var c = new Vector3(origin.X + (x + 0.5f) * vs, origin.Y + (y + 0.5f) * vs, origin.Z + (z + 0.5f) * vs);
                            if (Math.Abs(Vector3.Dot(nrm, c) - nd) > nrad) continue;
                            bool sep = false;
                            for (int k = 0; k < 9; k++)
                            {
                                float d = Vector3.Dot(ax[k], c);
                                if (lo[k] > d + rad[k] || hi[k] < d - rad[k]) { sep = true; break; }
                            }
                            if (!sep) { solid[idx] = 1; SolidVoxels++; }
                        }
                    }
            }
        }

        private static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);

        private bool[] Enclosure()
        {
            long n = solid.LongLength;
            var enc = new byte[n];
            const byte NX_ = 1, PX = 2, NY_ = 4, PY = 8, NZ_ = 16, PZ = 32;

            for (int z = 0; z < NZ; z++)
                for (int y = 0; y < NY; y++)
                {
                    int row = NX * (y + NY * z);
                    bool seen = false;
                    for (int x = 0; x < NX; x++) { int i = row + x; if (seen) enc[i] |= NX_; if (solid[i] != 0) seen = true; }
                    seen = false;
                    for (int x = NX - 1; x >= 0; x--) { int i = row + x; if (seen) enc[i] |= PX; if (solid[i] != 0) seen = true; }
                }
            for (int z = 0; z < NZ; z++)
                for (int x = 0; x < NX; x++)
                {
                    bool seen = false;
                    for (int y = 0; y < NY; y++) { int i = Idx(x, y, z); if (seen) enc[i] |= NY_; if (solid[i] != 0) seen = true; }
                    seen = false;
                    for (int y = NY - 1; y >= 0; y--) { int i = Idx(x, y, z); if (seen) enc[i] |= PY; if (solid[i] != 0) seen = true; }
                }
            for (int y = 0; y < NY; y++)
                for (int x = 0; x < NX; x++)
                {
                    bool seen = false;
                    for (int z = 0; z < NZ; z++) { int i = Idx(x, y, z); if (seen) enc[i] |= NZ_; if (solid[i] != 0) seen = true; }
                    seen = false;
                    for (int z = NZ - 1; z >= 0; z--) { int i = Idx(x, y, z); if (seen) enc[i] |= PZ; if (solid[i] != 0) seen = true; }
                }

            var inside = new bool[n];
            for (long i = 0; i < n; i++)
            {
                if (solid[i] != 0) continue;
                byte e = enc[i];
                if ((e & NZ_) == 0 || (e & PZ) == 0) continue;
                int h = ((e & NX_) != 0 ? 1 : 0) + ((e & PX) != 0 ? 1 : 0) + ((e & NY_) != 0 ? 1 : 0) + ((e & PY) != 0 ? 1 : 0);
                if (h < 3) continue;
                inside[i] = true; InsideVoxels++;
            }
            return inside;
        }

        private int[] Distance(bool[] inside)
        {
            int n = inside.Length;
            var dist = new int[n];
            var queue = new int[n];
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++)
            {
                if (inside[i]) { dist[i] = int.MaxValue; continue; }
                dist[i] = 0; queue[tail++] = i;
            }
            int planeStride = NX * NY;
            while (head < tail)
            {
                int i = queue[head++];
                int d = dist[i] + 1;
                int x = i % NX, y = (i / NX) % NY, z = i / planeStride;
                if (x > 0) Push(i - 1);
                if (x < NX - 1) Push(i + 1);
                if (y > 0) Push(i - NX);
                if (y < NY - 1) Push(i + NX);
                if (z > 0) Push(i - planeStride);
                if (z < NZ - 1) Push(i + planeStride);
                void Push(int j) { if (dist[j] > d) { dist[j] = d; queue[tail++] = j; } }
            }
            return dist;
        }

        private void AutoSegment(bool[] inside, int[] dist, Options opt, float vs, Action<string> progress)
        {
            float core = opt.RoomCore;
            List<RoomHit> bestRooms = null; int[] bestLabel = null; float bestDom = 2.0f; float bestCore = core; int passes = 0;
            for (int pass = 0; pass < Math.Max(1, opt.MaxAutoPasses); pass++)
            {
                Rooms.Clear();
                Segment(inside, dist, opt, vs, core);
                passes = pass + 1;
                float dom = 0;
                foreach (var r in Rooms) dom = Math.Max(dom, InsideVoxels > 0 ? (float)r.Voxels / InsideVoxels : 1.0f);
                bool sane = Rooms.Count > 0 && Rooms.Count < opt.MaxRooms;
                if (sane && dom < bestDom) { bestDom = dom; bestRooms = new List<RoomHit>(Rooms); bestLabel = (int[])label.Clone(); bestCore = core; }
                if (!opt.AutoSplit || !sane || dom <= 0.5f) break;
                core *= 1.7f;
                progress?.Invoke($"one space held {100 * dom:0}% of the interior - trying a {2 * core:0.0} m doorway rule...");
            }
            if (bestRooms != null && bestLabel != null)
            {
                Rooms.Clear(); Rooms.AddRange(bestRooms); label = bestLabel;
            }
            RoomCoreUsed = bestCore; Dominance = bestDom > 1.0f ? 1.0f : bestDom; AutoPasses = passes;
        }

        private void Segment(bool[] inside, int[] dist, Options opt, float vs, float roomCore)
        {
            int n = inside.Length;
            SeedVoxels = 0;
            int seedD = Math.Max(2, (int)Math.Round(roomCore / vs));
            label = new int[n];
            for (int i = 0; i < n; i++) label[i] = inside[i] ? 0 : -1;

            var cores = new List<List<int>>();
            var seen = new bool[n];
            var stack = new List<int>(1024);
            int planeStride = NX * NY;
            for (int i = 0; i < n; i++)
            {
                if (!inside[i] || seen[i] || dist[i] < seedD) continue;
                stack.Clear(); stack.Add(i); seen[i] = true;
                var comp = new List<int>();
                while (stack.Count > 0)
                {
                    int c = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                    comp.Add(c);
                    int x = c % NX, y = (c / NX) % NY, z = c / planeStride;
                    if (x > 0) Try(c - 1); if (x < NX - 1) Try(c + 1);
                    if (y > 0) Try(c - NX); if (y < NY - 1) Try(c + NX);
                    if (z > 0) Try(c - planeStride); if (z < NZ - 1) Try(c + planeStride);
                    void Try(int j) { if (!seen[j] && inside[j] && dist[j] >= seedD) { seen[j] = true; stack.Add(j); } }
                }
                cores.Add(comp);
                SeedVoxels += comp.Count;
            }

            if (cores.Count == 0)
            {
                var all = new List<int>();
                for (int i = 0; i < n; i++) if (inside[i]) all.Add(i);
                cores.Add(all);
            }

            cores.Sort((a, b) => b.Count.CompareTo(a.Count));
            float voxelVol = vs * vs * vs;
            var kept = new List<List<int>>();
            foreach (var c in cores)
            {
                if (kept.Count >= opt.MaxRooms) break;
                if (c.Count * voxelVol < opt.MinRoomVolume * 0.05f && kept.Count > 0) continue;
                kept.Add(c);
            }

            var queue = new int[n];
            int head = 0, tail = 0;
            for (int k = 0; k < kept.Count; k++)
                foreach (var i in kept[k]) { label[i] = k + 1; queue[tail++] = i; }
            while (head < tail)
            {
                int i = queue[head++];
                int lab = label[i];
                int x = i % NX, y = (i / NX) % NY, z = i / planeStride;
                if (x > 0) Grow(i - 1); if (x < NX - 1) Grow(i + 1);
                if (y > 0) Grow(i - NX); if (y < NY - 1) Grow(i + NX);
                if (z > 0) Grow(i - planeStride); if (z < NZ - 1) Grow(i + planeStride);
                void Grow(int j) { if (label[j] == 0) { label[j] = lab; queue[tail++] = j; } }
            }

            var mins = new Vector3[kept.Count]; var maxs = new Vector3[kept.Count]; var counts = new int[kept.Count];
            for (int k = 0; k < kept.Count; k++) { mins[k] = new Vector3(float.MaxValue); maxs[k] = new Vector3(float.MinValue); }
            for (int i = 0; i < n; i++)
            {
                int k = label[i] - 1;
                if (k < 0) continue;
                int x = i % NX, y = (i / NX) % NY, z = i / planeStride;
                var a = new Vector3(origin.X + x * vs, origin.Y + y * vs, origin.Z + z * vs);
                mins[k] = Vector3.Min(mins[k], a);
                maxs[k] = Vector3.Max(maxs[k], a + new Vector3(vs));
                counts[k]++;
            }
            var remap = new int[kept.Count + 1];
            for (int k = 0; k < kept.Count; k++)
            {
                var box = new BoundingBox(mins[k] - new Vector3(vs), maxs[k] + new Vector3(vs));
                var hit = new RoomHit { Box = box, Voxels = counts[k] };
                if (counts[k] * voxelVol < opt.MinRoomVolume) { remap[k + 1] = 0; continue; }
                Rooms.Add(hit);
                remap[k + 1] = Rooms.Count;
            }
            for (int i = 0; i < n; i++) if (label[i] > 0) label[i] = remap[label[i]];
        }

        private sealed class Face
        {
            public int A, B;
            public int Axis;
            public Vector3 Centre;
            public int X, Y, Z;
        }

        private void FindPortals(bool[] inside, Options opt, float vs)
        {
            int planeStride = NX * NY;
            var faces = new List<Face>();
            for (int z = 0; z < NZ; z++)
                for (int y = 0; y < NY; y++)
                    for (int x = 0; x < NX; x++)
                    {
                        int i = Idx(x, y, z);
                        if (solid[i] != 0) continue;
                        int a = label[i];
                        if (x < NX - 1) Look(i, i + 1, 0, x, y, z);
                        if (y < NY - 1) Look(i, i + NX, 1, x, y, z);
                        if (z < NZ - 1) Look(i, i + planeStride, 2, x, y, z);

                        void Look(int ii, int j, int axis, int cx, int cy, int cz)
                        {
                            if (solid[j] != 0) return;
                            int b = label[j];
                            if (a == b) return;
                            int ra, rb;
                            if (a > 0 && b > 0) { ra = Math.Min(a, b); rb = Math.Max(a, b); }
                            else if (a > 0) { if (inside[j]) return; ra = a; rb = -1; }
                            else if (b > 0) { if (inside[ii]) return; ra = b; rb = -1; }
                            else return;
                            faces.Add(new Face
                            {
                                A = ra, B = rb, Axis = axis,
                                Centre = new Vector3(origin.X + (cx + 0.5f + (axis == 0 ? 0.5f : 0)) * vs,
                                                     origin.Y + (cy + 0.5f + (axis == 1 ? 0.5f : 0)) * vs,
                                                     origin.Z + (cz + 0.5f + (axis == 2 ? 0.5f : 0)) * vs),
                                X = cx, Y = cy, Z = cz,
                            });
                        }
                    }
            if (faces.Count == 0) return;

            var byPair = new Dictionary<long, List<Face>>();
            foreach (var f in faces)
            {
                long key = (long)f.A * 4096 + (f.B + 1);
                if (!byPair.TryGetValue(key, out var list)) byPair[key] = list = new List<Face>();
                list.Add(f);
            }
            var hits = new List<PortalHit>();
            foreach (var kv in byPair)
            {
                var raw = new List<PortalHit>();
                foreach (var cluster in Cluster(kv.Value))
                {
                    var h = QuadFromCluster(cluster, vs);
                    if (h != null) raw.Add(h);
                }
                foreach (var h in MergePanes(raw, vs * 3.0f))
                {
                    float minArea = h.ToOutside ? opt.MinOutsidePortalArea : opt.MinPortalArea;
                    if (h.Area < minArea) continue;
                    hits.Add(h);
                }
            }
            hits.Sort((a, b) => b.Area.CompareTo(a.Area));
            for (int i = 0; i < hits.Count && i < opt.MaxPortals; i++) Portals.Add(hits[i]);
        }

        private static List<List<Face>> Cluster(List<Face> list)
        {
            var byCell = new Dictionary<long, int>();
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                byCell[Key(f.X, f.Y, f.Z, f.Axis)] = i;
            }
            var seen = new bool[list.Count];
            var outp = new List<List<Face>>();
            var stack = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (seen[i]) continue;
                seen[i] = true; stack.Clear(); stack.Add(i);
                var comp = new List<Face>();
                while (stack.Count > 0)
                {
                    int c = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                    var f = list[c];
                    comp.Add(f);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                                for (int ax = 0; ax < 3; ax++)
                                {
                                    if (dx == 0 && dy == 0 && dz == 0 && ax == f.Axis) continue;
                                    if (byCell.TryGetValue(Key(f.X + dx, f.Y + dy, f.Z + dz, ax), out int j) && !seen[j])
                                    { seen[j] = true; stack.Add(j); }
                                }
                }
                outp.Add(comp);
            }
            return outp;
        }

        private static long Key(int x, int y, int z, int axis) =>
            (((long)(x + 1) * 4096 + (y + 1)) * 4096 + (z + 1)) * 4 + axis;

        private static PortalHit QuadFromCluster(List<Face> cluster, float vs)
        {
            if (cluster == null || cluster.Count == 0) return null;
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            var sum = Vector3.Zero;
            foreach (var f in cluster) { mn = Vector3.Min(mn, f.Centre); mx = Vector3.Max(mx, f.Centre); sum += f.Centre; }
            var centre = sum / cluster.Count;
            var ext = mx - mn;
            int axis = ext.X <= ext.Y && ext.X <= ext.Z ? 0 : (ext.Y <= ext.Z ? 1 : 2);
            var lo = mn - new Vector3(vs * 0.5f); var hi = mx + new Vector3(vs * 0.5f);
            float u0, u1, v0, v1;
            Vector3 C(float u, float v)
            {
                switch (axis)
                {
                    case 0: return new Vector3(centre.X, u, v);
                    case 1: return new Vector3(u, centre.Y, v);
                    default: return new Vector3(u, v, centre.Z);
                }
            }
            switch (axis)
            {
                case 0: u0 = lo.Y; u1 = hi.Y; v0 = lo.Z; v1 = hi.Z; break;
                case 1: u0 = lo.X; u1 = hi.X; v0 = lo.Z; v1 = hi.Z; break;
                default: u0 = lo.X; u1 = hi.X; v0 = lo.Y; v1 = hi.Y; break;
            }
            float area = (u1 - u0) * (v1 - v0);
            if (!(area > 0)) return null;
            var f0 = cluster[0];
            float plane = axis == 0 ? centre.X : (axis == 1 ? centre.Y : centre.Z);
            return new PortalHit
            {
                RoomA = f0.A, RoomB = f0.B, Axis = axis, Area = area,
                Plane = plane, U0 = u0, U1 = u1, V0 = v0, V1 = v1,
                Corners = new[] { C(u0, v0), C(u0, v1), C(u1, v1), C(u1, v0) },
            };
        }

        private static List<PortalHit> MergePanes(List<PortalHit> list, float tol)
        {
            bool merged = true;
            while (merged && list.Count > 1)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                    for (int j = i + 1; j < list.Count && !merged; j++)
                    {
                        var a = list[i]; var b = list[j];
                        if (a.Axis != b.Axis || Math.Abs(a.Plane - b.Plane) > tol) continue;
                        if (a.U0 > b.U1 + tol || b.U0 > a.U1 + tol) continue;
                        if (a.V0 > b.V1 + tol || b.V0 > a.V1 + tol) continue;
                        a.U0 = Math.Min(a.U0, b.U0); a.U1 = Math.Max(a.U1, b.U1);
                        a.V0 = Math.Min(a.V0, b.V0); a.V1 = Math.Max(a.V1, b.V1);
                        a.Plane = (a.Plane + b.Plane) * 0.5f;
                        list.RemoveAt(j);
                        merged = true;
                    }
            }
            foreach (var h in list)
            {
                h.Area = (h.U1 - h.U0) * (h.V1 - h.V0);
                Vector3 C(float u, float v)
                {
                    switch (h.Axis)
                    {
                        case 0: return new Vector3(h.Plane, u, v);
                        case 1: return new Vector3(u, h.Plane, v);
                        default: return new Vector3(u, v, h.Plane);
                    }
                }
                h.Corners = new[] { C(h.U0, h.V0), C(h.U0, h.V1), C(h.U1, h.V1), C(h.U1, h.V0) };
            }
            return list;
        }

        public string Describe()
        {
            if (!string.IsNullOrEmpty(Problem)) return Problem;
            int outs = 0;
            foreach (var p in Portals) if (p.ToOutside) outs++;
            return $"{Rooms.Count} room{(Rooms.Count == 1 ? "" : "s")}, {Portals.Count - outs} doorway{(Portals.Count - outs == 1 ? "" : "s")} " +
                   $"and {outs} opening{(outs == 1 ? "" : "s")} to the outside, from {TriangleCount:n0} triangles at {VoxelSize:0.00} m " +
                   $"with a {2 * RoomCoreUsed:0.0} m doorway rule ({Seconds:0.0}s)";
        }
    }
}

