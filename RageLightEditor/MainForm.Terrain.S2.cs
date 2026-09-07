using System;
using System.Collections.Generic;
using System.Globalization;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly TerrainTextureLibrary terrainLib_S2 = new TerrainTextureLibrary();

        private bool terrainEnvTex_S2, terrainEnvSubdiv_S2, terrainEnvStroke_S2, terrainEnvProfile_S2, terrainEnvScan_S2;
        private bool terrainTopView_S2;
        private bool terrainWaitLib_S2;
        private bool terrainEnvButtons_S2;
        private string terrainExportPath_S2;
        private bool terrainExportReload_S2;
        private int terrainExportWait_S2;
        private int terrainLibWaitFrames_S2;

        private void OnTerrainTick_S2()
        {
            var te = TerrainEd;
            if (te.Library == null) te.Library = terrainLib_S2;

            if (gameFiles != null && gameFiles.Ready && (panel.TerrainMode || te.LibraryWanted))
                terrainLib_S2.Begin(gameFiles);

            if (te.RequestSubdivide)
            {
                te.RequestSubdivide = false;
                TerrainSubdivide_S2();
            }

            TerrainLibraryThumbs_S2();
        }

        private void TerrainSubdivide_S2()
        {
            var te = TerrainEd;
            if (!te.HasMesh) { te.Status = "nothing to subdivide"; return; }
            int before = te.VertexCount;
            if (!te.Subdivide(1000000, out string msg)) { te.Status = msg; Console.WriteLine("TERRAIN subdivide refused: " + msg); return; }
            TerrainBuildModel_R4();
            te.Status = msg;
            Console.WriteLine($"TERRAIN subdivide {before:N0} -> {te.VertexCount:N0} vertices, spacing {te.VertexSpacing:0.###} m");
        }

        private void TerrainLibraryThumbs_S2()
        {
            var want = TerrainEd.LibraryVisible;
            if (want.Count == 0) return;
            if (gameFiles == null || !gameFiles.Ready || textureLoader == null || imguiRenderer == null) { want.Clear(); return; }
            int budget = 8;
            foreach (var e in want)
            {
                if (budget-- <= 0) break;
                if (e.Thumb != IntPtr.Zero || e.ThumbTried) continue;
                e.ThumbTried = true;
                try
                {
                    var tex = gameFiles.FindTexture(e.NameHash, 0);
                    if (tex?.Data?.FullData == null) continue;
                    var srv = textureLoader.GetSRV(tex, srgb: true);
                    if (srv == null) continue;
                    e.Thumb = imguiRenderer.RegisterTexture(srv);
                }
                catch { }
            }
            want.Clear();
        }

        private void TerrainHeadless_S2()
        {
            var te = TerrainEd;

            if (!terrainEnvTex_S2 && terrainEnvFrame_R4 >= 2)
            {
                terrainEnvTex_S2 = true;
                var mode = (Environment.GetEnvironmentVariable("RLE_TERRAINTEXMODE") ?? "").Trim().ToLowerInvariant();
                if (mode == "embed") te.TextureExport = TerrainEditor.TexDestination.Embed;
                else if (mode == "ref" || mode == "none") te.TextureExport = TerrainEditor.TexDestination.Reference;
                else if (mode == "ytd") te.TextureExport = TerrainEditor.TexDestination.Ytd;
                if (Environment.GetEnvironmentVariable("RLE_TERRAINGAMETEX") == "1") te.IncludeGameTextures = true;

                var outp = Environment.GetEnvironmentVariable("RLE_TERRAINEXPORT");
                bool wantsLayers = false;
                for (int i = 0; i < TerrainEditor.LayerCount; i++)
                    wantsLayers |= !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RLE_TERRAINLAYER" + i));
                if (!string.IsNullOrWhiteSpace(outp) && wantsLayers)
                {
                    terrainExportPath_S2 = outp.Trim();
                    terrainExportReload_S2 = Environment.GetEnvironmentVariable("RLE_TERRAINRELOAD") == "1";
                    terrainEnvExported_R4 = true;
                }
                if (Environment.GetEnvironmentVariable("RLE_TERRAINTOP") == "1") terrainTopView_S2 = true;
                if (Environment.GetEnvironmentVariable("RLE_TERRAINBUTTONS") == "1") terrainTopView_S2 = true;
                if (Environment.GetEnvironmentVariable("RLE_TERRAINLIB") == "1")
                {
                    te.LibraryOpen = true;
                    te.LibraryWanted = true;
                    terrainWaitLib_S2 = true;
                    var q = Environment.GetEnvironmentVariable("RLE_TERRAINLIBQ");
                    if (!string.IsNullOrWhiteSpace(q)) te.LibraryQuery = q.Trim();
                    var g = Environment.GetEnvironmentVariable("RLE_TERRAINLIBGROUP");
                    if (!string.IsNullOrWhiteSpace(g)) te.LibraryGroup = g.Trim();
                }
            }

            if (!terrainEnvSubdiv_S2 && te.HasMesh && terrainEnvFrame_R4 > 5)
            {
                terrainEnvSubdiv_S2 = true;
                if (int.TryParse(Environment.GetEnvironmentVariable("RLE_TERRAINSUBDIV"), out int passes))
                    for (int i = 0; i < Math.Max(0, Math.Min(4, passes)); i++) TerrainSubdivide_S2();
            }

            if (!terrainEnvStroke_S2 && te.HasMesh && terrainEnvFrame_R4 > 6)
            {
                terrainEnvStroke_S2 = true;
                TerrainRunStroke_S2(Environment.GetEnvironmentVariable("RLE_TERRAINSTROKE"));
            }

            if (!terrainEnvButtons_S2 && te.HasMesh && terrainEnvFrame_R4 > 7 &&
                Environment.GetEnvironmentVariable("RLE_TERRAINBUTTONS") == "1")
            {
                terrainEnvButtons_S2 = true;
                TerrainButtonTest_S2();
            }

            if (!terrainEnvProfile_S2 && te.HasMesh && terrainEnvFrame_R4 > 7)
            {
                terrainEnvProfile_S2 = true;
                if (Environment.GetEnvironmentVariable("RLE_TERRAINPROFILE") == "1") TerrainProfile_S2();
            }

            if (terrainExportPath_S2 != null && te.HasMesh && terrainEnvFrame_R4 > 8) TerrainExportWhenReady_S2();

            if (terrainTopView_S2 && te.HasMesh) TerrainTopView_S2();

            if (screenshotPath != null && terrainWaitLib_S2 && !terrainLib_S2.Ready &&
                terrainLibWaitFrames_S2++ < 9000)
                screenshotFrames = Math.Max(screenshotFrames, 4);

            if (Environment.GetEnvironmentVariable("RLE_TERRAINTEXSCAN") == "1")
            {
                terrainWaitLib_S2 = true;
                if (!terrainLib_S2.Ready && (terrainEnvFrame_R4 % 60) == 0)
                    Console.WriteLine($"TERRAINLIB running={terrainLib_S2.Running} {terrainLib_S2.Done}/{terrainLib_S2.Total} " +
                                      $"found {terrainLib_S2.Count} - {terrainLib_S2.Status} " +
                                      $"[gameFiles={(gameFiles != null)} ready={gameFiles?.Ready} mode={panel?.TerrainMode} wanted={TerrainEd.LibraryWanted}]");
                if (!terrainEnvScan_S2 && terrainLib_S2.Ready)
                {
                    terrainEnvScan_S2 = true;
                    TerrainDumpLibrary_S2();
                }
            }
        }

        private void TerrainRunStroke_S2(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            var te = TerrainEd;
            foreach (var one in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = one.Split(',');
                if (p.Length < 8) continue;
                float G(int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.0f;
                var a = new Vector3(G(0), G(1), G(2));
                var b = new Vector3(G(3), G(4), G(5));
                float saveR = te.BrushRadius, saveS = te.BrushStrength;
                te.BrushRadius = Math.Max(G(6), 0.01f);
                int layer = (int)G(7);
                if (p.Length >= 9) te.BrushStrength = G(8);
                int steps = p.Length >= 10 ? Math.Max(1, (int)G(9)) : 24;

                te.BeginStrokeAt(layer, a);
                for (int i = 1; i <= steps; i++) te.StrokeTo(Vector3.Lerp(a, b, (float)i / steps), layer);
                te.EndStrokeS2();

                te.BrushRadius = saveR; te.BrushStrength = saveS;
                var cov = te.Coverage();
                Console.WriteLine($"TERRAIN stroke {a.X:0.##},{a.Y:0.##} -> {b.X:0.##},{b.Y:0.##} r {G(6):0.##} layer {layer} " +
                                  $"strength {(p.Length >= 9 ? G(8) : te.BrushStrength):0.##} in {steps} steps; " +
                                  $"coverage {cov.X:0.000} {cov.Y:0.000} {cov.Z:0.000} {cov.W:0.000}");
            }
        }

        private void TerrainProfile_S2()
        {
            var te = TerrainEd;
            var b = te.Bounds;
            var mid = (b.Minimum + b.Maximum) * 0.5f;
            float band = Math.Max(te.VertexSpacing * 0.6f, 0.01f);

            void Line(string label, bool alongX)
            {
                var row = new List<(float at, Vector4 w)>();
                foreach (var part in te.Parts)
                    foreach (var v in part.Verts)
                    {
                        float off = alongX ? v.Position.Y - mid.Y : v.Position.X - mid.X;
                        if (Math.Abs(off) > band) continue;
                        row.Add((alongX ? v.Position.X : v.Position.Y,
                                 TerrainEditor.WeightsOf(new Vector3(v.Colour1.X, v.Colour1.Y, v.Colour1.Z))));
                    }
                row.Sort((l, r) => l.at.CompareTo(r.at));
                float worst = 0.0f, peak = 0.0f;
                for (int i = 0; i < row.Count; i++)
                {
                    peak = Math.Max(peak, row[i].w.Y);
                    if (i > 0) worst = Math.Max(worst, Math.Abs(row[i].w.Y - row[i - 1].w.Y));
                }
                Console.WriteLine($"TERRAINPROFILE {label}: {row.Count} vertices, spacing {te.VertexSpacing:0.###} m, " +
                                  $"peak layer-1 weight {peak:0.000}, biggest neighbour step {worst:0.000}");
                for (int i = 0; i < row.Count && i < 100; i++)
                    Console.WriteLine($"TERRAINPROFILE {label} {row[i].at,8:0.##} {row[i].w.X:0.000} {row[i].w.Y:0.000} {row[i].w.Z:0.000} {row[i].w.W:0.000}");
            }

            Line("along", true);
            Line("across", false);
        }

        private void TerrainExportWhenReady_S2()
        {
            var te = TerrainEd;
            bool ready = true;
            for (int i = 0; i < TerrainEditor.LayerCount; i++)
            {
                var want = Environment.GetEnvironmentVariable("RLE_TERRAINLAYER" + i);
                if (string.IsNullOrWhiteSpace(want)) continue;
                if (te.Layers[i].Texture == null) ready = false;
            }
            if (!ready && terrainExportWait_S2++ < 6000)
            {
                if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 4);
                return;
            }
            var path = terrainExportPath_S2;
            terrainExportPath_S2 = null;
            Console.WriteLine($"TERRAIN export (waited {terrainExportWait_S2} frames for the layer textures)");
            TerrainExportTo_R4(path, terrainExportReload_S2);
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 6);
        }

        private void TerrainButtonTest_S2()
        {
            var te = TerrainEd;
            int x = deviceResources.Width / 2, y = deviceResources.Height / 2;
            te.ActiveLayer = 1;

            var start = te.Coverage();
            bool consumed = false;
            TerrainRightDown_R4(x, y);
            TerrainMouseDown_R4(x, y, true, ref consumed);
            TerrainMouseMove_R4(x, y + 60, false, true);
            TerrainMouseUp_R4();
            var afterRight = te.Coverage();

            consumed = false;
            TerrainMouseDown_R4(x, y, false, ref consumed);
            TerrainMouseMove_R4(x, y + 60, true, false);
            TerrainMouseUp_R4();
            var afterLeft = te.Coverage();

            Console.WriteLine($"TERRAINBUTTON right-drag changed layer 1 coverage by {afterRight.Y - start.Y:0.0000} " +
                              $"(consumed={consumed}); left-drag by {afterLeft.Y - afterRight.Y:0.0000}");
        }

        private void TerrainTopView_S2()
        {
            var b = TerrainEd.Bounds;
            var centre = (b.Minimum + b.Maximum) * 0.5f;
            float radius = Math.Max((b.Maximum - b.Minimum).Length() * 0.5f, 1.0f);
            camera.Target = centre;
            camera.Distance = camera.TargetDistance = radius * 1.6f;
            camera.Yaw = camera.TargetYaw = 0.0f;
            camera.Pitch = camera.TargetPitch = 1.45f;
            camera.SnapSmoothing();
        }

        private void TerrainDumpLibrary_S2()
        {
            var all = terrainLib_S2.All();
            Console.WriteLine($"TERRAINLIB {all.Count} ground textures ({(terrainLib_S2.FromCache ? "from the cache" : "swept")}), " +
                              $"{terrainLib_S2.Done}/{terrainLib_S2.Total} dictionaries");
            foreach (var kv in terrainLib_S2.GroupCounts())
                Console.WriteLine($"TERRAINLIB group {kv.Key,-10} {kv.Value,6}");
            int n = 0;
            foreach (var e in all)
            {
                if (n++ >= 80) break;
                Console.WriteLine($"TERRAINLIB {e.Name,-40} {e.Width,5}x{e.Height,-5} {e.Group,-9} {e.Ytd}");
            }
        }
    }
}

