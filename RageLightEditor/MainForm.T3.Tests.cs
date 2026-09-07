using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_T3(Action<string, bool, string> check)
        {
            TerrainExportIsSelfContained_T3(check);
            TerrainExportRoundTrip_T3(check);
        }

        private static GameTexture MakeTex_T3(string name, byte r, byte g, byte b)
        {
            const int w = 4, h = 4;
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
            return new GameTexture
            {
                Name = name,
                NameHash = JenkHash.GenHash(name),
                Width = w,
                Height = h,
                Depth = 1,
                Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = w * 4,
                Data = new TextureData { FullData = px },
            };
        }

        private static TerrainEditor MakePaintedGrid_T3()
        {
            var te = new TerrainEditor();
            const int cells = 16;
            var verts = new MeshVertex[(cells + 1) * (cells + 1)];
            for (int y = 0; y <= cells; y++)
                for (int x = 0; x <= cells; x++)
                    verts[y * (cells + 1) + x] = new MeshVertex
                    {
                        Position = new Vector3(x - cells * 0.5f, y - cells * 0.5f, 0),
                        Normal = Vector3.UnitZ,
                        Tangent = new Vector4(1, 0, 0, 1),
                        Colour0 = Vector4.One,
                        Colour1 = new Vector4(0, 0, 0, 1),
                        UV0 = new Vector2((float)x / cells, (float)y / cells),
                        UV1 = new Vector2((float)x / cells, (float)y / cells),
                    };
            var idx = new List<ushort>();
            int w = cells + 1;
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    int a = y * w + x, b = a + 1, c = a + w, d = c + 1;
                    idx.Add((ushort)a); idx.Add((ushort)b); idx.Add((ushort)c);
                    idx.Add((ushort)b); idx.Add((ushort)d); idx.Add((ushort)c);
                }
            te.Parts.Add(new TerrainEditor.Part { Verts = verts, Indices = idx.ToArray() });
            te.RebuildBounds();

            var names = new[] { "t3_seq_l0", "t3_seq_l1", "t3_seq_l2", "t3_seq_l3" };
            var cols = new byte[][] { new byte[] { 40, 160, 40 }, new byte[] { 200, 40, 40 },
                                      new byte[] { 40, 60, 200 }, new byte[] { 230, 210, 60 } };
            for (int i = 0; i < TerrainEditor.LayerCount; i++)
            {
                var t = MakeTex_T3(names[i], cols[i][0], cols[i][1], cols[i][2]);
                te.Layers[i].Name = t.Name;
                te.Layers[i].Texture = t;
                te.Layers[i].Source = "disk";
            }

            te.BrushStrength = 1.0f;
            te.BrushRadius = 2.0f;
            te.BeginStroke(1);
            for (float x = -8; x <= 8; x += 1.0f) te.Paint(new Vector3(x, 0, 0), 1);
            te.EndStroke();
            te.BrushRadius = 3.0f;
            te.BeginStroke(2);
            te.Paint(new Vector3(4, -5, 0), 2);
            te.EndStroke();
            return te;
        }

        private void TerrainExportIsSelfContained_T3(Action<string, bool, string> check)
        {
            var te = MakePaintedGrid_T3();
            check("terrain export: the tool defaults to a self-contained .ydr, not a .ytd nothing reads",
                  new TerrainEditor().TextureExport == TerrainEditor.TexDestination.Embed,
                  new TerrainEditor().TextureExport.ToString());

            var ydr = TerrainYdr.Build(te, "t3_seq", null, null);
            TerrainYdr.EmbedTextures(ydr.Drawable, te, null, null, false, out _);
            var bytes = ydr.Save();

            var checks = TerrainYdr.Verify(bytes, out string sname, out int blended, out int verts, out bool hasC1);
            int inFile = 0;
            foreach (var c in checks) if (c.InFile) inFile++;
            check("terrain export: the file's four layer samplers all carry their pixels IN the .ydr",
                  checks.Count == 4 && inFile == 4, $"{inFile}/{checks.Count} in the file, shader {sname}");

            check("terrain export: the blend channel (COLOUR1) is in the vertex declaration",
                  hasC1, hasC1 ? "present" : "ABSENT - the blend has nowhere to live");

            check("terrain export: the painted pattern is in the file, not a blank sheet",
                  blended > 0 && blended < verts, $"{blended} of {verts} vertices carry a blend");

            TerrainYdr.Describe(checks, n => true, out int distinct);
            check("terrain export: four assigned layers stay four different textures in the file",
                  distinct == 4, distinct + " distinct layer texture(s)");

            var one = MakePaintedGrid_T3();
            for (int i = 1; i < TerrainEditor.LayerCount; i++) one.Layers[i].Clear();
            var oneYdr = TerrainYdr.Build(one, "t3_seq_one", null, null);
            var oneChecks = TerrainYdr.Verify(oneYdr.Save(), out _, out _, out _, out _);
            var said = TerrainYdr.Describe(oneChecks, n => true, out int oneDistinct);
            check("terrain export: 'all four layers name the same texture' is said out loud",
                  oneDistinct == 1 && said.Contains("SAME"), said);
        }

        private void TerrainExportRoundTrip_T3(Action<string, bool, string> check)
        {
            var te = MakePaintedGrid_T3();
            var before = te.Coverage();
            var ydr = TerrainYdr.Build(te, "t3_seq_rt", null, null);
            TerrainYdr.EmbedTextures(ydr.Drawable, te, null, null, false, out _);
            var bytes = ydr.Save();

            var back = new YdrFile();
            back.Load(bytes);
            var local = TerrainYdr.LocalTextures(back.Drawable, null);
            check("terrain reopen: the file hands back the textures it carries",
                  local.Count == 4, local.Count + " texture(s) found in the drawable itself");

            var shader = back.Drawable?.ShaderGroup?.Shaders?.data_items?[0];
            var want = new[]
            {
                ShaderParamNames.TextureSampler_layer0, ShaderParamNames.TextureSampler_layer1,
                ShaderParamNames.TextureSampler_layer2, ShaderParamNames.TextureSampler_layer3,
            };
            int matched = 0;
            var ps = shader?.ParametersList?.Parameters;
            var hs = shader?.ParametersList?.Hashes;
            for (int i = 0; ps != null && i < ps.Length; i++)
            {
                if (!(ps[i].Data is TextureBase tb) || string.IsNullOrEmpty(tb.Name)) continue;
                foreach (var wname in want)
                    if ((uint)hs[i] == (uint)wname && local.ContainsKey(JenkHash.GenHash(tb.Name.ToLowerInvariant())))
                        matched++;
            }
            check("terrain reopen: every layer sampler resolves out of the file itself",
                  matched == 4, matched + "/4 resolved without asking the game archives");

            var geoms = back.Drawable?.DrawableModels?.High?[0]?.Geometries;
            var cov = Vector4.Zero;
            int n = 0;
            if (geoms != null)
                foreach (var g in geoms)
                {
                    var dv = VertexDecoder.Decode(g.VertexData, g.IndexBuffer?.Indices);
                    if (dv == null) continue;
                    foreach (var v in dv)
                    { cov += TerrainEditor.WeightsOf(new Vector3(v.Colour1.X, v.Colour1.Y, v.Colour1.Z)); n++; }
                }
            if (n > 0) cov /= n;
            float worst = Math.Max(Math.Max(Math.Abs(cov.X - before.X), Math.Abs(cov.Y - before.Y)),
                                   Math.Max(Math.Abs(cov.Z - before.Z), Math.Abs(cov.W - before.W)));
            check("terrain reopen: the painted coverage comes back the same (to within a byte of colour)",
                  n > 0 && worst < 0.01f,
                  $"painted {before.X:0.000}/{before.Y:0.000}/{before.Z:0.000}/{before.W:0.000} -> " +
                  $"read {cov.X:0.000}/{cov.Y:0.000}/{cov.Z:0.000}/{cov.W:0.000}, worst {worst:0.0000}");
        }
    }
}

