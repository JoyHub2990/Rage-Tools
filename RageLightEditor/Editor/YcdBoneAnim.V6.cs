using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class YcdBoneAnim_V6
    {
        public const byte TrackPosition = 0, TrackRotation = 1, TrackScale = 2;

        public const uint Unknown1C_V6 = 0x6B002400;

        public sealed class BoneTrack_V6
        {
            public ushort BoneId;
            public byte Track = TrackPosition;
            public readonly List<Vector4> Frames = new List<Vector4>();
            public int Components => Track == TrackRotation ? 4 : 3;
            public string Describe() =>
                $"bone {BoneId} {YcdDocument_V6.TrackName_V6(Track)}, {Frames.Count} frame(s)";
        }

        public static BoneTrack_V6 Straight(ushort boneId, byte track, IEnumerable<Vector3> frames)
        {
            var t = new BoneTrack_V6 { BoneId = boneId, Track = track };
            foreach (var v in frames ?? Enumerable.Empty<Vector3>()) t.Frames.Add(new Vector4(v, 0f));
            return t;
        }

        public static BoneTrack_V6 Turning(ushort boneId, IEnumerable<Quaternion> frames)
        {
            var t = new BoneTrack_V6 { BoneId = boneId, Track = TrackRotation };
            foreach (var q in frames ?? Enumerable.Empty<Quaternion>())
            {
                var n = q; n.Normalize();
                t.Frames.Add(new Vector4(n.X, n.Y, n.Z, n.W));
            }
            return t;
        }

        public static YcdFile Build(string name, List<BoneTrack_V6> tracks, int frames, float fps, out string message)
        {
            message = null;
            var xml = BuildXml(name, tracks, frames, fps, out message);
            if (xml == null) return null;
            var ycd = YcdDocument_V6.FromXml(xml, out message);
            if (ycd != null) YcdDocument_V6.RebuildMaps(ycd);
            return ycd;
        }

        public static string BuildXml(string name, List<BoneTrack_V6> tracks, int frames, float fps, out string message)
        {
            message = null;
            if (tracks == null || tracks.Count == 0) { message = "An animation needs at least one track."; return null; }
            if (frames < 2) { message = "An animation needs at least two frames."; return null; }
            if (fps <= 0f) { message = "The frame rate has to be above zero."; return null; }

            string baseName = Sanitise_V6(name);
            string animName = baseName;
            string clipName = "pack:/" + baseName + ".clip";
            float duration = (frames - 1) / fps;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<ClipDictionary>");
            sb.AppendLine("  <Clips>");
            sb.AppendLine("    <Item>");
            sb.AppendLine($"      <Hash>hash_{JenkHash.GenHash(clipName.ToLowerInvariant()):X8}</Hash>");
            sb.AppendLine($"      <Name>{Esc_V6(clipName)}</Name>");
            sb.AppendLine("      <Type value=\"Animation\" />");
            sb.AppendLine("      <Unknown30 value=\"0\" />");
            sb.AppendLine("      <Tags />");
            sb.AppendLine("      <Properties />");
            sb.AppendLine($"      <AnimationHash>{Esc_V6(animName)}</AnimationHash>");
            sb.AppendLine("      <StartTime value=\"0\" />");
            sb.AppendLine($"      <EndTime value=\"{F_V6(duration)}\" />");
            sb.AppendLine("      <Rate value=\"1\" />");
            sb.AppendLine("    </Item>");
            sb.AppendLine("  </Clips>");

            sb.AppendLine("  <Animations>");
            sb.AppendLine("    <Item>");
            sb.AppendLine($"      <Hash>{Esc_V6(animName)}</Hash>");
            sb.AppendLine("      <Unknown10 value=\"1\" />");
            sb.AppendLine($"      <FrameCount value=\"{frames}\" />");
            sb.AppendLine($"      <SequenceFrameLimit value=\"{frames + 2}\" />");
            sb.AppendLine($"      <Duration value=\"{F_V6(duration)}\" />");
            sb.AppendLine($"      <Unknown1C>hash_{Unknown1C_V6:X8}</Unknown1C>");

            sb.AppendLine("      <BoneIds>");
            foreach (var t in tracks)
                sb.AppendLine($"        <Item><BoneId value=\"{t.BoneId}\" /><Track value=\"{t.Track}\" /><Unk0 value=\"0\" /></Item>");
            sb.AppendLine("      </BoneIds>");

            sb.AppendLine("      <Sequences>");
            sb.AppendLine("        <Item>");
            sb.AppendLine("          <Hash>hash_00000000</Hash>");
            sb.AppendLine($"          <FrameCount value=\"{frames}\" />");
            sb.AppendLine("          <SequenceData>");
            foreach (var t in tracks)
            {
                sb.AppendLine("            <Item>");
                sb.AppendLine("              <Channels>");
                for (int c = 0; c < t.Components; c++)
                {
                    sb.AppendLine("                <Item>");
                    sb.AppendLine("                  <Type value=\"RawFloat\" />");
                    sb.Append("                  <Values>");
                    for (int f = 0; f < frames; f++)
                    {
                        var v = At_V6(t, f);
                        float x = c == 0 ? v.X : c == 1 ? v.Y : c == 2 ? v.Z : v.W;
                        if (f > 0) sb.Append(' ');
                        sb.Append(F_V6(x));
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
            sb.AppendLine("  </Animations>");
            sb.AppendLine("</ClipDictionary>");
            return sb.ToString();
        }

        private static Vector4 At_V6(BoneTrack_V6 t, int frame)
        {
            if (t.Frames.Count == 0) return t.Track == TrackRotation ? new Vector4(0, 0, 0, 1) :
                                            t.Track == TrackScale ? new Vector4(1, 1, 1, 0) : Vector4.Zero;
            return t.Frames[Math.Clamp(frame, 0, t.Frames.Count - 1)];
        }

        public static List<BoneTrack_V6> Read(Animation anim)
        {
            var outp = new List<BoneTrack_V6>();
            var ids = anim?.BoneIds?.data_items;
            if (ids == null) return outp;

            for (int i = 0; i < ids.Length; i++)
            {
                var t = new BoneTrack_V6 { BoneId = ids[i].BoneId, Track = ids[i].Track };
                for (int f = 0; f < anim.Frames; f++)
                    t.Frames.Add(YcdDocument_V6.ValueAt_V6(anim, i, f));
                outp.Add(t);
            }
            return outp;
        }

        public static string Sanitise_V6(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "anim";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            var outp = sb.ToString().Trim('_');
            return outp.Length == 0 ? "anim" : outp;
        }

        private static string F_V6(float f) => f.ToString("0.#######", CultureInfo.InvariantCulture);

        private static string Esc_V6(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

