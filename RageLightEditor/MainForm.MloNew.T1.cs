using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {

        private void ServiceMloNew_T1(MloCreatorPanel ui)
        {
            if (ui == null) return;
            var sc = mloScene;
            ui.SceneFileCount_T1 = sc?.Files.Count ?? 0;
            ui.SceneImportCount_T1 = sc?.Imports_R1?.Count ?? 0;

            ServiceShotEnv_T1(ui);

            if (!ui.RequestNewInterior_T1) return;
            ui.RequestNewInterior_T1 = false;
            NewMloInterior_T1(ui);
        }

        private bool shotEnvDone_T1;

        private void ServiceShotEnv_T1(MloCreatorPanel ui)
        {
            if (shotEnvDone_T1) return;
            var spec = Environment.GetEnvironmentVariable("RLE_T1SHOT");
            if (string.IsNullOrEmpty(spec) || ui.Session == null) return;
            shotEnvDone_T1 = true;
            foreach (var bit in spec.Split(','))
            {
                var b = bit.Trim();
                if (b.Equals("new", StringComparison.OrdinalIgnoreCase)) { ui.ForceNewPopup_T1 = true; continue; }
                if (Enum.TryParse<MloCreatorPanel.PageKind>(b, true, out var page)) ui.ShowPage(page);
            }
            Console.WriteLine($"T1SHOT page={ui.Page} newPopup={ui.ForceNewPopup_T1}");
        }

        private void NewMloInterior_T1(MloCreatorPanel ui)
        {
            int rooms = ui.Session?.Rooms.Count ?? 0, portals = ui.Session?.Portals.Count ?? 0;
            int ents = ui.Session?.Entities.Count ?? 0, sets = ui.Session?.EntitySets.Count ?? 0;
            int files = mloScene?.Files.Count ?? 0, imports = mloScene?.Imports_R1?.Count ?? 0;

            ui.Session?.PushUndo("New interior");

            ui.EndSnap();
            ui.PickingPortalCorners = false; ui.PickedPoints.Clear();
            ui.PickingRoomCorners = false; ui.PickedRoomPoints.Clear(); ui.RoomPickAll = false;
            ui.CancelRoomMeshPick();
            ui.CornerMode = false; ui.SelectedCorner = -1;
            ui.HasSnapPoint = false;
            ui.SelectedEntities.Clear();
            ui.EntityClipboard.Clear();

            ClearMloCreatorScene_T1();

            ui.Session = new MloCreatorSession();
            if (mloScene != null) ui.Session.FitBoundsToScene(mloScene);
            ui.ProjectPath = null;
            ui.SelectRoom(0);
            ui.ShowPage(MloCreatorPanel.PageKind.Interior);
            creatorAutoStarted = true;
            ui.SetStatus("New interior - everything was cleared. Open a shell, or add props, to start over.");
            Console.WriteLine($"T1NEW cleared {rooms} rooms, {portals} portals, {sets} sets, {ents} entities, " +
                              $"{files} scene file(s), {imports} import(s); scene now {mloScene?.Files.Count ?? 0} file(s), " +
                              $"{mloScene?.Lights.Count ?? 0} light(s), {mloScene?.Imports_R1?.Count ?? 0} import(s)");
        }

        private void ClearMloCreatorScene_T1()
        {
            var sc = mloScene;
            if (sc == null) return;
            if (ReferenceEquals(sc, scene)) ClearImports();
            else { sc.RemoveMloProps(); sc.ClearMlo(); }
            sc.Imports_R1?.Clear();
            sc.CloseAllFiles();
            ClearImporterCacheIfUnused_L3();
            InvalidateMloLocalBoxes_S1();
        }

        private void MloNewProjectTest_T1(Action<string, bool, string> check)
        {
            var wasSpace = panel.Workspace;
            try
            {
                panel.SwitchWorkspace(LightPanel.Space.Mlo);
                var ui = Creator;
                if (ui == null) { check("mlo new: the creator exists", false, "no creator"); return; }

                ui.EntitySnapOn = true; ui.EntityTool = 1;
                MloEntityGizmoMode_N3(ui);
                check("mlo snap: the position step is gone",
                      creatorGizmo == null || creatorGizmo.TranslateSnap <= 0.0f, creatorGizmo?.TranslateSnap.ToString("0.###") ?? "-");
                check("mlo snap: the scale step is gone",
                      creatorGizmo == null || creatorGizmo.ScaleSnap <= 0.0f, creatorGizmo?.ScaleSnap.ToString("0.###") ?? "-");
                ui.EntityTool = 2;
                MloEntityGizmoMode_N3(ui);
                check("mlo snap: rotation still snaps, at 5 degrees",
                      creatorGizmo != null && Math.Abs(creatorGizmo.RotateSnapDeg - 5.0f) < 1e-4f,
                      creatorGizmo?.RotateSnapDeg.ToString("0.##") ?? "-");
                ui.EntityTool = 1;

                ui.Session ??= new MloCreatorSession();
                var s = ui.Session;
                int roomsBefore = s.Rooms.Count;
                s.AddRoom("t1_room", new SharpDX.Vector3(-2, -2, -1), new SharpDX.Vector3(2, 2, 2));
                s.AddEntitySet("t1_set");
                int filesBefore = mloScene?.Files.Count ?? 0;
                bool hadSomething = s.Rooms.Count > roomsBefore;
                check("mlo new: there is an interior to clear", hadSomething, $"{s.Rooms.Count} rooms, {filesBefore} files");

                NewMloInterior_T1(ui);
                var after = ui.Session;
                check("mlo new: the interior is empty again",
                      after != null && after.Rooms.Count == 1 && after.Portals.Count == 0 &&
                      after.Entities.Count == 0 && after.EntitySets.Count == 0,
                      after == null ? "no session" : $"{after.Rooms.Count} rooms, {after.Portals.Count} portals, {after.Entities.Count} entities, {after.EntitySets.Count} sets");
                check("mlo new: the scene behind it is empty too",
                      (mloScene?.Files.Count ?? 0) == 0 && (mloScene?.Imports_R1?.Count ?? 0) == 0 && (mloScene?.Lights.Count ?? 0) == 0,
                      $"{mloScene?.Files.Count ?? 0} files, {mloScene?.Imports_R1?.Count ?? 0} imports, {mloScene?.Lights.Count ?? 0} lights");
                check("mlo new: no picker is left armed",
                      !ui.SnapMode && !ui.PickingPortalCorners && !ui.PickingRoomCorners && !ui.PickingRoomMesh && !ui.HasSnapPoint,
                      $"snap {ui.SnapMode} portal {ui.PickingPortalCorners} room {ui.PickingRoomCorners} mesh {ui.PickingRoomMesh}");
                check("mlo new: the .mloproj path is forgotten", string.IsNullOrEmpty(ui.ProjectPath), ui.ProjectPath ?? "");
            }
            catch (Exception ex)
            {
                check("mlo new: no exception", false, ex.ToString());
            }
            finally
            {
                if (screenshotPath == null) panel.SwitchWorkspace(wasSpace);
            }
        }
    }
}

