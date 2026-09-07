using System;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public class SunCascades
    {
        public const int MaxCascades = 4;
        public const float BlendBetweenCascades = 0.05f;
        public static readonly float[] DefaultIntervals = { 7.0f, 20.0f, 65.0f, 160.0f, 600.0f, 3000.0f };

        public int Count;
        public readonly Matrix[] ViewProj = new Matrix[MaxCascades];
        public readonly float[] SplitFar = new float[MaxCascades];
        public readonly float[] TexelWorld = new float[MaxCascades];
        public Vector3 SunPos;
        public readonly BoundingSphere[] Cull = new BoundingSphere[MaxCascades];
        public Vector3 FittedCamera;
        public Vector3 FittedSunDir;

        public void Fit(Camera camera, Vector3 sunDir, float[] intervals, int count, int textureSize,
            Vector3 sceneMin, Vector3 sceneMax)
        {
            Count = Math.Clamp(count, 1, MaxCascades);
            var dir = sunDir.LengthSquared() > 1e-6f ? Vector3.Normalize(sunDir) : Vector3.UnitZ;
            FittedCamera = camera.Position;
            FittedSunDir = dir;

            var up = Math.Abs(dir.Z) > 0.95f ? Vector3.UnitX : Vector3.UnitZ;
            var lightView = Matrix.LookAtLH(dir, Vector3.Zero, up);

            var pm = camera.ProjMatrix;
            float tanHalfH = 1.0f / Math.Max(Math.Abs(pm.M11), 1e-4f);
            float tanHalfV = 1.0f / Math.Max(Math.Abs(pm.M22), 1e-4f);
            var fwd = camera.GetForward();
            var right = camera.GetRight();
            var camUp = Vector3.Cross(right, fwd);
            if (Vector3.Dot(camUp, Vector3.UnitZ) < 0) camUp = -camUp;
            camUp = Vector3.Normalize(camUp);
            var camPos = camera.Position;

            var sceneCorners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                sceneCorners[i] = new Vector3((i & 1) != 0 ? sceneMax.X : sceneMin.X,
                                              (i & 2) != 0 ? sceneMax.Y : sceneMin.Y,
                                              (i & 4) != 0 ? sceneMax.Z : sceneMin.Z);
            }
            var sceneLsMin = new Vector3(float.MaxValue);
            var sceneLsMax = new Vector3(float.MinValue);
            foreach (var c in sceneCorners)
            {
                var p = Vector3.TransformCoordinate(c, lightView);
                sceneLsMin = Vector3.Min(sceneLsMin, p);
                sceneLsMax = Vector3.Max(sceneLsMax, p);
            }

            float sunStandoff = Math.Max((sceneMax - sceneMin).Length(), 1.0f) + 200.0f;
            SunPos = camPos + dir * sunStandoff;

            var corners = new Vector3[8];
            for (int ci = 0; ci < Count; ci++)
            {
                float begin = 0.0f;
                float end = intervals[Math.Min(ci, intervals.Length - 1)];
                SplitFar[ci] = end;

                for (int k = 0; k < 8; k++)
                {
                    float d = (k < 4) ? Math.Max(begin, camera.NearClip) : end;
                    float sx = ((k & 1) == 0 ? 1 : -1) * tanHalfH * d;
                    float sy = ((k & 2) == 0 ? 1 : -1) * tanHalfV * d;
                    corners[k] = camPos + fwd * d + right * sx + camUp * sy;
                }

                var lsMin = new Vector3(float.MaxValue);
                var lsMax = new Vector3(float.MinValue);
                var lsCorners = new Vector3[8];
                for (int k = 0; k < 8; k++)
                {
                    lsCorners[k] = Vector3.TransformCoordinate(corners[k], lightView);
                    lsMin = Vector3.Min(lsMin, lsCorners[k]);
                    lsMax = Vector3.Max(lsMax, lsCorners[k]);
                }

                float bound = (corners[0] - corners[6]).Length();
                var border = (new Vector3(bound) - (lsMax - lsMin)) * 0.5f;
                border.Z = 0.0f;
                lsMax += border;
                lsMin -= border;

                float wupt = bound / textureSize;
                TexelWorld[ci] = wupt;
                lsMin.X = (float)Math.Floor(lsMin.X / wupt) * wupt;
                lsMin.Y = (float)Math.Floor(lsMin.Y / wupt) * wupt;
                lsMax.X = (float)Math.Floor(lsMax.X / wupt) * wupt;
                lsMax.Y = (float)Math.Floor(lsMax.Y / wupt) * wupt;

                float near = Math.Min(sceneLsMin.Z, lsMin.Z) - 1.0f;
                float far = Math.Max(sceneLsMax.Z, lsMax.Z) + 1.0f;

                var ortho = Matrix.OrthoOffCenterLH(lsMin.X, lsMax.X, lsMin.Y, lsMax.Y, near, far);
                ViewProj[ci] = lightView * ortho;

                var lsCentre = new Vector3((lsMin.X + lsMax.X) * 0.5f, (lsMin.Y + lsMax.Y) * 0.5f, (near + far) * 0.5f);
                var inv = Matrix.Invert(lightView);
                var wc = Vector3.TransformCoordinate(lsCentre, inv);
                float r = 0.5f * (float)Math.Sqrt((lsMax.X - lsMin.X) * (lsMax.X - lsMin.X)
                                                 + (lsMax.Y - lsMin.Y) * (lsMax.Y - lsMin.Y)
                                                 + (far - near) * (far - near));
                Cull[ci] = new BoundingSphere(wc, r);
            }
            for (int ci = Count; ci < MaxCascades; ci++)
            {
                ViewProj[ci] = ViewProj[Count - 1];
                SplitFar[ci] = SplitFar[Count - 1];
                TexelWorld[ci] = TexelWorld[Count - 1];
                Cull[ci] = Cull[Count - 1];
            }
        }
    }
}

