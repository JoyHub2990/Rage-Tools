using System;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int furShotStage_V21;
        private int furShotSettle_V21;

        partial void OnWorldTick_FurShot_V21()
        {
            if (furShotStage_V21 >= 2) return;
            var name = Environment.GetEnvironmentVariable("RLE_FURSHOT");
            if (string.IsNullOrEmpty(name)) return;

            if (furShotStage_V21 == 1)
            {
                if (Environment.GetEnvironmentVariable("RLE_FURSHOT_FRAME") == "1") { furShotStage_V21 = 2; return; }
                if (scene == null || scene.Files.Count == 0) return;
                if (++furShotSettle_V21 < 10) return;
                furShotStage_V21 = 2;
                camera.Target = new SharpDX.Vector3(0, 0, 0.3f);
                camera.Distance = 6.0f;
                camera.Pitch = 0.22f;
                camera.Yaw = 0.8f;
                camera.SnapSmoothing();
                camera.Update();
                screenshotFrames = Math.Max(screenshotFrames, 12);
                Console.WriteLine("FURSHOT camera moved onto the lawn (6 m, grazing)");
                return;
            }
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) return;

            uint h = JenkHash.GenHash(System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant());
            if (!c.YdrDict.TryGetValue(h, out var fe) || fe == null)
            {
                furShotStage_V21 = 2;
                Console.WriteLine($"FURSHOT no .ydr called '{name}' in the archives");
                return;
            }

            if (!gameFiles.TextureIndexReady) return;

            furShotStage_V21 = 1;
            panel.RequestOpenArchiveFile = fe;
            screenshotFrames = Math.Max(screenshotFrames, 150);
            Console.WriteLine($"FURSHOT opening {fe.Name} from {fe.Path}");
        }

        partial void OnCapture_FurShot_V21()
        {
            if (Environment.GetEnvironmentVariable("RLE_FURDBG") != "1") return;
            Console.WriteLine($"FURDBG at capture: {sceneRenderer.FurShellsDrawn_V21} shell draw(s) over {sceneRenderer.FurMeshesDrawn_V21} fur mesh pass(es), {sceneRenderer.FurFinsDrawn_V21} fin pass(es) this run");
            Console.WriteLine($"FURDBG at capture: camera pos {camera.Position} target {camera.Target} dist {camera.Distance:0.0}");
        }
    }
}

