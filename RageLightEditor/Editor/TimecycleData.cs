using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Xml;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class TimecycleData
    {

        public struct Sample
        {
            public float Hour;
            public float Duration;
        }

        public readonly List<Sample> Samples = new List<Sample>();
        public float SunRoll = 122.0f;
        public float MoonRoll = -122.0f;

        public int CurrentSampleIndex { get; private set; }
        public float CurrentSampleBlend { get; private set; } = 1.0f;
        public float CurrentHour { get; private set; }

        private static readonly (float h, float d)[] DefaultSchedule =
        {
            (0f,4f), (5f,0f), (6f,0f), (7f,0f), (10f,2f), (12f,3f), (16f,1f),
            (17f,0f), (18f,0f), (19f,0f), (20f,0f), (21f,0f), (22f,1f), (23f,0f),
        };

        public bool ScheduleFromFile { get; private set; }

        public TimecycleData()
        {
            UseDefaultSchedule();
        }

        public void UseDefaultSchedule()
        {
            Samples.Clear();
            foreach (var (h, d) in DefaultSchedule) Samples.Add(new Sample { Hour = h, Duration = d });
            ScheduleFromFile = false;
        }

        public bool LoadScheduleXml(string path, out string error)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                return LoadScheduleDoc(doc, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool LoadScheduleXmlText(string xml, out string error)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return LoadScheduleDoc(doc, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private bool LoadScheduleDoc(XmlDocument doc, out string error)
        {
            error = null;
            try
            {
                var root = doc.DocumentElement;
                var samples = new List<Sample>();
                foreach (XmlNode n in root.ChildNodes)
                {
                    if (n.NodeType != XmlNodeType.Element) continue;
                    if (string.Equals(n.Name, "sample", StringComparison.OrdinalIgnoreCase))
                    {
                        samples.Add(new Sample
                        {
                            Hour = Attr(n, "hour"),
                            Duration = Attr(n, "duration"),
                        });
                    }
                    else if (string.Equals(n.Name, "suninfo", StringComparison.OrdinalIgnoreCase))
                    {
                        SunRoll = Attr(n, "sun_roll", 122.0f);
                    }
                    else if (string.Equals(n.Name, "mooninfo", StringComparison.OrdinalIgnoreCase))
                    {
                        MoonRoll = Attr(n, "moon_roll", -122.0f);
                    }
                }
                if (samples.Count == 0) { error = "No <sample> entries found in time.xml"; return false; }
                Samples.Clear();
                Samples.AddRange(samples);
                ScheduleFromFile = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static float Attr(XmlNode node, string name, float fallback = 0.0f)
        {
            var a = node?.Attributes?[name]?.Value;
            if (string.IsNullOrEmpty(a)) return fallback;
            return float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public void SetTime(float hour)
        {
            if (Samples.Count == 0) return;
            float day = Math.Max(hour / 24.0f, 0.0f);
            float h = hour - ((float)Math.Floor(day) * 24.0f);
            CurrentHour = h;

            for (int i = 0; i < Samples.Count; i++)
            {
                bool lasti = (i >= Samples.Count - 1);
                var cur = Samples[i];
                var nxt = Samples[lasti ? 0 : i + 1];
                var nxth = lasti ? nxt.Hour + 24.0f : nxt.Hour;
                if (((h >= cur.Hour) && (h < nxth)) || lasti)
                {
                    float blendrange = (nxth - cur.Hour) - cur.Duration;
                    float blendstart = cur.Hour + cur.Duration;
                    float blendrel = h - blendstart;
                    float blendval = blendrange > 0.0001f ? blendrel / blendrange : 0.0f;
                    float blend = Math.Min(Math.Max(blendval, 0.0f), 1.0f);
                    CurrentSampleBlend = 1.0f - blend;
                    CurrentSampleIndex = i;
                    break;
                }
            }
        }

        public class Region
        {
            public string Name = "";
            public readonly Dictionary<string, float[]> Values = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, float[]> Original;

            public float Get(string name, int sample, float curblend, float fallback = 0.0f)
            {
                if (!Values.TryGetValue(name, out var vals) || vals.Length == 0) return fallback;
                if (sample >= vals.Length) sample = vals.Length - 1;
                if (sample < 0) sample = 0;
                int nxt = (sample < vals.Length - 1) ? sample + 1 : 0;
                return vals[sample] * curblend + vals[nxt] * (1.0f - curblend);
            }

            public bool Has(string name) => Values.ContainsKey(name);
        }

        public class Modifier
        {
            public string Name = "";
            public string Source = "";
            public uint NameHash;
            public readonly Dictionary<string, float> Values =
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, float> Original;
            public override string ToString() => Name;
        }

        public readonly List<Modifier> Modifiers = new List<Modifier>();
        public int SelectedModifier = -1;
        public bool SuppressDirectionalIndoors = true;

        public float ModifierStrength = 1.0f;

        public Modifier CurrentModifier =>
            (SelectedModifier >= 0 && SelectedModifier < Modifiers.Count) ? Modifiers[SelectedModifier] : null;

        public bool LoadModifiersXml(string path, out string error)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                return LoadModifiersDoc(doc, System.IO.Path.GetFileName(path), out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool LoadModifiersXmlText(string xml, string source, out string error)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return LoadModifiersDoc(doc, source, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private bool LoadModifiersDoc(XmlDocument doc, string source, out string error)
        {
            error = null;
            var nodes = doc.GetElementsByTagName("modifier");
            if (nodes.Count == 0)
            {
                error = "No <modifier> entries found (is this a timecycle_mods_*.xml?)";
                return false;
            }

            int added = 0;
            foreach (XmlNode mn in nodes)
            {
                var name = mn.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(name)) continue;

                var mod = new Modifier
                {
                    Name = name,
                    Source = source,
                    NameHash = CodeWalker.GameFiles.JenkHash.GenHash(name.ToLowerInvariant()),
                };
                foreach (XmlNode vn in mn.ChildNodes)
                {
                    if (vn.NodeType != XmlNodeType.Element) continue;
                    var parts = (vn.InnerText ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        mod.Values[vn.Name] = v;
                }
                if (mod.Values.Count == 0) continue;

                var existing = Modifiers.FindIndex(m => m.Name == name && m.Source == source);
                mod.Original = new Dictionary<string, float>(mod.Values, StringComparer.OrdinalIgnoreCase);
                if (existing >= 0) Modifiers[existing] = mod; else Modifiers.Add(mod);
                added++;
            }

            Modifiers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            SelectedModifier = -1;
            if (added == 0) { error = "No usable variables in any <modifier>"; return false; }
            return true;
        }

        public int FindModifier(uint nameHash, string name)
        {
            for (int i = 0; i < Modifiers.Count; i++)
            {
                if (Modifiers[i].NameHash == nameHash) return i;
            }
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < Modifiers.Count; i++)
                    if (string.Equals(Modifiers[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public void SaveTimecycleXml(string path)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine();
            sb.AppendLine("<timecycle_keyframe_data version=\"1.000000\">");
            sb.AppendLine($"  <cycle name=\"{Esc(LoadedName ?? "CUSTOM")}\" regions=\"{Regions.Count}\">");
            foreach (var r in Regions)
            {
                sb.AppendLine($"    <region name=\"{Esc(r.Name)}\">");
                foreach (var kv in r.Values)
                {
                    sb.Append("      <").Append(kv.Key).Append('>');
                    for (int i = 0; i < kv.Value.Length; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(kv.Value[i].ToString("0.0000", CultureInfo.InvariantCulture));
                    }
                    sb.Append("</").Append(kv.Key).AppendLine(">");
                }
                sb.AppendLine("    </region>");
            }
            sb.AppendLine("  </cycle>");
            sb.AppendLine("</timecycle_keyframe_data>");
            File.WriteAllText(path, sb.ToString());
        }

        public void SaveModifiersXml(string path, string sourceFilter = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine();
            sb.AppendLine("<timecycle_modifier_data version=\"1.000000\">");
            foreach (var m in Modifiers)
            {
                if (sourceFilter != null && m.Source != sourceFilter) continue;
                sb.AppendLine($"  <modifier name=\"{Esc(m.Name)}\" numMods=\"{m.Values.Count}\" userFlags=\"0\">");
                foreach (var kv in m.Values)
                {
                    sb.AppendLine($"    <{kv.Key}>{kv.Value.ToString("0.000", CultureInfo.InvariantCulture)} 0.000</{kv.Key}>");
                }
                sb.AppendLine("  </modifier>");
            }
            sb.AppendLine("</timecycle_modifier_data>");
            File.WriteAllText(path, sb.ToString());
        }

        private static string Esc(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        public IEnumerable<string> AllVariableNames()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Regions)
                foreach (var k in r.Values.Keys)
                    if (seen.Add(k)) yield return k;
        }

        public void ClearModifiers()
        {
            Modifiers.Clear();
            SelectedModifier = -1;
        }

        public readonly List<Region> Regions = new List<Region>();
        public int SelectedRegion;
        public string LoadedPath { get; private set; }
        public string LoadedName { get; private set; }
        public bool HasData => Regions.Count > 0;

        public Region Current =>
            (Regions.Count > 0) ? Regions[Math.Clamp(SelectedRegion, 0, Regions.Count - 1)] : null;

        public string Name => LoadedName;

        public void CopyCycleFrom(TimecycleData other)
        {
            if (other == null) return;
            Regions.Clear();
            Regions.AddRange(other.Regions);
            SelectedRegion = 0;
            LoadedName = other.LoadedName;
            LoadedPath = other.LoadedPath;
        }

        public bool LoadTimecycleXml(string path, out string error)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                return LoadTimecycleDoc(doc, path, System.IO.Path.GetFileNameWithoutExtension(path), out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool LoadTimecycleXmlText(string xml, string name, out string error)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return LoadTimecycleDoc(doc, null, name, out error);
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private bool LoadTimecycleDoc(XmlDocument doc, string path, string name, out string error)
        {
            error = null;
            try
            {
                var regions = new List<Region>();

                var cycles = doc.GetElementsByTagName("cycle");
                if (cycles.Count > 0)
                {
                    foreach (XmlNode cyc in cycles)
                    {
                        foreach (XmlNode rn in cyc.ChildNodes)
                        {
                            if (rn.NodeType != XmlNodeType.Element) continue;
                            if (!string.Equals(rn.Name, "region", StringComparison.OrdinalIgnoreCase)) continue;
                            regions.Add(ParseRegion(rn, RegionName(rn, cyc)));
                        }
                    }
                }

                if (regions.Count == 0)
                {
                    var rnodes = doc.GetElementsByTagName("region");
                    if (rnodes.Count > 0)
                    {
                        foreach (XmlNode rn in rnodes) regions.Add(ParseRegion(rn, RegionName(rn, null)));
                    }
                    else if (doc.DocumentElement != null)
                    {
                        var r = ParseRegion(doc.DocumentElement, "GLOBAL");
                        if (r.Values.Count > 0) regions.Add(r);
                    }
                }

                if (regions.Count == 0)
                {
                    error = "No timecycle variables found (expected <cycle>/<region> with value arrays).";
                    return false;
                }

                Regions.Clear();
                foreach (var reg in regions)
                    reg.Original = reg.Values.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone(), StringComparer.OrdinalIgnoreCase);
                Regions.AddRange(regions);
                SelectedRegion = 0;
                LoadedPath = path;
                LoadedName = name;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Unload()
        {
            Regions.Clear();
            LoadedPath = null;
            LoadedName = null;
        }

        private static string RegionName(XmlNode regionNode, XmlNode cycleNode)
        {
            var n = regionNode.Attributes?["name"]?.Value;
            if (!string.IsNullOrEmpty(n)) return n;
            var c = cycleNode?.Attributes?["name"]?.Value;
            return string.IsNullOrEmpty(c) ? "GLOBAL" : c;
        }

        private static Region ParseRegion(XmlNode node, string name)
        {
            var r = new Region { Name = name };
            foreach (XmlNode v in node.ChildNodes)
            {
                if (v.NodeType != XmlNodeType.Element) continue;
                var text = v.InnerText?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var parts = text.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var vals = new float[parts.Length];
                bool ok = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i])) { ok = false; break; }
                }
                if (ok && vals.Length > 0) r.Values[v.Name] = vals;
            }
            return r;
        }

        public int KeyframeCount
        {
            get
            {
                int n = 0;
                var r = Current;
                if (r != null) foreach (var v in r.Values.Values) if (v.Length > n) n = v.Length;
                return n;
            }
        }

        public bool ScheduleMatchesData => KeyframeCount == 0 || KeyframeCount == Samples.Count;

        public struct PostFxState
        {
            public float FilmicA, FilmicB, FilmicC, FilmicD, FilmicE, FilmicF, FilmicW;
            public float Exposure;
            public float ExposureTweakStops, ExposureMinStops, ExposureMaxStops;
            public Vector4 ColorCorrectHighLum;
            public Vector4 ColorShiftLowLum;
            public float Desaturate;
            public bool Valid;
        }

        public PostFxState EvaluatePostFx(float hour)
        {
            SetTime(hour);
            var st = new PostFxState
            {
                FilmicA = 0.22f, FilmicB = 0.30f, FilmicC = 0.10f, FilmicD = 0.20f,
                FilmicE = 0.01f, FilmicF = 0.30f, FilmicW = 4.0f,
                Exposure = 1.0f,
                ExposureTweakStops = 0.0f, ExposureMinStops = -3.5f, ExposureMaxStops = 3.0f,
                ColorCorrectHighLum = new Vector4(0.451f, 0.478f, 0.443f, 0.0f),
                ColorShiftLowLum = new Vector4(0.0f, 0.0f, 0.0f, 0.0f),
                Desaturate = 0.70f,
            };

            var r = Current;
            if (r == null) return st;

            st.FilmicA = V(r, "postfx_tonemap_filmic_a", st.FilmicA);
            st.FilmicB = V(r, "postfx_tonemap_filmic_b", st.FilmicB);
            st.FilmicC = V(r, "postfx_tonemap_filmic_c", st.FilmicC);
            st.FilmicD = V(r, "postfx_tonemap_filmic_d", st.FilmicD);
            st.FilmicE = V(r, "postfx_tonemap_filmic_e", st.FilmicE);
            st.FilmicF = V(r, "postfx_tonemap_filmic_f", st.FilmicF);
            st.FilmicW = V(r, "postfx_tonemap_filmic_w", st.FilmicW);

            st.ExposureTweakStops = V(r, "postfx_exposure", 0.0f);
            st.Exposure = (float)Math.Pow(2.0, st.ExposureTweakStops);
            st.ExposureMinStops = V(r, "postfx_exposure_min", -3.5f);
            st.ExposureMaxStops = V(r, "postfx_exposure_max", 3.0f);

            st.ColorCorrectHighLum = new Vector4(
                V(r, "postfx_correct_col_r", 0.451f),
                V(r, "postfx_correct_col_g", 0.478f),
                V(r, "postfx_correct_col_b", 0.443f),
                V(r, "postfx_correct_cutoff", 0.0f));
            st.ColorShiftLowLum = new Vector4(
                V(r, "postfx_shift_col_r", 0.0f),
                V(r, "postfx_shift_col_g", 0.0f),
                V(r, "postfx_shift_col_b", 0.0f),
                V(r, "postfx_shift_cutoff", 0.0f));
            st.Desaturate = V(r, "postfx_desaturation", 0.70f);
            st.Valid = r.Has("postfx_desaturation") || r.Has("postfx_tonemap_filmic_a");
            return st;
        }

        public struct GlobalLightState
        {
            public Vector3 LightDir; public float LightHdr;
            public Vector4 LightDirColour;
            public Vector4 LightDirAmbColour;
            public Vector4 NaturalAmbUp, NaturalAmbDown;
            public Vector4 ArtificialAmbUp, ArtificialAmbDown;
            public Vector4 ArtificialIntUp, ArtificialIntDown;
        }

        public void GetLightDirection(float hour, out Vector3 sunDir, out Vector3 moonDir)
        {
            float sunroll = SunRoll * (float)Math.PI / 180.0f;
            float moonroll = MoonRoll * (float)Math.PI / 180.0f;
            float dayval = (0.5f + (hour - 6.0f) / 14.0f);
            float nightval = (((hour > 12.0f) ? (hour - 7.0f) : (hour + 17.0f)) / 9.0f);
            float daycyc = (float)Math.PI * dayval;
            float nightcyc = (float)Math.PI * nightval;
            var sdir = new Vector3((float)Math.Sin(daycyc), -(float)Math.Cos(daycyc), 0.0f);
            var mdir = new Vector3(-(float)Math.Sin(nightcyc), 0.0f, -(float)Math.Cos(nightcyc));
            var saxis = Quaternion.RotationYawPitchRoll(0.0f, sunroll, 0.0f);
            var maxis = Quaternion.RotationYawPitchRoll(0.0f, -moonroll, 0.0f);
            sunDir = Vector3.Normalize(Rotate(sdir, saxis));
            moonDir = Vector3.Normalize(Rotate(mdir, maxis));
        }

        private static Vector3 Rotate(Vector3 v, Quaternion q)
        {
            Vector3.Transform(ref v, ref q, out Vector3 r);
            return r;
        }

        private float V(Region r, string name, float fallback = 0.0f)
        {
            float baseVal = r.Get(name, CurrentSampleIndex, CurrentSampleBlend, fallback);
            var mod = CurrentModifier;
            if (mod == null || ModifierStrength <= 0.0001f) return baseVal;
            if (!mod.Values.TryGetValue(name, out var mv)) return baseVal;
            return baseVal + (mv - baseVal) * Math.Clamp(ModifierStrength, 0.0f, 1.0f);
        }

        private Vector4 RGBA(Region r, string prefix, string intensity, float defIntensity = 1.0f)
        {
            return new Vector4(
                V(r, prefix + "_r"),
                V(r, prefix + "_g"),
                V(r, prefix + "_b"),
                intensity == null ? defIntensity : V(r, intensity, defIntensity));
        }

        public GlobalLightState Evaluate(float hour, bool hdr)
        {
            SetTime(hour);
            var st = new GlobalLightState { LightHdr = 1.0f };

            GetLightDirection(CurrentHour, out var sunDir, out var moonDir);
            var dir = ((CurrentHour < 5.0f) || (CurrentHour > 21.0f)) ? moonDir : sunDir;
            if (dir.Z < 0) dir.Z = 0;
            if (!(dir.LengthSquared() > 1e-8f)) dir = Vector3.UnitZ;
            st.LightDir = Vector3.Normalize(dir);

            var r = Current;
            if (r == null) return st;

            var dirCol = RGBA(r, "light_dir_col", "light_dir_mult");
            var dirAmb = RGBA(r, "light_directional_amb_col", "light_directional_amb_intensity");
            float lamult = V(r, "light_directional_amb_intensity_mult", 1.0f);
            var natUp = RGBA(r, "light_natural_amb_up_col", "light_natural_amb_up_intensity");
            var natDn = RGBA(r, "light_natural_amb_down_col", "light_natural_amb_down_intensity");
            float natUpMult = V(r, "light_natural_amb_up_intensity_mult", 1.0f);
            var artUp = RGBA(r, "light_artificial_ext_up_col", "light_artificial_ext_up_intensity");
            var artDn = RGBA(r, "light_artificial_ext_down_col", "light_artificial_ext_down_intensity");

            float minmult = hdr ? 0.0f : 0.5f;
            dirCol = Scale(dirCol, Math.Max(dirCol.W, minmult));
            dirAmb = Scale(dirAmb, dirAmb.W * lamult);
            natUp = Scale(natUp, natUp.W * natUpMult);
            natDn = Scale(natDn, natDn.W);
            artUp = Scale(artUp, artUp.W);
            artDn = Scale(artDn, artDn.W);

            if (!hdr)
            {
                dirCol = Min(dirCol, 1.0f);
                dirAmb = Min(dirAmb, 0.5f);
                natUp = Min(natUp, 0.5f); natDn = Min(natDn, 0.5f);
                artUp = Min(artUp, 0.5f); artDn = Min(artDn, 0.5f);
            }
            else
            {
                st.LightHdr = Math.Max(V(r, "sky_hdr", 1.0f), 1.0f);
            }

            if (SuppressDirectionalIndoors && CurrentModifier != null && ModifierStrength > 0.0001f)
            {
                float keep = 1.0f - Math.Clamp(ModifierStrength, 0.0f, 1.0f);
                dirCol = Scale(dirCol, keep);
            }
            st.LightDirColour = dirCol;
            st.LightDirAmbColour = dirAmb;
            st.NaturalAmbUp = natUp; st.NaturalAmbDown = natDn;
            st.ArtificialAmbUp = artUp; st.ArtificialAmbDown = artDn;
            var aiUp = RGBA(r, "light_artificial_int_up_col", "light_artificial_int_up_intensity");
            var aiDn = RGBA(r, "light_artificial_int_down_col", "light_artificial_int_down_intensity");
            aiUp = Scale(aiUp, aiUp.W); aiDn = Scale(aiDn, aiDn.W);
            if (!hdr) { aiUp = Min(aiUp, 0.5f); aiDn = Min(aiDn, 0.5f); }
            st.ArtificialIntUp = new Vector4(aiUp.X, aiUp.Y, aiUp.Z, Math.Max(V(r, "natural_ambient_multiplier", 1.0f), 0.0f));
            st.ArtificialIntDown = new Vector4(aiDn.X, aiDn.Y, aiDn.Z, Math.Max(V(r, "artificial_int_ambient_multiplier", 0.0f), 0.0f));
            return st;
        }

        public struct SkyState
        {
            public Vector3 AzimuthEast, AzimuthWest, AzimuthTransition;
            public Vector3 Zenith, ZenithTransition;
            public float AzimuthTransitionPos, ZenithTransitionPos, ZenithBlendStart;
            public float ZenithTransitionEastBlend, ZenithTransitionWestBlend;

            public Vector3 SunColour, SunDiscColour, SunMie;
            public float SunDiscSize, SunHdr, SunInfluenceRadius, SunScatterIntensity;

            public Vector3 MoonColour;
            public float MoonDiscSize, MoonIntensity, MoonInfluenceRadius, MoonScatterIntensity;

            public Vector3 CloudBaseColour, CloudMidColour, CloudShadowColour;
            public float CloudBaseStrength, CloudDensityMult, CloudDensityBias;
            public float CloudFadeOut, CloudShadowStrength;

            public Vector3 FogColour;
            public float FogDensity, FogStart;
            public float HdrIntensity, StarfieldIntensity;
            public float AmbientDownWrap;

            public Vector3 FogNearCol, FogSunCol, FogFarCol, FogMoonCol, FogHazeCol;
            public float FogHeightFalloff, FogBaseHeight, FogAlpha, FogHorizonTintScale, FogHdr;
            public float FogHazeDensity, FogHazeAlpha, FogHazeHdr, FogHazeStart;
            public float FogSunPower, FogMoonPower, FarClip;
        }

        public bool CodeWalkerSkyMapping = true;

        public float Peek(string name, float fallback = 0.0f) => Current == null ? fallback : V(Current, name, fallback);
        public bool Has(string name) => Current != null && Current.Has(name);

        private Vector3 RGB(Region r, string prefix, float mult = 1.0f)
        {
            return new Vector3(V(r, prefix + "_r"), V(r, prefix + "_g"), V(r, prefix + "_b")) * mult;
        }

        public SkyState EvaluateSky(float hour)
        {
            SetTime(hour);
            var s = new SkyState
            {
                AzimuthTransitionPos = 0.5f,
                ZenithTransitionPos = 0.4f,
                SunDiscSize = 0.02f,
                SunHdr = 1.0f,
                MoonDiscSize = 0.03f,
                MoonIntensity = 1.0f,
                CloudDensityMult = 1.0f,
                HdrIntensity = 1.0f,
                AmbientDownWrap = 1.0f,
            };
            var r = Current;
            if (r == null) return s;

            if (CodeWalkerSkyMapping)
            {
                s.AzimuthEast = RGB(r, "sky_azimuth_east_col");
                s.AzimuthWest = RGB(r, "sky_azimuth_west_col");
                s.AzimuthTransition = RGB(r, "sky_azimuth_transition_col");
                s.Zenith = RGB(r, "sky_zenith_col");
                s.ZenithTransition = RGB(r, "sky_zenith_transition_col");
            }
            else
            {
                s.AzimuthEast = RGB(r, "sky_azimuth_east_col", V(r, "sky_azimuth_east_col_inten", 1.0f));
                s.AzimuthWest = RGB(r, "sky_azimuth_west_col", V(r, "sky_azimuth_west_col_inten", 1.0f));
                s.AzimuthTransition = RGB(r, "sky_azimuth_transition_col", V(r, "sky_azimuth_transition_col_inten", 1.0f));
                s.Zenith = RGB(r, "sky_zenith_col", V(r, "sky_zenith_col_inten", 1.0f));
                s.ZenithTransition = RGB(r, "sky_zenith_transition_col", V(r, "sky_zenith_transition_col_inten", 1.0f));
            }

            s.AzimuthTransitionPos = V(r, "sky_azimuth_transition_position", 0.5f);
            s.ZenithTransitionPos = V(r, "sky_zenith_transition_position", 0.4f);
            s.ZenithBlendStart = V(r, "sky_zenith_blend_start", 0.0f);
            s.ZenithTransitionEastBlend = V(r, "sky_zenith_transition_east_blend", 0.0f);
            s.ZenithTransitionWestBlend = V(r, "sky_zenith_transition_west_blend", 0.0f);
            if (CodeWalkerSkyMapping)
            {
                float ztp = s.ZenithTransitionPos, zbs = s.ZenithBlendStart;
                s.ZenithTransitionPos = zbs;
                s.ZenithBlendStart = 1.0f - ztp;
            }

            s.SunColour = RGB(r, "sky_sun_col");
            s.SunDiscColour = RGB(r, "sky_sun_disc_col");
            s.SunDiscSize = V(r, "sky_sun_disc_size", 0.02f);
            s.SunHdr = V(r, "sky_sun_hdr", 1.0f);
            s.SunMie = new Vector3(V(r, "sky_sun_miephase", 0.7f),
                                   V(r, "sky_sun_miescatter", 0.05f),
                                   V(r, "sky_sun_mie_intensity_mult", 1.0f));
            s.SunInfluenceRadius = V(r, "sky_sun_influence_radius", 0.1f);
            s.SunScatterIntensity = V(r, "sky_sun_scatter_inten", 1.0f);

            s.MoonColour = RGB(r, "sky_moon_col");
            s.MoonDiscSize = V(r, "sky_moon_disc_size", 0.03f);
            s.MoonIntensity = V(r, "sky_moon_iten", 1.0f);
            s.MoonInfluenceRadius = V(r, "sky_moon_influence_radius", 0.1f);
            s.MoonScatterIntensity = V(r, "sky_moon_scatter_inten", 1.0f);

            s.CloudBaseColour = RGB(r, "cloud_base_minus_mid_col");
            s.CloudMidColour = RGB(r, "cloud_mid_col");
            s.CloudShadowColour = RGB(r, "cloud_shadow_minus_base_col");
            s.CloudBaseStrength = V(r, "cloud_base_strength", 1.0f);
            s.CloudDensityMult = V(r, "cloud_density_mult", 1.0f);
            s.CloudDensityBias = V(r, "cloud_density_bias", 0.0f);
            s.CloudFadeOut = V(r, "cloud_fadeout", 0.0f);
            s.CloudShadowStrength = V(r, "cloud_shadow_strength", 1.0f);
            if (s.CloudMidColour.LengthSquared() < 0.0004f) s.CloudMidColour = new Vector3(0.55f, 0.57f, 0.62f);
            if (s.CloudBaseColour.LengthSquared() < 0.0004f) s.CloudBaseColour = new Vector3(0.30f, 0.32f, 0.36f);
            if (s.CloudShadowColour.LengthSquared() < 0.0004f) s.CloudShadowColour = new Vector3(0.16f, 0.17f, 0.20f);

            s.FogStart = V(r, "fog_start", 73.0f);
            s.FogDensity = V(r, "fog_density", 1.0f) / 10000.0f;
            s.FogHeightFalloff = V(r, "fog_falloff", 0.5f) / 1000.0f;
            s.FogBaseHeight = V(r, "fog_base_height", 0.0f);
            s.FogAlpha = V(r, "fog_alpha", 1.0f);
            s.FogHorizonTintScale = V(r, "fog_horizon_tint_scale", 4.0f);
            s.FogHdr = V(r, "fog_hdr", 1.0f);
            s.FogNearCol = RGB(r, "fog_near_col");
            s.FogSunCol = RGB(r, "fog_col");
            s.FogFarCol = RGB(r, "fog_east_col");
            s.FogMoonCol = RGB(r, "fog_moon_col");
            s.FogHazeCol = RGB(r, "fog_haze_col");
            s.FogHazeDensity = V(r, "fog_haze_density", 0.5f) / 10000.0f;
            s.FogHazeAlpha = V(r, "fog_haze_alpha", 1.0f);
            s.FogHazeHdr = V(r, "fog_haze_hdr", 1.0f);
            s.FogHazeStart = V(r, "fog_haze_start", 0.0f);
            s.FogSunPower = V(r, "fog_sun_lighting_calc_pow", 8.0f);
            s.FogMoonPower = V(r, "fog_moon_lighting_calc_pow", 18.0f);
            s.FarClip = Math.Max(V(r, "far_clip", 1500.0f), s.FogStart + 1.0f);
            s.FogColour = s.FogFarCol;
            if (s.FogColour.LengthSquared() < 0.0004f) s.FogColour = RGB(r, "sky_azimuth_transition_col");
            s.HdrIntensity = Math.Max(V(r, "sky_hdr", 1.0f), 0.05f);
            s.StarfieldIntensity = V(r, "sky_stars_iten", 0.0f) * 5.0f;
            s.AmbientDownWrap = V(r, "light_amb_down_wrap", 1.0f);
            return s;
        }

        private static Vector4 Scale(Vector4 v, float s) => new Vector4(v.X * s, v.Y * s, v.Z * s, v.W);
        private static Vector4 Min(Vector4 v, float m) =>
            new Vector4(Math.Min(v.X, m), Math.Min(v.Y, m), Math.Min(v.Z, m), v.W);
    }
}

