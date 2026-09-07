using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.World;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {

        partial void ProjectSelectionTest_I2(Action<string, bool, string> check)
        {
            if (DebugGtaFolder == null) { Console.WriteLine("  SKIP scenario ymt round trip (no --gta)"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!gameFiles.Ready && string.IsNullOrEmpty(gameFiles.Error) && sw.Elapsed.TotalSeconds < 240) System.Threading.Thread.Sleep(50);
            var cache = gameFiles.Ready ? gameFiles.Cache : null;
            check("scenario: game files ready for the round trip", cache != null, cache != null ? $"in {sw.Elapsed.TotalSeconds:0}s" : gameFiles.Error ?? "not ready in time");
            if (cache == null) return;
            string dir = Path.Combine(Path.GetTempPath(), "rle_scenario_i2");
            try { Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            var savedProject = ProjWin.Project;
            try
            {
                var rpfman = cache.RpfMan;
                var manifest = rpfman.GetFile<YmtFile>("update\\update.rpf\\x64\\levels\\gta5\\sp_manifest.ymt");
                var defs = manifest?.CScenarioPointManifest?.RegionDefs;
                check("scenario: sp_manifest.ymt read", defs != null && defs.Length > 0, $"{defs?.Length ?? 0} region defs");
                if (defs == null || defs.Length == 0) return;
                YmtFile game = null;
                foreach (var region in defs)
                {
                    string rn = region.Name.ToString() + ".ymt";
                    game = rpfman.GetFile<YmtFile>(rn.Replace("platform:", "update\\update.rpf\\x64")) ?? rpfman.GetFile<YmtFile>(rn.Replace("platform:", "x64a.rpf"));
                    if (game?.ScenarioRegion?.Nodes != null && game.ScenarioRegion.Nodes.Count > 10) break;
                }
                check("scenario: a game region loaded from the archives", game?.ScenarioRegion != null, game?.Name ?? "none");
                if (game?.ScenarioRegion == null) return;
                check("scenario: the game region can be saved safely (types resolved)", ProjectController.ScenarioSaveSafe(game, cache, out var whyNot), whyNot ?? "types loaded");
                string leaf = Path.GetFileName(game.Name ?? game.RpfFileEntry?.Name ?? "region.ymt");
                string path = Path.Combine(dir, leaf);
                var bytes = game.Save();
                check("scenario: YmtFile.Save produces the region", bytes != null && bytes.Length > 64, $"{bytes?.Length ?? 0} bytes");
                if (bytes == null) return;
                File.WriteAllBytes(path, bytes);

                ProjWin.Project = new CwProject { Name = "rle_scenario_i2", Filepath = Path.Combine(dir, "rle_scenario_i2.cwproj"), HasChanged = true };
                RebuildProjectOverrides();
                int added = projCtl.AddFilesToProject(new[] { path }, quiet: true);
                check("scenario: the .ymt joins the project's Scenario Files", added == 1 && ProjWin.Project.ScenarioFilenames.Count == 1, $"{added} added, names {string.Join(",", ProjWin.Project.ScenarioFilenames)}");
                var ymt = projCtl.OpenScenarioFile(ProjWin.Project.ScenarioFilenames.FirstOrDefault());
                check("scenario: clicking the file opens the region", ymt != null && ReferenceEquals(ProjWin.CurrentScenario, ymt) && ProjWin.Project.ScenarioFiles.Contains(ymt), ymt == null ? ProjWin.Status : $"{ymt.Name} page shown");
                if (ymt == null) return;
                int n = ymt.ScenarioRegion?.Nodes?.Count ?? 0;
                check("scenario: the region reads back with its points", n == game.ScenarioRegion.Nodes.Count && n > 0, $"{n} points (game {game.ScenarioRegion.Nodes.Count})");
                var typed = ymt.ScenarioRegion.Nodes.FirstOrDefault(nd => nd?.MyPoint?.Type != null);
                check("scenario: point types resolved to names (ScenarioTypes)", typed != null, typed?.MyPoint?.Type?.Name ?? "no typed point");

                var node = ymt.ScenarioRegion.Nodes.FirstOrDefault(nd => nd?.MyPoint != null) ?? ymt.ScenarioRegion.Nodes[0];
                ProjWin.ScenarioNodeClicked = node;
                TickProjectScenario();
                check("scenario: a point clicked on the page is the world selection", ReferenceEquals(WorldEdit.Selection.ScenarioNode, node), WorldEdit.Selection.GetNameString("nothing"));
                check("scenario: the click switches to Scenario mode with the overlay on", SelMode == WorldSelectionMode.Scenario && panel.ShowScenarios, $"mode {SelMode} overlay {panel.ShowScenarios}");
                check("scenario: the point has a gizmo", WorldEdit.Selection.GizmoTarget() != null, WorldEdit.Selection.TypeName);

                var before = node.Position;
                var moved = before + new Vector3(1.5f, 0, 0.5f);
                WorldEdit.Selection.SetPosition(moved);
                ymt.HasChanged = true;
                check("scenario: the project region can be saved safely (types resolved)", ProjectController.ScenarioSaveSafe(ymt, cache, out var whyNot2), whyNot2 ?? "types loaded");
                var bytes2 = ymt.Save();
                File.WriteAllBytes(path, bytes2);
                var back = ProjectController.LoadScenarioYmt(path, cache);
                var backNode = back?.ScenarioRegion?.Nodes?.FirstOrDefault(nd => nd?.MyPoint != null && (nd.Position - moved).Length() < 0.01f);
                check("scenario: the moved point saved and read back at the new position", backNode != null, backNode != null ? $"{before} -> {backNode.Position}" : $"no point at {moved} in the re-read file ({back?.ScenarioRegion?.Nodes?.Count ?? 0} points)");
                var sd = SpaceDataOrNull;
                sd?.EnsureScenarios();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (sd != null && !sd.ScenariosReady && sd.ScenariosLoading && wait.Elapsed.TotalSeconds < 120) System.Threading.Thread.Sleep(50);
                if (sd != null && sd.ScenariosReady)
                {
                    projScenarioDirty = true;
                    SyncProjectScenarioRegions();
                    var list = sd.Scenarios.ScenarioRegions;
                    bool inList = list.Contains(ymt);
                    bool gameOut = !list.Any(g => !ReferenceEquals(g, ymt) && string.Equals(g?.Name, ymt.Name, StringComparison.OrdinalIgnoreCase));
                    check("scenario: the project region is in the world's overlay list, replacing the game's", inList && gameOut, $"in list {inList}, game copy displaced {gameOut}, {list.Count} regions");
                    projCtl.RemoveScenario(ymt);
                    SyncProjectScenarioRegions();
                    bool restored = !list.Contains(ymt) && list.Any(g => string.Equals(g?.Name, ymt.Name, StringComparison.OrdinalIgnoreCase));
                    check("scenario: removing it from the project gives the game region back", restored, $"{list.Count} regions");
                }
                else Console.WriteLine($"  SKIP overlay-list check (scenarios not loaded: {sd?.Status})");
            }
            catch (Exception ex)
            {
                check("scenario: the round trip ran without an exception", false, ex.ToString().Split('\n')[0]);
            }
            finally
            {
                if (ProjWin.Project != null) { foreach (var f in ProjWin.Project.ScenarioFiles) f.HasChanged = false; ProjWin.Project.HasChanged = false; }
                ProjWin.Project = savedProject;
                ProjWin.Select(savedProject);
                RebuildProjectOverrides();
                WorldEdit.Deselect();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        partial void RunWorldTestExtras_I2(Action<string, bool, string> check, Action<Vector3> settle)
        {
            var cache = gameFiles?.Cache;
            if (cache == null || projCtl == null) return;
            var pdir = Path.Combine(Path.GetTempPath(), "rle_projsel_i2");
            try { Directory.Delete(pdir, true); } catch { }
            Directory.CreateDirectory(pdir);
            var spot = new Vector3(-1490, -1400, 8);
            try
            {
                var tp = MakeProjectAssetsTestProject(pdir, spot, spot);
                if (tp.Source == null) { check("projsel: a test project could be built", false, "no source prop"); return; }
                projCtl.OpenProject(tp.ProjectPath);
                var mloArch = ProjWin.Project?.YtypFiles.SelectMany(t => t?.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>().FirstOrDefault(a => a.Hash == tp.IntHash);
                check("projsel: the project's interior archetype is in the window", mloArch != null && (mloArch.entities?.Length ?? 0) == 1, mloArch?.Name ?? "null");
                if (mloArch == null) return;
                settle(tp.IntSpot + new Vector3(0, 0, 1.5f));
                WireProjectSelection();
                var inst = ProjWin.FindMloInstance?.Invoke(mloArch);
                var imap = ProjWin.Project.YmapFiles.FirstOrDefault(y => y.Name.StartsWith("rle_pa_int_map", StringComparison.OrdinalIgnoreCase));
                var shell = imap?.MloEntities?.FirstOrDefault();
                check("projsel: the interior's live instance is found (project ymap fallback included)", inst?.Entities != null && inst.Entities.Length == 1,
                      inst == null ? $"no instance (ymap {(imap == null ? "missing" : imap.Name)} mloEntities {imap?.MloEntities?.Length ?? 0} shell arch {(shell?.Archetype?.Name ?? "null")} instance {(shell?.MloInstance != null)} visible {(shell != null && World.Visible.Contains(shell))})"
                                   : $"{inst.Entities?.Length ?? 0} entities, owner {inst.Owner?.Archetype?.Name}");
                if (inst?.Entities == null || inst.Entities.Length == 0) return;
                WorldEdit.Deselect();
                var room = mloArch.rooms?.FirstOrDefault(r => r.AttachedObjects != null && r.AttachedObjects.Length > 0);
                var me = room != null ? mloArch.entities[room.AttachedObjects[0]] : mloArch.entities[0];
                ProjWin.Visible = true;
                ProjWin.SelectMloEntity(me);
                TickProjectEntitySelection();
                var live = inst.Entities[0];
                check("projsel: clicking the room's prop in the window selects the LIVE placement in the world",
                      ReferenceEquals(ProjWin.CurrentEntity, live) && ReferenceEquals(WorldEdit.Selected, live),
                      $"window {(ProjWin.CurrentEntity == null ? "null" : ProjWin.CurrentEntity.Archetype?.Name)} world {(WorldEdit.Selected == null ? "null" : WorldEdit.Selected.Archetype?.Name)} pos {live.Position}");
                check("projsel: the gizmo has the prop", WorldEdit.Selection.GizmoTarget()?.Key is YmapEntityDef g && ReferenceEquals(g, live), WorldEdit.Selection.TypeName);
                var before = live.Position;
                WorldEdit.Selection.GizmoTarget().SetPosition(before + new Vector3(0.5f, 0, 0));
                WorldEntityChanged(live);
                check("projsel: a gizmo move edits the same entity the page shows",
                      (ProjWin.CurrentEntity.Position - (before + new Vector3(0.5f, 0, 0))).Length() < 0.001f && (mloArch.Ytyp?.HasChanged ?? false),
                      $"page pos {ProjWin.CurrentEntity.Position} ytyp changed {mloArch.Ytyp?.HasChanged}");
                ProjWin.Select(ProjWin.Project);
                WorldEdit.Deselect();
                TickProject();
                WorldEdit.Select(live);
                TickProject();
                check("projsel: a world pick of the interior prop shows it in the window", ReferenceEquals(ProjWin.CurrentEntity, live), ProjWin.CurrentEntity?.Archetype?.Name ?? "null");
                foreach (var t in ProjWin.Project.YtypFiles) t.HasChanged = false;
                projCtl.CloseProject();
                check("projsel: the test project closed", ProjWin.Project == null, ProjWin.Status);
            }
            catch (Exception ex)
            {
                check("projsel: the test ran without an exception", false, ex.ToString().Split('\n')[0]);
                try { if (ProjWin.Project != null) { foreach (var t in ProjWin.Project.YtypFiles) t.HasChanged = false; foreach (var y in ProjWin.Project.YmapFiles) y.HasChanged = false; } projCtl.CloseProject(); } catch { }
            }
            WorldEdit.Deselect();
            try { Directory.Delete(pdir, true); } catch { }
        }
    }
}

