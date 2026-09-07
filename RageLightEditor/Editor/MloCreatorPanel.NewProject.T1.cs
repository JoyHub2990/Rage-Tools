using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {

        public bool RequestNewInterior_T1;

        public int SceneFileCount_T1;
        public int SceneImportCount_T1;

        public bool ForceNewPopup_T1;

        public void DrawNewInteriorButton_T1()
        {
            bool press = LightPanel.DangerButton("New", Vector2.Zero);
            if (ForceNewPopup_T1) press = true;
            if (press) ImGui.OpenPopup("##t1new");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start over: throw the whole interior away - rooms, portals, entity sets, imported\n" +
                                 ".ytyps, the shell and every placed prop - and leave an empty creator.");
            if (!ImGui.BeginPopup("##t1new")) return;
            ImGui.TextDisabled("NEW INTERIOR");
            var s = Session;
            ImGui.TextWrapped(s == null
                ? "This clears everything loaded in the MLO Creator."
                : $"This throws away {s.Rooms.Count} room(s), {s.Portals.Count} portal(s), {s.EntitySets.Count} entity set(s) and " +
                  $"{s.Entities.Count} entit{(s.Entities.Count == 1 ? "y" : "ies")}, closes the {SceneFileCount_T1} loaded file(s)" +
                  (SceneImportCount_T1 > 0 ? $" and removes {SceneImportCount_T1} imported .ytyp(s)" : "") + ".");
            ImGui.Spacing();
            ImGui.TextColored(UiTheme.Warn, "Ctrl+Z brings the rooms and portals back, not the models.");
            ImGui.Spacing();
            if (LightPanel.DangerButton("Yes - delete everything", new Vector2(-1, 0)))
            {
                RequestNewInterior_T1 = true;
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Cancel", new Vector2(-1, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}

