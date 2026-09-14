using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class MaterialDefs
    {
        private static void AddFurDefs_U20()
        {
            AddTex(ShaderParamNames.ComboHeightSamplerFur01, MatTexRole.Other, 10, "Fur height 0-1",
                "Shell heights for layers 0 and 1: green is layer 0, alpha is layer 1. A shell keeps the\n" +
                "pixels whose height beats that layer's clip, so this is the shape of the grass.");
            AddTex(ShaderParamNames.ComboHeightSamplerFur23, MatTexRole.Other, 11, "Fur height 2-3",
                "Shell heights for layers 2 (green) and 3 (alpha).");
            AddTex(ShaderParamNames.ComboHeightSamplerFur45, MatTexRole.Other, 12, "Fur height 4-5",
                "Shell heights for layers 4 (green) and 5 (alpha).");
            AddTex(ShaderParamNames.ComboHeightSamplerFur67, MatTexRole.Other, 13, "Fur height 6-7",
                "Shell heights for layers 6 (green) and 7 (alpha).");
            AddTex(ShaderParamNames.FurMaskSampler, MatTexRole.Other, 14, "Fur mask",
                "grass_fur_mask: where the grass grows (red channel), on the second UV set.\n" +
                "It multiplies the vertex alpha - black or alpha 0 means bare ground, no shells at all.");
            AddTex(ShaderParamNames.DiffuseHfSampler, MatTexRole.Other, 15, "Area diffuse",
                "grass_fur_mask: the large-scale colour on the third UV set, multiplied into the tiled\n" +
                "diffuse. Without it a lawn is one flat tiled texture.");
            AddTex(ShaderParamNames.StippleSampler, MatTexRole.Other, 16, "Stipple",
                "The game's dither pattern for the distance fade. The editor dithers with its own\n" +
                "ordered pattern, so this is kept for the file and not read.");

            AddParam(ShaderParamNames.furLayerParams, "Fur shells", MatParamKind.Float4, 0f, 2f,
                FurMath_U20.DefaultLayerParams,
                "Step: how far each shell stands off the one below, in metres - eight shells sit 0 to 7\n" +
                "steps along the normal, so the grass is 7 x step tall. Far shade: what the shells' shading\n" +
                "fades to with distance (1 = unshaded).",
                new[] { "Step (m)", "unused", "unused", "Far shade" });
            AddParam(ShaderParamNames.furUvScales, "Fur tiling", MatParamKind.Float4, 0f, 64f,
                FurMath_U20.DefaultUvScales,
                "How many times each texture repeats over the UVs: the diffuse, the height maps, the\n" +
                "normal map and, on grass_fur_mask, the area diffuse.",
                new[] { "Diffuse", "Heights", "Normal", "Area" });
            AddParam(ShaderParamNames.furAlphaDistance, "Fur fade (m)", MatParamKind.Float2, 0f, 500f,
                new Vector4(FurMath_U20.DefaultAlphaDistance.X, FurMath_U20.DefaultAlphaDistance.Y, 0, 0),
                "Shells are full inside the first distance and dithered away by the second - what keeps\n" +
                "eight passes affordable. Past the second only the ground under the grass remains.",
                new[] { "Full", "Gone" });
            AddParam(ShaderParamNames.furAlphaClip03, "Fur clip 0-3", MatParamKind.Float4, 0f, 1f,
                FurMath_U20.DefaultAlphaClip03,
                "The height a pixel needs to survive on shells 0 to 3. Rising values thin the grass\n" +
                "towards the tips; layer 0 at 0 keeps the whole ground.",
                new[] { "Layer 0", "Layer 1", "Layer 2", "Layer 3" });
            AddParam(ShaderParamNames.furAlphaClip47, "Fur clip 4-7", MatParamKind.Float4, 0f, 1f,
                FurMath_U20.DefaultAlphaClip47,
                "The height a pixel needs to survive on shells 4 to 7.",
                new[] { "Layer 4", "Layer 5", "Layer 6", "Layer 7" });
            AddParam(ShaderParamNames.furShadow03, "Fur shade 0-3", MatParamKind.Float4, 0f, 1f,
                FurMath_U20.DefaultShadow03,
                "How lit shells 0 to 3 are. The root sits in the shade of the blades above it, the tip\n" +
                "in full light - the self-shadowing that makes the pile read as depth.",
                new[] { "Layer 0", "Layer 1", "Layer 2", "Layer 3" });
            AddParam(ShaderParamNames.furShadow47, "Fur shade 4-7", MatParamKind.Float4, 0f, 1f,
                FurMath_U20.DefaultShadow47,
                "How lit shells 4 to 7 are; 1 on the last one.",
                new[] { "Layer 4", "Layer 5", "Layer 6", "Layer 7" });
            AddParam(ShaderParamNames.furLayerParams2, "Fur LOD offsets", MatParamKind.Float4, -1f, 1f,
                new Vector4(0, 0, 0.0025f, 0.00125f),
                "grass_fur_lod only: UV offset (xy) and sway scale (zw). The game draws its LOD grass\n" +
                "at runtime; here the mesh is the plain ground it stands on.",
                new[] { "Offset U", "Offset V", "Sway X", "Sway Y" });
            AddParam(ShaderParamNames.furLayerParams3, "Fur LOD tiling", MatParamKind.Float4, 0f, 64f,
                new Vector4(1, 1, 1, 0),
                "grass_fur_lod only: UV scales for the diffuse, the mask and the normal map.",
                new[] { "Diffuse", "Mask", "Normal", "unused" });
        }
    }
}
