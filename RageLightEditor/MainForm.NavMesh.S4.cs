using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using CodeWalker.World;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {

        private bool navRightPicking_S4;

        public bool NavSelectByRightClick_S4 => !navRightPicking_S4;

        private YnvPoly[] navRightWasPolys_S4;
        private YnvPoint navRightWasPoint_S4;
        private YnvPortal navRightWasPortal_S4;
        private NavMeshEditor.NavDoc navRightWasActive_S4;
        private bool navRightArmed_S4;

        partial void NavMouseRightDown_S4(int x, int y)
        {
            if (panel == null || !panel.NavMode || NavEd == null || deviceResources == null) return;
            if (NavEd.PlacingPoly) return;

            navRightWasPolys_S4 = NavEd.SelectedPolys.ToArray();
            navRightWasPoint_S4 = NavEd.SelectedPoint;
            navRightWasPortal_S4 = NavEd.SelectedPortal;
            navRightWasActive_S4 = NavEd.Active;
            navRightArmed_S4 = true;

            bool handled = false;
            navRightPicking_S4 = true;
            try { NavMouseClick_P4(x, y, ref handled); }
            finally { navRightPicking_S4 = false; }
        }

        partial void NavMouseRightUp_S4(float dragPixels, bool scrubbed)
        {
            if (!navRightArmed_S4) return;
            navRightArmed_S4 = false;
            if (!scrubbed || dragPixels <= 2.0f || NavEd == null) { navRightWasPolys_S4 = null; return; }
            NavEd.ClearSelection();
            if (navRightWasPolys_S4 != null) NavEd.SelectedPolys.AddRange(navRightWasPolys_S4);
            NavEd.SelectedPoint = navRightWasPoint_S4;
            NavEd.SelectedPortal = navRightWasPortal_S4;
            if (navRightWasActive_S4 != null && NavEd.Docs.Contains(navRightWasActive_S4)) NavEd.Active = navRightWasActive_S4;
            navRightWasPolys_S4 = null;
        }

        private Vector3 navStreamPlannedAt_S4 = new Vector3(float.MaxValue);
        private float navStreamPlannedReach_S4 = -1.0f;
        private int navStreamPlannedCap_S4 = -1;
        private readonly List<RpfFileEntry> navStreamQueue_S4 = new List<RpfFileEntry>();
        private readonly List<Vector2> navStreamQueueAt_S4 = new List<Vector2>();
        private readonly Dictionary<string, Vector2> navStreamWanted_S4 = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private int navStreamOpened_S4, navStreamClosed_S4;

        private const int NavStreamPerFrame_S4 = 2;

        partial void NavStreamTick_S4()
        {
            if (panel == null || !panel.NavMode || NavEd == null) return;
            NavStreamHeadless_S4();
            NavEd.PruneStreamed_S4();
            if (!NavEd.FollowCamera_S4)
            {
                if (navStreamQueue_S4.Count > 0) { navStreamQueue_S4.Clear(); navStreamQueueAt_S4.Clear(); }
                NavEd.StreamStatus_S4 = "not following the camera";
                navStreamPlannedReach_S4 = -1.0f;
                return;
            }
            var sd = SpaceDataOrNull;
            if (sd == null) { NavEd.StreamStatus_S4 = "the game archives are not open"; return; }
            sd.EnsureNav();
            if (!sd.NavReady || sd.NavGrid == null) { NavEd.StreamStatus_S4 = "scanning the archives for nav meshes"; return; }
            var grid = sd.NavGrid;

            var pos = camera.Position;
            float reach = Math.Max(75.0f, NavEd.LoadRadius);
            int cap = Math.Max(1, NavEd.StreamCellCap_S4);
            bool moved = Vector3.DistanceSquared(new Vector3(pos.X, pos.Y, 0), new Vector3(navStreamPlannedAt_S4.X, navStreamPlannedAt_S4.Y, 0)) > (grid.CellSize / 3.0f) * (grid.CellSize / 3.0f);
            if (moved || Math.Abs(reach - navStreamPlannedReach_S4) > 0.5f || cap != navStreamPlannedCap_S4)
            {
                NavStreamPlan_S4(grid, pos, reach, cap);
                navStreamPlannedAt_S4 = pos;
                navStreamPlannedReach_S4 = reach;
                navStreamPlannedCap_S4 = cap;
            }

            int did = 0;
            while (navStreamQueue_S4.Count > 0 && did < NavStreamPerFrame_S4)
            {
                var entry = navStreamQueue_S4[0]; var at = navStreamQueueAt_S4[0];
                navStreamQueue_S4.RemoveAt(0); navStreamQueueAt_S4.RemoveAt(0);
                if (entry == null) continue;
                if (NavStreamHas_S4(entry.Name)) continue;
                if (NavStreamOpen_S4(entry, at)) { navStreamOpened_S4++; did++; }
            }

            foreach (var doc in NavEd.Docs)
            {
                if (NavEd.StreamedDocs_S4.ContainsKey(doc) || doc.FilePath != null || doc.Dirty) continue;
                if (navStreamWanted_S4.TryGetValue(doc.Name, out var centre)) NavEd.StreamedDocs_S4[doc] = centre;
            }

            float drop = reach * 1.35f + grid.CellSize;
            float drop2 = drop * drop;
            List<NavMeshEditor.NavDoc> evict = null;
            foreach (var kv in NavEd.StreamedDocs_S4)
            {
                bool wanted = navStreamWanted_S4.ContainsKey(kv.Key.Name);
                float dx = kv.Value.X - pos.X, dy = kv.Value.Y - pos.Y;
                if (wanted && dx * dx + dy * dy <= drop2) continue;
                (evict ??= new List<NavMeshEditor.NavDoc>()).Add(kv.Key);
            }
            if (evict != null)
                foreach (var d in evict) if (NavEd.CloseStreamed_S4(d)) navStreamClosed_S4++;

            NavEd.StreamStatus_S4 = navStreamQueue_S4.Count > 0
                ? $"loading - {navStreamQueue_S4.Count} cell(s) to go"
                : $"following the camera within {reach:0} m";
            NavRightClickHeadless_S4();
        }

        private bool NavStreamHas_S4(string name)
        {
            foreach (var d in NavEd.Docs) if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void NavStreamPlan_S4(SpaceNavGrid grid, Vector3 pos, float reach, int cap)
        {
            navStreamQueue_S4.Clear();
            navStreamQueueAt_S4.Clear();
            navStreamWanted_S4.Clear();

            int gr = Math.Min(40, Math.Max(1, (int)Math.Ceiling(reach / grid.CellSize)));
            var cp = grid.GetCellPos(pos);
            var found = new List<(float d2, RpfFileEntry e, Vector2 at, string name)>();
            float reach2 = reach * reach;
            for (int x = Math.Max(cp.X - gr, 0); x <= Math.Min(cp.X + gr, grid.CellCountX - 1); x++)
                for (int y = Math.Max(cp.Y - gr, 0); y <= Math.Min(cp.Y + gr, grid.CellCountY - 1); y++)
                {
                    var cell = grid.Cells[x, y];
                    var entry = cell?.YnvEntry;
                    if (entry == null) continue;
                    var cmin = grid.GetCellMin(cell); var cmax = grid.GetCellMax(cell);
                    float dx = Math.Max(Math.Max(cmin.X - pos.X, 0.0f), pos.X - cmax.X);
                    float dy = Math.Max(Math.Max(cmin.Y - pos.Y, 0.0f), pos.Y - cmax.Y);
                    float d2 = dx * dx + dy * dy;
                    if (d2 > reach2) continue;
                    found.Add((d2, entry, new Vector2((cmin.X + cmax.X) * 0.5f, (cmin.Y + cmax.Y) * 0.5f), entry.Name));
                }
            found.Sort((a, b) => a.d2.CompareTo(b.d2));
            int n = Math.Min(cap, found.Count);
            for (int i = 0; i < n; i++)
            {
                navStreamWanted_S4[found[i].name] = found[i].at;
                if (NavStreamHas_S4(found[i].name)) continue;
                navStreamQueue_S4.Add(found[i].e);
                navStreamQueueAt_S4.Add(found[i].at);
            }
        }

        private float NavLoadHereReach_S4(SpaceNavGrid grid, Vector3 pos, float reach)
        {
            if (NavEd == null || grid == null) return reach;
            int cap = Math.Max(1, NavEd.StreamCellCap_S4);
            int gr = Math.Min(40, Math.Max(1, (int)Math.Ceiling(reach / grid.CellSize)));
            var cp = grid.GetCellPos(pos);
            var d2s = new List<float>();
            float reach2 = reach * reach;
            for (int x = Math.Max(cp.X - gr, 0); x <= Math.Min(cp.X + gr, grid.CellCountX - 1); x++)
                for (int y = Math.Max(cp.Y - gr, 0); y <= Math.Min(cp.Y + gr, grid.CellCountY - 1); y++)
                {
                    var cell = grid.Cells[x, y];
                    if (cell?.YnvEntry == null) continue;
                    var cmin = grid.GetCellMin(cell); var cmax = grid.GetCellMax(cell);
                    float dx = Math.Max(Math.Max(cmin.X - pos.X, 0.0f), pos.X - cmax.X);
                    float dy = Math.Max(Math.Max(cmin.Y - pos.Y, 0.0f), pos.Y - cmax.Y);
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= reach2) d2s.Add(d2);
                }
            if (d2s.Count <= cap) return reach;
            d2s.Sort();
            return (float)Math.Sqrt(d2s[cap - 1]) + 0.01f;
        }

        private bool NavStreamOpen_S4(RpfFileEntry entry, Vector2 at)
        {
            try
            {
                var ynv = gameFiles?.Cache?.RpfMan?.GetFile<YnvFile>(entry);
                if (ynv == null) return false;
                if (string.IsNullOrEmpty(ynv.Name)) ynv.Name = entry.Name;
                return NavEd.AddStreamed_S4(ynv, entry.Path ?? "archives", at) != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("NAVSTREAM " + entry.Name + ": " + ex.Message);
                return false;
            }
        }

        private bool navStreamDumped_S4;
        private string navStreamLastDump_S4 = "";

        private void NavStreamHeadless_S4()
        {
            if (panel == null || !panel.NavMode || NavEd == null) return;
            var env = Environment.GetEnvironmentVariable("RLE_NAVSTREAM");
            if (string.IsNullOrEmpty(env)) return;
            if (!navStreamDumped_S4 && navStreamPlannedReach_S4 < 0.0f)
            {
                var bits = env.Split(',');
                if (bits.Length > 0 && float.TryParse(bits[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float r) && r > 0) NavEd.LoadRadius = r;
                if (bits.Length > 1 && int.TryParse(bits[1], out int c) && c > 0) NavEd.StreamCellCap_S4 = c;
                NavEd.FollowCamera_S4 = true;
            }
            if (navStreamQueue_S4.Count > 0 || navStreamPlannedReach_S4 < 0.0f) return;
            if (NavEd.Docs.Count == 0) return;
            string now = $"{NavEd.Docs.Count}/{navStreamOpened_S4}/{navStreamClosed_S4}";
            if (now == navStreamLastDump_S4) return;
            navStreamLastDump_S4 = now;
            navStreamDumped_S4 = true;
            Console.WriteLine($"NAVSTREAM live: {NavEd.Docs.Count} cell(s), {NavEd.TotalPolys:N0} polys, " +
                              $"{NavEd.StreamedCount_S4} streamed (reach {NavEd.LoadRadius:0} m, cap {NavEd.StreamCellCap_S4}); " +
                              $"opened {navStreamOpened_S4}, closed {navStreamClosed_S4}; cam {camera.Position.X:0},{camera.Position.Y:0}");
        }

        private bool navRightClickDone_S4;

        private void NavRightClickHeadless_S4()
        {
            if (navRightClickDone_S4 || NavEd == null || deviceResources == null) return;
            var env = Environment.GetEnvironmentVariable("RLE_NAVRCLICK");
            if (env == null || !worldBuilt) return;
            if (screenshotPath != null && worldWarmup < 380) return;
            navRightClickDone_S4 = true;

            int x = deviceResources.Width / 2, y = deviceResources.Height / 2;
            var bits = env.Split(',');
            if (bits.Length >= 2 && int.TryParse(bits[0].Trim(), out int px) && int.TryParse(bits[1].Trim(), out int py))
            { x = px; y = py; }

            NavMouseRightDown_S4(x, y);
            NavMouseRightUp_S4(0.0f, false);
            string what = NavEd.SelectedPoly != null
                ? $"poly {NavEd.SelectedPoly.Index} ({NavMeshEditor.CatNames[(int)NavEd.CategoryOf(NavEd.SelectedPoly)]}) of {NavEd.DocOf(NavEd.SelectedPoly.Ynv)?.Name}"
                : NavEd.SelectedPoint != null ? $"point {NavEd.SelectedPoint.Index}"
                : NavEd.SelectedPortal != null ? $"portal {NavEd.SelectedPortal.Index}" : "nothing";
            Console.WriteLine($"NAVRCLICK at {x},{y}: right button selected {what}; world = {WorldEdit.Selection.GetNameString("nothing")}");
        }

        private void SeqTest_S4(Action<string, bool, string> check)
        {
            StartupWorkspaceTest_S4(check);

            var was = panel.Workspace;
            panel.SwitchWorkspace(LightPanel.Space.NavMesh);

            check("a left click in the NavMesh workspace no longer selects", NavSelectByRightClick_S4, "");
            NavEd.ClearSelection();
            bool handled = false;
            NavMouseClick_P4(8, 8, ref handled);
            check("...but it still claims the click, so nothing in the world is picked instead",
                  handled && NavEd.SelectedPolys.Count == 0, handled ? "claimed" : "NOT claimed");

            navRightArmed_S4 = false;
            NavMouseRightDown_S4(8, 8);
            check("the right button is the one that picks now", navRightArmed_S4 && !navRightPicking_S4, "");

            navRightWasPolys_S4 = Array.Empty<YnvPoly>();
            navRightWasPoint_S4 = null; navRightWasPortal_S4 = null; navRightWasActive_S4 = null;
            navRightArmed_S4 = true;
            NavMouseRightUp_S4(9.0f, true);
            check("a right-drag that scrubbed the clock is not a selection", NavEd.SelectedPolys.Count == 0, "");

            var ynv = NavMeshEditor.NewFile(0, 0, new Vector3(-10, -10, -10), new Vector3(10, 10, 10));
            var doc = NavEd.AddStreamed_S4(ynv, "seqtest", new Vector2(0, 0));
            check("a streamed cell is opened without stealing the active file",
                  doc != null && NavEd.StreamedDocs_S4.ContainsKey(doc), doc == null ? "not added" : NavEd.Active?.Name ?? "none");
            var keepActive = NavEd.Active;
            NavEd.Active = doc;
            check("the streamer will not evict the file you are working in", !NavEd.CloseStreamed_S4(doc), "");
            NavEd.Active = ReferenceEquals(keepActive, doc) ? null : keepActive;
            doc.Dirty = true;
            check("...nor one with unsaved edits in it", !NavEd.CloseStreamed_S4(doc), "");
            doc.Dirty = false;
            check("but an untouched one out of reach is dropped", NavEd.CloseStreamed_S4(doc), "");
            check("...and the editor forgets it cleanly", !NavEd.Docs.Contains(doc) && !NavEd.StreamedDocs_S4.ContainsKey(doc), $"{NavEd.Docs.Count} open");

            check("the reach the panel offers goes far past one cell", NavEd.LoadRadius >= 150.0f, $"{NavEd.LoadRadius:0} m");
            check("and the streamer follows the camera by default", NavEd.FollowCamera_S4, "");

            if (was != LightPanel.Space.NavMesh && screenshotPath == null) panel.SwitchWorkspace(was);
        }
    }
}

