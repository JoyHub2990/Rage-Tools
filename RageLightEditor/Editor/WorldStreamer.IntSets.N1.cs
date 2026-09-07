using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        private static bool IsCustomInterior_N1(MloArchetype mlo)
        {
            var ytyp = mlo?.Ytyp;
            if (ytyp == null) return false;
            if (!string.IsNullOrEmpty(ytyp.FilePath)) return true;
            var fe = ytyp.RpfFileEntry;
            return fe == null || fe.Parent == null;
        }

        private static void RoomsOfSet_N1(MloArchetype mlo, int setIndex, Dictionary<int, int> counts)
        {
            counts.Clear();
            var sets = mlo?.entitySets;
            if (sets == null || setIndex < 0 || setIndex >= sets.Length) return;
            var locs = sets[setIndex]?.Locations;
            if (locs == null) return;
            foreach (var l in locs)
            {
                int r = (int)l;
                if (r <= 0) continue;
                counts.TryGetValue(r, out int n);
                counts[r] = n + 1;
            }
        }

        private static bool CanFillEmptyRoom_N1(SetInfo si) =>
            si.Why.Length == 0 || si.Why == "off: lone option of a DLC interior" ||
            si.Why.StartsWith("off: no family", StringComparison.Ordinal);

        private static void NoEmptyRoomSets_N1(List<SetInfo> infos, MloArchetype mlo)
        {
            if (infos == null || mlo?.rooms == null || mlo.entitySets == null) return;
            int nrooms = mlo.rooms.Length;
            if (nrooms < 2) return;
            var has = new bool[nrooms];
            for (int r = 1; r < nrooms; r++) has[r] = (mlo.rooms[r]?.AttachedObjects?.Length ?? 0) > 0;
            var counts = new Dictionary<int, int>();
            foreach (var si in infos)
            {
                if (!si.On || si.Count == 0) continue;
                RoomsOfSet_N1(mlo, si.Index, counts);
                foreach (var kv in counts) if (kv.Key < nrooms) has[kv.Key] = true;
            }
            var dressers = new List<SetInfo>();
            for (int r = 1; r < nrooms; r++)
            {
                if (has[r]) continue;
                dressers.Clear();
                foreach (var si in infos)
                {
                    if (si.On || si.Count == 0 || !CanFillEmptyRoom_N1(si)) continue;
                    RoomsOfSet_N1(mlo, si.Index, counts);
                    if (counts.ContainsKey(r)) dressers.Add(si);
                }
                if (dressers.Count != 1) continue;
                var win = dressers[0];
                win.On = true;
                win.Why = $"the only dressing of room {r} '{mlo.rooms[r]?.RoomName}' - it would be empty";
                RoomsOfSet_N1(mlo, win.Index, counts);
                foreach (var kv in counts) if (kv.Key < nrooms) has[kv.Key] = true;
            }
        }

        public bool MirrorOnlyKeep_N1(YmapEntityDef ie)
        {
            var arch = ie?.Archetype;
            if (arch == null) return false;
            uint f = ie._CEntityDef.flags;
            if ((f & FlagOnlyMirrorReflections_N1) == 0) return false;
            if ((f & (0x800000u | 0x2000000u | 0x8000000u)) != 0) return false;
            if (f == 536870912u || f == 39321602u) return false;
            if (arch.Type == MetaName.CTimeArchetypeDef && !(TimedEntitiesAlways || arch.IsActive(Hour))) return false;
            if ((arch._BaseArchetypeDef.flags & 2048) != 0) return false;
            var name = arch.Name ?? "";
            if (name.IndexOf("refl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            var inst = ie.MloParent?.MloInstance;
            return inst == null || !MirrorTwins_N1(inst).Contains(arch.Hash);
        }
        private const uint FlagOnlyMirrorReflections_N1 = 0x20000000u;

        private static HashSet<uint> MirrorTwins_N1(MloInstanceData inst)
        {
            if (mirrorTwins.TryGetValue(inst, out var set)) return set;
            set = new HashSet<uint>();
            void Note(YmapEntityDef e)
            {
                if (e?.Archetype == null) return;
                if ((e._CEntityDef.flags & FlagsOnlyOtherPasses) != 0) return;
                set.Add(e.Archetype.Hash);
            }
            if (inst.Entities != null) foreach (var e in inst.Entities) Note(e);
            if (inst.EntitySets != null)
                foreach (var s in inst.EntitySets)
                    if (s?.Entities != null) foreach (var e in s.Entities) Note(e);
            try { mirrorTwins.Add(inst, set); } catch { }
            return set;
        }
        private static readonly ConditionalWeakTable<MloInstanceData, HashSet<uint>> mirrorTwins =
            new ConditionalWeakTable<MloInstanceData, HashSet<uint>>();

        public List<string> DescribeSets_N1(MloInstanceData inst)
        {
            var lines = new List<string>();
            var sets = inst?.EntitySets;
            var mlo = inst?.Owner?.Archetype as MloArchetype;
            if (sets == null || mlo == null) return lines;
            var infos = new List<SetInfo>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                if (s == null) continue;
                var name = s.EntitySet?.Name ?? s.EntitySet?._Data.name.ToString() ?? "";
                infos.Add(MakeSetInfo(i, name, s.Entities?.Count ?? 0, s.Visible, s.Locations, s));
            }
            DecideAutoSets(infos, IsDlcInterior(mlo), mlo);
            var counts = new Dictionary<int, int>();
            var sb = new StringBuilder();
            foreach (var si in infos)
            {
                RoomsOfSet_N1(mlo, si.Index, counts);
                sb.Clear();
                foreach (var kv in counts) { if (sb.Length > 0) sb.Append(','); sb.Append(kv.Key).Append('x').Append(kv.Value); }
                bool forced = si.Set != null && (si.Set.Visible || (si.Set.EntitySet?.ForceVisible ?? false));
                bool on = interiorSetsMode == 2 || forced || (interiorSetsMode != 0 && si.On);
                lines.Add($"{(on ? "ON " : "off")} {si.Name}({si.Count}) rooms[{sb}] [{(forced ? "placed / forced visible" : si.Why)}]");
            }
            return lines;
        }

        public bool IsSetVisible_N1(MloInstanceData inst, MloInstanceEntitySet set) =>
            set != null && (set.Visible || (set.EntitySet?.ForceVisible ?? false) || IsInteriorSetAutoVisible(inst, set));

        public bool IsInteriorEntityFlagHidden_N1(YmapEntityDef ie) =>
            ie != null && ((ie._CEntityDef.flags & FlagOnlyInReflections) != 0 || (!IsFinalRender(ie) && !MirrorOnlyKeep_N1(ie)));

        public static int[] PlainEntityRooms_N1(MloInstanceData inst)
        {
            var ents = inst?.Entities;
            var arch = inst?.Owner?.Archetype as MloArchetype;
            if (ents == null) return Array.Empty<int>();
            var rooms = new int[ents.Length];
            for (int i = 0; i < rooms.Length; i++) rooms[i] = -1;
            if (arch?.rooms != null)
                for (int r = 0; r < arch.rooms.Length; r++)
                {
                    var att = arch.rooms[r]?.AttachedObjects;
                    if (att == null) continue;
                    foreach (var idx in att) if (idx < rooms.Length) rooms[idx] = r;
                }
            return rooms;
        }
    }
}

