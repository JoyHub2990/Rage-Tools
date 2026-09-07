using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public readonly AreaToolState AreaTool = new AreaToolState();

        private bool areaWired;
        private double areaLastAutoRefresh, areaLastHover, areaLastSideSave;
        private double areaLastClickTime = -10;
        private System.Drawing.Point areaLastClickAt = new System.Drawing.Point(-100, -100);
        private CwProject areaLastProject;
        private string areaLastProjectPath;
        private bool areaEnvDone, areaHoldPrinted;
        private double areaHoldStart, areaPostHoldStart;
        private readonly List<YmapFile> areaYmaps = new List<YmapFile>();
        private readonly HashSet<YmapEntityDef> areaSeen = new HashSet<YmapEntityDef>();
        private static float AreaHandleRadius(Vector3 p, Vector3 camPos) => Math.Max(0.22f, (p - camPos).Length() * 0.010f);

        partial void OnWorldTick_Area()
        {
            if (panel == null) return;
            if (!areaWired)
            {
                areaWired = true;
                panel.AreaTool = AreaTool;
                if (projCtl != null) projCtl.ProjectSaved += p => { if (p != null && !string.IsNullOrEmpty(p.Filepath)) SaveAreasBeside(p.Filepath, true); };
            }
            SyncAreaProject();
            if (!panel.WorldMode) { if (AreaTool.Drawing) CancelAreaDraw(); return; }
            var st = AreaTool;

            if (!panel.ShowRightPanel && screenshotPath == null) st.TabActive = false;
            if (WorldEdit.Selection.HasValue && st.SelectedCorner >= 0) st.SelectedCorner = -1;
            if (st.Selected >= st.Areas.Count) st.Selected = st.Areas.Count - 1;

            if (st.RequestStartDraw) { st.RequestStartDraw = false; StartAreaDraw(); }
            if (st.RequestFinishDraw) { st.RequestFinishDraw = false; FinishAreaDraw(); }
            if (st.RequestCancelDraw) { st.RequestCancelDraw = false; CancelAreaDraw(); }
            if (st.RequestUndoCorner) { st.RequestUndoCorner = false; if (st.Drawing && st.Draft.Count > 0) st.Draft.RemoveAt(st.Draft.Count - 1); }
            if (st.RequestBoxFromSelection) { st.RequestBoxFromSelection = false; AreaBoxFromSelection(); }
            if (st.RequestNewEmpty) { st.RequestNewEmpty = false; st.Add(new WorldArea { Name = st.NewName() }); st.Status = "empty area added - Draw area to give it corners"; }
            if (st.RequestRemoveArea) { st.RequestRemoveArea = false; var nm = st.Current?.Name; st.Remove(st.Selected); st.Status = nm != null ? "removed " + nm : ""; }
            if (st.RequestDuplicateArea) { st.RequestDuplicateArea = false; if (st.Current != null) { var c = st.Current.Clone(); c.Name = st.Current.Name + " copy"; st.Add(c); } }
            if (st.RequestRefresh) { st.RequestRefresh = false; RefreshAreaContents(false); }
            if (st.RequestDelete) { st.RequestDelete = false; AreaDeleteContents(); }
            if (st.RequestMove) { st.RequestMove = false; AreaMoveContents(st.MoveOffset); }
            if (st.RequestSaveList) { st.RequestSaveList = false; AreaSaveListDialog(); }
            if (st.RequestUndo) { st.RequestUndo = false; if (WorldHistory.CanUndo) { var nm = WorldHistory.NextUndoName; TryWorldUndo(); RefreshAreaContents(true); st.Status = "undone: " + nm; } else st.Status = "nothing to undo"; }
            if (st.RequestFrameArea) { st.RequestFrameArea = false; FrameArea(st.Current); }
            if (st.RequestSelectCorner != -2) { int c = st.RequestSelectCorner; st.RequestSelectCorner = -2; SelectAreaCorner(c); }
            if (st.RequestSelectEntry >= 0)
            {
                int i = st.RequestSelectEntry; st.RequestSelectEntry = -1;
                if (i < st.Contents.Count && st.Contents[i].Entity != null) { WorldEdit.Select(st.Contents[i].Entity); st.SelectedEntry = i; st.SelectedCorner = -1; }
            }
            if (st.RequestFrameEntry >= 0)
            {
                int i = st.RequestFrameEntry; st.RequestFrameEntry = -1;
                if (i < st.Contents.Count && st.Contents[i].Entity != null) { WorldEdit.Select(st.Contents[i].Entity); st.SelectedEntry = i; FrameSelection(); }
            }
            if (st.RequestSaveAreas) { st.RequestSaveAreas = false; AreaSaveAreasDialog(); }
            if (st.RequestLoadAreas) { st.RequestLoadAreas = false; AreaLoadAreasDialog(); }

            if (st.Drawing) UpdateAreaDraftHover();
            else st.DraftHover = null;

            double now = clock.Elapsed.TotalSeconds;
            if (st.TabActive && st.Current != null && st.Current.IsValid && !worldGizmo.Dragging && now - areaLastAutoRefresh > 1.5)
                RefreshAreaContents(true);

            if (st.AreasDirty && st.HasProjectFile && now - areaLastSideSave > 1.0 && !worldGizmo.Dragging)
                SaveAreasBeside(ProjWin?.Project?.Filepath, false);

            if (worldBuilt && screenshotPath != null && !areaEnvDone && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_AREA")))
            {
                if (worldWarmup == 350 && (worldRender.LoadsPending > 0 || World.YmapsOpen + 5 < World.YmapsWanted))
                {
                    if (areaHoldStart == 0) areaHoldStart = now;
                    if (now - areaHoldStart < 90.0) worldWarmup--;
                    else if (!areaHoldPrinted) { areaHoldPrinted = true; Console.WriteLine($"AREA hold gave up: loads pending {worldRender.LoadsPending}, ymaps {World.YmapsOpen}/{World.YmapsWanted}"); }
                }
                if (worldWarmup == 430) { areaEnvDone = true; ApplyAreaEnv(); }
            }
            if (areaEnvDone && screenshotPath != null && worldWarmup == 445 && (worldRender.LoadsPending > 0 || worldRender.BuiltThisFrame > 0))
            {
                if (areaPostHoldStart == 0) areaPostHoldStart = now;
                if (now - areaPostHoldStart < 40.0) worldWarmup--;
            }
        }

        private void StartAreaDraw()
        {
            var st = AreaTool;
            st.Drawing = true;
            st.Draft.Clear();
            st.DraftHover = null;
            st.SelectedCorner = -1;
            WorldEdit.Deselect();
            st.RequestShowTab = true;
            st.Status = "Draw area: click the ground for each corner (3 or more) - Enter / Done / double-click finishes, Esc cancels, Backspace removes the last corner";
        }

        private void FinishAreaDraw()
        {
            var st = AreaTool;
            if (!st.Drawing) return;
            st.Drawing = false;
            st.DraftHover = null;
            if (st.Draft.Count < 3) { st.Status = $"area needs at least 3 corners ({st.Draft.Count} clicked) - cancelled"; st.Draft.Clear(); return; }
            var a = new WorldArea { Name = st.NewName(), ZMin = st.DraftZMin, ZMax = st.DraftZMax };
            a.Corners.AddRange(st.Draft);
            st.Draft.Clear();
            st.Add(a);
            RefreshAreaContents(false);
            st.Status = $"{a.Name}: {a.Count} corners, {st.Contents.Count} entities inside";
            Console.WriteLine($"AREA drawn {a.Name}: {a.Count} corners, base z {a.BaseZ:0.##}, {st.Contents.Count} inside");
        }

        private void CancelAreaDraw()
        {
            var st = AreaTool;
            if (!st.Drawing) return;
            st.Drawing = false;
            st.Draft.Clear();
            st.DraftHover = null;
            st.Status = "draw cancelled";
        }

        private bool AreaGroundPoint(Ray ray, out Vector3 point)
        {
            var e = WorldPickPrecise(ray, out float d);
            if (e != null && d > 0.0f && d < 5000.0f) { point = ray.Position + ray.Direction * d; return true; }
            var be = WorldPickEntityBoxes(ray, null, float.MaxValue);
            if (be != null && worldPickEntityDist > 0.0f && worldPickEntityDist < 5000.0f) { point = ray.Position + ray.Direction * worldPickEntityDist; return true; }
            var st = AreaTool;
            float z = st.Draft.Count > 0 ? st.Draft[st.Draft.Count - 1].Z : (st.Current != null && st.Current.Count > 0 ? st.Current.BaseZ : camera.Position.Z - 5.0f);
            if (Math.Abs(ray.Direction.Z) < 1e-4f) { point = Vector3.Zero; return false; }
            float t = (z - ray.Position.Z) / ray.Direction.Z;
            if (t <= 0.0f || t > 5000.0f) { point = Vector3.Zero; return false; }
            point = ray.Position + ray.Direction * t;
            return true;
        }

        private void UpdateAreaDraftHover()
        {
            var st = AreaTool;
            if (ImGuiWantsMouse || orbiting || panning) return;
            double now = clock.Elapsed.TotalSeconds;
            if (now - areaLastHover < 0.05) return;
            areaLastHover = now;
            var p = PointToClient(Cursor.Position);
            if (p.X < 0 || p.Y < 0 || p.X >= ClientSize.Width || p.Y >= ClientSize.Height) { st.DraftHover = null; return; }
            WorldPickScreenToDevice(p.X, p.Y, out float sx, out float sy);
            var ray = camera.GetPickRay(sx, sy, deviceResources.Width, deviceResources.Height);
            st.DraftHover = AreaGroundPoint(ray, out var gp) ? gp : (Vector3?)null;
        }

        private void AreaMouseClick_J5(int x, int y, ref bool handled)
        {
            if (!worldBuilt || panel == null) return;
            var st = AreaTool;
            WorldPickScreenToDevice(x, y, out float sx, out float sy);
            var ray = camera.GetPickRay(sx, sy, deviceResources.Width, deviceResources.Height);
            if (st.Drawing)
            {
                handled = true;
                double now = clock.Elapsed.TotalSeconds;
                int ddx = x - areaLastClickAt.X, ddy = y - areaLastClickAt.Y;
                bool doubleClick = now - areaLastClickTime < 0.4 && ddx * ddx + ddy * ddy < 64;
                areaLastClickTime = now; areaLastClickAt = new System.Drawing.Point(x, y);
                if (doubleClick && st.Draft.Count >= 3) { FinishAreaDraw(); return; }
                if (doubleClick) return;
                if (AreaGroundPoint(ray, out var gp))
                {
                    st.Draft.Add(gp);
                    st.Status = $"corner {st.Draft.Count} at {gp.X:0.##}, {gp.Y:0.##}, {gp.Z:0.##}" + (st.Draft.Count >= 3 ? "  -  Enter / Done / double-click to finish" : "");
                }
                else st.Status = "no ground under the cursor - aim at the map";
                return;
            }
            var a = st.Current;
            if (a == null || !a.Visible || a.Count == 0) return;
            int hit = -1; float best = float.MaxValue;
            for (int i = 0; i < a.Corners.Count; i++)
            {
                var sph = new BoundingSphere(a.Corners[i], AreaHandleRadius(a.Corners[i], camera.Position) * 1.4f);
                if (ray.Intersects(ref sph, out float d) && d < best) { best = d; hit = i; }
            }
            if (hit >= 0) { SelectAreaCorner(hit); handled = true; }
            else if (st.SelectedCorner >= 0) st.SelectedCorner = -1;
        }

        private void SelectAreaCorner(int index)
        {
            var st = AreaTool;
            var a = st.Current;
            if (index < 0 || a == null || index >= a.Corners.Count) { st.SelectedCorner = -1; return; }
            st.SelectedCorner = index;
            WorldEdit.Deselect();
            st.Status = $"corner {index + 1} of {a.Name} - drag the gizmo to move it (height too)";
        }

        private void AreaKeyDown_J5(Keys combo, ref bool handled)
        {
            var st = AreaTool;
            if (st.Drawing)
            {
                if (combo == Keys.Escape) { CancelAreaDraw(); handled = true; return; }
                if (ImGuiWantsKeyboard && !ignoreImGuiKeyboard) return;
                if (combo == Keys.Enter || combo == Keys.Return) { FinishAreaDraw(); handled = true; return; }
                if (combo == Keys.Back) { if (st.Draft.Count > 0) st.Draft.RemoveAt(st.Draft.Count - 1); handled = true; return; }
                return;
            }
            if (combo == Keys.Escape && st.SelectedCorner >= 0 && !WorldEdit.Selection.HasValue) { st.SelectedCorner = -1; handled = true; }
        }

        private void AreaGizmoTargets_J5(List<IWorldGizmoTarget> list)
        {
            var st = AreaTool;
            var a = st.Current;
            if (a == null || st.SelectedCorner < 0 || st.SelectedCorner >= a.Corners.Count) return;
            var t = st.CornerTarget(a, st.SelectedCorner);
            if (t != null) list.Add(t);
        }

        private void AreaTargetChanged_J5(IWorldGizmoTarget t, ref bool handled)
        {
            if (!(t is AreaCornerTarget)) return;
            handled = true;
            AreaTool.AreasDirty = true;
        }

        private void AreaBoxFromSelection()
        {
            var st = AreaTool;
            var s = WorldEdit.Selection;
            BoundingBox b;
            string nm;
            var e = WorldEdit.Selected ?? s.MloEntityDef;
            if (e != null)
            {
                b = new BoundingBox(e.BBMin, e.BBMax);
                if (b.Maximum.X <= b.Minimum.X) { var h = new Vector3(1.0f); b = new BoundingBox(e.Position - h, e.Position + h); }
                nm = "Box " + (e.Archetype?.Name ?? "entity");
            }
            else if (s.HasValue && s.AABB.Maximum.X > s.AABB.Minimum.X) { b = s.AABB; nm = "Box " + s.GetNameString("selection"); }
            else { st.Status = "select an entity first - the box wraps its bounds"; return; }
            var a = WorldArea.FromBox(b, Math.Max(0.0f, st.BoxMargin), nm);
            st.Add(a);
            RefreshAreaContents(false);
            st.Status = $"{a.Name}: {a.Count} corners around the selection, {st.Contents.Count} inside";
        }

        private void RefreshAreaContents(bool quiet)
        {
            var st = AreaTool;
            var a = st.Current;
            areaLastAutoRefresh = clock.Elapsed.TotalSeconds;
            var prevSel = st.SelectedEntry >= 0 && st.SelectedEntry < st.Contents.Count ? st.Contents[st.SelectedEntry].Entity : null;
            st.Contents.Clear();
            st.SelectedEntry = -1;
            st.ContentsInteriorSkipped = 0;
            if (a == null || !a.IsValid || !worldBuilt) return;
            var box = a.Bounds;
            World.SnapshotWalkedYmaps(areaYmaps);
            areaSeen.Clear();
            foreach (var y in areaYmaps)
            {
                if (y == null) continue;
                var yname = y.Name ?? y.RpfFileEntry?.Name ?? "";
                if (st.YmapExcluded(yname)) continue;
                var em = y._CMapData.entitiesExtentsMin; var ex = y._CMapData.entitiesExtentsMax;
                bool hasExt = ex.X > em.X && ex.Y > em.Y;
                if (hasExt && (ex.X < box.Minimum.X || em.X > box.Maximum.X || ex.Y < box.Minimum.Y || em.Y > box.Maximum.Y)) continue;
                var ents = y.AllEntities;
                if (ents != null)
                {
                    foreach (var e in ents)
                    {
                        if (e == null || !areaSeen.Add(e)) continue;
                        if (!st.Passes(a, e, false)) continue;
                        st.Contents.Add(MakeEntry(e, yname, false));
                    }
                }
                if (st.IncludeInterior && y.MloEntities != null)
                {
                    foreach (var mlo in y.MloEntities)
                    {
                        var inst = mlo?.MloInstance;
                        if (inst == null) continue;
                        if (mlo.BBMax.X < box.Minimum.X || mlo.BBMin.X > box.Maximum.X || mlo.BBMax.Y < box.Minimum.Y || mlo.BBMin.Y > box.Maximum.Y) continue;
                        string src = "(interior: " + (mlo.Archetype?.Name ?? "?") + ")";
                        if (inst.Entities != null)
                            foreach (var ie in inst.Entities)
                            {
                                if (ie == null || !areaSeen.Add(ie)) continue;
                                if (!st.Passes(a, ie, true)) continue;
                                st.Contents.Add(MakeEntry(ie, src, true));
                            }
                        if (inst.EntitySets != null)
                            foreach (var set in inst.EntitySets)
                            {
                                if (set?.Entities == null) continue;
                                foreach (var ie in set.Entities)
                                {
                                    if (ie == null || !areaSeen.Add(ie)) continue;
                                    if (!st.Passes(a, ie, true)) continue;
                                    st.Contents.Add(MakeEntry(ie, src + " set", true));
                                }
                            }
                    }
                }
            }
            var c = a.Centre;
            st.Contents.Sort((p, q) => (p.Position - c).LengthSquared().CompareTo((q.Position - c).LengthSquared()));
            foreach (var en in st.Contents) if (en.Interior) st.ContentsInteriorSkipped++;
            if (prevSel != null) st.SelectedEntry = st.Contents.FindIndex(en => ReferenceEquals(en.Entity, prevSel));
            st.ContentsAt = clock.Elapsed.TotalSeconds;
            RefreshAreaGrass_R2();
            if (!quiet) st.Status = $"{st.Contents.Count} entities inside {a.Name}" + (st.ContentsInteriorSkipped > 0 ? $" ({st.ContentsInteriorSkipped} interior props: move only)" : "");
        }

        private static AreaEntry MakeEntry(YmapEntityDef e, string source, bool interior)
        {
            var arch = e.Archetype;
            return new AreaEntry
            {
                Entity = e,
                Name = arch?.Name ?? e._CEntityDef.archetypeName.ToString(),
                Position = e.Position,
                Source = source,
                LodLevel = AreaToolState.LevelName(e),
                Radius = arch != null ? arch.BSRadius * Math.Max(Math.Max(Math.Abs(e.Scale.X), Math.Abs(e.Scale.Y)), Math.Abs(e.Scale.Z)) : 0.0f,
                Interior = interior,
                IsMloInstance = e.MloInstance != null,
            };
        }

        private void AreaDeleteContents()
        {
            var st = AreaTool;
            var a = st.Current;
            if (a == null) { st.Status = "no area selected"; return; }
            if (st.Contents.Count == 0) RefreshAreaContents(true);
            int grassN = AreaDeleteGrass_R2(false, out var grassDo, out var grassUndo, out int grassBatches);
            var snaps = new List<EntitySnapshot>();
            var ymaps = new List<YmapFile>();
            var parents = new List<YmapEntityDef>();
            var parentIdx = new List<int>();
            var flags = new List<uint>();
            var current = new List<YmapEntityDef>();
            int interior = 0;
            foreach (var en in st.Contents)
            {
                var e = en.Entity;
                if (e == null) continue;
                if (e.Ymap == null) { interior++; continue; }
                if (e.Ymap.AllEntities == null || Array.IndexOf(e.Ymap.AllEntities, e) < 0) continue;
                snaps.Add(EntityOps.Capture(e));
                ymaps.Add(e.Ymap);
                parents.Add(e.Parent);
                parentIdx.Add(e._CEntityDef.parentIndex);
                flags.Add(e._CEntityDef.flags);
                current.Add(e);
            }
            if (current.Count == 0)
            {
                if (grassN > 0)
                {
                    grassDo();
                    WorldHistory.Push(new DelegateCommand($"Delete {grassN} grass in area {a.Name}", grassDo, grassUndo));
                    RefreshAreaContents(true);
                    st.Status = $"deleted {grassN} grass instance{(grassN == 1 ? "" : "s")}" + (grassBatches > 0 ? $" ({grassBatches} empty batch{(grassBatches == 1 ? "" : "es")} removed)" : "") + " - Ctrl+Z restores";
                    Console.WriteLine($"AREA delete {a.Name}: no entities, {grassN} grass instances, {grassBatches} batches removed");
                    return;
                }
                st.Status = interior > 0 ? $"nothing deletable: {interior} interior props (move them, or delete them in the Project window)" : "nothing inside the area to delete";
                return;
            }
            var touched = new HashSet<YmapFile>();
            var cache = gameFiles?.Cache;
            string name = a.Name;
            int n = current.Count;

            void DoIt()
            {
                touched.Clear();
                for (int i = 0; i < current.Count; i++)
                {
                    var e = current[i]; var y = ymaps[i];
                    if (e == null || y == null) continue;
                    if (e.Parent?.Ymap != null && e.Parent.Ymap != y) touched.Add(e.Parent.Ymap);
                    touched.Add(y);
                    WorldEdit.MarkDirty(y);
                    if (!EntityOps.Remove(y, e)) continue;
                    WorldEntityChanged(e);
                    if (ReferenceEquals(WorldEdit.Selected, e)) WorldEdit.Deselect();
                }
                foreach (var y in touched) { WorldEdit.MarkDirty(y); ProjectAutoAddYmap(y, null); }
                grassDo?.Invoke();
                World.Invalidate();
            }
            void UndoIt()
            {
                for (int i = current.Count - 1; i >= 0; i--)
                {
                    var y = ymaps[i];
                    var cur = EntityOps.Restore(snaps[i], y, cache);
                    if (cur == null || !EntityOps.Add(y, cur, cache)) { current[i] = null; continue; }
                    AreaRelinkParent(cur, parents[i], parentIdx[i], flags[i]);
                    current[i] = cur;
                    WorldEntityChanged(cur);
                }
                foreach (var y in touched) WorldEdit.MarkDirty(y);
                grassUndo?.Invoke();
                World.Invalidate();
            }

            DoIt();
            WorldHistory.Push(new DelegateCommand(grassN > 0 ? $"Delete {n} + {grassN} grass in area {name}" : $"Delete {n} in area {name}", DoIt, UndoIt));
            st.LastDirtyYmaps = touched.Count;
            RefreshAreaContents(true);
            st.Status = $"deleted {n} entit{(n == 1 ? "y" : "ies")} in {name} ({touched.Count} ymap{(touched.Count == 1 ? "" : "s")} edited)" +
                        (grassN > 0 ? $" + {grassN} grass instance{(grassN == 1 ? "" : "s")}" + (grassBatches > 0 ? $" ({grassBatches} empty batch{(grassBatches == 1 ? "" : "es")} removed)" : "") : "") +
                        (interior > 0 ? $"; {interior} interior props left (move only)" : "") + " - Ctrl+Z restores";
            Console.WriteLine($"AREA delete {name}: {n} removed, dirty ymaps [{string.Join(", ", touched.Select(y => y.Name))}]" + (interior > 0 ? $", {interior} interior skipped" : ""));
        }

        private static void AreaRelinkParent(YmapEntityDef child, YmapEntityDef parent, int parentIndex, uint flags)
        {
            if (child?.Ymap == null || parent?.Ymap == null) return;
            if (parent.Ymap == child.Ymap) { EntityOps.SetParent(child, parent); return; }
            child._CEntityDef.parentIndex = parent.Index;
            child._CEntityDef.flags = flags;
            child.Parent = parent;
            child.ParentName = parent._CEntityDef.archetypeName;
            var merged = parent.ChildrenMerged ?? parent.Children;
            if (merged == null || Array.IndexOf(merged, child) < 0)
            {
                var next = new YmapEntityDef[(merged?.Length ?? 0) + 1];
                if (merged != null) Array.Copy(merged, next, merged.Length);
                next[next.Length - 1] = child;
                parent.ChildrenMerged = next;
            }
            parent._CEntityDef.numChildren = parent._CEntityDef.numChildren + 1;
            parent.Ymap.HasChanged = true;
            parent.Ymap.LodManagerUpdate = true;
            child.Ymap.LodManagerUpdate = true;
        }

        private void AreaMoveContents(Vector3 offset)
        {
            var st = AreaTool;
            var a = st.Current;
            if (a == null) { st.Status = "no area selected"; return; }
            if (st.Contents.Count == 0) RefreshAreaContents(true);
            if (offset.LengthSquared() < 1e-8f) { st.Status = "the offset is zero - nothing to move"; return; }
            var list = new List<YmapEntityDef>();
            foreach (var en in st.Contents) if (en.Entity != null && (en.Entity.Ymap != null || en.Entity.MloParent != null)) list.Add(en.Entity);
            if (list.Count == 0) { st.Status = "nothing inside the area to move"; return; }
            var pend = EntityTransformCommand.Begin($"Move {list.Count} in area {a.Name}", list, WorldEntityChanged);
            var touched = new HashSet<string>();
            foreach (var e in list)
            {
                e.SetPosition(e.Position + offset);
                WorldEntityChanged(e);
                touched.Add(e.Ymap?.Name ?? ("ytyp " + (e.MloParent?.Archetype?.Ytyp?.Name ?? "?")));
            }
            var cmd = pend.Complete();
            if (cmd != null) WorldHistory.Push(cmd);
            World.Invalidate();
            st.LastDirtyYmaps = touched.Count;
            RefreshAreaContents(true);
            st.Status = $"moved {list.Count} entit{(list.Count == 1 ? "y" : "ies")} by ({offset.X:0.##}, {offset.Y:0.##}, {offset.Z:0.##}) - {touched.Count} file{(touched.Count == 1 ? "" : "s")} edited, Ctrl+Z restores";
            Console.WriteLine($"AREA move {a.Name}: {list.Count} moved by {offset}, files [{string.Join(", ", touched)}]");
        }

        private void FrameArea(WorldArea a)
        {
            if (a == null || a.Count == 0) return;
            var c = a.Centre;
            float size = Math.Max(a.Size, 6.0f);
            var eye = c + new Vector3(0, -size * 1.1f, size * 0.9f + 3.0f);
            var f = Vector3.Normalize(c - eye);
            float yaw = (float)Math.Atan2(-f.Y, -f.X);
            float pitch = (float)Math.Asin(MathUtil.Clamp(-f.Z, -1.0f, 1.0f));
            CameraSequence.ApplyToCamera(camera, eye, yaw, pitch, settings.FovDeg);
        }

        private void AreaSaveListDialog()
        {
            var st = AreaTool;
            var a = st.Current;
            if (a == null) { st.Status = "no area selected"; return; }
            if (st.Contents.Count == 0) RefreshAreaContents(true);
            using var dlg = new SaveFileDialog { Filter = "Text files|*.txt|All files|*.*", FileName = SafeFileName(a.Name) + "_contents.txt" };
            var dir = ProjWin?.Project?.Directory;
            if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, AreaToolState.ContentsText(a, st.Contents)); st.Status = $"wrote {st.Contents.Count} lines to {Path.GetFileName(dlg.FileName)}"; }
            catch (Exception ex) { st.Status = "save failed: " + ex.Message; }
        }

        private static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "area" : s.Trim();
        }

        private void AreaSaveAreasDialog()
        {
            var st = AreaTool;
            using var dlg = new SaveFileDialog { Filter = "Area files|*.areas.xml|All files|*.*", FileName = (ProjWin?.Project?.Name ?? "world") + ".areas.xml" };
            var dir = ProjWin?.Project?.Directory;
            if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { AreaToolState.Save(dlg.FileName, st.Areas); st.AreasPath = dlg.FileName; st.Status = $"saved {st.Areas.Count} area(s) to {Path.GetFileName(dlg.FileName)}"; }
            catch (Exception ex) { st.Status = "save failed: " + ex.Message; }
        }

        private void AreaLoadAreasDialog()
        {
            var st = AreaTool;
            using var dlg = new OpenFileDialog { Filter = "Area files|*.areas.xml;*.xml|All files|*.*" };
            var dir = ProjWin?.Project?.Directory;
            if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            int added = MergeAreasFrom(dlg.FileName);
            st.Status = $"loaded {added} area(s) from {Path.GetFileName(dlg.FileName)}";
        }

        private int MergeAreasFrom(string path)
        {
            var st = AreaTool;
            List<WorldArea> list;
            try { list = AreaToolState.Load(path); }
            catch (Exception ex) { st.Status = "could not read areas: " + ex.Message; return 0; }
            int added = 0;
            foreach (var a in list)
            {
                if (st.Areas.Any(x => string.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase) && x.Count == a.Count)) continue;
                st.Areas.Add(a); added++;
            }
            if (added > 0 && st.Selected < 0) st.Selected = 0;
            st.AreasPath = path;
            return added;
        }

        private void SaveAreasBeside(string projectPath, bool loud)
        {
            var st = AreaTool;
            var side = AreaToolState.SidePathFor(projectPath);
            if (side == null) return;
            areaLastSideSave = clock.Elapsed.TotalSeconds;
            try
            {
                if (st.Areas.Count == 0 && !File.Exists(side)) { st.AreasDirty = false; return; }
                AreaToolState.Save(side, st.Areas);
                st.AreasDirty = false;
                st.AreasPath = side;
                if (loud) st.Status = $"areas saved beside the project ({Path.GetFileName(side)})";
            }
            catch (Exception ex) { st.Status = "areas file: " + ex.Message; }
        }

        private void SyncAreaProject()
        {
            var st = AreaTool;
            var p = ProjWin?.Project;
            var path = p?.Filepath;
            st.HasProjectFile = !string.IsNullOrEmpty(path);
            if (!ReferenceEquals(p, areaLastProject) || !string.Equals(path, areaLastProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                bool newObject = !ReferenceEquals(p, areaLastProject);
                if (p != null && !string.IsNullOrEmpty(path))
                {
                    var side = AreaToolState.SidePathFor(path);
                    if (newObject && File.Exists(side))
                    {
                        int added = MergeAreasFrom(side);
                        if (added > 0) { st.Status = $"{added} area(s) loaded from {Path.GetFileName(side)}"; Console.WriteLine($"AREA loaded {added} from {side}"); }
                    }
                    else if (!newObject && st.Areas.Count > 0) st.AreasDirty = true;
                }
                areaLastProject = p;
                areaLastProjectPath = path;
            }
        }

        private void DrawAreaHelpers_J5()
        {
            if (panel == null || !panel.WorldMode || !worldBuilt) return;
            var st = AreaTool;
            var camPos = camera.Position;
            for (int i = 0; i < st.Areas.Count; i++)
            {
                var a = st.Areas[i];
                if (a == null || !a.Visible || a.Count == 0) continue;
                DrawOneArea(a, i == st.Selected, camPos);
            }
            if (st.Drawing) DrawAreaDraft(camPos);
            if (st.Highlight && st.TabActive && st.Current != null && st.Contents.Count > 0)
            {
                var col = T(UiTheme.Warn, 0.85f);
                var selCol = SelColour;
                int shown = 0;
                for (int i = 0; i < st.Contents.Count && shown < 3000; i++)
                {
                    var e = st.Contents[i].Entity;
                    if (e == null || (e.Ymap == null && e.MloParent == null)) continue;
                    DrawEntityBox(e, i == st.SelectedEntry ? selCol : col);
                    shown++;
                }
            }
        }

        private void DrawOneArea(WorldArea a, bool selected, Vector3 camPos)
        {
            float lineA = selected ? 1.0f : 0.55f;
            var line = T(UiTheme.Accent, lineA);
            var lineTop = T(UiTheme.AccentBright, selected ? 0.75f : 0.35f);
            var wall = TF(UiTheme.Accent, selected ? 0.16f : 0.07f);
            var cap = TF(UiTheme.Accent, selected ? 0.26f : 0.12f);
            float bottom = a.BottomZ, top = a.TopZ;
            int n = a.Corners.Count;
            for (int i = 0; i < n; i++)
            {
                var p = a.Corners[i]; var q = a.Corners[(i + 1) % n];
                if (n >= 2)
                {
                    lineRenderer.AddLine(p, q, line);
                    if (selected)
                    {
                        float hw = Math.Max(0.03f, ((p + q) * 0.5f - camPos).Length() * 0.0022f);
                        triRenderer.AddThickLine(p + new Vector3(0, 0, 0.05f), q + new Vector3(0, 0, 0.05f), camPos, hw, T(UiTheme.AccentBright, 0.9f));
                    }
                }
                if (n >= 3)
                {
                    var pb = new Vector3(p.X, p.Y, bottom); var qb = new Vector3(q.X, q.Y, bottom);
                    var pt = new Vector3(p.X, p.Y, top); var qt = new Vector3(q.X, q.Y, top);
                    lineRenderer.AddLine(pt, qt, lineTop);
                    lineRenderer.AddLine(pb, qb, lineTop);
                    lineRenderer.AddLine(pb, pt, lineTop);
                    triRenderer.AddQuad(pb, qb, qt, pt, wall);
                    triRenderer.AddQuad(pt, qt, qb, pb, wall);
                }
            }
            if (n >= 3)
            {
                var tris = a.Triangulate();
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    var p0 = a.Corners[tris[i]] + new Vector3(0, 0, 0.04f);
                    var p1 = a.Corners[tris[i + 1]] + new Vector3(0, 0, 0.04f);
                    var p2 = a.Corners[tris[i + 2]] + new Vector3(0, 0, 0.04f);
                    triRenderer.AddTri(p0, p1, p2, cap);
                    triRenderer.AddTri(p0, p2, p1, cap);
                }
            }
            for (int i = 0; i < n; i++)
            {
                var c = a.Corners[i];
                bool sel = selected && i == AreaTool.SelectedCorner;
                float r = AreaHandleRadius(c, camPos);
                lineRenderer.AddSphere(c, r, sel ? SelColour : (selected ? T(UiTheme.AccentBright, 0.95f) : line), 12);
                if (sel) lineRenderer.AddSphere(c, r * 1.6f, SelColourSoft, 16);
            }
            if (n >= 1)
            {
                var lc = a.Centre + new Vector3(0, 0, 1.2f);
                string txt = a.Name + (selected && AreaTool.Contents.Count > 0 ? $"  ({AreaTool.Contents.Count})" : "");
                DrawWorldLabel(lc, txt, selected ? new Vector4(1.0f, 1.0f, 1.0f, 0.95f) : new Vector4(0.85f, 0.9f, 1.0f, 0.75f));
            }
        }

        private void DrawAreaDraft(Vector3 camPos)
        {
            var st = AreaTool;
            var col = T(UiTheme.AccentBright, 1.0f);
            var soft = T(UiTheme.AccentBright, 0.45f);
            var pts = st.Draft;
            for (int i = 0; i < pts.Count; i++)
            {
                lineRenderer.AddSphere(pts[i], AreaHandleRadius(pts[i], camPos), col, 12);
                if (i > 0) lineRenderer.AddLine(pts[i - 1], pts[i], col);
            }
            if (st.DraftHover.HasValue)
            {
                var h = st.DraftHover.Value;
                lineRenderer.AddSphere(h, AreaHandleRadius(h, camPos) * 0.8f, soft, 10);
                if (pts.Count > 0) lineRenderer.AddLine(pts[pts.Count - 1], h, soft);
                if (pts.Count >= 2) lineRenderer.AddLine(h, pts[0], soft);
            }
            else if (pts.Count >= 3) lineRenderer.AddLine(pts[pts.Count - 1], pts[0], soft);
            if (pts.Count > 0)
                DrawWorldLabel(pts[pts.Count - 1] + new Vector3(0, 0, 1.0f), $"corner {pts.Count}" + (pts.Count >= 3 ? " - Enter / double-click to finish" : ""), new Vector4(1, 1, 1, 0.95f));
        }

        private void AreaGroundCorners(WorldArea a)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < a.Corners.Count; i++)
            {
                var c = a.Corners[i];
                var ray = new Ray(new Vector3(c.X, c.Y, camera.Position.Z + 150.0f), -Vector3.UnitZ);
                var e = WorldPickPrecise(ray, out float d);
                if (e != null && d < 5000.0f) lowest = Math.Min(lowest, ray.Position.Z - d);
            }
            float z = lowest < float.MaxValue ? lowest : camera.Position.Z - 3.0f;
            for (int i = 0; i < a.Corners.Count; i++) a.Corners[i] = new Vector3(a.Corners[i].X, a.Corners[i].Y, z);
        }

        private void ApplyAreaEnv()
        {
            var spec = Environment.GetEnvironmentVariable("RLE_AREA");
            if (string.IsNullOrEmpty(spec)) return;
            var st = AreaTool;
            WorldArea a;
            if (spec.StartsWith("clicks:", StringComparison.OrdinalIgnoreCase))
            {
                StartAreaDraw();
                foreach (var p in spec.Substring(7).Split(';'))
                {
                    var xy = p.Split(',');
                    if (xy.Length < 2 || !float.TryParse(xy[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx)
                                      || !float.TryParse(xy[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fy)) continue;
                    int px = fx <= 1.0f ? (int)(fx * ClientSize.Width) : (int)fx;
                    int py = fy <= 1.0f ? (int)(fy * ClientSize.Height) : (int)fy;
                    bool h = false;
                    areaLastClickTime = -10;
                    AreaMouseClick_J5(px, py, ref h);
                    Console.WriteLine($"AREA click {px},{py}: {st.Status}");
                }
                FinishAreaDraw();
                a = st.Current;
                if (a == null) { Console.WriteLine("AREA clicks made no area: " + st.Status); return; }
                if (int.TryParse(Environment.GetEnvironmentVariable("RLE_AREA_CORNER"), out int ci)) SelectAreaCorner(ci);
            }
            else
            {
                a = AreaToolState.ParseEnv(spec, out _, out _);
                if (a == null) { Console.WriteLine("AREA spec did not parse: " + spec); return; }
                AreaGroundCorners(a);
                st.Add(a);
            }
            st.RequestShowTab = true;
            st.TabActive = true;
            RefreshAreaContents(false);
            Console.WriteLine($"AREA {a.Name}: {a.Count} corners base z {a.BaseZ:0.##} range {a.ZMin:0.#}..{a.ZMax:0.#}, {st.Contents.Count} entities inside (walked ymaps {World.WalkedYmapCount})");
            int shown = 0;
            foreach (var en in st.Contents)
            {
                if (shown++ >= 80) { Console.WriteLine($"AREA ... {st.Contents.Count - 80} more"); break; }
                Console.WriteLine($"AREA  {en.Name} pos={en.Position.X:0.##},{en.Position.Y:0.##},{en.Position.Z:0.##} lod={en.LodLevel} r={en.Radius:0.#} {en.Source}");
            }
            var listPath = Environment.GetEnvironmentVariable("RLE_AREA_LIST");
            if (!string.IsNullOrEmpty(listPath))
            {
                try { File.WriteAllText(listPath, AreaToolState.ContentsText(a, st.Contents)); Console.WriteLine("AREA list written: " + listPath); }
                catch (Exception ex) { Console.WriteLine("AREA list failed: " + ex.Message); }
            }
            Console.WriteLine(AreaGrassLine_R2("before"));
            var action = Environment.GetEnvironmentVariable("RLE_AREA_ACTION") ?? "list";
            projectAutoAddTestOverride = true;
            if (action.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
            {
                AreaDeleteContents();
                Console.WriteLine($"AREA after delete: {st.Contents.Count} inside, dirty ymaps {WorldEdit.DirtyCount} [{string.Join(", ", WorldEdit.Dirty.Select(y => y.Name))}], project ymaps {ProjWin.Project?.YmapFiles.Count ?? 0}, undo '{WorldHistory.NextUndoName}'");
                Console.WriteLine(AreaGrassLine_R2("after delete"));
                if (action.EndsWith("+undo", StringComparison.OrdinalIgnoreCase))
                {
                    TryWorldUndo();
                    RefreshAreaContents(true);
                    Console.WriteLine($"AREA after undo: {st.Contents.Count} inside");
                    Console.WriteLine(AreaGrassLine_R2("after undo"));
                }
            }
            else if (action.StartsWith("move", StringComparison.OrdinalIgnoreCase))
            {
                var off = new Vector3(0, 0, -100.0f);
                int colon = action.IndexOf(':');
                if (colon >= 0)
                {
                    var xyz = action.Substring(colon + 1).Split(',');
                    if (xyz.Length == 3 && float.TryParse(xyz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ox)
                        && float.TryParse(xyz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float oy)
                        && float.TryParse(xyz[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float oz)) off = new Vector3(ox, oy, oz);
                }
                st.MoveOffset = off;
                var before = st.Contents.Select(en => (en.Entity, en.Position)).ToList();
                AreaMoveContents(off);
                int movedOk = 0;
                foreach (var (e, p) in before) if (e != null && (e.Position - (p + off)).Length() < 0.01f) movedOk++;
                Console.WriteLine($"AREA after move: {movedOk}/{before.Count} at the new position, {st.Contents.Count} still inside, dirty ymaps {WorldEdit.DirtyCount} [{string.Join(", ", WorldEdit.Dirty.Select(y => y.Name))}], undo '{WorldHistory.NextUndoName}'");
            }
            projectAutoAddTestOverride = false;
            if (ProjWin != null && !action.Equals("list", StringComparison.OrdinalIgnoreCase)) ProjWin.Visible = false;
            screenshotFrames = Math.Max(screenshotFrames, 6);
        }

        partial void RunWorldTestExtras_Area(Action<string, bool, string> check, Action<Vector3> settle)
        {
            Console.WriteLine("---- WS-J5 area tool ----");
            var spot = new Vector3(195.0f, -935.0f, 30.0f);
            settle(spot);
            var st = AreaTool;
            YmapEntityDef prop = null; float best = float.MaxValue;
            foreach (var v in World.Visible)
            {
                if (v?.Ymap == null || v.Archetype == null || v.MloInstance != null || v.MloParent != null) continue;
                var lvl = v._CEntityDef.lodLevel;
                if (lvl != rage__eLodType.LODTYPES_DEPTH_HD && lvl != rage__eLodType.LODTYPES_DEPTH_ORPHANHD) continue;
                if (v.Archetype.BSRadius > 4.0f || v.Archetype.BSRadius < 0.3f) continue;
                if (v._CEntityDef.numChildren != 0) continue;
                float d = (v.Position - spot).Length();
                if (d < best) { best = d; prop = v; }
            }
            check("a small HD prop is resident near Legion Square", prop != null, prop != null ? $"{prop.Archetype.Name} at {best:0} m in {prop.Ymap.Name}" : "none");
            if (prop == null) return;

            var ymap = prop.Ymap;
            var archName = prop.Archetype.Name;
            var pos0 = prop.Position;
            var a = WorldArea.FromBox(new BoundingBox(prop.BBMin, prop.BBMax), 3.0f, "Test area");
            st.Areas.Clear(); st.Selected = -1;
            st.Add(a);
            RefreshAreaContents(false);
            check("the list finds the prop inside the box", st.Contents.Any(en => ReferenceEquals(en.Entity, prop)), $"{st.Contents.Count} inside: {string.Join(", ", st.Contents.Take(6).Select(en => en.Name))}");
            check("nothing outside the box is listed", st.Contents.All(en => a.Contains(en.Position) || a.Contains((en.Entity.BBMin + en.Entity.BBMax) * 0.5f)), $"{st.Contents.Count} entries");
            st.NameFilter = archName;
            RefreshAreaContents(true);
            check("the name filter narrows the list", st.Contents.Count >= 1 && st.Contents.All(en => en.Name.IndexOf(archName, StringComparison.OrdinalIgnoreCase) >= 0), $"{st.Contents.Count} match '{archName}'");
            st.NameFilter = "";
            st.ExcludeYmaps = Path.GetFileNameWithoutExtension(ymap.Name ?? "");
            RefreshAreaContents(true);
            check("excluding the ymap by name drops its entities", !st.Contents.Any(en => ReferenceEquals(en.Entity, prop)), $"{st.Contents.Count} left");
            st.ExcludeYmaps = "";
            RefreshAreaContents(true);
            int countBefore = st.Contents.Count;
            int entsBefore = ymap.AllEntities.Length;

            var hadProject = ProjWin.Project != null;
            projectAutoAddTestOverride = true;
            WorldHistory.Clear();
            AreaDeleteContents();
            projectAutoAddTestOverride = false;
            check("Delete inside removes the prop from its ymap", Array.IndexOf(ymap.AllEntities, prop) < 0 && ymap.AllEntities.Length < entsBefore, $"{entsBefore} -> {ymap.AllEntities.Length} entities");
            check("the ymap is dirty and in the project", WorldEdit.IsDirty(ymap) && ProjWin.Project != null && ProjWin.Project.ContainsYmap(ymap), $"dirty {WorldEdit.IsDirty(ymap)} project {ProjWin.Project?.ContainsYmap(ymap)}");
            check("the delete is one undo step", WorldHistory.CanUndo && (WorldHistory.NextUndoName ?? "").StartsWith("Delete"), WorldHistory.NextUndoName ?? "none");
            settle(spot);
            check("the world draws fewer: the prop is out of the visible set", !World.Visible.Contains(prop), $"{World.Visible.Count} visible");
            check("the list is empty of it after the delete", !st.Contents.Any(en => ReferenceEquals(en.Entity, prop)), $"{st.Contents.Count} inside");
            TryWorldUndo();
            RefreshAreaContents(true);
            var back = ymap.AllEntities.FirstOrDefault(x => x?.Archetype?.Name == archName && (x.Position - pos0).Length() < 0.01f);
            check("undo restores the prop into the ymap where it was", back != null && ymap.AllEntities.Length == entsBefore, back != null ? $"{back.Archetype.Name} at {back.Position}" : $"missing ({ymap.AllEntities.Length} entities)");
            check("the list finds it again", back != null && st.Contents.Any(en => ReferenceEquals(en.Entity, back)), $"{st.Contents.Count} inside");
            settle(spot);
            check("the restored prop is visible again", back != null && World.Visible.Contains(back), $"{World.Visible.Count} visible");

            if (back != null)
            {
                RefreshAreaContents(true);
                var mv = new Vector3(0, 0, -100.0f);
                projectAutoAddTestOverride = true;
                AreaMoveContents(mv);
                projectAutoAddTestOverride = false;
                check("Move inside shifts the prop down 100 m", (back.Position - (pos0 + mv)).Length() < 0.01f, $"{back.Position} vs {pos0 + mv}");
                check("the moved prop leaves the prism (list drops it)", !st.Contents.Any(en => ReferenceEquals(en.Entity, back)), $"{st.Contents.Count} inside");
                check("the move is one undo step", (WorldHistory.NextUndoName ?? "").StartsWith("Move"), WorldHistory.NextUndoName ?? "none");
                TryWorldUndo();
                check("undo puts it back", (back.Position - pos0).Length() < 0.01f, back.Position.ToString());
            }

            {
                var dir = Path.Combine(Path.GetTempPath(), "rle_area_project");
                try { Directory.Delete(dir, true); } catch { }
                Directory.CreateDirectory(dir);
                var ppath = Path.Combine(dir, "areatest.cwproj");
                var proj = new CwProject { Name = "areatest" };
                proj.Save(ppath);
                SaveAreasBeside(ppath, false);
                var side = AreaToolState.SidePathFor(ppath);
                check("the areas file is written beside the .cwproj", File.Exists(side), side);
                st.Areas.Clear(); st.Selected = -1;
                areaLastProject = null; areaLastProjectPath = null;
                var prevProject = ProjWin.Project;
                ProjWin.Project = CwProject.Load(ppath);
                SyncAreaProject();
                var loaded = st.Areas.FirstOrDefault(x => x.Name == "Test area");
                check("reopening the project brings the area back", loaded != null && loaded.Count == 4 && (loaded.Corners[0] - a.Corners[0]).Length() < 0.01f && Math.Abs(loaded.ZMax - a.ZMax) < 0.01f,
                      loaded != null ? $"{loaded.Name}: {loaded.Count} corners, z {loaded.ZMin:0.#}..{loaded.ZMax:0.#}" : $"{st.Areas.Count} areas");
                ProjWin.Project = prevProject;
                areaLastProject = prevProject; areaLastProjectPath = prevProject?.Filepath;
                try { Directory.Delete(dir, true); } catch { }
            }

            {
                RefreshAreaContents(true);
                var txt = AreaToolState.ContentsText(a, st.Contents);
                check("the saved list names the prop and its ymap", txt.Contains(archName) && txt.Contains(ymap.Name ?? "?"), $"{txt.Split('\n').Length} lines");
            }

            st.Areas.Clear(); st.Selected = -1; st.Contents.Clear();
            ProjWin.Project?.RemoveYmapFile(ymap);
            ymap.HasChanged = false;
            if (!hadProject) { ProjWin.Project = null; ProjWin.Select(null); ProjWin.Visible = false; }
            RebuildProjectOverrides();
            WorldHistory.Clear();
        }
    }
}

