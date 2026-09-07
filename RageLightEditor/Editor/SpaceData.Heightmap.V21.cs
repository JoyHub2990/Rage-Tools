using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CodeWalker.World;

namespace RageLightEditor.Editor
{
    public partial class SpaceData
    {
        public volatile bool HeightmapReady;
        public volatile bool HeightmapLoading;
        public double HeightmapLoadMs;
        public Heightmaps Heightmaps;

        public void EnsureHeightmap()
        {
            if (HeightmapReady || HeightmapLoading || !CacheReady) return;
            HeightmapLoading = true;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Status = "Loading the map's heightmap...";
                    var h = new Heightmaps();
                    h.Init(Cache, s => { if (!string.IsNullOrEmpty(s)) Status = s; });
                    Heightmaps = h;
                    HeightmapLoadMs = sw.Elapsed.TotalMilliseconds;
                    int cells = 0;
                    foreach (var f in h.HeightmapFiles) cells += (f?.Width ?? 0) * (f?.Height ?? 0);
                    Status = $"Heightmap loaded: {h.HeightmapFiles.Count} file(s), {cells:N0} cells, " +
                             $"{(h.TriangleVerts?.Length ?? 0) / 3:N0} triangles in {HeightmapLoadMs:0} ms";
                    Console.WriteLine("SPACEDATA " + Status);
                    HeightmapReady = true;
                }
                catch (Exception ex)
                {
                    Error = "heightmap: " + ex.Message;
                    Status = "Heightmap failed: " + ex.Message;
                    Console.WriteLine("SPACEDATA heightmap failed: " + ex);
                }
                finally { HeightmapLoading = false; }
            });
        }
    }
}

