using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static partial class ShaderPresets
    {
        static partial void TerrainParams_R4(string name, ShaderTemplate t, ref bool isTerrain);

        static partial void TerrainParams_R4(string name, ShaderTemplate t, ref bool isTerrain)
        {
            if (name == null || !name.Contains("4lyr")) return;
            isTerrain = true;

            t.Params.Add(new PresetParam(ShaderParamNames.TextureSampler_layer0));
            t.Params.Add(new PresetParam(ShaderParamNames.TextureSampler_layer1));
            t.Params.Add(new PresetParam(ShaderParamNames.TextureSampler_layer2));
            t.Params.Add(new PresetParam(ShaderParamNames.TextureSampler_layer3));

            if (!name.Contains("_lod"))
            {
                t.Params.Add(new PresetParam(ShaderParamNames.BumpSampler_layer0));
                t.Params.Add(new PresetParam(ShaderParamNames.BumpSampler_layer1));
                t.Params.Add(new PresetParam(ShaderParamNames.BumpSampler_layer2));
                t.Params.Add(new PresetParam(ShaderParamNames.BumpSampler_layer3));
            }
            if (name.Contains("_cm") || name.Contains("_2tex_blend"))
                t.Params.Add(new PresetParam(ShaderParamNames.lookupSampler));
        }
    }
}

