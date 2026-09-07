using System;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class ExtensionHelpers
    {

        public const float ShaftMaxStretch_T5 = 1.5f;
        public const float ShaftMaxExtraMetres_T5 = 6.0f;
        public const float ShaftMaxLength_T5 = 24.0f;

        public static readonly bool ShaftS5_T5 = Environment.GetEnvironmentVariable("RLE_SHAFTS5") == "1";

        public static float ShaftReachCap_T5(float length, float want)
        {
            if (ShaftS5_T5) return Math.Min(Math.Min(want, length * ShaftMaxStretch_S5), ShaftMaxLength_S5);
            float cap = Math.Min(length * ShaftMaxStretch_T5, length + ShaftMaxExtraMetres_T5);
            cap = Math.Min(cap, Math.Max(length, ShaftMaxLength_T5));
            return Math.Min(want, cap);
        }

        public const float ShaftRadianceScale_T5 = 9.0f;

        public static float ShaftRadiance_T5(float authoredIntensity, float sunAmount)
        {
            float authored = Math.Clamp(authoredIntensity, 0.0f, 20.0f);
            if (ShaftS5_T5) return authored * sunAmount * ShaftRadianceScale;
            float sunRatio = Math.Max(sunAmount, 0.0f) / ShaftReferenceSun;
            return authored * sunRatio * ShaftRadianceScale_T5;
        }

        public static void ShaftCalibrationTest_T5(Action<string, bool, string> check)
        {
            var hangarStored = Vector3.Normalize(new Vector3(0.30f, 0.08f, -0.95f));
            var morning = Vector3.Normalize(new Vector3(-0.90f, 0.23f, -0.37f));
            var noon = Vector3.Normalize(new Vector3(-0.22f, 0.52f, -0.83f));
            float hm = ShaftReach_S5(9.0f, hangarStored, morning, true);
            float hn = ShaftReach_S5(9.0f, hangarStored, noon, true);
            check("t5 shaft: the hangar's 9 m beam is no longer stretched to 36 m at 08:00",
                  hm <= 9.0f * ShaftMaxStretch_T5 + 1e-3f, $"{hm:0.0} m (was 36.0)");
            check("t5 shaft: and it is still longer than the author's, so it still lands somewhere",
                  hn >= 9.0f && hm > hn, $"noon {hn:0.0} m, 08:00 {hm:0.0} m");
            check("t5 shaft: the allowance is the smaller of half again and six metres",
                  ShaftReachCap_T5(20.0f, 999.0f) <= 26.0f + 1e-3f, $"{ShaftReachCap_T5(20.0f, 999.0f):0.0} m from 20 m");
            check("t5 shaft: an authored length longer than the ceiling is kept",
                  ShaftReachCap_T5(50.0f, 50.0f) >= 50.0f - 1e-3f, $"{ShaftReachCap_T5(50.0f, 50.0f):0.0} m from 50 m");
            float noonInten = ShaftRadiance_T5(5.0f, ShaftReferenceSun);
            float oldInten = 5.0f * ShaftReferenceSun * ShaftRadianceScale;
            check("t5 shaft: Michael's authored intensity 5 is no longer 24x itself under a noon sun",
                  noonInten < oldInten * 0.5f, $"{noonInten:0.0} per metre, was {oldInten:0.0}");
            check("t5 shaft: at a clear noon the sun contributes exactly one",
                  Math.Abs(noonInten - 5.0f * ShaftRadianceScale_T5) < 1e-3f, $"{noonInten:0.0}");
            check("t5 shaft: and at night there is no beam at all", ShaftRadiance_T5(5.0f, 0.0f) == 0.0f, "");
            check("t5 shaft: the sun scales the beam in proportion",
                  Math.Abs(ShaftRadiance_T5(5.0f, ShaftReferenceSun * 0.5f) - noonInten * 0.5f) < 1e-3f, "");
        }
    }
}

