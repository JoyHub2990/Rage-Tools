using System;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public const float JokeStandoff_U4 = 0.45f;
        public const float JokeDrop_U4 = -0.10f;
        public const float JokeNudge_U4 = 1.0f;

        private static Vector3 MirrorPoint_U4(Vector3 p, Vector3 n, float d) => p - n * (2.0f * (Vector3.Dot(n, p) + d));

        public static JokePlacement_T6 JokeFitCamera_U4(BoundingBox mirror, Vector3 eye,
            Vector3 fwd, Vector3 right, Vector3 up, Vector3 planeNormal, float planeD,
            float aspect = JokeAspect_T6)
        {
            var n = planeNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(planeNormal) : Vector3.UnitY;
            float dp = Vector3.Dot(n, eye) + planeD;
            dp = Math.Clamp(dp, 0.05f, 100.0f);

            var t6 = JokeFit_T6(mirror, eye, n, dp);
            float t6Look = Math.Max((MirrorPoint_U4(t6.Centre, n, planeD) - eye).Length(), 0.05f);
            float halfAngle = 0.5f * t6.Height / t6Look;

            float standoff = Math.Max(Math.Min(JokeStandoff_U4, dp * 0.7f), 0.01f);
            var o = fwd * standoff + up * (JokeDrop_U4 * standoff);

            (Vector3 img, Vector3 v, float t, Vector3 hit) Trace(Vector3 off)
            {
                var p = eye + off;
                var q = MirrorPoint_U4(p, n, planeD);
                var vv = q - eye;
                float denom = dp + (Vector3.Dot(n, p) + planeD);
                float tt = Math.Abs(denom) > 1e-4f ? dp / denom : 0.5f;
                return (q, vv, tt, eye + vv * tt);
            }
            var tr = Trace(o);

            var gz = -n;
            var gu = Vector3.UnitZ - gz * Vector3.Dot(Vector3.UnitZ, gz);
            gu = gu.LengthSquared() > 1e-6f ? Vector3.Normalize(gu) : Vector3.UnitZ;
            var gr = Vector3.Normalize(Vector3.Cross(gu, gz));
            bool known = mirror.Maximum.X > mirror.Minimum.X;
            var gc = known ? (mirror.Minimum + mirror.Maximum) * 0.5f : eye + gz * dp;
            var ext = known ? (mirror.Maximum - mirror.Minimum) * 0.5f : new Vector3(0.35f);
            float hr = Math.Max(Math.Abs(ext.X * gr.X) + Math.Abs(ext.Y * gr.Y) + Math.Abs(ext.Z * gr.Z), 0.05f);
            float hu = Math.Max(Math.Abs(ext.X * gu.X) + Math.Abs(ext.Y * gu.Y) + Math.Abs(ext.Z * gu.Z), 0.05f);

            float glassDist = Math.Max(tr.t * tr.v.Length(), 0.05f);
            float fh = halfAngle * glassDist;
            float fw = fh * (aspect > 0.01f && aspect < 100f ? aspect : JokeAspect_T6);
            float shrink = Math.Min(Math.Min(hr / Math.Max(fw, 1e-4f), hu / Math.Max(fh, 1e-4f)), 1.0f);
            fh *= shrink; fw *= shrink; halfAngle *= shrink;

            var rel = tr.hit - gc;
            float lr = Vector3.Dot(rel, gr), lu = Vector3.Dot(rel, gu);
            float clr = Math.Clamp(lr, -Math.Max(hr - fw, 0f), Math.Max(hr - fw, 0f));
            float clu = Math.Clamp(lu, -Math.Max(hu - fh, 0f), Math.Max(hu - fh, 0f));
            var delta = gr * (clr - lr) + gu * (clu - lu);
            if (known && delta.LengthSquared() > 1e-8f)
            {
                var fix = delta / Math.Max(tr.t, 1e-3f);
                float len = fix.Length(), cap = JokeNudge_U4 * dp;
                if (len > cap) fix *= cap / len;
                o += fix;
                tr = Trace(o);
            }

            return new JokePlacement_T6
            {
                Centre = eye + o,
                Right = right,
                Up = up,
                Fwd = fwd,
                Height = Math.Clamp(2.0f * halfAngle * Math.Max(tr.v.Length(), 0.05f), 0.05f, 4.0f),
                Fill = t6.Fill,
            };
        }

        private JokePlacement_T6 JokeFitCamera_U4(BoundingBox mirror, Plane plane, float aspect = JokeAspect_T6)
        {
            var fwd = camera.GetForward();
            var right = camera.GetRight();
            var up = Vector3.Cross(right, fwd);
            up = up.LengthSquared() > 1e-6f ? Vector3.Normalize(up) : Vector3.UnitZ;
            return JokeFitCamera_U4(mirror, camera.Position, fwd, right, up, plane.Normal, plane.D, aspect);
        }
    }
}

