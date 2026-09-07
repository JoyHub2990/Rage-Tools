using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private void DrawGizmoExtras_U11()
        {
            if (gizmo == null) return;
            bool local = gizmo.LocalSpace;
            if (local)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.ButtonOn);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiTheme.ButtonOn);
            }
            if (ImGui.Button(local ? "Axes: Local##lgaxes" : "Axes: World##lgaxes")) gizmo.LocalSpace = !local;
            if (local) ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Which way the handles point.\n" +
                                 "World: the map's own north, east and up.\n" +
                                 "Local: along the light itself - Z down the beam, X along its tangent -\n" +
                                 "so a tilted spot moves and turns along the way it is aimed.");

            ImGui.TextDisabled("Snap");
            ImGui.SameLine();
            void SnapButton(string label, int mode, string tip)
            {
                bool on = gizmo.SnapMode == mode;
                if (on)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.ButtonOn);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiTheme.ButtonOn);
                }
                if (ImGui.SmallButton(label)) gizmo.SnapMode = mode;
                if (on) ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
                ImGui.SameLine();
            }
            SnapButton("Off##snapU11", 0, "Move freely.");
            SnapButton("Vertex##snapU11", 1, "While you drag, the light jumps to the model vertex under the cursor.");
            SnapButton("Surface##snapU11", 2, "While you drag, the light lands on the model surface under the cursor.");
            ImGui.NewLine();
            if (gizmo.Dragging && gizmo.SnapMode != 0 && !string.IsNullOrEmpty(gizmo.SnapNote))
                ImGui.TextColored(UiTheme.Accent, gizmo.SnapNote);
            else
                ImGui.TextDisabled(gizmo.SnapMode == 0 ? "Snap off: lights move freely"
                                 : gizmo.SnapMode == 1 ? "Drag a light onto a model vertex"
                                 : "Drag a light onto the model surface");
        }

        private void DrawInstanceLinkButtons_U11()
        {
            int selCount = scene.SelectedIndices.Count;
            ImGui.BeginDisabled(selCount < 2);
            if (ImGui.Button("Link")) scene.LinkSelectedAsInstance();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(selCount < 2
                    ? "Select two or more lights to turn them into one instance."
                    : "Turn the selected lights into one instance: they take the settings of the light you clicked last\n" +
                      "and stay in sync from now on (positions and bones stay their own).\n" +
                      "If one of them already belongs to an instance, the others join that instance instead.");
            ImGui.SameLine();
            bool linked = scene.SelectionHasInstance_U11();
            ImGui.BeginDisabled(!linked);
            if (ImGui.Button("Unlink")) scene.UnlinkSelected();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(linked ? "Cut the selected lights loose from their instance - they keep their settings but stop following."
                                        : "None of the selected lights is part of an instance.");
        }
    }
}
