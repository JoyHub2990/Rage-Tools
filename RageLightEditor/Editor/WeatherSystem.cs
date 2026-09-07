using System;
using System.Collections.Generic;
using System.Linq;

namespace RageLightEditor.Editor
{
    public class WeatherPreset
    {
        public string Name = "";
        public string[] Cycles = Array.Empty<string>();
        public float FogDensity;
        public float FogStart = 20.0f;
        public float CloudCoverage = 0.35f;
        public float RainAmount;
        public float Wetness;
        public float LightningChance;

        public override string ToString() => Name;
    }

    public class WeatherSystem
    {
        public static readonly WeatherPreset[] Presets =
        {
            new WeatherPreset { Name = "Clear",      Cycles = new[] { "w_clear", "w_extrasunny" },
                                FogDensity = 0.0006f, CloudCoverage = 0.18f },
            new WeatherPreset { Name = "Extra sunny", Cycles = new[] { "w_extrasunny", "w_clear" },
                                FogDensity = 0.0003f, CloudCoverage = 0.08f },
            new WeatherPreset { Name = "Cloudy",     Cycles = new[] { "w_cloudy", "w_clouds", "w_clear" },
                                FogDensity = 0.0015f, CloudCoverage = 0.62f },
            new WeatherPreset { Name = "Overcast",   Cycles = new[] { "w_overcast", "w_cloudy", "w_clear" },
                                FogDensity = 0.0028f, CloudCoverage = 0.85f, Wetness = 0.15f },
            new WeatherPreset { Name = "Rain",       Cycles = new[] { "w_rain", "w_overcast", "w_clear" },
                                FogDensity = 0.0060f, CloudCoverage = 0.92f, RainAmount = 0.75f, Wetness = 0.8f },
            new WeatherPreset { Name = "Storm",      Cycles = new[] { "w_thunder", "w_rain", "w_clear" },
                                FogDensity = 0.0090f, CloudCoverage = 1.0f, RainAmount = 1.0f, Wetness = 1.0f,
                                LightningChance = 0.35f },
            new WeatherPreset { Name = "Fog",        Cycles = new[] { "w_foggy", "w_smog", "w_clear" },
                                FogDensity = 0.0300f, FogStart = 3.0f, CloudCoverage = 0.5f },
            new WeatherPreset { Name = "Snow",       Cycles = new[] { "w_snow", "w_snowlight", "w_xmas", "w_overcast" },
                                FogDensity = 0.0075f, CloudCoverage = 0.9f, RainAmount = 0.5f },
        };

        public int Current { get; private set; } = 0;
        public int Target { get; private set; } = 0;
        public float Blend { get; private set; } = 0.0f;
        public float TransitionSeconds = 6.0f;

        public float Hour = 12.0f;
        public bool AutoAdvance = false;
        public float MinutesPerSecond = 30.0f;

        public float LightningFlash { get; private set; }
        private float lightningTimer = 3.0f;
        private uint rngState = 0x9E3779B9;

        public WeatherPreset CurrentPreset => Presets[Math.Clamp(Current, 0, Presets.Length - 1)];
        public WeatherPreset TargetPreset => Presets[Math.Clamp(Target, 0, Presets.Length - 1)];
        public bool InTransition => Current != Target;

        public string StatusText => InTransition
            ? $"{CurrentPreset.Name} -> {TargetPreset.Name} ({Blend * 100:0}%)"
            : CurrentPreset.Name;

        public void SetWeather(int index, bool immediate = false)
        {
            index = Math.Clamp(index, 0, Presets.Length - 1);
            if (index == Target && !immediate) return;
            if (immediate)
            {
                Current = Target = index;
                Blend = 0.0f;
                return;
            }
            if (InTransition && Blend > 0.0f) Current = Target;
            Target = index;
            Blend = 0.0f;
        }

        public void Update(float dt)
        {
            if (dt <= 0.0f) return;

            if (AutoAdvance)
            {
                Hour += dt * MinutesPerSecond / 60.0f;
                while (Hour >= 24.0f) Hour -= 24.0f;
                while (Hour < 0.0f) Hour += 24.0f;
            }

            if (InTransition)
            {
                float step = TransitionSeconds > 0.01f ? dt / TransitionSeconds : 1.0f;
                Blend += step;
                if (Blend >= 1.0f)
                {
                    Current = Target;
                    Blend = 0.0f;
                }
            }

            LightningFlash = Math.Max(LightningFlash - dt * 4.0f, 0.0f);
            float chance = Lerp(CurrentPreset.LightningChance, TargetPreset.LightningChance, SmoothBlend);
            if (chance > 0.001f)
            {
                lightningTimer -= dt;
                if (lightningTimer <= 0.0f)
                {
                    lightningTimer = 2.0f + NextFloat() * 8.0f / Math.Max(chance, 0.05f);
                    LightningFlash = 1.0f;
                }
            }
        }

        public float SmoothBlend => Blend * Blend * (3.0f - 2.0f * Blend);

        public float FogDensity => Lerp(CurrentPreset.FogDensity, TargetPreset.FogDensity, SmoothBlend);
        public float FogStart => Lerp(CurrentPreset.FogStart, TargetPreset.FogStart, SmoothBlend);
        public float CloudCoverage => Lerp(CurrentPreset.CloudCoverage, TargetPreset.CloudCoverage, SmoothBlend);
        public float RainAmount => Lerp(CurrentPreset.RainAmount, TargetPreset.RainAmount, SmoothBlend);
        public float Wetness => Lerp(CurrentPreset.Wetness, TargetPreset.Wetness, SmoothBlend);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private float NextFloat()
        {
            rngState ^= rngState << 13;
            rngState ^= rngState >> 17;
            rngState ^= rngState << 5;
            return (rngState & 0xFFFFFF) / (float)0x1000000;
        }

        public static string ResolveCycle(WeatherPreset p, IEnumerable<string> available)
        {
            if (p == null || available == null) return null;
            var set = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
            return p.Cycles.FirstOrDefault(c => set.Contains(c));
        }
    }
}

