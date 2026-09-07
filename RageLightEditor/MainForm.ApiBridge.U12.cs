using System;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool apiDemoDone_U12;
        private int apiHoldFrames_U12, apiHoldSettle_U12;

        private void Tick_U12()
        {
            var ui = Creator;
            if (ui == null || panel == null) return;
            ServiceApiDemo_U12(ui);
            HoldCaptureForClient_U12();
            TickMloBridge(ui);
            Tick_FiveM_U12();
        }

        private void ServiceApiDemo_U12(MloCreatorPanel ui)
        {
            if (apiDemoDone_U12) return;
            var want = Environment.GetEnvironmentVariable("RLE_BRIDGE");
            if (string.IsNullOrEmpty(want)) { apiDemoDone_U12 = true; return; }
            if (DebugMlo != null && !debugMloDone) return;
            apiDemoDone_U12 = true;
            int port = int.TryParse(want, out var p) ? p : MloBridge.DefaultPort;
            int got = StartMloBridge(ui, port);
            if (screenshotPath != null)
            {
                apiHoldFrames_U12 = 1800;
                apiHoldSettle_U12 = int.TryParse(Environment.GetEnvironmentVariable("RLE_BRIDGEHOLD"), out var hold) ? hold : 900;
            }
            Console.WriteLine($"BRIDGEDEMO listening 127.0.0.1:{got}");
        }

        private void HoldCaptureForClient_U12()
        {
            if (apiHoldFrames_U12 <= 0 || screenshotPath == null) return;
            if (screenshotFrames <= 0) return;
            if ((mloBridge?.Clients ?? 0) == 0) { apiHoldFrames_U12--; screenshotFrames = Math.Max(screenshotFrames, 3); }
            else if (apiHoldSettle_U12-- > 0) screenshotFrames = Math.Max(screenshotFrames, 2);
            else apiHoldFrames_U12 = 0;
        }

        private static string AppVersion_P5()
        {
            try { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0"; }
            catch { return "1.0"; }
        }

        private DccLight BridgeLight_P5(int i)
        {
            var l = scene != null && i >= 0 && i < scene.Lights.Count ? scene.Lights[i] : null;
            if (l == null) return new DccLight { Index = -1, Kind = "point" };
            var owner = scene.OwnerFile(l);
            var mtx = owner != null && owner.HasPlacement ? owner.Placement : Matrix.Identity;
            var dir = Vector3.TransformNormal(l.Direction, mtx);
            if (dir.LengthSquared() > 1e-8f) dir.Normalize(); else dir = new Vector3(0, 0, -1);
            return new DccLight
            {
                Index = i,
                Kind = l.Type == CodeWalker.GameFiles.LightType.Spot ? "spot" : (l.Type == CodeWalker.GameFiles.LightType.Capsule ? "capsule" : "point"),
                Position = Vector3.TransformCoordinate(l.Position, mtx),
                Direction = dir,
                Colour = new Vector3(l.ColorR / 255.0f, l.ColorG / 255.0f, l.ColorB / 255.0f),
                Intensity = l.Intensity,
                Range = l.Falloff,
                InnerDeg = l.ConeInnerAngle,
                OuterDeg = l.ConeOuterAngle,
                Owner = owner?.Name ?? "",
            };
        }

        private void SetCameraFrom_P5(Vector3 pos, Vector3 target, float fovDeg)
        {
            var dir = target - pos;
            float dist = dir.Length();
            if (dist < 1e-4f) { dir = camera.GetForward(); dist = 1.0f; }
            dir /= dist;
            camera.Pitch = camera.TargetPitch = MathUtil.Clamp((float)Math.Asin(MathUtil.Clamp(-dir.Z, -1.0f, 1.0f)), -Camera_PitchLimit_P5, Camera_PitchLimit_P5);
            camera.Yaw = camera.TargetYaw = (float)Math.Atan2(-dir.Y, -dir.X);
            float d = MathUtil.Clamp(dist, camera.MinDistance, camera.MaxDistance);
            camera.Distance = camera.TargetDistance = d;
            camera.Target = pos + dir * d;
            if (fovDeg > 1.0f)
            {
                settings.FovDeg = MathUtil.Clamp(fovDeg, Rendering.Camera.MinFovDeg, Rendering.Camera.MaxFovDeg);
                camera.FieldOfView = MathUtil.DegreesToRadians(settings.FovDeg);
            }
            camera.SnapSmoothing();
            camera.Update();
        }

        private const float Camera_PitchLimit_P5 = 1.55f;
    }
}
