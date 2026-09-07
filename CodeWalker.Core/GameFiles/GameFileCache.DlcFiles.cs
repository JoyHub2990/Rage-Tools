using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// One FILE (not an rpf) a DLC's content change sets took out of the active map, and why.
    /// </summary>
    public class DlcInvalidatedFile
    {
        /// <summary>The path as the content.xml wrote it (platform:/levels/gta5/.../ch1_06e_lod.ymap).</summary>
        public string Path;
        /// <summary>Lower-case file name with extension (ch1_06e_lod.ymap).</summary>
        public string FileName;
        /// <summary>JenkHash of the name without extension - the key of YmapDict / YmapHierarchyDict.</summary>
        public uint Hash;
        /// <summary>The DLC whose change set names it (mp2025_01) and the change set (mp2025_01_map).</summary>
        public string Dlc, ChangeSet;
        /// <summary>"invalidate" (filesToInvalidate) or "disable" (filesToDisable).</summary>
        public string How;
        /// <summary>The DLC's setup order, so a later DLC's re-enable wins over an earlier invalidation.</summary>
        public int Order;
        public override string ToString() => $"{FileName} ({How} by {Dlc}/{ChangeSet})";
    }

    /// <summary>
    /// The per-FILE half of the DLC content change sets.
    ///
    /// A DLC's content.xml can invalidate or disable WHOLE RPFS - CodeWalker honours that in
    /// InitActiveMapRpfFiles (mpheist drops downtown_01_metadata.rpf and enables its own copy) -
    /// and it can name INDIVIDUAL FILES: "how to deal with individual files?" is the comment left
    /// where those are ignored. They are not rare and they are not harmless to ignore. The Hills
    /// mansion DLC (mp2025_02, m25_2_ch1_06e_*) reshapes the hillside at -1704,479: its change set
    /// invalidates the base map's ch1_06e terrain ymaps by NAME (the hill, its _lod, the props that
    /// stood there) and enables its own copies with the new ground; with the invalidation ignored,
    /// the base hill and the DLC's hill were both in the active map and both drew - the doubled
    /// slope in the user's screenshot, and the base props (a fence, a mast) standing through the
    /// new terrace. The same mechanism is what most MP DLCs use for their per-ymap replacements
    /// (mpexecutive's dt1_02 office variants, mpsecurity's ss1 changes...), and it is what the
    /// "DLC replaces base buildings" variant heuristic in the world streamer was papering over.
    ///
    /// The rule follows the game: walking the DLCs in setup order up to the selected one, every
    /// filesToInvalidate / filesToDisable entry that is a FILE goes on the invalidated list; a later
    /// change set's filesToEnable of a file of the same name takes it off again (a DLC that
    /// re-enables its own copy). Only ymaps matter for the map dictionaries here (a .ytyp / .meta
    /// entry is recorded for the report but changes nothing), and the invalidated ymaps are dropped
    /// from YmapDict / AllYmapsDict lookups and get no cache node in YmapHierarchyDict - so the
    /// world streamer never opens them, exactly as if their rpf had been invalidated whole.
    ///
    /// A DLC's own copy of a file it also invalidates is not touched: the invalidation names the
    /// BASE map's path (platform:/levels/gta5/...), and what stays active is whichever rpf carries
    /// that short name after the DLC's rpfs are mounted - the DLC copy in dlcpacks/. So the ymap
    /// leaves the active set only if no ACTIVE DLC rpf (mounted after the invalidating change set)
    /// carries a file of that name; the shape of the data is that DLCs replace whole rpfs (a new
    /// path) rather than shadow individual names, so the practical rule is: the invalidated name is
    /// gone unless it is present in an rpf the SAME OR A LATER change set enabled.
    /// </summary>
    public partial class GameFileCache
    {
        /// <summary>The "DLC file invalidations" option: honour the per-file entries (default on).
        /// Off gives CodeWalker's behaviour (only whole rpfs are invalidated).</summary>
        public bool EnableDlcFileInvalidation { get; set; } = true;
        /// <summary>Every per-file invalidate/disable entry the active DLCs' change sets named, in DLC order (diagnostic and the report).</summary>
        public List<DlcInvalidatedFile> DlcInvalidatedFiles { get; private set; } = new List<DlcInvalidatedFile>();
        /// <summary>The ymap hashes the rule keeps OUT of the active map (invalidated and never re-enabled by a later DLC).</summary>
        public HashSet<uint> DlcInvalidatedYmaps { get; private set; } = new HashSet<uint>();
        /// <summary>The rule's report: every entry, and whether it took a ymap out of the active set.</summary>
        public string DlcFileReport { get; private set; } = "";
        /// <summary>How many ymaps the rule took out of YmapDict.</summary>
        public int DlcInvalidatedYmapsApplied { get; private set; }

        /// <summary>
        /// Walk the active DLCs' content change sets for per-FILE invalidations. Runs right after
        /// InitMapDicts (needs DlcSetupFiles, the active rpf set and YmapDict to take the names out of).
        /// </summary>
        private void InitDlcFileInvalidations()
        {
            DlcInvalidatedFiles = new List<DlcInvalidatedFile>();
            DlcInvalidatedYmaps = new HashSet<uint>();
            DlcInvalidatedYmapsApplied = 0;
            var sb = new StringBuilder();
            try
            {
                if (EnableDlc) CollectDlcFileInvalidations(sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("DLCFILES failed: " + ex);
            }
            DlcFileReport = sb.ToString().TrimEnd();
        }

        private void CollectDlcFileInvalidations(StringBuilder sb)
        {
            var ctx = new DlcFileWalk();
            var outNow = ctx.OutNow;
            foreach (var setupfile in DlcSetupFiles)
            {
                if (setupfile?.DlcFile == null || setupfile.ContentFile == null) continue;
                string dlcname = GetDlcNameFromPath(setupfile.DlcFile.Path);
                if ((dlcname == "patchday27ng") && (SelectedDlc != dlcname)) continue;   //InitActiveMapRpfFiles skips it too
                foreach (var changeset in setupfile.ContentFile.contentChangeSets)
                {
                    if (changeset == null) continue;
                    WalkChangeSet(changeset, changeset.changeSetName, dlcname, setupfile.order, ctx, sb);
                    if (changeset.mapChangeSetData != null)
                        foreach (var mapcs in changeset.mapChangeSetData)
                            WalkChangeSet(mapcs, changeset.changeSetName + "/" + (mapcs?.associatedMap ?? "map"), dlcname, setupfile.order, ctx, sb);
                }
                if (dlcname == SelectedDlc) break;   //everything's loaded up to the selected DLC
            }
            foreach (var kv in outNow)
            {
                var f = kv.Value;
                DlcInvalidatedFiles.Add(f);
                if (f.FileName.EndsWith(".ymap", StringComparison.Ordinal))
                {
                    DlcInvalidatedYmaps.Add(f.Hash);
                    string held = "-";
                    if (YmapDict != null && YmapDict.TryGetValue(f.Hash, out var ye) && ye != null) held = ye.Path;
                    string all = "-";
                    if (AllYmapsDict != null && AllYmapsDict.TryGetValue(f.Hash, out var ae) && ae != null) all = ae.Path;
                    //out of the ACTIVE map: GetYmap(hash) no longer finds it and the world streamer
                    //gives it no node (WorldStreamer.DlcFiles.cs). AllYmapsDict keeps it - the
                    //explorer and "any copy" lookups still see the file, as they see disabled rpfs.
                    if (EnableDlcFileInvalidation && held != "-" && YmapDict.Remove(f.Hash)) DlcInvalidatedYmapsApplied++;
                    sb.AppendLine($"DLCFILES out {f.FileName} ({f.How} by {f.Dlc}/{f.ChangeSet}) {f.Path} | active: {held} | any: {all}");
                }
                else sb.AppendLine($"DLCFILES noted {f.FileName} ({f.How} by {f.Dlc}/{f.ChangeSet}; not a ymap, nothing to do here) {f.Path}");
            }
            sb.AppendLine($"DLCFILES summary: {ctx.FileEntries} per-file entries in the active change sets ({ctx.RpfEntries} whole-rpf entries left to InitActiveMapRpfFiles), {ctx.Reenabled} re-enabled by a later change set, {DlcInvalidatedYmaps.Count} ymaps out of the active map");
        }

        /// <summary>The state of one walk over the change sets (see CollectDlcFileInvalidations).</summary>
        private sealed class DlcFileWalk
        {
            /// <summary>name -> the entry that has it out right now (a later enable of the same name clears it).</summary>
            public readonly Dictionary<string, DlcInvalidatedFile> OutNow = new Dictionary<string, DlcInvalidatedFile>();
            public int Reenabled, RpfEntries, FileEntries;
        }

        private void WalkChangeSet(DlcContentChangeSet cs, string csname, string dlcname, int order, DlcFileWalk ctx, StringBuilder sb)
        {
            var outNow = ctx.OutNow;
            //ENABLES first? No - the game applies a change set's invalidations, then its enables:
            //a set that invalidates platform:/x.ymap and enables dlc:/x.ymap ends with x active.
            void take(List<string> list, string how)
            {
                if (list == null) return;
                foreach (var file in list)
                {
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    string fpath = GetDlcPlatformPath(GetDlcMountedPath(file));
                    if (fpath.EndsWith(".rpf", StringComparison.Ordinal)) { ctx.RpfEntries++; continue; }
                    ctx.FileEntries++;
                    string fname = FileNameOf(fpath);
                    if (fname.Length == 0) continue;
                    var entry = new DlcInvalidatedFile
                    {
                        Path = file, FileName = fname, Hash = JenkHash.GenHash(NameWithoutExt(fname)),
                        Dlc = dlcname, ChangeSet = csname, How = how, Order = order,
                    };
                    outNow[fname] = entry;
                }
            }
            take(cs.filesToInvalidate, "invalidate");
            take(cs.filesToDisable, "disable");
            if (cs.filesToEnable != null)
            {
                foreach (var file in cs.filesToEnable)
                {
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    string fpath = GetDlcPlatformPath(GetDlcMountedPath(file));
                    string fname = FileNameOf(fpath);
                    if (fname.EndsWith(".rpf", StringComparison.Ordinal))
                    {
                        //an rpf enabled by this or a later change set: whatever ymaps it carries are
                        //active by name from here on - the invalidated base copy of such a name is
                        //replaced, not gone (the DLC copy is what YmapDict ends up holding, since
                        //InitMapDicts visits the DLC rpfs after the base ones)
                        var rpf = FindEnabledRpf(fpath, file);
                        if (rpf?.AllEntries != null)
                            foreach (var e in rpf.AllEntries)
                                if (e is RpfFileEntry && outNow.TryGetValue(e.NameLower, out var was))
                                {
                                    outNow.Remove(e.NameLower);
                                    ctx.Reenabled++;
                                    sb.AppendLine($"DLCFILES back {e.NameLower}: {was.How}d by {was.Dlc}/{was.ChangeSet}, carried by {rpf.Path} enabled by {dlcname}/{csname}");
                                }
                        continue;
                    }
                    if (outNow.TryGetValue(fname, out var had))
                    {
                        outNow.Remove(fname);
                        ctx.Reenabled++;
                        sb.AppendLine($"DLCFILES back {fname}: {had.How}d by {had.Dlc}/{had.ChangeSet}, enabled by {dlcname}/{csname}");
                    }
                }
            }
        }

        /// <summary>The loaded rpf a change set's filesToEnable path names, by its physical path (any DLC's mount).</summary>
        private RpfFile FindEnabledRpf(string fpath, string rawpath)
        {
            //ActiveMapRpfFiles is keyed by the virtual (platform) path when the rpf is active
            if (ActiveMapRpfFiles.TryGetValue(fpath.ToLowerInvariant(), out var active) && active != null) return active;
            //otherwise: the device-prefixed path (dlcMP2025_02:/x64/levels/...) resolved against every DLC
            string lower = rawpath.ToLowerInvariant().Replace('\\', '/');
            int colon = lower.IndexOf(':');
            if (colon <= 0) return null;
            string dev = lower.Substring(0, colon);
            string rest = lower.Substring(colon + 1).TrimStart('/').Replace('/', '\\');
            foreach (var su in DlcSetupFiles)
            {
                if (su?.DlcFile == null || !string.Equals(su.deviceName, dev, StringComparison.OrdinalIgnoreCase)) continue;
                var p = su.DlcFile.Path + "\\" + rest;
                var r = RpfMan.FindRpfFile(p);
                if (r == null && su.DlcSubpacks != null)
                    foreach (var sp in su.DlcSubpacks) { r = RpfMan.FindRpfFile(sp.Path + "\\" + rest); if (r != null) break; }
                if (r != null) return r;
            }
            return null;
        }

        private static string FileNameOf(string path)
        {
            int i = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return (i >= 0 ? path.Substring(i + 1) : path).ToLowerInvariant();
        }
        private static string NameWithoutExt(string fname)
        {
            int i = fname.LastIndexOf('.');
            return i > 0 ? fname.Substring(0, i) : fname;
        }

        /// <summary>
        /// Every change set of the DLCs whose name contains <paramref name="dlcFilter"/>, with its
        /// group (setup2.xml: GROUP_STARTUP / GROUP_MAP / GROUP_MAP_SP ...), enables, invalidates and
        /// map change sets - the raw content.xml semantics, for reading a pack's intent (diagnostic).
        /// </summary>
        public string DescribeDlcChangeSets(string dlcFilter)
        {
            var sb = new StringBuilder();
            foreach (var su in DlcSetupFiles)
            {
                if (su?.DlcFile == null || su.ContentFile == null) continue;
                string dn = GetDlcNameFromPath(su.DlcFile.Path);
                if (!string.IsNullOrEmpty(dlcFilter) && dn.IndexOf(dlcFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.AppendLine($"DLCCS {dn} order {su.order} minor {su.minorOrder} type {su.type} subpacks {su.subPackCount}");
                if (su.contentChangeSetGroups != null)
                    foreach (var g in su.contentChangeSetGroups)
                        sb.AppendLine($"DLCCS   group {g.NameHash}: {string.Join(", ", g.ContentChangeSets ?? new List<string>())}");
                foreach (var df in su.ContentFile.dataFiles)
                    sb.AppendLine($"DLCCS   datafile {df.filename} type {df.fileType} overlay {df.overlay} disabled {df.disabled} persistent {df.persistent}");
                foreach (var cs in su.ContentFile.contentChangeSets)
                {
                    if (cs == null) continue;
                    DescribeChangeSet(cs, "  ", sb);
                }
            }
            return sb.ToString().TrimEnd();
        }
        private static void DescribeChangeSet(DlcContentChangeSet cs, string indent, StringBuilder sb)
        {
            sb.AppendLine($"DLCCS {indent}changeset {cs.changeSetName ?? cs.associatedMap ?? "?"} useCacheLoader {cs.useCacheLoader} conditions [{cs.executionConditions}]");
            void list(string what, List<string> l) { if (l != null && l.Count > 0) foreach (var f in l) sb.AppendLine($"DLCCS {indent}  {what} {f}"); }
            list("enable", cs.filesToEnable);
            list("invalidate", cs.filesToInvalidate);
            list("disable", cs.filesToDisable);
            list("txdload", cs.txdToLoad);
            list("resident", cs.residentResources);
            list("unregister", cs.unregisterResources);
            if (cs.mapChangeSetData != null)
                foreach (var m in cs.mapChangeSetData) { sb.AppendLine($"DLCCS {indent}  map {m.associatedMap}"); DescribeChangeSet(m, indent + "    ", sb); }
        }

        /// <summary>Is this ymap one the per-file DLC rule keeps out of the active map (diagnostic).</summary>
        public bool IsDlcInvalidatedYmap(uint ymapHash) => EnableDlcFileInvalidation && DlcInvalidatedYmaps.Contains(ymapHash);
    }
}
