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
        private const float GrassVertMul_R2 = 0.00001525878f;

        private List<YmapFile> areaGrassYmapsOverride_R2;

        private static Vector3 GrassWorldPos_R2(in rage__fwGrassInstanceListDef__InstanceData inst, Vector3 min, Vector3 size)
        {
            var p = inst.Position.XYZ();
            return min + size * (p * GrassVertMul_R2);
        }

        private void RefreshAreaGrass_R2()
        {
            var st = AreaTool;
            st.GrassInside_R2.Clear();
            st.GrassInstancesInside_R2 = 0;
            st.GrassBatchesEmptied_R2 = 0;
            var a = st.Current;
            if (a == null || !a.IsValid || !worldBuilt) return;
            var box = a.Bounds;
            if (areaGrassYmapsOverride_R2 != null) { areaYmaps.Clear(); areaYmaps.AddRange(areaGrassYmapsOverride_R2); }
            else World.SnapshotWalkedYmaps(areaYmaps);
            var seen = new HashSet<YmapGrassInstanceBatch>();
            foreach (var y in areaYmaps)
            {
                var batches = y?.GrassInstanceBatches;
                if (batches == null || batches.Length == 0) continue;
                var yname = y.Name ?? y.RpfFileEntry?.Name ?? "";
                if (st.YmapExcluded(yname)) continue;
                foreach (var b in batches)
                {
                    if (b?.Instances == null || b.Instances.Length == 0 || !seen.Add(b)) continue;
                    if (b.AABBMax.X < box.Minimum.X || b.AABBMin.X > box.Maximum.X ||
                        b.AABBMax.Y < box.Minimum.Y || b.AABBMin.Y > box.Maximum.Y ||
                        b.AABBMax.Z < box.Minimum.Z || b.AABBMin.Z > box.Maximum.Z) continue;
                    int inside = CountGrassInside_R2(b, a);
                    if (inside == 0) continue;
                    st.GrassInside_R2.Add(new AreaToolState.GrassHit_R2
                    {
                        Batch = b,
                        Ymap = yname,
                        Archetype = b.Archetype?.Name ?? b.Batch.archetypeName.ToString(),
                        Inside = inside,
                        Total = b.Instances.Length,
                    });
                    st.GrassInstancesInside_R2 += inside;
                    if (inside >= b.Instances.Length) st.GrassBatchesEmptied_R2++;
                }
            }
            st.GrassInside_R2.Sort((p, q) => q.Inside.CompareTo(p.Inside));
        }

        private static int CountGrassInside_R2(YmapGrassInstanceBatch b, WorldArea a)
        {
            var min = b.AABBMin; var size = b.AABBMax - b.AABBMin;
            int n = 0;
            var inst = b.Instances;
            for (int i = 0; i < inst.Length; i++)
                if (a.Contains(GrassWorldPos_R2(in inst[i], min, size))) n++;
            return n;
        }

        private int AreaDeleteGrass_R2(bool ownStep, out Action doIt, out Action undoIt, out int batchesRemoved)
        {
            doIt = null; undoIt = null; batchesRemoved = 0;
            var st = AreaTool;
            var a = st.Current;
            if (a == null || !a.IsValid || !st.IncludeGrass_R2) return 0;
            RefreshAreaGrass_R2();
            if (st.GrassInside_R2.Count == 0) return 0;

            var edits = new List<(YmapGrassInstanceBatch batch, YmapFile ymap,
                                  rage__fwGrassInstanceListDef__InstanceData[] before,
                                  rage__fwGrassInstanceListDef__InstanceData[] after)>();
            var ymapBatchBefore = new Dictionary<YmapFile, YmapGrassInstanceBatch[]>();
            var ymapBatchAfter = new Dictionary<YmapFile, YmapGrassInstanceBatch[]>();
            int removed = 0;
            foreach (var hit in st.GrassInside_R2)
            {
                var b = hit.Batch;
                var y = b.Ymap;
                if (y == null || b.Instances == null) continue;
                var min = b.AABBMin; var size = b.AABBMax - b.AABBMin;
                var keep = new List<rage__fwGrassInstanceListDef__InstanceData>(b.Instances.Length);
                foreach (var inst in b.Instances)
                    if (!a.Contains(GrassWorldPos_R2(in inst, min, size))) keep.Add(inst);
                if (keep.Count == b.Instances.Length) continue;
                removed += b.Instances.Length - keep.Count;
                edits.Add((b, y, b.Instances, keep.ToArray()));
                if (keep.Count == 0)
                {
                    if (!ymapBatchBefore.ContainsKey(y)) ymapBatchBefore[y] = y.GrassInstanceBatches;
                    var list = (ymapBatchAfter.TryGetValue(y, out var cur) ? cur : y.GrassInstanceBatches).ToList();
                    list.Remove(b);
                    ymapBatchAfter[y] = list.ToArray();
                }
            }
            if (edits.Count == 0) return 0;
            batchesRemoved = ymapBatchAfter.Count == 0 ? 0 : ymapBatchAfter.Sum(kv => ymapBatchBefore[kv.Key].Length - kv.Value.Length);

            var touched = new HashSet<YmapFile>();
            foreach (var e in edits) touched.Add(e.ymap);

            doIt = () =>
            {
                foreach (var e in edits) ApplyGrassInstances_R2(e.batch, e.after);
                foreach (var kv in ymapBatchAfter) kv.Key.GrassInstanceBatches = kv.Value;
                AfterGrassEdit_R2(touched);
            };
            undoIt = () =>
            {
                foreach (var kv in ymapBatchBefore) kv.Key.GrassInstanceBatches = kv.Value;
                foreach (var e in edits) ApplyGrassInstances_R2(e.batch, e.before);
                AfterGrassEdit_R2(touched);
            };

            if (ownStep)
            {
                doIt();
                WorldHistory.Push(new DelegateCommand($"Delete {removed} grass in area {a.Name}", doIt, undoIt));
            }
            return removed;
        }

        private void ApplyGrassInstances_R2(YmapGrassInstanceBatch b, rage__fwGrassInstanceListDef__InstanceData[] instances)
        {
            b.Instances = instances ?? Array.Empty<rage__fwGrassInstanceListDef__InstanceData>();
            b.UpdateInstanceCount();
            b.HasChanged = true;
            grassRenderer?.Forget_R2(b);
        }

        private void AfterGrassEdit_R2(HashSet<YmapFile> touched)
        {
            foreach (var y in touched)
            {
                y.HasChanged = true;
                WorldEdit.MarkDirty(y);
                ProjectAutoAddYmap(y, null);
            }
            World.Invalidate();
        }

        partial void ServiceAreaGrass_R2()
        {
            if (panel == null || !panel.WorldMode) return;
            var st = AreaTool;
            if (st.RequestRefreshGrass_R2)
            {
                st.RequestRefreshGrass_R2 = false;
                RefreshAreaGrass_R2();
                st.Status = $"{st.GrassInstancesInside_R2} grass instance(s) inside {st.Current?.Name ?? "the area"} in {st.GrassInside_R2.Count} batch(es)";
            }
            if (st.RequestDeleteGrassOnly_R2)
            {
                st.RequestDeleteGrassOnly_R2 = false;
                int n = AreaDeleteGrass_R2(true, out _, out _, out int batches);
                st.Status = n > 0
                    ? $"deleted {n} grass instance{(n == 1 ? "" : "s")}" + (batches > 0 ? $" ({batches} empty batch{(batches == 1 ? "" : "es")} removed)" : "") + " - Ctrl+Z restores"
                    : "no grass inside the area";
                Console.WriteLine($"AREAGRASS delete {n} instances, {batches} batches removed, dirty ymaps {WorldEdit.DirtyCount}");
                RefreshAreaGrass_R2();
            }
        }

        private string AreaGrassLine_R2(string when)
        {
            var st = AreaTool;
            RefreshAreaGrass_R2();
            return $"AREAGRASS {when}: {st.GrassInstancesInside_R2} instance(s) inside {st.Current?.Name ?? "-"} across {st.GrassInside_R2.Count} batch(es)" +
                   (st.GrassInside_R2.Count > 0
                        ? " [" + string.Join(", ", st.GrassInside_R2.Take(4).Select(h => $"{h.Archetype} {h.Inside}/{h.Total} in {h.Ymap}")) + "]"
                        : "") +
                   $"; grass enabled={st.IncludeGrass_R2}; drawn now {(grassRenderer?.InstancesDrawn ?? -1)} in {(grassRenderer?.BatchesDrawn ?? -1)} batch(es)";
        }
    }
}

