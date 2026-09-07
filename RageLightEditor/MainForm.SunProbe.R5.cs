using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SunProbe_R5(DeviceContext context);

        private static readonly int SunProbeMode_R5 = ReadSunProbeMode_R5();
        private bool sunProbeDone_R5;

        private static int ReadSunProbeMode_R5()
        {
            var s = Environment.GetEnvironmentVariable("RLE_SUNDBG");
            return int.TryParse(s, out int v) ? v : 0;
        }

        private struct SunSample_R5
        {
            public Vector3 Pos, Norm;
            public float Ndl, MapLit, RoomScale, ShaderSun, TruthLit, TruthSun, ViewDist, MapDist, PDist;
            public int Cascade;
            public string Mesh;
            public string Blocker;
        }

        partial void SunProbe_R5(DeviceContext context)
        {
            if (SunProbeMode_R5 <= 0 || sunProbeDone_R5) return;
            sunProbeDone_R5 = true;
            try { RunSunProbe_R5(context); }
            catch (Exception ex) { Console.WriteLine("SUNDBG failed: " + ex.Message); }
        }

        private void RunSunProbe_R5(DeviceContext context)
        {
            if (!sceneRenderer.GlobalLight.HasValue) { Console.WriteLine("SUNDBG no global light this frame"); return; }
            var sunDir = Vector3.Normalize(sceneRenderer.GlobalLight.Value.LightDir);

            bool world = panel.WorldMode;
            var casters = new List<RenderMesh>();
            if (world)
            {
                if (worldRender == null) { Console.WriteLine("SUNDBG no world"); return; }
                casters.AddRange(worldRender.SunCasters());
            }
            else if (scene != null)
            {
                foreach (var m in scene.AllMeshes) casters.Add(m);
            }
            if (casters.Count == 0) { Console.WriteLine("SUNDBG nothing loaded"); return; }

            var cs = shadowRenderer.Cascades;
            bool cascaded = cs.Count > 0 && (world ? cascadesValid : sunCascaded_R5);
            int count = cascaded ? cs.Count : 1;
            var mats = new Matrix[SunCascades.MaxCascades];
            var texels = new float[SunCascades.MaxCascades];
            var splits = new float[SunCascades.MaxCascades];
            Vector3 sunPos;
            if (cascaded)
            {
                for (int i = 0; i < SunCascades.MaxCascades; i++)
                {
                    mats[i] = cs.ViewProj[Math.Min(i, cs.Count - 1)];
                    texels[i] = cs.TexelWorld[Math.Min(i, cs.Count - 1)];
                    splits[i] = cs.SplitFar[Math.Min(i, cs.Count - 1)];
                }
                sunPos = cs.SunPos;
            }
            else
            {
                for (int i = 0; i < SunCascades.MaxCascades; i++)
                {
                    mats[i] = shadowRenderer.SunMatrix;
                    texels[i] = shadowRenderer.SunTexelWorld;
                    splits[i] = 1e9f;
                }
                sunPos = shadowRenderer.SunPos;
            }
            float strength = MathUtil.Clamp(panel.SunShadowStrength, 0.0f, 1.0f);
            bool sunMapOn = panel.SunShadows && panel.ShowShadows && (world ? cascadesValid : sunMapValid);

            var slices = shadowRenderer.ReadSunMap_R5(context);

            var pm = camera.ProjMatrix;
            float tanH = 1.0f / Math.Max(Math.Abs(pm.M11), 1e-4f);
            float tanV = 1.0f / Math.Max(Math.Abs(pm.M22), 1e-4f);
            var fwd = camera.GetForward();
            var right = camera.GetRight();
            var up = Vector3.Cross(right, fwd);
            if (Vector3.Dot(up, Vector3.UnitZ) < 0) up = -up;
            up = Vector3.Normalize(up);
            var eye = camera.Position;

            const int Grid = 9;
            var samples = new List<SunSample_R5>(Grid * Grid);
            int misses = 0;
            for (int gy = 0; gy < Grid; gy++)
                for (int gx = 0; gx < Grid; gx++)
                {
                    float sx = (gx / (Grid - 1.0f) * 2.0f - 1.0f) * 0.75f;
                    float sy = (gy / (Grid - 1.0f) * 2.0f - 1.0f) * 0.6f;
                    var dir = Vector3.Normalize(fwd + right * (sx * tanH) - up * (sy * tanV));
                    var ray = new Ray(eye, dir);
                    if (!NearestHit_R5(casters, ray, 250.0f, out var mesh, out float dist, out var nrm)) { misses++; continue; }
                    var pos = eye + dir * dist;
                    if (Vector3.Dot(nrm, dir) > 0) nrm = -nrm;

                    var s = new SunSample_R5
                    {
                        Pos = pos, Norm = nrm, ViewDist = dist,
                        Mesh = mesh.Shader?.Name.ToString() ?? "?",
                        RoomScale = mesh.SunScale,
                        Ndl = MathUtil.Clamp(Vector3.Dot(nrm, sunDir), 0.0f, 1.0f),
                    };
                    s.MapLit = sunMapOn ? SunShadowFactorCpu_R5(slices, mats, texels, splits, count, sunPos, pos, nrm, dist, strength)
                                        : 1.0f;
                    s.PDist = (sunPos - pos).Length();
                    s.MapDist = MapDistAt_R5(slices, mats, splits, count, pos, dist);
                    s.Cascade = CascadeOf_R5(splits, count, dist);
                    s.ShaderSun = s.Ndl * s.MapLit * s.RoomScale;
                    var lifted = pos + nrm * 0.05f;
                    s.TruthLit = NearestHit_R5(casters, new Ray(lifted, sunDir), 200.0f, out var blocker, out _, out _) ? 0.0f : 1.0f;
                    s.Blocker = blocker == null ? "-" : (blocker.Shader?.Name.ToString() ?? "?");
                    s.TruthSun = s.Ndl * s.TruthLit;
                    samples.Add(s);
                }

            int n = samples.Count;
            if (n == 0)
            {
                var bmin = new Vector3(float.MaxValue); var bmax = new Vector3(float.MinValue);
                foreach (var m in casters) { bmin = Vector3.Min(bmin, m.WorldSphere.Center); bmax = Vector3.Max(bmax, m.WorldSphere.Center); }
                var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
                foreach (var m in casters) { xs.Add(m.WorldSphere.Center.X); ys.Add(m.WorldSphere.Center.Y); zs.Add(m.WorldSphere.Center.Z); }
                xs.Sort(); ys.Sort(); zs.Sort();
                var shellC = new Vector3(xs[xs.Count / 2], ys[ys.Count / 2], zs[zs.Count / 2]);
                int shellN = casters.Count;
                Console.WriteLine($"SUNDBG every ray missed - {casters.Count} meshes, centres span {bmin} .. {bmax}, " +
                                  $"shell centroid {(shellN > 0 ? shellC.ToString() : "none")} ({shellN} shell meshes), camera {eye} fwd {fwd}");
                return;
            }
            float shaderMean = 0, truthMean = 0, excess = 0;
            int shaderLit = 0, truthLit = 0, leaks = 0, roomDark = 0;
            int mapMiss = 0, floored = 0;
            foreach (var s in samples)
            {
                shaderMean += s.ShaderSun; truthMean += s.TruthSun;
                if (s.ShaderSun > 0.05f) shaderLit++;
                if (s.TruthSun > 0.05f) truthLit++;
                if (s.RoomScale < 0.5f) roomDark++;
                if (s.ShaderSun > 0.05f && s.TruthSun <= 0.001f)
                {
                    leaks++; excess += s.ShaderSun;
                    if (s.MapLit > 0.5f) mapMiss++; else floored++;
                }
            }
            shaderMean /= n; truthMean /= n;

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("SUNDBG ").Append(world ? "WORLD" : panel.MaterialMode ? "MATERIAL" : "LIGHT")
              .Append(" hour ").Append(panel.PreviewHour.ToString("0.0", ci))
              .Append(" cam ").Append(eye.X.ToString("0.0", ci)).Append(',').Append(eye.Y.ToString("0.0", ci)).Append(',').Append(eye.Z.ToString("0.0", ci))
              .Append(" sun ").Append(sunDir.X.ToString("0.00", ci)).Append(',').Append(sunDir.Y.ToString("0.00", ci)).Append(',').Append(sunDir.Z.ToString("0.00", ci))
              .Append(" | samples ").Append(n).Append(" (").Append(misses).Append(" missed)")
              .Append(" | SHADER lit ").Append((100.0f * shaderLit / n).ToString("0.0", ci)).Append("% mean ").Append(shaderMean.ToString("0.0000", ci))
              .Append(" | TRUTH lit ").Append((100.0f * truthLit / n).ToString("0.0", ci)).Append("% mean ").Append(truthMean.ToString("0.0000", ci))
              .Append(" | LEAK ").Append(leaks).Append('/').Append(n).Append(" (").Append((100.0f * leaks / n).ToString("0.0", ci)).Append("%) mean excess ")
              .Append((leaks > 0 ? excess / leaks : 0.0f).ToString("0.0000", ci))
              .Append(" [map-miss ").Append(mapMiss).Append(", strength-floor ").Append(floored).Append(']');
            Console.WriteLine(sb.ToString());
            Console.WriteLine($"  SUNDBG setup: casters {casters.Count}, cascades {(cascaded ? count : 0)} " +
                              $"texel0 {texels[0].ToString("0.000", ci)} splits ({string.Join(",", Array.ConvertAll(splits, x => x.ToString("0", ci)))}) " +
                              $"sunMap {(sunMapOn ? "on" : "OFF")} strength {strength.ToString("0.00", ci)} casterDraws {shadowRenderer.LastCascadeDraws} skipped-small {shadowRenderer.CascadeMeshesSkipped} " +
                              $"roomScale<0.5 on {roomDark}/{n} samples, interior cull {(interiorCull?.ToString() ?? "none")}");

            if (SunProbeMode_R5 < 2) return;
            samples.Sort((a, b) => (b.ShaderSun - b.TruthSun).CompareTo(a.ShaderSun - a.TruthSun));
            int shown = 0;
            foreach (var s in samples)
            {
                if (s.ShaderSun - s.TruthSun < 0.02f) break;
                if (shown++ >= 12) break;
                Console.WriteLine($"    leak at {s.Pos.X.ToString("0.0", ci)},{s.Pos.Y.ToString("0.0", ci)},{s.Pos.Z.ToString("0.0", ci)} " +
                                  $"n=({s.Norm.X.ToString("0.00", ci)},{s.Norm.Y.ToString("0.00", ci)},{s.Norm.Z.ToString("0.00", ci)}) " +
                                  $"ndl {s.Ndl.ToString("0.000", ci)} mapLit {s.MapLit.ToString("0.000", ci)} room {s.RoomScale.ToString("0.00", ci)} " +
                                  $"shader {s.ShaderSun.ToString("0.000", ci)} truth {s.TruthSun.ToString("0.000", ci)} " +
                                  $"cascade {s.Cascade} dist {s.ViewDist.ToString("0.0", ci)} map {s.MapDist.ToString("0.0", ci)} vs {s.PDist.ToString("0.0", ci)} on '{s.Mesh}' blocked-by '{s.Blocker}'");
            }
        }

        private static float MapDistAt_R5(float[][] slices, Matrix[] mats, float[] splits, int count, Vector3 worldPos, float viewDist)
        {
            if (slices == null) return -1.0f;
            int ci = CascadeOf_R5(splits, count, viewDist);
            var sp = Vector4.Transform(new Vector4(worldPos, 1.0f), mats[ci]);
            if (sp.W <= 0.0001f) return -2.0f;
            float u = sp.X / sp.W * 0.5f + 0.5f;
            float v = sp.Y / sp.W * -0.5f + 0.5f;
            if (u < 0 || u > 1 || v < 0 || v > 1) return -3.0f;
            return ShadowRenderer.SampleSunMap_R5(slices[ci], u, v);
        }

        private static int CascadeOf_R5(float[] splits, int count, float viewDist)
        {
            int ci = 0;
            for (int k = 0; k < 3; k++) if (k + 1 < count && viewDist > splits[k]) ci = k + 1;
            return ci;
        }

        private static float SunShadowFactorCpu_R5(float[][] slices, Matrix[] mats, float[] texels, float[] splits,
            int count, Vector3 sunPos, Vector3 worldPos, Vector3 norm, float viewDist, float strength)
        {
            if (slices == null) return 1.0f;
            if (viewDist > splits[count - 1]) return 1.0f;
            int ci = CascadeOf_R5(splits, count, viewDist);
            var sp = Vector4.Transform(new Vector4(worldPos, 1.0f), mats[ci]);
            if (sp.W <= 0.0001f) return 1.0f;
            float u = sp.X / sp.W * 0.5f + 0.5f;
            float v = sp.Y / sp.W * -0.5f + 0.5f;
            if (u < 0 || u > 1 || v < 0 || v > 1) return 1.0f;

            float pdist = (sunPos - worldPos).Length();
            float texelWorld = Math.Max(texels[ci], 0.01f);
            float ndl = MathUtil.Clamp(Vector3.Dot(norm, Vector3.Normalize(sunPos - worldPos)), 0.0f, 1.0f);
            bool legacy = SceneRenderer.LegacySunShadow_R5;
            float bias = Math.Max(legacy ? 0.15f : 0.02f, texelWorld * 1.5f)
                       + (1.0f - ndl) * Math.Max(legacy ? 0.9f : 0.08f, texelWorld * 4.0f);

            const float texel = 2.5f / ShadowRenderer.SunSize;
            float lit = 0.0f; int taps = 0;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    float sd = ShadowRenderer.SampleSunMap_R5(slices[ci], u + ox * texel, v + oy * texel);
                    lit += (pdist > sd + bias) ? 0.0f : 1.0f;
                    taps++;
                }
            lit /= taps;
            lit = lit * lit * (3.0f - 2.0f * lit);
            if (legacy) return 1.0f + (lit - 1.0f) * strength;
            if (strength <= 0.001f) return 1.0f;
            return (float)Math.Pow(MathUtil.Clamp(lit, 0.0f, 1.0f), 0.25f + 0.75f * strength);
        }

        private static bool NearestHit_R5(List<RenderMesh> meshes, Ray ray, float maxDist,
            out RenderMesh hitMesh, out float hitDist, out Vector3 hitNorm)
        {
            hitMesh = null; hitDist = maxDist; hitNorm = Vector3.UnitZ;
            foreach (var m in meshes)
            {
                if (m == null || !m.Visible) continue;
                if (m.AlphaMode != GeomAlphaMode.Opaque && m.AlphaMode != GeomAlphaMode.Cutout) continue;
                if (m.PickVerts == null || m.PickIndices == null || m.PickIndices.Length < 3) continue;
                var sph = m.WorldSphere;
                if (!ray.Intersects(ref sph, out float sd) || sd > hitDist) continue;

                var inv = m.Transform; inv.Invert();
                var o = Vector3.TransformCoordinate(ray.Position, inv);
                var d = Vector3.TransformNormal(ray.Direction, inv);
                float dl = d.Length();
                if (dl < 1e-9f) continue;
                d /= dl;
                var verts = m.PickVerts; var idx = m.PickIndices;
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    var a = verts[idx[i]]; var b = verts[idx[i + 1]]; var c = verts[idx[i + 2]];
                    if (!MollerTrumbore_R5(o, d, a, b, c, out float t)) continue;
                    float world = t / dl;
                    if (world <= 0.01f || world >= hitDist) continue;
                    hitDist = world;
                    hitMesh = m;
                    var nl = Vector3.Cross(b - a, c - a);
                    if (nl.LengthSquared() < 1e-12f) nl = Vector3.UnitZ;
                    hitNorm = Vector3.Normalize(Vector3.TransformNormal(Vector3.Normalize(nl), m.Transform));
                }
            }
            return hitMesh != null;
        }

        private static bool MollerTrumbore_R5(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < 1e-8f) return false;
            float invDet = 1.0f / det;
            var tv = o - a;
            float u = Vector3.Dot(tv, p) * invDet;
            if (u < -1e-5f || u > 1.0f + 1e-5f) return false;
            var q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, q) * invDet;
            if (v < -1e-5f || u + v > 1.0f + 1e-5f) return false;
            t = Vector3.Dot(e2, q) * invDet;
            return t > 0;
        }
    }
}

