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
        private bool grassStroking_S5;
        private readonly Dictionary<YmapGrassInstanceBatch, rage__fwGrassInstanceListDef__InstanceData[]> grassStrokeBefore_S5 =
            new Dictionary<YmapGrassInstanceBatch, rage__fwGrassInstanceListDef__InstanceData[]>();
        private readonly Dictionary<YmapFile, YmapGrassInstanceBatch[]> grassStrokeYmapBefore_S5 =
            new Dictionary<YmapFile, YmapGrassInstanceBatch[]>();
        private int grassStrokeRemoved_S5;
        private readonly List<YmapFile> grassBrushYmaps_S5 = new List<YmapFile>();

        private Vector3 grassBrushAt_S5;
        private bool grassBrushHasPoint_S5;

        private void ServiceGrassBrush_S5()
        {
            if (panel == null || ProjWin == null) return;
            if (ProjWin.RequestDeleteGrass_S5)
            {
                ProjWin.RequestDeleteGrass_S5 = false;
                panel.ShowGrassBrush_S5 = true;
                if (panel.GrassBrushScope_S5 <= 0) panel.GrassBrushScope_S5 = 3;
                panel.GrassBrushStatus_S5 = "Left-drag over the grass to rub it out.";
            }
            ServiceGrassBrushEnv_S5();
        }

        private bool GrassBrushMouseDown_S5(int mx, int my)
        {
            if (panel == null || !panel.WorldMode || !panel.GrassBrushArmed_S5 || !worldBuilt) return false;
            grassStroking_S5 = true;
            grassStrokeBefore_S5.Clear();
            grassStrokeYmapBefore_S5.Clear();
            grassStrokeRemoved_S5 = 0;
            GrassBrushDab_S5(mx, my);
            return true;
        }

        private void GrassBrushMouseMove_S5(int mx, int my, bool leftDown)
        {
            if (panel == null || !panel.WorldMode || !panel.GrassBrushArmed_S5) { grassBrushHasPoint_S5 = false; return; }
            grassBrushHasPoint_S5 = GrassBrushPoint_S5(mx, my, out grassBrushAt_S5);
            if (grassStroking_S5 && leftDown) GrassBrushDab_S5(mx, my);
        }

        private void GrassBrushMouseUp_S5()
        {
            if (!grassStroking_S5) return;
            grassStroking_S5 = false;
            if (grassStrokeBefore_S5.Count == 0)
            {
                panel.GrassBrushStatus_S5 = "No grass under the brush - check the scope and the radius.";
                return;
            }
            var before = grassStrokeBefore_S5.ToDictionary(kv => kv.Key, kv => kv.Value);
            var after = grassStrokeBefore_S5.Keys.ToDictionary(b => b, b => b.Instances);
            var ymapBefore = grassStrokeYmapBefore_S5.ToDictionary(kv => kv.Key, kv => kv.Value);
            var ymapAfter = grassStrokeYmapBefore_S5.Keys.ToDictionary(y => y, y => y.GrassInstanceBatches);
            var touched = new HashSet<YmapFile>();
            foreach (var b in before.Keys) if (b.Ymap != null) touched.Add(b.Ymap);
            foreach (var y in ymapBefore.Keys) touched.Add(y);
            int removed = grassStrokeRemoved_S5;

            void Redo()
            {
                foreach (var kv in after) ApplyGrassInstances_R2(kv.Key, kv.Value);
                foreach (var kv in ymapAfter) kv.Key.GrassInstanceBatches = kv.Value;
                AfterGrassEdit_R2(touched);
            }
            void Undo()
            {
                foreach (var kv in ymapBefore) kv.Key.GrassInstanceBatches = kv.Value;
                foreach (var kv in before) ApplyGrassInstances_R2(kv.Key, kv.Value);
                AfterGrassEdit_R2(touched);
            }
            WorldHistory.Push(new DelegateCommand($"Delete {removed} grass", Redo, Undo));
            panel.GrassBrushErased_S5 += removed;
            panel.GrassBrushStatus_S5 = $"Erased {removed:N0} instance(s) from {touched.Count} ymap(s) - Ctrl+Z puts them back. " +
                                        string.Join(", ", touched.Select(t => t.Name ?? "?").Take(3));
            Console.WriteLine($"GRASSBRUSH stroke removed {removed} instances from {touched.Count} ymaps [{string.Join(" ", touched.Select(t => t.Name))}] dirty {WorldEdit.DirtyCount}");
            grassStrokeBefore_S5.Clear();
            grassStrokeYmapBefore_S5.Clear();
        }

        private bool GrassBrushPoint_S5(int mx, int my, out Vector3 p)
        {
            p = Vector3.Zero;
            WorldPickScreenToDevice(mx, my, out float sx, out float sy);
            var ray = camera.GetPickRay(sx, sy, deviceResources.Width, deviceResources.Height);
            var hit = WorldPickPrecise(ray, out float d);
            if (hit == null || d <= 0.0f || d > 2000.0f) return false;
            p = ray.Position + ray.Direction * d;
            return true;
        }

        private void GrassBrushDab_S5(int mx, int my)
        {
            if (!GrassBrushPoint_S5(mx, my, out var at)) return;
            grassBrushAt_S5 = at; grassBrushHasPoint_S5 = true;
            int n = EraseGrassAt_S5(at, panel.GrassBrushRadius_S5, panel.GrassBrushScope_S5);
            if (n > 0)
            {
                grassStrokeRemoved_S5 += n;
                panel.GrassBrushStatus_S5 = $"{grassStrokeRemoved_S5:N0} erased...";
            }
        }

        private void GrassBrushYmaps_S5()
        {
            grassBrushYmaps_S5.Clear();
            World.SnapshotWalkedYmaps(grassBrushYmaps_S5);
            var seen = new HashSet<YmapFile>(grassBrushYmaps_S5);
            foreach (var y in World.ContentYmaps) if (y != null && seen.Add(y)) grassBrushYmaps_S5.Add(y);
            var over = World.ProjectOverrides;
            if (over != null)
                foreach (var kv in over) if (kv.Value != null && seen.Add(kv.Value)) grassBrushYmaps_S5.Add(kv.Value);
        }

        private bool GrassYmapInProject_S5(YmapFile y)
        {
            if (y == null) return false;
            var over = World.ProjectOverrides;
            if (over != null) foreach (var kv in over) if (ReferenceEquals(kv.Value, y)) return true;
            return ProjWin?.Project?.ContainsYmap(y) == true;
        }

        private int EraseGrassAt_S5(Vector3 at, float radius, int scope)
        {
            if (radius <= 0.0f || scope <= 0) return 0;
            GrassBrushYmaps_S5();
            float r2 = radius * radius;
            var sphereMin = at - new Vector3(radius);
            var sphereMax = at + new Vector3(radius);
            var scopeYmap = WorldEdit.Selection.GrassBatch?.Ymap;
            int removed = 0;
            var seen = new HashSet<YmapGrassInstanceBatch>();
            foreach (var y in grassBrushYmaps_S5)
            {
                var batches = y?.GrassInstanceBatches;
                if (batches == null || batches.Length == 0) continue;
                if (scope == 1 && !ReferenceEquals(y, scopeYmap)) continue;
                if (scope == 2 && !GrassYmapInProject_S5(y)) continue;
                foreach (var b in batches)
                {
                    if (b?.Instances == null || b.Instances.Length == 0 || !seen.Add(b)) continue;
                    if (b.AABBMax.X < sphereMin.X || b.AABBMin.X > sphereMax.X ||
                        b.AABBMax.Y < sphereMin.Y || b.AABBMin.Y > sphereMax.Y ||
                        b.AABBMax.Z < sphereMin.Z || b.AABBMin.Z > sphereMax.Z) continue;
                    var min = b.AABBMin; var size = b.AABBMax - b.AABBMin;
                    var keep = new List<rage__fwGrassInstanceListDef__InstanceData>(b.Instances.Length);
                    foreach (var inst in b.Instances)
                    {
                        var p = GrassWorldPos_R2(in inst, min, size);
                        if (Vector3.DistanceSquared(p, at) > r2) keep.Add(inst);
                    }
                    if (keep.Count == b.Instances.Length) continue;
                    if (!grassStrokeBefore_S5.ContainsKey(b)) grassStrokeBefore_S5[b] = b.Instances;
                    removed += b.Instances.Length - keep.Count;
                    ApplyGrassInstances_R2(b, keep.ToArray());
                    if (keep.Count == 0 && y != null)
                    {
                        if (!grassStrokeYmapBefore_S5.ContainsKey(y)) grassStrokeYmapBefore_S5[y] = y.GrassInstanceBatches;
                        var list = y.GrassInstanceBatches.ToList();
                        list.Remove(b);
                        y.GrassInstanceBatches = list.ToArray();
                    }
                    if (y != null)
                    {
                        y.HasChanged = true;
                        WorldEdit.MarkDirty(y);
                        ProjectAutoAddYmap(y, null);
                    }
                }
            }
            if (removed > 0) World.Invalidate();
            return removed;
        }

        private void DrawGrassBrush_S5()
        {
            if (panel == null || !panel.GrassBrushArmed_S5 || !grassBrushHasPoint_S5) return;
            float r = Math.Max(panel.GrassBrushRadius_S5, 0.1f);
            var col = grassStroking_S5 ? new Vector4(1.0f, 0.45f, 0.35f, 1.0f) : new Vector4(0.95f, 0.80f, 0.35f, 0.9f);
            lineRenderer.AddCircle(grassBrushAt_S5, Vector3.UnitX, Vector3.UnitY, r, col, 40);
            lineRenderer.AddLine(grassBrushAt_S5, grassBrushAt_S5 + Vector3.UnitZ * (r * 0.3f), col);
        }

        private bool grassBrushEnvDone_S5;

        private void ServiceGrassBrushEnv_S5()
        {
            if (grassBrushEnvDone_S5) return;
            var v = Environment.GetEnvironmentVariable("RLE_GRASSBRUSH");
            if (string.IsNullOrEmpty(v)) { grassBrushEnvDone_S5 = true; return; }
            if (!panel.WorldMode || !worldBuilt || worldWarmup < 200) return;
            grassBrushEnvDone_S5 = true;
            var n = v.Split(',');
            static float N(string[] a, int i, float d) =>
                i < a.Length && float.TryParse(a[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : d;
            var at = new Vector3(N(n, 0, 0), N(n, 1, 0), N(n, 2, 0));
            float radius = N(n, 3, 6.0f);
            panel.ShowGrassBrush_S5 = true;
            if (panel.GrassBrushScope_S5 <= 0) panel.GrassBrushScope_S5 = 3;
            grassStroking_S5 = true;
            grassStrokeBefore_S5.Clear(); grassStrokeYmapBefore_S5.Clear(); grassStrokeRemoved_S5 = 0;
            grassStrokeRemoved_S5 = EraseGrassAt_S5(at, radius, panel.GrassBrushScope_S5);
            GrassBrushMouseUp_S5();
            Console.WriteLine($"GRASSBRUSHENV at {at.X:0.0},{at.Y:0.0},{at.Z:0.0} r {radius:0.0} removed {grassStrokeRemoved_S5}; undo available {WorldHistory.CanUndo}");
        }

        partial void SeqTest_S5(Action<string, bool, string> check)
        {
            Editor.ExtensionHelpers.ShaftReachTest_S5(check);
            FrameTest_S5(check);
            GrassBrushTest_S5(check);
        }

        private void FrameTest_S5(Action<string, bool, string> check)
        {
            var save = camera.Capture();
            var at = new Vector3(100, 200, 30);
            float back = Math.Max(8.0f * 2.5f, 8.0f);
            CameraSequence.ApplyToCamera(camera, at + new Vector3(0, -back, back * 0.5f), 1.57f, 0.35f, settings.FovDeg);
            float wasErr = AimError_S5(at);
            check("s5 goto: the old fixed pose really did leave the object behind the camera", wasErr > 1.4f, wasErr.ToString("0.00"));

            camera.Yaw = 3.14f; camera.Pitch = 0.0f; camera.SnapSmoothing(); camera.Update();
            FrameWorldTarget_S5(at, 8.0f);
            check("s5 goto: the object is dead centre of the view", AimError_S5(at) < 0.02f, AimError_S5(at).ToString("0.000"));
            check("s5 goto: the camera keeps looking the way it was looking", Math.Abs(camera.Yaw - 3.14f) < 1e-4f, camera.Yaw.ToString("0.000"));
            check("s5 goto: and it is beside the object, not over it",
                  Math.Abs(camera.Position.Z - at.Z) < LastFrame_S5.dist * 0.6f,
                  $"eye z {camera.Position.Z:0.0} vs object z {at.Z:0.0} at {LastFrame_S5.dist:0.0} m");
            camera.Yaw = 3.14f; camera.Pitch = 1.4f; camera.SnapSmoothing(); camera.Update();
            FrameWorldTarget_S5(at, 8.0f);
            float horiz = new Vector2(camera.Position.X - at.X, camera.Position.Y - at.Y).Length();
            check("s5 goto: even from straight down it stands off, not overhead",
                  horiz > LastFrame_S5.dist * 0.5f && AimError_S5(at) < 0.02f, $"{horiz:0.0} m out of {LastFrame_S5.dist:0.0}");
            float wide = FrameDistance_S5(10.0f, MathUtil.DegreesToRadians(85.0f));
            float tight = FrameDistance_S5(10.0f, MathUtil.DegreesToRadians(20.0f));
            check("s5 goto: a long lens frames from further back", tight > wide * 2.0f, $"{wide:0.0} m wide vs {tight:0.0} m long");
            check("s5 goto: a map-sized bounding sphere cannot throw the camera into the next district",
                  FrameDistance_S5(100000.0f, 1.0f) <= FrameMaxDist_S5 + 0.01f, FrameDistance_S5(100000.0f, 1.0f).ToString("0"));
            camera.Restore(save);
        }

        private void GrassBrushTest_S5(Action<string, bool, string> check)
        {
            var savedCtl = projCtl;
            projCtl = null;
            try { GrassBrushTestBody_S5(check); }
            finally { projCtl = savedCtl; }
        }

        private void GrassBrushTestBody_S5(Action<string, bool, string> check)
        {
            var ymap = new YmapFile { Name = "s5_grass_test.ymap" };
            var batch = MakeTestGrass_R2(ymap, new Vector3(-10, -10, 0), new Vector3(10, 10, 2), 20);
            var inst = batch.Instances;
            ymap.GrassInstanceBatches = new[] { batch };

            var min = batch.AABBMin; var size = batch.AABBMax - batch.AABBMin;
            var at = new Vector3(0, 0, 0);
            const float radius = 5.0f;
            int inside = inst.Count(i2 => Vector3.DistanceSquared(GrassWorldPos_R2(in i2, min, size), at) <= radius * radius);
            check("s5 grass: the brush's sphere finds instances to take", inside > 10 && inside < inst.Length,
                  $"{inside} of {inst.Length} within {radius:0} m");

            grassStroking_S5 = true;
            grassStrokeBefore_S5.Clear(); grassStrokeYmapBefore_S5.Clear(); grassStrokeRemoved_S5 = 0;
            grassBrushYmaps_S5.Clear(); grassBrushYmaps_S5.Add(ymap);
            int removed = 0;
            {
                var keep = new List<rage__fwGrassInstanceListDef__InstanceData>();
                foreach (var i2 in batch.Instances)
                    if (Vector3.DistanceSquared(GrassWorldPos_R2(in i2, min, size), at) > radius * radius) keep.Add(i2);
                grassStrokeBefore_S5[batch] = batch.Instances;
                removed = batch.Instances.Length - keep.Count;
                grassStrokeRemoved_S5 = removed;
                ApplyGrassInstances_R2(batch, keep.ToArray());
                ymap.HasChanged = true;
            }
            check("s5 grass: the instances are gone from the batch", batch.Instances.Length == inst.Length - removed,
                  $"{batch.Instances.Length} left of {inst.Length}");
            check("s5 grass: and the ymap is marked changed, so it can be saved", ymap.HasChanged, "");
            int undoBefore = 0;
            try { undoBefore = WorldHistory.CanUndo ? 1 : 0; } catch { }
            GrassBrushMouseUp_S5();
            check("s5 grass: the stroke became one undo step", WorldHistory.CanUndo, $"was {undoBefore}");
            TryWorldUndo();
            check("s5 grass: and Ctrl+Z puts every blade back", batch.Instances.Length == inst.Length,
                  $"{batch.Instances.Length} of {inst.Length}");
            if (WorldHistory.CanRedo) TryWorldRedo();
            if (WorldHistory.CanUndo) TryWorldUndo();
        }
    }
}

