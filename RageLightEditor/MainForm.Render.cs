using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private float PostFxPassthrough_Render() =>
            RpfExplorerOnly_Q1 ||
            panel.RenderMode == 8 || panel.VertexColourMode ||
            (!panel.WorldMode && !scene.HasModel && !TerrainHasModel_R4 &&
             !(panel.ArchiveMode && assetPreview?.Model != null)) ? 1.0f : 0.0f;

        private static readonly bool mirrorsDisabledByEnv = Environment.GetEnvironmentVariable("RLE_NOMIRROR") == "1";
        private double lastMirrorLog;

        private void RenderMirrors_Render(DeviceContext context, IEnumerable<RenderModel> toDraw,
            GpuLight[] gpuLights, int lightCount, ShaderResourceView[] projTexSrvs, ShadowSetup shadowSetup)
        {
            sceneRenderer.MirrorReflections = !mirrorsDisabledByEnv;
            WireMirrorJoke_S6();
            if (mirrorsDisabledByEnv) return;
            if (float.TryParse(Environment.GetEnvironmentVariable("RLE_MIRRORSCALE"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float sc) && sc > 0.05f && sc <= 1.0f)
                sceneRenderer.MirrorTargetScale = sc;
            if (int.TryParse(Environment.GetEnvironmentVariable("RLE_MIRRORHOLD"), out int hold) && hold >= 0) sceneRenderer.MirrorHoldFrames = hold;
            if (Environment.GetEnvironmentVariable("RLE_MIRRORFLOOR") == "1") sceneRenderer.MirrorFloorScale = 1.0f;
            var models = toDraw as IList<RenderModel> ?? new List<RenderModel>(toDraw);
            models = WithMirrorExtras_U4(models);
            sceneRenderer.RenderMirrorReflections(context, camera, models, gpuLights, lightCount, projTexSrvs, shadowSetup,
                deviceResources.Width, deviceResources.Height);
            if (screenshotPath != null)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now - lastMirrorLog > 1.0)
                {
                    lastMirrorLog = now;
                    LogMirrorExtras_U4();
                    int total = 0;
                    var list = new List<RenderMesh>();
                    foreach (var m in models) foreach (var mesh in m.Meshes) if (mesh.IsMirror) { total++; if (list.Count < 4000) list.Add(mesh); }
                    Console.WriteLine($"MIRRORS total {total} inView {sceneRenderer.MirrorMeshesInView} planes {sceneRenderer.MirrorPlanesInView} " +
                        $"passes {sceneRenderer.MirrorPassesThisFrame} nearest {sceneRenderer.MirrorNearestDist:0.0} m scale {sceneRenderer.MirrorTargetScale:0.00}" +
                        $" crop {sceneRenderer.MirrorCropArea:0.00} reused {sceneRenderer.MirrorReused} ({sceneRenderer.MirrorHoldWhy}, {sceneRenderer.MirrorReusedFrames} frames) drawn {sceneRenderer.ReflectionMeshesDrawn} skipped {sceneRenderer.ReflectionMeshesSkipped}");
                    if (total > 0 && Environment.GetEnvironmentVariable("RLE_MIRRORLIST") == "1")
                    {
                        list.Sort((a, b) => (a.WorldSphere.Center - camera.Position).LengthSquared().CompareTo((b.WorldSphere.Center - camera.Position).LengthSquared()));
                        for (int i = 0; i < Math.Min(list.Count, 12); i++)
                        {
                            var mesh = list[i]; var c = mesh.WorldSphere.Center;
                            string pl = mesh.MirrorPlaneWorld(out var plane) ? $"n ({plane.Normal.X:0.00},{plane.Normal.Y:0.00},{plane.Normal.Z:0.00}) d {plane.D:0.00}" : "no plane";
                            Console.WriteLine($"  MIRROR {mesh.ShaderName} at {c.X:0.0},{c.Y:0.0},{c.Z:0.0} r {mesh.WorldSphere.Radius:0.0} dist {(c - camera.Position).Length():0.0} {pl} decalKind {mesh.DecalKind}");
                        }
                    }
                }
            }
        }

        private bool debugExtractDone;
        private void TickDebugExtract_Render()
        {
            if (debugExtractDone) return;
            var want = Environment.GetEnvironmentVariable("RLE_EXTRACT");
            if (string.IsNullOrWhiteSpace(want)) { debugExtractDone = true; return; }
            if (panel?.Archive == null || !panel.Archive.Ready) return;
            debugExtractDone = true;
            var dir = Environment.GetEnvironmentVariable("RLE_EXTRACTDIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_extract");
            System.IO.Directory.CreateDirectory(dir);
            foreach (var name in want.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var n = name.Trim().ToLowerInvariant();
                var ext = System.IO.Path.GetExtension(n);
                panel.Archive.Search(System.IO.Path.GetFileNameWithoutExtension(n), ext, 50);
                CodeWalker.GameFiles.RpfFileEntry hit = null;
                foreach (var r in panel.Archive.Results) if (r.NameLower == n) { hit = r.File; break; }
                if (hit == null && panel.Archive.Results.Count > 0) hit = panel.Archive.Results[0].File;
                if (hit == null)
                {
                    panel.Archive.Search(System.IO.Path.GetFileNameWithoutExtension(n).Split('_')[0], ext, 12);
                    Console.WriteLine($"EXTRACT {n}: not found (index {panel.Archive.FileCount} files); near: " +
                        string.Join(", ", System.Linq.Enumerable.Select(panel.Archive.Results, r => r.Path)));
                    continue;
                }
                try
                {
                    var data = Editor.ArchiveBrowser.ExtractForDisk(hit);
                    var outPath = System.IO.Path.Combine(dir, hit.Name);
                    System.IO.File.WriteAllBytes(outPath, data);
                    Console.WriteLine($"EXTRACT {hit.Path} -> {outPath} ({data.Length} bytes)");
                    if (ext == ".ytd" && Environment.GetEnvironmentVariable("RLE_EXTRACTDDS") == "1")
                    {
                        var ytd = gameFiles?.Cache?.RpfMan?.GetFile<CodeWalker.GameFiles.YtdFile>(hit);
                        var texs = ytd?.TextureDict?.Textures?.data_items;
                        if (texs != null)
                        {
                            var ddir = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(hit.Name));
                            ExportTexturesTo(new List<CodeWalker.GameFiles.Texture>(texs), ddir);
                            Console.WriteLine($"EXTRACT   {texs.Length} texture(s) -> {ddir}: {string.Join(", ", System.Linq.Enumerable.Select(texs, t => $"{t.Name} {t.Width}x{t.Height} {t.Format}"))}");
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"EXTRACT {n}: {ex.Message}"); }
            }
        }
    }
}

