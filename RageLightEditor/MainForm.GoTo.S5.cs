using System;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public const float FramePitchLimit_S5 = 0.55f;
        public const float FrameFit_S5 = 1.25f;
        public const float FrameMinDist_S5 = 2.0f, FrameMaxDist_S5 = 600.0f;

        public static float FrameDistance_S5(float radius, float fovRadians)
        {
            float r = Math.Max(radius, 0.25f);
            float half = MathUtil.Clamp(fovRadians * 0.5f, 0.08f, 1.4f);
            float d = r / (float)Math.Sin(half) * FrameFit_S5;
            return MathUtil.Clamp(d, FrameMinDist_S5, FrameMaxDist_S5);
        }

        private void FrameWorldTarget_S5(Vector3 centre, float radius)
        {
            if (camera == null) return;
            float r = MathUtil.Clamp(radius, 0.5f, 250.0f);
            float dist = FrameDistance_S5(r, camera.FieldOfView);
            camera.Pitch = MathUtil.Clamp(camera.Pitch, -FramePitchLimit_S5, FramePitchLimit_S5);
            camera.Target = centre;
            camera.Distance = dist;
            camera.MaxDistance = Math.Max(camera.MaxDistance, dist * 4.0f);
            camera.SnapSmoothing();
            camera.Update();
            if (!camera.OrbitSubject_U3 && camera.Distance > camera.OrbitAnchorMax) camera.ReanchorPivot(camera.OrbitAnchorMax);
            LastFrame_S5 = (centre, dist);
            if (Environment.GetEnvironmentVariable("RLE_GOTO_S5") == "1")
                Console.WriteLine($"GOTOS5 framed {centre.X:0.0},{centre.Y:0.0},{centre.Z:0.0} r {r:0.0} at {dist:0.0} m, eye {camera.Position.X:0.0},{camera.Position.Y:0.0},{camera.Position.Z:0.0} " +
                                  $"look {camera.GetForward().X:0.00},{camera.GetForward().Y:0.00},{camera.GetForward().Z:0.00} " +
                                  $"aim error {AimError_S5(centre):0.000}");
        }

        public (Vector3 centre, float dist) LastFrame_S5;

        public float AimError_S5(Vector3 p)
        {
            var v = p - camera.Position;
            float len = v.Length();
            if (len < 1e-4f) return 0.0f;
            return (v / len - camera.GetForward()).Length();
        }
    }
}

