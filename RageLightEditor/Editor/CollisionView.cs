using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class CollisionMesh
    {
        public string Name = "";

        public LineVertex[] Vertices = Array.Empty<LineVertex>();

        public byte[] TriMaterials = Array.Empty<byte>();

        public BoundMaterial_s[] TriMaterialData;

        public byte[] MaterialsUsed = Array.Empty<byte>();

        public BoundingBox Bounds;

        public int PrimitiveCount;

        public SharpDX.Direct3D11.Buffer VB;
        public int VBCount;
        public void ReleaseVB() { VB?.Dispose(); VB = null; VBCount = 0; }

        public int TriangleCount => Vertices.Length / 3;
        public bool IsEmpty => Vertices.Length == 0;

        public string MaterialName(int tri)
        {
            if (TriMaterials == null || tri < 0 || tri >= TriMaterials.Length) return string.Empty;
            return BoundsMaterialTypes.GetMaterialName(TriMaterials[tri]);
        }

        public bool RayHit(ref Ray ray, out float dist, out int tri)
        {
            dist = float.MaxValue;
            tri = -1;
            var v = Vertices;
            if (v == null) return false;
            for (int i = 0; i + 2 < v.Length; i += 3)
            {
                var a = v[i].Position;
                var b = v[i + 1].Position;
                var c = v[i + 2].Position;
                if (!ray.Intersects(ref a, ref b, ref c, out float t)) continue;
                if (t >= dist) continue;
                dist = t;
                tri = i / 3;
            }
            return tri >= 0;
        }
    }

    public partial class CollisionView : IDisposable
    {
        private readonly GameFileManager files;

        public CollisionView(GameFileManager fileManager)
        {
            files = fileManager;
        }

        private float alpha = 0.55f;
        private float shading = 0.55f;
        private bool storeMaterialData;

        public float Alpha
        {
            get => alpha;
            set { value = MathUtil.Clamp(value, 0.0f, 1.0f); if (value != alpha) { alpha = value; Clear(); } }
        }

        public float Shading
        {
            get => shading;
            set { value = MathUtil.Clamp(value, 0.0f, 1.0f); if (value != shading) { shading = value; Clear(); } }
        }

        public bool StoreMaterialData
        {
            get => storeMaterialData;
            set { if (value != storeMaterialData) { storeMaterialData = value; Clear(); } }
        }

        public Func<BoundMaterial_s, Vector4> ColourSelector;

        public int SphereSegments = 10;
        public int SphereRings = 6;
        public int TubeSegments = 10;

        private sealed class Entry
        {
            public CollisionMesh Mesh;
            public bool Missing;
            public volatile bool Queued;
            public int LastUsed;
        }

        private readonly Dictionary<uint, Entry> entries = new Dictionary<uint, Entry>();
        private readonly object sync = new object();
        private int tick;
        private int frameTick;
        private long cachedTris;

        public long MaxCachedTriangles = 3_000_000;

        public void BeginFrame() { lock (sync) frameTick = tick; }

        public readonly ConcurrentQueue<CollisionMesh> Retired = new ConcurrentQueue<CollisionMesh>();
        public long RetiredCount;

        public double ReadMsTotal, BuildMsTotal, ReadMsMax, BuildMsMax;
        public int Built;
        public string SlowestFile = "";

        public int CachedMeshes { get { lock (sync) return entries.Count; } }
        public long CachedTriangles { get { lock (sync) return cachedTris; } }
        public int LoadedCount { get; private set; }
        public double AverageTriangles { get { lock (sync) return LoadedCount > 0 ? (double)cachedTris / LoadedCount : 40000.0; } }
        public int LoadsPending => queue.Count;
        public string Status
        {
            get
            {
                int loaded = 0, missing = 0, queued = 0, empty = 0;
                lock (sync) foreach (var e in entries.Values) { if (e.Mesh != null) { loaded++; if (e.Mesh.IsEmpty) empty++; } else if (e.Missing) missing++; else if (e.Queued) queued++; }
                return $"entries {entries.Count}: loaded {loaded} (empty {empty}) missing {missing} queued {queued} lastError {LastError}";
            }
        }
        public string LastError = "";

        public CollisionMesh Get(uint ybnHash)
        {
            if (ybnHash == 0) return null;
            var cache = files?.Cache;
            if (cache == null || !files.Ready) return null;

            Entry e;
            YbnFile ybn = null;
            lock (sync)
            {
                if (!entries.TryGetValue(ybnHash, out e))
                {
                    e = new Entry();
                    entries[ybnHash] = e;
                }
                e.LastUsed = ++tick;
                if (e.Mesh != null || e.Missing) return e.Mesh;
                if (e.Queued) return null;

                ybn = ProjectYbn_V25(ybnHash);
                if (ybn == null)
                {
                    try { ybn = cache.GetYbn(ybnHash); }
                    catch { e.Missing = true; return null; }
                }
                if (ybn == null) { e.Missing = true; return null; }

                e.Queued = true;
            }

            StartLoader();
            try { queue.Add(new Request { Hash = ybnHash, Entry = e, Ybn = ybn }); }
            catch { lock (sync) { e.Queued = false; e.Missing = true; } }
            return null;
        }

        public CollisionMesh GetImmediate(uint ybnHash)
        {
            if (ybnHash == 0) return null;
            var cache = files?.Cache;
            if (cache == null || !files.Ready) return null;

            Entry e;
            YbnFile ybn;
            lock (sync)
            {
                if (!entries.TryGetValue(ybnHash, out e))
                {
                    e = new Entry();
                    entries[ybnHash] = e;
                }
                e.LastUsed = ++tick;
                if (e.Mesh != null || e.Missing) return e.Mesh;

                ybn = ProjectYbn_V25(ybnHash);
                if (ybn == null)
                {
                    try { ybn = cache.GetYbn(ybnHash); }
                    catch { e.Missing = true; return null; }
                }
                if (ybn == null) { e.Missing = true; return null; }
            }

            var mesh = LoadAndBuild(ybn);
            Publish(ybnHash, e, mesh);
            return mesh;
        }

        public CollisionMesh Get(string ybnName)
        {
            if (string.IsNullOrWhiteSpace(ybnName)) return null;
            return Get(HashOf(ybnName));
        }

        public CollisionMesh GetImmediate(string ybnName)
        {
            if (string.IsNullOrWhiteSpace(ybnName)) return null;
            return GetImmediate(HashOf(ybnName));
        }

        public static uint HashOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var n = name.Trim().ToLowerInvariant();
            if (n.EndsWith(".ybn", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 4);
            return JenkHash.GenHash(n);
        }

        public bool IsMissing(uint ybnHash)
        {
            lock (sync) return entries.TryGetValue(ybnHash, out var e) && e.Missing;
        }

        private struct Request
        {
            public uint Hash;
            public Entry Entry;
            public YbnFile Ybn;
        }

        private readonly BlockingCollection<Request> queue =
            new BlockingCollection<Request>(new ConcurrentQueue<Request>());
        private Thread[] loaders;
        private volatile bool stopping;
        public int LoaderThreads = Math.Clamp(Environment.ProcessorCount / 4, 2, 3);

        private void StartLoader()
        {
            if (loaders != null) return;
            lock (sync)
            {
                if (loaders != null) return;
                stopping = false;
                var arr = new Thread[Math.Max(1, LoaderThreads)];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = new Thread(LoaderProc)
                    {
                        IsBackground = true,
                        Name = "CollisionView" + i,
                        Priority = ThreadPriority.BelowNormal,
                    };
                    arr[i].Start();
                }
                loaders = arr;
            }
        }

        private void LoaderProc()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (!stopping)
            {
                Request r;
                try { r = queue.Take(); }
                catch { return; }
                if (stopping) return;
                sw.Restart();
                var mesh = LoadAndBuild(r.Ybn, out double readMs);
                double total = sw.Elapsed.TotalMilliseconds;
                double buildMs = Math.Max(0, total - readMs);
                lock (sync)
                {
                    Built++;
                    ReadMsTotal += readMs; BuildMsTotal += buildMs;
                    if (readMs > ReadMsMax) ReadMsMax = readMs;
                    if (buildMs > BuildMsMax) { BuildMsMax = buildMs; SlowestFile = (r.Ybn?.Name ?? "?") + " " + (mesh?.TriangleCount ?? 0) + " tris"; }
                }
                Publish(r.Hash, r.Entry, mesh);
            }
        }

        private CollisionMesh LoadAndBuild(YbnFile ybn) => LoadAndBuild(ybn, out _);
        private CollisionMesh LoadAndBuild(YbnFile ybn, out double readMs)
        {
            readMs = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!files.EnsureLoaded(ybn)) { LastError = "load failed " + (ybn?.Name ?? "?"); return null; }
                readMs = sw.Elapsed.TotalMilliseconds;
                return Build(ybn);
            }
            catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        private void Publish(uint hash, Entry e, CollisionMesh mesh)
        {
            lock (sync)
            {
                e.Queued = false;
                if (!entries.TryGetValue(hash, out var cur) || cur != e) return;
                if (e.Mesh != null) return;
                if (mesh == null) { e.Missing = true; return; }
                e.Mesh = mesh;
                cachedTris += mesh.TriangleCount;
                LoadedCount++;
                Trim(e);
            }
        }

        private void Trim(Entry keep)
        {
            while (cachedTris > MaxCachedTriangles)
            {
                uint oldestKey = 0;
                int oldest = int.MaxValue;
                Entry oldestEntry = null;
                foreach (var kv in entries)
                {
                    var v = kv.Value;
                    if (v.Mesh == null) continue;
                    if (v == keep) continue;
                    if (v.LastUsed > frameTick) continue;
                    if (v.LastUsed >= oldest) continue;
                    oldest = v.LastUsed;
                    oldestKey = kv.Key;
                    oldestEntry = v;
                }
                if (oldestEntry == null) return;
                cachedTris -= oldestEntry.Mesh.TriangleCount;
                LoadedCount--;
                entries.Remove(oldestKey);
                Retire(oldestEntry.Mesh);
            }
        }

        private void Retire(CollisionMesh m)
        {
            if (m == null) return;
            Retired.Enqueue(m);
            RetiredCount++;
        }

        public void Forget(uint ybnHash)
        {
            lock (sync)
            {
                if (!entries.TryGetValue(ybnHash, out var e)) return;
                if (e.Mesh != null) { cachedTris -= e.Mesh.TriangleCount; LoadedCount--; Retire(e.Mesh); }
                entries.Remove(ybnHash);
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                foreach (var e in entries.Values) if (e.Mesh != null) Retire(e.Mesh);
                entries.Clear();
                cachedTris = 0;
                LoadedCount = 0;
            }
        }

        public void Dispose()
        {
            stopping = true;
            try { queue.CompleteAdding(); } catch { }
            if (loaders != null) foreach (var l in loaders) { try { l?.Join(500); } catch { } }
            loaders = null;
            Clear();
        }

        private static bool materialsTried;
        private static readonly object materialsSync = new object();

        public static bool MaterialsReady => BoundsMaterialTypes.Materials != null;

        public static string MaterialsError { get; private set; } = "";

        public static void EnsureMaterials(GameFileManager files)
        {
            if (BoundsMaterialTypes.Materials != null) return;
            var cache = files?.Cache;
            if (cache == null || !cache.IsInited) return;
            lock (materialsSync)
            {
                if (materialsTried || BoundsMaterialTypes.Materials != null) return;
                materialsTried = true;
                try { BoundsMaterialTypes.Init(cache); }
                catch (Exception ex) { MaterialsError = ex.Message; }
            }
        }

        public static Vector4 ColourFor(BoundMaterial_s mat)
        {
            const float s = 1.0f / 255.0f;
            if (BoundsMaterialTypes.Materials != null)
            {
                var c = BoundsMaterialTypes.GetMaterialColour(mat.Type);
                return new Vector4(c.R * s, c.G * s, c.B * s, 1.0f);
            }
            return FallbackColour(mat.Type);
        }

        private static Vector4 FallbackColour(byte index)
        {
            float h = (index * 0.61803399f) % 1.0f * 6.0f;
            float x = 1.0f - Math.Abs(h % 2.0f - 1.0f);
            float r, g, b;
            switch ((int)h)
            {
                case 0: r = 1; g = x; b = 0; break;
                case 1: r = x; g = 1; b = 0; break;
                case 2: r = 0; g = 1; b = x; break;
                case 3: r = 0; g = x; b = 1; break;
                case 4: r = x; g = 0; b = 1; break;
                default: r = 1; g = 0; b = x; break;
            }
            const float sat = 0.55f, val = 0.95f;
            return new Vector4(
                (1.0f - sat + sat * r) * val,
                (1.0f - sat + sat * g) * val,
                (1.0f - sat + sat * b) * val,
                1.0f);
        }

        public CollisionMesh Build(YbnFile ybn, Matrix? world = null)
        {
            var name = ybn?.Name ?? ybn?.RpfFileEntry?.Name ?? "collision";
            return BuildFromBounds(ybn?.Bounds, world, name);
        }

        public CollisionMesh BuildFromBounds(Bounds root, Matrix? world = null, string name = null)
        {
            EnsureMaterials(files);

            var b = new Builder
            {
                Alpha = alpha,
                Shading = shading,
                Colour = ColourSelector ?? ColourFor,
                SphereSegments = Math.Max(3, SphereSegments),
                SphereRings = Math.Max(2, SphereRings),
                TubeSegments = Math.Max(3, TubeSegments),
                KeepMaterialData = storeMaterialData,
            };

            if (root != null) b.AddBounds(root, world ?? Matrix.Identity);

            return b.Finish(name ?? "collision");
        }

        private sealed class Builder
        {
            public float Alpha;
            public float Shading;
            public Func<BoundMaterial_s, Vector4> Colour;
            public int SphereSegments;
            public int SphereRings;
            public int TubeSegments;
            public bool KeepMaterialData;

            private readonly List<LineVertex> verts = new List<LineVertex>(4096);
            private readonly List<byte> triMats = new List<byte>(2048);
            private List<BoundMaterial_s> triMatData;
            private readonly bool[] used = new bool[256];
            private Vector3 min = new Vector3(float.MaxValue);
            private Vector3 max = new Vector3(float.MinValue);
            private int primitives;

            private static readonly Vector3 KeyDir = Vector3.Normalize(new Vector3(-0.35f, -0.55f, 0.76f));

            public CollisionMesh Finish(string name)
            {
                var mesh = new CollisionMesh
                {
                    Name = name,
                    Vertices = verts.ToArray(),
                    TriMaterials = triMats.ToArray(),
                    TriMaterialData = triMatData?.ToArray(),
                    PrimitiveCount = primitives,
                };

                var mats = new List<byte>();
                for (int i = 0; i < used.Length; i++) if (used[i]) mats.Add((byte)i);
                mesh.MaterialsUsed = mats.ToArray();

                mesh.Bounds = verts.Count > 0
                    ? new BoundingBox(min, max)
                    : new BoundingBox(Vector3.Zero, Vector3.Zero);
                return mesh;
            }

            public void AddBounds(Bounds b, Matrix ancestors)
            {
                if (b == null) return;

                if (b is BoundComposite comp)
                {
                    var childFrame = Matrix.Multiply(b.Transform, ancestors);
                    var children = comp.Children?.data_items;
                    if (children == null) return;
                    foreach (var child in children) AddBounds(child, childFrame);
                    return;
                }

                if (b is BoundGeometry geom)
                {
                    AddGeometry(geom, ancestors);
                    return;
                }

                var xf = Matrix.Multiply(b.Transform, ancestors);
                var mat = MaterialOf(b);
                var col = ColourOf(mat);
                byte type = mat.Type;

                switch (b)
                {
                    case BoundBox _:
                        {
                            var lo = b.BoxMin;
                            var hi = b.BoxMax;
                            var d = hi - lo;
                            AddBox(lo, new Vector3(d.X, 0, 0), new Vector3(0, d.Y, 0), new Vector3(0, 0, d.Z),
                                col, type, mat, ref xf);
                            primitives++;
                            break;
                        }
                    case BoundSphere _:
                        AddSphere(b.SphereCenter, b.SphereRadius, col, type, mat, ref xf);
                        primitives++;
                        break;
                    case BoundCapsule _:
                        {
                            var extent = new Vector3(0, b.SphereRadius - b.Margin, 0);
                            AddCapsule(b.SphereCenter - extent, b.SphereCenter + extent, b.Margin,
                                col, type, mat, ref xf);
                            primitives++;
                            break;
                        }
                    case BoundCylinder _:
                        {
                            var d = b.BoxMax - b.BoxMin;
                            var extent = new Vector3(Math.Abs(d.X), Math.Abs(d.Y), Math.Abs(d.Z));
                            var half = new Vector3(0, extent.Y * 0.5f, 0);
                            AddTube(b.SphereCenter - half, b.SphereCenter + half, extent.X * 0.5f,
                                true, col, type, mat, ref xf);
                            primitives++;
                            break;
                        }
                    case BoundDisc _:
                        {
                            float half = Math.Max(b.Margin, 0.005f);
                            var off = new Vector3(half, 0, 0);
                            AddTube(b.SphereCenter - off, b.SphereCenter + off, b.SphereRadius,
                                true, col, type, mat, ref xf);
                            primitives++;
                            break;
                        }
                    default:
                        break;
                }
            }

            private void AddGeometry(BoundGeometry geom, Matrix ancestors)
            {
                var polys = geom.Polygons;
                if (polys == null || geom.Vertices == null) return;

                verts.Capacity = Math.Max(verts.Capacity, verts.Count + polys.Length * 3);

                for (int i = 0; i < polys.Length; i++)
                {
                    var poly = polys[i];
                    if (poly == null) continue;

                    var mat = poly.MaterialCustom ?? geom.GetMaterial(i);
                    var col = ColourOf(mat);
                    byte type = mat.Type;

                    switch (poly)
                    {
                        case BoundPolygonTriangle tri:
                            {
                                var p1 = geom.GetVertexPos(tri.vertIndex1);
                                var p2 = geom.GetVertexPos(tri.vertIndex2);
                                var p3 = geom.GetVertexPos(tri.vertIndex3);
                                AddTri(p1, p2, p3, col, type, mat, ref ancestors);
                                break;
                            }
                        case BoundPolygonSphere sph:
                            AddSphere(geom.GetVertexPos(sph.sphereIndex), sph.sphereRadius,
                                col, type, mat, ref ancestors);
                            primitives++;
                            break;
                        case BoundPolygonCapsule cap:
                            AddCapsule(geom.GetVertexPos(cap.capsuleIndex1),
                                geom.GetVertexPos(cap.capsuleIndex2), cap.capsuleRadius,
                                col, type, mat, ref ancestors);
                            primitives++;
                            break;
                        case BoundPolygonCylinder cyl:
                            AddTube(geom.GetVertexPos(cyl.cylinderIndex1),
                                geom.GetVertexPos(cyl.cylinderIndex2), cyl.cylinderRadius,
                                true, col, type, mat, ref ancestors);
                            primitives++;
                            break;
                        case BoundPolygonBox box:
                            {
                                var p1 = geom.GetVertexPos(box.boxIndex1);
                                var p2 = geom.GetVertexPos(box.boxIndex2);
                                var p3 = geom.GetVertexPos(box.boxIndex3);
                                var p4 = geom.GetVertexPos(box.boxIndex4);
                                var a1 = ((p3 + p4) - (p1 + p2)) * 0.5f;
                                var a2 = p3 - (p1 + a1);
                                var a3 = p4 - (p1 + a1);
                                var bs = new Vector3(a1.Length(), a2.Length(), a3.Length());
                                if (bs.X <= 1e-6f || bs.Y <= 1e-6f || bs.Z <= 1e-6f) break;
                                var m1 = a1 / bs.X;
                                var m2 = a2 / bs.Y;
                                var m3 = a3 / bs.Z;
                                if ((bs.X < bs.Y) && (bs.X < bs.Z)) m1 = Vector3.Cross(m2, m3);
                                else if (bs.Y < bs.Z) m2 = Vector3.Cross(m3, m1);
                                else m3 = Vector3.Cross(m1, m2);
                                AddBox(p1, m1 * bs.X, m2 * bs.Y, m3 * bs.Z, col, type, mat, ref ancestors);
                                primitives++;
                                break;
                            }
                    }
                }
            }

            private static BoundMaterial_s MaterialOf(Bounds b)
            {
                return new BoundMaterial_s
                {
                    Type = b.MaterialIndex,
                    ProceduralId = b.ProceduralId,
                    RoomId = b.RoomId,
                    PedDensity = b.PedDensity,
                    MaterialColorIndex = b.MaterialColorIndex,
                };
            }

            private Vector4 ColourOf(BoundMaterial_s mat)
            {
                var c = Colour(mat);
                c.W *= Alpha;
                return c;
            }

            private void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector4 col, byte type,
                BoundMaterial_s mat, ref Matrix xf)
            {
                var wa = Vector3.TransformCoordinate(a, xf);
                var wb = Vector3.TransformCoordinate(b, xf);
                var wc = Vector3.TransformCoordinate(c, xf);

                var n = Vector3.Cross(wb - wa, wc - wa);
                float nl = n.Length();
                float lit = nl > 1e-9f ? Math.Abs(Vector3.Dot(n / nl, KeyDir)) : 1.0f;
                float shade = (1.0f - Shading) + Shading * lit;

                var sc = new Vector4(col.X * shade, col.Y * shade, col.Z * shade, col.W);

                verts.Add(new LineVertex(wa, sc));
                verts.Add(new LineVertex(wb, sc));
                verts.Add(new LineVertex(wc, sc));

                triMats.Add(type);
                used[type] = true;
                if (KeepMaterialData)
                {
                    triMatData = triMatData ?? new List<BoundMaterial_s>(2048);
                    triMatData.Add(mat);
                }

                min = Vector3.Min(min, Vector3.Min(wa, Vector3.Min(wb, wc)));
                max = Vector3.Max(max, Vector3.Max(wa, Vector3.Max(wb, wc)));
            }

            private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 col, byte type,
                BoundMaterial_s mat, ref Matrix xf)
            {
                AddTri(a, b, c, col, type, mat, ref xf);
                AddTri(a, c, d, col, type, mat, ref xf);
            }

            private void AddBox(Vector3 o, Vector3 ax, Vector3 ay, Vector3 az, Vector4 col, byte type,
                BoundMaterial_s mat, ref Matrix xf)
            {
                var p000 = o;
                var p100 = o + ax;
                var p010 = o + ay;
                var p001 = o + az;
                var p110 = o + ax + ay;
                var p101 = o + ax + az;
                var p011 = o + ay + az;
                var p111 = o + ax + ay + az;

                AddQuad(p000, p010, p110, p100, col, type, mat, ref xf);
                AddQuad(p001, p101, p111, p011, col, type, mat, ref xf);
                AddQuad(p000, p100, p101, p001, col, type, mat, ref xf);
                AddQuad(p010, p011, p111, p110, col, type, mat, ref xf);
                AddQuad(p000, p001, p011, p010, col, type, mat, ref xf);
                AddQuad(p100, p110, p111, p101, col, type, mat, ref xf);
            }

            private void AddSphere(Vector3 centre, float radius, Vector4 col, byte type,
                BoundMaterial_s mat, ref Matrix xf)
            {
                if (radius <= 0.0f) return;
                int rings = SphereRings, segs = SphereSegments;
                for (int r = 0; r < rings; r++)
                {
                    float p0 = (float)(Math.PI * r / rings - Math.PI / 2);
                    float p1 = (float)(Math.PI * (r + 1) / rings - Math.PI / 2);
                    for (int s = 0; s < segs; s++)
                    {
                        float t0 = (float)(s * Math.PI * 2.0 / segs);
                        float t1 = (float)((s + 1) * Math.PI * 2.0 / segs);
                        var a = SpherePoint(centre, radius, p0, t0);
                        var b = SpherePoint(centre, radius, p0, t1);
                        var c = SpherePoint(centre, radius, p1, t1);
                        var d = SpherePoint(centre, radius, p1, t0);
                        AddQuad(a, b, c, d, col, type, mat, ref xf);
                    }
                }
            }

            private static Vector3 SpherePoint(Vector3 centre, float radius, float pitch, float theta)
            {
                return centre + new Vector3(
                    (float)(Math.Cos(pitch) * Math.Cos(theta)),
                    (float)(Math.Cos(pitch) * Math.Sin(theta)),
                    (float)Math.Sin(pitch)) * radius;
            }

            private void AddTube(Vector3 p0, Vector3 p1, float radius, bool caps, Vector4 col, byte type,
                BoundMaterial_s mat, ref Matrix xf)
            {
                if (radius <= 0.0f) return;
                Basis(p1 - p0, out var u, out var v);
                int segs = TubeSegments;
                for (int i = 0; i < segs; i++)
                {
                    float t0 = (float)(i * Math.PI * 2.0 / segs);
                    float t1 = (float)((i + 1) * Math.PI * 2.0 / segs);
                    var o0 = (u * (float)Math.Cos(t0) + v * (float)Math.Sin(t0)) * radius;
                    var o1 = (u * (float)Math.Cos(t1) + v * (float)Math.Sin(t1)) * radius;
                    AddQuad(p0 + o0, p1 + o0, p1 + o1, p0 + o1, col, type, mat, ref xf);
                    if (!caps) continue;
                    AddTri(p0, p0 + o1, p0 + o0, col, type, mat, ref xf);
                    AddTri(p1, p1 + o0, p1 + o1, col, type, mat, ref xf);
                }
            }

            private void AddCapsule(Vector3 p0, Vector3 p1, float radius, Vector4 col, byte type,
                BoundMaterial_s mat, ref Matrix xf)
            {
                if (radius <= 0.0f) return;
                AddTube(p0, p1, radius, false, col, type, mat, ref xf);
                AddSphere(p0, radius, col, type, mat, ref xf);
                AddSphere(p1, radius, col, type, mat, ref xf);
            }

            private static void Basis(Vector3 dir, out Vector3 a, out Vector3 b)
            {
                var d = dir;
                float len = d.Length();
                if (len < 1e-6f) d = Vector3.UnitZ; else d /= len;
                var t = Math.Abs(d.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
                a = Vector3.Normalize(Vector3.Cross(d, t));
                b = Vector3.Cross(d, a);
            }
        }
    }
}

