using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private float worldHitchMaxMs;
        private int worldHitchFrame, worldFlyFrames;
        private double worldFlyMsSum;

        private string WorldStageLine()
        {
            var L = worldRender.Lights;
            return $"WORLDSTAGE walks {World.Walks} skipped {World.WalksSkipped} syncs {World.Syncs} " +
                   $"lastWalk {World.LastWalkMs:0.00} ms lastSync {World.LastSyncMs:0.00} ms lastPrepare {World.LastPrepareMs:0.00} ms lastVariant {World.LastVariantMs:0.00} ms walkTotal {World.WalkMsTotal:0} ms syncTotal {World.SyncMsTotal:0} ms prepareTotal {World.PrepareMsTotal:0} ms variantTotal {World.VariantMsTotal:0} ms variantSetup {World.VariantSetupMs:0} ms variantKept {World.VariantKeptMs:0} ms ymapEvictTotal {World.EvictMsTotal:0} ms sweepTotal {worldRender.SweepMsTotal:0} ms modelEvictTotal {worldRender.EvictMsTotal:0} ms " +
                   $"variantEvals {World.VariantEvaluations}+{World.VariantIncrementals} variantHiddenEnts {World.VariantHiddenEntities} interiorShellsHidden {World.InteriorShellsHidden} interiorGroups {World.InteriorVariantGroups} interiorsEmitted {World.InteriorsEmitted.Count} interiorsDuplicated {World.InteriorsDuplicated} hourFiltered {World.HourFilteredYmaps} adoptedParents {World.AdoptedParentNodes} " +
                   $"built {worldRender.BuiltThisFrame} loadsPending {worldRender.LoadsPending} texPrefetched {worldRender.TexturesPrefetched} " +
                   $"lodTable {L.LodTableSize} rebuilds {L.LodTableRebuilds} coronasCulled {L.CoronasCulled} lightsMs {L.LastBuildMs:0.00} " +
                   $"resident {World.YmapsResident} tree {World.LodTreeYmaps} cascades {sunCascadeRebuilds} rebuilds last {perfCascadeMs:0.0} ms worst {perfCascadeMaxMs:0.0} ms total {perfCascadeMsTotal:0} ms";
        }

        private uint WorldWeatherHash()
        {
            var name = weather?.CurrentPreset?.Name ?? "";
            string game;
            switch (name)
            {
                case "Clear": game = "clear"; break;
                case "Extra sunny": game = "extrasunny"; break;
                case "Cloudy": game = "clouds"; break;
                case "Overcast": game = "overcast"; break;
                case "Rain": game = "rain"; break;
                case "Storm": game = "thunder"; break;
                case "Fog": game = "foggy"; break;
                case "Snow": game = "snow"; break;
                default: return 0;
            }
            return JenkHash.GenHash(game);
        }

        partial void OnWorldTick_World()
        {
            if (!worldBuilt || !panel.WorldMode) return;
            World.YmapHourFilter = panel.WorldYmapHourFilter;
            World.YmapWeatherFilter = panel.WorldYmapWeatherFilter;
            World.VariantsIncludeDlc = panel.WorldVariantsIncludeDlc;
            panel.WorldHourFiltered = World.HourFilteredYmaps;
            panel.WorldTimedYmaps = World.TimedYmapCount + World.WeatherYmapCount;
            if (++worldMloDumpTick == 430 && Environment.GetEnvironmentVariable("RLE_DUMPMLO") != null)
                Console.WriteLine(WorldInteriorReport(camera.Position, 400.0f));
            if (worldMloDumpTick == 430 && Environment.GetEnvironmentVariable("RLE_FINDENT") is string fe && fe.Length > 0)
                Console.WriteLine(WorldEntityReport(fe, camera.Position));
            ServiceNearEnts_V10();
            TickProjectProbe();
            if (!worldLightEnvRead)
            {
                worldLightEnvRead = true;
                if (float.TryParse(Environment.GetEnvironmentVariable("RLE_LIGHTRANGE"), out float lr) && lr > 0) panel.WorldLightsRange = lr;
                if (int.TryParse(Environment.GetEnvironmentVariable("RLE_MAXLIGHTS"), out int ml) && ml > 0) worldRender.Lights.MaxLights = ml;
            }
            if (worldFlyMetres != 0.0f && screenshotPath != null && worldArriveHoldSecs > 0 && worldWarmup >= 458)
            {
                if (worldArriveAt == 0) worldArriveAt = clock.Elapsed.TotalSeconds;
                double held = clock.Elapsed.TotalSeconds - worldArriveAt;
                if (held < worldArriveHoldSecs)
                {
                    worldWarmup = 458;
                    if (held - worldArriveLastPrint >= 2.0)
                    {
                        worldArriveLastPrint = held;
                        Console.WriteLine($"WORLDARRIVE +{held:0}s " + WorldLoadersLine());
                    }
                }
            }
        }
        private int worldMloDumpTick;
        private bool worldLightEnvRead;
        private float perfCascadeMs, perfCascadeMaxMs; private double perfCascadeMsTotal;
        private double worldArriveAt, worldArriveLastPrint;
        private readonly float worldArriveHoldSecs = float.TryParse(Environment.GetEnvironmentVariable("RLE_ARRIVEHOLD"), out float s) ? s : 0;

        private string WorldLoadersLine()
        {
            return $"WORLDLOADERS {World.LoaderStatus} | drawables {worldRender.LoaderStatus} | " +
                   $"visible {World.Visible.Count} meshes {worldRender.MeshesDrawn} models {worldRender.ArchetypesLoaded} failed {worldRender.ArchetypesFailed} released {worldRender.ModelsReleased} " +
                   $"ymaps open {World.YmapsOpen}/{World.YmapsWanted} resident {World.YmapsResident} tree {World.LodTreeYmaps} ymapsFailed {World.YmapsFailed} cache {gameFiles?.CacheStatus}";
        }

        private string WorldInteriorReport(Vector3 at, float radius)
        {
            var sb = new System.Text.StringBuilder();
            var emitted = new HashSet<YmapEntityDef>(World.InteriorsEmitted);
            sb.AppendLine($"MLOREPORT interiors emitted {World.InteriorsEmitted.Count} duplicated(same origin) {World.InteriorsDuplicated} shellsHiddenByRule {World.InteriorShellsHidden}");
            var rows = new List<(float d, string line)>();
            foreach (var y in World.ResidentYmaps)
            {
                var ents = y.AllEntities;
                if (ents == null) continue;
                foreach (var e in ents)
                {
                    if (e?.MloInstance == null) continue;
                    float d = (e.Position - at).Length();
                    if (d > radius) continue;
                    var a = e.Archetype;
                    string sets = "";
                    var iss = e.MloInstance.EntitySets;
                    if (iss != null)
                        foreach (var s in iss)
                            if (s != null) sets += $" {s.EntitySet?.Name ?? "?"}({(s.Entities?.Count ?? 0)}){(s.Visible ? ":ON" : "")}";
                    rows.Add((d, $"  MLO {a?.Name} ymap={y.Name} scripted={y.IsScripted} scriptIpl={gameFiles?.Cache?.IsScriptIpl(y.RpfFileEntry?.ShortNameHash ?? 0) ?? false} pos={e.Position.X:0.0},{e.Position.Y:0.0},{e.Position.Z:0.0} d={d:0} bsR={e.BSRadius:0} lodDist={e.LodDist:0} " +
                                 $"bb={(e.BBMax - e.BBMin).X:0}x{(e.BBMax - e.BBMin).Y:0}x{(e.BBMax - e.BBMin).Z:0} inTree={World.IsInLodTree(y.RpfFileEntry?.ShortNameHash ?? 0)} emitted={emitted.Contains(e)} variantHidden={World.IsVariantHiddenEnt(e)} shellHidden={World.IsInteriorShellHidden(e)} " +
                                 $"ents={(e.MloInstance.Entities?.Length ?? 0)} flags={e._CEntityDef.flags} parent={(e.Parent?.Archetype?.Name ?? "-")} path={y.RpfFileEntry?.Path} sets[{iss?.Length ?? 0}]:{sets}"));
                }
            }
            foreach (var r in rows.OrderBy(r => r.d)) sb.AppendLine(r.line);
            try
            {
                var c = gameFiles?.Cache;
                if (c?.AllYmapsDict != null && radius <= 400)
                {
                    var resident = new HashSet<uint>(World.ResidentYmaps.Select(y => y.RpfFileEntry?.ShortNameHash ?? 0));
                    int scanned = 0;
                    bool sweepAll = Environment.GetEnvironmentVariable("RLE_DUMPMLO") == "all";
                    foreach (var kv in c.AllYmapsDict)
                    {
                        var fe = kv.Value;
                        var nm = fe?.NameLower ?? "";
                        if (!sweepAll && nm.IndexOf("milo", StringComparison.Ordinal) < 0 && nm.IndexOf("interior", StringComparison.Ordinal) < 0 && nm.IndexOf("int_", StringComparison.Ordinal) < 0) continue;
                        if (resident.Contains(fe.ShortNameHash)) continue;
                        scanned++;
                        YmapFile ym = null;
                        try { ym = c.RpfMan.GetFile<YmapFile>(fe); } catch { }
                        if (ym?.CMloInstanceDefs == null) continue;
                        foreach (var md in ym.CMloInstanceDefs)
                        {
                            float d = (md.CEntityDef.position - at).Length();
                            if (d > radius) continue;
                            bool active = c.YmapDict.TryGetValue(fe.ShortNameHash, out var ae) && ReferenceEquals(ae, fe);
                            sb.AppendLine($"  MLO(not resident) {md.CEntityDef.archetypeName} ymap={fe.Name} active={active} pos={md.CEntityDef.position.X:0.0},{md.CEntityDef.position.Y:0.0},{md.CEntityDef.position.Z:0.0} d={d:0} path={fe.Path} node={(World.NodeOf(fe.ShortNameHash) != null)}");
                        }
                    }
                    sb.AppendLine($"  (archive sweep: {scanned} non-resident interior ymaps read)");
                    var wantName = Environment.GetEnvironmentVariable("RLE_YMAPNAME");
                    if (!string.IsNullOrEmpty(wantName))
                    {
                        wantName = wantName.ToLowerInvariant();
                        if (!wantName.EndsWith(".ymap")) wantName += ".ymap";
                        foreach (var kv in c.AllYmapsDict)
                        {
                            var fe = kv.Value;
                            if (fe?.NameLower != wantName) continue;
                            bool active = c.YmapDict.TryGetValue(fe.ShortNameHash, out var ae) && ReferenceEquals(ae, fe);
                            var node = World.NodeOf(fe.ShortNameHash);
                            sb.AppendLine($"  YMAPNAME {fe.Name} active={active} activePath={(ae?.Path ?? "-")} resident={resident.Contains(fe.ShortNameHash)} node={(node != null)} inTree={World.IsInLodTree(fe.ShortNameHash)} path={fe.Path}");
                        }
                        if (c.YmapDict.TryGetValue(JenkHash.GenHash(Path.GetFileNameWithoutExtension(wantName)), out var ae2))
                            sb.AppendLine($"  YMAPNAME active entry: {ae2.Path}");
                    }
                    var findFile = Environment.GetEnvironmentVariable("RLE_FINDFILE");
                    if (!string.IsNullOrEmpty(findFile))
                    {
                        var subs = findFile.ToLowerInvariant().Split(';', StringSplitOptions.RemoveEmptyEntries);
                        int shownF = 0;
                        foreach (var r in c.AllRpfs)
                            foreach (var e in r?.AllEntries ?? new List<RpfEntry>())
                                if (subs.Any(s => e.NameLower.Contains(s)) && shownF++ < 300)
                                    sb.AppendLine($"  FINDFILE {e.Path}");
                    }
                    var wantRpf = Environment.GetEnvironmentVariable("RLE_RPFNAME");
                    if (!string.IsNullOrEmpty(wantRpf))
                    {
                        foreach (var kv in c.ActiveMapRpfFiles)
                            if (kv.Key.Contains(wantRpf, StringComparison.OrdinalIgnoreCase) || (kv.Value?.Path ?? "").Contains(wantRpf, StringComparison.OrdinalIgnoreCase))
                                sb.AppendLine($"  RPFNAME active {kv.Key} -> {kv.Value?.Path} ({kv.Value?.AllEntries?.Count ?? 0} entries)");
                        foreach (var r in c.AllRpfs)
                            if ((r?.Path ?? "").Contains(wantRpf, StringComparison.OrdinalIgnoreCase))
                                sb.AppendLine($"  RPFNAME archive {r.Path} ({r.AllEntries?.Count ?? 0} entries){(r.AllEntries?.Any(e => e.NameLower == "shr_int.ymap") == true ? " HAS shr_int.ymap" : "")}");
                        {
                            var activeNames = new HashSet<string>(c.ActiveMapRpfFiles.Values.Where(r => (r?.Path ?? "").Contains(wantRpf, StringComparison.OrdinalIgnoreCase))
                                .SelectMany(r => r.AllEntries ?? new List<RpfEntry>()).Select(e => e.NameLower));
                            foreach (var r in c.AllRpfs)
                            {
                                if (!(r?.Path ?? "").Contains(wantRpf, StringComparison.OrdinalIgnoreCase) || c.ActiveMapRpfFiles.Values.Contains(r)) continue;
                                var missing = (r.AllEntries ?? new List<RpfEntry>()).Where(e => e.NameLower.EndsWith(".ymap") && !activeNames.Contains(e.NameLower)).Select(e => e.NameLower).ToList();
                                var orphan = missing.Where(m => !activeNames.Any(a => a.Length > m.Length && a.EndsWith("_" + m))).ToList();
                                sb.AppendLine($"  RPFNAME {r.Path}: {missing.Count} ymaps not in any active version, {orphan.Count} without a prefixed replacement: {string.Join(" ", orphan.Take(120))}");
                            }
                        }
                        foreach (var su in c.DlcSetupFiles)
                        {
                            var cf = su?.ContentFile; if (cf == null) continue;
                            string dn = Path.GetFileName(Path.GetDirectoryName(su.DlcFile?.Path ?? "") ?? "");
                            foreach (var df in cf.dataFiles)
                                if ((df.filename ?? "").Contains(wantRpf, StringComparison.OrdinalIgnoreCase))
                                    sb.AppendLine($"  RPFNAME dlc {dn} (order {su.order}) datafile {df.filename} type {df.fileType} overlay {df.overlay} disabled {df.disabled}");
                            void Dump(DlcContentChangeSet cs, string tag)
                            {
                                if (cs == null) return;
                                foreach (var f in cs.filesToInvalidate ?? new List<string>()) if (f.Contains(wantRpf, StringComparison.OrdinalIgnoreCase)) sb.AppendLine($"  RPFNAME dlc {dn} {tag} {cs.changeSetName} INVALIDATE {f}");
                                foreach (var f in cs.filesToEnable ?? new List<string>()) if (f.Contains(wantRpf, StringComparison.OrdinalIgnoreCase)) sb.AppendLine($"  RPFNAME dlc {dn} {tag} {cs.changeSetName} ENABLE {f}");
                                foreach (var f in cs.filesToDisable ?? new List<string>()) if (f.Contains(wantRpf, StringComparison.OrdinalIgnoreCase)) sb.AppendLine($"  RPFNAME dlc {dn} {tag} {cs.changeSetName} DISABLE {f}");
                                foreach (var m in cs.mapChangeSetData ?? new List<DlcContentChangeSet>()) Dump(m, tag + "/map");
                            }
                            foreach (var cs in cf.contentChangeSets) Dump(cs, "changeset");
                        }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  archive sweep failed: " + ex.Message); }
            return sb.ToString().TrimEnd();
        }

        public string WorldEntityReport(string archName, Vector3 at)
        {
            var sb = new System.Text.StringBuilder();
            var visSet = new HashSet<YmapEntityDef>(World.Visible);
            int n = 0;
            foreach (var y in World.ResidentYmaps)
            {
                var ents = y.AllEntities;
                if (ents == null) continue;
                foreach (var e in ents)
                {
                    if (e == null) continue;
                    var an = e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString();
                    if (!string.Equals(an, archName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (n++ >= 40) break;
                    var pe = e.Parent;
                    float d = (e.Position - at).Length();
                    string chain = "";
                    for (var q = pe; q != null && chain.Length < 400; q = q.Parent)
                        chain += $" <- {q.Archetype?.Name ?? q._CEntityDef.archetypeName.ToString()}[{q._CEntityDef.lodLevel}]@{q.Ymap?.Name} d {(q.Position - at).Length():0} lodDist {q.LodDist:0} childLod {q.ChildLodDist:0} numChildren {q._CEntityDef.numChildren} linked {(q.LodManagerChildren?.Count ?? 0)} visible {visSet.Contains(q)} built {(q.Archetype != null && worldRender.PeekModel(q.Archetype.Hash) != null)} variantHidden {World.IsVariantHiddenEnt(q)}";
                    sb.AppendLine($"FINDENT {an} ymap={y.Name} scripted={y.IsScripted} lvl={e._CEntityDef.lodLevel} pos={e.Position.X:0.0},{e.Position.Y:0.0},{e.Position.Z:0.0} d={d:0} lodDist={e.LodDist:0} childLod={e.ChildLodDist:0} bsR={e.BSRadius:0} " +
                                  $"final={World.IsFinalRenderPublic(e)} flags={e._CEntityDef.flags} archFlags={(e.Archetype?._BaseArchetypeDef.flags ?? 0)} inTree={World.IsInLodTree(y.RpfFileEntry?.ShortNameHash ?? 0)} candidate={World.IsCandidate(y.RpfFileEntry?.ShortNameHash ?? 0)} " +
                                  $"variantHidden={World.IsVariantHiddenEnt(e)} ymapVariantHidden={World.IsHiddenVariant(y.RpfFileEntry?.ShortNameHash ?? 0)} visible={visSet.Contains(e)} built={(e.Archetype != null && worldRender.PeekModel(e.Archetype.Hash) != null)} failed={(e.Archetype != null && worldRender.IsFailed(e.Archetype.Hash))} " +
                                  $"drawable={(e.Archetype != null ? gameFiles.GetDrawable(e.Archetype.Hash, out _) != null : false)} txd={(e.Archetype?.TextureDict.ToString() ?? "-")} ydd={(e.Archetype?.DrawableDict.ToString() ?? "-")} children={(e.LodManagerChildren?.Count ?? 0)}/{e._CEntityDef.numChildren} ymapPath={y.RpfFileEntry?.Path} coincident:{World.ExplainCoincident(e)} parents:{chain}");
                }
            }
            if (n == 0) sb.AppendLine($"FINDENT {archName}: no resident entity of that archetype");
            return sb.ToString().TrimEnd();
        }

        private static readonly string[] BeachWords = { "beach", "towel", "parasol", "umbrella", "volley", "lounger", "deckchair", "cooler", "bin_", "lifeguard", "sunbed", "surf" };
        private static bool IsBeachName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            foreach (var w in BeachWords) if (n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool WorldBlock(string name)
        {
            var v = Environment.GetEnvironmentVariable("RLE_WORLDBLOCKS");
            if (string.IsNullOrEmpty(v)) return true;
            foreach (var p in v.Split(',')) if (string.Equals(p.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private (float maxMs, int emptyFrames, int frames) FlyWorld(Vector3 from, Vector3 to, float speed, string tag)
        {
            float total = (to - from).Length();
            var frF = new BoundingFrustum(camera.ViewProjMatrix);
            float maxMs = 0; int empty = 0, frames = 0;
            var clockF = System.Diagnostics.Stopwatch.StartNew();
            var frameClock = new System.Diagnostics.Stopwatch();
            double lastPrint = -10;
            float flown = 0;
            double lastT = 0;
            while (flown < total)
            {
                double now = clockF.Elapsed.TotalSeconds;
                flown = Math.Min(total, (float)(now * speed));
                var pos = Vector3.Lerp(from, to, total > 0 ? flown / total : 1);
                frameClock.Restart();
                World.Select(pos, 24, WorldPrepareBudget);
                worldRender.PlayAnimations = panel.WorldAnimations;
                worldRender.Update(World.Visible, gameFiles, modelRenderer, frF, false, World.Fade);
                if (worldRender.WaitAnswerChanged) { World.Invalidate(); worldRender.ClearWaitAnswer(); }
                float ms = (float)frameClock.Elapsed.TotalMilliseconds;
                frames++;
                if (flown > total / 8)
                {
                    if (ms > maxMs) maxMs = ms;
                    if (worldRender.MeshesDrawn < 200) empty++;
                }
                if (ms > 150)
                    Console.WriteLine($"    {tag} HITCH {ms:0} ms at {pos.X:0},{pos.Y:0}: select sync {World.LastSyncMs:0} walk {World.LastWalkMs:0} prepare {World.LastPrepareMs:0} variant {World.LastVariantMs:0} (evals {World.VariantEvaluations}+{World.VariantIncrementals} total {World.VariantMsTotal:0} setup {World.VariantSetupMs:0} left {World.VariantLeftMs:0} base {World.VariantBaseMs:0} kept {World.VariantKeptMs:0} inc {World.VariantIncMs:0} coincident {World.CoincidentCalls} mine {World.CoincidentMine} inner {World.CoincidentInner}) evict {World.LastEvictMs:0} | update build {worldRender.BuildMs:0} (slowest {worldRender.SlowestBuildName} {worldRender.SlowestBuildMs:0} ms) place {worldRender.PlaceMs:0} sweep {worldRender.SweepMs:0} evict {worldRender.EvictMs:0} built {worldRender.BuiltThisFrame} released {worldRender.ModelsReleased}");
                if (now - lastPrint >= 10)
                {
                    lastPrint = now;
                    Console.WriteLine($"    {tag} {flown:0}/{total:0} m at {pos.X:0},{pos.Y:0} ({(now - lastT > 0 ? frames : 0)} frames): " + WorldLoadersLine());
                }
                System.Threading.Thread.Sleep(4);
            }
            Console.WriteLine($"  {tag}: {total:0} m at {speed:0} m/s in {clockF.Elapsed.TotalSeconds:0.0} s, {frames} frames ({frames / Math.Max(clockF.Elapsed.TotalSeconds, 0.001):0} fps), worst world tick {maxMs:0} ms, frames under 200 meshes {empty}");
            return (maxMs, empty, frames);
        }

        private (double secs, int near, int built) FillWorld(Vector3 at, float r, float fraction, double maxSecs, string tag)
        {
            var frF = new BoundingFrustum(camera.ViewProjMatrix);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            (int near, int built) c = (0, 0);
            double lastPrint = -10;
            while (sw.Elapsed.TotalSeconds < maxSecs)
            {
                World.Select(at, 24, WorldPrepareBudget);
                worldRender.PlayAnimations = panel.WorldAnimations;
                worldRender.Update(World.Visible, gameFiles, modelRenderer, frF, false, World.Fade);
                if (worldRender.WaitAnswerChanged) { World.Invalidate(); worldRender.ClearWaitAnswer(); }
                c = CountBuiltNear(at, r);
                if (c.near > 100 && c.built >= c.near * fraction && worldRender.LoadsPending < 50) break;
                if (sw.Elapsed.TotalSeconds - lastPrint >= 2)
                {
                    lastPrint = sw.Elapsed.TotalSeconds;
                    Console.WriteLine($"    {tag} fill +{lastPrint:0}s: {c.built} of {c.near} within {r:0} m built; " + WorldLoadersLine());
                }
                System.Threading.Thread.Sleep(4);
            }
            Console.WriteLine($"  {tag} fill: {c.built} of {c.near} within {r:0} m built after {sw.Elapsed.TotalSeconds:0.0} s");
            return (sw.Elapsed.TotalSeconds, c.near, c.built);
        }

        private (int doubles, int waitRule, int sibFailed, int sibLoading, int sibNeverAsked, int sibEvicted, int missingChild, int leafParent, int other)
            LodDoubleReport(Vector3 at, string name)
        {
            var vis = new HashSet<YmapEntityDef>(World.Visible);
            int doubles = 0, waitRule = 0, sibFailed = 0, sibLoading = 0, sibNever = 0, sibEvicted = 0, missingChild = 0, leafParent = 0, other = 0, shown = 0;
            var seenParents = new HashSet<YmapEntityDef>();
            foreach (var v in World.Visible)
            {
                if (v?.Archetype == null || v.MloParent != null) continue;
                YmapEntityDef anc = null;
                for (var a = v.Parent; a != null; a = a.Parent) if (vis.Contains(a)) { anc = a; break; }
                if (anc == null) continue;
                doubles++;
                var pc = anc.LodManagerChildren;
                int linked = pc?.Count ?? 0;
                string why;
                var sibs = new List<string>();
                if (linked < anc._CEntityDef.numChildren) { missingChild++; why = "numChildren>linked"; }
                else if (anc.Distance <= anc.ChildLodDist * World.LodScale || ReferenceEquals(anc, v.Parent))
                {
                    bool any = false;
                    if (pc != null)
                        for (var n = pc.First; n != null; n = n.Next)
                        {
                            var c = n.Value;
                            if (c.Archetype == null || c.MloInstance != null || !World.IsFinalRenderPublic(c) || World.IsVariantHiddenEnt(c)) continue;
                            if (worldRender.PeekModel(c.Archetype.Hash) != null || worldRender.IsFailed(c.Archetype.Hash)) continue;
                            any = true;
                            string state = worldRender.IsLoading(c.Archetype.Hash) ? "loading" : worldRender.WasBuilt(c.Archetype.Hash) ? "evicted" : "never asked";
                            if (state == "loading") sibLoading++; else if (state == "evicted") sibEvicted++; else sibNever++;
                            if (sibs.Count < 4) sibs.Add($"{c.Archetype.Name}:{state} d {(c.Position - at).Length():0} lodDist {c.LodDist:0} vis {vis.Contains(c)}");
                        }
                    if (any) { waitRule++; why = "wait-rule"; }
                    else { other++; why = "other (all siblings built - a stale walk?)"; }
                }
                else { leafParent++; why = $"leaf-parent (parent d {anc.Distance:0} > childLod {anc.ChildLodDist:0}, child d {v.Distance:0} <= lodDist {v.LodDist:0}: Rockstar data)"; }
                if (shown++ < 12 && seenParents.Add(anc))
                    Console.WriteLine($"  {name} DOUBLE {v.Archetype.Name} [{v._CEntityDef.lodLevel}] in {v.Ymap?.Name} d {(v.Position - at).Length():0} under {anc.Archetype?.Name} [{anc._CEntityDef.lodLevel}] in {anc.Ymap?.Name} numChildren {anc._CEntityDef.numChildren} linked {linked} childLod {anc.ChildLodDist:0} d {anc.Distance:0}: {why}{(sibs.Count > 0 ? " siblings: " + string.Join("; ", sibs) : "")}");
            }
            Console.WriteLine($"  {name}: {doubles} entities drawn with a visible LOD ancestor: wait-rule {waitRule} (siblings failed {sibFailed}, loading {sibLoading}, never asked {sibNever}, evicted {sibEvicted}), numChildren>linked {missingChild}, leaf-parent {leafParent}, other {other}; visible {World.Visible.Count} models {worldRender.ArchetypesLoaded} cap {worldRender.EffectiveMaxArchetypes} released {worldRender.ModelsReleased} " + WorldLoadersLine());
            return (doubles, waitRule, sibFailed, sibLoading, sibNever, sibEvicted, missingChild, leafParent, other);
        }

        private (int near, int built) CountBuiltNear(Vector3 at, float r)
        {
            int near = 0, built = 0;
            foreach (var v in World.Visible)
            {
                if (v?.Archetype == null || v.MloInstance != null) continue;
                if ((v.Position - at).Length() > r) continue;
                near++;
                if (worldRender.PeekModel(v.Archetype.Hash) != null) built++;
            }
            return (near, built);
        }

        partial void RunWorldTestExtras_World(Action<string, bool, string> check, Action<Vector3> settle)
        {
            if (WorldBlock("paleto"))
            {
                var ls = new Vector3(-270, -960, 60);
                var paleto = new Vector3(-300, 6200, 40);
                if (float.TryParse(Environment.GetEnvironmentVariable("RLE_FLYFRAC"), out float flyFrac) && flyFrac > 0 && flyFrac < 1)
                    paleto = Vector3.Lerp(ls, paleto, flyFrac);
                settle(ls);
                var lsBefore = CountBuiltNear(ls, 300);
                Console.WriteLine($"  PALETO start LS: {lsBefore.built} of {lsBefore.near} within 300 m built; " + WorldLoadersLine());
                var f1 = FlyWorld(ls, paleto, 150f, "PALETO fly north");
                Console.WriteLine("  PALETO arrived: " + WorldLoadersLine());
                int queueOnArrival = worldRender.LoadsPending, droppedBefore = worldRender.LoadsDropped;
                var fill = FillWorld(paleto, 300, 0.97f, 60.0, "PALETO");
                settle(paleto);
                var atPaleto = CountBuiltNear(paleto, 300);
                Console.WriteLine($"  PALETO settled: {atPaleto.built} of {atPaleto.near} within 300 m built; ymapsFailed {World.YmapsFailed}; " + WorldLoadersLine());
                if (World.YmapsFailed > 0)
                    Console.WriteLine("  PALETO failed ymaps e.g. " + string.Join(", ", World.Nodes.Where(n => n.LoadFailed).Take(8).Select(n => $"{n.Name}({n.FailReason})")));
                check("PALETO: after a 7 km flight north at 150 m/s Paleto Bay fills in (> 300 entities within 300 m visible, 97 % built)",
                      atPaleto.near > 300 && atPaleto.built >= atPaleto.near * 0.97, $"{atPaleto.built} of {atPaleto.near}");
                check("PALETO: standing at Paleto after the flight, the view is 97 % built within 12 s (stale requests skipped, not read)",
                      fill.secs < 12.0, $"{fill.secs:0.0} s to {fill.built} of {fill.near}; queue on arrival {queueOnArrival}, unwanted reads skipped {worldRender.LoadsDropped - droppedBefore}");
                check("PALETO: the world loader threads are alive after the flight",
                      World.LoaderStatus.Contains("alive") &&
                      worldRender.LoadersAlive == Editor.WorldRenderer.LoaderThreadCount &&
                      worldRender.LoaderErrors == 0,
                      $"{worldRender.LoadersAlive}/{Editor.WorldRenderer.LoaderThreadCount} alive, {worldRender.LoaderErrors} error(s) | " + WorldLoadersLine());
                var f2 = FlyWorld(paleto, ls, 150f, "PALETO fly back");
                settle(ls);
                var lsAfter = CountBuiltNear(ls, 300);
                Console.WriteLine($"  PALETO back in LS: {lsAfter.built} of {lsAfter.near} within 300 m built (was {lsBefore.built} of {lsBefore.near}); " + WorldLoadersLine());
                check("PALETO: flying back, Los Santos fills in again (> 300 within 300 m visible, 97 % built)",
                      lsAfter.near > 300 && lsAfter.built >= lsAfter.near * 0.97, $"{lsAfter.built} of {lsAfter.near}");
                check("PALETO: neither leg left the view empty for long (< 5 % of frames under 200 meshes)",
                      f1.emptyFrames < f1.frames * 0.05 && f2.emptyFrames < f2.frames * 0.05, $"north {f1.emptyFrames}/{f1.frames}, back {f2.emptyFrames}/{f2.frames}");
            }

            if (WorldBlock("vespucci"))
            {
                foreach (var (name, at) in new[] { ("Vespucci rooftops", new Vector3(-1150, -1450, 120)), ("Vespucci street", new Vector3(-1330, -1560, 12)) })
                {
                    settle(at);
                    var r = LodDoubleReport(at, name);
                    check($"{name}: no entity is drawn together with its own LOD ancestor after settling (<= 3, Rockstar data)",
                          r.doubles <= 3, $"{r.doubles} doubles: wait-rule {r.waitRule} (sibling failed {r.sibFailed}, loading {r.sibLoading}, never asked {r.sibNeverAsked}, evicted {r.sibEvicted}), numChildren>linked {r.missingChild}, leaf-parent {r.leafParent}, other {r.other}");
                    int orphanLods = 0, orphanLodsOnHd = 0;
                    var visHd = World.Visible.Where(v => v?.Archetype != null && v.MloParent == null &&
                                                         (v._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_HD || v._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_ORPHANHD) &&
                                                         v.BSRadius > 6).ToList();
                    foreach (var v in World.Visible)
                    {
                        if (v?.Archetype == null || v.MloParent != null) continue;
                        var lv = v._CEntityDef.lodLevel;
                        if (lv == rage__eLodType.LODTYPES_DEPTH_HD || lv == rage__eLodType.LODTYPES_DEPTH_ORPHANHD) continue;
                        if (v._CEntityDef.numChildren == 0 || (v.LodManagerChildren?.Count ?? 0) >= v._CEntityDef.numChildren) continue;
                        if ((v.Position - at).Length() > 300) continue;
                        orphanLods++;
                        YmapEntityDef inside = null;
                        foreach (var h in visHd)
                        {
                            if (h.Ymap == v.Ymap) continue;
                            var mn = Vector3.Max(h.BBMin, v.BBMin); var mx = Vector3.Min(h.BBMax, v.BBMax);
                            var d = mx - mn; if (d.X <= 0 || d.Y <= 0 || d.Z <= 0) continue;
                            var hd = h.BBMax - h.BBMin; float hv = hd.X * hd.Y * hd.Z;
                            if (hv > 1e-3f && d.X * d.Y * d.Z / hv > 0.6f) { inside = h; break; }
                        }
                        if (inside != null) orphanLodsOnHd++;
                        if (orphanLods <= 12)
                        {
                            var hn = gameFiles.Cache.YmapHierarchyDict != null && v.Ymap != null &&
                                     gameFiles.Cache.YmapHierarchyDict.TryGetValue(v.Ymap.RpfFileEntry?.ShortNameHash ?? 0, out var hnode) ? hnode : null;
                            var kids = hn?.Children == null ? "" : string.Join(", ", hn.Children.Take(6).Select(c =>
                            {
                                var cn = World.NodeOf(c.Name.Hash);
                                bool active = gameFiles.Cache.YmapDict.ContainsKey(c.Name.Hash);
                                return $"{c.Name}[{(cn == null ? "no node" : cn.Ymap == null ? "not loaded" : cn.Prepared ? "prepared" : "loading")}{(active ? "" : ",not in active map")}]";
                            }));
                            Console.WriteLine($"  {name} ORPHANLOD {v.Archetype.Name} [{lv}] in {v.Ymap?.Name} d {(v.Position - at).Length():0} lodDist {v.LodDist:0} childLod {v.ChildLodDist:0} numChildren {v._CEntityDef.numChildren} linked {(v.LodManagerChildren?.Count ?? 0)} childYmaps: {kids}" +
                                              (inside != null ? $" | HD inside it: {inside.Archetype.Name} in {inside.Ymap?.Name} (parent {(inside.Parent?.Archetype?.Name ?? "none")}@{inside.Parent?.Ymap?.Name})" : ""));
                        }
                    }
                    Console.WriteLine($"  {name}: {orphanLods} LOD-level leaves within 300 m with children missing (numChildren > linked), {orphanLodsOnHd} of them standing over another file's HD");
                    check($"{name}: no LOD-level entity with missing children stands over another file's HD (a base LOD over a DLC's HD)",
                          orphanLodsOnHd == 0, $"{orphanLodsOnHd} of {orphanLods}");
                }
            }

            int savedCap = World.MaxEntities;
            World.MaxEntities = int.MaxValue;
            if (WorldBlock("beach")) foreach (var beach in new[] { new Vector3(-1480, -1390, 30), new Vector3(-1330, -1560, 12) })
            {
                settle(beach);
                Console.WriteLine($"  BEACH at {beach}: truncated {World.Truncated} visible {World.Visible.Count} open {World.YmapsOpen}/{World.YmapsWanted} tree {World.LodTreeYmaps} hourFiltered {World.HourFilteredYmaps} variantHiddenEnts {World.VariantHiddenEntities}");
                int stuck = 0, failed = 0, hidden = 0, listed = 0;
                foreach (var n in World.Nodes.OrderBy(n => n.DistanceTo(beach)))
                {
                    if (n.DistanceTo(beach) > 400) continue;
                    var y = n.Ymap;
                    bool resident = y != null && n.Prepared;
                    var pn = n.ParentHash != 0 ? World.NodeOf(n.ParentHash) : null;
                    bool inTree = World.IsInLodTree(n.Hash);
                    bool candidate = World.IsCandidate(n.Hash);
                    bool sched = World.IsScheduledYmap(n.Hash);
                    if (n.LoadFailed) failed++;
                    if (resident && candidate && !inTree) stuck++;
                    if (World.IsHiddenVariant(n.Hash)) hidden++;
                    if (listed++ < 60)
                        Console.WriteLine($"    NODE {n.Name} d={n.DistanceTo(beach):0} flags={n.ContentFlags} scale={n.RangeScale} resident={resident} prepared={n.Prepared} failed={n.LoadFailed}{(n.LoadFailed ? " (" + n.FailReason + ")" : "")} " +
                                          $"scripted={(y?.IsScripted ?? false)} scheduled={sched} parent={n.ParentHash} parentNode={(pn != null)} parentPrepared={(pn?.Prepared ?? false)} parentObj={(y?.Parent != null)} candidate={candidate} inTree={inTree} variantHidden={World.IsHiddenVariant(n.Hash)} ents={(y?.AllEntities?.Length ?? 0)}");
                }
                var visSet = new HashSet<YmapEntityDef>(World.Visible);
                int found = 0, farOut = 0, notFinal = 0, archFailed = 0, blockedParent = 0, notVisible = 0, varHidden = 0;
                var byArch = new Dictionary<string, int>();
                foreach (var y in World.ResidentYmaps)
                {
                    var ents = y.AllEntities;
                    if (ents == null) continue;
                    var n = World.NodeOf(y.RpfFileEntry?.ShortNameHash ?? 0);
                    if (n != null && n.DistanceTo(beach) > 400) continue;
                    foreach (var e in ents)
                    {
                        if (e == null) continue;
                        var an = e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString();
                        if (!IsBeachName(an)) continue;
                        found++;
                        byArch.TryGetValue(an, out int c); byArch[an] = c + 1;
                        float dist = (e.Position - beach).Length();
                        bool vis = visSet.Contains(e);
                        bool final = World.IsFinalRenderPublic(e);
                        bool built = e.Archetype != null && worldRender.IsBuilt(e.Archetype);
                        bool afail = e.Archetype != null && worldRender.IsFailed(e.Archetype.Hash);
                        var pe = e.Parent;
                        bool pBlocked = pe != null && (pe.LodManagerChildren?.Count ?? 0) < pe._CEntityDef.numChildren;
                        bool vh = World.IsVariantHiddenEnt(e);
                        if (!vis)
                        {
                            notVisible++;
                            if (dist > e.LodDist * World.LodScale) farOut++;
                            else if (!final) notFinal++;
                            else if (vh) varHidden++;
                            else if (pBlocked) blockedParent++;
                        }
                        if (afail) archFailed++;
                        if (found <= 40 || (!vis && dist <= e.LodDist && found <= 200))
                            Console.WriteLine($"    ENT {an} ymap={y.Name} lvl={e._CEntityDef.lodLevel} dist={dist:0} lodDist={e.LodDist:0} childLod={e.ChildLodDist:0} " +
                                              $"parent={(pe?.Archetype?.Name ?? "none")}@{pe?.Ymap?.Name} numChildren={(pe?._CEntityDef.numChildren ?? 0)} linked={(pe?.LodManagerChildren?.Count ?? 0)} " +
                                              $"final={final} flags={e._CEntityDef.flags} archFlags={(e.Archetype?._BaseArchetypeDef.flags ?? 0)} visible={vis} built={built} failed={afail} variantHidden={vh} txd={(e.Archetype?.TextureDict.ToString() ?? "-")} ydd={(e.Archetype?.DrawableDict.ToString() ?? "-")}");
                    }
                }
                Console.WriteLine($"  BEACH entities named beach/towel/parasol/... in resident ymaps within 400 m: {found} (visible {found - notVisible}); not visible {notVisible} = beyond lodDist {farOut}, not final render {notFinal}, variant-hidden {varHidden}, parent numChildren>linked {blockedParent}; archetype failed {archFailed}; nodes failed {failed}, stuck (candidate but not in tree) {stuck}, ymaps variant-hidden {hidden}");
                Console.WriteLine("  BEACH by archetype: " + string.Join(", ", byArch.OrderByDescending(k => k.Value).Take(40).Select(k => $"{k.Key}={k.Value}")));
                if (beach.X < -1400)
                {
                    int vbFiles = 0, towelEnts = 0, parasolEnts = 0, beachProps = 0;
                    var vbNames = new HashSet<string>();
                    var cache = gameFiles.Cache;
                    if (cache?.YmapDict != null)
                        foreach (var kv in cache.YmapDict)
                        {
                            var nm = kv.Value?.NameLower ?? "";
                            if (!nm.StartsWith("vb_") && !nm.StartsWith("hei_vb")) continue;
                            vbFiles++;
                            YmapFile ym = null;
                            try { ym = cache.GetYmap(kv.Key); if (ym != null) gameFiles.EnsureLoaded(ym); } catch { }
                            var ents = ym?.AllEntities;
                            if (ents == null) continue;
                            foreach (var e in ents)
                            {
                                var an = e?._CEntityDef.archetypeName.ToString() ?? "";
                                if (an.IndexOf("towel", StringComparison.OrdinalIgnoreCase) >= 0) { towelEnts++; vbNames.Add(nm); }
                                if (an.IndexOf("parasol", StringComparison.OrdinalIgnoreCase) >= 0) { parasolEnts++; vbNames.Add(nm); }
                                if (an.StartsWith("prop_beach", StringComparison.OrdinalIgnoreCase)) { beachProps++; vbNames.Add(nm); }
                            }
                        }
                    Console.WriteLine($"  BEACH vb_/hei_vb_ files {vbFiles}: entities named *towel* {towelEnts}, *parasol* {parasolEnts}, prop_beach_* {beachProps}; files holding them: {string.Join(", ", vbNames.Take(30))}");
                    if (towelEnts + parasolEnts == 0)
                        Console.WriteLine("  BEACH => towels/parasols are NOT ymap entities in this install: the game spawns them from procobj.meta (procedural objects) - CodeWalker does not draw them either");
                }
                check($"beach {beach.X:0},{beach.Y:0}: no resident ymap within 400 m is a candidate stuck outside the LOD tree", stuck == 0, $"{stuck} stuck");
                check($"beach {beach.X:0},{beach.Y:0}: the selection is not truncated", !World.Truncated, $"visible {World.Visible.Count}");
                check($"beach {beach.X:0},{beach.Y:0}: no beach prop within its lodDist is hidden by the variant rule", varHidden == 0, $"{varHidden} variant-hidden");
            }
            World.MaxEntities = savedCap;

            if (WorldBlock("build2"))
            {
                var build2At = new Vector3(210.5f, -903.1f, 41.4f);
                var from = new Vector3(155, -903, 58);
                settle(from);
                YmapEntityDef hd = null, lod = null;
                foreach (var y in World.ResidentYmaps)
                    foreach (var e in y.AllEntities ?? Array.Empty<YmapEntityDef>())
                    {
                        var an = e?.Archetype?.Name ?? "";
                        if (an.Equals("dt1_13_build2", StringComparison.OrdinalIgnoreCase) && (e.Position - build2At).Length() < 2) hd = e;
                        else if (an.Equals("dt1_13_build2_lod", StringComparison.OrdinalIgnoreCase) && (e.Position - build2At).Length() < 30) lod = e;
                    }
                bool hdVisible = hd != null && World.Visible.Contains(hd);
                bool hdBuilt = hd?.Archetype != null && worldRender.PeekModel(hd.Archetype.Hash) != null && !worldRender.IsFailed(hd.Archetype.Hash);
                bool lodVisible = lod != null && World.Visible.Contains(lod);
                Console.WriteLine("  " + WorldEntityReport("dt1_13_build2", from).Replace("\n", "\n  "));
                check("dt1_13_build2 HD is visible and built from 60 m (not hidden by the variant rule, its LOD not drawn instead)",
                      hd != null && hdVisible && hdBuilt && !lodVisible,
                      $"hd {(hd == null ? "not resident" : $"visible {hdVisible} built {hdBuilt} variantHidden {World.IsVariantHiddenEnt(hd)} in {hd.Ymap?.Name}")}, lod {(lod == null ? "not resident" : $"visible {lodVisible}")}, variantHiddenEnts {World.VariantHiddenEntities}");
            }

            if (WorldBlock("bank"))
            {
                var bankAt = new Vector3(235, 215, 110);
                var bankOrigin = new Vector3(247.9f, 218.0f, 105.3f);
                settle(bankAt);
                var atOrigin = World.InteriorsEmitted.Where(e => (e.Position - bankOrigin).Length() < 1.0f).ToList();
                var residentAtOrigin = new List<string>();
                foreach (var y in World.ResidentYmaps)
                    foreach (var e in y.MloEntities ?? Array.Empty<YmapEntityDef>())
                        if (e != null && (e.Position - bankOrigin).Length() < 1.0f)
                            residentAtOrigin.Add($"{e.Archetype?.Name}@{y.Name}{(World.IsInteriorShellHidden(e) ? "(hidden)" : "")}");
                Console.WriteLine("  " + WorldInteriorReport(bankAt, 60.0f).Replace("\n", "\n  "));
                check("the Ornate Bank: exactly one interior is emitted at its origin (v_bank / hei_heist_ornate_bank resolve to one)",
                      atOrigin.Count == 1,
                      $"emitted {atOrigin.Count} [{string.Join(", ", atOrigin.Select(e => e.Archetype?.Name + "@" + e.Ymap?.Name))}], resident shells at the origin: {string.Join(", ", residentAtOrigin)}; duplicated {World.InteriorsDuplicated}");
                {
                    var near = World.Visible.Where(v => v?.Archetype != null && (v.Position - bankOrigin).Length() < 80).ToList();
                    var byKey = new Dictionary<(uint, int, int, int), List<YmapEntityDef>>();
                    foreach (var v in near)
                    {
                        var k = (v.Archetype.Hash, (int)Math.Round(v.Position.X * 4), (int)Math.Round(v.Position.Y * 4), (int)Math.Round(v.Position.Z * 4));
                        if (!byKey.TryGetValue(k, out var l)) byKey[k] = l = new List<YmapEntityDef>(2);
                        l.Add(v);
                    }
                    int dupInterior = 0, dupOther = 0, shown = 0;
                    foreach (var kv in byKey)
                    {
                        if (kv.Value.Count < 2) continue;
                        var a = kv.Value[0]; var b = kv.Value[1];
                        if (a.MloParent != null || b.MloParent != null) dupInterior++; else dupOther++;
                        if (shown++ < 10)
                            Console.WriteLine($"  BANK DUP {a.Archetype.Name} x{kv.Value.Count} at {a.Position.X:0.0},{a.Position.Y:0.0},{a.Position.Z:0.0}: " +
                                              string.Join(" | ", kv.Value.Take(3).Select(e => $"{(e.MloParent != null ? "interior of " + e.MloParent.Archetype?.Name : "ymap " + e.Ymap?.Name)}{(e.MloEntitySet != null ? " set " + e.MloEntitySet.EntitySet?.Name : "")}")));
                    }
                    var bigNear = near.Where(v => v.MloParent == null && v.MloInstance == null && v.BSRadius > 8).ToList();
                    int stacks = 0;
                    for (int i = 0; i < bigNear.Count; i++)
                        for (int j = i + 1; j < bigNear.Count; j++)
                        {
                            var a = bigNear[i]; var b = bigNear[j];
                            if (a.Ymap == b.Ymap || a.Archetype.Hash == b.Archetype.Hash) continue;
                            if ((a.Position - b.Position).Length() > 1.5f) continue;
                            if (Math.Abs(a.BSRadius - b.BSRadius) > Math.Max(a.BSRadius, b.BSRadius) * 0.35f) continue;
                            bool related = false;
                            for (var q = a.Parent; q != null; q = q.Parent) if (q == b) related = true;
                            for (var q = b.Parent; q != null; q = q.Parent) if (q == a) related = true;
                            if (related) continue;
                            stacks++;
                            if (stacks <= 6) Console.WriteLine($"  BANK STACK {a.Archetype.Name} [{a._CEntityDef.lodLevel}] in {a.Ymap?.Name} r{a.BSRadius:0} <-> {b.Archetype.Name} [{b._CEntityDef.lodLevel}] in {b.Ymap?.Name} r{b.BSRadius:0} d {(a.Position - b.Position).Length():0.0}");
                        }
                    var interior = near.Where(v => v.MloParent != null && v.BSRadius > 2).ToList();
                    int twoStates = 0;
                    for (int i = 0; i < interior.Count; i++)
                        for (int j = i + 1; j < interior.Count; j++)
                        {
                            var a = interior[i]; var b = interior[j];
                            if (a.Archetype.Hash == b.Archetype.Hash) continue;
                            if ((a.Position - b.Position).Length() > 0.5f) continue;
                            if (Math.Abs(a.BSRadius - b.BSRadius) > Math.Max(a.BSRadius, b.BSRadius) * 0.25f) continue;
                            twoStates++;
                            if (twoStates <= 12) Console.WriteLine($"  BANK TWO-STATE {a.Archetype.Name} <-> {b.Archetype.Name} r{a.BSRadius:0.0}/{b.BSRadius:0.0} at {a.Position.X:0.0},{a.Position.Y:0.0},{a.Position.Z:0.0} sets {(a.MloEntitySet?.EntitySet?.Name ?? "-")}/{(b.MloEntitySet?.EntitySet?.Name ?? "-")} flags {a._CEntityDef.flags}/{b._CEntityDef.flags}");
                        }
                    Console.WriteLine($"  BANK within 80 m of the origin: {near.Count} visible entities ({near.Count(v => v.MloParent != null)} interior), same archetype at one spot: interior {dupInterior}, other {dupOther}; big two-file stacks {stacks}; interior two-state pairs (different archetypes, same spot and size) {twoStates}");
                    check("the Ornate Bank: nothing inside is drawn twice (no two interior entities of one archetype at one spot, no two-file stack on the interior)",
                          dupInterior == 0 && stacks == 0, $"interior duplicates {dupInterior}, other duplicates {dupOther}, stacks {stacks}");
                    var otherPass = near.Where(v => (v._CEntityDef.flags & WorldStreamer.FlagsOnlyOtherPasses) != 0).ToList();
                    var proxiesDrawn = otherPass.Where(v => !(v.MloParent != null && World.MirrorOnlyKeep_N1(v))).ToList();
                    bool mirrorShellVisible = near.Any(v => string.Equals(v.Archetype.Name, "hei_obankmirror_reflect", StringComparison.OrdinalIgnoreCase));
                    check("the Ornate Bank: the mirror-pass shell (hei_obankmirror_reflect) and every OTHER-PASS PROXY stay out of the main pass (its mirror-flagged furniture draws, as in game)",
                          !mirrorShellVisible && proxiesDrawn.Count == 0,
                          $"mirror shell visible {mirrorShellVisible}; proxies drawn {proxiesDrawn.Count}: {string.Join(", ", proxiesDrawn.Take(6).Select(v => v.Archetype.Name + " flags " + v._CEntityDef.flags))}; " +
                          $"mirror-flagged interior furniture kept (by design) {otherPass.Count - proxiesDrawn.Count}");
                }
            }

            if (WorldBlock("casino"))
            {
                var cache = gameFiles.Cache;
                var mloNames = new List<string>();
                if (cache?.YtypDict != null)
                    foreach (var kv in cache.YtypDict)
                        foreach (var a in kv.Value?.AllArchetypes ?? Array.Empty<Archetype>())
                        {
                            if (!(a is MloArchetype)) continue;
                            var nm = a.Name ?? "";
                            if (nm.IndexOf("casino", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("warehouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                nm.StartsWith("vw_", StringComparison.OrdinalIgnoreCase) || nm.IndexOf("_vw_", StringComparison.OrdinalIgnoreCase) >= 0)
                                mloNames.Add($"{nm} (ytyp {kv.Value.Name}, {(a as MloArchetype).entities?.Length ?? 0} ents)");
                        }
                Console.WriteLine($"  CASINO MLO archetypes named casino/warehouse/vw_: {mloNames.Count}: {string.Join(" | ", mloNames.Take(60))}");
                var placements = new List<(string arch, string ymap, Vector3 pos, bool active, bool node, bool scripted)>();
                if (cache?.AllYmapsDict != null)
                    foreach (var kv in cache.AllYmapsDict)
                    {
                        var fe = kv.Value; var nm = fe?.NameLower ?? "";
                        if (nm.IndexOf("casino", StringComparison.Ordinal) < 0 && nm.IndexOf("warehouse", StringComparison.Ordinal) < 0 && !nm.StartsWith("vw_") && nm.IndexOf("_vw", StringComparison.Ordinal) < 0 && nm.IndexOf("ch_dlc", StringComparison.Ordinal) < 0) continue;
                        YmapFile ym = null;
                        try { ym = cache.RpfMan.GetFile<YmapFile>(fe); } catch { }
                        if (ym?.CMloInstanceDefs == null) continue;
                        bool active = cache.YmapDict.TryGetValue(fe.ShortNameHash, out var ae) && ReferenceEquals(ae, fe);
                        foreach (var md in ym.CMloInstanceDefs)
                            placements.Add((md.CEntityDef.archetypeName.ToString(), fe.Name, md.CEntityDef.position, active, World.NodeOf(fe.ShortNameHash) != null, (ym._CMapData.flags & 1) != 0));
                    }
                foreach (var p in placements.OrderBy(p => p.ymap))
                    Console.WriteLine($"  CASINO placement {p.arch} in {p.ymap} at {p.pos.X:0},{p.pos.Y:0},{p.pos.Z:0} active={p.active} node={p.node} scripted={p.scripted}");
                var targets = placements.Where(p => p.active && (p.arch.IndexOf("warehouse", StringComparison.OrdinalIgnoreCase) >= 0 || p.arch.IndexOf("casino", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                foreach (var t in targets.Take(24))
                {
                    var at = t.pos + new Vector3(0, 0, 1.5f);
                    settle(at);
                    var shell = World.InteriorsEmitted.FirstOrDefault(e => string.Equals(e.Archetype?.Name, t.arch, StringComparison.OrdinalIgnoreCase) && (e.Position - t.pos).Length() < 1);
                    var resident = World.ResidentYmaps.SelectMany(y => y.MloEntities ?? Array.Empty<YmapEntityDef>()).FirstOrDefault(e => (e.Position - t.pos).Length() < 1);
                    int inner = World.Visible.Count(v => v.MloParent != null && ReferenceEquals(v.MloParent, shell));
                    int built = World.Visible.Count(v => v.MloParent != null && ReferenceEquals(v.MloParent, shell) && v.Archetype != null && worldRender.PeekModel(v.Archetype.Hash) != null);
                    int failed = World.Visible.Count(v => v.MloParent != null && ReferenceEquals(v.MloParent, shell) && v.Archetype != null && worldRender.IsFailed(v.Archetype.Hash));
                    string why = shell != null ? "emitted" : resident == null ? "shell not resident (ymap not loaded / not in tree)" :
                                 World.IsInteriorShellHidden(resident) ? "hidden by the interior variant rule" : World.IsVariantHiddenEnt(resident) ? "hidden by the building variant rule" :
                                 !World.IsFinalRenderPublic(resident) ? "not final render (timed / proxy)" : resident.MloInstance == null ? "no MloInstance (archetype not resolved as MLO)" : "in tree but not emitted (out of its lodDist / parent walk)";
                    Console.WriteLine($"  CASINO at {t.arch} ({t.ymap}) {t.pos.X:0},{t.pos.Y:0},{t.pos.Z:0}: {why}; interior entities visible {inner}, built {built}, failed {failed}; resident shell {(resident != null ? $"{resident.Archetype?.Name} lodDist {resident.LodDist:0} inTree {World.IsInLodTree(resident.Ymap?.RpfFileEntry?.ShortNameHash ?? 0)}" : "-")}");
                    if (shell != null && inner > 0)
                    {
                        var walls = World.Visible.Where(v => ReferenceEquals(v.MloParent, shell) && v.Archetype != null).OrderByDescending(v => v.BSRadius).Take(3)
                                                 .Select(v => $"{v.Archetype.Name} r{v.BSRadius:0} built {(worldRender.PeekModel(v.Archetype.Hash) != null)} failed {worldRender.IsFailed(v.Archetype.Hash)} drawable {(gameFiles.GetDrawable(v.Archetype.Hash, out _) != null)}");
                        Console.WriteLine($"  CASINO   biggest interior entities: {string.Join(" | ", walls)}");
                    }
                    check($"casino interior {t.arch}: standing in it, the interior is emitted and its entities built",
                          shell != null && inner > 10 && built >= inner * 0.9, $"{why}; {built} of {inner} built, {failed} failed");
                }
            }

            if (WorldBlock("hour"))
            {
                var at = WorldStreamer.DowntownLosSantos;
                Console.WriteLine($"  HOURTEST manifest schedules {World.TimedYmapCount} ymaps by hour and {World.WeatherYmapCount} by weather");
                float savedHour = World.Hour;
                bool savedFilter = World.YmapHourFilter;
                World.YmapHourFilter = true;
                var perHour = new List<string>();
                int maxFiltered = 0, minFiltered = int.MaxValue;
                foreach (int h in new[] { 0, 6, 12, 18 })
                {
                    World.Hour = h;
                    World.Drain(at, 20000);
                    int scheduledResident = 0;
                    foreach (var y in World.ResidentYmaps) if (World.IsScheduledYmap(y.RpfFileEntry?.ShortNameHash ?? 0)) scheduledResident++;
                    perHour.Add($"h{h}: scheduled resident {scheduledResident}, filtered out {World.HourFilteredYmaps}, tree {World.LodTreeYmaps}, visible {World.Visible.Count}");
                    maxFiltered = Math.Max(maxFiltered, World.HourFilteredYmaps);
                    minFiltered = Math.Min(minFiltered, World.HourFilteredYmaps);
                }
                Console.WriteLine("  HOURTEST " + string.Join(" | ", perHour));
                check("hour filter: the manifest's timed ymaps are filtered by the hour (some hour hides some ymap downtown)",
                      World.TimedYmapCount == 0 || maxFiltered > 0, $"max filtered {maxFiltered}, min {minFiltered}");
                World.Hour = savedHour;
                World.YmapHourFilter = savedFilter;
                World.Drain(at, 20000);
            }

            if (WorldBlock("stack"))
            {
                var spots = new List<(string name, Vector3 at)>();
                for (float x = -2100; x <= 1600; x += 700)
                    for (float y = -3300; y <= 700; y += 700)
                        spots.Add(($"grid {x:0},{y:0}", new Vector3(x, y, 80)));
                spots.Add(("Paleto", new Vector3(-300, 6200, 40)));
                spots.Add(("Sandy", new Vector3(1900, 3700, 40)));
                spots.Add(("Casino", new Vector3(925, 46, 80)));
                spots.Add(("LS Car Meet", new Vector3(760, -1870, 30)));
                spots.Add(("Arena", new Vector3(-250, -2020, 60)));
                var table = new Dictionary<string, (int pairs, bool sa, bool sb, string sample)>();
                int totalPairs = 0, spotsWithPairs = 0;
                var sweep = System.Diagnostics.Stopwatch.StartNew();
                foreach (var (name, at) in spots)
                {
                    World.Drain(at, 15000);
                    var near = World.Visible.Where(v => v?.Archetype != null && v.MloParent == null && v.MloInstance == null &&
                                                        (v.Position - at).Length() < 350 && v.BSRadius > 8).ToList();
                    int pairs = 0;
                    for (int i = 0; i < near.Count; i++)
                        for (int j = i + 1; j < near.Count; j++)
                        {
                            var a = near[i]; var b = near[j];
                            if (a.Ymap == b.Ymap) continue;
                            if ((a.Position - b.Position).Length() > 1.5f) continue;
                            if (Math.Abs(a.BSRadius - b.BSRadius) > Math.Max(a.BSRadius, b.BSRadius) * 0.35f) continue;
                            if (a.Archetype.Hash == b.Archetype.Hash) continue;
                            bool related = false;
                            for (var q = a.Parent; q != null; q = q.Parent) if (q == b) related = true;
                            for (var q = b.Parent; q != null; q = q.Parent) if (q == a) related = true;
                            if (related) continue;
                            bool hdLod = (a._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_LOD) != (b._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_LOD);
                            if (hdLod) continue;
                            float iou = 0;
                            {
                                var mn = SharpDX.Vector3.Max(a.BBMin, b.BBMin); var mx = SharpDX.Vector3.Min(a.BBMax, b.BBMax);
                                var d = mx - mn;
                                if (d.X > 0 && d.Y > 0 && d.Z > 0)
                                {
                                    float inter = d.X * d.Y * d.Z;
                                    var da = a.BBMax - a.BBMin; var db = b.BBMax - b.BBMin;
                                    float uni = da.X * da.Y * da.Z + db.X * db.Y * db.Z - inter;
                                    iou = uni > 1e-6f ? inter / uni : 0;
                                }
                            }
                            bool sameLevel = a._CEntityDef.lodLevel == b._CEntityDef.lodLevel;
                            bool stacked = sameLevel && iou >= 0.6f && a.BSRadius > 15 && b.BSRadius > 15;
                            if (stacked && WorldStreamer.SameLayer(a.Ymap, b.Ymap)) stacked = false;
                            var ya = a.Ymap?.Name ?? "?"; var yb = b.Ymap?.Name ?? "?";
                            var key = string.CompareOrdinal(ya, yb) <= 0 ? ya + " | " + yb : yb + " | " + ya;
                            table.TryGetValue(key, out var t);
                            if (!stacked)
                            {
                                if (near.Count > 0 && t.sample == null) table[key] = (0, a.Ymap?.IsScripted ?? false, b.Ymap?.IsScripted ?? false, $"(neighbours iou {iou:0.00}) {a.Archetype.Name}[{a._CEntityDef.lodLevel}] r{a.BSRadius:0} <-> {b.Archetype.Name}[{b._CEntityDef.lodLevel}] r{b.BSRadius:0} d {(a.Position - b.Position).Length():0.0} at {name}");
                                continue;
                            }
                            pairs++;
                            table[key] = (t.pairs + 1, a.Ymap?.IsScripted ?? false, b.Ymap?.IsScripted ?? false,
                                          t.sample ?? $"{a.Archetype.Name}[{a._CEntityDef.lodLevel}] r{a.BSRadius:0} <-> {b.Archetype.Name}[{b._CEntityDef.lodLevel}] r{b.BSRadius:0} d {(a.Position - b.Position).Length():0.0} flagsA {a.Ymap?._CMapData.flags} flagsB {b.Ymap?._CMapData.flags} hiddenA {World.IsHiddenVariant(a.Ymap?.RpfFileEntry?.ShortNameHash ?? 0)} hiddenB {World.IsHiddenVariant(b.Ymap?.RpfFileEntry?.ShortNameHash ?? 0)} at {name}");
                        }
                    if (pairs > 0) { spotsWithPairs++; Console.WriteLine($"  STACK {name}: {near.Count} large entities, {pairs} stacked pairs"); }
                    totalPairs += pairs;
                }
                Console.WriteLine($"  STACKSWEEP {spots.Count} spots in {sweep.Elapsed.TotalSeconds:0} s: {totalPairs} stacked pairs at {spotsWithPairs} spots");
                foreach (var kv in table.OrderByDescending(k => k.Value.pairs).Take(40))
                    Console.WriteLine($"  STACKTABLE {kv.Key} pairs {kv.Value.pairs} scriptedA {kv.Value.sa} scriptedB {kv.Value.sb} :: {kv.Value.sample}");
                check("no two versions of a building stand on each other across the map sweep", totalPairs == 0, $"{totalPairs} pairs at {spotsWithPairs} of {spots.Count} spots");
            }
        }
    }
}

