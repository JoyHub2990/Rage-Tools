using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class PtfxSimulator
    {
        public Func<object, (int Cols, int Rows)> SheetGridOverride_V29;

        public static float PreviewSizeScale = 1.0f;

        public const float FixedDt = 1.0f / 60.0f;
        public const int MaxSubSteps = 6;
        public static float BoundsRadius = 300.0f;
        public static float MaxSpeed = 500.0f;

        public static readonly bool LegacyLook =
            Environment.GetEnvironmentVariable("RLE_PTFXOLD") == "1";

        public bool Playing = true;
        public bool Loop = true;
        public float TimeScale = 1.0f;
        public float EffectTime;
        public Vector3 Origin = Vector3.Zero;
        public float EffectScale = 1.0f;
        public string DebugInfo = "";
        public string EffectName = "";
        private int dbgCount;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 WindVelocity = new Vector3(1.5f, 0.0f, 0.0f);
        public int MaxParticlesPerEmitter = 4096;

        public PtfxEffect Effect { get; private set; }
        public float Duration { get; private set; } = 5.0f;
        public int AliveCount { get; private set; }
        public int StepCount { get; private set; }
        public int CulledCount { get; private set; }
        public uint Seed = 0x9E3779B9;

        private float accum;

        private readonly List<EmitterSim> emitters = new List<EmitterSim>();

        public struct Particle
        {
            public Vector3 Pos;
            public Vector3 Vel;
            public float Age;
            public float Life;
            public float Rotation;
            public float RotSpeed;
            public uint Seed;
            public int BornStep;
            public bool Alive;
        }

        public struct Sprite
        {
            public Vector3 Pos;
            public Vector2 Size;
            public Vector4 Colour;
            public float Rotation;
            public int BlendSet;
            public int EmitterIndex;

            public Vector4 UvRect;
            public Vector4 UvRect2;
            public float FrameBlend;
            public float Emissive;
        }

        private static Vector4 CellRect(int frame, int total, int gx, int gy)
        {
            frame = ((frame % total) + total) % total;
            int fu = frame % gx, fv = frame / gx;
            return new Vector4(fu / (float)gx, fv / (float)gy, 1f / gx, 1f / gy);
        }

        private class EmitterSim
        {
            public PtfxEmitter Src;
            public Particle[] Pool;
            public int Alive;
            public float SpawnAccum;
            public uint Rng;
            public uint BaseRng;
            public int Cursor;
            public uint SpawnCounter;
            public bool OneShotDone;

            public ParticleKeyframeProp SpawnRate, ParticleLife, SpeedScalar, SizeScalar,
                                        AccnScalar, DampScalar, InheritVelocity;
            public ParticleKeyframeProp AccMin, AccMax, DampMin, DampMax,
                                        ColMin, ColMax, Emissive,
                                        SizeMin, SizeMax,
                                        RotInitMin, RotInitMax, RotMin, RotMax,
                                        NoiseVelMin, NoiseVelMax, WindInfluence;
            public ParticleDomain Creation, Target;
            public int BlendSet;
            public int TexFrameMin, TexFrameMax;
            public ParticleKeyframeProp AnimRate;
            public int AnimLastFrame;
            public bool AnimOverLife, AnimRandomStart, HasTexAnim;
            public int AnimLoopMode = 1;
            public bool AnimHoldLast, AnimBlend = true;
            public int GridX = 1, GridY = 1;
            public float StartRatio, EndRatio;
            public float PlaybackScalar, ZoomScalar;
            public Vector4 TintMin, TintMax;
        }

        public PtfxEmitter GetSourceEmitter(int simIndex) =>
            simIndex >= 0 && simIndex < emitters.Count ? emitters[simIndex].Src : null;

        public void SetEffect(PtfxEffect effect)
        {
            EffectName = effect?.Name ?? "(none)";
            Effect = effect;
            emitters.Clear();
            EffectTime = 0f;
            if (effect?.Rule == null) return;

            var dur = Math.Max(effect.Rule.DurationMin, effect.Rule.DurationMax);
            Duration = float.IsFinite(dur) ? Math.Clamp(dur, 0.25f, 600.0f) : 5.0f;

            foreach (var em in effect.Emitters)
            {
                var er = em.EmitterRule;
                var pr = em.ParticleRule;
                if (er == null || pr == null) continue;

                var s = new EmitterSim
                {
                    Src = em,
                    Pool = new Particle[MaxParticlesPerEmitter],
                    BaseRng = StableSeed(Seed, effect.Name, em.Name, emitters.Count),
                    Creation = er.CreationDomainObj,
                    Target = er.TargetDomainObj,
                    BlendSet = pr.BlendSet,
                    TexFrameMin = (int)pr.TexFrameIDMin,
                    TexFrameMax = (int)pr.TexFrameIDMax,
                    StartRatio = em.Event?.StartRatio ?? 0f,
                    EndRatio = (em.Event?.EndRatio ?? 1f) <= 0f ? 1f : em.Event.EndRatio,
                    PlaybackScalar = Avg(em.Event?.PlaybackRateScalarMin ?? 1f, em.Event?.PlaybackRateScalarMax ?? 1f, 1f),
                    ZoomScalar = Avg(em.Event?.ZoomScalarMin ?? 1f, em.Event?.ZoomScalarMax ?? 1f, 1f),
                };
                foreach (var bl in new[] { pr.AllBehaviours?.data_items, pr.DrawBehaviours?.data_items })
                {
                    if (bl == null) continue;
                    foreach (var b in bl)
                        if (b is CodeWalker.GameFiles.ParticleBehaviourAnimateTexture at)
                        {
                            s.AnimRate = at.AnimRateKFP;
                            s.AnimLastFrame = at.LastFrameID;
                            s.AnimOverLife = at.IsScaledOverParticleLife != 0;
                            s.AnimRandomStart = at.IsRandomised != 0;
                            s.AnimLoopMode = at.LoopMode;
                            s.AnimHoldLast = at.IsHeldOnLastFrame != 0;
                            s.AnimBlend = at.DoFrameBlending != 0;
                            s.HasTexAnim = true;
                            break;
                        }
                    if (s.HasTexAnim) break;
                }
                {
                    int frames = Math.Max(
                        s.HasTexAnim ? s.AnimLastFrame + 1 : 1,
                        (int)pr.TexFrameIDMax + 1);
                    s.GridX = s.GridY = 1;
                    if (frames > 1)
                    {
                        CodeWalker.GameFiles.Texture sheet = null;
                        if (pr.ShaderVars?.data_items != null)
                            foreach (var sv2 in pr.ShaderVars.data_items)
                                if (sv2 is CodeWalker.GameFiles.ParticleShaderVarTexture svt2 && svt2.Texture != null)
                                {
                                    sheet = svt2.Texture;
                                    break;
                                }
                        var g = AtlasDetect.Detect(sheet, frames);
                        s.GridX = g.gx;
                        s.GridY = g.gy;
                        var ov = SheetGridOverride_V29?.Invoke(em) ?? (0, 0);
                        if (ov.Cols > 0) s.GridX = ov.Cols;
                        if (ov.Rows > 0) s.GridY = ov.Rows;
                    }
                }
                UnpackTint(em.Event?.ColourTintMin ?? 0xFFFFFFFF, out s.TintMin);
                UnpackTint(em.Event?.ColourTintMax ?? 0xFFFFFFFF, out s.TintMax);

                var ek = er.KeyframeProps;
                s.SpawnRate       = PtfxKeyframes.Find(ek, "ptxemitterrule:m_spawnrateovertimekfp");
                s.ParticleLife    = PtfxKeyframes.Find(ek, "ptxemitterrule:m_particlelifekfp");
                s.SpeedScalar     = PtfxKeyframes.Find(ek, "ptxemitterrule:m_speedscalarkfp");
                s.SizeScalar      = PtfxKeyframes.Find(ek, "ptxemitterrule:m_sizescalarkfp");
                s.AccnScalar      = PtfxKeyframes.Find(ek, "ptxemitterrule:m_accnscalarkfp");
                s.DampScalar      = PtfxKeyframes.Find(ek, "ptxemitterrule:m_dampeningscalarkfp");
                s.InheritVelocity = PtfxKeyframes.Find(ek, "ptxemitterrule:m_inheritvelocitykfp");

                foreach (var bh in pr.AllBehaviours?.data_items ?? Array.Empty<ParticleBehaviour>())
                {
                    var kp = bh?.KeyframeProps?.data_items;
                    if (kp == null) continue;
                    s.AccMin      ??= PtfxKeyframes.Find(kp, "ptxu_acceleration:m_xyzminkfp");
                    s.AccMax      ??= PtfxKeyframes.Find(kp, "ptxu_acceleration:m_xyzmaxkfp");
                    s.DampMin     ??= PtfxKeyframes.Find(kp, "ptxu_dampening:m_xyzminkfp");
                    s.DampMax     ??= PtfxKeyframes.Find(kp, "ptxu_dampening:m_xyzmaxkfp");
                    s.ColMin      ??= PtfxKeyframes.Find(kp, "ptxu_colour:m_rgbaminkfp");
                    s.ColMax      ??= PtfxKeyframes.Find(kp, "ptxu_colour:m_rgbamaxkfp");
                    s.Emissive    ??= PtfxKeyframes.Find(kp, "ptxu_colour:m_emissiveintensitykfp");
                    s.SizeMin     ??= PtfxKeyframes.Find(kp, "ptxu_size:m_whdminkfp");
                    s.SizeMax     ??= PtfxKeyframes.Find(kp, "ptxu_size:m_whdmaxkfp");
                    s.RotInitMin  ??= PtfxKeyframes.Find(kp, "ptxu_rotation:m_initialangleminkfp");
                    s.RotInitMax  ??= PtfxKeyframes.Find(kp, "ptxu_rotation:m_initialanglemaxkfp");
                    s.RotMin      ??= PtfxKeyframes.Find(kp, "ptxu_rotation:m_angleminkfp");
                    s.RotMax      ??= PtfxKeyframes.Find(kp, "ptxu_rotation:m_anglemaxkfp");
                    s.NoiseVelMin ??= PtfxKeyframes.Find(kp, "ptxu_noise:m_velnoiseminkfp");
                    s.NoiseVelMax ??= PtfxKeyframes.Find(kp, "ptxu_noise:m_velnoisemaxkfp");
                    s.WindInfluence ??= PtfxKeyframes.Find(kp, "ptxu_wind:m_influencekfp");
                }

                s.Rng = s.BaseRng;
                emitters.Add(s);
            }
            Restart();
        }

        private static uint StableSeed(uint root, string effect, string emitter, int index)
        {
            uint h = root;
            void Feed(string s)
            {
                if (s == null) return;
                foreach (var c in s)
                {
                    h += char.ToLowerInvariant(c);
                    h += h << 10;
                    h ^= h >> 6;
                }
            }
            Feed(effect); Feed("|"); Feed(emitter);
            h += (uint)index * 2654435761u;
            h += h << 3; h ^= h >> 11; h += h << 15;
            return h | 1u;
        }

        private static uint Mix(uint a, uint b)
        {
            a ^= b + 0x9E3779B9u + (a << 6) + (a >> 2);
            a ^= a >> 16; a *= 0x7FEB352Du;
            a ^= a >> 15; a *= 0x846CA68Bu;
            a ^= a >> 16;
            return a | 1u;
        }

        public void Restart()
        {
            EffectTime = 0f;
            accum = 0f;
            StepCount = 0;
            CulledCount = 0;
            AliveCount = 0;
            foreach (var e in emitters)
            {
                Array.Clear(e.Pool, 0, e.Pool.Length);
                e.Alive = 0;
                e.SpawnAccum = 0f;
                e.Cursor = 0;
                e.SpawnCounter = 0;
                e.Rng = e.BaseRng;
                e.OneShotDone = false;
            }
        }

        public void SeekTo(float time)
        {
            if (Effect == null) { EffectTime = 0f; return; }
            var target = Math.Clamp(float.IsFinite(time) ? time : 0f, 0f, Duration);
            Restart();
            int steps = Math.Clamp((int)MathF.Round(target / FixedDt), 0, 60 * 300);
            for (int i = 0; i < steps; i++) StepOnce();
            EffectTime = target;
        }

        public void Update(float wallDt)
        {
            if (Effect == null) return;
            if (!Playing) { accum = 0f; return; }
            if (!float.IsFinite(wallDt) || wallDt <= 0f) return;

            accum += Math.Min(wallDt, MaxSubSteps * FixedDt) * Math.Clamp(TimeScale, 0.01f, 10.0f);
            int steps = 0;
            while (accum >= FixedDt && steps < MaxSubSteps) { StepOnce(); accum -= FixedDt; steps++; }
            if (steps >= MaxSubSteps) accum = 0f;
        }

        private void StepOnce()
        {
            EffectTime += FixedDt;
            if (EffectTime > Duration)
            {
                if (Loop) { EffectTime -= Duration; foreach (var e in emitters) e.OneShotDone = false; }
                else EffectTime = Duration;
            }
            StepCount++;
            var effectRatio = Duration > 0f ? Math.Clamp(EffectTime / Duration, 0f, 1f) : 0f;

            AliveCount = 0;
            foreach (var e in emitters)
            {
                Spawn(e, effectRatio, FixedDt);
                Step(e, FixedDt);
                AliveCount += e.Alive;
            }
        }

        private void Spawn(EmitterSim e, float effectRatio, float dt)
        {
            var er = e.Src.EmitterRule;
            if (er == null || dt <= 0f) return;
            if (effectRatio < e.StartRatio || effectRatio > e.EndRatio) return;

            var span = Math.Max(1e-4f, e.EndRatio - e.StartRatio);
            var t = (effectRatio - e.StartRatio) / span;

            int toSpawn;
            if (er.IsOneShot != 0)
            {
                if (e.OneShotDone) return;
                e.OneShotDone = true;
                var burst = PtfxKeyframes.Evaluate(e.SpawnRate, 0f, new Vector4(10, 0, 0, 0)).X;
                toSpawn = Math.Max(1, (int)burst);
            }
            else
            {
                var rate = PtfxKeyframes.Evaluate(e.SpawnRate, t, new Vector4(10, 0, 0, 0)).X * e.PlaybackScalar;
                e.SpawnAccum += Math.Max(0f, rate) * dt;
                toSpawn = (int)e.SpawnAccum;
                e.SpawnAccum -= toSpawn;
            }

            toSpawn = Math.Clamp(toSpawn, 0, e.Pool.Length);

            for (int n = 0; n < toSpawn; n++)
            {
                int slot = -1;
                for (int i = 0; i < e.Pool.Length; i++)
                {
                    int k = e.Cursor + i;
                    if (k >= e.Pool.Length) k -= e.Pool.Length;
                    if (!e.Pool[k].Alive) { slot = k; break; }
                }
                if (slot < 0) break;
                e.Cursor = slot + 1 < e.Pool.Length ? slot + 1 : 0;

                var seed = Mix(e.BaseRng, e.SpawnCounter++);
                PtfxKeyframes.NextFloat(ref e.Rng);

                var life = PtfxKeyframes.Evaluate(e.ParticleLife, t, new Vector4(1.5f, 1.5f, 0, 0));
                var lifeVal = life.X + (life.Y - life.X) * PtfxKeyframes.NextFloat(ref e.Rng);
                if (!float.IsFinite(lifeVal) || lifeVal <= 0.01f)
                    lifeVal = float.IsFinite(life.X) && life.X > 0.01f ? life.X : 1.0f;
                lifeVal = Math.Min(lifeVal, 120.0f);

                var spawnPos = SampleDomain(e.Creation, t, ref e.Rng) * e.ZoomScalar;
                var targetPos = SampleDomain(e.Target, t, ref e.Rng) * e.ZoomScalar;

                var dir = targetPos - spawnPos;
                if (dir.LengthSquared() < 1e-6f) dir = Vector3.UnitZ;
                else dir.Normalize();

                var speed = PtfxKeyframes.Evaluate(e.SpeedScalar, t, new Vector4(1, 1, 0, 0));
                var speedVal = speed.X + (speed.Y - speed.X) * PtfxKeyframes.NextFloat(ref e.Rng);

                var rotInit = PtfxKeyframes.EvaluateMinMax(e.RotInitMin, e.RotInitMax, t, Vector4.Zero, ref e.Rng).X;
                var rotSpd = PtfxKeyframes.EvaluateMinMax(e.RotMin, e.RotMax, t, Vector4.Zero, ref e.Rng).X;

                var world = Vector3.Transform(spawnPos * EffectScale, Rotation) + Origin;
                var worldDir = Vector3.Transform(dir, Rotation);
                if (!float.IsFinite(speedVal)) speedVal = 0f;
                var vel = worldDir * (Math.Clamp(speedVal, -MaxSpeed, MaxSpeed) * EffectScale);
                if (!IsFinite(world) || !IsFinite(vel)) continue;

                e.Pool[slot] = new Particle
                {
                    Pos = world,
                    Vel = vel,
                    Age = 0f,
                    Life = lifeVal,
                    Rotation = Finite(MathUtil.DegreesToRadians(rotInit)),
                    RotSpeed = Finite(MathUtil.DegreesToRadians(rotSpd)),
                    Seed = seed,
                    BornStep = StepCount,
                    Alive = true,
                };
                e.Alive++;
            }
        }

        private void Step(EmitterSim e, float dt)
        {
            for (int i = 0; i < e.Pool.Length; i++)
            {
                if (!e.Pool[i].Alive) continue;
                ref var p = ref e.Pool[i];

                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.Alive = false;
                    e.Alive--;
                    continue;
                }
                var t = Math.Clamp(p.Age / p.Life, 0f, 1f);
                var seed = Mix(p.Seed, (uint)(StepCount - p.BornStep));

                var accnScale = PtfxKeyframes.Evaluate(e.AccnScalar, t, new Vector4(1, 1, 1, 0));
                var acc = PtfxKeyframes.EvaluateMinMax(e.AccMin, e.AccMax, t, Vector4.Zero, ref seed);
                p.Vel += new Vector3(acc.X, acc.Y, acc.Z) * accnScale.X * dt;

                if (e.WindInfluence != null)
                {
                    var infl = PtfxKeyframes.Evaluate(e.WindInfluence, t, Vector4.Zero).X;
                    p.Vel += WindVelocity * infl * dt;
                }

                if (e.NoiseVelMin != null || e.NoiseVelMax != null)
                {
                    var nz = PtfxKeyframes.EvaluateMinMax(e.NoiseVelMin, e.NoiseVelMax, t, Vector4.Zero, ref seed);
                    p.Vel += new Vector3(
                        (PtfxKeyframes.NextFloat(ref seed) - 0.5f) * 2f * nz.X,
                        (PtfxKeyframes.NextFloat(ref seed) - 0.5f) * 2f * nz.Y,
                        (PtfxKeyframes.NextFloat(ref seed) - 0.5f) * 2f * nz.Z) * dt;
                }

                var dampScale = PtfxKeyframes.Evaluate(e.DampScalar, t, new Vector4(1, 1, 1, 0)).X;
                var damp = PtfxKeyframes.EvaluateMinMax(e.DampMin, e.DampMax, t, Vector4.Zero, ref seed);
                p.Vel.X *= Math.Clamp(1f - damp.X * dampScale * dt, 0f, 1f);
                p.Vel.Y *= Math.Clamp(1f - damp.Y * dampScale * dt, 0f, 1f);
                p.Vel.Z *= Math.Clamp(1f - damp.Z * dampScale * dt, 0f, 1f);

                var sp = p.Vel.LengthSquared();
                if (!float.IsFinite(sp))
                {
                    p.Alive = false; e.Alive--; CulledCount++; continue;
                }
                if (sp > MaxSpeed * MaxSpeed) p.Vel *= MaxSpeed / MathF.Sqrt(sp);

                p.Pos += p.Vel * dt;
                p.Rotation += p.RotSpeed * dt;

                var reach = BoundsRadius * Math.Max(1f, EffectScale);
                if (!IsFinite(p.Pos) || !float.IsFinite(p.Rotation) ||
                    (p.Pos - Origin).LengthSquared() > reach * reach)
                {
                    p.Alive = false; e.Alive--; CulledCount++;
                }
            }
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        private static float Finite(float v) => float.IsFinite(v) ? v : 0f;

        public void CollectSprites(List<Sprite> sprites)
        {
            dbgCount = 0;
            for (int ei = 0; ei < emitters.Count; ei++)
            {
                var e = emitters[ei];
                for (int i = 0; i < e.Pool.Length; i++)
                {
                    if (!e.Pool[i].Alive) continue;
                    var p = e.Pool[i];
                    var t = Math.Clamp(p.Age / p.Life, 0f, 1f);
                    var seed = p.Seed;

                    var whd = PtfxKeyframes.EvaluateMinMaxUniform(e.SizeMin, e.SizeMax, t,
                                new Vector4(0.5f, 0.5f, 0, 0), ref seed);
                    var w = Math.Max(0.001f, whd.X);
                    var h = Math.Max(0.001f, whd.Y > 0.0001f ? whd.Y : whd.X);
                    if (LegacyLook)
                    {
                        var legacyScale = PtfxKeyframes.Evaluate(e.SizeScalar, t, new Vector4(1, 1, 0, 0)).X;
                        w = Math.Max(0.001f, whd.X * legacyScale) * 0.005f;
                        h = Math.Max(0.001f, (whd.Y > 0.0001f ? whd.Y : whd.X) * legacyScale) * 0.005f;
                    }
                    if (!float.IsFinite(w) || !float.IsFinite(h)) continue;

                    var col = PtfxKeyframes.EvaluateMinMaxUniform(e.ColMin, e.ColMax, t, Vector4.One, ref seed);
                    var tint = Vector4.Lerp(e.TintMin, e.TintMax, PtfxKeyframes.NextFloat(ref seed));
                    col *= tint;
                    var emissive = Math.Clamp(PtfxKeyframes.Evaluate(e.Emissive, t, Vector4.Zero).X, 0f, 64f);
                    if (!float.IsFinite(emissive)) emissive = 0f;

                    var uvRect = new Vector4(0, 0, 1, 1);
                    var uvRect2 = uvRect;
                    float frameBlend = 0f;
                    if (e.GridX > 1 || e.GridY > 1)
                    {
                        var total = Math.Max(1, e.GridX * e.GridY);
                        float rate = e.HasTexAnim
                            ? PtfxKeyframes.Evaluate(e.AnimRate, t, new Vector4(12, 0, 0, 0)).X : 0f;
                        var fr = SheetFrame_V29(e.TexFrameMin, e.TexFrameMax, e.HasTexAnim ? e.AnimLastFrame + 1 : 1,
                                                e.HasTexAnim, e.AnimLoopMode, e.AnimHoldLast, e.AnimOverLife,
                                                e.AnimBlend && !LegacyLook, t, p.Age, rate, p.Seed, total);
                        uvRect = CellRect(fr.Cell, total, e.GridX, e.GridY);
                        uvRect2 = CellRect(fr.Next, total, e.GridX, e.GridY);
                        frameBlend = fr.Blend;
                    }

                    if (dbgCount++ == 0)
                        DebugInfo = $"{EffectName}  size={w:0.###}x{h:0.###}m  grid={e.GridX}x{e.GridY}  " +
                                    $"frames={(e.HasTexAnim ? e.AnimLastFrame + 1 : 1)}  texID={e.TexFrameMin}..{e.TexFrameMax}  " +
                                    $"blend={(e.BlendSet == 0 ? "alpha" : "add")}  step={StepCount}  culled={CulledCount}";
                    sprites.Add(new Sprite
                    {
                        Pos = p.Pos,
                        UvRect = uvRect,
                        UvRect2 = uvRect2,
                        FrameBlend = Math.Clamp(frameBlend, 0f, 1f),
                        Emissive = LegacyLook ? 0f : emissive,
                        Size = new Vector2(w, h) * (PreviewSizeScale * EffectScale),
                        Colour = LegacyLook
                            ? new Vector4(col.X * (1f + emissive), col.Y * (1f + emissive),
                                          col.Z * (1f + emissive), Math.Clamp(col.W, 0f, 1f))
                            : new Vector4(Math.Clamp(col.X, 0f, 8f), Math.Clamp(col.Y, 0f, 8f),
                                          Math.Clamp(col.Z, 0f, 8f), Math.Clamp(col.W, 0f, 1f)),
                        Rotation = p.Rotation,
                        BlendSet = e.BlendSet,
                        EmitterIndex = ei,
                    });
                }
            }
        }

        private Vector3 SampleDomain(ParticleDomain d, float t, ref uint rng)
        {
            if (d == null) return Vector3.Zero;

            var pos = PtfxKeyframes.Evaluate(d.PositionKFP, t, Vector4.Zero);
            var outer = PtfxKeyframes.Evaluate(d.SizeOuterKFP, t, Vector4.Zero);
            var inner = PtfxKeyframes.Evaluate(d.SizeInnerKFP, t, Vector4.Zero);
            var rotDeg = PtfxKeyframes.Evaluate(d.RotationKFP, t, Vector4.Zero);

            Vector3 local;
            switch (d.DomainType)
            {
                case ParticleDomainType.Sphere:
                {
                    var u = PtfxKeyframes.NextFloat(ref rng) * 2f - 1f;
                    var ang = PtfxKeyframes.NextFloat(ref rng) * MathUtil.TwoPi;
                    var r = Lerp(inner.X, Math.Max(inner.X, outer.X), PtfxKeyframes.NextFloat(ref rng));
                    var s = (float)Math.Sqrt(Math.Max(0f, 1f - u * u));
                    local = new Vector3(s * (float)Math.Cos(ang), s * (float)Math.Sin(ang), u) * r;
                    break;
                }
                case ParticleDomainType.Cylinder:
                {
                    var ang = PtfxKeyframes.NextFloat(ref rng) * MathUtil.TwoPi;
                    var r = Lerp(inner.X, Math.Max(inner.X, outer.X), PtfxKeyframes.NextFloat(ref rng));
                    var z = (PtfxKeyframes.NextFloat(ref rng) - 0.5f) * 2f * Math.Max(0.0001f, outer.Z);
                    local = new Vector3(r * (float)Math.Cos(ang), r * (float)Math.Sin(ang), z);
                    break;
                }
                case ParticleDomainType.Box:
                default:
                {
                    local = new Vector3(
                        (PtfxKeyframes.NextFloat(ref rng) - 0.5f) * 2f * outer.X,
                        (PtfxKeyframes.NextFloat(ref rng) - 0.5f) * 2f * outer.Y,
                        (PtfxKeyframes.NextFloat(ref rng) - 0.5f) * 2f * outer.Z);
                    break;
                }
            }

            if (Math.Abs(rotDeg.X) + Math.Abs(rotDeg.Y) + Math.Abs(rotDeg.Z) > 0.001f)
            {
                var q = Quaternion.RotationYawPitchRoll(
                    MathUtil.DegreesToRadians(rotDeg.Z),
                    MathUtil.DegreesToRadians(rotDeg.X),
                    MathUtil.DegreesToRadians(rotDeg.Y));
                local = Vector3.Transform(local, q);
            }
            return new Vector3(pos.X, pos.Y, pos.Z) + local;
        }

        private static float Lerp(float a, float b, float f) => a + (b - a) * f;

        private static float Avg(float a, float b, float dflt)
        {
            var v = (a + b) * 0.5f;
            return v > 0.0001f ? v : dflt;
        }

        private static void UnpackTint(uint rgba, out Vector4 v)
        {
            var a = ((rgba >> 24) & 0xFF) / 255f;
            var r = ((rgba >> 16) & 0xFF) / 255f;
            var g = ((rgba >> 8) & 0xFF) / 255f;
            var b = (rgba & 0xFF) / 255f;
            if (a <= 0.001f && r <= 0.001f && g <= 0.001f && b <= 0.001f) { v = Vector4.One; return; }
            v = new Vector4(r, g, b, a);
        }
    }
}

