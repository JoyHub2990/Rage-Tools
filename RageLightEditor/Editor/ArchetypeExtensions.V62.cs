using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class ArchetypeExtensions_V62
    {
        public sealed class ExtType
        {
            public string Label;
            public string Wrapper;
            public string Hint;
            public Type Type;
            public Type DataType;
        }

        public sealed class Field
        {
            public string Label;
            public PropertyInfo Prop;
            public bool OnWrapper;
        }

        private static List<ExtType> types;

        public static IReadOnlyList<ExtType> Types => types ??= BuildTypes();

        private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            ["LightShaft"] = "A visible beam of light - the shafts through a window or a doorway.",
            ["Ladder"] = "A climbable ladder: the bottom, the top, and which way it faces.",
            ["ParticleEffect"] = "Plays a .ypt effect on this prop - smoke, fire, steam.",
            ["AudioEmitter"] = "A sound emitter carried by the prop.",
            ["AudioCollisionSettings"] = "Which audio material this prop sounds like when it is hit.",
            ["Buoyancy"] = "Makes the prop float.",
            ["Door"] = "Marks the prop as a door the game can open.",
            ["ExplosionEffect"] = "An explosion tied to the prop.",
            ["Expression"] = "Drives an expression - procedural animation - on the drawable.",
            ["LightEffect"] = "Attaches light instances to a placement.",
            ["ProcObject"] = "Scatters procedural objects - grass, rocks - over the prop.",
            ["SpawnPoint"] = "A scenario point: where a ped or vehicle spawns and what it does there.",
            ["SpawnPointOverride"] = "Overrides a scenario point on one placement.",
            ["WindDisturbance"] = "Pushes the wind around - fans, vents, rotor wash.",
            ["ScriptChild"] = "Links the prop to a script entity.",
            ["Decal"] = "A decal projected by this prop.",
            ["Scrollbars"] = "A scrolling text sign.",
            ["WalkDontWalk"] = "A pedestrian crossing signal.",
            ["SwayableEffect"] = "Lets the prop sway in the wind.",
            ["ClimbHandHold"] = "A hand hold the player can climb.",
            ["Light"] = "A light definition carried by the archetype.",
        };

        private static List<ExtType> BuildTypes()
        {
            var found = new List<ExtType>();
            var asm = typeof(MetaWrapper).Assembly;
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || !t.Name.StartsWith("MCExtensionDef", StringComparison.Ordinal)) continue;
                if (!typeof(MetaWrapper).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                var data = t.GetField("_Data", BindingFlags.Public | BindingFlags.Instance);
                if (data == null || !data.FieldType.IsValueType) continue;
                var label = t.Name.Substring("MCExtensionDef".Length);
                found.Add(new ExtType
                {
                    Label = Spaced(label),
                    Wrapper = t.Name,
                    Type = t,
                    DataType = data.FieldType,
                    Hint = Hints.TryGetValue(label, out var h) ? h : "",
                });
            }
            found.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
            return found;
        }

        public static string Spaced(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return pascal;
            var sb = new System.Text.StringBuilder(pascal.Length + 8);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascal[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static MetaWrapper[] Get(Archetype a) => a?.Extensions ?? Array.Empty<MetaWrapper>();

        public static string TypeLabel(MetaWrapper w)
        {
            if (w == null) return "?";
            var n = w.GetType().Name;
            return n.StartsWith("MCExtensionDef", StringComparison.Ordinal)
                ? Spaced(n.Substring("MCExtensionDef".Length)) : n;
        }

        private static FieldInfo DataField(MetaWrapper w) =>
            w?.GetType().GetField("_Data", BindingFlags.Public | BindingFlags.Instance);

        public static string NameOf(MetaWrapper w)
        {
            var f = DataField(w);
            var data = f?.GetValue(w);
            var p = data?.GetType().GetProperty("name");
            if (p == null || p.PropertyType != typeof(MetaHash)) return "";
            return ((MetaHash)p.GetValue(data)).ToString();
        }

        public static List<Field> Fields(MetaWrapper w)
        {
            var list = new List<Field>();
            if (w == null) return list;
            foreach (var p in w.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.PropertyType != typeof(string)) continue;
                list.Add(new Field { Label = Spaced(p.Name), Prop = p, OnWrapper = true });
            }
            var f = DataField(w);
            if (f == null) return list;
            foreach (var p in f.FieldType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (p.Name.StartsWith("Unused", StringComparison.OrdinalIgnoreCase)) continue;
                if (list.Any(x => x.OnWrapper && string.Equals(x.Prop.Name, p.Name, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new Field { Label = Spaced(p.Name), Prop = p });
            }
            return list;
        }

        public static object GetValue(MetaWrapper w, Field field)
        {
            if (field == null || w == null) return null;
            if (field.OnWrapper) return field.Prop.GetValue(w) ?? "";
            var f = DataField(w);
            var data = f?.GetValue(w);
            return data == null ? null : field.Prop.GetValue(data);
        }

        public static void SetValue(MetaWrapper w, Field field, object value)
        {
            if (field == null || w == null) return;
            if (field.OnWrapper) { field.Prop.SetValue(w, value); return; }
            var f = DataField(w);
            if (f == null) return;
            var boxed = f.GetValue(w);
            if (boxed == null) return;
            field.Prop.SetValue(boxed, value);
            f.SetValue(w, boxed);
        }

        public static MetaWrapper Create(ExtType t, string name)
        {
            if (t == null) return null;
            var w = (MetaWrapper)Activator.CreateInstance(t.Type);
            var f = DataField(w);
            if (f != null)
            {
                var boxed = Activator.CreateInstance(t.DataType);
                var np = t.DataType.GetProperty("name");
                if (np != null && np.PropertyType == typeof(MetaHash) && !string.IsNullOrEmpty(name))
                {
                    JenkIndex.Ensure(name);
                    np.SetValue(boxed, new MetaHash(JenkHash.GenHash(name)));
                }
                f.SetValue(w, boxed);
            }
            return w;
        }

        public static MetaWrapper Clone_V69(MetaWrapper w)
        {
            if (w == null) return null;
            var copy = (MetaWrapper)Activator.CreateInstance(w.GetType());
            var f = DataField(w);
            if (f != null) f.SetValue(copy, f.GetValue(w));
            return copy;
        }

        public static void Add(Archetype a, MetaWrapper w)
        {
            if (a == null || w == null) return;
            var list = new List<MetaWrapper>(a.Extensions ?? Array.Empty<MetaWrapper>()) { w };
            a.Extensions = list.ToArray();
        }

        public static bool Remove(Archetype a, int index)
        {
            var cur = a?.Extensions;
            if (cur == null || index < 0 || index >= cur.Length) return false;
            var list = new List<MetaWrapper>(cur);
            list.RemoveAt(index);
            a.Extensions = list.ToArray();
            return true;
        }

        public static int SelfTest_V62(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var all = Types;
            // the whole set the format defines a wrapper for, which is the same set Sollumz edits
            var want = new[]
            {
                "AudioCollisionSettings", "AudioEmitter", "Buoyancy", "Door", "ExplosionEffect",
                "Expression", "Ladder", "LightEffect", "LightShaft", "ParticleEffect",
                "ProcObject", "SpawnPoint", "SpawnPointOverride", "WindDisturbance",
            };
            var missing = want.Where(n => !all.Any(t => t.Wrapper == "MCExtensionDef" + n)).ToArray();
            Chk("v62 extensions: every extension type the format supports is offered",
                missing.Length == 0 && all.Count >= want.Length,
                missing.Length == 0 ? all.Count + " types, all of them" : "missing " + string.Join(", ", missing));

            var shaft = all.FirstOrDefault(t => t.Wrapper == "MCExtensionDefLightShaft");
            var ladder = all.FirstOrDefault(t => t.Wrapper == "MCExtensionDefLadder");
            Chk("v62 extensions: light shafts and ladders are among them",
                shaft != null && ladder != null, shaft != null && ladder != null ? "both" : "missing");
            if (shaft == null || ladder == null) return fails;

            var w = Create(shaft, "rle_v62_shaft");
            Chk("v62 extensions: a new light shaft is created with its name set",
                w != null && NameOf(w) == "rle_v62_shaft", NameOf(w));

            var fields = Fields(w);
            Chk("v62 extensions: its editable fields are found, and the format padding is not",
                fields.Count >= 10 && !fields.Any(f => f.Prop.Name.StartsWith("Unused")),
                fields.Count + " fields: " + string.Join(", ", fields.Take(4).Select(f => f.Label)));

            var lenField = fields.FirstOrDefault(f => f.Prop.Name == "length");
            if (lenField != null)
            {
                SetValue(w, lenField, 12.5f);
                Chk("v62 extensions: editing a value survives the struct round trip",
                    Math.Abs((float)GetValue(w, lenField) - 12.5f) < 0.001f,
                    GetValue(w, lenField)?.ToString());
            }

            var dirField = fields.FirstOrDefault(f => f.Prop.Name == "direction");
            if (dirField != null)
            {
                SetValue(w, dirField, new Vector3(0, 0, -1));
                var v = (Vector3)GetValue(w, dirField);
                Chk("v62 extensions: ...including a vector field", Math.Abs(v.Z + 1f) < 0.001f, v.ToString());
            }

            var arch = new Archetype();
            Add(arch, w);
            Add(arch, Create(ladder, "rle_v62_ladder"));
            Chk("v62 extensions: they attach to an archetype", Get(arch).Length == 2,
                Get(arch).Length + " on the archetype");

            Remove(arch, 0);
            Chk("v62 extensions: ...and one can be removed, leaving the rest",
                Get(arch).Length == 1 && TypeLabel(Get(arch)[0]) == "Ladder",
                Get(arch).Length == 1 ? TypeLabel(Get(arch)[0]) : "wrong count");
            return fails;
        }
    }
}
