using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {

        public int FocusKind;

        public bool SnapToVertex;
        public bool SnapHeld;
        public bool SnapActive => SnapToVertex || SnapHeld || SnapMode;
        public VertexSnapHit Snap;

        public bool CornerMode;
        public int SelectedCorner = -1;

        public bool PickingRoomCorners;
        public bool RoomPickAll;
        public bool RequestFinishRoomFromPoints;
        public readonly List<SDX.Vector3> PickedRoomPoints = new List<SDX.Vector3>();

        public bool HasSnapPoint;
        public SDX.Vector3 SnapPoint;
        public string SnapPointSource = "";
        public bool RequestSnapPointToEntity;
        public bool RequestSnapPointToRoomMin, RequestSnapPointToRoomMax, RequestSnapPointToRoomCentre;
        public bool RequestSnapPointAsCorner;
        public bool RequestSnapCornerToNearestVertex;
        public int RequestSnapCornerIndex;

        public void SetSnapPoint(SDX.Vector3 p, string source)
        {
            SnapPoint = p; HasSnapPoint = true; SnapPointSource = source ?? "";
        }

        public void CancelPicking()
        {
            PickingPortalCorners = false; PickedPoints.Clear();
            PickingRoomCorners = false; PickedRoomPoints.Clear();
        }

        public void DrawSnapSection()
        {
            var s = Session;
            DrawSnapModeControls_L4();
            ImGui.Checkbox("Snap to vertex", ref SnapToVertex);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The cursor grabs the nearest vertex of the mesh under it (Blender / 3ds Max vertex snap):\n" +
                                 "portal corners, room corners and gizmo drags land on the vertex, not the surface.\n" +
                                 "Off = hold V to snap for the moment.");
            ImGui.SameLine();
            ImGui.TextDisabled(SnapHeld ? "V held" : "(or hold V)");
            ImGui.SameLine();
            ImGui.Checkbox("Corner handles", ref CornerMode);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Every corner of the selected room (8) or portal (4) becomes a small handle: click one\n" +
                                 "and the gizmo moves THAT corner alone - with V held it snaps to the mesh's vertices.");
            if (CornerMode && SelectedCorner >= 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"corner {SelectedCorner} - release")) SelectedCorner = -1;
            }
            if (SnapActive)
                ImGui.TextColored(Snap.Valid ? UiTheme.Ok : UiTheme.Muted, Snap.Valid ? "vertex  " + Snap.Describe() : "vertex  (none under the cursor)");

            if (s == null) { ImGui.TextDisabled("Start an interior to place rooms and portals from vertices."); return; }

            ImGui.Separator();
            if (PickingPortalCorners || PickingRoomCorners)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.ButtonOn);
                if (ImGui.Button("Cancel picking", new Vector2(-1, 0))) CancelPicking();
                ImGui.PopStyleColor();
                if (PickingRoomCorners)
                {
                    ImGui.TextColored(UiTheme.AccentBright, RoomPickAll
                        ? $"Room: click as many vertices as you like ({PickedRoomPoints.Count} so far), then Finish - the box bounds them all."
                        : PickedRoomPoints.Count == 0
                            ? "Room: click the MIN corner (low, near) - the snap is on, click one of the vertex dots."
                            : "Room: now click the MAX corner (high, far).");
                    if (RoomPickAll && PickedRoomPoints.Count >= 2)
                        if (ImGui.Button($"Finish room from {PickedRoomPoints.Count} vertices", new Vector2(-1, 0))) RequestFinishRoomFromPoints = true;
                }
            }
            else
            {
                ImGui.TextDisabled("Use Add room / Add portal on the toolbar, then drag the corner handles.");
            }

            ImGui.Separator();
            ImGui.TextDisabled("PLACEMENT POINT");
            if (!HasSnapPoint) ImGui.TextDisabled("  none - click a vertex with V held, or send one from 3ds Max");
            else
            {
                ImGui.Text($"  {SnapPoint.X:0.000}, {SnapPoint.Y:0.000}, {SnapPoint.Z:0.000}");
                ImGui.SameLine();
                ImGui.TextDisabled(SnapPointSource);
                if (SelectedEntity >= 0 && SelectedEntity < s.Entities.Count)
                    if (ImGui.Button($"Move entity {SelectedEntity} here", new Vector2(-1, 0))) RequestSnapPointToEntity = true;
                if (CurrentRoom != null)
                {
                    if (ImGui.Button("Room min here")) RequestSnapPointToRoomMin = true;
                    ImGui.SameLine();
                    if (ImGui.Button("Room max here")) RequestSnapPointToRoomMax = true;
                    ImGui.SameLine();
                    if (ImGui.Button("Room centre here")) RequestSnapPointToRoomCentre = true;
                }
                if (PickingPortalCorners || PickingRoomCorners)
                {
                    if (ImGui.Button("Use as the next corner", new Vector2(-1, 0))) RequestSnapPointAsCorner = true;
                }
                if (ImGui.SmallButton("clear##snappt")) HasSnapPoint = false;
            }
        }

        public bool BridgeEnabled;
        public int BridgePort = MloBridge.DefaultPort;
        public string BridgeStatus = "off";
        public bool BridgeError;
        public bool BridgeOpenHeader;
        public bool RequestBridgeToggle;
        public bool DemoCollapseCreator;
        public readonly List<string> BridgeLog = new List<string>();

        public void BridgeSay(string line)
        {
            BridgeLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (BridgeLog.Count > 8) BridgeLog.RemoveAt(0);
        }
    }
}

