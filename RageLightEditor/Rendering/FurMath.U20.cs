using System;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public static class FurMath_U20
    {
        public static readonly Vector4 DefaultLayerParams = new Vector4(0.008f, 0.0f, 0.0f, 1.0f);
        public static readonly Vector4 DefaultUvScales = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        public static readonly Vector2 DefaultAlphaDistance = new Vector2(10.0f, 25.0f);
        public static readonly Vector4 DefaultAlphaClip03 = new Vector4(0.0f / 255.0f, 10.0f / 255.0f, 10.0f / 255.0f, 15.0f / 255.0f);
        public static readonly Vector4 DefaultAlphaClip47 = new Vector4(20.0f / 255.0f, 25.0f / 255.0f, 30.0f / 255.0f, 34.0f / 255.0f);
        public static readonly Vector4 DefaultShadow03 = new Vector4(90.0f / 255.0f, 118.0f / 255.0f, 148.0f / 255.0f, 168.0f / 255.0f);
        public static readonly Vector4 DefaultShadow47 = new Vector4(200.0f / 255.0f, 220.0f / 255.0f, 238.0f / 255.0f, 255.0f / 255.0f);

        public const int MaxLayers = 8;

        public static float ShellOffset(float furStep, int layer) => furStep * Math.Max(0, layer);

        public static float FadeAlpha(float distance, float near, float far)
        {
            float f2 = far * far, c2 = near * near;
            if (f2 <= c2) return distance < far ? 1.0f : 0.0f;
            float inv = 1.0f / (f2 - c2);
            return MathUtil.Clamp(distance * distance * -inv + f2 * inv, 0.0f, 1.0f);
        }

        public static float ClipLevel(float alphaClip, float cpv) =>
            0.01f + (alphaClip - 0.01f) * MathUtil.Clamp(cpv * 2.0f, 0.0f, 1.0f);

        public static bool ShellSurvives(float comboHeight, float alphaClip, float cpv) =>
            comboHeight * cpv >= ClipLevel(alphaClip, cpv);

        public static float Shade(float fadeTo, float layerShadow, float fadeAlpha, float cpv)
        {
            float s = fadeTo + (layerShadow - fadeTo) * MathUtil.Clamp(fadeAlpha, 0.0f, 1.0f);
            return fadeTo + (s - fadeTo) * MathUtil.Clamp(cpv, 0.0f, 1.0f);
        }

        public static int ComboSlot(int layer) => Math.Max(0, Math.Min(7, layer)) >> 1;

        public static bool ComboUsesAlpha(int layer) => (layer & 1) != 0;

        public static bool IsMaskShader(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith("grass_fur_mask", StringComparison.OrdinalIgnoreCase);

        public static bool IsLodShader(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith("grass_fur_lod", StringComparison.OrdinalIgnoreCase);
    }
}
