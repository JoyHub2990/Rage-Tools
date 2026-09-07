using System;
using System.Collections.Generic;
using System.Linq;

namespace RageLightEditor.Editor
{
    public static class LightPresetsBuiltin_V21
    {
        private static LightPresets_V20.Preset_V20 P(string name, string note, byte r, byte g, byte b, float inten, float fall, float exp, byte type,
                                                     float inner = 0, float outer = 45, byte flash = 0, float extent = 0, float corona = 0.3f)
            => new LightPresets_V20.Preset_V20
            {
                Name = name, Note = note, Builtin = true,
                ColorR = r, ColorG = g, ColorB = b,
                Intensity = inten, Falloff = fall, FalloffExponent = exp, Type = type,
                ConeInnerAngle = inner, ConeOuterAngle = outer, Flashiness = flash, ExtentX = extent,
                CoronaSize = corona, CoronaIntensity = 1.0f, CoronaZBias = 0.1f,
                VolumeIntensity = 1.0f, VolumeSizeScale = 1.0f, VolumeOuterColorR = 255, VolumeOuterColorG = 255, VolumeOuterColorB = 255,
                VolumeOuterIntensity = 1.0f, VolumeOuterExponent = 1.0f, ShadowNearClip = 0.05f,
                TimeFlags = Scene.AllHoursTimeFlags,
            };

        public static List<LightPresets_V20.Preset_V20> All() => new List<LightPresets_V20.Preset_V20>
        {
            P("Interior bulb (warm)", "a room light - 2700K, short reach", 255, 214, 170, 4.0f, 6.0f, 8.0f, 1),
            P("Interior bulb (cool)", "office / shop lighting", 235, 244, 255, 5.0f, 7.0f, 8.0f, 1),
            P("Fluorescent tube", "a long cool tube - capsule light", 232, 242, 255, 6.0f, 5.0f, 8.0f, 4, 0, 0, 0, 1.2f),
            P("Garage strip (LED)", "bright white bar under a ceiling", 220, 235, 255, 8.0f, 6.0f, 8.0f, 4, 0, 0, 0, 1.5f),
            P("Street lamp (sodium)", "the orange one, high and wide", 255, 176, 88, 12.0f, 16.0f, 12.0f, 2, 0, 60),
            P("Street lamp (LED)", "the newer white ones", 240, 248, 255, 14.0f, 16.0f, 12.0f, 2, 0, 60),
            P("Wall sconce", "a small downward pool on a wall", 255, 200, 150, 3.0f, 4.0f, 6.0f, 2, 30, 60),
            P("Stage spotlight", "hard, narrow, far", 255, 255, 255, 40.0f, 30.0f, 20.0f, 2, 5, 20),
            P("Vehicle headlight", "narrow, far, cool white", 245, 245, 235, 20.0f, 40.0f, 16.0f, 2, 8, 30),
            P("Neon pink", "small, saturated, no shadow work", 255, 60, 160, 8.0f, 4.0f, 6.0f, 1),
            P("Neon blue", "small, saturated", 60, 120, 255, 8.0f, 4.0f, 6.0f, 1),
            P("Neon green", "small, saturated", 60, 255, 120, 8.0f, 4.0f, 6.0f, 1),
            P("Candle", "tiny, warm, flickering", 255, 150, 60, 1.2f, 2.5f, 4.0f, 1, 0, 45, 15, 0, 0.15f),
            P("Fire barrel", "orange, unsteady", 255, 120, 40, 8.0f, 7.0f, 6.0f, 1, 0, 45, 17),
            P("TV glow", "cold flicker from a screen", 150, 180, 255, 2.0f, 3.0f, 6.0f, 1, 0, 45, 14, 0, 0.0f),
            P("Warning beacon (amber)", "blinks once a second", 255, 170, 20, 12.0f, 10.0f, 8.0f, 1, 0, 45, 3),
            P("Police strobe (red)", "three-phase strobe, phase one", 255, 30, 30, 30.0f, 15.0f, 12.0f, 1, 0, 45, 11),
            P("Police strobe (blue)", "three-phase strobe, phase two", 40, 80, 255, 30.0f, 15.0f, 12.0f, 1, 0, 45, 12),
            P("Emergency red", "alarm / warning, tight and hard", 255, 40, 30, 10.0f, 8.0f, 10.0f, 1),
            P("Underwater teal", "a soft cyan wash", 60, 200, 220, 3.0f, 10.0f, 4.0f, 1, 0, 45, 0, 0, 0.0f),
            P("Moonlight fill", "a cold, dim wash for an exterior", 150, 175, 220, 1.5f, 25.0f, 6.0f, 1, 0, 45, 0, 0, 0.0f),
        };

        public static int Ensure(List<LightPresets_V20.Preset_V20> list)
        {
            if (list == null) return 0;
            int added = 0;
            foreach (var b in All())
            {
                if (list.Any(p => p != null && string.Equals(p.Name, b.Name, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(b);
                added++;
            }
            var mine = list.Where(p => p != null && !p.Builtin).ToList();
            var built = list.Where(p => p != null && p.Builtin).ToList();
            list.Clear();
            list.AddRange(mine);
            list.AddRange(built);
            return added;
        }
    }
}
