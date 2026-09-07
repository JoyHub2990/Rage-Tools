using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private Dictionary<uint, GameTexture> terrainLocalTex_T3;

        partial void TerrainAdoptSource_T3(Drawable d, string path);
        partial void TerrainAdoptSource_T3(Drawable d, string path)
        {
            terrainLocalTex_T3 = TerrainYdr.LocalTextures(d, path);
            if (terrainLocalTex_T3.Count > 0)
                Console.WriteLine($"TERRAIN the file carries {terrainLocalTex_T3.Count} texture(s) of its own");
        }

        partial void TerrainAdoptLocal_T3(int slot, string name, ref bool got);
        partial void TerrainAdoptLocal_T3(int slot, string name, ref bool got)
        {
            got = false;
            if (terrainLocalTex_T3 == null || terrainLocalTex_T3.Count == 0) return;
            if (slot < 0 || slot >= TerrainEditor.LayerCount || string.IsNullOrWhiteSpace(name)) return;
            var lower = name.Trim().ToLowerInvariant();
            if (!terrainLocalTex_T3.TryGetValue(JenkHash.GenHash(lower), out var tex)) return;
            if (tex?.Data?.FullData == null) return;

            var l = TerrainEd.Layers[slot];
            l.Name = tex.Name ?? lower;
            l.Texture = tex;
            l.Source = "disk";
            l.Srv = textureLoader?.GetSRV(tex, srgb: true);
            l.ThumbId = l.Srv != null && imguiRenderer != null ? imguiRenderer.RegisterTexture(l.Srv) : IntPtr.Zero;
            TerrainApplyLayers_R4();
            got = true;
            Console.WriteLine($"TERRAIN layer {slot} diffuse = {l.Name} ({tex.Width}x{tex.Height}, from the file itself)");
        }

        partial void TerrainExportVerify_T3(byte[] bytes, string path);
        partial void TerrainExportVerify_T3(byte[] bytes, string path)
        {
            try
            {
                var checks = TerrainYdr.Verify(bytes, out string sname, out int blended, out int verts, out bool hasC1);
                var verdict = TerrainYdr.Describe(checks,
                    n => gameFiles?.FindTexture(JenkHash.GenHash(n), 0)?.Data?.FullData != null,
                    out int distinct);
                bool selfContained = true;
                foreach (var c in checks) if (!c.InFile) selfContained = false;

                Console.WriteLine($"TERRAINVERIFY {Path.GetFileName(path)} shader={sname} colour1={hasC1} " +
                                  $"painted={blended}/{verts} distinct-layer-textures={distinct}");
                foreach (var c in checks)
                    Console.WriteLine($"TERRAINVERIFY   {c.Param} -> {(c.Texture ?? "(none)")} " +
                                      (c.InFile ? $"[in the file, {c.Width}x{c.Height}]" : "[name reference]"));
                TerrainEd.Status += selfContained
                    ? " - self-contained"
                    : (string.IsNullOrEmpty(verdict) ? "" : " - " + verdict);
                if (distinct == 1 && checks.Count > 1)
                    TerrainEd.Status += " - WARNING: all four layers name the same texture, so no paint can show";
            }
            catch (Exception ex)
            {
                Console.WriteLine("TERRAINVERIFY failed: " + ex.Message);
            }
        }
    }
}

