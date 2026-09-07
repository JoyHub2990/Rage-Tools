using System;
using System.Collections.Generic;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void RenderSunMap_R5(DeviceContext context, IEnumerable<RenderMesh> meshes, Vector3 sunDir,
            BoundingBox sceneBounds, ref bool done);
        partial void SunCascadeDirty_R5(ref bool dirty);
        partial void SeqTest_R5(Action<string, bool, string> check);

        private static readonly bool SunCascadesEverywhere_R5 =
            Environment.GetEnvironmentVariable("RLE_SUNCASCADES_R5") != "0" && !SceneRenderer.LegacySunShadow_R5;

        private bool sunCascaded_R5;
        private Vector3 r5LastFitCam, r5LastFitFwd;
        private static readonly float[] r5Intervals = new float[SunCascades.MaxCascades];

        private SunCascades SunCascadesActive_R5 => sunCascaded_R5 ? shadowRenderer.Cascades : null;

        partial void SunCascadeDirty_R5(ref bool dirty)
        {
            if (!SunCascadesEverywhere_R5 || panel.WorldMode) return;
            if (Vector3.DistanceSquared(camera.Position, r5LastFitCam) > 4.0f ||
                Vector3.DistanceSquared(camera.GetForward(), r5LastFitFwd) > 0.0004f) dirty = true;
        }

        partial void RenderSunMap_R5(DeviceContext context, IEnumerable<RenderMesh> meshes, Vector3 sunDir,
            BoundingBox sceneBounds, ref bool done)
        {
            if (!SunCascadesEverywhere_R5) { sunCascaded_R5 = false; return; }

            float diag = (sceneBounds.Maximum - sceneBounds.Minimum).Length();
            float dist = MathUtil.Clamp(diag, 40.0f, Math.Max(panel.SunShadowDistance, 40.0f));
            int count = Math.Clamp(panel.SunCascadeCount, 1, SunCascades.MaxCascades);
            float scale = dist / SunCascades.DefaultIntervals[count - 1];
            for (int i = 0; i < count; i++) r5Intervals[i] = SunCascades.DefaultIntervals[i] * scale;

            shadowRenderer.RenderSunCascades(context, meshes, camera, sunDir, r5Intervals, count,
                                             sceneBounds.Minimum, sceneBounds.Maximum);
            sunCascaded_R5 = true;
            r5LastFitCam = camera.Position;
            r5LastFitFwd = camera.GetForward();
            done = true;
        }

        partial void SeqTest_R5(Action<string, bool, string> check)
        {
            var bounds = new BoundingBox(new Vector3(-10, -10, -3), new Vector3(10, 10, 3));
            float diag = (bounds.Maximum - bounds.Minimum).Length();
            float dist = MathUtil.Clamp(diag, 40.0f, 600.0f);
            check("r5 sun: a small interior's cascades reach 40 m, not the street's 600", Math.Abs(dist - 40.0f) < 0.01f, dist.ToString("0.0"));

            float scale = dist / SunCascades.DefaultIntervals[3];
            float near = SunCascades.DefaultIntervals[0] * scale;
            check("r5 sun: its near cascade is a small slice of the range", near > 0.1f && near <= dist / 20.0f,
                  $"{near:0.000} m of {dist:0} m");

            var cam = new Camera { Target = new Vector3(0, 0, 1), Distance = 6.0f, Yaw = 0.8f, Pitch = 0.1f };
            cam.FieldOfView = 85.0f * 0.0174533f;
            cam.SetAspect(16.0f / 9.0f);
            cam.SnapSmoothing();
            cam.Update();
            var cs = new SunCascades();
            var ivals = new float[SunCascades.MaxCascades];
            for (int i = 0; i < 4; i++) ivals[i] = SunCascades.DefaultIntervals[i] * scale;
            cs.Fit(cam, Vector3.Normalize(new Vector3(0.2f, -0.5f, 0.83f)), ivals, 4, ShadowRenderer.SunSize,
                   bounds.Minimum, bounds.Maximum);
            var clip = Vector4.Transform(new Vector4(cam.Position + cam.GetForward() * 0.5f, 1.0f), cs.ViewProj[0]);
            bool inside = Math.Abs(clip.X / clip.W) <= 1.0f && Math.Abs(clip.Y / clip.W) <= 1.0f;
            check("r5 sun: the near cascade covers what the eye is looking at", inside,
                  $"clip {(clip.X / clip.W):0.00},{(clip.Y / clip.W):0.00} texel {cs.TexelWorld[0]:0.0000} m");
            check("r5 sun: ...at a texel fine enough to see a wall", cs.TexelWorld[0] < 0.05f, cs.TexelWorld[0].ToString("0.0000"));
        }
    }
}

