using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SharpDX;
using RageLightEditor.Rendering;

namespace RageLightEditor.Editor
{
    public enum BlendStyle
    {
        Cut = 0,
        Linear = 1,
        EaseInOut = 2,
        EaseIn = 3,
        EaseOut = 4,
        HardIn = 5,
        HardOut = 6,
    }

    public class ShakeSettings
    {
        [JsonInclude] public float PositionAmplitude = 0.035f;
        [JsonInclude] public float RotationAmplitude = 0.35f;
        [JsonInclude] public float Frequency = 1.0f;

        public static readonly (float Freq, float Weight)[] Channels =
        {
            (0.31f, 1.00f), (0.87f, 0.55f), (2.13f, 0.28f), (5.70f, 0.12f),
        };

        public ShakeSettings Clone() => (ShakeSettings)MemberwiseClone();
    }

    public class CameraShot
    {
        [JsonInclude] public float X, Y, Z;
        [JsonInclude] public float Yaw, Pitch;
        [JsonInclude] public float Fov = 54.0f;
        [JsonInclude] public float Focus;
        [JsonInclude] public float Duration = 3.0f;
        [JsonInclude] public float Hold;
        [JsonInclude] public float Ease = 1.0f;
        [JsonInclude] public BlendStyle Blend = BlendStyle.EaseInOut;
        [JsonInclude] public float Shake = 0.0f;
        [JsonInclude] public string Name = "Shot";

        [JsonIgnore]
        public Vector3 Position
        {
            get => new Vector3(X, Y, Z);
            set { X = value.X; Y = value.Y; Z = value.Z; }
        }

        public CameraShot Clone() => (CameraShot)MemberwiseClone();
    }

    public class CameraSequence
    {
        [JsonInclude] public List<CameraShot> Shots = new List<CameraShot>();
        [JsonInclude] public int Fps = 30;
        [JsonInclude] public bool Loop = true;
        [JsonInclude] public ShakeSettings Shake = new ShakeSettings();

        public float Length
        {
            get
            {
                float t = 0;
                for (int i = 0; i < Shots.Count; i++)
                {
                    if (i > 0) t += Math.Max(Shots[i].Duration, 0.0f);
                    t += Math.Max(Shots[i].Hold, 0.0f);
                }
                return t;
            }
        }

        public int FrameCount => Math.Max(1, (int)Math.Round(Length * Math.Max(Fps, 1)));

        public bool Sample(float time, out Vector3 pos, out float yaw, out float pitch,
                           out float fov, out float focus, out float shake)
        {
            pos = Vector3.Zero; yaw = pitch = 0; fov = 54.0f; focus = 0; shake = 0;
            if (Shots.Count == 0) return false;
            if (Shots.Count == 1)
            {
                var only = Shots[0];
                pos = only.Position; yaw = only.Yaw; pitch = only.Pitch;
                fov = only.Fov; focus = only.Focus; shake = only.Shake;
                return true;
            }

            time = Math.Max(time, 0.0f);

            float cursor = 0;
            for (int i = 0; i < Shots.Count; i++)
            {
                float hold = Math.Max(Shots[i].Hold, 0.0f);
                if (time <= cursor + hold)
                {
                    var s = Shots[i];
                    pos = s.Position; yaw = s.Yaw; pitch = s.Pitch; fov = s.Fov;
                    focus = s.Focus; shake = s.Shake;
                    return true;
                }
                cursor += hold;

                if (i == Shots.Count - 1) break;
                float dur = Math.Max(Shots[i + 1].Duration, 0.0001f);
                if (time <= cursor + dur)
                {
                    float u = (time - cursor) / dur;
                    Interpolate(i, u, out pos, out yaw, out pitch, out fov, out focus, out shake);
                    return true;
                }
                cursor += dur;
            }

            var last = Shots[Shots.Count - 1];
            pos = last.Position; yaw = last.Yaw; pitch = last.Pitch; fov = last.Fov;
            focus = last.Focus; shake = last.Shake;
            return true;
        }

        private void Interpolate(int i, float u, out Vector3 pos, out float yaw, out float pitch,
                                 out float fov, out float focus, out float shake)
        {
            var a = Shots[i];
            var b = Shots[i + 1];

            float e = MathUtil.Clamp(b.Ease, 0.0f, 1.0f);
            float t = MathUtil.Lerp(u, BlendCurve(b.Blend, u), e);

            Vector3 p1 = a.Position, p2 = b.Position;
            Vector3 p0 = i > 0 ? Shots[i - 1].Position : p1 + (p1 - p2);
            Vector3 p3 = (i + 2) < Shots.Count ? Shots[i + 2].Position : p2 + (p2 - p1);
            pos = CatmullRom(p0, p1, p2, p3, t);

            yaw = MathUtil.Lerp(a.Yaw, Unwrap(a.Yaw, b.Yaw), t);
            pitch = MathUtil.Lerp(a.Pitch, b.Pitch, t);
            fov = MathUtil.Lerp(a.Fov, b.Fov, t);
            focus = (a.Focus <= 0.0f || b.Focus <= 0.0f) ? 0.0f : MathUtil.Lerp(a.Focus, b.Focus, t);
            shake = MathUtil.Lerp(a.Shake, b.Shake, t);
        }

        public static float BlendCurve(BlendStyle style, float u)
        {
            u = MathUtil.Clamp(u, 0.0f, 1.0f);
            if (style == BlendStyle.Cut) return u >= 1.0f ? 1.0f : 0.0f;
            if (style == BlendStyle.Linear) return u;

            float m0, m1;
            switch (style)
            {
                case BlendStyle.EaseIn:  m0 = 1.4f; m1 = 0.0f; break;
                case BlendStyle.EaseOut: m0 = 0.0f; m1 = 1.4f; break;
                case BlendStyle.HardIn:  m0 = 0.0f; m1 = 3.0f; break;
                case BlendStyle.HardOut: m0 = 3.0f; m1 = 0.0f; break;
                default:                 m0 = 0.0f; m1 = 0.0f; break;
            }
            float t2 = u * u, t3 = t2 * u;
            return (-2.0f * t3 + 3.0f * t2) + (t3 - 2.0f * t2 + u) * m0 + (t3 - t2) * m1;
        }

        public static void SampleShake(ShakeSettings s, float time, float gain,
                                       out Vector3 posOffset, out Vector3 rotDegrees)
        {
            posOffset = Vector3.Zero; rotDegrees = Vector3.Zero;
            if (s == null || gain <= 0.0001f) return;

            float t = time * Math.Max(s.Frequency, 0.0f);
            for (int axis = 0; axis < 3; axis++)
            {
                float p = 0, r = 0;
                for (int c = 0; c < ShakeSettings.Channels.Length; c++)
                {
                    var (f, w) = ShakeSettings.Channels[c];
                    p += Noise1D(t * f + axis * 37.13f + c * 5.77f) * w;
                    r += Noise1D(t * f + 101.7f + axis * 53.91f + c * 9.31f) * w;
                }
                posOffset[axis] = p * s.PositionAmplitude * gain;
                rotDegrees[axis] = r * s.RotationAmplitude * gain;
            }
        }

        private static float Noise1D(float x)
        {
            int i = (int)Math.Floor(x);
            float f = x - i;
            f = f * f * (3.0f - 2.0f * f);
            return MathUtil.Lerp(Hash1D(i), Hash1D(i + 1), f) - 0.5f;
        }

        private static float Hash1D(int i)
        {
            uint v = (uint)i * 1664525u + 1013904223u;
            v ^= v >> 15; v *= 2246822519u;
            v ^= v >> 13; v *= 3266489917u;
            v ^= v >> 16;
            return (v & 0xFFFFFFu) / 16777216.0f;
        }

        private static float Unwrap(float from, float to)
        {
            float d = to - from;
            while (d > MathUtil.Pi) d -= MathUtil.TwoPi;
            while (d < -MathUtil.Pi) d += MathUtil.TwoPi;
            return from + d;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2.0f * p1)
                + (-p0 + p2) * t
                + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2
                + (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3);
        }

        public static CameraShot FromCamera(Camera cam, float fovDeg, string name)
        {
            return new CameraShot
            {
                Position = cam.Position,
                Yaw = cam.Yaw,
                Pitch = cam.Pitch,
                Fov = fovDeg,
                Name = name,
            };
        }

        public static void ApplyToCamera(Camera cam, Vector3 pos, float yaw, float pitch, float fovDeg,
                                         ShakeSettings shake = null, float shakeGain = 0.0f,
                                         float shakeTime = 0.0f)
        {
            if (shake != null && shakeGain > 0.0001f)
            {
                SampleShake(shake, shakeTime, shakeGain, out var po, out var ro);
                float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
                float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
                var fwd = new Vector3(cy * cp, sy * cp, sp);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitZ));
                var up = Vector3.Cross(right, fwd);
                pos += right * po.X + up * po.Y + fwd * po.Z;
                yaw += MathUtil.DegreesToRadians(ro.X);
                pitch = MathUtil.Clamp(pitch + MathUtil.DegreesToRadians(ro.Y), -1.55f, 1.55f);
            }

            const float pivot = 4.0f;
            cam.Yaw = cam.TargetYaw = yaw;
            cam.Pitch = cam.TargetPitch = pitch;
            cam.Distance = cam.TargetDistance = pivot;
            cam.FieldOfView = MathUtil.DegreesToRadians(MathUtil.Clamp(fovDeg, 5.0f, 140.0f));

            var offset = new Vector3(
                (float)(Math.Cos(pitch) * Math.Cos(yaw)),
                (float)(Math.Cos(pitch) * Math.Sin(yaw)),
                (float)Math.Sin(pitch)) * pivot;
            cam.Target = pos - offset;
            cam.SnapSmoothing();
            cam.Update(0.0f);
        }
    }
}

