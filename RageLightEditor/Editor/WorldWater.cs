using System;
using System.Linq;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using CodeWalker.World;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace RageLightEditor.Editor
{
    public partial class WorldWater : IDisposable
    {
        private readonly Water water = new Water();
        private readonly List<RenderMesh> quads = new List<RenderMesh>();
        public readonly RenderModel Model = new RenderModel { Name = "Water" };

        public bool Ready { get; private set; }
        public int QuadCount => quads.Count;
        public string Status = "";

        public void Build(GameFileCache cache, Device device)
        {
            Ready = false;
            Dispose();
            if (cache == null || device == null) { Status = "no game cache"; return; }
            try
            {
                water.Init(cache, s => Status = s);
            }
            catch (Exception ex) { Status = "water.xml: " + ex.Message; return; }

            int made = 0, invisible = 0;
            var dumpAt = ParseDumpPoint(Environment.GetEnvironmentVariable("RLE_WATERDUMP"));
            var shown = new List<WaterQuad>();
            foreach (var q in water.WaterQuads)
            {
                if (q == null) continue;
                if (dumpAt.HasValue && q.maxX >= dumpAt.Value.X - dumpAt.Value.Z && q.minX <= dumpAt.Value.X + dumpAt.Value.Z
                    && q.maxY >= dumpAt.Value.Y - dumpAt.Value.Z && q.minY <= dumpAt.Value.Y + dumpAt.Value.Z)
                    Console.WriteLine($"WATERQUAD x {q.minX}..{q.maxX} y {q.minY}..{q.maxY} z {q.z} a {q.a1} {q.a2} {q.a3} {q.a4} type {q.Type} inv {q.IsInvisible} limDepth {q.HasLimitedDepth} noStencil {q.NoStencil}");
                if (q.IsInvisible) { invisible++; continue; }
                shown.Add(q);
            }
            var rects = shown.Select(q => new QuadRect_U9(q.minX, q.maxX, q.minY, q.maxY, q.z ?? 0.0f)).ToList();
            var splits = SplitPointsFor_U9(rects);
            for (int i = 0; i < shown.Count; i++)
            {
                var m = BuildQuad(shown[i], device, splits[i]);
                if (m != null) { quads.Add(m); made++; }
            }
            Model.Meshes.Clear();
            Model.Meshes.AddRange(quads);
            Ready = made > 0;
            Status = $"{made} water quads ({invisible} invisible skipped)";
        }

        public static readonly float InlandWaterMaxDepth =
            float.TryParse(Environment.GetEnvironmentVariable("RLE_WATERMAXDEPTH"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 40.0f;

        public static ushort[] QuadIndices_V60(int type)
        {
            switch (type)
            {
                case 1: return new ushort[] { 0, 1, 3 };
                case 2: return new ushort[] { 0, 2, 3 };
                case 3: return new ushort[] { 1, 2, 3 };
                case 4: return new ushort[] { 0, 1, 2 };
                default: return new ushort[] { 0, 3, 1, 1, 3, 2 };
            }
        }

        private static RenderMesh BuildQuad(WaterQuad q, Device device, List<Vector2> splits)
        {
            float x0 = q.minX, x1 = q.maxX, y0 = q.minY, y1 = q.maxY, z = q.z ?? 0.0f;
            if (!(x1 > x0) || !(y1 > y0)) return null;

            float a1 = q.a1 / 255.0f, a2 = q.a2 / 255.0f, a3 = q.a3 / 255.0f, a4 = q.a4 / 255.0f;
            var rect = new QuadRect_U9(x0, x1, y0, y1, z);
            var up = Vector3.UnitZ;
            var tan = new Vector4(1, 0, 0, 1);
            const float uvScale = 1.0f / 8.0f;
            MeshVertex At(Vector2 p, float alpha) => new MeshVertex
            {
                Position = new Vector3(p.X, p.Y, z), Normal = up, Tangent = tan,
                Colour0 = new Vector4(1, 1, 1, alpha), Colour1 = Vector4.One,
                UV0 = p * uvScale, UV1 = p * uvScale,
            };

            MeshVertex[] verts;
            ushort[] indices;
            if (q.Type == 0)
            {
                var xs = GridAxis_U9(x0, x1, splits?.Select(v => v.X));
                var ys = GridAxis_U9(y0, y1, splits?.Select(v => v.Y));
                verts = new MeshVertex[xs.Count * ys.Count];
                for (int j = 0; j < ys.Count; j++)
                    for (int i = 0; i < xs.Count; i++)
                    {
                        var p = new Vector2(xs[i], ys[j]);
                        verts[j * xs.Count + i] = At(p, CornerAlphaAt_U9(rect, a1, a2, a3, a4, p));
                    }
                indices = GridIndices_U9(xs.Count, ys.Count);
            }
            else
            {
                var ring = Perimeter_U9(rect, q.Type, splits);
                if (ring.Count < 3) return null;
                verts = new MeshVertex[ring.Count + 1];
                var centre = Vector2.Zero;
                float centreAlpha = 0.0f;
                for (int i = 0; i < ring.Count; i++)
                {
                    float alpha = CornerAlphaAt_U9(rect, a1, a2, a3, a4, ring[i]);
                    verts[i] = At(ring[i], alpha);
                    centre += ring[i];
                    centreAlpha += alpha;
                }
                centre /= ring.Count;
                verts[ring.Count] = At(centre, centreAlpha / ring.Count);
                indices = FanIndices_U9(ring.Count);
            }
            if (indices.Length == 0) return null;

            var mesh = new RenderMesh
            {
                Transform = Matrix.Identity,
                VB = Buffer.Create(device, BindFlags.VertexBuffer, verts),
                IB = Buffer.Create(device, BindFlags.IndexBuffer, indices),
                IndexCount = indices.Length,
                AlphaMode = GeomAlphaMode.Water,
                DoubleSided = true,
                MatDiffuse = new Vector4(0.10f, 0.28f, 0.42f, 0.78f),
                SpecIntensity = 1.0f,
                Bumpiness = Math.Abs(z) > 1.0f ? InlandWaterMaxDepth : 0.0f,
            };
            mesh.PickVerts = new[]
            {
                new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z),
            };
            mesh.PickIndices = QuadIndices_V60(q.Type);
            var min = new Vector3(x0, y0, z - 0.5f);
            var max = new Vector3(x1, y1, z + 0.5f);
            mesh.WorldBounds = new BoundingBox(min, max);
            mesh.WorldSphere = BoundingSphere.FromBox(mesh.WorldBounds);
            return mesh;
        }

        private static Vector3? ParseDumpPoint(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var p = s.Split(',');
            if (p.Length < 2) return null;
            if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return null;
            if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) return null;
            float r = 200;
            if (p.Length > 2) float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r);
            return new Vector3(x, y, r);
        }

        public float? HeightAt(float x, float y, float camZ)
        {
            float? bestAbove = null, bestBelow = null;
            foreach (var q in water.WaterQuads)
            {
                if (q == null || q.IsInvisible || q.z == null) continue;
                if (x < q.minX || x > q.maxX || y < q.minY || y > q.maxY) continue;
                float z = q.z.Value;
                if (z >= camZ) { if (bestAbove == null || z < bestAbove.Value) bestAbove = z; }
                else { if (bestBelow == null || z > bestBelow.Value) bestBelow = z; }
            }
            return bestAbove ?? bestBelow;
        }

        public bool IsUnderwater(Vector3 p)
        {
            var h = HeightAt(p.X, p.Y, p.Z);
            return h.HasValue && p.Z < h.Value;
        }

        public void Dispose()
        {
            foreach (var m in quads) m.Dispose();
            quads.Clear();
            Model.Meshes.Clear();
        }
    }
}

