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

        private bool shaftScanDone_S5;

        private void ShaftScan_S5()
        {
            if (shaftScanDone_S5 || Environment.GetEnvironmentVariable("RLE_SHAFTSCAN") != "1") return;
            if (gameFiles?.Cache?.YtypDict == null) return;
            shaftScanDone_S5 = true;
            int total = 0, flagged = 0, pinned = 0, follows = 0, partial = 0;
            var lengths = new List<float>();
            var byArch = new List<(string arch, string ytyp, int n, float len, float amt, uint flags)>();
            foreach (var yt in gameFiles.Cache.YtypDict.Values)
                foreach (var a in yt?.AllArchetypes ?? Array.Empty<Archetype>())
                    foreach (var x in a?.Extensions ?? Array.Empty<MetaWrapper>())
                    {
                        if (!(x is MCExtensionDefLightShaft ls)) continue;
                        var d = ls._Data;
                        total++;
                        bool flag = (d.flags & ExtensionHelpers.ShaftFlagUseSunDirection) != 0;
                        float amt = d.directionAmount;
                        if (flag) flagged++;
                        if (flag && amt <= 0.0f) pinned++;
                        else if (amt >= 0.999f || !flag) follows++;
                        else partial++;
                        lengths.Add(d.length);
                        byArch.Add((a.Name.ToString(), yt.Name, 1, d.length, amt, d.flags));
                    }
            lengths.Sort();
            float med = lengths.Count > 0 ? lengths[lengths.Count / 2] : 0;
            Console.WriteLine($"SHAFTSCAN {total} shafts on {byArch.Select(b => b.arch).Distinct().Count()} archetypes: " +
                              $"flagged {flagged}, follow the sun {follows}, partial blend {partial}, PINNED by the author {pinned}; " +
                              $"length median {med:0.00} m, max {(lengths.Count > 0 ? lengths[lengths.Count - 1] : 0):0.0} m, " +
                              $"under 3 m {lengths.Count(l => l < 3.0f)}");
            foreach (var g in byArch.GroupBy(b => b.arch).OrderByDescending(g => g.Max(b => b.len)).Take(12))
                Console.WriteLine($"  SHAFTSCAN {g.Key} x{g.Count()} longest {g.Max(b => b.len):0.0} m amount {g.First().amt:0.00} flags {g.First().flags} ytyp {g.First().ytyp}");
        }

        private bool lodAuditDone_S5;

        private void LodAudit_S5()
        {
            if (lodAuditDone_S5 || !panel.WorldMode || !worldBuilt) return;
            var v = Environment.GetEnvironmentVariable("RLE_LODAUDIT");
            if (string.IsNullOrEmpty(v) || !float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float radius) || radius <= 0) return;
            if (worldWarmup < 300) return;
            lodAuditDone_S5 = true;
            var cam = camera.Position;
            var drawn = new HashSet<YmapEntityDef>(World.Visible);
            int lods = 0, lodsWithHdInRange = 0, shown = 0;
            var byYmap = new Dictionary<string, int>();
            foreach (var e in World.Visible.OrderBy(e => (e.Position - cam).Length()))
            {
                if (e?.Archetype == null) continue;
                float d = (e.Position - cam).Length();
                if (d > radius) break;
                int lvl = (int)e._CEntityDef.lodLevel;
                if (lvl == 0 || lvl == 5) continue;
                lods++;
                var kids = e.LodManagerChildren;
                int kidsInRange = 0, kidsDrawn = 0;
                if (kids != null)
                    foreach (var k in kids)
                    {
                        if (k == null) continue;
                        float kd = (k.Position - cam).Length();
                        if (kd <= k.LodDist) kidsInRange++;
                        if (drawn.Contains(k)) kidsDrawn++;
                    }
                if (kidsInRange == 0) continue;
                lodsWithHdInRange++;
                var yn = e.Ymap?.Name ?? "?";
                byYmap.TryGetValue(yn, out int c); byYmap[yn] = c + 1;
                if (shown++ < 30)
                    Console.WriteLine($"LODAUDIT {e.Archetype.Name} [{e._CEntityDef.lodLevel}] d={d:0} lodDist={e.LodDist:0} childLodDist={e.ChildLodDist:0} " +
                                      $"numChildren={e._CEntityDef.numChildren} linked={kids?.Count ?? 0} childrenInRange={kidsInRange} childrenDrawn={kidsDrawn} " +
                                      $"built={worldRender.IsBuilt(e.Archetype)} ymap={yn}");
            }
            Console.WriteLine($"LODAUDIT within {radius:0} m: {lods} LOD-level entities drawn, {lodsWithHdInRange} of them with HD children in range " +
                              $"[{string.Join(", ", byYmap.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} x{k.Value}"))}]");
        }

        partial void OnWorldTick_S5()
        {
            ShaftScan_S5();
            LodAudit_S5();
            ServiceGrassBrush_S5();
            ServiceGotoProbe_S5();
        }

        private bool gotoProbeDone_S5;
        private void ServiceGotoProbe_S5()
        {
            if (gotoProbeDone_S5 || Environment.GetEnvironmentVariable("RLE_GOTO_S5") != "1") return;
            if (!panel.WorldMode || !worldBuilt || worldWarmup < 260) return;
            if (WorldEdit.Selected == null && !WorldEdit.Selection.HasValue) return;
            gotoProbeDone_S5 = true;
            var was = camera.Position;
            var target = WorldEdit.Selected?.Position ?? WorldEdit.Selection.WidgetPosition;
            GoToSelection();
            Console.WriteLine($"GOTOS5 probe: selection {WorldEdit.Selected?.Archetype?.Name ?? WorldEdit.Selection.GetNameString("?")} at " +
                              $"{target.X:0.0},{target.Y:0.0},{target.Z:0.0}; camera {was.X:0.0},{was.Y:0.0},{was.Z:0.0} -> " +
                              $"{camera.Position.X:0.0},{camera.Position.Y:0.0},{camera.Position.Z:0.0}; aim error {AimError_S5(target):0.000} " +
                              $"(0 = dead centre, >1.41 = behind the camera); height over it {(camera.Position.Z - target.Z):0.0} m");
        }
    }
}

