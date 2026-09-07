using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class ExtPreset_V70
    {
        public string Wrapper;
        public string Name;
        public string What;
        public Dictionary<string, object> Values = new Dictionary<string, object>();
    }

    public static class ExtensionPresets_V70
    {
        public static readonly ExtPreset_V70[] All =
        {
            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefLightShaft", Name = "Window or doorway",
                What = "A bright shaft through a window or an open door - the game's balcony light.",
                Values =
                {
                    ["length"] = 1.68f, ["intensity"] = 10.0f, ["softness"] = 1.0f,
                    ["directionAmount"] = 0.0f, ["flags"] = 35u, ["color"] = 4294965476u,
                    ["densityType"] = "LIGHTSHAFT_DENSITYTYPE_QUADRATIC_GRADIENT",
                    ["volumeType"] = "LIGHTSHAFT_VOLUMETYPE_SHAFT", ["scaleBySunIntensity"] = (byte)1,
                },
            },
            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefLightShaft", Name = "Small interior glow",
                What = "Short and dim, hard-edged, pointing where you aim it - Franklin's apartment.",
                Values =
                {
                    ["length"] = 0.9f, ["intensity"] = 2.0f, ["softness"] = 0.0f,
                    ["directionAmount"] = 1.0f, ["flags"] = 35u, ["color"] = 4294967295u,
                    ["densityType"] = "LIGHTSHAFT_DENSITYTYPE_QUADRATIC_GRADIENT",
                    ["volumeType"] = "LIGHTSHAFT_VOLUMETYPE_SHAFT", ["scaleBySunIntensity"] = (byte)1,
                },
            },
            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefLightShaft", Name = "Long soft shaft",
                What = "Ten metres with soft-shadow density - a skylight or a tall warehouse window.",
                Values =
                {
                    ["length"] = 10.0f, ["intensity"] = 5.0f, ["softness"] = 1.0f,
                    ["directionAmount"] = 0.5f, ["flags"] = 99u, ["color"] = 4294961586u,
                    ["densityType"] = "LIGHTSHAFT_DENSITYTYPE_SOFT_SHADOW",
                    ["volumeType"] = "LIGHTSHAFT_VOLUMETYPE_SHAFT", ["scaleBySunIntensity"] = (byte)1,
                },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefLadder", Name = "Metal ladder",
                What = "The rooftop air-conditioner ladder: metal, climbable off either end.",
                Values =
                {
                    ["materialType"] = "METAL_SOLID_LADDER", ["template"] = "default",
                    ["canGetOffAtTop"] = (byte)1, ["canGetOffAtBottom"] = (byte)1,
                    ["normal"] = new Vector3(0, -1, 0),
                },
            },
            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefLadder", Name = "Exit at the top only",
                What = "Same ladder, but you can only climb off where it ends - a wall or a pit ladder.",
                Values =
                {
                    ["materialType"] = "METAL_SOLID_LADDER", ["template"] = "default",
                    ["canGetOffAtTop"] = (byte)1, ["canGetOffAtBottom"] = (byte)0,
                },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefParticleEffect", Name = "Small, always on",
                What = "Half scale at full probability, no tint - the prologue door's effect.",
                Values =
                {
                    ["scale"] = 0.5f, ["probability"] = 100u, ["flags"] = 0u,
                    ["color"] = 4294967295u, ["fxType"] = 0u, ["boneTag"] = 0u,
                },
            },
            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefParticleEffect", Name = "Full size",
                What = "The same, at the effect's own scale.",
                Values = { ["scale"] = 1.0f, ["probability"] = 100u, ["flags"] = 0u, ["color"] = 4294967295u },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefSpawnPoint", Name = "Seat (chair or bench)",
                What = "The office side-chair's scenario: a ped sits here, in story and online.",
                Values =
                {
                    ["spawnType"] = 2868304871u, ["pedType"] = 3744729013u,
                    ["availableInMpSp"] = "kBoth", ["probability"] = 0.0f, ["radius"] = 0.0f,
                    ["timeTillPedLeaves"] = 0.0f, ["flags"] = 0u, ["highPri"] = (byte)0,
                    ["extendedRange"] = (byte)0, ["shortRange"] = (byte)0,
                    ["start"] = (byte)0, ["end"] = (byte)0, ["group"] = 0u, ["interior"] = 0u,
                },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefWindDisturbance", Name = "Fan or vent",
                What = "The tailor's sewing-shop fan: one direction, strength 7.",
                Values =
                {
                    ["disturbanceType"] = 0, ["strength"] = 7.0f, ["flags"] = 1u,
                    ["size"] = new Vector4(1, 0, 0, 0), ["boneTag"] = 0,
                },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefProcObject", Name = "Litter around a bin",
                What = "The street bin's scatter: a small ring, spaced 1.3 m, unscaled.",
                Values =
                {
                    ["radiusInner"] = 0.236719f, ["radiusOuter"] = 0.54126f, ["spacing"] = 1.3f,
                    ["minScale"] = 1.0f, ["maxScale"] = 1.0f, ["minScaleZ"] = 1.0f, ["maxScaleZ"] = 1.0f,
                    ["minZOffset"] = 0.0f, ["maxZOffset"] = 0.0f, ["flags"] = 0u,
                },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefAudioCollisionSettings", Name = "Soft furniture",
                What = "What the office lazy chair sounds like when something hits it.",
                Values = { ["settings"] = 3009447484u },
            },
            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefAudioCollisionSettings", Name = "Hard cabinet",
                What = "The server cabinet's collision sound.",
                Values = { ["settings"] = 127027908u },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefExplosionEffect", Name = "Machine sparks",
                What = "The welding machine's explosion effect, on its own bone.",
                Values = { ["boneTag"] = 58187, ["explosionTag"] = 0, ["explosionType"] = 0, ["flags"] = 2u },
            },

            new ExtPreset_V70
            {
                Wrapper = "MCExtensionDefDoor", Name = "Plain door",
                What = "A door the game can swing, not locked open.",
                Values = { ["enableLimitAngle"] = (byte)0, ["startsLocked"] = (byte)0, ["doorTargetRatio"] = 0.0f },
            },
        };

        public static IEnumerable<ExtPreset_V70> For(MetaWrapper w) =>
            w == null ? Enumerable.Empty<ExtPreset_V70>()
                      : All.Where(p => p.Wrapper == w.GetType().Name);

        public static bool Apply(MetaWrapper w, ExtPreset_V70 p)
        {
            if (w == null || p == null || w.GetType().Name != p.Wrapper) return false;
            var fields = ArchetypeExtensions_V62.Fields(w);
            int set = 0;
            foreach (var kv in p.Values)
            {
                var f = fields.FirstOrDefault(x => x.Prop.Name == kv.Key);
                if (f == null) continue;
                try
                {
                    object v = kv.Value;
                    var pt = f.Prop.PropertyType;
                    if (pt.IsEnum && v is string sv) v = Enum.Parse(pt, sv);
                    else if (pt == typeof(MetaHash) && v is string ms) { JenkIndex.Ensure(ms); v = new MetaHash(JenkHash.GenHash(ms)); }
                    else if (pt == typeof(MetaHash) && v is uint mu) v = new MetaHash(mu);
                    else if (pt == typeof(Vector3) || pt == typeof(Vector4)) { }
                    else if (!pt.IsInstanceOfType(v)) v = Convert.ChangeType(v, pt);
                    ArchetypeExtensions_V62.SetValue(w, f, v);
                    set++;
                }
                catch { }
            }
            return set > 0;
        }

        public static int SelfTest_V70(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var typesWithPresets = All.Select(p => p.Wrapper).Distinct().ToList();
            Chk("v70 presets: the types you actually dress up all have presets",
                typesWithPresets.Count >= 8, typesWithPresets.Count + " types: " +
                string.Join(", ", typesWithPresets.Select(t => t.Substring("MCExtensionDef".Length))));

            int applied = 0, checkedTypes = 0;
            foreach (var wrapper in typesWithPresets)
            {
                var type = ArchetypeExtensions_V62.Types.FirstOrDefault(t => t.Wrapper == wrapper);
                if (type == null) continue;
                checkedTypes++;
                var w = ArchetypeExtensions_V62.Create(type, "v70_test");
                var preset = All.First(p => p.Wrapper == wrapper);
                if (Apply(w, preset)) applied++;
                else check("v70 presets: " + wrapper, false, "nothing applied");
            }
            Chk("v70 presets: every one of them lands on a fresh extension",
                applied == checkedTypes && checkedTypes > 0, $"{applied} of {checkedTypes}");

            var shaftType = ArchetypeExtensions_V62.Types.FirstOrDefault(t => t.Wrapper == "MCExtensionDefLightShaft");
            var shaft = ArchetypeExtensions_V62.Create(shaftType, "v70_shaft");
            var cornerA = ArchetypeExtensions_V62.Fields(shaft).First(f => f.Prop.Name == "cornerA");
            ArchetypeExtensions_V62.SetValue(shaft, cornerA, new Vector3(7, 8, 9));
            Apply(shaft, All.First(p => p.Wrapper == "MCExtensionDefLightShaft"));
            var back = (Vector3)ArchetypeExtensions_V62.GetValue(shaft, cornerA);
            Chk("v70 presets: none of them move a point you placed",
                (back - new Vector3(7, 8, 9)).Length() < 0.0001f, back.ToString());

            var ladderType = ArchetypeExtensions_V62.Types.FirstOrDefault(t => t.Wrapper == "MCExtensionDefLadder");
            var ladder = ArchetypeExtensions_V62.Create(ladderType, "v70_ladder");
            Apply(ladder, All.First(p => p.Wrapper == "MCExtensionDefLadder" && p.Name == "Exit at the top only"));
            var bottomOff = ArchetypeExtensions_V62.Fields(ladder).FirstOrDefault(f => f.Prop.Name == "canGetOffAtBottom");
            Chk("v70 presets: ...and a preset that turns something off really turns it off",
                bottomOff != null && Convert.ToInt32(ArchetypeExtensions_V62.GetValue(ladder, bottomOff)) == 0,
                ArchetypeExtensions_V62.GetValue(ladder, bottomOff)?.ToString());
            return fails;
        }
    }
}
