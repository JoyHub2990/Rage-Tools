using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static class LightSettings_O3
    {
        public static LightAttributes Copied;
        public static string SourceLabel = "";
        public static bool Has => Copied != null;

        public static void Copy(LightAttributes from, string label)
        {
            if (from == null) return;
            Copied = Scene.CloneLight(from);
            SourceLabel = string.IsNullOrEmpty(label) ? "a light" : label;
        }

        public static void Apply(LightAttributes to)
        {
            var c = Copied;
            if (c == null || to == null) return;
            to.ColorR = c.ColorR; to.ColorG = c.ColorG; to.ColorB = c.ColorB;
            to.Flashiness = c.Flashiness; to.Intensity = c.Intensity; to.Flags = c.Flags;
            to.Type = c.Type; to.GroupId = c.GroupId; to.TimeFlags = c.TimeFlags;
            to.Falloff = c.Falloff; to.FalloffExponent = c.FalloffExponent;
            to.CullingPlaneNormal = c.CullingPlaneNormal; to.CullingPlaneOffset = c.CullingPlaneOffset;
            to.ShadowBlur = c.ShadowBlur;
            to.VolumeIntensity = c.VolumeIntensity; to.VolumeSizeScale = c.VolumeSizeScale;
            to.VolumeOuterColorR = c.VolumeOuterColorR; to.VolumeOuterColorG = c.VolumeOuterColorG;
            to.VolumeOuterColorB = c.VolumeOuterColorB;
            to.VolumeOuterIntensity = c.VolumeOuterIntensity; to.VolumeOuterExponent = c.VolumeOuterExponent;
            to.LightHash = c.LightHash;
            to.CoronaSize = c.CoronaSize; to.CoronaIntensity = c.CoronaIntensity; to.CoronaZBias = c.CoronaZBias;
            to.LightFadeDistance = c.LightFadeDistance; to.ShadowFadeDistance = c.ShadowFadeDistance;
            to.SpecularFadeDistance = c.SpecularFadeDistance; to.VolumetricFadeDistance = c.VolumetricFadeDistance;
            to.ShadowNearClip = c.ShadowNearClip;
            to.ConeInnerAngle = c.ConeInnerAngle; to.ConeOuterAngle = c.ConeOuterAngle;
            to.Extent = c.Extent;
            to.ProjectedTextureHash = c.ProjectedTextureHash;
        }

        public static string Describe()
        {
            var c = Copied;
            if (c == null) return "nothing copied yet";
            string s = $"{c.Type}, colour {c.ColorR},{c.ColorG},{c.ColorB}, intensity {c.Intensity:0.##}, falloff {c.Falloff:0.##} m";
            if (c.Type == LightType.Spot) s += $", cone {c.ConeInnerAngle:0.#}/{c.ConeOuterAngle:0.#} deg";
            if (c.CoronaSize > 0) s += $", corona {c.CoronaSize:0.##}";
            if (c.VolumeIntensity > 0) s += $", volume {c.VolumeIntensity:0.##}";
            return s;
        }

        public static List<string> Differences(LightAttributes to)
        {
            var list = new List<string>();
            var c = Copied;
            if (c == null || to == null) return list;
            if (c.Type != to.Type) list.Add("type");
            if (c.ColorR != to.ColorR || c.ColorG != to.ColorG || c.ColorB != to.ColorB) list.Add("colour");
            if (c.Intensity != to.Intensity) list.Add("intensity");
            if (c.Falloff != to.Falloff || c.FalloffExponent != to.FalloffExponent) list.Add("falloff");
            if (c.ConeInnerAngle != to.ConeInnerAngle || c.ConeOuterAngle != to.ConeOuterAngle) list.Add("cone");
            if (c.Extent != to.Extent) list.Add("extent");
            if (c.Flags != to.Flags) list.Add("flags");
            if (c.Flashiness != to.Flashiness) list.Add("flashiness");
            if (c.TimeFlags != to.TimeFlags) list.Add("time flags");
            if (c.ShadowBlur != to.ShadowBlur || c.ShadowNearClip != to.ShadowNearClip || c.ShadowFadeDistance != to.ShadowFadeDistance) list.Add("shadow");
            if (c.CoronaSize != to.CoronaSize || c.CoronaIntensity != to.CoronaIntensity || c.CoronaZBias != to.CoronaZBias) list.Add("corona");
            if (c.VolumeIntensity != to.VolumeIntensity || c.VolumeSizeScale != to.VolumeSizeScale ||
                c.VolumeOuterIntensity != to.VolumeOuterIntensity || c.VolumeOuterExponent != to.VolumeOuterExponent ||
                c.VolumeOuterColorR != to.VolumeOuterColorR || c.VolumeOuterColorG != to.VolumeOuterColorG ||
                c.VolumeOuterColorB != to.VolumeOuterColorB) list.Add("volume");
            if (c.CullingPlaneNormal != to.CullingPlaneNormal || c.CullingPlaneOffset != to.CullingPlaneOffset) list.Add("culling plane");
            if (c.LightFadeDistance != to.LightFadeDistance || c.SpecularFadeDistance != to.SpecularFadeDistance ||
                c.VolumetricFadeDistance != to.VolumetricFadeDistance) list.Add("fade distances");
            if (c.ProjectedTextureHash != to.ProjectedTextureHash) list.Add("texture");
            if (c.GroupId != to.GroupId || c.LightHash != to.LightHash) list.Add("ids");
            return list;
        }

        public static bool SettingsEqual(LightAttributes a, LightAttributes b)
        {
            if (a == null || b == null) return false;
            var save = Copied;
            try { Copied = a; return Differences(b).Count == 0; }
            finally { Copied = save; }
        }
    }
}

