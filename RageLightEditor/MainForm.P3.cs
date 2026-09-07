using System;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void ApplyProjectContentSettings_P3()
        {
            if (settings == null || panel == null) return;
            if (Environment.GetEnvironmentVariable("RLE_NOPROJHIDE") == "1") return;
            panel.HideBaseUnderProject = settings.HideBaseUnderProject;
        }

        private void SyncProjectContentOption_P3()
        {
            World.HideBaseUnderProject = panel.HideBaseUnderProject;
            if (settings != null) settings.HideBaseUnderProject = panel.HideBaseUnderProject;
            panel.ProjectBaseYmapsHidden = World.BaseContentYmapsHidden;
            panel.ProjectFootprints = World.ProjectFootprintBoxes;
            panel.ProjectGrassHidden = World.GrassBatchesUnderProject;
            panel.ProjectLodLightsHidden = World.LodLightsUnderProject;
        }

        private void BeforeGrassDraw_P3()
        {
            SyncProjectContentOption_P3();
            if (grassRenderer == null) return;
            World.GrassBatchesUnderProject = 0;
            grassRenderer.BatchHidden = World.ProjectOverrides != null && World.HideBaseUnderProject
                ? World.GrassBatchHidden_P3
                : null;
        }

        private void BeforeWorldLights_P3()
        {
            SyncProjectContentOption_P3();
            World.LodLightsUnderProject = 0;
            worldRender.Lights.LodLightHidden = World.ProjectOverrides != null && World.HideBaseUnderProject
                ? World.LodLightHidden_P3
                : null;
        }

        private void PrintProjectContentDiagnostics_P3()
        {
            int projYmaps = World.ProjectOverrides?.Count ?? 0;
            int grassBatches = 0, grassYmaps = 0, lodLights = 0, lodYmaps = 0;
            foreach (var y in World.ContentYmaps)
            {
                var b = y?.GrassInstanceBatches;
                if (b != null && b.Length > 0) { grassYmaps++; grassBatches += b.Length; }
                var ll = y?.LODLights?.LodLights;
                if (ll != null && ll.Length > 0) { lodYmaps++; lodLights += ll.Length; }
            }
            Console.WriteLine($"P3CONTENT project ymaps {projYmaps} hideBase {World.HideBaseUnderProject} " +
                              $"baseYmapsTakenOver {World.BaseContentYmapsHidden} footprints {World.ProjectFootprintBoxes} " +
                              $"grass {grassBatches} batches in {grassYmaps} ymaps (drawn {grassRenderer?.BatchesDrawn ?? 0}, " +
                              $"{World.GrassBatchesUnderProject} refused under project) " +
                              $"lodLights {lodLights} in {lodYmaps} ymaps ({World.LodLightsUnderProject} refused under project)");
        }

        partial void SelfTest_P3(Action<string, bool, string> check)
        {
            Editor.ExtensionHelpers.ShaftFluxTest_P3(check);
            Editor.WorldStreamer.ContentPartnerTest_P3(check);
            Rendering.ModelRenderer.ShadowBakeTest_P3(check);
        }
    }
}

