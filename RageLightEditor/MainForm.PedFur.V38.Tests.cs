using System;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_PedFur_V38(Action<string, bool, string> check)
        {
            check("v38 ped_fur: the shader is recognised, and the hair shaders are not",
                  ModelRenderer.IsPedFurShader_V38("ped_fur") &&
                  !ModelRenderer.IsPedFurShader_V38("ped_hair_cutout_alpha") &&
                  !ModelRenderer.IsPedFurShader_V38("grass_fur"),
                  "ped_fur only");

            int near = ModelRenderer.PedFurLayers_V38(2, 15, 0.5f);
            int mid = ModelRenderer.PedFurLayers_V38(2, 15, 14.0f);
            int far = ModelRenderer.PedFurLayers_V38(2, 15, 100.0f);
            check("v38 ped_fur: the layer count is the material's own range, chosen by distance",
                  near == 15 && far == 2 && mid > far && mid < near,
                  $"{near} up close, {mid} at 14 m, {far} far off");

            float c0 = ModelRenderer.PedFurClip_V38(new SharpDX.Vector4(1.21f, -0.22f, 0, 0), 0, 8);
            float c7 = ModelRenderer.PedFurClip_V38(new SharpDX.Vector4(1.21f, -0.22f, 0, 0), 7, 8);
            check("v38 ped_fur: the strand cut rises from root to tip, which is what tapers the coat",
                  c7 > c0 && c0 >= 0f && c0 < 0.05f && c7 > 0.9f && c7 <= 0.995f,
                  $"clip {c0:0.####} at the root, {c7:0.####} at the tip");

            float s0 = ModelRenderer.PedFurShadow_V38(0.45f, 1.0f, 0, 8);
            float s7 = ModelRenderer.PedFurShadow_V38(0.45f, 1.0f, 7, 8);
            check("v38 ped_fur: the root is shadowed and the tip is lit (furSelfShadowMin)",
                  s0 > 0.4f && s0 < 0.7f && Math.Abs(s7 - 1.0f) < 0.001f,
                  $"{s0:0.###} at the root, {s7:0.###} at the tip");

            float noAo = ModelRenderer.PedFurShadow_V38(0.45f, 0.0f, 0, 8);
            check("v38 ped_fur: furAOBlend 0 leaves the shading alone instead of inverting it",
                  Math.Abs(noAo - 1.0f) < 0.001f, noAo.ToString("0.###"));

            var t = ShaderPresets.Template("ped_fur");
            var names = t?.Params?.Select(p => ((ShaderParamNames)p.Hash).ToString()).ToList()
                        ?? new System.Collections.Generic.List<string>();
            var want = new[] { "furLength", "furMaxLayers", "furMinLayers", "furNoiseUVScale",
                               "furAttenCoef", "furSelfShadowMin", "furAOBlend", "furStiffness",
                               "NoiseSampler", "DiffuseSampler", "BumpSampler", "SpecSampler" };
            var missing = want.Where(w => !names.Contains(w)).ToList();
            check("v38 ped_fur: the material editor offers every fur parameter the shader declares",
                  missing.Count == 0 && names.Count >= 23,
                  missing.Count == 0 ? $"{names.Count} parameter(s)" : "missing: " + string.Join(", ", missing));

            var raw = want.Where(w => Enum.TryParse<ShaderParamNames>(w, out var pn) &&
                                      MaterialDefs.Param((uint)pn) == null).ToList();
            check("v38 ped_fur: ...and names them rather than showing raw float4s",
                  raw.Count <= 4, raw.Count == 0 ? "all named" : "raw: " + string.Join(", ", raw));

            check("u6 fur: the modelled coat IS the outer extent - shells sink inward, never outward",
                  ModelRenderer.PedFurShellOffset_U6(0.15f, 13, 15) < 0.0f &&
                  Math.Abs(ModelRenderer.PedFurShellOffset_U6(0.15f, 13, 15) + 0.01f) < 0.001f &&
                  Math.Abs(ModelRenderer.PedFurShellOffset_U6(0.15f, 0, 15) + 0.14f) < 0.001f,
                  $"innermost {ModelRenderer.PedFurShellOffset_U6(0.15f, 0, 15):0.###} m, next-to-outer {ModelRenderer.PedFurShellOffset_U6(0.15f, 13, 15):0.###} m");

            check("u6 fur: the outermost shell lands exactly on the modelled surface",
                  Math.Abs(ModelRenderer.PedFurShellOffset_U6(0.15f, 14, 15)) < 0.011f &&
                  ModelRenderer.PedFurShellOffset_U6(0.25f, 9, 10) <= 0.0f,
                  ModelRenderer.PedFurShellOffset_U6(0.15f, 14, 15).ToString("0.####") + " m from the coat");

            var cache = gameFiles?.Cache;
            if (cache?.YddDict == null || !gameFiles.Ready)
            { Console.WriteLine("  v38 ped_fur: (the cat check skipped - no game folder)"); return; }

            if (!cache.YddDict.TryGetValue(JenkHash.GenHash("a_c_cat_01"), out var fe) || fe == null)
            { Console.WriteLine("  v38 ped_fur: (a_c_cat_01 not in this install)"); return; }

            try
            {
                var ydd = cache.RpfMan.GetFile<YddFile>(fe);
                var drawables = Scene.DrawablesInYdd_V38(ydd);
                check("v38 ydd: a .ydd opens as several drawables, one prop each",
                      drawables.Count > 1, $"{drawables.Count} drawable(s) in a_c_cat_01.ydd");

                var furMat = drawables
                    .SelectMany(d => d.Drawable?.ShaderGroup?.Shaders?.data_items ?? Array.Empty<ShaderFX>())
                    .FirstOrDefault(sh => ModelRenderer.IsPedFurShader_V38(sh?.Name.ToString()));
                check("v38 ped_fur: the cat really does carry a ped_fur material",
                      furMat != null, furMat == null ? "none found" : furMat.Name.ToString());

                if (furMat != null)
                {
                    float len = 0; int maxL = 0;
                    var ps = furMat.ParametersList?.Parameters; var hs = furMat.ParametersList?.Hashes;
                    for (int i = 0; ps != null && hs != null && i < ps.Length && i < hs.Length; i++)
                    {
                        var v = ps[i].Data as SharpDX.Vector4? ?? SharpDX.Vector4.Zero;
                        if ((ShaderParamNames)(uint)hs[i] == ShaderParamNames.furLength) len = v.X;
                        if ((ShaderParamNames)(uint)hs[i] == ShaderParamNames.furMaxLayers) maxL = (int)v.X;
                    }
                    check("v38 ped_fur: ...with the length and layer count this build was written against",
                          Math.Abs(len - 0.15f) < 0.001f && maxL == 15,
                          $"furLength {len:0.###} m, furMaxLayers {maxL}");
                }
            }
            catch (Exception ex) { check("v38 ped_fur: reading the cat", false, ex.Message); }
        }
    }
}

