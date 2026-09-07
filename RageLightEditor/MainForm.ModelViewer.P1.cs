using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using ImGuiNET;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public readonly ModelViewer ModelView = new ModelViewer();

        private ModelViewerForm modelViewForm;
        private AssetPreview modelViewPreview;
        private CollisionMesh modelViewCollision;

        private Texture2D mvHdrTex, mvLdrTex, mvDepthTex;
        private RenderTargetView mvHdrRtv, mvLdrRtv;
        private ShaderResourceView mvHdrSrv, mvLdrSrv;
        private DepthStencilView mvDsv;
        private int mvW, mvH;
        private GpuLight[] mvLights;

        private readonly Dictionary<GameTexture, IntPtr> mvTexIds = new Dictionary<GameTexture, IntPtr>();

        private bool mvPlacementDirty;
        private double mvPlacementAt;
        private bool mvShotDone;
        private bool mvTabApplied;

        partial void RenderModelViewer_P1(bool captureNow, float dt)
        {
            if (deviceResources == null || deviceResources.DeviceLost || panel == null) return;

            ServiceModelViewer_P1();
            ModelViewCamera_R3();

            if (!ModelView.Visible)
            {
                if (modelViewForm != null && modelViewForm.Visible) modelViewForm.Hide();
                FlushModelViewPlacement_P1();
                return;
            }

            if (modelViewForm == null) CreateModelViewForm_P1();
            if (modelViewForm.DeviceLost) return;
            if (!modelViewForm.Visible) modelViewForm.Show(this);
            var caption = string.IsNullOrEmpty(ModelView.Title) ? "Model viewer" : ModelView.Title + " - Model viewer";
            if (modelViewForm.Text != caption) modelViewForm.Text = caption;

            if (!mvTabApplied && modelViewPreview != null)
            {
                mvTabApplied = true;
                if (int.TryParse(Environment.GetEnvironmentVariable("RLE_RPFVIEWTAB"), out int tab) &&
                    tab >= 0 && tab < ModelViewer.TabNames.Length)
                    ModelView.SetTab(tab);
            }

            ApplyRpfViewThen_P1();

            RenderModelViewport_P1();

            float mainScale = DeviceDpi / 96.0f;
            modelViewForm.RenderFrame(dt, ImGui.GetStyle(), mainScale, ModelView.Draw);

            if (captureNow && screenshotPath != null && !mvShotDone)
            {
                mvShotDone = true;
                modelViewForm.PendingScreenshot = Path.ChangeExtension(screenshotPath, null) + ".modelviewer.png";
                modelViewForm.RenderFrame(dt, ImGui.GetStyle(), mainScale, ModelView.Draw);
                var s = modelViewPreview?.Stats;
                Console.WriteLine($"MODELVIEW window {modelViewForm.ClientSize.Width}x{modelViewForm.ClientSize.Height} " +
                                  $"frames={modelViewForm.FramesRendered} tab={ModelViewer.TabNames[Math.Clamp(ModelView.Tab, 0, ModelViewer.TabNames.Length - 1)]} " +
                                  $"file={ModelView.Title} kind={s?.Kind} models={ModelView.Models.Count} " +
                                  $"image={ModelView.ImageWidth}x{ModelView.ImageHeight}");
                Console.WriteLine(ModelViewIsolationLine_P1());
            }

            FlushModelViewPlacement_P1();
        }

        private int rpfViewThenTick;
        private bool rpfViewThenDone;

        private void ApplyRpfViewThen_P1()
        {
            if (rpfViewThenDone || screenshotPath == null) return;
            var spec = Environment.GetEnvironmentVariable("RLE_RPFVIEWTHEN");
            if (string.IsNullOrWhiteSpace(spec)) { rpfViewThenDone = true; return; }
            var bits = spec.Split(',');
            int at = bits.Length > 1 && int.TryParse(bits[1], out int n) ? Math.Max(n, 1) : 40;
            if (++rpfViewThenTick < at) { screenshotFrames = Math.Max(screenshotFrames, 3); return; }
            rpfViewThenDone = true;
            if (!SpaceNames.TryParse(bits[0], out var space)) return;
            var was = panel.Workspace;
            panel.SwitchWorkspace(space);
            screenshotFrames = Math.Max(screenshotFrames, 3);
            Console.WriteLine($"MODELVIEW then {was} -> {space} at frame {rpfViewThenTick}");
            Console.WriteLine(ModelViewIsolationLine_P1());
        }

        private string ModelViewIsolationLine_P1() =>
            $"MODELVIEW isolation: props={lightScene?.Files.Count ?? 0} lights={lightScene?.Lights.Count ?? 0} " +
            $"ytds={lightScene?.LoadedYtds.Count ?? 0} mloModel={(lightScene?.MloModel != null)} " +
            $"mloProps={mloScene?.Files.Count ?? 0} workspace={panel.Workspace} " +
            $"archivePreview={(panel.ArchivePreview != null)} viewerOwnsPreview={(modelViewPreview != null)}";

        public static bool ModelViewerTakes_P1(string viewKind) =>
            viewKind == "model" || viewKind == "textures" || viewKind == "collision";

        partial void OpenDiskFileInModelViewer_P1(string path, ref bool handled)
        {
            if (string.IsNullOrEmpty(path) || !AssetPreview.CanPreviewDiskFile(path)) return;
            handled = true;

            if (gameFiles == null || !gameFiles.Ready)
            {
                panel.RpfStatus = "the game archives are not open yet";
                return;
            }

            var name = System.IO.Path.GetFileName(path);
            try
            {
                Cursor = Cursors.WaitCursor;
                ClearModelViewTextures_P1();
                modelViewCollision = null;
                modelViewPreview?.Dispose();
                modelViewPreview = new AssetPreview(gameFiles, modelRenderer, textureLoader);

                if (!modelViewPreview.OpenDiskFile(path))
                {
                    panel.RpfStatus = modelViewPreview.Error;
                    Console.WriteLine($"MODELVIEW open FAILED {path}: {modelViewPreview.Error}");
                }
                if (modelViewPreview.Ybn != null)
                {
                    if (collisionView == null) collisionView = new CollisionView(gameFiles);
                    modelViewCollision = BuildViewerCollision_V54(modelViewPreview.Ybn);
                }

                ModelView.TextureId = ModelViewTextureId_P1;
                InstallYtdPicker_V24();
                ModelView.DiskPath = path;
                ModelView.Adopt(modelViewPreview.Entry, modelViewPreview);
                SetupViewerAnim_V55();
                ModelView.Status = string.IsNullOrEmpty(modelViewPreview.Error)
                    ? modelViewPreview.Stats.Summary()
                    : modelViewPreview.Error;
                mvTabApplied = false;
                panel.RpfStatus = name + " opened in the model viewer window";
                Console.WriteLine($"MODELVIEW opened {path}: {modelViewPreview.Stats.Summary()}");
                Console.WriteLine(ModelViewIsolationLine_P1());
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "could not open " + name + ": " + ex.Message;
                Console.WriteLine($"MODELVIEW open THREW {path}: {ex}");
            }
            finally { Cursor = Cursors.Default; }
        }

        partial void OpenInModelViewer_P1(RpfFileEntry e, string kind, ref bool handled)
        {
            if (e == null || !ModelViewerTakes_P1(kind)) return;
            handled = true;

            if (gameFiles == null || !gameFiles.Ready)
            {
                panel.RpfStatus = "the game archives are not open yet";
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                ClearModelViewTextures_P1();
                modelViewCollision = null;
                modelViewPreview?.Dispose();
                modelViewPreview = new AssetPreview(gameFiles, modelRenderer, textureLoader);

                if (!modelViewPreview.Open(e))
                {
                    panel.RpfStatus = modelViewPreview.Error;
                    Console.WriteLine($"MODELVIEW open FAILED {e.Path}: {modelViewPreview.Error}");
                }
                if (modelViewPreview.Ybn != null)
                {
                    if (collisionView == null) collisionView = new CollisionView(gameFiles);
                    modelViewCollision = BuildViewerCollision_V54(modelViewPreview.Ybn);
                }

                ModelView.TextureId = ModelViewTextureId_P1;
                InstallYtdPicker_V24();
                ModelView.DiskPath = null;
                ModelView.Adopt(e, modelViewPreview);
                ModelView.Status = string.IsNullOrEmpty(modelViewPreview.Error)
                    ? modelViewPreview.Stats.Summary()
                    : modelViewPreview.Error;
                mvTabApplied = false;
                panel.RpfStatus = $"{e.Name} opened in the model viewer window";
                Console.WriteLine($"MODELVIEW opened {e.Path}: {modelViewPreview.Stats.Summary()}");
                Console.WriteLine(ModelViewIsolationLine_P1());
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "could not open " + e.Name + ": " + ex.Message;
                Console.WriteLine($"MODELVIEW open THREW {e.Path}: {ex}");
            }
            finally { Cursor = Cursors.Default; }
        }

        private void OpenRpfDiskFileForTest_P1(string path)
        {
            panel.RpfStatus = System.IO.Path.GetFileName(path) + " is not something the model viewer shows";
            Console.WriteLine($"MODELVIEW disk file REFUSED {path}: not a previewable resource");
        }

        private void ServiceModelViewer_P1()
        {
            ServiceModelViewTextureExport_Q1();
            ServiceModelViewTextureDump_S3();
            ServiceModelViewerExtras_V22();
            ServiceYtdPicker_V24();
            if (ModelView.RequestClose)
            {
                ModelView.RequestClose = false;
                ModelView.Visible = false;
            }
            if (ModelView.RequestSelectDrawable >= 0)
            {
                int i = ModelView.RequestSelectDrawable;
                ModelView.RequestSelectDrawable = -1;
                if (modelViewPreview != null && modelViewPreview.SelectDrawable(i))
                {
                    ClearModelViewTextures_P1();
                    ModelView.Rebuild();
                    ModelView.FrameModel();
                    ModelView.Status = modelViewPreview.Stats.Summary();
                }
            }
            if (ModelView.RequestExtract != null)
            {
                var e = ModelView.RequestExtract;
                ModelView.RequestExtract = null;
                if (ModelView.DiskPath != null) ShowInExplorer_O1(ModelView.DiskPath);
                else ExtractRpfEntry_N4(e);
                ModelView.Status = panel.RpfStatus;
            }
            if (ModelView.RequestSpawnInWorld != null)
            {
                var e = ModelView.RequestSpawnInWorld;
                ModelView.RequestSpawnInWorld = null;
                if (ModelView.DiskPath != null)
                {
                    ModelView.Status = panel.RpfStatus =
                        "a file in a folder is placed through the World workspace's Open folder, which reads its .ytyp too";
                    return;
                }
                panel.SwitchWorkspace(LightPanel.Space.World);
                SpawnArchiveModelInWorld(e);
                ModelView.Status = panel.MloStatus;
                panel.RpfStatus = panel.MloStatus;
            }
            if (ModelView.RequestToMloCreator != null)
            {
                var e = ModelView.RequestToMloCreator;
                ModelView.RequestToMloCreator = null;
                SendRpfEntryToMloCreator_N4(e, ModelView.DiskPath);
                ModelView.Status = panel.RpfStatus;
            }
        }

        private IntPtr ModelViewTextureId_P1(GameTexture t)
        {
            if (t == null || modelViewForm == null || !modelViewForm.Ready) return IntPtr.Zero;
            if (mvTexIds.TryGetValue(t, out var id)) return id;
            var srv = t.Data?.FullData != null ? textureLoader?.GetSRV(t, false) : null;
            id = srv != null ? modelViewForm.RegisterTexture(srv) : IntPtr.Zero;
            mvTexIds[t] = id;
            return id;
        }

        private void ClearModelViewTextures_P1()
        {
            if (modelViewForm != null)
                foreach (var kv in mvTexIds)
                    if (kv.Value != IntPtr.Zero) modelViewForm.UnregisterTexture(kv.Value);
            mvTexIds.Clear();
        }

        private void RenderModelViewport_P1()
        {
            var pv = modelViewPreview;
            bool hasGeometry = pv != null && ((pv.Model != null && pv.Model.Meshes.Count > 0) ||
                                              (modelViewCollision != null && !modelViewCollision.IsEmpty));
            if (!hasGeometry || pv.Kind == AssetKind.TextureDict)
            {
                ReleaseModelViewTargets_P1();
                return;
            }

            int w = Math.Clamp(ModelView.WantWidth, 64, 4096);
            int h = Math.Clamp(ModelView.WantHeight, 64, 4096);
            if (!EnsureModelViewTargets_P1(w, h)) return;

            var ctx = deviceResources.Device.ImmediateContext;
            ModelView.ApplyCamera((float)w / Math.Max(h, 1));
            var cam = ModelView.Cam;
            cam.ViewportHeight = h;

            bool sCull = CommonStates.BackfaceCulling;
            int sCullMode = CommonStates.BackfaceMode;
            var sAmb = sceneRenderer.AmbientColour;
            var sGlobal = sceneRenderer.GlobalLight;
            var sFogD = sceneRenderer.FogDensity;
            var sGameFog = sceneRenderer.GameFog;
            var sMode = sceneRenderer.RenderMode;
            var sMul = sceneRenderer.LightsMultiplier;
            var sHqShadow = sceneRenderer.HighQualityShadows;
            var sBumpTilt = sceneRenderer.BumpTiltLimit;
            var sRoomP = sceneRenderer.EyeRoomParams;
            var sRoomS = sceneRenderer.EyeRoomScales;
            var sRoomU = sceneRenderer.EyeRoomAmbUp;
            var sRoomD = sceneRenderer.EyeRoomAmbDown;
            var sVars = postFx.Vars;
            var hiddenTextures = ModelView.ShowTextures ? null : HideModelViewTextures_P1(pv.Model);

            try
            {
                CommonStates.BackfaceCulling = ModelView.BackfaceCulling;
                CommonStates.BackfaceMode = 0;
                float gain = Math.Clamp(ModelView.Exposure, 0.05f, 8.0f);
                sceneRenderer.AmbientColour = new Vector3(ModelView.AmbientLevel * gain);
                sceneRenderer.GlobalLight = null;
                sceneRenderer.FogDensity = 0.0f;
                sceneRenderer.GameFog = default;
                sceneRenderer.EyeRoomParams = Vector4.Zero;
                sceneRenderer.EyeRoomScales = Vector4.Zero;
                sceneRenderer.EyeRoomAmbUp = Vector4.Zero;
                sceneRenderer.EyeRoomAmbDown = Vector4.Zero;
                sceneRenderer.RenderMode = (uint)ModelView.RenderMode;
                sceneRenderer.LightsMultiplier = gain;
                sceneRenderer.HighQualityShadows = false;
                sceneRenderer.BumpTiltLimit = 0.0f;

                var bg = ModelView.Background;
                var clear = new Color4(
                    (float)Math.Pow(Math.Clamp(bg.X, 0f, 1f), 2.2),
                    (float)Math.Pow(Math.Clamp(bg.Y, 0f, 1f), 2.2),
                    (float)Math.Pow(Math.Clamp(bg.Z, 0f, 1f), 2.2), 1.0f);

                ctx.Rasterizer.SetViewport(0, 0, w, h);
                ctx.OutputMerger.SetTargets(mvDsv, mvHdrRtv);
                ctx.ClearRenderTargetView(mvHdrRtv, clear);
                ctx.ClearDepthStencilView(mvDsv, DepthStencilClearFlags.Depth, 0.0f, 0);

                ServiceViewerAnim_V55();
                TickViewerAnim_V55();
                int lights = BuildModelViewLights_P1();
                var drawList = pv.RenderList_V22();
                if (drawList.Length > 0)
                    sceneRenderer.Render(ctx, cam, drawList, mvLights, lights);

                if (modelViewCollision != null && !modelViewCollision.IsEmpty)
                {
                    triRenderer.AddTriangles(modelViewCollision.Vertices, Vector3.Zero);
                    triRenderer.Flush(ctx, cam.ViewProjMatrix, CommonStates.BlendAlpha, CommonStates.DepthDefault);
                }

                if (ModelView.ShowGrid || ModelView.ShowAxes)
                {
                    if (ModelView.ShowGrid) AddModelViewGrid_P1();
                    if (ModelView.ShowAxes) lineRenderer.AddAxes(Vector3.Zero, ModelView.GridStep * 2.0f);
                    lineRenderer.Flush(ctx, cam.ViewProjMatrix, CommonStates.DepthReadOnly);
                }

                postFx.Vars.Passthrough = 1.0f;
                ctx.OutputMerger.SetTargets((DepthStencilView)null, mvLdrRtv);
                ctx.Rasterizer.SetViewport(0, 0, w, h);
                postFx.Composite(ctx, mvHdrSrv);
            }
            catch (Exception ex)
            {
                if (mvDrawError != ex.Message)
                {
                    mvDrawError = ex.Message;
                    Console.WriteLine("MODELVIEW render failed: " + ex);
                }
                ModelView.Status = "could not draw this model: " + ex.Message;
            }
            finally
            {
                CommonStates.BackfaceCulling = sCull;
                CommonStates.BackfaceMode = sCullMode;
                sceneRenderer.AmbientColour = sAmb;
                sceneRenderer.GlobalLight = sGlobal;
                sceneRenderer.FogDensity = sFogD;
                sceneRenderer.GameFog = sGameFog;
                sceneRenderer.RenderMode = sMode;
                sceneRenderer.LightsMultiplier = sMul;
                sceneRenderer.HighQualityShadows = sHqShadow;
                sceneRenderer.BumpTiltLimit = sBumpTilt;
                sceneRenderer.EyeRoomParams = sRoomP;
                sceneRenderer.EyeRoomScales = sRoomS;
                sceneRenderer.EyeRoomAmbUp = sRoomU;
                sceneRenderer.EyeRoomAmbDown = sRoomD;
                postFx.Vars = sVars;
                RestoreModelViewTextures_P1(hiddenTextures);
            }
        }

        private string mvDrawError;

        private static List<(RenderMesh mesh, ShaderResourceView srv)> HideModelViewTextures_P1(RenderModel model)
        {
            if (model == null) return null;
            var saved = new List<(RenderMesh, ShaderResourceView)>();
            foreach (var m in model.Meshes)
            {
                if (m?.DiffuseSRV == null || m.AlphaMode != GeomAlphaMode.Opaque) continue;
                saved.Add((m, m.DiffuseSRV));
                m.DiffuseSRV = null;
            }
            return saved;
        }

        private static void RestoreModelViewTextures_P1(List<(RenderMesh mesh, ShaderResourceView srv)> saved)
        {
            if (saved == null) return;
            foreach (var (mesh, srv) in saved) mesh.DiffuseSRV = srv;
        }

        private int BuildModelViewLights_P1()
        {
            mvLights ??= new GpuLight[GpuLight.MaxLights];
            var pv = modelViewPreview;
            float radius = Math.Max(ModelView.ModelRadius, 0.05f);
            var centre = ModelView.ModelCentre;

            if (ModelView.LightSetup == 2) return 0;

            if (ModelView.LightSetup == 1)
            {
                var lights = pv?.Yft?.Fragment?.LightAttributes?.data_items
                          ?? (pv?.Drawable as Drawable)?.LightAttributes?.data_items;
                if (lights == null || lights.Length == 0) return 0;
                var probe = new Scene(null, null);
                probe.AddImportedProp(ModelView.Title, null, null, null,
                    pv.Drawable?.Skeleton, lights, Matrix.Identity, 1, true, ModelView.Title);
                return probe.BuildGpuLights(mvLights, 12, false, 0f, null, cameraPositionForViewer_P1());
            }

            var fwd = cameraForwardForViewer_P1();
            var right = Vector3.Cross(Vector3.UnitZ, fwd);
            if (right.LengthSquared() < 1e-6f) right = Vector3.UnitX;
            right.Normalize();
            var up = Vector3.Cross(fwd, right);
            up.Normalize();

            float dist = radius * 3.0f;
            float range = radius * 12.0f;
            void Add(Vector3 dir, float intensity, Vector3 colour)
            {
                mvLights[mvLightCount++] = new GpuLight
                {
                    ProjTexIndex = -1.0f,
                    ShadowSlot = -1.0f,
                    Position = centre + dir * dist,
                    Intensity = intensity,
                    Colour = colour,
                    Falloff = range,
                    FalloffExponent = 8.0f,
                    Direction = -dir,
                    TangentX = right,
                    TangentY = up,
                    Type = 1,
                };
            }
            mvLightCount = 0;
            Add(Vector3.Normalize(-fwd * 0.6f + right * -0.7f + up * -0.9f), 6.0f, new Vector3(1.00f, 0.97f, 0.92f));
            Add(Vector3.Normalize(-fwd * 0.5f + right * 0.9f + up * 0.1f), 2.2f, new Vector3(0.85f, 0.90f, 1.00f));
            Add(Vector3.Normalize(fwd * 0.9f + up * -0.4f), 3.0f, new Vector3(1.00f, 1.00f, 1.00f));
            return mvLightCount;
        }

        private int mvLightCount;

        private Vector3 cameraPositionForViewer_P1() => ModelView.Cam.Position;
        private Vector3 cameraForwardForViewer_P1()
        {
            var f = ModelView.Cam.Target - ModelView.Cam.Position;
            if (f.LengthSquared() < 1e-9f) return Vector3.UnitX;
            f.Normalize();
            return f;
        }

        private void AddModelViewGrid_P1()
        {
            float step = ModelView.GridStep;
            const int half = 20;
            var minor = new Vector4(0.09f, 0.09f, 0.11f, 0.60f);
            var major = new Vector4(0.24f, 0.24f, 0.27f, 0.85f);
            var axisX = new Vector4(0.34f, 0.05f, 0.05f, 1.0f);
            var axisY = new Vector4(0.05f, 0.30f, 0.07f, 1.0f);
            float ext = half * step;
            for (int i = -half; i <= half; i++)
            {
                if (i % 10 == 0) continue;
                lineRenderer.AddLine(new Vector3(i * step, -ext, 0), new Vector3(i * step, ext, 0), minor);
                lineRenderer.AddLine(new Vector3(-ext, i * step, 0), new Vector3(ext, i * step, 0), minor);
            }
            for (int i = -half; i <= half; i += 10)
            {
                var cx = i == 0 ? axisY : major;
                var cy = i == 0 ? axisX : major;
                lineRenderer.AddLine(new Vector3(i * step, -ext, 0), new Vector3(i * step, ext, 0), cx);
                lineRenderer.AddLine(new Vector3(-ext, i * step, 0), new Vector3(ext, i * step, 0), cy);
            }
        }

        private bool EnsureModelViewTargets_P1(int w, int h)
        {
            if (modelViewForm == null || !modelViewForm.Ready) return false;
            if (mvLdrRtv != null && mvW == w && mvH == h) return true;
            ReleaseModelViewTargets_P1();
            try
            {
                var dev = deviceResources.Device;
                mvHdrTex = new Texture2D(dev, new Texture2DDescription
                {
                    Width = w, Height = h, MipLevels = 1, ArraySize = 1,
                    Format = Format.R16G16B16A16_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                });
                mvHdrRtv = new RenderTargetView(dev, mvHdrTex);
                mvHdrSrv = new ShaderResourceView(dev, mvHdrTex);

                mvLdrTex = new Texture2D(dev, new Texture2DDescription
                {
                    Width = w, Height = h, MipLevels = 1, ArraySize = 1,
                    Format = Format.R8G8B8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                });
                mvLdrRtv = new RenderTargetView(dev, mvLdrTex);
                mvLdrSrv = new ShaderResourceView(dev, mvLdrTex);

                mvDepthTex = new Texture2D(dev, new Texture2DDescription
                {
                    Width = w, Height = h, MipLevels = 1, ArraySize = 1,
                    Format = Format.D32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.DepthStencil,
                });
                mvDsv = new DepthStencilView(dev, mvDepthTex);

                mvW = w; mvH = h;
                ModelView.ImageId = modelViewForm.RegisterTexture(mvLdrSrv);
                ModelView.ImageWidth = w;
                ModelView.ImageHeight = h;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MODELVIEW target {w}x{h} failed: {ex.Message}");
                ReleaseModelViewTargets_P1();
                return false;
            }
        }

        private void ReleaseModelViewTargets_P1()
        {
            if (ModelView.ImageId != IntPtr.Zero)
            {
                modelViewForm?.UnregisterTexture(ModelView.ImageId);
                ModelView.ImageId = IntPtr.Zero;
            }
            ModelView.ImageWidth = ModelView.ImageHeight = 0;
            mvDsv?.Dispose(); mvDsv = null;
            mvDepthTex?.Dispose(); mvDepthTex = null;
            mvLdrSrv?.Dispose(); mvLdrSrv = null;
            mvLdrRtv?.Dispose(); mvLdrRtv = null;
            mvLdrTex?.Dispose(); mvLdrTex = null;
            mvHdrSrv?.Dispose(); mvHdrSrv = null;
            mvHdrRtv?.Dispose(); mvHdrRtv = null;
            mvHdrTex?.Dispose(); mvHdrTex = null;
            mvW = mvH = 0;
        }

        private void CreateModelViewForm_P1()
        {
            modelViewForm = new ModelViewerForm(deviceResources.Device) { LogTag = "MODELVIEW" };
            modelViewForm.CloseRequested += () => ModelView.Visible = false;
            modelViewForm.PlacementChanged += () =>
            {
                if (modelViewForm == null || !modelViewForm.Visible) return;
                mvPlacementDirty = true;
                mvPlacementAt = clock.Elapsed.TotalSeconds;
            };
            RestoreModelViewPlacement_P1();
        }

        private void RestoreModelViewPlacement_P1()
        {
            var b = settings?.ModelViewerBounds;
            var rect = System.Drawing.Rectangle.Empty;
            if (b != null && b.Length == 4 && b[2] >= 320 && b[3] >= 240)
            {
                rect = new System.Drawing.Rectangle(b[0], b[1], b[2], b[3]);
                var want = rect;
                bool onScreen = Screen.AllScreens.Any(s =>
                {
                    var i = System.Drawing.Rectangle.Intersect(s.WorkingArea, want);
                    return i.Width >= 120 && i.Height >= 80;
                });
                if (!onScreen) rect = System.Drawing.Rectangle.Empty;
            }
            if (rect.IsEmpty)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                int w = Math.Min(1280, wa.Width - 40), h = Math.Min(800, wa.Height - 80);
                rect = new System.Drawing.Rectangle(
                    Math.Max(wa.Left, Math.Min(Bounds.Left + 90, wa.Right - w)),
                    Math.Max(wa.Top, Math.Min(Bounds.Top + 60, wa.Bottom - h)), w, h);
            }
            modelViewForm.StartPosition = FormStartPosition.Manual;
            modelViewForm.Bounds = rect;
            if (settings != null && settings.ModelViewerMaximized) modelViewForm.WindowState = FormWindowState.Maximized;
        }

        private void FlushModelViewPlacement_P1()
        {
            if (!mvPlacementDirty || modelViewForm == null || settings == null) return;
            if (clock.Elapsed.TotalSeconds - mvPlacementAt < 1.0) return;
            mvPlacementDirty = false;
            var r = modelViewForm.WindowState == FormWindowState.Normal ? modelViewForm.Bounds : modelViewForm.RestoreBounds;
            if (r.Width >= 320 && r.Height >= 240)
                settings.ModelViewerBounds = new[] { r.X, r.Y, r.Width, r.Height };
            settings.ModelViewerMaximized = modelViewForm.WindowState == FormWindowState.Maximized;
            if (screenshotPath == null && !DebugFpsBench) settings.Save();
        }

        partial void DisposeModelViewer_P1()
        {
            try
            {
                bool lost = deviceResources != null && deviceResources.DeviceLost;
                if (!lost) ReleaseModelViewTargets_P1();
                modelViewPreview?.Dispose();
                modelViewPreview = null;
                modelViewCollision = null;
                mvTexIds.Clear();
                if (modelViewForm != null)
                {
                    if (lost) modelViewForm.AbandonResources();
                    else modelViewForm.ReleaseResources();
                    modelViewForm.Dispose();
                    modelViewForm = null;
                }
            }
            catch { }
        }
    }
}

