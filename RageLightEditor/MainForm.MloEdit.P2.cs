using System;
using System.Collections.Generic;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly HashSet<LoadedFile> mloAdopted_P2 = new HashSet<LoadedFile>();
        private MloCreatorSession mloAdoptSession_P2;
        private int mloAdoptFileCount_P2 = -1;
        private bool mloBoxNoteSaid_P2;

        private void ServiceMloEdit_P2(MloCreatorPanel ui)
        {
            var s = ui?.Session;
            if (s == null || mloScene == null) return;
            if (!ReferenceEquals(s, mloAdoptSession_P2))
            {
                mloAdoptSession_P2 = s;
                mloAdopted_P2.Clear();
                mloAdoptFileCount_P2 = -1;
                mloBoxNoteSaid_P2 = false;
                foreach (var e in s.Entities) if (e.SourceFile != null) mloAdopted_P2.Add(e.SourceFile);
                if (s.FlippedRooms_P2 > 0)
                {
                    ui.BoxNote_P2 = $"{s.FlippedRooms_P2} box(es) in this .ytyp had min/max the wrong way round - re-ordered, so the rooms are real again.";
                    ui.SetStatus(ui.BoxNote_P2);
                    mloBoxNoteSaid_P2 = true;
                }
            }
            if (!creatorGizmoDragging) s.NormaliseBoxes_P2();
            AdoptLooseFiles_P2(ui, s);
        }

        private void AdoptLooseFiles_P2(MloCreatorPanel ui, MloCreatorSession s)
        {
            if (mloAdoptFileCount_P2 == mloScene.Files.Count) return;
            mloAdoptFileCount_P2 = mloScene.Files.Count;

            var present = new HashSet<LoadedFile>(mloScene.Files);
            int pruned = 0;
            for (int i = s.Entities.Count - 1; i >= 0; i--)
            {
                var f = s.Entities[i].SourceFile;
                if (f == null || present.Contains(f)) continue;
                s.Entities.RemoveAt(i);
                ui.ForgetEntity(i);
                pruned++;
            }
            if (pruned > 0)
            {
                mloAdopted_P2.RemoveWhere(f => !present.Contains(f));
                if (s.ShellFile != null && !present.Contains(s.ShellFile)) s.ShellFile = null;
            }

            var claimed = new HashSet<LoadedFile>();
            foreach (var e in s.Entities) if (e.SourceFile != null) claimed.Add(e.SourceFile);
            var loose = new List<LoadedFile>();
            foreach (var f in mloScene.Files)
            {
                if (f == null || f.FromMlo || f == s.ShellFile || f.Model == null || f.Model.Meshes.Count == 0) continue;
                if (claimed.Contains(f) || mloAdopted_P2.Contains(f)) continue;
                if (mloEntityFiles_N3.Contains(f) || !f.Visible) continue;
                loose.Add(f);
            }
            SyncShellEntityLive_V35(ui, s);

            if (loose.Count == 0)
            {
                if (pruned > 0) ui.SetStatus($"{pruned} prop(s) left the interior with their model.");
                return;
            }
            s.PushUndo(loose.Count == 1 ? "Add prop to the interior" : $"Add {loose.Count} props to the interior");
            int first = s.Entities.Count;
            foreach (var f in loose)
            {
                mloAdopted_P2.Add(f);
                mloEntityFiles_N3.Add(f);
                s.AddEntityFromFile(f);
            }
            s.AutoAssignRooms();
            for (int i = first; i < s.Entities.Count; i++)
            {
                int room = PlaceRoomWithFallback_O3(ui, s.Entities[i].Position, out _);
                if (room >= 0) s.Entities[i].RoomOverride = room;
            }
            s.AutoAssignRooms();
            mloSyncedHistory_N3 = s.History.Version;
            if (s.Entities.Count > first)
            {
                ui.SelectedEntities.Clear();
                ui.SelectEntity(s.Entities.Count - 1);
                ui.RevealSelection = true;
                var last = s.Entities[s.Entities.Count - 1];
                int added = s.Entities.Count - first;
                ui.SetStatus($"{added} prop{(added == 1 ? "" : "s")} added to the interior (room {last.Room}: " +
                             $"{(last.Room < s.Rooms.Count ? s.Rooms[last.Room].Name : "?")}). Drag the gizmo (W) to place it; Del removes it.");
            }
            Console.WriteLine($"MLOADOPT_P2: {loose.Count} loose file(s) became entities ({first} -> {s.Entities.Count}); pruned {pruned}");
        }

        private void SeqTest_P2(Action<string, bool, string> check)
        {
            MloPropControlTest_P2(check);
            MloAssetCategoryTest_P2(check);
        }

        private void MloPropControlTest_P2(Action<string, bool, string> check)
        {
            try
            {
                var ui = Creator;
                var s = ui?.Session;
                if (s == null) { check("mloP2: a session exists", false, "none"); return; }

                var r = s.AddRoom("p2_room", new Vector3(700, 700, 700), new Vector3(710, 712, 704));
                int roomIdx = s.Rooms.Count - 1;
                r.Min = new Vector3(710, 700, 704); r.Max = new Vector3(700, 712, 700);
                check("mloP2: an inverted box reads as empty before the fix", !r.IsValid, $"valid {r.IsValid}");
                int flipped = s.NormaliseBoxes_P2();
                check("mloP2: ordering the corners makes it a real room", r.IsValid && flipped >= 1 &&
                      r.Min == new Vector3(700, 700, 700) && r.Max == new Vector3(710, 712, 704),
                      $"{flipped} fixed, {r.Min} .. {r.Max}");
                check("mloP2: and the room then contains what is inside it", r.Contains(new Vector3(705, 706, 702)) && s.RoomAt(new Vector3(705, 706, 702)) == roomIdx,
                      $"RoomAt = {s.RoomAt(new Vector3(705, 706, 702))}");
                check("mloP2: a second pass changes nothing (idempotent)", s.NormaliseBoxes_P2() == 0, "0 expected");

                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_p2");
                System.IO.Directory.CreateDirectory(dir);
                string ydr = System.IO.Path.Combine(dir, "prop_p2_late.ydr");
                if (!System.IO.File.Exists(ydr))
                {
                    string src = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_mlocreator", "light_test_scene.ydr");
                    if (!System.IO.File.Exists(src)) TestSceneGenerator.Run(src);
                    System.IO.File.Copy(src, ydr, true);
                }
                var wasWorkspace = panel.Workspace;
                panel.SwitchWorkspace(LightPanel.Space.Mlo);
                ui.PlaceRoomMode = 1; ui.SelectRoom(roomIdx);
                int e0 = s.Entities.Count;
                LoadFile(ydr);
                mloAdoptFileCount_P2 = -1;
                ServiceMloEdit_P2(ui);
                var lf = mloScene.Files.LastOrDefault(f => f.Path == ydr);
                int ei = s.Entities.FindIndex(e => e.SourceFile == lf);
                check("mloP2: 'Add props...' puts the model in the interior", ei >= 0 && s.Entities.Count == e0 + 1,
                      $"{e0} -> {s.Entities.Count} entities, index {ei}");
                if (ei >= 0)
                {
                    check("mloP2: and into the room the tool says", s.Entities[ei].RoomOverride == roomIdx, $"room {s.Entities[ei].Room} (override {s.Entities[ei].RoomOverride})");
                    ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
                    var e = s.Entities[ei];
                    var meshBefore = lf.Model.Meshes[0].WorldBounds.Center;
                    var t = new CreatorEntityTarget(s, e);
                    t.SetPosition(e.Position + new Vector3(2, 0, 0));
                    MloTargetChanged_N3(t);
                    check("mloP2: the added prop moves with the gizmo, model and all",
                          (lf.Model.Meshes[0].WorldBounds.Center - (meshBefore + new Vector3(2, 0, 0))).Length() < 1e-3f,
                          $"{meshBefore} -> {lf.Model.Meshes[0].WorldBounds.Center}");
                    check("mloP2: and the gizmo target exists for it", MloWorkspaceGizmoTarget(ui) != null, "null" );
                    bool l0 = ui.LightsSectionOpen;
                    ui.LightsSectionOpen = true;
                    check("mloP2: a selected prop keeps its gizmo with the Lights section open",
                          !ui.LightEditingActive && CreatorGizmoTargets().Count == 1, $"lightEditing {ui.LightEditingActive}, targets {CreatorGizmoTargets().Count}");
                    ui.SelectRoom(roomIdx);
                    check("mloP2: with no prop selected the Lights section still owns it", ui.LightEditingActive, "expected true");
                    ui.LightsSectionOpen = l0;
                    ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
                    int other = s.Entities.FindIndex(x => x != e && x.Include);
                    if (other >= 0)
                    {
                        ui.SelectEntityMulti(other, true);
                        ui.SelectEntityMulti(ei, false);
                        check("mloP2: a plain tree click drops the old multi-selection",
                              ui.ActiveEntityCount == 1 && ui.SelectedEntity == ei && ui.SelectedEntities.Count == 0,
                              $"{ui.ActiveEntityCount} active, {ui.SelectedEntities.Count} in the list");
                    }
                    ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
                    DeleteMloEntities_N3(ui);
                    string ydr2 = System.IO.Path.Combine(dir, "prop_p2_late2.ydr");
                    if (!System.IO.File.Exists(ydr2)) System.IO.File.Copy(ydr, ydr2, true);
                    LoadFile(ydr2);
                    mloAdoptFileCount_P2 = -1;
                    ServiceMloEdit_P2(ui);
                    check("mloP2: a deleted prop is not adopted back by the next load",
                          !s.Entities.Any(x => x.SourceFile == lf) && s.Entities.Any(x => x.SourceFile?.Path == ydr2),
                          $"{s.Entities.Count} entities, deleted one back = {s.Entities.Any(x => x.SourceFile == lf)}");
                    var lf2 = mloScene.Files.LastOrDefault(f => f.Path == ydr2);
                    if (lf2 != null) mloScene.RemoveFile(lf2);
                    ui.RequestUndo = true; ui.DoUndoRedo();
                    SyncSceneToEntities_N3(ui); mloSyncedHistory_N3 = s.History.Version;
                    mloAdoptFileCount_P2 = -1;
                    ServiceMloEdit_P2(ui);

                    mloScene.RemoveFile(lf);
                    mloAdoptFileCount_P2 = -1;
                    ServiceMloEdit_P2(ui);
                    check("mloP2: closing the model takes its entity with it", !s.Entities.Any(x => x.SourceFile == lf), $"{s.Entities.Count} entities");
                }

                for (int i = s.Entities.Count - 1; i >= 0; i--) if (s.Entities[i].RoomOverride == roomIdx) s.Entities.RemoveAt(i);
                if (s.Rooms.Count - 1 == roomIdx) s.Rooms.RemoveAt(roomIdx);
                s.AutoAssignRooms();
                ui.SelectedEntities.Clear(); ui.SelectedEntity = -1; ui.SelectRoom(0);
                if (panel.Workspace != wasWorkspace) panel.SwitchWorkspace(wasWorkspace);
            }
            catch (Exception ex)
            {
                check("mloP2: no exception", false, ex.ToString());
            }
        }
    }
}

