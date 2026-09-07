using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX.Direct3D11;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private ParticleRenderer particleRenderer;
        private readonly Stopwatch particleClock = new Stopwatch();
        private double lastParticleTick;
        private readonly List<PtfxSimulator.Sprite> particleSprites = new List<PtfxSimulator.Sprite>(2048);
        private bool particleYptsListed;
        private bool ptfxFlagDone;

        private ParticlePanel Ptfx => panel?.Particles;

        private void EnsureParticles_N4()
        {
            if (panel == null) return;
            if (panel.Particles == null)
            {
                panel.Particles = new ParticlePanel();
                particleClock.Start();
                panel.Particles.SheetTextureId_V29 = SheetTextureId_V26;
            }
            if (particleRenderer == null && deviceResources?.Device != null && textureLoader != null)
                particleRenderer = new ParticleRenderer(deviceResources.Device, textureLoader);
        }

        private readonly System.Collections.Generic.Dictionary<CodeWalker.GameFiles.Texture, IntPtr> sheetTexIds_V26
            = new System.Collections.Generic.Dictionary<CodeWalker.GameFiles.Texture, IntPtr>();

        private IntPtr SheetTextureId_V26(CodeWalker.GameFiles.Texture t)
        {
            if (t == null || imguiRenderer == null) return IntPtr.Zero;
            if (sheetTexIds_V26.TryGetValue(t, out var id)) return id;
            var srv = t.Data?.FullData != null ? textureLoader?.GetSRV(t, false) : null;
            id = srv != null ? imguiRenderer.RegisterTexture(srv) : IntPtr.Zero;
            sheetTexIds_V26[t] = id;
            return id;
        }

        private void DisposeParticles_N4()
        {
            particleRenderer?.Dispose();
            particleRenderer = null;
        }

        private void ServiceParticles_N4()
        {
            EnsureParticles_N4();
            var p = Ptfx;
            if (p == null) return;

            if (!particleYptsListed && gameFiles != null && gameFiles.Ready)
            {
                particleYptsListed = true;
                try { p.GameYpts = PtfxGameIndex.ListYpts(gameFiles); }
                catch (Exception ex) { p.Status = "could not list the game's .ypt files: " + ex.Message; }
            }

            var want = Environment.GetEnvironmentVariable("RLE_PTFX");
            if (!ptfxFlagDone && !string.IsNullOrWhiteSpace(want) && gameFiles != null && gameFiles.Ready)
            {
                ptfxFlagDone = true;
                panel.SwitchWorkspace(LightPanel.Space.Particles);
                p.Sim.Origin = camera.Position + camera.GetForward() * 3.0f;
                PlayEffectByName_N4(want.Trim());
                WarmParticles_N4(1.5f);
            }

            RefreshYtypTargets_N4(p);

            if (p.RequestOpenYpt) { p.RequestOpenYpt = false; DoOpenYptDialog_N4(); }
            if (p.RequestSave)
            {
                p.RequestSave = false;
                try { var path = p.Doc?.Save(); p.Status = path != null ? "saved " + path : "nothing open"; Editor.UiSound.Success(); }
                catch (Exception ex) { p.Status = "save failed: " + ex.Message; Editor.UiSound.Error(); }
            }
            if (p.RequestSaveAs) { p.RequestSaveAs = false; DoSaveYptAsDialog_N4(); }
            if (p.RequestOpenGameYpt != null)
            {
                var path = p.RequestOpenGameYpt;
                p.RequestOpenGameYpt = null;
                OpenGameYpt_N4(path, Path.GetFileNameWithoutExtension(path));
            }
            if (p.RequestPlayEffect != null)
            {
                var fx = p.RequestPlayEffect;
                p.RequestPlayEffect = null;
                PlayEffectByName_N4(fx);
            }
            if (p.RequestPlaceAtView)
            {
                p.RequestPlaceAtView = false;
                p.Sim.Origin = ParticleViewPoint_N4();
                p.Status = $"playing at ({p.Sim.Origin.X:0.##}, {p.Sim.Origin.Y:0.##}, {p.Sim.Origin.Z:0.##})";
            }
            if (p.RequestAddToYtyp) { p.RequestAddToYtyp = false; AddEffectToYtyp_N4(p); }
        }

        private void RenderParticles_N4(DeviceContext context)
        {
            var p = Ptfx;
            if (p == null || particleRenderer == null || p.Sim.Effect == null) return;
            if (!ParticlesBelongHere_U3()) { ParkParticles_U3(); return; }

            var now = particleClock.Elapsed.TotalSeconds;
            var pdt = lastParticleTick > 0 ? (float)(now - lastParticleTick) : 0.016f;
            lastParticleTick = now;

            p.Sim.Update(pdt);
            particleSprites.Clear();
            p.Sim.CollectSprites(particleSprites);
            FilterUnsupportedRules_S6(p, particleSprites);
            if (particleSprites.Count == 0) return;

            particleRenderer.Add(particleSprites, p.GetEmitterTexture, camera.Position, p.BlendOverride);
            particleRenderer.Flush(context, camera.ViewProjMatrix, camera.Position);
            NoteParticleDraw_U3(particleSprites.Count, true);
            TraceParticleDraw_R6(p, particleSprites);
        }

        private void WarmParticles_N4(float seconds)
        {
            var p = Ptfx;
            if (p?.Sim.Effect == null) return;
            for (float t = 0; t < seconds; t += 1.0f / 60.0f) p.Sim.Update(1.0f / 60.0f);
            lastParticleTick = particleClock.Elapsed.TotalSeconds;
        }

        private SDX.Vector3 ParticleViewPoint_N4()
        {
            int cx = Math.Max(1, deviceResources.Width) / 2;
            int cy = Math.Max(1, deviceResources.Height) / 2;
            var ray = camera.GetPickRay(cx, cy, deviceResources.Width, deviceResources.Height);
            float best = float.MaxValue;
            foreach (var f in scene.Files)
            {
                if (f?.Model?.Meshes == null || !f.Visible) continue;
                foreach (var mesh in f.Model.Meshes)
                {
                    if (!mesh.Visible) continue;
                    var b = mesh.WorldBounds;
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    if (!ray.Intersects(ref b, out float bt) || bt > best) continue;
                    if (mesh.RayHit(ref ray, out float t) && t < best) best = t;
                }
            }
            var dist = best < float.MaxValue ? best : 4.0f;
            return ray.Position + ray.Direction * dist;
        }

        private void LoadYptFile_N4(string path)
        {
            var p = Ptfx;
            if (p == null) return;
            try
            {
                var doc = PtfxDocument.FromFile(path);
                p.SetDocument(doc);
                p.Status = $"{doc.Name}: {doc.Effects.Count} effects";
                panel.SwitchWorkspace(LightPanel.Space.Particles);
            }
            catch (Exception ex) { p.Status = "could not load " + Path.GetFileName(path) + ": " + ex.Message; }
        }

        private bool OpenGameYpt_N4(string rpfPath, string name)
        {
            var p = Ptfx;
            if (p == null) return false;
            try
            {
                var doc = PtfxDocument.FromGame(gameFiles, name, rpfPath);
                if (doc == null) { p.Status = "could not read " + rpfPath; return false; }
                p.SetDocument(doc);
                return true;
            }
            catch (Exception ex) { p.Status = "could not read " + rpfPath + ": " + ex.Message; return false; }
        }

        private void OpenYptFromArchive_N4(RpfFileEntry e)
        {
            EnsureParticles_N4();
            if (e == null) return;
            if (OpenGameYpt_N4(e.Path, Path.GetFileNameWithoutExtension(e.Name)))
            {
                panel.SwitchWorkspace(LightPanel.Space.Particles);
                panel.RpfStatus = $"{e.Name} opened in the Particles workspace";
            }
            else panel.RpfStatus = Ptfx?.Status ?? "could not open " + e.Name;
        }

        private void PlayEffectByName_N4(string fx)
        {
            var p = Ptfx;
            if (p == null || string.IsNullOrWhiteSpace(fx)) return;
            if (p.PlayByName(fx)) { p.Status = "playing " + fx; return; }

            if (gameFiles == null || !gameFiles.Ready)
            {
                p.Status = "the game archives are not open yet";
                return;
            }
            ParticlePanel.EffectAsset.TryGetValue(fx, out var asset);
            var candidates = new List<string>();
            var list = p.GameYpts ?? PtfxGameIndex.ListYpts(gameFiles);
            if (asset != null)
                foreach (var entry in list)
                    if (string.Equals(entry.Name, asset, StringComparison.OrdinalIgnoreCase))
                        candidates.Add(entry.Path);
            candidates.Reverse();
            if (candidates.Count == 0)
                foreach (var entry in list)
                    if (fx.StartsWith(entry.Name, StringComparison.OrdinalIgnoreCase))
                        candidates.Add(entry.Path);

            foreach (var path in candidates)
            {
                try
                {
                    if (!OpenGameYpt_N4(path, asset ?? fx)) continue;
                    if (p.PlayByName(fx)) { p.Status = $"playing {fx} from {Path.GetFileName(path)}"; return; }
                }
                catch { }
            }
            p.Sim.SetEffect(null);
            p.Status = $"'{fx}' is not in this install's archives - nothing is playing";
        }

        private void RefreshYtypTargets_N4(ParticlePanel p)
        {
            if (!panel.ParticlesMode) return;
            p.YtypTargets.Clear();
            var ytyps = scene?.MloInfo?.Ytyps;
            if (ytyps != null)
                foreach (var y in ytyps)
                    if (!string.IsNullOrEmpty(y.Path) && File.Exists(y.Path)) p.YtypTargets.Add(y.Path);

            p.ArchetypeTargets.Clear();
            if (p.YtypTargets.Count == 0) return;
            p.YtypTarget = Math.Clamp(p.YtypTarget, 0, p.YtypTargets.Count - 1);
            var sel = p.YtypTargets[p.YtypTarget];
            var info = ytyps?.FirstOrDefault(y => string.Equals(y.Path, sel, StringComparison.OrdinalIgnoreCase));
            if (info == null) return;
            foreach (var a in info.Archetypes)
                p.ArchetypeTargets.Add(a?.Name ?? a?.Hash.ToString() ?? "?");
        }

        private void AddEffectToYtyp_N4(ParticlePanel p)
        {
            var fx = p.Sim.Effect?.Name;
            if (string.IsNullOrEmpty(fx)) { p.Status = "nothing is playing"; return; }
            if (p.YtypTargets.Count == 0) { p.Status = "no .ytyp is open"; return; }
            var path = p.YtypTargets[Math.Clamp(p.YtypTarget, 0, p.YtypTargets.Count - 1)];
            try
            {
                var ytyp = new YtypFile();
                ytyp.Load(File.ReadAllBytes(path));
                var archs = ytyp.AllArchetypes;
                if (archs == null || archs.Length == 0) { p.Status = Path.GetFileName(path) + " has no archetypes"; return; }
                int ai = Math.Clamp(p.ArchetypeTarget, 0, archs.Length - 1);
                var target = archs[ai];

                var local = p.Sim.Origin;
                foreach (var f in scene.Files)
                {
                    if (f?.Name == null || !f.HasPlacement) continue;
                    if (!string.Equals(Path.GetFileNameWithoutExtension(f.Name), target.Name,
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    var inv = f.Placement;
                    inv.Invert();
                    local = SDX.Vector3.TransformCoordinate(p.Sim.Origin, inv);
                    break;
                }

                var rot = p.Sim.Rotation;
                var ext = new MCExtensionDefParticleEffect
                {
                    _Data = new CExtensionDefParticleEffect
                    {
                        name = JenkHash.GenHash(fx.ToLowerInvariant()),
                        offsetPosition = local,
                        offsetRotation = new SDX.Vector4(rot.X, rot.Y, rot.Z, rot.W),
                        fxType = 0,
                        boneTag = 0,
                        scale = Math.Max(0.01f, p.Sim.EffectScale),
                        probability = 100,
                        flags = 0,
                        color = 0xFFFFFFFF,
                    },
                    fxName = fx,
                };

                var old = target.Extensions;
                var arr = old != null ? new MetaWrapper[old.Length + 1] : new MetaWrapper[1];
                if (old != null) Array.Copy(old, arr, old.Length);
                arr[arr.Length - 1] = ext;
                target.Extensions = arr;

                if (!File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
                File.WriteAllBytes(path, ytyp.Save());
                p.Status = $"'{fx}' added to {target.Name} in {Path.GetFileName(path)} at " +
                           $"({local.X:0.##}, {local.Y:0.##}, {local.Z:0.##})";
                Console.WriteLine($"PTFX added {fx} to {target.Name} in {path}");
            }
            catch (Exception ex) { p.Status = "could not write the ytyp: " + ex.Message; }
        }

        private void DoOpenYptDialog_N4()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Particle assets (*.ypt)|*.ypt|All files (*.*)|*.*",
                Title = "Open a particle asset",
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) LoadYptFile_N4(dlg.FileName);
        }

        private void DoSaveYptAsDialog_N4()
        {
            var p = Ptfx;
            var doc = p?.Doc;
            if (doc == null) return;
            using var dlg = new SaveFileDialog
            {
                Filter = "Particle assets (*.ypt)|*.ypt",
                FileName = doc.Name != null && doc.Name.EndsWith(".ypt", StringComparison.OrdinalIgnoreCase)
                    ? doc.Name : doc.Name + ".ypt",
                Title = "Save the particle asset as",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { doc.Save(dlg.FileName); p.Status = "saved " + dlg.FileName; Editor.UiSound.Success(); }
            catch (Exception ex) { p.Status = "save failed: " + ex.Message; Editor.UiSound.Error(); }
        }
    }
}

