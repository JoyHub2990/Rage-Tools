using System;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class ExtensionHelpers
    {
        public const float ShaftMaxStretch_S5 = 4.0f;
        public const float ShaftMaxLength_S5 = 80.0f;
        public const float ShaftDropFloor_S5 = 0.15f;

        public static float ShaftReach_S5(float length, Vector3 stored, Vector3 beam, bool followsSun)
        {
            if (!followsSun || length <= 0.0f) return length;
            if (stored.LengthSquared() <= 1e-8f || beam.LengthSquared() <= 1e-8f) return length;
            var s = Vector3.Normalize(stored);
            var b = Vector3.Normalize(beam);
            float want = length;
            float drop = Math.Abs(s.Z);
            if (drop > ShaftDropFloor_S5)
            {
                float bz = Math.Max(Math.Abs(b.Z), 0.05f);
                want = Math.Max(want, length * drop / bz);
            }
            float along = Math.Max(Vector3.Dot(b, s), ShaftDropFloor_S5);
            want = Math.Max(want, length / along);
            return ShaftReachCap_T5(length, want);
        }

        public static void ShaftReachTest_S5(Action<string, bool, string> check)
        {
            var stored = Vector3.Normalize(new Vector3(-0.20f, 0.68f, -0.70f));
            const float len = 2.08f;
            var noon = Vector3.Normalize(new Vector3(-0.22f, 0.52f, -0.83f));
            float ln = ShaftReach_S5(len, stored, noon, true);
            check("s5 shaft: at the authored angle the length is the author's", ln >= len && ln < len * 1.3f, $"{ln:0.00} m from {len:0.00}");
            var morning = Vector3.Normalize(new Vector3(-0.90f, 0.23f, -0.37f));
            float lm = ShaftReach_S5(len, stored, morning, true);
            check("s5 shaft: a shallower sun makes a longer beam, so it still reaches further",
                  lm > len * 1.3f, $"{lm:0.00} m from {len:0.00}");
            var tipNoon = noon * ln; var tipMorning = morning * lm;
            check("s5 shaft: and its end lands somewhere else", (tipNoon - tipMorning).Length() > 2.0f,
                  $"{(tipNoon - tipMorning).Length():0.00} m apart");
            check("s5 shaft: both ends are near the same floor", Math.Abs(tipNoon.Z - tipMorning.Z) < 0.75f,
                  $"{tipNoon.Z:0.00} vs {tipMorning.Z:0.00}");
            var graze = Vector3.Normalize(new Vector3(-0.99f, 0.10f, -0.02f));
            float lg = ShaftReach_S5(len, stored, graze, true);
            check("s5 shaft: a grazing sun is capped, not stretched across the district",
                  lg <= len * ShaftMaxStretch_S5 + 1e-3f && lg <= ShaftMaxLength_S5, $"{lg:0.00} m");
            var level = Vector3.Normalize(new Vector3(1.0f, 0.0f, 0.0f));
            float ll = ShaftReach_S5(10.0f, level, Vector3.Normalize(new Vector3(0.7f, 0.7f, 0.0f)), true);
            check("s5 shaft: a level shaft keeps its depth into the room", ll > 13.0f && ll < 15.0f, $"{ll:0.00} m");
            check("s5 shaft: a pinned shaft keeps the author's length exactly",
                  Math.Abs(ShaftReach_S5(len, stored, morning, false) - len) < 1e-6f, "");
            check("s5 shaft: the ceiling holds", ShaftReach_S5(50.0f, stored, morning, true) <= ShaftMaxLength_S5 + 1e-3f,
                  ShaftReach_S5(50.0f, stored, morning, true).ToString("0.0"));
        }
    }
}

