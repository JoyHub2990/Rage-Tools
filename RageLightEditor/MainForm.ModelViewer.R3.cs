using System;
using System.Globalization;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool mvCamApplied_R3;

        private void ModelViewCamera_R3()
        {
            if (ModelView == null || settings == null) return;
            ModelView.MoveSpeed = settings.WalkSpeed;
            ModelView.Smoothness = settings.CameraSmoothing;
            ModelViewCameraHeadless_R3();
        }

        private void ModelViewCameraHeadless_R3()
        {
            if (mvCamApplied_R3) return;
            var spec = Environment.GetEnvironmentVariable("RLE_MVCAM");
            if (string.IsNullOrWhiteSpace(spec)) return;
            if (ModelView.Preview == null) return;
            mvCamApplied_R3 = true;

            var p = spec.Split(',');
            float F(int i) => i < p.Length &&
                              float.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0f;

            var cam = ModelView.Cam;
            var before = cam.Position;
            float beforeYaw = cam.Yaw, beforePitch = cam.Pitch;

            cam.Update(0.0f);

            if (Math.Abs(F(3)) > 0.001f || Math.Abs(F(4)) > 0.001f)
            {
                cam.Orbit(F(3), F(4));
                cam.Update(1.0f / 60.0f);
            }
            var move = cam.GetForward() * F(0) + cam.GetRight() * F(1) + Vector3.UnitZ * F(2);
            if (move.LengthSquared() > 1e-6f) cam.Translate(move);
            cam.Update(1.0f / 60.0f);
            ModelView.PushCameraBack_R3();

            Console.WriteLine($"MVCAM moved by ({F(0):0.##},{F(1):0.##},{F(2):0.##}) m and " +
                              $"({F(3):0.#},{F(4):0.#}) px: " +
                              $"eye {before.X:0.##},{before.Y:0.##},{before.Z:0.##} -> " +
                              $"{cam.Position.X:0.##},{cam.Position.Y:0.##},{cam.Position.Z:0.##}, " +
                              $"yaw {beforeYaw:0.###}->{cam.Yaw:0.###} pitch {beforePitch:0.###}->{cam.Pitch:0.###}, " +
                              $"speed {ModelView.FlySpeed_R3(false, false):0.#} m/s at {ModelView.MoveSpeed:0.##}x");
        }

        private void ModelViewCameraTest_R3(Action<string, bool, string> check)
        {
            var world = new Camera();
            var mv = new ModelViewer();
            world.Smoothness = 0.0f; mv.Cam.Smoothness = 0.0f;
            world.Update(0.016f); mv.Cam.Update(0.016f);
            float y0 = world.Yaw, p0 = world.Pitch;
            world.Orbit(37.0f, -11.0f); world.Update(0.016f);
            mv.Cam.Orbit(37.0f, -11.0f); mv.Cam.Update(0.016f);
            bool sameTurn = Math.Abs(world.Yaw - mv.Cam.Yaw) < 1e-4f &&
                            Math.Abs(world.Pitch - mv.Cam.Pitch) < 1e-4f &&
                            Math.Abs(world.Yaw - y0) > 1e-3f;

            float d0 = world.TargetDistance;
            world.Zoom(120.0f);
            mv.Cam.Zoom(120.0f);
            bool sameZoom = Math.Abs(world.TargetDistance - mv.Cam.TargetDistance) < 1e-4f &&
                            world.TargetDistance < d0;

            check("model viewer turns and zooms exactly as the world does",
                  sameTurn && sameZoom,
                  $"yaw {y0:0.####}->{mv.Cam.Yaw:0.####} (world {world.Yaw:0.####}), " +
                  $"pitch {p0:0.####}->{mv.Cam.Pitch:0.####} (world {world.Pitch:0.####}), " +
                  $"distance {d0:0.###}->{mv.Cam.TargetDistance:0.###} (world {world.TargetDistance:0.###})");

            mv.MoveSpeed = 1.7f;
            mv.Cam.TargetDistance = 6.0f;
            float mine = mv.FlySpeed_R3(false, false);
            float theirs = 50.0f * mv.MoveSpeed * Math.Min(mv.Cam.TargetDistance, 20.0f);
            mv.Cam.TargetDistance = 400.0f;
            float mineFar = mv.FlySpeed_R3(false, false);
            float theirsFar = 50.0f * mv.MoveSpeed * 20.0f;
            bool mods = Math.Abs(mv.FlySpeed_R3(true, false) - mineFar * 5.0f) < 0.01f &&
                        Math.Abs(mv.FlySpeed_R3(false, true) - mineFar * 0.2f) < 0.01f;
            check("model viewer flies at the world's speed",
                  Math.Abs(mine - theirs) < 0.01f && Math.Abs(mineFar - theirsFar) < 0.01f && mods,
                  $"{mine:0.#} m/s at 6 m (world {theirs:0.#}), {mineFar:0.#} m/s at 400 m (world {theirsFar:0.#}), " +
                  "Shift x5 and Ctrl x0.2 both right");

            ModelViewCamera_R3();
            check("model viewer reads the same camera settings as the world",
                  ModelView != null && settings != null &&
                  Math.Abs(ModelView.MoveSpeed - settings.WalkSpeed) < 1e-6f &&
                  Math.Abs(ModelView.Smoothness - settings.CameraSmoothing) < 1e-6f,
                  $"speed {ModelView?.MoveSpeed:0.###} = WalkSpeed {settings?.WalkSpeed:0.###}, " +
                  $"smoothing {ModelView?.Smoothness:0.###} = CameraSmoothing {settings?.CameraSmoothing:0.###}");

            var mv2 = new ModelViewer();
            mv2.Cam.Smoothness = 0.0f;
            mv2.Cam.Update(0.016f);
            var start = mv2.Cam.Position;
            mv2.Cam.Translate(mv2.Cam.GetForward() * 25.0f);
            mv2.Cam.Update(0.016f);
            mv2.PushCameraBack_R3();
            var flown = mv2.Cam.Position;
            mv2.ApplyCamera(1.6f);
            float lost = (mv2.Cam.Position - flown).Length();
            check("a flown model-viewer camera is not put back by the next frame",
                  (flown - start).Length() > 20.0f && lost < 0.05f,
                  $"flew {(flown - start).Length():0.##} m, ApplyCamera moved it back {lost:0.###} m");
        }
    }
}

