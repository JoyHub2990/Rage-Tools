using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public bool RequestRoomFromShell;
        public bool PickingRoomMesh;
        public int PickingRoomMeshRoom = -1;

        public bool LightsSectionOpen;
        public bool LightEditingActive => Page == PageKind.Lights || (LightsSectionOpen && !PropFocused_P2);
        public bool AssetsSectionOpen;

        private void DrawRoomCaptureRow_M1()
        {
            var s = Session;
            if (s == null || CurrentRoom == null || SelectedRoom == 0) return;
            if (ImGui.SmallButton("From shell##m1fromshell")) RequestRoomFromShell = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The room's box from the shell mesh: the loaded files whose name contains this room's name\n" +
                                 $"('{CurrentRoom.Name}'), else the whole shell (limbo's box). Room 0 / limbo is always the whole shell.");
            ImGui.SameLine();
            bool armed = PickingRoomMesh && PickingRoomMeshRoom == SelectedRoom;
            if (armed) ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.ButtonOn);
            if (ImGui.SmallButton(armed ? "From mesh: click one in the viewport... (Esc cancels)##m1frommesh" : "From mesh (click)##m1frommesh"))
            {
                if (armed) { PickingRoomMesh = false; PickingRoomMeshRoom = -1; }
                else { PickingRoomMesh = true; PickingRoomMeshRoom = SelectedRoom; SetStatus("Click a mesh in the viewport (a wall, a floor, a prop): its bounds become the room's box.  (Esc cancels)"); }
            }
            if (armed) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered() && !armed) ImGui.SetTooltip("The next click in the viewport: the bounds of the mesh under it become this room's box.");
            if (!s.ShellBoundsKnown)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(no shell yet)");
            }
        }

        public bool CancelRoomMeshPick()
        {
            if (!PickingRoomMesh) return false;
            PickingRoomMesh = false; PickingRoomMeshRoom = -1;
            return true;
        }
    }
}

