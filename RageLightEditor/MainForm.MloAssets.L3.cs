using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool mloAssetsWired;
        private bool mloAssetDemoDone;

        partial void WireMloAssets_L3()
        {
            if (mloAssetsWired) return;
            mloAssetsWired = true;
        }

        private void ServiceMloAssets_L3(MloCreatorPanel ui)
        {
            if (!panel.MloMode || ui == null) return;
            var lib = ui.Assets;
            TickArchiveIndex_N3(lib);

            lib.ArchiveModels = lib.ArchivesReady ? panel.Archive.FileCount : 0;
            lib.PropFolders.Clear();
            if (settings?.PropFolders != null) lib.PropFolders.AddRange(settings.PropFolders);
            lib.ProjectFolders.Clear();
            foreach (var d in MloProjectFolders_L3(ui)) lib.ProjectFolders.Add(d);
            lib.LightPropCount = lightPropLibrary?.Count ?? 0;

            ServiceMloAssets_P2(ui);

            if (lib.Dirty)
            {
                lib.Dirty = false;
                RunMloAssetSearch_L3(lib);
            }

            if (!mloAssetDemoDone && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_MLOASSET")) &&
                mloScene.HasModel && (DebugMlo == null || debugMloDone))
            {
                var name = Environment.GetEnvironmentVariable("RLE_MLOASSET");
                lib.Query = name; lib.Dirty = true;
                RunMloAssetSearch_L3(lib);
                if (ui.Session == null) { ui.Session = MloCreatorSession.FromScene(mloScene); ui.SelectRoom(ui.Session.Rooms.Count > 1 ? 1 : 0); }
                var pick = lib.Results.FirstOrDefault();
                if (pick != null) { lib.PlaceAt = 1; PlaceMloAssets_L3(ui, new[] { pick }, 1); lib.Selected = pick; }
                ui.WindowVisible = true; ui.ShowPage(MloCreatorPanel.PageKind.Assets);
                Console.WriteLine($"MLOASSET '{name}': {lib.Results.Count} hits, placed {(pick != null ? pick.Name : "(none)")}; mloScene {mloScene.Files.Count} files, lightScene {lightScene.Files.Count} files");
                if (Environment.GetEnvironmentVariable("RLE_MLOLIGHTS") == "1" && mloScene.Lights.Count > 0)
                {
                    ui.ShowPage(MloCreatorPanel.PageKind.Lights);
                    mloScene.SelectedIndex = 0;
                    var owner = mloScene.OwnerFile(mloScene.Lights[0]);
                    if (owner != null) mloScene.SelectFile(owner, false, false);
                    UpdateMloLightGizmoEnabled_L3(ui);
                    Console.WriteLine($"MLOLIGHTS: {mloScene.Lights.Count} lights, selected 0 on {owner?.Name}, gizmo {gizmo.Enabled}");
                }
                if (Environment.GetEnvironmentVariable("RLE_MLOSHOWLIGHTS") == "1")
                {
                    panel.SwitchWorkspace(LightPanel.Space.Light);
                    Console.WriteLine($"MLOASSET Lights workspace: {lightScene.Files.Count} files, {lightScene.Lights.Count} lights, HasModel {lightScene.HasModel}");
                }
                mloAssetDemoDone = true;
            }

            if (lib.RequestRescan)
            {
                lib.RequestRescan = false;
                lib.FolderFiles = CountModelFiles_L3(lib.PropFolders);
                lib.ProjectFiles = CountModelFiles_L3(lib.ProjectFolders);
                lib.Dirty = true;
                lib.Status = $"Rescanned: {lib.FolderFiles} in prop folders, {lib.ProjectFiles} in the interior's folder.";
            }
            if (lib.RequestAddPropFolder)
            {
                lib.RequestAddPropFolder = false;
                DoAddPropFolder();
                lib.Dirty = true;
            }
            if (lib.RequestPickFolderToPlace)
            {
                lib.RequestPickFolderToPlace = false;
                var folder = PickFolder_L3("Folder of .ydr / .yft to place all of");
                if (!string.IsNullOrEmpty(folder)) lib.RequestPlaceFolder = folder;
            }
            if (lib.RequestPlace != null)
            {
                var it = lib.RequestPlace; lib.RequestPlace = null;
                int count = Math.Max(1, lib.RequestPlaceCount); lib.RequestPlaceCount = 1;
                PlaceMloAssets_L3(ui, new[] { it }, count);
            }
            if (!string.IsNullOrEmpty(lib.RequestPlaceFolder))
            {
                var folder = lib.RequestPlaceFolder; lib.RequestPlaceFolder = null;
                var items = MloAssetLibrary.ModelsInFolder(folder, MloAssetSource.PropFolder);
                if (items.Count == 0) ui.SetStatus($"No .ydr / .yft in {folder}.", true);
                else PlaceMloAssets_L3(ui, items, 1);
            }
            if (lib.RequestResolveTyped)
            {
                lib.RequestResolveTyped = false;
                ResolveTypedEntities_L3(ui);
            }
        }

        private IEnumerable<string> MloProjectFolders_L3(MloCreatorPanel ui)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in mloScene.Files)
            {
                if (f == null || string.IsNullOrEmpty(f.Path) || f.ReadOnly) continue;
                var d = SafeDir(f.Path);
                if (d != null) set.Add(d);
            }
            var yt = mloScene.MloInfo?.Ytyps?.FirstOrDefault(y => !string.IsNullOrEmpty(y.Path))?.Path;
            if (!string.IsNullOrEmpty(yt))
            {
                try { set.Add(LocalAssetIndex.FindRoot(yt)); } catch { }
            }
            return set.Where(Directory.Exists);
        }

        private static string SafeDir(string path) { try { return Path.GetDirectoryName(Path.GetFullPath(path)); } catch { return null; } }
        private static int CountModelFiles_L3(IEnumerable<string> folders)
        {
            var tmp = new List<MloAssetItem>();
            return MloAssetLibrary.ScanFolders(folders, "", MloAssetSource.PropFolder, tmp, max: 100000);
        }

        private void RunMloAssetSearch_L3(MloAssetLibrary lib)
        {
            int Max = Math.Clamp(lib.FetchLimit_P2, MloAssetLibrary.PageSize_P2, MloAssetLibrary.MaxFetch_P2);
            int fetch = lib.OverFetch_P2(Max);
            var results = new List<MloAssetItem>();
            var exts = lib.KindFilter == 1 ? new[] { ".ydr" } : lib.KindFilter == 2 ? new[] { ".yft" } : new[] { ".ydr", ".yft" };
            int total = 0;
            bool wantArchive = lib.SourceFilter == 0 || lib.SourceFilter == 1;
            bool wantFolders = lib.SourceFilter == 0 || lib.SourceFilter == 2;
            bool wantProject = lib.SourceFilter == 0 || lib.SourceFilter == 3;
            bool wantLightProps = lib.SourceFilter == 0 || lib.SourceFilter == 4;

            int diskMax = wantArchive && lib.ArchivesReady ? fetch * 2 / 5 : fetch;
            if (wantProject) total += MloAssetLibrary.ScanFolders(lib.ProjectFolders, lib.Query, MloAssetSource.Project, results, diskMax);
            if (wantFolders) total += MloAssetLibrary.ScanFolders(lib.PropFolders, lib.Query, MloAssetSource.PropFolder, results, diskMax);
            if (wantLightProps && lightPropLibrary != null)
            {
                var hits = lightPropLibrary.Search(lib.Query ?? "");
                if (exts.Length == 1) hits = hits.Where(h => h.IsYft == (exts[0] == ".yft")).ToList();
                total += hits.Count;
                MloAssetLibrary.FromLightProps(hits, results, diskMax);
            }
            if (lib.Category_P2 != 0) total = lib.FilterToCategory_P2(results);
            if (wantArchive && lib.ArchivesReady)
                total += lib.Category_P2 != 0 ? SearchArchiveByCategory_P2(lib, exts, results, Max)
                                              : SearchArchiveAssets_N3(lib, exts, results, fetch);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<MloAssetItem>();
            foreach (var it in results)
            {
                var key = it.FromArchive ? "arch:" + it.Path : "disk:" + it.Path;
                if (!seen.Add(key)) continue;
                deduped.Add(it);
            }
            OrderMloAssets_N3(deduped, lib);
            if (deduped.Count > Max) deduped.RemoveRange(Max, deduped.Count - Max);
            lib.Results.Clear();
            lib.Results.AddRange(deduped);
            lib.ResultTotal = Math.Max(total, lib.Results.Count);
            lib.AllLoaded_P2 = lib.Results.Count >= lib.ResultTotal || Max >= MloAssetLibrary.MaxFetch_P2;
            lib.FolderFiles = lib.FolderFiles == 0 ? CountModelFiles_L3(lib.PropFolders) : lib.FolderFiles;
            lib.ProjectFiles = CountModelFiles_L3(lib.ProjectFolders);
            if (lib.Selected != null && !lib.Results.Contains(lib.Selected)) lib.Selected = null;
        }

        private Vector3 MloPlacementPoint_L3(MloCreatorPanel ui)
        {
            var lib = ui.Assets;
            switch (lib.PlaceAt)
            {
                case 1: return camera.Target;
                case 2:
                    return DropPoint(deviceResources.Width * 0.5f, deviceResources.Height * 0.5f);
                case 3:
                    var r = ui.CurrentRoom;
                    if (r != null) return new Vector3(r.Centre.X, r.Centre.Y, r.Min.Z + 0.02f);
                    return camera.Target;
                default:
                    return ui.HasSnapPoint ? ui.SnapPoint : camera.Target;
            }
        }

        private void PlaceMloAssets_L3(MloCreatorPanel ui, IList<MloAssetItem> items, int perItem)
        {
            var s = ui.Session;
            if (s == null) { ui.Session = MloCreatorSession.FromScene(mloScene); s = ui.Session; }
            var origin = MloPlacementPoint_L3(ui);
            int entities0 = s.Entities.Count;
            s.PushUndo(items.Count > 1 ? $"Place {items.Count} assets" : "Place asset");
            int placedFiles = 0, placedEntities = 0, failed = 0; string firstName = null;
            LoadedFile lastFile = null;
            int total = items.Count * Math.Max(1, perItem);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(total)));
            int k = 0;
            foreach (var it in items)
            {
                for (int c = 0; c < Math.Max(1, perItem); c++)
                {
                    var off = total > 1 ? new Vector3((k % cols) * 1.2f, (k / cols) * 1.2f, 0) : Vector3.Zero;
                    var lf = BuildAndAddMloAsset_L3(it, origin + off, out bool built);
                    k++;
                    if (!built) { failed++; continue; }
                    placedFiles++;
                    lastFile = lf;
                    firstName ??= it.Name;
                    int before = s.Entities.Count;
                    s.AddEntityFromFile(lf);
                    placedEntities += s.Entities.Count - before;
                    ui.Assets.Touch(it);
                }
            }
            s.AutoAssignRooms();
            ApplyPlaceRoom_N3(ui, s, entities0);
            if (lastFile != null && ui.Assets.SelectAfterPlace)
            {
                mloScene.SelectFile(lastFile, false, false);
                panel.ScrollToActiveProp = true;
                int ei = s.Entities.FindLastIndex(e => e.SourceFile == lastFile);
                if (ei >= 0) { ui.SelectEntity(ei); ui.RevealSelection = true; }
            }
            string room = ui.PlaceRoomIndex >= 0 && ui.PlaceRoomIndex < s.Rooms.Count ? $" into room {ui.PlaceRoomIndex} ({s.Rooms[ui.PlaceRoomIndex].Name})" : "";
            ui.SetStatus(failed == 0
                ? $"Placed {placedFiles} model{(placedFiles == 1 ? "" : "s")} ({placedEntities} entit{(placedEntities == 1 ? "y" : "ies")}){room} at ({origin.X:0.0}, {origin.Y:0.0}, {origin.Z:0.0}). W moves it, E turns it, Del deletes it."
                : $"Placed {placedFiles}; {failed} could not be loaded" + (firstName != null ? $" (first ok: {firstName})." : "."), failed > 0 && placedFiles == 0);
        }

        private LoadedFile BuildAndAddMloAsset_L3(MloAssetItem it, Vector3 pos, out bool built)
        {
            built = false;
            RenderModel model = null;
            try
            {
                var e = it.ToEntry();
                if (!PropThumbnails.Load(e, gameFiles, out var drawable, out var lights, out var ydr, out var yft) || drawable == null)
                    return null;
                var placement = Matrix.Translation(pos);
                var arch = gameFiles != null && gameFiles.Ready ? gameFiles.Cache?.GetArchetype(JenkHash.GenHash(it.Name.ToLowerInvariant())) : null;
                if (arch != null && arch.TextureDict != 0)
                {
                    var ytd = gameFiles.GetTextureDict(arch.TextureDict);
                    if (ytd?.TextureDict != null && !modelRenderer.ExternalTextureDicts.Contains(ytd.TextureDict))
                        modelRenderer.ExternalTextureDicts.Add(ytd.TextureDict);
                }
                modelRenderer.TextureContext = arch?.TextureDict ?? 0;
                model = modelRenderer.BuildFromDrawable(drawable, it.Name, placement);
                modelRenderer.TextureContext = 0;
                if (model.Meshes.Count == 0 && (lights == null || lights.Length == 0))
                {
                    model.Dispose();
                    return null;
                }
                var lf = mloScene.AddImportedProp(it.FromArchive ? null : it.Path, ydr, yft, model,
                    drawable.Skeleton, lights, placement, 1, it.FromArchive, it.Name, null, fromMlo: false, drawable: drawable);
                built = true;
                model = null;
                return lf;
            }
            catch (Exception ex)
            {
                model?.Dispose();
                Console.WriteLine($"MLOASSET place failed {it.Name}: {ex.Message}");
                return null;
            }
        }

        private void ResolveTypedEntities_L3(MloCreatorPanel ui)
        {
            var s = ui.Session;
            if (s == null) { ui.SetStatus("No interior yet.", true); return; }
            var typed = s.Entities.Where(e => e.SourceFile == null && !e.FromImportedMlo && !string.IsNullOrWhiteSpace(e.ArchetypeName)).ToList();
            if (typed.Count == 0) { ui.SetStatus("No typed entities to resolve (every entity already has a model)."); return; }
            s.PushUndo("Resolve typed entities");
            int ok = 0;
            foreach (var e in typed)
            {
                var it = FindMloAsset_L3(e.ArchetypeName);
                if (it == null) continue;
                var lf = BuildAndAddMloAsset_L3(it, e.Position, out bool built);
                if (!built) continue;
                e.SourceFile = lf;
                ok++;
            }
            s.AutoAssignRooms();
            ui.SetStatus(ok == 0 ? "None of the typed entities' models were found in the archives / prop folders." : $"Resolved {ok} of {typed.Count} typed entit{(typed.Count == 1 ? "y" : "ies")} - their models are placed and their lights are editable.", ok == 0);
        }

        private MloAssetItem FindMloAsset_L3(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var exts = new[] { ".ydr", ".yft" };
            var into = new List<MloAssetItem>();
            MloAssetLibrary.ScanFolders(MloProjectFolders_L3(Creator), name, MloAssetSource.Project, into, 32);
            if (settings?.PropFolders != null) MloAssetLibrary.ScanFolders(settings.PropFolders, name, MloAssetSource.PropFolder, into, 32);
            var exact = into.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) ?? into.FirstOrDefault();
            if (exact != null) return exact;
            if (panel.Archive != null && panel.Archive.Ready)
            {
                var hits = new List<ArchiveBrowser.Entry>();
                panel.Archive.Find(name, exts, hits, 32);
                var arch = new List<MloAssetItem>();
                MloAssetLibrary.FromArchive(hits, arch, 32);
                return arch.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)) ?? arch.FirstOrDefault();
            }
            return null;
        }

        private string PickFolder_L3(string title)
        {
            if (IsHeadless) return null;
            using var dlg = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
        }

        private void MloAssetsTest_L3(Action<string, bool, string> check)
        {
            var ui = Creator;
            var lib = ui.Assets;
            string dir = Path.Combine(Path.GetTempPath(), "rle_mloassets");
            Directory.CreateDirectory(dir);
            string src = Path.Combine(Path.GetTempPath(), "rle_mlocreator", "light_test_scene.ydr");
            if (!File.Exists(src)) { TestSceneGenerator.Run(src); }
            string a = Path.Combine(dir, "prop_alpha_bench.ydr");
            string b = Path.Combine(dir, "prop_beta_chair.ydr");
            string y = Path.Combine(dir, "prop_gamma_lamp.yft");
            File.Copy(src, a, true); File.Copy(src, b, true); File.Copy(src, y, true);

            var found = new List<MloAssetItem>();
            int total = MloAssetLibrary.ScanFolders(new[] { dir }, "beta", MloAssetSource.PropFolder, found, 50);
            check("mloassets: folder search by name", total == 1 && found.Count == 1 && found[0].Name == "prop_beta_chair", $"{found.Count}/{total}");
            found.Clear();
            MloAssetLibrary.ScanFolders(new[] { dir }, "", MloAssetSource.PropFolder, found, 50);
            check("mloassets: empty query lists the folder", found.Count == 3, $"{found.Count} of 3");
            check("mloassets: .yft flagged", found.Any(i => i.Name == "prop_gamma_lamp" && i.IsYft), found.Count(i => i.IsYft) + " yft");
            var inFolder = MloAssetLibrary.ModelsInFolder(dir);
            check("mloassets: models-in-folder finds all three", inFolder.Count == 3, $"{inFolder.Count}");

            if (ui.Session == null) ui.Session = MloCreatorSession.FromScene(mloScene);
            var s = ui.Session;
            int mFiles = mloScene.Files.Count, lFiles = lightScene.Files.Count, ents = s.Entities.Count;
            var item = new MloAssetItem { Name = "prop_alpha_bench", FileName = "prop_alpha_bench.ydr", Path = a, IsYft = false, Source = MloAssetSource.PropFolder };
            lib.PlaceAt = 1;
            PlaceMloAssets_L3(ui, new[] { item }, 1);
            var lf = mloScene.Files.LastOrDefault(f => f.Path == a);
            check("mloassets: the model is in the MLO scene", lf != null && mloScene.Files.Count == mFiles + 1, $"{mFiles} -> {mloScene.Files.Count}");
            check("mloassets: an entity was added to the interior", s.Entities.Count == ents + 1 && s.Entities.Any(e => e.SourceFile == lf), $"{ents} -> {s.Entities.Count}");
            check("mloassets: nothing landed in the light scene", lightScene.Files.Count == lFiles && !lightScene.Files.Any(f => f.Path == a), $"light scene {lightScene.Files.Count} (was {lFiles})");
            check("mloassets: recent remembers it", lib.Recent.Any(r => r.Path == a), $"{lib.Recent.Count} recent");

            var oldName = s.Name; s.Name = "rle_l3_assets"; s.TextureDictionary = "rle_l3_assets";
            var ytyp = s.BuildYtyp("rle_l3_assets.ytyp");
            var mlo = ytyp.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
            bool named = mlo?.entities?.Any(en => en._Data.archetypeName.Hash == JenkHash.GenHash("prop_alpha_bench")) ?? false;
            check("mloassets: the placed asset is written as an entity", named, $"{mlo?.entities?.Length ?? 0} entities");
            check("mloassets: MloEditor.Validate passes with it", string.IsNullOrEmpty(MloEditor.Validate(ytyp)), MloEditor.Validate(ytyp) ?? "");
            s.Name = oldName;
        }
    }
}

