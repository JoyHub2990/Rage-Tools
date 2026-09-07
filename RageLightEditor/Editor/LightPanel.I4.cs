using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool ShowScenarioModels = true;

        partial void DrawHelpersExtras_I4()
        {
            OptCheck("Scenario models", ref ShowScenarioModels,
                     "The ped (or vehicle) a scenario point spawns, standing on the point and facing\n" +
                     "its direction - the point's model set (ambientpedmodelsets / vehiclemodelsets)\n" +
                     "decides who. The selected point always shows its model; with this on the\n" +
                     "hovered point shows its model too (as a ghost).");
        }
    }
}

