using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ProjectWindow
    {
        public void ClearPage()
        {
            currentItem = null;
            CurrentYmap = null; CurrentYtyp = null;
            CurrentEntity = null; CurrentArchetype = null;
            CurrentRoom = null; CurrentPortal = null; CurrentEntitySet = null;
            ManifestText = null;
        }

        private void DrawCloseProjectButton_J2()
        {
            bool hasProj = Project != null;
            ImGui.SameLine();
            if (!hasProj) ImGui.BeginDisabled();
            if (ImGui.Button("Close")) RequestCloseProject = true;
            if (!hasProj) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(hasProj
                    ? "Close the project" + (Project.AnyUnsaved ? " (it has unsaved changes - you will be asked)" : "") +
                      ".\nThe world goes back to the game's own map data; nothing on disk is touched."
                    : "No project is open.");
        }
    }
}

