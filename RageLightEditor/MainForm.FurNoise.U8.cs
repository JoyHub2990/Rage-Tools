using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool furNoiseDone_U8;

        partial void OnTick_FurNoise_U8()
        {
            if (furNoiseDone_U8) return;
            var path = Environment.GetEnvironmentVariable("RLE_FURNOISE");
            if (string.IsNullOrWhiteSpace(path)) { furNoiseDone_U8 = true; return; }
            furNoiseDone_U8 = true;
            Console.WriteLine("FURNOISE reading " + path.Trim());
            try
            {
                var data = File.ReadAllBytes(path.Trim());
                var ydd = new YddFile();
                RpfFile.LoadResourceFile(ydd, data, 165);
                var list = Editor.Scene.DrawablesInYdd_V38(ydd);
                Console.WriteLine($"FURNOISE {list.Count} drawable(s)");
                foreach (var d in list)
                {

                    foreach (var sh in d.Drawable?.ShaderGroup?.Shaders?.data_items ?? Array.Empty<ShaderFX>())
                    {
                        if (sh == null || ((uint)sh.Name != JenkHash.GenHash("ped_fur") &&
                                           !ModelRenderer.IsPedFurShader_V38(sh.Name.ToString()))) continue;
                        var ps = sh.ParametersList?.Parameters;
                        var hs = sh.ParametersList?.Hashes;
                        if (ps == null || hs == null) continue;
                        for (int i = 0; i < ps.Length && i < hs.Length; i++)
                        {
                            var name = (ShaderParamNames)(uint)hs[i];
                            if (name != ShaderParamNames.NoiseSampler && name != ShaderParamNames.DiffuseSampler) continue;
                            var tb = ps[i].Data as TextureBase;
                            var tex = tb as Texture ??
                                      d.Drawable?.ShaderGroup?.TextureDictionary?.Lookup(tb?.NameHash ?? 0);
                            if (tex?.Data?.FullData == null)
                            { Console.WriteLine($"FURNOISE {name}: '{tb?.Name ?? "?"}' has no data here"); continue; }
                            StatTexture_U8(name.ToString(), tex);
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("FURNOISE failed: " + ex.Message); }
        }

        private static void StatTexture_U8(string label, Texture tex)
        {
            try
            {
                var px = CodeWalker.Utils.DDSIO.GetPixels(tex, 0);
                if (px == null) { Console.WriteLine($"FURNOISE {label}: could not decode {tex.Name}"); return; }
                long n = px.Length / 4;
                if (n == 0) return;
                double aSum = 0, lSum = 0;
                byte aMin = 255, aMax = 0;
                long aHi = 0, aLo = 0, lHi = 0, lLo = 0;
                for (long i = 0; i < n; i++)
                {
                    byte r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2], a = px[i * 4 + 3];
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    aSum += a; lSum += l;
                    if (a < aMin) aMin = a;
                    if (a > aMax) aMax = a;
                    if (a > 230) aHi++; else if (a < 25) aLo++;
                    if (l > 230) lHi++; else if (l < 25) lLo++;
                }
                Console.WriteLine($"FURNOISE {label} '{tex.Name}' {tex.Width}x{tex.Height} {tex.Format}: " +
                                  $"alpha min {aMin} max {aMax} avg {aSum / n:0.0}, {100.0 * aHi / n:0.#}% bright / {100.0 * aLo / n:0.#}% dark; " +
                                  $"luma avg {lSum / n:0.0}, {100.0 * lHi / n:0.#}% bright / {100.0 * lLo / n:0.#}% dark");
            }
            catch (Exception ex) { Console.WriteLine($"FURNOISE {label}: {ex.Message}"); }
        }
    }
}
