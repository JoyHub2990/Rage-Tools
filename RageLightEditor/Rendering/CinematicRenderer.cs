using System;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CineVars
    {
        public uint CinePass;
        public float AoRadius;
        public float AoStrength;
        public float BloomThreshold;
        public Vector4 TexelSize;
        public Vector4 CamPos;
        public Matrix InvViewProj;
        public Matrix ViewProj;
        public Matrix PrevViewProj;
        public float AoQuality;
        public float SsrThickness;
        public float SsrMaxDistance;
        public float BloomSpread;
        public float BloomAnamorphic;
        public float DofFocus;
        public float DofAperture;
        public float CinePad2;
        public float SsrSky;
        public float SsrFresnel;
        public float SsrBlur;
        public float DofRange;
        public float DofMaxRadius;
        public float DofBokehBoost;
        public float DofBlades;
        public float CinePad3;
        public Vector4 SsrSkyColour;
        public float DofStretch;
        public float DofRadial;
        public float MotionBlur;
        public float CinePad4;
        public Vector4 MotionDelta;
    }

    public struct CineSettings
    {
        public float AoStrength, AoRadius, AoQuality;
        public float Bloom, BloomThreshold, BloomSpread, BloomAnamorphic;
        public float SsrIntensity, SsrThickness, SsrMaxDistance;
        public float SsrSky, SsrFresnel, SsrBlur;
        public Vector3 SsrSkyColour;
        public float DofStrength, DofFocus, DofAperture, DofRange;
        public float DofMaxRadius, DofBokehBoost, DofBlades;
        public float DofStretch, DofRadial;
        public float MotionBlur;
    }

    public class CinematicRenderer : IDisposable
    {
        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<CineVars> cb;

        private int width, height;
        private Texture2D aoA, aoB, bloomA, bloomB, ssrA, ssrB, dofA, dofB, blurA;
        private RenderTargetView aoARtv, aoBRtv, bloomARtv, bloomBRtv, ssrARtv, ssrBRtv, dofARtv, dofBRtv, blurARtv;
        private ShaderResourceView aoASrv, aoBSrv, bloomASrv, bloomBSrv, ssrASrv, ssrBSrv, dofASrv, dofBSrv, blurASrv;

        public ShaderResourceView AoSRV => aoASrv;
        public ShaderResourceView BloomSRV => bloomASrv;
        public ShaderResourceView SsrSRV => ssrASrv;
        public ShaderResourceView DofSRV => dofASrv;
        public ShaderResourceView MotionSRV => motionActive ? blurASrv : null;
        private bool motionActive;

        public CinematicRenderer(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "cinematic.hlsl", null);
            cb = new ConstantBuffer<CineVars>(device);
        }

        private int AoW => Math.Max(width / 2, 1);
        private int AoH => Math.Max(height / 2, 1);
        private int BloomW => Math.Max(width / 4, 1);
        private int BloomH => Math.Max(height / 4, 1);

        private void EnsureTargets(int w, int h)
        {
            if (w == width && h == height && aoA != null) return;
            ReleaseTargets();
            width = w;
            height = h;

            void Make(int tw, int th, Format fmt, out Texture2D tex, out RenderTargetView rtv, out ShaderResourceView srv)
            {
                tex = new Texture2D(device, new Texture2DDescription
                {
                    Width = tw, Height = th, MipLevels = 1, ArraySize = 1, Format = fmt,
                    SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                });
                rtv = new RenderTargetView(device, tex);
                srv = new ShaderResourceView(device, tex);
            }

            Make(AoW, AoH, Format.R8_UNorm, out aoA, out aoARtv, out aoASrv);
            Make(AoW, AoH, Format.R8_UNorm, out aoB, out aoBRtv, out aoBSrv);
            Make(BloomW, BloomH, Format.R16G16B16A16_Float, out bloomA, out bloomARtv, out bloomASrv);
            Make(BloomW, BloomH, Format.R16G16B16A16_Float, out bloomB, out bloomBRtv, out bloomBSrv);
            Make(AoW, AoH, Format.R16G16B16A16_Float, out ssrA, out ssrARtv, out ssrASrv);
            Make(AoW, AoH, Format.R16G16B16A16_Float, out ssrB, out ssrBRtv, out ssrBSrv);
            Make(AoW, AoH, Format.R16G16B16A16_Float, out dofA, out dofARtv, out dofASrv);
            Make(AoW, AoH, Format.R16G16B16A16_Float, out dofB, out dofBRtv, out dofBSrv);
            Make(w, h, Format.R16G16B16A16_Float, out blurA, out blurARtv, out blurASrv);
        }

        private void ReleaseTargets()
        {
            aoASrv?.Dispose(); aoARtv?.Dispose(); aoA?.Dispose();
            aoBSrv?.Dispose(); aoBRtv?.Dispose(); aoB?.Dispose();
            bloomASrv?.Dispose(); bloomARtv?.Dispose(); bloomA?.Dispose();
            bloomBSrv?.Dispose(); bloomBRtv?.Dispose(); bloomB?.Dispose();
            ssrASrv?.Dispose(); ssrARtv?.Dispose(); ssrA?.Dispose();
            ssrBSrv?.Dispose(); ssrBRtv?.Dispose(); ssrB?.Dispose();
            dofASrv?.Dispose(); dofARtv?.Dispose(); dofA?.Dispose();
            dofBSrv?.Dispose(); dofBRtv?.Dispose(); dofB?.Dispose();
            blurASrv?.Dispose(); blurARtv?.Dispose(); blurA?.Dispose();
            aoA = aoB = bloomA = bloomB = ssrA = ssrB = dofA = dofB = blurA = null;
            aoARtv = aoBRtv = bloomARtv = bloomBRtv = ssrARtv = ssrBRtv = dofARtv = dofBRtv = blurARtv = null;
            aoASrv = aoBSrv = bloomASrv = bloomBSrv = ssrASrv = ssrBSrv = dofASrv = dofBSrv = blurASrv = null;
        }

        private void RunPass(DeviceContext ctx, uint pass, RenderTargetView target, int tw, int th,
            ShaderResourceView src, ShaderResourceView depth, int sw, int sh, ref CineVars v)
        {
            v.CinePass = pass;
            v.TexelSize = new Vector4(1.0f / tw, 1.0f / th, 1.0f / Math.Max(sw, 1), 1.0f / Math.Max(sh, 1));
            cb.Update(ctx, ref v);

            ctx.OutputMerger.SetTargets((DepthStencilView)null, target);
            ctx.Rasterizer.SetViewport(0, 0, tw, th);
            ctx.PixelShader.SetShaderResource(0, src);
            ctx.PixelShader.SetShaderResource(1, depth);
            ctx.Draw(3, 0);
            ctx.PixelShader.SetShaderResource(0, null);
            ctx.PixelShader.SetShaderResource(1, null);
        }

        public void Build(DeviceContext ctx, int screenW, int screenH,
            ShaderResourceView sceneSrv, ShaderResourceView depthSrv,
            Matrix viewProj, Matrix prevViewProj, Vector3 camPos, in CineSettings s)
        {
            EnsureTargets(screenW, screenH);

            var ivp = viewProj;
            ivp.Invert();
            var v = new CineVars
            {
                AoRadius = s.AoRadius,
                AoStrength = s.AoStrength,
                BloomThreshold = s.BloomThreshold,
                CamPos = new Vector4(camPos, 1.0f),
                InvViewProj = Matrix.Transpose(ivp),
                ViewProj = Matrix.Transpose(viewProj),
                AoQuality = s.AoQuality,
                SsrThickness = s.SsrThickness,
                SsrMaxDistance = s.SsrMaxDistance,
                BloomSpread = s.BloomSpread,
                BloomAnamorphic = s.BloomAnamorphic,
                DofFocus = s.DofFocus,
                DofAperture = s.DofAperture,
                SsrSky = s.SsrSky,
                SsrFresnel = s.SsrFresnel,
                SsrBlur = s.SsrBlur,
                SsrSkyColour = new Vector4(s.SsrSkyColour, 1.0f),
                DofRange = s.DofRange,
                DofMaxRadius = s.DofMaxRadius,
                DofBokehBoost = s.DofBokehBoost,
                DofBlades = s.DofBlades,
                DofStretch = s.DofStretch,
                DofRadial = s.DofRadial,
                MotionBlur = s.MotionBlur,
                PrevViewProj = Matrix.Transpose(prevViewProj),
            };

            SetUpPipeline(ctx);

            if (s.AoStrength > 0.001f)
            {
                RunPass(ctx, 0, aoARtv, AoW, AoH, null, depthSrv, screenW, screenH, ref v);
                RunPass(ctx, 1, aoBRtv, AoW, AoH, aoASrv, depthSrv, AoW, AoH, ref v);
                RunPass(ctx, 2, aoARtv, AoW, AoH, aoBSrv, depthSrv, AoW, AoH, ref v);
            }
            else
            {
                ctx.ClearRenderTargetView(aoARtv, new Color4(1, 1, 1, 1));
            }

            if (s.Bloom > 0.001f)
            {
                RunPass(ctx, 3, bloomARtv, BloomW, BloomH, SourceScene(sceneSrv), depthSrv, screenW, screenH, ref v);
                RunPass(ctx, 4, bloomBRtv, BloomW, BloomH, bloomASrv, depthSrv, BloomW, BloomH, ref v);
                RunPass(ctx, 5, bloomARtv, BloomW, BloomH, bloomBSrv, depthSrv, BloomW, BloomH, ref v);
            }
            else
            {
                ctx.ClearRenderTargetView(bloomARtv, new Color4(0, 0, 0, 0));
            }

            if (s.SsrIntensity > 0.001f)
            {
                RunPass(ctx, 6, ssrARtv, AoW, AoH, sceneSrv, depthSrv, screenW, screenH, ref v);
                RunPass(ctx, 7, ssrBRtv, AoW, AoH, ssrASrv, depthSrv, AoW, AoH, ref v);
                RunPass(ctx, 8, ssrARtv, AoW, AoH, ssrBSrv, depthSrv, AoW, AoH, ref v);
            }
            else
            {
                ctx.ClearRenderTargetView(ssrARtv, new Color4(0, 0, 0, 0));
            }

            motionActive = s.MotionBlur > 0.001f;
            if (motionActive)
                RunPass(ctx, 13, blurARtv, screenW, screenH, sceneSrv, depthSrv, screenW, screenH, ref v);

            if (s.DofStrength > 0.001f)
            {
                RunPass(ctx, 9, dofARtv, AoW, AoH, SourceScene(sceneSrv), depthSrv, screenW, screenH, ref v);
                RunPass(ctx, 10, dofBRtv, AoW, AoH, dofASrv, depthSrv, AoW, AoH, ref v);
                RunPass(ctx, 11, dofARtv, AoW, AoH, dofBSrv, depthSrv, AoW, AoH, ref v);
            }
            else
            {
                ctx.ClearRenderTargetView(dofARtv, new Color4(0, 0, 0, 0));
            }
        }

        public void ResolveDepth(DeviceContext ctx, ShaderResourceView msDepth, RenderTargetView target,
            int w, int h)
        {
            if (msDepth == null || target == null) return;
            var v = new CineVars { CinePass = 12 };
            SetUpPipeline(ctx);

            v.TexelSize = new Vector4(1.0f / Math.Max(w, 1), 1.0f / Math.Max(h, 1),
                                      1.0f / Math.Max(w, 1), 1.0f / Math.Max(h, 1));
            cb.Update(ctx, ref v);
            ctx.OutputMerger.SetTargets((DepthStencilView)null, target);
            ctx.Rasterizer.SetViewport(0, 0, w, h);
            ctx.PixelShader.SetShaderResource(2, msDepth);
            ctx.Draw(3, 0);
            ctx.PixelShader.SetShaderResource(2, null);
        }

        private ShaderResourceView SourceScene(ShaderResourceView sceneSrv)
            => motionActive ? blurASrv : sceneSrv;

        private void SetUpPipeline(DeviceContext ctx)
        {
            shader.Apply(ctx);
            ctx.VertexShader.SetConstantBuffer(0, cb.Buffer);
            ctx.PixelShader.SetConstantBuffer(0, cb.Buffer);
            ctx.PixelShader.SetSampler(0, CommonStates.LinearClamp);
            ctx.PixelShader.SetSampler(1, CommonStates.PointClamp);
            ctx.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            ctx.InputAssembler.InputLayout = null;
            ctx.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding());
            ctx.InputAssembler.SetIndexBuffer(null, Format.Unknown, 0);
            ctx.OutputMerger.SetDepthStencilState(CommonStates.DepthDisabled);
            ctx.OutputMerger.SetBlendState(CommonStates.BlendOpaque);
            ctx.Rasterizer.State = CommonStates.RasterSolid;
        }

        public void Dispose()
        {
            ReleaseTargets();
            cb?.Dispose();
            shader?.Dispose();
        }
    }
}

