using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        partial void DrawInteriorTimecycleRow_K2()
        {
            if (WorldMode) return;
            LoadInteriorTimecycleOption();
            ImGui.Checkbox("Interior timecycle", ref WorldInteriorTimecycle);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Grade the scene with the modifier of the ROOM the camera is in - the one the\n" +
                                 "room's ytyp names (timecycleName) - blended in over half a second and out again\n" +
                                 "at the door, room by room, as the game does. Outside every room nothing is\n" +
                                 "applied. Picking a modifier below by hand holds until the next room.\n\n" +
                                 "Off: the modifier below is yours to choose and stays put.");
            if (WorldInteriorTimecycle && !string.IsNullOrEmpty(InteriorTimecycleStatus)) DrawInteriorTimecycleReadout();
        }

        private bool gridHadModel;

        public bool ShowGridEffective(bool hasModel)
        {
            if (WorldMode) return false;
            if (hasModel && !gridHadModel) ShowGrid = false;
            else if (!hasModel && gridHadModel) ShowGrid = true;
            gridHadModel = hasModel;
            return ShowGrid;
        }
    }
}

