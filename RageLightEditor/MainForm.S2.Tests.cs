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
        partial void SeqTest_S2(Action<string, bool, string> check)
        {
            TerrainStrokeTest_S2(check);
            TerrainSubdivideTest_S2(check);
            TerrainEmbedTest_S2(check);
        }

        private static TerrainEditor MakeGrid_S2(int cells = 40, float step = 1.0f)
        {
            var te = new TerrainEditor();
            var verts = new MeshVertex[(cells + 1) * (cells + 1)];
            float half = cells * step * 0.5f;
            for (int y = 0; y <= cells; y++)
                for (int x = 0; x <= cells; x++)
                    verts[y * (cells + 1) + x] = new MeshVertex
                    {
                        Position = new Vector3(x * step - half, y * step - half, 0),
                        Normal = Vector3.UnitZ,
                        Tangent = new Vector4(1, 0, 0, 1),
                        Colour0 = Vector4.One,
                        Colour1 = new Vector4(0, 0, 0, 1),
                        UV0 = new Vector2((float)x / cells, (float)y / cells),
                        UV1 = new Vector2((float)x / cells, (float)y / cells),
                    };
            var idx = new System.Collections.Generic.List<ushort>();
            int w = cells + 1;
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    int a = y * w + x;
                    idx.Add((ushort)a); idx.Add((ushort)(a + 1)); idx.Add((ushort)(a + w));
                    idx.Add((ushort)(a + 1)); idx.Add((ushort)(a + w + 1)); idx.Add((ushort)(a + w));
                }
            te.Parts.Add(new TerrainEditor.Part { Verts = verts, Indices = idx.ToArray() });
            te.RebuildBounds();
            te.CaptureBaseUvs();
            return te;
        }

        private static Vector4[] Colours_S2(TerrainEditor te)
        {
            var c = new Vector4[te.Parts[0].Verts.Length];
            for (int i = 0; i < c.Length; i++) c[i] = te.Parts[0].Verts[i].Colour1;
            return c;
        }

        private static void TerrainStrokeTest_S2(Action<string, bool, string> check)
        {
            Vector4[] Run(int steps)
            {
                var te = MakeGrid_S2();
                te.BrushRadius = 4.0f;
                te.BrushHardness = 0.3f;
                te.BrushStrength = 0.7f;
                var a = new Vector3(-15, 0, 0);
                var b = new Vector3(15, 0, 0);
                te.BeginStrokeAt(1, a);
                for (int i = 1; i <= steps; i++) te.StrokeTo(Vector3.Lerp(a, b, (float)i / steps), 1);
                te.EndStrokeS2();
                return Colours_S2(te);
            }

            var slow = Run(200);
            var fast = Run(4);
            float worstDiff = 0.0f;
            for (int i = 0; i < slow.Length; i++) worstDiff = Math.Max(worstDiff, Math.Abs(slow[i].Z - fast[i].Z));
            check("terrain: a drag paints the same thing at 4 mouse events as at 200 (no dab spacing left)",
                  worstDiff < 0.002f, $"worst difference {worstDiff:0.0000}");

            float maxV = 0.0f;
            foreach (var c in slow) maxV = Math.Max(maxV, c.Z);
            check("terrain: one stroke moves a vertex at most `strength` toward the layer, never further",
                  maxV <= 0.7f + 1e-3f, $"strongest vertex {maxV:0.000} of 0.700");

            var teP = MakeGrid_S2();
            teP.BrushRadius = 4.0f; teP.BrushHardness = 0.3f; teP.BrushStrength = 1.0f;
            teP.BeginStrokeAt(1, new Vector3(-15, 0, 0));
            for (int i = 1; i <= 60; i++) teP.StrokeTo(Vector3.Lerp(new Vector3(-15, 0, 0), new Vector3(15, 0, 0), i / 60.0f), 1);
            teP.EndStrokeS2();

            static float Profile(float dist, float radius, float hard)
            {
                float x = dist / radius;
                if (x >= 1.0f) return 0.0f;
                float f = x <= hard ? 1.0f : 1.0f - (x - hard) / (1.0f - hard);
                return f * f * (3.0f - 2.0f * f);
            }

            float worstErr = 0.0f, worstRipple = 0.0f, lastW = float.MaxValue;
            var byY = new System.Collections.Generic.List<(float y, float w)>();
            foreach (var v in teP.Parts[0].Verts)
            {
                if (Math.Abs(v.Position.X) > 0.01f) continue;
                byY.Add((v.Position.Y, TerrainEditor.WeightsOf(new Vector3(v.Colour1.X, v.Colour1.Y, v.Colour1.Z)).Y));
            }
            byY.Sort((l, r) => Math.Abs(l.y).CompareTo(Math.Abs(r.y)));
            foreach (var (y, w) in byY)
            {
                worstErr = Math.Max(worstErr, Math.Abs(w - Profile(Math.Abs(y), 4.0f, 0.3f)));
                if (w > lastW + 1e-4f) worstRipple = Math.Max(worstRipple, w - lastW);
                lastW = w;
            }
            check("terrain: the stroke is exactly the brush profile - dabs no longer compound into a slab",
                  worstErr < 0.01f, $"worst error against the profile {worstErr:0.0000} over {byY.Count} vertices");
            check("terrain: the blend falls off monotonically away from the stroke (no blobs or ridges)",
                  worstRipple <= 1e-4f, $"worst rise away from the line {worstRipple:0.0000}");

            var teU = MakeGrid_S2();
            var before = Colours_S2(teU);
            teU.BeginStrokeAt(2, Vector3.Zero);
            teU.StrokeTo(new Vector3(5, 0, 0), 2);
            teU.EndStrokeS2();
            bool changed = false;
            var mid = Colours_S2(teU);
            for (int i = 0; i < mid.Length; i++) if ((mid[i] - before[i]).Length() > 1e-4f) { changed = true; break; }
            int steps = teU.History.Count;
            teU.History.Undo();
            var after = Colours_S2(teU);
            bool restored = true;
            for (int i = 0; i < after.Length; i++) if ((after[i] - before[i]).Length() > 1e-5f) { restored = false; break; }
            check("terrain: a swept stroke is one undo step, and undoing it restores every vertex",
                  changed && restored && steps == 1,
                  $"changed {changed} restored {restored} steps {steps}");
        }

        private static void TerrainSubdivideTest_S2(Action<string, bool, string> check)
        {
            var te = MakeGrid_S2(8, 2.0f);
            te.BrushRadius = 5.0f; te.BrushStrength = 1.0f; te.BrushHardness = 0.5f;
            te.BeginStrokeAt(1, new Vector3(-4, 0, 0));
            te.StrokeTo(new Vector3(4, 0, 0), 1);
            te.EndStrokeS2();
            var coverBefore = te.Coverage();
            int trisBefore = te.TriangleCount;
            float spacingBefore = te.VertexSpacing;

            bool ok = te.Subdivide(1000000, out string msg);
            var coverAfter = te.Coverage();
            check("terrain: subdivide makes four triangles out of one", ok && te.TriangleCount == trisBefore * 4,
                  $"{trisBefore} -> {te.TriangleCount} ({msg})");
            check("terrain: subdivide halves the vertex spacing, which is what makes a finer gradient possible",
                  te.VertexSpacing > 0 && Math.Abs(te.VertexSpacing - spacingBefore * 0.5f) < spacingBefore * 0.12f,
                  $"{spacingBefore:0.###} m -> {te.VertexSpacing:0.###} m");
            check("terrain: the painting survives subdivision",
                  Math.Abs(coverAfter.Y - coverBefore.Y) < 0.06f,
                  $"layer 1 coverage {coverBefore.Y:0.000} -> {coverAfter.Y:0.000}");

            check("terrain: the new midpoints are shared between neighbouring triangles",
                  te.VertexCount < trisBefore * 4 * 3 / 2,
                  $"{te.VertexCount:N0} vertices for {te.TriangleCount:N0} triangles");

            var big = MakeGrid_S2(8, 2.0f);
            bool refused = !big.Subdivide(10, out string why);
            check("terrain: subdivide refuses past its vertex budget and says what to do instead",
                  refused && !string.IsNullOrEmpty(why) && why.Contains("mask"), why ?? "it went ahead");
        }

        private static CodeWalker.GameFiles.Texture MakeTexture_S2(string name)
        {
            var px = new byte[4 * 4 * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 20; px[i + 1] = 160; px[i + 2] = 40; px[i + 3] = 255; }
            return new CodeWalker.GameFiles.Texture
            {
                Name = name,
                NameHash = JenkHash.GenHash(name),
                Width = 4, Height = 4, Depth = 1, Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = 4 * 4,
                Data = new TextureData { FullData = px },
            };
        }

        private static void TerrainEmbedTest_S2(Action<string, bool, string> check)
        {
            var te = MakeGrid_S2(4, 4.0f);
            te.ExportPreset = "terrain_cb_w_4lyr";
            var tex = MakeTexture_S2("s2_test_grass");
            te.Layers[0].Name = tex.Name;
            te.Layers[0].Texture = tex;
            te.Layers[0].Source = "disk";
            var gameTex = MakeTexture_S2("s2_test_gamerock");
            te.Layers[1].Name = gameTex.Name;
            te.Layers[1].Texture = gameTex;
            te.Layers[1].Source = "game";

            var ydr = TerrainYdr.Build(te, "s2_embed", null, null);
            int n = TerrainYdr.EmbedTextures(ydr.Drawable, te, null, null, false, out string note);
            check("terrain: embedding takes the layer that came off disk and leaves the game's own alone",
                  n == 1 && note != null, $"{n} embedded, note: {note ?? "none"}");

            var bytes = ydr.Save();
            var back = new YdrFile();
            back.Load(bytes);
            var dict = back.Drawable?.ShaderGroup?.TextureDictionary;
            var items = dict?.Textures?.data_items;
            bool found = false, hasPixels = false;
            if (items != null)
                foreach (var t in items)
                    if (t != null && t.Name == tex.Name) { found = true; hasPixels = (t.Data?.FullData?.Length ?? 0) > 0; }
            check("terrain: the embedded texture is inside the saved .ydr, with its pixels",
                  found && hasPixels, items == null ? "no dictionary at all" : $"{items.Length} texture(s) in the file");

            bool pointsAt = false;
            var shader = back.Drawable?.ShaderGroup?.Shaders?.data_items?[0];
            var ps = shader?.ParametersList?.Parameters;
            if (ps != null)
                foreach (var p in ps)
                    if (p.Data is CodeWalker.GameFiles.Texture t && t.NameHash == tex.NameHash) pointsAt = true;
            check("terrain: the material's layer sampler points at the embedded texture, not at a name",
                  pointsAt, pointsAt ? "" : "the sampler is still a plain name reference");

            var ydr2 = TerrainYdr.Build(te, "s2_embed2", null, null);
            int n2 = TerrainYdr.EmbedTextures(ydr2.Drawable, te, null, null, true, out _);
            check("terrain: 'include the game's own textures' embeds those as well", n2 == 2, n2 + " embedded");
        }
    }
}

