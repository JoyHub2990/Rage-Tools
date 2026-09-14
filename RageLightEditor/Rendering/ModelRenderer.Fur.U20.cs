using System;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class ModelRenderer
    {
        private TextureBase furMask_U20, furHf_U20;

        public static bool IsGrassFurSps_U20(uint sps) =>
            sps == 3333227093u || sps == 4256676773u || sps == 1483370959u || sps == 3895559374u;

        private void ReadFurTextures_U20(RenderMesh mesh, TextureDictionary embeddedDict)
        {
            mesh.FurMaskSRV = ResolveTexture(furMask_U20, embeddedDict, false, out _);
            mesh.FurHfSRV = ResolveTexture(furHf_U20, embeddedDict, true, out var hfTex);
            mesh.FurHfSrgbView = hfTex != null && TextureLoader.HasSrgbFormat(hfTex.Format);
            mesh.IsFurMask = FurMath_U20.IsMaskShader(mesh.ShaderName);
            NoteMissing_V21(furMask_U20, mesh.FurMaskSRV);
            NoteMissing_V21(furHf_U20, mesh.FurHfSRV);
            if (furDbg_V21 && (furMask_U20 != null || furHf_U20 != null))
                Console.WriteLine($"FURDBG mask '{furMask_U20?.Name ?? "(none)"}' {(mesh.FurMaskSRV != null ? "resolved" : "MISSING")}, area diffuse '{furHf_U20?.Name ?? "(none)"}' {(mesh.FurHfSRV != null ? "resolved" : "MISSING")}");
        }

        private void ResetFurDefaults_U20(RenderMesh mesh)
        {
            mesh.FurLayerParams = FurMath_U20.DefaultLayerParams;
            mesh.FurUvScales = FurMath_U20.DefaultUvScales;
            mesh.FurAlphaDistance = FurMath_U20.DefaultAlphaDistance;
            mesh.FurAlphaClip03 = FurMath_U20.DefaultAlphaClip03;
            mesh.FurAlphaClip47 = FurMath_U20.DefaultAlphaClip47;
            mesh.FurShadow03 = FurMath_U20.DefaultShadow03;
            mesh.FurShadow47 = FurMath_U20.DefaultShadow47;
            mesh.FurLayers = FurMath_U20.MaxLayers;
            mesh.IsFur = false;
            mesh.IsFurMask = false;
            mesh.FurMaskSRV = null;
            mesh.FurHfSRV = null;
            for (int k = 0; k < 4; k++) mesh.FurComboSRV[k] = null;
        }
    }
}
