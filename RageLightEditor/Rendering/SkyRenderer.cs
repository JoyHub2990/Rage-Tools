using System;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SkyVars
    {
        public Matrix InvViewProjDir;
        public Vector4 CameraPos;

        public Vector3 AzimuthEastColour; public float AzimuthTransitionPosition;
        public Vector3 AzimuthWestColour; public float ZenithTransitionPosition;
        public Vector3 AzimuthTransitionColour; public float ZenithBlendStart;
        public Vector3 ZenithColour; public float ZenithTransitionEastBlend;
        public Vector3 ZenithTransitionColour; public float ZenithTransitionWestBlend;

        public Vector3 SunDirection; public float SunDiscSize;
        public Vector3 SunColour; public float SunHdr;
        public Vector3 SunDiscColour; public float SunInfluenceRadius;
        public Vector3 SunMie; public float SunScatterIntensity;

        public Vector3 MoonDirection; public float MoonDiscSize;
        public Vector3 MoonColour; public float MoonIntensity;
        public Vector3 LunarCycle; public float MoonInfluenceRadius;

        public Vector3 CloudBaseColour; public float CloudBaseStrength;
        public Vector3 CloudMidColour; public float CloudDensityMultiplier;
        public Vector3 CloudShadowColour; public float CloudDensityBias;
        public float CloudFadeOut; public float CloudShadowStrength;
        public float CloudCoverage; public float CloudTime;

        public Vector3 FogColour; public float FogDensity;
        public float HdrIntensity; public float StarfieldIntensity; public float SkyPad0, SkyPad1;
        public Vector4 SkyExtra0;
        public GameFogVars Fog;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GameFogVars
    {
        public Vector4 Params0;
        public Vector4 Params1;
        public Vector4 Params2;
        public Vector4 SunDirAndPower;
        public Vector4 MoonDirAndPower;
        public Vector4 ColSun, ColAtmosphere, ColGround, ColHaze, ColMoon;

        public static GameFogVars Build(in Editor.TimecycleData.SkyState s, float cameraHeight,
            Vector3 sunDir, Vector3 moonDir, bool hdr, float densityScale, bool enabled)
        {
            float fogStart = s.FogStart;
            float fogEnd = Math.Max(s.FarClip, fogStart);
            float fogNearDensity = s.FogDensity * densityScale;
            float fogNearHeightFalloff = s.FogHeightFalloff;
            float fogNearAlpha = s.FogAlpha;
            float fogGroundHeight = s.FogBaseHeight;
            float fogHeightDensityAtViewer = (float)Math.Exp(Math.Min(-fogNearHeightFalloff * (cameraHeight - fogGroundHeight), 20.0f));
            float fogFarDensity = s.FogHazeDensity * densityScale;
            float fogFarAlpha = s.FogHazeAlpha;
            float fogHorizonTintScale = s.FogHorizonTintScale;
            float fogHazeStart = s.FogHazeStart;

            var v = new GameFogVars();
            v.Params0 = new Vector4(fogStart, fogEnd, 0.0f, enabled ? 1.0f : 0.0f);
            v.Params1 = new Vector4(-fogFarDensity, fogFarAlpha, fogHorizonTintScale / fogEnd,
                                    -Math.Min(1.0f, fogHeightDensityAtViewer * fogNearDensity));
            v.Params2 = new Vector4(fogHazeStart, fogNearAlpha, fogNearHeightFalloff,
                                    MathUtil.Clamp(1.0f - (float)Math.Exp(fogFarDensity * fogEnd), 0.0f, 1.0f) * fogFarAlpha);
            v.SunDirAndPower = new Vector4(sunDir, Math.Max(s.FogSunPower, 0.01f));
            v.MoonDirAndPower = new Vector4(moonDir, Math.Max(s.FogMoonPower, 0.01f));

            float hdrMultiplier = hdr ? s.FogHdr : 1.0f;
            float hazeHdr = hdr ? s.FogHazeHdr : 1.0f;
            var colSun = s.FogSunCol * hdrMultiplier;
            var colMoon = s.FogMoonCol * hdrMultiplier;
            var colAtmo = s.FogFarCol * hdrMultiplier;
            var colGround = s.FogNearCol * hdrMultiplier;
            var colHaze = s.FogHazeCol * hazeHdr;
            float fogColorLerp = (float)Math.Pow(MathUtil.Clamp(fogHeightDensityAtViewer, 0.0f, 1.0f), 0.3);
            colAtmo = Vector3.Lerp(colAtmo, colGround, fogColorLerp);
            float fade = MathUtil.Clamp(Math.Max(sunDir.Z, moonDir.Z) * 8.0f + 0.2f, 0.0f, 1.0f);
            v.ColSun = new Vector4(Vector3.Lerp(colAtmo, colSun, fade), 1.0f);
            v.ColAtmosphere = new Vector4(colAtmo, 1.0f);
            v.ColGround = new Vector4(colGround, 1.0f);
            v.ColHaze = new Vector4(colHaze, 1.0f);
            v.ColMoon = new Vector4(colMoon, 1.0f);
            return v;
        }
    }

    public class SkyRenderer : IDisposable
    {
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<SkyVars> cb;

        public SkyRenderer(Device device)
        {
            shader = new ShaderSet(device, "sky.hlsl", null);
            cb = new ConstantBuffer<SkyVars>(device);
        }

        public void Render(DeviceContext context, ref SkyVars vars)
        {
            cb.Update(context, ref vars);
            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cb.Buffer);
            context.PixelShader.SetConstantBuffer(0, cb.Buffer);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding());
            context.InputAssembler.SetIndexBuffer(null, SharpDX.DXGI.Format.Unknown, 0);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthDisabled);
            context.OutputMerger.SetBlendState(CommonStates.BlendOpaque);
            context.Rasterizer.State = CommonStates.RasterSolid;
            context.Draw(3, 0);
        }

        public void Dispose()
        {
            cb?.Dispose();
            shader?.Dispose();
        }
    }
}

