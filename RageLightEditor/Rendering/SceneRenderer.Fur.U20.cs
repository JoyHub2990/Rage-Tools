using System;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        private void SetGrassFurVars_U20(RenderMesh mesh, ref ObjectVars ov, int layer, bool ditherByDistance)
        {
            ModelRenderer.FurLayer_V21(mesh, layer, out float clip, out float shadow);
            float step = mesh.FurLayerParams.X;
            float fadeTo = mesh.FurLayerParams.W;
            if (furBaseOverride_V64 > 0.0f && layer == 0) shadow = furBaseOverride_V64;
            float near = mesh.FurAlphaDistance.X, far = mesh.FurAlphaDistance.Y;
            if (far <= near) far = near + 1.0f;
            ov.FurParams = new Vector4(FurMath_U20.ShellOffset(step, layer), clip, shadow, 1.0f);
            ov.FurParams2 = new Vector4(mesh.FurUvScales.X, mesh.FurUvScales.Y, ditherByDistance ? 1.0f : 0.0f, layer);
            ov.FurParams3 = new Vector4(near * near, far * far, fadeTo, mesh.FurUvScales.Z);
            ov.FurParams4 = new Vector4(mesh.FurUvScales.W, mesh.FurMaskSRV != null ? 1.0f : 0.0f,
                                        mesh.FurHfSRV != null ? (mesh.FurHfSrgbView ? 2.0f : 1.0f) : 0.0f, 0.0f);
        }

        private void BindGrassFurTextures_U20(DeviceContext context, RenderMesh mesh, bool bind)
        {
            for (int k = 0; k < 4; k++) context.PixelShader.SetShaderResource(31 + k, bind ? mesh.FurComboSRV[k] : null);
            context.PixelShader.SetShaderResource(35, bind ? mesh.FurMaskSRV : null);
            context.PixelShader.SetShaderResource(36, bind ? mesh.FurHfSRV : null);
        }

        private void DrawGrassFurShells_U20(DeviceContext context, RenderMesh mesh, ref ObjectVars ov)
        {
            var depthWas = context.OutputMerger.GetDepthStencilState(out int stencilWas);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthReadOnly);
            int layers = Math.Max(1, Math.Min(FurMath_U20.MaxLayers, mesh.FurLayers));
            for (int layer = 1; layer < layers; layer++)
            {
                SetGrassFurVars_U20(mesh, ref ov, layer, true);
                objectCB.Update(context, ref ov);
                context.DrawIndexed(mesh.IndexCount, 0, 0);
                FurShellsDrawn_V21++;
            }
            FurMeshesDrawn_V21++;
            ov.FurParams = Vector4.Zero;
            ov.FurParams2 = Vector4.Zero;
            ov.FurParams3 = Vector4.Zero;
            ov.FurParams4 = Vector4.Zero;
            BindGrassFurTextures_U20(context, mesh, false);
            context.OutputMerger.SetDepthStencilState(depthWas, stencilWas);
            depthWas?.Dispose();
        }
    }
}
