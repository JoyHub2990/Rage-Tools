using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RageLightEditor.Editor
{
    public static class UiSound
    {
        public static bool Enabled = false;

        public static bool Suppressed = true;

        public static bool Silent => !Enabled || Suppressed;

        public enum Cue { Success, Error, InvalidDrop, Blocked }

        public static readonly int[] Played = new int[4];
        public static int Refused;

        private static readonly float[][] clips = new float[4][];
        private static readonly bool[] tried = new bool[4];
        public static readonly string[] Trouble = { "", "", "", "" };

        private static string ResourceOf(Cue c) => c switch
        {
            Cue.Success => "ui_success.wav",
            Cue.Error => "ui_error.wav",
            Cue.InvalidDrop => "ui_invalid.wav",
            _ => "ui_blocked.wav",
        };

        public static float[] Clip(Cue c)
        {
            int i = (int)c;
            if (tried[i]) return clips[i];
            tried[i] = true;
            try
            {
                var name = ResourceOf(c);
                using var s = typeof(UiSound).Assembly.GetManifestResourceStream(name);
                if (s == null) { Trouble[i] = name + " is not embedded in this build"; return null; }
                var bytes = new byte[s.Length];
                int got = 0;
                while (got < bytes.Length)
                {
                    int n = s.Read(bytes, got, bytes.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                clips[i] = AudioEngine_U1.DecodeWav(bytes, out var err);
                if (clips[i] == null) Trouble[i] = err;
            }
            catch (Exception ex) { Trouble[i] = ex.Message; }
            return clips[i];
        }

        private static readonly double[] lastAt = { -1000, -1000, -1000, -1000 };
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private const double RepeatGuardSeconds = 0.09;

        private static float LevelOf(Cue c) => c switch
        {
            Cue.Success => 0.85f,
            Cue.Error => 0.85f,
            Cue.InvalidDrop => 0.80f,
            _ => 0.55f,
        };

        public static void Play(Cue c)
        {
            if (Silent) { Refused++; return; }
            PlayNow(c);
        }

        public static bool PlayNow(Cue c)
        {
            try
            {
                int i = (int)c;
                double now = clock.Elapsed.TotalSeconds;
                if (now - lastAt[i] < RepeatGuardSeconds) { Refused++; return false; }
                var clip = Clip(c);
                if (clip == null) { Refused++; return false; }
                lastAt[i] = now;
                float g = LevelOf(c);
                int v = AudioEngine_U1.Play(clip, g, g, false);
                if (v == 0) { Refused++; return false; }
                Played[i]++;
                return true;
            }
            catch { Refused++; return false; }
        }

        internal static void ClearRepeatGuard()
        {
            for (int i = 0; i < lastAt.Length; i++) lastAt[i] = -1000.0;
        }

        public static void Success() => Play(Cue.Success);
        public static void Error() => Play(Cue.Error);
        public static void InvalidDrop() => Play(Cue.InvalidDrop);
        public static void Blocked() => Play(Cue.Blocked);

        public static void Result(bool ok) => Play(ok ? Cue.Success : Cue.Error);

        public static float LengthOf(Cue c)
        {
            var clip = Clip(c);
            return clip == null ? 0.0f : clip.Length / (float)AudioEngine_U1.SampleRate;
        }

        public static IEnumerable<string> Describe()
        {
            foreach (Cue c in new[] { Cue.Success, Cue.Error, Cue.InvalidDrop, Cue.Blocked })
            {
                var clip = Clip(c);
                if (clip == null) { yield return $"{c}: NOT LOADED ({Trouble[(int)c]})"; continue; }
                float peak = 0.0f;
                for (int i = 0; i < clip.Length; i++) { float a = Math.Abs(clip[i]); if (a > peak) peak = a; }
                yield return $"{c}: {clip.Length} samples, {clip.Length * 1000.0f / AudioEngine_U1.SampleRate:0} ms, peak {peak:0.000}";
            }
        }
    }
}

