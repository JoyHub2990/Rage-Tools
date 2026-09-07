using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly struct EdgeSnap_V16
        {
            public readonly uint P1, A1, P2, A2;
            public EdgeSnap_V16(YnvEdge e)
            { P1 = e?.PolyID1 ?? 0x3FFF; A1 = e?.AreaID1 ?? 0x3FFF; P2 = e?.PolyID2 ?? 0x3FFF; A2 = e?.AreaID2 ?? 0x3FFF; }
            public bool Same(in EdgeSnap_V16 o) => P1 == o.P1 && A1 == o.A1 && P2 == o.P2 && A2 == o.A2;
            public bool Linked => P1 != 0x3FFF || P2 != 0x3FFF;
        }

        private static List<EdgeSnap_V16[]> SnapEdges_V16(YnvFile ynv)
        {
            var list = new List<EdgeSnap_V16[]>();
            foreach (var p in ynv.Polys)
            {
                var es = p?.Edges ?? Array.Empty<YnvEdge>();
                var arr = new EdgeSnap_V16[es.Length];
                for (int i = 0; i < es.Length; i++) arr[i] = new EdgeSnap_V16(es[i]);
                list.Add(arr);
            }
            return list;
        }

        private static (int compared, int changed, int lost, int gained) CompareEdges_V16(
            List<EdgeSnap_V16[]> a, List<EdgeSnap_V16[]> b)
        {
            int compared = 0, changed = 0, lost = 0, gained = 0;
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var x = a[i]; var y = b[i];
                for (int j = 0; j < Math.Min(x.Length, y.Length); j++)
                {
                    compared++;
                    if (x[j].Same(y[j])) continue;
                    changed++;
                    if (x[j].Linked && !y[j].Linked) lost++;
                    else if (!x[j].Linked && y[j].Linked) gained++;
                }
            }
            return (compared, changed, lost, gained);
        }

        private void NavAuditRoundTrip_V16(Action<string, bool, string> check)
        {
            if (gameFiles?.Cache?.AllRpfs == null) return;

            RpfFileEntry entry;
            YnvFile ynv;
            try { entry = NavPickRealYnv_V16(); ynv = NavLoadRealYnv_V15(entry); }
            catch (Exception ex) { Console.WriteLine("  v16: (skipped - " + ex.Message + ")"); return; }
            if (ynv?.Polys == null || ynv.Polys.Count < 20) { Console.WriteLine("  v16: (skipped - no navmesh)"); return; }

            string name = entry.Name;
            int count = ynv.Polys.Count;
            var beforeEdges = SnapEdges_V16(ynv);
            var beforeFlags = ynv.Polys.Select(p => (p._RawData.PolyFlags0, p._RawData.PolyFlags1, p._RawData.PolyFlags2)).ToList();
            int linkedBefore = beforeEdges.Sum(a => a.Count(e => e.Linked));

            {
                var map = new Dictionary<(SharpDX.Vector3, SharpDX.Vector3), int>();
                foreach (var p in ynv.Polys)
                {
                    var vs = p?.Vertices;
                    if (vs == null || vs.Length < 3) continue;
                    for (int e = 0; e < vs.Length; e++)
                    {
                        var a = vs[e]; var b = vs[(e + 1) % vs.Length];
                        bool swap = a.X != b.X ? a.X > b.X : a.Y != b.Y ? a.Y > b.Y : a.Z > b.Z;
                        var key = swap ? (b, a) : (a, b);
                        map[key] = map.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
                int one = 0, two = 0, many = 0;
                foreach (var kv in map) { if (kv.Value == 1) one++; else if (kv.Value == 2) two++; else many++; }
                int linkedButUnpairable = 0;
                foreach (var p in ynv.Polys)
                {
                    var vs = p?.Vertices; var es = p?.Edges;
                    if (vs == null || es == null || vs.Length < 3) continue;
                    for (int e = 0; e < Math.Min(vs.Length, es.Length); e++)
                    {
                        var ed0 = es[e];
                        if (ed0 == null || (ed0.PolyID1 == 0x3FFF && ed0.PolyID2 == 0x3FFF)) continue;
                        var a = vs[e]; var b = vs[(e + 1) % vs.Length];
                        bool swap = a.X != b.X ? a.X > b.X : a.Y != b.Y ? a.Y > b.Y : a.Z > b.Z;
                        if (map.TryGetValue(swap ? (b, a) : (a, b), out int n) && n != 2) linkedButUnpairable++;
                    }
                }
                Console.WriteLine($"  NAVAUDIT vertex-pair buckets: {one:N0} seen once, {two:N0} seen twice, {many:N0} seen 3+ times. " +
                                  $"{linkedButUnpairable:N0} edge slots the FILE links sit in a bucket the shared-vertex rule cannot pair.");
            }

            Editor.NavMeshEditor.Reindex(ynv);
#pragma warning disable CS0618
            Editor.NavMeshEditor.RebuildAdjacency(ynv);
#pragma warning restore CS0618
            var afterRebuild = SnapEdges_V16(ynv);
            var rb = CompareEdges_V16(beforeEdges, afterRebuild);
            Console.WriteLine($"  NAVAUDIT {name}: {count:N0} polys, {linkedBefore:N0} linked edge slots. " +
                              $"RebuildAdjacency alone changed {rb.changed:N0}/{rb.compared:N0} " +
                              $"({100.0 * rb.changed / Math.Max(1, rb.compared):0.0}%) - {rb.lost:N0} links CUT, {rb.gained:N0} invented.");
            var ynvR = NavLoadRealYnv_V15(entry);
            var edR = new Editor.NavMeshEditor();
            var docR = edR.Add(ynvR, null, "v16 relink");
            edR.Touch(docR, structural: true);
            var rl = CompareEdges_V16(beforeEdges, SnapEdges_V16(ynvR));
            Console.WriteLine($"  NAVAUDIT {name}: a full structural edit pass now changes {rl.changed:N0}/{rl.compared:N0} " +
                              $"({100.0 * rl.changed / Math.Max(1, rl.compared):0.0}%) - {rl.lost:N0} links cut, {rl.gained:N0} invented.");
            check($"v16 ({name}): a structural edit must not touch the adjacency the game shipped",
                  rl.changed == 0,
                  rl.changed == 0 ? $"all {rl.compared:N0} edge slots identical (the old rule cut {rb.lost:N0})"
                                  : $"{rl.changed:N0} of {rl.compared:N0} differ ({rl.lost:N0} cut, {rl.gained:N0} invented)");

            check($"v16 ({name}): ...and it moves no polygon",
                  ynvR.Polys.Count == count, $"{count:N0} -> {ynvR.Polys.Count:N0}");

            var ynv2 = NavLoadRealYnv_V15(entry);
            var doc2 = new Editor.NavMeshEditor().Add(ynv2, null, "v16");
            Editor.NavMeshEditor.Reindex(ynv2);
            Editor.NavMeshEditor.SyncVertexLists(ynv2);
            ynv2.UpdateContentFlags(false);
            var reloaded = new YnvFile();
            reloaded.Load(ynv2.Save());

            check($"v16 ({name}): a save with no edits keeps the poly count",
                  reloaded.Polys?.Count == count, $"{count:N0} -> {reloaded.Polys?.Count ?? 0:N0}");

            if (reloaded.Polys != null && reloaded.Polys.Count == count)
            {
                var afterSave = SnapEdges_V16(reloaded);
                var sv = CompareEdges_V16(beforeEdges, afterSave);
                Console.WriteLine($"  NAVAUDIT {name}: save+reload with NO EDITS changed {sv.changed:N0}/{sv.compared:N0} edge slots " +
                                  $"({100.0 * sv.changed / Math.Max(1, sv.compared):0.0}%) - {sv.lost:N0} links CUT, {sv.gained:N0} invented.");
                check($"v16 ({name}): ...and every edge still names the polygon it named",
                      sv.changed == 0,
                      sv.changed == 0 ? "identical" : $"{sv.changed:N0} of {sv.compared:N0} differ ({sv.lost:N0} cut, {sv.gained:N0} invented)");

                int flagDiff = 0;
                for (int i = 0; i < count; i++)
                {
                    var q = reloaded.Polys[i];
                    if (q._RawData.PolyFlags0 != beforeFlags[i].PolyFlags0 ||
                        q._RawData.PolyFlags1 != beforeFlags[i].PolyFlags1 ||
                        q._RawData.PolyFlags2 != beforeFlags[i].PolyFlags2) flagDiff++;
                }
                check($"v16 ({name}): ...and every polygon keeps its flags",
                      flagDiff == 0, flagDiff == 0 ? "identical" : $"{flagDiff:N0} polys changed flags");

                int portBefore = ynv.Nav?.Portals?.Length ?? 0;
                int portAfter = reloaded.Nav?.Portals?.Length ?? 0;
                check($"v16 ({name}): ...and the portals survive",
                      portBefore == portAfter, $"{portBefore} -> {portAfter}");
            }
        }

        private RpfFileEntry NavPickRealYnv_V16() => NavPickRealYnv_V15();
    }
}

