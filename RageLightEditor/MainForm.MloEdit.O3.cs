using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly MloEntityMeshMap_O3 mloMeshMap_O3 = new MloEntityMeshMap_O3();
        private readonly HashSet<MloEntityInfo> mloImportedSeen_O3 = new HashSet<MloEntityInfo>();
        private MloCreatorSession mloSeenSession_O3;
        private int mloSeenCount_O3 = -1, mloSeenVersion_O3 = -1;
        private bool mloClickDemoDone_O3;
        public string PlaceRoomWhy_O3 = "";

        private void SyncImportedEntityModel_O3(MloCreatorEntity e)
        {
            var info = e?.SourceInfo;
            if (info == null || !info.HasPlacedMeshes_O3) return;
            if (!info.MovePlacedMeshes_O3(EntityMatrix_N3(e))) return;
            if (mloScene?.MloModel != null && info.PlacedBounds_O3(out var b))
            {
                var mb = mloScene.MloModel.Bounds;
                mloScene.MloModel.Bounds = new BoundingBox(Vector3.Min(mb.Minimum, b.Minimum), Vector3.Max(mb.Maximum, b.Maximum));
            }
            if (mloScene != null) mloScene.Dirty = true;
        }

        private void SyncImportedEntities_O3(MloCreatorPanel ui)
        {
            var s = ui?.Session;
            if (s == null) return;
            if (!ReferenceEquals(s, mloSeenSession_O3)) { mloSeenSession_O3 = s; mloImportedSeen_O3.Clear(); }
            var live = new HashSet<MloEntityInfo>();
            foreach (var e in s.Entities)
            {
                var info = e.SourceInfo;
                if (info == null) continue;
                live.Add(info);
                mloImportedSeen_O3.Add(info);
                SyncImportedEntityModel_O3(e);
            }
            foreach (var info in mloImportedSeen_O3)
            {
                bool want = live.Contains(info);
                info.SetPlacedVisible_O3(want);
            }
            mloMeshMap_O3.Rebuild(s);
            if (mloScene != null) mloScene.Dirty = true;
        }

        private bool MloPickEntity_O3(MloCreatorPanel ui, int x, int y)
        {
            var s = ui?.Session;
            if (s == null || mloScene == null) return false;
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            var file = FindPropUnder(ray, out var fileMesh);
            float fileDist = float.MaxValue;
            if (file != null && file != s.ShellFile && fileMesh != null && fileMesh.RayHit(ref ray, out float ft)) fileDist = ft;
            var hit = mloMeshMap_O3.Pick(s, mloScene, ray, out float dist);
            if (hit == null || dist > fileDist) return false;
            int ei = s.Entities.IndexOf(hit);
            if (ei < 0) return false;
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            ui.SelectEntityMulti(ei, ctrl);
            ui.RevealSelection = true;
            if (ui.EntityTool == 0) ui.EntityTool = 1;
            int n = ui.ActiveEntityCount;
            ui.SetStatus(n > 1
                ? $"{n} props selected. W move, E rotate, T scale, Del deletes, Ctrl+D duplicates."
                : $"'{hit.Label}' in room {hit.Room} ({(hit.Room < s.Rooms.Count ? s.Rooms[hit.Room].Name : "?")}). Drag the gizmo to move it; the Room combo on the page puts it in another room.");
            return true;
        }

        private int PlaceRoomWithFallback_O3(MloCreatorPanel ui, Vector3 at, out string why)
        {
            why = "";
            var s = ui?.Session;
            if (s == null) return -1;
            if (ui.PlaceRoomMode == 2) { why = "containment: the box the prop lands in"; return -1; }
            if (ui.PlaceRoomMode == 0)
            {
                int r = s.RoomAt(camera.Position);
                if (r > 0) { why = $"the room the camera is in ({s.Rooms[r].Name})"; return r; }
            }
            if (ui.SelectedRoom >= 0 && ui.SelectedRoom < s.Rooms.Count)
            {
                why = ui.PlaceRoomMode == 0
                    ? $"the camera is outside every room, so the room selected in the tree ({s.Rooms[ui.SelectedRoom].Name})"
                    : $"the room selected in the tree ({s.Rooms[ui.SelectedRoom].Name})";
                return ui.SelectedRoom;
            }
            int near = NearestRoom_O3(s, at);
            if (near > 0)
            {
                why = $"nothing is selected and the camera is outside every room, so the nearest one ({s.Rooms[near].Name})";
                return near;
            }
            why = "no room reaches here - it goes to limbo";
            return -1;
        }

        private static int NearestRoom_O3(MloCreatorSession s, Vector3 p)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 1; i < s.Rooms.Count; i++)
            {
                var r = s.Rooms[i];
                if (!r.IsValid) continue;
                var c = Vector3.Clamp(p, r.Min, r.Max);
                float d = (c - p).LengthSquared();
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void AssignRoom_O3(MloCreatorPanel ui, int room, string why)
        {
            var s = ui.Session;
            var idx = ui.ActiveEntities().Where(i => i >= 0 && i < s.Entities.Count).Distinct().ToList();
            if (idx.Count == 0) { ui.SetStatus("Select a prop first - click it in the viewport, or in the tree.", true); return; }
            s.PushUndo(room < 0 ? "Unpin prop room" : "Put prop in a room");
            foreach (var i in idx) s.Entities[i].RoomOverride = room;
            s.AutoAssignRooms();
            mloSyncedHistory_N3 = s.History.Version;
            string name = room >= 0 && room < s.Rooms.Count ? s.Rooms[room].Name : "?";
            ui.SetStatus(room < 0
                ? $"{idx.Count} prop(s) back to containment - the room their origin is inside."
                : $"{idx.Count} prop(s) put in room {room} ({name}){(string.IsNullOrEmpty(why) ? "" : " - " + why)}. It is written into that room's attachedObjects.");
        }

        private void ServiceMloEdit_O3(MloCreatorPanel ui)
        {
            var s = ui?.Session;
            if (s == null) return;
            if (!ReferenceEquals(s, mloSeenSession_O3))
            {
                mloSeenSession_O3 = s;
                mloImportedSeen_O3.Clear();
                mloSeenCount_O3 = -1;
            }
            if (mloSeenCount_O3 != s.Entities.Count || mloSeenVersion_O3 != s.History.Version)
            {
                mloSeenCount_O3 = s.Entities.Count;
                mloSeenVersion_O3 = s.History.Version;
                foreach (var e in s.Entities) if (e.SourceInfo != null) mloImportedSeen_O3.Add(e.SourceInfo);
                mloMeshMap_O3.Rebuild(s);
            }

            var at = ui.HasSnapPoint ? ui.SnapPoint : camera.Target;
            int room = PlaceRoomWithFallback_O3(ui, at, out var why);
            PlaceRoomWhy_O3 = why;
            ui.PlaceRoomIndex = room;
            ui.PlaceRoomLabel = room >= 0 && room < s.Rooms.Count ? s.Rooms[room].Name : "";
            ui.PlaceRoomWhy_O3 = why;

            if (ui.RequestAssignEntityRoom_O3 > -2)
            {
                int r = ui.RequestAssignEntityRoom_O3;
                ui.RequestAssignEntityRoom_O3 = -2;
                AssignRoom_O3(ui, r, r == room ? why : "");
            }
            if (ui.RequestAssignToCurrentRoom_O3)
            {
                ui.RequestAssignToCurrentRoom_O3 = false;
                if (room < 0) ui.SetStatus("No room to put it in - the camera is outside every room and nothing is selected in the tree.", true);
                else AssignRoom_O3(ui, room, why);
            }
            if (ui.DropEntityOnRoom_O3 >= 0 && ui.DropEntityIndex_O3 >= 0)
            {
                int r = ui.DropEntityOnRoom_O3, ei = ui.DropEntityIndex_O3;
                ui.DropEntityOnRoom_O3 = -1; ui.DropEntityIndex_O3 = -1;
                if (ei < s.Entities.Count && r < s.Rooms.Count)
                {
                    if (!ui.SelectedEntities.Contains(ei)) { ui.SelectedEntities.Clear(); ui.SelectEntity(ei); }
                    AssignRoom_O3(ui, r, "dragged onto the room in the tree");
                }
            }
            ApplyAssetViewEnv_O3(ui);
            ServiceMloClickDemo_O3(ui);
            ServiceMloProbe_P2(ui);
            ServiceMloClick_S1(ui);
            ServiceMloLayout_S1(ui);
            ServiceMloRot_S1(ui);
        }

        private void ServiceMloClickDemo_O3(MloCreatorPanel ui)
        {
            if (mloClickDemoDone_O3) return;
            var s = ui.Session;
            string click = Environment.GetEnvironmentVariable("RLE_MLOCLICK");
            string drag = Environment.GetEnvironmentVariable("RLE_MLODRAG");
            string room = Environment.GetEnvironmentVariable("RLE_MLOROOM");
            if (string.IsNullOrEmpty(click) && string.IsNullOrEmpty(drag) && string.IsNullOrEmpty(room)) return;
            if (!mloScene.HasModel || (DebugMlo != null && !debugMloDone)) return;
            mloClickDemoDone_O3 = true;

            if (!string.IsNullOrEmpty(click))
            {
                int x = deviceResources.Width / 2, y = deviceResources.Height / 2;
                if (click.StartsWith("e", StringComparison.OrdinalIgnoreCase) && int.TryParse(click.Substring(click.IndexOf(':') + 1), out int aim))
                {
                    if (aim >= 0 && aim < s.Entities.Count)
                    {
                        var ae = s.Entities[aim];
                        var c = MeshCentre_O3(ae);
                        float rad = 2.5f;
                        if (ae.SourceInfo != null && ae.SourceInfo.PlacedBounds_O3(out var ab)) rad = Math.Max((ab.Maximum - ab.Minimum).Length(), 1.0f);
                        camera.Target = c;
                        camera.Distance = Math.Max(rad * 0.8f, 0.6f);
                        float[] pitches = { 1.35f, 1.1f, 0.8f, 0.4f, 0.05f, -0.5f };
                        bool clear = false; float gotYaw = camera.Yaw, gotPitch = camera.Pitch;
                        for (int pi = 0; pi < pitches.Length && !clear; pi++)
                            for (int yi = 0; yi < 8 && !clear; yi++)
                            {
                                camera.Yaw = yi * (float)(Math.PI / 4.0);
                                camera.Pitch = pitches[pi];
                                camera.SnapSmoothing(); camera.Update();
                                var pr = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
                                if (!ReferenceEquals(mloMeshMap_O3.Pick(s, mloScene, pr, out _), ae)) continue;
                                clear = true; gotYaw = camera.Yaw; gotPitch = camera.Pitch;
                            }
                        camera.Yaw = gotYaw; camera.Pitch = gotPitch;
                        camera.SnapSmoothing();
                        camera.Update();
                        Console.WriteLine($"MLOCLICK aimed at entity {aim} '{ae.Label}' centre ({c.X:0.00},{c.Y:0.00},{c.Z:0.00}) radius {rad:0.00} " +
                                          $"(imported={(ae.SourceInfo != null)}, {ae.SourceInfo?.PlacedMeshes_O3.Count ?? 0} tracked meshes); " +
                                          $"clear view found={clear} at yaw {gotYaw:0.00} pitch {gotPitch:0.00}");
                    }
                }
                else
                {
                    var p = click.Split(',');
                    if (p.Length >= 2 && int.TryParse(p[0], out var cx) && int.TryParse(p[1], out var cy)) { x = cx; y = cy; }
                }
                var probe = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
                bool anyGeom = MloCreatorSession.RayHitScene(mloScene, probe, out var gp, out _);
                var owner = mloMeshMap_O3.Pick(s, mloScene, probe, out float od);
                Console.WriteLine($"MLOCLICK probe: geometry under the cursor={anyGeom} at ({gp.X:0.00},{gp.Y:0.00},{gp.Z:0.00}); " +
                                  $"tracked meshes={mloMeshMap_O3.Count}; owner={(owner == null ? "none" : owner.Label)} at {od:0.00} m");
                bool hit = MloWorkspaceClick(ui, x, y);
                var e = ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count ? s.Entities[ui.SelectedEntity] : null;
                var target = MloWorkspaceGizmoTarget(ui);
                Console.WriteLine($"MLOCLICK at {x},{y}: handled={hit} entity={ui.SelectedEntity} '{e?.Label}' room={e?.Room} " +
                                  $"imported={(e?.SourceInfo != null)} meshes={(e?.SourceInfo?.PlacedMeshes_O3.Count ?? (e?.SourceFile?.Model?.Meshes.Count ?? 0))} " +
                                  $"gizmoTarget={(target == null ? "NONE" : target.GetType().Name)} tool={MloCreatorPanel.EntityToolNames[Math.Clamp(ui.EntityTool, 0, 3)]}");
            }
            if (!string.IsNullOrEmpty(drag) && ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count)
            {
                var p = drag.Split(',');
                if (p.Length >= 3 &&
                    float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dx) &&
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dy) &&
                    float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dz))
                {
                    var e = s.Entities[ui.SelectedEntity];
                    var t = MloWorkspaceGizmoTarget(ui);
                    var meshBefore = MeshCentre_O3(e);
                    var before = e.Position;
                    s.PushUndo("Move prop");
                    MloDragBegan_N3();
                    if (t != null) t.SetPosition(before + new Vector3(dx, dy, dz));
                    else { e.Position = before + new Vector3(dx, dy, dz); s.AutoAssignRooms(); }
                    MloTargetChanged_N3(t ?? new CreatorEntityTarget(s, e));
                    var meshAfter = MeshCentre_O3(e);
                    Console.WriteLine($"MLODRAG entity {ui.SelectedEntity} '{e.Label}' ({before.X:0.00},{before.Y:0.00},{before.Z:0.00}) -> " +
                                      $"({e.Position.X:0.00},{e.Position.Y:0.00},{e.Position.Z:0.00}); model centre " +
                                      $"({meshBefore.X:0.00},{meshBefore.Y:0.00},{meshBefore.Z:0.00}) -> ({meshAfter.X:0.00},{meshAfter.Y:0.00},{meshAfter.Z:0.00}); " +
                                      $"model followed={( (meshAfter - meshBefore) - new Vector3(dx, dy, dz) ).Length() < 0.01f}; room={e.Room}");
                }
            }
            if (!string.IsNullOrEmpty(room))
            {
                int want;
                if (room.Equals("current", StringComparison.OrdinalIgnoreCase)) want = ui.PlaceRoomIndex;
                else if (!int.TryParse(room, out want)) want = -1;
                AssignRoom_O3(ui, want, PlaceRoomWhy_O3);
                var e = ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count ? s.Entities[ui.SelectedEntity] : null;
                Console.WriteLine($"MLOROOM asked for {room} -> {want}: entity {ui.SelectedEntity} '{e?.Label}' room={e?.Room} override={e?.RoomOverride} " +
                                  $"({(want >= 0 && want < s.Rooms.Count ? s.Rooms[want].Name : "?")}); why: {PlaceRoomWhy_O3}");
            }
            string write = Environment.GetEnvironmentVariable("RLE_MLOWRITEO3");
            if (!string.IsNullOrEmpty(write))
            {
                try
                {
                    var oldName = s.Name;
                    if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "rle_o3_test";
                    s.SaveYtyp(write);
                    var rt = new YtypFile();
                    rt.Load(System.IO.File.ReadAllBytes(write));
                    var mlo = rt.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
                    var e = ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count ? s.Entities[ui.SelectedEntity] : null;
                    int wi = -1, wroom = -1;
                    if (mlo?.entities != null && e != null)
                        for (int i = 0; i < mlo.entities.Length; i++)
                            if ((mlo.entities[i]._Data.position - e.Position).Length() < 1e-3f &&
                                mlo.entities[i]._Data.archetypeName.Hash == JenkHash.GenHash(e.ArchetypeName.ToLowerInvariant())) { wi = i; break; }
                    for (int r = 0; wi >= 0 && r < (mlo.rooms?.Length ?? 0); r++)
                        if (mlo.rooms[r].AttachedObjects?.Contains((uint)wi) == true) { wroom = r; break; }
                    Console.WriteLine($"MLOWRITEO3 {write}: {mlo?.rooms?.Length ?? 0} rooms, {mlo?.entities?.Length ?? 0} entities, validate '{MloEditor.Validate(rt)}'; " +
                                      $"the edited prop '{e?.Label}' is entity {wi} at ({e?.Position.X ?? 0:0.00},{e?.Position.Y ?? 0:0.00},{e?.Position.Z ?? 0:0.00}) in room {wroom}");
                    s.Name = oldName;
                }
                catch (Exception ex) { Console.WriteLine("MLOWRITEO3 failed: " + ex.Message); }
            }

            ui.WindowVisible = Environment.GetEnvironmentVariable("RLE_MLOWIN") != "0";
            if (Environment.GetEnvironmentVariable("RLE_MLOFRAME") == "1" && ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count)
            {
                var e = s.Entities[ui.SelectedEntity];
                camera.Target = MeshCentre_O3(e);
                float r = 3.5f;
                if (e.SourceInfo != null && e.SourceInfo.PlacedBounds_O3(out var eb))
                    r = Math.Max((eb.Maximum - eb.Minimum).Length() * 1.2f, 1.5f);
                camera.Distance = r;
                camera.SnapSmoothing();
                camera.Update();
            }
        }

        private static Vector3 MeshCentre_O3(MloCreatorEntity e)
        {
            if (e == null) return Vector3.Zero;
            if (e.SourceInfo != null && e.SourceInfo.PlacedBounds_O3(out var b)) return b.Center;
            var m = e.SourceFile?.Model;
            return m != null ? m.Bounds.Center : e.Position;
        }

        private void MloEntityGeomTest_O3(Action<string, bool, string> check)
        {
            try
            {
                var ui = Creator;
                var s = ui?.Session;
                if (s == null) { check("mloO3: a session exists", false, "none"); return; }

                var src = mloScene?.Files.FirstOrDefault(f => f.Model != null && f.Model.Meshes.Count > 0)?.Model;
                if (src == null) { check("mloO3: a model to instance", false, "no model in the MLO scene"); return; }
                var host = new RenderModel { Name = "o3host" };
                var world = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, Vector3.One, Vector3.Zero, Quaternion.Identity, new Vector3(600, 600, 600));
                foreach (var m in src.Meshes) host.Meshes.Add(m.CreateInstance(world));
                var info = new MloEntityInfo { ArchetypeName = "o3_imported", Position = new Vector3(600, 600, 600), Rotation = Quaternion.Identity, Scale = Vector3.One };
                info.TrackPlacedMeshes_O3(host, src.Meshes.Count, world);
                check("mloO3: the importer's note says which meshes are the entity's",
                      info.PlacedMeshes_O3.Count == src.Meshes.Count && info.HasPlacedMeshes_O3,
                      $"{info.PlacedMeshes_O3.Count} of {src.Meshes.Count}");

                var ent = new MloCreatorEntity
                {
                    ArchetypeName = "o3_imported", Position = info.Position, Rotation = Quaternion.Identity,
                    Scale = Vector3.One, SourceInfo = info, Include = true,
                };
                s.Entities.Add(ent);
                int ei = s.Entities.Count - 1;
                s.AutoAssignRooms();

                info.PlacedBounds_O3(out var b0);
                var target = new CreatorEntityTarget(s, ent);
                target.SetPosition(ent.Position + new Vector3(3.0f, -2.0f, 1.0f));
                MloTargetChanged_N3(target);
                info.PlacedBounds_O3(out var b1);
                check("mloO3: an IMPORTED entity's model follows its gizmo",
                      (b1.Center - (b0.Center + new Vector3(3.0f, -2.0f, 1.0f))).Length() < 1e-3f,
                      $"{b0.Center} -> {b1.Center}");

                var q = Quaternion.RotationAxis(Vector3.UnitZ, 0.6f);
                target.SetOrientation(q); MloTargetChanged_N3(target);
                target.SetScale(new Vector3(2, 2, 2)); MloTargetChanged_N3(target);
                var want = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, ent.Scale, Vector3.Zero, ent.Rotation, ent.Position);
                check("mloO3: rotate and scale reach it as well", Similar_N3(info.PlacedAt_O3, want),
                      $"scale {ent.Scale}, placed {(Similar_N3(info.PlacedAt_O3, want) ? "matches" : "differs")}");
                target.SetScale(Vector3.One); target.SetOrientation(Quaternion.Identity); MloTargetChanged_N3(target);

                mloMeshMap_O3.Rebuild(s);
                info.PlacedBounds_O3(out var bb);
                var from = bb.Center + new Vector3(0, 0, Math.Max((bb.Maximum - bb.Minimum).Length(), 1.0f) + 5.0f);
                var ray = new Ray(from, -Vector3.UnitZ);
                var found = mloMeshMap_O3.Pick(s, mloScene, ray, out float d);
                check("mloO3: a ray at its geometry picks that entity", ReferenceEquals(found, ent), found == null ? "nothing hit" : found.Label);
                var miss = mloMeshMap_O3.Pick(s, mloScene, new Ray(new Vector3(5000, 5000, 5000), Vector3.UnitZ), out _);
                check("mloO3: a ray at empty space picks nothing", miss == null, miss?.Label ?? "null");

                var room = s.AddRoom("o3_room", new Vector3(590, 590, 590), new Vector3(615, 615, 615));
                int roomIdx = s.Rooms.Count - 1;
                s.AutoAssignRooms();
                var camWas = camera.Position;
                ui.PlaceRoomMode = 0;
                ui.SelectRoom(roomIdx);
                camera.Target = new Vector3(-9000, -9000, 900); camera.SnapSmoothing(); camera.Update();
                int r1 = PlaceRoomWithFallback_O3(ui, ent.Position, out var why1);
                check("mloO3: camera outside every room falls back to the tree's room", r1 == roomIdx && why1.Contains("tree"), $"room {r1}: {why1}");
                ui.SelectedRoom = -1;
                int r2 = PlaceRoomWithFallback_O3(ui, ent.Position, out var why2);
                check("mloO3: with nothing selected either, the NEAREST room answers", r2 == roomIdx && why2.Contains("nearest"), $"room {r2}: {why2}");
                ui.SelectRoom(roomIdx);

                ui.SelectedEntities.Clear();
                ui.SelectEntity(ei);
                AssignRoom_O3(ui, roomIdx, "test");
                check("mloO3: assign to a room pins the entity", s.Entities[ei].RoomOverride == roomIdx && s.Entities[ei].Room == roomIdx,
                      $"room {s.Entities[ei].Room} override {s.Entities[ei].RoomOverride}");
                s.History.Undo();
                check("mloO3: and one undo takes the pin back", s.Entities.Count <= ei || s.Entities[ei].RoomOverride != roomIdx,
                      $"override {(s.Entities.Count > ei ? s.Entities[ei].RoomOverride : -99)}");
                ei = s.Entities.FindIndex(x => x.ArchetypeName == "o3_imported");
                if (ei >= 0)
                {
                    ui.SelectedEntities.Clear();
                    ui.SelectEntity(ei);
                    AssignRoom_O3(ui, roomIdx, "test");
                    var oldName = s.Name; s.Name = "rle_o3_edit"; s.TextureDictionary = "rle_o3_edit";
                    var ytyp = s.BuildYtyp("rle_o3_edit.ytyp");
                    var arch = ytyp.AllArchetypes?.OfType<CodeWalker.GameFiles.MloArchetype>().FirstOrDefault();
                    int wi = -1;
                    if (arch?.entities != null)
                        for (int i = 0; i < arch.entities.Length; i++)
                            if (arch.entities[i]._Data.archetypeName.Hash == JenkHash.GenHash("o3_imported")) { wi = i; break; }
                    bool inRoom = wi >= 0 && roomIdx < (arch.rooms?.Length ?? 0) && (arch.rooms[roomIdx].AttachedObjects?.Contains((uint)wi) ?? false);
                    check("mloO3: the assigned room is written into the ytyp", inRoom,
                          $"entity {wi}, room {roomIdx} holds {(wi >= 0 && roomIdx < (arch?.rooms?.Length ?? 0) ? string.Join(",", arch.rooms[roomIdx].AttachedObjects ?? Array.Empty<uint>()) : "-")}");
                    s.Name = oldName;
                }

                if (ei >= 0)
                {
                    ui.SelectedEntities.Clear();
                    ui.SelectEntity(ei);
                    DeleteMloEntities_N3(ui);
                    SyncImportedEntities_O3(ui);
                    bool hidden = info.PlacedMeshes_O3.All(m => !m.Visible);
                    check("mloO3: deleting an imported prop takes its geometry off the screen", hidden, $"visible {info.PlacedMeshes_O3.Count(m => m.Visible)} of {info.PlacedMeshes_O3.Count}");
                    ui.RequestUndo = true; ui.DoUndoRedo();
                    SyncSceneToEntities_N3(ui);
                    SyncImportedEntities_O3(ui);
                    mloSyncedHistory_N3 = s.History.Version;
                    check("mloO3: and Ctrl+Z brings it back", info.PlacedMeshes_O3.All(m => m.Visible) && s.Entities.Any(x => x.SourceInfo == info),
                          $"visible {info.PlacedMeshes_O3.Count(m => m.Visible)} of {info.PlacedMeshes_O3.Count}");
                }

                for (int i = s.Entities.Count - 1; i >= 0; i--) if (s.Entities[i].SourceInfo == info) s.Entities.RemoveAt(i);
                if (s.Rooms.Count - 1 == roomIdx && s.Rooms[roomIdx] == room) s.Rooms.RemoveAt(roomIdx);
                s.AutoAssignRooms();
                ui.SelectedEntities.Clear();
                ui.SelectedEntity = -1;
                ui.SelectRoom(0);
                camera.Target = camWas; camera.SnapSmoothing(); camera.Update();
                mloMeshMap_O3.Rebuild(s);
                host.Meshes.Clear();
            }
            catch (Exception ex)
            {
                check("mloO3: no exception", false, ex.ToString());
            }
        }
    }
}

