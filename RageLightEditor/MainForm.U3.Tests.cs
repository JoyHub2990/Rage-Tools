using System;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int u3Checks;

        partial void SeqTest_U3(Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail) { u3Checks++; check(name, ok, detail); }

            SubjectCameraTest_U3(Check);
            ParticleOwnershipTest_U3(Check);

            Console.WriteLine($"U3TESTS {u3Checks} checks ran " +
                              "(the subject stands still while the camera orbits it; particles belong to Particles)");
        }

        private void SubjectCameraTest_U3(Action<string, bool, string> check)
        {
            check("u3: Terrain and Materials are the subject workspaces...",
                  SubjectWorkspace_U3(LightPanel.Space.Terrain) && SubjectWorkspace_U3(LightPanel.Space.Material),
                  "both");
            var others = ((LightPanel.Space[])Enum.GetValues(typeof(LightPanel.Space)))
                .Where(s => s != LightPanel.Space.Terrain && s != LightPanel.Space.Material).ToArray();
            check("u3: ...and no other section turns into one",
                  others.All(s => !SubjectWorkspace_U3(s)),
                  string.Join(",", others.Where(SubjectWorkspace_U3).Select(s => s.ToString())) is var bad && bad.Length == 0
                      ? $"{others.Length} left alone" : bad);

            var centre = new Vector3(120.0f, -40.0f, 15.0f);
            const float radius = 64.0f;

            var world = new Camera { OrbitAnchorMax = 1.5f };
            world.SetAspect(16.0f / 9.0f);
            world.FrameBounds(centre, radius);
            world.Update(0.0f);
            check("u3: the World still leaves the pivot a metre in front of the eye (CodeWalker's feel)",
                  Math.Abs(world.Distance - 1.5f) < 0.001f, $"{world.Distance:0.###} m");

            var view = new Camera { OrbitAnchorMax = 1.5f, OrbitSubject_U3 = true, RotateInPlace = false };
            view.SetAspect(16.0f / 9.0f);
            view.FrameBounds(centre, radius);
            view.Update(0.0f);
            check("u3: a subject workspace frames with the pivot ON the subject",
                  (view.Target - centre).Length() < 0.001f && view.Distance > radius,
                  $"pivot {(view.Target - centre).Length():0.####} m off centre, orbit radius {view.Distance:0.#} m");

            Vector2 Screen(Camera c, Vector3 p)
            {
                var v = Vector3.TransformCoordinate(p, c.ViewProjMatrix);
                return new Vector2((v.X * 0.5f + 0.5f) * 1600.0f, (0.5f - v.Y * 0.5f) * 900.0f);
            }
            var subjectXform = Matrix.Translation(centre);
            var before = Screen(view, centre);
            var eyeBefore = view.Position;
            view.Orbit(120.0f, 40.0f);
            view.Update(1.0f / 60.0f);
            var after = Screen(view, centre);
            float onScreen = (after - before).Length();
            float eyeMoved = (view.Position - eyeBefore).Length();

            check("u3: the subject's world transform does not change when the camera moves",
                  (Matrix.Translation(centre) - subjectXform).ToArray().All(f => Math.Abs(f) < 1e-6f) &&
                  (view.Target - centre).Length() < 0.001f,
                  $"pivot still {(view.Target - centre).Length():0.####} m off the subject");
            check("u3: ...and it does not move on screen either - the camera goes around it",
                  onScreen < 2.0f, $"{onScreen:0.##} px (was ~296 px, off the frame, before this)");
            check("u3: ...while the camera itself really did move",
                  eyeMoved > radius * 0.25f, $"the eye travelled {eyeMoved:0.#} m around it");
            check("u3: ...at a constant range, which is what orbiting means",
                  Math.Abs((view.Position - centre).Length() - (eyeBefore - centre).Length()) < 0.01f,
                  $"{(eyeBefore - centre).Length():0.###} -> {(view.Position - centre).Length():0.###} m");

            var eyeW = world.Position;
            var beforeW = Screen(world, centre);
            world.Orbit(120.0f, 40.0f);
            world.Update(1.0f / 60.0f);
            check("u3: the World's head-turn is untouched (the eye holds still, the view sweeps)",
                  (world.Position - eyeW).Length() < 0.01f && (Screen(world, centre) - beforeW).Length() > 50.0f,
                  $"eye moved {(world.Position - eyeW).Length():0.###} m, the view swept {(Screen(world, centre) - beforeW).Length():0.#} px");

            var stale = new Camera { OrbitAnchorMax = 1.5f, OrbitSubject_U3 = true, RotateInPlace = false };
            stale.SetAspect(16.0f / 9.0f);
            stale.FrameBounds(centre, radius);
            stale.ReanchorPivot(1.5f);
            var eyeS = stale.Position; var fwdS = stale.GetForward();
            bool first = stale.AnchorOnSubject_U3(centre);
            check("u3: a remembered 1.5 m pivot is put back on the subject on the way in",
                  first && Math.Abs(stale.Distance - (centre - eyeS).Length()) < 1.0f,
                  $"orbit radius {stale.Distance:0.#} m");
            check("u3: ...without moving the picture by a pixel",
                  (stale.Position - eyeS).Length() < 0.001f && (stale.GetForward() - fwdS).Length() < 0.001f,
                  $"eye moved {(stale.Position - eyeS).Length():0.#####} m");
            int again = 0;
            for (int i = 0; i < 120; i++) if (stale.AnchorOnSubject_U3(centre)) again++;
            check("u3: ...and calling it every frame afterwards is a no-op, not a pivot rewritten each frame",
                  again == 0, $"{again} of 120 further calls moved it");

            ApplySubjectCamera_U3();
            bool wantSubject = SubjectWorkspace_U3(panel.Workspace);
            check("u3: the shared camera's mode is set from the section it is in, every frame",
                  camera.OrbitSubject_U3 == wantSubject && camera.RotateInPlace == !wantSubject,
                  $"in {panel.Workspace}: orbitSubject={camera.OrbitSubject_U3} rotateInPlace={camera.RotateInPlace}");

            var mv = new ModelViewer();
            check("u3: the detached model viewer keeps a camera of its own",
                  !ReferenceEquals(mv.Cam, camera) && (ModelView == null || !ReferenceEquals(ModelView.Cam, camera)),
                  "its own instance, so a section's mode cannot reach it");
            mv.Cam.Smoothness = 0.0f;
            mv.Cam.RotateInPlace = false;
            mv.Cam.Target = centre; mv.Cam.Distance = 40.0f;
            mv.Cam.SetAspect(16.0f / 9.0f);
            mv.Cam.Update(0.0f);
            var mvBefore = Screen(mv.Cam, centre);
            var mvEye = mv.Cam.Position;
            mv.Cam.Orbit(120.0f, 40.0f);
            mv.Cam.Update(1.0f / 60.0f);
            check("u3: ...and its subject holds still too while it circles",
                  (Screen(mv.Cam, centre) - mvBefore).Length() < 2.0f && (mv.Cam.Position - mvEye).Length() > 5.0f,
                  $"{(Screen(mv.Cam, centre) - mvBefore).Length():0.##} px while the eye went " +
                  $"{(mv.Cam.Position - mvEye).Length():0.#} m around it");
        }

        private void ParticleOwnershipTest_U3(Action<string, bool, string> check)
        {
            var spaces = (LightPanel.Space[])Enum.GetValues(typeof(LightPanel.Space));
            ReleaseParticles_U3();
            var wrong = spaces.Where(s => ParticlesBelongIn_U3(s) != (s == LightPanel.Space.Particles)).ToArray();
            check("u3: no particles are drawn outside the Particles workspace",
                  wrong.Length == 0,
                  wrong.Length == 0 ? $"{spaces.Length} sections checked, only Particles owns them"
                                    : string.Join(",", wrong.Select(s => s.ToString())));

            RequestParticles_U3(LightPanel.Space.World);
            check("u3: ...unless a section asks for them by name",
                  ParticlesBelongIn_U3(LightPanel.Space.World) && !ParticlesBelongIn_U3(LightPanel.Space.Terrain),
                  "the World asked and got them; Terrain still does not");
            ReleaseParticles_U3();
            check("u3: ...and giving them back takes them away again",
                  !ParticlesBelongIn_U3(LightPanel.Space.World), "released");

            int was = particlesDrawnOutside_U3;
            var wasSpace = panel?.Workspace ?? LightPanel.Space.Light;
            RequestParticles_U3(wasSpace);
            NoteParticleDraw_U3(17, false);
            bool quietWhenOwned = particlesDrawnOutside_U3 == was;
            ReleaseParticles_U3();
            NoteParticleDraw_U3(17, false);
            bool countedWhenNot = particlesDrawnOutside_U3 == was + (wasSpace == LightPanel.Space.Particles ? 0 : 17);
            particlesDrawnOutside_U3 = was;
            check("u3: a sprite drawn by a section that owns the system is not counted against it",
                  quietWhenOwned, $"{particlesDrawnOutside_U3} outside-draws");
            check("u3: ...and one drawn by a section that does not IS counted (the capture's yardstick works)",
                  countedWhenNot, $"in {wasSpace}");

            lastParticleTick = particleClock.Elapsed.TotalSeconds - 45.0;
            ParkParticles_U3();
            double dt = particleClock.Elapsed.TotalSeconds - lastParticleTick;
            check("u3: leaving parks the sim clock, so returning is not a 45 s burst",
                  dt < 0.25, $"the next step would be {dt:0.###} s");
        }
    }
}

