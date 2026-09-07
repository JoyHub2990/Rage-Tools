using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int probeTick_U3;
        private bool probeDone_U3;
        private Matrix probeXf_U3;
        private Vector3 probeCentre_U3, probeScreen_U3, probeCamPos_U3;
        private bool probeArmed_U3;
        private int probeArmedAt_U3;

        private bool SubjectProbe_U3(out string name, out Matrix xf, out Vector3 centre)
        {
            name = null; xf = Matrix.Identity; centre = Vector3.Zero;
            RenderModel model = null;
            if (panel != null && panel.TerrainMode && terrainModel_R4 != null && terrainModel_R4.Meshes.Count > 0)
                model = terrainModel_R4;
            else
                model = CurrentScene?.Models?.FirstOrDefault(m => m != null && m.Meshes.Count > 0);
            if (model == null) return false;
            var mesh = model.Meshes.FirstOrDefault(m => m != null);
            if (mesh == null) return false;
            name = model.Name ?? "(model)";
            xf = mesh.Transform;
            var b = mesh.WorldBounds;
            centre = (b.Minimum + b.Maximum) * 0.5f;
            return true;
        }

        private Vector3 ScreenOf_U3(Vector3 world)
        {
            var c = Vector3.TransformCoordinate(world, camera.ViewProjMatrix);
            float w = Math.Max(deviceResources?.Width ?? 1, 1);
            float h = Math.Max(deviceResources?.Height ?? 1, 1);
            return new Vector3((c.X * 0.5f + 0.5f) * w, (0.5f - c.Y * 0.5f) * h, c.Z);
        }

        private static Vector3 Vec_U3(string s, Vector3 def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            var p = s.Split(',');
            if (p.Length < 3) return def;
            float N(int i) => float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            return new Vector3(N(0), N(1), N(2));
        }

        partial void OnWorldTick_U3()
        {
            ParticleCount_U3();
            if (probeDone_U3 || panel == null || camera == null) return;
            var spec = Environment.GetEnvironmentVariable("RLE_U3PROBE");
            if (string.IsNullOrWhiteSpace(spec)) return;
            if (!int.TryParse(spec, out int at) || at < 1) at = 30;
            probeTick_U3++;
            screenshotFrames = Math.Max(screenshotFrames, 3);

            if (!probeArmed_U3)
            {
                if (probeTick_U3 < at) return;
                if (!SubjectProbe_U3(out var n0, out probeXf_U3, out probeCentre_U3))
                {
                    if (probeTick_U3 < at + 600) return;
                    Console.WriteLine("U3PROBE no subject in " + panel.Workspace);
                    probeDone_U3 = true;
                    return;
                }
                probeScreen_U3 = ScreenOf_U3(probeCentre_U3);
                probeCamPos_U3 = camera.Position;
                Console.WriteLine($"U3PROBE before space={panel.Workspace} subj={n0} cam={probeCamPos_U3} " +
                                  $"tgt={camera.Target} dist={camera.Distance:0.###} " +
                                  $"xfT=({probeXf_U3.M41:0.####},{probeXf_U3.M42:0.####},{probeXf_U3.M43:0.####}) " +
                                  $"centre={probeCentre_U3} screen=({probeScreen_U3.X:0.#},{probeScreen_U3.Y:0.#})");

                var step = Vec_U3(Environment.GetEnvironmentVariable("RLE_U3PROBEMOVE"), new Vector3(4, 3, 2));
                camera.Translate(step);
                camera.Orbit(120, 40);
                camera.Update(1.0f / 60.0f);
                probeArmed_U3 = true;
                probeArmedAt_U3 = probeTick_U3;
                return;
            }

            if (probeTick_U3 < probeArmedAt_U3 + 5) return;
            probeDone_U3 = true;
            if (!SubjectProbe_U3(out var n1, out var xf1, out var c1))
            {
                Console.WriteLine("U3PROBE the subject vanished when the camera moved");
                return;
            }
            var s1 = ScreenOf_U3(c1);
            float dWorld = (c1 - probeCentre_U3).Length();
            float dXf = new Vector3(xf1.M41 - probeXf_U3.M41, xf1.M42 - probeXf_U3.M42, xf1.M43 - probeXf_U3.M43).Length();
            float dScreen = new Vector2(s1.X - probeScreen_U3.X, s1.Y - probeScreen_U3.Y).Length();
            float dCam = (camera.Position - probeCamPos_U3).Length();
            Console.WriteLine($"U3PROBE after  space={panel.Workspace} subj={n1} cam={camera.Position} " +
                              $"tgt={camera.Target} dist={camera.Distance:0.###} " +
                              $"xfT=({xf1.M41:0.####},{xf1.M42:0.####},{xf1.M43:0.####}) " +
                              $"centre={c1} screen=({s1.X:0.#},{s1.Y:0.#})");
            bool worldFixed = dWorld < 0.001f && dXf < 0.001f;
            bool stayedFramed = dScreen < Math.Max(24.0f, dCam);
            Console.WriteLine($"U3PROBE delta  cam={dCam:0.###} m   subjectWorld={dWorld:0.####} m   " +
                              $"subjectTransform={dXf:0.####} m   onScreen={dScreen:0.#} px   " +
                              $"=> {(worldFixed ? "stands still in the world" : "IS BEING MOVED")}, " +
                              $"{(stayedFramed ? "and the camera went around it" : "BUT THE VIEW SWUNG IT AWAY")}");
        }

        private int partCountTick_U3;
        private LightPanel.Space partLastSpace_U3 = (LightPanel.Space)(-1);
        private int partHold_U3;

        private void ParticleCount_U3()
        {
            if (Environment.GetEnvironmentVariable("RLE_U3PARTCOUNT") != "1") return;
            var sp = panel?.Workspace ?? LightPanel.Space.Light;
            bool changed = sp != partLastSpace_U3;
            if (changed) { partLastSpace_U3 = sp; partHold_U3 = 90; }
            if (partHold_U3 > 0) { partHold_U3--; screenshotFrames = Math.Max(screenshotFrames, 4); }
            if (!changed && (partCountTick_U3++ % 15) != 0) return;
            var p = Ptfx;
            Console.WriteLine($"U3PART space={sp} effect={(p?.Sim.Effect == null ? "(none)" : "loaded")} " +
                              $"alive={p?.Sim.AliveCount ?? 0} spritesDrawn={particlesDrawn_U3} (in {particlesDrawnIn_U3}) " +
                              $"simTicks={particleSimTicks_U3} drawnOutsideParticles={particlesDrawnOutside_U3}");
        }

        private int particlesDrawn_U3;
        private int particleSimTicks_U3;
        private LightPanel.Space particlesDrawnIn_U3 = (LightPanel.Space)(-1);

        internal int particlesDrawnOutside_U3;

        private void NoteParticleDraw_U3(int sprites, bool ticked)
        {
            particlesDrawn_U3 = sprites;
            if (ticked) particleSimTicks_U3++;
            if (sprites <= 0 || panel == null) return;
            particlesDrawnIn_U3 = panel.Workspace;
            if (!ParticlesBelongHere_U3()) particlesDrawnOutside_U3 += sprites;
        }
    }
}

