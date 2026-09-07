using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public int RequestAssignEntityRoom_O3 = -2;
        public bool RequestAssignToCurrentRoom_O3;
        public int DropEntityOnRoom_O3 = -1, DropEntityIndex_O3 = -1;
        public string PlaceRoomWhy_O3 = "";

        private int dragEntity_O3 = -1;

        private void DrawEntityRoom_O3(MloCreatorEntity e)
        {
            var s = Session;
            if (s == null || e == null) return;
            ImGui.Spacing();
            ImGui.TextDisabled("ROOM");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Which room of the interior this prop belongs to - the room's attachedObjects\n" +
                                 "in the .ytyp. A prop in no room is one the game will not draw.");

            string cur = e.RoomOverride < 0
                ? $"{e.Room}: {NameOnly(e.Room)}  (by containment)"
                : $"{e.RoomOverride}: {NameOnly(e.RoomOverride)}  (pinned)";
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##o3entroom", cur))
            {
                if (ImGui.Selectable($"by containment - the box its origin is in (now {e.AutoRoom}: {NameOnly(e.AutoRoom)})", e.RoomOverride < 0))
                    RequestAssignEntityRoom_O3 = -1;
                ImGui.Separator();
                for (int r = 0; r < s.Rooms.Count; r++)
                {
                    int n = s.CountInRoom(r);
                    if (ImGui.Selectable($"{r}: {s.Rooms[r].Name}{(r == 0 ? " (limbo)" : "")}   [{n} prop{(n == 1 ? "" : "s")}]##o3er{r}", e.RoomOverride == r))
                        RequestAssignEntityRoom_O3 = r;
                }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Pin the prop to a room whatever its origin sits inside, or hand it back to\n" +
                                 "containment. Undoable, and written into that room when the .ytyp is saved.");

            bool haveRoom = PlaceRoomIndex >= 0 && PlaceRoomIndex < s.Rooms.Count;
            if (!haveRoom) ImGui.BeginDisabled();
            if (ImGui.Button(haveRoom ? $"Assign to the room I am in ({PlaceRoomIndex}: {NameOnly(PlaceRoomIndex)})##o3assignhere"
                                      : "Assign to the room I am in##o3assignhere", new Vector2(-1, 0)))
                RequestAssignToCurrentRoom_O3 = true;
            if (!haveRoom) ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(haveRoom
                    ? "Put the selected prop(s) in the room the tool has chosen:\n" + PlaceRoomWhy_O3 +
                      "\n(The same room a placed asset would go into.)"
                    : "There is no room to assign to: the camera is outside every room box and\nno room is selected in the tree. Select one, or make a room first.");
            if (!string.IsNullOrEmpty(PlaceRoomWhy_O3)) ImGui.TextDisabled("-> " + PlaceRoomWhy_O3);
            ImGui.TextDisabled("Or drag the prop onto a room in the tree on the left.");
        }

        private void EntityLeafDragSource_O3(int ei)
        {
            if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID)) return;
            dragEntity_O3 = ei;
            ImGui.SetDragDropPayload("RLE_MLOENT_O3", IntPtr.Zero, 0);
            var e = Session != null && ei >= 0 && ei < Session.Entities.Count ? Session.Entities[ei] : null;
            ImGui.Text($"move '{e?.Label}' into a room");
            ImGui.EndDragDropSource();
        }

        private void RoomNodeDropTarget_O3(int room)
        {
            if (!ImGui.BeginDragDropTarget()) return;
            var payload = ImGui.AcceptDragDropPayload("RLE_MLOENT_O3");
            unsafe
            {
                if (payload.NativePtr != null && dragEntity_O3 >= 0)
                {
                    DropEntityOnRoom_O3 = room;
                    DropEntityIndex_O3 = dragEntity_O3;
                    dragEntity_O3 = -1;
                }
            }
            ImGui.EndDragDropTarget();
        }
    }
}

