using System;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool sheetProbeDone_V29;

        private static void SheetArt_V41(Texture tex)
        {
            try
            {
                var data = tex.Data?.FullData;
                if (data == null) { Console.WriteLine("SHEETART (no pixels)"); return; }
                int w = tex.Width, h = tex.Height;
                int bw = Math.Max(1, w / 4), bh = Math.Max(1, h / 4);
                int bs = tex.Format.ToString().Contains("DXT1") ? 8 : 16;
                if (data.Length < (long)bw * bh * bs) { Console.WriteLine("SHEETART (short data)"); return; }
                int outW = Math.Min(64, bw), outH = Math.Max(1, (int)Math.Round(outW * (double)h / w));
                var sb = new System.Text.StringBuilder();
                for (int oy = 0; oy < outH; oy++)
                {
                    sb.Clear(); sb.Append("SHEETART |");
                    for (int ox = 0; ox < outW; ox++)
                    {
                        int x0 = ox * bw / outW, x1 = Math.Max(x0 + 1, (ox + 1) * bw / outW);
                        int y0 = oy * bh / outH, y1 = Math.Max(y0 + 1, (oy + 1) * bh / outH);
                        float sum = 0; int n = 0;
                        for (int by = y0; by < y1; by++)
                            for (int bx = x0; bx < x1; bx++)
                            {
                                long o = ((long)by * bw + bx) * bs;
                                sum += (data[o] + data[o + 1]) * 0.5f; n++;
                            }
                        float v = n > 0 ? sum / n : 0;
                        sb.Append(v > 96 ? (char)35 : v > 32 ? (char)46 : (char)32);
                    }
                    sb.Append((char)124);
                    Console.WriteLine(sb.ToString());
                }
            }
            catch (Exception ex) { Console.WriteLine("SHEETART failed: " + ex.Message); }
        }

        private void ServiceSheetProbe_V29()
        {
            if (sheetProbeDone_V29) return;
            var want = Environment.GetEnvironmentVariable("RLE_SHEETPROBE");
            if (string.IsNullOrWhiteSpace(want) || gameFiles == null || !gameFiles.Ready) return;
            sheetProbeDone_V29 = true;
            EnsureParticles_N4();
            foreach (var fx in want.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                PlayEffectByName_N4(fx.Trim());
                var eff = Ptfx?.Sim?.Effect;
                if (eff == null) { Console.WriteLine($"SHEETPROBE {fx}: not found"); continue; }
                Console.WriteLine($"SHEETPROBE ===== {eff.Name}: {eff.Emitters.Count} emitter(s) =====");
                foreach (var em in eff.Emitters)
                {
                    var pr = em?.ParticleRule;
                    if (pr == null) continue;
                    Texture sheet = null;
                    if (pr.ShaderVars?.data_items != null)
                        foreach (var sv in pr.ShaderVars.data_items)
                            if (sv is ParticleShaderVarTexture svt && svt.Texture != null) { sheet = svt.Texture; break; }
                    int frames = Math.Max(1, (int)pr.TexFrameIDMax + 1);
                    ParticleBehaviourAnimateTexture at = null;
                    foreach (var bl in new[] { pr.AllBehaviours?.data_items, pr.DrawBehaviours?.data_items })
                    {
                        if (bl == null) continue;
                        at = bl.OfType<ParticleBehaviourAnimateTexture>().FirstOrDefault();
                        if (at != null) break;
                    }
                    if (at != null) frames = Math.Max(frames, at.LastFrameID + 1);
                    var g = sheet != null ? AtlasDetect.Detect(sheet, frames) : (0, 0);
                    Console.WriteLine($"SHEETPROBE   {em.Name}: texFrame {pr.TexFrameIDMin}..{pr.TexFrameIDMax}" +
                        (at == null ? "  (no AnimateTexture)" :
                         $"  anim lastFrame={at.LastFrameID} kfMode={at.KeyframeMode} loopMode={at.LoopMode} " +
                         $"random={at.IsRandomised} overLife={at.IsScaledOverParticleLife} hold={at.IsHeldOnLastFrame} " +
                         $"blend={at.DoFrameBlending} rate0={(at.AnimRateKFP?.Values?.data_items?.FirstOrDefault()?.KeyframeValue.X ?? -1):0.##}"));
                    if (pr.ShaderVars?.data_items != null)
                        foreach (var sv in pr.ShaderVars.data_items)
                        {
                            if (sv == null) continue;
                            string val = sv is ParticleShaderVarVector vv
                                ? $" = {vv.VectorX:0.###}, {vv.VectorY:0.###}, {vv.VectorZ:0.###}, {vv.VectorW:0.###}"
                                : sv is ParticleShaderVarTexture tv ? " = tex " + (tv.Texture?.Name ?? "(none)")
                                : "";
                            Console.WriteLine($"SHEETPROBE      var {sv.Name} ({sv.Type}){val}");
                        }
                    if (sheet == null) { Console.WriteLine("SHEETPROBE      (no sheet)"); continue; }
                    Console.WriteLine($"SHEETPROBE      sheet {sheet.Name} {sheet.Width}x{sheet.Height} {sheet.Format} mips={sheet.Levels} " +
                        $"DEPTH={sheet.Depth} stride={sheet.Stride} u5Ch={sheet.Unknown_5Ch} " +
                        $"usage={sheet.Usage} flags={sheet.UsageFlags} extra={sheet.ExtraFlags} u32h={sheet.Unknown_32h} u30h={sheet.Unknown_30h} " +
                        $"detect={g.Item1}x{g.Item2} for {frames} frame(s)");
                    if (Environment.GetEnvironmentVariable("RLE_SHEETART") == "1") SheetArt_V41(sheet);
                }
            }
        }
    }
}

