using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpDX;

namespace RageLightEditor.Editor
{
    public enum UvAnimChannel
    {
        ScaleU = 0, ScaleV, Rotation, OffsetU, OffsetV,
        Row0X, Row0Y, Row0Z, Row1X, Row1Y, Row1Z,
    }

    public class UvAnimKey
    {
        public float Time { get; set; }
        public float Value { get; set; }

        public UvAnimKey() { }
        public UvAnimKey(float t, float v) { Time = t; Value = v; }
    }

    public class UvAnimCurve
    {
        public float Constant { get; set; }
        public List<UvAnimKey> Keys { get; set; } = new List<UvAnimKey>();

        [JsonIgnore] public bool Animated => Keys != null && Keys.Count > 0;

        public UvAnimCurve() { }
        public UvAnimCurve(float constant) { Constant = constant; }

        public float Evaluate(float t)
        {
            var k = Keys;
            if (k == null || k.Count == 0) return Constant;
            if (k.Count == 1) return k[0].Value;
            if (t <= k[0].Time) return k[0].Value;
            if (t >= k[k.Count - 1].Time) return k[k.Count - 1].Value;
            for (int i = 0; i < k.Count - 1; i++)
            {
                var a = k[i]; var b = k[i + 1];
                if (t < a.Time || t > b.Time) continue;
                float span = b.Time - a.Time;
                if (span <= 1e-6f) return b.Value;
                float f = (t - a.Time) / span;
                return a.Value + (b.Value - a.Value) * f;
            }
            return k[k.Count - 1].Value;
        }

        public void SetKey(float t, float v)
        {
            Keys ??= new List<UvAnimKey>();
            foreach (var k in Keys)
            {
                if (Math.Abs(k.Time - t) < 1e-4f) { k.Value = v; return; }
            }
            Keys.Add(new UvAnimKey(t, v));
            Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        public bool RemoveKeyAt(int index)
        {
            if (Keys == null || index < 0 || index >= Keys.Count) return false;
            Keys.RemoveAt(index);
            return true;
        }

        public void Clear() { Keys?.Clear(); }

        public UvAnimCurve Clone()
        {
            var c = new UvAnimCurve(Constant);
            if (Keys != null) foreach (var k in Keys) c.Keys.Add(new UvAnimKey(k.Time, k.Value));
            return c;
        }
    }

    public class UvAnimTrack
    {
        public string MaterialName { get; set; } = "";
        public int MaterialIndex { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Raw { get; set; }
        public float PivotU { get; set; } = 0.5f;
        public float PivotV { get; set; } = 0.5f;
        public string Preset { get; set; } = "";

        public List<UvAnimCurve> Curves { get; set; } = NewCurves();

        public const int ChannelCount = 11;

        public static List<UvAnimCurve> NewCurves()
        {
            var list = new List<UvAnimCurve>(ChannelCount);
            for (int i = 0; i < ChannelCount; i++) list.Add(new UvAnimCurve());
            list[(int)UvAnimChannel.ScaleU].Constant = 1.0f;
            list[(int)UvAnimChannel.ScaleV].Constant = 1.0f;
            list[(int)UvAnimChannel.Row0X].Constant = 1.0f;
            list[(int)UvAnimChannel.Row1Y].Constant = 1.0f;
            return list;
        }

        public UvAnimCurve Curve(UvAnimChannel c)
        {
            Curves ??= NewCurves();
            while (Curves.Count < ChannelCount) Curves.Add(new UvAnimCurve());
            return Curves[(int)c];
        }

        [JsonIgnore] public bool AnyKeys => Curves != null && Curves.Any(c => c != null && c.Animated);

        public static readonly UvAnimChannel[] ComposedChannels =
        {
            UvAnimChannel.ScaleU, UvAnimChannel.ScaleV, UvAnimChannel.Rotation,
            UvAnimChannel.OffsetU, UvAnimChannel.OffsetV,
        };
        public static readonly UvAnimChannel[] RawChannels =
        {
            UvAnimChannel.Row0X, UvAnimChannel.Row0Y, UvAnimChannel.Row0Z,
            UvAnimChannel.Row1X, UvAnimChannel.Row1Y, UvAnimChannel.Row1Z,
        };

        [JsonIgnore] public UvAnimChannel[] ActiveChannels => Raw ? RawChannels : ComposedChannels;

        public static string ChannelLabel(UvAnimChannel c)
        {
            switch (c)
            {
                case UvAnimChannel.ScaleU: return "Scale U";
                case UvAnimChannel.ScaleV: return "Scale V";
                case UvAnimChannel.Rotation: return "Rotation";
                case UvAnimChannel.OffsetU: return "Offset U";
                case UvAnimChannel.OffsetV: return "Offset V";
                case UvAnimChannel.Row0X: return "U row .x (scale)";
                case UvAnimChannel.Row0Y: return "U row .y (shear)";
                case UvAnimChannel.Row0Z: return "U row .z (offset)";
                case UvAnimChannel.Row1X: return "V row .x (shear)";
                case UvAnimChannel.Row1Y: return "V row .y (scale)";
                case UvAnimChannel.Row1Z: return "V row .z (offset)";
                default: return c.ToString();
            }
        }

        public void Evaluate(float t, out Vector4 uv0, out Vector4 uv1)
        {
            if (Raw)
            {
                uv0 = new Vector4(Curve(UvAnimChannel.Row0X).Evaluate(t),
                                  Curve(UvAnimChannel.Row0Y).Evaluate(t),
                                  Curve(UvAnimChannel.Row0Z).Evaluate(t), 0.0f);
                uv1 = new Vector4(Curve(UvAnimChannel.Row1X).Evaluate(t),
                                  Curve(UvAnimChannel.Row1Y).Evaluate(t),
                                  Curve(UvAnimChannel.Row1Z).Evaluate(t), 0.0f);
                return;
            }

            float su = Curve(UvAnimChannel.ScaleU).Evaluate(t);
            float sv = Curve(UvAnimChannel.ScaleV).Evaluate(t);
            float ang = Curve(UvAnimChannel.Rotation).Evaluate(t) * (float)(Math.PI / 180.0);
            float ou = Curve(UvAnimChannel.OffsetU).Evaluate(t);
            float ov = Curve(UvAnimChannel.OffsetV).Evaluate(t);
            Compose(su, sv, ang, ou, ov, PivotU, PivotV, out uv0, out uv1);
        }

        public static void Compose(float su, float sv, float angleRad, float ou, float ov,
                                   float pu, float pv, out Vector4 uv0, out Vector4 uv1)
        {
            float c = (float)Math.Cos(angleRad), s = (float)Math.Sin(angleRad);
            float m00 = su * c, m01 = -sv * s;
            float m10 = su * s, m11 = sv * c;
            float tu = ou + pu - (m00 * pu + m01 * pv);
            float tv = ov + pv - (m10 * pu + m11 * pv);
            uv0 = new Vector4(m00, m01, tu, 0.0f);
            uv1 = new Vector4(m10, m11, tv, 0.0f);
        }

        public void BakeToRaw(float duration, int fps)
        {
            if (Raw) return;
            int frames = Math.Max(2, (int)Math.Round(duration * Math.Max(fps, 1)) + 1);
            var rows = new (Vector4 a, Vector4 b)[frames];
            for (int f = 0; f < frames; f++)
            {
                float t = duration * f / (frames - 1);
                Evaluate(t, out var a, out var b);
                rows[f] = (a, b);
            }
            foreach (var ch in RawChannels) Curve(ch).Clear();
            for (int f = 0; f < frames; f++)
            {
                float t = duration * f / (frames - 1);
                Curve(UvAnimChannel.Row0X).SetKey(t, rows[f].a.X);
                Curve(UvAnimChannel.Row0Y).SetKey(t, rows[f].a.Y);
                Curve(UvAnimChannel.Row0Z).SetKey(t, rows[f].a.Z);
                Curve(UvAnimChannel.Row1X).SetKey(t, rows[f].b.X);
                Curve(UvAnimChannel.Row1Y).SetKey(t, rows[f].b.Y);
                Curve(UvAnimChannel.Row1Z).SetKey(t, rows[f].b.Z);
            }
            Raw = true;
            Preset = "baked";
        }

        public UvAnimTrack Clone()
        {
            var t = new UvAnimTrack
            {
                MaterialName = MaterialName, MaterialIndex = MaterialIndex, Enabled = Enabled,
                Raw = Raw, PivotU = PivotU, PivotV = PivotV, Preset = Preset,
                Curves = new List<UvAnimCurve>(),
            };
            foreach (var c in Curves ?? NewCurves()) t.Curves.Add(c?.Clone() ?? new UvAnimCurve());
            return t;
        }
    }

    public class UvAnimClip
    {
        public string Name { get; set; } = "uv_anim";
        public float Duration { get; set; } = 4.0f;
        public int Fps { get; set; } = 30;
        public bool Loop { get; set; } = true;
        public string ModelPath { get; set; } = "";
        public List<UvAnimTrack> Tracks { get; set; } = new List<UvAnimTrack>();

        [JsonIgnore] public int FrameCount => Math.Max(2, (int)Math.Round(Duration * Math.Max(Fps, 1)) + 1);

        public UvAnimTrack Find(int materialIndex) =>
            Tracks?.FirstOrDefault(t => t != null && t.MaterialIndex == materialIndex);

        public UvAnimTrack Add(int materialIndex, string name)
        {
            var t = new UvAnimTrack { MaterialIndex = materialIndex, MaterialName = name ?? "" };
            Tracks ??= new List<UvAnimTrack>();
            Tracks.Add(t);
            Tracks.Sort((a, b) => a.MaterialIndex.CompareTo(b.MaterialIndex));
            return t;
        }

        public bool Remove(int materialIndex)
        {
            var t = Find(materialIndex);
            return t != null && Tracks.Remove(t);
        }

        public float Wrap(float t)
        {
            float d = Math.Max(Duration, 1e-4f);
            if (!Loop) return Math.Max(0f, Math.Min(t, d));
            t %= d;
            return t < 0 ? t + d : t;
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public const string ProjectFilter = "UV animation project (*.rleuv)|*.rleuv|All files (*.*)|*.*";

        public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

        public static UvAnimClip FromJson(string json) => JsonSerializer.Deserialize<UvAnimClip>(json, JsonOpts);

        public bool Save(string path, out string message)
        {
            try
            {
                File.WriteAllText(path, ToJson());
                message = $"Saved {Path.GetFileName(path)} - {Tracks?.Count ?? 0} track(s), {Duration:0.##} s";
                return true;
            }
            catch (Exception ex) { message = "Could not save: " + ex.Message; return false; }
        }

        public static UvAnimClip Load(string path, out string message)
        {
            try
            {
                var c = FromJson(File.ReadAllText(path));
                if (c == null) { message = "Nothing in " + Path.GetFileName(path); return null; }
                foreach (var t in c.Tracks ?? new List<UvAnimTrack>()) t.Curve(UvAnimChannel.Row1Z);
                message = $"Opened {Path.GetFileName(path)} - {c.Tracks?.Count ?? 0} track(s), {c.Duration:0.##} s";
                return c;
            }
            catch (Exception ex) { message = "Could not open: " + ex.Message; return null; }
        }

        public UvAnimClip Clone()
        {
            var c = new UvAnimClip
            {
                Name = Name, Duration = Duration, Fps = Fps, Loop = Loop, ModelPath = ModelPath,
                Tracks = new List<UvAnimTrack>(),
            };
            foreach (var t in Tracks ?? new List<UvAnimTrack>()) c.Tracks.Add(t.Clone());
            return c;
        }
    }

    public static class UvAnimPresets
    {
        public struct Preset
        {
            public string Name;
            public string Blurb;
            public Action<UvAnimTrack, float> Apply;
        }

        public static void Scroll(UvAnimTrack t, float duration, float speedU, float speedV)
        {
            float d = Math.Max(duration, 0.01f);
            float du = (float)Math.Round(speedU * d);
            float dv = (float)Math.Round(speedV * d);
            if (Math.Abs(du) < 0.5f && Math.Abs(speedU) > 1e-4f) du = Math.Sign(speedU);
            if (Math.Abs(dv) < 0.5f && Math.Abs(speedV) > 1e-4f) dv = Math.Sign(speedV);
            var cu = t.Curve(UvAnimChannel.OffsetU); cu.Clear();
            var cv = t.Curve(UvAnimChannel.OffsetV); cv.Clear();
            if (Math.Abs(du) > 0) { cu.SetKey(0f, 0f); cu.SetKey(d, du); }
            if (Math.Abs(dv) > 0) { cv.SetKey(0f, 0f); cv.SetKey(d, dv); }
        }

        private static void Reset(UvAnimTrack t)
        {
            t.Raw = false;
            foreach (var ch in UvAnimTrack.ComposedChannels) t.Curve(ch).Clear();
            foreach (var ch in UvAnimTrack.RawChannels) t.Curve(ch).Clear();
            t.Curve(UvAnimChannel.ScaleU).Constant = 1.0f;
            t.Curve(UvAnimChannel.ScaleV).Constant = 1.0f;
            t.Curve(UvAnimChannel.Rotation).Constant = 0.0f;
            t.Curve(UvAnimChannel.OffsetU).Constant = 0.0f;
            t.Curve(UvAnimChannel.OffsetV).Constant = 0.0f;
        }

        public static readonly Preset[] All =
        {
            new Preset
            {
                Name = "Still (identity)",
                Blurb = "No animation - the material samples its UVs untouched.\nWhere you start when you want to key everything yourself.",
                Apply = (t, d) => { Reset(t); t.Preset = "Still"; },
            },
            new Preset
            {
                Name = "Conveyor belt",
                Blurb = "V scrolls steadily along the belt at about half a texture per second.\nPut it on the belt's top face; the sides usually want the same track.",
                Apply = (t, d) => { Reset(t); Scroll(t, d, 0f, 0.5f); t.Preset = "Conveyor belt"; },
            },
            new Preset
            {
                Name = "Waterfall",
                Blurb = "V scrolls fast (about 1.2 textures a second) - the falling sheet.\nPair it with an emissive or alpha preset that declares USE_ANIMATED_UVS.",
                Apply = (t, d) => { Reset(t); Scroll(t, d, 0f, 1.2f); t.Preset = "Waterfall"; },
            },
            new Preset
            {
                Name = "Flowing river",
                Blurb = "A slow drift mostly along U with a little V, so the surface never\nreads as a single sliding sheet.",
                Apply = (t, d) => { Reset(t); Scroll(t, d, 0.15f, 0.05f); t.Preset = "Flowing river"; },
            },
            new Preset
            {
                Name = "Scrolling sign",
                Blurb = "U scrolls right to left - a ticker, a departure board, a shop banner.",
                Apply = (t, d) => { Reset(t); Scroll(t, d, -0.35f, 0f); t.Preset = "Scrolling sign"; },
            },
            new Preset
            {
                Name = "Spinning",
                Blurb = "One full turn over the clip, about the middle of the texture:\na fan, a radar sweep, a rotating logo.",
                Apply = (t, d) =>
                {
                    Reset(t);
                    var c = t.Curve(UvAnimChannel.Rotation);
                    for (int i = 0; i <= 4; i++) c.SetKey(d * i / 4f, 90f * i);
                    t.Preset = "Spinning";
                },
            },
            new Preset
            {
                Name = "Pulsing tiles",
                Blurb = "The texture breathes between 1x and 1.5x tiling and back,\nabout the middle - a heat shimmer, a throbbing screen.",
                Apply = (t, d) =>
                {
                    Reset(t);
                    var u = t.Curve(UvAnimChannel.ScaleU); var v = t.Curve(UvAnimChannel.ScaleV);
                    u.SetKey(0f, 1f); u.SetKey(d * 0.5f, 1.5f); u.SetKey(d, 1f);
                    v.SetKey(0f, 1f); v.SetKey(d * 0.5f, 1.5f); v.SetKey(d, 1f);
                    t.Preset = "Pulsing tiles";
                },
            },
            new Preset
            {
                Name = "Wobble",
                Blurb = "A small circular drift in both axes - a flag, a curtain, water in a bowl.\nA quarter cycle apart, so it circles instead of sliding diagonally.",
                Apply = (t, d) =>
                {
                    Reset(t);
                    var u = t.Curve(UvAnimChannel.OffsetU); var v = t.Curve(UvAnimChannel.OffsetV);
                    const float A = 0.02f;
                    for (int i = 0; i <= 8; i++)
                    {
                        float f = i / 8.0f, tt = d * f;
                        u.SetKey(tt, A * (float)Math.Sin(f * Math.PI * 2));
                        v.SetKey(tt, A * (float)Math.Cos(f * Math.PI * 2));
                    }
                    t.Preset = "Wobble";
                },
            },
        };

        public static string[] Names => All.Select(p => p.Name).ToArray();
    }

    public static class UvAnimation
    {
        public static int SelfTest()
        {
            int fails = 0, ran = 0;
            void Check(string what, bool ok, string detail = "")
            {
                ran++;
                if (!ok) { fails++; Console.WriteLine($"  UVANIM FAIL {what} {detail}"); }
            }

            var t = new UvAnimTrack();
            t.Evaluate(0f, out var a0, out var a1);
            Check("identity row 0", Near(a0, new Vector4(1, 0, 0, 0)), a0.ToString());
            Check("identity row 1", Near(a1, new Vector4(0, 1, 0, 0)), a1.ToString());

            t.Curve(UvAnimChannel.OffsetU).Constant = 0.25f;
            t.Curve(UvAnimChannel.OffsetV).Constant = -0.5f;
            t.Evaluate(0f, out a0, out a1);
            Check("offset U is row0.z", Math.Abs(a0.Z - 0.25f) < 1e-5f, a0.Z.ToString(CultureInfo.InvariantCulture));
            Check("offset V is row1.z", Math.Abs(a1.Z + 0.5f) < 1e-5f, a1.Z.ToString(CultureInfo.InvariantCulture));

            var r = new UvAnimTrack { PivotU = 0.5f, PivotV = 0.5f };
            r.Curve(UvAnimChannel.Rotation).Constant = 90f;
            r.Evaluate(0f, out var r0, out var r1);
            var uvp = Apply(r0, r1, 0.5f, 0.5f);
            Check("a rotation holds its pivot still",
                  Math.Abs(uvp.X - 0.5f) < 1e-4f && Math.Abs(uvp.Y - 0.5f) < 1e-4f, uvp.ToString());
            var uvr = Apply(r0, r1, 1.0f, 0.5f);
            Check("...and turns a point a quarter turn round it",
                  Math.Abs(uvr.X - 0.5f) < 1e-4f && Math.Abs(uvr.Y - 1.0f) < 1e-4f, uvr.ToString());

            var s = new UvAnimTrack { PivotU = 0f, PivotV = 0f };
            s.Curve(UvAnimChannel.ScaleU).Constant = 2f;
            s.Evaluate(0f, out var s0, out var s1);
            var uvs = Apply(s0, s1, 1f, 1f);
            Check("scale U 2x doubles the U coordinate", Math.Abs(uvs.X - 2f) < 1e-5f, uvs.ToString());

            var belt = new UvAnimTrack();
            UvAnimPresets.All.First(p => p.Name == "Conveyor belt").Apply(belt, 4.0f);
            float v0 = belt.Curve(UvAnimChannel.OffsetV).Evaluate(0f);
            float v1 = belt.Curve(UvAnimChannel.OffsetV).Evaluate(4.0f);
            Check("a conveyor loops on a whole tile",
                  Math.Abs(v1 - v0 - Math.Round(v1 - v0)) < 1e-4f && Math.Abs(v1 - v0) >= 1f,
                  (v1 - v0).ToString("0.####", CultureInfo.InvariantCulture));
            Check("...and it actually moves at half a tile a second", Math.Abs(v1 - 2f) < 1e-4f,
                  v1.ToString("0.####", CultureInfo.InvariantCulture));

            var spin = new UvAnimTrack();
            UvAnimPresets.All.First(p => p.Name == "Spinning").Apply(spin, 2.0f);
            Check("a spin is a whole turn over the clip",
                  Math.Abs(spin.Curve(UvAnimChannel.Rotation).Evaluate(2.0f) - 360f) < 1e-3f, "");
            Check("...and is a quarter of the way round a quarter in",
                  Math.Abs(spin.Curve(UvAnimChannel.Rotation).Evaluate(0.5f) - 90f) < 1e-3f, "");

            var c = new UvAnimCurve();
            c.SetKey(1f, 10f); c.SetKey(3f, 30f);
            Check("a curve interpolates between its keys", Math.Abs(c.Evaluate(2f) - 20f) < 1e-4f, c.Evaluate(2f).ToString(CultureInfo.InvariantCulture));
            Check("...holds before the first key", Math.Abs(c.Evaluate(0f) - 10f) < 1e-4f, "");
            Check("...and after the last", Math.Abs(c.Evaluate(9f) - 30f) < 1e-4f, "");
            c.SetKey(1f, 11f);
            Check("keying the same time moves the key rather than adding one", c.Keys.Count == 2 && Math.Abs(c.Keys[0].Value - 11f) < 1e-5f, c.Keys.Count.ToString());

            var comp = new UvAnimTrack();
            UvAnimPresets.All.First(p => p.Name == "Spinning").Apply(comp, 2.0f);
            var baked = comp.Clone();
            baked.BakeToRaw(2.0f, 30);
            bool same = true; float worst = 0f;
            for (int i = 0; i <= 20; i++)
            {
                float tt = 2.0f * i / 20f;
                comp.Evaluate(tt, out var ca, out var cb);
                baked.Evaluate(tt, out var ba, out var bb);
                worst = Math.Max(worst, Math.Max(Diff(ca, ba), Diff(cb, bb)));
            }
            same = worst < 0.01f;
            Check("a baked track evaluates to what it was baked from", same, worst.ToString("0.#####", CultureInfo.InvariantCulture));
            Check("...and is in raw mode with six keyed channels", baked.Raw &&
                  UvAnimTrack.RawChannels.All(ch => baked.Curve(ch).Animated), "");

            static string OneLine(string t, int max)
            {
                if (string.IsNullOrEmpty(t)) return "";
                var sb = new System.Text.StringBuilder(Math.Min(t.Length, max));
                foreach (var ch in t)
                {
                    if (sb.Length >= max) break;
                    sb.Append(char.IsControl(ch) ? ' ' : ch);
                }
                return sb.ToString();
            }

            var clip = new UvAnimClip { Name = "belt", Duration = 3.5f, Fps = 24, Loop = true };
            var tr = clip.Add(2, "conveyor_mat");
            UvAnimPresets.All.First(p => p.Name == "Conveyor belt").Apply(tr, clip.Duration);
            var written = clip.ToJson();
            var back = UvAnimClip.FromJson(written);
            bool tracksOk = back != null && back.Tracks != null && back.Tracks.Count == 1;
            Check("a project round-trips through its own file",
                  tracksOk && back.Name == "belt" && Math.Abs(back.Duration - 3.5f) < 1e-5f &&
                  back.Fps == 24 && back.Tracks[0].MaterialIndex == 2 &&
                  back.Tracks[0].Curve(UvAnimChannel.OffsetV).Keys.Count == 2,
                  back == null ? "nothing came back"
                               : $"name '{back.Name}' dur {back.Duration} fps {back.Fps} tracks {back.Tracks?.Count ?? -1}" +
                                 (tracksOk ? $" mat {back.Tracks[0].MaterialIndex} keys {back.Tracks[0].Curve(UvAnimChannel.OffsetV).Keys.Count}" : "") +
                                 " | wrote " + OneLine(written, 220));
            if (!tracksOk) { Console.WriteLine("UVANIM self-test: " + ran + " checks, " + fails + " failed"); return fails; }
            back.Tracks[0].Evaluate(clip.Duration, out var e0, out var e1);
            tr.Evaluate(clip.Duration, out var w0, out var w1);
            Check("...and evaluates to the same rows afterwards", Near(e0, w0) && Near(e1, w1), "");

            var loopclip = new UvAnimClip { Duration = 2f, Loop = true };
            Check("time wraps inside a looping clip", Math.Abs(loopclip.Wrap(5f) - 1f) < 1e-5f, loopclip.Wrap(5f).ToString(CultureInfo.InvariantCulture));
            loopclip.Loop = false;
            Check("...and clamps when it does not loop", Math.Abs(loopclip.Wrap(5f) - 2f) < 1e-5f, "");

            Console.WriteLine($"UVANIM self-test: {ran} checks, {fails} failed");
            return fails;
        }

        public static Vector2 Apply(Vector4 uv0, Vector4 uv1, float u, float v) =>
            new Vector2(uv0.X * u + uv0.Y * v + uv0.Z, uv1.X * u + uv1.Y * v + uv1.Z);

        private static bool Near(Vector4 a, Vector4 b) => Diff(a, b) < 1e-5f;

        private static float Diff(Vector4 a, Vector4 b) =>
            Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)),
                     Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));
    }
}

