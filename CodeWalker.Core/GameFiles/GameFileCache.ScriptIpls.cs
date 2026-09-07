using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// One ymap the "Script IPLs (FiveM)" rule brought back into the active map, and why.
    /// </summary>
    public class ScriptIplYmap
    {
        /// <summary>The ymap's short name (shr_int).</summary>
        public string Name;
        public uint Hash;
        /// <summary>The archive copy that stands in for it (the newest overlay by DLC order).</summary>
        public RpfFileEntry Entry;
        /// <summary>Its node in the game's main cache (gta5_cache_y.dat) - the streamer's box.</summary>
        public MapDataStoreNode Node;
        /// <summary>"places v_carshowroom at x,y,z" or "LOD parent of shr_int".</summary>
        public string Reason;
        /// <summary>The interiors it places (archetype name and position), from its cache node.</summary>
        public List<string> Interiors = new List<string>();
        /// <summary>Whether it is in YmapDict / YmapHierarchyDict right now.</summary>
        public bool Applied;
        public override string ToString() => $"{Name} ({Reason})";
    }

    /// <summary>
    /// The "Script IPLs (FiveM)" rescue.
    ///
    /// The MP DLCs' map change sets INVALIDATE whole base rpfs and ENABLE their own copies: mpheist
    /// drops platform:/levels/gta5/_citye/downtown_01/downtown_01_metadata.rpf (225 files) and enables
    /// dlcMPHeist:/.../downtown_01_metadata.rpf (161 files). Most of what leaves comes back under a
    /// DLC prefix (dt1_02 -> hei_dt1_02, dt1_19_interior_v_policehub_milo_ -> hei_dt1_19_interior_0_
    /// heist_police_dlc_milo_), but a few dozen ymaps of each such rpf simply vanish from the active
    /// set: the SP mission-state variants (carshowroom_broken, dt1_05_hc_*, fib_heist_*) - which is
    /// right, they are what a story mission swaps in - and a handful of INTERIORS that no DLC ever
    /// re-supplied: shr_int / shr_int_lod (Premium Deluxe Motorsport, the v_carshowroom showroom),
    /// FIBlobby, finbank, rc12b_hospitalinterior... In the game these are script-requested IPLs, and
    /// on FiveM every server does RequestIpl("shr_int") to get its car dealership back; a FiveM
    /// mapper expects to see them, and CodeWalker's stock logic (InitActiveMapRpfFiles) leaves the
    /// PDM site as a bare concrete slab.
    ///
    /// The rule is data-driven, no name list: a ymap comes back when (1) the game's MAIN cache
    /// (gta5_cache_y.dat, the MapDataStore + InteriorProxies modules) knows it, (2) no active rpf
    /// carries it but an archive does, (3) its cache node PLACES AN INTERIOR (a CInteriorProxy - the
    /// SP mission variants have none, so they stay out without a list), and (4) NO active ymap places
    /// an interior within 3 m of it (a DLC rebuilt the police hub under a new name at the same pivot,
    /// so the old one stays out; nothing stands where the showroom was, so it comes in). Its LOD
    /// parents (shr_int_lod) come along so the parent link works. Done here, at the GameFileCache
    /// level - YmapDict + YmapHierarchyDict - so the world streamer picks them up like any ymap.
    /// </summary>
    public partial class GameFileCache
    {
        /// <summary>The "Script IPLs (FiveM)" option: apply the rescue at init (the host sets it before Init).</summary>
        public bool EnableScriptIpls { get; set; } = true;
        /// <summary>The ymaps the rule found (applied or not), for the option's tooltip and the diagnostics.</summary>
        public List<ScriptIplYmap> ScriptIpls { get; private set; } = new List<ScriptIplYmap>();
        /// <summary>The rule's full report: every dropped interior ymap, rescued or refused and why.</summary>
        public string ScriptIplReport { get; private set; } = "";
        /// <summary>How many main-cache ymaps left the active set without an interior (the mission variants left out).</summary>
        public int ScriptIplsDroppedWithoutInterior { get; private set; }
        /// <summary>Whether the rescued ymaps are in the active dictionaries right now.</summary>
        public bool ScriptIplsApplied { get; private set; }

        /// <summary>Two interiors within this many metres share a slot: the dropped one stays out.</summary>
        public const float ScriptIplSlotRadius = 3.0f;

        /// <summary>
        /// The post-pass after InitMapDicts / InitMapCaches: find the dropped interior ymaps and, if
        /// the option is on, put them into the active map. Every decision goes into ScriptIplReport.
        /// </summary>
        private void InitScriptIpls()
        {
            ScriptIpls = new List<ScriptIplYmap>();
            ScriptIplsApplied = false;
            ScriptIplsDroppedWithoutInterior = 0;
            var sb = new StringBuilder();
            try
            {
                FindScriptIpls(sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("SCRIPTIPL failed: " + ex);
            }
            ScriptIplReport = sb.ToString().TrimEnd();
            if (EnableScriptIpls) ApplyScriptIpls(true);
        }

        private void FindScriptIpls(StringBuilder sb)
        {
            if (AllCacheFiles == null || YmapDict == null || AllYmapsDict == null || YmapHierarchyDict == null) return;
            //the MAIN cache: the one from update.rpf (or common.rpf without DLC) - the DLC caches only
            //know their own DLC's ymaps, and a DLC ymap missing from the active set is a different
            //story (a DLC after the selected one, or a pack CodeWalker leaves out on purpose)
            CacheDatFile main = null;
            foreach (var c in AllCacheFiles)
            {
                var p = c?.FileEntry?.Path ?? "";
                if (p.EndsWith("gta5_cache_y.dat", StringComparison.OrdinalIgnoreCase)) { main = c; break; }
            }
            if (main?.AllMapNodes == null)
            {
                sb.AppendLine("SCRIPTIPL no main cache (gta5_cache_y.dat) loaded - nothing to rescue");
                return;
            }

            //1. every interior the ACTIVE map places, from every loaded cache file: proxy -> its
            //parent ymap must be in YmapDict. (The DLC caches carry the DLC ymaps' proxies - the
            //heist police hub, the DLC-rebuilt apartments - which is exactly what has to block.)
            var activeInteriors = new List<(Vector3 pos, uint arch, uint ymap)>();
            foreach (var c in AllCacheFiles)
            {
                if (c?.AllCInteriorProxies == null) continue;
                foreach (var prx in c.AllCInteriorProxies)
                    if (YmapDict.ContainsKey(prx.Parent)) activeInteriors.Add((prx.Position, prx.Name, prx.Parent));
            }
            //2. plus the active ymaps NO cache knows (a DLC without a cache loader, or its cache not
            //loaded) that are named like an interior placement: read them once, they are few, and
            //an interior standing in one of them must block too
            var cachedNames = new HashSet<uint>();
            foreach (var c in AllCacheFiles)
                if (c?.AllMapNodes != null)
                    foreach (var n in c.AllMapNodes) cachedNames.Add(n.Name);
            int uncachedRead = 0;
            foreach (var kv in YmapDict)
            {
                if (cachedNames.Contains(kv.Key)) continue;
                var nm = kv.Value?.NameLower ?? "";
                if (nm.IndexOf("milo", StringComparison.Ordinal) < 0 && nm.IndexOf("interior", StringComparison.Ordinal) < 0 && nm.IndexOf("int_", StringComparison.Ordinal) < 0) continue;
                YmapFile ym = null;
                try { ym = RpfMan.GetFile<YmapFile>(kv.Value); } catch { }
                uncachedRead++;
                if (ym?.CMloInstanceDefs == null) continue;
                foreach (var md in ym.CMloInstanceDefs)
                    activeInteriors.Add((md.CEntityDef.position, md.CEntityDef.archetypeName, kv.Key));
            }

            //3. the candidates: main-cache nodes no active rpf carries but an archive does
            var candidates = new List<(MapDataStoreNode node, RpfFileEntry entry)>();
            var candidateHashes = new HashSet<uint>();
            foreach (var node in main.AllMapNodes)
            {
                if (node == null || YmapDict.ContainsKey(node.Name)) continue;
                if (!AllYmapsDict.TryGetValue(node.Name, out var entry) || entry == null) continue;   //not in any archive: nothing to rescue
                if (node.InteriorProxies == null || node.InteriorProxies.Length == 0)
                {
                    ScriptIplsDroppedWithoutInterior++;   //carshowroom_broken, dt1_05_hc_*: not interiors, stay out
                    continue;
                }
                candidates.Add((node, entry));
                candidateHashes.Add(node.Name);
            }
            candidates.Sort((a, b) => string.CompareOrdinal(a.entry.NameLower, b.entry.NameLower));

            //4. the newest archive copy of each candidate (and of its LOD parents), by DLC order:
            //AllYmapsDict keeps whichever rpf came last in the scan, which is alphabetical
            //(patchday2ng after patchday27ng), not the game's order
            var parentHashes = new HashSet<uint>();
            foreach (var c in candidates)
                for (var p = main.MapNodeDict != null && main.MapNodeDict.TryGetValue(c.node.ParentName, out var pn) ? pn : null; p != null; p = main.MapNodeDict.TryGetValue(p.ParentName, out var pp) ? pp : null)
                {
                    if (!parentHashes.Add(p.Name)) break;
                }
            var wanted = new HashSet<uint>(candidateHashes);
            wanted.UnionWith(parentHashes);
            var bestEntry = FindNewestYmapEntries(wanted);

            //5. decide
            var rescued = new List<ScriptIplYmap>();
            var rescuedInteriors = new List<(Vector3 pos, string name)>();
            foreach (var (node, _) in candidates)
            {
                if (!bestEntry.TryGetValue(node.Name, out var entry) || entry == null) continue;
                string name = entry.GetShortNameLower();
                var ipl = new ScriptIplYmap { Name = name, Hash = node.Name, Entry = entry, Node = node };
                string blocked = null;
                foreach (var prx in node.InteriorProxies)
                {
                    string archName = JenkIndex.TryGetString(prx.Name) ?? prx.Name.ToString();
                    ipl.Interiors.Add($"{archName} @ {prx.Position.X:0.0},{prx.Position.Y:0.0},{prx.Position.Z:0.0}");
                    //(4) an active interior within the slot radius: this one is replaced, stays out
                    foreach (var ai in activeInteriors)
                    {
                        if ((ai.pos - prx.Position).Length() > ScriptIplSlotRadius) continue;
                        string aiArch = JenkIndex.TryGetString(ai.arch) ?? ai.arch.ToString();
                        string aiYmap = JenkIndex.TryGetString(ai.ymap) ?? ai.ymap.ToString();
                        blocked = $"{aiArch} of {aiYmap} stands {(ai.pos - prx.Position).Length():0.0} m from its {archName}";
                        break;
                    }
                    if (blocked != null) break;
                    //two dropped ymaps at one pivot (two SP states of one interior): the first by name stands
                    foreach (var ri in rescuedInteriors)
                    {
                        if ((ri.pos - prx.Position).Length() > ScriptIplSlotRadius) continue;
                        blocked = $"rescued {ri.name} already stands at the same pivot";
                        break;
                    }
                    if (blocked != null) break;
                }
                if (blocked != null)
                {
                    sb.AppendLine($"SCRIPTIPL refused {name}: {blocked} [{string.Join("; ", ipl.Interiors)}] {entry.Path}");
                    continue;
                }
                //(3) confirmed on the file itself: the cache said interior, the ymap must agree
                YmapFile ym = null;
                try { ym = RpfMan.GetFile<YmapFile>(entry); } catch { }
                if (ym == null || ym.CMloInstanceDefs == null || ym.CMloInstanceDefs.Length == 0)
                {
                    sb.AppendLine($"SCRIPTIPL refused {name}: the cache lists an interior but the ymap has no CMloInstanceDefs ({(ym == null ? "unreadable" : "0")}) {entry.Path}");
                    continue;
                }
                ipl.Reason = "places " + string.Join(", ", ipl.Interiors);
                rescued.Add(ipl);
                foreach (var prx in node.InteriorProxies) rescuedInteriors.Add((prx.Position, name));
                sb.AppendLine($"SCRIPTIPL rescued {name}: {ipl.Reason}; scripted={ym.IsScripted} entities={ym.AllEntities?.Length ?? 0} parent={(JenkIndex.TryGetString(node.ParentName) ?? node.ParentName.ToString())} {entry.Path}");
            }
            //6. their LOD parents (shr_int_lod): whatever of the parent chain is dropped too
            var have = new HashSet<uint>(rescued.Select(r => r.Hash));
            foreach (var r in rescued.ToList())
            {
                var pn = r.Node;
                for (int guard = 0; guard < 8; guard++)
                {
                    if (pn == null || pn.ParentName == 0 || main.MapNodeDict == null || !main.MapNodeDict.TryGetValue(pn.ParentName, out var parent)) break;
                    pn = parent;
                    if (YmapDict.ContainsKey(pn.Name) || have.Contains(pn.Name)) continue;   //active already, or rescued
                    if (!bestEntry.TryGetValue(pn.Name, out var pe) || pe == null)
                    {
                        sb.AppendLine($"SCRIPTIPL parent {(JenkIndex.TryGetString(pn.Name) ?? pn.Name.ToString())} of {r.Name} is in no archive");
                        continue;
                    }
                    var pipl = new ScriptIplYmap { Name = pe.GetShortNameLower(), Hash = pn.Name, Entry = pe, Node = pn, Reason = "LOD parent of " + r.Name };
                    rescued.Add(pipl);
                    have.Add(pn.Name);
                    sb.AppendLine($"SCRIPTIPL rescued {pipl.Name}: {pipl.Reason} {pe.Path}");
                }
            }
            ScriptIpls = rescued;
            sb.AppendLine($"SCRIPTIPL summary: {candidates.Count} dropped interior ymaps in the main cache, {rescued.Count} rescued (LOD parents included), " +
                          $"{ScriptIplsDroppedWithoutInterior} dropped without an interior left out; {activeInteriors.Count} active interiors checked ({uncachedRead} uncached ymaps read)");
        }

        /// <summary>
        /// For each wanted ymap hash, the archive copy the game would stream: the highest DLC order
        /// (patchday27ng over patchday2ng over patchday1ng over x64i.rpf) among the packs up to the
        /// selected one; a mods copy over the original when mods are on.
        /// </summary>
        private Dictionary<uint, RpfFileEntry> FindNewestYmapEntries(HashSet<uint> wanted)
        {
            var best = new Dictionary<uint, RpfFileEntry>();
            var bestRank = new Dictionary<uint, int>();
            //DLC name -> setup order index (dlclist order); patchday27ng is a real overlay in the game
            //even though InitActiveMapRpfFiles skips it
            var dlcRank = new Dictionary<string, int>();
            int rank = 0, selectedRank = int.MaxValue;
            foreach (var su in DlcSetupFiles)
            {
                if (su?.DlcFile == null) continue;
                string dn = GetDlcNameFromPath(su.DlcFile.Path);
                if (!dlcRank.ContainsKey(dn)) dlcRank[dn] = ++rank;
                if (dn == SelectedDlc) selectedRank = rank;
            }
            foreach (var rpf in AllRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var e in rpf.AllEntries)
                {
                    if (!(e is RpfFileEntry fe) || !e.NameLower.EndsWith(".ymap") || !wanted.Contains(fe.ShortNameHash)) continue;
                    string path = fe.Path.Replace('\\', '/').ToLowerInvariant();
                    int r = 0;
                    int di = path.IndexOf("dlcpacks/", StringComparison.Ordinal);
                    int pi = path.IndexOf("dlc_patch/", StringComparison.Ordinal);
                    string dn = null;
                    if (di >= 0) { var rest = path.Substring(di + 9); dn = rest.Substring(0, Math.Max(0, rest.IndexOf('/'))); }
                    else if (pi >= 0) { var rest = path.Substring(pi + 10); dn = rest.Substring(0, Math.Max(0, rest.IndexOf('/'))); }
                    if (dn != null)
                    {
                        if (!dlcRank.TryGetValue(dn, out r)) continue;          //a pack not in dlclist: the game never mounts it
                        if (r > selectedRank) continue;                            //after the selected DLC
                    }
                    if (!bestRank.TryGetValue(fe.ShortNameHash, out var had) || r >= had)
                    {
                        best[fe.ShortNameHash] = fe;
                        bestRank[fe.ShortNameHash] = r;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Put the rescued ymaps into (enable) or take them out of (disable) YmapDict and
        /// YmapHierarchyDict. The host rebuilds its streamer's map afterwards; the option toggles
        /// without a restart.
        /// </summary>
        public void ApplyScriptIpls(bool enable)
        {
            if (ScriptIpls == null || YmapDict == null || YmapHierarchyDict == null) return;
            //under the request lock: GetYmap reads YmapDict (GetYmapEntry) on the streamer's loader
            //thread inside the same lock, and a Dictionary must not be read while it is written
            lock (requestSyncRoot)
            {
                foreach (var ipl in ScriptIpls)
                {
                    if (ipl?.Entry == null || ipl.Node == null) continue;
                    if (enable)
                    {
                        //never over an active ymap of the same name (a re-init could have made one active)
                        if (YmapDict.TryGetValue(ipl.Hash, out var have) && have != null && !ReferenceEquals(have, ipl.Entry)) { ipl.Applied = false; continue; }
                        YmapDict[ipl.Hash] = ipl.Entry;
                        YmapHierarchyDict[ipl.Hash] = ipl.Node;
                        ipl.Applied = true;
                    }
                    else
                    {
                        if (YmapDict.TryGetValue(ipl.Hash, out var have) && ReferenceEquals(have, ipl.Entry)) YmapDict.Remove(ipl.Hash);
                        if (YmapHierarchyDict.TryGetValue(ipl.Hash, out var hn) && ReferenceEquals(hn, ipl.Node)) YmapHierarchyDict.Remove(ipl.Hash);
                        ipl.Applied = false;
                    }
                }
            }
            ScriptIplsApplied = enable;
        }

        /// <summary>Is this ymap hash one the rescue put into the active map (diagnostic).</summary>
        public bool IsScriptIpl(uint ymapHash)
        {
            var l = ScriptIpls;
            if (l == null) return false;
            foreach (var i in l) if (i != null && i.Hash == ymapHash && i.Applied) return true;
            return false;
        }
    }
}
