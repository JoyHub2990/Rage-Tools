using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool grassLodPrinted_U5;

        internal static float GrassFade_U5(Vector3 ipos, uint iid, float camDist, float fadeStart,
                                           float lodDist, float fadeRange, float fadePower)
        {
            float threshold = (camDist - fadeStart) / Math.Max(lodDist - fadeStart, 0.001f);
            float range = Math.Min(Math.Max(fadeRange, 0.01f), 1.0f);
            float scaledRange = 1.0f - range;
            float sx = (float)Math.Sin(ipos.X), sy = (float)Math.Sin(ipos.Y);
            float c = (float)Math.Cos(sx + sy + iid);
            float r = (float)Math.Pow(Math.Min(Math.Max((c + 1.0f) * 0.5f, 0f), 1f), Math.Max(fadePower, 0.001f)) * scaledRange;
            return Math.Min(Math.Max(((r + range) - threshold) / range, 0f), 1f);
        }

        internal static float GrassFadeQ5_U5(float camDist, float fadeStart, float lodDist, float instFadeRange)
        {
            float range = Math.Max(lodDist - fadeStart, Math.Max(instFadeRange, 1.0f));
            return Math.Min(Math.Max((lodDist - camDist) / range, 0f), 1f);
        }

        partial void GrassLodDump_U5()
        {
            if (grassLodPrinted_U5) return;
            if (Environment.GetEnvironmentVariable("RLE_GRASSLOD") != "1") return;
            if (grassRenderer == null || !worldBuilt) return;
            grassLodPrinted_U5 = true;
            try { GrassLodDumpCore_U5(); }
            catch (Exception ex) { Console.WriteLine("GRASSLOD failed: " + ex.Message); }
        }

        private void GrassLodDumpCore_U5()
        {
            var cam = camera.Position;
            Console.WriteLine($"GRASSLOD cam {cam.X:0.0},{cam.Y:0.0},{cam.Z:0.0} cullFudge {Rendering.GrassRenderer.CullFudge_U5:0.00} " +
                              $"fadeRange {Rendering.GrassRenderer.FadeRange_U5:0.000} fadePower {Rendering.GrassRenderer.FadePower_U5:0.00} " +
                              $"detail {panel.WorldLodScale:0.00} grassDist {panel.WorldGrassDistance:0.00}");

            int shown = 0;
            foreach (var ymap in World.ContentYmaps ?? Enumerable.Empty<YmapFile>())
            {
                var batches = ymap?.GrassInstanceBatches;
                if (batches == null) continue;
                foreach (var b in batches)
                {
                    if (b?.Instances == null || b.Instances.Length == 0) continue;
                    float lodDist = b.Batch.lodDist * panel.WorldLodScale * panel.WorldGrassDistance * Rendering.GrassRenderer.CullFudge_U5;
                    if (Vector3.Distance(cam, b.Position) > lodDist) continue;
                    if (shown++ >= 4) { Console.WriteLine("GRASSLOD ... (more batches in range)"); return; }

                    float scale = panel.WorldLodScale * panel.WorldGrassDistance;
                    float authored = b.Batch.LodFadeStartDist * scale * Rendering.GrassRenderer.CullFudge_U5;
                    float fadeStart = Rendering.GrassRenderer.FadeStartFor_U5(authored, lodDist);
                    Console.WriteLine($"GRASSLOD batch {b.Archetype?.Name} ymap={ymap.Name} n={b.Instances.Length} " +
                                      $"ymap lodDist={b.Batch.lodDist:0.###} LodFadeStartDist={b.Batch.LodFadeStartDist:0.###} " +
                                      $"LodInstFadeRange={b.Batch.LodInstFadeRange:0.###} -> cull at {lodDist:0.#} m, thinning from {fadeStart:0.#} m");

                    var delta = b.AABBMax - b.AABBMin;
                    foreach (float d in new[] { 10f, 30f, 50f, 80f, 120f, 200f })
                    {
                        int full = 0, gone = 0, n = 0; double sum = 0, sumOld = 0;
                        foreach (var inst in b.Instances)
                        {
                            var p = new Vector3(inst.Position.u0 / 65535.0f * delta.X + b.AABBMin.X,
                                                inst.Position.u1 / 65535.0f * delta.Y + b.AABBMin.Y,
                                                inst.Position.u2 / 65535.0f * delta.Z + b.AABBMin.Z);
                            float f = GrassFade_U5(p, (uint)n, d, fadeStart, lodDist,
                                                   Rendering.GrassRenderer.FadeRange_U5, Rendering.GrassRenderer.FadePower_U5);
                            sum += f;
                            sumOld += GrassFadeQ5_U5(d, b.Batch.LodFadeStartDist * scale * 0.75f,
                                                     b.Batch.lodDist * scale * 0.75f, b.Batch.LodInstFadeRange);
                            if (f > 0.999f) full++; else if (f < 0.001f) gone++;
                            n++;
                        }
                        Console.WriteLine($"GRASSLOD   at {d,3:0} m: full size {100.0 * full / n,5:0.0}%  thinned out {100.0 * gone / n,5:0.0}%  " +
                                          $"mean size {sum / n,5:0.000}   (WS-Q5 shared ramp: every tuft at {sumOld / n,5:0.000})");
                    }
                }
            }
            if (shown == 0) Console.WriteLine("GRASSLOD no grass batch within its lodDist of the camera");
        }

        partial void SeqTest_U5(Action<string, bool, string> check)
        {
            int ran = 0;
            void Check(string what, bool ok, string detail) { ran++; check(what, ok, detail); }

            string lod, grass;
            try { lod = Rendering.ShaderSet.Source("grasslod.hlsl"); grass = Rendering.ShaderSet.Source("grass.hlsl"); }
            catch (Exception ex) { check("grasslod.hlsl is embedded", false, ex.Message); return; }

            Check("grasslod.hlsl carries the game's per-instance LOD fade",
                  lod.Contains("float GrassLodFade_U5("), "GrassLodFade_U5");
            Check("VSGrass takes its fade from it, not from a single shared ramp",
                  grass.Contains("GrassLodFade_U5(ipos, iid, d,") &&
                  !grass.Contains("saturate((LodDist - d) / fadeRange)"), "VSGrass call site");
            Check("grass reaches the ymap's own lodDist (CodeWalker's 0.75 fudge is gone)",
                  Math.Abs(Rendering.GrassRenderer.CullFudge_U5 - 1.0f) < 1e-6f,
                  $"cull fudge {Rendering.GrassRenderer.CullFudge_U5:0.###}");

            const float lodDist = 100f, authored = 18f;
            float fadeStart = Rendering.GrassRenderer.FadeStartFor_U5(authored, lodDist);
            Check("the thinning is confined to the last quarter of the batch's reach",
                  fadeStart >= lodDist * 0.7f && fadeStart < lodDist,
                  $"thinning {fadeStart:0.#} m .. {lodDist:0.#} m");

            var pts = new Vector3[1000];
            var rnd = new Random(1234);
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Vector3(-2100f + (float)rnd.NextDouble() * 40f, 30f + (float)rnd.NextDouble() * 40f, 114f);

            float MeanAt(float d, float range)
            {
                double s = 0;
                for (int i = 0; i < pts.Length; i++) s += GrassFade_U5(pts[i], (uint)i, d, fadeStart, lodDist, range, 1.0f);
                return (float)(s / pts.Length);
            }
            int FullAt(float d, float range)
            {
                int f = 0;
                for (int i = 0; i < pts.Length; i++) if (GrassFade_U5(pts[i], (uint)i, d, fadeStart, lodDist, range, 1.0f) > 0.999f) f++;
                return f;
            }

            float rangeNow = Rendering.GrassRenderer.FadeRange_U5;
            Check("inside the batch's reach every tuft is at its authored size",
                  FullAt(10f, rangeNow) == pts.Length && FullAt(fadeStart - 1f, rangeNow) == pts.Length,
                  $"{FullAt(10f, rangeNow)}/{pts.Length} full at 10 m, {FullAt(fadeStart - 1f, rangeNow)}/{pts.Length} at {fadeStart - 1f:0} m");

            float mid = (fadeStart + lodDist) * 0.5f;
            int fullMid = FullAt(mid, rangeNow);
            Check("half way through the thinning band most survivors are still full size, not half size",
                  fullMid > pts.Length * 0.35f && MeanAt(mid, rangeNow) < 0.75f,
                  $"{100.0 * fullMid / pts.Length:0.0}% full size, mean size {MeanAt(mid, rangeNow):0.000} at {mid:0} m");

            Check("the tufts do not all go at the same distance",
                  FullAt(mid, rangeNow) > 0 && FullAt(mid, rangeNow) < pts.Length,
                  $"{FullAt(mid, rangeNow)} of {pts.Length} still full size at {mid:0} m");

            Check("nothing is left when the batch is culled",
                  MeanAt(lodDist + 0.5f, rangeNow) < 1e-4f, $"mean size {MeanAt(lodDist + 0.5f, rangeNow):0.00000} at {lodDist + 0.5f:0} m");

            float worst = 0;
            for (float d = 0; d <= 110f; d += 2.5f)
            {
                float mine = GrassFade_U5(pts[0], 0, d, fadeStart, lodDist, 1.0f, 1.0f);
                float q5 = GrassFadeQ5_U5(d, fadeStart, lodDist, 1.0f);
                worst = Math.Max(worst, Math.Abs(mine - q5));
            }
            Check("RLE_GRASSFADE=1 reproduces WS-Q5's shared ramp exactly (the A/B is honest)",
                  worst < 1e-5f, $"worst difference {worst:0.0000000} over 0..110 m");

            Console.WriteLine($"  WS-U5 grass fade: {ran} checks ran");
        }
    }
}

