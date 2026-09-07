using CodeWalker.GameFiles;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class ShaderPresets
    {
        public static bool PedFurParams_V38(string name, ShaderTemplate t)
        {
            if (t == null || !string.Equals(name, "ped_fur", System.StringComparison.OrdinalIgnoreCase)) return false;

            t.Bucket = 0;

            t.Params.Add(new PresetParam(ShaderParamNames.DiffuseSampler));
            t.Params.Add(new PresetParam(ShaderParamNames.TextureSamplerDiffPal));
            t.Params.Add(new PresetParam(ShaderParamNames.NoiseSampler));
            t.Params.Add(new PresetParam(ShaderParamNames.BumpSampler));
            t.Params.Add(new PresetParam(ShaderParamNames.SpecSampler));

            t.Params.Add(new PresetParam(ShaderParamNames.furLength, new SDX.Vector4(0.15f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furMaxLayers, new SDX.Vector4(15f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furMinLayers, new SDX.Vector4(2f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furNoiseUVScale, new SDX.Vector4(15f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furAttenCoef, new SDX.Vector4(1.21f, -0.22f, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furSelfShadowMin, new SDX.Vector4(0.45f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furAOBlend, new SDX.Vector4(1f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furStiffness, new SDX.Vector4(0.5f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.furBendParams, SDX.Vector4.Zero));
            t.Params.Add(new PresetParam(ShaderParamNames.furGlobalParams, new SDX.Vector4(0, 0, 0.0039f, 0)));

            t.Params.Add(new PresetParam(ShaderParamNames.envEffFatThickness, new SDX.Vector4(25f, 25f, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.orderNumber, SDX.Vector4.Zero));
            t.Params.Add(new PresetParam(ShaderParamNames.specularIntensityMult, new SDX.Vector4(1f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.specularFalloffMult, new SDX.Vector4(250f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.specularFresnel, new SDX.Vector4(0.96f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.bumpiness, new SDX.Vector4(1f, 0, 0, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.detailSettings, new SDX.Vector4(0.1f, 0.75f, 40f, 0)));
            t.Params.Add(new PresetParam(ShaderParamNames.StubbleControl, new SDX.Vector4(2f, 0.6f, 0, 0)));
            return true;
        }
    }
}

