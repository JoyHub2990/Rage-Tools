using System;
using System.Collections.Generic;
using System.Linq;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorSession
    {
        public bool BBoxManual;

        public BoundingBox ShellBounds;
        public bool ShellBoundsKnown;
        public string ShellBoundsSource = "";
        private int shellSyncVersion = -1;
        private LoadedFile shellSyncFile;
        private bool shellSyncHadModel;

        public bool TryGetShellBounds(Scene scene, out BoundingBox bounds, out string source)
        {
            source = "";
            bounds = default;
            if (ShellFile?.Model != null)
            {
                var b = ModelBounds(ShellFile);
                if (b.Maximum.X > b.Minimum.X) { bounds = b; source = "shell " + ShellFile.Name; return true; }
            }
            if (scene?.MloModel != null)
            {
                var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); bool any = false;
                foreach (var m in scene.MloModel.Meshes)
                {
                    if (m == null || !m.IsMloShell) continue;
                    var b = m.WorldBounds;
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    min = Vector3.Min(min, b.Minimum); max = Vector3.Max(max, b.Maximum); any = true;
                }
                if (any) { bounds = new BoundingBox(min, max); source = "imported shell"; return true; }
            }
            if (SourceArchetype != null)
            {
                var bd = SourceArchetype._BaseArchetypeDef;
                if (bd.bbMax.X > bd.bbMin.X && bd.bbMax.Y > bd.bbMin.Y && bd.bbMax.Z > bd.bbMin.Z)
                {
                    bounds = new BoundingBox(bd.bbMin, bd.bbMax); source = "archetype box (no shell mesh loaded)"; return true;
                }
            }
            if (TryGetRoomUnionBounds_P2(out var roomUnion)) { bounds = roomUnion; source = "the authored rooms"; return true; }
            if (scene?.MloModel != null)
            {
                var sb = scene.GetSceneBounds();
                if (sb.HasValue && sb.Value.Maximum.X > sb.Value.Minimum.X) { bounds = sb.Value; source = "everything placed (no shell drawable)"; return true; }
            }
            return false;
        }

        public bool SyncToShell(Scene scene)
        {
            int ver = scene?.GeometryVersion ?? -1;
            bool hasModel = scene?.HasModel ?? false;
            if (ver != shellSyncVersion || !ReferenceEquals(ShellFile, shellSyncFile) || hasModel != shellSyncHadModel || !ShellBoundsKnown)
            {
                shellSyncVersion = ver; shellSyncFile = ShellFile; shellSyncHadModel = hasModel;
                ShellBoundsKnown = TryGetShellBounds(scene, out ShellBounds, out ShellBoundsSource);
            }
            if (!ShellBoundsKnown) return false;
            if (Rooms.Count > 0 && !LimboAuthored_P2) { Rooms[0].Min = ShellBounds.Minimum; Rooms[0].Max = ShellBounds.Maximum; }
            if (!BBoxManual) { BBMin = ShellBounds.Minimum; BBMax = ShellBounds.Maximum; }
            return true;
        }

        public void InvalidateShellBounds() { shellSyncVersion = -1; ShellBoundsKnown = false; }

        public bool RoomBoundsFromShell(MloCreatorRoom room, Scene scene, out BoundingBox bounds, out string how)
        {
            how = ""; bounds = default;
            if (room != null && scene != null && !string.IsNullOrWhiteSpace(room.Name))
            {
                var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); int n = 0;
                foreach (var f in scene.Files)
                {
                    if (f?.Model == null || f.FromMlo) continue;
                    var stem = System.IO.Path.GetFileNameWithoutExtension(f.Name ?? "");
                    if (stem.IndexOf(room.Name.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var b = ModelBounds(f);
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    min = Vector3.Min(min, b.Minimum); max = Vector3.Max(max, b.Maximum); n++;
                }
                if (n > 0) { bounds = new BoundingBox(min, max); how = $"{n} file{(n == 1 ? "" : "s")} named like '{room.Name}'"; return true; }
            }
            if (TryGetShellBounds(scene, out bounds, out var src)) { how = "the whole shell (" + src + ")"; return true; }
            return false;
        }

        public MloCreatorRoom LastSnapRoom;
        public bool LastSnapWasMax;
        public Vector3 LastSnapPoint;

        public const float MinRoomExtent = 0.05f;

        public void SnapRoomCorner(MloCreatorRoom room, bool max, Vector3 p)
        {
            if (room == null) return;
            if (ReferenceEquals(LastSnapRoom, room) && LastSnapWasMax != max)
            {
                if (BoxFromPoints(new[] { LastSnapPoint, p }, out var mn, out var mx, out _)) { room.Min = mn; room.Max = mx; }
                LastSnapRoom = null;
            }
            else
            {
                if (max) room.Max = p; else room.Min = p;
                NormaliseRoom(room);
                LastSnapRoom = room; LastSnapWasMax = max; LastSnapPoint = p;
            }
            AutoAssignRooms();
        }

        public static void NormaliseRoom(MloCreatorRoom room)
        {
            var mn = Vector3.Min(room.Min, room.Max);
            var mx = Vector3.Max(room.Min, room.Max);
            if (mx.X - mn.X < MinRoomExtent) mx.X = mn.X + MinRoomExtent;
            if (mx.Y - mn.Y < MinRoomExtent) mx.Y = mn.Y + MinRoomExtent;
            if (mx.Z - mn.Z < MinRoomExtent) mx.Z = mn.Z + MinRoomExtent;
            room.Min = mn; room.Max = mx;
        }

        public static bool BoundsOfMesh(RenderMesh mesh, out BoundingBox bounds, float pad = 0.01f)
        {
            bounds = default;
            if (mesh == null) return false;
            var b = mesh.WorldBounds;
            if (b.Maximum.X <= b.Minimum.X) return false;
            bounds = new BoundingBox(b.Minimum - new Vector3(pad), b.Maximum + new Vector3(pad));
            return true;
        }
    }
}

