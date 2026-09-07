using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed partial class InteriorCuller
    {
        private static readonly bool OldCull_N1 = Environment.GetEnvironmentVariable("RLE_OLDINTCULL") == "1";
        private static readonly bool NoExtFallback_N1 = OldCull_N1 || Environment.GetEnvironmentVariable("RLE_NOEXTFALLBACK") == "1";
        private static readonly bool NoRoomView_N1 = OldCull_N1 || Environment.GetEnvironmentVariable("RLE_NOROOMVIEW") == "1";
        private static readonly bool NoWindows_N1 = OldCull_N1 || Environment.GetEnvironmentVariable("RLE_NOWINDOWS") == "1";

        public bool ExteriorOpen { get; private set; }
        public string ExteriorWhy { get; private set; } = "";
        public int RoomsByView { get; private set; }
        public int RoomsByNear { get; private set; }
        public float RoomNearReach = 20.0f;
        public int WindowRegions { get; private set; }
        public int MaxWindowRegions = 12;
        public float WindowReach = 3.0f;

        public string ExteriorNote_N1() =>
            Inside == null ? "" : $"| exterior {(ExteriorOpen ? "OPEN (not culled)" : "through " + exteriorRegions.Count + " region(s)")} {ExteriorWhy}, rooms by view {RoomsByView} + near {RoomsByNear}, window regions {WindowRegions}";

        internal void AfterTraversal_N1(Vector3 eye, Vector3 forward, Matrix viewProj, int extFromView)
        {
            ExteriorOpen = false; ExteriorWhy = ""; RoomsByView = 0; RoomsByNear = 0; WindowRegions = 0;
            if (Inside == null || InsideArch == null) return;
            var frustum = FrustumPlanes(viewProj, eye + forward * 2.0f);

            if (!NoRoomView_N1) OpenRoomsInView_N1(eye, frustum);

            int opened = NoWindows_N1 ? 0 : OpenWindows_N1(eye, frustum);
            WindowRegions = opened;

            if (NoExtFallback_N1) { ExteriorWhy = "(fallback off by RLE_NOEXTFALLBACK)"; return; }
            var thin = ThinPortalData_N1(InsideArch, out bool sealedIn);
            if (thin != null) { ExteriorOpen = true; ExteriorWhy = "fallback: " + thin; return; }
            if (sealedIn) { ExteriorWhy = "sealed interior: no way out but mirrors - the game shows nothing of the world here either"; return; }
            if (extFromView + opened == 0)
            {
                ExteriorOpen = true;
                ExteriorWhy = $"fallback: no exterior portal in view ({ExteriorPortals} in this interior) - the eye may be at glass that is not a portal";
            }
        }

        private void OpenRoomsInView_N1(Vector3 eye, Plane[] frustum)
        {
            var arch = InsideArch;
            if (arch?.rooms == null) return;
            var ori = Inside.Orientation; var pos = Inside.Position;
            var eyeLocal = mloInv.Multiply(eye - mloPos);
            for (int r = 1; r < arch.rooms.Length; r++)
            {
                if (visibleRooms.Contains(r)) continue;
                if (!RoomBox(arch.rooms[r], out var box)) continue;
                var near = Vector3.Clamp(eyeLocal, box.Minimum, box.Maximum);
                if ((near - eyeLocal).LengthSquared() <= RoomNearReach * RoomNearReach)
                {
                    visibleRooms.Add(r); RoomsByNear++;
                    continue;
                }
                var mn = box.Minimum - new Vector3(0.5f); var mx = box.Maximum + new Vector3(0.5f);
                bool inView = true;
                for (int p = 0; p < frustum.Length && inView; p++)
                {
                    var pl = frustum[p];
                    if (pl.Normal == Vector3.Zero) continue;
                    bool anyIn = false;
                    for (int c = 0; c < 8 && !anyIn; c++)
                    {
                        var local = new Vector3((c & 1) == 0 ? mn.X : mx.X, (c & 2) == 0 ? mn.Y : mx.Y, (c & 4) == 0 ? mn.Z : mx.Z);
                        var w = pos + ori.Multiply(local);
                        if (Vector3.Dot(pl.Normal, w) + pl.D >= 0) anyIn = true;
                    }
                    if (!anyIn) inView = false;
                }
                if (!inView) continue;
                visibleRooms.Add(r);
                RoomsByView++;
            }
        }

        private int OpenWindows_N1(Vector3 eye, Plane[] frustum)
        {
            var arch = InsideArch;
            if (arch?.portals == null) return 0;
            var ori = Inside.Orientation; var pos = Inside.Position;
            int added = 0;
            foreach (var pd in arch.portals)
            {
                if (added >= MaxWindowRegions) break;
                if (pd == null) continue;
                var cs = pd.Corners;
                if (cs == null || cs.Length < 3) continue;
                int from = (int)pd._Data.roomFrom, to = (int)pd._Data.roomTo;
                if ((from == 0) == (to == 0)) continue;
                int inner = from != 0 ? from : to;
                if (!visibleRooms.Contains(inner)) continue;
                var centreL = Vector3.Zero;
                for (int i = 0; i < cs.Length; i++) centreL += cs[i].XYZ();
                centreL /= cs.Length;
                var p = new Portal { Corners = new Vector3[cs.Length], From = from, To = to };
                for (int i = 0; i < cs.Length; i++)
                {
                    var c = cs[i].XYZ();
                    var d = c - centreL;
                    float len = d.Length();
                    if (len > 1e-4f) c += d * (PortalPad / len);
                    p.Corners[i] = pos + ori.Multiply(c);
                }
                p.Centre = pos + ori.Multiply(centreL);
                var n = Vector3.Cross(p.Corners[1] - p.Corners[0], p.Corners[2] - p.Corners[0]);
                if (n.LengthSquared() < 1e-10f) continue;
                int seen = PortalSeen(p, frustum, eye);
                bool close = Vector3.DistanceSquared(p.Centre, eye) < WindowReach * WindowReach;
                if (seen == 0 && !close) continue;
                var region = (seen == 2 || close) ? frustum : Narrow(frustum, eye, p);
                exteriorRegions.Add(region);
                exteriorCones.Add(seen == 2 || close ? default : ConeOf(eye, p));
                added++;
            }
            return added;
        }

        private string ThinPortalData_N1(MloArchetype arch, out bool sealedIn)
        {
            if (extThin.TryGetValue(arch, out var s)) { sealedIn = s.sealedIn; return s.why; }
            int rooms = Math.Max((arch.rooms?.Length ?? 1) - 1, 0);
            int exterior = 0, real = 0;
            var touched = new HashSet<int>();
            if (arch.portals != null)
                foreach (var pd in arch.portals)
                {
                    if (pd == null || pd.Corners == null || pd.Corners.Length < 3) continue;
                    int from = (int)pd._Data.roomFrom, to = (int)pd._Data.roomTo;
                    if ((from == 0) != (to == 0))
                    {
                        exterior++;
                        if ((pd._Data.flags & MirrorPortalFlag_N1) == 0) real++;
                    }
                    if (from > 0) touched.Add(from);
                    if (to > 0) touched.Add(to);
                }
            int withPortal = touched.Count;
            string why = null;
            if (rooms > 1 && withPortal * 2 < rooms) why = $"only {withPortal} of {rooms} rooms have a portal at all - the walk cannot leave the room the eye is in";
            bool sealedNow = why == null && real == 0;
            extThin[arch] = (why, sealedNow);
            sealedIn = sealedNow;
            Console.WriteLine($"INTCULL exterior {arch.Name}: rooms {rooms} withPortal {withPortal} exteriorPortals {exterior} (real ways out {real}, the rest mirrors) -> " +
                              (why != null ? "exterior culling OFF (" + why + ")" : sealedNow ? "SEALED interior, exterior culling on and no fallback" : "exterior culling on"));
            return why;
        }
        private const uint MirrorPortalFlag_N1 = 4u;
        private readonly Dictionary<MloArchetype, (string why, bool sealedIn)> extThin = new Dictionary<MloArchetype, (string, bool)>();

        public bool IsRoomOpen_N1(int room) => room <= 0 || visibleRooms.Contains(room);
        public bool IsInside_N1(YmapEntityDef shell) => shell != null && ReferenceEquals(shell, Inside);
    }
}

