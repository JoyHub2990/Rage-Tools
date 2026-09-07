using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool navCellsBuilt_R3;
        private bool navAutoLoaded_R3;
        private bool navWasIn_R3;
        private bool navCellsDumped_R3, navCellOpenDone_R3;

        private void NavCellsTick_R3()
        {
            if (panel == null) return;
            var nav = NavEd;

            bool inNav = panel.NavMode;
            if (inNav && !navWasIn_R3) NavEnterWorkspace_R3();
            navWasIn_R3 = inNav;
            if (!inNav) return;

            nav.CellCameraPos = camera.Position;

            if (nav.RequestRescanCells) { nav.RequestRescanCells = false; navCellsBuilt_R3 = false; }
            if (!navCellsBuilt_R3) BuildNavCells_R3();

            if (nav.RequestOpenCell != null) { var c = nav.RequestOpenCell; nav.RequestOpenCell = null; NavOpenCell_R3(c); }
            if (nav.RequestGoToCell != null) { var c = nav.RequestGoToCell; nav.RequestGoToCell = null; NavGoToCell_R3(c); }

            if (!navAutoLoaded_R3 && nav.CellsReady && nav.Cells.Count > 0)
            {
                navAutoLoaded_R3 = true;
                if (nav.Docs.Count == 0) NavAutoLoadAroundCamera_R3();
            }

            NavCellsHeadless_R3();
        }

        private void NavEnterWorkspace_R3()
        {
            var sd = SpaceDataOrNull;
            if (sd == null)
            {
                NavEd.CellsStatus = "the game archives are not open";
                return;
            }
            sd.EnsureNav();
            if (!sd.NavReady) NavEd.CellsStatus = "scanning the archives...";
            if (NavEd.Docs.Count == 0) navAutoLoaded_R3 = false;
        }

        private void BuildNavCells_R3()
        {
            var nav = NavEd;
            var sd = SpaceDataOrNull;
            if (sd == null) { nav.CellsStatus = "the game archives are not open"; return; }
            sd.EnsureNav();
            if (!sd.NavReady)
            {
                nav.CellsStatus = sd.NavLoading ? "scanning the archives..." : "waiting for the archives";
                return;
            }
            var grid = sd.NavGrid;
            if (grid == null) { nav.CellsStatus = "no nav grid in this install"; return; }

            nav.Cells.Clear();
            for (int x = 0; x < grid.CellCountX; x++)
                for (int y = 0; y < grid.CellCountY; y++)
                {
                    var cell = grid.Cells[x, y];
                    var entry = cell?.YnvEntry;
                    if (entry == null) continue;
                    var min = grid.GetCellMin(cell);
                    var max = grid.GetCellMax(cell);
                    nav.Cells.Add(new NavMeshEditor.NavCell
                    {
                        Name = entry.Name,
                        NameLower = (entry.NameLower ?? entry.Name ?? "").ToLowerInvariant(),
                        FileX = cell.FileX,
                        FileY = cell.FileY,
                        GridX = cell.X,
                        GridY = cell.Y,
                        Min = min,
                        Max = max,
                        Centre = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, 0.0f),
                        Entry = entry,
                        ArchivePath = entry.Path ?? "",
                    });
                }

            navCellsBuilt_R3 = true;
            nav.CellsReady = true;
            nav.CellsStatus = nav.Cells.Count > 0
                ? $"{nav.Cells.Count:N0} cells"
                : "this install ships no nav meshes";
            Console.WriteLine($"NAVCELLS index built: {nav.Cells.Count:N0} .ynv cells " +
                              $"({sd.NavCellCount:N0} on the grid, {sd.NavLoadMs:0} ms scan)");
        }

        private void NavOpenCell_R3(NavMeshEditor.NavCell c)
        {
            if (c?.Entry == null) return;
            var nav = NavEd;
            var already = nav.DocOfCell(c);
            if (already != null)
            {
                nav.Active = already;
                nav.Status = already.PolyCount > 0
                    ? $"{already.Name} is already open - {already.PolyCount:N0} polys (now the active file)"
                    : $"{already.Name} is already open, and it has NO POLYGONS - the game ships this cell empty";
                return;
            }

            var doc = NavOpenEntry_P4(c.Entry);
            if (doc == null) return;
            c.PolyCount = doc.PolyCount;
            nav.Active = doc;

            float dist = nav.CellDistance(c);
            if (doc.PolyCount == 0)
                nav.Status = $"{doc.Name} has NO POLYGONS - the game ships this cell empty. " +
                             "Pick another from the list, or draw / generate into this one.";
            else if (dist > 150.0f)
                nav.Status = $"{doc.Name}: {doc.PolyCount:N0} polys, {dist:N0} m away - press Go to see it";
            else
                nav.Status = $"{doc.Name}: {doc.PolyCount:N0} polys, {doc.PointCount} points, {doc.PortalCount} portals";
            Console.WriteLine($"NAVCELLS opened {doc.Name} polys={doc.PolyCount} dist={dist:0} m");
        }

        private void NavGoToCell_R3(NavMeshEditor.NavCell c)
        {
            if (c == null) return;
            float z = 40.0f;
            var doc = NavEd.DocOfCell(c);
            if (doc != null && doc.PolyCount > 0 && doc.Bounds.Minimum.Z <= doc.Bounds.Maximum.Z)
                z = (doc.Bounds.Minimum.Z + doc.Bounds.Maximum.Z) * 0.5f;

            camera.Target = new Vector3(c.Centre.X, c.Centre.Y, z);
            camera.Distance = 220.0f;
            camera.Pitch = 0.95f;
            camera.MaxDistance = Math.Max(camera.MaxDistance, 2000.0f);
            camera.SnapSmoothing();
            camera.Update();
            NavEd.CellCameraPos = camera.Position;
            NavEd.Status = $"went to {c.Name} at {c.Centre.X:0}, {c.Centre.Y:0}" +
                           (doc == null ? " (not open yet - click the row to open it)" : "");
            Console.WriteLine($"NAVCELLS goto {c.Name} at {c.Centre.X:0},{c.Centre.Y:0},{z:0}");
        }

        private void NavAutoLoadAroundCamera_R3()
        {
            var nav = NavEd;
            nav.RequestLoadAroundCamera = false;
            NavLoadAroundCamera_P4();
            foreach (var c in nav.Cells)
            {
                var d = nav.DocOfCell(c);
                if (d != null) c.PolyCount = d.PolyCount;
            }
            if (nav.Docs.Count > 0)
            {
                Console.WriteLine($"NAVCELLS auto-opened {nav.Docs.Count} cell(s) around the camera - {nav.Status}");
                return;
            }
            NavMeshEditor.NavCell near = null;
            float best = float.MaxValue;
            foreach (var c in nav.Cells)
            {
                float d = nav.CellDistance(c);
                if (d < best) { best = d; near = c; }
            }
            nav.Status = near == null
                ? "this install ships no nav meshes"
                : $"no nav mesh where you are standing - the nearest is {near.Name}, {best:N0} m away " +
                  "(click it in the list, then Go)";
            Console.WriteLine("NAVCELLS " + nav.Status);
        }

        private void NavCellsHeadless_R3()
        {
            var nav = NavEd;

            if (!navCellsDumped_R3 && nav.CellsReady &&
                Environment.GetEnvironmentVariable("RLE_NAVCELLS") == "1")
            {
                navCellsDumped_R3 = true;
                Console.WriteLine(NavCellsLine_R3());
                int n = 0;
                int total;
                foreach (var c in nav.FilteredCells(8, out total))
                {
                    var d = nav.DocOfCell(c);
                    Console.WriteLine($"  NAVCELL {++n,2} {c.Name,-24} cell {c.FileX,3},{c.FileY,3} " +
                                      $"centre {c.Centre.X,7:0},{c.Centre.Y,7:0} {nav.CellDistance(c),7:0} m " +
                                      $"{(d == null ? "shut" : d.PolyCount > 0 ? d.PolyCount.ToString("N0") + " polys" : "EMPTY")}");
                }
            }

            if (!navCellOpenDone_R3 && nav.CellsReady)
            {
                var want = Environment.GetEnvironmentVariable("RLE_NAVCELLOPEN");
                if (!string.IsNullOrWhiteSpace(want))
                {
                    navCellOpenDone_R3 = true;
                    nav.CellFilter = want.Trim();
                    int total;
                    NavMeshEditor.NavCell first = null;
                    foreach (var c in nav.FilteredCells(1, out total)) { first = c; break; }
                    nav.CellFilter = "";
                    if (first == null) Console.WriteLine($"NAVCELLS open '{want}': no cell matches");
                    else
                    {
                        nav.SelectedCell = first;
                        NavOpenCell_R3(first);
                        NavGoToCell_R3(first);
                        var d = nav.DocOfCell(first);
                        Console.WriteLine($"NAVCELLS open '{want}' -> {first.Name} polys={d?.PolyCount ?? -1} " +
                                          $"camera {camera.Position.X:0},{camera.Position.Y:0},{camera.Position.Z:0}");
                    }
                }
            }
        }

        private string NavCellsLine_R3()
        {
            var nav = NavEd;
            int empty = 0, opened = 0;
            foreach (var d in nav.Docs) { opened++; if (d.PolyCount == 0) empty++; }
            return $"NAVCELLS ready={nav.CellsReady} cells={nav.Cells.Count} " +
                   $"open={opened} empty={empty} polys={nav.TotalPolys} " +
                   $"camera={camera.Position.X:0},{camera.Position.Y:0},{camera.Position.Z:0} status=\"{nav.Status}\"";
        }

        private void NavCellsTest_R3(Action<string, bool, string> check)
        {
            var nav = NavEd;

            var saveCells = new List<NavMeshEditor.NavCell>(nav.Cells);
            var saveFilter = nav.CellFilter;
            bool saveNear = nav.CellsNearestFirst, saveOpen = nav.CellsOpenOnly;
            var savePos = nav.CellCameraPos;
            nav.Cells.Clear();
            for (int i = 0; i < 6; i++)
            {
                float x = -6000.0f + i * 150.0f;
                nav.Cells.Add(new NavMeshEditor.NavCell
                {
                    Name = $"navmesh[{i * 3}][{12}].ynv",
                    NameLower = $"navmesh[{i * 3}][{12}].ynv",
                    FileX = i * 3, FileY = 12,
                    Min = new Vector3(x, 0, 0), Max = new Vector3(x + 150.0f, 150.0f, 0),
                    Centre = new Vector3(x + 75.0f, 75.0f, 0),
                });
            }
            nav.CellsOpenOnly = false;
            nav.CellsNearestFirst = true;
            nav.CellCameraPos = new Vector3(-6000.0f + 4 * 150.0f + 75.0f, 75.0f, 0);

            int total;
            nav.CellFilter = "";
            var all = new List<NavMeshEditor.NavCell>(nav.FilteredCells(100, out total));
            bool nearest = all.Count == 6 && total == 6 && all[0].FileX == 12;

            nav.CellFilter = "navmesh[9]";
            var byName = new List<NavMeshEditor.NavCell>(nav.FilteredCells(100, out total));
            bool nameOk = byName.Count == 1 && byName[0].FileX == 9;

            nav.CellFilter = "6,12";
            var byGrid = new List<NavMeshEditor.NavCell>(nav.FilteredCells(100, out total));
            bool gridOk = byGrid.Count == 1 && byGrid[0].FileX == 6;

            float wx = -6000.0f + 2 * 150.0f + 10.0f;
            nav.CellFilter = $"{wx:0.0},75";
            var byWorld = new List<NavMeshEditor.NavCell>(nav.FilteredCells(100, out total));
            bool worldOk = byWorld.Count == 1 && byWorld[0].FileX == 6;

            check("navmesh list searches by name, cell and world position",
                  nearest && nameOk && gridOk && worldOk,
                  $"all={all.Count} nearestFirst={(all.Count > 0 ? all[0].Name : "-")} " +
                  $"name={byName.Count} grid={byGrid.Count} world={byWorld.Count}");

            check("an unopened cell states no polygon count",
                  nav.Cells[0].PolyCount == -1 && !nav.CellIsOpen(nav.Cells[0]),
                  "PolyCount = -1 until the file is parsed");

            bool inside = nav.CellDistance(nav.Cells[4]) < 0.01f;
            bool outside = nav.CellDistance(nav.Cells[0]) > 500.0f;
            check("navmesh list measures to the cell, not to its centre", inside && outside,
                  $"inside={nav.CellDistance(nav.Cells[4]):0.0} m, four cells away={nav.CellDistance(nav.Cells[0]):0} m");

            nav.Cells.Clear();
            nav.Cells.AddRange(saveCells);
            nav.CellFilter = saveFilter;
            nav.CellsNearestFirst = saveNear;
            nav.CellsOpenOnly = saveOpen;
            nav.CellCameraPos = savePos;

            if (gameFiles != null && SpaceDataOrNull != null)
            {
                var sd = SpaceDataOrNull;
                sd.EnsureNav();
                var waited = System.Diagnostics.Stopwatch.StartNew();
                while (!sd.NavReady && waited.Elapsed.TotalSeconds < 60.0) System.Threading.Thread.Sleep(50);
                navCellsBuilt_R3 = false;
                BuildNavCells_R3();
                bool built = nav.CellsReady;
                bool many = nav.Cells.Count > 100;
                bool named = nav.Cells.Count == 0 ||
                             nav.Cells[0].Name.StartsWith("navmesh[", StringComparison.OrdinalIgnoreCase);
                check("navmesh list is filled from the install",
                      built && many && named,
                      $"{nav.Cells.Count:N0} cells in {waited.Elapsed.TotalSeconds:0.0} s, " +
                      $"first={(nav.Cells.Count > 0 ? nav.Cells[0].Name : "-")}, status=\"{nav.CellsStatus}\"");
            }

            check("navmesh list reports itself", NavCellsLine_R3().StartsWith("NAVCELLS") &&
                                                 NavCellsLine_R3().Contains("cells="),
                  NavCellsLine_R3());
        }
    }
}

