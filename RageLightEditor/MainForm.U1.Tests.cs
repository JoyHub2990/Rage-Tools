using System;
using System.IO;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static int u1Checks;

        partial void SeqTest_U1(Action<string, bool, string> check)
        {
            u1Checks = 0;
            void Check(string what, bool ok, string detail) { u1Checks++; check(what, ok, detail); }

            SpawnChecks_U1(Check);
            SoundChecks_U1(Check);
            AnimChecks_U1(Check);
            ParticleAudioChecks_U1(Check);
            RpfOpenChecks_U1(Check);

            Console.WriteLine($"U1 CHECKS RAN: {u1Checks}");
        }

        private void SpawnChecks_U1(Action<string, bool, string> check)
        {
            check("u1: the world spawn is the position that was asked for",
                  Math.Abs(WorldSpawn_U1.X - 220.911f) < 0.0005f &&
                  Math.Abs(WorldSpawn_U1.Y - (-1060.756f)) < 0.0005f &&
                  Math.Abs(WorldSpawn_U1.Z - 60.948f) < 0.0005f,
                  FormatSpawn_U1());

            float cy = MathF.Cos(WorldSpawnYaw_U1), sy = MathF.Sin(WorldSpawnYaw_U1);
            float cp = MathF.Cos(WorldSpawnPitch_U1), sp = MathF.Sin(WorldSpawnPitch_U1);
            var fwd = new Vector3(-cy * cp, -sy * cp, -sp);
            var toTowers = Vector3.Normalize(new Vector3(-75.0f, -818.0f, 100.0f) - WorldSpawn_U1);
            float agree = Vector3.Dot(new Vector3(fwd.X, fwd.Y, 0), Vector3.Normalize(new Vector3(toTowers.X, toTowers.Y, 0)));
            check("u1: ...and the spawn heading points at downtown", agree > 0.95f, $"cos {agree:0.000}");
            check("u1: ...tilted down rather than up", fwd.Z < 0.0f, $"forward z {fwd.Z:0.000}");

            if (camera != null)
            {
                var was = camera.Capture();
                float wasFov = settings?.FovDeg ?? 85.0f;
                camera.Target = new Vector3(-3000, 4000, 20);
                camera.Distance = 40; camera.Yaw = 1.0f; camera.Pitch = -0.4f;
                camera.SnapSmoothing(); camera.Update();
                float away = (camera.Position - WorldSpawn_U1).Length();

                GoToWorldSpawn_U1();
                camera.Update();
                float landed = (camera.Position - WorldSpawn_U1).Length();
                check("u1: Reset the view goes back to the spawn from anywhere",
                      away > 1000.0f && landed < 0.05f, $"{away:0} m away -> {landed:0.000} m");

                camera.Restore(was);
                if (settings != null) settings.FovDeg = wasFov;
                camera.Update();
            }
            else check("u1: Reset the view goes back to the spawn from anywhere", false, "no camera");
        }

        private void SoundChecks_U1(Action<string, bool, string> check)
        {
            foreach (UiSound.Cue c in new[] { UiSound.Cue.Success, UiSound.Cue.Error,
                                              UiSound.Cue.InvalidDrop, UiSound.Cue.Blocked })
            {
                var clip = UiSound.Clip(c);
                float ms = clip == null ? 0 : clip.Length * 1000.0f / AudioEngine_U1.SampleRate;
                float peak = 0.0f;
                if (clip != null) for (int i = 0; i < clip.Length; i++) peak = Math.Max(peak, Math.Abs(clip[i]));
                check($"u1: the {c} cue is embedded and decodes",
                      clip != null && clip.Length > 128, clip == null ? UiSound.Trouble[(int)c] : clip.Length + " samples");
                check($"u1: ...{c} is short and quiet (a UI blip, not a jingle)",
                      clip != null && ms > 20.0f && ms < 400.0f && peak > 0.05f && peak <= 0.5f,
                      $"{ms:0} ms, peak {peak:0.00}");
            }

            UiSound.ClearRepeatGuard();
            int reached = 0;
            bool threw = false;
            try
            {
                foreach (UiSound.Cue c in new[] { UiSound.Cue.Success, UiSound.Cue.Error,
                                                  UiSound.Cue.InvalidDrop, UiSound.Cue.Blocked })
                    if (UiSound.PlayNow(c)) reached++;
            }
            catch (Exception ex) { threw = true; Console.WriteLine("U1 SOUND THREW: " + ex); }
            check("u1: playing all four cues never throws, with or without an audio device",
                  !threw, AudioEngine_U1.Available
                          ? $"device open, {reached} of 4 voices started"
                          : $"no device ({AudioEngine_U1.Trouble}), {reached} of 4 started, no exception");
            check("u1: with a device present all four cues actually start a voice",
                  !AudioEngine_U1.Available || reached == 4, reached + " of 4");

            bool wasEnabled = UiSound.Enabled, wasSup = UiSound.Suppressed;
            UiSound.Enabled = false; UiSound.Suppressed = false;
            bool offSilences = UiSound.Silent;
            UiSound.Enabled = true; UiSound.Suppressed = true;
            bool supSilences = UiSound.Silent;
            UiSound.Suppressed = false;
            bool onSpeaks = !UiSound.Silent;
            UiSound.Enabled = wasEnabled; UiSound.Suppressed = wasSup;
            check("u1: 'UI sounds' off silences everything, and so does a headless / photo / render frame",
                  offSilences && supSilences && onSpeaks,
                  $"off={offSilences} suppressed={supSilences} on={onSpeaks}");
            check("u1: ...and nothing plays at all now that the sounds are gone",
                  !Editor.UiSound.Enabled && Editor.UiSound.Silent, "silent");

            var wav = MakeTestWav_U1(2000, 22050, 1);
            var back = AudioEngine_U1.DecodeWav(wav, out var err);
            check("u1: the wav decoder reads a 16-bit mono file and resamples it to the mixer's rate",
                  back != null && Math.Abs(back.Length - 2000 * (44100.0 / 22050.0)) < 4,
                  back == null ? err : back.Length + " samples from 2000 @22050");
            var stereo = AudioEngine_U1.DecodeWav(MakeTestWav_U1(1024, 44100, 2), out var err2);
            check("u1: ...and folds a stereo file to mono at the native rate",
                  stereo != null && stereo.Length == 1024, stereo == null ? err2 : stereo.Length + " samples");
            check("u1: ...and refuses rubbish instead of throwing",
                  AudioEngine_U1.DecodeWav(new byte[] { 1, 2, 3, 4 }, out _) == null &&
                  AudioEngine_U1.DecodeWav(null, out _) == null, "null");
        }

        private static byte[] MakeTestWav_U1(int frames, int rate, int channels)
        {
            int bytes = frames * channels * 2;
            var b = new byte[44 + bytes];
            void Str(int at, string s) { for (int i = 0; i < s.Length; i++) b[at + i] = (byte)s[i]; }
            void I32(int at, int v) => BitConverter.GetBytes(v).CopyTo(b, at);
            void I16(int at, short v) => BitConverter.GetBytes(v).CopyTo(b, at);
            Str(0, "RIFF"); I32(4, 36 + bytes); Str(8, "WAVE");
            Str(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, (short)channels);
            I32(24, rate); I32(28, rate * channels * 2); I16(32, (short)(channels * 2)); I16(34, 16);
            Str(36, "data"); I32(40, bytes);
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++)
                    I16(44 + (f * channels + c) * 2, (short)(f % 256 * 8));
            return b;
        }

        private void AnimChecks_U1(Action<string, bool, string> check)
        {
            bool wasOff = UiAnim.Disabled;
            UiAnim.Disabled = false;
            UiAnim.Cancel("u1.probe");
            check("u1: an animation that was never started reads as finished",
                  UiAnim.Progress("u1.probe", 0.2f) >= 1.0f, "1.0");
            UiAnim.Start("u1.probe");
            float p0 = UiAnim.Progress("u1.probe", 5.0f);
            check("u1: a fresh animation starts at the beginning and is running",
                  p0 >= 0.0f && p0 < 0.2f && UiAnim.Running("u1.probe", 5.0f), $"{p0:0.000}");
            check("u1: it finishes when its time is up",
                  UiAnim.Progress("u1.probe", 0.0000001f) >= 1.0f, "1.0");
            UiAnim.Disabled = true;
            check("u1: a scripted run draws every animation finished, so captures are repeatable",
                  UiAnim.Progress("u1.probe", 5.0f) >= 1.0f, "1.0");
            UiAnim.Disabled = wasOff;
            UiAnim.Cancel("u1.probe");

            check("u1: the ease is a real 0..1 curve with flat ends",
                  Math.Abs(UiAnim.Ease(0)) < 1e-5f && Math.Abs(UiAnim.Ease(1) - 1) < 1e-5f &&
                  Math.Abs(UiAnim.Ease(0.5f) - 0.5f) < 1e-3f,
                  $"{UiAnim.Ease(0.25f):0.000} at a quarter");
        }

        private void ParticleAudioChecks_U1(Action<string, bool, string> check)
        {
            var eye = new Vector3(0, 0, 0);
            var right = new Vector3(0, 1, 0);
            MainForm.GainsFor_U1(new Vector3(0.0f, 0.0f, 0.0f), eye, right, 1.0f, 20.0f, out float l0, out float r0);
            MainForm.GainsFor_U1(new Vector3(20.0f, 0.0f, 0.0f), eye, right, 1.0f, 20.0f, out float lF, out float rF);
            MainForm.GainsFor_U1(new Vector3(0.0f, 5.0f, 0.0f), eye, right, 1.0f, 20.0f, out float lR, out float rR);
            MainForm.GainsFor_U1(new Vector3(0.0f, -5.0f, 0.0f), eye, right, 1.0f, 20.0f, out float lL, out float rL);
            check("u1: an emitter at the camera is at full level and dead centre",
                  l0 > 0.6f && Math.Abs(l0 - r0) < 0.02f, $"L{l0:0.000} R{r0:0.000}");
            check("u1: an emitter at its falloff radius is silent",
                  lF < 0.0005f && rF < 0.0005f, $"L{lF:0.000} R{rF:0.000}");
            check("u1: one to the camera's right is louder on the right, and the mirror image on the left",
                  rR > lR * 1.5f && lL > rL * 1.5f, $"right L{lR:0.00}/R{rR:0.00}  left L{lL:0.00}/R{rL:0.00}");
            check("u1: the pan is constant power - nothing dips as it crosses the middle",
                  Math.Abs(MathF.Sqrt(lR * lR + rR * rR) - MathF.Sqrt(l0 * l0 + r0 * r0) * 0.5625f) < 0.25f,
                  $"{MathF.Sqrt(lR * lR + rR * rR):0.000}");

            string tmp = Path.Combine(Path.GetTempPath(), "rle_u1_" + Guid.NewGuid().ToString("N") + ".ypt");
            try
            {
                var doc = new PtfxAudioDoc_U1();
                doc.Bed.File = @"C:\sounds\room.wav";
                doc.Bed.Volume = 0.42f;
                doc.Emitters.Add(new PtfxAudioEmitter_U1
                {
                    Name = "burst", File = @"C:\sounds\whoosh.wav", GameSound = "fire_loop",
                    OffsetX = 1.5f, OffsetY = -2.25f, OffsetZ = 0.75f,
                    Volume = 0.7f, Radius = 33.0f, Loop = true, Delay = 0.85f,
                });
                doc.Save(tmp);
                bool wrote = File.Exists(PtfxAudioDoc_U1.SidecarPath(tmp));
                var back = PtfxAudioDoc_U1.Load(tmp);
                var e = back.Emitters.Count > 0 ? back.Emitters[0] : null;
                check("u1: the effect's audio is written beside the .ypt and read back whole",
                      wrote && e != null && e.Name == "burst" && e.Loop &&
                      Math.Abs(e.Delay - 0.85f) < 1e-4f && Math.Abs(e.Radius - 33.0f) < 1e-4f &&
                      Math.Abs(e.OffsetY - (-2.25f)) < 1e-4f && e.GameSound == "fire_loop" &&
                      Math.Abs(back.Bed.Volume - 0.42f) < 1e-4f,
                      wrote ? $"{back.Emitters.Count} emitter(s), bed {back.Bed.Volume:0.00}" : "no sidecar written");

                var text = File.ReadAllText(PtfxAudioDoc_U1.SidecarPath(tmp));
                check("u1: ...and carries no runtime state (voice handles, fired flags)",
                      !text.Contains("\"Voice\"") && !text.Contains("\"Fired\"") && !text.Contains("\"Trouble\""),
                      text.Length + " bytes");

                var empty = new PtfxAudioDoc_U1();
                empty.Save(tmp);
                check("u1: removing all the audio removes the sidecar",
                      !File.Exists(PtfxAudioDoc_U1.SidecarPath(tmp)), "gone");
            }
            catch (Exception ex)
            {
                check("u1: the effect's audio is written beside the .ypt and read back whole", false, ex.Message);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
                try { File.Delete(PtfxAudioDoc_U1.SidecarPath(tmp)); } catch { }
            }

            {
                var em = new PtfxAudioEmitter_U1 { Delay = 0.5f, Loop = false, GameSound = "x" };
                float last = -1.0f;
                int fires = 0; float firstAt = -1.0f;
                for (int i = 0; i < 600; i++)
                {
                    float t = (i % 200) * 0.01f;
                    if (t < last - 1e-4f) em.Fired = false;
                    last = t;
                    if (t + 1e-4f >= em.Delay && !em.Fired)
                    {
                        em.Fired = true;
                        fires++;
                        if (firstAt < 0) firstAt = t;
                    }
                }
                check("u1: an emitter fires at its delay, once per pass of a looping effect",
                      fires == 3 && Math.Abs(firstAt - 0.5f) < 0.011f, $"{fires} fires, first at {firstAt:0.00}s");
            }

            check("u1: a game audio name still fires something audible at the right moment",
                  PtfxAudioClips_U1.Pip != null && PtfxAudioClips_U1.Pip.Length > 100,
                  PtfxAudioClips_U1.Pip?.Length + " samples");
            check("u1: an .ogg is refused with a reason rather than silently doing nothing",
                  AudioEngine_U1.DecodeWavFile("nope.ogg", out var oggErr) == null &&
                  oggErr != null && oggErr.Contains("decoder"), oggErr);
        }

        private void RpfOpenChecks_U1(Action<string, bool, string> check)
        {
            var ex = new RpfExplorer();
            string dir = Path.Combine(Path.GetTempPath(), "rle_u1rpf_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                ex.BackgroundOpen_U1 = true;
                bool wait = false;
                int jobsBefore = RpfExplorer.JobsRun_U1;
                ex.ArchiveFolderGate_U1(dir, ref wait);
                check("u1: a folder with nothing to open is never deferred and starts no job",
                      !wait && RpfExplorer.JobsRun_U1 == jobsBefore, "no wait, no job");

                wait = false;
                ex.ArchiveFolderGate_U1(Path.Combine(dir, "gone"), ref wait);
                check("u1: a folder that is not there is answered, not thrown at", !wait, "no wait");

                File.WriteAllBytes(Path.Combine(dir, "notes.txt"), new byte[] { 65, 66 });
                wait = false;
                ex.ArchiveFolderGate_U1(dir, ref wait);
                check("u1: loose files in a folder are not mistaken for archives to open", !wait, "no wait");

                File.WriteAllBytes(Path.Combine(dir, "made.rpf"), new byte[4096]);
                wait = false;
                ex.ArchiveFolderGate_U1(dir, ref wait);
                var job = ex.OpenJob_U1Current;
                check("u1: an archive that has never been read is opened on a worker, not on the UI thread",
                      wait && job != null && job.Total == 1 && job.TotalBytes == 4096,
                      job == null ? "no job" : $"{job.Total} archive(s), {job.TotalBytes} bytes");
                for (int i = 0; i < 200 && job != null && !job.Finished; i++) System.Threading.Thread.Sleep(10);
                wait = false;
                ex.ArchiveFolderGate_U1(dir, ref wait);
                check("u1: ...and once it has been read the folder opens with no wait at all",
                      !wait && ex.OpenJob_U1Current == null,
                      job != null ? $"{job.Seconds:0.000}s, {job.Entries} entries" : "");
                wait = false;
                ex.ArchiveFolderGate_U1(dir, ref wait);
                check("u1: an archive that could not be read is not re-read on every frame",
                      !wait && ex.OpenJob_U1Current == null, "cached as a failure");
            }
            catch (Exception e)
            {
                check("u1: the RPF opener survives a temp folder", false, e.Message);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}

