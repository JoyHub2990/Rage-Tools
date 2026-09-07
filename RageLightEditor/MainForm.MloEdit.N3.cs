using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly HashSet<LoadedFile> mloEntityFiles_N3 = new HashSet<LoadedFile>();
        private int mloSyncedHistory_N3 = -1;
        private MloCreatorSession mloTrackedSession_N3;
        private readonly List<(MloCreatorEntity e, Vector3 pos, Quaternion rot, Vector3 scl)> mloDragOthers_N3 = new List<(MloCreatorEntity, Vector3, Quaternion, Vector3)>();
        private (Vector3 pos, Quaternion rot, Vector3 scl) mloDragPrimary_N3;
        private bool mloEditDemoDone_N3;

        private static Matrix EntityMatrix_N3(MloCreatorEntity e) =>
            Matrix.Transformation(Vector3.Zero, Quaternion.Identity, e.Scale, Vector3.Zero, e.Rotation, e.Position);

        private static LoadedFile EntityFile_N3(MloCreatorSession s, MloCreatorEntity e)
        {
            var f = e?.SourceFile;
            if (f == null || s == null) return null;
            int n = 0;
            foreach (var x in s.Entities) if (x.SourceFile == f && ++n > 1) return null;
            return f;
        }

        private static void PlaceFileAt_N3(LoadedFile f, Matrix want)
        {
            if (f == null) return;
            var old = f.HasPlacement ? f.Placement : Matrix.Identity;
            if (Similar_N3(old, want)) return;
            var delta = Matrix.Invert(old) * want;
            if (f.Model != null)
            {
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                foreach (var m in f.Model.Meshes)
                {
                    m.Transform = m.Transform * delta;
                    m.SetBoundsFromLocal();
                    var b = m.WorldBounds;
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    mn = Vector3.Min(mn, b.Minimum); mx = Vector3.Max(mx, b.Maximum);
                }
                if (mx.X >= mn.X) f.Model.Bounds = new BoundingBox(mn, mx);
            }
            f.Placement = want;
            f.HasPlacement = true;
        }

        private static bool Similar_N3(Matrix a, Matrix b)
        {
            for (int i = 0; i < 16; i++)
                if (Math.Abs(a[i / 4, i % 4] - b[i / 4, i % 4]) > 1e-6f) return false;
            return true;
        }

        private void SyncEntityModel_N3(MloCreatorSession s, MloCreatorEntity e)
        {
            var f = EntityFile_N3(s, e);
            if (f == null) { SyncImportedEntityModel_O3(e); return; }
            PlaceFileAt_N3(f, EntityMatrix_N3(e));
        }

        private void SyncSceneToEntities_N3(MloCreatorPanel ui)
        {
            var s = ui?.Session;
            if (s == null || mloScene == null) return;
            var live = new HashSet<LoadedFile>();
            foreach (var e in s.Entities)
            {
                if (e.SourceFile == null) continue;
                live.Add(e.SourceFile);
                mloEntityFiles_N3.Add(e.SourceFile);
                SyncEntityModel_N3(s, e);
            }
            foreach (var f in mloEntityFiles_N3)
            {
                if (f == null || f == s.ShellFile) continue;
                bool want = live.Contains(f);
                if (f.Visible != want) { f.Visible = want; mloScene.Dirty = true; }
            }
            SyncImportedEntities_O3(ui);
        }

        private WorldGizmoMode MloEntityGizmoMode_N3(MloCreatorPanel ui)
        {
            if (creatorGizmo != null)
            {
                creatorGizmo.TranslateSnap = 0.0f;
                creatorGizmo.RotateSnapDeg = ui.EntitySnapOn ? Math.Max(ui.EntityRotateSnapDeg, 0.0f) : 0.0f;
                creatorGizmo.ScaleSnap = 0.0f;
            }
            return ui.EntityTool switch
            {
                0 => WorldGizmoMode.Select,
                2 => WorldGizmoMode.Rotate,
                3 => WorldGizmoMode.Scale,
                _ => WorldGizmoMode.Translate,
            };
        }

        private void MloDragBegan_N3()
        {
            mloDragOthers_N3.Clear();
            var ui = Creator; var s = ui?.Session;
            if (s == null || ui.SelectedEntities.Count < 2 || ui.SelectedEntity < 0 || ui.SelectedEntity >= s.Entities.Count) return;
            var primary = s.Entities[ui.SelectedEntity];
            mloDragPrimary_N3 = (primary.Position, primary.Rotation, primary.Scale);
            foreach (var i in ui.SelectedEntities)
            {
                if (i < 0 || i >= s.Entities.Count) continue;
                var e = s.Entities[i];
                if (e == primary) continue;
                mloDragOthers_N3.Add((e, e.Position, e.Rotation, e.Scale));
            }
        }

        private void MloTargetChanged_N3(IWorldGizmoTarget t)
        {
            var ui = Creator; var s = ui?.Session;
            if (s == null || !(t?.Key is MloCreatorEntity primary)) return;
            SyncEntityModel_N3(s, primary);
            if (mloDragOthers_N3.Count == 0) return;
            var dPos = primary.Position - mloDragPrimary_N3.pos;
            var dRot = primary.Rotation * Quaternion.Invert(mloDragPrimary_N3.rot);
            var fScale = new Vector3(
                mloDragPrimary_N3.scl.X > 1e-6f ? primary.Scale.X / mloDragPrimary_N3.scl.X : 1.0f,
                mloDragPrimary_N3.scl.Y > 1e-6f ? primary.Scale.Y / mloDragPrimary_N3.scl.Y : 1.0f,
                mloDragPrimary_N3.scl.Z > 1e-6f ? primary.Scale.Z / mloDragPrimary_N3.scl.Z : 1.0f);
            foreach (var (e, pos, rot, scl) in mloDragOthers_N3)
            {
                e.Position = pos + dPos;
                e.Rotation = dRot * rot;
                e.Scale = new Vector3(Math.Max(scl.X * fScale.X, 0.01f), Math.Max(scl.Y * fScale.Y, 0.01f), Math.Max(scl.Z * fScale.Z, 0.01f));
                SyncEntityModel_N3(s, e);
            }
            s.AutoAssignRooms();
        }

        private bool MloEntityClick_N3(MloCreatorPanel ui, int x, int y)
        {
            var s = ui?.Session;
            if (s == null) return false;
            if (MloPickEntity_O3(ui, x, y)) return true;
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            var file = FindPropUnder(ray);
            int ei = -1;
            if (file != null)
            {
                if (file == s.ShellFile) return false;
                ei = s.Entities.FindLastIndex(e => e.SourceFile == file);
                if (ei < 0) return false;
                scene.SelectFile(file, ctrl, false);
            }
            if (ei < 0)
            {
                if (!MloCreatorSession.RayHitScene(scene, ray, out var hit, out _)) return false;
                float best = 3.0f; int bestI = -1;
                for (int i = 0; i < s.Entities.Count; i++)
                {
                    var e = s.Entities[i];
                    if (!e.Include || e.SourceFile != null) continue;
                    float d = (e.Position - hit).Length();
                    if (d < best) { best = d; bestI = i; }
                }
                ei = bestI;
            }
            if (ei < 0) return false;
            ui.SelectEntityMulti(ei, ctrl);
            ui.RevealSelection = true;
            var ent = s.Entities[ei];
            int n = ui.ActiveEntityCount;
            ui.SetStatus(n > 1
                ? $"{n} props selected. W move, E rotate, T scale, Del deletes, Ctrl+D duplicates."
                : $"'{ent.Label}' in room {ent.Room} ({(ent.Room < s.Rooms.Count ? s.Rooms[ent.Room].Name : "?")}). W move, E rotate, T scale, Del deletes.");
            return true;
        }

        private int PlaceRoom_N3(MloCreatorPanel ui)
        {
            var s = ui?.Session;
            if (s == null) return -1;
            switch (ui.PlaceRoomMode)
            {
                case 0: return s.RoomAt(camera.Position);
                case 1: return ui.SelectedRoom >= 0 && ui.SelectedRoom < s.Rooms.Count ? ui.SelectedRoom : -1;
                default: return -1;
            }
        }

        private void ApplyPlaceRoom_N3(MloCreatorPanel ui, MloCreatorSession s, int e0)
        {
            int room = PlaceRoom_N3(ui);
            if (room < 0 || s == null) return;
            for (int i = Math.Max(e0, 0); i < s.Entities.Count; i++) s.Entities[i].RoomOverride = room;
        }

        private void DeleteMloEntities_N3(MloCreatorPanel ui)
        {
            var s = ui.Session;
            var idx = ui.ActiveEntities().Where(i => i >= 0 && i < s.Entities.Count).Distinct().OrderByDescending(i => i).ToList();
            if (idx.Count == 0) { ui.SetStatus("Select a prop first - click it in the viewport, or in the tree.", true); return; }
            string what = idx.Count == 1 ? $"'{s.Entities[idx[0]].Label}'" : $"{idx.Count} props";
            s.PushUndo(idx.Count == 1 ? "Delete prop" : $"Delete {idx.Count} props");
            foreach (var i in idx) if (s.Entities[i].SourceFile != null) mloEntityFiles_N3.Add(s.Entities[i].SourceFile);
            foreach (var i in idx) if (s.Entities[i].SourceInfo != null) mloImportedSeen_O3.Add(s.Entities[i].SourceInfo);
            foreach (var i in idx) { s.RemoveEntity(i); ui.ForgetEntity(i); }
            ui.SelectedEntities.Clear();
            ui.SelectedEntity = -1;
            ui.ShowPage(MloCreatorPanel.PageKind.Interior);
            SyncSceneToEntities_N3(ui);
            mloSyncedHistory_N3 = s.History.Version;
            ui.SetStatus($"Deleted {what}. Ctrl+Z brings it back.");
        }

        private LoadedFile CloneMloProp_N3(LoadedFile src, Matrix placement, string name)
        {
            if (src?.Model == null || src.Model.Meshes.Count == 0) return null;
            var old = src.HasPlacement ? src.Placement : Matrix.Identity;
            var rebase = Matrix.Invert(old) * placement;
            var copy = new RenderModel { Name = src.Model.Name };
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            foreach (var m in src.Model.Meshes)
            {
                var inst = m.CreateInstance(rebase);
                copy.Meshes.Add(inst);
                var b = inst.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                mn = Vector3.Min(mn, b.Minimum); mx = Vector3.Max(mx, b.Maximum);
            }
            if (mx.X >= mn.X) copy.Bounds = new BoundingBox(mn, mx);
            var lights = mloScene.Lights.Where(l => mloScene.OwnerFile(l) == src).Select(Scene.CloneLight).ToArray();
            var lf = mloScene.AddImportedProp(src.Path, src.Ydr, src.Yft, copy, src.Skeleton,
                lights.Length > 0 ? lights : null, placement, 1, src.ReadOnly, name ?? src.Name,
                null, fromMlo: false, drawable: src.Drawable);
            mloEntityFiles_N3.Add(lf);
            return lf;
        }

        private int AddMloEntityCopies_N3(MloCreatorPanel ui, IList<MloCreatorEntity> from, Vector3 offset, string label)
        {
            var s = ui.Session;
            if (from.Count == 0) return 0;
            int first = s.Entities.Count, made = 0;
            foreach (var src in from)
            {
                var e = src.Clone();
                e.Position = src.Position + offset;
                e.SourceInfo = null;
                e.SourceFile = null;
                var m = EntityMatrix_N3(e);
                if (src.SourceFile != null) e.SourceFile = CloneMloProp_N3(src.SourceFile, m, src.SourceFile.Name);
                s.Entities.Add(e);
                made++;
            }
            s.AutoAssignRooms();
            ApplyPlaceRoom_N3(ui, s, first);
            ui.SelectedEntities.Clear();
            ui.SelectEntity(s.Entities.Count - 1);
            if (made > 1) for (int i = first; i < s.Entities.Count; i++) ui.SelectedEntities.Add(i);
            ui.RevealSelection = true;
            mloSyncedHistory_N3 = s.History.Version;
            ui.SetStatus($"{label} {made} prop{(made == 1 ? "" : "s")} into room {s.Entities[s.Entities.Count - 1].Room}. Drag with the gizmo (W).");
            return made;
        }

        private void DuplicateMloEntities_N3(MloCreatorPanel ui)
        {
            var s = ui.Session;
            var picks = ui.ActiveEntities().Where(i => i >= 0 && i < s.Entities.Count).Distinct().Select(i => s.Entities[i]).ToList();
            if (picks.Count == 0) { ui.SetStatus("Select a prop to duplicate.", true); return; }
            s.PushUndo(picks.Count == 1 ? "Duplicate prop" : $"Duplicate {picks.Count} props");
            AddMloEntityCopies_N3(ui, picks, new Vector3(0.25f, 0.25f, 0.0f), "Duplicated");
        }

        private void CopyMloEntities_N3(MloCreatorPanel ui)
        {
            var s = ui.Session;
            var picks = ui.ActiveEntities().Where(i => i >= 0 && i < s.Entities.Count).Distinct().Select(i => s.Entities[i]).ToList();
            ui.EntityClipboard.Clear();
            foreach (var e in picks) ui.EntityClipboard.Add(e.Clone());
            ui.SetStatus(picks.Count == 0 ? "Nothing selected to copy." : $"Copied {picks.Count} prop{(picks.Count == 1 ? "" : "s")} - Ctrl+V puts them down at the placement point.", picks.Count == 0);
        }

        private void PasteMloEntities_N3(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (ui.EntityClipboard.Count == 0) { ui.SetStatus("The clipboard is empty (Ctrl+C copies the selection).", true); return; }
            var anchor = ui.EntityClipboard[0].Position;
            var to = ui.HasSnapPoint ? ui.SnapPoint : camera.Target;
            s.PushUndo(ui.EntityClipboard.Count == 1 ? "Paste prop" : $"Paste {ui.EntityClipboard.Count} props");
            AddMloEntityCopies_N3(ui, ui.EntityClipboard, to - anchor, "Pasted");
        }

        private void AssignMloEntityRoom_N3(MloCreatorPanel ui, int room)
        {
            var s = ui.Session;
            var idx = ui.ActiveEntities().Where(i => i >= 0 && i < s.Entities.Count).Distinct().ToList();
            if (idx.Count == 0) { ui.SetStatus("Select a prop first.", true); return; }
            s.PushUndo(room < 0 ? "Unpin prop room" : "Put prop in a room");
            foreach (var i in idx) s.Entities[i].RoomOverride = room;
            s.AutoAssignRooms();
            mloSyncedHistory_N3 = s.History.Version;
            ui.SetStatus(room < 0
                ? $"{idx.Count} prop(s) back to containment - the room their origin is inside."
                : $"{idx.Count} prop(s) put in room {room}: {(room < s.Rooms.Count ? s.Rooms[room].Name : "?")}.");
        }

        private void ServiceMloEdit_N3(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (s == null) return;
            if (!ReferenceEquals(s, mloTrackedSession_N3))
            {
                mloTrackedSession_N3 = s;
                mloEntityFiles_N3.Clear();
                mloSyncedHistory_N3 = s.History.Version;
                foreach (var e in s.Entities) if (e.SourceFile != null) mloEntityFiles_N3.Add(e.SourceFile);
            }

            ui.PlaceRoomIndex = PlaceRoom_N3(ui);
            ui.PlaceRoomLabel = ui.PlaceRoomIndex >= 0 && ui.PlaceRoomIndex < s.Rooms.Count ? s.Rooms[ui.PlaceRoomIndex].Name : "";

            if (ui.EntityMoved)
            {
                ui.EntityMoved = false;
                if (ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count) SyncEntityModel_N3(s, s.Entities[ui.SelectedEntity]);
            }
            if (s.History.Version != mloSyncedHistory_N3)
            {
                mloSyncedHistory_N3 = s.History.Version;
                SyncSceneToEntities_N3(ui);
            }

            if (ui.RequestDeleteEntities) { ui.RequestDeleteEntities = false; DeleteMloEntities_N3(ui); }
            if (ui.RequestDuplicateEntities) { ui.RequestDuplicateEntities = false; DuplicateMloEntities_N3(ui); }
            if (ui.RequestCopyEntities) { ui.RequestCopyEntities = false; CopyMloEntities_N3(ui); }
            if (ui.RequestPasteEntities) { ui.RequestPasteEntities = false; PasteMloEntities_N3(ui); }
            if (ui.RequestAssignEntityRoom > -2) { int r = ui.RequestAssignEntityRoom; ui.RequestAssignEntityRoom = -2; AssignMloEntityRoom_N3(ui, r); }
            if (ui.RequestPlaceIntoRoom)
            {
                ui.RequestPlaceIntoRoom = false;
                var pick = ui.Assets.Selected;
                if (pick == null) ui.SetStatus("Pick a model in the list first.", true);
                else { ui.Assets.RequestPlace = pick; ui.Assets.RequestPlaceCount = 1; }
            }
            ServiceMloEditDemo_N3(ui);
            ServiceMloEdit_P2(ui);
            ServiceMloEdit_O3(ui);
        }

        private bool MloEditKeyDown_N3(Keys combo)
        {
            if (panel == null || !panel.MloMode) return false;
            var ui = Creator;
            if (ui == null || ui.Session == null) return false;
            if (ImGuiWantsKeyboard && !ignoreImGuiKeyboard) return false;
            if (ui.LightEditingActive) return false;

            if (combo == settings.GetBind("GizmoSelect")) { ui.EntityTool = 0; ui.SetStatus("Select tool  (Q)"); return false; }
            if (combo == settings.GetBind("GizmoMove")) { ui.EntityTool = 1; ui.SetStatus("Move tool  (W) - drag the arrows"); return false; }
            if (combo == settings.GetBind("GizmoRotate")) { ui.EntityTool = 2; ui.SetStatus("Rotate tool  (E) - drag the rings"); return false; }
            if (combo == settings.GetBind("GizmoScale")) { ui.EntityTool = 3; ui.SetStatus("Scale tool  (T) - drag the handles"); return false; }

            if (combo == settings.GetBind("Delete")) { ui.RequestDeleteEntities = true; return true; }
            if (combo == settings.GetBind("Duplicate")) { ui.RequestDuplicateEntities = true; return true; }
            if (combo == settings.GetBind("Copy")) { ui.RequestCopyEntities = true; return true; }
            if (combo == settings.GetBind("Paste")) { ui.RequestPasteEntities = true; return true; }
            if (combo == settings.GetBind("Undo")) { ui.RequestUndo = true; return true; }
            if (combo == settings.GetBind("Redo")) { ui.RequestRedo = true; return true; }
            return false;
        }

        private void ServiceMloEditDemo_N3(MloCreatorPanel ui)
        {
            if (mloEditDemoDone_N3) return;
            var s = ui.Session;
            string place = Environment.GetEnvironmentVariable("RLE_MLOPLACE");
            string move = Environment.GetEnvironmentVariable("RLE_MLOMOVE");
            string rot = Environment.GetEnvironmentVariable("RLE_MLOROT");
            bool del = Environment.GetEnvironmentVariable("RLE_MLODELETE") == "1";
            bool dup = Environment.GetEnvironmentVariable("RLE_MLODUP") == "1";
            bool undo = Environment.GetEnvironmentVariable("RLE_MLOUNDO") == "1";
            string write = Environment.GetEnvironmentVariable("RLE_MLOWRITE");
            string search = Environment.GetEnvironmentVariable("RLE_MLOSEARCH");
            bool any = !string.IsNullOrEmpty(place) || !string.IsNullOrEmpty(move) || !string.IsNullOrEmpty(rot) || del || dup || undo
                       || !string.IsNullOrEmpty(write) || !string.IsNullOrEmpty(search);
            if (!any || !mloScene.HasModel || (DebugMlo != null && !debugMloDone)) return;
            if (!string.IsNullOrEmpty(search) && !(panel.Archive?.Ready ?? false)) return;
            mloEditDemoDone_N3 = true;

            if (!string.IsNullOrEmpty(search))
            {
                var lib = ui.Assets;
                lib.Query = search == "*" ? "" : search;
                lib.SourceFilter = 0;
                lib.Dirty = false;
                TickArchiveIndex_N3(lib);
                RunMloAssetSearch_L3(lib);
                ui.ShowPage(MloCreatorPanel.PageKind.Assets);
                Console.WriteLine($"MLOEDIT search '{lib.Query}': {lib.Results.Count} of {lib.ResultTotal} shown, {lib.Results.Count(r => r.FromArchive)} from the game archives; header '{lib.ArchiveStatus}'");
            }

            if (!string.IsNullOrEmpty(place))
            {
                var it = FindMloAsset_L3(place);
                if (it == null) { Console.WriteLine($"MLOEDIT place '{place}': not found in the archives / prop folders"); }
                else
                {
                    ui.Assets.PlaceAt = 1;
                    ui.PlaceRoomMode = 0;
                    int before = s.Entities.Count;
                    PlaceMloAssets_L3(ui, new[] { it }, 1);
                    int ei = s.Entities.Count - 1;
                    if (ei >= before)
                    {
                        ui.SelectEntity(ei);
                        var e = s.Entities[ei];
                        Console.WriteLine($"MLOEDIT placed '{it.Name}' entity {ei} at ({e.Position.X:0.00},{e.Position.Y:0.00},{e.Position.Z:0.00}) room {e.Room} " +
                                          $"(override {e.RoomOverride}); mloScene {mloScene.Files.Count} files, model {(EntityFile_N3(s, e)?.Model?.Meshes.Count ?? 0)} meshes");
                    }
                    else Console.WriteLine($"MLOEDIT place '{place}': the model would not load");
                }
            }
            bool editing = del || dup || undo || !string.IsNullOrEmpty(move) || !string.IsNullOrEmpty(rot);
            if (editing && ui.SelectedEntity < 0 && s.Entities.Count > 0) ui.SelectEntity(s.Entities.Count - 1);
            var sel = ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count ? s.Entities[ui.SelectedEntity] : null;

            if (sel != null && !string.IsNullOrEmpty(move))
            {
                var p = move.Split(',');
                if (p.Length >= 3 &&
                    float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dx) &&
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dy) &&
                    float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dz))
                {
                    var before = sel.Position;
                    var target = new CreatorEntityTarget(s, sel);
                    s.PushUndo("Move prop");
                    target.SetPosition(before + new Vector3(dx, dy, dz));
                    MloTargetChanged_N3(target);
                    var f = EntityFile_N3(s, sel);
                    Console.WriteLine($"MLOEDIT moved entity {ui.SelectedEntity} ({before.X:0.00},{before.Y:0.00},{before.Z:0.00}) -> " +
                                      $"({sel.Position.X:0.00},{sel.Position.Y:0.00},{sel.Position.Z:0.00}) room {sel.Room}; " +
                                      $"model at ({(f != null ? f.Placement.TranslationVector.X : float.NaN):0.00},{(f != null ? f.Placement.TranslationVector.Y : float.NaN):0.00},{(f != null ? f.Placement.TranslationVector.Z : float.NaN):0.00})");
                }
            }
            if (sel != null && !string.IsNullOrEmpty(rot) &&
                float.TryParse(rot, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var deg))
            {
                var target = new CreatorEntityTarget(s, sel);
                s.PushUndo("Rotate prop");
                target.SetOrientation(Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(deg)) * sel.Rotation);
                MloTargetChanged_N3(target);
                Console.WriteLine($"MLOEDIT rotated entity {ui.SelectedEntity} by {deg:0.#} deg about Z");
            }
            if (dup)
            {
                int n = s.Entities.Count, f0 = mloScene.Files.Count;
                DuplicateMloEntities_N3(ui);
                var copy = s.Entities.Count > 0 ? s.Entities[s.Entities.Count - 1] : null;
                Console.WriteLine($"MLOEDIT duplicated: {n} -> {s.Entities.Count} entities, {f0} -> {mloScene.Files.Count} files; " +
                                  $"copy '{copy?.Label}' at ({copy?.Position.X ?? 0:0.00},{copy?.Position.Y ?? 0:0.00},{copy?.Position.Z ?? 0:0.00}) room {copy?.Room}");
            }
            if (del)
            {
                int n = s.Entities.Count;
                var f = sel != null ? EntityFile_N3(s, sel) : null;
                DeleteMloEntities_N3(ui);
                Console.WriteLine($"MLOEDIT deleted: {n} -> {s.Entities.Count} entities; the model is {(f == null ? "(none)" : f.Visible ? "STILL VISIBLE" : "hidden")}");
            }
            if (undo)
            {
                ui.RequestUndo = true;
                ui.DoUndoRedo();
                SyncSceneToEntities_N3(ui);
                mloSyncedHistory_N3 = s.History.Version;
                var back = s.Entities.Count > 0 ? s.Entities[s.Entities.Count - 1] : null;
                var f = back != null ? EntityFile_N3(s, back) : null;
                Console.WriteLine($"MLOEDIT undo: {s.Entities.Count} entities; last '{back?.Label}' room {back?.Room}; the model is {(f == null ? "(none)" : f.Visible ? "visible again" : "STILL HIDDEN")}");
                if (back != null) ui.SelectEntity(s.Entities.Count - 1);
            }
            if (!string.IsNullOrEmpty(write))
            {
                try
                {
                    var oldName = s.Name;
                    if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "rle_n3_test";
                    var yt = s.SaveYtyp(write);
                    var rt = new YtypFile();
                    rt.Load(File.ReadAllBytes(write));
                    var mlo = rt.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
                    string err = MloEditor.Validate(rt);
                    int rooms = mlo?.rooms?.Length ?? 0, ents = mlo?.entities?.Length ?? 0;
                    string where = "(none)";
                    if (mlo?.entities != null && mlo.entities.Length > 0)
                    {
                        int last = mlo.entities.Length - 1;
                        int room = -1;
                        for (int r = 0; r < rooms; r++)
                            if (mlo.rooms[r].AttachedObjects?.Contains((uint)last) == true) { room = r; break; }
                        var d = mlo.entities[last]._Data;
                        where = $"{d.archetypeName} at ({d.position.X:0.00},{d.position.Y:0.00},{d.position.Z:0.00}) in room {room} ({(room >= 0 && room < rooms ? mlo.rooms[room].RoomName : "?")})";
                    }
                    Console.WriteLine($"MLOEDIT wrote {write}: {rooms} rooms, {ents} entities, validate '{err}'; last entity {where}");
                    s.Name = oldName;
                }
                catch (Exception ex) { Console.WriteLine("MLOEDIT write failed: " + ex.Message); }
            }
            ui.WindowVisible = Environment.GetEnvironmentVariable("RLE_MLOWIN") != "0";
            if (!string.IsNullOrEmpty(search)) ui.ShowPage(MloCreatorPanel.PageKind.Assets);
            if (Environment.GetEnvironmentVariable("RLE_MLOFRAME") == "1")
            {
                var at = ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count ? s.Entities[ui.SelectedEntity].Position : camera.Target;
                camera.Target = at;
                camera.Distance = 3.5f;
                camera.SnapSmoothing();
                camera.Update();
            }
        }

        private void MloEditTest_N3(Action<string, bool, string> check)
        {
            try
            {
                var ui = Creator;
                var s = ui.Session;
                if (s == null) { check("mloedit: a session exists", false, "none"); return; }

                string dir = Path.Combine(Path.GetTempPath(), "rle_mloassets");
                Directory.CreateDirectory(dir);
                string a = Path.Combine(dir, "prop_alpha_bench.ydr");
                if (!File.Exists(a))
                {
                    string src = Path.Combine(Path.GetTempPath(), "rle_mlocreator", "light_test_scene.ydr");
                    if (!File.Exists(src)) TestSceneGenerator.Run(src);
                    File.Copy(src, a, true);
                }
                var room = s.AddRoom("n3_room", new Vector3(-40, -40, -20), new Vector3(-30, -30, -10));
                int roomIdx = s.Rooms.Count - 1;
                s.AutoAssignRooms();
                ui.SelectRoom(roomIdx);

                var item = new MloAssetItem { Name = "prop_alpha_bench", FileName = "prop_alpha_bench.ydr", Path = a, IsYft = false, Source = MloAssetSource.PropFolder };
                ui.PlaceRoomMode = 1;
                ui.Assets.PlaceAt = 1;
                int ents0 = s.Entities.Count, files0 = mloScene.Files.Count;
                PlaceMloAssets_L3(ui, new[] { item }, 1);
                check("mloedit: the prop was placed", s.Entities.Count == ents0 + 1 && mloScene.Files.Count == files0 + 1, $"{ents0} -> {s.Entities.Count} entities, {files0} -> {mloScene.Files.Count} files");
                int ei = s.Entities.Count - 1;
                var e = s.Entities[ei];
                check("mloedit: it went into the room the tool says", e.Room == roomIdx && e.RoomOverride == roomIdx, $"room {e.Room} (override {e.RoomOverride}), wanted {roomIdx}");
                var file = EntityFile_N3(s, e);
                check("mloedit: the entity owns one model in the MLO scene", file != null && file.Model != null && file.Model.Meshes.Count > 0, file == null ? "no file" : $"{file.Model?.Meshes.Count ?? 0} meshes");
                if (file == null) return;

                ui.SelectEntity(ei);
                var before = e.Position;
                var meshBefore = file.Model.Meshes[0].WorldBounds.Center;
                var target = new CreatorEntityTarget(s, e);
                s.PushUndo("Move prop");
                target.SetPosition(before + new Vector3(2.5f, -1.0f, 0.5f));
                MloTargetChanged_N3(target);
                check("mloedit: the entity moved", (e.Position - (before + new Vector3(2.5f, -1.0f, 0.5f))).Length() < 1e-4f, $"{e.Position}");
                check("mloedit: its model moved with it",
                      (file.Model.Meshes[0].WorldBounds.Center - (meshBefore + new Vector3(2.5f, -1.0f, 0.5f))).Length() < 1e-3f &&
                      (file.Placement.TranslationVector - e.Position).Length() < 1e-4f,
                      $"mesh {file.Model.Meshes[0].WorldBounds.Center}, placement {file.Placement.TranslationVector}");

                {
                    var mt0 = new CreatorEntityTarget(s, e);
                    mt0.SetPosition(new Vector3(500, 500, 500));
                    MloTargetChanged_N3(mt0);
                    camera.Target = file.Model.Bounds.Center;
                    camera.Distance = Math.Max((file.Model.Bounds.Maximum - file.Model.Bounds.Minimum).Length(), 1.0f) * 1.2f;
                    camera.SnapSmoothing(); camera.Update();
                    ui.SelectedEntities.Clear(); ui.SelectedEntity = -1;
                    bool picked = MloEntityClick_N3(ui, deviceResources.Width / 2, deviceResources.Height / 2);
                    check("mloedit: a click in the viewport picks the prop", picked && ui.SelectedEntity == ei, $"picked {picked}, entity {ui.SelectedEntity} (wanted {ei})");
                    int other = s.Entities.FindIndex(x => x != e && x.Include);
                    if (other >= 0)
                    {
                        ui.SelectEntityMulti(other, true);
                        check("mloedit: Ctrl+click multi-selects", ui.ActiveEntityCount == 2 && ui.SelectedEntities.Contains(ei) && ui.SelectedEntities.Contains(other), $"{ui.ActiveEntityCount} selected");
                        ui.SelectEntityMulti(other, true);
                        check("mloedit: Ctrl+click again drops it", ui.ActiveEntityCount == 1, $"{ui.ActiveEntityCount} selected");
                    }
                    ui.SelectedEntities.Clear();
                    mt0.SetPosition(before + new Vector3(2.5f, -1.0f, 0.5f));
                    MloTargetChanged_N3(mt0);
                    ui.SelectEntity(ei);
                }

                var q = Quaternion.RotationAxis(Vector3.UnitZ, 0.7f);
                target.SetOrientation(q); MloTargetChanged_N3(target);
                target.SetScale(new Vector3(2.0f, 2.0f, 2.0f)); MloTargetChanged_N3(target);
                var want = EntityMatrix_N3(e);
                check("mloedit: rotate + scale reach the model", Similar_N3(file.Placement, want) && Math.Abs(e.Scale.X - 2.0f) < 1e-4f,
                      $"scale {e.Scale}, placement {(Similar_N3(file.Placement, want) ? "matches" : "differs")}");

                int fBefore = mloScene.Files.Count;
                DuplicateMloEntities_N3(ui);
                var dup = s.Entities[s.Entities.Count - 1];
                var dupFile = EntityFile_N3(s, dup);
                check("mloedit: duplicate makes its own prop", s.Entities.Count == ei + 2 && mloScene.Files.Count == fBefore + 1 && dupFile != null && dupFile != file && (dupFile?.Model?.Meshes.Count ?? 0) > 0,
                      $"{s.Entities.Count} entities, {mloScene.Files.Count} files, dup model {(dupFile?.Model?.Meshes.Count ?? 0)} meshes");
                check("mloedit: the copy is beside the original", dupFile != null && (dup.Position - e.Position).Length() > 0.2f, $"{(dupFile != null ? (dup.Position - e.Position).Length() : 0):0.###} m apart");
                var origAt = file.Placement.TranslationVector;
                var dt = new CreatorEntityTarget(s, dup);
                dt.SetPosition(dup.Position + new Vector3(0, 3, 0)); MloTargetChanged_N3(dt);
                check("mloedit: the copy moves on its own", (file.Placement.TranslationVector - origAt).Length() < 1e-4f && (dupFile.Placement.TranslationVector - dup.Position).Length() < 1e-4f,
                      $"original {file.Placement.TranslationVector}, copy {dupFile.Placement.TranslationVector}");

                ui.SelectEntity(s.Entities.Count - 1);
                CopyMloEntities_N3(ui);
                check("mloedit: copy fills the clipboard", ui.EntityClipboard.Count == 1, $"{ui.EntityClipboard.Count}");
                int pEnts = s.Entities.Count;
                ui.HasSnapPoint = true; ui.SnapPoint = new Vector3(-35, -35, -15);
                s.PushUndo("Paste prop");
                PasteMloEntities_N3(ui);
                var pasted = s.Entities[s.Entities.Count - 1];
                check("mloedit: paste lands on the placement point", s.Entities.Count == pEnts + 1 && (pasted.Position - new Vector3(-35, -35, -15)).Length() < 1e-3f, $"{pasted.Position}");
                ui.HasSnapPoint = false;

                ui.SelectedEntities.Clear();
                ui.SelectEntity(ei);
                int dEnts = s.Entities.Count;
                DeleteMloEntities_N3(ui);
                check("mloedit: delete removes the entity", s.Entities.Count == dEnts - 1, $"{dEnts} -> {s.Entities.Count}");
                check("mloedit: and takes its model out of the scene", !file.Visible, $"visible {file.Visible}");
                ui.RequestUndo = true; ui.DoUndoRedo();
                SyncSceneToEntities_N3(ui);
                mloSyncedHistory_N3 = s.History.Version;
                check("mloedit: undo brings the entity back", s.Entities.Count == dEnts, $"{s.Entities.Count}");
                check("mloedit: and its model with it", file.Visible && s.Entities.Any(x => x.SourceFile == file), $"visible {file.Visible}");

                var moved = s.Entities.First(x => x.SourceFile == file);
                moved.RoomOverride = -1;
                var mt = new CreatorEntityTarget(s, moved);
                mt.SetPosition(room.Centre);
                check("mloedit: dropping it in a room's box puts it in that room", moved.Room == roomIdx, $"room {moved.Room}, wanted {roomIdx}");
                mt.SetPosition(new Vector3(900, 900, 900));
                check("mloedit: dragging it out of every room falls back to limbo", moved.Room == 0, $"room {moved.Room}");

                moved.RoomOverride = roomIdx;
                moved.Position = room.Centre;
                var oldName = s.Name; s.Name = "rle_n3_edit"; s.TextureDictionary = "rle_n3_edit";
                var ytyp = s.BuildYtyp("rle_n3_edit.ytyp");
                var arch = ytyp.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
                int idx = -1;
                if (arch?.entities != null)
                    for (int i = 0; i < arch.entities.Length; i++)
                        if (arch.entities[i]._Data.archetypeName.Hash == JenkHash.GenHash("prop_alpha_bench") &&
                            (arch.entities[i]._Data.position - room.Centre).Length() < 1e-3f) { idx = i; break; }
                bool inRoom = idx >= 0 && roomIdx < (arch.rooms?.Length ?? 0) && (arch.rooms[roomIdx].AttachedObjects?.Contains((uint)idx) ?? false);
                check("mloedit: the moved prop is written into its room", inRoom, $"entity {idx}, room {roomIdx} holds {(idx >= 0 && roomIdx < (arch?.rooms?.Length ?? 0) ? string.Join(",", arch.rooms[roomIdx].AttachedObjects ?? Array.Empty<uint>()) : "-")}");
                check("mloedit: MloEditor.Validate passes", string.IsNullOrEmpty(MloEditor.Validate(ytyp)), MloEditor.Validate(ytyp) ?? "");
                s.Name = oldName;

                ui.EntityTool = 1;
                check("mloedit: the tool maps to the gizmo mode", MloEntityGizmoMode_N3(ui) == WorldGizmoMode.Translate, MloEntityGizmoMode_N3(ui).ToString());
                ui.EntityTool = 2;
                check("mloedit: E is rotate", MloEntityGizmoMode_N3(ui) == WorldGizmoMode.Rotate, MloEntityGizmoMode_N3(ui).ToString());
                ui.EntityTool = 3;
                check("mloedit: T is scale", MloEntityGizmoMode_N3(ui) == WorldGizmoMode.Scale, MloEntityGizmoMode_N3(ui).ToString());
                ui.EntityTool = 1;

                MloAssetsSearchTest_N3(check);
            }
            catch (Exception ex)
            {
                check("mloedit: no exception", false, ex.ToString());
            }
        }
    }
}

