using System;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public sealed class DepthCopyPass : IDisposable
    {
        private const string Source = @"
Texture2D<float> DepthTex : register(t0);
struct VSOut { float4 pos : SV_Position; };
VSOut VSMain(uint id : SV_VertexID)
{
    //one triangle over the whole viewport
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}
float PSMain(VSOut i) : SV_Depth
{
    return DepthTex.Load(int3(i.pos.xy, 0));
}";
        private readonly ShaderSet shader;
        private readonly DepthStencilState writeAlways;

        public DepthCopyPass(Device device)
        {
            shader = new ShaderSet(device, Source, "VSMain", "PSMain", null, "depthcopy.hlsl");
            writeAlways = new DepthStencilState(device, new DepthStencilStateDescription
            {
                IsDepthEnabled = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthComparison = Comparison.Always,
                IsStencilEnabled = false,
            });
        }

        public void Copy(DeviceContext ctx, ShaderResourceView depth, DepthStencilView dsv, int w, int h)
        {
            if (depth == null || dsv == null) return;
            ctx.OutputMerger.SetTargets(dsv, (RenderTargetView)null);
            ctx.OutputMerger.SetDepthStencilState(writeAlways);
            ctx.OutputMerger.SetBlendState(null);
            ctx.Rasterizer.State = CommonStates.RasterSolid;
            ctx.Rasterizer.SetViewport(0, 0, w, h);
            shader.Apply(ctx);
            ctx.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            ctx.InputAssembler.InputLayout = null;
            ctx.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding());
            ctx.PixelShader.SetShaderResource(0, depth);
            ctx.Draw(3, 0);
            ctx.PixelShader.SetShaderResource(0, null);
            ctx.OutputMerger.SetTargets((DepthStencilView)null, (RenderTargetView)null);
        }

        public void Dispose()
        {
            shader?.Dispose();
            writeAlways?.Dispose();
        }
    }
}

