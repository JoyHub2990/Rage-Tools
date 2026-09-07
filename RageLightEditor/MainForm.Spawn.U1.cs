using System;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public static readonly Vector3 WorldSpawn_U1 = new Vector3(220.911f, -1060.756f, 60.948f);

        public const float WorldSpawnYaw_U1 = 5.563f;
        public const float WorldSpawnPitch_U1 = 0.05f;

        public static string FormatSpawn_U1() =>
            LightPanel.FormatCoords_O3(new System.Numerics.Vector3(
                WorldSpawn_U1.X, WorldSpawn_U1.Y, WorldSpawn_U1.Z));

        private void GoToWorldSpawn_U1()
        {
            if (camera == null) return;
            CameraSequence.ApplyToCamera(camera, WorldSpawn_U1, WorldSpawnYaw_U1, WorldSpawnPitch_U1,
                                         settings?.FovDeg ?? 85.0f);
            camera.SnapSmoothing();
        }

        partial void ApplyWorldSpawn_U1()
        {
            GoToWorldSpawn_U1();
            Console.WriteLine($"WORLDSPAWN -> ({WorldSpawn_U1.X:0.###}, {WorldSpawn_U1.Y:0.###}, " +
                              $"{WorldSpawn_U1.Z:0.###}) yaw {WorldSpawnYaw_U1:0.###} pitch {WorldSpawnPitch_U1:0.###}" +
                              $" - camera at {camera?.Position}");
            WorldCameraJumped_U1();
        }

        partial void ServiceResetView_U1()
        {
            ServiceSpawnEnv_U1();
            if (panel == null || !panel.RequestResetView_U1) return;
            panel.RequestResetView_U1 = false;
            GoToWorldSpawn_U1();
            WorldCameraJumped_U1();
            panel.GotoStatus_O3 = "View reset to the world spawn - " +
                                  LightPanel.FormatCoords_O3(new System.Numerics.Vector3(
                                      WorldSpawn_U1.X, WorldSpawn_U1.Y, WorldSpawn_U1.Z));
            Console.WriteLine($"WORLDSPAWN reset -> {camera?.Position}");
        }

        private bool resetEnvDone_U1;
        private int animEnvFrame_U1;

        private void ServiceSpawnEnv_U1()
        {
            if (!resetEnvDone_U1 && Environment.GetEnvironmentVariable("RLE_U1RESET") == "1")
            {
                if (panel != null && panel.WorldMode && !worldBuilt) return;
                resetEnvDone_U1 = true;
                panel.RequestResetView_U1 = true;
                Console.WriteLine("U1RESET pressing Reset the view");
            }

            var anim = Environment.GetEnvironmentVariable("RLE_U1ANIM");
            if (string.IsNullOrEmpty(anim)) return;
            Editor.UiAnim.Disabled = false;
            if (!int.TryParse(anim, out int hold) || hold < 1) hold = 6;
            if (panel != null && panel.ArchiveMode && !(panel.Rpf?.Ready ?? false))
            {
                if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 2);
                return;
            }
            if (++animEnvFrame_U1 < hold && screenshotPath != null)
                screenshotFrames = Math.Max(screenshotFrames, 2);
        }

        private void WorldCameraJumped_U1()
        {
            gotoStart_O3 = -1;
        }
    }
}

