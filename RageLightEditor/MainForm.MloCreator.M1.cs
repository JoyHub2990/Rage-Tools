using System;
using System.Windows.Forms;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool m1ShellLogged;

        private void ServiceMloShell_M1(MloCreatorPanel ui)
        {
            var s = ui?.Session;
            if (s == null) return;
            if (!panel.ShowRightPanel || !panel.ShowInterface || panel.PhotoMode) { ui.LightsSectionOpen = false; ui.AssetsSectionOpen = false; }
            bool known = s.SyncToShell(scene);
            if (known && !m1ShellLogged && scene.HasModel)
            {
                m1ShellLogged = true;
                Console.WriteLine($"SHELLBOUNDS {s.ShellBoundsSource}: {s.ShellBounds.Minimum} .. {s.ShellBounds.Maximum} -> limbo, bbox manual={s.BBoxManual}");
            }
            if (ui.RequestRoomFromShell)
            {
                ui.RequestRoomFromShell = false;
                var room = ui.CurrentRoom;
                if (room == null || ui.SelectedRoom == 0) ui.SetStatus("Select a room other than limbo (limbo is always the whole shell).", true);
                else if (!s.RoomBoundsFromShell(room, scene, out var b, out var how)) ui.SetStatus("No shell to capture from: pick the shell on the Interior page, or open / import one.", true);
                else
                {
                    s.PushUndo("Room from shell");
                    room.Min = b.Minimum; room.Max = b.Maximum;
                    MloCreatorSession.NormaliseRoom(room);
                    s.AutoAssignRooms();
                    ui.SetStatus($"Room {ui.SelectedRoom} '{room.Name}' = {how}: {room.Size.X:0.00} x {room.Size.Y:0.00} x {room.Size.Z:0.00} m.");
                    Console.WriteLine($"ROOMSHELL room {ui.SelectedRoom} {how}: {room.Min} .. {room.Max}");
                }
            }
            if (ui.PickingRoomMesh && (ui.PickingRoomMeshRoom != ui.SelectedRoom || ui.CurrentRoom == null || ui.SelectedRoom == 0)) ui.CancelRoomMeshPick();
        }

        private bool MloRoomMeshClick_M1(MloCreatorPanel ui, int x, int y)
        {
            if (ui == null || !ui.PickingRoomMesh) return false;
            var s = ui.Session; var room = ui.CurrentRoom;
            if (s == null || room == null) { ui.CancelRoomMeshPick(); return false; }
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            RenderMesh best = null; float bestT = float.MaxValue;
            foreach (var mesh in scene.AllMeshes)
            {
                if (mesh == null || !mesh.Visible || mesh.NeverDraw) continue;
                var b = mesh.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                if (!ray.Intersects(ref b, out float bt) || bt > bestT) continue;
                if (mesh.RayHit(ref ray, out float t) && t < bestT) { bestT = t; best = mesh; }
            }
            if (best == null || !MloCreatorSession.BoundsOfMesh(best, out var bounds))
            {
                ui.SetStatus("Nothing under the cursor - click ON a wall, a floor or a prop (Esc cancels).", true);
                return true;
            }
            s.PushUndo("Room from mesh");
            room.Min = bounds.Minimum; room.Max = bounds.Maximum;
            MloCreatorSession.NormaliseRoom(room);
            s.AutoAssignRooms();
            ui.CancelRoomMeshPick();
            ui.SetStatus($"Room {ui.SelectedRoom} '{room.Name}' = the mesh under the click ({best.ShaderName}): {room.Size.X:0.00} x {room.Size.Y:0.00} x {room.Size.Z:0.00} m.");
            Console.WriteLine($"ROOMMESH room {ui.SelectedRoom} mesh {best.ShaderName} verts {best.PickVerts?.Length}: {room.Min} .. {room.Max}");
            return true;
        }

        private void MloShellTest_M1(Action<string, bool, string> check, MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (s == null) return;
            var shellWas = s.ShellFile;
            var shell = shellWas ?? (scene.Files.Count > 0 ? scene.Files[0] : null);
            if (shell != null)
            {
                s.ShellFile = shell; s.InvalidateShellBounds();
                s.Rooms[0].Min = new Vector3(1, 2, 3); s.Rooms[0].Max = new Vector3(4, 5, 6);
                bool known = s.SyncToShell(scene);
                var b = MloCreatorSession.ModelBounds(shell);
                check("shell: limbo is the shell's bounds", known && (s.Rooms[0].Min - b.Minimum).Length() < 1e-5f && (s.Rooms[0].Max - b.Maximum).Length() < 1e-5f, known ? $"{s.Rooms[0].Min} .. {s.Rooms[0].Max} vs {b.Minimum} .. {b.Maximum}" : "shell unknown");
                s.BBoxManual = false; s.BBMin = Vector3.Zero; s.BBMax = Vector3.One; s.SyncToShell(scene);
                check("shell: the interior's box follows the shell by default", (s.BBMin - b.Minimum).Length() < 1e-5f && (s.BBMax - b.Maximum).Length() < 1e-5f, $"{s.BBMin} .. {s.BBMax}");
                s.BBoxManual = true; s.BBMin = new Vector3(-50); s.SyncToShell(scene);
                check("shell: a typed box is left alone", (s.BBMin - new Vector3(-50)).Length() < 1e-5f, $"{s.BBMin}");
                s.BBoxManual = false; s.SyncToShell(scene);
                var r = s.AddRoom("m1_capture", Vector3.Zero, Vector3.One);
                ui.SelectRoom(s.Rooms.Count - 1);
                ui.RequestRoomFromShell = true;
                ServiceMloShell_M1(ui);
                check("shell: 'From shell' bounds the room to the shell", (r.Min - b.Minimum).Length() < 1e-5f && (r.Max - b.Maximum).Length() < 1e-5f && !ui.RequestRoomFromShell, $"{r.Min} .. {r.Max}");
                FrameModel(); camera.SnapSmoothing(); camera.Update();
                ui.PickingRoomMesh = true; ui.PickingRoomMeshRoom = ui.SelectedRoom;
                r.Min = Vector3.Zero; r.Max = Vector3.One;
                bool used = MloRoomMeshClick_M1(ui, (int)(deviceResources.Width * 0.5f), (int)(deviceResources.Height * 0.5f));
                check("shell: 'From mesh' takes the mesh under the click", used && !ui.PickingRoomMesh && r.IsValid && (r.Max - r.Min).Length() > 0.1f && s.History.UndoName.Contains("mesh"), $"used {used} {r.Min} .. {r.Max} undo '{s.History.UndoName}'");
                s.Rooms.Remove(r);
                s.ShellFile = shellWas; s.InvalidateShellBounds(); s.SyncToShell(scene);
            }
            else check("shell: a loaded file to test the shell rule with", false, "no files");
        }
    }
}

