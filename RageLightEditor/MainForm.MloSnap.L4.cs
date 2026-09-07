using System;
using System.Collections.Generic;
using System.Windows.Forms;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private MloVertexDots vertexDots;
        private bool l4MarkerDrawn;
        private bool l4EnvDone;
        private bool l4NoLabels;
        private const int L4DotBudget = 400000;

        private void DrawSnapMode_L4(MloCreatorPanel ui)
        {
            l4MarkerDrawn = false;
            if (ui == null) return;
            ApplySnapEnv_L4(ui);
            ui.TickSnapMode();
            if (!ui.SnapActive || !scene.HasModel) { ui.SnapDotsDrawn = 0; ui.SnapMeshesDrawn = 0; return; }

            vertexDots ??= new MloVertexDots(deviceResources.Device);
            vertexDots.BeginFrame();
            RenderMesh hitMesh = ui.Snap.Valid ? ui.Snap.Mesh : null;
            if (ui.Snap.Valid)
            {
                var centre = ui.Snap.Triangle >= 0 ? ui.Snap.SurfacePoint : ui.Snap.Position;
                float radius = Math.Max(ui.VertexRadius, 0.5f);
                var sphere = new BoundingSphere(centre, radius);
                var cands = new List<(RenderMesh m, float d)>();
                foreach (var m in scene.AllMeshes)
                {
                    if (m == null || !m.Visible || m.NeverDraw || m.PickVerts == null || m.PickVerts.Length == 0) continue;
                    var b = m.WorldBounds;
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    if (ReferenceEquals(m, hitMesh)) { cands.Add((m, -1f)); continue; }
                    if (b.Contains(ref sphere) == ContainmentType.Disjoint) continue;
                    var near = Vector3.Clamp(centre, b.Minimum, b.Maximum);
                    cands.Add((m, Vector3.Distance(near, centre)));
                }
                cands.Sort((x, y) => x.d.CompareTo(y.d));
                var ctx = deviceResources.Context;
                float vw = deviceResources.Width, vh = deviceResources.Height;
                var dotCol = T(UiTheme.Accent, 0.85f);
                var hitCol = T(UiTheme.AccentBright, 0.95f);
                int budget = L4DotBudget;
                foreach (var (m, d) in cands)
                {
                    if (budget <= 0) break;
                    bool isHit = ReferenceEquals(m, hitMesh);
                    float r = isHit && ui.ShowAllVertices ? 0f : radius;
                    vertexDots.Draw(ctx, camera, vw, vh, m, centre, r, ui.VertexSizePx, isHit ? hitCol : dotCol);
                    budget -= m.PickVerts.Length;
                }
            }
            ui.SnapDotsDrawn = vertexDots.DotsDrawn; ui.SnapMeshesDrawn = vertexDots.MeshesDrawn;

            if (ui.Snap.Valid)
            {
                var p = ui.Snap.Position;
                float wpp = camera.WorldPerPixel(p);
                var f = camera.GetForward();
                var r = camera.GetRight(); r.Normalize();
                var u = Vector3.Cross(r, f); u.Normalize();
                float rad = (ui.VertexSizePx * 1.6f + 2.5f) * wpp;
                var white = C(1.0f, 1.0f, 1.0f, 1.0f);
                var ring = T(UiTheme.AccentBright, 1.0f);
                triRenderer.AddDiscAA(p, r, u, rad, 1.2f * wpp, white, 24);
                triRenderer.AddThickCircleAA(p, r, u, rad + 2.5f * wpp, camera.Position, 0.7f * wpp, 1.2f * wpp, ring, 32);
                if (ui.Snap.Triangle >= 0 && (ui.Snap.SurfacePoint - p).LengthSquared() > 1e-6f)
                    triRenderer.AddThickLineAA(ui.Snap.SurfacePoint, p, camera.Position, 0.5f * wpp, 1.2f * wpp, L4Alpha(ring, 0.6f));
                var mesh = ui.Snap.Mesh;
                if (ui.Snap.Triangle >= 0 && mesh?.PickIndices != null && mesh.PickVerts != null && ui.Snap.Triangle * 3 + 2 < mesh.PickIndices.Length)
                {
                    var tcol = C(1.0f, 1.0f, 1.0f, 0.55f);
                    var xf = mesh.Transform;
                    var t0 = Vector3.TransformCoordinate(mesh.PickVerts[mesh.PickIndices[ui.Snap.Triangle * 3]], xf);
                    var t1 = Vector3.TransformCoordinate(mesh.PickVerts[mesh.PickIndices[ui.Snap.Triangle * 3 + 1]], xf);
                    var t2 = Vector3.TransformCoordinate(mesh.PickVerts[mesh.PickIndices[ui.Snap.Triangle * 3 + 2]], xf);
                    float twpp = camera.WorldPerPixel((t0 + t1 + t2) / 3f);
                    triRenderer.AddThickLineAA(t0, t1, camera.Position, 0.6f * twpp, 1.2f * twpp, tcol);
                    triRenderer.AddThickLineAA(t1, t2, camera.Position, 0.6f * twpp, 1.2f * twpp, tcol);
                    triRenderer.AddThickLineAA(t2, t0, camera.Position, 0.6f * twpp, 1.2f * twpp, tcol);
                }
                if (!l4NoLabels) DrawWorldLabel(p + u * (rad + 14f * wpp), $"{p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}", new Vector4(1f, 1f, 1f, 1f));
                l4MarkerDrawn = true;
            }

            if (ui.SnapMode && !renderingStill && !l4NoLabels && MloSnapPixel(out float px, out float py))
            {
                string text = ui.SnapPrompt() + (ui.Snap.Valid ? "" : "  -  no vertex here") + (string.IsNullOrEmpty(ui.SnapHint) ? "" : "  -  " + ui.SnapHint);
                var dl = ImGuiNET.ImGui.GetForegroundDrawList();
                var size = ImGuiNET.ImGui.CalcTextSize(text);
                var p0 = new System.Numerics.Vector2(px + 18, py + 16);
                var p1 = new System.Numerics.Vector2(p0.X + size.X + 10, p0.Y + size.Y + 6);
                dl.AddRectFilled(p0, p1, ImGuiNET.ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.06f, 0.08f, 0.12f, 0.80f)), 3.0f);
                dl.AddRect(p0, p1, ImGuiNET.ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(UiTheme.Accent.X, UiTheme.Accent.Y, UiTheme.Accent.Z, 0.7f)), 3.0f);
                dl.AddText(new System.Numerics.Vector2(p0.X + 5, p0.Y + 3), ImGuiNET.ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(UiTheme.AccentBright.X, UiTheme.AccentBright.Y, UiTheme.AccentBright.Z, 1f)), text);
            }
        }

        private bool DrawSnapMarker_L4(MloCreatorPanel ui) => l4MarkerDrawn;

        private void ApplySnapEnv_L4(MloCreatorPanel ui)
        {
            if (l4EnvDone || ui.Session == null) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_MLODEMO")) && !mloDemoDone) return;
            l4EnvDone = true;
            if (Environment.GetEnvironmentVariable("RLE_MLOWIN") == "0") ui.WindowVisible = false;
            var mode = Environment.GetEnvironmentVariable("RLE_MLOSNAPMODE");
            if (string.IsNullOrEmpty(mode)) return;
            var parts = mode.Split(':');
            int idx = parts.Length > 1 && int.TryParse(parts[1], out var pi) ? pi : 0;
            switch (parts[0].ToLowerInvariant())
            {
                case "portal": if (ui.CurrentPortal == null && ui.Session.Portals.Count > 0) ui.SelectPortal(0); if (ui.CurrentPortal != null) ui.BeginSnap(MloCreatorPanel.SnapTargetKind.PortalCorner, idx, chain: parts.Length > 2 && parts[2] == "all"); break;
                case "roommin": if (ui.CurrentRoom == null && ui.Session.Rooms.Count > 1) ui.SelectRoom(1); if (ui.CurrentRoom != null) ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMin); break;
                case "roommax": if (ui.CurrentRoom == null && ui.Session.Rooms.Count > 1) ui.SelectRoom(1); if (ui.CurrentRoom != null) ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMax); break;
                case "entity": if (ui.SelectedEntity < 0 && ui.Session.Entities.Count > 0) ui.SelectEntity(0); if (ui.SelectedEntity >= 0) ui.BeginSnap(MloCreatorPanel.SnapTargetKind.Entity); break;
                default: ui.BeginSnap(MloCreatorPanel.SnapTargetKind.PlacementPoint); break;
            }
            Console.WriteLine($"MLOSNAPMODE {mode}: on={ui.SnapMode} target={ui.SnapTarget} idx={ui.SnapTargetIndex}");
        }

        private bool MloSnapModeClick_L4(MloCreatorPanel ui, int x, int y)
        {
            if (ui == null || !ui.SnapMode || ui.SnapTarget == MloCreatorPanel.SnapTargetKind.None || ui.SnapTarget == MloCreatorPanel.SnapTargetKind.Picker) return false;
            if (ui.Session == null) { ui.EndSnap(); return false; }
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            if (!MloVertexSnap.Find(scene.AllMeshes, camera, ray, x, y, deviceResources.Width, deviceResources.Height, out var hit))
            {
                ui.SnapHint = "nothing under the cursor";
                ui.SetStatus("Snap: nothing under the cursor - click ON a vertex dot (Esc cancels).", true);
                return true;
            }
            ui.SnapHint = "";
            ApplySnapVertex_L4(ui, hit.Position);
            return true;
        }

        private void ApplySnapVertex_L4(MloCreatorPanel ui, Vector3 p)
        {
            var s = ui.Session;
            if (s == null) { ui.EndSnap(); return; }
            ui.SetSnapPoint(p, "vertex");
            string at = $"{p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}";
            switch (ui.SnapTarget)
            {
                case MloCreatorPanel.SnapTargetKind.PortalCorner:
                {
                    if (ui.SnapTargetPortal < 0 || ui.SnapTargetPortal >= s.Portals.Count) break;
                    var portal = s.Portals[ui.SnapTargetPortal];
                    int ci = ui.SnapTargetIndex;
                    if (ci < 0 || ci >= portal.Corners.Length) break;
                    s.PushUndo($"Snap portal corner {ci}");
                    portal.Corners[ci] = p;
                    if (ui.SnapChainCorners && ci + 1 < portal.Corners.Length)
                    {
                        ui.SnapTargetIndex = ci + 1;
                        ui.SetStatus($"Corner {ci} of portal {ui.SnapTargetPortal} on the vertex {at}.  {ui.SnapPrompt()}");
                        return;
                    }
                    if (ui.SnapChainCorners) s.OrientPortal(portal);
                    ui.SetStatus($"Corner {ci} of portal {ui.SnapTargetPortal} snapped to the vertex {at}.");
                    break;
                }
                case MloCreatorPanel.SnapTargetKind.RoomMin:
                {
                    if (ui.SnapTargetRoom < 0 || ui.SnapTargetRoom >= s.Rooms.Count) break;
                    var room = s.Rooms[ui.SnapTargetRoom];
                    s.PushUndo("Snap room min");
                    s.SnapRoomCorner(room, false, p);
                    ui.SetStatus($"Room {ui.SnapTargetRoom} min corner snapped to the vertex {at}. Now snap MAX for the box between the two.");
                    Console.WriteLine($"ROOMSNAP min room {ui.SnapTargetRoom} vertex {at} -> {room.Min} .. {room.Max}");
                    break;
                }
                case MloCreatorPanel.SnapTargetKind.RoomMax:
                {
                    if (ui.SnapTargetRoom < 0 || ui.SnapTargetRoom >= s.Rooms.Count) break;
                    var room = s.Rooms[ui.SnapTargetRoom];
                    s.PushUndo("Snap room max");
                    s.SnapRoomCorner(room, true, p);
                    ui.SetStatus($"Room {ui.SnapTargetRoom} max corner snapped to the vertex {at}: {room.Size.X:0.00} x {room.Size.Y:0.00} x {room.Size.Z:0.00} m.");
                    Console.WriteLine($"ROOMSNAP max room {ui.SnapTargetRoom} vertex {at} -> {room.Min} .. {room.Max}");
                    break;
                }
                case MloCreatorPanel.SnapTargetKind.Entity:
                {
                    if (ui.SnapTargetEntity < 0 || ui.SnapTargetEntity >= s.Entities.Count) break;
                    s.PushUndo("Snap entity to vertex");
                    s.Entities[ui.SnapTargetEntity].Position = p;
                    s.AutoAssignRooms();
                    ui.SetStatus($"Entity {ui.SnapTargetEntity} moved onto the vertex {at}.");
                    break;
                }
                default:
                    ui.SetStatus($"Vertex {at} noted as the placement point.");
                    break;
            }
            ui.EndSnap();
        }

        private bool MloSnapEscape_L4()
        {
            var ui = Creator;
            if (ui == null || !ui.SnapMode) return false;
            if (ui.SnapTarget == MloCreatorPanel.SnapTargetKind.Picker) { ui.CancelPicking(); ui.EndSnap(); ui.SetStatus("Picking cancelled."); }
            else { ui.EndSnap(); ui.SetStatus("Snap cancelled."); }
            return true;
        }

        private partial bool MloSnapRightDown_L4()
        {
            if (panel == null || !panel.MloMode) return false;
            return MloSnapEscape_L4();
        }

        partial void MloSnapModeTest_L4(Action<string, bool, string> check)
        {
            var ui = Creator;
            try
            {
                var was = panel.Workspace;
                panel.SwitchWorkspace(LightPanel.Space.Mlo);
                if (ui.Session == null && scene.HasModel) ui.Session = MloCreatorSession.FromScene(scene);
                var s = ui.Session;
                check("snapmode: session", s != null, s == null ? "none" : $"{s.Rooms.Count} rooms");
                if (s == null) return;

                ui.EndSnap(); ui.CancelPicking();
                ui.PickingPortalCorners = true; ui.PickShape = 0; ui.PickedPoints.Clear();
                ui.TickSnapMode();
                check("snapmode: a picker enters the state", ui.SnapMode && ui.SnapTarget == MloCreatorPanel.SnapTargetKind.Picker, $"on {ui.SnapMode} target {ui.SnapTarget}");
                check("snapmode: the prompt counts the corners", ui.SnapPrompt().Contains("corner 1 of 4"), ui.SnapPrompt());
                ui.PickedPoints.Add(new Vector3(1, 2, 3));
                check("snapmode: ...and moves on", ui.SnapPrompt().Contains("corner 2 of 4"), ui.SnapPrompt());
                bool esc = MloSnapEscape_L4();
                check("snapmode: Esc cancels the picker and the state", esc && !ui.SnapMode && !ui.PickingPortalCorners && ui.PickedPoints.Count == 0, $"esc {esc} on {ui.SnapMode} picking {ui.PickingPortalCorners}");
                ui.TickSnapMode();
                check("snapmode: stays off after the picker is gone", !ui.SnapMode, ui.SnapMode.ToString());

                var portal = s.AddPortal(0, Math.Min(1, s.Rooms.Count - 1), MloCreatorSession.RectangleAt(new Vector3(0, 0, 1), Vector3.UnitY, 0.6f, 1.1f));
                ui.SelectPortal(s.Portals.Count - 1);
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.PortalCorner, 2);
                check("snapmode: portal corner armed", ui.SnapMode && ui.SnapTargetPortal == ui.SelectedPortal && ui.SnapPrompt().Contains("corner 3 of 4"), ui.SnapPrompt());
                ApplySnapVertex_L4(ui, new Vector3(4, 5, 6));
                check("snapmode: the corner took the vertex and the state ended", (portal.Corners[2] - new Vector3(4, 5, 6)).Length() < 1e-5f && !ui.SnapMode, $"{portal.Corners[2]} on {ui.SnapMode}");
                check("snapmode: it is one undo step", s.History != null && s.History.CanUndo && s.History.UndoName.Contains("corner"), s.History?.UndoName ?? "no history");
                s.Undo();
                portal = s.Portals[s.Portals.Count - 1];
                check("snapmode: undo puts the corner back", (portal.Corners[2] - new Vector3(4, 5, 6)).Length() > 1e-3f, $"{portal.Corners[2]}");

                ui.SelectPortal(s.Portals.Count - 1);
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.PortalCorner, 0, chain: true);
                var quad = new[] { new Vector3(-1, 0, 0), new Vector3(-1, 0, 2), new Vector3(1, 0, 2), new Vector3(1, 0, 0) };
                for (int i = 0; i < 4; i++)
                {
                    check($"snapmode: chain waits at corner {i}", ui.SnapMode && ui.SnapTargetIndex == i, $"on {ui.SnapMode} idx {ui.SnapTargetIndex}");
                    ApplySnapVertex_L4(ui, quad[i]);
                }
                bool allOn = true;
                for (int i = 0; i < 4; i++) allOn &= Array.Exists(quad, q => (q - portal.Corners[i]).Length() < 1e-5f);
                check("snapmode: chain ends after the last corner, corners on the clicks", !ui.SnapMode && allOn, $"on {ui.SnapMode} c0 {portal.Corners[0]} c2 {portal.Corners[2]}");

                var room = s.AddRoom("snap_test", new Vector3(0, 0, 0), new Vector3(2, 2, 2));
                ui.SelectRoom(s.Rooms.Count - 1);
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMin);
                ApplySnapVertex_L4(ui, new Vector3(-1, -1, -1));
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMax);
                ApplySnapVertex_L4(ui, new Vector3(3, 4, 5));
                check("snapmode: room min / max on the vertices", (room.Min - new Vector3(-1, -1, -1)).Length() < 1e-5f && (room.Max - new Vector3(3, 4, 5)).Length() < 1e-5f && !ui.SnapMode, $"{room.Min} .. {room.Max}");
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMin);
                ApplySnapVertex_L4(ui, new Vector3(3, 4, 5));
                check("snapmode: a min past the max re-normalises (min <= max)", room.Min.X <= room.Max.X && room.Min.Y <= room.Max.Y && room.Min.Z <= room.Max.Z && room.IsValid, $"{room.Min} .. {room.Max}");
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMax);
                ApplySnapVertex_L4(ui, new Vector3(-1, -1, -1));
                check("snapmode: min then max = the two vertices' bbox", (room.Min - new Vector3(-1, -1, -1)).Length() < 1e-5f && (room.Max - new Vector3(3, 4, 5)).Length() < 1e-5f, $"{room.Min} .. {room.Max}");
                check("snapmode: each room snap is one undo step", s.History.CanUndo && s.History.UndoName.Contains("room max"), s.History.UndoName);
                s.Undo(); room = s.Rooms[s.Rooms.Count - 1];
                check("snapmode: undo puts the room's max back", (room.Max - new Vector3(-1, -1, -1)).Length() > 1e-3f, $"{room.Min} .. {room.Max}");
                if (s.Entities.Count > 0)
                {
                    ui.SelectEntity(0);
                    ui.BeginSnap(MloCreatorPanel.SnapTargetKind.Entity);
                    ApplySnapVertex_L4(ui, new Vector3(0.5f, 0.5f, 0.5f));
                    check("snapmode: the entity moved onto the vertex", (s.Entities[0].Position - new Vector3(0.5f, 0.5f, 0.5f)).Length() < 1e-5f && !ui.SnapMode, $"{s.Entities[0].Position}");
                }
                s.Rooms.Remove(room);
                MloShellTest_M1(check, ui);

                ui.SelectPortal(s.Portals.Count - 1);
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.PortalCorner, 1);
                s.Portals.RemoveAt(s.Portals.Count - 1); ui.ClampSelection();
                ui.TickSnapMode();
                check("snapmode: a deleted portal drops the state", !ui.SnapMode, ui.SnapMode.ToString());

                FrameModel();
                camera.SnapSmoothing(); camera.Update();
                float w = deviceResources.Width, h = deviceResources.Height;
                var room2 = s.AddRoom("snap_click", new Vector3(0, 0, 0), new Vector3(2, 2, 2));
                ui.SelectRoom(s.Rooms.Count - 1);
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.RoomMin);
                bool used = MloSnapModeClick_L4(ui, (int)(w * 0.5f), (int)(h * 0.5f));
                bool onVertex = MloVertexSnap.NearestToPoint(scene.AllMeshes, room2.Min, 0.001f, out _);
                check("snapmode: a click puts a real vertex into the room's min", used && onVertex && !ui.SnapMode, $"used {used} min {room2.Min} on {ui.SnapMode}");
                s.Rooms.Remove(room2);
                ui.BeginSnap(MloCreatorPanel.SnapTargetKind.PlacementPoint);
                bool rc = MloSnapRightDown_L4();
                bool rc2 = MloSnapRightDown_L4();
                check("snapmode: right click cancels once, then falls through", rc && !rc2 && !ui.SnapMode, $"{rc} {rc2}");

                ui.SnapToVertex = true;
                var ray = camera.GetPickRay(w * 0.5f, h * 0.5f, w, h);
                ui.Snap = MloVertexSnap.Find(scene.AllMeshes, camera, ray, w * 0.5f, h * 0.5f, w, h, out var hit) ? hit : default;
                deviceResources.BeginBackbuffer();
                l4NoLabels = true;
                DrawSnapMode_L4(ui);
                l4NoLabels = false;
                check("snapmode: vertex dots drawn for the meshes around the cursor", ui.Snap.Valid && ui.SnapDotsDrawn > 0 && ui.SnapMeshesDrawn > 0, $"{ui.SnapDotsDrawn} dots, {ui.SnapMeshesDrawn} meshes, snap {ui.Snap.Valid}");
                ui.SnapToVertex = false; ui.Snap = default;

                ui.SetStatus("--seqtest: snap mode checked.");
                if (screenshotPath == null) panel.SwitchWorkspace(was);
            }
            catch (Exception ex)
            {
                check("snapmode: no exception", false, ex.ToString());
            }
        }
    }
}

