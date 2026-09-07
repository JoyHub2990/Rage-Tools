using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_Sheet_V29(Action<string, bool, string> check)
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                var f = PtfxSimulator.SheetFrame_V29(8, 18, 49, true, 1, true, false, true, 0f, i / 4f, 4f, 0x55u << 7, 49);
                seen.Add(f.Cell);
            }
            check("v29 sheet: loop mode 1 walks the whole run from its start phase (the game's smoke)",
                  seen.Count == 49 && seen.Min() == 0 && seen.Max() == 48, $"{seen.Count} distinct cell(s), {seen.Min()}..{seen.Max()}");

            seen.Clear();
            for (int i = 0; i < 200; i++)
            {
                var f = PtfxSimulator.SheetFrame_V29(4, 7, 8, true, 2, false, false, false, 0f, i / 36.6f, 36.6f, (uint)i << 7, 16);
                seen.Add(f.Cell); seen.Add(f.Next);
            }
            check("v29 sheet: loop mode 2 cycles inside the start window only (the game's butterflies)",
                  seen.Min() >= 4 && seen.Max() <= 7 && seen.Count == 4, $"cells {seen.Min()}..{seen.Max()}");

            var e0 = PtfxSimulator.SheetFrame_V29(0, 0, 4, true, 1, false, true, true, 0.0f, 0f, 0f, 0, 4);
            var e1 = PtfxSimulator.SheetFrame_V29(0, 0, 4, true, 1, false, true, true, 0.5f, 9f, 0f, 0, 4);
            var e2 = PtfxSimulator.SheetFrame_V29(0, 0, 4, true, 1, false, true, true, 0.99f, 9f, 0f, 0, 4);
            check("v29 sheet: over-life plays the run once across the particle's life (the game's embers)",
                  e0.Cell == 0 && e1.Cell == 2 && e2.Cell == 3, $"{e0.Cell}, {e1.Cell}, {e2.Cell} at 0 / 0.5 / 0.99 of life");

            var h = PtfxSimulator.SheetFrame_V29(0, 0, 8, true, 0, true, false, false, 0f, 100f, 24f, 0, 16);
            check("v29 sheet: mode 0 plays once and holds the last cell, with no blend when blending is off",
                  h.Cell == 7 && h.Next == 7 && h.Blend == 0f, $"cell {h.Cell}, next {h.Next}, blend {h.Blend}");

            seen.Clear();
            for (uint s = 0; s < 64; s++) seen.Add(PtfxSimulator.SheetFrame_V29(3, 5, 1, false, 1, false, false, false, 0f, 10f, 0f, s << 7, 9).Cell);
            check("v29 sheet: an unanimated sheet gives each particle one cell from the start window",
                  seen.SetEquals(new[] { 3, 4, 5 }), string.Join(",", seen.OrderBy(x => x)));

            string ypt = null;
            try
            {
                var doc = PtfxAuthor.NewDocument("rle_v29", "rle_v29_fx", PtfxAuthor.MakePuffSheet("rle_v29_puff", 64));
                var em = doc?.Effects?.FirstOrDefault(e => e.Name == "rle_v29_fx")?.Emitters?.FirstOrDefault();
                var pr = em?.ParticleRule;
                if (pr == null) { check("v29 sheet: a new effect has an emitter to animate", false, "none"); return; }

                check("v29 sheet: a fresh emitter is not animated", ParticlePanel.SetAnimated_V29(pr, false, 49) == null &&
                      !(pr.AllBehaviours?.data_items?.OfType<ParticleBehaviourAnimateTexture>().Any() ?? false), "no AnimateTexture");

                var at = ParticlePanel.SetAnimated_V29(pr, true, 49);
                check("v29 sheet: Animate on gives the game's fire profile - the whole sheet, 24 fps, looping, blended, held",
                      at != null && at.LastFrameID == 48 && Math.Abs(ParticlePanel.RateOf_V29(at) - 24f) < 0.01f &&
                      at.LoopMode == 1 && at.DoFrameBlending == 1 && at.IsHeldOnLastFrame == 1 && at.IsScaledOverParticleLife == 0,
                      at == null ? "no behaviour" : $"last {at.LastFrameID}, {ParticlePanel.RateOf_V29(at):0.#} fps, loop {at.LoopMode}, blend {at.DoFrameBlending}, hold {at.IsHeldOnLastFrame}");
                if (at == null) return;

                at.LastFrameID = 15; ParticlePanel.SetRate_V29(at, 12.5f); at.LoopMode = 2; at.IsScaledOverParticleLife = 1; at.DoFrameBlending = 0;
                pr.TexFrameIDMin = 4; pr.TexFrameIDMax = 7;

                ypt = Path.Combine(Path.GetTempPath(), "rle_v29_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ypt");
                doc.Save(ypt);
                var back = PtfxDocument.FromFile(ypt);
                var bpr = back?.Effects?.FirstOrDefault(e => e.Name == "rle_v29_fx")?.Emitters?.FirstOrDefault()?.ParticleRule;
                var bat = bpr?.AllBehaviours?.data_items?.OfType<ParticleBehaviourAnimateTexture>().FirstOrDefault()
                          ?? bpr?.DrawBehaviours?.data_items?.OfType<ParticleBehaviourAnimateTexture>().FirstOrDefault();
                check("v29 sheet: every animation field comes back out of the saved .ypt",
                      bat != null && bat.LastFrameID == 15 && Math.Abs(ParticlePanel.RateOf_V29(bat) - 12.5f) < 0.01f &&
                      bat.LoopMode == 2 && bat.IsScaledOverParticleLife == 1 && bat.DoFrameBlending == 0 &&
                      bpr.TexFrameIDMin == 4 && bpr.TexFrameIDMax == 7,
                      bat == null ? "no AnimateTexture came back"
                                  : $"last {bat.LastFrameID}, {ParticlePanel.RateOf_V29(bat):0.#} fps, loop {bat.LoopMode}, overLife {bat.IsScaledOverParticleLife}, blend {bat.DoFrameBlending}, start {bpr.TexFrameIDMin}..{bpr.TexFrameIDMax}");

                ParticlePanel.SetAnimated_V29(pr, false, 49);
                check("v29 sheet: Animate off removes the behaviour",
                      !(pr.AllBehaviours?.data_items?.OfType<ParticleBehaviourAnimateTexture>().Any() ?? false), "gone");
            }
            catch (Exception ex) { check("v29 sheet: the authoring round trip", false, ex.Message); }
            finally { try { if (ypt != null && File.Exists(ypt)) File.Delete(ypt); } catch { } }

            var was = panel.Workspace;
            try
            {
                var allowed = new[] { LightPanel.Space.Light, LightPanel.Space.Cinematic };
                var wrong = new System.Collections.Generic.List<string>();
                foreach (LightPanel.Space sp in Enum.GetValues(typeof(LightPanel.Space)))
                {
                    panel.Workspace = sp;
                    bool want = allowed.Contains(sp);
                    if (panel.PhotoModeAllowed != want) wrong.Add(SpaceNames.NameOf(sp));
                }
                check("v29 photo mode: offered in Lights and Cinematic, nowhere else",
                      wrong.Count == 0, wrong.Count == 0 ? "every workspace agrees" : "wrong in " + string.Join(", ", wrong));

                panel.Workspace = LightPanel.Space.Particles;
                SetPhotoMode(true);
                bool refused = !photoMode;
                panel.Workspace = LightPanel.Space.Light;
                SetPhotoMode(true);
                bool onInLights = photoMode;
                panel.SwitchWorkspace(LightPanel.Space.World);
                bool leftOnSwitch = !photoMode;
                check("v29 photo mode: refused outside those two, and left behind on a switch away",
                      refused && onInLights && leftOnSwitch, $"refused {refused}, on in Lights {onInLights}, off after switching to World {leftOnSwitch}");
                SetPhotoMode(false);
            }
            finally { panel.Workspace = was; }
        }
    }
}

