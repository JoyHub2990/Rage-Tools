using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool texIndexWasReady_V21;

        partial void OnTick_TexDicts_V21()
        {
            if (scene == null) return;
            bool idxReady = gameFiles != null && gameFiles.TextureIndexReady;
            if (idxReady && !texIndexWasReady_V21 && scene.HasModel && scene.MissingTextures_V21().Length > 0)
            {
                int before = scene.MissingTextures_V21().Length;
                scene.RebuildModel();
                int after = scene.MissingTextures_V21().Length;
                if (before != after)
                    Console.WriteLine($"TEXDICTS index ready: rebuilt the scene, {before} -> {after} missing");
            }
            texIndexWasReady_V21 = idxReady;
        }
    }
}

