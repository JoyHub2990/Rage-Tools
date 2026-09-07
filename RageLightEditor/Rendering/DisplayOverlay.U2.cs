using System;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public sealed class DisplayOverlayPass_U2 : IDisposable
    {
        public struct InkVars
        {
            public Vector4 Screen;
            public Vector4 Ink;
        }

        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<InkVars> inkBuffer;
        private Texture2D backdrop;
        private ShaderResourceView backdropSrv;
        private int width, height;

        public bool Ready { get; private set; }

        public DisplayOverlayPass_U2(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "overlay_u2.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 12, 0),
            });
            inkBuffer = new ConstantBuffer<InkVars>(device);
        }

        public ShaderSet Shader => shader;

        public void Snapshot(DeviceContext context, DeviceResources dr)
        {
            Ready = false;
            if (dr == null || dr.BackbufferRTV == null) return;
            using (var bb = dr.BackbufferRTV.ResourceAs<Texture2D>())
            {
                if (bb == null) return;
                var d = bb.Description;
                if (backdrop == null || width != d.Width || height != d.Height)
                {
                    backdropSrv?.Dispose(); backdrop?.Dispose();
                    width = d.Width; height = d.Height;
                    backdrop = new Texture2D(device, new Texture2DDescription
                    {
                        Width = width,
                        Height = height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = d.Format,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.ShaderResource,
                    });
                    backdropSrv = new ShaderResourceView(device, backdrop);
                }
                context.OutputMerger.SetTargets((DepthStencilView)null, (RenderTargetView)null);
                context.CopyResource(bb, backdrop);
            }
            context.OutputMerger.SetTargets((DepthStencilView)null, dr.BackbufferRTV);
            Ready = true;
        }

        public ShaderSet Bind(DeviceContext context, ShaderResourceView sceneDepth)
        {
            if (!Ready) return null;
            context.PixelShader.SetShaderResource(0, sceneDepth);
            context.PixelShader.SetShaderResource(1, backdropSrv);
            context.PixelShader.SetConstantBuffer(1, inkBuffer.Buffer);
            return shader;
        }

        public void SetInk(DeviceContext context, float gap, float mix, bool depthTest)
        {
            var v = new InkVars
            {
                Screen = new Vector4(depthTest ? 1.0f : 0.0f, 0, 0, 0),
                Ink = new Vector4(gap, mix, 0, 0),
            };
            inkBuffer.Update(context, ref v);
            context.PixelShader.SetConstantBuffer(1, inkBuffer.Buffer);
        }

        public void Unbind(DeviceContext context)
        {
            context.PixelShader.SetShaderResource(0, null);
            context.PixelShader.SetShaderResource(1, null);
        }

        public void Dispose()
        {
            backdropSrv?.Dispose(); backdropSrv = null;
            backdrop?.Dispose(); backdrop = null;
            inkBuffer?.Dispose();
            shader?.Dispose();
        }
    }
}

