using System;
using SharpDX;
using SharpDX.Direct3D11;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private GpuProfiler gpuProf;
        private static readonly bool gpuProfOff = Environment.GetEnvironmentVariable("RLE_GPUPROF") == "0";
        private double lastGpuLog;

        private static readonly bool noVsyncByEnv = Environment.GetEnvironmentVariable("RLE_NOVSYNC") == "1";
        private void GpuFrameBegin_J4(DeviceContext context)
        {
            if (noVsyncByEnv && settings.VSync) settings.VSync = false;
            if (gpuProfOff) return;
            try
            {
                if (gpuProf == null)
                {
                    gpuProf = new GpuProfiler(deviceResources.Device);
                    SceneRenderer.Profiler = gpuProf;
                }
                gpuProf.BeginFrame(context);
            }
            catch (Exception ex) { Console.WriteLine("GPUPROF: " + ex.Message); gpuProf?.Dispose(); gpuProf = null; SceneRenderer.Profiler = null; }
        }

        private void GpuMark_J4(string name, bool begin)
        {
            if (gpuProf == null) return;
            if (begin) gpuProf.Begin(name); else gpuProf.End(name);
        }

        private static readonly bool gpuTrace = Environment.GetEnvironmentVariable("RLE_GPUTRACE") == "1";
        private int gpuTraceLeft = 40;
        private void GpuFrameEnd_J4()
        {
            if (gpuProf == null) return;
            gpuProf.EndFrame();
            if (gpuTrace && screenshotPath != null && worldBuilt && worldWarmup > 400 && gpuTraceLeft-- > 0)
                Console.WriteLine("GPUTRACE " + gpuProf.Report(false));
            if (screenshotPath != null && panel.WorldMode && worldBuilt)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now - lastGpuLog > 2.0 && gpuProf.Resolved > 8)
                {
                    lastGpuLog = now;
                    Console.WriteLine("WORLDGPU " + gpuProf.Report() + $"  (cpu frame {lastRenderMs:0.0} ms, {fpsSmoothed:0} fps, main {sceneRenderer.MainStats}; reflection {sceneRenderer.ReflectionStats}; cascade draws {shadowRenderer.LastCascadeDraws} rebuilds {sunCascadeRebuilds} last {perfCascadeMs:0.0} ms; {interiorCull})");
                    if (SceneRenderer.PassDump && !sceneRenderer.PassDumpDone && worldWarmup > 200)
                    {
                        var dump = sceneRenderer.PassDumpReport();
                        if (dump != null) Console.Write(dump);
                    }
                }
            }
        }

        private string GpuReport_J4() => gpuProf == null ? "WORLDGPU off" : "WORLDGPU " + gpuProf.Report();

        private Editor.InteriorCuller interiorCull;
        private static readonly bool interiorCullOffByEnv = Environment.GetEnvironmentVariable("RLE_NOINTCULL") == "1";
        private string interiorCullLast = "";
        private readonly System.Collections.Generic.List<Plane> interiorCullPlanes = new System.Collections.Generic.List<Plane>();

        private void UpdateInteriorCull_J4()
        {
            if (interiorCull == null) interiorCull = new Editor.InteriorCuller();
            interiorCull.Enabled = panel.WorldInteriorCull && !interiorCullOffByEnv && !(panel.RenderMode == 8);
            interiorCull.CullRooms = panel.WorldInteriorCullRooms && Environment.GetEnvironmentVariable("RLE_NOROOMCULL") != "1";
            interiorCullPlanes.Clear();
            for (int i = 0; i < sceneRenderer.LastMirrorPlaneCount; i++) interiorCullPlanes.Add(sceneRenderer.LastMirrorPlane(i));
            interiorCull.Update(camera.Position, camera.GetForward(), camera.ViewProjMatrix, World.InteriorsEmitted, interiorCullPlanes);
            worldRender.InteriorCull = interiorCull.Enabled && interiorCull.Inside != null ? interiorCull : null;
        }

        private void ReportInteriorCull_J4()
        {
            if (screenshotPath != null && ProjWin != null && ProjWin.Visible && Environment.GetEnvironmentVariable("RLE_PROJHIDE") == "1") ProjWin.Visible = false;
            if (interiorCull == null) return;
            var line = interiorCull.ToString();
            panel.WorldInteriorCullStatus = line;
            if (screenshotPath == null) return;
            ReportInteriorCull_L1();
            string key = interiorCull.Inside == null ? "outside" : $"{interiorCull.Inside.Archetype?.Name}/{interiorCull.Room}";
            if (key != interiorCullLast) { interiorCullLast = key; Console.WriteLine("INTCULL " + line); }
        }

        private LightShaftRenderer shaftRenderer;
        private Editor.ExtensionHelpers.ShaftSun shaftSun;
        private static readonly bool ShaftsFlat_J4 = Environment.GetEnvironmentVariable("RLE_SHAFTFLAT") == "1";

        private Editor.ExtensionHelpers.ShaftSun BuildShaftSun_J4()
        {
            Vector3 toSun = new Vector3(0.3f, -0.4f, 0.87f);
            var col = new Vector4(40.0f, 36.0f, 30.0f, 1.0f);
            bool sunNow = true;
            if (timecycle != null && timecycle.HasData)
            {
                timecycle.GetLightDirection(panel.PreviewHour, out var sd, out _);
                toSun = sd;
                sunNow = !(panel.PreviewHour < 5.0f || panel.PreviewHour > 21.0f);
                if (sceneRenderer.GlobalLight.HasValue) col = sceneRenderer.GlobalLight.Value.LightDirColour;
            }
            else if (sceneRenderer.GlobalLight.HasValue)
            {
                toSun = sceneRenderer.GlobalLight.Value.LightDir;
                col = sceneRenderer.GlobalLight.Value.LightDirColour;
            }
            return Editor.ExtensionHelpers.SunFor(toSun, col, sunNow);
        }

        private void FlushShafts_J4(DeviceContext context)
        {
            if (shaftRenderer == null || shaftRenderer.Count == 0) { shaftRenderer?.Clear(); return; }
            try
            {
                int mode = deviceResources.BeginDepthRead(out var depthSrv);
                try
                {
                    shaftRenderer.Flush(context, camera, depthSrv, mode, deviceResources.Width, deviceResources.Height, (float)clock.Elapsed.TotalSeconds);
                }
                finally { deviceResources.EndDepthRead(); }
            }
            catch (Exception ex)
            {
                if (shaftError != ex.Message) { shaftError = ex.Message; Console.WriteLine("LIGHTSHAFTS draw: " + ex.Message); }
                shaftRenderer.Clear();
            }
        }
        private string shaftError;

        private void DisposeRenderers_J4()
        {
            shaftRenderer?.Dispose(); shaftRenderer = null;
            gpuProf?.Dispose(); gpuProf = null; SceneRenderer.Profiler = null;
        }
    }
}

