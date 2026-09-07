using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        public int InteriorSetsMode
        {
            get => interiorSetsMode;
            set
            {
                value = Math.Clamp(value, 0, 2);
                if (value == interiorSetsMode) return;
                interiorSetsMode = value;
                autoSets = new ConditionalWeakTable<MloInstanceData, HashSet<MloInstanceEntitySet>>();
                worldChanged = true;
            }
        }
        private int interiorSetsMode = int.TryParse(Environment.GetEnvironmentVariable("RLE_INTSETS"), out int envMode) ? Math.Clamp(envMode, 0, 2) : 1;
        private ConditionalWeakTable<MloInstanceData, HashSet<MloInstanceEntitySet>> autoSets =
            new ConditionalWeakTable<MloInstanceData, HashSet<MloInstanceEntitySet>>();
        public int InteriorSetsAutoOn { get; private set; }
        public int InteriorSetsDecided { get; private set; }
        public readonly Dictionary<string, string> InteriorSetsReport = new Dictionary<string, string>();

        private bool IsInteriorSetAutoVisible(MloInstanceData inst, MloInstanceEntitySet set)
        {
            if (interiorSetsMode == 0 || inst == null || set == null) return false;
            if (interiorSetsMode == 2) return true;
            if (!autoSets.TryGetValue(inst, out var chosen))
            {
                chosen = ChooseAutoSets(inst);
                try { autoSets.Add(inst, chosen); } catch { }
                InteriorSetsAutoOn += chosen.Count;
                InteriorSetsDecided++;
            }
            return chosen.Contains(set);
        }

        private static readonly string[] ConstantWords = { "constant", "default", "shared", "shell" };
        private static readonly string[] StateWords =
        {
            "basic", "standard", "plain", "low", "small", "closed", "off", "down", "clean", "tidy", "empty",
            "open", "on", "up", "medium", "high", "large", "upgrade", "upgraded", "full", "dirty", "messy",
            "modern", "retro", "traditional", "urban", "branded", "beams", "flat", "mirror", "setup", "production",
        };
        private static readonly string[] LoneStateWords = { "swap", "moved", "broken", "damaged", "destroyed", "dmg", "heist", "mission", "cutscene", "cs", "alt", "alternate", "hidden", "removed" };
        private static bool IsLoneState(SetInfo si)
        {
            int cut = si.Name.LastIndexOf('_');
            string raw = cut >= 0 ? si.Name.Substring(cut + 1) : si.Name;
            return Array.IndexOf(StateWords, raw) >= 0 || Array.IndexOf(LoneStateWords, raw) >= 0 ||
                   si.Name.Contains("heist") || si.Name.Contains("mission") || si.Name.Contains("cutscene");
        }
        private static readonly string[] AspectWords = { "ceiling", "wall", "walls", "floor", "style", "theme", "design", "pattern", "tint", "lighting", "light", "lights", "decor", "art", "furnishings", "wallpaper", "wpaper", "colour", "color", "carpet" };
        private static readonly Regex GluedNumber = new Regex(@"^(?<word>[a-z]+)(?<num>\d+)$", RegexOptions.Compiled);
        private static readonly Regex GluedLetter = new Regex(@"^(?<word>[a-z]{3,})(?<letter>[a-z])$", RegexOptions.Compiled);
        private static readonly Regex NumberToken = new Regex(@"^\d+[a-z]?$", RegexOptions.Compiled);
        private static readonly Regex AllDigits = new Regex(@"^\d+$", RegexOptions.Compiled);

        private sealed class SetInfo
        {
            public int Index;
            public MloInstanceEntitySet Set;
            public string Name;
            public string Norm;
            public string Key;
            public string Last;
            public int Count;
            public bool Placed;
            public bool Hashed;
            public string Footprint;
            public bool On;
            public string Why = "";
        }

        private HashSet<MloInstanceEntitySet> ChooseAutoSets(MloInstanceData inst)
        {
            var chosen = new HashSet<MloInstanceEntitySet>();
            var sets = inst.EntitySets;
            if (sets == null || sets.Length == 0) return chosen;
            var infos = new List<SetInfo>(sets.Length);
            for (int i = 0; i < sets.Length; i++)
            {
                var s = sets[i];
                if (s == null) continue;
                var name = s.EntitySet?.Name ?? s.EntitySet?._Data.name.ToString() ?? "";
                infos.Add(MakeSetInfo(i, name, s.Entities?.Count ?? 0, s.Visible, s.Locations, s));
            }
            var ownerMlo = inst.Owner?.Archetype as MloArchetype;
            DecideAutoSets(infos, IsDlcInterior(ownerMlo), ownerMlo);
            foreach (var si in infos) if (si.On) chosen.Add(si.Set);
            string mlo = inst.Owner?.Archetype?.Name ?? inst.Owner?._CEntityDef.archetypeName.ToString() ?? "?";
            var line = IntSetsLine(mlo, inst.Owner?.Ymap?.Name, infos);
            InteriorSetsReport[mlo] = line;
            Console.WriteLine(line);
            return chosen;
        }

        public static string DescribeAutoSets(MloArchetype mlo)
        {
            var infos = DecideArchetypeSets(mlo);
            return infos == null ? "" : IntSetsLine(mlo.Name, "(archetype)", infos);
        }

        public static List<string> AutoSetNames(MloArchetype mlo)
        {
            var names = new List<string>();
            var infos = DecideArchetypeSets(mlo);
            if (infos != null) foreach (var si in infos) if (si.On) names.Add(si.Name);
            return names;
        }

        private static List<SetInfo> DecideArchetypeSets(MloArchetype mlo)
        {
            var sets = mlo?.entitySets;
            if (sets == null) return null;
            var infos = new List<SetInfo>();
            for (int i = 0; i < sets.Length; i++)
            {
                var es = sets[i];
                if (es == null) continue;
                infos.Add(MakeSetInfo(i, es.Name ?? es._Data.name.ToString(), es.Entities?.Length ?? 0, false, es.Locations, null));
            }
            DecideAutoSets(infos, IsDlcInterior(mlo), mlo);
            return infos;
        }

        private static SetInfo MakeSetInfo(int index, string rawName, int count, bool placed, uint[] locations, MloInstanceEntitySet set)
        {
            var name = (rawName ?? "").ToLowerInvariant();
            var si = new SetInfo { Index = index, Set = set, Name = name, Count = count, Placed = placed };
            si.Hashed = name.Length == 0 || AllDigits.IsMatch(name);
            if (!si.Hashed)
            {
                si.Norm = Normalise(name);
                int cut = si.Norm.LastIndexOf('_');
                si.Last = cut >= 0 ? si.Norm.Substring(cut + 1) : si.Norm;
                si.Key = cut > 0 ? si.Norm.Substring(0, cut) : null;
            }
            else si.Footprint = FootprintOf(locations);
            return si;
        }

        private static bool IsDlcInterior(MloArchetype mlo)
        {
            var name = mlo?.Name ?? "";
            if (IsCustomInterior_N1(mlo)) return false;
            return !name.StartsWith("v_", StringComparison.OrdinalIgnoreCase);
        }

        private static void DecideAutoSets(List<SetInfo> infos, bool dlcInterior = false, MloArchetype mlo = null)
        {
            foreach (var si in infos) if (si.Placed) { si.On = true; si.Why = "placed"; }
            foreach (var si in infos)
                if (!si.On && !si.Hashed && si.Count > 0 && HasWord(si.Name, ConstantWords)) { si.On = true; si.Why = "constant"; }
            var byName = new Dictionary<string, SetInfo>();
            foreach (var si in infos) if (!si.Hashed && !byName.ContainsKey(si.Name)) byName[si.Name] = si;
            var decided = new HashSet<SetInfo>();
            foreach (var si in infos)
            {
                if (si.Hashed || decided.Contains(si)) continue;
                int no = si.Name.IndexOf("no_", StringComparison.Ordinal);
                if (no < 0 || (no > 0 && si.Name[no - 1] != '_')) continue;
                var other = si.Name.Remove(no, 3);
                if (!byName.TryGetValue(other, out var yes) || decided.Contains(yes)) continue;
                decided.Add(si); decided.Add(yes);
                if (yes.On && !si.On) { si.Why = "off: " + yes.Name + " placed"; continue; }
                if (!si.On) { si.On = true; si.Why = "the plain state of " + yes.Name; }
                if (!yes.On) yes.Why = "off: " + si.Name + " stands";
                yes.Key = null; si.Key = null;
            }
            var families = new Dictionary<string, List<SetInfo>>();
            foreach (var si in infos)
            {
                if (si.Hashed || decided.Contains(si) || si.Key == null || si.Count == 0) continue;
                bool alt = NumberToken.IsMatch(si.Last) || si.Last.Length == 1 || Array.IndexOf(StateWords, si.Last) >= 0 || EndsWithWord(si.Key, AspectWords);
                if (!alt) continue;
                if (!families.TryGetValue(si.Key, out var l)) families[si.Key] = l = new List<SetInfo>(4);
                l.Add(si);
            }
            var chosenMembers = new List<SetInfo>();
            var rejectedMembers = new List<SetInfo>();
            SettleFamilies(families, decided, chosenMembers, rejectedMembers, false);
            var familiesF = new Dictionary<string, List<SetInfo>>();
            foreach (var si in infos)
            {
                if (si.Hashed || decided.Contains(si) || si.Count == 0) continue;
                int cut = si.Name.IndexOf('_');
                if (cut <= 0 || cut >= si.Name.Length - 1) continue;
                string first = si.Name.Substring(0, cut);
                if (Array.IndexOf(StateWords, first) < 0) continue;
                string keyF = si.Name.Substring(cut + 1);
                if (!familiesF.TryGetValue(keyF, out var l)) familiesF[keyF] = l = new List<SetInfo>(4);
                l.Add(si);
            }
            SettleFamilies(familiesF, decided, chosenMembers, rejectedMembers, true);
            var keyGroups = new Dictionary<string, List<SetInfo>>();
            foreach (var m in chosenMembers)
            {
                if (m.Placed || m.Key == null || m.Why == "constant") continue;
                if (m.Last != null && Array.IndexOf(StateWords, m.Last) >= 0) continue;
                var toks = m.Key.Split('_');
                bool numbered = false;
                for (int t = 0; t < toks.Length; t++) if (AllDigits.IsMatch(toks[t])) { toks[t] = "#"; numbered = true; }
                if (!numbered) continue;
                var g = string.Join("_", toks);
                if (!keyGroups.TryGetValue(g, out var l)) keyGroups[g] = l = new List<SetInfo>(4);
                l.Add(m);
            }
            foreach (var kv in keyGroups)
            {
                var l = kv.Value;
                if (l.Count < 2) continue;
                l.Sort((a, b) => { long va = FirstNumberIn(a.Key), vb = FirstNumberIn(b.Key); return va != vb ? va.CompareTo(vb) : a.Index.CompareTo(b.Index); });
                for (int i = 1; i < l.Count; i++)
                {
                    var m = l[i];
                    m.On = false; m.Why = "off: " + l[0].Name + " stands for the numbered " + kv.Key + " families";
                    chosenMembers.Remove(m); rejectedMembers.Add(m);
                }
            }
            foreach (var si in infos)
            {
                if (si.Hashed || decided.Contains(si) || si.On) continue;
                foreach (var m in chosenMembers) if (si.Name.StartsWith(m.Name + "_", StringComparison.Ordinal)) { si.On = true; si.Why = "goes with " + m.Name; decided.Add(si); break; }
                if (si.On) continue;
                foreach (var m in rejectedMembers) if (si.Name.StartsWith(m.Name + "_", StringComparison.Ordinal)) { si.Why = "off: goes with " + m.Name; decided.Add(si); break; }
            }
            var footprints = new Dictionary<string, SetInfo>();
            foreach (var si in infos)
            {
                if (!si.Hashed || si.Count == 0) continue;
                if (si.On) { footprints[si.Footprint] = si; continue; }
                if (footprints.TryGetValue(si.Footprint, out var first)) { si.Why = "off: hashed name, " + first.Name + " already dresses rooms " + si.Footprint; continue; }
                footprints[si.Footprint] = si;
                si.On = true; si.Why = "hashed name, first to dress rooms " + si.Footprint;
            }
            foreach (var si in infos)
            {
                if (si.On || si.Why.Length > 0 || si.Hashed || si.Count == 0 || decided.Contains(si)) continue;
                if (dlcInterior) { si.Why = "off: lone option of a DLC interior"; continue; }
                if (IsLoneState(si)) { si.Why = "off: lone set named as a state"; continue; }
                si.On = true; si.Why = "lone item set, additive";
            }
            NoEmptyRoomSets_N1(infos, mlo);
            foreach (var si in infos) if (!si.On && si.Why.Length == 0) si.Why = si.Count == 0 ? "off: empty" : "off: no family, not constant";
        }

        private static void SettleFamilies(Dictionary<string, List<SetInfo>> families, HashSet<SetInfo> decided, List<SetInfo> chosenMembers, List<SetInfo> rejectedMembers, bool byFirstToken)
        {
            foreach (var kv in families)
            {
                var l = kv.Value;
                if (l.Count < 2) continue;
                SetInfo best = null;
                foreach (var m in l) if (m.Placed) { best = m; break; }
                if (best == null)
                {
                    if (byFirstToken) l.Sort((a, b) => CompareFirstTokens(a, b)); else l.Sort((a, b) => CompareMembers(a, b));
                    best = l[0];
                }
                string label = byFirstToken ? "*_" + kv.Key : kv.Key + "_*";
                foreach (var m in l)
                {
                    decided.Add(m);
                    if (m == best) { if (!m.On) { m.On = true; m.Why = "family " + label + ": " + l.Count + " members, this one stands"; } chosenMembers.Add(m); }
                    else if (m.On) { }
                    else { m.Why = "off: " + best.Name + " stands for " + label; rejectedMembers.Add(m); }
                }
            }
        }
        private static int CompareFirstTokens(SetInfo a, SetInfo b)
        {
            string fa = a.Name.Substring(0, a.Name.IndexOf('_')), fb = b.Name.Substring(0, b.Name.IndexOf('_'));
            int sa = Array.IndexOf(StateWords, fa), sb = Array.IndexOf(StateWords, fb);
            if (sa != sb) return sa - sb;
            return a.Index.CompareTo(b.Index);
        }

        private static int CompareMembers(SetInfo a, SetInfo b)
        {
            bool na = NumberToken.IsMatch(a.Last), nb = NumberToken.IsMatch(b.Last);
            if (na && nb)
            {
                long va = ParseLeadingNumber(a.Last), vb = ParseLeadingNumber(b.Last);
                if (va != vb) return va.CompareTo(vb);
                return string.CompareOrdinal(a.Last, b.Last);
            }
            if (na != nb) return na ? -1 : 1;
            int sa = Array.IndexOf(StateWords, a.Last), sb = Array.IndexOf(StateWords, b.Last);
            if (sa >= 0 && sb >= 0 && sa != sb) return sa - sb;
            if ((sa >= 0) != (sb >= 0)) return sa >= 0 ? -1 : 1;
            int c = string.CompareOrdinal(a.Last, b.Last);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        }
        private static long FirstNumberIn(string key)
        {
            foreach (var t in key.Split('_')) if (AllDigits.IsMatch(t)) return ParseLeadingNumber(t);
            return long.MaxValue;
        }
        private static long ParseLeadingNumber(string s)
        {
            long v = 0; foreach (var ch in s) { if (ch < '0' || ch > '9') break; v = v * 10 + (ch - '0'); }
            return v;
        }
        private static string Normalise(string name)
        {
            int cut = name.LastIndexOf('_');
            string head = cut >= 0 ? name.Substring(0, cut + 1) : "";
            string last = cut >= 0 ? name.Substring(cut + 1) : name;
            var m = GluedNumber.Match(last);
            if (m.Success) return head + m.Groups["word"].Value + "_" + m.Groups["num"].Value;
            m = GluedLetter.Match(last);
            if (m.Success && Array.IndexOf(StateWords, last) < 0 && Array.IndexOf(AspectWords, last) < 0 && Array.IndexOf(ConstantWords, last) < 0)
                return head + m.Groups["word"].Value + "_" + m.Groups["letter"].Value;
            return name;
        }
        private static bool HasWord(string name, string[] words)
        {
            foreach (var w in words) if (name.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
        private static bool EndsWithWord(string key, string[] words)
        {
            int cut = key.LastIndexOf('_');
            string last = cut >= 0 ? key.Substring(cut + 1) : key;
            return Array.IndexOf(words, last) >= 0;
        }
        private static string FootprintOf(uint[] locs)
        {
            if (locs == null || locs.Length == 0) return "?";
            var rooms = new SortedSet<uint>(locs);
            var sb = new StringBuilder();
            foreach (var r in rooms) { if (sb.Length > 0) sb.Append(','); sb.Append(r); }
            return sb.ToString();
        }

        private static string IntSetsLine(string mlo, string ymap, List<SetInfo> infos)
        {
            var sb = new StringBuilder();
            int on = 0; foreach (var si in infos) if (si.On) on++;
            sb.Append($"INTSETS {mlo} ymap={ymap} sets {infos.Count} on {on}:");
            foreach (var si in infos) if (si.On) sb.Append($" ON {si.Name}({si.Count}) [{si.Why}]");
            foreach (var si in infos) if (!si.On) sb.Append($" off {si.Name}({si.Count}) [{si.Why}]");
            return sb.ToString();
        }
    }
}

