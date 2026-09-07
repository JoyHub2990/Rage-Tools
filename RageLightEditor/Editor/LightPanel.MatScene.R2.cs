using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private Scene matScene;

        public Scene MatScene { get => matScene; set => matScene = value; }

        public bool RequestMatOpenLightProps_R2;
        public int MatSceneLightPropCount_R2;

        private void DrawMatSceneRow_R2()
        {
            if (!MaterialMode) return;
            float full = ImGui.GetContentRegionAvail().X;
            ImGui.TextDisabled($"This workspace has its own props ({scene.Files.Count}).");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Materials, Lights and the MLO Creator each keep their own props.\nOpening or importing here changes nothing in the other workspaces.");
            if (MatSceneLightPropCount_R2 > 0)
            {
                if (ImGui.Button($"Open the Lights props here ({MatSceneLightPropCount_R2})", new Vector2(full, 0)))
                    RequestMatOpenLightProps_R2 = true;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Loads the same files into this workspace. The Lights workspace keeps everything it has.");
            }
        }
    }
}

