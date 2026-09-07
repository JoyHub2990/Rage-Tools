using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class InteriorAmbientResolver
    {
        public struct EntityAmbient
        {
            public float NaturalScale, ArtificialScale, InInterior, ReflectIntAmb;
            public Vector4 ArtIntAmbUp, ArtIntAmbDown;
            public string Modifier;
        }

        public static readonly EntityAmbient Exterior = new EntityAmbient { NaturalScale = 1.0f, ArtificialScale = 1.0f, InInterior = 0.0f, ReflectIntAmb = 2.0f, Modifier = "" };

        public static readonly EntityAmbient InteriorUngraded = new EntityAmbient { NaturalScale = -1.0f, ArtificialScale = -1.0f, InInterior = 1.0f, ReflectIntAmb = 2.0f, Modifier = "cycle" };

        private readonly TimecycleData timecycle;
        public static readonly bool Disabled = Environment.GetEnvironmentVariable("RLE_NOINTAMB") == "1";

        public InteriorAmbientResolver(TimecycleData timecycle) { this.timecycle = timecycle; }

        public bool Ready => timecycle != null && timecycle.Modifiers.Count > 0;

        private sealed class RoomMap { public Dictionary<YmapEntityDef, int> Map; public int Count; }
        private readonly ConditionalWeakTable<MloInstanceData, RoomMap> roomMaps = new ConditionalWeakTable<MloInstanceData, RoomMap>();
        private readonly Dictionary<(MloArchetype, int, uint, uint), EntityAmbient> byRoom = new Dictionary<(MloArchetype, int, uint, uint), EntityAmbient>();

        public void ClearCache() => byRoom.Clear();

        public EntityAmbient Resolve(YmapEntityDef e)
        {
            if (Disabled || e == null) return Exterior;
            if (e.MloParent == null)
            {
                var shellArch = e.MloInstance?.MloArch;
                if (shellArch?.rooms == null) return Exterior;
                for (int r = 1; r < shellArch.rooms.Length; r++)
                    if (shellArch.rooms[r] != null && (shellArch.rooms[r]._Data.timecycleName.Hash != 0 || shellArch.rooms[r]._Data.secondaryTimecycleName.Hash != 0))
                        return Resolve(shellArch, r);
                return InteriorUngraded;
            }
            var inst = e.MloParent.MloInstance;
            var arch = inst?.MloArch;
            if (arch?.rooms == null) return Exterior;
            return Resolve(arch, RoomOf(inst, arch, e));
        }

        public static readonly EntityAmbient Limbo = InteriorUngraded;
        private static readonly bool limboAsExterior = Environment.GetEnvironmentVariable("RLE_LIMBOEXT") == "1";

        public EntityAmbient Resolve(MloArchetype arch, int room)
        {
            if (Disabled || arch?.rooms == null) return Exterior;
            if (room == 0 && !limboAsExterior) return Limbo;
            if (room <= 0 || room >= arch.rooms.Length) return InteriorUngraded;
            var rd = arch.rooms[room]?._Data;
            if (rd == null) return InteriorUngraded;
            uint tc = rd.Value.timecycleName.Hash, tc2 = rd.Value.secondaryTimecycleName.Hash;
            var key = (arch, room, tc, tc2);
            if (byRoom.TryGetValue(key, out var cached)) return cached;
            var res = FromModifiers(tc, rd.Value.timecycleName.ToString(), tc2, rd.Value.secondaryTimecycleName.ToString(), rd.Value.blend);
            byRoom[key] = res;
            return res;
        }

        public void Stamp(Rendering.RenderMesh inst, MloArchetype arch, MCEntityDef ent, int roomHint)
        {
            if (inst == null || arch?.rooms == null) return;
            int room = roomHint;
            if (room < 0 && ent != null)
            {
                var r = arch.GetEntityRoom(ent);
                if (r != null) room = Array.IndexOf(arch.rooms, r);
                else
                {
                    var p = arch.GetEntityPortal(ent);
                    if (p != null) room = Math.Max((int)p._Data.roomFrom, (int)p._Data.roomTo);
                }
            }
            Apply(inst, Resolve(arch, room));
        }

        public static void Apply(Rendering.RenderMesh inst, in EntityAmbient a)
        {
            inst.NaturalAmbientScale = a.NaturalScale;
            inst.ArtificialAmbientScale = a.ArtificialScale;
            inst.InInterior = a.InInterior;
            inst.ReflectIntAmb = a.ReflectIntAmb;
            inst.ArtIntAmbUp = a.ArtIntAmbUp;
            inst.ArtIntAmbDown = a.ArtIntAmbDown;
        }

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

        private EntityAmbient FromModifiers(uint tc, string tcName, uint tc2, string tc2Name, float blend)
        {
            TimecycleData.Modifier mod = null, mod2 = null;
            if (tc != 0) { int i = timecycle.FindModifier(tc, tcName); if (i >= 0) mod = timecycle.Modifiers[i]; }
            if (tc2 != 0) { int i = timecycle.FindModifier(tc2, tc2Name); if (i >= 0) mod2 = timecycle.Modifiers[i]; }
            if (mod == null && mod2 == null) return InteriorUngraded;
            if (mod == null) { mod = mod2; mod2 = null; }
            float w = float.IsNaN(blend) ? 1.0f : Math.Clamp(blend, 0.0f, 1.0f);
            if (w <= 0.0f) return InteriorUngraded;

            float natScale = 1.0f, artScale = 0.0f;
            bool natValid = TryGet(mod, "natural_ambient_multiplier", ref natScale) | TryGet(mod2, "natural_ambient_multiplier", ref natScale);
            bool artValid = TryGet(mod, "artificial_int_ambient_multiplier", ref artScale) | TryGet(mod2, "artificial_int_ambient_multiplier", ref artScale);

            var res = new EntityAmbient
            {
                NaturalScale = natValid ? (1.0f - w) + Math.Max(natScale, 0.0f) * w : -1.0f,
                ArtificialScale = artValid ? (1.0f - w) + Math.Max(artScale, 0.0f) * w : -1.0f,
                InInterior = 1.0f,
                ReflectIntAmb = 2.0f,
                Modifier = mod2 != null ? mod.Name + "+" + mod2.Name : mod.Name,
            };
            TryGet(mod, "reflection_tweak_interior_amb", ref res.ReflectIntAmb); TryGet(mod2, "reflection_tweak_interior_amb", ref res.ReflectIntAmb);
            res.ArtIntAmbUp = ColourTimesIntensity(mod, mod2, "light_artificial_int_up_col", "light_artificial_int_up_intensity");
            res.ArtIntAmbDown = ColourTimesIntensity(mod, mod2, "light_artificial_int_down_col", "light_artificial_int_down_intensity");
            return res;
        }

        private static bool TryGet(TimecycleData.Modifier m, string name, ref float v)
        {
            if (m == null || !m.Values.TryGetValue(name, out var mv)) return false;
            v = mv; return true;
        }

        private static Vector4 ColourTimesIntensity(TimecycleData.Modifier m, TimecycleData.Modifier m2, string col, string intensity)
        {
            float r = 0, g = 0, b = 0, i = 0;
            bool any = TryGet(m, col + "_r", ref r) | TryGet(m2, col + "_r", ref r);
            any |= TryGet(m, col + "_g", ref g) | TryGet(m2, col + "_g", ref g);
            any |= TryGet(m, col + "_b", ref b) | TryGet(m2, col + "_b", ref b);
            bool anyI = TryGet(m, intensity, ref i) | TryGet(m2, intensity, ref i);
            i = Math.Max(i, 0.0f);
            return new Vector4(r * i, g * i, b * i, (any && anyI) ? 1.0f : 0.0f);
        }
    }
}

