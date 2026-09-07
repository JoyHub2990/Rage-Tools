using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool grassDbgPrinted_Q5;

        partial void GrassDebugDump_Q5()
        {
            if (grassDbgPrinted_Q5) return;
            if (Environment.GetEnvironmentVariable("RLE_GRASSDBG") != "1") return;
            if (grassRenderer == null) return;
            grassDbgPrinted_Q5 = true;
            try { GrassDebugDumpCore_Q5(); }
            catch (Exception ex) { Console.WriteLine("GRASSDBG failed: " + ex.Message); }
        }

        partial void SeqTest_Q5(Action<string, bool, string> check)
        {
            string src;
            try { src = Rendering.ShaderSet.Source("grass.hlsl"); }
            catch (Exception ex) { check("grass.hlsl is embedded", false, ex.Message); return; }

            check("grass tint is gated by the model's ground blend, not a flat multiply",
                src.Contains("lerp(c.rgb, c.rgb * input.Colour.rgb"), "PSGrass tint lerp");
            check("grass lights with the model's own normal through the instance rotation",
                src.Contains("n = n.x * InstRot[ri].xyz"), "VSGrass normal rotation");
            check("grass distance fade shrinks the instance rather than fading its alpha",
                src.Contains("if (GrassLegacy < 0.5) s *= fade;"), "VSGrass fade on scale");
            check("grass alpha is a derivative-resolved cutout, not the x7 decal inflation",
                src.Contains("fwidth(c.a)") && src.Contains("float aRef = 0.33;"), "PSGrass cutout");

            bool envSet = Environment.GetEnvironmentVariable("RLE_GRASSOLD") == "1";
            check("grass legacy shading is off unless RLE_GRASSOLD asks for it",
                  Rendering.GrassRenderer.LegacyDefault == envSet,
                  $"LegacyDefault={Rendering.GrassRenderer.LegacyDefault} RLE_GRASSOLD={(envSet ? "1" : "unset")}");
        }

        private void GrassDebugDumpCore_Q5()
        {
            var camPos = camera.Position;
            Console.WriteLine($"GRASSDBG cam {camPos.X:0.0},{camPos.Y:0.0},{camPos.Z:0.0} hour {panel.PreviewHour:0.00} " +
                              $"batches drawn {grassRenderer.BatchesDrawn} instances {grassRenderer.InstancesDrawn}");

            var gl = sceneRenderer?.GlobalLight;
            if (gl.HasValue)
                Console.WriteLine($"GRASSDBG light dir={gl.Value.LightDir} dirCol={gl.Value.LightDirColour} dirAmb={gl.Value.LightDirAmbColour} " +
                                  $"natUp={gl.Value.NaturalAmbUp} natDn={gl.Value.NaturalAmbDown} " +
                                  $"artUp={gl.Value.ArtificialAmbUp} artDn={gl.Value.ArtificialAmbDown}");

            {
                int near = 0, loaded = 0, prepared = 0, withBatches = 0, failed = 0;
                var lines = new List<string>();
                foreach (var n in World.Nodes)
                {
                    if (n?.Name == null || n.Name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    float d = n.DistanceTo(camPos);
                    if (d > 600f) continue;
                    near++;
                    if (n.Ymap != null) loaded++;
                    if (n.Prepared) prepared++;
                    if (n.LoadFailed) failed++;
                    int nb = n.Ymap?.GrassInstanceBatches?.Length ?? 0;
                    if (nb > 0) withBatches++;
                    if (lines.Count < 12)
                        lines.Add($"GRASSDBG ymap {n.Name} d={d:0} at {(n.Min.X + n.Max.X) * 0.5f:0},{(n.Min.Y + n.Max.Y) * 0.5f:0},{n.Max.Z:0} " +
                                  $"loaded={n.Ymap != null} prepared={n.Prepared} " +
                                  $"failed={n.LoadFailed}{(n.FailReason != null ? " (" + n.FailReason + ")" : "")} batches={nb}");
                }
                Console.WriteLine($"GRASSDBG grassYmaps within 600m: {near} (loaded {loaded}, prepared {prepared}, withBatches {withBatches}, failed {failed})");
                foreach (var l in lines) Console.WriteLine(l);
            }

            var seenArch = new HashSet<uint>();
            int shown = 0;
            foreach (var ymap in World.ContentYmaps ?? Enumerable.Empty<YmapFile>())
            {
                var batches = ymap?.GrassInstanceBatches;
                if (batches == null) continue;
                foreach (var b in batches)
                {
                    if (b?.Instances == null || b.Instances.Length == 0 || b.Archetype == null) continue;
                    float lod = b.Batch.lodDist * panel.WorldLodScale * 0.75f;
                    if (Vector3.Distance(camPos, b.Position) > lod) continue;
                    if (shown++ >= 24) { Console.WriteLine("GRASSDBG ... (more batches in range, list truncated)"); goto archetypes; }

                    int rmin = 255, gmin = 255, bmin = 255, rmax = 0, gmax = 0, bmax = 0;
                    long rsum = 0, gsum = 0, bsum = 0;
                    int smin = 255, smax = 0, aomin = 255, aomax = 0;
                    long aosum = 0;
                    float nzmin = 2, nzmax = -2;
                    foreach (var i in b.Instances)
                    {
                        int cr = i.Color.b0, cg = i.Color.b1, cb = i.Color.b2;
                        rmin = Math.Min(rmin, cr); rmax = Math.Max(rmax, cr); rsum += cr;
                        gmin = Math.Min(gmin, cg); gmax = Math.Max(gmax, cg); gsum += cg;
                        bmin = Math.Min(bmin, cb); bmax = Math.Max(bmax, cb); bsum += cb;
                        smin = Math.Min(smin, i.Scale); smax = Math.Max(smax, i.Scale);
                        aomin = Math.Min(aomin, i.Ao); aomax = Math.Max(aomax, i.Ao); aosum += i.Ao;
                        float nx = i.NormalX * (2f / 255f) - 1f, ny = i.NormalY * (2f / 255f) - 1f;
                        float nz = (float)Math.Sqrt(Math.Max(0f, 1f - nx * nx - ny * ny));
                        nzmin = Math.Min(nzmin, nz); nzmax = Math.Max(nzmax, nz);
                    }
                    int n = b.Instances.Length;
                    Console.WriteLine($"GRASSDBG batch {b.Archetype.Name} ymap={ymap.Name} n={n} " +
                                      $"pos={b.Position.X:0.0},{b.Position.Y:0.0},{b.Position.Z:0.0} d={Vector3.Distance(camPos, b.Position):0} " +
                                      $"col r[{rmin}..{rmax}]avg{rsum / n} g[{gmin}..{gmax}]avg{gsum / n} b[{bmin}..{bmax}]avg{bsum / n} " +
                                      $"scale[{smin}..{smax}] ao[{aomin}..{aomax}]avg{aosum / n} terrainNz[{nzmin:0.00}..{nzmax:0.00}] " +
                                      $"lodDist={b.Batch.lodDist:0} fadeStart={b.Batch.LodFadeStartDist:0} instFade={b.Batch.LodInstFadeRange:0} " +
                                      $"scaleRange={b.Batch.ScaleRange} orient={b.Batch.OrientToTerrain:0.00}");
                    seenArch.Add(b.Archetype.Hash);
                }
            }

        archetypes:
            foreach (var hash in seenArch)
            {
                Archetype arch = null;
                var drw = gameFiles?.GetDrawable(hash, out arch);
                if (drw == null) { Console.WriteLine($"GRASSDBG model {hash} not resident"); continue; }
                var models = drw.DrawableModels?.High ?? drw.AllModels;
                if (models == null) continue;
                foreach (var m in models)
                {
                    if (m?.Geometries == null) continue;
                    foreach (var geom in m.Geometries)
                    {
                        var vd = geom?.VertexData;
                        if (vd == null) continue;
                        var verts = Rendering.VertexDecoder.Decode(vd, null);
                        if (verts == null || verts.Length == 0) continue;
                        Vector4 c0min = new Vector4(9), c0max = new Vector4(-9), c1min = new Vector4(9), c1max = new Vector4(-9);
                        Vector4 c0sum = Vector4.Zero, c1sum = Vector4.Zero;
                        float nzmin = 9, nzmax = -9;
                        foreach (var v in verts)
                        {
                            c0min = Vector4.Min(c0min, v.Colour0); c0max = Vector4.Max(c0max, v.Colour0); c0sum += v.Colour0;
                            c1min = Vector4.Min(c1min, v.Colour1); c1max = Vector4.Max(c1max, v.Colour1); c1sum += v.Colour1;
                            nzmin = Math.Min(nzmin, v.Normal.Z); nzmax = Math.Max(nzmax, v.Normal.Z);
                        }
                        float inv = 1f / verts.Length;
                        var sh = geom.Shader;
                        Console.WriteLine($"GRASSDBG model {arch?.Name} sps={sh?.FileName.ToString() ?? "?"} verts={verts.Length} " +
                                          $"c0 r[{c0min.X:0.00}..{c0max.X:0.00}] g[{c0min.Y:0.00}..{c0max.Y:0.00}] b[{c0min.Z:0.00}..{c0max.Z:0.00}] a[{c0min.W:0.00}..{c0max.W:0.00}] " +
                                          $"c1 r[{c1min.X:0.00}..{c1max.X:0.00}]avg{c1sum.X * inv:0.00} g[{c1min.Y:0.00}..{c1max.Y:0.00}] " +
                                          $"b[{c1min.Z:0.00}..{c1max.Z:0.00}] a[{c1min.W:0.00}..{c1max.W:0.00}]avg{c1sum.W * inv:0.00} " +
                                          $"normZ[{nzmin:0.00}..{nzmax:0.00}]");
                    }
                }
            }
        }
    }
}

