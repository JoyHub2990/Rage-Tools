using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public enum ExtFieldKind_V72
    {
        Number,
        TimeOfDay,
        ColourArgb,
    }

    public sealed class ExtFieldLimit_V72
    {
        public string Wrapper;
        public string Field;
        public float Min;
        public float Max;
        public float Step;
        public ExtFieldKind_V72 Kind = ExtFieldKind_V72.Number;
        public string Note;
    }

    public static class ExtensionLimits_V72
    {
        private const string Shaft = "MCExtensionDefLightShaft";

        public static readonly ExtFieldLimit_V72[] All =
        {
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "intensity", Min = 0.0f, Max = 50.0f, Step = 0.01f },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "softness", Min = 0.0f, Max = 1.0f, Step = 0.03125f },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "directionAmount", Min = 0.0f, Max = 1.0f, Step = 0.01f },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "length", Min = 0.0f, Max = 99.0f, Step = 0.01f },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "fadeDistanceStart", Min = 0.0f, Max = 999.0f, Step = 0.01f, Note = "metres" },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "fadeDistanceEnd", Min = 0.0f, Max = 999.0f, Step = 0.01f, Note = "metres" },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "flashiness", Min = 0, Max = 20, Step = 1 },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "color", Kind = ExtFieldKind_V72.ColourArgb },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "fadeInTimeStart", Kind = ExtFieldKind_V72.TimeOfDay },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "fadeInTimeEnd", Kind = ExtFieldKind_V72.TimeOfDay },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "fadeOutTimeStart", Kind = ExtFieldKind_V72.TimeOfDay },
            new ExtFieldLimit_V72 { Wrapper = Shaft, Field = "fadeOutTimeEnd", Kind = ExtFieldKind_V72.TimeOfDay },

            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefParticleEffect", Field = "scale", Min = 0.0f, Max = 10.0f, Step = 0.01f },
            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefParticleEffect", Field = "probability", Min = 0, Max = 100, Step = 1, Note = "per cent" },
            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefParticleEffect", Field = "color", Kind = ExtFieldKind_V72.ColourArgb },
            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefWindDisturbance", Field = "strength", Min = 0.0f, Max = 20.0f, Step = 0.1f },
            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefProcObject", Field = "radiusInner", Min = 0.0f, Max = 100.0f, Step = 0.01f, Note = "metres" },
            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefProcObject", Field = "radiusOuter", Min = 0.0f, Max = 100.0f, Step = 0.01f, Note = "metres" },
            new ExtFieldLimit_V72 { Wrapper = "MCExtensionDefProcObject", Field = "spacing", Min = 0.0f, Max = 50.0f, Step = 0.01f, Note = "metres" },
        };

        public static ExtFieldLimit_V72 For(MetaWrapper w, string field)
        {
            if (w == null || string.IsNullOrEmpty(field)) return null;
            var wn = w.GetType().Name;
            return All.FirstOrDefault(x => x.Wrapper == wn && x.Field == field);
        }

        public static float ClampHours_V72(float hours)
        {
            if (float.IsNaN(hours)) return 0.0f;
            while (hours < 0.0f) hours += 24.0f;
            while (hours >= 24.0f) hours -= 24.0f;
            return hours;
        }

        public static int HoursToMinutes_V72(float hours) =>
            Math.Min(1439, Math.Max(0, (int)Math.Round(ClampHours_V72(hours) * 60.0f)));

        public static float MinutesToHours_V72(int minutes) =>
            Math.Min(1439, Math.Max(0, minutes)) / 60.0f;

        public static string TimeText_V72(float hours)
        {
            int m = HoursToMinutes_V72(hours);
            return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
        }

        public static void UnpackArgb_V72(uint packed, out float r, out float g, out float b, out float a)
        {
            a = ((packed >> 24) & 0xFF) / 255.0f;
            r = ((packed >> 16) & 0xFF) / 255.0f;
            g = ((packed >> 8) & 0xFF) / 255.0f;
            b = (packed & 0xFF) / 255.0f;
        }

        public static uint PackArgb_V72(float r, float g, float b, float a)
        {
            uint B(float v) => (uint)Math.Min(255, Math.Max(0, (int)Math.Round(v * 255.0f)));
            return (B(a) << 24) | (B(r) << 16) | (B(g) << 8) | B(b);
        }

        public static object Clamp_V72(ExtFieldLimit_V72 lim, object value)
        {
            if (lim == null || value == null || lim.Kind != ExtFieldKind_V72.Number) return value;
            try
            {
                switch (value)
                {
                    case float f: return Math.Min(lim.Max, Math.Max(lim.Min, f));
                    case byte bt: return (byte)Math.Min(lim.Max, Math.Max(lim.Min, bt));
                    case ushort us: return (ushort)Math.Min(lim.Max, Math.Max(lim.Min, us));
                    case uint ui: return (uint)Math.Min(lim.Max, Math.Max(lim.Min, ui));
                    case int iv: return (int)Math.Min(lim.Max, Math.Max(lim.Min, iv));
                }
            }
            catch { }
            return value;
        }

        public static int SelfTest_V72(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var shaftType = ArchetypeExtensions_V62.Types.FirstOrDefault(t => t.Wrapper == Shaft);
            if (shaftType == null) { Chk("v72 limits: light shaft type", false, "missing"); return fails; }
            var w = ArchetypeExtensions_V62.Create(shaftType, "v72_limits");

            var wanted = new[]
            {
                "intensity", "softness", "directionAmount", "length",
                "fadeDistanceStart", "fadeDistanceEnd", "flashiness", "color",
                "fadeInTimeStart", "fadeInTimeEnd", "fadeOutTimeStart", "fadeOutTimeEnd",
            };
            var missing = wanted.Where(f => For(w, f) == null).ToArray();
            Chk("v72 limits: every light shaft value the table lists has its range",
                missing.Length == 0, missing.Length == 0 ? wanted.Length + " fields" : "missing " + string.Join(", ", missing));

            var inten = For(w, "intensity");
            Chk("v72 limits: intensity is 0 to 50 in hundredths",
                Math.Abs(inten.Min) < 0.001f && Math.Abs(inten.Max - 50f) < 0.001f && Math.Abs(inten.Step - 0.01f) < 1e-6f,
                $"{inten.Min}..{inten.Max} step {inten.Step}");

            var soft = For(w, "softness");
            Chk("v72 limits: softness steps in thirty-seconds, as the game does",
                Math.Abs(soft.Step - 0.03125f) < 1e-6f && Math.Abs(soft.Max - 1f) < 1e-6f,
                $"{soft.Min}..{soft.Max} step {soft.Step}");

            Chk("v72 limits: a value past the end is pulled back to it",
                Math.Abs((float)Clamp_V72(inten, 90.0f) - 50.0f) < 0.001f &&
                Math.Abs((float)Clamp_V72(inten, -5.0f)) < 0.001f,
                "90 -> " + Clamp_V72(inten, 90.0f) + ", -5 -> " + Clamp_V72(inten, -5.0f));

            Chk("v72 times: the game stores hours, so 6.5 reads as half past six",
                TimeText_V72(6.5f) == "06:30" && TimeText_V72(19.0f) == "19:00" && TimeText_V72(0.0f) == "00:00",
                TimeText_V72(6.5f) + " " + TimeText_V72(19.0f));

            Chk("v72 times: a minute is the smallest step, and 23:59 is the last one",
                HoursToMinutes_V72(MinutesToHours_V72(1439)) == 1439 &&
                TimeText_V72(MinutesToHours_V72(1439)) == "23:59" &&
                HoursToMinutes_V72(MinutesToHours_V72(1)) == 1,
                TimeText_V72(MinutesToHours_V72(1439)));

            UnpackArgb_V72(4294965476u, out float r, out float g, out float b, out float a);
            Chk("v72 colour: the balcony light's colour reads as the warm white it is",
                Math.Abs(a - 1f) < 0.005f && Math.Abs(r - 1f) < 0.005f &&
                Math.Abs(g - 248f / 255f) < 0.005f && Math.Abs(b - 228f / 255f) < 0.005f,
                $"a{(int)(a * 255)} r{(int)(r * 255)} g{(int)(g * 255)} b{(int)(b * 255)}");

            Chk("v72 colour: ...and packs back to exactly the same number",
                PackArgb_V72(r, g, b, a) == 4294965476u, PackArgb_V72(r, g, b, a).ToString());
            return fails;
        }
    }
}
