using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool ptfxDumpDone_R6;

        private void ServiceParticleDiagnostics_R6()
        {
            FrameParticleCamera_R6();
            if (ptfxDumpDone_R6) return;
            var dump = Environment.GetEnvironmentVariable("RLE_PTFXDUMP");
            var stab = Environment.GetEnvironmentVariable("RLE_PTFXSTAB");
            var scan = Environment.GetEnvironmentVariable("RLE_PTFXSCAN");
            var seek = Environment.GetEnvironmentVariable("RLE_PTFXSEEK");
            if (string.IsNullOrWhiteSpace(dump) && string.IsNullOrWhiteSpace(stab) &&
                string.IsNullOrWhiteSpace(scan) && string.IsNullOrWhiteSpace(seek)) return;
            if (gameFiles == null || !gameFiles.Ready) return;
            ptfxDumpDone_R6 = true;

            EnsureParticles_N4();
            if (!string.IsNullOrWhiteSpace(seek) && float.TryParse(seek.Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out var st))
            {
                var sim = Ptfx?.Sim;
                if (sim?.Effect != null)
                {
                    sim.Playing = false;
                    sim.SeekTo(st);
                    Console.WriteLine($"PTFXSEEK {sim.EffectName} to {sim.EffectTime:0.###}s " +
                                      $"(frame {sim.StepCount}) -> {sim.AliveCount} particles");
                }
            }
            if (!string.IsNullOrWhiteSpace(scan)) ScanAsset_R6(scan.Trim());
            if (!string.IsNullOrWhiteSpace(dump))
                foreach (var fx in dump.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    DumpEffect_R6(fx.Trim());
            if (!string.IsNullOrWhiteSpace(stab))
                foreach (var fx in stab.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    StabilityProbe_R6(fx.Trim());
        }

        private void ScanAsset_R6(string asset)
        {
            var list = Ptfx?.GameYpts ?? PtfxGameIndex.ListYpts(gameFiles);
            string path = null;
            foreach (var e in list)
                if (string.Equals(e.Name, asset, StringComparison.OrdinalIgnoreCase)) path = e.Path;
            if (path == null) { Console.WriteLine($"PTFXSCAN {asset}: not in this install"); return; }
            var doc = PtfxDocument.FromGame(gameFiles, asset, path);
            if (doc == null) { Console.WriteLine($"PTFXSCAN {asset}: could not read"); return; }

            var blend = new Dictionary<int, int>();
            var alpha = new Dictionary<int, int>();
            var depthW = new Dictionary<int, int>();
            var sizeScalars = new List<float>();
            int rules = 0;
            foreach (var eff in doc.Effects)
                foreach (var em in eff.Emitters)
                {
                    var pr = em.ParticleRule; var er = em.EmitterRule;
                    if (pr == null || er == null) continue;
                    rules++;
                    Bump_R6(blend, pr.BlendSet);
                    Bump_R6(alpha, pr.AlphaBlend);
                    Bump_R6(depthW, pr.DepthWrite);
                    var ss = PtfxKeyframes.Find(er.KeyframeProps, "ptxemitterrule:m_sizescalarkfp");
                    if (PtfxKeyframes.HasValues(ss)) sizeScalars.Add(PtfxKeyframes.Evaluate(ss, 0f, SDX.Vector4.One).X);
                }
            Console.WriteLine($"PTFXSCAN {asset}: {doc.Effects.Count} effects, {rules} rules");
            Console.WriteLine($"PTFXSCAN   blendSet {Hist_R6(blend)}");
            Console.WriteLine($"PTFXSCAN   alphaBlend {Hist_R6(alpha)}   depthWrite {Hist_R6(depthW)}");
            if (sizeScalars.Count > 0)
            {
                sizeScalars.Sort();
                Console.WriteLine($"PTFXSCAN   m_sizeScalarKFP n={sizeScalars.Count} min={sizeScalars[0]:0.##} " +
                                  $"med={sizeScalars[sizeScalars.Count / 2]:0.##} max={sizeScalars[^1]:0.##} " +
                                  $"zeros={sizeScalars.Count(v => v == 0f)} " +
                                  $"over10={sizeScalars.Count(v => v > 10f)}");
            }
        }

        private static void Bump_R6(Dictionary<int, int> d, int k) => d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1;
        private static string Hist_R6(Dictionary<int, int> d) =>
            string.Join(" ", d.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));

        private void FrameParticleCamera_R6()
        {
            var d = Environment.GetEnvironmentVariable("RLE_PTFXFRAME");
            if (string.IsNullOrWhiteSpace(d) || camera == null) return;
            var sim = Ptfx?.Sim;
            if (sim?.Effect == null) return;
            if (!float.TryParse(d.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var dist)) dist = 6f;
            camera.Target = sim.Origin;
            camera.Distance = Math.Clamp(dist, 0.2f, 500f);
            camera.TargetDistance = camera.Distance;
        }

        private int ptfxTraced_R6;

        private void TraceParticleDraw_R6(ParticlePanel p, List<PtfxSimulator.Sprite> sprites)
        {
            var n = Environment.GetEnvironmentVariable("RLE_PTFXTRACE");
            if (string.IsNullOrWhiteSpace(n) || !int.TryParse(n, out var want) || ptfxTraced_R6 >= want) return;
            ptfxTraced_R6++;
            if (sprites.Count == 0) { Console.WriteLine("PTFXTRACE no sprites"); return; }
            var s = sprites[0];
            var tex = p.GetEmitterTexture(s.EmitterIndex);
            var toCam = s.Pos - camera.Position;
            Console.WriteLine($"PTFXTRACE n={sprites.Count} cam={camera.Position} origin={p.Sim.Origin} " +
                              $"pos={s.Pos} dist={toCam.Length():0.##} size={s.Size} " +
                              $"col={s.Colour} emis={s.Emissive:0.###} blendSet={s.BlendSet} " +
                              $"uv={s.UvRect} uv2={s.UvRect2} blend={s.FrameBlend:0.##} " +
                              $"tex={(tex?.Name ?? "(none)")}");
        }

        private void DumpEffect_R6(string fx)
        {
            PlayEffectByName_N4(fx);
            var eff = Ptfx?.Sim?.Effect;
            if (eff == null) { Console.WriteLine($"PTFXDUMP {fx}: not found"); return; }

            Console.WriteLine($"PTFXDUMP effect {eff.Name}  durMin={eff.Rule.DurationMin} durMax={eff.Rule.DurationMax} " +
                              $"emitters={eff.Emitters.Count}");
            for (int i = 0; i < eff.Emitters.Count; i++)
            {
                var em = eff.Emitters[i];
                var er = em.EmitterRule;
                var pr = em.ParticleRule;
                if (er == null || pr == null) { Console.WriteLine($"PTFXDUMP   [{i}] {em.Name}: no rule"); continue; }
                Console.WriteLine($"PTFXDUMP   [{i}] {em.Name} -> {em.ParticleName}");
                Console.WriteLine($"PTFXDUMP       event start={em.Event?.StartRatio} end={em.Event?.EndRatio} " +
                                  $"zoom={em.Event?.ZoomScalarMin}..{em.Event?.ZoomScalarMax} " +
                                  $"play={em.Event?.PlaybackRateScalarMin}..{em.Event?.PlaybackRateScalarMax} " +
                                  $"tint={em.Event?.ColourTintMin:X8}..{em.Event?.ColourTintMax:X8}");
                Console.WriteLine($"PTFXDUMP       render blendSet={pr.BlendSet} alphaBlend={pr.AlphaBlend} " +
                                  $"depthWrite={pr.DepthWrite} depthTest={pr.DepthTest} cull={pr.CullMode} " +
                                  $"lighting={pr.LightingMode} oneShot={er.IsOneShot} " +
                                  $"texFrame={pr.TexFrameIDMin}..{pr.TexFrameIDMax}");
                Texture sheet = null;
                if (pr.ShaderVars?.data_items != null)
                    foreach (var sv in pr.ShaderVars.data_items)
                        if (sv is ParticleShaderVarTexture svt && svt.Texture != null) { sheet = svt.Texture; break; }
                if (sheet != null)
                    Console.WriteLine($"PTFXDUMP       sheet {sheet.Name} {sheet.Width}x{sheet.Height} fmt={sheet.Format}");
                foreach (var bl in new[] { pr.AllBehaviours?.data_items, pr.DrawBehaviours?.data_items })
                {
                    if (bl == null) continue;
                    foreach (var b in bl)
                        if (b is ParticleBehaviourAnimateTexture at)
                            Console.WriteLine($"PTFXDUMP       animtex last={at.LastFrameID} overLife={at.IsScaledOverParticleLife} " +
                                              $"random={at.IsRandomised}");
                }
                DumpCurves_R6("emitter", er.KeyframeProps);
                DumpDomain_R6("creation", er.CreationDomainObj);
                DumpDomain_R6("target", er.TargetDomainObj);
                foreach (var bh in pr.AllBehaviours?.data_items ?? Array.Empty<ParticleBehaviour>())
                    DumpCurves_R6(bh?.GetType().Name.Replace("ParticleBehaviour", "") ?? "?", bh?.KeyframeProps?.data_items);
            }
        }

        private static void DumpDomain_R6(string what, ParticleDomain d)
        {
            if (d == null) return;
            Console.WriteLine($"PTFXDUMP       {what} domain type={d.DomainType}");
            DumpCurve_R6(what, d.PositionKFP, "position");
            DumpCurve_R6(what, d.RotationKFP, "rotation");
            DumpCurve_R6(what, d.SizeOuterKFP, "sizeouter");
            DumpCurve_R6(what, d.SizeInnerKFP, "sizeinner");
        }

        private static void DumpCurves_R6(string owner, ParticleKeyframeProp[] props)
        {
            if (props == null) return;
            foreach (var p in props) DumpCurve_R6(owner, p, p?.Name.ToString());
        }

        private static void DumpCurve_R6(string owner, ParticleKeyframeProp p, string name)
        {
            var vals = p?.Values?.data_items;
            if (vals == null || vals.Length == 0) return;
            var sb = new StringBuilder();
            sb.Append($"PTFXDUMP         {owner}.{name} n={vals.Length}");
            foreach (var v in vals)
                sb.Append($"  t={v.KeyframeTime.X:0.###}:({v.KeyframeValue.X:0.####},{v.KeyframeValue.Y:0.####}," +
                          $"{v.KeyframeValue.Z:0.####},{v.KeyframeValue.W:0.####})");
            Console.WriteLine(sb.ToString());
        }

        private void StabilityProbe_R6(string fx)
        {
            PlayEffectByName_R6Fresh(fx);
            var sim = Ptfx?.Sim;
            if (sim?.Effect == null) { Console.WriteLine($"PTFXSTAB {fx}: not found"); return; }

            var sprites = new List<PtfxSimulator.Sprite>();
            int maxAlive = 0, badFrames = 0;
            float maxR = 0f, maxSize = 0f;
            var t0 = DateTime.UtcNow;
            for (int f = 0; f < 600; f++)
            {
                sim.Update(1.0f / 60.0f);
                sprites.Clear();
                sim.CollectSprites(sprites);
                maxAlive = Math.Max(maxAlive, sim.AliveCount);
                bool bad = false;
                foreach (var s in sprites)
                {
                    var d = (s.Pos - sim.Origin).Length();
                    if (!IsFinite_R6(s.Pos) || !float.IsFinite(d) || !float.IsFinite(s.Size.X) ||
                        !float.IsFinite(s.Colour.X) || !float.IsFinite(s.Rotation)) { bad = true; continue; }
                    if (d > maxR) maxR = d;
                    if (s.Size.X > maxSize) maxSize = s.Size.X;
                }
                if (bad) badFrames++;
            }
            var ms = (DateTime.UtcNow - t0).TotalMilliseconds;
            Console.WriteLine($"PTFXSTAB {fx}: 600 frames in {ms:0}ms  maxAlive={maxAlive} " +
                              $"maxRadius={maxR:0.###}m maxSprite={maxSize:0.###}m nonFiniteFrames={badFrames} " +
                              $"finalAlive={sim.AliveCount} t={sim.EffectTime:0.###}");
        }

        private static bool IsFinite_R6(SDX.Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private void PlayEffectByName_R6Fresh(string fx)
        {
            PlayEffectByName_N4(fx);
            Ptfx?.Sim?.Restart();
        }
    }
}

