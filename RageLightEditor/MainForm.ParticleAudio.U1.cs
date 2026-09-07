using System;
using System.IO;
using System.Windows.Forms;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private float ptfxAudioLastTime_U1 = -1.0f;
        private bool ptfxAudioWasPlaying_U1;
        private PtfxAudioDoc_U1 ptfxAudioDoc_U1;
        private static readonly bool animForced_U1 =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_U1ANIM"));

        partial void AudioTick_U1()
        {
            if (settings != null) UiSound.Enabled = settings.UiSounds;
            bool suppress = IsHeadless || screenshotPath != null || photoMode ||
                            renderStillPending || renderingStill || renderSeqPending ||
                            DebugVideoTest || DebugRenderOut != null ||
                            DebugSeqTest || DebugWorldTest || DebugArchiveTest ||
                            (panel != null && panel.CineMode && panel.Playing);
            UiSound.Suppressed = suppress;
            UiAnim.Disabled = (IsHeadless || screenshotPath != null) && !animForced_U1;

            ServiceParticleAudioRequests_U1();
            ServiceParticleAudioEnv_U1();
            TickParticleAudio_U1();
        }

        private void ServiceParticleAudioRequests_U1()
        {
            var p = Ptfx;
            if (p == null) return;

            if (p.RequestPickEmitterWav_U1 >= 0)
            {
                int i = p.RequestPickEmitterWav_U1;
                p.RequestPickEmitterWav_U1 = -1;
                var em = EmitterAt_U1(p, i);
                if (em != null && !IsHeadless)
                {
                    var path = PickWav_U1("Sound for " + em.Name);
                    if (path != null) { em.File = path; PtfxAudioClips_U1.Forget(path); StopEmitter_U1(em); }
                }
            }
            if (p.RequestPickBedWav_U1)
            {
                p.RequestPickBedWav_U1 = false;
                var bed = p.Doc?.Audio_U1?.Bed;
                if (bed != null && !IsHeadless)
                {
                    var path = PickWav_U1("Ambient bed");
                    if (path != null)
                    {
                        bed.File = path;
                        PtfxAudioClips_U1.Forget(path);
                        if (bed.Voice != 0) { AudioEngine_U1.Stop(bed.Voice); bed.Voice = 0; }
                    }
                }
            }
            if (p.RequestEmitterAtView_U1 >= 0)
            {
                int i = p.RequestEmitterAtView_U1;
                p.RequestEmitterAtView_U1 = -1;
                var em = EmitterAt_U1(p, i);
                if (em != null)
                {
                    var at = ParticleViewPoint_N4() - p.Sim.Origin;
                    if (float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z))
                    {
                        em.OffsetX = at.X; em.OffsetY = at.Y; em.OffsetZ = at.Z;
                        p.Status = $"{em.Name} placed at ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}) from the effect";
                    }
                }
            }
        }

        private static PtfxAudioEmitter_U1 EmitterAt_U1(ParticlePanel p, int i)
        {
            var list = p?.Doc?.Audio_U1?.Emitters;
            return list != null && i >= 0 && i < list.Count ? list[i] : null;
        }

        private string PickWav_U1(string title)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Sounds (*.wav;*.ogg)|*.wav;*.ogg|All files (*.*)|*.*",
                Title = title,
            };
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        private void TickParticleAudio_U1()
        {
            var p = Ptfx;
            var doc = p?.Doc;
            var audio = doc?.Audio_U1;
            if (!ReferenceEquals(audio, ptfxAudioDoc_U1))
            {
                StopAllParticleAudio_U1(ptfxAudioDoc_U1);
                ptfxAudioDoc_U1 = audio;
                ptfxAudioWasPlaying_U1 = false;
                ptfxAudioLastTime_U1 = -1.0f;
            }
            if (audio == null) return;

            bool live = panel != null && panel.Workspace == Editor.LightPanel.Space.Particles &&
                        (!UiSound.Silent || ptfxAudioForced_U1) && AudioEngine_U1.Available;
            var sim = p.Sim;
            bool playing = live && sim != null && sim.Playing && sim.Effect != null;

            if (!playing)
            {
                if (ptfxAudioWasPlaying_U1 && ptfxAudioForced_U1)
                    Console.WriteLine($"PTFXAUDIO silenced - the timeline is not running (playing={sim?.Playing})");
                if (ptfxAudioWasPlaying_U1) StopAllParticleAudio_U1(audio);
                ptfxAudioWasPlaying_U1 = false;
                ptfxAudioLastTime_U1 = sim?.EffectTime ?? 0.0f;
                return;
            }
            ptfxAudioWasPlaying_U1 = true;

            float t = sim.EffectTime;
            if (t < ptfxAudioLastTime_U1 - 0.0001f)
                foreach (var em in audio.Emitters) { em.Fired = false; StopEmitter_U1(em); }
            ptfxAudioLastTime_U1 = t;

            var eye = camera?.Position ?? Vector3.Zero;
            var fwd = camera?.GetForward() ?? Vector3.UnitX;
            var right = Vector3.Cross(fwd, Vector3.UnitZ);
            if (right.LengthSquared() < 1e-6f) right = Vector3.UnitY;
            right.Normalize();

            var bed = audio.Bed;
            if (bed != null && bed.Enabled && bed.HasSource)
            {
                float g = Math.Clamp(bed.Volume, 0.0f, 1.0f);
                if (bed.Voice == 0 || !AudioEngine_U1.Playing(bed.Voice))
                {
                    var clip = PtfxAudioClips_U1.Get(bed.File, out var err);
                    bed.Trouble = err ?? "";
                    bed.Voice = clip == null ? 0 : AudioEngine_U1.Play(clip, g, g, true);
                }
                else AudioEngine_U1.Update(bed.Voice, g, g);
            }
            else if (bed != null && bed.Voice != 0) { AudioEngine_U1.Stop(bed.Voice); bed.Voice = 0; }

            foreach (var em in audio.Emitters)
            {
                if (em == null) continue;
                if (!em.Enabled || !em.HasSource)
                {
                    StopEmitter_U1(em);
                    continue;
                }

                var world = sim.Origin + new Vector3(em.OffsetX, em.OffsetY, em.OffsetZ);
                GainsFor_U1(world, eye, right, em.Volume, em.Radius, out float gl, out float gr);

                bool due = t + 1e-4f >= em.Delay;
                if (!due) { StopEmitter_U1(em); continue; }

                if (!em.Fired)
                {
                    em.Fired = true;
                    var clip = ClipFor_U1(em, out var err);
                    em.Trouble = err ?? "";
                    if (clip != null) em.Voice = AudioEngine_U1.Play(clip, gl, gr, em.Loop);
                    if (ptfxAudioForced_U1)
                        Console.WriteLine($"PTFXAUDIO fired '{em.Name}' at effect t={t:0.###}s " +
                                          $"(delay {em.Delay:0.###}s) voice={em.Voice} " +
                                          $"gains L{gl:0.000} R{gr:0.000} loop={em.Loop}");
                }
                else if (em.Voice != 0)
                {
                    AudioEngine_U1.Update(em.Voice, gl, gr);
                }
            }
        }

        internal static void GainsFor_U1(Vector3 world, Vector3 eye, Vector3 camRight,
                                         float volume, float radius, out float gl, out float gr)
        {
            var d = world - eye;
            float dist = d.Length();
            float r = Math.Max(0.5f, radius);
            float att = Math.Clamp(1.0f - dist / r, 0.0f, 1.0f);
            att *= att;
            float g = Math.Clamp(volume, 0.0f, 1.0f) * att;

            float pan = 0.0f;
            if (dist > 0.001f)
            {
                pan = Math.Clamp(Vector3.Dot(d / dist, camRight), -1.0f, 1.0f);
                pan *= Math.Clamp(dist / 1.5f, 0.0f, 1.0f);
            }
            float a = (pan + 1.0f) * (MathF.PI * 0.25f);
            gl = g * MathF.Cos(a);
            gr = g * MathF.Sin(a);
        }

        private static float[] ClipFor_U1(PtfxAudioEmitter_U1 em, out string error)
        {
            error = "";
            if (!string.IsNullOrWhiteSpace(em.File)) return PtfxAudioClips_U1.Get(em.File, out error);
            error = "game audio is not decoded here - this is a timing pip";
            return PtfxAudioClips_U1.Pip;
        }

        private static void StopEmitter_U1(PtfxAudioEmitter_U1 em)
        {
            if (em == null || em.Voice == 0) return;
            AudioEngine_U1.Stop(em.Voice);
            em.Voice = 0;
        }

        private static void StopAllParticleAudio_U1(PtfxAudioDoc_U1 audio)
        {
            if (audio == null) return;
            foreach (var em in audio.Emitters) StopEmitter_U1(em);
            if (audio.Bed != null && audio.Bed.Voice != 0)
            {
                AudioEngine_U1.Stop(audio.Bed.Voice);
                audio.Bed.Voice = 0;
            }
        }

        private bool ptfxAudioEnvDone_U1;
        private static readonly bool ptfxAudioForced_U1 =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RLE_PTFXAUDIO"));

        private void ServiceParticleAudioEnv_U1()
        {
            if (ptfxAudioEnvDone_U1) return;
            var spec = Environment.GetEnvironmentVariable("RLE_PTFXAUDIO");
            if (string.IsNullOrWhiteSpace(spec)) { ptfxAudioEnvDone_U1 = true; return; }
            var p = Ptfx;
            if (p?.Doc == null || p.Sim?.Effect == null) return;
            ptfxAudioEnvDone_U1 = true;

            var parts = spec.Split(',');
            var em = new PtfxAudioEmitter_U1
            {
                Name = "headless",
                File = parts.Length > 0 ? parts[0].Trim() : "",
                Delay = parts.Length > 1 && float.TryParse(parts[1], out var d) ? d : 0.0f,
                Radius = parts.Length > 2 && float.TryParse(parts[2], out var r) ? r : 25.0f,
                Loop = parts.Length > 3 && parts[3].Trim().Equals("loop", StringComparison.OrdinalIgnoreCase),
                OffsetZ = 1.0f,
            };
            if (!File.Exists(em.File)) { em.GameSound = em.File; em.File = ""; }
            p.Doc.Audio_U1.Emitters.Add(em);

            var clip = ClipFor_U1(em, out var err);
            var world = p.Sim.Origin + new Vector3(em.OffsetX, em.OffsetY, em.OffsetZ);
            var eye = camera?.Position ?? Vector3.Zero;
            var fwd = camera?.GetForward() ?? Vector3.UnitX;
            var right = Vector3.Cross(fwd, Vector3.UnitZ);
            if (right.LengthSquared() > 1e-6f) right.Normalize(); else right = Vector3.UnitY;
            GainsFor_U1(world, eye, right, em.Volume, em.Radius, out float gl, out float gr);
            Console.WriteLine($"PTFXAUDIO emitter '{em.Name}' src={(em.File.Length > 0 ? em.File : "game:" + em.GameSound)} " +
                              $"delay={em.Delay:0.###}s radius={em.Radius:0.#} loop={em.Loop} " +
                              $"clip={(clip == null ? "none (" + err + ")" : clip.Length + " samples")} " +
                              $"at ({world.X:0.##},{world.Y:0.##},{world.Z:0.##}) " +
                              $"dist={(world - eye).Length():0.##} gains L{gl:0.000} R{gr:0.000} " +
                              $"device={(AudioEngine_U1.Available ? "yes" : "no - " + AudioEngine_U1.Trouble)}");
        }
    }
}

