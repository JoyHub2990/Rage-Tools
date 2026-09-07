using System;

namespace RageLightEditor.Rendering
{
    public partial class NavMeshRenderer
    {
        public const float PullMaxMetres_T5 = 0.5f;

        public static readonly float PullMax_T5 = ReadPullMax_T5();

        private static float ReadPullMax_T5()
        {
            var v = Environment.GetEnvironmentVariable("RLE_NAVPULLMAX");
            if (!string.IsNullOrEmpty(v) && float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float m) && m >= 0.0f) return m;
            return PullMaxMetres_T5;
        }

        public static void SelfTest_T5(Action<string, bool, string> check)
        {
            check("t5 nav: the camera pull has a ceiling in metres",
                  PullMaxMetres_T5 > 0.0f && PullMaxMetres_T5 <= 1.0f, $"{PullMaxMetres_T5:0.00} m");
            const float frac = 0.012f;
            float at10 = Math.Min(10.0f * frac, PullMaxMetres_T5);
            float at750 = Math.Min(750.0f * frac, PullMaxMetres_T5);
            check("t5 nav: close up the fraction still decides", at10 < PullMaxMetres_T5, $"{at10:0.000} m at 10 m");
            check("t5 nav: at the far end of the reach the ceiling decides", Math.Abs(at750 - PullMaxMetres_T5) < 1e-6f,
                  $"{at750:0.00} m at 750 m, not {(750.0f * frac):0.0} m");
        }
    }
}

