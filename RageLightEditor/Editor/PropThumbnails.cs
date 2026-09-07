using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.ImGuiBackend;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Editor
{
    public class PropThumbnails : IDisposable
    {
        public const int Size = 144;

        public int PerFrameBudget = 12;

        public double TimeBudgetMs = 4.0;

        private GpuLight[] thumbLights;

        public int MaxCached = 320;

        private class Thumb
        {
            public Texture2D Texture;
            public ShaderResourceView Srv;
            public IntPtr ImGuiId;
            public bool Failed;
            public int LastUsedFrame;
        }

        private readonly Device device;
        private readonly SceneRenderer sceneRenderer;
        private readonly ModelRenderer modelRenderer;
        private readonly ImGuiRenderer imgui;
        private readonly GameFileManager game;
        private readonly PostFxRenderer postFx;

        private readonly Dictionary<uint, Thumb> cache = new Dictionary<uint, Thumb>();
        private readonly List<LightPropEntry> queue = new List<LightPropEntry>();
        private Texture2D depthTex;
        private DepthStencilView dsv;
        private Texture2D hdrTex;
        private RenderTargetView hdrRtv;
        private ShaderResourceView hdrSrv;

        private void EnsureHdr()
        {
            if (hdrRtv != null) return;
            hdrTex = new Texture2D(device, new Texture2DDescription
            {
                Width = Size,
                Height = Size,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R16G16B16A16_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            hdrRtv = new RenderTargetView(device, hdrTex);
            hdrSrv = new ShaderResourceView(device, hdrTex);
        }
        private int frame;

        public PropThumbnails(Device device, SceneRenderer sceneRenderer, ModelRenderer modelRenderer,
            ImGuiRenderer imgui, GameFileManager game, PostFxRenderer postFx)
        {
            this.postFx = postFx;
            this.device = device;
            this.sceneRenderer = sceneRenderer;
            this.modelRenderer = modelRenderer;
            this.imgui = imgui;
            this.game = game;
        }

        public IntPtr Get(LightPropEntry e)
        {
            if (e == null) return IntPtr.Zero;
            if (cache.TryGetValue(e.Hash, out var t))
            {
                t.LastUsedFrame = frame;
                return t.Failed ? IntPtr.Zero : t.ImGuiId;
            }
            if (!queue.Contains(e)) queue.Add(e);
            return IntPtr.Zero;
        }

        public void Tick()
        {
            frame++;
            if (DebugLog.Enabled)
                DebugLog.Log($"THUMBTICK frame={frame} queue={queue.Count} cache={cache.Count} " +
                             $"archives={(game?.Cache?.RpfMan != null)} budget={PerFrameBudget}");
            if (queue.Count == 0) return;
            EnsureDepth();

            bool archivesReady = game?.Cache?.RpfMan != null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int done = 0, guard = 0;
            while (queue.Count > 0 && done < PerFrameBudget && guard++ < 512)
            {
                if (done > 0 && sw.Elapsed.TotalMilliseconds >= TimeBudgetMs) break;
                var e = queue[queue.Count - 1];
                queue.RemoveAt(queue.Count - 1);
                if (cache.ContainsKey(e.Hash)) continue;
                if (e.FromArchive && !archivesReady) continue;
                cache[e.Hash] = RenderOne(e) ?? new Thumb { Failed = true, LastUsedFrame = frame };
                done++;
            }
            Evict();
        }

        private Thumb RenderOne(LightPropEntry e)
        {
            RenderModel model = null;
            try
            {
                if (!Load(e, out var drawable, out var lights) || drawable == null)
                {
                    DebugLog.Log($"THUMB no drawable: {e.Name} ({e.Path})");
                    return null;
                }

                model = modelRenderer.BuildFromDrawable(drawable, e.Name);
                if (model.Meshes.Count == 0)
                {
                    DebugLog.Log($"THUMB no meshes: {e.Name}");
                    return null;
                }

                var probe = new Scene(null, null);
                probe.AddImportedProp(e.Name, null, null, null, drawable.Skeleton, lights,
                    Matrix.Identity, 1, true, e.Name);
                var gpu = thumbLights ??= new GpuLight[GpuLight.MaxLights];
                int n = probe.BuildGpuLights(gpu, 12, false, 0f, null, Vector3.Zero);

                return Draw(e, model, gpu, n);
            }
            catch (Exception ex)
            {
                DebugLog.Log($"THUMB FAIL {e.Name} ({e.Path}): {ex}");
                return null;
            }
            finally
            {
                model?.Dispose();
            }
        }

        private Thumb Draw(LightPropEntry e, RenderModel model, GpuLight[] gpu, int lightCount)
        {
            EnsureHdr();
            var tex = new Texture2D(device, new Texture2DDescription
            {
                Width = Size,
                Height = Size,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });

            var ctx = device.ImmediateContext;
            using (var rtv = new RenderTargetView(device, tex))
            {
                ctx.Rasterizer.SetViewport(0, 0, Size, Size);
                ctx.OutputMerger.SetTargets(dsv, hdrRtv);
                ctx.ClearRenderTargetView(hdrRtv, new Color4(0.10f, 0.11f, 0.13f, 1.0f));
                ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 0.0f, 0);

                var cam = new Camera { Yaw = 0.9f, Pitch = 0.5f, FieldOfView = MathUtil.PiOverFour };
                cam.SetAspect(1.0f);
                var b = model.Bounds;
                var centre = (b.Minimum + b.Maximum) * 0.5f;
                float radius = Math.Max((b.Maximum - b.Minimum).Length() * 0.5f, 0.05f);
                cam.FrameBounds(centre, radius);
                cam.NearClip = Math.Max(radius * 0.01f, 0.01f);
                cam.Update();

                var saveAmbient = sceneRenderer.AmbientColour;
                var saveGlobal = sceneRenderer.GlobalLight;
                var saveFog = sceneRenderer.FogDensity;
                var saveMode = sceneRenderer.RenderMode;
                var saveMul = sceneRenderer.LightsMultiplier;
                sceneRenderer.AmbientColour = new Vector3(0.22f, 0.23f, 0.26f);
                sceneRenderer.GlobalLight = null;
                sceneRenderer.FogDensity = 0.0f;
                sceneRenderer.RenderMode = 0;
                sceneRenderer.LightsMultiplier = 1.0f;
                try
                {
                    sceneRenderer.Render(ctx, cam, new[] { model }, gpu, lightCount);
                }
                finally
                {
                    sceneRenderer.AmbientColour = saveAmbient;
                    sceneRenderer.GlobalLight = saveGlobal;
                    sceneRenderer.FogDensity = saveFog;
                    sceneRenderer.RenderMode = saveMode;
                    sceneRenderer.LightsMultiplier = saveMul;
                }

                ctx.OutputMerger.SetTargets((DepthStencilView)null, rtv);
                ctx.Rasterizer.SetViewport(0, 0, Size, Size);
                postFx.Composite(ctx, hdrSrv);
            }

            var srv = new ShaderResourceView(device, tex);
            return new Thumb
            {
                Texture = tex,
                Srv = srv,
                ImGuiId = imgui.RegisterTexture(srv),
                LastUsedFrame = frame,
            };
        }

        public static bool Load(LightPropEntry e, GameFileManager game,
            out DrawableBase drawable, out LightAttributes[] lights, out YdrFile ydrOut, out YftFile yftOut)
        {
            drawable = null; lights = null; ydrOut = null; yftOut = null;
            if (e == null) return false;

            if (e.FromArchive)
            {
                var rpfman = game?.Cache?.RpfMan;
                if (rpfman == null) { DebugLog.Log($"THUMB no rpfman (game ready={game?.Ready})"); return false; }
                if (!(rpfman.GetEntry(e.Path) is RpfFileEntry fe))
                {
                    DebugLog.Log($"THUMB entry not found: {e.Path}");
                    return false;
                }
                if (e.IsYft)
                {
                    yftOut = rpfman.GetFile<YftFile>(fe);
                    drawable = yftOut?.Fragment?.Drawable;
                    lights = yftOut?.Fragment?.LightAttributes?.data_items;
                }
                else
                {
                    ydrOut = rpfman.GetFile<YdrFile>(fe);
                    drawable = ydrOut?.Drawable;
                    lights = ydrOut?.Drawable?.LightAttributes?.data_items;
                }
                return drawable != null;
            }

            if (!File.Exists(e.Path)) return false;
            var data = File.ReadAllBytes(e.Path);
            if (e.IsYft)
            {
                yftOut = new YftFile();
                yftOut.Load(data);
                drawable = yftOut.Fragment?.Drawable;
                lights = yftOut.Fragment?.LightAttributes?.data_items;
            }
            else
            {
                ydrOut = new YdrFile();
                ydrOut.Load(data);
                drawable = ydrOut.Drawable;
                lights = ydrOut.Drawable?.LightAttributes?.data_items;
            }
            return drawable != null;
        }

        private bool Load(LightPropEntry e, out DrawableBase drawable, out LightAttributes[] lights)
            => Load(e, game, out drawable, out lights, out _, out _);

        private void EnsureDepth()
        {
            if (dsv != null) return;
            depthTex = new Texture2D(device, new Texture2DDescription
            {
                Width = Size,
                Height = Size,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
            });
            dsv = new DepthStencilView(device, depthTex);
        }

        private void Evict()
        {
            if (cache.Count <= MaxCached) return;
            var byAge = new List<KeyValuePair<uint, Thumb>>(cache);
            byAge.Sort((a, b) => a.Value.LastUsedFrame.CompareTo(b.Value.LastUsedFrame));
            int drop = cache.Count - MaxCached;
            for (int i = 0; i < drop && i < byAge.Count; i++)
            {
                var t = byAge[i].Value;
                if (t.ImGuiId != IntPtr.Zero) imgui.UnregisterTexture(t.ImGuiId);
                t.Srv?.Dispose();
                t.Texture?.Dispose();
                cache.Remove(byAge[i].Key);
            }
        }

        public void Dispose()
        {
            foreach (var kv in cache)
            {
                kv.Value.Srv?.Dispose();
                kv.Value.Texture?.Dispose();
            }
            cache.Clear();
            dsv?.Dispose();
            depthTex?.Dispose();
            hdrSrv?.Dispose();
            hdrRtv?.Dispose();
            hdrTex?.Dispose();
        }
    }
}

