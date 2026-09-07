using System;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private Scene matScene;

        public Scene MatScene => matScene;

        partial void CreateMatScene_R2()
        {
            matScene = new Scene(modelRenderer, null);
            if (modelRenderer != null && lightScene != null) modelRenderer.ImportedTextures = lightScene.ImportedTextures;
        }

        partial void DisposeMatScene_R2()
        {
            matScene?.Dispose();
        }

        private Scene MatSceneFor_R2(bool materialWorkspace) => materialWorkspace && matScene != null ? matScene : null;

        private void BindMatPanel_R2(Scene target)
        {
            if (materialPanel != null && target != null) materialPanel.Scene = target;
        }

        private void MatOpenLightPropsHere_R2()
        {
            if (matScene == null || lightScene == null) return;
            if (!panel.MaterialMode) { materialPanel.Status = "switch to the Materials workspace first"; return; }
            var paths = lightScene.Files.Where(f => f != null && !string.IsNullOrEmpty(f.Path)).Select(f => f.Path).Distinct().ToList();
            if (paths.Count == 0) { materialPanel.Status = "the Lights workspace has no props open"; return; }
            int added = 0, skipped = 0;
            foreach (var p in paths)
            {
                if (matScene.Files.Any(f => string.Equals(f?.Path, p, StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                try { LoadFile(p); added++; } catch (Exception ex) { Console.WriteLine("MATSCENE copy failed for " + p + ": " + ex.Message); }
            }
            materialPanel.Status = added > 0
                ? $"opened {added} prop{(added == 1 ? "" : "s")} from the Lights workspace here" + (skipped > 0 ? $" ({skipped} already open)" : "")
                : "those props are already open here";
            Console.WriteLine($"MATSCENE opened {added} of {paths.Count} Lights props in the Materials scene ({skipped} already there); light scene still has {lightScene.Files.Count} files");
        }

        internal void ServiceMatScene_R2()
        {
            if (panel == null) return;
            if (panel.RequestMatOpenLightProps_R2)
            {
                panel.RequestMatOpenLightProps_R2 = false;
                MatOpenLightPropsHere_R2();
            }
            panel.MatSceneLightPropCount_R2 = lightScene?.Files.Count ?? 0;
        }
    }
}

