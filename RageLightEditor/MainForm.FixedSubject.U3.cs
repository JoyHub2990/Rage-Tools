using System;
using System.Linq;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {

        internal static bool SubjectWorkspace_U3(LightPanel.Space sp) =>
            sp == LightPanel.Space.Terrain || sp == LightPanel.Space.Material;

        private readonly Vector3?[] subjectAnchoredOn_U3 =
            new Vector3?[Enum.GetValues(typeof(LightPanel.Space)).Length];

        internal int subjectAnchors_U3;

        internal bool SubjectCentre_U3(out Vector3 centre, out float radius)
        {
            centre = Vector3.Zero; radius = 0.0f;
            if (panel == null) return false;
            BoundingBox b;
            if (panel.TerrainMode)
            {
                if (!TerrainEd.HasMesh) return false;
                b = TerrainEd.Bounds;
            }
            else
            {
                var sb = CurrentScene?.GetSceneBounds();
                if (!sb.HasValue) return false;
                b = sb.Value;
            }
            if (!(b.Maximum.X > b.Minimum.X)) return false;
            centre = (b.Minimum + b.Maximum) * 0.5f;
            radius = Math.Max((b.Maximum - b.Minimum).Length() * 0.5f, 0.5f);
            return float.IsFinite(centre.X) && float.IsFinite(radius);
        }

        private void ApplySubjectCamera_U3()
        {
            if (panel == null || camera == null) return;
            bool subject = SubjectWorkspace_U3(panel.Workspace);
            camera.OrbitSubject_U3 = subject;
            camera.RotateInPlace = !subject;

            if (!subject) return;
            int slot = (int)panel.Workspace;
            if (slot < 0 || slot >= subjectAnchoredOn_U3.Length) return;

            if (!SubjectCentre_U3(out var centre, out _)) { subjectAnchoredOn_U3[slot] = null; return; }
            var was = subjectAnchoredOn_U3[slot];
            if (was.HasValue && (centre - was.Value).Length() <= 0.05f) return;
            subjectAnchoredOn_U3[slot] = centre;
            if (!camera.AnchorOnSubject_U3(centre)) return;
            subjectAnchors_U3++;
            Console.WriteLine($"U3ORBIT {panel.Workspace}: pivot put on the subject at " +
                              $"({centre.X:0.##},{centre.Y:0.##},{centre.Z:0.##}), orbit radius {camera.Distance:0.##} m " +
                              $"(the view did not move - eye {camera.Position})");
        }

        private LightPanel.Space? particleGuest_U3;

        internal void RequestParticles_U3(LightPanel.Space sp) => particleGuest_U3 = sp;

        internal void ReleaseParticles_U3() => particleGuest_U3 = null;

        internal bool ParticlesBelongHere_U3() =>
            panel != null && ParticlesBelongIn_U3(panel.Workspace);

        internal bool ParticlesBelongIn_U3(LightPanel.Space sp) =>
            sp == LightPanel.Space.Particles ||
            (particleGuest_U3.HasValue && particleGuest_U3.Value == sp);

        private void ParkParticles_U3()
        {
            NoteParticleDraw_U3(0, false);
            lastParticleTick = particleClock.Elapsed.TotalSeconds;
        }
    }
}

