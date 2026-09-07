using System;
using System.Globalization;
using System.IO;
using SharpDX;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static readonly string grassFlySpec_U5 = Environment.GetEnvironmentVariable("RLE_GRASSFLY");
        private Vector3 grassFlyAim_U5;
        private float grassFlyYaw_U5, grassFlyPitch_U5;
        private float[] grassFlyDists_U5;
        private int grassFlyStep_U5, grassFlyFrames_U5;
        private bool grassFlyDone_U5;

        private bool GrassFlyParse_U5()
        {
            if (grassFlyDists_U5 != null) return grassFlyDists_U5.Length > 0;
            grassFlyDists_U5 = Array.Empty<float>();
            if (string.IsNullOrEmpty(grassFlySpec_U5)) return false;
            var p = grassFlySpec_U5.Split(',');
            if (p.Length < 6) { Console.WriteLine("GRASSFLY needs x,y,z,yaw,pitch,d1[,d2...]"); return false; }
            float N(int i) => float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            grassFlyAim_U5 = new Vector3(N(0), N(1), N(2));
            grassFlyYaw_U5 = N(3); grassFlyPitch_U5 = N(4);
            var d = new float[p.Length - 5];
            for (int i = 0; i < d.Length; i++) d[i] = N(i + 5);
            grassFlyDists_U5 = d;
            return true;
        }

        private Vector3 GrassFlyEye_U5(float dist)
        {
            var offset = new Vector3(
                (float)(Math.Cos(grassFlyPitch_U5) * Math.Cos(grassFlyYaw_U5)),
                (float)(Math.Cos(grassFlyPitch_U5) * Math.Sin(grassFlyYaw_U5)),
                (float)Math.Sin(grassFlyPitch_U5));
            return grassFlyAim_U5 + offset * dist;
        }

        partial void OnWorldTick_U5()
        {
            if (grassFlyDone_U5 || !GrassFlyParse_U5()) return;
            if (!panel.WorldMode || !worldBuilt || screenshotPath == null) return;

            int settle = int.TryParse(Environment.GetEnvironmentVariable("RLE_GRASSFLYSETTLE"), out int s) && s > 10 ? s : 150;
            if (worldWarmup > 200) worldWarmup = 200;

            if (grassFlyFrames_U5 == 0)
            {
                Console.WriteLine($"GRASSFLY aim {grassFlyAim_U5.X:0.0},{grassFlyAim_U5.Y:0.0},{grassFlyAim_U5.Z:0.0} " +
                                  $"yaw {grassFlyYaw_U5:0.00} pitch {grassFlyPitch_U5:0.00} rungs {grassFlyDists_U5.Length} settle {settle}");
                CameraSequence.ApplyToCamera(camera, GrassFlyEye_U5(grassFlyDists_U5[0]), grassFlyYaw_U5, grassFlyPitch_U5, settings.FovDeg);
            }
            grassFlyFrames_U5++;
            if (grassFlyFrames_U5 < settle) return;
            bool notReady = grassRenderer == null || grassRenderer.BatchesAwaitingModel > 0
                            || World.YmapsOpen + 4 < World.YmapsWanted;
            if (notReady && grassFlyFrames_U5 < settle * 8) return;

            float d = grassFlyDists_U5[grassFlyStep_U5];
            probeShotPending = Path.ChangeExtension(screenshotPath, null) + $".d{d:0}.png";
            Console.WriteLine($"GRASSFLY rung {d:0} m -> {Path.GetFileName(probeShotPending)}  " +
                              $"batches {grassRenderer?.BatchesDrawn ?? -1}/{grassRenderer?.BatchesInRange ?? -1} " +
                              $"instances {grassRenderer?.InstancesDrawn ?? -1} awaiting {grassRenderer?.BatchesAwaitingModel ?? -1} " +
                              $"ymaps {World.YmapsOpen}/{World.YmapsWanted} meshes {worldRender?.MeshesDrawn ?? -1}");

            grassFlyStep_U5++;
            grassFlyFrames_U5 = 1;
            if (grassFlyStep_U5 >= grassFlyDists_U5.Length) { grassFlyDone_U5 = true; return; }
            CameraSequence.ApplyToCamera(camera, GrassFlyEye_U5(grassFlyDists_U5[grassFlyStep_U5]),
                                         grassFlyYaw_U5, grassFlyPitch_U5, settings.FovDeg);
        }
    }
}

