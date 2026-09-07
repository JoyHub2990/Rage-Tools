using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class UvAnimImport_V16
    {
        public sealed class Result_V16
        {
            public UvAnimClip Clip;
            public int Tracks;
            public int Frames;
            public int BoneAnims;
            public string Message = "";
            public bool Any => Tracks > 0;
        }

        private static readonly Regex NameRule_V16 =
            new Regex(@"^(?:pack:/)?(?<base>.*)_uv_(?<idx>\d+)(?:\.clip)?$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static int MaterialIndexOf_V16(string clipName, uint clipHash, string modelName, int fallback)
        {
            var m = NameRule_V16.Match(clipName ?? "");
            if (m.Success && int.TryParse(m.Groups["idx"].Value, NumberStyles.Integer,
                                          CultureInfo.InvariantCulture, out int idx)) return idx;

            if (!string.IsNullOrWhiteSpace(modelName) && clipHash != 0)
            {
                uint b = JenkHash.GenHash(UvAnimYcd.SanitiseName_V16(modelName));
                long d = (long)clipHash - b;
                if (d >= 1 && d <= 256) return (int)(d - 1);
            }
            return fallback;
        }

        public static Result_V16 FromYcd_V16(YcdFile ycd, string modelName)
        {
            var r = new Result_V16 { Clip = new UvAnimClip() };
            if (ycd?.ClipMapEntries == null) { r.Message = "no clips in this dictionary"; return r; }

            string baseName = null;
            float duration = 0f;
            int maxFrames = 0;
            int fallbackIndex = 0;

            foreach (var cme in ycd.ClipMapEntries)
            {
                string clipName = cme?.Clip?.Name ?? cme?.Clip?.ShortName ?? "";
                foreach (var anim in UvAnimYcd.AnimationsOf(cme?.Clip))
                {
                    var ids = anim?.BoneIds?.data_items;
                    if (ids == null) continue;

                    int slot0 = -1, slot1 = -1;
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (ids[i].Track == UvAnimYcd.TrackUV0 && slot0 < 0) slot0 = i;
                        else if (ids[i].Track == UvAnimYcd.TrackUV1 && slot1 < 0) slot1 = i;
                    }
                    if (slot0 < 0 && slot1 < 0) continue;

                    int frames = Math.Max((int)anim.Frames, 2);
                    float dur = anim.Duration > 0.0001f ? anim.Duration : 1f;
                    maxFrames = Math.Max(maxFrames, frames);
                    duration = Math.Max(duration, dur);

                    var nm = NameRule_V16.Match(clipName);
                    if (baseName == null && nm.Success) baseName = nm.Groups["base"].Value;

                    int matIndex = MaterialIndexOf_V16(clipName, cme?.Hash ?? 0, modelName, fallbackIndex);
                    fallbackIndex = matIndex + 1;

                    var track = r.Clip.Find(matIndex) ?? r.Clip.Add(matIndex, "");
                    track.Raw = true;
                    track.Enabled = true;
                    track.Preset = "read from " + (string.IsNullOrEmpty(clipName) ? "a .ycd" : clipName);
                    track.Curves = UvAnimTrack.NewCurves();

                    var chans = new[]
                    {
                        UvAnimChannel.Row0X, UvAnimChannel.Row0Y, UvAnimChannel.Row0Z,
                        UvAnimChannel.Row1X, UvAnimChannel.Row1Y, UvAnimChannel.Row1Z,
                    };
                    foreach (var c in chans) track.Curve(c).Keys.Clear();

                    for (int f = 0; f < frames; f++)
                    {
                        float t = dur * f / Math.Max(1, frames - 1);
                        Vector4 row0 = new Vector4(1, 0, 0, 0), row1 = new Vector4(0, 1, 0, 0);
                        try
                        {
                            var pos = anim.GetFramePosition(t);
                            if (slot0 >= 0) row0 = anim.EvaluateVector4(pos, slot0, false);
                            if (slot1 >= 0) row1 = anim.EvaluateVector4(pos, slot1, false);
                        }
                        catch { }

                        track.Curve(UvAnimChannel.Row0X).Keys.Add(new UvAnimKey(t, row0.X));
                        track.Curve(UvAnimChannel.Row0Y).Keys.Add(new UvAnimKey(t, row0.Y));
                        track.Curve(UvAnimChannel.Row0Z).Keys.Add(new UvAnimKey(t, row0.Z));
                        track.Curve(UvAnimChannel.Row1X).Keys.Add(new UvAnimKey(t, row1.X));
                        track.Curve(UvAnimChannel.Row1Y).Keys.Add(new UvAnimKey(t, row1.Y));
                        track.Curve(UvAnimChannel.Row1Z).Keys.Add(new UvAnimKey(t, row1.Z));
                    }

                    foreach (var c in chans)
                    {
                        var cur = track.Curve(c);
                        if (cur.Keys.Count == 0) continue;
                        float first = cur.Keys[0].Value;
                        if (cur.Keys.All(k => Math.Abs(k.Value - first) < 1e-6f))
                        { cur.Keys.Clear(); cur.Constant = first; }
                    }

                    r.Tracks++;
                    r.Frames = Math.Max(r.Frames, frames);
                }
            }

            r.BoneAnims = CountBoneAnims_V16(ycd);

            if (r.Tracks == 0)
            {
                r.Message = r.BoneAnims > 0
                    ? $"no UV tracks here - {r.BoneAnims} bone animation(s) instead"
                    : "no UV tracks in this dictionary";
                return r;
            }

            r.Clip.Name = string.IsNullOrWhiteSpace(baseName) ? (ycd.Name ?? "uv_anim") : baseName;
            r.Clip.Duration = duration > 0.0001f ? duration : 4f;
            r.Clip.Fps = Math.Max(1, (int)Math.Round((maxFrames - 1) / Math.Max(r.Clip.Duration, 1e-4f)));
            r.Clip.Loop = true;
            r.Message = $"read {r.Tracks} UV track(s), {r.Frames} frame(s) over {r.Clip.Duration:0.##} s at {r.Clip.Fps} fps";
            return r;
        }

        public static int CountBoneAnims_V16(YcdFile ycd)
        {
            int n = 0;
            foreach (var cme in ycd?.ClipMapEntries ?? Array.Empty<ClipMapEntry>())
                foreach (var anim in UvAnimYcd.AnimationsOf(cme?.Clip))
                {
                    var ids = anim?.BoneIds?.data_items;
                    if (ids == null) continue;
                    if (ids.Any(b => b.Track == YcdBoneAnim_V6.TrackPosition ||
                                     b.Track == YcdBoneAnim_V6.TrackRotation ||
                                     b.Track == YcdBoneAnim_V6.TrackScale)) n++;
                }
            return n;
        }

        public static int SelfTest_V16(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string detail)
            { if (!ok) fails++; check(what, ok, detail); }

            Chk("v16 uv: a clip name carries its material index",
                MaterialIndexOf_V16("pack:/prop_fan_uv_3.clip", 0, null, -1) == 3,
                MaterialIndexOf_V16("pack:/prop_fan_uv_3.clip", 0, null, -1).ToString());
            Chk("v16 uv: ...and so does the KEY when the name does not",
                MaterialIndexOf_V16("something_else", JenkHash.GenHash("prop_fan") + 3, "prop_fan", -1) == 2,
                MaterialIndexOf_V16("something_else", JenkHash.GenHash("prop_fan") + 3, "prop_fan", -1).ToString());

            var clip = new UvAnimClip { Name = "v16_probe", Duration = 2f, Fps = 15 };
            var tr = clip.Add(0, "emissive");
            tr.Raw = true;
            int n = clip.FrameCount;
            for (int f = 0; f < n; f++)
            {
                float t = clip.Duration * f / (n - 1);
                tr.Curve(UvAnimChannel.Row0X).Keys.Add(new UvAnimKey(t, 1f));
                tr.Curve(UvAnimChannel.Row0Z).Keys.Add(new UvAnimKey(t, 0.25f * t));
                tr.Curve(UvAnimChannel.Row1Y).Keys.Add(new UvAnimKey(t, 1f));
            }

            var xml = UvAnimYcd.BuildXml(clip, out int wrote);
            YcdFile ycd = null;
            try { ycd = UvAnimYcd.FromXml_V16(xml); } catch { }
            if (ycd == null) { Chk("v16 uv: the exported dictionary parses", false, "could not build it"); return fails; }

            var back = FromYcd_V16(ycd, "v16_probe");
            Chk("v16 uv: an exported dictionary reads back as a track", back.Tracks == 1, back.Message);
            if (back.Tracks == 1)
            {
                var got = back.Clip.Tracks[0];
                Chk("v16 uv: ...on the right material", got.MaterialIndex == 0, got.MaterialIndex.ToString());
                Chk("v16 uv: ...with the frames it was written with", back.Frames == wrote, $"{back.Frames} vs {wrote}");

                float worst = 0f;
                for (int f = 0; f < n; f++)
                {
                    float t = clip.Duration * f / (n - 1);
                    tr.Evaluate(t, out var a0, out var b0);
                    got.Evaluate(t, out var a1, out var b1);
                    worst = Math.Max(worst, (a0 - a1).Length());
                    worst = Math.Max(worst, (b0 - b1).Length());
                }
                Chk("v16 uv: ...and it plays the same numbers it was given", worst < 0.002f, $"worst {worst:0.#####}");
                Chk("v16 uv: ...and the moving channel came back MOVING, not constant",
                    got.Curve(UvAnimChannel.Row0Z).Animated, "Row0Z keys " + got.Curve(UvAnimChannel.Row0Z).Keys.Count);
                Chk("v16 uv: ...while a channel that never moved is a constant",
                    !got.Curve(UvAnimChannel.Row0X).Animated &&
                    Math.Abs(got.Curve(UvAnimChannel.Row0X).Constant - 1f) < 1e-4f,
                    $"Row0X constant {got.Curve(UvAnimChannel.Row0X).Constant:0.###}");
            }
            return fails;
        }
    }
}

