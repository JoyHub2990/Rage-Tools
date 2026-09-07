using System;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private MCMloRoomDef mloFocusRoomLast;
        private MCMloPortalDef mloFocusPortalLast;
        private bool mloFocusEnvDone;

        private void TickMloFocus_H2()
        {
            if (ProjWin == null) return;
            if (!mloFocusEnvDone && WorldEdit.Selection.MloEntityDef?.Archetype is MloArchetype envMlo)
            {
                var env = Environment.GetEnvironmentVariable("RLE_MLOFOCUS");
                if (!string.IsNullOrEmpty(env))
                {
                    mloFocusEnvDone = true;
                    var parts = env.Split(',');
                    int n = parts.Length > 1 && int.TryParse(parts[1], out var pn) ? pn : 1;
                    object node = null;
                    if (parts[0].StartsWith("room", StringComparison.OrdinalIgnoreCase) && envMlo.rooms != null && n < envMlo.rooms.Length) node = envMlo.rooms[n];
                    if (parts[0].StartsWith("portal", StringComparison.OrdinalIgnoreCase) && envMlo.portals != null && n < envMlo.portals.Length) node = envMlo.portals[n];
                    if (node != null)
                    {
                        ProjWin.Visible = true; ProjWin.Select(node); ProjWin.Minimized = true;
                        Console.WriteLine($"MLOFOCUS {env} -> {node}");
                    }
                    else Console.WriteLine($"MLOFOCUS {env}: no such node in {envMlo.Name} (rooms {envMlo.rooms?.Length}, portals {envMlo.portals?.Length})");
                }
            }
            var room = ProjWin.Visible ? ProjWin.CurrentRoom : null;
            var portal = ProjWin.Visible ? ProjWin.CurrentPortal : null;
            if (ReferenceEquals(room, mloFocusRoomLast) && ReferenceEquals(portal, mloFocusPortalLast)) return;
            mloFocusRoomLast = room; mloFocusPortalLast = portal;
            var mlo = room?.OwnerMlo ?? portal?.OwnerMlo;
            if (mlo == null)
            {
                if (WorldEdit.Selection.MloEntityDef != null && (WorldEdit.Selection.MloRoomDef != null || WorldEdit.Selection.MloPortalDef != null))
                {
                    var keep = WorldEdit.Selection; keep.MloRoomDef = null; keep.MloPortalDef = null;
                    WorldEdit.Select(keep);
                }
                return;
            }
            var inst = ProjWin.FindMloInstance?.Invoke(mlo);
            var owner = inst?.Owner;
            if (owner == null) return;
            var sel = WorldSelection.FromProjectObject(owner);
            sel.MloEntityDef = owner;
            sel.MloRoomDef = room;
            sel.MloPortalDef = portal;
            sel.AABB = new BoundingBox(new Vector3(-1.5f), new Vector3(1.5f));
            sel.CamRel = owner.Position - camera.Position;
            WorldEdit.Select(sel);
            lastWorldSelForProject = owner;
            WorldEdit.LastStatus = room != null
                ? $"room {room.Index}: {room.RoomName} of {mlo.Name}"
                : $"portal {portal.Index} of {mlo.Name} (room {portal._Data.roomFrom} -> {portal._Data.roomTo})";
        }

        private static Vector4 Faint(Vector4 c, float alpha = 0.18f) => new Vector4(c.X, c.Y, c.Z, alpha);

        private static Vector3 RoomCentreWorld(YmapEntityDef mlo, MCMloRoomDef room)
        {
            var c = (room.BBMin_CW + room.BBMax_CW) * 0.5f;
            if (room.BBMax_CW.X <= room.BBMin_CW.X) c = (room._Data.bbMin + room._Data.bbMax) * 0.5f;
            return mlo.Position + mlo.Orientation.Multiply(c);
        }

        private void DrawPortalArrow(YmapEntityDef mlo, MloArchetype mloa, MCMloPortalDef portal, Vector4 col)
        {
            if (portal?.Corners == null || portal.Corners.Length < 3) return;
            var centre = mlo.Position + mlo.Orientation.Multiply(portal.Center);
            var c0 = mlo.Position + mlo.Orientation.Multiply(portal.Corners[0].XYZ());
            var c1 = mlo.Position + mlo.Orientation.Multiply(portal.Corners[1].XYZ());
            var c2 = mlo.Position + mlo.Orientation.Multiply(portal.Corners[2].XYZ());
            var normal = Vector3.Cross(c1 - c0, c2 - c0);
            if (normal.LengthSquared() > 1e-8f) normal.Normalize(); else normal = Vector3.UnitY;
            uint rf = portal._Data.roomFrom, rt = portal._Data.roomTo;
            var rooms = mloa.rooms;
            Vector3? from = RoomPoint(rf), to = RoomPoint(rt);
            float reach = 4.0f;
            if (from == null && to == null) { from = centre - normal * reach; to = centre + normal * reach; }
            else if (from == null) { var side = Vector3.Dot(to.Value - centre, normal) > 0 ? -1.0f : 1.0f; from = centre + normal * reach * side; }
            else if (to == null) { var side = Vector3.Dot(from.Value - centre, normal) > 0 ? -1.0f : 1.0f; to = centre + normal * reach * side; }
            DrawArrowLine(from.Value, centre, col, headAtEnd: false);
            DrawArrowLine(centre, to.Value, col, headAtEnd: true);
            var ax = c1 - c0; var ay = Vector3.Cross(normal, ax);
            if (ax.LengthSquared() > 1e-8f && ay.LengthSquared() > 1e-8f)
                lineRenderer.AddCircle(centre, Vector3.Normalize(ax), Vector3.Normalize(ay), 0.15f, col, 16);
            var fromName = rf < (rooms?.Length ?? 0) ? rooms[rf].RoomName : "?";
            var toName = rt < (rooms?.Length ?? 0) ? rooms[rt].RoomName : "?";
            DrawWorldLabel(from.Value + new Vector3(0, 0, 0.3f), $"from {rf}: {fromName}", RoomLabel);
            DrawWorldLabel(to.Value + new Vector3(0, 0, 0.3f), $"to {rt}: {toName}", RoomLabel);

            Vector3? RoomPoint(uint idx)
            {
                if (idx == 0 || rooms == null || idx >= rooms.Length || rooms[idx] == null) return null;
                var r = rooms[idx];
                if (r.RoomName == "limbo") return null;
                return RoomCentreWorld(mlo, r);
            }
        }

        private void DrawArrowLine(Vector3 a, Vector3 b, Vector4 col, bool headAtEnd)
        {
            var d = b - a; float len = d.Length();
            if (len < 1e-4f) return;
            d /= len;
            lineRenderer.AddLine(a, b, col);
            float head = Math.Min(0.6f, len * 0.3f);
            float rad = head * 0.35f;
            var tip = headAtEnd ? b : a;
            var back = headAtEnd ? b - d * head : a + d * head;
            var up = Math.Abs(d.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            var sx = Vector3.Normalize(Vector3.Cross(d, up)) * rad;
            var sy = Vector3.Normalize(Vector3.Cross(sx, d)) * rad;
            var p0 = back + sx + sy; var p1 = back - sx + sy; var p2 = back - sx - sy; var p3 = back + sx - sy;
            lineRenderer.AddLine(tip, p0, col); lineRenderer.AddLine(tip, p1, col); lineRenderer.AddLine(tip, p2, col); lineRenderer.AddLine(tip, p3, col);
            lineRenderer.AddLine(p0, p1, col); lineRenderer.AddLine(p1, p2, col); lineRenderer.AddLine(p2, p3, col); lineRenderer.AddLine(p3, p0, col);
            var fc = new Vector4(col.X, col.Y, col.Z, col.W * 0.35f);
            triRenderer.AddTri(tip, p0, p1, fc); triRenderer.AddTri(tip, p1, p2, fc); triRenderer.AddTri(tip, p2, p3, fc); triRenderer.AddTri(tip, p3, p0, fc);
        }
    }
}

