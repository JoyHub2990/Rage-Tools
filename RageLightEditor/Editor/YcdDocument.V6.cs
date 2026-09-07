using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public static class YcdDocument_V6
    {

        public static YcdFile Open(string path, out string message)
        {
            message = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                { message = "No such file: " + path; return null; }

                if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    return FromXml(File.ReadAllText(path), out message);

                return OpenBytes(File.ReadAllBytes(path), Path.GetFileName(path), out message);
            }
            catch (Exception e) { message = e.Message; return null; }
        }

        public static YcdFile OpenBytes(byte[] data, string name, out string message)
        {
            message = null;
            try
            {
                if (data == null || data.Length < 16) { message = "Empty file: " + name; return null; }
                if (BitConverter.ToUInt32(data, 0) != 0x37435352u)
                { message = name + " has no RSC7 header - extract it in its on-disk form."; return null; }
                var ycd = new YcdFile();
                RpfFile.LoadResourceFile(ycd, data, 46);
                if (ycd.ClipDictionary == null)
                { message = "Not a clip dictionary: " + name; return null; }
                return ycd;
            }
            catch (Exception e) { message = e.Message; return null; }
        }

        public static string ToXml(YcdFile ycd) => ycd == null ? "" : YcdXml.GetXml(ycd);

        public static YcdFile FromXml(string xml, out string message)
        {
            message = null;
            try
            {
                if (string.IsNullOrWhiteSpace(xml)) { message = "The XML is empty."; return null; }
                var ycd = XmlYcd.GetYcd(xml);
                if (ycd?.ClipDictionary == null)
                { message = "The clip dictionary would not build from its XML."; return null; }
                return ycd;
            }
            catch (Exception e) { message = e.Message; return null; }
        }

        public static bool Save(YcdFile ycd, string path, out string message, out int bytes)
            => Save(ycd, path, null, false, out message, out bytes);

        public static bool Save(YcdFile ycd, string path, byte[] original, bool edited,
                                out string message, out int bytes)
        {
            message = null; bytes = 0;
            try
            {
                if (ycd?.ClipDictionary == null) { message = "Nothing to save."; return false; }
                if (string.IsNullOrWhiteSpace(path)) { message = "No file name."; return false; }
                if (!path.EndsWith(".ycd", StringComparison.OrdinalIgnoreCase)) path += ".ycd";

                if (!edited && original != null && original.Length > 0)
                {
                    File.WriteAllBytes(path, original);
                    bytes = original.Length;
                    var same = OpenBytes(original, Path.GetFileName(path), out _);
                    File.WriteAllText(path + ".xml", ToXml(same ?? ycd));
                    message = "exported unchanged - byte for byte the file that was opened";
                    return true;
                }

                var data = ycd.Save();
                if (data == null || data.Length == 0) { message = "The clip dictionary produced no data."; return false; }
                File.WriteAllBytes(path, data);
                bytes = data.Length;

                var reread = OpenBytes(data, Path.GetFileName(path), out var why);
                File.WriteAllText(path + ".xml", ToXml(reread ?? ycd));
                if (reread == null) { message = "Saved, but the file did not read back: " + why; return true; }

                if (SameValues_V6(ycd, reread, out float moved, out _))
                    message = moved <= 0.0000001f ? "written exactly"
                            : $"written; re-encoded channels moved by at most {moved:0.######}";
                else
                    message = $"WARNING - the saved file does not match what was open (worst {moved:0.######})";
                return true;
            }
            catch (Exception e) { message = e.Message; return false; }
        }

        public sealed class TrackInfo_V6
        {
            public ushort BoneId;
            public byte Track;
            public string TrackName = "";
            public string Kind = "";
            public int Frames;
            public string Sample = "";
        }

        public sealed class AnimInfo_V6
        {
            public string Hash = "";
            public int FrameCount;
            public float Duration;
            public float Fps => Duration > 0.0001f ? (FrameCount - 1) / Duration : 0f;
            public int SequenceCount;
            public int SequenceFrameLimit;
            public readonly List<TrackInfo_V6> Tracks = new List<TrackInfo_V6>();
            public bool IsUv => Tracks.Any(t => t.Track == 17 || t.Track == 18);
            public bool IsBone => Tracks.Any(t => t.Track <= 2);
        }

        public sealed class ClipInfo_V6
        {
            public string Name = "";
            public string Hash = "";
            public string Kind = "";
            public string AnimationHash = "";
            public float StartTime, EndTime, Rate;
            public float Duration => Math.Max(EndTime - StartTime, 0f);
            public uint Unknown30;
            public int TagCount, PropertyCount;
            public readonly List<string> Properties = new List<string>();
            public readonly List<string> Tags = new List<string>();
        }

        public sealed class Outline_V6
        {
            public readonly List<ClipInfo_V6> Clips = new List<ClipInfo_V6>();
            public readonly List<AnimInfo_V6> Animations = new List<AnimInfo_V6>();
            public int TrackCount => Animations.Sum(a => a.Tracks.Count);
            public int KeyframeCount => Animations.Sum(a => a.Tracks.Sum(t => t.Frames));
        }

        public static string TrackName_V6(byte track) => track switch
        {
            0 => "Position",
            1 => "Rotation",
            2 => "Scale",
            5 => "Camera FOV",
            6 => "Camera DOF",
            7 => "Camera unknown",
            17 => "globalAnimUV0",
            18 => "globalAnimUV1",
            21 => "Light intensity",
            22 => "Light falloff",
            23 => "Light cone angle",
            24 => "Light colour",
            27 => "Visibility",
            _ => "track " + track.ToString(CultureInfo.InvariantCulture),
        };

        public static Outline_V6 Describe(YcdFile ycd)
        {
            var o = new Outline_V6();
            var cd = ycd?.ClipDictionary;
            if (cd == null) return o;

            foreach (var cme in cd.Clips?.data_items ?? Array.Empty<ClipMapEntry>())
            {
                var clip = cme?.Clip;
                if (clip == null) continue;
                var ci = new ClipInfo_V6
                {
                    Name = clip.Name ?? "",
                    Hash = cme.Hash.ToString(),
                    Kind = clip.Type.ToString(),
                    Unknown30 = clip.Unknown_30h,
                    TagCount = clip.Tags?.Tags?.data_items?.Length ?? 0,
                    PropertyCount = clip.Properties?.Properties?.data_items?.Length ?? 0,
                };
                if (clip is ClipAnimation ca)
                {
                    ci.StartTime = ca.StartTime; ci.EndTime = ca.EndTime; ci.Rate = ca.Rate;
                    ci.AnimationHash = (ca.Animation?.Hash ?? ca.AnimationHash).ToString();
                }
                else if (clip is ClipAnimationList cal)
                {
                    ci.Rate = 1f;
                    ci.AnimationHash = (cal.Animations?.Count ?? 0) + " animation(s)";
                }
                foreach (var pe in clip.Properties?.Properties?.data_items ?? Array.Empty<ClipPropertyMapEntry>())
                {
                    for (var p = pe?.Data; p != null; p = null)
                        ci.Properties.Add(p.NameHash.ToString() + " = " + DescribeProperty_V6(p));
                    for (var nx = pe?.Next; nx != null; nx = nx.Next)
                        if (nx.Data != null) ci.Properties.Add(nx.Data.NameHash.ToString() + " = " + DescribeProperty_V6(nx.Data));
                }
                foreach (var t in clip.Tags?.Tags?.data_items ?? Array.Empty<ClipTag>())
                    if (t != null)
                        ci.Tags.Add(t.NameHash.ToString() + " " +
                                    t.StartPhase.ToString("0.###", CultureInfo.InvariantCulture) + " - " +
                                    t.EndPhase.ToString("0.###", CultureInfo.InvariantCulture));
                o.Clips.Add(ci);
            }

            foreach (var ame in cd.Animations?.Animations?.data_items ?? Array.Empty<AnimationMapEntry>())
            {
                var a = ame?.Animation;
                if (a == null) continue;
                var ai = new AnimInfo_V6
                {
                    Hash = a.Hash.ToString(),
                    FrameCount = a.Frames,
                    Duration = a.Duration,
                    SequenceFrameLimit = a.SequenceFrameLimit,
                    SequenceCount = a.Sequences?.data_items?.Length ?? 0,
                };

                var ids = a.BoneIds?.data_items ?? Array.Empty<AnimationBoneId>();
                for (int i = 0; i < ids.Length; i++)
                {
                    var ti = new TrackInfo_V6
                    {
                        BoneId = ids[i].BoneId,
                        Track = ids[i].Track,
                        TrackName = TrackName_V6(ids[i].Track),
                        Frames = a.Frames,
                    };
                    var kinds = new List<string>();
                    foreach (var seq in a.Sequences?.data_items ?? Array.Empty<Sequence>())
                    {
                        var sd = seq?.Sequences;
                        if (sd == null || i >= sd.Length) continue;
                        foreach (var ch in sd[i]?.Channels ?? Array.Empty<AnimChannel>())
                            if (ch != null) kinds.Add(ch.Type.ToString());
                        if (ti.Sample.Length == 0) ti.Sample = SampleOf_V6(sd[i], a.Frames);
                    }
                    ti.Kind = kinds.Count == 0 ? "(none)"
                            : string.Join(", ", kinds.GroupBy(k => k).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));
                    ai.Tracks.Add(ti);
                }
                o.Animations.Add(ai);
            }
            return o;
        }

        private static string DescribeProperty_V6(ClipProperty p)
        {
            var attrs = p?.Attributes?.data_items;
            if (attrs == null || attrs.Length == 0) return "(no value)";
            var parts = new List<string>();
            foreach (var a in attrs)
            {
                switch (a)
                {
                    case ClipPropertyAttributeFloat f: parts.Add(f.Value.ToString("0.###", CultureInfo.InvariantCulture)); break;
                    case ClipPropertyAttributeInt i: parts.Add(i.Value.ToString(CultureInfo.InvariantCulture)); break;
                    case ClipPropertyAttributeBool b: parts.Add(b.Value != 0 ? "true" : "false"); break;
                    default: parts.Add(a?.Type.ToString() ?? "?"); break;
                }
            }
            return string.Join(", ", parts);
        }

        private static string SampleOf_V6(AnimSequence seq, int frames)
        {
            try
            {
                var chans = seq?.Channels;
                if (chans == null || chans.Length == 0 || frames <= 0) return "";
                var vals = new List<string>();
                int n = Math.Min(3, frames);
                for (int f = 0; f < n; f++)
                {
                    var v = seq.EvaluateVector(f);
                    vals.Add($"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})");
                }
                return string.Join(" ", vals);
            }
            catch { return ""; }
        }

        public static bool SetClipTiming(YcdFile ycd, string clipName, float start, float end, float rate, out string message)
        {
            message = null;
            var clip = FindClip(ycd, clipName);
            if (clip == null) { message = "No clip called " + clipName; return false; }
            if (!(clip is ClipAnimation ca)) { message = "Only a single-animation clip has its own times."; return false; }
            if (end < start) { message = "The end is before the start."; return false; }
            if (rate <= 0f) { message = "The rate has to be above zero."; return false; }
            ca.StartTime = start; ca.EndTime = end; ca.Rate = rate;
            return true;
        }

        public static bool SetClipFlags(YcdFile ycd, string clipName, uint flags, out string message)
        {
            message = null;
            var clip = FindClip(ycd, clipName);
            if (clip == null) { message = "No clip called " + clipName; return false; }
            clip.Unknown_30h = flags;
            return true;
        }

        public static bool RenameClip(YcdFile ycd, string oldName, string newName, out string message)
        {
            message = null;
            var cd = ycd?.ClipDictionary;
            if (cd?.Clips?.data_items == null) { message = "Nothing is open."; return false; }
            if (string.IsNullOrWhiteSpace(newName)) { message = "A clip needs a name."; return false; }

            foreach (var cme in cd.Clips.data_items)
            {
                if (cme?.Clip == null) continue;
                if (!string.Equals(cme.Clip.Name, oldName, StringComparison.OrdinalIgnoreCase)) continue;
                cme.Clip.Name = newName;
                cme.Hash = JenkHash.GenHash(newName.ToLowerInvariant());
                cme.Clip.Hash = cme.Hash;
                RebuildMaps(ycd);
                return true;
            }
            message = "No clip called " + oldName;
            return false;
        }

        public static bool DeleteClip(YcdFile ycd, string clipName, out string message)
        {
            message = null;
            var cd = ycd?.ClipDictionary;
            if (cd?.Clips?.data_items == null) { message = "Nothing is open."; return false; }
            var keep = cd.Clips.data_items.Where(c => c?.Clip != null &&
                          !string.Equals(c.Clip.Name, clipName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (keep.Length == cd.Clips.data_items.Length) { message = "No clip called " + clipName; return false; }
            cd.Clips.data_items = keep;
            RebuildMaps(ycd);
            return true;
        }

        public static ClipBase FindClip(YcdFile ycd, string name)
        {
            foreach (var cme in ycd?.ClipDictionary?.Clips?.data_items ?? Array.Empty<ClipMapEntry>())
                if (cme?.Clip != null && string.Equals(cme.Clip.Name, name, StringComparison.OrdinalIgnoreCase))
                    return cme.Clip;
            return null;
        }

        public static Animation FindAnimation(YcdFile ycd, MetaHash hash)
        {
            foreach (var ame in ycd?.ClipDictionary?.Animations?.Animations?.data_items ?? Array.Empty<AnimationMapEntry>())
                if (ame?.Animation != null && ame.Hash == hash) return ame.Animation;
            return null;
        }

        public static void RebuildMaps(YcdFile ycd)
        {
            var cd = ycd?.ClipDictionary;
            if (cd == null) return;
            var clipMap = new Dictionary<MetaHash, ClipMapEntry>();
            foreach (var c in cd.Clips?.data_items ?? Array.Empty<ClipMapEntry>())
                if (c != null) clipMap[c.Hash] = c;
            cd.ClipMap = clipMap;
            ycd.ClipMap = clipMap;

            var animMap = new Dictionary<MetaHash, AnimationMapEntry>();
            foreach (var a in cd.Animations?.Animations?.data_items ?? Array.Empty<AnimationMapEntry>())
                if (a != null) animMap[a.Hash] = a;
            cd.AnimMap = animMap;
            ycd.AnimMap = animMap;
        }

        public static bool RoundTrip(byte[] original, string name, out string report)
            => RoundTrip(original, name, out report, out _, out _);

        public static bool RoundTrip(byte[] original, string name, out string report,
                                     out bool identical, out float worstDelta)
        {
            report = ""; identical = false; worstDelta = 0f;
            var a = OpenBytes(original, name, out var why1);
            if (a == null) { report = "would not open: " + why1; return false; }
            string xmlA = ToXml(a);

            byte[] saved;
            try { saved = a.Save(); }
            catch (Exception e) { report = "would not save: " + e.Message; return false; }

            var b = OpenBytes(saved, name, out var why2);
            if (b == null) { report = "the saved file would not open: " + why2; return false; }
            string xmlB = ToXml(b);

            var oa = Describe(a);
            report = $"{oa.Clips.Count} clip(s), {oa.Animations.Count} animation(s), {oa.TrackCount} track(s), " +
                     $"{oa.KeyframeCount} keyframe(s); {original.Length:N0} -> {saved.Length:N0} bytes";

            identical = xmlA == xmlB;
            if (identical) return true;

            if (!SameValues_V6(a, b, out worstDelta, out var where))
            {
                report += $" | VALUES MOVED by {worstDelta:0.######} at {where} | {FirstDifference_V6(xmlA, xmlB)}";
                return false;
            }
            report += $" | re-encoded, values match to {worstDelta:0.######}";
            return true;
        }

        public static bool SameValues_V6(YcdFile a, YcdFile b, out float worst, out string where)
        {
            worst = 0f; where = "";
            var aa = AnimationsOf_V6(a);
            var bb = AnimationsOf_V6(b);
            if (aa.Count != bb.Count) { where = $"{aa.Count} animation(s) became {bb.Count}"; worst = float.MaxValue; return false; }

            foreach (var kv in aa)
            {
                if (!bb.TryGetValue(kv.Key, out var b2))
                { where = "animation " + kv.Key + " is not in the saved file"; worst = float.MaxValue; return false; }
                var a2 = kv.Value;

                var ia = a2.BoneIds?.data_items ?? Array.Empty<AnimationBoneId>();
                var ib = b2.BoneIds?.data_items ?? Array.Empty<AnimationBoneId>();
                if (ia.Length != ib.Length)
                { where = $"animation {kv.Key}: {ia.Length} track(s) became {ib.Length}"; worst = float.MaxValue; return false; }

                for (int t = 0; t < ia.Length; t++)
                {
                    if (ia[t].BoneId != ib[t].BoneId || ia[t].Track != ib[t].Track)
                    { where = $"animation {kv.Key} track {t}: bone {ia[t].BoneId}/{ia[t].Track} became {ib[t].BoneId}/{ib[t].Track}"; worst = float.MaxValue; return false; }

                    int frames = Math.Min(a2.Frames, b2.Frames);
                    for (int f = 0; f < frames; f++)
                    {
                        var va = ValueAt_V6(a2, t, f);
                        var vb = ValueAt_V6(b2, t, f);
                        float d = Math.Max(Math.Max(Math.Abs(va.X - vb.X), Math.Abs(va.Y - vb.Y)),
                                           Math.Max(Math.Abs(va.Z - vb.Z), Math.Abs(va.W - vb.W)));
                        if (d > worst)
                        {
                            worst = d;
                            where = $"animation {kv.Key}, bone {ia[t].BoneId} {TrackName_V6(ia[t].Track)}, frame {f}";
                        }
                    }
                }
            }
            return worst <= 0.01f;
        }

        public static SharpDX.Vector4 ValueAt_V6(Animation anim, int trackIndex, int frame)
        {
            var seqs = anim?.Sequences?.data_items;
            if (seqs == null || seqs.Length == 0) return SharpDX.Vector4.Zero;

            int limit = anim.SequenceFrameLimit > 0 ? anim.SequenceFrameLimit : Math.Max((int)anim.Frames, 1);
            int si = Math.Clamp(frame / limit, 0, seqs.Length - 1);
            int local = frame % limit;

            var sd = seqs[si]?.Sequences;
            if (sd == null || trackIndex >= sd.Length || sd[trackIndex] == null) return SharpDX.Vector4.Zero;
            int n = Math.Max((int)seqs[si].NumFrames, 1);
            try { return sd[trackIndex].EvaluateVector(Math.Min(local, n - 1)); }
            catch { return SharpDX.Vector4.Zero; }
        }

        private static Dictionary<MetaHash, Animation> AnimationsOf_V6(YcdFile ycd)
        {
            var d = new Dictionary<MetaHash, Animation>();
            foreach (var ame in ycd?.ClipDictionary?.Animations?.Animations?.data_items ?? Array.Empty<AnimationMapEntry>())
                if (ame?.Animation != null) d[ame.Hash] = ame.Animation;
            return d;
        }

        public static string FirstDifference_V6(string a, string b)
        {
            var la = (a ?? "").Replace("\r\n", "\n").Split('\n');
            var lb = (b ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                string x = i < la.Length ? la[i].Trim() : "(end)";
                string y = i < lb.Length ? lb[i].Trim() : "(end)";
                if (x == y) continue;
                if (x.Length > 70) x = x.Substring(0, 70) + "...";
                if (y.Length > 70) y = y.Substring(0, 70) + "...";
                return $"line {i + 1}: '{x}' became '{y}'";
            }
            return la.Length == lb.Length ? "no difference found" : "one is longer";
        }
    }
}

