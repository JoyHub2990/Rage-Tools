using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public const float JokeVisibleSeconds_T6 = 4.6f;
        public const float JokeCooldownMin_T6 = 12.0f, JokeCooldownMax_T6 = 25.0f;
        private const float JokeFill_T6 = 0.72f;
        private const float JokeAspect_T6 = 0.75f;

        public static bool MirrorJokeBlocked_T6(bool photo, bool still, bool cine) => photo || still || cine;

        private bool jokeBlockLogged_T6;

        private void NoteJokeBlocked_T6(bool photo, bool still, bool cine)
        {
            if (screenshotPath == null || jokeBlockLogged_T6) return;
            jokeBlockLogged_T6 = true;
            Console.WriteLine("MIRRORJOKE blocked: " +
                              (photo ? "photo mode" : still ? "render to file" : cine ? "Cinematic" : "?") +
                              " - a deliverable frame never gets it, whatever Help says");
        }

        public struct JokePlacement_T6
        {
            public Vector3 Centre;
            public Vector3 Right, Up, Fwd;
            public float Height;
            public float Fill;
        }

        public static JokePlacement_T6 JokeFit_T6(BoundingBox mirror, Vector3 eye, Vector3 planeNormal, float distToPlane)
        {
            var n = planeNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(planeNormal) : Vector3.UnitY;
            var d = Math.Clamp(distToPlane, 0.05f, 100.0f);
            var fz = -n;
            var up = Vector3.UnitZ - fz * Vector3.Dot(Vector3.UnitZ, fz);
            up = up.LengthSquared() > 1e-6f ? Vector3.Normalize(up) : Vector3.UnitZ;
            var right = Vector3.Normalize(Vector3.Cross(up, fz));

            bool known = mirror.Maximum.X > mirror.Minimum.X;
            var centre = known ? (mirror.Minimum + mirror.Maximum) * 0.5f : eye + fz * d;
            var ext = known ? (mirror.Maximum - mirror.Minimum) * 0.5f : new Vector3(0.35f);
            float hr = Math.Abs(ext.X * right.X) + Math.Abs(ext.Y * right.Y) + Math.Abs(ext.Z * right.Z);
            float hu = Math.Abs(ext.X * up.X) + Math.Abs(ext.Y * up.Y) + Math.Abs(ext.Z * up.Z);
            hr = Math.Max(hr, 0.05f);
            hu = Math.Max(hu, 0.05f);
            float fitHalf = Math.Clamp(Math.Min(hu, hr / JokeAspect_T6), 0.12f, 3.0f);

            float standoff = Math.Clamp(d * 0.42f, 0.30f, 0.90f);
            float look = Math.Max(0.25f, 2.0f * d - standoff);

            var dir = centre - eye;
            dir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : fz;
            var q = eye + dir * look;
            float qd = d + look * Vector3.Dot(n, dir);
            var pos = q - n * (2.0f * qd);

            float height = Math.Clamp(JokeFill_T6 * (2.0f * fitHalf) * look / d, 0.35f, 4.0f);

            return new JokePlacement_T6
            {
                Centre = pos,
                Right = right,
                Up = up,
                Fwd = fz,
                Height = height,
                Fill = JokeFill_T6,
            };
        }

        private void DrawBoldFinger_T6(DeviceContext context, Matrix reflViewProj, JokePlacement_T6 fit, float k)
        {
            float s = fit.Height * Math.Clamp(k, 0f, 1f);
            if (s < 0.02f) return;
            var skin = new Vector4(0.99f, 0.80f, 0.63f, 1f);
            var cuff = new Vector4(0.20f, 0.24f, 0.34f, 1f);
            var ink = new Vector4(0.04f, 0.04f, 0.05f, 1f);

            void Layer(float grow, Vector4 palm, Vector4 sleeve)
            {
                jokeTris_S6.Clear();
                Vector3 W(float x, float y) => fit.Centre + (fit.Right * x + fit.Up * y) * s;
                Slab_T6(W, -0.15f, 0.15f, -0.50f, -0.30f, 0.05f + grow, grow, sleeve);
                Slab_T6(W, -0.27f, 0.27f, -0.34f, 0.04f, 0.10f + grow, grow, palm);
                Slab_T6(W, -0.44f, -0.23f, -0.24f, -0.07f, 0.08f + grow, grow, palm);
                Slab_T6(W, -0.10f, 0.10f, -0.05f, 0.50f, 0.10f + grow, grow, palm);
                foreach (var t in jokeTris_S6) triRenderer.AddTri(t.A, t.B, t.C, t.CA, t.CB, t.CC);
                triRenderer.Flush(context, reflViewProj, CommonStates.BlendOpaque, CommonStates.DepthDisabled);
            }
            Layer(0.045f, ink, ink);
            Layer(0f, skin, cuff);
        }

        private void Slab_T6(Func<float, float, Vector3> W, float x0, float x1, float y0, float y1,
                             float radius, float grow, Vector4 col)
        {
            x0 -= grow; x1 += grow; y0 -= grow; y1 += grow;
            float r = Math.Min(radius, Math.Min((x1 - x0), (y1 - y0)) * 0.5f);
            const int corner = 5;
            var pts = new List<Vector3>((corner + 1) * 4);
            void Corner(float cx, float cy, double a0)
            {
                for (int i = 0; i <= corner; i++)
                {
                    double a = a0 + Math.PI * 0.5 * i / corner;
                    pts.Add(W(cx + (float)Math.Cos(a) * r, cy + (float)Math.Sin(a) * r));
                }
            }
            Corner(x1 - r, y1 - r, 0);
            Corner(x0 + r, y1 - r, Math.PI * 0.5);
            Corner(x0 + r, y0 + r, Math.PI);
            Corner(x1 - r, y0 + r, Math.PI * 1.5);

            var mid = W((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                jokeTris_S6.Add((mid, a, b, col, col, col));
                jokeTris_S6.Add((mid, b, a, col, col, col));
            }
        }
    }
}

