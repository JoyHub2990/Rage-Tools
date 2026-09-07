using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_T6(Action<string, bool, string> check)
        {
            var doc = PtfxAuthor.NewDocument("rle_test_asset", "rle_test_puff", null);
            var eff = doc.Effects.FirstOrDefault();
            check("ptfx New gives a playable effect",
                  eff != null && eff.Emitters.Count == 1 &&
                  eff.Emitters[0].EmitterRule != null && eff.Emitters[0].ParticleRule != null &&
                  PtfxAuthor.EmitterSheet(eff.Emitters[0]) != null,
                  eff == null ? "no effect" :
                  $"{eff.Name}: {eff.Emitters.Count} emitter, sheet " +
                  (PtfxAuthor.EmitterSheet(eff.Emitters[0])?.Name ?? "(none)"));

            var sim = new PtfxSimulator { Origin = SDX.Vector3.Zero };
            sim.SetEffect(eff);
            for (int i = 0; i < 90; i++) sim.Update(PtfxSimulator.FixedDt);
            var sprites = new List<PtfxSimulator.Sprite>();
            sim.CollectSprites(sprites);
            float maxR = 0f;
            bool finite = true;
            foreach (var s in sprites)
            {
                if (!float.IsFinite(s.Pos.X) || !float.IsFinite(s.Size.X)) { finite = false; break; }
                maxR = Math.Max(maxR, s.Pos.Length());
            }
            check("ptfx the New template actually puffs",
                  sprites.Count > 20 && finite && maxR < 20.0f && sim.CulledCount == 0,
                  $"{sprites.Count} sprites after 1.5 s, furthest {maxR:0.##} m, {sim.CulledCount} culled");

            int before = eff.Emitters.Count;
            PtfxAuthor.AddEmitter(doc, eff, "rle_test_puff_b", null);
            eff = doc.Effects.FirstOrDefault(e => e.Name == "rle_test_puff");
            bool added = eff != null && eff.Emitters.Count == before + 1;
            bool removed = added && PtfxAuthor.RemoveEmitter(doc, eff, 1);
            eff = doc.Effects.FirstOrDefault(e => e.Name == "rle_test_puff");
            check("ptfx emitters can be added and removed",
                  added && removed && eff != null && eff.Emitters.Count == before,
                  $"{before} -> {(added ? before + 1 : -1)} -> {eff?.Emitters.Count ?? -1} " +
                  "(the last one is never removed: an effect with no emitter draws nothing)");

            var pr = eff?.Emitters[0].ParticleRule;
            var noise = PtfxAuthor.AddBehaviour(pr, ParticleBehaviourType.Noise);
            bool hasNoise = pr?.AllBehaviours?.data_items?.Any(b => b?.Type == ParticleBehaviourType.Noise) ?? false;
            bool spriteStays = !PtfxAuthor.RemoveBehaviour(pr,
                pr?.AllBehaviours?.data_items?.FirstOrDefault(b => b?.Type == ParticleBehaviourType.Sprite));
            bool noiseGone = PtfxAuthor.RemoveBehaviour(pr, noise) &&
                             !(pr?.AllBehaviours?.data_items?.Any(b => b?.Type == ParticleBehaviourType.Noise) ?? true);
            check("ptfx rules can be added and removed, except the one that draws",
                  noise != null && hasNoise && spriteStays && noiseGone,
                  "added ptxu_Noise, removed it again, ptxd_Sprite refused");

            var tmp = Path.Combine(Path.GetTempPath(), "rle_t6_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ypt");
            string why = "";
            bool trip = false;
            try
            {
                doc.Save(tmp);
                var back = PtfxDocument.FromFile(tmp);
                var beff = back.Effects.FirstOrDefault(e => e.Name == "rle_test_puff");
                var bem = beff?.Emitters.FirstOrDefault();
                var life = PtfxKeyframes.Find(bem?.EmitterRule?.KeyframeProps, "ptxemitterrule:m_particlelifekfp");
                var size = bem?.ParticleRule?.AllBehaviours?.data_items?
                              .OfType<ParticleBehaviourSize>().FirstOrDefault();
                var sizeKeys = size?.WhdMinKFP?.Values?.data_items?.Length ?? 0;
                var sheet = PtfxAuthor.EmitterSheet(bem);
                var dom = bem?.EmitterRule?.CreationDomainObj;
                trip = beff != null && bem != null &&
                       bem.EmitterRule.Name?.Value == "rle_test_puff_puff" &&
                       PtfxKeyframes.HasValues(life) &&
                       Math.Abs(PtfxKeyframes.Evaluate(life, 0f, SDX.Vector4.Zero).Y - 2.6f) < 0.001f &&
                       sizeKeys == 2 && sheet != null && sheet.Width == 128 &&
                       dom != null && dom.DomainType == ParticleDomainType.Sphere &&
                       beff.Rule.DurationMax > 3.9f;
                why = $"{new FileInfo(tmp).Length:N0} bytes -> {back.Effects.Count} effect(s), " +
                      $"emitter '{bem?.EmitterRule?.Name?.Value}', life max " +
                      $"{PtfxKeyframes.Evaluate(life, 0f, SDX.Vector4.Zero).Y:0.##}s, {sizeKeys} size keys, " +
                      $"sheet {sheet?.Name} {sheet?.Width}x{sheet?.Height}, domain {dom?.DomainType}";
            }
            catch (Exception ex) { why = "threw: " + ex.Message; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            check("ptfx an authored .ypt saves and reloads intact", trip, why);

            var wall = new SDX.Vector3(0, 1, 0);
            check("mirror joke still only fires facing a wall mirror, up close",
                  FacingAMirror_S6(wall, 1.2f, new SDX.Vector3(0, -1, 0)) &&
                  !FacingAMirror_S6(wall, 6.0f, new SDX.Vector3(0, -1, 0)) &&
                  !FacingAMirror_S6(wall, 1.2f, new SDX.Vector3(1, 0, 0)) &&
                  !FacingAMirror_S6(new SDX.Vector3(0, 0, 1), 1.2f, new SDX.Vector3(0, 0, -1)),
                  "unchanged from WS-S6 - the fix was the size and the timing, not the gate");

            check("mirror joke is held long enough to see",
                  JokeVisibleSeconds_T6 >= 3.0f && JokeCooldownMin_T6 <= 30.0f,
                  $"{JokeVisibleSeconds_T6:0.#} s on screen, then {JokeCooldownMin_T6:0}-{JokeCooldownMax_T6:0} s " +
                  "before it can happen again (it was 1.8 s and 2-6 minutes)");

            var glass = new SDX.BoundingBox(new SDX.Vector3(-0.6f, -0.02f, 0.9f), new SDX.Vector3(0.6f, 0.02f, 2.1f));
            var eye = new SDX.Vector3(0, -1.4f, 1.6f);
            var fit = JokeFit_T6(glass, eye, new SDX.Vector3(0, -1, 0), 1.4f);
            float apparent = (fit.Centre - (eye - new SDX.Vector3(0, -1, 0) * 2.8f)).Length();
            float share = (fit.Height * 0.5f / apparent) / (0.6f / 1.4f);
            bool inFront = fit.Centre.Y < 0 && fit.Centre.Y > -1.4f;
            check("mirror joke fills the glass it is drawn in",
                  share > 0.45f && share < 0.85f && inFront &&
                  Math.Abs(fit.Centre.Z - 1.44f) < 0.25f && Math.Abs(fit.Centre.X) < 0.05f,
                  $"a {fit.Height:0.00} m hand at ({fit.Centre.X:0.00}, {fit.Centre.Y:0.00}, {fit.Centre.Z:0.00}), " +
                  $"{apparent:0.00} m away in the image - {share * 100:0}% of a 1.2 x 1.2 m mirror, " +
                  "aimed at its middle rather than straight ahead of the camera");

            check("mirror joke can never reach a render to file",
                  MirrorJokeBlocked_T6(photo: true, still: false, cine: false) &&
                  MirrorJokeBlocked_T6(photo: false, still: true, cine: false) &&
                  MirrorJokeBlocked_T6(photo: false, still: false, cine: true) &&
                  !MirrorJokeBlocked_T6(photo: false, still: false, cine: false),
                  "photo mode, Render to file / the mp4 encode, and Cinematic each refuse it " +
                  "whatever Help > Mirror surprise says");
        }
    }
}

