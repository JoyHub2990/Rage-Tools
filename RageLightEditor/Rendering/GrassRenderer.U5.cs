using System;
using System.Globalization;

namespace RageLightEditor.Rendering
{
    public partial class GrassRenderer
    {
        private static float EnvFloat(string name, float fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f : fallback;
        }

        internal static readonly bool Q5_U5 = Environment.GetEnvironmentVariable("RLE_GRASSQ5") == "1";

        internal static readonly float CullFudge_U5 = EnvFloat("RLE_GRASSCULL", Q5_U5 ? 0.75f : 1.0f);

        internal static readonly float FadeRange_U5 = EnvFloat("RLE_GRASSFADE", Q5_U5 ? 1.0f : 0.05f);

        internal static readonly float FadePower_U5 = EnvFloat("RLE_GRASSFADEPOW", 1.0f);

        internal static float FadeStartFor_U5(float authoredStart, float lodDist)
            => Q5_U5 ? authoredStart : Math.Max(authoredStart, lodDist * 0.75f);
    }
}

