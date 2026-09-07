using System;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        private static readonly bool furDisabled_V21 =
            Environment.GetEnvironmentVariable("RLE_NOFUR") == "1";

        private Vector3 frameEye_V21;

        public int FurShellsDrawn_V21;
        public int FurMeshesDrawn_V21;
        public int FurFinsDrawn_V21;

        private static readonly bool furDbgScene_V21 = Environment.GetEnvironmentVariable("RLE_FURDBG") == "1";
        private int furDbgSaid_V21;

        private Vector3 eyeForFur_V38;

        private float FurFade_V21(RenderMesh mesh, Vector3 eye)
        {
            if (furDisabled_V21 || !mesh.IsFur || mesh.FurLayerParams.X <= 0.0f) return 0.0f;
            eyeForFur_V38 = eye;

            float near = mesh.FurAlphaDistance.X, far = mesh.FurAlphaDistance.Y;
            if (far <= near) far = near + 1.0f;

            float d = Math.Max(0.0f, (mesh.WorldSphere.Center - eye).Length() - mesh.WorldSphere.Radius);
            float fade = d >= far ? 0.0f : (d <= near ? 1.0f : 1.0f - (d - near) / (far - near));
            if (furDbgScene_V21 && furDbgSaid_V21 < 4)
            {
                furDbgSaid_V21++;
                Console.WriteLine($"FURDBG draw: d={d:0.0} m (sphere r {mesh.WorldSphere.Radius:0.0} at {mesh.WorldSphere.Center}) eye {eye} -> fade {fade:0.00}");
            }
            return fade;
        }

        public float FurFadeForTest_V21(RenderMesh mesh, Vector3 eye) => FurFade_V21(mesh, eye);

        private void DrawFurShells_V21(DeviceContext context, RenderMesh mesh, ref ObjectVars ov, float fade)
        {
            bool pedFur = mesh.IsPedFur_V38;
            var depthWas = context.OutputMerger.GetDepthStencilState(out int stencilWas);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthDefault);
            int layers = pedFur
                ? ModelRenderer.PedFurLayers_V38(mesh.PedFurMinLayers_V38, mesh.PedFurMaxLayers_V38,
                      Math.Max(0.0f, (mesh.WorldSphere.Center - eyeForFur_V38).Length() - mesh.WorldSphere.Radius))
                : Math.Max(1, Math.Min(8, mesh.FurLayers));

            for (int k = 0; k < 4; k++)
                context.PixelShader.SetShaderResource(31 + k, mesh.FurComboSRV[k]);

            for (int layer = 0; layer < layers; layer++)
            {
                float clip, shadow;
                if (pedFur)
                {
                    clip = ModelRenderer.PedFurClip_V38(mesh.PedFurAtten_V38, layer, layers);
                    shadow = ModelRenderer.PedFurShadow_V38(mesh.PedFurSelfShadowMin_V38, mesh.PedFurAOBlend_V38, layer, layers);
                }
                else ModelRenderer.FurLayer_V21(mesh, layer, out clip, out shadow);
                float t = (layer + 1) / (float)layers;
                ov.FurParams = new Vector4(pedFur ? t - 1.0f : t, clip, shadow, mesh.FurLayerParams.X);
                ov.FurParams2 = new Vector4(mesh.FurUvScales.X, mesh.FurUvScales.Y, fade,
                                            pedFur ? -(layer + 1) : layer);
                objectCB.Update(context, ref ov);
                context.DrawIndexed(mesh.IndexCount, 0, 0);
                FurShellsDrawn_V21++;
            }
            FurMeshesDrawn_V21++;

            if (pedFur && mesh.FinVB != null && mesh.FinIndexCount > 0)
            {
                ov.FurParams = new Vector4(0.0f, -1.0f, mesh.PedFurSelfShadowMin_V38, mesh.FurLayerParams.X);
                ov.FurParams2 = new Vector4(mesh.FurUvScales.X, mesh.FurUvScales.Y, fade, -100.0f);
                uint tintWas = ov.HasTintPalette;
                ov.HasTintPalette = 0;
                objectCB.Update(context, ref ov);
                context.PixelShader.SetShaderResource(1, null);
                context.InputAssembler.SetVertexBuffers(0,
                    new SharpDX.Direct3D11.VertexBufferBinding(mesh.FinVB, MeshVertex.Stride, 0));
                context.InputAssembler.SetIndexBuffer(mesh.FinIB, SharpDX.DXGI.Format.R32_UInt, 0);
                context.DrawIndexed(mesh.FinIndexCount, 0, 0);
                ov.HasTintPalette = tintWas;
                FurFinsDrawn_V21++;
            }

            ov.FurParams = Vector4.Zero;
            ov.FurParams2 = Vector4.Zero;
            for (int k = 0; k < 4; k++)
                context.PixelShader.SetShaderResource(31 + k, null);
            context.OutputMerger.SetDepthStencilState(depthWas, stencilWas);
            depthWas?.Dispose();
        }
    }
}

