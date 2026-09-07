using System;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace RageLightEditor.Rendering
{
    public partial class ShadowRenderer
    {
        partial void DisposeProbe_R5();

        public static readonly bool TwoSidedSunCasters_R5 =
            Environment.GetEnvironmentVariable("RLE_SUNCULL_R5") != "0" && !SceneRenderer.LegacySunShadow_R5;
        private bool sunPass_R5;

        private RasterizerState CasterRaster_R5() =>
            (sunPass_R5 && TwoSidedSunCasters_R5) ? CommonStates.RasterSolid : CommonStates.RasterCullBackLH;

        private Texture2D sunStaging;

        public float[][] ReadSunMap_R5(DeviceContext context)
        {
            if (sunMap == null) return null;
            if (sunStaging == null)
            {
                sunStaging = new Texture2D(device, new Texture2DDescription
                {
                    Width = SunSize, Height = SunSize, MipLevels = 1, ArraySize = SunCascades.MaxCascades,
                    Format = Format.R32_Float, SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read,
                });
            }
            context.CopyResource(sunMap, sunStaging);
            var outp = new float[SunCascades.MaxCascades][];
            for (int s = 0; s < SunCascades.MaxCascades; s++)
            {
                var box = context.MapSubresource(sunStaging, 0, s, MapMode.Read, MapFlags.None, out DataStream _);
                var buf = new float[SunSize * SunSize];
                unsafe
                {
                    var row = (byte*)box.DataPointer;
                    for (int y = 0; y < SunSize; y++)
                    {
                        var src = (float*)(row + (long)y * box.RowPitch);
                        for (int x = 0; x < SunSize; x++) buf[y * SunSize + x] = src[x];
                    }
                }
                context.UnmapSubresource(sunStaging, s);
                outp[s] = buf;
            }
            return outp;
        }

        public static float SampleSunMap_R5(float[] slice, float u, float v)
        {
            if (slice == null) return 1e9f;
            int x = Math.Clamp((int)(u * SunSize), 0, SunSize - 1);
            int y = Math.Clamp((int)(v * SunSize), 0, SunSize - 1);
            return slice[y * SunSize + x];
        }

        partial void DisposeProbe_R5()
        {
            sunStaging?.Dispose();
            sunStaging = null;
        }
    }
}

