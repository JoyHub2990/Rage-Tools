using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public readonly NavMeshEditor NavEd = new NavMeshEditor();

        private NavMeshRenderer navRenderer;
        private bool navWired;
        private string navLastSaveDir;
        private NavMeshEditor.NavDoc navDragDoc;
        private NavMeshEditor.NavDoc navCloseArmed;
        private bool navEnvOpened, navEnvEdited, navEnvSaved, navEnvLayers;
        private int navEnvFrame;

        private void ApplyNavSpaceFlag_P4()
        {
            if (panel != null && panel.Nav == null) { panel.Nav = NavEd; navWired = true; }
            if (Environment.GetEnvironmentVariable("RLE_NAVSPACE") != "1") return;
            panel.Workspace = LightPanel.Space.NavMesh;
            panel.ApplyThemeFromSettings(false);
            panel.ShowNavMeshes = false;
        }

        private void OnWorldTick_Nav_P4()
        {
            if (panel == null) return;
            if (!navWired) { navWired = true; panel.Nav = NavEd; }
            NavWorkspaceGuard_Q2();
            NavCellsTick_R3();
            if (!panel.NavMode) return;
            var nav = NavEd;

            if (nav.RequestOpenFile) { nav.RequestOpenFile = false; NavOpenDialog_P4(); }
            if (nav.RequestOpenByName != null) { var n = nav.RequestOpenByName; nav.RequestOpenByName = null; NavOpenByName_P4(n); }
            if (nav.RequestLoadAroundCamera) { nav.RequestLoadAroundCamera = false; NavLoadAroundCamera_P4(); }
            if (nav.RequestNewFile) { nav.RequestNewFile = false; NavNewFileHere_P4(); }
            if (nav.RequestCloseActive) { nav.RequestCloseActive = false; NavCloseActive_P4(); }
            if (nav.RequestSave) { nav.RequestSave = false; NavSave_P4(false); }
            if (nav.RequestSaveAs) { nav.RequestSaveAs = false; NavSave_P4(true); }
            if (nav.RequestAddToProject) { nav.RequestAddToProject = false; NavAddToProject_P4(); }
            if (nav.RequestClosePoly) { nav.RequestClosePoly = false; NavClosePendingPoly_P4(); }
            if (nav.RequestDeleteSelection) { nav.RequestDeleteSelection = false; NavDeleteSelection_P4(); }
            if (nav.RequestAddPoint) { nav.RequestAddPoint = false; NavAddNodeAtCrosshair_P4(false); }
            if (nav.RequestAddPortal) { nav.RequestAddPortal = false; NavAddNodeAtCrosshair_P4(true); }
            if (nav.RequestGenerate) { nav.RequestGenerate = false; NavGenerate_P4(); }
            if (nav.RequestFrameSelection) { nav.RequestFrameSelection = false; NavFrameSelection_P4(); }

            if (navDragDoc != null && !worldGizmo.Dragging)
            {
                NavMeshEditor.RecomputeBounds(navDragDoc);
                navDragDoc = null;
            }

            NavStreamTick_S4();
            NavHeadless_P4();
            NavHeadlessClick_Q2();
        }

        private void NavOpenDialog_P4()
        {
            using var dlg = new OpenFileDialog { Filter = "Nav meshes|*.ynv|All files|*.*", Multiselect = true };
            var dir = navLastSaveDir ?? ProjWin?.Project?.Directory;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in dlg.FileNames) NavOpenPath_P4(f);
        }

        public NavMeshEditor.NavDoc NavOpenPath_P4(string path)
        {
            try
            {
                var ynv = new YnvFile();
                ynv.Load(File.ReadAllBytes(path));
                ynv.Name = Path.GetFileName(path);
                ynv.FilePath = path;
                var doc = NavEd.Add(ynv, path, "disk");
                navLastSaveDir = Path.GetDirectoryName(path);
                NavEd.Status = $"{doc.Name}: {doc.PolyCount:N0} polys, {doc.PointCount} points, {doc.PortalCount} portals";
                Console.WriteLine("NAV opened " + path + " - " + NavEd.Status);
                return doc;
            }
            catch (Exception ex)
            {
                NavEd.Status = "open failed: " + ex.Message;
                Console.WriteLine("NAV open failed " + path + ": " + ex.Message);
                return null;
            }
        }

        public NavMeshEditor.NavDoc NavOpenByName_P4(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            if (name.Contains('\\') || name.Contains('/'))
            {
                var e = gameFiles?.Cache?.RpfMan?.GetEntry(name.Replace('/', '\\')) as RpfFileEntry;
                if (e != null) return NavOpenEntry_P4(e);
            }
            var parts = name.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int cx) && int.TryParse(parts[1].Trim(), out int cy))
                name = $"navmesh[{cx}][{cy}]";
            if (!name.EndsWith(".ynv", StringComparison.OrdinalIgnoreCase)) name += ".ynv";

            var entry = NavFindEntry_P4(name);
            if (entry == null)
            {
                NavEd.Status = name + " is not in this install";
                Console.WriteLine("NAV not found: " + name);
                return null;
            }
            return NavOpenEntry_P4(entry);
        }

        private RpfFileEntry NavFindEntry_P4(string fileName)
        {
            var sd = SpaceDataOrNull;
            if (sd != null)
            {
                sd.EnsureNav();
                var grid = sd.NavGrid;
                if (grid != null)
                {
                    for (int x = 0; x < grid.CellCountX; x++)
                        for (int y = 0; y < grid.CellCountY; y++)
                        {
                            var e = grid.Cells[x, y]?.YnvEntry;
                            if (e != null && string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase)) return e;
                        }
                }
            }
            var hits = new List<ArchiveBrowser.Entry>();
            panel?.Archive?.Find(Path.GetFileNameWithoutExtension(fileName), null, hits, 64);
            foreach (var h in hits)
                if (string.Equals(h.File?.Name, fileName, StringComparison.OrdinalIgnoreCase)) return h.File;
            return null;
        }

        public NavMeshEditor.NavDoc NavOpenEntry_P4(RpfFileEntry entry)
        {
            if (entry == null) return null;
            try
            {
                var ynv = gameFiles?.Cache?.RpfMan?.GetFile<YnvFile>(entry);
                if (ynv == null) { NavEd.Status = "could not read " + entry.Name; return null; }
                if (string.IsNullOrEmpty(ynv.Name)) ynv.Name = entry.Name;
                var doc = NavEd.Add(ynv, null, entry.Path ?? "archives");
                NavEd.Status = $"{doc.Name}: {doc.PolyCount:N0} polys, {doc.PointCount} points, {doc.PortalCount} portals (from the archives - Save As to write a copy)";
                Console.WriteLine($"NAV opened {entry.Name} from {entry.Path} - {doc.PolyCount} polys");
                return doc;
            }
            catch (Exception ex)
            {
                NavEd.Status = "open failed: " + ex.Message;
                Console.WriteLine("NAV open failed " + entry.Name + ": " + ex.Message);
                return null;
            }
        }

        private void NavLoadAroundCamera_P4()
        {
            var sd = SpaceDataOrNull;
            if (sd == null) { NavEd.Status = "the game archives are not open"; return; }
            sd.EnsureNav();
            if (!sd.NavReady) { NavEd.Status = "scanning the archives for nav meshes - try again in a moment"; return; }
            var grid = sd.NavGrid;
            if (grid == null) { NavEd.Status = "no nav grid"; return; }

            var pos = camera.Position;
            float range = NavEd.LoadRadius;
            range = NavLoadHereReach_S4(grid, pos, range);
            int gr = Math.Max(1, (int)Math.Ceiling(range / grid.CellSize));
            var cp = grid.GetCellPos(pos);
            int opened = 0, already = 0;
            for (int x = Math.Max(cp.X - gr, 0); x <= Math.Min(cp.X + gr, grid.CellCountX - 1); x++)
                for (int y = Math.Max(cp.Y - gr, 0); y <= Math.Min(cp.Y + gr, grid.CellCountY - 1); y++)
                {
                    var cell = grid.Cells[x, y];
                    var entry = cell?.YnvEntry;
                    if (entry == null) continue;
                    var cmin = grid.GetCellMin(cell); var cmax = grid.GetCellMax(cell);
                    float dx = Math.Max(Math.Max(cmin.X - pos.X, 0.0f), pos.X - cmax.X);
                    float dy = Math.Max(Math.Max(cmin.Y - pos.Y, 0.0f), pos.Y - cmax.Y);
                    if (dx * dx + dy * dy > range * range) continue;
                    if (NavEd.Docs.Any(d => string.Equals(d.Name, entry.Name, StringComparison.OrdinalIgnoreCase))) { already++; continue; }
                    if (NavOpenEntry_P4(entry) != null) opened++;
                }
            NavEd.Status = opened > 0
                ? $"{opened} cell(s) opened around {pos.X:0}, {pos.Y:0}" + (already > 0 ? $" ({already} already open)" : "")
                : already > 0 ? $"the {already} cell(s) within {range:0} m are already open"
                : $"no nav mesh within {range:0} m of {pos.X:0}, {pos.Y:0}";
            Console.WriteLine("NAV " + NavEd.Status);
        }

        private void NavNewFileHere_P4()
        {
            var sd = SpaceDataOrNull;
            var grid = sd?.NavGrid;
            var pos = camera.Position;
            Vector3 cmin, cmax; int fx, fy;
            if (grid != null)
            {
                var cp = grid.GetCellPos(pos);
                var cell = grid.Cells[cp.X, cp.Y];
                cmin = grid.GetCellMin(cell); cmax = grid.GetCellMax(cell);
                fx = cell.FileX; fy = cell.FileY;
                cmin.Z = -200.0f; cmax.Z = 1200.0f;
            }
            else
            {
                const float size = 150.0f, corner = -6000.0f;
                int gx = (int)Math.Floor((pos.X - corner) / size), gy = (int)Math.Floor((pos.Y - corner) / size);
                cmin = new Vector3(corner + gx * size, corner + gy * size, -200.0f);
                cmax = new Vector3(cmin.X + size, cmin.Y + size, 1200.0f);
                fx = gx; fy = gy;
            }
            var ynv = NavMeshEditor.NewFile(fx, fy, cmin, cmax);
            var doc = NavEd.Add(ynv, null, "new");
            NavEd.Status = $"{doc.Name} created (empty) - draw or generate polygons, then Save As";
            Console.WriteLine("NAV new file " + doc.Name);
        }

        private void NavCloseActive_P4()
        {
            var doc = NavEd.Active;
            if (doc == null) return;
            if (doc.Dirty && !ReferenceEquals(navCloseArmed, doc))
            {
                navCloseArmed = doc;
                NavEd.Status = doc.Name + " has unsaved edits - press Close again to discard them";
                return;
            }
            navCloseArmed = null;
            navRenderer?.Invalidate(doc);
            NavEd.Close(doc);
            NavEd.Status = "closed " + doc.Name;
        }

        private void NavSave_P4(bool saveAs)
        {
            var doc = NavEd.Active;
            if (doc?.Ynv == null) { NavEd.Status = "no file is active"; return; }
            string path = doc.FilePath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                using var dlg = new SaveFileDialog { Filter = "Nav meshes|*.ynv|All files|*.*", FileName = doc.Name };
                var dir = navLastSaveDir ?? ProjWin?.Project?.Directory;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            if (NavSaveTo_P4(doc, path)) navLastSaveDir = Path.GetDirectoryName(path);
        }

        public bool NavSaveTo_P4(NavMeshEditor.NavDoc doc, string path)
        {
            if (doc?.Ynv == null || string.IsNullOrEmpty(path)) return false;
            if (doc.PolyCount == 0)
            {
                NavEd.Status = doc.Name + " has no polygons - draw or generate some before saving";
                Console.WriteLine("NAV save refused (no polygons): " + path);
                return false;
            }
            try
            {
                NavMeshEditor.Reindex(doc.Ynv);
                NavMeshEditor.SyncVertexLists(doc.Ynv);
                doc.Ynv.UpdateContentFlags(false);
                var seam = NavEd.CheckSeams_V15(doc);
                if (!seam.Safe) Console.WriteLine("NAVSEAM " + doc.Name + ": " + seam.Message);

                var data = doc.Ynv.Save();
                if (File.Exists(path) && !File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
                File.WriteAllBytes(path, data);
                doc.FilePath = path;
                doc.Ynv.FilePath = path;
                doc.Name = Path.GetFileName(path);
                doc.Ynv.Name = doc.Name;
                doc.Dirty = false;
                doc.PolyCountOnLoad_V15 = doc.PolyCount;
                NavEd.Status = seam.Safe
                    ? $"wrote {doc.Name} ({data.Length:N0} bytes, {doc.PolyCount:N0} polys) - indices unchanged, neighbouring files still valid"
                    : $"wrote {doc.Name} ({data.Length:N0} bytes, {doc.PolyCount:N0} polys) - WARNING: the poly count changed ({seam.PolyCountOnLoad:N0} -> {doc.PolyCount:N0}), so re-export every .ynv touching this one";
                Console.WriteLine("NAV saved " + path + " - " + data.Length + " bytes");
                return true;
            }
            catch (Exception ex)
            {
                NavEd.Status = "save failed: " + ex.Message;
                Console.WriteLine("NAV save failed " + path + ": " + ex.Message);
                return false;
            }
        }

        private void NavAddToProject_P4()
        {
            var doc = NavEd.Active;
            if (doc?.Ynv == null) { NavEd.Status = "no file is active"; return; }
            if (string.IsNullOrEmpty(doc.FilePath))
            {
                NavEd.Status = "save this file somewhere first - the project remembers it by path";
                NavSave_P4(true);
                if (string.IsNullOrEmpty(doc.FilePath)) return;
            }
            NavProjectAdd_Q2(doc.Ynv, doc.Name);
            NavEd.Status = doc.Name + " is in the project";
        }

        private void NavMouseClick_P4(int x, int y, ref bool handled)
        {
            if (panel == null || !panel.NavMode) return;
            handled = true;
            if (NavSelectByRightClick_S4 && !NavEd.PlacingPoly) return;
            WorldPickScreenToDevice(x, y, out float sx, out float sy);
            var ray = camera.GetPickRay(sx, sy, deviceResources.Width, deviceResources.Height);

            if (NavEd.PlacingPoly)
            {
                if (NavGroundPoint_P4(ray, out var gp))
                {
                    NavEd.PendingPoly.Add(gp);
                    NavEd.Status = $"corner {NavEd.PendingPoly.Count} at {gp.X:0.##}, {gp.Y:0.##}, {gp.Z:0.##}" +
                                   (NavEd.PendingPoly.Count >= 3 ? "  -  Enter closes the polygon" : "");
                }
                else NavEd.Status = "nothing under the cursor - aim at the ground";
                return;
            }

            var poly = NavEd.Pick(ray, out var polyDoc, out float polyDist);
            float d0 = polyDist < float.MaxValue ? polyDist : 40.0f;
            float nodeR = Math.Max(0.3f, d0 * 0.012f);
            var node = NavEd.PickNode(ray, nodeR, out var nodeDoc, out float nodeDist);
            if (node != null && (poly == null || nodeDist <= polyDist + 0.5f))
            {
                NavEd.ClearSelection();
                if (nodeDoc != null) NavEd.Active = nodeDoc;
                if (node is YnvPoint pt) { NavEd.SelectedPoint = pt; NavEd.Status = $"point {pt.Index} of {nodeDoc?.Name}"; }
                else if (node is YnvPortal po) { NavEd.SelectedPortal = po; NavEd.Status = $"portal {po.Index} of {nodeDoc?.Name}"; }
                WorldEdit.Deselect();
                return;
            }
            if (poly == null)
            {
                NavEd.ClearSelection();
                NavEd.Status = "nothing under the cursor";
                return;
            }
            if (polyDoc != null) NavEd.Active = polyDoc;
            NavEd.SelectPoly(poly, (ModifierKeys & Keys.Control) != 0);
            WorldEdit.Deselect();
            var cat = NavMeshEditor.CatNames[(int)NavEd.CategoryOf(poly)];
            NavEd.Status = $"poly {poly.Index} ({cat}) of {polyDoc?.Name}" +
                           (NavEd.SelectedPolys.Count > 1 ? $"  -  {NavEd.SelectedPolys.Count} selected" : "");
        }

        private bool NavGroundPoint_P4(Ray ray, out Vector3 point)
        {
            var np = NavEd.Pick(ray, out _, out float nd);
            var e = WorldPickPrecise(ray, out float d);
            bool hasMesh = np != null && nd > 0.0f && nd < 5000.0f;
            bool hasWorld = e != null && d > 0.0f && d < 5000.0f;
            if (hasMesh && (!hasWorld || nd <= d + 0.25f)) { point = ray.Position + ray.Direction * nd; return true; }
            if (hasWorld) { point = ray.Position + ray.Direction * d; return true; }
            var be = WorldPickEntityBoxes(ray, null, float.MaxValue);
            if (be != null && worldPickEntityDist > 0.0f && worldPickEntityDist < 5000.0f)
            { point = ray.Position + ray.Direction * worldPickEntityDist; return true; }
            float z = NavEd.PendingPoly.Count > 0 ? NavEd.PendingPoly[NavEd.PendingPoly.Count - 1].Z : camera.Position.Z - 5.0f;
            if (Math.Abs(ray.Direction.Z) < 1e-4f) { point = Vector3.Zero; return false; }
            float t = (z - ray.Position.Z) / ray.Direction.Z;
            if (t <= 0.0f || t > 5000.0f) { point = Vector3.Zero; return false; }
            point = ray.Position + ray.Direction * t;
            return true;
        }

        private void NavClosePendingPoly_P4()
        {
            var doc = NavEd.Active;
            if (doc == null) { NavEd.Status = "open or create a .ynv first"; return; }
            if (NavEd.PendingPoly.Count < 3) { NavEd.Status = "a polygon needs at least three corners"; return; }
            var p = NavEd.AddPoly(WorldHistory, doc, NavEd.PendingPoly.ToArray(), NavEd.SelectedPoly);
            NavEd.PendingPoly.Clear();
            if (p != null) { NavEd.SelectPoly(p, false); NavProjectAdd_Q2(doc.Ynv, doc.Name); }
        }

        private void NavDeleteSelection_P4()
        {
            var doc = NavEd.Active;
            if (NavEd.SelectedPolys.Count == 0) { NavEd.Status = "nothing selected"; return; }
            var owner = NavEd.DocOf(NavEd.SelectedPoly?.Ynv) ?? doc;
            var sel = NavEd.SelectedPolys.ToArray();

            int n = NavEd.HardDeletePolys_V15
                ? NavEd.DeletePolys(WorldHistory, owner, sel)
                : NavEd.DisablePolys_V15(WorldHistory, owner, sel);

            if (n > 0)
            {
                NavEd.ClearSelection();
                NavProjectAdd_Q2(owner.Ynv, owner.Name);
                if (NavEd.HardDeletePolys_V15)
                {
                    var seam = NavEd.CheckSeams_V15(owner);
                    if (!seam.Safe) Console.WriteLine("NAVSEAM " + seam.Message);
                }
            }
        }

        private void NavAddNodeAtCrosshair_P4(bool portal)
        {
            var doc = NavEd.Active;
            if (doc == null) { NavEd.Status = "open or create a .ynv first"; return; }
            var ray = camera.GetPickRay(deviceResources.Width * 0.5f, deviceResources.Height * 0.5f,
                                        deviceResources.Width, deviceResources.Height);
            if (!NavGroundPoint_P4(ray, out var p)) { NavEd.Status = "aim at the ground first"; return; }
            var lift = new Vector3(0, 0, 0.1f);
            if (portal)
            {
                var fwd = camera.Forward; fwd.Z = 0;
                if (fwd.LengthSquared() < 1e-6f) fwd = Vector3.UnitY; else fwd.Normalize();
                var po = NavEd.AddPortal(WorldHistory, doc, p + lift, p + lift + fwd * 2.0f);
                if (po != null) { NavEd.ClearSelection(); NavEd.SelectedPortal = po; }
            }
            else
            {
                var pt = NavEd.AddPoint(WorldHistory, doc, p + lift, 1);
                if (pt != null) { NavEd.ClearSelection(); NavEd.SelectedPoint = pt; }
            }
            NavProjectAdd_Q2(doc.Ynv, doc.Name);
        }

        private void NavFrameSelection_P4()
        {
            Vector3 c; float size = 8.0f;
            if (NavEd.SelectedPoly != null)
            {
                c = NavEd.SelectedPoly.Position;
                var vs = NavEd.SelectedPoly.Vertices;
                if (vs != null && vs.Length > 0)
                {
                    float r = 0; foreach (var v in vs) r = Math.Max(r, (v - c).Length());
                    size = Math.Max(r * 2.5f, 6.0f);
                }
            }
            else if (NavEd.SelectedPoint != null) c = NavEd.SelectedPoint.Position;
            else if (NavEd.SelectedPortal != null) c = NavEd.SelectedPortal.PositionFrom;
            else if (NavEd.Active != null && NavEd.Active.PolyCount > 0)
            {
                var b = NavEd.Active.Bounds;
                c = (b.Minimum + b.Maximum) * 0.5f;
                size = Math.Max((b.Maximum - b.Minimum).Length() * 0.5f, 20.0f);
            }
            else { NavEd.Status = "nothing to frame"; return; }

            var eye = c + new Vector3(0, -size * 1.1f, size * 0.9f + 3.0f);
            var f = Vector3.Normalize(c - eye);
            float yaw = (float)Math.Atan2(-f.Y, -f.X);
            float pitch = (float)Math.Asin(MathUtil.Clamp(-f.Z, -1.0f, 1.0f));
            CameraSequence.ApplyToCamera(camera, eye, yaw, pitch, settings.FovDeg);
        }

        private void NavKeyDown_P4(Keys combo, ref bool handled)
        {
            if (panel == null || !panel.NavMode) return;
            if (ImGuiWantsKeyboard && !ignoreImGuiKeyboard) return;
            var key = combo & Keys.KeyCode;
            bool ctrl = (combo & Keys.Control) != 0;

            if (ctrl && key == Keys.S) { NavSave_P4(false); handled = true; return; }
            if (ctrl) return;

            switch (key)
            {
                case Keys.P:
                    NavEd.PlacingPoly = !NavEd.PlacingPoly;
                    if (!NavEd.PlacingPoly) NavEd.PendingPoly.Clear();
                    NavEd.Status = NavEd.PlacingPoly ? "drawing: click the corners, Enter closes" : "draw mode off";
                    handled = true;
                    return;
                case Keys.Return:
                    if (NavEd.PendingPoly.Count >= 3) { NavClosePendingPoly_P4(); handled = true; }
                    return;
                case Keys.Escape:
                    if (NavEd.PendingPoly.Count > 0) { NavEd.PendingPoly.Clear(); NavEd.Status = "corners discarded"; handled = true; }
                    else if (NavEd.PlacingPoly) { NavEd.PlacingPoly = false; NavEd.Status = "draw mode off"; handled = true; }
                    else if (NavEd.SelectedPolys.Count > 0 || NavEd.SelectedPoint != null || NavEd.SelectedPortal != null)
                    { NavEd.ClearSelection(); handled = true; }
                    return;
                case Keys.Back:
                    if (NavEd.PendingPoly.Count > 0) { NavEd.PendingPoly.RemoveAt(NavEd.PendingPoly.Count - 1); handled = true; }
                    return;
                case Keys.Delete:
                    if (NavEd.SelectedPoint != null) NavEd.DeletePoint(WorldHistory, NavEd.DocOf(NavEd.SelectedPoint.Ynv), NavEd.SelectedPoint);
                    else if (NavEd.SelectedPortal != null) NavEd.DeletePortal(WorldHistory, NavEd.DocOf(NavEd.SelectedPortal.Ynv), NavEd.SelectedPortal);
                    else if (NavEd.SelectedPolys.Count > 0) NavDeleteSelection_P4();
                    handled = true;
                    return;
                case Keys.F:
                    NavFrameSelection_P4(); handled = true;
                    return;
            }
        }

        private void NavGizmoTargets_P4(List<IWorldGizmoTarget> list)
        {
            if (panel == null || !panel.NavMode) return;
            if (NavEd.SelectedPoint != null) { list.Add(new NavPointTarget(NavEd.DocOf(NavEd.SelectedPoint.Ynv), NavEd.SelectedPoint)); return; }
            if (NavEd.SelectedPortal != null) { list.Add(new NavPortalTarget(NavEd.DocOf(NavEd.SelectedPortal.Ynv), NavEd.SelectedPortal)); return; }
            var p = NavEd.SelectedPoly;
            if (p == null) return;
            var doc = NavEd.DocOf(p.Ynv);
            if (doc == null) return;
            if (NavEd.SelectedVertex >= 0 && p.Vertices != null && NavEd.SelectedVertex < p.Vertices.Length)
                list.Add(new NavVertexTarget(NavEd, doc, p, NavEd.SelectedVertex));
            else
                list.Add(new NavSelectionTarget(NavEd, doc,
                    NavEd.SelectedPolys.Where(q => ReferenceEquals(q.Ynv, doc.Ynv)).ToArray()));
        }

        private void NavTargetChanged_P4(IWorldGizmoTarget t, ref bool handled)
        {
            YnvFile ynv = null;
            switch (t)
            {
                case NavVertexTarget v: ynv = v.Poly?.Ynv; break;
                case NavSelectionTarget s: ynv = s.Polys.Length > 0 ? s.Polys[0].Ynv : null; break;
                case NavPointTarget pt: ynv = pt.Point?.Ynv; break;
                case NavPortalTarget po: ynv = po.Portal?.Ynv; break;
                default: return;
            }
            handled = true;
            var doc = NavEd.DocOf(ynv);
            if (doc == null) return;
            doc.Dirty = true;
            navDragDoc = doc;
            if (!worldGizmo.Dragging) { NavMeshEditor.RecomputeBounds(doc); navDragDoc = null; }
            NavProjectAdd_Q2(ynv, doc.Name);
        }

        private void NavGenerate_P4()
        {
            var doc = NavEd.Active;
            if (doc?.Ynv == null) { NavEd.Status = "open or create a .ynv first (New empty .ynv for this cell)"; return; }
            if (!worldBuilt) { NavEd.Status = "the world has not streamed in yet"; return; }

            BoundingBox box;
            Func<float, float, bool> inside = null;
            var area = AreaTool?.Current;
            if (NavEd.GenUseArea && area != null && area.IsValid)
            {
                box = area.Bounds;
                inside = area.ContainsXY;
            }
            else
            {
                float r = Math.Min(NavEd.LoadRadius, 150.0f);
                var c = camera.Position;
                box = new BoundingBox(new Vector3(c.X - r, c.Y - r, c.Z - 200.0f), new Vector3(c.X + r, c.Y + r, c.Z + 200.0f));
            }

            float top = box.Maximum.Z + 5.0f;
            bool Sample(float x, float y, out float z)
            {
                var ray = new Ray(new Vector3(x, y, top), -Vector3.UnitZ);
                var e = WorldPickPrecise(ray, out float d);
                if (e == null || d <= 0.0f || d > 4000.0f) { z = 0; return false; }
                z = top - d;
                return z >= box.Minimum.Z && z <= box.Maximum.Z;
            }

            var polys = NavMeshEditor.Generate(doc.Ynv, inside, box, NavEd.GenDensity, NavEd.GenSlopeLimit,
                                               NavEd.GenInterior, Sample, out var report);
            if (polys.Count == 0) { NavEd.Status = "nothing generated: " + report; Console.WriteLine("NAV generate: " + report); return; }
            int n = NavEd.AddPolys(WorldHistory, doc, polys, $"Generate {polys.Count} nav polys");
            NavProjectAdd_Q2(doc.Ynv, doc.Name);
            NavEd.Status = $"generated {n} polygon(s) into {doc.Name} - {report}";
            Console.WriteLine("NAV " + NavEd.Status);
        }

        private void DrawNavMesh_P4(DeviceContext context)
        {
            if (panel == null || !panel.NavMode || NavEd.Docs.Count == 0) return;
            if (NavDrawDeferred_U2() || NavDrawSuppressed_U2()) return;
            navRenderer ??= new NavMeshRenderer(deviceResources.Device);
            navRenderer.Draw(context, camera.ViewProjMatrix, camera.Position, NavEd);

            var camPos = camera.Position;
            float Handle(Vector3 p) => Math.Max(0.16f, (p - camPos).Length() * 0.008f);
            var sel = new Vector4(1.8f, 1.32f, 0.30f, 0.75f);
            var selLine = new Vector4(3.2f, 2.50f, 0.80f, 1.0f);
            var draft = new Vector4(0.91f, 2.21f, 2.6f, 0.9f);
            NavOverlayInk_U2(ref sel, ref selLine, ref draft);

            foreach (var p in NavEd.SelectedPolys)
            {
                var vs = p?.Vertices;
                if (vs == null || vs.Length < 3) continue;
                for (int t = 0; t < vs.Length - 2; t++) triRenderer.AddTri(vs[0], vs[t + 1], vs[t + 2], sel);
                for (int e = 0; e < vs.Length; e++)
                    triRenderer.AddThickLine(vs[e], vs[(e + 1) % vs.Length], camPos, Handle(vs[e]) * 0.6f, selLine);
            }
            var last = NavEd.SelectedPoly;
            if (last?.Vertices != null)
                for (int i = 0; i < last.Vertices.Length; i++)
                {
                    var v = last.Vertices[i];
                    bool on = NavEd.SelectedVertex == i;
                    triRenderer.AddSphere(v, Handle(v) * (on ? 1.9f : 1.1f), on ? selLine : sel);
                }
            if (NavEd.SelectedPoint != null)
            {
                var p = NavEd.SelectedPoint.Position;
                triRenderer.AddSphere(p, Handle(p) * 2.0f, selLine);
            }
            if (NavEd.SelectedPortal != null)
            {
                var a = NavEd.SelectedPortal.PositionFrom; var b = NavEd.SelectedPortal.PositionTo;
                triRenderer.AddSphere(a, Handle(a) * 2.0f, selLine);
                triRenderer.AddThickLine(a, b, camPos, Handle(a) * 0.5f, selLine);
            }
            var pend = NavEd.PendingPoly;
            if (pend.Count > 0)
            {
                for (int i = 0; i < pend.Count; i++)
                {
                    triRenderer.AddSphere(pend[i], Handle(pend[i]) * 1.6f, draft);
                    if (i > 0) triRenderer.AddThickLine(pend[i - 1], pend[i], camPos, Handle(pend[i]) * 0.3f, draft);
                }
                if (pend.Count >= 3)
                {
                    triRenderer.AddThickLine(pend[pend.Count - 1], pend[0], camPos, Handle(pend[0]) * 0.3f, draft);
                    var fill = new Vector4(draft.X, draft.Y, draft.Z, 0.28f);
                    for (int t = 0; t < pend.Count - 2; t++) triRenderer.AddTri(pend[0], pend[t + 1], pend[t + 2], fill);
                }
            }
            triRenderer.Flush(context, camera.ViewProjMatrix, CommonStates.BlendAlpha, CommonStates.DepthDisabled);
        }

        private void NavHeadless_P4()
        {
            var open = Environment.GetEnvironmentVariable("RLE_NAVOPEN");
            if (string.IsNullOrWhiteSpace(open)) return;
            if (worldStart.HasValue) return;
            navEnvFrame++;

            if (!navEnvLayers)
            {
                navEnvLayers = true;
                var layers = Environment.GetEnvironmentVariable("RLE_NAVLAYERS");
                if (!string.IsNullOrWhiteSpace(layers))
                    foreach (var raw in layers.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var tok = raw.Trim();
                        bool on = !tok.StartsWith("-");
                        var nm = tok.TrimStart('-').Trim().ToLowerInvariant();
                        switch (nm)
                        {
                            case "fills": NavEd.ShowFills = on; break;
                            case "edges": NavEd.ShowEdges = on; break;
                            case "links": NavEd.ShowLinks = on; break;
                            case "portals": NavEd.ShowPortals = on; break;
                            case "points": NavEd.ShowPoints = on; break;
                            case "legend": NavEd.ShowLegend = on; break;
                            case "isolated": NavEd.HighlightIsolated = on; break;
                            default: Console.WriteLine("NAVTEST unknown layer: " + nm); break;
                        }
                        NavEd.LayerVersion++;
                    }
            }

            if (!navEnvOpened)
            {
                var sd = SpaceDataOrNull;
                sd?.EnsureNav();
                bool byPath = File.Exists(open);
                bool around = string.Equals(open.Trim(), "around", StringComparison.OrdinalIgnoreCase);
                bool ready = byPath || (sd != null && sd.NavReady) || (!around && (panel?.Archive?.Ready ?? false));
                if (!ready)
                {
                    if (navEnvFrame > 3000) { navEnvOpened = true; Console.WriteLine("NAVTEST gave up waiting for the archives"); }
                    return;
                }
                navEnvOpened = true;
                if (string.Equals(open.Trim(), "new", StringComparison.OrdinalIgnoreCase))
                {
                    NavNewFileHere_P4();
                    Console.WriteLine($"NAVTEST new empty file: {NavEd.Active?.Name}");
                    return;
                }
                if (around)
                {
                    NavLoadAroundCamera_P4();
                    Console.WriteLine($"NAVTEST loaded {NavEd.Docs.Count} cell(s) around the camera: " +
                                      $"{NavEd.TotalPolys} polys, {NavEd.TotalPoints} points, {NavEd.TotalPortals} portals");
                    return;
                }
                var doc = byPath ? NavOpenPath_P4(open) : NavOpenByName_P4(open);
                Console.WriteLine(doc != null
                    ? $"NAVTEST opened {doc.Name}: {doc.PolyCount} polys, {doc.PointCount} points, {doc.PortalCount} portals"
                    : "NAVTEST open FAILED: " + open);
                return;
            }
            if (navEnvEdited) { NavHeadlessSave_P4(); return; }

            var genEnv = Environment.GetEnvironmentVariable("RLE_NAVGEN");
            if (!string.IsNullOrWhiteSpace(genEnv) && (navEnvFrame < 260 || worldRender.MeshesDrawn <= 0)) return;

            navEnvEdited = true;

            var doc2 = NavEd.Active;
            if (doc2 == null) { Console.WriteLine("NAVTEST nothing open"); return; }

            if (!string.IsNullOrWhiteSpace(genEnv))
            {
                var g = genEnv.Split(',');
                float N(int i, float d) => i < g.Length && float.TryParse(g[i].Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
                NavEd.GenDensity = N(0, 1.0f);
                NavEd.GenSlopeLimit = N(1, 40.0f);
                NavEd.LoadRadius = N(2, 25.0f);
                NavEd.GenUseArea = false;
                int before = doc2.PolyCount;
                NavGenerate_P4();
                Console.WriteLine($"NAVTEST generated {doc2.PolyCount - before} polys into {doc2.Name} " +
                                  $"(grid {NavEd.GenDensity:0.##} m, slope {NavEd.GenSlopeLimit:0} deg, {NavEd.LoadRadius:0} m square)");
                if (screenshotPath != null) ProjWin.Visible = false;
            }

            int want = 0;
            var selEnv = Environment.GetEnvironmentVariable("RLE_NAVSELECT");
            if (!string.IsNullOrWhiteSpace(selEnv)) int.TryParse(selEnv.Trim(), out want);
            var polys = doc2.Ynv?.Polys;
            if (polys != null && want >= 0 && want < polys.Count)
            {
                NavEd.SelectPoly(polys[want], false);
                var p = polys[want];
                Console.WriteLine($"NAVTEST selected poly {p.Index} ({NavMeshEditor.CatNames[(int)NavEd.CategoryOf(p)]}) " +
                                  $"at {p.Position.X:0.##},{p.Position.Y:0.##},{p.Position.Z:0.##} " +
                                  $"flags 0x{p._RawData.PolyFlags0:X4} 0x{p._RawData.PolyFlags1:X8}");
                NavFrameSelection_P4();
            }
            else Console.WriteLine($"NAVTEST poly {want} is not in this file ({polys?.Count ?? 0} polys)");

            var flagEnv = Environment.GetEnvironmentVariable("RLE_NAVFLAG");
            if (!string.IsNullOrWhiteSpace(flagEnv))
            {
                var bits = flagEnv.Split('=');
                int fi = NavMeshEditor.FlagIndex(bits[0]);
                bool on = bits.Length < 2 || bits[1].Trim() != "0";
                if (fi < 0) Console.WriteLine("NAVTEST unknown flag: " + bits[0]);
                else if (NavEd.SelectedPoly == null) Console.WriteLine("NAVTEST no poly selected to flag");
                else
                {
                    bool was = NavMeshEditor.Flags[fi].Get(NavEd.SelectedPoly);
                    NavEd.SetFlag(WorldHistory, NavEd.SelectedPolys.ToArray(), fi, on);
                    bool now = NavMeshEditor.Flags[fi].Get(NavEd.SelectedPoly);
                    Console.WriteLine($"NAVTEST flag '{NavMeshEditor.Flags[fi].Name}' {was} -> {now} on poly {NavEd.SelectedPoly.Index}");
                }
            }
        }

        private void NavHeadlessSave_P4()
        {
            if (navEnvSaved) return;
            var path = Environment.GetEnvironmentVariable("RLE_NAVSAVE");
            if (string.IsNullOrWhiteSpace(path)) { navEnvSaved = true; return; }
            navEnvSaved = true;
            var doc = NavEd.Active;
            if (doc?.Ynv == null) { Console.WriteLine("NAVTEST nothing to save"); return; }
            int selIndex = NavEd.SelectedPoly?.Index ?? 0;
            int polyCount = doc.PolyCount, pointCount = doc.PointCount, portalCount = doc.PortalCount;

            var flagEnv = Environment.GetEnvironmentVariable("RLE_NAVFLAG");
            int fi = string.IsNullOrWhiteSpace(flagEnv) ? -1 : NavMeshEditor.FlagIndex(flagEnv.Split('=')[0]);
            bool want = fi < 0 || !flagEnv.Contains('=') || flagEnv.Split('=')[1].Trim() != "0";

            if (!NavSaveTo_P4(doc, path)) { Console.WriteLine("NAVTEST save FAILED"); return; }
            try
            {
                var re = new YnvFile();
                re.Load(File.ReadAllBytes(path));
                int rp = re.Polys?.Count ?? 0;
                Console.WriteLine($"NAVTEST reopened {Path.GetFileName(path)}: {rp} polys " +
                                  $"(was {polyCount}), {re.Points?.Count ?? 0} points (was {pointCount}), " +
                                  $"{re.Portals?.Count ?? 0} portals (was {portalCount})");
                Console.WriteLine(rp == polyCount ? "NAVTEST POLYCOUNT OK" : "NAVTEST POLYCOUNT MISMATCH");
                if (fi >= 0 && selIndex < rp)
                {
                    bool got = NavMeshEditor.Flags[fi].Get(re.Polys[selIndex]);
                    Console.WriteLine($"NAVTEST poly {selIndex} '{NavMeshEditor.Flags[fi].Name}' after reload = {got} (wanted {want})");
                    Console.WriteLine(got == want ? "NAVTEST FLAG SURVIVED" : "NAVTEST FLAG LOST");
                }
            }
            catch (Exception ex) { Console.WriteLine("NAVTEST reopen FAILED: " + ex.Message); }
        }

        private void SeqTest_P4(Action<string, bool, string> check)
        {
            int fails = NavMeshEditor.SelfTest();
            check("the nav mesh editor's own self-test", fails == 0, $"{fails} failure(s)");

            NavMeshEditor.SeamSelfTest_V15(check);
            NavMeshEditor.RelinkSelfTest_V16(check);

            {
                var v15ed = new NavMeshEditor();
                var v15ynv = NavMeshEditor.NewFile(1234, 20, new SharpDX.Vector3(0, 0, -20), new SharpDX.Vector3(150, 150, 130));
                var v15doc = v15ed.Add(v15ynv, null, "v15 seam");
                var made = new List<CodeWalker.GameFiles.YnvPoly>();
                for (int i = 0; i < 5; i++)
                {
                    float x = i * 5f;
                    made.Add(NavMeshEditor.BuildPoly(v15ynv, new[]
                    {
                        new SharpDX.Vector3(x, 0, 0), new SharpDX.Vector3(x + 4, 0, 0),
                        new SharpDX.Vector3(x + 4, 4, 0), new SharpDX.Vector3(x, 4, 0),
                    }, null));
                }
                v15ed.AddPolys(null, v15doc, made, "v15 fixture");
                v15doc.PolyCountOnLoad_V15 = v15ynv.Polys.Count;
                int before = v15ynv.Polys.Count;
                int idxOfLast = v15ynv.Polys.IndexOf(made[4]);

                v15ed.DisablePolys_V15(null, v15doc, new[] { made[1] });
                check("v15: disabling a poly leaves the file's poly COUNT alone",
                      v15ynv.Polys.Count == before, $"{before} -> {v15ynv.Polys.Count}");
                check("v15: ...and every later poly keeps the index other files know it by",
                      v15ynv.Polys.IndexOf(made[4]) == idxOfLast,
                      $"last poly at {v15ynv.Polys.IndexOf(made[4])}, was {idxOfLast}");
                check("v15: ...and the disabled one is out of the graph",
                      NavMeshEditor.IsDisabled_V15(made[1]) &&
                      (made[1].Edges ?? Array.Empty<CodeWalker.GameFiles.YnvEdge>())
                          .All(e => e == null || e.PolyID1 == NavMeshEditor.NoPoly_V15),
                      "flags cleared, edges severed");

                var seamOk = v15ed.CheckSeams_V15(v15doc);
                check("v15: the seam check calls that save safe", seamOk.Safe,
                      seamOk.Message.Substring(0, Math.Min(78, seamOk.Message.Length)));

                v15ed.DeletePolys(null, v15doc, new[] { made[0] });
                var seamBad = v15ed.CheckSeams_V15(v15doc);
                check("v15: a hard delete moves the indices, and the seam check says so",
                      !seamBad.Safe && v15ynv.Polys.IndexOf(made[4]) != idxOfLast,
                      $"last poly {idxOfLast} -> {v15ynv.Polys.IndexOf(made[4])}");
            }

            check("the NavMesh workspace keeps its enum value (saved indices keep their meaning)",
                  (int)LightPanel.Space.NavMesh == 7,
                  $"NavMesh = {(int)LightPanel.Space.NavMesh} of {Enum.GetValues(typeof(LightPanel.Space)).Length}");

            check("the panel has the editor", ReferenceEquals(panel?.Nav, NavEd), panel?.Nav == null ? "null" : "wired");

            var was = panel.Workspace;
            panel.SwitchWorkspace(LightPanel.Space.NavMesh);
            check("standing in NavMesh still counts as the world (toolbar, gizmo, project window)",
                  panel.WorldMode && panel.NavMode, $"world {panel.WorldMode} nav {panel.NavMode}");

            var ynv = NavMeshEditor.NewFile(50, 20, new Vector3(0, 0, -20), new Vector3(150, 150, 130));
            var doc = NavEd.Add(ynv, null, "seqtest");
            var poly = NavEd.AddPoly(WorldHistory, doc, new[]
            {
                new Vector3(5, 5, 1), new Vector3(9, 5, 1), new Vector3(9, 9, 1), new Vector3(5, 9, 1)
            }, null);
            check("a polygon can be drawn into a new file", poly != null && doc.PolyCount == 1, $"{doc.PolyCount}");
            NavEd.SelectPoly(poly, false);
            check("clicking it selects it", ReferenceEquals(NavEd.SelectedPoly, poly), "");

            int fi = NavMeshEditor.FlagIndex("interior");
            NavEd.SetFlag(WorldHistory, NavEd.SelectedPolys.ToArray(), fi, true);
            check("a flag ticked on the selection reaches the polygon", poly.B14_IsInterior, "");
            check("a freshly drawn polygon shows the type you picked, even with nothing joined to it",
                  NavEd.CategoryOf(poly) == NavMeshEditor.NavCat.Interior,
                  NavEd.CategoryOf(poly).ToString());
            check("...and it is still MARKED as isolated, because it is",
                  NavMeshEditor.IsIsolated(poly) && NavEd.ShowsAsIsolated(poly), "");
            check("...and it is still visible/clickable",
                  NavEd.PolyVisible(poly), "");
            {
                var types = new[] { NavMeshEditor.NavCat.Water, NavMeshEditor.NavCat.Road,
                                    NavMeshEditor.NavCat.Pavement, NavMeshEditor.NavCat.Train };
                int wrong = 0;
                foreach (var want in types)
                {
                    NavEd.SetType(WorldHistory, new[] { poly }, want);
                    if (NavEd.CategoryOf(poly) != want) wrong++;
                }
                check("...and every type button lands on it", wrong == 0,
                      wrong == 0 ? "water, road, pavement, train track all read back" : $"{wrong} did not");
                NavEd.SetType(WorldHistory, new[] { poly }, NavMeshEditor.NavCat.Interior);
            }
            TryWorldUndo();
            check("Ctrl+Z takes the flag back off", !poly.B14_IsInterior, "");
            TryWorldRedo();
            check("Ctrl+Y puts it back", poly.B14_IsInterior, "");

            NavEd.SelectedVertex = 0;
            var targets = new List<IWorldGizmoTarget>();
            NavGizmoTargets_P4(targets);
            check("a chosen corner hands the gizmo a target", targets.Count == 1 && targets[0] is NavVertexTarget,
                  targets.Count == 0 ? "none" : targets[0].GetType().Name);
            if (targets.Count == 1)
            {
                var before = targets[0].Position;
                targets[0].SetPosition(before + new Vector3(0, 0, 2));
                check("dragging it moves the corner", (poly.Vertices[0] - before - new Vector3(0, 0, 2)).Length() < 0.001f,
                      poly.Vertices[0].ToString());
            }

            var poly2 = NavEd.AddPoly(WorldHistory, doc, new[]
            {
                new Vector3(9, 5, 1), new Vector3(13, 5, 1), new Vector3(13, 9, 1), new Vector3(9, 9, 1)
            }, null);
            check("a polygon drawn against another links to it",
                  poly2 != null && !NavMeshEditor.IsIsolated(poly) && !NavMeshEditor.IsIsolated(poly2),
                  $"{NavEd.CategoryOf(poly)} / {NavEd.CategoryOf(poly2)}");
            check("...and NOW the interior flag is what colours it",
                  NavEd.CategoryOf(poly) == NavMeshEditor.NavCat.Interior, NavEd.CategoryOf(poly).ToString());

            NavEd.SelectedVertex = -1;
            NavEd.SelectPoly(poly, false);
            NavEd.SelectPoly(poly2, true);
            targets.Clear();
            NavGizmoTargets_P4(targets);
            check("a multi-polygon selection is ONE gizmo target", targets.Count == 1 && targets[0] is NavSelectionTarget,
                  targets.Count == 0 ? "none" : $"{targets.Count} x {targets[0].GetType().Name}");
            if (targets.Count == 1 && poly2?.Vertices != null)
            {
                var shared = poly2.Vertices.First(v => Math.Abs(v.X - 9) < 0.001f && Math.Abs(v.Y - 5) < 0.001f);
                var start = targets[0].Position;
                targets[0].SetPosition(start + new Vector3(3, 0, 0));
                var moved = poly2.Vertices.FirstOrDefault(v => Math.Abs(v.Y - 5) < 0.001f && Math.Abs(v.Z - shared.Z) < 0.001f && v.X > 10);
                check("the corner the two share moves exactly once", Math.Abs(moved.X - (shared.X + 3)) < 0.001f,
                      $"{moved.X:0.###} vs {shared.X + 3:0.###}");
            }
            NavEd.DeletePolys(WorldHistory, doc, new[] { poly2 });
            NavEd.SelectPoly(poly, false);

            var tmp = Path.Combine(Path.GetTempPath(), "rle_navtest_p4.ynv");
            bool saved = NavSaveTo_P4(doc, tmp);
            check("Save writes the .ynv", saved && File.Exists(tmp), tmp);
            if (saved)
            {
                try
                {
                    var re = new YnvFile();
                    re.Load(File.ReadAllBytes(tmp));
                    check("it reads back with the polygon in it", (re.Polys?.Count ?? 0) == 1, $"{re.Polys?.Count ?? 0}");
                    check("and the flag survived the round trip", re.Polys?.Count == 1 && re.Polys[0].B14_IsInterior, "");
                }
                catch (Exception ex) { check("the saved .ynv reloads", false, ex.Message); }
                try { File.Delete(tmp); File.Delete(tmp + ".bak"); } catch { }
            }

            NavEd.Filter = "interior";
            check("the filter finds it by flag name", NavEd.FilteredPolys(doc, 10).Count() == 1, "");
            NavEd.Filter = "";

            NavEd.DeletePolys(WorldHistory, doc, new[] { poly });
            check("Delete removes it", doc.PolyCount == 0, $"{doc.PolyCount}");
            TryWorldUndo();
            check("and undo brings it back", doc.PolyCount == 1, $"{doc.PolyCount}");

            NavEd.Close(doc);
            check("closing the file empties the editor", NavEd.Docs.Count == 0, $"{NavEd.Docs.Count}");
            SeqTest_Q2(check);
            if (was != LightPanel.Space.NavMesh && screenshotPath == null) panel.SwitchWorkspace(was);
        }
    }
}

