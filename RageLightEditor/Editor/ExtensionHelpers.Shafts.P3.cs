using System;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class ExtensionHelpers
    {
        public const float ShaftGrazeCos = 0.30f;
        public const float ShaftMinCos = 0.06f;

        public static float ShaftFlux_P3(Vector3 beam, Vector3 inward, Vector3 quadNormal, bool followsSun)
        {
            if (!followsSun) return 1.0f;
            if (quadNormal.LengthSquared() <= 1e-10f)
            {
                if (inward.LengthSquared() <= 1e-8f) return 1.0f;
                return Vector3.Dot(beam, Vector3.Normalize(inward)) > 0.0f ? 1.0f : 0.0f;
            }
            var n = Vector3.Normalize(quadNormal);
            if (inward.LengthSquared() > 1e-8f && Vector3.Dot(n, inward) < 0.0f) n = -n;
            float c = Vector3.Dot(beam, n);
            if (inward.LengthSquared() <= 1e-8f) c = Math.Abs(c);
            if (c <= ShaftMinCos) return 0.0f;
            return MathUtil.SmoothStep(MathUtil.Clamp((c - ShaftMinCos) / (ShaftGrazeCos - ShaftMinCos), 0.0f, 1.0f));
        }

        public static void ShaftFluxTest_P3(Action<string, bool, string> check)
        {
            var pane = Vector3.Normalize(new Vector3(0.355f, -0.943f, 0));
            var inward = Vector3.Normalize(new Vector3(-0.43f, 0.57f, -0.70f));
            float noon = ShaftFlux_P3(Vector3.Normalize(new Vector3(-0.22f, 0.52f, -0.83f)), inward, pane, true);
            check("p3 shaft: the noon sun pours through Michael's window", noon > 0.99f, noon.ToString("0.000"));
            float morning = ShaftFlux_P3(Vector3.Normalize(new Vector3(-0.90f, 0.23f, -0.37f)), inward, pane, true);
            check("p3 shaft: so does the 08:00 sun, from the other azimuth", morning > 0.99f, morning.ToString("0.000"));
            float evening = ShaftFlux_P3(Vector3.Normalize(new Vector3(0.78f, 0.33f, -0.53f)), inward, pane, true);
            check("p3 shaft: the 17:00 sun runs along that pane and makes no beam", evening == 0.0f, evening.ToString("0.000"));
            float flipped = ShaftFlux_P3(Vector3.Normalize(new Vector3(-0.22f, 0.52f, -0.83f)), inward, -pane, true);
            check("p3 shaft: the quad's winding no longer decides anything", Math.Abs(flipped - noon) < 1e-6f, $"{flipped:0.000} vs {noon:0.000}");
            float back = ShaftFlux_P3(Vector3.Normalize(new Vector3(0.355f, -0.943f, 0)), inward, pane, true);
            check("p3 shaft: a sun behind the window makes no beam", back == 0.0f, back.ToString("0.000"));
            float pinned = ShaftFlux_P3(new Vector3(0, -1, 0), inward, pane, false);
            check("p3 shaft: a pinned shaft is not gated by the sun", pinned == 1.0f, pinned.ToString("0.000"));
            float fallback = ShaftFlux_P3(Vector3.Normalize(new Vector3(0.355f, -0.943f, 0)), Vector3.Zero, pane, true);
            check("p3 shaft: without a stored direction either face of the quad takes the sun", fallback > 0.99f, fallback.ToString("0.000"));
            float half = ShaftFlux_P3(Vector3.Normalize(new Vector3(-0.355f, 0.943f, 0) * 0.18f + new Vector3(0.943f, 0.355f, 0) * 0.98f), inward, pane, true);
            check("p3 shaft: a near-graze sun is a faint beam, not a full one", half > 0.0f && half < 1.0f, half.ToString("0.000"));
        }
    }
}

