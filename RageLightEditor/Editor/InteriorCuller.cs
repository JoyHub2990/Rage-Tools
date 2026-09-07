using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed partial class InteriorCuller
    {
        public bool Enabled = true;
        public static readonly bool Debug = Environment.GetEnvironmentVariable("RLE_INTCULLDBG") == "1";
        public YmapEntityDef Inside { get; private set; }
        public MloArchetype InsideArch { get; private set; }
        public int Room { get; private set; } = -1;
        public string RoomName { get; private set; } = "";
        public int ExteriorPortals { get; private set; }
        public int RoomsVisible => visibleRooms.Count;
        public int ExteriorRegions => exteriorRegions.Count;
        public int Tested, Hidden, HiddenRooms, OwnTested;
        public float RoomMargin = 0.25f;
        public float PortalPad = 0.25f;
        public int MaxDepth = 5, MaxRegions = 96;
        public bool CullRooms = true;

        private struct Portal
        {
            public Vector3[] Corners;
            public Vector3 Centre, Normal;
            public int From, To;
        }
        private readonly List<Portal> portals = new List<Portal>();
        private readonly List<BoundingBox> rooms = new List<BoundingBox>();
        private Vector3 mloPos; private Quaternion mloInv = Quaternion.Identity;
        private readonly HashSet<int> visibleRooms = new HashSet<int>();
        private readonly List<Plane[]> exteriorRegions = new List<Plane[]>();
        private struct Cone { public Vector3 Apex, Axis; public float CosH, SinH; public bool Valid; }
        private readonly List<Cone> exteriorCones = new List<Cone>();
        private readonly Dictionary<YmapEntityDef, int> roomOf = new Dictionary<YmapEntityDef, int>();
        private YmapEntityDef roomOfFor; private int roomOfCount = -1;

        public void Update(Vector3 eye, Vector3 forward, Matrix viewProj, IReadOnlyList<YmapEntityDef> interiors, IReadOnlyList<Plane> mirrorPlanes)
        {
            Tested = 0; Hidden = 0; HiddenRooms = 0; OwnTested = 0; ticks = 0; lastEntity = null;
            var wasInside = Inside;
            Inside = null; InsideArch = null; Room = -1; RoomName = ""; ExteriorPortals = 0;
            portals.Clear(); rooms.Clear(); visibleRooms.Clear(); exteriorRegions.Clear(); exteriorCones.Clear();
            if (!Enabled || interiors == null) return;

            DecideRoom_L1(eye, interiors, wasInside);
            if (Inside == null) return;
            EnsureRoomMap();

            visibleRooms.Add(Room);
            var frustum = FrustumPlanes(viewProj, eye + forward * 2.0f);
            var queue = new Queue<(int room, Plane[] planes, int depth)>();
            queue.Enqueue((Room, frustum, 0));
            foreach (var r in seedRooms) if (r != Room && r > 0) { visibleRooms.Add(r); queue.Enqueue((r, frustum, 0)); }
            int regions = 0;
            var opened = new Dictionary<int, int>();
            var extByPortal = new Dictionary<int, int>();
            while (queue.Count > 0 && regions < MaxRegions)
            {
                var (r, planes, depth) = queue.Dequeue();
                for (int pi = 0; pi < portals.Count; pi++)
                {
                    var p = portals[pi];
                    int other;
                    if (p.From == r) other = p.To; else if (p.To == r) other = p.From; else continue;
                    if (other == r) continue;
                    int seen = PortalSeen(p, planes, eye);
                    if (seen == 0) continue;
                    var child = seen == 2 ? planes : Narrow(planes, eye, p);
                    regions++;
                    if (other == 0)
                    {
                        var cone = seen == 2 ? default : ConeOf(eye, p);
                        if (extByPortal.TryGetValue(pi, out int ei)) { if (child.Length < exteriorRegions[ei].Length) { exteriorRegions[ei] = child; exteriorCones[ei] = cone; } }
                        else { extByPortal[pi] = exteriorRegions.Count; exteriorRegions.Add(child); exteriorCones.Add(cone); }
                        continue;
                    }
                    visibleRooms.Add(other);
                    opened.TryGetValue(other, out int times);
                    if (depth + 1 < MaxDepth && times < 3)
                    {
                        opened[other] = times + 1;
                        queue.Enqueue((other, child, depth + 1));
                    }
                }
            }

            AfterTraversal_N1(eye, forward, viewProj, extByPortal.Count);

            if (mirrorPlanes != null)
                for (int i = 0; i < mirrorPlanes.Count && i < 2; i++)
                {
                    var mp = mirrorPlanes[i];
                    float d = Vector3.Dot(mp.Normal, eye) + mp.D;
                    var reye = eye - 2.0f * d * mp.Normal;
                    foreach (var p in portals)
                        if (p.From == 0 || p.To == 0) { exteriorRegions.Add(Pyramid(reye, p)); exteriorCones.Add(ConeOf(reye, p)); }
                }
        }

        private static bool RoomBox(MCMloRoomDef room, out BoundingBox box)
        {
            box = default;
            if (room == null) return false;
            var cmn = room.BBMin_CW; var cmx = room.BBMax_CW;
            bool cw = cmx.X > cmn.X && cmx.Y > cmn.Y && cmx.Z > cmn.Z;
            var fmn = Vector3.Min(room._Data.bbMin, room._Data.bbMax); var fmx = Vector3.Max(room._Data.bbMin, room._Data.bbMax);
            bool file = fmx.X > fmn.X + 0.2f && fmx.Y > fmn.Y + 0.2f && fmx.Z > fmn.Z + 0.2f;
            if (file && cw)
            {
                const float tol = 1.5f;
                bool within = fmn.X >= cmn.X - tol && fmn.Y >= cmn.Y - tol && fmn.Z >= cmn.Z - tol &&
                              fmx.X <= cmx.X + tol && fmx.Y <= cmx.Y + tol && fmx.Z <= cmx.Z + tol;
                if (within) { box = new BoundingBox(fmn, fmx); return true; }
            }
            if (cw) { box = new BoundingBox(cmn, cmx); return true; }
            return false;
        }

        private void EnsureRoomMap()
        {
            var inst = Inside.MloInstance;
            int count = (inst?.Entities?.Length ?? 0) + (inst?.EntitySets?.Length ?? 0);
            if (ReferenceEquals(roomOfFor, Inside) && roomOfCount == count) return;
            roomOf.Clear();
            roomOfFor = Inside; roomOfCount = count;
            if (inst == null) return;
            var arch = InsideArch;
            var ents = inst.Entities;
            if (ents != null && arch.rooms != null)
                for (int r = 0; r < arch.rooms.Length; r++)
                {
                    var att = arch.rooms[r]?.AttachedObjects;
                    if (att == null) continue;
                    foreach (var idx in att) if (idx < ents.Length && ents[idx] != null) roomOf[ents[idx]] = r;
                }
            if (ents != null && arch.portals != null)
                foreach (var p in arch.portals)
                {
                    var att = p?.AttachedObjects;
                    if (att == null) continue;
                    foreach (var idx in att) if (idx < ents.Length && ents[idx] != null) roomOf[ents[idx]] = -1;
                }
            var sets = inst.EntitySets;
            if (sets != null)
                foreach (var set in sets)
                {
                    if (set?.Entities == null) continue;
                    var loc = set.Locations;
                    for (int j = 0; j < set.Entities.Count; j++)
                    {
                        var e = set.Entities[j];
                        if (e == null) continue;
                        roomOf[e] = (loc != null && j < loc.Length) ? (int)loc[j] : -1;
                    }
                }
        }

        private static Plane[] FrustumPlanes(Matrix viewProj, Vector3 insidePoint)
        {
            var f = new BoundingFrustum(viewProj);
            var planes = new[] { f.Near, f.Far, f.Left, f.Right, f.Top, f.Bottom };
            for (int i = 0; i < planes.Length; i++)
            {
                var p = planes[i];
                float len = p.Normal.Length();
                if (len < 1e-8f) { planes[i] = new Plane(Vector3.Zero, 0); continue; }
                p = new Plane(p.Normal / len, p.D / len);
                if (Vector3.Dot(p.Normal, insidePoint) + p.D < 0) p = new Plane(-p.Normal, -p.D);
                planes[i] = p;
            }
            return planes;
        }

        private static int PortalSeen(in Portal p, Plane[] planes, Vector3 eye)
        {
            var n = Vector3.Cross(p.Corners[1] - p.Corners[0], p.Corners[2] - p.Corners[0]);
            if (n.LengthSquared() > 1e-12f)
            {
                n.Normalize();
                var rel = eye - p.Centre;
                float off = Vector3.Dot(n, rel);
                if (Math.Abs(off) < 0.75f)
                {
                    var foot = rel - n * off;
                    float ext = 0.0f;
                    foreach (var c in p.Corners) ext = Math.Max(ext, (c - p.Centre).Length());
                    if (foot.Length() < ext + 0.5f) return 2;
                }
            }
            foreach (var pl in planes)
            {
                if (pl.Normal == Vector3.Zero) continue;
                bool allOut = true;
                foreach (var c in p.Corners) if (Vector3.Dot(pl.Normal, c) + pl.D >= 0) { allOut = false; break; }
                if (allOut) return 0;
            }
            return 1;
        }

        private static Plane[] Narrow(Plane[] parent, Vector3 eye, in Portal p)
        {
            var pyr = Pyramid(eye, p);
            const int keep = 6 + 5;
            int take = Math.Min(parent.Length, keep);
            var all = new Plane[take + pyr.Length];
            if (parent.Length <= keep) parent.CopyTo(all, 0);
            else { Array.Copy(parent, 0, all, 0, 6); Array.Copy(parent, parent.Length - 5, all, 6, 5); }
            pyr.CopyTo(all, take);
            return all;
        }

        private static Plane[] Pyramid(Vector3 eye, in Portal p)
        {
            int n = p.Corners.Length;
            var planes = new Plane[n + 1];
            var toCentre = p.Centre - eye;
            for (int i = 0; i < n; i++)
            {
                var a = p.Corners[i]; var b = p.Corners[(i + 1) % n];
                var nrm = Vector3.Cross(a - eye, b - eye);
                if (nrm.LengthSquared() < 1e-12f) { planes[i] = new Plane(Vector3.Zero, 0); continue; }
                nrm.Normalize();
                if (Vector3.Dot(nrm, toCentre) < 0) nrm = -nrm;
                planes[i] = new Plane(nrm, -Vector3.Dot(nrm, eye));
            }
            var pn = Vector3.Cross(p.Corners[1] - p.Corners[0], p.Corners[2] - p.Corners[0]);
            if (pn.LengthSquared() > 1e-12f)
            {
                pn.Normalize();
                if (Vector3.Dot(pn, toCentre) < 0) pn = -pn;
                planes[n] = new Plane(pn, -Vector3.Dot(pn, p.Centre));
            }
            else planes[n] = new Plane(Vector3.Zero, 0);
            return planes;
        }

        private static Cone ConeOf(Vector3 eye, in Portal p)
        {
            var axis = p.Centre - eye;
            if (axis.LengthSquared() < 1e-6f) return default;
            axis.Normalize();
            float minCos = 1.0f;
            foreach (var c in p.Corners)
            {
                var d = c - eye;
                float len = d.Length();
                if (len < 1e-4f) return default;
                minCos = Math.Min(minCos, Vector3.Dot(d, axis) / len);
            }
            if (minCos <= 0.0f) return default;
            float sinH = (float)Math.Sqrt(Math.Max(0.0f, 1.0f - minCos * minCos));
            return new Cone { Apex = eye, Axis = axis, CosH = minCos, SinH = sinH, Valid = true };
        }

        private static bool InRegion(Plane[] planes, ref BoundingSphere s)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                var pl = planes[i];
                if (pl.Normal == Vector3.Zero) continue;
                if (Vector3.Dot(pl.Normal, s.Center) + pl.D < -s.Radius) return false;
            }
            return true;
        }

        public bool IsHidden(YmapEntityDef e, ref BoundingSphere sphere)
        {
            if (Inside == null || e == null) return false;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool hidden;
            if (ReferenceEquals(e, lastEntity)) hidden = lastEntityHidden || IsHiddenCore(e, ref sphere);
            else
            {
                lastEntity = e;
                var es = new BoundingSphere(e.Position + e.BSCenter, (e.BSRadius > 0 ? e.BSRadius : sphere.Radius) * 1.1f + 0.5f);
                lastEntityHidden = IsHiddenCore(e, ref es);
                hidden = lastEntityHidden || IsHiddenCore(e, ref sphere);
            }
            ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            return hidden;
        }
        private YmapEntityDef lastEntity; private bool lastEntityHidden;
        private long ticks;
        public double LastMs => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        private bool IsHiddenCore(YmapEntityDef e, ref BoundingSphere sphere)
        {
            Tested++;
            if (ReferenceEquals(e, Inside)) return false;
            if (ReferenceEquals(e.MloParent, Inside))
            {
                OwnTested++;
                if (!CullRooms) return false;
                if (!roomOf.TryGetValue(e, out int r) || r <= 0) return false;
                if (visibleRooms.Contains(r)) return false;
                Hidden++; HiddenRooms++;
                return true;
            }
            var lc = mloInv.Multiply(sphere.Center - mloPos);
            const float slack = 1.5f;
            for (int i = 0; i < rooms.Count; i++)
            {
                var b = rooms[i];
                if (lc.X + slack >= b.Minimum.X && lc.X - slack <= b.Maximum.X &&
                    lc.Y + slack >= b.Minimum.Y && lc.Y - slack <= b.Maximum.Y &&
                    lc.Z + slack >= b.Minimum.Z && lc.Z - slack <= b.Maximum.Z) return false;
            }
            if (ExteriorOpen) return false;
            for (int k = 0; k < exteriorRegions.Count; k++)
            {
                var cone = exteriorCones[k];
                if (cone.Valid)
                {
                    var v = sphere.Center - cone.Apex;
                    float a = Vector3.Dot(v, cone.Axis);
                    float b2 = v.LengthSquared() - a * a;
                    float b = b2 > 0 ? (float)Math.Sqrt(b2) : 0.0f;
                    if (b * cone.CosH - a * cone.SinH > sphere.Radius) continue;
                }
                if (InRegion(exteriorRegions[k], ref sphere)) return false;
            }
            Hidden++;
            return true;
        }

        public override string ToString() =>
            Inside == null ? (InteriorsRefused > 0 ? $"outside ({InteriorsRefused} interior(s) refused by the sanity check)" : "outside") :
            $"inside {Inside.Archetype?.Name} room {Room} '{RoomName}' rooms {visibleRooms.Count}/{Math.Max((InsideArch?.rooms?.Length ?? 1) - 1, 0)} seen, portals {portals.Count} ({ExteriorPortals} out, {exteriorRegions.Count} open) hidden {Hidden}/{Tested} ({HiddenRooms} of the interior's own {OwnTested}, {roomOf.Count} placed in rooms) {LastMs:0.00} ms {ExteriorNote_N1()}";
    }
}

