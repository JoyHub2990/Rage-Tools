using System;
using System.Collections.Generic;
using SharpDX;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static readonly bool mirrorExtrasOff_U4 = Environment.GetEnvironmentVariable("RLE_NOMIRROREXTRA") == "1";
        private readonly List<Plane> mirrorCullPlanes_U4 = new List<Plane>();
        private readonly List<Matrix> mirrorCullViews_U4 = new List<Matrix>();
        private readonly List<RenderModel> mirrorModels_U4 = new List<RenderModel>();
        private double lastMirrorExtraLog_U4;

        private void UpdateMirrorCull_U4()
        {
            mirrorCullPlanes_U4.Clear();
            mirrorCullViews_U4.Clear();
            if (!mirrorExtrasOff_U4 && sceneRenderer != null && !mirrorsDisabledByEnv)
                for (int i = 0; i < sceneRenderer.MirrorCullCount_U4; i++)
                {
                    mirrorCullPlanes_U4.Add(sceneRenderer.LastMirrorPlane(i));
                    mirrorCullViews_U4.Add(sceneRenderer.MirrorCullViewProj_U4(i));
                }
            worldRender?.SetMirrorCull_U4(mirrorCullPlanes_U4, mirrorCullViews_U4);
        }

        private IList<RenderModel> WithMirrorExtras_U4(IList<RenderModel> models)
        {
            if (mirrorExtrasOff_U4 || !(panel?.WorldMode ?? false)) return models;
            var extras = worldRender?.MirrorExtras_U4;
            if (extras == null || extras.Meshes.Count == 0) return models;
            mirrorModels_U4.Clear();
            mirrorModels_U4.AddRange(models);
            mirrorModels_U4.Add(extras);
            return mirrorModels_U4;
        }

        private static readonly bool mirrorDrawsProbe_U4 = Environment.GetEnvironmentVariable("RLE_MIRRORDRAWS") == "1";
        private void ProbeMirrorDraws_U4()
        {
            if (!mirrorDrawsProbe_U4 || screenshotPath == null) return;
            if (sceneRenderer == null || worldRender == null || sceneRenderer.MirrorCullCount_U4 == 0) return;
            var plane = sceneRenderer.LastMirrorPlane(0);
            var n = plane.Normal; float pd = plane.D;
            var e = camera.Position;
            float dEye = Vector3.Dot(n, e) + pd;
            var reflEye = e - n * (2.0f * dEye);
            var fr = new BoundingFrustum(sceneRenderer.MirrorCullViewProj_U4(0));
            var rows = new List<(float ang, string s)>();
            void Walk(IList<Rendering.RenderMesh> list, string tag)
            {
                foreach (var m in list)
                {
                    if (m == null || !m.Visible || m.NeverDraw || m.IsMirror) continue;
                    if (Vector3.Dot(n, m.WorldSphere.Center) + pd < -m.WorldSphere.Radius) continue;
                    if (!fr.Intersects(ref m.WorldSphere)) continue;
                    float d = Math.Max(Vector3.Distance(m.WorldSphere.Center, reflEye), 0.01f);
                    var owner = worldRender.OwnerOf(m);
                    rows.Add((m.WorldSphere.Radius / d,
                        $"    {m.WorldSphere.Radius / d:0.000} {tag} {owner?.Archetype?.Name ?? "?"} [{m.ShaderName}] r {m.WorldSphere.Radius:0.00} d {d:0.0} planeD {Vector3.Dot(n, m.WorldSphere.Center) + pd:0.00} alpha {m.AlphaMode}"));
                }
            }
            Walk(worldRender.Model.Meshes, "cam");
            Walk(worldRender.MirrorExtras_U4.Meshes, "EXT");
            if (rows.Count == 0) return;
            rows.Sort((a, b) => b.ang.CompareTo(a.ang));
            Console.WriteLine($"MIRRORDRAWS eye {e.X:0.0},{e.Y:0.0},{e.Z:0.0} reflEye {reflEye.X:0.0},{reflEye.Y:0.0},{reflEye.Z:0.0} " +
                              $"plane n {n.X:0.00},{n.Y:0.00},{n.Z:0.00} d {pd:0.00} eyeDist {dEye:0.00} - {rows.Count} meshes in the mirror frustum:");
            for (int i = 0; i < Math.Min(rows.Count, 24); i++) Console.WriteLine(rows[i].s);
        }

        private void LogMirrorExtras_U4()
        {
            if (screenshotPath == null || worldRender == null) return;
            double now = clock.Elapsed.TotalSeconds;
            if (now - lastMirrorExtraLog_U4 < 1.0) return;
            lastMirrorExtraLog_U4 = now;
            ProbeMirrorDraws_U4();
            Console.WriteLine($"MIRROREXTRA frusta {mirrorCullViews_U4.Count} tested {worldRender.MirrorExtrasTested} " +
                              $"kept {worldRender.MirrorExtrasKept} (of {worldRender.MeshesDrawn} in the camera's list)" +
                              (mirrorExtrasOff_U4 ? " [RLE_NOMIRROREXTRA=1: off]" : ""));
        }
    }
}

