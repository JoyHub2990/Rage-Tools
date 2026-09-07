using System;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public sealed class SecondaryWindow : IDisposable
    {
        public Device Device { get; }
        public DeviceContext Context { get; }
        public SwapChain SwapChain { get; private set; }
        public RenderTargetView BackbufferRTV { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool DeviceLost { get; private set; }
        public string DeviceLostReason { get; private set; } = "";
        public int SyncInterval = 0;

        private Texture2D backbuffer;

        public SecondaryWindow(Device device, IntPtr windowHandle, int width, int height)
        {
            Device = device;
            Context = device.ImmediateContext;
            Width = Math.Max(width, 8);
            Height = Math.Max(height, 8);

            var scd = new SwapChainDescription
            {
                BufferCount = 2,
                ModeDescription = new ModeDescription(Width, Height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                IsWindowed = true,
                OutputHandle = windowHandle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput,
                Flags = SwapChainFlags.None,
            };
            using (var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>())
            using (var adapter = dxgiDevice.Adapter)
            using (var factory = adapter.GetParent<Factory>())
            {
                SwapChain = new SwapChain(factory, device, scd);
                factory.MakeWindowAssociation(windowHandle, WindowAssociationFlags.IgnoreAltEnter);
            }
            CreateSizedResources();
        }

        private void CreateSizedResources()
        {
            backbuffer = Texture2D.FromSwapChain<Texture2D>(SwapChain, 0);
            BackbufferRTV = new RenderTargetView(Device, backbuffer);
        }

        public void Resize(int width, int height)
        {
            if (DeviceLost) return;
            if (width < 8 || height < 8) return;
            if (width == Width && height == Height) return;
            Width = width;
            Height = height;
            Context.OutputMerger.SetTargets((DepthStencilView)null, (RenderTargetView)null);
            BackbufferRTV?.Dispose(); BackbufferRTV = null;
            backbuffer?.Dispose(); backbuffer = null;
            SwapChain.ResizeBuffers(2, Width, Height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);
            CreateSizedResources();
        }

        public void Begin(Color4 clear)
        {
            Context.Rasterizer.SetViewport(0, 0, Width, Height);
            Context.OutputMerger.SetTargets((DepthStencilView)null, BackbufferRTV);
            Context.ClearRenderTargetView(BackbufferRTV, clear);
        }

        public void Present()
        {
            if (DeviceLost) return;
            var r = SwapChain.TryPresent(SyncInterval, PresentFlags.None);
            if (DeviceResources.DebugFakeDeviceLoss) r = SharpDX.DXGI.ResultCode.DeviceRemoved.Result;
            if (r.Success) return;
            DeviceLostReason = $"present={r.Code:X8} device={Device.DeviceRemovedReason.Code:X8}";
            DeviceLost = true;
        }

        public string SaveScreenshot(string path)
        {
            try
            {
                var desc = backbuffer.Description;
                desc.Usage = ResourceUsage.Staging;
                desc.BindFlags = BindFlags.None;
                desc.CpuAccessFlags = CpuAccessFlags.Read;
                desc.OptionFlags = ResourceOptionFlags.None;
                using var staging = new Texture2D(Device, desc);
                Context.CopyResource(backbuffer, staging);
                var box = Context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                using var bmp = new System.Drawing.Bitmap(desc.Width, desc.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, desc.Width, desc.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                unsafe
                {
                    for (int y = 0; y < desc.Height; y++)
                    {
                        var src = (byte*)box.DataPointer + y * box.RowPitch;
                        var dst = (byte*)bd.Scan0 + y * bd.Stride;
                        for (int x = 0; x < desc.Width; x++)
                        {
                            dst[x * 4 + 0] = src[x * 4 + 2];
                            dst[x * 4 + 1] = src[x * 4 + 1];
                            dst[x * 4 + 2] = src[x * 4 + 0];
                            dst[x * 4 + 3] = 255;
                        }
                    }
                }
                bmp.UnlockBits(bd);
                Context.UnmapSubresource(staging, 0);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public void Dispose()
        {
            if (DeviceLost) return;
            BackbufferRTV?.Dispose(); BackbufferRTV = null;
            backbuffer?.Dispose(); backbuffer = null;
            SwapChain?.Dispose(); SwapChain = null;
        }
    }
}

