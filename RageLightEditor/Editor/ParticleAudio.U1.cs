using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RageLightEditor.Editor
{
    public sealed class PtfxAudioDoc_U1
    {
        public List<PtfxAudioEmitter_U1> Emitters { get; set; } = new List<PtfxAudioEmitter_U1>();
        public PtfxAudioBed_U1 Bed { get; set; } = new PtfxAudioBed_U1();

        public int Version { get; set; } = 1;

        public bool Any => (Emitters != null && Emitters.Count > 0) ||
                           (Bed != null && Bed.HasSource);

        public static string SidecarPath(string yptPath) =>
            string.IsNullOrEmpty(yptPath) ? null : yptPath + ".audio.json";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public void Save(string yptPath)
        {
            var p = SidecarPath(yptPath);
            if (p == null) return;
            try
            {
                if (!Any)
                {
                    if (File.Exists(p)) File.Delete(p);
                    return;
                }
                File.WriteAllText(p, JsonSerializer.Serialize(this, Json));
            }
            catch (Exception ex) { Console.WriteLine("PTFXAUDIO could not save " + p + ": " + ex.Message); }
        }

        public static PtfxAudioDoc_U1 Load(string yptPath)
        {
            var p = SidecarPath(yptPath);
            try
            {
                if (p != null && File.Exists(p))
                {
                    var d = JsonSerializer.Deserialize<PtfxAudioDoc_U1>(File.ReadAllText(p));
                    if (d != null)
                    {
                        d.Emitters ??= new List<PtfxAudioEmitter_U1>();
                        d.Bed ??= new PtfxAudioBed_U1();
                        return d;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("PTFXAUDIO could not read " + p + ": " + ex.Message); }
            return new PtfxAudioDoc_U1();
        }
    }

    public sealed class PtfxAudioEmitter_U1
    {
        public string Name { get; set; } = "emitter";
        public string File { get; set; } = "";
        public string GameSound { get; set; } = "";
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; }
        public float Volume { get; set; } = 0.8f;
        public float Radius { get; set; } = 25.0f;
        public bool Loop { get; set; }
        public float Delay { get; set; }
        public bool Enabled { get; set; } = true;

        [JsonIgnore] public int Voice;
        [JsonIgnore] public bool Fired;
        [JsonIgnore] public string Trouble = "";

        public bool HasSource => !string.IsNullOrWhiteSpace(File) || !string.IsNullOrWhiteSpace(GameSound);
    }

    public sealed class PtfxAudioBed_U1
    {
        public string File { get; set; } = "";
        public float Volume { get; set; } = 0.35f;
        public bool Enabled { get; set; } = true;

        [JsonIgnore] public int Voice;
        [JsonIgnore] public string Trouble = "";

        public bool HasSource => !string.IsNullOrWhiteSpace(File);
    }

    public static class PtfxAudioClips_U1
    {
        private sealed class Loaded { public float[] Data; public string Error = ""; }
        private static readonly Dictionary<string, Loaded> cache =
            new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);

        public static float[] Get(string path, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(path)) { error = "no file"; return null; }
            if (cache.TryGetValue(path, out var l)) { error = l.Error; return l.Data; }
            var data = AudioEngine_U1.DecodeWavFile(path, out var err);
            cache[path] = new Loaded { Data = data, Error = err ?? "" };
            error = err ?? "";
            return data;
        }

        public static void Forget(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) cache.Remove(path);
        }

        public static float[] Pip
        {
            get
            {
                if (pip != null) return pip;
                int sr = AudioEngine_U1.SampleRate;
                int n = sr / 20;
                pip = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float e = MathF.Min(1.0f, i / (sr * 0.003f)) * MathF.Exp(-5.0f * t);
                    pip[i] = MathF.Sin(2.0f * MathF.PI * 700.0f * i / sr) * e * 0.5f;
                }
                return pip;
            }
        }
        private static float[] pip;
    }
}

