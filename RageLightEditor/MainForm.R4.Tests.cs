using System;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_R4(Action<string, bool, string> check)
        {
            TerrainBlendTest_R4(check);
            TerrainBrushTest_R4(check);
            TerrainRoundTripTest_R4(check);
            TerrainSectionTest_R4(check);
        }

        private static void TerrainBlendTest_R4(Action<string, bool, string> check)
        {
            bool corners = true;
            string detail = "";
            for (int i = 0; i < 4; i++)
            {
                var w = TerrainEditor.WeightsOf(TerrainEditor.CornerOf(i));
                float mine = i == 0 ? w.X : i == 1 ? w.Y : i == 2 ? w.Z : w.W;
                float others = w.X + w.Y + w.Z + w.W - mine;
                if (Math.Abs(mine - 1.0f) > 1e-5f || others > 1e-5f)
                { corners = false; detail += $"layer {i} -> {w.X:0.###},{w.Y:0.###},{w.Z:0.###},{w.W:0.###} "; }
            }
            check("terrain: each layer's corner of the blend cube gives that layer weight 1", corners, detail);

            bool sums = true;
            var rnd = new Random(4);
            for (int i = 0; i < 64; i++)
            {
                var v = new Vector3(0, (float)rnd.NextDouble(), (float)rnd.NextDouble());
                var w = TerrainEditor.WeightsOf(v);
                float s = w.X + w.Y + w.Z + w.W;
                if (Math.Abs(s - 1.0f) > 1e-4f) { sums = false; detail = $"{v.Y:0.##},{v.Z:0.##} -> {s:0.####}"; break; }
            }
            check("terrain: the four layer weights sum to one for any blend vector", sums, detail);

            var half = TerrainEditor.WeightsOf(new Vector3(0, 0, 0.5f));
            check("terrain: halfway to layer 1 is half layer 0 and half layer 1",
                  Math.Abs(half.X - 0.5f) < 1e-4f && Math.Abs(half.Y - 0.5f) < 1e-4f && half.Z + half.W < 1e-4f,
                  $"{half.X:0.###},{half.Y:0.###},{half.Z:0.###},{half.W:0.###}");
        }

        private static TerrainEditor MakeTestTerrain_R4(int cells = 8, float step = 1.0f)
        {
            var te = new TerrainEditor();
            int w = cells + 1;
            var verts = new MeshVertex[w * w];
            for (int y = 0; y < w; y++)
                for (int x = 0; x < w; x++)
                    verts[y * w + x] = new MeshVertex
                    {
                        Position = new Vector3(x * step, y * step, 0),
                        Normal = Vector3.UnitZ,
                        Tangent = new Vector4(1, 0, 0, 1),
                        Colour0 = Vector4.One,
                        Colour1 = new Vector4(0, 0, 0, 1),
                        UV0 = new Vector2((float)x / cells, (float)y / cells),
                        UV1 = new Vector2((float)x / cells, (float)y / cells),
                    };
            var idx = new System.Collections.Generic.List<ushort>();
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    ushort a = (ushort)(y * w + x), b = (ushort)(a + 1), c = (ushort)(a + w), d = (ushort)(c + 1);
                    idx.Add(a); idx.Add(c); idx.Add(b);
                    idx.Add(b); idx.Add(c); idx.Add(d);
                }
            te.Parts.Add(new TerrainEditor.Part { Verts = verts, Indices = idx.ToArray() });
            te.RebuildBounds();
            te.CaptureBaseUvs();
            return te;
        }

        private static void TerrainBrushTest_R4(Action<string, bool, string> check)
        {
            var te = MakeTestTerrain_R4();
            te.BrushRadius = 2.0f;
            te.BrushStrength = 1.0f;
            te.BrushHardness = 0.0f;
            var verts = te.Parts[0].Verts;

            te.BeginStroke(1);
            int touched = te.Paint(new Vector3(4, 4, 0), 1);
            te.EndStroke();

            int centre = -1, far = -1;
            for (int i = 0; i < verts.Length; i++)
            {
                if ((verts[i].Position - new Vector3(4, 4, 0)).LengthSquared() < 1e-6f) centre = i;
                if ((verts[i].Position - new Vector3(0, 0, 0)).LengthSquared() < 1e-6f) far = i;
            }
            var cw = TerrainEditor.WeightsOf(new Vector3(verts[centre].Colour1.X, verts[centre].Colour1.Y, verts[centre].Colour1.Z));
            check("terrain: a full-strength dab puts the vertex under the cursor entirely on the chosen layer",
                  centre >= 0 && cw.Y > 0.99f, $"weight {cw.Y:0.###}, {touched} vertices touched");
            check("terrain: a vertex outside the brush radius is untouched",
                  far >= 0 && verts[far].Colour1.Z < 1e-5f, far >= 0 ? verts[far].Colour1.Z.ToString("0.####") : "not found");

            var t2 = MakeTestTerrain_R4();
            t2.BrushRadius = 3.5f; t2.BrushStrength = 1.0f; t2.BrushHardness = 0.0f;
            t2.Paint(new Vector3(4, 4, 0), 1);
            var by = new System.Collections.Generic.List<(float d, float w)>();
            foreach (var v in t2.Parts[0].Verts)
                by.Add(((v.Position - new Vector3(4, 4, 0)).Length(), v.Colour1.Z));
            by.Sort((a, b) => a.d.CompareTo(b.d));
            bool monotonic = true;
            string mdetail = "";
            for (int i = 1; i < by.Count; i++)
                if (by[i].w > by[i - 1].w + 1e-4f)
                { monotonic = false; mdetail = $"at {by[i - 1].d:0.##} m w {by[i - 1].w:0.###}, at {by[i].d:0.##} m w {by[i].w:0.###}"; break; }
            check("terrain: the brush falloff never rises as you move away from the centre",
                  monotonic && by[0].w > 0.99f && by[by.Count - 1].w < 1e-5f, mdetail);

            te.History.Undo();
            bool restored = true;
            foreach (var v in te.Parts[0].Verts)
                if (v.Colour1.Y > 1e-6f || v.Colour1.Z > 1e-6f) { restored = false; break; }
            check("terrain: undo puts a paint stroke back exactly", restored && te.History.CanRedo,
                  restored ? "" : "some vertices kept their paint");

            te.History.Redo();
            var rw = TerrainEditor.WeightsOf(new Vector3(te.Parts[0].Verts[centre].Colour1.X,
                                                        te.Parts[0].Verts[centre].Colour1.Y,
                                                        te.Parts[0].Verts[centre].Colour1.Z));
            check("terrain: redo puts the stroke back", rw.Y > 0.99f, rw.Y.ToString("0.###"));

            var te2 = MakeTestTerrain_R4();
            te2.Fill(2);
            var cov = te2.Coverage();
            check("terrain: filling with layer 2 gives it the whole mesh", cov.Z > 0.999f,
                  $"{cov.X:0.##}/{cov.Y:0.##}/{cov.Z:0.##}/{cov.W:0.##}");
        }

        private static void TerrainRoundTripTest_R4(Action<string, bool, string> check)
        {
            var te = MakeTestTerrain_R4(8, 2.0f);
            te.Name = "seqtest_terrain";
            te.ExportPreset = "terrain_cb_w_4lyr";
            for (int i = 0; i < TerrainEditor.LayerCount; i++) te.Layers[i].Name = "layer_test_" + i;
            te.Layers[0].Tiling = 8.0f;
            te.ApplyTiling();

            te.BrushRadius = 5.0f; te.BrushStrength = 1.0f; te.BrushHardness = 0.5f;
            te.BeginStroke(1); te.Paint(new Vector3(8, 8, 0), 1); te.EndStroke();
            te.BeginStroke(3); te.Paint(new Vector3(2, 2, 0), 3); te.EndStroke();
            var before = te.Coverage();

            byte[] bytes = null;
            string err = null;
            try
            {
                var ydr = TerrainYdr.Build(te, te.Name, null, null);
                bytes = ydr?.Save();
            }
            catch (Exception ex) { err = ex.Message; }
            check("terrain: a painted mesh builds and saves as a .ydr", bytes != null && bytes.Length > 256,
                  err ?? (bytes == null ? "null" : bytes.Length + " bytes"));
            if (bytes == null) return;

            var read = new YdrFile();
            try { read.Load(bytes); } catch (Exception ex) { err = ex.Message; }
            var geoms = read.Drawable?.DrawableModels?.High?[0]?.Geometries;
            check("terrain: the .ydr reads back with its geometry", geoms != null && geoms.Length == te.Parts.Count,
                  err ?? (geoms == null ? "no geometries" : geoms.Length + " geometries"));
            if (geoms == null) return;

            var shader = geoms[0].Shader;
            string sname = ShaderPresets.NameOf(shader?.Name.Hash ?? 0);
            int layerParams = 0;
            bool hasDiffuse = false;
            var hashes = shader?.ParametersList?.Hashes;
            if (hashes != null)
                foreach (var h in hashes)
                {
                    uint u = (uint)h;
                    if (u == (uint)ShaderParamNames.TextureSampler_layer0 || u == (uint)ShaderParamNames.TextureSampler_layer1 ||
                        u == (uint)ShaderParamNames.TextureSampler_layer2 || u == (uint)ShaderParamNames.TextureSampler_layer3) layerParams++;
                    if (u == (uint)ShaderParamNames.DiffuseSampler) hasDiffuse = true;
                }
            check("terrain: the exported material is the terrain preset with four layer samplers and no diffuse",
                  sname == "terrain_cb_w_4lyr" && layerParams == 4 && !hasDiffuse,
                  $"{sname ?? shader?.Name.ToString()}, {layerParams} layer params, diffuse {hasDiffuse}");

            string firstLayer = null;
            var ps = shader?.ParametersList?.Parameters;
            if (ps != null && hashes != null)
                for (int i = 0; i < ps.Length && i < hashes.Length; i++)
                    if ((uint)hashes[i] == (uint)ShaderParamNames.TextureSampler_layer0 && ps[i].Data is TextureBase tb) firstLayer = tb.Name;
            check("terrain: the layer texture names are in the exported material", firstLayer == "layer_test_0", firstLayer ?? "(none)");

            float worst = 0;
            int n = 0;
            var src = te.Parts[0].Verts;
            var dv = VertexDecoder.Decode(geoms[0].VertexData, geoms[0].IndexBuffer?.Indices);
            if (dv != null && dv.Length == src.Length)
                for (int i = 0; i < dv.Length; i++)
                {
                    var a = TerrainEditor.WeightsOf(new Vector3(src[i].Colour1.X, src[i].Colour1.Y, src[i].Colour1.Z));
                    var b = TerrainEditor.WeightsOf(new Vector3(dv[i].Colour1.X, dv[i].Colour1.Y, dv[i].Colour1.Z));
                    worst = Math.Max(worst, Math.Abs(a.X - b.X));
                    worst = Math.Max(worst, Math.Abs(a.Y - b.Y));
                    worst = Math.Max(worst, Math.Abs(a.Z - b.Z));
                    worst = Math.Max(worst, Math.Abs(a.W - b.W));
                    n++;
                }
            check("terrain: the painted blend survives the .ydr round trip",
                  n == src.Length && worst < 0.01f, $"{n} vertices, worst weight error {worst:0.0000}");

            float uvMax = 0;
            if (dv != null) foreach (var v in dv) uvMax = Math.Max(uvMax, Math.Max(v.UV0.X, v.UV0.Y));
            check("terrain: the layer tiling is baked into the exported UVs", uvMax > 7.5f && uvMax < 8.5f,
                  $"max UV0 {uvMax:0.##} (expected 8 at 8x tiling)");

            var teBack = new TerrainEditor();
            teBack.Parts.Add(new TerrainEditor.Part { Verts = dv, Indices = geoms[0].IndexBuffer.Indices });
            var after = teBack.Coverage();
            check("terrain: the reloaded file reports the same layer coverage",
                  Math.Abs(after.X - before.X) < 0.01f && Math.Abs(after.Y - before.Y) < 0.01f &&
                  Math.Abs(after.W - before.W) < 0.01f,
                  $"before {before.X:0.###}/{before.Y:0.###}/{before.Z:0.###}/{before.W:0.###} " +
                  $"after {after.X:0.###}/{after.Y:0.###}/{after.Z:0.###}/{after.W:0.###}");

            var mask = TerrainYdr.RasteriseMask(te, 64, "seqtest_mask", out string why);
            check("terrain: the _cm blend mask bakes from the painted vertices",
                  mask?.Data?.FullData != null && mask.Data.FullData.Length == 64 * 64 * 4,
                  why ?? (mask == null ? "null" : mask.Width + "x" + mask.Height));
            if (mask?.Data?.FullData != null)
            {
                bool anyPaint = false;
                var px = mask.Data.FullData;
                for (int i = 0; i < px.Length; i += 4) if (px[i] > 8 || px[i + 1] > 8) { anyPaint = true; break; }
                check("terrain: the baked mask carries the painted blend rather than a blank sheet", anyPaint, "");
            }

            var flat = MakeTestTerrain_R4();
            for (int i = 0; i < flat.Parts[0].Verts.Length; i++) flat.Parts[0].Verts[i].UV1 = Vector2.Zero;
            var none = TerrainYdr.RasteriseMask(flat, 64, "x", out string why2);
            check("terrain: baking a mask is refused, with a reason, when the mesh has no second UV set",
                  none == null && !string.IsNullOrEmpty(why2), why2 ?? "it produced one anyway");
        }

        private static void TerrainSectionTest_R4(Action<string, bool, string> check)
        {
            check("terrain: the Space enum keeps every existing number and Terrain is last",
                  (int)Editor.LightPanel.Space.Light == 0 && (int)Editor.LightPanel.Space.Material == 1 &&
                  (int)Editor.LightPanel.Space.Cinematic == 2 && (int)Editor.LightPanel.Space.Archive == 3 &&
                  (int)Editor.LightPanel.Space.World == 4 && (int)Editor.LightPanel.Space.Mlo == 5 &&
                  (int)Editor.LightPanel.Space.Particles == 6 && (int)Editor.LightPanel.Space.NavMesh == 7 &&
                  (int)Editor.LightPanel.Space.Terrain == 8,
                  "Terrain = " + (int)Editor.LightPanel.Space.Terrain);

            var order = Editor.LightPanel.WorkspaceTabOrder_P1;
            int ti = Array.IndexOf(order, Editor.LightPanel.Space.Terrain);
            int ci = Array.IndexOf(order, Editor.LightPanel.Space.Cinematic);
            check("terrain: the tab is in the bar, before Cinematic (which stays last)",
                  ti >= 0 && ci == order.Length - 1 && ti < ci,
                  string.Join(" ", order));

            var mine = Editor.LightPanel.WorkspaceColour_Q3(Editor.LightPanel.Space.Terrain);
            bool unique = true;
            foreach (Editor.LightPanel.Space s in Enum.GetValues(typeof(Editor.LightPanel.Space)))
            {
                if (s == Editor.LightPanel.Space.Terrain) continue;
                var c = Editor.LightPanel.WorkspaceColour_Q3(s);
                if (Math.Abs(c.X - mine.X) < 0.02f && Math.Abs(c.Y - mine.Y) < 0.02f && Math.Abs(c.Z - mine.Z) < 0.02f)
                { unique = false; break; }
            }
            check("terrain: the section has an accent colour of its own", unique,
                  $"#{(int)(mine.X * 255):X2}{(int)(mine.Y * 255):X2}{(int)(mine.Z * 255):X2}");
        }
    }
}

