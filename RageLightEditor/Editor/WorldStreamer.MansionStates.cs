using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        private static readonly Regex StateFamily = new Regex(@"^(?<stem>.+?)_(?<kind>mansion|props)_(?<state>original|shared|private|generic)(?<lod>_lod|_slod\d*|_lodlights|_distantlights)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SiteCompanion = new Regex(@"(_construction|_constr|_original|_orig)(_lod|_slod\d*|_lodlights|_distantlights)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int StateHiddenYmaps => stateHiddenYmaps.Count;
        public int StateSites { get; private set; }
        public bool IsStateHiddenYmap(uint ymapHash) => stateHiddenYmaps.Contains(ymapHash);
        private readonly HashSet<uint> stateHiddenYmaps = new HashSet<uint>();
        private readonly HashSet<YmapFile> stateWinners = new HashSet<YmapFile>();
        private readonly Dictionary<string, List<KeyValuePair<uint, YmapFile>>> stateSites = new Dictionary<string, List<KeyValuePair<uint, YmapFile>>>();
        private readonly List<uint> stateRemove = new List<uint>();
        public string StateReport { get; private set; } = "";
        private readonly System.Text.StringBuilder stateSb = new System.Text.StringBuilder();

        private static readonly bool statesDisabledByEnv = Environment.GetEnvironmentVariable("RLE_NOSTATES") == "1";

        private void HideScriptStates()
        {
            stateHiddenYmaps.Clear();
            stateWinners.Clear();
            stateSites.Clear();
            stateRemove.Clear();
            stateSb.Clear();
            StateSites = 0;
            if (statesDisabledByEnv) { StateReport = ""; return; }
            foreach (var kv in lodCandidates)
            {
                var y = kv.Value;
                if (y == null || !y.IsScripted) continue;
                var proxyStem = NameStem(y);
                if (proxyStem.IndexOf("impexpproxy", StringComparison.OrdinalIgnoreCase) < 0) continue;
                stateHiddenYmaps.Add(kv.Key);
                stateRemove.Add(kv.Key);
                stateSb.Append("STATES cutscene proxy: ").Append(proxyStem).Append(" OFF (a script-only stand-in the game shows during cutscenes, never in the streamed world)").AppendLine();
            }
            foreach (var kv in lodCandidates)
            {
                var y = kv.Value;
                if (y == null || !y.IsScripted || stateHiddenYmaps.Contains(kv.Key)) continue;
                var m = StateFamily.Match(NameStem(y));
                if (!m.Success) continue;
                string stem = m.Groups["stem"].Value.ToLowerInvariant();
                if (!stateSites.TryGetValue(stem, out var l)) stateSites[stem] = l = new List<KeyValuePair<uint, YmapFile>>(8);
                l.Add(kv);
            }
            var siteExtents = new Dictionary<string, (Vector3 mn, Vector3 mx)>();
            foreach (var site in stateSites)
            {
                bool hasShared = false, hasPrivate = false;
                foreach (var kv in site.Value)
                {
                    var m = StateFamily.Match(NameStem(kv.Value));
                    string state = m.Groups["state"].Value.ToLowerInvariant();
                    if (m.Groups["kind"].Value.Equals("mansion", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state == "shared") { hasShared = true; GrowSiteExtents(siteExtents, site.Key, kv.Value); }
                        else if (state == "private") hasPrivate = true;
                    }
                }
                if (!hasShared) continue;
                StateSites++;
                stateSb.Append("STATES ").Append(site.Key).Append(": ");
                foreach (var kv in site.Value)
                {
                    var m = StateFamily.Match(NameStem(kv.Value));
                    string state = m.Groups["state"].Value.ToLowerInvariant();
                    bool hide = state == "original" || (state == "generic" && hasPrivate);
                    if (hide) { stateHiddenYmaps.Add(kv.Key); stateRemove.Add(kv.Key); }
                    else stateWinners.Add(kv.Value);
                    stateSb.Append(NameStem(kv.Value)).Append(hide ? " OFF (" : " on (").Append(state == "original" ? "the site's undeveloped state" : state == "generic" && hasPrivate ? "private stands for the house" : "shared/" + state).Append(") ");
                }
                stateSb.AppendLine();
            }
            if (siteExtents.Count > 0)
                foreach (var kv in lodCandidates)
                {
                    var y = kv.Value;
                    if (y == null || !y.IsScripted || stateHiddenYmaps.Contains(kv.Key) || stateWinners.Contains(y)) continue;
                    var stem = NameStem(y);
                    if (StateFamily.IsMatch(stem) || !SiteCompanion.IsMatch(stem)) continue;
                    var emn = y._CMapData.entitiesExtentsMin; var emx = y._CMapData.entitiesExtentsMax;
                    if (!(emx.X > emn.X) || !(emx.Y > emn.Y)) continue;
                    foreach (var site in siteExtents)
                    {
                        const float slack = 30.0f;
                        var (smn, smx) = site.Value;
                        var c = (emn + emx) * 0.5f;
                        if (c.X < smn.X - slack || c.X > smx.X + slack || c.Y < smn.Y - slack || c.Y > smx.Y + slack) continue;
                        stateHiddenYmaps.Add(kv.Key); stateRemove.Add(kv.Key);
                        stateSb.Append("STATES ").Append(site.Key).Append(" companion: ").Append(stem).Append(" OFF (the site's construction / original dressing, standing on ").Append(site.Key).Append("'s developed ground)").AppendLine();
                        break;
                    }
                }
            foreach (var h in stateRemove) lodCandidates.Remove(h);
            StateReport = stateSb.ToString().TrimEnd();
        }

        private static void GrowSiteExtents(Dictionary<string, (Vector3 mn, Vector3 mx)> ext, string site, YmapFile y)
        {
            var mn = y._CMapData.entitiesExtentsMin; var mx = y._CMapData.entitiesExtentsMax;
            if (!(mx.X > mn.X) || !(mx.Y > mn.Y)) return;
            if (ext.TryGetValue(site, out var cur)) { mn = Vector3.Min(mn, cur.mn); mx = Vector3.Max(mx, cur.mx); }
            ext[site] = (mn, mx);
        }

        private static string NameStem(YmapFile y)
        {
            var n = y?.Name ?? "";
            int i = n.LastIndexOf('.');
            return i > 0 ? n.Substring(0, i) : n;
        }

        private static bool IsOriginalStateName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("_original", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("_orig_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.EndsWith("_orig", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("_orig.ymap", StringComparison.OrdinalIgnoreCase);
        }
    }
}

