using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class MloEditor
    {
        private const int MetaArrayLimit = ushort.MaxValue;

        public static MetaHash NameToHash(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new MetaHash(0);
            var s = name.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
                return new MetaHash(raw);
            s = s.ToLowerInvariant();
            JenkIndex.Ensure(s);
            return new MetaHash(JenkHash.GenHash(s));
        }

        private static void Touch(MloArchetype m)
        {
            if (m?.Ytyp != null) m.Ytyp.HasChanged = true;
        }

        public static int RoomCount(MloArchetype m) => m?.rooms?.Length ?? 0;
        public static int PortalCount(MloArchetype m) => m?.portals?.Length ?? 0;
        public static int EntitySetCount(MloArchetype m) => m?.entitySets?.Length ?? 0;
        public static int EntityCount(MloArchetype m) => m?.entities?.Length ?? 0;

        public static MCMloRoomDef GetRoom(MloArchetype m, int index) =>
            (m?.rooms != null && index >= 0 && index < m.rooms.Length) ? m.rooms[index] : null;

        public static MCMloPortalDef GetPortal(MloArchetype m, int index) =>
            (m?.portals != null && index >= 0 && index < m.portals.Length) ? m.portals[index] : null;

        public static MCMloEntitySet GetEntitySet(MloArchetype m, int index) =>
            (m?.entitySets != null && index >= 0 && index < m.entitySets.Length) ? m.entitySets[index] : null;

        public static int IndexOfRoom(MloArchetype m, MCMloRoomDef r) =>
            (m?.rooms == null || r == null) ? -1 : Array.IndexOf(m.rooms, r);

        public static int IndexOfPortal(MloArchetype m, MCMloPortalDef p) =>
            (m?.portals == null || p == null) ? -1 : Array.IndexOf(m.portals, p);

        public static int IndexOfEntitySet(MloArchetype m, MCMloEntitySet s) =>
            (m?.entitySets == null || s == null) ? -1 : Array.IndexOf(m.entitySets, s);

        public static string DescribeRoom(MCMloRoomDef r)
        {
            if (r == null) return "";
            var tc = r._Data.timecycleName.ToCleanString();
            var size = r.BBSize;
            return $"{(string.IsNullOrEmpty(r.RoomName) ? "(unnamed)" : r.RoomName)}  ·  " +
                   $"{r._Data.portalCount} portal(s), {r.AttachedObjects?.Length ?? 0} object(s)  ·  " +
                   $"{size.X:0.#} x {size.Y:0.#} x {size.Z:0.#} m" +
                   (string.IsNullOrEmpty(tc) ? "" : "  ·  " + tc);
        }

        public static string DescribePortal(MloArchetype m, MCMloPortalDef p)
        {
            if (p == null) return "";
            return $"{RoomNameFor(m, p._Data.roomFrom)} -> {RoomNameFor(m, p._Data.roomTo)}  ·  " +
                   $"{p.Corners?.Length ?? 0} corner(s), {p.AttachedObjects?.Length ?? 0} object(s)  ·  " +
                   $"opacity {p._Data.opacity}";
        }

        public static string DescribeEntitySet(MCMloEntitySet s)
        {
            if (s == null) return "";
            int ents = s.Entities?.Length ?? 0;
            int locs = s.Locations?.Length ?? 0;
            return $"{s._Data.name.ToCleanString()}  ·  {ents} entit{(ents == 1 ? "y" : "ies")}" +
                   (ents == locs ? "" : $"  ·  MISMATCH: {locs} location(s)");
        }

        public static string RoomNameFor(MloArchetype m, uint roomIndex)
        {
            var r = GetRoom(m, (int)roomIndex);
            if (r == null) return $"<missing room {roomIndex}>";
            return string.IsNullOrEmpty(r.RoomName) ? $"room {roomIndex}" : r.RoomName;
        }

        public static string GetRoomName(MCMloRoomDef r) => r?.RoomName ?? "";
        public static uint GetRoomFlags(MCMloRoomDef r) => r?._Data.flags ?? 0u;
        public static string GetRoomTimecycle(MCMloRoomDef r) => r?._Data.timecycleName.ToCleanString() ?? "";
        public static string GetRoomSecondaryTimecycle(MCMloRoomDef r) => r?._Data.secondaryTimecycleName.ToCleanString() ?? "";
        public static Vector3 GetRoomBBMin(MCMloRoomDef r) => r?._Data.bbMin ?? Vector3.Zero;
        public static Vector3 GetRoomBBMax(MCMloRoomDef r) => r?._Data.bbMax ?? Vector3.Zero;
        public static float GetRoomBlend(MCMloRoomDef r) => r?._Data.blend ?? 0.0f;
        public static int GetRoomFloorId(MCMloRoomDef r) => r?._Data.floorId ?? 0;
        public static int GetRoomExteriorVisibilityDepth(MCMloRoomDef r) => r?._Data.exteriorVisibiltyDepth ?? -1;
        public static uint GetRoomPortalCount(MCMloRoomDef r) => r?._Data.portalCount ?? 0u;
        public static uint[] GetRoomAttachedObjects(MCMloRoomDef r) => r?.AttachedObjects ?? Array.Empty<uint>();

        public static void SetRoomName(MloArchetype m, MCMloRoomDef r, string name)
        {
            if (r == null) return;
            r.RoomName = name ?? "";
            Touch(m);
        }

        public static void SetRoomFlags(MloArchetype m, MCMloRoomDef r, uint flags)
        {
            if (r == null) return;
            r._Data.flags = flags;
            Touch(m);
        }

        public static void SetRoomTimecycle(MloArchetype m, MCMloRoomDef r, string name)
        {
            if (r == null) return;
            r._Data.timecycleName = NameToHash(name);
            Touch(m);
        }

        public static void SetRoomSecondaryTimecycle(MloArchetype m, MCMloRoomDef r, string name)
        {
            if (r == null) return;
            r._Data.secondaryTimecycleName = NameToHash(name);
            Touch(m);
        }

        public static void SetRoomBB(MloArchetype m, MCMloRoomDef r, Vector3 min, Vector3 max)
        {
            if (r == null) return;
            r._Data.bbMin = Vector3.Min(min, max);
            r._Data.bbMax = Vector3.Max(min, max);
            Touch(m);
        }

        public static void SetRoomBlend(MloArchetype m, MCMloRoomDef r, float blend)
        {
            if (r == null) return;
            r._Data.blend = blend;
            Touch(m);
        }

        public static void SetRoomFloorId(MloArchetype m, MCMloRoomDef r, int floorId)
        {
            if (r == null) return;
            r._Data.floorId = floorId;
            Touch(m);
        }

        public static void SetRoomExteriorVisibilityDepth(MloArchetype m, MCMloRoomDef r, int depth)
        {
            if (r == null) return;
            r._Data.exteriorVisibiltyDepth = depth;
            Touch(m);
        }

        public static void SetRoomAttachedObjects(MloArchetype m, MCMloRoomDef r, IEnumerable<uint> objects)
        {
            if (r == null) return;
            int max = m?.entities?.Length ?? 0;
            r.AttachedObjects = (objects ?? Enumerable.Empty<uint>())
                .Where(i => i < max).Distinct().OrderBy(i => i).ToArray();
            Touch(m);
        }

        public static uint GetPortalRoomFrom(MCMloPortalDef p) => p?._Data.roomFrom ?? 0u;
        public static uint GetPortalRoomTo(MCMloPortalDef p) => p?._Data.roomTo ?? 0u;
        public static uint GetPortalFlags(MCMloPortalDef p) => p?._Data.flags ?? 0u;
        public static uint GetPortalMirrorPriority(MCMloPortalDef p) => p?._Data.mirrorPriority ?? 0u;
        public static uint GetPortalOpacity(MCMloPortalDef p) => p?._Data.opacity ?? 0u;
        public static uint GetPortalAudioOcclusion(MCMloPortalDef p) => p?._Data.audioOcclusion ?? 0u;
        public static uint[] GetPortalAttachedObjects(MCMloPortalDef p) => p?.AttachedObjects ?? Array.Empty<uint>();
        public static int GetPortalCornerCount(MCMloPortalDef p) => p?.Corners?.Length ?? 0;

        public static Vector3 GetPortalCorner(MCMloPortalDef p, int i) =>
            (p?.Corners != null && i >= 0 && i < p.Corners.Length) ? Xyz(p.Corners[i]) : Vector3.Zero;

        public static Vector3[] GetPortalCorners(MCMloPortalDef p) =>
            p?.Corners?.Select(Xyz).ToArray() ?? Array.Empty<Vector3>();

        private static Vector3 Xyz(Vector4 v) => new Vector3(v.X, v.Y, v.Z);

        public static Vector3 GetPortalCenter(MCMloPortalDef p) => p?.Center ?? Vector3.Zero;

        public static bool SetPortalRoomFrom(MloArchetype m, MCMloPortalDef p, uint roomIndex)
        {
            if (p == null || roomIndex >= (uint)RoomCount(m)) return false;
            p._Data.roomFrom = roomIndex;
            m?.UpdatePortalCounts();
            Touch(m);
            return true;
        }

        public static bool SetPortalRoomTo(MloArchetype m, MCMloPortalDef p, uint roomIndex)
        {
            if (p == null || roomIndex >= (uint)RoomCount(m)) return false;
            p._Data.roomTo = roomIndex;
            m?.UpdatePortalCounts();
            Touch(m);
            return true;
        }

        public static void SetPortalFlags(MloArchetype m, MCMloPortalDef p, uint flags)
        {
            if (p == null) return;
            p._Data.flags = flags;
            Touch(m);
        }

        public static void SetPortalMirrorPriority(MloArchetype m, MCMloPortalDef p, uint priority)
        {
            if (p == null) return;
            p._Data.mirrorPriority = priority;
            Touch(m);
        }

        public static void SetPortalOpacity(MloArchetype m, MCMloPortalDef p, uint opacity)
        {
            if (p == null) return;
            p._Data.opacity = opacity;
            Touch(m);
        }

        public static void SetPortalAudioOcclusion(MloArchetype m, MCMloPortalDef p, uint occlusion)
        {
            if (p == null) return;
            p._Data.audioOcclusion = occlusion;
            Touch(m);
        }

        public static bool SetPortalCorner(MloArchetype m, MCMloPortalDef p, int i, Vector3 v)
        {
            if (p?.Corners == null || i < 0 || i >= p.Corners.Length) return false;
            p.Corners[i] = new Vector4(v, p.Corners[i].W);
            Touch(m);
            return true;
        }

        public static void SetPortalCorners(MloArchetype m, MCMloPortalDef p, IList<Vector3> corners)
        {
            if (p == null) return;
            if (corners == null || corners.Count == 0) { p.Corners = null; Touch(m); return; }
            var arr = new Vector4[corners.Count];
            for (int i = 0; i < corners.Count; i++) arr[i] = new Vector4(corners[i], 0.0f);
            p.Corners = arr;
            Touch(m);
        }

        public static void AddPortalCorner(MloArchetype m, MCMloPortalDef p, Vector3 v)
        {
            if (p == null) return;
            var list = p.Corners?.ToList() ?? new List<Vector4>();
            list.Add(new Vector4(v, 0.0f));
            p.Corners = list.ToArray();
            Touch(m);
        }

        public static bool RemovePortalCorner(MloArchetype m, MCMloPortalDef p, int i)
        {
            if (p?.Corners == null || i < 0 || i >= p.Corners.Length) return false;
            var list = p.Corners.ToList();
            list.RemoveAt(i);
            p.Corners = list.Count > 0 ? list.ToArray() : null;
            Touch(m);
            return true;
        }

        public static void MovePortal(MloArchetype m, MCMloPortalDef p, Vector3 delta)
        {
            if (p?.Corners == null) return;
            for (int i = 0; i < p.Corners.Length; i++)
                p.Corners[i] = new Vector4(Xyz(p.Corners[i]) + delta, p.Corners[i].W);
            Touch(m);
        }

        public static void SetPortalAttachedObjects(MloArchetype m, MCMloPortalDef p, IEnumerable<uint> objects)
        {
            if (p == null) return;
            int max = m?.entities?.Length ?? 0;
            p.AttachedObjects = (objects ?? Enumerable.Empty<uint>())
                .Where(i => i < max).Distinct().OrderBy(i => i).ToArray();
            Touch(m);
        }

        public static string GetEntitySetName(MCMloEntitySet s) => s?._Data.name.ToCleanString() ?? "";

        public static void SetEntitySetName(MloArchetype m, MCMloEntitySet s, string name)
        {
            if (s == null) return;
            s._Data.name = NameToHash(name);
            Touch(m);
        }

        public static int EntitySetEntityCount(MCMloEntitySet s) => s?.Entities?.Length ?? 0;

        public static MCEntityDef GetEntitySetEntity(MCMloEntitySet s, int i) =>
            (s?.Entities != null && i >= 0 && i < s.Entities.Length) ? s.Entities[i] : null;

        public static uint GetEntitySetLocation(MCMloEntitySet s, int i) =>
            (s?.Locations != null && i >= 0 && i < s.Locations.Length) ? s.Locations[i] : 0u;

        public static bool SetEntitySetLocation(MloArchetype m, MCMloEntitySet s, int i, uint roomIndex)
        {
            if (s?.Locations == null || i < 0 || i >= s.Locations.Length) return false;
            if (roomIndex >= (uint)RoomCount(m)) return false;
            s.Locations[i] = roomIndex;
            Touch(m);
            return true;
        }

        public static bool AddEntitySetEntity(MloArchetype m, MCMloEntitySet s, MCEntityDef ent, uint roomIndex)
        {
            if (s == null || ent == null) return false;
            if (roomIndex >= (uint)RoomCount(m)) roomIndex = 0;

            var ents = s.Entities?.ToList() ?? new List<MCEntityDef>();
            var locs = s.Locations?.ToList() ?? new List<uint>();
            while (locs.Count < ents.Count) locs.Add(0);
            while (ents.Count < locs.Count) locs.RemoveAt(locs.Count - 1);

            ent.OwnerMlo = m;
            ents.Add(ent);
            locs.Add(roomIndex);
            s.Entities = ents.ToArray();
            s.Locations = locs.ToArray();

            RenumberEntities(m);
            Touch(m);
            return true;
        }

        public static bool RemoveEntitySetEntity(MloArchetype m, MCMloEntitySet s, int i)
        {
            if (s?.Entities == null || i < 0 || i >= s.Entities.Length) return false;

            var ents = s.Entities.ToList();
            ents.RemoveAt(i);
            s.Entities = ents.ToArray();

            if (s.Locations != null && i < s.Locations.Length)
            {
                var locs = s.Locations.ToList();
                locs.RemoveAt(i);
                s.Locations = locs.ToArray();
            }

            RenumberEntities(m);
            Touch(m);
            return true;
        }

        public static bool NormaliseEntitySet(MloArchetype m, MCMloEntitySet s)
        {
            if (s == null) return false;
            int ents = s.Entities?.Length ?? 0;
            int locs = s.Locations?.Length ?? 0;
            if (ents == locs) return false;

            var l = s.Locations?.ToList() ?? new List<uint>();
            while (l.Count < ents) l.Add(0);
            while (l.Count > ents) l.RemoveAt(l.Count - 1);
            s.Locations = l.ToArray();
            Touch(m);
            return true;
        }

        public static MCMloRoomDef AddRoom(MloArchetype m, string name)
        {
            if (m == null) return null;
            var r = new MCMloRoomDef();
            r.RoomName = string.IsNullOrWhiteSpace(name) ? "room_" + RoomCount(m) : name.Trim();
            r._Data.blend = 1.0f;
            r._Data.exteriorVisibiltyDepth = -1;
            r._Data.floorId = 0;
            r._Data.flags = 0;
            r._Data.bbMin = Vector3.Zero;
            r._Data.bbMax = Vector3.Zero;
            m.AddRoom(r);
            m.UpdatePortalCounts();
            Touch(m);
            return r;
        }

        public static MCMloPortalDef AddPortal(MloArchetype m, uint roomFrom, uint roomTo, IList<Vector3> corners)
        {
            if (m == null) return null;
            int rc = RoomCount(m);
            if (rc == 0) return null;
            if (roomFrom >= (uint)rc) roomFrom = 0;
            if (roomTo >= (uint)rc) roomTo = 0;

            var p = new MCMloPortalDef();
            p._Data.roomFrom = roomFrom;
            p._Data.roomTo = roomTo;
            p._Data.flags = 0;
            p._Data.mirrorPriority = 0;
            p._Data.opacity = 0;
            p._Data.audioOcclusion = 0;
            SetPortalCorners(m, p, corners != null && corners.Count >= 3 ? corners : DefaultPortalQuad());

            m.AddPortal(p);
            Touch(m);
            return p;
        }

        public static MCMloPortalDef AddPortal(MloArchetype m, uint roomFrom, uint roomTo) =>
            AddPortal(m, roomFrom, roomTo, null);

        private static List<Vector3> DefaultPortalQuad() => new List<Vector3>
        {
            new Vector3(-0.5f, 0.0f, 0.0f),
            new Vector3(-0.5f, 0.0f, 2.0f),
            new Vector3( 0.5f, 0.0f, 2.0f),
            new Vector3( 0.5f, 0.0f, 0.0f),
        };

        public static MCMloEntitySet AddEntitySet(MloArchetype m, string name)
        {
            if (m == null) return null;
            var s = new MCMloEntitySet();
            s._Data.name = NameToHash(string.IsNullOrWhiteSpace(name) ? "set_" + EntitySetCount(m) : name);
            s.Entities = Array.Empty<MCEntityDef>();
            s.Locations = Array.Empty<uint>();
            m.AddEntitySet(s);
            Touch(m);
            return s;
        }

        public static MCEntityDef NewEntity(MloArchetype m, string archetypeName, Vector3 position,
                                            Quaternion orientation, Vector3 scale, float lodDist = 100.0f)
        {
            var anHash = NameToHash(archetypeName);

            var q = orientation;
            if (q.LengthSquared() < 1e-6f) q = Quaternion.Identity; else q.Normalize();
            q = Quaternion.Invert(q);

            var def = new CEntityDef
            {
                archetypeName = anHash,
                flags = 0,
                guid = 0,
                position = position,
                rotation = new Vector4(q.X, q.Y, q.Z, q.W),
                scaleXY = Math.Max(scale.X, 0.001f),
                scaleZ = Math.Max(scale.Z, 0.001f),
                parentIndex = -1,
                lodDist = Math.Max(lodDist, 0.0f),
                childLodDist = 0,
                lodLevel = rage__eLodType.LODTYPES_DEPTH_HD,
                numChildren = 0,
                priorityLevel = rage__ePriorityLevel.PRI_REQUIRED,
                ambientOcclusionMultiplier = 255,
                artificialAmbientOcclusion = 255,
                tintValue = 0,
            };
            return new MCEntityDef(ref def, m);
        }

        public static string RemoveRoom(MloArchetype m, MCMloRoomDef room, bool salvageEntities = true)
        {
            int idx = IndexOfRoom(m, room);
            if (idx < 0) return "room not found in this interior";

            uint r = (uint)idx;
            var report = new StringBuilder();

            var doomed = (m.portals ?? Array.Empty<MCMloPortalDef>())
                         .Where(pd => pd != null && (pd._Data.roomFrom == r || pd._Data.roomTo == r))
                         .ToList();

            var orphans = new List<uint>();
            if (salvageEntities)
            {
                if (room.AttachedObjects != null) orphans.AddRange(room.AttachedObjects);
                foreach (var op in doomed) if (op.AttachedObjects != null) orphans.AddRange(op.AttachedObjects);
            }

            foreach (var dp in doomed) m.RemovePortal(dp);
            m.RemoveRoom(room);

            if (m.portals != null)
            {
                foreach (var pd in m.portals)
                {
                    if (pd == null) continue;
                    if (pd._Data.roomFrom > r) pd._Data.roomFrom--;
                    if (pd._Data.roomTo > r) pd._Data.roomTo--;
                }
            }

            int locFixed = 0;
            if (m.entitySets != null)
            {
                foreach (var es in m.entitySets)
                {
                    if (es?.Locations == null) continue;
                    for (int i = 0; i < es.Locations.Length; i++)
                    {
                        if (es.Locations[i] > r) { es.Locations[i]--; locFixed++; }
                        else if (es.Locations[i] == r) { es.Locations[i] = 0; locFixed++; }
                    }
                }
            }

            int salvaged = 0;
            var survivor = GetRoom(m, 0);
            if (salvageEntities && orphans.Count > 0 && survivor != null)
            {
                var merged = new List<uint>(survivor.AttachedObjects ?? Array.Empty<uint>());
                foreach (var o in orphans) if (!merged.Contains(o)) { merged.Add(o); salvaged++; }
                merged.Sort();
                survivor.AttachedObjects = merged.ToArray();
            }

            m.UpdatePortalCounts();
            RenumberEntities(m);
            Touch(m);

            report.Append($"removed room {idx}");
            if (doomed.Count > 0) report.Append($", {doomed.Count} portal(s) that used it");
            if (locFixed > 0) report.Append($", remapped {locFixed} entity set location(s)");
            if (salvaged > 0) report.Append($", moved {salvaged} object(s) to \"{RoomNameFor(m, 0)}\"");
            else if (orphans.Count > 0 && survivor == null)
                report.Append($", {orphans.Count} object(s) LEFT DETACHED - no rooms remain");
            if (RoomCount(m) == 0) report.Append(" - the interior now has NO rooms and will not load");
            return report.ToString();
        }

        public static string RemovePortal(MloArchetype m, MCMloPortalDef portal, bool salvageEntities = true)
        {
            int idx = IndexOfPortal(m, portal);
            if (idx < 0) return "portal not found in this interior";

            int salvaged = 0;
            if (salvageEntities && portal.AttachedObjects != null && portal.AttachedObjects.Length > 0)
            {
                var host = GetRoom(m, (int)portal._Data.roomFrom) ?? GetRoom(m, 0);
                if (host != null)
                {
                    var merged = new List<uint>(host.AttachedObjects ?? Array.Empty<uint>());
                    foreach (var o in portal.AttachedObjects) if (!merged.Contains(o)) { merged.Add(o); salvaged++; }
                    merged.Sort();
                    host.AttachedObjects = merged.ToArray();
                }
            }

            m.RemovePortal(portal);
            RenumberEntities(m);
            Touch(m);

            return $"removed portal {idx}" + (salvaged > 0 ? $", moved {salvaged} object(s) to a room" : "");
        }

        public static string RemoveEntitySet(MloArchetype m, MCMloEntitySet set)
        {
            int idx = IndexOfEntitySet(m, set);
            if (idx < 0) return "entity set not found in this interior";

            int ents = set.Entities?.Length ?? 0;
            m.RemoveEntitySet(set);
            Touch(m);
            return $"removed entity set {idx} and its {ents} entit{(ents == 1 ? "y" : "ies")}";
        }

        public static void RenumberEntities(MloArchetype m)
        {
            if (m == null) return;
            int index = 0;
            if (m.entities != null)
            {
                for (int i = 0; i < m.entities.Length; i++)
                    if (m.entities[i] != null) m.entities[i].Index = index++;
            }
            if (m.entitySets != null)
            {
                for (int e = 0; e < m.entitySets.Length; e++)
                {
                    var set = m.entitySets[e];
                    if (set?.Entities == null) continue;
                    for (int i = 0; i < set.Entities.Length; i++)
                        if (set.Entities[i] != null) set.Entities[i].Index = index++;
                }
            }
        }

        public static void RefreshDerived(MloArchetype m)
        {
            if (m == null) return;

            if (m.rooms != null && Array.IndexOf(m.rooms, null) >= 0)
                m.rooms = m.rooms.Where(x => x != null).ToArray();
            if (m.portals != null && Array.IndexOf(m.portals, null) >= 0)
                m.portals = m.portals.Where(x => x != null).ToArray();
            if (m.entitySets != null && Array.IndexOf(m.entitySets, null) >= 0)
                m.entitySets = m.entitySets.Where(x => x != null).ToArray();

            if (m.rooms != null)
                for (int i = 0; i < m.rooms.Length; i++)
                    if (m.rooms[i] != null) { m.rooms[i].OwnerMlo = m; m.rooms[i].Index = i; }

            if (m.portals != null)
                for (int i = 0; i < m.portals.Length; i++)
                    if (m.portals[i] != null) { m.portals[i].OwnerMlo = m; m.portals[i].Index = i; }

            if (m.entitySets != null)
                for (int i = 0; i < m.entitySets.Length; i++)
                {
                    var s = m.entitySets[i];
                    if (s == null) continue;
                    s.OwnerMlo = m;
                    s.Index = i;
                    NormaliseEntitySet(m, s);
                }

            m.UpdatePortalCounts();
            RenumberEntities(m);
        }

        public static int RebuildInstances(MloArchetype m, IEnumerable<YmapEntityDef> worldEntities,
                                           GameFileCache cache, Action<YmapEntityDef> forget = null)
        {
            if (m == null || worldEntities == null) return 0;
            if (m.entities == null) m.entities = Array.Empty<MCEntityDef>();
            int done = 0;

            foreach (var ent in worldEntities)
            {
                var inst = ent?.MloInstance;
                if (inst == null) continue;
                if (!ReferenceEquals(inst.MloArch, m) && !ReferenceEquals(ent.Archetype, m)) continue;

                var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (inst.EntitySets != null)
                    foreach (var es in inst.EntitySets)
                        if (es != null && es.Visible && es.EntitySet != null) visible.Add(es.EntitySet.Name);

                if (forget != null)
                {
                    if (inst.Entities != null) foreach (var c in inst.Entities) forget(c);
                    if (inst.EntitySets != null)
                        foreach (var es in inst.EntitySets)
                            if (es?.Entities != null) foreach (var c in es.Entities) forget(c);
                }

                inst.MloArch = m;
                inst.CreateYmapEntities();
                if (cache != null) inst.InitYmapEntityArchetypes(cache);
                inst.UpdateEntities();

                if (inst.EntitySets != null)
                    foreach (var es in inst.EntitySets)
                        if (es?.EntitySet != null && visible.Contains(es.EntitySet.Name)) es.Visible = true;

                forget?.Invoke(ent);
                done++;
            }

            return done;
        }

        public static string Validate(MloArchetype m)
        {
            if (m == null) return "";
            var p = new List<string>();
            var who = string.IsNullOrEmpty(m.Name) ? "(unnamed MLO)" : m.Name;

            int roomCount = m.rooms?.Length ?? 0;
            int entCount = m.entities?.Length ?? 0;

            if (m.entities == null)
                p.Add("entities array is null - YtypFile.Save() reads its Length outside its own " +
                      "try/catch, so the whole ytyp fails to save. Use an empty array instead.");

            NullElements(p, "entities", m.entities);
            NullElements(p, "rooms", m.rooms);
            NullElements(p, "portals", m.portals);
            NullElements(p, "entitySets", m.entitySets);

            if (m.entitySets != null)
            {
                for (int i = 0; i < m.entitySets.Length; i++)
                    NullElements(p, $"entitySets[{i}].Entities", m.entitySets[i]?.Entities);
            }

            TooLong(p, "entities", entCount);
            TooLong(p, "rooms", roomCount);
            TooLong(p, "portals", m.portals?.Length ?? 0);
            TooLong(p, "entitySets", m.entitySets?.Length ?? 0);

            if (roomCount == 0)
                p.Add("no rooms - an MLO must have at least room 0 (limbo) or the game cannot place " +
                      "anything inside it.");
            else if (string.IsNullOrEmpty(m.rooms[0]?.RoomName))
                p.Add("room 0 has no name - it is the limbo room and is conventionally called " +
                      "\"limbo\"; scripts and other ytyps look rooms up by name.");

            if (roomCount > 0)
            {
                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < roomCount; i++)
                {
                    var n = m.rooms[i]?.RoomName;
                    if (string.IsNullOrEmpty(n)) { p.Add($"room {i} has no name."); continue; }
                    if (seen.TryGetValue(n, out int first))
                        p.Add($"rooms {first} and {i} are both called \"{n}\" - a name lookup finds " +
                              "only the first, so one of them is unreachable.");
                    else seen[n] = i;
                }
            }

            if (m.portals != null)
            {
                for (int i = 0; i < m.portals.Length; i++)
                {
                    var pd = m.portals[i];
                    if (pd == null) continue;
                    if (pd._Data.roomFrom >= (uint)roomCount)
                        p.Add($"portal {i} roomFrom is {pd._Data.roomFrom} but there are only {roomCount} room(s).");
                    if (pd._Data.roomTo >= (uint)roomCount)
                        p.Add($"portal {i} roomTo is {pd._Data.roomTo} but there are only {roomCount} room(s).");
                    if (pd._Data.roomFrom == pd._Data.roomTo && pd._Data.mirrorPriority == 0)
                        p.Add($"portal {i} joins room {pd._Data.roomFrom} to itself and is not a " +
                              "mirror - it culls nothing.");

                    int corners = pd.Corners?.Length ?? 0;
                    if (corners < 3)
                        p.Add($"portal {i} has {corners} corner(s) - fewer than three defines no plane " +
                              "and the portal cannot be seen through or selected.");
                    else if (corners != 4)
                        p.Add($"portal {i} has {corners} corners; every vanilla portal is a quad.");

                    OutOfRange(p, $"portal {i}", pd.AttachedObjects, entCount);
                }
            }

            var attached = new HashSet<uint>();
            if (m.rooms != null)
            {
                for (int i = 0; i < roomCount; i++)
                {
                    var r = m.rooms[i];
                    if (r == null) continue;
                    OutOfRange(p, $"room {i} (\"{r.RoomName}\")", r.AttachedObjects, entCount);
                    if (r.AttachedObjects != null) foreach (var o in r.AttachedObjects) attached.Add(o);
                }
            }
            if (m.portals != null)
            {
                foreach (var pd in m.portals)
                    if (pd?.AttachedObjects != null) foreach (var o in pd.AttachedObjects) attached.Add(o);
            }

            if (entCount > 0)
            {
                int orphans = 0;
                for (uint i = 0; i < entCount; i++) if (!attached.Contains(i)) orphans++;
                if (orphans > 0)
                    p.Add($"{orphans} of {entCount} entities are attached to no room and no portal - " +
                          "the game has nowhere to put them and will not draw them.");
            }

            if (m.entitySets != null)
            {
                for (int i = 0; i < m.entitySets.Length; i++)
                {
                    var es = m.entitySets[i];
                    if (es == null) continue;
                    var nm = es._Data.name.ToCleanString();
                    var label = $"entity set {i}" + (string.IsNullOrEmpty(nm) ? "" : $" (\"{nm}\")");

                    if (es._Data.name.Hash == 0)
                        p.Add($"entity set {i} has no name - nothing can switch it on.");

                    int se = es.Entities?.Length ?? 0;
                    int sl = es.Locations?.Length ?? 0;
                    if (se != sl)
                        p.Add($"{label} has {se} entities but {sl} locations - they are parallel arrays " +
                              "and a mismatch puts every entity after the shorter one in the wrong room.");

                    if (es.Locations != null)
                    {
                        for (int j = 0; j < es.Locations.Length; j++)
                            if (es.Locations[j] >= (uint)roomCount)
                                p.Add($"{label} location {j} points at room {es.Locations[j]}, " +
                                      $"but there are only {roomCount} room(s).");
                    }
                }
            }

            if (m.rooms != null && m.portals != null)
            {
                for (int i = 0; i < roomCount; i++)
                {
                    var r = m.rooms[i];
                    if (r == null) continue;
                    uint expect = 0;
                    foreach (var pd in m.portals)
                        if (pd != null && (pd._Data.roomFrom == i || pd._Data.roomTo == i)) expect++;
                    if (r._Data.portalCount != expect)
                        p.Add($"room {i} records {r._Data.portalCount} portal(s) but {expect} actually " +
                              "reference it - call RefreshDerived before saving.");
                }
            }

            if (entCount == 0 && (m.entitySets?.Any(x => (x?.Entities?.Length ?? 0) > 0) ?? false))
                p.Add("all entities live in entity sets and the room entity array is empty - " +
                      "YtypFile.Save() then omits the lodLevel and priorityLevel enum info, because " +
                      "it decides on m.entities.Length alone. Keep at least one entity in a room.");

            if (p.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"{who}: {p.Count} problem(s)");
            foreach (var line in p) sb.AppendLine("  - " + line);
            return sb.ToString();
        }

        public static string Validate(YtypFile ytyp)
        {
            if (ytyp?.AllArchetypes == null) return "";
            var sb = new StringBuilder();
            foreach (var a in ytyp.AllArchetypes)
            {
                if (!(a is MloArchetype m)) continue;
                var t = Validate(m);
                if (!string.IsNullOrEmpty(t)) sb.Append(t);
            }
            return sb.ToString();
        }

        private static void NullElements<T>(List<string> problems, string what, T[] array) where T : class
        {
            if (array == null) return;
            int n = 0;
            for (int i = 0; i < array.Length; i++) if (array[i] == null) n++;
            if (n > 0)
                problems.Add($"{what} contains {n} null element(s) - the save throws partway through " +
                             "and YtypFile.Save()'s empty catch turns that into a silently truncated ytyp.");
        }

        private static void TooLong(List<string> problems, string what, int count)
        {
            if (count > MetaArrayLimit)
                problems.Add($"{what} holds {count} items - a meta array counts its items in a ushort, " +
                             $"so anything past {MetaArrayLimit} wraps and the file describes the wrong length.");
        }

        private static void OutOfRange(List<string> problems, string owner, uint[] objects, int entityCount)
        {
            if (objects == null) return;
            var bad = objects.Where(o => o >= (uint)entityCount).ToArray();
            if (bad.Length == 0) return;
            problems.Add($"{owner} attaches object(s) {string.Join(", ", bad.Take(6))}" +
                         (bad.Length > 6 ? ", ..." : "") +
                         $" but the interior has {entityCount} entit{(entityCount == 1 ? "y" : "ies")}.");
        }
    }
}

