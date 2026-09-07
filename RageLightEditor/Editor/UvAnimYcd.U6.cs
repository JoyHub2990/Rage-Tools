using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class UvAnimYcd
    {
        public const byte TrackUV0 = 17;
        public const byte TrackUV1 = 18;

        public const uint UvAnimUnknown1C = 0x6B002400;

        public struct Result
        {
            public bool Ok;
            public string Message;
            public string Path;
            public int Tracks;
            public int Frames;
            public int Bytes;
        }

        public static string BuildXml(UvAnimClip clip, out int frames)
        {
            frames = clip?.FrameCount ?? 2;
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;

            var tracks = (clip?.Tracks ?? new List<UvAnimTrack>()).Where(t => t != null && t.Enabled).ToList();

            float duration = Math.Max(clip?.Duration ?? 1.0f, 0.01f);
            string baseName = Sanitise(clip?.Name);

            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<ClipDictionary>");

            sb.AppendLine("  <Clips>");
            for (int i = 0; i < tracks.Count; i++)
            {
                string clipName = ClipNameFor(baseName, tracks[i]);
                string animName = AnimNameFor(baseName, tracks[i]);
                uint clipHash = UvClipHash(baseName, tracks[i].MaterialIndex);
                sb.AppendLine("    <Item>");
                sb.AppendLine($"      <Hash>hash_{clipHash:X8}</Hash>");
                sb.AppendLine($"      <Name>{Esc(clipName)}</Name>");
                sb.AppendLine("      <Type value=\"Animation\" />");
                sb.AppendLine("      <Unknown30 value=\"0\" />");
                sb.AppendLine("      <Tags />");
                sb.AppendLine("      <Properties />");
                sb.AppendLine($"      <AnimationHash>{Esc(animName)}</AnimationHash>");
                sb.AppendLine("      <StartTime value=\"0\" />");
                sb.AppendLine($"      <EndTime value=\"{F(duration)}\" />");
                sb.AppendLine("      <Rate value=\"1\" />");
                sb.AppendLine("    </Item>");
            }
            sb.AppendLine("  </Clips>");

            sb.AppendLine("  <Animations>");
            foreach (var tr in tracks)
            {
                string animName = AnimNameFor(baseName, tr);
                var rows = new Vector4[frames][];
                for (int f = 0; f < frames; f++)
                {
                    float t = duration * f / (frames - 1);
                    tr.Evaluate(t, out var a, out var b);
                    rows[f] = new[] { a, b };
                }

                sb.AppendLine("    <Item>");
                sb.AppendLine($"      <Hash>{Esc(animName)}</Hash>");
                sb.AppendLine("      <Unknown10 value=\"1\" />");
                sb.AppendLine($"      <FrameCount value=\"{frames}\" />");
                sb.AppendLine($"      <SequenceFrameLimit value=\"{frames + 2}\" />");
                sb.AppendLine($"      <Duration value=\"{F(duration)}\" />");
                sb.AppendLine($"      <Unknown1C>hash_{UvAnimUnknown1C:X8}</Unknown1C>");
                sb.AppendLine("      <BoneIds>");
                sb.AppendLine($"        <Item><BoneId value=\"0\" /><Track value=\"{TrackUV0}\" /><Unk0 value=\"0\" /></Item>");
                sb.AppendLine($"        <Item><BoneId value=\"0\" /><Track value=\"{TrackUV1}\" /><Unk0 value=\"0\" /></Item>");
                sb.AppendLine("      </BoneIds>");
                sb.AppendLine("      <Sequences>");
                sb.AppendLine("        <Item>");
                sb.AppendLine("          <Hash>hash_00000000</Hash>");
                sb.AppendLine($"          <FrameCount value=\"{frames}\" />");
                sb.AppendLine("          <SequenceData>");
                for (int row = 0; row < 2; row++)
                {
                    sb.AppendLine("            <Item>");
                    sb.AppendLine("              <Channels>");
                    for (int comp = 0; comp < 3; comp++)
                    {
                        sb.AppendLine("                <Item>");
                        sb.AppendLine("                  <Type value=\"RawFloat\" />");
                        sb.Append("                  <Values>");
                        for (int f = 0; f < frames; f++)
                        {
                            var v = rows[f][row];
                            float x = comp == 0 ? v.X : comp == 1 ? v.Y : v.Z;
                            if (f > 0) sb.Append(' ');
                            sb.Append(F(x));
                        }
                        sb.AppendLine("</Values>");
                        sb.AppendLine("                </Item>");
                    }
                    sb.AppendLine("              </Channels>");
                    sb.AppendLine("            </Item>");
                }
                sb.AppendLine("          </SequenceData>");
                sb.AppendLine("        </Item>");
                sb.AppendLine("      </Sequences>");
                sb.AppendLine("    </Item>");
            }
            sb.AppendLine("  </Animations>");
            sb.AppendLine("</ClipDictionary>");
            return sb.ToString();
        }

        public static Result Write(UvAnimClip clip, string path)
        {
            var r = new Result { Path = path };
            try
            {
                var live = (clip?.Tracks ?? new List<UvAnimTrack>()).Count(t => t != null && t.Enabled);
                if (live == 0)
                {
                    r.Message = "Nothing to export: no material has an enabled animation track.";
                    return r;
                }

                var xml = BuildXml(clip, out int frames);
                try { File.WriteAllText(path + ".xml", xml); } catch { }

                var ycd = XmlYcd.GetYcd(xml);
                if (ycd?.ClipDictionary == null) { r.Message = "The clip dictionary would not build from its XML."; return r; }
                var data = ycd.Save();
                if (data == null || data.Length == 0) { r.Message = "The resource builder produced no bytes."; return r; }
                File.WriteAllBytes(path, data);

                r.Ok = true;
                r.Tracks = live;
                r.Frames = frames;
                r.Bytes = data.Length;
                r.Message = $"Wrote {Path.GetFileName(path)}: {live} clip(s), {frames} frames at " +
                            $"{clip.Fps} fps, {data.Length:N0} bytes (and the .ycd.xml beside it).";
                return r;
            }
            catch (Exception ex)
            {
                r.Message = "Export failed: " + ex.Message;
                return r;
            }
        }

        public static string SanitiseName_V16(string s) => Sanitise(s);

        public static YcdFile FromXml_V16(string xml)
        {
            var built = XmlYcd.GetYcd(xml);
            if (built?.ClipDictionary == null) return null;
            var data = built.Save();
            if (data == null || data.Length == 0) return null;
            var ycd = new YcdFile();
            RpfFile.LoadResourceFile(ycd, data, 46);
            return ycd.ClipDictionary == null ? null : ycd;
        }

        public class FoundTrack
        {
            public string ClipName = "";
            public string AnimName = "";
            public uint ClipHash;
            public int BoneId;
            public byte Track;
            public int Frames;
            public float Duration;
            public Vector4 First, Last;
            public byte Unk10;
            public uint Unk1C;
            public override string ToString() =>
                $"{ClipName} hash {ClipHash:X8} bone {BoneId} track {Track} {Frames}f {Duration:0.###}s " +
                $"unk10 {Unk10} unk1C {Unk1C:X8} " +
                $"first ({First.X:0.###},{First.Y:0.###},{First.Z:0.###}) last ({Last.X:0.###},{Last.Y:0.###},{Last.Z:0.###})";
        }

        public static List<FoundTrack> Read(string path, out string message)
        {
            try
            {
                var data = File.ReadAllBytes(path);
                var ycd = new YcdFile();
                RpfFile.LoadResourceFile(ycd, data, 46);
                if (ycd.ClipDictionary == null) { message = "Not a clip dictionary: " + Path.GetFileName(path); return null; }
                var list = Describe(ycd);
                message = list.Count == 0
                    ? $"{Path.GetFileName(path)} read back, but it carries no track 17/18 - no UV animation in it."
                    : $"{Path.GetFileName(path)} read back: {list.Count} UV track(s) over {ycd.ClipMapEntries?.Length ?? 0} clip(s).";
                return list;
            }
            catch (Exception ex) { message = "Could not read back: " + ex.Message; return null; }
        }

        public static List<FoundTrack> Describe(YcdFile ycd)
        {
            var found = new List<FoundTrack>();
            if (ycd?.ClipMapEntries == null) return found;
            foreach (var cme in ycd.ClipMapEntries)
            {
                var clipName = cme?.Clip?.Name ?? cme?.Clip?.ShortName ?? "";
                foreach (var anim in AnimationsOf(cme?.Clip))
                {
                    var ids = anim?.BoneIds?.data_items;
                    if (ids == null) continue;
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (ids[i].Track != TrackUV0 && ids[i].Track != TrackUV1) continue;
                        var ft = new FoundTrack
                        {
                            ClipName = clipName,
                            AnimName = anim.Hash.ToString(),
                            ClipHash = cme?.Hash ?? 0,
                            BoneId = ids[i].BoneId,
                            Track = ids[i].Track,
                            Frames = anim.Frames,
                            Duration = anim.Duration,
                            Unk10 = anim.Unknown_10h,
                            Unk1C = anim.Unknown_1Ch,
                        };
                        try
                        {
                            ft.First = anim.EvaluateVector4(anim.GetFramePosition(0f), i, false);
                            ft.Last = anim.EvaluateVector4(anim.GetFramePosition(anim.Duration), i, false);
                        }
                        catch { }
                        found.Add(ft);
                    }
                }
            }
            return found;
        }

        public static IEnumerable<Animation> AnimationsOf(ClipBase clip)
        {
            if (clip is ClipAnimation ca) { if (ca.Animation != null) yield return ca.Animation; yield break; }
            if (clip is ClipAnimationList cal && cal.Animations != null)
                foreach (var e in cal.Animations) if (e?.Animation != null) yield return e.Animation;
        }

        public static string ClipNameFor(string baseName, UvAnimTrack t) =>
            $"pack:/{Sanitise(baseName)}_uv_{Math.Max(t?.MaterialIndex ?? 0, 0)}.clip";

        public static uint UvClipHash(string baseName, int materialIndex) =>
            JenkHash.GenHash(Sanitise(baseName)) + (uint)(Math.Max(materialIndex, 0) + 1);

        public static string AnimNameFor(string baseName, UvAnimTrack t) =>
            $"{Sanitise(baseName)}_uv_{Math.Max(t?.MaterialIndex ?? 0, 0)}";

        private static string Sanitise(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "uv_anim";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            var outp = sb.ToString().Trim('_');
            return outp.Length == 0 ? "uv_anim" : outp;
        }

        private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;

        private static string F(float v) =>
            (v == 0.0f ? 0.0f : v).ToString("0.#######", CultureInfo.InvariantCulture);

        public static bool RoundTrip(UvAnimClip clip, string path, out string message, out float worstError)
        {
            worstError = 0f;
            var w = Write(clip, path);
            if (!w.Ok) { message = w.Message; return false; }

            var back = Read(path, out var readMsg);
            if (back == null) { message = readMsg; return false; }

            var live = clip.Tracks.Where(t => t != null && t.Enabled).ToList();
            if (back.Count != live.Count * 2)
            {
                message = $"{readMsg} - expected {live.Count * 2} tracks (two rows each), found {back.Count}";
                return false;
            }

            foreach (var t in live)
            {
                uint key = UvClipHash(clip.Name, t.MaterialIndex);
                var mine = back.Where(f => f.ClipHash == key).ToList();
                var uv0 = mine.FirstOrDefault(f => f.Track == TrackUV0);
                var uv1 = mine.FirstOrDefault(f => f.Track == TrackUV1);
                if (uv0 == null || uv1 == null)
                {
                    message = $"material {t.MaterialIndex} came back without both rows " +
                              $"(looked for clip key {key:X8}; got {string.Join(" ", back.Select(f => f.ClipHash.ToString("X8")))})";
                    return false;
                }
                t.Evaluate(0f, out var a0, out var a1);
                worstError = Math.Max(worstError, Math.Max(Err(a0, uv0.First), Err(a1, uv1.First)));
                t.Evaluate(clip.Duration, out var b0, out var b1);
                worstError = Math.Max(worstError, Math.Max(Err(b0, uv0.Last), Err(b1, uv1.Last)));
            }

            bool ok = worstError < 1e-3f;
            message = ok
                ? $"{w.Message}\n{readMsg} Values match to {worstError:0.#######}."
                : $"{w.Message}\n{readMsg} BUT the values came back {worstError:0.#####} off.";
            return ok;
        }

        private static float Err(Vector4 a, Vector4 b) =>
            Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Abs(a.Z - b.Z));
    }
}

