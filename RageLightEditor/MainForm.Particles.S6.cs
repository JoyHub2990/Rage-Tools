using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool ptfxDrawScanDone_S6;

        private void ServiceParticleDiagnostics_S6()
        {
            if (ptfxDrawScanDone_S6) return;
            var scan = Environment.GetEnvironmentVariable("RLE_PTFXDRAWSCAN");
            var rules = Environment.GetEnvironmentVariable("RLE_PTFXRULES");
            if (string.IsNullOrWhiteSpace(scan) && string.IsNullOrWhiteSpace(rules)) return;
            if (gameFiles == null || !gameFiles.Ready) return;
            ptfxDrawScanDone_S6 = true;
            EnsureParticles_N4();
            if (!string.IsNullOrWhiteSpace(scan)) DrawScanAsset_S6(scan.Trim());
            if (!string.IsNullOrWhiteSpace(rules))
                foreach (var fx in rules.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    DrawScanEffect_S6(fx.Trim());
        }

        private void DrawScanAsset_S6(string asset)
        {
            var list = Ptfx?.GameYpts ?? PtfxGameIndex.ListYpts(gameFiles);
            string path = null;
            foreach (var e in list)
                if (string.Equals(e.Name, asset, StringComparison.OrdinalIgnoreCase)) path = e.Path;
            if (path == null) { Console.WriteLine($"PTFXDRAW {asset}: not in this install"); return; }
            var doc = PtfxDocument.FromGame(gameFiles, asset, path);
            if (doc == null) { Console.WriteLine($"PTFXDRAW {asset}: could not read"); return; }

            var byDraw = new Dictionary<string, int>();
            int rules = 0, noTex = 0, noSprite = 0;
            var examples = new List<string>();
            foreach (var eff in doc.Effects)
                foreach (var em in eff.Emitters)
                {
                    var pr = em.ParticleRule;
                    if (pr == null || em.EmitterRule == null) continue;
                    rules++;
                    var draws = PtfxDrawKinds.Of(pr);
                    var key = draws.Count == 0 ? "(none)" : string.Join("+", draws.OrderBy(s => s));
                    byDraw[key] = byDraw.TryGetValue(key, out var n) ? n + 1 : 1;
                    if (!PtfxDrawKinds.IsSpriteRule(pr))
                    {
                        noSprite++;
                        if (examples.Count < 12) examples.Add($"{eff.Name}/{em.ParticleName}={key}");
                    }
                    if (PtfxDrawKinds.SheetOf(pr) == null) noTex++;
                }
            Console.WriteLine($"PTFXDRAW {asset}: {doc.Effects.Count} effects, {rules} rules, " +
                              $"{noSprite} that do not draw a sprite, {noTex} with no sprite sheet");
            foreach (var kv in byDraw.OrderByDescending(k => k.Value))
                Console.WriteLine($"PTFXDRAW   {kv.Key}: {kv.Value}");
            foreach (var e in examples) Console.WriteLine($"PTFXDRAW   e.g. {e}");
        }

        private PtfxEffect ptfxDrawKindEffect_S6;
        private readonly List<bool> ptfxIsSprite_S6 = new List<bool>();
        private int ptfxSkippedRules_S6;

        private void FilterUnsupportedRules_S6(ParticlePanel p, List<PtfxSimulator.Sprite> sprites)
        {
            var sim = p?.Sim;
            var eff = sim?.Effect;
            if (eff == null) return;

            if (!ReferenceEquals(eff, ptfxDrawKindEffect_S6))
            {
                ptfxDrawKindEffect_S6 = eff;
                ptfxIsSprite_S6.Clear();
                ptfxSkippedRules_S6 = 0;
                for (int i = 0; ; i++)
                {
                    var em = sim.GetSourceEmitter(i);
                    if (em == null) break;
                    var ok = PtfxDrawKinds.IsSpriteRule(em.ParticleRule);
                    ptfxIsSprite_S6.Add(ok);
                    if (!ok) ptfxSkippedRules_S6++;
                }
                p.UnsupportedNote_S6 = ptfxSkippedRules_S6 == 0 ? ""
                    : $"{ptfxSkippedRules_S6} of {ptfxIsSprite_S6.Count} rules draw models or trails - " +
                      "the game draws geometry there, this preview draws nothing";
            }

            if (ptfxSkippedRules_S6 == 0 || ParticlePanel.ShowUnsupportedRules_S6) return;
            sprites.RemoveAll(s => s.EmitterIndex >= 0 && s.EmitterIndex < ptfxIsSprite_S6.Count
                                   && !ptfxIsSprite_S6[s.EmitterIndex]);
        }

        private void DrawScanEffect_S6(string fx)
        {
            PlayEffectByName_N4(fx);
            var eff = Ptfx?.Sim?.Effect;
            if (eff == null) { Console.WriteLine($"PTFXRULES {fx}: not found"); return; }
            Console.WriteLine($"PTFXRULES {eff.Name}: {eff.Emitters.Count} emitters");
            for (int i = 0; i < eff.Emitters.Count; i++)
            {
                var em = eff.Emitters[i];
                var pr = em.ParticleRule;
                if (pr == null) { Console.WriteLine($"PTFXRULES   [{i}] {em.Name}: no rule"); continue; }
                var draws = PtfxDrawKinds.Of(pr);
                var sheet = PtfxDrawKinds.SheetOf(pr);
                Console.WriteLine($"PTFXRULES   [{i}] {em.ParticleName} draws=[{string.Join(",", draws)}] " +
                                  $"sprite={PtfxDrawKinds.IsSpriteRule(pr)} sheet={(sheet?.Name ?? "(none)")} " +
                                  $"drawType={pr.DrawType} blendSet={pr.BlendSet} drawables={pr.Drawables?.data_items?.Length ?? 0}");
            }
        }
    }
}

namespace RageLightEditor.Editor
{
    public static class PtfxDrawKinds
    {
        public static List<string> Of(ParticleRule pr)
        {
            var res = new List<string>();
            var dbs = pr?.DrawBehaviours?.data_items;
            if (dbs != null)
                foreach (var b in dbs)
                    if (b != null) res.Add(b.Type.ToString());
            return res;
        }

        public static bool IsSpriteRule(ParticleRule pr)
        {
            if (pr == null) return false;
            var dbs = pr.DrawBehaviours?.data_items;
            bool any = false;
            if (dbs != null)
                foreach (var b in dbs)
                {
                    if (b == null) continue;
                    any = true;
                    if (b.Type == ParticleBehaviourType.Sprite) return true;
                }
            return !any;
        }

        public static Texture SheetOf(ParticleRule pr)
        {
            var svs = pr?.ShaderVars?.data_items;
            if (svs == null) return null;
            foreach (var sv in svs)
                if (sv is ParticleShaderVarTexture svt && svt.Texture != null) return svt.Texture;
            return null;
        }
    }
}

