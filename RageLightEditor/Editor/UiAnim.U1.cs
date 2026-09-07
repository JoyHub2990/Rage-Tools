using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RageLightEditor.Editor
{
    public static class UiAnim
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, double> starts = new Dictionary<string, double>();

        public static double Now => clock.Elapsed.TotalSeconds;

        public static bool Disabled;

        public static void Start(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            starts[id] = Now;
            if (starts.Count > 96) Prune();
        }

        public static void StartOnce(string id, float seconds)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (starts.TryGetValue(id, out var t) && Now - t < seconds) return;
            starts[id] = Now;
        }

        public static void Cancel(string id)
        {
            if (!string.IsNullOrEmpty(id)) starts.Remove(id);
        }

        public static float Progress(string id, float seconds)
        {
            if (Disabled || string.IsNullOrEmpty(id) || seconds <= 0.0f) return 1.0f;
            if (!starts.TryGetValue(id, out var t)) return 1.0f;
            double e = Now - t;
            if (e >= seconds) return 1.0f;
            return e <= 0.0 ? 0.0f : (float)(e / seconds);
        }

        public static bool Running(string id, float seconds) => Progress(id, seconds) < 1.0f;

        public static float Ease(float t)
        {
            t = Math.Clamp(t, 0.0f, 1.0f);
            return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
        }

        public static float EaseOut(float t)
        {
            t = Math.Clamp(t, 0.0f, 1.0f);
            float u = 1.0f - t;
            return 1.0f - u * u * u;
        }

        public static float Pulse(string id, float seconds)
        {
            float t = Progress(id, seconds);
            if (t >= 1.0f) return 0.0f;
            return MathF.Sin(t * MathF.PI);
        }

        private static void Prune()
        {
            double cut = Now - 10.0;
            List<string> dead = null;
            foreach (var kv in starts)
                if (kv.Value < cut) (dead ??= new List<string>()).Add(kv.Key);
            if (dead == null) return;
            foreach (var k in dead) starts.Remove(k);
        }
    }
}

