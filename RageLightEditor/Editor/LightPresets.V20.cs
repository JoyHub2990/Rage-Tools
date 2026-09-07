using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static class LightPresets_V20
    {
        public sealed class Preset_V20
        {
            public string Name { get; set; } = "";
            public string Note { get; set; } = "";

            public byte ColorR { get; set; }
            public byte ColorG { get; set; }
            public byte ColorB { get; set; }
            public float Intensity { get; set; }
            public float Falloff { get; set; }
            public float FalloffExponent { get; set; }
            public byte Type { get; set; }
            public uint Flags { get; set; }
            public byte Flashiness { get; set; }
            public byte GroupId { get; set; }
            public uint TimeFlags { get; set; }
            public float ConeInnerAngle { get; set; }
            public float ConeOuterAngle { get; set; }
            public float VolumeIntensity { get; set; }
            public float VolumeSizeScale { get; set; }
            public byte VolumeOuterColorR { get; set; }
            public byte VolumeOuterColorG { get; set; }
            public byte VolumeOuterColorB { get; set; }
            public float VolumeOuterIntensity { get; set; }
            public float VolumeOuterExponent { get; set; }
            public float CoronaSize { get; set; }
            public float CoronaIntensity { get; set; }
            public float CoronaZBias { get; set; }
            public byte ShadowBlur { get; set; }
            public float ShadowNearClip { get; set; }
            public byte LightFadeDistance { get; set; }
            public byte ShadowFadeDistance { get; set; }
            public byte SpecularFadeDistance { get; set; }
            public byte VolumetricFadeDistance { get; set; }
            public uint ProjectedTextureHash { get; set; }
            public byte LightHash { get; set; }
            public float ExtentX { get; set; }
            public bool Builtin { get; set; }
        }

        public static readonly List<Preset_V20> All = new List<Preset_V20>();

        private static string FilePath =>
            Path.Combine(AppContext.BaseDirectory, "light_presets.json");

        public static Preset_V20 From_V20(LightAttributes l, string name)
        {
            if (l == null) return null;
            return new Preset_V20
            {
                Name = string.IsNullOrWhiteSpace(name) ? "preset" : name.Trim(),
                ColorR = l.ColorR, ColorG = l.ColorG, ColorB = l.ColorB,
                Intensity = l.Intensity, Falloff = l.Falloff, FalloffExponent = l.FalloffExponent,
                Type = (byte)l.Type, Flags = l.Flags, Flashiness = l.Flashiness,
                GroupId = l.GroupId, TimeFlags = l.TimeFlags,
                ConeInnerAngle = l.ConeInnerAngle, ConeOuterAngle = l.ConeOuterAngle,
                VolumeIntensity = l.VolumeIntensity, VolumeSizeScale = l.VolumeSizeScale,
                VolumeOuterColorR = l.VolumeOuterColorR, VolumeOuterColorG = l.VolumeOuterColorG,
                VolumeOuterColorB = l.VolumeOuterColorB,
                VolumeOuterIntensity = l.VolumeOuterIntensity, VolumeOuterExponent = l.VolumeOuterExponent,
                CoronaSize = l.CoronaSize, CoronaIntensity = l.CoronaIntensity, CoronaZBias = l.CoronaZBias,
                ShadowBlur = l.ShadowBlur, ShadowNearClip = l.ShadowNearClip,
                LightFadeDistance = l.LightFadeDistance, ShadowFadeDistance = l.ShadowFadeDistance,
                SpecularFadeDistance = l.SpecularFadeDistance, VolumetricFadeDistance = l.VolumetricFadeDistance,
                ProjectedTextureHash = l.ProjectedTextureHash, LightHash = l.LightHash,
                ExtentX = l.Type == LightType.Capsule ? l.Extent.X : 0.0f,
            };
        }

        public static void Apply_V20(Preset_V20 p, LightAttributes to)
        {
            if (p == null || to == null) return;
            var tmp = Scene.CloneLight(to);
            tmp.ColorR = p.ColorR; tmp.ColorG = p.ColorG; tmp.ColorB = p.ColorB;
            tmp.Intensity = p.Intensity; tmp.Falloff = p.Falloff; tmp.FalloffExponent = p.FalloffExponent;
            tmp.Type = (LightType)p.Type; tmp.Flags = p.Flags; tmp.Flashiness = p.Flashiness;
            tmp.GroupId = p.GroupId; tmp.TimeFlags = p.TimeFlags;
            tmp.ConeInnerAngle = p.ConeInnerAngle; tmp.ConeOuterAngle = p.ConeOuterAngle;
            tmp.VolumeIntensity = p.VolumeIntensity; tmp.VolumeSizeScale = p.VolumeSizeScale;
            tmp.VolumeOuterColorR = p.VolumeOuterColorR; tmp.VolumeOuterColorG = p.VolumeOuterColorG;
            tmp.VolumeOuterColorB = p.VolumeOuterColorB;
            tmp.VolumeOuterIntensity = p.VolumeOuterIntensity; tmp.VolumeOuterExponent = p.VolumeOuterExponent;
            tmp.CoronaSize = p.CoronaSize; tmp.CoronaIntensity = p.CoronaIntensity; tmp.CoronaZBias = p.CoronaZBias;
            tmp.ShadowBlur = p.ShadowBlur; tmp.ShadowNearClip = p.ShadowNearClip;
            tmp.LightFadeDistance = p.LightFadeDistance; tmp.ShadowFadeDistance = p.ShadowFadeDistance;
            tmp.SpecularFadeDistance = p.SpecularFadeDistance; tmp.VolumetricFadeDistance = p.VolumetricFadeDistance;
            tmp.ProjectedTextureHash = p.ProjectedTextureHash; tmp.LightHash = p.LightHash;
            if (p.ExtentX > 0.0f) tmp.Extent = new SharpDX.Vector3(p.ExtentX, tmp.Extent.Y, tmp.Extent.Z);

            var savedClip = LightSettings_O3.Copied;
            var savedLabel = LightSettings_O3.SourceLabel;
            try
            {
                LightSettings_O3.Copied = tmp;
                LightSettings_O3.Apply(to);
            }
            finally
            {
                LightSettings_O3.Copied = savedClip;
                LightSettings_O3.SourceLabel = savedLabel;
            }
        }

        public static Preset_V20 Find_V20(string name) =>
            All.FirstOrDefault(p => string.Equals(p?.Name, name, StringComparison.OrdinalIgnoreCase));

        public static void Put_V20(Preset_V20 p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Name)) return;
            var had = Find_V20(p.Name);
            if (had != null) All[All.IndexOf(had)] = p; else All.Add(p);
            Save_V20();
        }

        public static bool Remove_V20(string name)
        {
            var p = Find_V20(name);
            if (p == null || p.Builtin) return false;
            All.Remove(p);
            Save_V20();
            return true;
        }

        public static void Save_V20()
        {
            try
            {
                var json = JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex) { Console.WriteLine("LIGHTPRESET save failed: " + ex.Message); }
        }

        public static void Load_V20()
        {
            try
            {
                All.Clear();
                if (!File.Exists(FilePath)) { EnsureBuiltin_V21(); return; }
                var list = JsonSerializer.Deserialize<List<Preset_V20>>(File.ReadAllText(FilePath));
                if (list != null) All.AddRange(list.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)));
                EnsureBuiltin_V21();
            }
            catch (Exception ex)
            {
                Console.WriteLine("LIGHTPRESET load failed: " + ex.Message);
                EnsureBuiltin_V21();
            }
        }

        private static void EnsureBuiltin_V21()
        {
            if (LightPresetsBuiltin_V21.Ensure(All) > 0) Save_V20();
        }

        public static int SelfTest_V20(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var src = new LightAttributes
            {
                ColorR = 12, ColorG = 34, ColorB = 56,
                Intensity = 7.5f, Falloff = 9.5f, FalloffExponent = 11.0f,
                ConeOuterAngle = 33.0f, CoronaSize = 0.75f,
            };
            src.Position = new SharpDX.Vector3(100, 200, 300);
            src.Direction = new SharpDX.Vector3(0, 0, -1);
            src.BoneId = 4242;

            var p = From_V20(src, "probe");
            Chk("v20 preset: a preset takes the settings off a light",
                p != null && p.ColorR == 12 && Math.Abs(p.Intensity - 7.5f) < 1e-4f,
                p == null ? "null" : $"rgb {p.ColorR},{p.ColorG},{p.ColorB} intensity {p.Intensity}");

            var dst = new LightAttributes();
            dst.Position = new SharpDX.Vector3(-5, -6, -7);
            dst.Direction = new SharpDX.Vector3(1, 0, 0);
            dst.BoneId = 99;
            Apply_V20(p, dst);

            Chk("v20 preset: applying it moves the settings across",
                dst.ColorR == 12 && dst.ColorG == 34 && dst.ColorB == 56 &&
                Math.Abs(dst.Intensity - 7.5f) < 1e-4f && Math.Abs(dst.Falloff - 9.5f) < 1e-4f,
                $"rgb {dst.ColorR},{dst.ColorG},{dst.ColorB} intensity {dst.Intensity} falloff {dst.Falloff}");

            Chk("v20 preset: ...and NEVER moves the light",
                Math.Abs(dst.Position.X - (-5)) < 1e-4f && Math.Abs(dst.Position.Z - (-7)) < 1e-4f,
                dst.Position.ToString());
            Chk("v20 preset: ...nor which way it points, nor its bone",
                Math.Abs(dst.Direction.X - 1.0f) < 1e-4f && dst.BoneId == 99,
                $"dir {dst.Direction} bone {dst.BoneId}");

            LightSettings_O3.Copy(src, "before");
            var beforeLabel = LightSettings_O3.SourceLabel;
            Apply_V20(p, new LightAttributes());
            Chk("v20 preset: applying one does not stamp on the copy/paste clipboard",
                LightSettings_O3.SourceLabel == beforeLabel && LightSettings_O3.Has,
                LightSettings_O3.SourceLabel);

            int n = All.Count;
            Put_V20(From_V20(src, "v20 probe preset"));
            Chk("v20 preset: saving one adds it to the library", All.Count == n + 1, All.Count.ToString());
            Put_V20(From_V20(src, "v20 probe preset"));
            Chk("v20 preset: ...and saving the same name REPLACES rather than duplicating",
                All.Count == n + 1, All.Count.ToString());
            Chk("v20 preset: deleting takes it away again",
                Remove_V20("v20 probe preset") && All.Count == n, All.Count.ToString());
            return fails;
        }
    }
}

