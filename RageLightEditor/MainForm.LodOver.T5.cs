using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool lodOverDone_T5;

        private static float DistanceToBox_T5(Vector3 p, Vector3 min, Vector3 max)
        {
            float dx = Math.Max(Math.Max(min.X - p.X, 0.0f), p.X - max.X);
            float dy = Math.Max(Math.Max(min.Y - p.Y, 0.0f), p.Y - max.Y);
            float dz = Math.Max(Math.Max(min.Z - p.Z, 0.0f), p.Z - max.Z);
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void LodOver_T5()
        {
            if (lodOverDone_T5 || !panel.WorldMode || !worldBuilt) return;
            var v = Environment.GetEnvironmentVariable("RLE_LODOVER");
            if (string.IsNullOrEmpty(v) || !float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float radius) || radius <= 0) return;
            if (worldWarmup < 300) return;
            lodOverDone_T5 = true;

            var cam = camera.Position;
            var drawn = new HashSet<YmapEntityDef>(World.Visible);
            int lods = 0, shown = 0, bad = 0;
            var near = new List<(float d, YmapEntityDef e)>();
            foreach (var e0 in World.Visible)
            {
                if (e0?.Archetype == null) continue;
                int lvl0 = (int)e0._CEntityDef.lodLevel;
                if (lvl0 == 0 || lvl0 == 5) continue;
                float d0 = DistanceToBox_T5(cam, e0.BBMin, e0.BBMax);
                if (d0 <= radius) near.Add((d0, e0));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (dbox, e) in near)
            {
                lods++;

                var kids = e.LodManagerChildren;
                int linked = kids?.Count ?? 0;
                int kidsDrawn = 0, kidsInRange = 0, kidsBuilt = 0, kidsReal = 0;
                if (kids != null)
                    foreach (var k in kids)
                    {
                        if (k == null) continue;
                        if (k.Archetype == null || k.MloInstance != null) continue;
                        kidsReal++;
                        if ((k.Position - cam).Length() <= k.LodDist) kidsInRange++;
                        if (drawn.Contains(k)) kidsDrawn++;
                        if (worldRender.IsBuilt(k.Archetype)) kidsBuilt++;
                    }

                string why;
                if (kids == null || linked < e._CEntityDef.numChildren) why = "children not linked";
                else if ((e.Position - cam).Length() <= e.ChildLodDist * World.LodScale) why = "KEPT BESIDE ITS CHILDREN";
                else if (kidsInRange > 0) why = "KEPT BESIDE ITS CHILDREN";
                else why = "out of child range";
                if (kidsDrawn == 0 && why == "KEPT BESIDE ITS CHILDREN") why = "wait rule, children not drawn";
                if (why.StartsWith("KEPT")) bad++;

                if (shown++ < 24)
                {
                    var ext = e.BBMax - e.BBMin;
                    string kidList = kids == null ? "" : string.Join(", ", kids.Where(k => k?.Archetype != null).Take(4)
                        .Select(k => $"{k.Archetype.Name}[lodDist {k.LodDist:0} d {(k.Position - cam).Length():0} " +
                                     $"{(drawn.Contains(k) ? "drawn" : "not drawn")} {(worldRender.IsBuilt(k.Archetype) ? "built" : "NOT BUILT")}]"));
                    Console.WriteLine($"LODOVER {e.Archetype.Name} #{e.Archetype.Hash} entFlags={e._CEntityDef.flags} archFlags={e.Archetype._BaseArchetypeDef.flags} " +
                                      $"archType={e.Archetype.Type} asset={e.Archetype._BaseArchetypeDef.assetName} assetType={e.Archetype._BaseArchetypeDef.assetType} ytyp={e.Archetype.Ytyp?.Name} " +
                                      $"special={e.Archetype._BaseArchetypeDef.specialAttribute} hdTex={e.Archetype._BaseArchetypeDef.hdTextureDist:0} txd={e.Archetype._BaseArchetypeDef.textureDictionary} " +
                                      $"prio={e._CEntityDef.priorityLevel} guid={e._CEntityDef.guid} " +
                                      $"[{e._CEntityDef.lodLevel}] boxDist={dbox:0} centreDist={(e.Position - cam).Length():0} " +
                                      $"ext={ext.X:0}x{ext.Y:0}x{ext.Z:0} lodDist={e.LodDist:0} childLodDist={e.ChildLodDist:0} " +
                                      $"numChildren={e._CEntityDef.numChildren} linked={linked} real={kidsReal} inRange={kidsInRange} drawn={kidsDrawn} built={kidsBuilt} " +
                                      $"ymap={e.Ymap?.Name} parent={(e.Parent?.Archetype?.Name.ToString() ?? "-")} :: {why} :: {kidList}");
                }
            }
            Console.WriteLine($"LODOVER within {radius:0} m of the camera: {lods} LOD-level entities have geometry here, {bad} of them drawn beside their own children");

            int prox = 0;
            foreach (var e in World.Visible)
            {
                var nm = e?.Archetype?.Name.ToString() ?? "";
                if (nm.IndexOf("refprox", StringComparison.OrdinalIgnoreCase) < 0 &&
                    nm.IndexOf("reflprox", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (prox++ < 12)
                    Console.WriteLine($"LODPROXY {nm} #{e.Archetype.Hash} [{e._CEntityDef.lodLevel}] entFlags={e._CEntityDef.flags} archFlags={e.Archetype._BaseArchetypeDef.flags} " +
                                      $"boxDist={DistanceToBox_T5(cam, e.BBMin, e.BBMax):0} ymap={e.Ymap?.Name}");
            }
            Console.WriteLine($"LODPROXY {prox} reflection-proxy-named entities are still in the main pass; " +
                              $"the WS-T5 rule has refused {Editor.WorldStreamer.RefProxiesSkipped_T5} draws of them so far " +
                              $"(RLE_NOREFPROXYFIX=1 puts them back)");
        }

        partial void OnWorldTick_T5()
        {
            LodOver_T5();
            NavReady_T5();
        }
    }
}

