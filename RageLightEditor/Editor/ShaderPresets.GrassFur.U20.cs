using System;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class ShaderPresets
    {
        public static bool GrassFurParams_U20(string name, ShaderTemplate t)
        {
            if (t == null || string.IsNullOrEmpty(name) || !name.StartsWith("grass_fur", StringComparison.OrdinalIgnoreCase)) return false;
            bool mask = FurMath_U20.IsMaskShader(name);
            bool lod = FurMath_U20.IsLodShader(name);
            bool tint = name.EndsWith("_tnt", StringComparison.OrdinalIgnoreCase);
            t.Bucket = 3;

            t.Params.Add(new PresetParam(ShaderParamNames.DiffuseSampler));
            t.Params.Add(new PresetParam(ShaderParamNames.BumpSampler));
            if (!lod) t.Params.Add(new PresetParam(ShaderParamNames.SpecSampler));
            if (lod)
            {
                t.Params.Add(new PresetParam(ShaderParamNames.DiffuseSamplerFur));
                t.Params.Add(new PresetParam(ShaderParamNames.BumpSamplerFur));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur2));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur3));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur4));
                t.Params.Add(new PresetParam(ShaderParamNames.StippleSampler));
                t.Params.Add(new PresetParam(ShaderParamNames.furLayerParams, new SDX.Vector4(1f, 0.01f, 1f / 255f, 0f)));
                t.Params.Add(new PresetParam(ShaderParamNames.furLayerParams2, new SDX.Vector4(0f, 0f, 0.0025f, 0.00125f)));
                t.Params.Add(new PresetParam(ShaderParamNames.furLayerParams3, new SDX.Vector4(1f, 1f, 1f, 0f)));
            }
            else
            {
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur01));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur23));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur45));
                t.Params.Add(new PresetParam(ShaderParamNames.ComboHeightSamplerFur67));
                if (mask)
                {
                    t.Params.Add(new PresetParam(ShaderParamNames.FurMaskSampler));
                    t.Params.Add(new PresetParam(ShaderParamNames.DiffuseHfSampler));
                }
                t.Params.Add(new PresetParam(ShaderParamNames.StippleSampler));
                if (tint) t.Params.Add(new PresetParam(ShaderParamNames.TintPaletteSampler));
                t.Params.Add(new PresetParam(ShaderParamNames.furLayerParams, FurMath_U20.DefaultLayerParams));
                t.Params.Add(new PresetParam(ShaderParamNames.furUvScales, FurMath_U20.DefaultUvScales));
                t.Params.Add(new PresetParam(ShaderParamNames.furAlphaDistance, new SDX.Vector4(FurMath_U20.DefaultAlphaDistance.X, FurMath_U20.DefaultAlphaDistance.Y, 0f, 0f)));
                t.Params.Add(new PresetParam(ShaderParamNames.furAlphaClip03, FurMath_U20.DefaultAlphaClip03));
                t.Params.Add(new PresetParam(ShaderParamNames.furAlphaClip47, FurMath_U20.DefaultAlphaClip47));
                t.Params.Add(new PresetParam(ShaderParamNames.furShadow03, FurMath_U20.DefaultShadow03));
                t.Params.Add(new PresetParam(ShaderParamNames.furShadow47, FurMath_U20.DefaultShadow47));
                t.Params.Add(new PresetParam(ShaderParamNames.wetnessMultiplier, new SDX.Vector4(1f, 0f, 0f, 0f)));
            }

            t.Params.Add(new PresetParam(ShaderParamNames.HardAlphaBlend, new SDX.Vector4(1f, 0f, 0f, 0f)));
            t.Params.Add(new PresetParam(ShaderParamNames.useTessellation, SDX.Vector4.Zero));
            t.Params.Add(new PresetParam(ShaderParamNames.bumpiness, new SDX.Vector4(1f, 0f, 0f, 0f)));
            if (!lod) t.Params.Add(new PresetParam(ShaderParamNames.specMapIntMask, new SDX.Vector4(1f, 0f, 0f, 0f)));
            t.Params.Add(new PresetParam(ShaderParamNames.specularIntensityMult, new SDX.Vector4(1f, 0f, 0f, 0f)));
            t.Params.Add(new PresetParam(ShaderParamNames.specularFalloffMult, new SDX.Vector4(32f, 0f, 0f, 0f)));
            t.Params.Add(new PresetParam(ShaderParamNames.specularFresnel, new SDX.Vector4(0.96f, 0f, 0f, 0f)));
            return true;
        }
    }
}
