using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool mloProbeDone_P2;

        private void ServiceMloProbe_P2(MloCreatorPanel ui)
        {
            if (mloProbeDone_P2) return;
            string spec = Environment.GetEnvironmentVariable("RLE_MLOPROBE");
            if (string.IsNullOrEmpty(spec)) return;
            var s = ui?.Session;
            if (s == null || mloScene == null || !mloScene.HasModel) return;
            if (DebugMlo != null && !debugMloDone) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_MLOASSET")) && !mloAssetDemoDone) return;
            mloProbeDone_P2 = true;
            try { RunMloProbe_P2(ui, spec); }
            catch (Exception ex) { Console.WriteLine("P2PROBE threw: " + ex); }
        }

        private void RunMloProbe_P2(MloCreatorPanel ui, string spec)
        {
            var s = ui.Session;
            int imported = s.Entities.Count(e => e.SourceInfo != null);
            int withGeom = s.Entities.Count(e => e.SourceInfo != null && e.SourceInfo.HasPlacedMeshes_O3);
            int withFile = s.Entities.Count(e => e.SourceFile != null);
            int tracked = s.Entities.Where(e => e.SourceInfo != null).Sum(e => e.SourceInfo.PlacedMeshes_O3.Count);
            Console.WriteLine($"P2PROBE session '{s.Name}': {s.Entities.Count} entities ({imported} imported, {withGeom} of them with tracked geometry, " +
                              $"{withFile} file-backed), {tracked} tracked meshes, {s.Rooms.Count} rooms, {s.Portals.Count} portals, " +
                              $"MloModel {(mloScene.MloModel?.Meshes.Count ?? 0)} meshes, {mloScene.Files.Count} files");
            Console.WriteLine($"P2PROBE   archetype box ({s.BBMin.X:0.00},{s.BBMin.Y:0.00},{s.BBMin.Z:0.00})..({s.BBMax.X:0.00},{s.BBMax.Y:0.00},{s.BBMax.Z:0.00}) manual={s.BBoxManual}; " +
                              $"validate: {string.Join(" | ", s.Validate().Take(3))}");
            for (int r = 0; r < s.Rooms.Count; r++)
            {
                var rm = s.Rooms[r];
                string raw = "";
                var src = s.SourceArchetype?.rooms;
                if (src != null && r < src.Length && src[r] != null)
                    raw = $" ytyp box ({src[r]._Data.bbMin.X:0.00},{src[r]._Data.bbMin.Y:0.00},{src[r]._Data.bbMin.Z:0.00})..({src[r]._Data.bbMax.X:0.00},{src[r]._Data.bbMax.Y:0.00},{src[r]._Data.bbMax.Z:0.00})";
                Console.WriteLine($"P2PROBE   room {r} '{rm.Name}': {s.CountInRoom(r)} props, valid={rm.IsValid}, " +
                                  $"box ({rm.Min.X:0.00},{rm.Min.Y:0.00},{rm.Min.Z:0.00})..({rm.Max.X:0.00},{rm.Max.Y:0.00},{rm.Max.Z:0.00}){raw}");
            }
            var bd = s.SourceArchetype?._BaseArchetypeDef;
            if (bd != null)
                Console.WriteLine($"P2PROBE   ytyp archetype box ({bd.Value.bbMin.X:0.00},{bd.Value.bbMin.Y:0.00},{bd.Value.bbMin.Z:0.00})..({bd.Value.bbMax.X:0.00},{bd.Value.bbMax.Y:0.00},{bd.Value.bbMax.Z:0.00}) radius {bd.Value.bsRadius:0.00}");
            {
                var far = s.Entities.Where(e => e.SourceInfo != null).OrderByDescending(e => e.Position.Length()).Take(4).ToList();
                Console.WriteLine("P2PROBE   entities furthest from the origin: " +
                                  string.Join(", ", far.Select(e => $"{e.Label}@({e.Position.X:0},{e.Position.Y:0},{e.Position.Z:0})")));
                var bigFiles = mloScene.Files.Where(f => f.Model != null).OrderByDescending(f => (f.Model.Bounds.Maximum - f.Model.Bounds.Minimum).Length()).Take(4).ToList();
                Console.WriteLine("P2PROBE   widest scene files: " +
                                  string.Join(", ", bigFiles.Select(f => $"{f.Name} {(f.Model.Bounds.Maximum - f.Model.Bounds.Minimum).Length():0}m at ({f.Model.Bounds.Center.X:0},{f.Model.Bounds.Center.Y:0},{f.Model.Bounds.Center.Z:0})")));
            }
            var noGeom = s.Entities.Where(e => e.SourceInfo != null && !e.SourceInfo.HasPlacedMeshes_O3).ToList();
            if (noGeom.Count > 0)
                Console.WriteLine($"P2PROBE   {noGeom.Count} imported entities have NO tracked geometry (unclickable, unmovable): " +
                                  string.Join(", ", noGeom.Take(8).Select(e => e.Label)) + (noGeom.Count > 8 ? ", ..." : ""));

            var picks = ProbePicks_P2(s, spec);
            Console.WriteLine($"P2PROBE probing {picks.Count} props: {string.Join(", ", picks)}");

            foreach (var ei0 in picks) ProbeOne_P2(ui, ei0);

            ProbeVetoes_P2(ui);
            ProbeAddedFile_P2(ui);

            string write = Environment.GetEnvironmentVariable("RLE_MLOPROBEWRITE");
            if (!string.IsNullOrEmpty(write)) ProbeWrite_P2(ui, write);

            if (Environment.GetEnvironmentVariable("RLE_MLOWIN") == "0") ui.WindowVisible = false;
        }

        private static List<int> ProbePicks_P2(MloCreatorSession s, string spec)
        {
            var picks = new List<int>();
            if (spec.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                string want = spec.Substring(5).Trim().ToLowerInvariant();
                for (int i = 0; i < s.Entities.Count && picks.Count < 4; i++)
                    if (s.Entities[i].Label.ToLowerInvariant().Contains(want)) picks.Add(i);
                return picks;
            }
            if (spec.Equals("last", StringComparison.OrdinalIgnoreCase))
            {
                if (s.Entities.Count > 0) picks.Add(s.Entities.Count - 1);
                return picks;
            }
            if (spec.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            {
                int n = 6;
                int c = spec.IndexOf(':');
                if (c >= 0) int.TryParse(spec.Substring(c + 1), out n);
                var live = new List<int>();
                for (int i = 0; i < s.Entities.Count; i++)
                    if (s.Entities[i].SourceInfo?.HasPlacedMeshes_O3 == true || s.Entities[i].SourceFile != null) live.Add(i);
                if (live.Count == 0) return picks;
                for (int k = 0; k < n; k++) picks.Add(live[(int)((long)k * live.Count / Math.Max(n, 1))]);
                return picks.Distinct().ToList();
            }
            foreach (var p in spec.Split(','))
                if (int.TryParse(p.Trim(), out int i) && i >= 0 && i < s.Entities.Count) picks.Add(i);
            return picks;
        }

        private bool AimAt_P2(MloCreatorPanel ui, int ei, out int px, out int py)
        {
            var s = ui.Session;
            var e = s.Entities[ei];
            px = deviceResources.Width / 2; py = deviceResources.Height / 2;
            var c = MeshCentre_O3(e);
            float rad = 2.0f;
            if (e.SourceInfo != null && e.SourceInfo.PlacedBounds_O3(out var b)) rad = Math.Max((b.Maximum - b.Minimum).Length(), 0.8f);
            else if (e.SourceFile?.Model != null) rad = Math.Max((e.SourceFile.Model.Bounds.Maximum - e.SourceFile.Model.Bounds.Minimum).Length(), 0.8f);
            camera.Target = c;
            camera.Distance = Math.Max(rad * 0.9f, 0.7f);
            float[] pitches = { 1.35f, 1.1f, 0.8f, 0.4f, 0.05f, -0.5f };
            for (int pi = 0; pi < pitches.Length; pi++)
                for (int yi = 0; yi < 8; yi++)
                {
                    camera.Yaw = yi * (float)(Math.PI / 4.0);
                    camera.Pitch = pitches[pi];
                    camera.SnapSmoothing(); camera.Update();
                    var ray = camera.GetPickRay(px, py, deviceResources.Width, deviceResources.Height);
                    if (ReferenceEquals(mloMeshMap_O3.Pick(s, mloScene, ray, out _), e)) return true;
                    if (e.SourceFile != null && ReferenceEquals(FindPropUnder(ray), e.SourceFile)) return true;
                }
            camera.Yaw = 0; camera.Pitch = 0.8f; camera.SnapSmoothing(); camera.Update();
            return false;
        }

        private bool Project_P2(Vector3 p, out int x, out int y)
        {
            var v = Vector4.Transform(new Vector4(p, 1.0f), camera.ViewProjMatrix);
            x = y = 0;
            if (v.W <= 1e-4f) return false;
            x = (int)((v.X / v.W * 0.5f + 0.5f) * deviceResources.Width);
            y = (int)((-v.Y / v.W * 0.5f + 0.5f) * deviceResources.Height);
            return x >= 0 && y >= 0 && x < deviceResources.Width && y < deviceResources.Height;
        }

        private void ProbeOne_P2(MloCreatorPanel ui, int ei)
        {
            var s = ui.Session;
            if (ei < 0 || ei >= s.Entities.Count) return;
            var e = s.Entities[ei];
            var info = e.SourceInfo;
            Console.WriteLine($"P2PROBE --- entity {ei} '{e.Label}' room={e.Room} (override {e.RoomOverride}) set='{e.EntitySet}' " +
                              $"imported={(info != null)} trackedMeshes={(info?.PlacedMeshes_O3.Count ?? 0)} file={(e.SourceFile?.Name ?? "-")} " +
                              $"pos=({e.Position.X:0.00},{e.Position.Y:0.00},{e.Position.Z:0.00}) include={e.Include}");

            ui.SelectedEntities.Clear(); ui.SelectedEntity = -1; ui.ShowPage(MloCreatorPanel.PageKind.Interior);
            bool aimed = AimAt_P2(ui, ei, out int px, out int py);
            var probeRay = camera.GetPickRay(px, py, deviceResources.Width, deviceResources.Height);
            var owner = mloMeshMap_O3.Pick(s, mloScene, probeRay, out float od);
            bool consumed = false, handled = false;
            MloCreatorMouseDown_H5(px, py, false, false, ref consumed);
            MloCreatorMouseUp_H5(px, py, true, ref handled);
            var target = MloWorkspaceGizmoTarget(ui);
            var live = CreatorLiveTargets();
            bool agree = ui.SelectedEntity == ei;
            Console.WriteLine($"P2PROBE   click({px},{py}) aimed={aimed} rayOwner={(owner?.Label ?? "none")}@{od:0.00}m gizmoGrabbed={consumed} handled={handled} " +
                              $"-> selected={ui.SelectedEntity} agree={agree} focusKind={ui.FocusKind} page={ui.Page} " +
                              $"target={(target == null ? "NONE" : target.GetType().Name)} liveTargets={live.Count} tool={MloCreatorPanel.EntityToolNames[Math.Clamp(ui.EntityTool, 0, 3)]} " +
                              $"multiSel={ui.SelectedEntities.Count} activeCount={ui.ActiveEntityCount}");
            if (!agree)
            {
                Console.WriteLine("P2PROBE   *** the click did not select this prop - the rest of the stages would measure the wrong entity");
                return;
            }

            ui.EntityTool = 1;
            var before = e.Position;
            var meshBefore = MeshCentre_O3(e);
            bool grabbed = false;
            int gx = 0, gy = 0, tx = 0, ty = 0;
            if (Project_P2(e.Position, out gx, out gy))
            {
                bool c2 = false;
                MloCreatorMouseDown_H5(gx, gy, false, false, ref c2);
                grabbed = c2 && (creatorGizmo?.Dragging ?? false);
                tx = gx + 90; ty = gy - 40;
                MloCreatorMouseMove_H5(tx, ty);
                bool h2 = false;
                MloCreatorMouseUp_H5(tx, ty, false, ref h2);
            }
            var meshAfter = MeshCentre_O3(e);
            var dEnt = e.Position - before;
            var dMesh = meshAfter - meshBefore;
            bool followed = (dMesh - dEnt).Length() < Math.Max(dEnt.Length() * 0.02f, 0.005f);
            Console.WriteLine($"P2PROBE   drag: grabbed={grabbed} at ({gx},{gy})->({tx},{ty}); entity moved {dEnt.Length():0.000} m " +
                              $"({before.X:0.00},{before.Y:0.00},{before.Z:0.00})->({e.Position.X:0.00},{e.Position.Y:0.00},{e.Position.Z:0.00}); " +
                              $"model moved {dMesh.Length():0.000} m; MODEL FOLLOWED={followed}; room now {e.Room}");

            var t3 = new CreatorEntityTarget(s, e);
            var rotBefore = MeshExtent_P2(e);
            s.PushUndo("probe rotate");
            t3.SetOrientation(Quaternion.RotationAxis(Vector3.UnitZ, 0.6f) * e.Rotation);
            MloTargetChanged_N3(t3);
            var rotAfter = MeshExtent_P2(e);
            s.PushUndo("probe scale");
            t3.SetScale(e.Scale * 1.5f);
            MloTargetChanged_N3(t3);
            var sclAfter = MeshExtent_P2(e);
            bool wantPlaced = info == null || Similar_N3(info.PlacedAt_O3, EntityMatrix_N3(e));
            Console.WriteLine($"P2PROBE   rotate+scale: model extent {rotBefore:0.000} -> {rotAfter:0.000} -> {sclAfter:0.000} m; " +
                              $"placedAt matches the entity matrix={wantPlaced}");
            t3.SetScale(e.Scale / 1.5f); t3.SetOrientation(Quaternion.RotationAxis(Vector3.UnitZ, -0.6f) * e.Rotation);
            MloTargetChanged_N3(t3);

            int wantRoom = -1;
            for (int r = 1; r < s.Rooms.Count; r++) if (r != e.Room) { wantRoom = r; break; }
            if (wantRoom >= 0)
            {
                ui.RequestAssignEntityRoom_O3 = wantRoom;
                ServiceMloEdit_O3(ui);
                Console.WriteLine($"P2PROBE   room: asked for {wantRoom} '{s.Rooms[wantRoom].Name}' -> room={e.Room} override={e.RoomOverride} " +
                                  $"ROOM CHANGED={(e.Room == wantRoom)}; status '{ui.Status}'");
            }

            int n0 = s.Entities.Count;
            ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
            DuplicateMloEntities_N3(ui);
            int nDup = s.Entities.Count;
            var copy = s.Entities.Count > 0 ? s.Entities[s.Entities.Count - 1] : null;
            bool copyMoves = false;
            if (copy != null)
            {
                var cBefore = MeshCentre_P2(copy);
                var ct = new CreatorEntityTarget(s, copy);
                ct.SetPosition(copy.Position + new Vector3(1.5f, 0, 0));
                MloTargetChanged_N3(ct);
                copyMoves = (MeshCentre_P2(copy) - cBefore).Length() > 1.0f;
            }
            ui.RequestUndo = true; ui.DoUndoRedo();
            SyncSceneToEntities_N3(ui); mloSyncedHistory_N3 = s.History.Version;
            int nUndo = s.Entities.Count;
            Console.WriteLine($"P2PROBE   duplicate: {n0} -> {nDup} entities, copy '{copy?.Label}' geometry moves on its own={copyMoves}; undo -> {nUndo} (back={nUndo == n0})");

            ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
            DeleteMloEntities_N3(ui);
            bool hidden = info == null || info.PlacedMeshes_O3.All(m => !m.Visible);
            int nDel = s.Entities.Count;
            ui.RequestUndo = true; ui.DoUndoRedo();
            SyncSceneToEntities_N3(ui); mloSyncedHistory_N3 = s.History.Version;
            bool backVisible = info == null || info.PlacedMeshes_O3.All(m => m.Visible);
            Console.WriteLine($"P2PROBE   delete: {nDel} entities, geometry hidden={hidden}; undo -> {s.Entities.Count} entities, geometry back={backVisible}");

            if (Environment.GetEnvironmentVariable("RLE_MLOPROBEKEEP") == "1")
            {
                int again = s.Entities.FindIndex(x => ReferenceEquals(x.SourceInfo, info) && info != null);
                if (again >= 0) { ui.SelectedEntities.Clear(); ui.SelectEntity(again); ui.RevealSelection = true; }
            }
        }

        private void ProbeVetoes_P2(MloCreatorPanel ui)
        {
            var s = ui.Session;
            int ei = s.Entities.FindIndex(e => e.SourceInfo?.HasPlacedMeshes_O3 == true);
            if (ei < 0) return;
            ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
            bool a0 = ui.AssetsSectionOpen, l0 = ui.LightsSectionOpen;
            var page0 = ui.Page;
            int Targets() => CreatorGizmoTargets().Count;
            ui.AssetsSectionOpen = false; ui.LightsSectionOpen = false; ui.ShowPage(MloCreatorPanel.PageKind.Entity);
            int baseN = Targets();
            ui.AssetsSectionOpen = true;
            int withAssets = Targets();
            ui.AssetsSectionOpen = false; ui.LightsSectionOpen = true;
            int withLights = Targets();
            ui.LightsSectionOpen = false; ui.ShowPage(MloCreatorPanel.PageKind.Assets);
            int onAssetsPage = Targets();
            ui.ShowPage(MloCreatorPanel.PageKind.Lights);
            int onLightsPage = Targets();
            ui.AssetsSectionOpen = a0; ui.LightsSectionOpen = l0; ui.ShowPage(page0);
            Console.WriteLine($"P2PROBE vetoes with a prop selected: plain={baseN}, Assets section open={withAssets}, " +
                              $"Lights section open={withLights}, Assets PAGE={onAssetsPage}, Lights PAGE={onLightsPage} (0 = no gizmo at all)");
        }

        private void ProbeAddedFile_P2(MloCreatorPanel ui)
        {
            var s = ui.Session;
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_p2probe");
            System.IO.Directory.CreateDirectory(dir);
            string ydr = System.IO.Path.Combine(dir, "prop_p2_added.ydr");
            try
            {
                if (!System.IO.File.Exists(ydr))
                {
                    string src = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_mlocreator", "light_test_scene.ydr");
                    if (!System.IO.File.Exists(src)) TestSceneGenerator.Run(src);
                    System.IO.File.Copy(src, ydr, true);
                }
            }
            catch (Exception ex) { Console.WriteLine("P2PROBE add-props: could not make a test prop: " + ex.Message); return; }

            int e0 = s.Entities.Count, f0 = mloScene.Files.Count;
            scene.LoadModelFile(ydr, additive: true);
            ServiceMloCreatorRequests(ui);
            var lf = mloScene.Files.LastOrDefault(f => f.Path == ydr);
            int ei = s.Entities.FindIndex(e => e.SourceFile == lf);
            Console.WriteLine($"P2PROBE add-props: loaded '{System.IO.Path.GetFileName(ydr)}' -> mloScene {f0} -> {mloScene.Files.Count} files, " +
                              $"session {e0} -> {s.Entities.Count} entities; the added prop is entity {ei} " +
                              $"({(ei < 0 ? "*** NOT IN THE INTERIOR - no tree entry, no gizmo, not written to the ytyp" : "ok")})");
            if (ei >= 0)
            {
                ui.SelectedEntities.Clear(); ui.SelectEntity(ei);
                bool aimed = AimAt_P2(ui, ei, out int px, out int py);
                bool consumed = false, handled = false;
                ui.SelectedEntities.Clear(); ui.SelectedEntity = -1;
                MloCreatorMouseDown_H5(px, py, false, false, ref consumed);
                MloCreatorMouseUp_H5(px, py, true, ref handled);
                Console.WriteLine($"P2PROBE add-props: click aimed={aimed} -> selected={ui.SelectedEntity} agree={(ui.SelectedEntity == ei)} " +
                                  $"target={(MloWorkspaceGizmoTarget(ui) == null ? "NONE" : "yes")}");
            }
            if (lf != null) { if (ei >= 0) s.Entities.RemoveAt(ei); mloScene.RemoveFile(lf); }
        }

        private static float MeshExtent_P2(MloCreatorEntity e)
        {
            if (e?.SourceInfo != null && e.SourceInfo.PlacedBounds_O3(out var b)) return (b.Maximum - b.Minimum).Length();
            var m = e?.SourceFile?.Model;
            return m != null ? (m.Bounds.Maximum - m.Bounds.Minimum).Length() : 0.0f;
        }

        private static Vector3 MeshCentre_P2(MloCreatorEntity e) => MeshCentre_O3(e);

        private void ProbeWrite_P2(MloCreatorPanel ui, string path)
        {
            var s = ui.Session;
            try
            {
                var oldName = s.Name;
                if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "rle_p2_probe";
                s.SaveYtyp(path);
                var rt = new YtypFile();
                rt.Load(System.IO.File.ReadAllBytes(path));
                var mlo = rt.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
                int ents = mlo?.entities?.Length ?? 0, rooms = mlo?.rooms?.Length ?? 0;
                Console.WriteLine($"P2PROBE write {path}: {rooms} rooms, {ents} entities, validate '{MloEditor.Validate(rt)}'");
                int checkedN = 0, posOk = 0, roomOk = 0;
                for (int i = 0; i < s.Entities.Count && checkedN < 40; i++)
                {
                    var e = s.Entities[i];
                    if (!e.Include || !string.IsNullOrEmpty(e.EntitySet)) continue;
                    checkedN++;
                    int wi = -1;
                    uint h = JenkHash.GenHash(e.ArchetypeName.ToLowerInvariant());
                    for (int k = 0; k < ents; k++)
                        if (mlo.entities[k]._Data.archetypeName.Hash == h && (mlo.entities[k]._Data.position - e.Position).Length() < 1e-3f) { wi = k; break; }
                    if (wi < 0) continue;
                    posOk++;
                    int wroom = -1;
                    for (int r = 0; r < rooms; r++)
                        if (mlo.rooms[r].AttachedObjects?.Contains((uint)wi) == true) { wroom = r; break; }
                    if (wroom == e.Room) roomOk++;
                    else Console.WriteLine($"P2PROBE   entity {i} '{e.Label}' is in room {e.Room} in the editor but room {wroom} in the written ytyp");
                }
                Console.WriteLine($"P2PROBE round trip: {posOk}/{checkedN} props found again at their position, {roomOk}/{checkedN} in the same room");
                s.Name = oldName;
            }
            catch (Exception ex) { Console.WriteLine("P2PROBE write failed: " + ex.Message); }
        }
    }
}

