using System;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool hmapProbeDone_V21;
        private HeightmapSurface_V21 hmapSurface_V21;

        private HeightmapSurface_V21 HeightmapSurfaceOrNull_V21(SpaceData sd)
        {
            if (hmapSurface_V21 != null) return hmapSurface_V21;
            if (sd?.Heightmaps == null || !sd.HeightmapReady) return null;
            var files = sd.Heightmaps.HeightmapFiles;
            if (files == null || files.Count == 0) return null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var surf = new HeightmapSurface_V21();
            try { surf.Build(files); }
            catch (Exception ex) { Console.WriteLine("HEIGHTMAP build failed: " + ex.Message); return null; }
            if (surf.TriangleCount == 0) return null;
            hmapSurface_V21 = surf;
            Console.WriteLine($"HEIGHTMAP {files.Count} file(s), {surf.CellCount:N0} cells -> " +
                              $"{surf.TriangleCount:N0} triangles in {sw.ElapsedMilliseconds} ms");
            if (panel != null)
                panel.SpaceDataStatus = $"Heightmap: {surf.CellCount:N0} cells, {surf.TriangleCount:N0} triangles";
            return hmapSurface_V21;
        }

        private void SeqTest_Heightmap_V21(Action<string, bool, string> check)
        {
            var f = new CodeWalker.GameFiles.HeightmapFile
            {
                Width = 16, Height = 16,
                BBMin = new SharpDX.Vector3(0, 0, 0),
                BBMax = new SharpDX.Vector3(800, 800, 255),
            };
            var flat = new byte[16 * 16];
            var hill = new byte[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    flat[y * 16 + x] = 10;
                    float d = (float)Math.Sqrt((x - 8) * (x - 8) + (y - 8) * (y - 8));
                    hill[y * 16 + x] = (byte)Math.Max(10, 200 - d * 25);
                }
            f.MinHeights = flat; f.MaxHeights = hill;

            var surf = new HeightmapSurface_V21();
            surf.Build(new[] { f });
            check("v21 heightmap: a grid becomes a surface",
                  surf.TriangleCount == 15 * 15 * 2 * 2,
                  surf.TriangleCount + " triangles from a 16x16 grid, both layers");

            var verts = surf.GetTriangleVertices();
            uint Peak = 0, Foot = 0;
            float peakZ = float.MinValue, footZ = float.MaxValue;
            foreach (var v in verts)
            {
                if (v.Position.Z > peakZ) { peakZ = v.Position.Z; Peak = v.Colour; }
                if (v.Position.Z < footZ) { footZ = v.Position.Z; Foot = v.Colour; }
            }
            check("v21 heightmap: the peak and the foot are different colours - it reads as relief, not a tint",
                  Peak != Foot, $"peak {Peak:X8} at {peakZ:0.0} m, foot {Foot:X8} at {footZ:0.0} m");

            var low = HeightmapSurface_V21.Ramp(0.0f);
            var high = HeightmapSurface_V21.Ramp(1.0f);
            check("v21 heightmap: the colour ramp runs from the water line to bare peaks",
                  (high - low).Length() > 0.5f, $"{low} -> {high}");

            f.MaxHeights = flat;
            var flatSurf = new HeightmapSurface_V21();
            flatSurf.Build(new[] { f });
            check("v21 heightmap: a perfectly flat map builds without dividing by its own zero range",
                  flatSurf.TriangleCount == 15 * 15 * 2 * 2, flatSurf.TriangleCount + " triangles");
        }

        partial void OnWorldTick_HeightmapProbe_V21()
        {
            if (hmapProbeDone_V21) return;
            if (Environment.GetEnvironmentVariable("RLE_HMAPTEST") != "1") return;
            var sd = SpaceDataOrNull;
            if (sd == null || !gameFiles.Ready) return;

            sd.EnsureHeightmap();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sd.HeightmapLoading && sw.Elapsed.TotalSeconds < 120) System.Threading.Thread.Sleep(100);
            hmapProbeDone_V21 = true;

            var h = sd.Heightmaps;
            if (h == null) { Console.WriteLine("HMAPTEST nothing loaded"); return; }
            foreach (var f in h.HeightmapFiles)
            {
                if (f == null) continue;
                Console.WriteLine($"HMAPTEST {f.Name} {f.Width} x {f.Height} = {f.Width * f.Height:N0} cells, " +
                                  $"bbox {f.BBMin} .. {f.BBMax}");
            }
            long verts = h.TriangleVerts?.Length ?? 0;
            Console.WriteLine($"HMAPTEST total {verts:N0} vertices = {verts / 3:N0} triangles, " +
                              $"{verts * 16L / 1024 / 1024:N0} MB at 16 bytes each, built in {sd.HeightmapLoadMs:0} ms");
        }
    }
}

