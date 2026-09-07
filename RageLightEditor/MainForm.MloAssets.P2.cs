using System;
using System.Collections.Generic;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int mloPageCooldown_P2;
        private int mloCatSourceKey_P2 = -1;
        private bool mloCatArchivesWere_P2;
        private int mloCatFolderKey_P2 = -1;

        private void ServiceMloAssets_P2(MloCreatorPanel ui)
        {
            var lib = ui?.Assets;
            if (lib == null) return;

            if (mloPageCooldown_P2 > 0) mloPageCooldown_P2--;
            if (lib.RequestMore_P2)
            {
                lib.RequestMore_P2 = false;
                if (mloPageCooldown_P2 == 0 && !lib.AllLoaded_P2 && lib.FetchLimit_P2 < MloAssetLibrary.MaxFetch_P2)
                {
                    lib.FetchLimit_P2 = Math.Min(lib.FetchLimit_P2 + MloAssetLibrary.PageSize_P2, MloAssetLibrary.MaxFetch_P2);
                    lib.Dirty = true;
                    mloPageCooldown_P2 = 5;
                }
            }

            int folderKey = lib.PropFolders.Count * 1000 + lib.ProjectFolders.Count + lib.FolderFiles + lib.ProjectFiles;
            int sourceKey = lib.SourceFilter * 8 + lib.KindFilter;
            if (!lib.CategoryCountsKnown_P2 || sourceKey != mloCatSourceKey_P2 ||
                lib.ArchivesReady != mloCatArchivesWere_P2 || folderKey != mloCatFolderKey_P2)
            {
                mloCatSourceKey_P2 = sourceKey;
                mloCatArchivesWere_P2 = lib.ArchivesReady;
                mloCatFolderKey_P2 = folderKey;
                CountAssetCategories_P2(lib);
            }
            ServiceMloAssetsDemo_P2(ui);
        }

        private void CountAssetCategories_P2(MloAssetLibrary lib)
        {
            Array.Clear(lib.CategoryCounts_P2, 0, lib.CategoryCounts_P2.Length);
            var exts = lib.KindFilter == 1 ? new[] { ".ydr" } : lib.KindFilter == 2 ? new[] { ".yft" } : new[] { ".ydr", ".yft" };
            bool wantArchive = lib.SourceFilter == 0 || lib.SourceFilter == 1;
            bool wantFolders = lib.SourceFilter == 0 || lib.SourceFilter == 2;
            bool wantProject = lib.SourceFilter == 0 || lib.SourceFilter == 3;
            bool wantLightProps = lib.SourceFilter == 0 || lib.SourceFilter == 4;
            int total = 0;

            if (wantArchive && lib.ArchivesReady && panel.Archive != null)
            {
                var hits = new List<ArchiveBrowser.Entry>();
                panel.Archive.Find("", exts, hits, MloAssetLibrary.MaxFetch_P2 * 4);
                foreach (var h in hits)
                {
                    var n = h.File?.NameLower ?? "";
                    int dot = n.LastIndexOf('.');
                    if (dot > 0) n = n.Substring(0, dot);
                    lib.CategoryCounts_P2[MloAssetCategories_P2.Of(n, h.Path)]++;
                    total++;
                }
            }
            var disk = new List<MloAssetItem>();
            if (wantProject) MloAssetLibrary.ScanFolders(lib.ProjectFolders, "", MloAssetSource.Project, disk, MloAssetLibrary.MaxFetch_P2);
            if (wantFolders) MloAssetLibrary.ScanFolders(lib.PropFolders, "", MloAssetSource.PropFolder, disk, MloAssetLibrary.MaxFetch_P2);
            if (wantLightProps && lightPropLibrary != null)
                MloAssetLibrary.FromLightProps(lightPropLibrary.Search(""), disk, MloAssetLibrary.MaxFetch_P2);
            foreach (var it in disk)
            {
                lib.CategoryCounts_P2[MloAssetCategories_P2.Of(it.Name, it.Path)]++;
                total++;
            }
            lib.CategoryCounts_P2[0] = total;
            lib.CategoryCountsKnown_P2 = true;
        }

        private int SearchArchiveByCategory_P2(MloAssetLibrary lib, IReadOnlyList<string> exts, List<MloAssetItem> into, int max)
        {
            if (panel.Archive == null || !panel.Archive.Ready) return 0;
            var hits = new List<ArchiveBrowser.Entry>();
            int total = panel.Archive.FindByCategory_P2(lib.Query, exts, lib.Category_P2, MloAssetCategories_P2.Of, hits, max);
            MloAssetLibrary.FromArchive(hits, into, into.Count + max);
            return total;
        }

        private bool mloAssetDemoDone_P2;

        private void ServiceMloAssetsDemo_P2(MloCreatorPanel ui)
        {
            if (mloAssetDemoDone_P2) return;
            var lib = ui.Assets;
            string cat = Environment.GetEnvironmentVariable("RLE_MLOCAT");
            string scroll = Environment.GetEnvironmentVariable("RLE_MLOSCROLL");
            if (string.IsNullOrEmpty(cat) && string.IsNullOrEmpty(scroll)) return;
            if (!lib.ArchivesReady || !lib.CategoryCountsKnown_P2) return;
            mloAssetDemoDone_P2 = true;

            Console.WriteLine("MLOCAT counts: " + string.Join(", ",
                Enumerable.Range(0, MloAssetCategories_P2.Count).Select(i => $"{MloAssetCategories_P2.Names[i]}={lib.CategoryCounts_P2[i]:N0}")));

            if (!string.IsNullOrEmpty(cat))
            {
                int want = -1;
                if (int.TryParse(cat, out int ci)) want = ci;
                else
                    for (int i = 0; i < MloAssetCategories_P2.Count; i++)
                        if (MloAssetCategories_P2.Names[i].StartsWith(cat, StringComparison.OrdinalIgnoreCase)) { want = i; break; }
                if (want >= 0 && want < MloAssetCategories_P2.Count)
                {
                    lib.Category_P2 = want;
                    lib.ResetPaging_P2();
                    RunMloAssetSearch_L3(lib);
                    Console.WriteLine($"MLOCAT '{MloAssetCategories_P2.Names[want]}': {lib.Results.Count:N0} shown of {lib.ResultTotal:N0}; " +
                                      $"first {string.Join(", ", lib.Results.Take(6).Select(r => r.Name))}");
                }
            }
            if (!string.IsNullOrEmpty(scroll) && int.TryParse(scroll, out int pages))
            {
                for (int p = 0; p < pages && !lib.AllLoaded_P2; p++)
                {
                    lib.RequestMore_P2 = true;
                    mloPageCooldown_P2 = 0;
                    ServiceMloAssets_P2(ui);
                    RunMloAssetSearch_L3(lib);
                    lib.Dirty = false;
                }
                Console.WriteLine($"MLOSCROLL after {pages} page(s): {lib.Results.Count:N0} results (window {lib.FetchLimit_P2:N0}, total {lib.ResultTotal:N0}, allLoaded {lib.AllLoaded_P2}); " +
                                  $"past the old 400 cap = {lib.Results.Count > 400}");
            }
            ui.ShowPage(MloCreatorPanel.PageKind.Assets);
            ui.WindowVisible = Environment.GetEnvironmentVariable("RLE_MLOWIN") != "0";
            var size = Environment.GetEnvironmentVariable("RLE_MLOWINSIZE");
            if (!string.IsNullOrEmpty(size))
            {
                var p = size.Split(',');
                if (p.Length >= 2 && float.TryParse(p[0], out float w) && float.TryParse(p[1], out float h))
                    ui.WindowSizeOverride_P2 = new System.Numerics.Vector2(w, h);
            }
            screenshotFrames = Math.Max(screenshotFrames, 90);
        }

        private void MloAssetCategoryTest_P2(Action<string, bool, string> check)
        {
            try
            {
                void Cat(string name, string want, string path = "")
                {
                    int got = MloAssetCategories_P2.Of(name, path);
                    check($"mloP2cat: '{name}' -> {want}", MloAssetCategories_P2.Names[got] == want,
                          $"got {MloAssetCategories_P2.Names[got]}");
                }
                Cat("prop_light_hang_01", "Lights");
                Cat("v_ilev_lightshade", "Lights");
                Cat("prop_chandelier_01", "Lights");
                Cat("prop_cigar_lighter", "Misc");
                Cat("prop_lighthouse_01", "Misc");
                Cat("prop_tv_flat_01", "Electronics");
                Cat("v_res_tt_computer", "Electronics");
                Cat("prop_micro_01", "Misc");
                Cat("prop_microwave_1", "Kitchen & Bathroom");
                Cat("prop_food_bag1", "Food & Drink");
                Cat("prop_beer_bottle", "Food & Drink");
                Cat("prop_table_para_01", "Desks & Tables");
                Cat("prop_off_desk_01", "Desks & Tables");
                Cat("v_ilev_chair02", "Seating");
                Cat("prop_bench_05", "Seating");
                Cat("prop_shelves_01", "Storage & Shelves");
                Cat("v_ilev_locker", "Storage & Shelves");
                Cat("prop_door_01", "Doors & Windows");
                Cat("v_ilev_window_01", "Doors & Windows");
                Cat("prop_toilet_01", "Kitchen & Bathroom");
                Cat("prop_paper_01", "Office");
                Cat("prop_wallart_01", "Office");
                Cat("prop_elecbox_01", "Industrial");
                Cat("prop_sign_road_01", "Signs & Decals");
                Cat("prop_tree_oak_01", "Vegetation");
                Cat("prop_pot_plant_02a", "Vegetation");
                Cat("prop_wheel_01", "Vehicles & Parts");
                Cat("zzz_unknown_thing", "Misc");
                Cat("hei_prop_xx_1", "Vegetation", "x64/levels/gta5/props/vegetation/v_trees.rpf/x.ydr");
                Cat("someblob", "Vehicles & Parts", "x64e.rpf/vehicles.rpf/adder.yft");

                check("mloP2cat: the picker has All ... Misc", MloAssetCategories_P2.Names.Length == MloAssetCategories_P2.Count &&
                      MloAssetCategories_P2.Names[0] == "All" && MloAssetCategories_P2.Names[MloAssetCategories_P2.Misc] == "Misc",
                      string.Join(" / ", MloAssetCategories_P2.Names));

                var lib = Creator?.Assets ?? new MloAssetLibrary();
                int cat0 = lib.Category_P2, fetch0 = lib.FetchLimit_P2;
                lib.Category_P2 = 0;
                lib.ResetPaging_P2();
                check("mloP2page: a fresh list is one page", lib.FetchLimit_P2 == MloAssetLibrary.PageSize_P2, $"{lib.FetchLimit_P2}");
                check("mloP2page: with no category the fetch is the page", lib.OverFetch_P2(300) == 300, $"{lib.OverFetch_P2(300)}");
                lib.Category_P2 = 1;
                check("mloP2page: a category over-fetches, capped", lib.OverFetch_P2(300) == 4800 && lib.OverFetch_P2(100000) == MloAssetLibrary.MaxFetch_P2,
                      $"{lib.OverFetch_P2(300)} / {lib.OverFetch_P2(100000)}");
                var items = new List<MloAssetItem>
                {
                    new MloAssetItem { Name = "prop_light_a", Path = "a" },
                    new MloAssetItem { Name = "prop_chair_a", Path = "b" },
                    new MloAssetItem { Name = "prop_lamp_a", Path = "c" },
                    new MloAssetItem { Name = "prop_lamp_b", Path = "d" },
                };
                int m = lib.ApplyCategory_P2(items, 2);
                check("mloP2page: the shelf filter keeps its own and pages them", m == 3 && items.Count == 2 && items.All(x => x.Name.Contains("light") || x.Name.Contains("lamp")),
                      $"{m} matched, {items.Count} shown");
                lib.Category_P2 = cat0; lib.FetchLimit_P2 = fetch0; lib.Dirty = true;

                var fresh = new AppSettings();
                check("mloP2view: a fresh install opens on Large icons", MloCreatorPanel.AssetViewFor_P2(fresh) == 1, $"{MloCreatorPanel.AssetViewFor_P2(fresh)}");
                var stale = new AppSettings { MloAssetView = 0, MloAssetViewChosen_P2 = false };
                check("mloP2view: an old settings.json does not hold it back", MloCreatorPanel.AssetViewFor_P2(stale) == 1, $"{MloCreatorPanel.AssetViewFor_P2(stale)}");
                var chose = new AppSettings { MloAssetView = 0, MloAssetViewChosen_P2 = true };
                check("mloP2view: a deliberate switch to the list is kept", MloCreatorPanel.AssetViewFor_P2(chose) == 0, $"{MloCreatorPanel.AssetViewFor_P2(chose)}");
            }
            catch (Exception ex)
            {
                check("mloP2cat: no exception", false, ex.ToString());
            }
        }
    }
}

