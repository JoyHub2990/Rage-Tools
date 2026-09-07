using System;
using System.Collections.Generic;
using System.Xml;
using CodeWalker.World;
using SharpDX;

namespace RageLightEditor.Editor
{
    public struct CloudKeyframeState
    {
        public bool Valid;
        public string SettingsName;
        public Vector3 CloudColor, LightColor, AmbientColor, SkyColor, BounceColor, EastColor, WestColor;
        public Vector4 ScaleFillColors;
        public Vector4 DensityShift_Scale_ScatteringConst_Scale;
        public Vector4 PiercingLightPower_Strength_NormalStrength_Thickness;
        public Vector4 ScaleDiffuseFillAmbient_WrapAmount;
    }

    public class CloudHats
    {
        public CloudHatManager HatManager;
        public CloudSettingsMap SettingsMap;
        public string Error = "";
        public bool Loaded => HatManager?.CloudHatFrags != null && HatManager.CloudHatFrags.Length > 0;
        public readonly Dictionary<string, string> CycleToSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> WeatherToSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool Load(GameFileManager gameFiles)
        {
            try
            {
                var rpfman = gameFiles?.Cache?.RpfMan;
                if (rpfman == null) { Error = "archives not open"; return false; }
                var cloudsxml = rpfman.GetFileXml("common.rpf\\data\\clouds.xml");
                if (cloudsxml?.DocumentElement == null) { Error = "clouds.xml not found"; return false; }
                HatManager = new CloudHatManager();
                HatManager.Init(cloudsxml.DocumentElement);

                XmlDocument kf = null;
                try { kf = rpfman.GetFileXml("update\\update.rpf\\common\\data\\cloudkeyframes.xml"); } catch { }
                if (kf?.DocumentElement == null)
                {
                    try { kf = rpfman.GetFileXml("common.rpf\\data\\cloudkeyframes.xml"); } catch { }
                }
                if (kf?.DocumentElement != null)
                {
                    SettingsMap = new CloudSettingsMap();
                    SettingsMap.Init(kf.DocumentElement);
                }

                XmlDocument wx = null;
                try { wx = rpfman.GetFileXml("update\\update.rpf\\common\\data\\levels\\gta5\\weather.xml"); } catch { }
                if (wx?.DocumentElement == null)
                {
                    try { wx = rpfman.GetFileXml("common.rpf\\data\\levels\\gta5\\weather.xml"); } catch { }
                }
                if (wx?.DocumentElement != null)
                {
                    foreach (XmlNode node in wx.DocumentElement.SelectNodes("WeatherTypes/Item"))
                    {
                        string name = node.SelectSingleNode("Name")?.InnerText?.Trim();
                        string cs = node.SelectSingleNode("CloudSettingsName")?.InnerText?.Trim();
                        string tc = node.SelectSingleNode("TimeCycleFilename")?.InnerText?.Trim();
                        if (string.IsNullOrEmpty(cs)) continue;
                        if (!string.IsNullOrEmpty(name)) WeatherToSettings[name] = cs;
                        if (!string.IsNullOrEmpty(tc))
                        {
                            string stem = tc.Replace('\\', '/');
                            int slash = stem.LastIndexOf('/');
                            if (slash >= 0) stem = stem.Substring(slash + 1);
                            int dot = stem.LastIndexOf('.');
                            if (dot >= 0) stem = stem.Substring(0, dot);
                            CycleToSettings[stem.ToLowerInvariant()] = cs;
                        }
                    }
                }
                return Loaded;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return false;
            }
        }

        public string[] FragNames()
        {
            if (!Loaded) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var f in HatManager.CloudHatFrags) if (!string.IsNullOrEmpty(f.Name)) names.Add(f.Name);
            return names.ToArray();
        }

        public string SettingsNameFor(string cycleOrWeather)
        {
            if (string.IsNullOrEmpty(cycleOrWeather)) return null;
            if (CycleToSettings.TryGetValue(cycleOrWeather, out var s)) return s;
            if (WeatherToSettings.TryGetValue(cycleOrWeather, out s)) return s;
            string bare = cycleOrWeather.StartsWith("w_", StringComparison.OrdinalIgnoreCase) ? cycleOrWeather.Substring(2) : cycleOrWeather;
            if (WeatherToSettings.TryGetValue(bare, out s)) return s;
            if (CycleToSettings.TryGetValue("w_" + bare, out s)) return s;
            return null;
        }

        public CloudSettingsMapItem Settings(string settingsName)
        {
            if (SettingsMap?.SettingsMap == null || string.IsNullOrEmpty(settingsName)) return null;
            SettingsMap.SettingsMap.TryGetValue(settingsName, out var item);
            return item;
        }

        public CloudKeyframeState Evaluate(string settingsName, float hour)
        {
            var st = new CloudKeyframeState();
            var item = Settings(settingsName) ?? Settings("default");
            if (item == null) return st;
            hour = ((hour % 24.0f) + 24.0f) % 24.0f;
            st.Valid = true;
            st.SettingsName = item.Name;
            st.CloudColor = Key(item.CloudColor, hour).V3();
            st.LightColor = Key(item.CloudLightColor, hour).V3();
            st.AmbientColor = Key(item.CloudAmbientColor, hour).V3();
            st.SkyColor = Key(item.CloudSkyColor, hour).V3();
            st.BounceColor = Key(item.CloudBounceColor, hour).V3();
            st.EastColor = Key(item.CloudEastColor, hour).V3();
            st.WestColor = Key(item.CloudWestColor, hour).V3();
            st.ScaleFillColors = Key(item.CloudScaleFillColors, hour);
            st.DensityShift_Scale_ScatteringConst_Scale = Key(item.CloudDensityShift_Scale_ScatteringConst_Scale, hour);
            st.PiercingLightPower_Strength_NormalStrength_Thickness = Key(item.CloudPiercingLightPower_Strength_NormalStrength_Thickness, hour);
            st.ScaleDiffuseFillAmbient_WrapAmount = Key(item.CloudScaleDiffuseFillAmbient_WrapAmount, hour);
            return st;
        }

        private static Vector4 Key(CloudSettingsMapKeyData kd, float hour)
        {
            var d = kd?.keyEntryData;
            if (d == null || d.Count == 0) return Vector4.Zero;
            float lo = float.MinValue, hi = float.MaxValue;
            Vector4 vlo = Vector4.Zero, vhi = Vector4.Zero;
            bool haveLo = false, haveHi = false;
            foreach (var kv in d)
            {
                if (kv.Key <= hour && kv.Key >= lo) { lo = kv.Key; vlo = kv.Value; haveLo = true; }
                if (kv.Key >= hour && kv.Key <= hi) { hi = kv.Key; vhi = kv.Value; haveHi = true; }
            }
            if (!haveLo) return vhi;
            if (!haveHi) return vlo;
            if (hi - lo < 1e-4f) return vlo;
            float t = (hour - lo) / (hi - lo);
            return Vector4.Lerp(vlo, vhi, t);
        }

        public string PickFrag(string settingsName)
        {
            if (!Loaded || SettingsMap?.SettingsMap == null || string.IsNullOrEmpty(settingsName)) return null;
            if (!SettingsMap.SettingsMap.TryGetValue(settingsName, out var item) || item?.CloudList == null) return null;
            var cl = item.CloudList;
            int best = -1, bestP = -1;
            int n = Math.Min(cl.Probability?.Length ?? 0, cl.Bits?.Length ?? 0);
            for (int i = 0; i < n; i++)
            {
                if (cl.Probability[i] <= bestP) continue;
                int bits = cl.Bits[i];
                for (int b = 0; b < HatManager.CloudHatFrags.Length && b < 32; b++)
                {
                    if ((bits & (1 << b)) == 0) continue;
                    if (!HatManager.CloudHatFrags[b].Enabled) continue;
                    best = b; bestP = cl.Probability[i];
                    break;
                }
            }
            return best >= 0 ? HatManager.CloudHatFrags[best].Name : null;
        }
    }

    internal static class CloudVectorExt
    {
        public static Vector3 V3(this Vector4 v) => new Vector3(v.X, v.Y, v.Z);
    }
}

