using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private string projectAssetSignature = "";

        private void RefreshProjectAssets()
        {
            if (projCtl == null || gameFiles == null) return;
            var p = ProjWin.Project;
            bool render = p != null && ProjWin.RenderProjectItems;
            int regs = 0;
            try { regs = projCtl.RegisterProjectArchetypes(render); } catch { }
            LocalAssetIndex idx = null;
            try { idx = render ? projCtl.BuildAssetIndex() : null; } catch { idx = null; }
            var sig = (idx?.Signature() ?? "") + "#" + regs + "#" +
                      (render && p != null ? string.Join(",", p.YtypFiles.Select(t => (t?.Name ?? "") + ":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t))) : "");
            if (sig == projectAssetSignature) return;
            projectAssetSignature = sig;
            gameFiles.SetProjectAssets(idx);
            (int entities, int interiors) re = (0, 0);
            try { re = World.ReresolveArchetypes(); } catch { }
            if (ProjectAssetsChangeMatters_R2(idx, regs, render ? p : null)) worldRender.ReleaseAllModels();
            World.Invalidate();
            if (idx != null || regs > 0)
                DebugLog.Log($"project assets: {idx?.FileCount ?? 0} models, {idx?.TextureDictCount ?? 0} ytds in {projCtl.AssetFolders.Count} folder(s); {regs} project archetypes registered; " +
                             $"re-resolved {projCtl.ReresolvedEntities}+{re.entities} entities, {projCtl.ReresolvedInteriors}+{re.interiors} interiors");
        }

        partial void RunWorldTestExtras_ProjectAssets(Action<string, bool, string> check, Action<Vector3> settle)
        {
            var cache = gameFiles?.Cache;
            if (cache == null || projCtl == null) return;
            var pdir = Path.Combine(Path.GetTempPath(), "rle_projassets");
            try { Directory.Delete(pdir, true); } catch { }
            Directory.CreateDirectory(pdir);

            var eclipse = new Vector3(-780, 330, 190);
            settle(eclipse);
            {
                var apa = World.InteriorsEmitted.Where(e => (e.Archetype?.Name ?? "").StartsWith("apa_", StringComparison.OrdinalIgnoreCase)).ToList();
                var byOrigin = World.InteriorsEmitted
                    .GroupBy(e => ((int)Math.Round(e.Position.X * 2), (int)Math.Round(e.Position.Y * 2), (int)Math.Round(e.Position.Z * 2)))
                    .Where(g => g.Count() > 1).ToList();
                Console.WriteLine($"  INTERIORS at Eclipse Towers: emitted {World.InteriorsEmitted.Count} (apa_ {apa.Count}: {string.Join(", ", apa.Select(e => e.Archetype?.Name).Take(8))}) " +
                                  $"duplicated(same origin) {World.InteriorsDuplicated} shellsHiddenByRule {World.InteriorShellsHidden} groups {World.InteriorVariantGroups}");
                foreach (var g in byOrigin.Take(6))
                    Console.WriteLine($"    SAME ORIGIN {g.Key}: {string.Join(", ", g.Select(e => $"{e.Archetype?.Name}@{e.Ymap?.Name}"))}");
                check("no two interiors are emitted at the same origin (one apartment style shows)",
                      World.InteriorsDuplicated == 0 && byOrigin.Count == 0,
                      $"{World.InteriorsDuplicated} duplicated, {byOrigin.Count} same-origin group(s); {World.InteriorShellsHidden} shells hidden by the rule");
                check("the interior-variant rule found the apartment styles to hide (or none are resident)",
                      World.InteriorShellsHidden > 0 || apa.Count <= 1,
                      $"{World.InteriorShellsHidden} hidden, {apa.Count} apa_ shells emitted");
            }

            var spot = new Vector3(-1490, -1400, 8);
            try
            {
                var tp = MakeProjectAssetsTestProject(pdir, spot, eclipse);
                check("a game prop with its own ytd is available to copy into the test project", tp.Source != null, tp.Source?.Name ?? "none of the candidates");
                if (tp.Source != null)
                {
                    uint newHash = tp.NewHash; string newName = tp.NewName;
                    check("the game's copy has no archetype/ydr/ytd of the new name", cache.GetArchetype(newHash) == null && !cache.YdrDict.ContainsKey(newHash) && !cache.YtdDict.ContainsKey(newHash), newName);
                    Console.WriteLine($"  BASE REMOVAL candidate: {(tp.BaseName ?? "none")} MLO {(tp.BaseMlo?.Archetype?.Name ?? "-")} at {tp.BaseMlo?.Position} ({tp.BaseEntsBefore} entities before the removal)");

                    long drBefore = gameFiles.ProjectDrawablesServed, txBefore = gameFiles.ProjectTexturesServed;
                    projCtl.OpenProject(tp.ProjectPath);
                    check("the test project opened through the project window", ProjWin.Project != null && ProjWin.Project.YmapFiles.Count >= 1, ProjWin.Status);
                    check("the project's archetypes are registered in the game cache (AddProjectArchetype)",
                          cache.GetArchetype(newHash) != null && projCtl.RegisteredArchetypes >= 1,
                          $"{projCtl.RegisteredArchetypes} registered; {newName} -> {(cache.GetArchetype(newHash)?.Name ?? "null")}");
                    if (tp.HaveUserYtyp)
                    {
                        var m26 = ProjWin.Project.YtypFiles.SelectMany(t => t?.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>().FirstOrDefault();
                        check("the user's MLO ytyp registers its interior archetype", m26 != null && ReferenceEquals(cache.GetArchetype(m26.Hash), m26), m26?.Name ?? "no MLO archetype in TEST_m26_1_int_01.ytyp");
                    }
                    var pa = gameFiles.ProjectAssets;
                    check("the project's files are indexed (ydr from the list, ytd from the folder)",
                          pa != null && pa.HasModel(newHash) && pa.HasTextureDict(newHash),
                          pa == null ? "no index" : $"{pa.FileCount} models, {pa.TextureDictCount} ytds, folders {string.Join(";", projCtl.AssetFolders)}");

                    var pymap = ProjWin.Project.YmapFiles.FirstOrDefault(y => y.Name.StartsWith("rle_pa_map", StringComparison.OrdinalIgnoreCase));
                    var pent = pymap?.AllEntities?.FirstOrDefault();
                    check("the project ymap's entity resolved the project ytyp's archetype", pent?.Archetype != null && pent.Archetype.Hash == newHash,
                          pent?.Archetype?.Name ?? "no archetype");
                    settle(spot);
                    settle(spot);
                    bool visible = pent != null && World.Visible.Contains(pent);
                    bool built = pent?.Archetype != null && worldRender.PeekModel(newHash) != null;
                    bool failed = worldRender.IsFailed(newHash);
                    check("the project entity is walked by the streamer", visible, visible ? "in the visible set" : $"not visible (failed {failed}); {World.DescribeYmap(pymap, spot)}");
                    check("the project entity's model built from the project .ydr", built && !failed && gameFiles.ProjectDrawablesServed > drBefore,
                          $"built {built} failed {failed} projectDrawablesServed +{gameFiles.ProjectDrawablesServed - drBefore} path {pa?.ModelPath(newHash)}");
                    var model = worldRender.PeekModel(newHash);
                    int texturedMeshes = model?.Meshes.Count(mm => mm.DiffuseSRV != null) ?? 0;
                    check("its textures came from the project .ytd", gameFiles.ProjectTexturesServed > txBefore && texturedMeshes > 0,
                          $"projectTexturesServed +{gameFiles.ProjectTexturesServed - txBefore}, {texturedMeshes} of {model?.Meshes.Count ?? 0} meshes textured, last ytd {gameFiles.LastYtdStatus}");

                    {
                        uint intHash = tp.IntHash, intPropHash = tp.IntPropHash;
                        var mloArch = cache.GetArchetype(intHash) as MloArchetype;
                        check("the project's interior ytyp registers its MLO archetype and the interior's own prop archetype",
                              mloArch != null && cache.GetArchetype(intPropHash) != null && (mloArch.entities?.Length ?? 0) == 1,
                              $"{tp.IntName} -> {(mloArch == null ? "null" : "MLO, " + (mloArch.entities?.Length ?? 0) + " entities")}, {tp.IntPropName} -> {(cache.GetArchetype(intPropHash)?.Name ?? "null")}");
                        check("the interior prop's .ydr and .ytd in the stream/ subfolder are indexed (recursive asset folders)",
                              pa != null && pa.HasModel(intPropHash) && pa.HasTextureDict(intPropHash) && (pa.ModelPath(intPropHash) ?? "").IndexOf("stream", StringComparison.OrdinalIgnoreCase) >= 0,
                              pa == null ? "no index" : $"model {pa.ModelPath(intPropHash) ?? "-"} ytd {pa.HasTextureDict(intPropHash)} folders {string.Join(";", projCtl.AssetFolders)}");
                        var imap = ProjWin.Project.YmapFiles.FirstOrDefault(y => y.Name.StartsWith("rle_pa_int_map", StringComparison.OrdinalIgnoreCase));
                        var shell = imap?.AllEntities?.FirstOrDefault();
                        var inst = shell?.MloInstance;
                        var ient = inst?.Entities?.FirstOrDefault();
                        check("the project ymap's MLO shell has an instance built from the project's MLO archetype",
                              shell?.Archetype != null && ReferenceEquals(shell.Archetype, mloArch) && inst != null && (inst.Entities?.Length ?? 0) == 1,
                              $"shell arch {(shell?.Archetype?.Name ?? "null")} instance {(inst != null)} entities {inst?.Entities?.Length ?? 0}; ymap {(imap == null ? "missing" : imap.Name)}");
                        check("the interior's entity resolved the project archetype (re-resolved after the ytyp registered)",
                              ient?.Archetype != null && ient.Archetype.Hash == intPropHash && ReferenceEquals(ient.Archetype, cache.GetArchetype(intPropHash)),
                              $"interior entity archetype {(ient?.Archetype?.Name ?? "null")}; re-resolved {projCtl.ReresolvedEntities} entities / {projCtl.ReresolvedInteriors} interiors on registration");
                        long drBeforeInt = gameFiles.ProjectDrawablesServed;
                        settle(tp.IntSpot + new Vector3(0, 0, 1.5f));
                        settle(tp.IntSpot + new Vector3(0, 0, 1.5f));
                        bool shellEmitted = shell != null && World.InteriorsEmitted.Contains(shell);
                        bool ientVisible = ient != null && World.Visible.Contains(ient);
                        bool ientBuilt = worldRender.PeekModel(intPropHash) != null;
                        bool ientFailed = worldRender.IsFailed(intPropHash);
                        check("the interior is emitted and its own prop is walked", shellEmitted && ientVisible,
                              $"shell emitted {shellEmitted} prop visible {ientVisible} (interiors emitted {World.InteriorsEmitted.Count}); {World.DescribeYmap(imap, tp.IntSpot)}");
                        check("the interior's own prop built from the project .ydr in stream/ (ProjectDrawablesServed)",
                              ientBuilt && !ientFailed && gameFiles.ProjectDrawablesServed > drBefore,
                              $"built {ientBuilt} failed {ientFailed} projectDrawablesServed +{gameFiles.ProjectDrawablesServed - drBefore} (since the interior settle +{gameFiles.ProjectDrawablesServed - drBeforeInt}) staleLoadsDropped {worldRender.StaleLoadsDropped}");
                    }

                    if (tp.BaseCopy != null && tp.BaseMlo != null)
                    {
                        settle(eclipse);
                        var replaced = ProjWin.Project.YmapFiles.FirstOrDefault(y => y.Name.Equals(tp.BaseName, StringComparison.OrdinalIgnoreCase));
                        bool inTree = replaced != null && World.IsInLodTree(replaced.RpfFileEntry.ShortNameHash);
                        bool gameWalked = World.Visible.Any(v => ReferenceEquals(v.Ymap, tp.BaseCopy));
                        bool gameMloEmitted = World.InteriorsEmitted.Contains(tp.BaseMlo);
                        int gameInstances = worldRender.InstancesOfYmap(tp.BaseCopy);
                        bool sameMloElsewhere = World.InteriorsEmitted.Any(e => e.Archetype?.Hash == tp.BaseMlo.Archetype?.Hash && (e.Position - tp.BaseMlo.Position).Length() < 0.5f);
                        check("a project ymap named after a game ymap replaces it in the tree",
                              replaced != null && inTree && (replaced.AllEntities?.Length ?? 0) == tp.BaseEntsBefore - 1,
                              $"{tp.BaseName}: project copy {(replaced != null ? "loaded" : "missing")} inTree {inTree} ents {replaced?.AllEntities?.Length ?? 0} (game {tp.BaseEntsBefore}); {World.DescribeYmap(replaced, eclipse)}");
                        check("the replaced game ymap's entities and its interior are gone (no stale MloInstance drawn)",
                              !gameWalked && !gameMloEmitted && gameInstances == 0 && !sameMloElsewhere,
                              $"game ymap walked {gameWalked}, its MLO emitted {gameMloEmitted}, instances of the game copy {gameInstances}, same MLO at that origin from another file {sameMloElsewhere}");
                    }

                    projCtl.CloseProject();
                    check("closing the project unregisters its archetypes and index",
                          ProjWin.Project == null && cache.GetArchetype(newHash) == null && gameFiles.ProjectAssets == null,
                          $"project {(ProjWin.Project == null ? "closed" : "open")}, {newName} -> {(cache.GetArchetype(newHash) == null ? "null" : "still registered")}, index {(gameFiles.ProjectAssets == null ? "cleared" : "still set")}");
                }
            }
            catch (Exception ex)
            {
                check("the project-assets test ran without an exception", false, ex.ToString().Split('\n')[0]);
                try { projCtl.CloseProject(); } catch { }
            }
            try { Directory.Delete(pdir, true); } catch { }
        }

        private sealed class ProjectAssetsTestProject
        {
            public string ProjectPath;
            public Archetype Source;
            public string NewName = "rle_pa_bench";
            public uint NewHash;
            public string IntName = "rle_pa_int", IntPropName = "rle_pa_intprop";
            public uint IntHash, IntPropHash;
            public Vector3 IntSpot;
            public bool HaveUserYtyp;
            public YmapFile BaseCopy;
            public YmapEntityDef BaseMlo;
            public string BaseName;
            public int BaseEntsBefore;
        }

        private ProjectAssetsTestProject MakeProjectAssetsTestProject(string pdir, Vector3 spot, Vector3 baseNear)
        {
            var tp = new ProjectAssetsTestProject { ProjectPath = Path.Combine(pdir, "rle_projassets.cwproj") };
            var cache = gameFiles.Cache;
            Directory.CreateDirectory(pdir);
            RpfFileEntry ydrEntry = null, ytdEntry = null;
            var wanted = new[] { "prop_bench_01a", "prop_barrel_01a", "prop_bin_01a", "prop_dumpster_01a" }.Select(JenkHash.GenHash).ToList();
            foreach (var h in wanted.Concat(cache.YdrDict.Keys))
            {
                if (!cache.YdrDict.TryGetValue(h, out ydrEntry)) continue;
                var a = cache.GetArchetype(h);
                if (a == null || a is MloArchetype || a.DrawableDict != 0 || a.TextureDict == 0) continue;
                if (a._BaseArchetypeDef.assetType != rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE) continue;
                if (!(a.Name ?? "").StartsWith("prop_", StringComparison.OrdinalIgnoreCase)) continue;
                if (!cache.YtdDict.TryGetValue(a.TextureDict, out ytdEntry)) continue;
                tp.Source = a; break;
            }
            if (tp.Source == null) return tp;
            var src = tp.Source;
            string newName = tp.NewName;
            uint newHash = tp.NewHash = JenkHash.GenHash(newName);
            JenkIndex.Ensure(newName);
            File.WriteAllBytes(Path.Combine(pdir, newName + ".ydr"), ArchiveBrowser.ExtractForDisk(ydrEntry));
            File.WriteAllBytes(Path.Combine(pdir, newName + ".ytd"), ArchiveBrowser.ExtractForDisk(ytdEntry));

            var cw = new CwProject { Name = "rle_projassets", Filepath = tp.ProjectPath };
            var ytyp = cw.NewYtyp();
            var arch = cw.NewArchetype(ytyp, src);
            var def = arch._BaseArchetypeDef;
            def.name = newHash; def.assetName = newHash; def.textureDictionary = newHash; def.drawableDictionary = 0;
            def.assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE;
            def.lodDist = 200.0f; def.hdTextureDist = 60.0f;
            arch.Init(ytyp, ref def);
            arch._BaseArchetypeDef = def;
            File.WriteAllBytes(Path.Combine(pdir, "rle_pa_types.ytyp"), ytyp.Save());
            var userYtyp = @"C:\Users\GS\Desktop\TEST_m26_1_int_01.ytyp";
            tp.HaveUserYtyp = File.Exists(userYtyp);
            if (tp.HaveUserYtyp) File.Copy(userYtyp, Path.Combine(pdir, "test_m26_1_int_01.ytyp"), true);

            YmapBuilder.Save(Path.Combine(pdir, "rle_pa_map.ymap"), "rle_pa_map",
                             new[] { new YmapEntry { ArchetypeName = newName, Position = spot, LodDist = 200.0f } });

            uint intHash = tp.IntHash = JenkHash.GenHash(tp.IntName);
            uint intPropHash = tp.IntPropHash = JenkHash.GenHash(tp.IntPropName);
            JenkIndex.Ensure(tp.IntName); JenkIndex.Ensure(tp.IntPropName);
            var streamDir = Path.Combine(pdir, "stream");
            Directory.CreateDirectory(streamDir);
            File.WriteAllBytes(Path.Combine(streamDir, tp.IntPropName + ".ydr"), ArchiveBrowser.ExtractForDisk(ydrEntry));
            File.WriteAllBytes(Path.Combine(streamDir, tp.IntPropName + ".ytd"), ArchiveBrowser.ExtractForDisk(ytdEntry));
            var intYtyp = cw.NewYtyp();
            var propArch = cw.NewArchetype(intYtyp, src);
            var pdef = propArch._BaseArchetypeDef;
            pdef.name = intPropHash; pdef.assetName = intPropHash; pdef.textureDictionary = intPropHash; pdef.drawableDictionary = 0;
            pdef.assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE;
            pdef.lodDist = 100.0f; pdef.hdTextureDist = 30.0f;
            propArch.Init(intYtyp, ref pdef);
            propArch._BaseArchetypeDef = pdef;
            var mlo = new MloArchetype();
            var mdef = new CMloArchetypeDef();
            mdef._BaseArchetypeDef.name = intHash; mdef._BaseArchetypeDef.assetName = intHash;
            mdef._BaseArchetypeDef.assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_ASSETLESS;
            mdef._BaseArchetypeDef.lodDist = 200.0f; mdef._BaseArchetypeDef.hdTextureDist = 60.0f;
            mdef._BaseArchetypeDef.bbMin = new Vector3(-4, -4, -1); mdef._BaseArchetypeDef.bbMax = new Vector3(4, 4, 4);
            mdef._BaseArchetypeDef.bsCentre = new Vector3(0, 0, 1.5f); mdef._BaseArchetypeDef.bsRadius = 6.5f;
            mdef._BaseArchetypeDef.flags = 0;
            mlo.Init(intYtyp, ref mdef);
            intYtyp.AddArchetype(mlo);
            var limbo = MloEditor.AddRoom(mlo, "limbo");
            var room = MloEditor.AddRoom(mlo, "rle_room");
            room._Data.bbMin = new Vector3(-4, -4, -1); room._Data.bbMax = new Vector3(4, 4, 4);
            var ie = new YmapEntityDef();
            var idef = new CEntityDef { archetypeName = intPropHash, flags = 0, guid = 7, position = new Vector3(0, 0, 0.2f), rotation = new Vector4(0, 0, 0, 1),
                                        scaleXY = 1.0f, scaleZ = 1.0f, lodDist = 100.0f, childLodDist = 0, lodLevel = rage__eLodType.LODTYPES_DEPTH_ORPHANHD, numChildren = 0,
                                        priorityLevel = rage__ePriorityLevel.PRI_REQUIRED, ambientOcclusionMultiplier = 255, artificialAmbientOcclusion = 255 };
            ie.CEntityDef = idef;
            mlo.AddEntity(ie, 1);
            File.WriteAllBytes(Path.Combine(pdir, "rle_pa_int.ytyp"), intYtyp.Save());
            tp.IntSpot = spot + new Vector3(0, 14, 0);
            var imap = new YmapFile();
            imap.RpfFileEntry = new RpfResourceFileEntry { Name = "rle_pa_int_map.ymap" };
            imap.Name = imap.RpfFileEntry.Name;
            imap._CMapData.name = JenkHash.GenHash("rle_pa_int_map");
            imap._CMapData.parent = new MetaHash(0);
            imap._CMapData.contentFlags = 65 | 8;
            imap.Loaded = true;
            var shell = new YmapEntityDef();
            var sdef = new CEntityDef { archetypeName = intHash, flags = 1572864, guid = 9, position = tp.IntSpot, rotation = new Vector4(0, 0, 0, 1),
                                        scaleXY = 1.0f, scaleZ = 1.0f, lodDist = 200.0f, childLodDist = 0, lodLevel = rage__eLodType.LODTYPES_DEPTH_ORPHANHD, numChildren = 0,
                                        priorityLevel = rage__ePriorityLevel.PRI_REQUIRED, ambientOcclusionMultiplier = 255, artificialAmbientOcclusion = 255 };
            shell.CEntityDef = sdef;
            shell.Position = tp.IntSpot; shell.Orientation = Quaternion.Identity; shell.Scale = Vector3.One;
            shell.BSRadius = 6.5f;
            shell.MloInstance = new MloInstanceData(shell, null) { Instance = new CMloInstanceDef { CEntityDef = sdef, groupId = 0, floorId = 0, numExitPortals = 0, MLOInstflags = 0 } };
            shell.IsMlo = true;
            imap.AddEntity(shell);
            imap.CalcFlags(); imap.CalcExtents();
            File.WriteAllBytes(Path.Combine(pdir, "rle_pa_int_map.ymap"), imap.Save());

            foreach (var y in World.ResidentYmaps.OrderBy(y => y.Name))
            {
                if (y?.RpfFileEntry == null || y.MloEntities == null || y.MloEntities.Length == 0) continue;
                if (y.IsScripted) continue;
                var m = y.MloEntities.FirstOrDefault(e => e?.MloInstance != null && (e.Position - baseNear).Length() < 250);
                if (m == null) continue;
                try
                {
                    var copy = new YmapFile();
                    copy.Load(ArchiveBrowser.ExtractForDisk(y.RpfFileEntry));
                    copy.RpfFileEntry ??= new RpfResourceFileEntry();
                    copy.RpfFileEntry.Name = y.RpfFileEntry.Name;
                    copy.Name = y.RpfFileEntry.Name;
                    var target = copy.AllEntities?.FirstOrDefault(e => e._CEntityDef.archetypeName == m._CEntityDef.archetypeName && (e.Position - m.Position).Length() < 0.01f);
                    if (target == null) continue;
                    int before = copy.AllEntities.Length;
                    copy.RemoveEntity(target);
                    if ((copy.AllEntities?.Length ?? 0) != before - 1) continue;
                    copy.CalcFlags(); copy.CalcExtents();
                    File.WriteAllBytes(Path.Combine(pdir, y.RpfFileEntry.Name), copy.Save());
                    tp.BaseCopy = y; tp.BaseMlo = m; tp.BaseName = y.RpfFileEntry.Name; tp.BaseEntsBefore = before;
                    break;
                }
                catch { }
            }

            cw.YtypFilenames.Clear();
            cw.YmapFilenames.Clear();
            cw.YtypFilenames.Add("rle_pa_types.ytyp");
            cw.YtypFilenames.Add("rle_pa_int.ytyp");
            if (tp.HaveUserYtyp) cw.YtypFilenames.Add("test_m26_1_int_01.ytyp");
            cw.YmapFilenames.Add("rle_pa_map.ymap");
            cw.YmapFilenames.Add("rle_pa_int_map.ymap");
            if (tp.BaseName != null) cw.YmapFilenames.Add(tp.BaseName);
            cw.YdrFilenames.Add(newName + ".ydr");
            cw.Save(tp.ProjectPath);
            return tp;
        }

        private int worldProjectProbeTick;
        private ProjectAssetsTestProject worldProbeProject;
        private void TickProjectProbe()
        {
            var open = Environment.GetEnvironmentVariable("RLE_PROJECT");
            bool make = Environment.GetEnvironmentVariable("RLE_PROJECT_MAKE") == "1";
            var folder = Environment.GetEnvironmentVariable("RLE_OPENFOLDER");
            if (string.IsNullOrEmpty(open) && !make && string.IsNullOrEmpty(folder)) return;
            int t = ++worldProjectProbeTick;
            if (t == 200)
            {
                try
                {
                    if (!string.IsNullOrEmpty(folder))
                    {
                        int n = projCtl.OpenFolder(folder, confirmLarge: false);
                        Console.WriteLine($"WORLDPROJECT open folder {folder}: {n} files, {ProjWin.Status}");
                    }
                    if (make)
                    {
                        var dir = Path.Combine(Path.GetTempPath(), "rle_projprobe");
                        try { Directory.Delete(dir, true); } catch { }
                        worldProbeProject = MakeProjectAssetsTestProject(dir, camera.Position + camera.GetForward() * 6.0f, camera.Position);
                        open = worldProbeProject.ProjectPath;
                        Console.WriteLine($"WORLDPROJECT made {open}: source {worldProbeProject.Source?.Name} base {worldProbeProject.BaseName ?? "none"} ({worldProbeProject.BaseMlo?.Archetype?.Name})");
                    }
                    if (!string.IsNullOrEmpty(open)) { projCtl.OpenProject(open); Console.WriteLine($"WORLDPROJECT open: {ProjWin.Status}"); }
                }
                catch (Exception ex) { Console.WriteLine("WORLDPROJECT probe failed: " + ex.Message); }
            }
            if (t == 430)
            {
                var p = ProjWin.Project;
                Console.WriteLine($"WORLDPROJECT project {(p?.Name ?? "none")} ymaps {p?.YmapFiles.Count ?? 0} ytyps {p?.YtypFiles.Count ?? 0} registered {projCtl.RegisteredArchetypes} " +
                                  $"index {(gameFiles.ProjectAssets == null ? "none" : gameFiles.ProjectAssets.FileCount + " models, " + gameFiles.ProjectAssets.TextureDictCount + " ytds")} " +
                                  $"served drawables {gameFiles.ProjectDrawablesServed} textures {gameFiles.ProjectTexturesServed} folders {string.Join(";", projCtl.AssetFolders)}");
                if (p != null)
                    foreach (var y in p.YmapFiles)
                    {
                        Console.WriteLine("WORLDPROJECT ymap " + World.DescribeYmap(y, camera.Position));
                        foreach (var e in y.AllEntities ?? Array.Empty<YmapEntityDef>())
                        {
                            if (e?.Archetype == null) { Console.WriteLine($"WORLDPROJECT   ent {e?._CEntityDef.archetypeName} NO ARCHETYPE"); continue; }
                            Console.WriteLine($"WORLDPROJECT   ent {e.Archetype.Name} built {worldRender.PeekModel(e.Archetype.Hash) != null} failed {worldRender.IsFailed(e.Archetype.Hash)} instances {worldRender.HasInstances(e)} projectFile {gameFiles.ProjectAssets?.ModelPath(e.Archetype.Hash) ?? "-"}");
                            var ients = e.MloInstance?.Entities;
                            if (ients == null) continue;
                            Console.WriteLine($"WORLDPROJECT     interior emitted {World.InteriorsEmitted.Contains(e)} entities {ients.Length}");
                            foreach (var ie in ients.Take(12))
                                Console.WriteLine($"WORLDPROJECT     int {(ie?.Archetype?.Name ?? ie?._CEntityDef.archetypeName.ToString() + " NO ARCHETYPE")} visible {World.Visible.Contains(ie)} built {(ie?.Archetype != null && worldRender.PeekModel(ie.Archetype.Hash) != null)} failed {(ie?.Archetype != null && worldRender.IsFailed(ie.Archetype.Hash))} projectFile {(ie?.Archetype != null ? gameFiles.ProjectAssets?.ModelPath(ie.Archetype.Hash) ?? "-" : "-")}");
                        }
                    }
                if (worldProbeProject?.BaseCopy != null)
                    Console.WriteLine($"WORLDPROJECT base game copy: walked {World.Visible.Any(v => ReferenceEquals(v.Ymap, worldProbeProject.BaseCopy))} mloEmitted {World.InteriorsEmitted.Contains(worldProbeProject.BaseMlo)} instances {worldRender.InstancesOfYmap(worldProbeProject.BaseCopy)}");
            }
        }
    }
}

