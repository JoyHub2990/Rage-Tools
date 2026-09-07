using System;
using System.Collections.Generic;
using RageLightEditor.Editor;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_R6(Action<string, bool, string> check)
        {
            var a = MakeSim_R6();
            var b = MakeSim_R6();
            for (int i = 0; i < 120; i++) a.Update(1.0f / 60.0f);
            float[] ragged = { 0.031f, 0.008f, 0.05f, 0.017f, 2.0f, 0.016f, 0.033f, 0.004f };
            float fed = 0f;
            int k = 0;
            while (fed < 2.0f) { var d = ragged[k++ % ragged.Length]; b.Update(d); fed += Math.Min(d, 0.1f); }
            check("ptfx step is fixed", a.StepCount == 120 && b.StepCount <= 120,
                  $"smooth {a.StepCount} steps, ragged {b.StepCount} (a 2 s stall must not become 120 steps)");

            var s1 = MakeSim_R6();
            var s2 = MakeSim_R6();
            for (int i = 0; i < 90; i++) { s1.Update(PtfxSimulator.FixedDt); s2.Update(PtfxSimulator.FixedDt); }
            var l1 = new List<PtfxSimulator.Sprite>();
            var l2 = new List<PtfxSimulator.Sprite>();
            s1.CollectSprites(l1); s2.CollectSprites(l2);
            bool same = l1.Count == l2.Count && l1.Count > 0;
            if (same)
                for (int i = 0; i < l1.Count; i++)
                    if ((l1[i].Pos - l2[i].Pos).LengthSquared() > 1e-10f) { same = false; break; }
            check("ptfx replays identically", same, $"{l1.Count} vs {l2.Count} sprites");

            var play = MakeSim_R6();
            for (int i = 0; i < 90; i++) play.Update(PtfxSimulator.FixedDt);
            var seek = MakeSim_R6();
            seek.SeekTo(90 * PtfxSimulator.FixedDt);
            var lp = new List<PtfxSimulator.Sprite>();
            var ls = new List<PtfxSimulator.Sprite>();
            play.CollectSprites(lp); seek.CollectSprites(ls);
            bool seekMatches = lp.Count == ls.Count && lp.Count > 0;
            if (seekMatches)
                for (int i = 0; i < lp.Count; i++)
                    if ((lp[i].Pos - ls[i].Pos).LengthSquared() > 1e-8f) { seekMatches = false; break; }
            check("ptfx scrub matches playback", seekMatches,
                  $"played {lp.Count} sprites, scrubbed to the same time {ls.Count}");

            var pause = MakeSim_R6();
            for (int i = 0; i < 90; i++) pause.Update(PtfxSimulator.FixedDt);
            var beforeAlive = pause.AliveCount;
            pause.Playing = false;
            for (int i = 0; i < 300; i++) pause.Update(PtfxSimulator.FixedDt);
            check("ptfx pause freezes the picture", beforeAlive > 0 && pause.AliveCount == beforeAlive,
                  $"{beforeAlive} alive, {pause.AliveCount} after 5 s paused");

            var run = MakeSim_R6();
            var sprites = new List<PtfxSimulator.Sprite>();
            bool finite = true, bounded = true;
            int maxAlive = 0;
            float maxR = 0f;
            for (int f = 0; f < 600; f++)
            {
                run.Update(PtfxSimulator.FixedDt);
                sprites.Clear();
                run.CollectSprites(sprites);
                maxAlive = Math.Max(maxAlive, run.AliveCount);
                foreach (var s in sprites)
                {
                    if (!float.IsFinite(s.Pos.X) || !float.IsFinite(s.Pos.Y) || !float.IsFinite(s.Pos.Z) ||
                        !float.IsFinite(s.Size.X) || !float.IsFinite(s.Colour.W)) { finite = false; break; }
                    var r = (s.Pos - run.Origin).Length();
                    if (r > maxR) maxR = r;
                    if (r > PtfxSimulator.BoundsRadius * 1.05f) bounded = false;
                }
            }
            check("ptfx 600 frames stay finite", finite, $"{maxAlive} alive at the peak");
            check("ptfx 600 frames stay bounded", bounded,
                  $"furthest {maxR:0.##} m of a {PtfxSimulator.BoundsRadius:0} m bound");

            var bad = MakeSim_R6(life: 0f, speed: float.NaN, size: float.PositiveInfinity);
            for (int i = 0; i < 120; i++) bad.Update(PtfxSimulator.FixedDt);
            sprites.Clear();
            bad.CollectSprites(sprites);
            bool clean = true;
            foreach (var s in sprites)
                if (!float.IsFinite(s.Pos.X) || !float.IsFinite(s.Size.X)) { clean = false; break; }
            check("ptfx survives a broken rule", clean && bad.AliveCount > 0,
                  $"{bad.AliveCount} particles alive, {sprites.Count} sprites drawn from a " +
                  "NaN speed / zero life / infinite size rule");

            check("ptfx sprite size is the asset's metres",
                  Math.Abs(PtfxSimulator.PreviewSizeScale - 1.0f) < 0.001f,
                  $"preview scale {PtfxSimulator.PreviewSizeScale}");

            check("ptfx blend set 0 is alpha, 1 is additive",
                  !Rendering.ParticleRenderer.IsAdditiveBlendSet(0) &&
                   Rendering.ParticleRenderer.IsAdditiveBlendSet(1),
                  "core.ypt uses only 0 (2226 rules) and 1 (317)");

            check("ptfx curve labels are readable",
                  ParticlePanel.CollectTimelineCurvesLabel_R6("ptxu_Colour:m_rgbaMinKFP") == "Colour rgbaMin" &&
                  ParticlePanel.CollectTimelineCurvesLabel_R6("ptxEmitterRule:m_spawnRateOverTimeKFP") == "emitter spawnRateOverTime",
                  ParticlePanel.CollectTimelineCurvesLabel_R6("ptxu_Colour:m_rgbaMinKFP"));
        }

        private static PtfxSimulator MakeSim_R6(float life = 1.5f, float speed = 2.0f, float size = 0.5f)
        {
            var rule = new CodeWalker.GameFiles.ParticleEmitterRule
            {
                KeyframeProps = new[]
                {
                    Curve_R6("ptxEmitterRule:m_spawnRateOverTimeKFP", (0f, new SDX.Vector4(60, 60, 0, 0))),
                    Curve_R6("ptxEmitterRule:m_particleLifeKFP", (0f, new SDX.Vector4(life, life, 0, 0))),
                    Curve_R6("ptxEmitterRule:m_speedScalarKFP", (0f, new SDX.Vector4(speed, speed, 0, 0))),
                },
                CreationDomainObj = Domain_R6(0.25f),
                TargetDomainObj = Domain_R6(1.0f),
            };
            var prule = new CodeWalker.GameFiles.ParticleRule
            {
                BlendSet = 0,
                AllBehaviours = new CodeWalker.GameFiles.ResourcePointerList64<CodeWalker.GameFiles.ParticleBehaviour>
                {
                    data_items = new CodeWalker.GameFiles.ParticleBehaviour[]
                    {
                        new CodeWalker.GameFiles.ParticleBehaviourSize
                        {
                            KeyframeProps = new CodeWalker.GameFiles.ResourcePointerList64<CodeWalker.GameFiles.ParticleKeyframeProp>
                            {
                                data_items = new[]
                                {
                                    Curve_R6("ptxu_Size:m_whdMinKFP", (0f, new SDX.Vector4(size, size, 0, 0))),
                                    Curve_R6("ptxu_Size:m_whdMaxKFP", (0f, new SDX.Vector4(size, size, 0, 0))),
                                },
                            },
                        },
                    },
                },
            };
            var ev = new CodeWalker.GameFiles.ParticleEventEmitter
            {
                EmitterRule = rule,
                ParticleRule = prule,
                StartRatio = 0f,
                EndRatio = 1f,
            };
            var erule = new CodeWalker.GameFiles.ParticleEffectRule
            {
                DurationMin = 2f,
                DurationMax = 2f,
                EventEmitters = new CodeWalker.GameFiles.ResourcePointerArray64<CodeWalker.GameFiles.ParticleEventEmitter>
                {
                    data_items = new[] { ev },
                },
            };
            var eff = new PtfxEffect(null, erule);
            var sim = new PtfxSimulator();
            sim.SetEffect(eff);
            return sim;
        }

        private static CodeWalker.GameFiles.ParticleDomain Domain_R6(float radius) =>
            new CodeWalker.GameFiles.ParticleDomain
            {
                DomainType = CodeWalker.GameFiles.ParticleDomainType.Box,
                PositionKFP = Curve_R6("ptxDomainBox:m_positionKFP", (0f, SDX.Vector4.Zero)),
                SizeOuterKFP = Curve_R6("ptxDomainBox:m_sizeOuterKFP", (0f, new SDX.Vector4(radius, radius, radius, 0))),
            };

        private static CodeWalker.GameFiles.ParticleKeyframeProp Curve_R6(string name,
            params (float t, SDX.Vector4 v)[] keys)
        {
            var vals = new CodeWalker.GameFiles.ParticleKeyframePropValue[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                vals[i] = new CodeWalker.GameFiles.ParticleKeyframePropValue
                {
                    KeyframeTime = new SDX.Vector4(keys[i].t, 0, 0, 0),
                    KeyframeValue = keys[i].v,
                };
            return new CodeWalker.GameFiles.ParticleKeyframeProp
            {
                Name = new CodeWalker.GameFiles.ParticleKeyframePropName(name),
                Values = new CodeWalker.GameFiles.ResourceSimpleList64<CodeWalker.GameFiles.ParticleKeyframePropValue>
                {
                    data_items = vals,
                },
            };
        }
    }
}

