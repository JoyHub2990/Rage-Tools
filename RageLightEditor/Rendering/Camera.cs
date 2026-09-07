using System;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class Camera
    {
        public Vector3 Target = Vector3.Zero;
        public float Distance = 5.0f;
        public float Yaw = 0.8f;
        public float Pitch = 0.45f;
        public float FieldOfView = MathUtil.PiOverFour * 1.2f;
        public const float MinFovDeg = 10.0f;
        public const float MaxFovDeg = 120.0f;
        public float NearClip = 0.05f;
        public float FarClip = 3000.0f;

        public float MinDistance = 0.1f;
        public float MaxDistance = 1000.0f;

        public float OrbitAnchorMax = 1.5f;

        public void ReanchorPivot(float newDistance)
        {
            newDistance = MathUtil.Clamp(newDistance, MinDistance, MaxDistance);
            var eye = Position;
            Target = eye + GetForward() * newDistance;
            Distance = newDistance;
            TargetDistance = newDistance;
        }

        public float Sensitivity = 0.005f;
        public float Smoothness = 0.0f;
        public float ZoomSpeed = 8.0f;
        public const float PitchLimit = 1.55f;

        public float TargetYaw = 0.8f;
        public float TargetPitch = 0.45f;
        public float TargetDistance = 5.0f;
        private bool smoothingPrimed;

        public void SnapSmoothing()
        {
            TargetYaw = Yaw;
            TargetPitch = Pitch;
            TargetDistance = Distance;
        }

        public Vector3 Position { get; private set; }
        public Matrix ViewMatrix { get; private set; }
        public Matrix ProjMatrix { get; private set; }
        public Matrix ViewProjMatrix { get; private set; }

        private float aspect = 1.0f;

        public void SetAspect(float a)
        {
            aspect = Math.Max(a, 0.0001f);
        }

        public float ViewportHeight = 900.0f;

        public float WorldPerPixel(Vector3 p)
        {
            float dist = Math.Max(Vector3.Dot(p - Position, GetForward()), NearClip);
            return dist * 2.0f * (float)Math.Tan(FieldOfView * 0.5f) / Math.Max(ViewportHeight, 1.0f);
        }

        public Vector3 Forward
        {
            get
            {
                float cp = (float)Math.Cos(Pitch);
                return new Vector3(
                    (float)(Math.Cos(Yaw) * cp),
                    (float)(Math.Sin(Yaw) * cp),
                    (float)Math.Sin(Pitch)) * -1.0f;
            }
        }

        public void Update(float elapsed)
        {
            if (!smoothingPrimed) { SnapSmoothing(); smoothingPrimed = true; }

            TargetPitch = MathUtil.Clamp(TargetPitch, -PitchLimit, PitchLimit);
            if (TargetDistance < MinDistance) TargetDistance = MinDistance;
            if (TargetDistance > MaxDistance) TargetDistance = MaxDistance;
            elapsed = MathUtil.Clamp(elapsed, 0.0f, 0.25f);

            float yaw0 = Yaw, pitch0 = Pitch;

            bool snap = Smoothness <= 0.0f;
            float zv = snap ? 1.0f : Math.Min(Math.Max(ZoomSpeed, 0.0f) * elapsed, 1.0f);
            Distance += (TargetDistance - Distance) * zv;
            Distance = MathUtil.Clamp(Distance, MinDistance, MaxDistance);

            var eyeNow = Target + Offset(yaw0, pitch0) * Distance;

            float sv = snap ? 1.0f : Math.Min(Smoothness * elapsed, 1.0f);
            Yaw += (TargetYaw - Yaw) * sv;
            Pitch += (TargetPitch - Pitch) * sv;
            Pitch = MathUtil.Clamp(Pitch, -PitchLimit, PitchLimit);
            if (snap) { Yaw = TargetYaw; Pitch = TargetPitch; }

            if (RotateInPlace &&
                (Math.Abs(Yaw - yaw0) > 1e-7f || Math.Abs(Pitch - pitch0) > 1e-7f))
            {
                Target = eyeNow - Offset(Yaw, Pitch) * Distance;
            }

            Update();
        }

        private static Vector3 Offset(float yaw, float pitch)
        {
            float cp = (float)Math.Cos(pitch);
            return new Vector3(
                (float)(Math.Cos(yaw) * cp),
                (float)(Math.Sin(yaw) * cp),
                (float)Math.Sin(pitch));
        }

        public bool RotateInPlace = true;

        public void Update()
        {
            Pitch = MathUtil.Clamp(Pitch, -PitchLimit, PitchLimit);
            Distance = MathUtil.Clamp(Distance, MinDistance, MaxDistance);

            float cp = (float)Math.Cos(Pitch);
            var offset = new Vector3(
                (float)(Math.Cos(Yaw) * cp),
                (float)(Math.Sin(Yaw) * cp),
                (float)Math.Sin(Pitch));

            Position = Target + offset * Distance;

            ViewMatrix = Matrix.LookAtRH(Position, Target, Vector3.UnitZ);
            FieldOfView = MathUtil.Clamp(FieldOfView, MinFovDeg * 0.0174533f, MaxFovDeg * 0.0174533f);
            ProjMatrix = Matrix.PerspectiveFovRH(FieldOfView, aspect, FarClip, NearClip);
            ViewProjMatrix = ViewMatrix * ProjMatrix;
        }

        public void Orbit(float dx, float dy)
        {
            TargetYaw -= dx * Sensitivity;
            TargetPitch += dy * Sensitivity;
        }

        public void Pan(float dx, float dy)
        {
            float cp = (float)Math.Cos(Pitch);
            var fwd = new Vector3((float)(Math.Cos(Yaw) * cp), (float)(Math.Sin(Yaw) * cp), (float)Math.Sin(Pitch));
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, fwd));
            var up = Vector3.Normalize(Vector3.Cross(fwd, right));
            float scale = Distance * 0.0016f;
            Target += right * (dx * scale) + up * (dy * scale);
        }

        public void Zoom(float wheelDelta)
        {
            int notches = (int)Math.Round(wheelDelta / 120.0f);
            if (notches == 0) notches = wheelDelta > 0 ? 1 : wheelDelta < 0 ? -1 : 0;
            for (int i = 0; i < Math.Abs(notches); i++)
            {
                TargetDistance *= notches > 0 ? (1.0f / 1.1f) : 1.1f;
            }
            TargetDistance = MathUtil.Clamp(TargetDistance, MinDistance, MaxDistance);
        }

        public void Translate(Vector3 worldDelta)
        {
            Target += worldDelta;
        }

        public void LookRotate(float dx, float dy)
        {
            var eye = Position;
            TargetYaw -= dx * Sensitivity;
            TargetPitch = MathUtil.Clamp(TargetPitch + dy * Sensitivity, -PitchLimit, PitchLimit);
            float cp = (float)Math.Cos(TargetPitch);
            var offset = new Vector3(
                (float)(Math.Cos(TargetYaw) * cp),
                (float)(Math.Sin(TargetYaw) * cp),
                (float)Math.Sin(TargetPitch));
            Target = eye - offset * Distance;
        }

        public Vector3 GetForward()
        {
            float cp = (float)Math.Cos(Pitch);
            return -new Vector3(
                (float)(Math.Cos(Yaw) * cp),
                (float)(Math.Sin(Yaw) * cp),
                (float)Math.Sin(Pitch));
        }

        public Vector3 GetRight()
        {
            var fwd = GetForward();
            var right = Vector3.Cross(fwd, Vector3.UnitZ);
            if (right.LengthSquared() < 1e-6f) return Vector3.UnitX;
            right.Normalize();
            return right;
        }

        public void FrameBounds(Vector3 center, float radius)
        {
            Target = center;
            if (radius > 0.001f)
            {
                Distance = radius * 2.2f;
                MaxDistance = Math.Max(radius * 50.0f, 100.0f);
                FarClip = Math.Max(radius * 100.0f, 3000.0f);
            }
            SnapSmoothing();
            Update();

            if (!OrbitSubject_U3 && Distance > OrbitAnchorMax) ReanchorPivot(OrbitAnchorMax);
        }

        public struct Snapshot
        {
            public bool Valid;
            public Vector3 Target;
            public float Distance, Yaw, Pitch;
            public float TargetDistance, TargetYaw, TargetPitch;
            public float FieldOfView, NearClip, FarClip, MaxDistance;

            public bool SameAs(in Snapshot o) =>
                Valid == o.Valid && Target == o.Target &&
                Distance == o.Distance && Yaw == o.Yaw && Pitch == o.Pitch &&
                TargetDistance == o.TargetDistance && TargetYaw == o.TargetYaw && TargetPitch == o.TargetPitch &&
                FieldOfView == o.FieldOfView && NearClip == o.NearClip && FarClip == o.FarClip &&
                MaxDistance == o.MaxDistance;
        }

        public Snapshot Capture() => new Snapshot
        {
            Valid = true,
            Target = Target,
            Distance = Distance, Yaw = Yaw, Pitch = Pitch,
            TargetDistance = TargetDistance, TargetYaw = TargetYaw, TargetPitch = TargetPitch,
            FieldOfView = FieldOfView, NearClip = NearClip, FarClip = FarClip, MaxDistance = MaxDistance,
        };

        public void Restore(in Snapshot s)
        {
            if (!s.Valid) return;
            Target = s.Target;
            Distance = s.Distance; Yaw = s.Yaw; Pitch = s.Pitch;
            TargetDistance = s.TargetDistance; TargetYaw = s.TargetYaw; TargetPitch = s.TargetPitch;
            FieldOfView = s.FieldOfView; NearClip = s.NearClip; FarClip = s.FarClip; MaxDistance = s.MaxDistance;
            smoothingPrimed = true;
            Update();
        }

        public Ray GetPickRay(float sx, float sy, float viewportW, float viewportH)
        {
            var vp = ViewProjMatrix;
            vp.Invert();
            float nx = (2.0f * sx / Math.Max(viewportW, 1)) - 1.0f;
            float ny = 1.0f - (2.0f * sy / Math.Max(viewportH, 1));
            var np = Vector3.TransformCoordinate(new Vector3(nx, ny, 1.0f), vp);
            var fp = Vector3.TransformCoordinate(new Vector3(nx, ny, 0.0f), vp);
            return new Ray(np, Vector3.Normalize(fp - np));
        }
    }
}

