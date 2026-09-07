using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public MloCreatorSession Session;
        public string Status = "";
        public bool StatusIsError;

        public int SelectedRoom = -1;
        public int SelectedPortal = -1;
        public int SelectedEntity = -1;
        public int SelectedSet = -1;

        public enum PageKind { Interior, Room, Portal, Entity, Set, Snap, Write, Timecycles, Lights, Assets }
        public PageKind Page = PageKind.Interior;

        public bool WindowVisible = true;
        public string ProjectPath;
        private float explorerWidth = 300.0f;
        public float ExplorerWidthSetting { get => explorerWidth; set => explorerWidth = value <= 0 ? 300.0f : value; }
        public bool RevealSelection;

        public bool ShowHelpers = true;
        public bool ShowAllRooms = true;
        public bool GizmoEnabled = true;
        public int GizmoMode;

        public bool RequestStartFromScene;
        public bool RequestSaveYtyp;
        public bool RequestImportShellYbn_V34;
        public bool RequestAddEntityByName_V32;
        public bool RequestAddEntityPlace_V32;
        public string AddEntityName_V32 = "";
        public bool RequestExportAll_V31;
        public MloCreatorSession.ExportResult_V31 LastExport_V31;
        public bool RequestAddToProject;
        public bool RequestExportYmap;
        public bool RequestCaptureFromSelection;
        public bool RequestAddRoomAtView;
        public bool RequestAddPortalAtView;
        public int RequestRenameResolve_V67 = -1;
        public bool RequestFrameSelected;
        public bool RequestAssignSelectedPropsToRoom;
        public bool RequestOpenShell, RequestAddProps, RequestImportYtyp;
        public bool RequestSaveProject, RequestSaveProjectAs, RequestOpenProject, RequestNewProject;
        public bool PickingPortalCorners;
        public int PickShape;
        public readonly List<SDX.Vector3> PickedPoints = new List<SDX.Vector3>();

        private string newRoomName = "room";
        private string newSetName = "";
        private string newEntityName = "";
        private int roomFlagsOpen = -1, portalFlagsOpen = -1;
        private string tcFilter = "";
        public bool RequestUndo, RequestRedo;

        private static readonly string[] RoomFlagNames =
        {
            "1 - Freeze Vehicles", "2 - Freeze Peds", "4 - No Directional Light", "8 - No Exterior Lights",
            "16 - Force Freeze", "32 - Reduce Cars", "64 - Reduce Peds", "128 - Force Directional Light On",
            "256 - Dont Render Exterior", "512 - Mirror Potentially Visible",
        };
        private static readonly string[] PortalFlagNames =
        {
            "1 - One-Way", "2 - Link Interiors together", "4 - Mirror", "8 - Disable Timecycle Modifier",
            "16 - Mirror Using Expensive Shaders", "32 - Low LOD Only", "64 - Hide when door closed",
            "128 - Mirror Can See Directional", "256 - Mirror Using Portal Traversal", "512 - Mirror Floor",
            "1024 - Mirror Can See Exterior View", "2048 - Water Surface", "4096 - Water Surface Extend To Horizon",
            "8192 - Use Light Bleed",
        };

        public MloCreatorRoom CurrentRoom => Session != null && SelectedRoom >= 0 && SelectedRoom < Session.Rooms.Count ? Session.Rooms[SelectedRoom] : null;
        public MloCreatorPortal CurrentPortal => Session != null && SelectedPortal >= 0 && SelectedPortal < Session.Portals.Count ? Session.Portals[SelectedPortal] : null;

        public void SetStatus(string s, bool error = false) { Status = s ?? ""; StatusIsError = error; }

        public MloCreatorHistory History => Session?.History;

        public void SelectRoom(int i) { SelectedRoom = i; SelectedPortal = SelectedEntity = SelectedSet = -1; FocusKind = 0; SelectedCorner = -1; Page = PageKind.Room; }
        public void SelectPortal(int i) { SelectedPortal = i; SelectedRoom = SelectedEntity = SelectedSet = -1; FocusKind = 1; SelectedCorner = -1; Page = PageKind.Portal; }
        public void SelectEntity(int i) { SelectedEntity = i; SelectedRoom = SelectedPortal = SelectedSet = -1; FocusKind = 2; SelectedCorner = -1; Page = PageKind.Entity; }
        public void SelectSet(int i) { SelectedSet = i; SelectedRoom = SelectedPortal = SelectedEntity = -1; Page = PageKind.Set; }
        public void ShowPage(PageKind p) { Page = p; }

        public void ClampSelection()
        {
            if (Session == null) { SelectedRoom = SelectedPortal = SelectedEntity = SelectedSet = -1; return; }
            if (SelectedRoom >= Session.Rooms.Count) SelectedRoom = Session.Rooms.Count - 1;
            if (SelectedPortal >= Session.Portals.Count) SelectedPortal = Session.Portals.Count - 1;
            if (SelectedEntity >= Session.Entities.Count) SelectedEntity = Session.Entities.Count - 1;
            if (SelectedSet >= Session.EntitySets.Count) SelectedSet = Session.EntitySets.Count - 1;
        }

        public void DoUndoRedo()
        {
            if (RequestUndo) { RequestUndo = false; if (History != null && History.Undo()) { ClampSelection(); SetStatus("Undo."); } }
            if (RequestRedo) { RequestRedo = false; if (History != null && History.Redo()) { ClampSelection(); SetStatus("Redo."); } }
        }

        private void DrawRoomCombo(string label, ref int value)
        {
            var s = Session;
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo(label, RoomName(value)))
            {
                for (int i = 0; i < s.Rooms.Count; i++)
                    if (ImGui.Selectable($"{i}: {s.Rooms[i].Name}##rc{i}", value == i)) value = i;
                ImGui.EndCombo();
            }
        }

        private string RoomName(int i) =>
            Session != null && i >= 0 && i < Session.Rooms.Count ? $"{i}: {Session.Rooms[i].Name}" : $"{i}: ?";

        private string NameOnly(int i) => Session != null && i >= 0 && i < Session.Rooms.Count ? Session.Rooms[i].Name : "?";

        private static Vector3 V(SDX.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        private static SDX.Vector3 V(Vector3 v) => new SDX.Vector3(v.X, v.Y, v.Z);
    }
}

