using System;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_Fur_U20(Action<string, bool, string> check)
        {
            try
            {
                check("u20 fur: shells stand step x layer off the surface, layer 0 on it",
                      FurMath_U20.ShellOffset(0.008f, 0) == 0f && Math.Abs(FurMath_U20.ShellOffset(0.008f, 7) - 0.056f) < 1e-6f, "");
                float mid = (float)Math.Sqrt((25f * 25f + 10f * 10f) * 0.5f);
                check("u20 fur: the distance fade is the game's squared ramp - full at 10 m, gone at 25 m, half way between the squares",
                      FurMath_U20.FadeAlpha(5f, 10f, 25f) == 1f && FurMath_U20.FadeAlpha(30f, 10f, 25f) == 0f && Math.Abs(FurMath_U20.FadeAlpha(mid, 10f, 25f) - 0.5f) < 1e-3f,
                      FurMath_U20.FadeAlpha(mid, 10f, 25f).ToString("0.###"));
                check("u20 fur: painted-out vertices lose every shell, layer 0 included",
                      !FurMath_U20.ShellSurvives(1f, 0f, 0f) && FurMath_U20.ShellSurvives(1f, 0f, 1f) && FurMath_U20.ShellSurvives(0.5f, 10f / 255f, 1f) && !FurMath_U20.ShellSurvives(0.03f, 10f / 255f, 1f), "");
                check("u20 fur: the shade is the layer's value up close and goes to the far shade with distance or painted alpha",
                      Math.Abs(FurMath_U20.Shade(1f, 0.35f, 1f, 1f) - 0.35f) < 1e-6f && Math.Abs(FurMath_U20.Shade(1f, 0.35f, 0f, 1f) - 1f) < 1e-6f &&
                      Math.Abs(FurMath_U20.Shade(1f, 0.35f, 1f, 0f) - 1f) < 1e-6f && Math.Abs(FurMath_U20.Shade(0.7f, 0.5f, 0.5f, 1f) - 0.6f) < 1e-6f, "");
                check("u20 fur: two shells per height texture - even layers from green, odd from alpha",
                      FurMath_U20.ComboSlot(0) == 0 && FurMath_U20.ComboSlot(1) == 0 && FurMath_U20.ComboSlot(6) == 3 && FurMath_U20.ComboSlot(7) == 3 && !FurMath_U20.ComboUsesAlpha(0) && FurMath_U20.ComboUsesAlpha(1), "");
                check("u20 fur: the defaults are the shader's own declared values",
                      Math.Abs(FurMath_U20.DefaultLayerParams.X - 0.008f) < 1e-6f && FurMath_U20.DefaultLayerParams.W == 1f && FurMath_U20.DefaultAlphaDistance == new Vector2(10f, 25f) &&
                      Math.Abs(FurMath_U20.DefaultShadow03.X - 90f / 255f) < 1e-6f && FurMath_U20.DefaultShadow47.W == 1f && FurMath_U20.DefaultAlphaClip03.X == 0f && FurMath_U20.DefaultUvScales == Vector4.One, "");

                var m = new RenderMesh();
                ModelRenderer.ClassifyDrawPublic(m, new ShaderFX { Name = new MetaHash(908546335u), FileName = new MetaHash(3333227093u), RenderBucket = 3 });
                var mm = new RenderMesh();
                ModelRenderer.ClassifyDrawPublic(mm, new ShaderFX { Name = new MetaHash(3668098687u), FileName = new MetaHash(4256676773u), RenderBucket = 3 });
                check("u20 fur: grass_fur and grass_fur_mask are drawn again, in the cutout pass, single sided",
                      !m.NeverDraw && m.AlphaMode == GeomAlphaMode.Cutout && !m.DoubleSided && !mm.NeverDraw && mm.AlphaMode == GeomAlphaMode.Cutout,
                      $"{m.NeverDraw} {m.AlphaMode} {mm.NeverDraw}");
                check("u20 fur: the four sps of the family are known and ped_fur is not one of them",
                      ModelRenderer.IsGrassFurSps_U20(3333227093u) && ModelRenderer.IsGrassFurSps_U20(4256676773u) && ModelRenderer.IsGrassFurSps_U20(1483370959u) &&
                      ModelRenderer.IsGrassFurSps_U20(3895559374u) && !ModelRenderer.IsGrassFurSps_U20(251230834u), "");
                check("u20 fur: only the mask variant packs its third UV set, and the LOD variant is plain ground",
                      FurMath_U20.IsMaskShader("grass_fur_mask") && !FurMath_U20.IsMaskShader("grass_fur") && FurMath_U20.IsLodShader("grass_fur_lod") && !FurMath_U20.IsLodShader("grass_fur_tnt"), "");

                check("u20 fur: the material editor names every fur texture and parameter",
                      MaterialDefs.Tex((uint)ShaderParamNames.ComboHeightSamplerFur01) != null && MaterialDefs.Tex((uint)ShaderParamNames.FurMaskSampler) != null &&
                      MaterialDefs.Tex((uint)ShaderParamNames.DiffuseHfSampler) != null && MaterialDefs.Param((uint)ShaderParamNames.furShadow03)?.Kind == MatParamKind.Float4 &&
                      MaterialDefs.Param((uint)ShaderParamNames.furAlphaDistance)?.Kind == MatParamKind.Float2 && MaterialDefs.Param((uint)ShaderParamNames.furLayerParams)?.SubLabels?[0] == "Step (m)", "");
                var t = ShaderPresets.Template("grass_fur_mask");
                check("u20 fur: a grass_fur_mask template declares the height maps, the mask, the area diffuse and the shell parameters",
                      t != null && t.Declares((uint)ShaderParamNames.ComboHeightSamplerFur67) && t.Declares((uint)ShaderParamNames.FurMaskSampler) && t.Declares((uint)ShaderParamNames.DiffuseHfSampler) &&
                      t.Declares((uint)ShaderParamNames.furShadow47) && t.Declares((uint)ShaderParamNames.furAlphaDistance) && t.Bucket == 3,
                      t == null ? "null" : t.Params.Count + " params");

                var live = new ShaderFX
                {
                    Name = new MetaHash(908546335u),
                    FileName = new MetaHash(3333227093u),
                    ParametersList = new ShaderParametersBlock
                    {
                        Parameters = new[]
                        {
                            new ShaderParameter { DataType = 1, Data = new Vector4(0.35f, 0.46f, 0.58f, 0.66f) },
                            new ShaderParameter { DataType = 1, Data = new Vector4(0.008f, 0f, 0f, 1f) },
                        },
                        Hashes = new[] { (MetaName)(uint)ShaderParamNames.furShadow03, (MetaName)(uint)ShaderParamNames.furLayerParams },
                    },
                };
                var lines = AsiLines_U16("prop_lawn", "", 0, live).ToList();
                check("u20 fur: the live link sends the fur shading and shell step to the game plugin as plain floats",
                      lines.Count == 2 && lines[0].line.Contains(" 1104745269 1 0.35 0.46 0.58 0.66") && lines[1].line.Contains(" 1122745807 1 0.008 0 0 1"),
                      lines.Count > 0 ? lines[0].line : "none");
            }
            catch (Exception ex) { check("u20 fur: no exception", false, ex.ToString()); }
        }
    }
}
