using System;
using System.Collections.Generic;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public struct VertexSnapHit
    {
        public bool Valid;
        public Vector3 Position;
        public RenderMesh Mesh;
        public int VertexIndex;
        public int Triangle;
        public Vector3 SurfacePoint;
        public float ScreenDistance;
        public string Describe() => Valid ? $"{Position.X:0.000}, {Position.Y:0.000}, {Position.Z:0.000}  (v{VertexIndex}{(Triangle >= 0 ? $" tri {Triangle}" : "")}, {ScreenDistance:0.0} px)" : "no vertex";
    }

    public static class MloVertexSnap
    {
        public const float RadiusPx = 14.0f;

        public static bool Find(IEnumerable<RenderMesh> meshes, Camera cam, Ray ray, float mx, float my, float vw, float vh, out VertexSnapHit hit, float radiusPx = RadiusPx)
        {
            hit = default;
            if (meshes == null || cam == null) return false;
            var vp = cam.ViewProjMatrix;

            RenderMesh bestMesh = null; int bestTri = -1; float bestDist = float.MaxValue;
            var candidates = new List<RenderMesh>();
            foreach (var mesh in meshes)
            {
                if (mesh == null || !mesh.Visible || mesh.NeverDraw || mesh.PickVerts == null || mesh.PickIndices == null || mesh.PickIndices.Length < 3) continue;
                var b = mesh.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                candidates.Add(mesh);
                if (!ray.Intersects(ref b, out float bt) || bt > bestDist) continue;
                if (RayHitTriangle(mesh, ref ray, out float t, out int tri) && t < bestDist) { bestDist = t; bestMesh = mesh; bestTri = tri; }
            }

            if (bestMesh != null)
            {
                var surface = ray.Position + ray.Direction * bestDist;
                bool found = false;
                float bestPx = float.MaxValue;
                foreach (var mesh in candidates)
                {
                    int tri = ReferenceEquals(mesh, bestMesh) ? bestTri : -1;
                    if (!NearestVertexOnScreen(mesh, cam, vp, mx, my, vw, vh, radiusPx, surface, bestDist, tri, out var h))
                        continue;
                    if (h.ScreenDistance >= bestPx) continue;
                    bestPx = h.ScreenDistance;
                    hit = h;
                    hit.Triangle = ReferenceEquals(mesh, bestMesh) ? bestTri : -1;
                    hit.SurfacePoint = surface;
                    hit.Valid = true;
                    found = true;
                }
                if (found) return true;
            }

            float best = radiusPx;
            bool any = false;
            foreach (var mesh in candidates)
            {
                var b = mesh.WorldBounds;
                float pad = radiusPx * cam.WorldPerPixel((b.Minimum + b.Maximum) * 0.5f) * 1.5f + 0.01f;
                var fat = new BoundingBox(b.Minimum - new Vector3(pad), b.Maximum + new Vector3(pad));
                if (!ray.Intersects(ref fat, out float _ft)) continue;
                if (NearestVertexOnScreen(mesh, cam, vp, mx, my, vw, vh, best, Vector3.Zero, float.MaxValue, -1, out var h) && h.ScreenDistance < best)
                {
                    best = h.ScreenDistance; hit = h; hit.Triangle = -1; hit.SurfacePoint = h.Position; hit.Valid = true; any = true;
                }
            }
            return any;
        }

        public static bool RayHitTriangle(RenderMesh mesh, ref Ray worldRay, out float dist, out int triangle)
        {
            dist = float.MaxValue; triangle = -1;
            var inv = mesh.Transform; inv.Invert();
            var o = Vector3.TransformCoordinate(worldRay.Position, inv);
            var d = Vector3.TransformNormal(worldRay.Direction, inv);
            float dl = d.Length();
            if (dl < 1e-9f) return false;
            d /= dl;
            var local = new Ray(o, d);
            var verts = mesh.PickVerts; var idx = mesh.PickIndices;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                int ia = idx[i], ib = idx[i + 1], ic = idx[i + 2];
                if (ia >= verts.Length || ib >= verts.Length || ic >= verts.Length) continue;
                ref var a = ref verts[ia]; ref var b = ref verts[ib]; ref var c = ref verts[ic];
                if (!local.Intersects(ref a, ref b, ref c, out float t)) continue;
                t /= dl;
                if (t < dist) { dist = t; triangle = i / 3; }
            }
            return triangle >= 0;
        }

        private static bool NearestVertexOnScreen(RenderMesh mesh, Camera cam, Matrix vp, float mx, float my, float vw, float vh, float radiusPx,
                                                  Vector3 surface, float surfaceDist, int triangle, out VertexSnapHit hit)
        {
            hit = default;
            var verts = mesh.PickVerts; var idx = mesh.PickIndices;
            var xf = mesh.Transform;
            var eye = cam.Position;
            float depthTol = surfaceDist < float.MaxValue ? Math.Max(0.15f, surfaceDist * 0.03f) : float.MaxValue;
            float maxDepth = surfaceDist < float.MaxValue ? surfaceDist + depthTol : float.MaxValue;
            float worldR = surfaceDist < float.MaxValue ? radiusPx * cam.WorldPerPixel(surface) * 1.6f + depthTol : float.MaxValue;
            float worldR2 = worldR < float.MaxValue ? worldR * worldR : float.MaxValue;

            int bestI = -1; float bestPx = radiusPx; float bestDepth = float.MaxValue;
            void Consider(int i)
            {
                if (i < 0 || i >= verts.Length) return;
                var w = Vector3.TransformCoordinate(verts[i], xf);
                if (worldR2 < float.MaxValue && (w - surface).LengthSquared() > worldR2) return;
                float depth = (w - eye).Length();
                if (depth > maxDepth) return;
                var clip = Vector4.Transform(new Vector4(w, 1.0f), vp);
                if (clip.W <= 1e-5f) return;
                float sx = (clip.X / clip.W * 0.5f + 0.5f) * vw;
                float sy = (0.5f - clip.Y / clip.W * 0.5f) * vh;
                float px = (float)Math.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (px < bestPx - 0.01f || (Math.Abs(px - bestPx) <= 0.01f && depth < bestDepth)) { bestPx = px; bestI = i; bestDepth = depth; }
            }
            if (verts.Length <= 600000) for (int i = 0; i < verts.Length; i++) Consider(i);
            if (triangle >= 0 && bestI < 0)
            {
                bestPx = float.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    int ii = triangle * 3 + k;
                    if (ii >= idx.Length) continue;
                    int i = idx[ii];
                    if (i < 0 || i >= verts.Length) continue;
                    var w = Vector3.TransformCoordinate(verts[i], xf);
                    var clip = Vector4.Transform(new Vector4(w, 1.0f), vp);
                    if (clip.W <= 1e-5f) continue;
                    float sx = (clip.X / clip.W * 0.5f + 0.5f) * vw;
                    float sy = (0.5f - clip.Y / clip.W * 0.5f) * vh;
                    float px = (float)Math.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                    if (px < bestPx) { bestPx = px; bestI = i; }
                }
            }
            if (bestI < 0) return false;
            hit.Mesh = mesh; hit.VertexIndex = bestI; hit.ScreenDistance = bestPx;
            hit.Position = Vector3.TransformCoordinate(verts[bestI], xf);
            hit.Valid = true;
            return true;
        }

        public static bool NearestToPoint(IEnumerable<RenderMesh> meshes, Vector3 p, float maxDist, out VertexSnapHit hit)
        {
            hit = default;
            float best2 = maxDist * maxDist; bool any = false;
            foreach (var mesh in meshes)
            {
                if (mesh == null || !mesh.Visible || mesh.PickVerts == null) continue;
                var b = mesh.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                var fat = new BoundingBox(b.Minimum - new Vector3(maxDist), b.Maximum + new Vector3(maxDist));
                if (fat.Contains(ref p) == ContainmentType.Disjoint) continue;
                var inv = mesh.Transform; inv.Invert();
                var lp = Vector3.TransformCoordinate(p, inv);
                var verts = mesh.PickVerts;
                float sc = Math.Max(Math.Max(mesh.Transform.Row1.Length(), mesh.Transform.Row2.Length()), mesh.Transform.Row3.Length());
                float lr2 = sc > 1e-6f ? best2 / (sc * sc) : best2;
                int bi = -1; float bd2 = lr2;
                for (int i = 0; i < verts.Length; i++)
                {
                    float d2 = (verts[i] - lp).LengthSquared();
                    if (d2 < bd2) { bd2 = d2; bi = i; }
                }
                if (bi < 0) continue;
                var w = Vector3.TransformCoordinate(verts[bi], mesh.Transform);
                float wd2 = (w - p).LengthSquared();
                if (wd2 < best2) { best2 = wd2; hit = new VertexSnapHit { Valid = true, Mesh = mesh, VertexIndex = bi, Position = w, Triangle = -1, SurfacePoint = w }; any = true; }
            }
            return any;
        }
    }
}

