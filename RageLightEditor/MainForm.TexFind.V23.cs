using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool texFindDone_V23;

        partial void OnWorldTick_TexFind_V23()
        {
            if (texFindDone_V23) return;
            var arg = Environment.GetEnvironmentVariable("RLE_TEXFIND");
            if (string.IsNullOrEmpty(arg)) return;
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) return;
            texFindDone_V23 = true;

            var bits = arg.Split(',');
            var want = bits[0].Trim();
            var modelFilter = bits.Length > 1 ? bits[1].Trim() : null;
            uint wantHash = JenkHash.GenHash(want.ToLowerInvariant());

            var byIndex = gameFiles.FindTexture(wantHash, 0);
            var ytd = c.TryGetTextureDictForTexture(wantHash);
            Console.WriteLine($"TEXFIND '{want}' (hash {wantHash}): FindTexture -> " +
                              (byIndex?.Data?.FullData != null ? $"FOUND {byIndex.Width}x{byIndex.Height} {byIndex.Format}" : "not found") +
                              $"; TryGetTextureDictForTexture -> {(ytd?.RpfFileEntry?.Path ?? "none")}");
            if (byIndex?.Data?.FullData != null)
            {
                try
                {
                    var px = CodeWalker.Utils.DDSIO.GetPixels(byIndex, 0);
                    if (px != null && px.Length >= 4)
                    {
                        int n = px.Length / 4;
                        long rb = 0, gb = 0, bb = 0;
                        for (int i = 0; i < n; i++) { bb += px[i * 4]; gb += px[i * 4 + 1]; rb += px[i * 4 + 2]; }
                        Console.WriteLine($"TEXFIND   pixel average R {rb / n} G {gb / n} B {bb / n} (sRGB bytes)");
                    }
                }
                catch (Exception ex) { Console.WriteLine("TEXFIND   decode failed: " + ex.Message); }
            }

            var stem = want.Split('_')[0].ToLowerInvariant();
            int named = 0;
            foreach (var kv in c.YtdDict.ToList())
            {
                var nm = kv.Value?.NameLower ?? "";
                if (!nm.Contains(stem)) continue;
                if (++named <= 6) Console.WriteLine($"TEXFIND   a .ytd named like it: {kv.Value?.Path}");
            }
            Console.WriteLine($"TEXFIND {named} .ytd file(s) named like '{stem}*'");

            int asked = 0;
            foreach (var kv in c.YdrDict.ToList())
            {
                var fe = kv.Value;
                if (fe == null) continue;
                if (modelFilter != null && (fe.Name ?? "").IndexOf(modelFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                YdrFile ydr = null;
                try { ydr = c.RpfMan.GetFile<YdrFile>(fe); } catch { }
                var shaders = ydr?.Drawable?.ShaderGroup?.Shaders?.data_items;
                if (shaders == null) continue;
                foreach (var sh in shaders)
                {
                    var ps = sh?.ParametersList?.Parameters;
                    var hs = sh?.ParametersList?.Hashes;
                    if (ps == null || hs == null) continue;
                    for (int i = 0; i < ps.Length && i < hs.Length; i++)
                    {
                        if (!(ps[i].Data is TextureBase tb)) continue;
                        if (tb.NameHash != wantHash) continue;
                        if (++asked > 10) break;
                        Console.WriteLine($"TEXFIND   {fe.Name} :: {sh.Name} ({sh.FileName}) slot {(ShaderParamNames)hs[i]}" +
                                          $"  embedded={(tb is Texture t && t.Data?.FullData != null)}  embeddedDict={(ydr.Drawable.ShaderGroup.TextureDictionary?.Textures?.data_items?.Length ?? 0)} tex");
                    }
                    if (asked > 10) break;
                }
                if (asked > 10) break;
            }
            if (asked == 0) Console.WriteLine($"TEXFIND no model{(modelFilter != null ? " matching '" + modelFilter + "'" : "")} references it");
        }
    }
}

