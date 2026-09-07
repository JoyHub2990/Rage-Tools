using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public bool DebugMloSpace;

        private MloBridge mloBridge;
        private string mloSnapDebugPixel;
        private bool mloSnapDebugLogged;
        private bool mloGizmoWasEnabled = true;
        private bool mloDemoDone;

        private static readonly Vector4 CrSnap = C(0.35f, 1.0f, 0.55f, 1.0f);
        private static readonly Vector4 CrSnapFill = F(0.35f, 1.0f, 0.55f, 0.55f);
        private static readonly Vector4 CrHandle = C(1.0f, 0.85f, 0.35f, 0.9f);
        private static readonly Vector4 CrHandleFill = F(1.0f, 0.85f, 0.35f, 0.35f);
        private static readonly Vector4 CrHandleSel = C(1.0f, 1.0f, 1.0f, 1.0f);

        partial void WireMloWorkspace_J6()
        {
            mloSnapDebugPixel = Environment.GetEnvironmentVariable("RLE_MLOSNAP");
            panel.MloScene = mloScene;
            panel.SectionSceneLookup_S1 = SceneFor_L3;
            Creator.LayoutVersionSeen_S1 = settings.MloCreatorLayoutVersion;
            panel.WorkspaceSwitching += (leaving, entering) =>
            {
                if (entering == LightPanel.Space.Mlo && gizmo != null) { mloGizmoWasEnabled = gizmo.Enabled; gizmo.Enabled = false; }
                else if (leaving == LightPanel.Space.Mlo && gizmo != null) gizmo.Enabled = mloGizmoWasEnabled;
                if (leaving == LightPanel.Space.Mlo) Creator.SnapHeld = false;
                if (entering != leaving) BindActiveScene_L3(SceneFor_L3(entering));
            };
            WireMloAssets_L3();
            ApplyMloCreatorSettings_K1();
        }

        partial void ApplyMloSpaceFlag_J6()
        {
            if (!DebugMloSpace) return;
            panel.Workspace = LightPanel.Space.Mlo;
            panel.ApplyThemeFromSettings(false);
            BindActiveScene_L3(mloScene);
        }

        partial void MloWorkspaceKeyDown_J6(Keys combo, ref bool handled)
        {
            if (!panel.MloMode) return;
            if (ImGuiWantsKeyboard && !ignoreImGuiKeyboard) return;
            var key = combo & Keys.KeyCode;
            bool chord = (combo & (Keys.Control | Keys.Alt)) != 0;
            if (key == Keys.V && !chord) { Creator.SnapHeld = true; handled = true; return; }
            if (key == Keys.Escape && MloSnapEscape_L4()) { handled = true; return; }
            if (key == Keys.Escape && Creator.CancelRoomMeshPick()) { Creator.SetStatus("Mesh pick cancelled."); handled = true; return; }
            if (key == Keys.Escape)
            {
                var ui = Creator;
                if (ui.PickingPortalCorners || ui.PickingRoomCorners) { ui.CancelPicking(); ui.SetStatus("Picking cancelled."); handled = true; }
                else if (ui.SelectedCorner >= 0) { ui.SelectedCorner = -1; handled = true; }
            }
        }

        partial void MloWorkspaceKeyUp_J6(Keys key)
        {
            if ((key & Keys.KeyCode) == Keys.V) Creator.SnapHeld = false;
        }

        private bool MloSnapPixel(out float px, out float py)
        {
            px = lastMouse.X; py = lastMouse.Y;
            if (!string.IsNullOrEmpty(mloSnapDebugPixel))
            {
                var parts = mloSnapDebugPixel.Split(',');
                if (parts.Length >= 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                                      && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                { px = x; py = y; return true; }
            }
            return !ImGuiWantsMouse;
        }

        private void DrawMloWorkspaceOverlay_J6(MloCreatorPanel ui)
        {
            TickMloBridge(ui);
            ServiceSnapPointRequests(ui);
            UpdateMloLightGizmoEnabled_L3(ui);
            var s = ui.Session;
            if (s != null && !mloDemoDone && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_MLODEMO")))
            {
                mloDemoDone = true;
                var demo = Environment.GetEnvironmentVariable("RLE_MLODEMO");
                if (demo.Contains("room"))
                {
                    var c = camera.Target;
                    var r = s.AddRoom(NextRoomName(s), c - new Vector3(2, 2, 1.5f), c + new Vector3(2, 2, 1.5f));
                    s.AutoAssignRooms();
                    ui.SelectRoom(s.Rooms.Count - 1);
                }
                if (demo.Contains("corners")) { ui.CornerMode = true; ui.SelectedCorner = 6; if (ui.SelectedRoom < 0) ui.SelectRoom(Math.Min(1, s.Rooms.Count - 1)); ui.FocusKind = 0; }
                if (demo.Contains("portal") && ui.CurrentRoom != null)
                {
                    var r = ui.CurrentRoom;
                    var c = new Vector3(r.Min.X + 0.02f, r.Centre.Y, r.Min.Z + 1.2f);
                    var p = s.AddPortal(ui.SelectedRoom, 0, MloCreatorSession.RectangleAt(c, Vector3.UnitY, 0.6f, 1.1f));
                    s.OrientPortal(p);
                    int rp = s.Portals.Count - 1;
                    if (!demo.Contains("corners")) ui.SelectPortal(rp); else { ui.SelectedPortal = rp; }
                }
                if (demo.Contains("bridge")) StartMloBridge(ui, ui.BridgePort);
                if (demo.Contains("sections")) ui.ShowPage(MloCreatorPanel.PageKind.Snap);
                Console.WriteLine($"MLODEMO {demo}: rooms {s.Rooms.Count} portals {s.Portals.Count} corner {ui.SelectedCorner}");
            }

            if (!string.IsNullOrEmpty(mloSnapDebugPixel)) ui.SnapHeld = true;

            float px = 0, py = 0;
            bool want = ui.SnapActive && MloSnapPixel(out px, out py);
            if (want)
            {
                var ray = camera.GetPickRay(px, py, deviceResources.Width, deviceResources.Height);
                if (MloVertexSnap.Find(scene.AllMeshes, camera, ray, px, py, deviceResources.Width, deviceResources.Height, out var hit)) ui.Snap = hit;
                else ui.Snap = default;
                if (!string.IsNullOrEmpty(mloSnapDebugPixel) && !mloSnapDebugLogged && scene.HasModel && (DebugMlo == null || debugMloDone))
                {
                    mloSnapDebugLogged = true;
                    var h = ui.Snap;
                    Console.WriteLine(h.Valid
                        ? $"SNAP px=({px:0},{py:0}) vertex=({h.Position.X:0.000},{h.Position.Y:0.000},{h.Position.Z:0.000}) v{h.VertexIndex} tri={h.Triangle} screen={h.ScreenDistance:0.0}px mesh={h.Mesh?.ShaderName} verts={h.Mesh?.PickVerts?.Length}"
                        : $"SNAP px=({px:0},{py:0}) none");
                    if (h.Valid) ui.SetSnapPoint(h.Position, "RLE_MLOSNAP");
                }
            }
            else ui.Snap = default;
            DrawSnapMode_L4(ui);

            if (ui.Snap.Valid && ui.SnapActive && !DrawSnapMarker_L4(ui))
            {
                DrawScreenSquare(ui.Snap.Position, 5.0f, CrSnap, CrSnapFill);
                if (ui.Snap.Triangle >= 0 && (ui.Snap.SurfacePoint - ui.Snap.Position).LengthSquared() > 1e-6f)
                    lineRenderer.AddLine(ui.Snap.SurfacePoint, ui.Snap.Position, C(0.35f, 1.0f, 0.55f, 0.5f));
                if (ui.LabelFor_V19(true))
                    DrawWorldLabel(ui.Snap.Position + new Vector3(0, 0, 0.12f),
                        $"v {ui.Snap.Position.X:0.00}, {ui.Snap.Position.Y:0.00}, {ui.Snap.Position.Z:0.00}", new Vector4(0.6f, 1.0f, 0.7f, 1.0f));
            }
            if (ui.HasSnapPoint && (!ui.Snap.Valid || (ui.Snap.Position - ui.SnapPoint).LengthSquared() > 1e-6f))
            {
                DrawScreenSquare(ui.SnapPoint, 4.0f, C(0.35f, 0.9f, 1.0f, 1.0f), F(0.35f, 0.9f, 1.0f, 0.4f));
                lineRenderer.AddLine(ui.SnapPoint - Vector3.UnitZ * 0.15f, ui.SnapPoint + Vector3.UnitZ * 0.15f, C(0.35f, 0.9f, 1.0f, 0.8f));
            }
            if (s == null) return;

            if (ui.CornerMode)
            {
                var corners = SelectedCorners(ui);
                for (int i = 0; corners != null && i < corners.Length; i++)
                {
                    bool sel = ui.SelectedCorner == i;
                    DrawScreenSquare(corners[i], sel ? 6.0f : 4.0f, sel ? CrHandleSel : CrHandle, sel ? CrSnapFill : CrHandleFill);
                    if (sel && ui.LabelFor_V19(true))
                        DrawWorldLabel(corners[i] + new Vector3(0, 0, 0.12f), $"corner {i}: {corners[i].X:0.00}, {corners[i].Y:0.00}, {corners[i].Z:0.00}", new Vector4(1.0f, 0.9f, 0.6f, 1.0f));
                }
            }

            if (ui.PickingRoomCorners)
            {
                foreach (var pt in ui.PickedRoomPoints) lineRenderer.AddSphere(pt, 0.06f, CrPick, 16);
                if (ui.PickedRoomPoints.Count > 0)
                {
                    Vector3? hover = ui.Snap.Valid ? ui.Snap.Position
                        : (MloSnapPixel(out float hx, out float hy) && MloCreatorSession.RayHitScene(scene, camera.GetPickRay(hx, hy, deviceResources.Width, deviceResources.Height), out var hp, out _) ? hp : (Vector3?)null);
                    if (hover.HasValue)
                    {
                        var a = ui.PickedRoomPoints[0]; var b = hover.Value;
                        var bb = new BoundingBox(Vector3.Min(a, b), Vector3.Max(a, b));
                        lineRenderer.AddBox(bb, CrPick);
                        AddBoxFill(bb.Minimum, bb.Maximum, CrPickFill);
                    }
                }
            }
        }

        private void DrawScreenSquare(Vector3 p, float halfPx, Vector4 col, Vector4 fill)
        {
            float m = camera.WorldPerPixel(p) * halfPx;
            var f = camera.GetForward();
            var r = camera.GetRight(); r.Normalize();
            var u = Vector3.Cross(r, f); u.Normalize();
            var p0 = p - r * m - u * m; var p1 = p + r * m - u * m; var p2 = p + r * m + u * m; var p3 = p - r * m + u * m;
            triRenderer.AddQuad(p0, p1, p2, p3, fill);
            lineRenderer.AddLine(p0, p1, col); lineRenderer.AddLine(p1, p2, col); lineRenderer.AddLine(p2, p3, col); lineRenderer.AddLine(p3, p0, col);
        }

        private static Vector3[] SelectedCorners(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (s == null) return null;
            if (ui.FocusKind == 1 && ui.CurrentPortal != null) return ui.CurrentPortal.Corners;
            var r = ui.CurrentRoom;
            if (r == null || (ui.FocusKind == 1 && ui.SelectedPortal >= 0)) return null;
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) != 0 ? r.Max.X : r.Min.X, (i & 2) != 0 ? r.Max.Y : r.Min.Y, (i & 4) != 0 ? r.Max.Z : r.Min.Z);
            return c;
        }

        private int CornerHandleAt(MloCreatorPanel ui, float mx, float my, float radiusPx = 10.0f)
        {
            var corners = SelectedCorners(ui);
            if (corners == null) return -1;
            int best = -1; float bestPx = radiusPx;
            for (int i = 0; i < corners.Length; i++)
            {
                var clip = Vector4.Transform(new Vector4(corners[i], 1.0f), camera.ViewProjMatrix);
                if (clip.W <= 1e-5f) continue;
                float sx = (clip.X / clip.W * 0.5f + 0.5f) * deviceResources.Width;
                float sy = (0.5f - clip.Y / clip.W * 0.5f) * deviceResources.Height;
                float d = (float)Math.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (d < bestPx) { bestPx = d; best = i; }
            }
            return best;
        }

        private sealed class CreatorRoomCornerTarget : IWorldGizmoTarget
        {
            public readonly MloCreatorSession Session; public readonly MloCreatorRoom Room; public readonly int Corner;
            public CreatorRoomCornerTarget(MloCreatorSession s, MloCreatorRoom r, int corner) { Session = s; Room = r; Corner = corner; }
            public object Key => this;
            public Vector3 Position => new Vector3((Corner & 1) != 0 ? Room.Max.X : Room.Min.X, (Corner & 2) != 0 ? Room.Max.Y : Room.Min.Y, (Corner & 4) != 0 ? Room.Max.Z : Room.Min.Z);
            public Quaternion Orientation => Quaternion.Identity;
            public Vector3 Scale => Vector3.One;
            public WorldWidgetAxis RotationAxes => WorldWidgetAxis.None;
            public bool ScaleLockXY => false;
            public bool CanScale => false;
            public void SetPosition(Vector3 p)
            {
                var mn = Room.Min; var mx = Room.Max;
                if ((Corner & 1) != 0) mx.X = p.X; else mn.X = p.X;
                if ((Corner & 2) != 0) mx.Y = p.Y; else mn.Y = p.Y;
                if ((Corner & 4) != 0) mx.Z = p.Z; else mn.Z = p.Z;
                Room.Min = Vector3.Min(mn, mx); Room.Max = Vector3.Max(mn, mx);
                Session.AutoAssignRooms();
            }
            public void SetOrientation(Quaternion q) { }
            public void SetScale(Vector3 s) { }
            public override bool Equals(object o) => o is CreatorRoomCornerTarget t && t.Room == Room && t.Corner == Corner;
            public override int GetHashCode() => Room.GetHashCode() * 31 + Corner;
        }

        private sealed class CreatorPortalCornerTarget : IWorldGizmoTarget
        {
            public readonly MloCreatorPortal Portal; public readonly int Corner;
            public CreatorPortalCornerTarget(MloCreatorPortal p, int corner) { Portal = p; Corner = corner; }
            public object Key => this;
            public Vector3 Position => Corner < Portal.Corners.Length ? Portal.Corners[Corner] : Portal.Centre;
            public Quaternion Orientation => Quaternion.Identity;
            public Vector3 Scale => Vector3.One;
            public WorldWidgetAxis RotationAxes => WorldWidgetAxis.None;
            public bool ScaleLockXY => false;
            public bool CanScale => false;
            public void SetPosition(Vector3 p) { if (Corner < Portal.Corners.Length) Portal.Corners[Corner] = p; }
            public void SetOrientation(Quaternion q) { }
            public void SetScale(Vector3 s) { }
            public override bool Equals(object o) => o is CreatorPortalCornerTarget t && t.Portal == Portal && t.Corner == Corner;
            public override int GetHashCode() => Portal.GetHashCode() * 31 + Corner;
        }

        private sealed class CreatorEntityTarget : IWorldGizmoTarget
        {
            public readonly MloCreatorSession Session; public readonly MloCreatorEntity Entity;
            public CreatorEntityTarget(MloCreatorSession s, MloCreatorEntity e) { Session = s; Entity = e; }
            public object Key => Entity;
            public Vector3 Position => Entity.Position;
            public Quaternion Orientation => Entity.Rotation;
            public Vector3 Scale => Entity.Scale;
            public WorldWidgetAxis RotationAxes => WorldWidgetAxis.XYZ;
            public bool ScaleLockXY => false;
            public bool CanScale => true;
            public void SetPosition(Vector3 p) { Entity.Position = p; Session.AutoAssignRooms(); }
            public void SetOrientation(Quaternion q) { Entity.Rotation = q; }
            public void SetScale(Vector3 s) { Entity.Scale = new Vector3(Math.Max(s.X, 0.01f), Math.Max(s.Y, 0.01f), Math.Max(s.Z, 0.01f)); }
        }

        private IWorldGizmoTarget MloWorkspaceGizmoTarget(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (s == null) return null;
            if (ui.CornerMode && ui.SelectedCorner >= 0)
            {
                if (ui.FocusKind == 1 && ui.CurrentPortal != null && ui.SelectedCorner < ui.CurrentPortal.Corners.Length)
                    return new CreatorPortalCornerTarget(ui.CurrentPortal, ui.SelectedCorner);
                if (ui.FocusKind != 1 && ui.CurrentRoom != null && ui.SelectedCorner < 8)
                    return new CreatorRoomCornerTarget(s, ui.CurrentRoom, ui.SelectedCorner);
            }
            if (ui.FocusKind == 2 && ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count)
                return new CreatorEntityTarget(s, s.Entities[ui.SelectedEntity]);
            return null;
        }

        private void MloSnapGizmoDrag(MloCreatorPanel ui, IList<IWorldGizmoTarget> targets)
        {
            if (!ui.SnapActive || !ui.Snap.Valid || targets == null || targets.Count == 0) return;
            if (creatorGizmo == null || !creatorGizmo.Dragging || creatorGizmo.Mode != WorldGizmoMode.Translate) return;
            var t = targets[0];
            t.SetPosition(ui.Snap.Position);
        }

        private bool MloClickPoint(MloCreatorPanel ui, int x, int y, out Vector3 p, out bool snapped)
        {
            snapped = false; p = Vector3.Zero;
            if (ui.SnapActive)
            {
                var ray0 = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
                if (MloVertexSnap.Find(scene.AllMeshes, camera, ray0, x, y, deviceResources.Width, deviceResources.Height, out var hit))
                {
                    p = hit.Position; snapped = true;
                    ui.SetSnapPoint(p, "vertex");
                    return true;
                }
            }
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            if (!MloCreatorSession.RayHitScene(scene, ray, out p, out _)) return false;
            return true;
        }

        private void MloAddPickedRoomPoint(MloCreatorPanel ui, Vector3 p)
        {
            var s = ui.Session;
            if (s == null) return;
            ui.PickedRoomPoints.Add(p);
            if (ui.RoomPickAll)
            {
                ui.SetStatus($"Vertex {ui.PickedRoomPoints.Count} placed - click more, then Finish (the room bounds them all).");
                return;
            }
            if (ui.PickedRoomPoints.Count < 2) { ui.SetStatus("Min corner placed - now click the max corner (the opposite one, at the ceiling)."); return; }
            FinishRoomFromPoints(ui);
        }

        private void FinishRoomFromPoints(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (s == null || ui.PickedRoomPoints.Count < 2) return;
            if (!MloCreatorSession.BoxFromPoints(ui.PickedRoomPoints, out var mn, out var mx, out var note)) return;
            int n = ui.PickedRoomPoints.Count;
            s.PushUndo($"Room from {n} vertices");
            var r = s.AddRoom(NextRoomName(s), mn, mx);
            s.AutoAssignRooms();
            ui.SelectRoom(s.Rooms.Count - 1);
            ui.PickedRoomPoints.Clear(); ui.PickingRoomCorners = false; ui.RoomPickAll = false;
            ui.SetStatus($"Room {ui.SelectedRoom} '{r.Name}' from {n} vertices: {r.Size.X:0.00} x {r.Size.Y:0.00} x {r.Size.Z:0.00} m{note}.");
        }

        private bool MloWorkspaceClick(MloCreatorPanel ui, int x, int y)
        {
            var s = ui.Session;
            if (MloRoomMeshClick_M1(ui, x, y)) return true;
            if (ui.LightEditingActive && PickLight(x, y)) return true;
            if (ui.CornerMode && s != null)
            {
                int c = CornerHandleAt(ui, x, y);
                if (c >= 0) { ui.SelectedCorner = c; ui.SetStatus($"Corner {c} selected - drag its gizmo (hold V to land on a vertex)."); return true; }
            }
            if (ui.PickingRoomCorners && s != null)
            {
                if (!MloClickPoint(ui, x, y, out var p, out bool snapped)) { ui.SetStatus("Click ON the geometry - nothing was under the cursor.", true); return true; }
                if (snapped) ui.SetSnapPoint(p, "vertex");
                MloAddPickedRoomPoint(ui, p);
                return true;
            }
            if (MloEntityClick_N3(ui, x, y)) return true;
            var ray = camera.GetPickRay(x, y, deviceResources.Width, deviceResources.Height);
            var file = FindPropUnder(ray);
            if (file != null)
            {
                var mods = ModifierKeys;
                scene.SelectFile(file, (mods & Keys.Control) != 0, (mods & Keys.Shift) != 0);
                panel.ScrollToActiveProp = true;
                ui.SetStatus($"Selected {file.Name}" + (s != null ? " - Capture selection makes a room around it." : "."));
                if (s != null)
                {
                    int ei = s.Entities.FindIndex(e => e.SourceFile == file);
                    if (ei >= 0) { ui.SelectEntity(ei); ui.RevealSelection = true; }
                }
            }
            else if (!(ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Shift)))
            {
                scene.SelectedFiles.Clear();
                UpdatePropHighlights();
            }
            return true;
        }

        private void ServiceSnapPointRequests(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (ui.RequestFinishRoomFromPoints)
            {
                ui.RequestFinishRoomFromPoints = false;
                if (s != null) FinishRoomFromPoints(ui);
            }
            if (ui.RequestSnapCornerToNearestVertex)
            {
                ui.RequestSnapCornerToNearestVertex = false;
                if (s != null && ui.CurrentPortal != null)
                {
                    var portal = ui.CurrentPortal;
                    int ci = ui.RequestSnapCornerIndex;
                    if (ci >= 0 && ci < portal.Corners.Length &&
                        MloVertexSnap.NearestToPoint(scene.AllMeshes, portal.Corners[ci], 2.0f, out var near))
                    {
                        s.PushUndo("Snap corner to vertex");
                        portal.Corners[ci] = near.Position;
                        ui.SetStatus($"Corner {ci} snapped to the nearest vertex: {near.Position.X:0.00}, {near.Position.Y:0.00}, {near.Position.Z:0.00}.");
                    }
                    else ui.SetStatus("No mesh vertex within reach of that corner.", true);
                }
            }
            if (s == null || !ui.HasSnapPoint)
            {
                ui.RequestSnapPointToEntity = ui.RequestSnapPointToRoomMin = ui.RequestSnapPointToRoomMax = ui.RequestSnapPointToRoomCentre = ui.RequestSnapPointAsCorner = false;
                return;
            }
            var p = ui.SnapPoint;
            if (ui.RequestSnapPointToEntity)
            {
                ui.RequestSnapPointToEntity = false;
                if (ui.SelectedEntity >= 0 && ui.SelectedEntity < s.Entities.Count)
                {
                    s.PushUndo();
                    s.Entities[ui.SelectedEntity].Position = p; s.AutoAssignRooms();
                    ui.SetStatus($"Entity {ui.SelectedEntity} moved to the placement point.");
                }
            }
            var room = ui.CurrentRoom;
            if (ui.RequestSnapPointToRoomMin) { ui.RequestSnapPointToRoomMin = false; if (room != null) { s.PushUndo("Room min to placement point"); s.SnapRoomCorner(room, false, p); ui.SetStatus("Room min set to the placement point."); } }
            if (ui.RequestSnapPointToRoomMax) { ui.RequestSnapPointToRoomMax = false; if (room != null) { s.PushUndo("Room max to placement point"); s.SnapRoomCorner(room, true, p); ui.SetStatus("Room max set to the placement point."); } }
            if (ui.RequestSnapPointToRoomCentre) { ui.RequestSnapPointToRoomCentre = false; if (room != null) { s.PushUndo(); var h = room.Size * 0.5f; room.Min = p - h; room.Max = p + h; s.AutoAssignRooms(); ui.SetStatus("Room centred on the placement point."); } }
            if (ui.RequestSnapPointAsCorner)
            {
                ui.RequestSnapPointAsCorner = false;
                MloUsePointAsCorner(ui, p, "placement point");
            }
        }

        private void MloUsePointAsCorner(MloCreatorPanel ui, Vector3 p, string source)
        {
            if (ui.PickingRoomCorners) { MloAddPickedRoomPoint(ui, p); return; }
            if (ui.PickingPortalCorners) { AddPortalCornerPoint(ui, p); return; }
            ui.SetStatus($"Point from {source} noted as the placement point - start 'Portal from 4 vertices' or 'Room from 2 vertices' to place with it.");
        }

        private void TickMloBridge(MloCreatorPanel ui)
        {
            if (ui.RequestBridgeToggle)
            {
                ui.RequestBridgeToggle = false;
                if (!ui.BridgeEnabled) StartMloBridge(ui, ui.BridgePort);
                else StopMloBridge(ui);
            }
            if (mloBridge == null || !mloBridge.Listening)
            {
                if (ui.BridgeEnabled) { ui.BridgeEnabled = false; ui.BridgeStatus = "off"; }
                return;
            }
            ui.BridgeStatus = $"listening on 127.0.0.1:{mloBridge.Port}  -  {mloBridge.Clients} client{(mloBridge.Clients == 1 ? "" : "s")}, {mloBridge.Received} message{(mloBridge.Received == 1 ? "" : "s")}";
            ui.BridgeError = false;
            foreach (var m in mloBridge.Drain()) HandleBridgeMessage(ui, m);
        }

        private int StartMloBridge(MloCreatorPanel ui, int port)
        {
            mloBridge ??= new MloBridge();
            if (!mloBridge.Start(port))
            {
                ui.BridgeEnabled = false; ui.BridgeError = true;
                ui.BridgeStatus = "could not listen on port " + port + ": " + mloBridge.Error;
                ui.BridgeSay(ui.BridgeStatus);
                return -1;
            }
            ui.BridgeEnabled = true; ui.BridgeError = false; ui.BridgePort = mloBridge.Port; ui.BridgeOpenHeader = true;
            ui.BridgeStatus = $"listening on 127.0.0.1:{mloBridge.Port}";
            ui.BridgeSay("listening on port " + mloBridge.Port);
            Console.WriteLine($"BRIDGE listening 127.0.0.1:{mloBridge.Port}");
            return mloBridge.Port;
        }

        private void StopMloBridge(MloCreatorPanel ui)
        {
            mloBridge?.Stop();
            ui.BridgeEnabled = false; ui.BridgeStatus = "off";
            ui.BridgeSay("stopped");
        }

        private void HandleBridgeMessage(MloCreatorPanel ui, MloBridgeMessage m)
        {
            var s = ui.Session;
            Console.WriteLine("BRIDGE " + m.Raw);
            ui.BridgeOpenHeader = true;
            bool handledW1 = false; ApiMessage_W1(m, ref handledW1); if (handledW1) return;
            switch (m.Command)
            {
                case "hello":
                    m.From?.Send(DccBridgeProtocol.Hello(m.Json, AppVersion_P5(), SpaceNames.NameOf(panel.Workspace), s?.Name ?? "", mloBridge?.Clients ?? 0));
                    ui.BridgeSay("hello from " + m.From?.Remote);
                    break;
                case "ping":
                    m.From?.Send(m.Json ? "{\"type\":\"pong\",\"app\":\"RAGE Tools\"}" : "pong RAGE Tools");
                    ui.BridgeSay("ping from " + m.From?.Remote);
                    break;
            }
        }

        partial void MloWorkspaceTest_J6(Action<string, bool, string> check)
        {
            var ui = Creator;
            try
            {
                var was = panel.Workspace;
                panel.SwitchWorkspace(LightPanel.Space.Mlo);
                check("mlospace: workspace switches", panel.MloMode, panel.Workspace.ToString());
                check("mlospace: light gizmo parked", gizmo == null || !gizmo.Enabled, gizmo == null ? "no gizmo" : gizmo.Enabled.ToString());
                if (ui.Session == null && scene.HasModel) ui.Session = MloCreatorSession.FromScene(scene);
                var s = ui.Session;
                check("mlospace: session over the scene", s != null, s == null ? "none" : $"{s.Rooms.Count} rooms");
                if (s == null) return;

                FrameModel();
                camera.SnapSmoothing(); camera.Update();
                float w = deviceResources.Width, h = deviceResources.Height;
                var ray = camera.GetPickRay(w * 0.5f, h * 0.5f, w, h);
                bool surface = MloCreatorSession.RayHitScene(scene, ray, out var sp, out _);
                check("snap: the centre ray meets the model", surface, surface ? $"{sp}" : "no surface");
                bool found = MloVertexSnap.Find(scene.AllMeshes, camera, ray, w * 0.5f, h * 0.5f, w, h, out var hit);
                check("snap: a vertex is found under the centre", found && hit.Valid, found ? hit.Describe() : "none");
                if (found)
                {
                    var v = Vector3.TransformCoordinate(hit.Mesh.PickVerts[hit.VertexIndex], hit.Mesh.Transform);
                    check("snap: the point is the mesh's vertex", (v - hit.Position).Length() < 1e-5f, $"{v} vs {hit.Position}");
                    bool onTri = false;
                    if (hit.Triangle >= 0)
                        for (int k = 0; k < 3; k++) onTri |= hit.Mesh.PickIndices[hit.Triangle * 3 + k] == hit.VertexIndex;
                    check("snap: within the radius or on the hit triangle", hit.ScreenDistance <= MloVertexSnap.RadiusPx + 0.01f || onTri,
                          $"{hit.ScreenDistance:0.0} px, tri {hit.Triangle}, onTri {onTri}");
                    bool near = MloVertexSnap.NearestToPoint(scene.AllMeshes, hit.Position + new Vector3(0.004f, -0.003f, 0.002f), 0.05f, out var nh);
                    check("snap: nearest-to-point finds it again", near && (nh.Position - hit.Position).Length() < 1e-4f, near ? $"{nh.Position}" : "none");
                    ui.SetSnapPoint(hit.Position, "seqtest");
                }
                var away = new Ray(new Vector3(500, 500, 500), Vector3.UnitZ);
                bool none = MloVertexSnap.Find(scene.AllMeshes, camera, away, -5000, -5000, w, h, out _);
                check("snap: nothing far from every mesh", !none, none ? "found something" : "none");

                var room = s.AddRoom("corner_test", new Vector3(0, 0, 0), new Vector3(2, 2, 2));
                var ct = new CreatorRoomCornerTarget(s, room, 7);
                ct.SetPosition(new Vector3(3, 2.5f, 4));
                check("corner: dragging corner 7 moves only max", (room.Max - new Vector3(3, 2.5f, 4)).Length() < 1e-5f && room.Min == Vector3.Zero, $"{room.Min} .. {room.Max}");
                var ct0 = new CreatorRoomCornerTarget(s, room, 0);
                ct0.SetPosition(new Vector3(-1, -1, -1));
                check("corner: dragging corner 0 moves only min", (room.Min - new Vector3(-1, -1, -1)).Length() < 1e-5f && (room.Max - new Vector3(3, 2.5f, 4)).Length() < 1e-5f, $"{room.Min} .. {room.Max}");
                ct0.SetPosition(new Vector3(5, 5, 5));
                check("corner: dragging past the opposite keeps a valid box", room.IsValid && room.Min.X <= room.Max.X, $"{room.Min} .. {room.Max}");
                s.Rooms.Remove(room);

                int before = s.Rooms.Count;
                ui.PickingRoomCorners = true; ui.PickedRoomPoints.Clear();
                MloAddPickedRoomPoint(ui, new Vector3(4, -3, 0));
                check("room from vertices: waits for the second point", ui.PickingRoomCorners && s.Rooms.Count == before, $"{s.Rooms.Count} rooms");
                MloAddPickedRoomPoint(ui, new Vector3(1, 2, 3));
                var nr = s.Rooms.Count > before ? s.Rooms[s.Rooms.Count - 1] : null;
                check("room from vertices: the box between the two", nr != null && (nr.Min - new Vector3(1, -3, 0)).Length() < 1e-5f && (nr.Max - new Vector3(4, 2, 3)).Length() < 1e-5f && !ui.PickingRoomCorners,
                      nr == null ? "no room" : $"{nr.Min} .. {nr.Max}");
                ui.PickingRoomCorners = true; ui.PickedRoomPoints.Clear();
                MloAddPickedRoomPoint(ui, new Vector3(0, 0, 0)); MloAddPickedRoomPoint(ui, new Vector3(3, 3, 0));
                var fr = s.Rooms[s.Rooms.Count - 1];
                check("room from vertices: flat picks get a room height", fr.Size.Z > 2.0f, $"{fr.Size}");

                {
                    ignoreImGuiKeyboard = true;
                    bool wm = walkMode;
                    ui.SnapHeld = false;
                    OnKeyDownEv(this, new KeyEventArgs(Keys.V));
                    check("keys: V held snaps and does not toggle walk mode", ui.SnapHeld && walkMode == wm, $"snapHeld {ui.SnapHeld} walk {walkMode} (was {wm})");
                    OnKeyUpEv(this, new KeyEventArgs(Keys.V));
                    check("keys: V released ends the snap", !ui.SnapHeld, ui.SnapHeld.ToString());
                    ignoreImGuiKeyboard = false;
                    ui.PickingRoomCorners = true; ui.PickedRoomPoints.Clear(); ui.SnapToVertex = true;
                    bool used = MloWorkspaceClick(ui, (int)(w * 0.5f), (int)(h * 0.5f));
                    bool onVertex = ui.PickedRoomPoints.Count == 1 && MloVertexSnap.NearestToPoint(scene.AllMeshes, ui.PickedRoomPoints[0], 0.001f, out _);
                    check("click: with the snap on the picked point is a vertex", used && onVertex, ui.PickedRoomPoints.Count == 1 ? $"{ui.PickedRoomPoints[0]}" : "no point");
                    ui.CancelPicking(); ui.SnapToVertex = false;
                }

                int port = StartMloBridge(ui, 0);
                check("bridge: listener starts on a free port", port > 0, port > 0 ? $"port {port}" : ui.BridgeStatus);
                if (port > 0)
                {
                    using var tcp = new TcpClient();
                    tcp.Connect("127.0.0.1", port);
                    tcp.ReceiveTimeout = 3000;
                    var stream = tcp.GetStream();
                    var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    void Pump(Func<bool> until)
                    {
                        for (int i = 0; i < 300 && !until(); i++) { TickMloBridge(ui); Thread.Sleep(10); }
                    }
                    void Send(string line)
                    {
                        int n = mloBridge.Received;
                        writer.WriteLine(line);
                        Pump(() => mloBridge.Received > n);
                    }
                    string ReadLine() { try { return reader.ReadLine(); } catch { return null; } }

                    Send("ping");
                    var ack = ReadLine();
                    check("api listener: a plain ping is answered", ack != null && ack.StartsWith("pong"), ack ?? "(no reply)");
                    Send("{\"cmd\":\"hello\"}");
                    var hello = ReadLine();
                    check("api listener: a JSON hello answers with the protocol", hello != null && hello.Contains("\"protocol\"") && hello.Contains("\"workspace\""), hello ?? "(no reply)");
                    Send("{\"cmd\":\"api.ping\",\"echo\":\"x\"}");
                    var pong = ReadLine();
                    check("api listener: an api verb answers", pong != null && pong.Contains("\"pong\":true"), pong ?? "(no reply)");
                    check("api listener: the client counted", mloBridge.Clients == 1 && mloBridge.Received >= 3, $"{mloBridge.Clients} clients, {mloBridge.Received} messages");
                }
                StopMloBridge(ui);
                check("bridge: stopped", !ui.BridgeEnabled && !(mloBridge?.Listening ?? false), ui.BridgeStatus);

                {
                    var bow = new[] { new Vector3(-0.6f, 0, 0), new Vector3(0.6f, 0, 0), new Vector3(-0.6f, 0, 2.2f), new Vector3(0.6f, 0, 2.2f) };
                    var loop = MloBridge.OrderQuadLoop(bow);
                    bool isLoop = true;
                    for (int i = 0; i < 4; i++)
                    {
                        var a = loop[i]; var b = loop[(i + 1) % 4];
                        isLoop &= Math.Abs(a.X - b.X) < 1e-5f || Math.Abs(a.Z - b.Z) < 1e-5f;
                    }
                    check("bridge: four verts in index order become a loop", isLoop && loop[0] == bow[0], string.Join(" ", loop.Select(v => $"({v.X},{v.Z})")));
                }

                ui.SelectRoom(Math.Min(1, s.Rooms.Count - 1));
                ui.SetStatus("--seqtest: snap, corners, room-from-vertices and the bridge checked.");
                if (screenshotPath == null) panel.SwitchWorkspace(was);
            }
            catch (Exception ex)
            {
                check("mlospace: no exception", false, ex.ToString());
                try { StopMloBridge(ui); } catch { }
            }
        }
    }
}

