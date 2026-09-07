using System;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_U4(Action<string, bool, string> check)
        {
            var glass = new SDX.BoundingBox(new SDX.Vector3(-0.6f, -0.02f, 0.9f), new SDX.Vector3(0.6f, 0.02f, 2.1f));
            var n = new SDX.Vector3(0, -1, 0);
            const float pd = 0.0f;
            var eye = new SDX.Vector3(0, -1.4f, 1.6f);
            var fwd = new SDX.Vector3(0, 1, 0);
            var right = new SDX.Vector3(1, 0, 0);
            var up = new SDX.Vector3(0, 0, 1);

            var fit = JokeFitCamera_U4(glass, eye, fwd, right, up, n, pd);

            var off = fit.Centre - eye;
            float ahead = SDX.Vector3.Dot(off, fwd);
            float sideways = Math.Abs(SDX.Vector3.Dot(off, right));
            check("mirror joke: the hand hangs at a fixed offset in front of the camera",
                  Math.Abs(ahead - JokeStandoff_U4) < 0.02f && sideways < 0.02f &&
                  fit.Right == right && fit.Up == up && fit.Fwd == fwd,
                  $"{ahead:0.00} m ahead (standoff {JokeStandoff_U4:0.00}), {sideways:0.000} m sideways, " +
                  "and its own axes are the camera's - so it faces the viewer whichever way they turn");

            var img = MirrorPoint_U4(fit.Centre, n, pd);
            float tHit = 1.4f / (1.4f + (SDX.Vector3.Dot(n, fit.Centre) + pd));
            var hit = eye + (img - eye) * tHit;
            bool inGlass = Math.Abs(hit.X) <= 0.6f && hit.Z >= 0.9f && hit.Z <= 2.1f;
            check("mirror joke: it reads as the viewer's own hand - its reflection is on their own line of sight",
                  inGlass && Math.Abs(hit.X) < 0.05f && Math.Abs(hit.Z - eye.Z) < 0.25f,
                  $"seen in the glass at ({hit.X:0.00}, {hit.Z:0.00}); the eye's own reflection is at " +
                  $"(0.00, {eye.Z:0.00}) - a hand held up in front of you appears next to your face, not on the frame");

            var wide = new SDX.BoundingBox(new SDX.Vector3(-1.6f, -0.02f, 0.9f), new SDX.Vector3(1.6f, 0.02f, 2.1f));
            var fit1 = JokeFitCamera_U4(wide, eye, fwd, right, up, n, pd);
            var eye2 = eye + right * 0.35f;
            var fit2 = JokeFitCamera_U4(wide, eye2, fwd, right, up, n, pd);
            float moved = (fit2.Centre - fit1.Centre).Length();
            var img2 = MirrorPoint_U4(fit2.Centre, n, pd);
            float t2 = 1.4f / (1.4f + (SDX.Vector3.Dot(n, fit2.Centre) + pd));
            var hit2 = eye2 + (img2 - eye2) * t2;
            check("mirror joke: it travels with the viewer instead of sitting on the glass",
                  Math.Abs(moved - 0.35f) < 0.02f && Math.Abs(hit2.X - eye2.X) < 0.05f &&
                  Math.Abs(hit2.X) <= 1.6f && hit2.Z >= 0.9f && hit2.Z <= 2.1f,
                  $"the eye stepped 0.35 m right along a 3.2 m mirror, the hand moved {moved:0.00} m and its " +
                  $"reflection went with it (0.00 -> {hit2.X:0.00}, the eye's reflection {eye.X:0.00} -> {eye2.X:0.00}) - " +
                  "WS-T6's stayed at the middle of the glass");

            var t6 = JokeFit_T6(glass, eye, n, 1.4f);
            float t6Look = (MirrorPoint_U4(t6.Centre, n, pd) - eye).Length();
            float myLook = (img - eye).Length();
            float t6Ang = t6.Height * 0.5f / t6Look, myAng = fit.Height * 0.5f / myLook;
            check("mirror joke: it is still the size WS-T6 fitted to the glass",
                  Math.Abs(myAng - t6Ang) < t6Ang * 0.02f && fit.Height > 0.2f,
                  $"{fit.Height:0.00} m at {myLook:0.00} m subtends the same angle as T6's {t6.Height:0.00} m at " +
                  $"{t6Look:0.00} m ({myAng:0.000} vs {t6Ang:0.000} rad) - moving it must not shrink it");

            var small = new SDX.BoundingBox(new SDX.Vector3(0.55f, -0.02f, 1.30f), new SDX.Vector3(0.95f, 0.02f, 1.90f));
            var fitS = JokeFitCamera_U4(small, eye, fwd, right, up, n, pd);
            var imgS = MirrorPoint_U4(fitS.Centre, n, pd);
            float tS = 1.4f / (1.4f + (SDX.Vector3.Dot(n, fitS.Centre) + pd));
            var hitS = eye + (imgS - eye) * tS;
            float halfS = fitS.Height * 0.5f * tS;
            check("mirror joke: a small mirror off to the side still gets the whole hand",
                  hitS.X >= 0.55f && hitS.X <= 0.95f && hitS.Z >= 1.30f && hitS.Z <= 1.90f,
                  $"a 0.4 x 0.6 m mirror at x 0.55..0.95: the hand's reflection lands at " +
                  $"({hitS.X:0.00}, {hitS.Z:0.00}), inside it; hand {fitS.Height:0.00} m " +
                  $"(half {halfS:0.00} m on the glass) - the nudge is capped at {JokeNudge_U4:0.00}x the mirror's " +
                  "distance, so it is still a hand of the viewer's own rather than a sticker on the frame");

            check("mirror pass: the reflection clips in FRONT of the glass, so nothing behind it can be drawn",
                  Rendering.SceneRenderer.MirrorClipFront_U4 > 0.0f &&
                  Rendering.SceneRenderer.MirrorClipFront_U4 < 0.10f,
                  $"{Rendering.SceneRenderer.MirrorClipFront_U4 * 100:0.#} cm in front (it was 1 cm BEHIND: the panel a " +
                  "mirror is inset into then sat nearer the reflected eye than the room, and the pre-pass hid everything)");

            {
                var view = SDX.Matrix.LookAtRH(eye, eye + fwd, SDX.Vector3.UnitZ);
                var proj = SDX.Matrix.PerspectiveFovRH(1.0f, 1.6f, 200.0f, 0.05f);
                var R = new SDX.Matrix(
                    1 - 2 * n.X * n.X, -2 * n.X * n.Y, -2 * n.X * n.Z, 0,
                    -2 * n.Y * n.X, 1 - 2 * n.Y * n.Y, -2 * n.Y * n.Z, 0,
                    -2 * n.Z * n.X, -2 * n.Z * n.Y, 1 - 2 * n.Z * n.Z, 0,
                    -2 * pd * n.X, -2 * pd * n.Y, -2 * pd * n.Z, 1);
                var camFr = new SDX.BoundingFrustum(view * proj);
                var reflFr = new SDX.BoundingFrustum((R * view) * proj);
                var behind = new SDX.BoundingSphere(eye - fwd * 3.0f + up * 0.2f, 0.8f);
                check("mirror pass: the reflected view holds what is behind the eye, which the camera's frustum drops",
                      !camFr.Intersects(ref behind) && reflFr.Intersects(ref behind),
                      $"a 0.8 m sphere 3 m behind the eye: camera frustum {camFr.Contains(ref behind)}, " +
                      "reflected view keeps it - so the world's draw list has to be built for BOTH " +
                      "(WorldRenderer.U4.cs), or the glass has nothing in it to reflect");
            }
        }
    }
}

