using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool furScanDone_V21;
        private static CodeWalker.GameFiles.GameFileCache furScanCache_V21;

        partial void OnWorldTick_FurScan_V21()
        {
            if (furScanDone_V21) return;
            var filter = Environment.GetEnvironmentVariable("RLE_FURSCAN");
            if (string.IsNullOrEmpty(filter)) return;
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) return;
            furScanDone_V21 = true;
            furScanCache_V21 = c;

            bool all = filter == "1";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int scanned = 0, read = 0, furModels = 0, furMats = 0;
            var byShader = new Dictionary<string, int>();
            var samples = new List<string>();

            foreach (var kv in c.YdrDict.ToList())
            {
                var fe = kv.Value;
                if (fe == null) continue;
                var nm = fe.Name ?? "";
                if (!all && nm.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                scanned++;
                YdrFile ydr = null;
                try { ydr = c.RpfMan.GetFile<YdrFile>(fe); } catch { }
                var sg = ydr?.Drawable?.ShaderGroup;
                if (sg?.Shaders?.data_items == null) continue;
                read++;

                bool modelHasFur = false;
                foreach (var sh in sg.Shaders.data_items)
                {
                    var name = sh?.Name.ToString() ?? "";
                    if (name.IndexOf("fur", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    furMats++;
                    modelHasFur = true;
                    byShader[name] = byShader.TryGetValue(name, out var n) ? n + 1 : 1;

                    if (samples.Count < 6)
                    {
                        var text = $"  {fe.Name} :: {name} ({sh.FileName})\n{DescribeFurParams_V21(sh)}";
                        var ps2 = sh.ParametersList?.Parameters; var hs2 = sh.ParametersList?.Hashes;
                        if (ps2 != null && hs2 != null)
                            for (int j = 0; j < ps2.Length && j < hs2.Length; j++)
                            {
                                var pn = (ShaderParamNames)hs2[j];
                                if (pn != ShaderParamNames.ComboHeightSamplerFur01 &&
                                    pn != ShaderParamNames.ComboHeightSamplerFur23 &&
                                    pn != ShaderParamNames.ComboHeightSamplerFur45 &&
                                    pn != ShaderParamNames.ComboHeightSamplerFur67) continue;
                                text += $"\n    --- {pn} ---\n" + DescribeComboChannels_V21(ps2[j].Data as TextureBase);
                            }
                        samples.Add(text);
                    }
                }
                if (modelHasFur) furModels++;
            }

            Console.WriteLine($"FURSCAN {scanned:N0} .ydr scanned ({read:N0} read) in {sw.ElapsedMilliseconds:N0} ms: " +
                              $"{furModels:N0} model(s) with fur, {furMats:N0} fur material(s)" +
                              (all ? "" : $"  [name filter '{filter}']"));
            foreach (var kv in byShader.OrderByDescending(k => k.Value))
                Console.WriteLine($"FURSCAN   {kv.Value,5}  {kv.Key}");
            foreach (var s in samples) Console.WriteLine("FURSCAN " + s);
            if (furMats == 0)
                Console.WriteLine("FURSCAN nothing matched - widen the filter (RLE_FURSCAN=1 scans everything)");
        }

        private static string DescribeComboChannels_V21(TextureBase tb)
        {
            var tex = tb as Texture;
            if (tex == null && tb != null && furScanCache_V21 != null)
            {
                var ytd = furScanCache_V21.TryGetTextureDictForTexture(tb.NameHash);
                var got = ytd?.TextureDict?.Lookup(tb.NameHash);
                tex = got ?? furScanCache_V21.TryFindTextureInParent(tb.NameHash, 0);
            }
            if (tex == null) return $"    ({tb?.Name ?? "(null)"}: not found in the archives)";
            byte[] px;
            try { px = CodeWalker.Utils.DDSIO.GetPixels(tex, 0); }
            catch (Exception ex) { return "    (could not decode: " + ex.Message + ")"; }
            if (px == null || px.Length < 4) return "    (no pixels)";

            var lo = new byte[4] { 255, 255, 255, 255 };
            var hi = new byte[4];
            var sum = new long[4];
            int n = px.Length / 4;
            for (int i = 0; i < n; i++)
                for (int ch = 0; ch < 4; ch++)
                {
                    byte v = px[i * 4 + ch];
                    if (v < lo[ch]) lo[ch] = v;
                    if (v > hi[ch]) hi[ch] = v;
                    sum[ch] += v;
                }
            var names = new[] { "B", "G", "R", "A" };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"    {tex.Name} {tex.Width}x{tex.Height} {tex.Format} ({n:N0} px)");
            for (int ch = 0; ch < 4; ch++)
                sb.AppendLine($"      {names[ch]}: {lo[ch],3}..{hi[ch],3} mean {sum[ch] / (double)n,6:0.0}" +
                              (hi[ch] - lo[ch] < 8 ? "   FLAT - not a height field" : "   varies - carries data"));
            return sb.ToString().TrimEnd();
        }

        private static string DescribeFurParams_V21(ShaderFX sh)
        {
            var sb = new System.Text.StringBuilder();
            var ps = sh?.ParametersList?.Parameters;
            var hashes = sh?.ParametersList?.Hashes;
            if (ps == null || hashes == null) return "    (no parameters)";
            for (int i = 0; i < ps.Length && i < hashes.Length; i++)
            {
                var p = ps[i];
                var nm = hashes[i].ToString();
                string val;
                if (p.Data is TextureBase t) val = "texture " + (t.Name ?? "(none)");
                else if (p.Data is Vector4[] v)
                    val = string.Join(" | ", v.Take(4).Select(x => $"({x.X:0.###},{x.Y:0.###},{x.Z:0.###},{x.W:0.###})"))
                          + (v.Length > 4 ? $" +{v.Length - 4} more" : "");
                else val = p.Data?.ToString() ?? "(null)";
                sb.AppendLine($"    {nm,-28} {val}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}

