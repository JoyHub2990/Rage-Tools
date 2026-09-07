using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class InteriorSunResolver
    {
        public static readonly int Mode = ReadMode();
        public static readonly bool Dump = Environment.GetEnvironmentVariable("RLE_SUNDUMP") == "1";

        private static int ReadMode()
        {
            var s = Environment.GetEnvironmentVariable("RLE_SUNINDOORS");
            if (int.TryParse(s, out int v) && v >= 0 && v <= 2) return v;
            return 2;
        }

        public Func<MloArchetype, int, float> RoomNaturalAmbient;

        public const uint RoomFlagNoDirectional = 4;
        public const uint RoomFlagForceDirectional = 128;

        public float SunFor(YmapEntityDef e)
        {
            if (Mode == 0 || e == null) return 1.0f;
            var parent = e.MloParent;
            if (parent == null)
            {
                var own = e.MloInstance?.MloArch;
                if (own == null) return 1.0f;
                return InfoOf(own).AnyExteriorPortal ? 1.0f : 0.0f;
            }
            var inst = parent.MloInstance;
            var arch = inst?.MloArch;
            if (arch?.rooms == null) return 1.0f;
            int room = RoomOf(inst, arch, e);
            return SunForRoom(arch, room);
        }

        public float SunForRoom(MloArchetype arch, int room)
        {
            if (Mode == 0 || arch?.rooms == null) return 1.0f;
            var info = InfoOf(arch);
            if (room <= 0 || room >= arch.rooms.Length) return info.AnyExteriorPortal ? 1.0f : 0.0f;
            if (info.Flag[room] != 0) return info.Flag[room] > 0 ? 1.0f : 0.0f;
            if (Mode < 2 || !info.Sealed[room]) return 1.0f;
            float nat = RoomNaturalAmbient?.Invoke(arch, room) ?? -1.0f;
            return nat > 0.001f ? 1.0f : 0.0f;
        }

        private sealed class Info
        {
            public sbyte[] Flag;
            public bool[] Sealed;
            public bool AnyExteriorPortal;
        }
        private readonly Dictionary<MloArchetype, Info> byArch = new Dictionary<MloArchetype, Info>();

        public void ClearCache() { lock (byArch) byArch.Clear(); }

        private Info InfoOf(MloArchetype arch)
        {
            lock (byArch)
            {
                if (byArch.TryGetValue(arch, out var i)) return i;
                i = Build(arch);
                byArch[arch] = i;
                return i;
            }
        }

        private Info Build(MloArchetype arch)
        {
            int n = arch.rooms?.Length ?? 0;
            int m = Math.Max(n, 1);
            var info = new Info { Flag = new sbyte[m], Sealed = new bool[m] };
            var hasOpening = new bool[m];
            if (arch.portals != null)
            {
                foreach (var p in arch.portals)
                {
                    if (p == null) continue;
                    int a = (int)p._Data.roomFrom, b = (int)p._Data.roomTo;
                    if (a != 0 && b != 0) continue;
                    int inner = a == 0 ? b : a;
                    if (inner > 0 && inner < hasOpening.Length) hasOpening[inner] = true;
                    info.AnyExteriorPortal = true;
                }
            }
            for (int r = 0; r < n; r++)
            {
                var rd = arch.rooms[r];
                info.Sealed[r] = r > 0 && !hasOpening[r];
                if (rd == null) continue;
                uint flags = rd._Data.flags;
                if ((flags & RoomFlagForceDirectional) != 0) info.Flag[r] = 1;
                else if ((flags & RoomFlagNoDirectional) != 0) info.Flag[r] = -1;
            }
            if (n > 0) { info.Flag[0] = 1; info.Sealed[0] = false; }
            if (Dump)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("SUNROOMS ").Append(arch.Name).Append(' ').Append(n).Append(" rooms, exterior portals ").Append(info.AnyExteriorPortal ? "yes" : "NONE").Append(':');
                for (int r = 0; r < n; r++)
                {
                    var rd = arch.rooms[r];
                    float nat = RoomNaturalAmbient?.Invoke(arch, r) ?? -1.0f;
                    sb.Append(' ').Append(r).Append('=').Append(rd?.RoomName ?? "?")
                      .Append("[flags ").Append(rd?._Data.flags ?? 0)
                      .Append(info.Sealed[r] ? " sealed" : " open")
                      .Append(" nat ").Append(nat.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(" sun ").Append(SunVerdict(info, r) > 0.5f ? "on" : "OFF").Append(']');
                }
                Console.WriteLine(sb.ToString());
            }
            return info;

            float SunVerdict(Info i, int r)
            {
                if (i.Flag[r] != 0) return i.Flag[r] > 0 ? 1.0f : 0.0f;
                if (Mode < 2 || !i.Sealed[r]) return 1.0f;
                float nat = RoomNaturalAmbient?.Invoke(arch, r) ?? -1.0f;
                return nat > 0.001f ? 1.0f : 0.0f;
            }
        }

        private sealed class RoomMap { public Dictionary<YmapEntityDef, int> Map; public int Count; }
        private readonly ConditionalWeakTable<MloInstanceData, RoomMap> roomMaps = new ConditionalWeakTable<MloInstanceData, RoomMap>();

        private int RoomOf(MloInstanceData inst, MloArchetype arch, YmapEntityDef e)
        {
            int count = (inst.Entities?.Length ?? 0) + (inst.EntitySets?.Length ?? 0);
            var rm = roomMaps.GetValue(inst, i => new RoomMap());
            if (rm.Map == null || rm.Count != count) { rm.Map = BuildRoomMap(inst, arch); rm.Count = count; }
            return rm.Map.TryGetValue(e, out int r) ? r : -1;
        }

        private static Dictionary<YmapEntityDef, int> BuildRoomMap(MloInstanceData inst, MloArchetype arch)
        {
            var map = new Dictionary<YmapEntityDef, int>();
            var ents = inst.Entities;
            if (ents != null && arch.rooms != null)
                for (int r = 0; r < arch.rooms.Length; r++)
                {
                    var att = arch.rooms[r]?.AttachedObjects;
                    if (att == null) continue;
                    foreach (var idx in att) if (idx < ents.Length && ents[idx] != null) map[ents[idx]] = r;
                }
            if (ents != null && arch.portals != null)
                foreach (var p in arch.portals)
                {
                    var att = p?.AttachedObjects;
                    if (att == null) continue;
                    int room = Math.Max((int)p._Data.roomFrom, (int)p._Data.roomTo);
                    foreach (var idx in att) if (idx < ents.Length && ents[idx] != null && !map.ContainsKey(ents[idx])) map[ents[idx]] = room;
                }
            var sets = inst.EntitySets;
            if (sets != null)
                foreach (var set in sets)
                {
                    if (set?.Entities == null) continue;
                    var loc = set.Locations;
                    for (int j = 0; j < set.Entities.Count; j++)
                    {
                        var se = set.Entities[j];
                        if (se == null) continue;
                        map[se] = (loc != null && j < loc.Length) ? (int)loc[j] : -1;
                    }
                }
            return map;
        }

        public static void Test_N2(Action<string, bool, string> check)
        {
            var arch = new MloArchetype();
            var rooms = new MCMloRoomDef[5];
            for (int i = 0; i < rooms.Length; i++) rooms[i] = new MCMloRoomDef { RoomName = "room" + i };
            rooms[3]._Data.flags = RoomFlagNoDirectional;
            rooms[4]._Data.flags = RoomFlagNoDirectional | RoomFlagForceDirectional;
            arch.rooms = rooms;
            var p0 = new MCMloPortalDef(); p0._Data.roomFrom = 0; p0._Data.roomTo = 1;
            var p1 = new MCMloPortalDef(); p1._Data.roomFrom = 1; p1._Data.roomTo = 2;
            var p2 = new MCMloPortalDef(); p2._Data.roomFrom = 2; p2._Data.roomTo = 3;
            var p3 = new MCMloPortalDef(); p3._Data.roomFrom = 1; p3._Data.roomTo = 4;
            arch.portals = new[] { p0, p1, p2, p3 };

            var r = new InteriorSunResolver();
            if (Mode == 0)
            {
                check("n2 sun: RLE_SUNINDOORS=0 leaves every room lit", r.SunForRoom(arch, 3) == 1.0f, "A/B mode");
                return;
            }
            check("n2 sun: the hall with a door to the street keeps the sun", r.SunForRoom(arch, 1) == 1.0f, r.SunForRoom(arch, 1).ToString());
            check("n2 sun: the room the artist flagged gets none", r.SunForRoom(arch, 3) == 0.0f, r.SunForRoom(arch, 3).ToString());
            check("n2 sun: flag 128 forces it back on", r.SunForRoom(arch, 4) == 1.0f, r.SunForRoom(arch, 4).ToString());
            check("n2 sun: limbo is the outside", r.SunForRoom(arch, 0) == 1.0f, r.SunForRoom(arch, 0).ToString());
            if (Mode >= 2)
                check("n2 sun: an inner room with no opening gets none", r.SunForRoom(arch, 2) == 0.0f, r.SunForRoom(arch, 2).ToString());

            var sealedArch = new MloArchetype();
            var srooms = new MCMloRoomDef[2];
            for (int i = 0; i < srooms.Length; i++) srooms[i] = new MCMloRoomDef { RoomName = "s" + i };
            sealedArch.rooms = srooms;
            var sp = new MCMloPortalDef(); sp._Data.roomFrom = 1; sp._Data.roomTo = 1;
            sealedArch.portals = new[] { sp };
            var r2 = new InteriorSunResolver();
            check("n2 sun: a sealed interior's shell is out of the sun too", r2.SunForRoom(sealedArch, 0) == 0.0f, r2.SunForRoom(sealedArch, 0).ToString());
            check("n2 sun: an interior with a door leaves its shell alone", r.SunForRoom(arch, -1) == 1.0f, r.SunForRoom(arch, -1).ToString());
            check("n2 sun: an entity outside every interior is unchanged", r.SunFor(new YmapEntityDef()) == 1.0f, "exterior");
        }
    }
}

