using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed partial class InteriorCuller
    {
        public int ConfirmFrames = 3;
        public float StayMargin = 1.0f;
        public float DoorReach = 6.0f;

        private readonly List<int> seedRooms = new List<int>();
        private YmapEntityDef confirmedInside;
        private int confirmedRoom = -1;
        private int pendingRoom = -1, pendingFrames;
        private BoundingBox confirmedBox;
        public int RoomSwitches { get; private set; }
        public int InsideFlips { get; private set; }

        private sealed class Sanity { public bool Ok; public string Why; public int Bad, Exterior; public float Coverage; public bool ShellFromAuthored; }
        private readonly Dictionary<MloArchetype, Sanity> sanity = new Dictionary<MloArchetype, Sanity>();
        public int InteriorsRefused { get; private set; }
        public bool IsRefused(MloArchetype arch) => arch != null && sanity.TryGetValue(arch, out var s) && !s.Ok;

        private void DecideRoom_L1(Vector3 eye, IReadOnlyList<YmapEntityDef> interiors, YmapEntityDef wasInside)
        {
            seedRooms.Clear();
            bool stillEmitted = false;
            if (wasInside != null) for (int i = 0; i < interiors.Count; i++) if (ReferenceEquals(interiors[i], wasInside)) { stillEmitted = true; break; }
            if (!stillEmitted && wasInside != null) { confirmedInside = null; confirmedRoom = -1; pendingRoom = -1; pendingFrames = 0; }
            if (stillEmitted && TryEnter(wasInside, eye)) { }
            else
            {
                for (int i = 0; i < interiors.Count && Inside == null; i++)
                {
                    var ent = interiors[i];
                    if (ent == null || ReferenceEquals(ent, wasInside)) continue;
                    if (Vector3.DistanceSquared(ent.Position, eye) > (ent.BSRadius + 5.0f) * (ent.BSRadius + 5.0f)) continue;
                    TryEnter(ent, eye);
                }
            }
            if (Inside == null)
            {
                if (confirmedInside != null && stillEmitted && ReferenceEquals(confirmedInside, wasInside))
                {
                    var arch = confirmedInside.Archetype as MloArchetype;
                    bool near = arch != null && confirmedRoom > 0 && confirmedRoom < (arch.rooms?.Length ?? 0) && InBox(confirmedInside, eye, confirmedBox, StayMargin);
                    if (near && TryEnter(confirmedInside, eye, keepRoom: true)) return;
                }
                if (wasInside != null) InsideFlips++;
                confirmedInside = null; confirmedRoom = -1; pendingRoom = -1; pendingFrames = 0;
                return;
            }
            if (wasInside == null) InsideFlips++;
        }

        private static bool InBox(YmapEntityDef ent, Vector3 eye, BoundingBox box, float grow)
        {
            var inv = Quaternion.Normalize(Quaternion.Invert(ent.Orientation));
            var local = inv.Multiply(eye - ent.Position);
            return local.X >= box.Minimum.X - grow && local.X <= box.Maximum.X + grow &&
                   local.Y >= box.Minimum.Y - grow && local.Y <= box.Maximum.Y + grow &&
                   local.Z >= box.Minimum.Z - grow && local.Z <= box.Maximum.Z + grow;
        }

        private bool TryEnter(YmapEntityDef ent, Vector3 eye, bool keepRoom = false)
        {
            var arch = ent.Archetype as MloArchetype;
            if (arch?.rooms == null || arch.rooms.Length < 2) return false;
            var san = SanityOf(ent, arch);
            if (!san.Ok) return false;
            var inv = Quaternion.Normalize(Quaternion.Invert(ent.Orientation));
            var local = inv.Multiply(eye - ent.Position);
            int hit = -1; float hitVol = float.MaxValue;
            var hits = hitScratch; hits.Clear();
            for (int r = 1; r < arch.rooms.Length; r++)
            {
                if (!RoomBox(arch.rooms[r], out var box)) continue;
                var mn = box.Minimum + new Vector3(RoomMargin);
                var mx = box.Maximum - new Vector3(RoomMargin);
                if (local.X >= mn.X && local.X <= mx.X && local.Y >= mn.Y && local.Y <= mx.Y && local.Z >= mn.Z && local.Z <= mx.Z)
                {
                    hits.Add(r);
                    var sz = box.Maximum - box.Minimum;
                    float vol = sz.X * sz.Y * sz.Z;
                    if (vol < hitVol) { hitVol = vol; hit = r; }
                }
            }
            bool sameInterior = ReferenceEquals(ent, confirmedInside);
            if (hit < 0 && keepRoom && sameInterior && confirmedRoom > 0) { hit = confirmedRoom; hits.Add(confirmedRoom); }
            if (Debug)
            {
                Console.WriteLine($"INTCULLDBG {arch.Name} rooms {arch.rooms.Length} eyeLocal {local} hit {hit} hits [{string.Join(",", hits)}] confirmed {confirmedRoom} pending {pendingRoom}x{pendingFrames} mloPos {ent.Position}");
                if (!dbgRoomsShown.Contains(arch))
                {
                    dbgRoomsShown.Add(arch);
                    Console.WriteLine($"INTCULLDBG   arch bb {arch.BBMin}..{arch.BBMax} ents {ent.MloInstance?.Entities?.Length ?? -1}");
                    for (int r = 1; r < arch.rooms.Length; r++)
                    {
                        var rm = arch.rooms[r]; if (rm == null) continue;
                        Console.WriteLine($"INTCULLDBG   room {r} '{rm.RoomName}' R* {rm._Data.bbMin}..{rm._Data.bbMax} CW {rm.BBMin_CW}..{rm.BBMax_CW} attached {rm.AttachedObjects?.Length ?? 0}");
                    }
                }
            }
            if (hit < 0) return false;

            int room = hit;
            if (sameInterior && confirmedRoom > 0 && confirmedRoom != hit && confirmedRoom < arch.rooms.Length)
            {
                bool stillNear = InBox(ent, eye, confirmedBox, StayMargin);
                if (hit == pendingRoom) pendingFrames++; else { pendingRoom = hit; pendingFrames = 1; }
                if (stillNear && pendingFrames < ConfirmFrames) room = confirmedRoom;
            }
            else { pendingRoom = -1; pendingFrames = 0; }
            if (!hits.Contains(room)) hits.Add(room);
            if (sameInterior && confirmedRoom > 0 && !hits.Contains(confirmedRoom) && InBox(ent, eye, confirmedBox, StayMargin)) hits.Add(confirmedRoom);

            var tmpPortals = tmpPortalScratch; tmpPortals.Clear();
            float nearestD2 = float.MaxValue; float nearestSide = -1.0f; int exterior = 0;
            if (arch.portals != null)
            {
                foreach (var pd in arch.portals)
                {
                    if (pd == null) continue;
                    var cs = pd.Corners;
                    if (cs == null || cs.Length < 3) continue;
                    int from = (int)pd._Data.roomFrom, to = (int)pd._Data.roomTo;
                    if (from < 0 || from >= arch.rooms.Length || to < 0 || to >= arch.rooms.Length) continue;
                    var p = new Portal { Corners = new Vector3[cs.Length], From = from, To = to };
                    var centreL = Vector3.Zero;
                    for (int i = 0; i < cs.Length; i++) centreL += cs[i].XYZ();
                    centreL /= cs.Length;
                    for (int i = 0; i < cs.Length; i++)
                    {
                        var c = cs[i].XYZ();
                        var d = c - centreL;
                        float len = d.Length();
                        if (len > 1e-4f) c += d * (PortalPad / len);
                        p.Corners[i] = ent.Position + ent.Orientation.Multiply(c);
                    }
                    p.Centre = ent.Position + ent.Orientation.Multiply(centreL);
                    var n = Vector3.Cross(p.Corners[1] - p.Corners[0], p.Corners[2] - p.Corners[0]);
                    if (n.LengthSquared() < 1e-10f) continue;
                    n.Normalize();
                    bool ext = (from == 0) != (to == 0);
                    if (ext)
                    {
                        exterior++;
                        int inner = from != 0 ? from : to;
                        if (RoomBox(arch.rooms[inner], out var ib))
                        {
                            var rc = ent.Position + ent.Orientation.Multiply((ib.Minimum + ib.Maximum) * 0.5f);
                            if (Vector3.Dot(n, rc - p.Centre) > 0) n = -n;
                        }
                        else n = Vector3.Zero;
                        float d2 = Vector3.DistanceSquared(p.Centre, eye);
                        bool mine = hits.Contains(inner);
                        if (mine && d2 < nearestD2 && d2 < DoorReach * DoorReach && Math.Abs(n.Z) < 0.7f)
                        {
                            var rel = eye - p.Centre;
                            float off = Vector3.Dot(n, rel);
                            var foot = rel - n * off;
                            float ext2 = 0.0f;
                            foreach (var c in p.Corners) ext2 = Math.Max(ext2, (c - p.Centre).Length());
                            if (foot.Length() < ext2 + 1.0f) { nearestD2 = d2; nearestSide = n == Vector3.Zero ? -1.0f : off; }
                        }
                        if (Debug && d2 < 400.0f) Console.WriteLine($"INTCULLDBG   ext portal {from}->{to} d {Math.Sqrt(d2):0.0} side {(n == Vector3.Zero ? 0 : Vector3.Dot(n, eye - p.Centre)):0.00} mine {mine} inner room {inner} '{arch.rooms[inner]?.RoomName}'");
                    }
                    p.Normal = ext ? n : Vector3.Zero;
                    tmpPortals.Add(p);
                }
            }
            if (Debug) Console.WriteLine($"INTCULLDBG   portals {tmpPortals.Count} exterior {exterior} nearestSide {nearestSide:0.00} room {room}");
            if (nearestSide > 0.05f) return false;

            if (!sameInterior || confirmedRoom != room)
            {
                if (sameInterior) RoomSwitches++;
                pendingRoom = -1; pendingFrames = 0;
            }
            Inside = ent; InsideArch = arch; Room = room;
            RoomName = arch.rooms[room]?.RoomName ?? "";
            mloPos = ent.Position; mloInv = inv;
            portals.AddRange(tmpPortals);
            ExteriorPortals = exterior;
            for (int r = 1; r < arch.rooms.Length; r++) if (RoomBox(arch.rooms[r], out var box)) rooms.Add(box);
            seedRooms.AddRange(hits);
            confirmedInside = ent; confirmedRoom = room;
            if (!RoomBox(arch.rooms[room], out confirmedBox)) confirmedBox = new BoundingBox(local - new Vector3(1), local + new Vector3(1));
            return true;
        }
        private readonly List<int> hitScratch = new List<int>();
        private readonly List<Portal> tmpPortalScratch = new List<Portal>();
        private readonly HashSet<MloArchetype> dbgRoomsShown = new HashSet<MloArchetype>();

        private Sanity SanityOf(YmapEntityDef ent, MloArchetype arch)
        {
            if (sanity.TryGetValue(arch, out var s)) return s;
            s = new Sanity { Ok = true, Why = "" };
            int portalsTotal = 0, bad = 0, exterior = 0, boxed = 0;
            if (arch.portals != null)
                foreach (var pd in arch.portals)
                {
                    if (pd == null) continue;
                    portalsTotal++;
                    var cs = pd.Corners;
                    int from = (int)pd._Data.roomFrom, to = (int)pd._Data.roomTo;
                    bool ok = cs != null && cs.Length >= 3 && from >= 0 && from < arch.rooms.Length && to >= 0 && to < arch.rooms.Length;
                    if (ok)
                    {
                        var n = Vector3.Cross(cs[1].XYZ() - cs[0].XYZ(), cs[2].XYZ() - cs[0].XYZ());
                        ok = n.LengthSquared() > 1e-8f;
                    }
                    if (!ok) bad++;
                    else if ((from == 0) != (to == 0)) exterior++;
                }
            float shellArea = Math.Max((arch.BBMax.X - arch.BBMin.X) * (arch.BBMax.Y - arch.BBMin.Y), 0.0f);
            float authoredArea = AuthoredFootprint(arch);
            if (authoredArea > 1.0f && shellArea > authoredArea * BogusShellFactor)
            { s.ShellFromAuthored = true; shellArea = authoredArea; }
            float roomsArea = 0.0f;
            for (int r = 1; r < arch.rooms.Length; r++)
                if (RoomBox(arch.rooms[r], out var b)) { boxed++; roomsArea += Math.Min((b.Maximum.X - b.Minimum.X) * (b.Maximum.Y - b.Minimum.Y), Math.Max(shellArea, 1.0f)); }
            s.Coverage = shellArea > 1.0f ? roomsArea / shellArea : (boxed > 0 ? 1.0f : 0.0f);
            s.Bad = bad; s.Exterior = exterior;
            if (boxed == 0) { s.Ok = false; s.Why = "no room has a usable box"; }
            else if (portalsTotal == 0) { s.Ok = false; s.Why = "no portals at all - nothing could be seen through"; }
            else if (bad > 0 && bad * 3 >= portalsTotal) { s.Ok = false; s.Why = $"{bad} of {portalsTotal} portals have no usable quad or room"; }
            else if (s.Coverage < 0.33f) { s.Ok = false; s.Why = $"the rooms' footprint covers {s.Coverage * 100:0}% of the shell's"; }
            sanity[arch] = s;
            if (!s.Ok) InteriorsRefused++;
            Console.WriteLine($"INTCULL sanity {arch.Name} at {ent.Position.X:0},{ent.Position.Y:0},{ent.Position.Z:0}: rooms {arch.rooms.Length - 1} boxed {boxed} footprint {s.Coverage * 100:0}% (rooms {roomsArea:0} m2 of shell {shellArea:0} m2{(s.ShellFromAuthored ? " from the rooms+portals - the archetype's own box is not believable" : "")}, shell bb {arch.BBMin.X:0},{arch.BBMin.Y:0},{arch.BBMin.Z:0} .. {arch.BBMax.X:0},{arch.BBMax.Y:0},{arch.BBMax.Z:0}) portals {portalsTotal} (exterior {exterior}, bad {bad}) -> culling {(s.Ok ? "on" : "OFF: " + s.Why)}");
            return s;
        }

        private const float BogusShellFactor = 4.0f;

        private static float AuthoredFootprint(MloArchetype arch)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            if (arch.rooms != null)
                for (int r = 1; r < arch.rooms.Length; r++)
                    if (RoomBox(arch.rooms[r], out var b))
                    {
                        any = true;
                        minX = Math.Min(minX, b.Minimum.X); minY = Math.Min(minY, b.Minimum.Y);
                        maxX = Math.Max(maxX, b.Maximum.X); maxY = Math.Max(maxY, b.Maximum.Y);
                    }
            if (arch.portals != null)
                foreach (var pd in arch.portals)
                {
                    var cs = pd?.Corners;
                    if (cs == null) continue;
                    foreach (var c in cs)
                    {
                        any = true;
                        minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
                        maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
                    }
                }
            return any ? Math.Max(maxX - minX, 0.0f) * Math.Max(maxY - minY, 0.0f) : 0.0f;
        }
    }
}

