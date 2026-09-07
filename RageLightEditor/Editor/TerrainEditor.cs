using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public partial class TerrainEditor
    {
        public const int LayerCount = 4;

        public static Vector3 CornerOf(int layer)
        {
            switch (layer)
            {
                case 1: return new Vector3(0, 0, 1);
                case 2: return new Vector3(0, 1, 0);
                case 3: return new Vector3(0, 1, 1);
                default: return Vector3.Zero;
            }
        }

        public static Vector4 WeightsOf(Vector3 rgb)
        {
            rgb = new Vector3(MathUtil.Clamp(rgb.X, 0, 1), MathUtil.Clamp(rgb.Y, 0, 1), MathUtil.Clamp(rgb.Z, 0, 1));
            var m = new Vector3(1 - rgb.X, 1 - rgb.Y, 1 - rgb.Z);
            return new Vector4(m.X * m.Y * m.Z, m.X * m.Y * rgb.Z, m.X * rgb.Y * m.Z, m.X * rgb.Y * rgb.Z);
        }

        public class Layer
        {
            public string Name;
            public GameTexture Texture;
            public ShaderResourceView Srv;
            public IntPtr ThumbId;
            public string Source;
            public string BumpName;
            public GameTexture BumpTexture;
            public ShaderResourceView BumpSrv;
            public float Tiling = 1.0f;

            public bool HasTexture => Srv != null;
            public void Clear()
            {
                Name = null; Texture = null; Srv = null; ThumbId = IntPtr.Zero; Source = null;
                BumpName = null; BumpTexture = null; BumpSrv = null;
            }
        }

        public readonly Layer[] Layers = { new Layer(), new Layer(), new Layer(), new Layer() };

        public int ActiveLayer;

        public class Part
        {
            public MeshVertex[] Verts;
            public ushort[] Indices;
            public Vector2[] BaseUV0, BaseUV1;
            public BoundingBox Box;
            public RenderMesh Mesh;
            public bool Dirty;
            public string SourceName = "";
            public int Group_V20;
        }

        public readonly List<Part> Parts = new List<Part>();

        public BoundingBox Bounds;

        public string SourcePath;
        public string Name = "";

        public bool Dirty;

        public bool HasMesh => Parts.Count > 0;
        public int VertexCount { get { int n = 0; foreach (var p in Parts) n += p.Verts.Length; return n; } }
        public int TriangleCount { get { int n = 0; foreach (var p in Parts) n += p.Indices.Length / 3; return n; } }

        public readonly EditHistory History = new EditHistory();

        public float BrushRadius = 3.0f;
        public float BrushHardness = 0.5f;
        public float BrushStrength = 0.5f;
        public bool ShowWeights;
        public bool ShowBrush = true;
        public bool PaintEnabled = true;

        public Vector3 CursorPoint;
        public bool CursorOnMesh;

        public string ExportPreset = "terrain_cb_w_4lyr";
        public TexDestination TextureExport = TexDestination.Embed;
        public enum TexDestination { Ytd = 0, Embed = 1, Reference = 2 }

        public string Status = "Import a mesh to start.";

        public bool RequestImportFile;
        public bool RequestImportOpen;
        public bool RequestExport;
        public bool RequestClear;
        public bool RequestFrame;
        public bool RequestUndo, RequestRedo;
        public int RequestLayerFromDisk = -1;
        public int RequestLayerFromGame = -1;
        public int RequestLayerBumpFromDisk = -1;
        public int RequestClearLayer = -1;
        public int RequestFillLayer = -1;
        public int PendingNameSlot = -1;
        public string PendingName;

        private PaintCommand stroke;

        public bool Painting => stroke != null;

        public void BeginStroke(int layer)
        {
            stroke = new PaintCommand(this, layer);
        }

        public int Paint(Vector3 centre, int layer, float strengthScale = 1.0f)
        {
            return PaintSegment(centre, centre, layer, strengthScale);
        }

        public void EndStroke()
        {
            var s = stroke;
            stroke = null;
            if (s == null || s.IsEmpty) return;
            s.Capture();
            History.Push(s);
        }

        public void Fill(int layer)
        {
            if (!HasMesh) return;
            var cmd = new PaintCommand(this, layer) { Label = "Fill with layer " + layer };
            var target = CornerOf(layer);
            foreach (var part in Parts)
            {
                for (int i = 0; i < part.Verts.Length; i++)
                {
                    cmd.Record(part, i, part.Verts[i].Colour1);
                    part.Verts[i].Colour1 = new Vector4(0, target.Y, target.Z, part.Verts[i].Colour1.W);
                }
                part.Dirty = true;
                TouchProp_V20(part.Group_V20);
            }
            cmd.Capture();
            History.Push(cmd);
            Dirty = true;
            Status = $"Filled {VertexCount:N0} vertices with layer {layer}" +
                     (string.IsNullOrEmpty(Layers[layer].Name) ? "" : " (" + Layers[layer].Name + ")");
        }

        public void Clear()
        {
            foreach (var p in Parts) p.Mesh?.Dispose();
            Parts.Clear();
            Bounds = new BoundingBox(Vector3.Zero, Vector3.Zero);
            SourcePath = null;
            Name = "";
            Dirty = false;
            History.Clear();
            Status = "Import a mesh to start.";
        }

        public bool TwoUvSets => TerrainYdr.UsesTwoUvSets(ExportPreset);

        private float appliedTile0 = 1.0f, appliedTile1 = 1.0f;
        private bool appliedTwoUv;

        public bool ApplyTiling()
        {
            float t0 = Math.Max(Layers[0].Tiling, 0.0001f);
            float t1 = TwoUvSets ? Math.Max(Layers[1].Tiling, 0.0001f) : 1.0f;
            if (Math.Abs(t0 - appliedTile0) < 1e-6f && Math.Abs(t1 - appliedTile1) < 1e-6f && appliedTwoUv == TwoUvSets)
                return false;
            appliedTile0 = t0; appliedTile1 = t1; appliedTwoUv = TwoUvSets;
            foreach (var part in Parts)
            {
                if (part.BaseUV0 == null) continue;
                for (int i = 0; i < part.Verts.Length; i++)
                {
                    part.Verts[i].UV0 = part.BaseUV0[i] * t0;
                    part.Verts[i].UV1 = part.BaseUV1[i] * t1;
                }
                part.Dirty = true;
                TouchProp_V20(part.Group_V20);
            }
            return Parts.Count > 0;
        }

        public void CaptureBaseUvs()
        {
            foreach (var part in Parts)
            {
                part.BaseUV0 = new Vector2[part.Verts.Length];
                part.BaseUV1 = new Vector2[part.Verts.Length];
                for (int i = 0; i < part.Verts.Length; i++)
                {
                    part.BaseUV0[i] = part.Verts[i].UV0;
                    part.BaseUV1[i] = part.Verts[i].UV1;
                }
            }
            appliedTile0 = appliedTile1 = 1.0f;
            appliedTwoUv = TwoUvSets;
            for (int i = 0; i < LayerCount; i++) Layers[i].Tiling = 1.0f;
        }

        public void RebuildBounds()
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in Parts)
                foreach (var v in p.Verts)
                {
                    min = Vector3.Min(min, v.Position);
                    max = Vector3.Max(max, v.Position);
                }
            if (Parts.Count == 0) { min = max = Vector3.Zero; }
            Bounds = new BoundingBox(min, max);
            MeasureMesh();
        }

        public Vector4 Coverage()
        {
            var sum = Vector4.Zero;
            int n = 0;
            foreach (var p in Parts)
                foreach (var v in p.Verts)
                {
                    sum += WeightsOf(new Vector3(v.Colour1.X, v.Colour1.Y, v.Colour1.Z));
                    n++;
                }
            return n > 0 ? sum / n : Vector4.Zero;
        }

        public bool RayHit(ref Ray ray, out Vector3 point)
        {
            point = Vector3.Zero;
            float best = float.MaxValue;
            foreach (var part in Parts)
            {
                var verts = part.Verts;
                var idx = part.Indices;
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    ref var a = ref verts[idx[i]].Position;
                    ref var b = ref verts[idx[i + 1]].Position;
                    ref var c = ref verts[idx[i + 2]].Position;
                    if (!ray.Intersects(ref a, ref b, ref c, out float t)) continue;
                    if (t < best) best = t;
                }
            }
            if (best == float.MaxValue) return false;
            point = ray.Position + ray.Direction * best;
            return true;
        }

        public class PaintCommand : IEditCommand
        {
            private readonly TerrainEditor owner;
            private readonly int layer;
            private readonly List<(TerrainEditor.Part part, int index, Vector4 before)> entries =
                new List<(TerrainEditor.Part, int, Vector4)>();
            private Vector4[] after;
            private readonly HashSet<(Part, int)> seen = new HashSet<(Part, int)>();

            public string Label;
            public PaintCommand(TerrainEditor owner, int layer) { this.owner = owner; this.layer = layer; }

            public bool IsEmpty => entries.Count == 0;
            public string Name => Label ?? $"Paint layer {layer} ({entries.Count} vertices)";

            public void Record(Part part, int index, Vector4 before)
            {
                if (!seen.Add((part, index))) return;
                entries.Add((part, index, before));
            }

            public void Capture()
            {
                after = new Vector4[entries.Count];
                for (int i = 0; i < entries.Count; i++)
                    after[i] = entries[i].part.Verts[entries[i].index].Colour1;
            }

            public void Do()
            {
                if (after == null) return;
                for (int i = 0; i < entries.Count; i++)
                    entries[i].part.Verts[entries[i].index].Colour1 = after[i];
                MarkDirty();
            }

            public void Undo()
            {
                for (int i = 0; i < entries.Count; i++)
                    entries[i].part.Verts[entries[i].index].Colour1 = entries[i].before;
                MarkDirty();
            }

            private void MarkDirty()
            {
                foreach (var e in entries) { e.part.Dirty = true; owner.TouchProp_V20(e.part.Group_V20); }
                owner.Dirty = true;
            }
        }
    }
}

