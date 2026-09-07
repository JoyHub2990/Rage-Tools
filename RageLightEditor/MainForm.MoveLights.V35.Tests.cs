using System;
using System.IO;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_MoveLights_V35(Action<string, bool, string> check)
        {
            string dir = Path.Combine(Path.GetTempPath(), "rle_v35_lights");
            try
            {
                Directory.CreateDirectory(dir);
                var a = Path.Combine(dir, "rle_v35_a.ydr");
                var b = Path.Combine(dir, "rle_v35_b.ydr");
                if (!File.Exists(a)) TestSceneGenerator.Run(a);
                if (!File.Exists(b)) TestSceneGenerator.Run(b);

                var sc = new Scene(modelRenderer, null);
                bool okA = sc.LoadModelFile(a, additive: false);
                bool okB = sc.LoadModelFile(b, additive: true);
                check("v35 move lights: two props load into a scene", okA && okB && sc.Files.Count == 2,
                      $"{sc.Files.Count} file(s)");
                if (sc.Files.Count < 2) return;

                var fa = sc.Files[0]; var fb = sc.Files[1];
                int onA0 = sc.LightsOf_V35(fa).Count, onB0 = sc.LightsOf_V35(fb).Count;
                check("v35 move lights: each prop owns its own lights to start with",
                      onA0 > 0 && onB0 > 0, $"{onA0} on A, {onB0} on B");
                if (onA0 == 0) return;

                var move = sc.LightsOf_V35(fa).Take(2).ToList();
                var worldBefore = move.Select(l => sc.GetInstance(l).WorldPosition).ToList();

                int moved = sc.MoveLightsToProp_V35(move, fb, keepWorldPosition: true);
                check("v35 move lights: they move onto the other prop",
                      moved == move.Count &&
                      sc.LightsOf_V35(fa).Count == onA0 - move.Count &&
                      sc.LightsOf_V35(fb).Count == onB0 + move.Count,
                      $"moved {moved}: A {onA0}->{sc.LightsOf_V35(fa).Count}, B {onB0}->{sc.LightsOf_V35(fb).Count}");

                float worst = 0f;
                for (int i = 0; i < move.Count; i++)
                    worst = Math.Max(worst, SharpDX.Vector3.Distance(worldBefore[i], sc.GetInstance(move[i]).WorldPosition));
                check("v35 move lights: ...and stay exactly where they were in the world",
                      worst < 0.001f, $"worst drift {worst:0.######} m");

                int again = sc.MoveLightsToProp_V35(move, fb, true);
                check("v35 move lights: moving them where they already are is a no-op",
                      again == 0, again + " moved");

                check("v35 move lights: both props are marked dirty, so neither is saved stale",
                      fa.Dirty && fb.Dirty, $"A dirty {fa.Dirty}, B dirty {fb.Dirty}");

                var keep = sc.LightsOf_V35(fb).Take(1).ToList();
                if (keep.Count == 1)
                {
                    var off = keep[0].Position;
                    sc.MoveLightsToProp_V35(keep, fa, keepWorldPosition: false);
                    check("v35 move lights: with 'keep in the world' off, the offset on the prop is kept instead",
                          SharpDX.Vector3.Distance(off, keep[0].Position) < 0.001f,
                          $"offset {off} -> {keep[0].Position}");
                }
                try { sc.Dispose(); } catch { }
            }
            catch (Exception ex) { check("v35 move lights: the ownership move", false, ex.Message); }
            finally { try { Directory.Delete(dir, true); } catch { } }

            string outside = Path.Combine(Path.GetTempPath(), "rle_v35_outside", "myresource", "stream");
            try
            {
                Directory.CreateDirectory(outside);
                File.WriteAllBytes(Path.Combine(outside, "neonix_prop_chair.ydr"), new byte[32]);
                File.WriteAllBytes(Path.Combine(outside, "neonix_prop_table.ydr"), new byte[32]);
                File.WriteAllBytes(Path.Combine(outside, "unrelated.ytd"), new byte[32]);

                var root = Path.Combine(Path.GetTempPath(), "rle_v35_outside");
                var hits = new System.Collections.Generic.List<RpfExplorer.DiskHit_V35>();
                var ex = new RpfExplorer();

                int n = ex.SearchDisk_V35(root, "neonix", null, hits, 100);
                check("v35 rpf search: a folder outside the install is searched on disk, however deep",
                      n == 2 && hits.Count == 2 && hits.All(h => Path.GetFileName(h.FullPath).StartsWith("neonix")),
                      $"{n} hit(s): " + string.Join(", ", hits.Select(h => Path.GetFileName(h.FullPath))));

                int byExt = ex.SearchDisk_V35(root, "", ".ytd", hits, 100);
                check("v35 rpf search: ...and the extension filter works there too",
                      byExt == 1 && hits.Count == 1 && hits[0].FullPath.EndsWith("unrelated.ytd", StringComparison.OrdinalIgnoreCase),
                      $"{byExt} hit(s)");

                int none = ex.SearchDisk_V35(root, "nothinglikethis", null, hits, 100);
                check("v35 rpf search: a query that matches nothing returns nothing, not everything",
                      none == 0 && hits.Count == 0, none + " hit(s)");
                ex.ClearDiskSearchCache_V35();
            }
            catch (Exception ex2) { check("v35 rpf search: the disk walk", false, ex2.Message); }
            finally { try { Directory.Delete(Path.Combine(Path.GetTempPath(), "rle_v35_outside"), true); } catch { } }
        }
    }
}

