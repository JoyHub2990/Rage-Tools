using System;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static ShaderFX WaterShaderWithBumpiness_V60(uint sps, float bumpiness)
        {
            return new ShaderFX
            {
                FileName = new MetaHash(sps),
                Name = new MetaHash(sps),
                ParametersList = new ShaderParametersBlock
                {
                    Parameters = new[]
                    {
                        new ShaderParameter { DataType = 1, Data = new Vector4(bumpiness, 0, 0, 0) },
                    },
                    Hashes = new[] { (MetaName)ShaderParamNames.bumpiness },
                },
            };
        }

        private void SeqTest_WaterBumpiness_V60(Action<string, bool, string> check)
        {
            try
            {
                const uint waterSps = 1529202445u;

                var mesh = new RenderMesh { Shader = WaterShaderWithBumpiness_V60(waterSps, 1.0f) };
                modelRenderer.RefreshMaterial(mesh);

                check("v60 water: a water material is classified as water",
                      mesh.AlphaMode == GeomAlphaMode.Water, mesh.AlphaMode.ToString());

                check("v60 water: its own bumpiness never lands on the body-depth field",
                      Math.Abs(mesh.Bumpiness) < 0.0001f,
                      $"Bumpiness {mesh.Bumpiness} (1.0 here would discard every pool over a metre deep)");

                var solid = new RenderMesh { Shader = WaterShaderWithBumpiness_V60(0u, 0.75f) };
                modelRenderer.RefreshMaterial(solid);
                check("v60 water: ...while ordinary materials still take their bumpiness",
                      solid.AlphaMode != GeomAlphaMode.Water && Math.Abs(solid.Bumpiness - 0.75f) < 0.0001f,
                      $"{solid.AlphaMode}, bumpiness {solid.Bumpiness}");

                // every sps CodeWalker routes to its water shader, both passes
                var cw = new (uint Sps, string Name)[]
                {
                    (1529202445u, "water_river"), (4064804434u, "water_riverlod"),
                    (2871265627u, "water_riverocean"), (1507348828u, "water_rivershallow"),
                    (3945561843u, "water_fountain"), (4234404348u, "water_shallow"),
                    (1077877097u, "water_poolenv"), (3053856997u, "water_riverfoam"),
                    (3066724854u, "water_terrainfoam"), (1471966282u, "water_decal"),
                };
                string missed = "";
                foreach (var (sps, nm) in cw)
                {
                    var m = new RenderMesh { Shader = WaterShaderWithBumpiness_V60(sps, 1.0f) };
                    modelRenderer.RefreshMaterial(m);
                    if (m.AlphaMode != GeomAlphaMode.Water) missed += " " + nm;
                }
                check("v60 water: every shader CodeWalker draws as water is drawn as water here",
                      missed.Length == 0, missed.Length == 0 ? $"all {cw.Length}" : "not water:" + missed);

                bool shapes = Editor.WorldWater.QuadIndices_V60(0).Length == 6;
                for (int type = 1; type <= 4; type++)
                    if (Editor.WorldWater.QuadIndices_V60(type).Length != 3) shapes = false;
                check("v60 water: a cut-corner quad keeps its shape instead of becoming a rectangle",
                      shapes,
                      $"type 0 -> {Editor.WorldWater.QuadIndices_V60(0).Length} indices, " +
                      $"types 1-4 -> {Editor.WorldWater.QuadIndices_V60(3).Length}");
            }
            catch (Exception ex) { check("v60 water", false, ex.Message); }
        }
    }
}
