using System;
using ImGuiNET;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static partial class GizmoStyle
    {

        private const float MStrokePx = 4.0f;
        private const float MOutlinePx = 1.0f;
        private const float MHeadR = 0.085f, MHeadLen = 0.24f;
        private const float MCubeR = 0.08f, MCentreCubeR = 0.105f;
        private const float DimOther = 0.45f;
        private const float MRingPx = 3.6f, MViewRingPx = 2.0f;

        private static readonly Vector4 MShadow = new Vector4(0f, 0f, 0f, 0.32f);
        private static readonly Vector4 MOutline = new Vector4(0.02f, 0.02f, 0.03f, 0.80f);
        private static readonly Vector4 GlowCol = new Vector4(1.00f, 0.88f, 0.34f, 1f);
        private static readonly Vector4 White = new Vector4(1f, 1f, 1f, 1f);
        private static readonly (float ext, float feather, float alpha)[] GlowLayers =
        {
            (2.2f, 3.0f, 0.30f), (5.0f, 6.0f, 0.13f), (9.5f, 10.0f, 0.05f),
        };

        private static float letterRadius = 1.21f;

        private static Vector4 A(Vector4 c, float alpha) => new Vector4(c.X, c.Y, c.Z, alpha);
        private static Vector4 Lighten(Vector4 c, float k) => new Vector4(c.X + (1f - c.X) * k, c.Y + (1f - c.Y) * k, c.Z + (1f - c.Z) * k, c.W);
        private static Vector4 Darken(Vector4 c, float k) => new Vector4(c.X * (1f - k), c.Y * (1f - k), c.Z * (1f - k), c.W);
        private static Vector3 ViewDir(Camera cam, Vector3 p) { var v = cam.Position - p; return v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : -cam.GetForward(); }

        private static void GlowStroke(TriRenderer tr, Camera cam, Vector3 a, Vector3 b, float widthPx, float alphaMul)
        {
            float wpp = cam.WorldPerPixel((a + b) * 0.5f);
            foreach (var (ext, feather, alpha) in GlowLayers)
                tr.AddCapsuleAA(a, b, cam.Position, (widthPx * 0.5f + ext) * wpp, feather * wpp, Fade(A(GlowCol, alpha), alphaMul));
        }

        private static void GlowArc(TriRenderer tr, Camera cam, Vector3 centre, Vector3 axisA, Vector3 axisB, float radius,
                                    float a0, float a1, float widthPx, float alphaMul, int segments)
        {
            float wpp = cam.WorldPerPixel(centre);
            foreach (var (ext, feather, alpha) in GlowLayers)
                tr.AddThickArcAA(centre, axisA, axisB, radius, a0, a1, cam.Position, (widthPx * 0.5f + ext) * wpp, feather * wpp,
                    Fade(A(GlowCol, alpha), alphaMul), segments);
        }

        private static void StrokeModern(TriRenderer tr, Camera cam, Vector3 a, Vector3 b, Vector4 col, float alphaMul,
                                         float widthPx, bool glow)
        {
            var mid = (a + b) * 0.5f;
            float wpp = cam.WorldPerPixel(mid);
            float f = FeatherPx * wpp;
            var cp = cam.Position;
            var sh = PixelOffset(cam, mid, 1.0f, 1.4f);
            tr.AddCapsuleAA(a + sh, b + sh, cp, (widthPx * 0.5f + MOutlinePx) * wpp, 2.0f * wpp, Fade(MShadow, alphaMul));
            if (glow) GlowStroke(tr, cam, a, b, widthPx, alphaMul);
            tr.AddCapsuleAA(a, b, cp, (widthPx * 0.5f + MOutlinePx) * wpp, f, Fade(MOutline, alphaMul));
            tr.AddCapsuleAA(a, b, cp, widthPx * 0.5f * wpp, f, Fade(col, alphaMul));
            if (widthPx >= 3.0f)
            {
                var spine = A(Lighten(col, glow ? 0.55f : 0.38f), 0.55f);
                tr.AddCapsuleAA(a, b, cp, widthPx * 0.16f * wpp, widthPx * 0.5f * wpp, Fade(spine, alphaMul));
            }
        }

        private static void RingModern(TriRenderer tr, Camera cam, Vector3 centre, Vector3 axisA, Vector3 axisB, float radius,
                                       Vector4 col, float alphaMul, float widthPx, int segments, bool dimBack, bool glow)
        {
            float wpp = cam.WorldPerPixel(centre);
            float f = FeatherPx * wpp;
            var cp = cam.Position;
            var toCam = cp - centre;
            float toCamLen = Math.Max(toCam.Length(), 1e-6f);
            Func<float, float> back = ang =>
            {
                if (!dimBack) return 1f;
                var p = axisA * (float)Math.Cos(ang) + axisB * (float)Math.Sin(ang);
                float d = Vector3.Dot(p, toCam) / toCamLen;
                return 0.30f + 0.70f * Math.Clamp(d * 3.5f + 0.5f, 0f, 1f);
            };
            const float TwoPi = (float)(Math.PI * 2.0);
            var sh = PixelOffset(cam, centre, 1.0f, 1.4f);
            var shc = Fade(MShadow, alphaMul);
            tr.AddThickArcAA(centre + sh, axisA, axisB, radius, 0f, TwoPi, cp, (widthPx * 0.5f + MOutlinePx) * wpp, 2.0f * wpp,
                ang => Fade(shc, back(ang)), segments);
            if (glow) GlowArc(tr, cam, centre, axisA, axisB, radius, 0f, TwoPi, widthPx, alphaMul, segments);
            var oc = Fade(MOutline, alphaMul);
            var cc = Fade(col, alphaMul);
            tr.AddThickArcAA(centre, axisA, axisB, radius, 0f, TwoPi, cp, (widthPx * 0.5f + MOutlinePx) * wpp, f,
                ang => Fade(oc, back(ang)), segments);
            tr.AddThickArcAA(centre, axisA, axisB, radius, 0f, TwoPi, cp, widthPx * 0.5f * wpp, f,
                ang => { float k = back(ang); return Fade(Darken(cc, 0.22f * (1f - k)), k); }, segments);
            if (widthPx >= 3.0f)
            {
                var spine = A(Lighten(col, glow ? 0.5f : 0.32f), 0.5f);
                tr.AddThickArcAA(centre, axisA, axisB, radius, 0f, TwoPi, cp, widthPx * 0.16f * wpp, widthPx * 0.5f * wpp,
                    ang => Fade(spine, alphaMul * back(ang)), segments);
            }
        }

        private static void ArcModern(TriRenderer tr, Camera cam, Vector3 centre, Vector3 axisA, Vector3 axisB, float radius,
                                      float a0, float a1, Vector4 col, float alphaMul, float widthPx, bool glow)
        {
            float wpp = cam.WorldPerPixel(centre);
            float f = FeatherPx * wpp;
            if (glow) GlowArc(tr, cam, centre, axisA, axisB, radius, a0, a1, widthPx, alphaMul, 128);
            tr.AddThickArcAA(centre, axisA, axisB, radius, a0, a1, cam.Position, (widthPx * 0.5f + MOutlinePx) * wpp, f, Fade(MOutline, alphaMul), 128);
            tr.AddThickArcAA(centre, axisA, axisB, radius, a0, a1, cam.Position, widthPx * 0.5f * wpp, f, Fade(col, alphaMul), 128);
        }

        private static void ArrowHeadModern(TriRenderer tr, Camera cam, Vector3 tip, Vector3 baseCentre, Vector3 pa, Vector3 pb,
                                            float radius, Vector4 col, float alphaMul, bool hot)
        {
            float wpp = cam.WorldPerPixel(baseCentre);
            var axis = tip - baseCentre;
            float len = axis.Length();
            if (len < 1e-7f) return;
            var n = axis / len;
            var cp = cam.Position;

            var sh = PixelOffset(cam, baseCentre, 1.0f, 1.4f);
            var shc = Fade(MShadow, alphaMul);
            tr.AddCone(tip + sh, baseCentre + sh, pa, pb, radius + 1.0f * wpp, shc, shc, 24);
            tr.AddDisc(baseCentre + sh, pa, pb, radius + 1.0f * wpp, shc, shc, 24);
            if (hot)
            {
                foreach (var (ext, _, alpha) in GlowLayers)
                {
                    float g = ext * wpp;
                    var gc = Fade(A(GlowCol, alpha * 0.85f), alphaMul);
                    tr.AddCone(tip + n * (g * 1.6f), baseCentre - n * g, pa, pb, radius + g, gc, gc, 24);
                    tr.AddDisc(baseCentre - n * g, pa, pb, radius + g, gc, gc, 24);
                }
            }
            tr.AddConeLit(tip, baseCentre, pa, pb, radius, Fade(col, alphaMul), LightDir(cam), ViewDir(cam, baseCentre), hot ? 0.68f : 0.56f, hot ? 0.55f : 0.45f, 32);
            ConeSilhouette(tr, cam, tip, baseCentre, pa, pb, radius, col, alphaMul);
        }

        private static void ConeSilhouette(TriRenderer tr, Camera cam, Vector3 apex, Vector3 baseCentre, Vector3 pa, Vector3 pb,
                                           float r, Vector4 col, float alphaMul)
        {
            var cp = cam.Position;
            var axis = apex - baseCentre;
            float h = axis.Length();
            var n = axis / h;
            var v = cp - baseCentre;
            float dn = Vector3.Dot(n, v);
            float a = Vector3.Dot(pa, v), b = Vector3.Dot(pb, v);
            float R = (float)Math.Sqrt(a * a + b * b);
            float phi = (float)Math.Atan2(b, a);
            float k = r - (r / h) * dn;
            bool discVisible = dn < 0f;
            float wpp = cam.WorldPerPixel(baseCentre);
            float hw = 0.8f * wpp, f = FeatherPx * wpp;
            var oc = Fade(MOutline, alphaMul);
            var crease = Fade(A(Darken(col, 0.45f), 0.55f), alphaMul);
            const float TwoPi = (float)(Math.PI * 2.0);
            Vector3 P(float t) => baseCentre + (pa * (float)Math.Cos(t) + pb * (float)Math.Sin(t)) * r;

            float ratio = R > 1e-9f ? k / R : (k < 0f ? -2f : 2f);
            if (ratio > -1f && ratio < 1f)
            {
                float d = (float)Math.Acos(ratio);
                float t1 = phi - d, t2 = phi + d;
                tr.AddThickLineAA(apex, P(t1), cp, hw, f, oc);
                tr.AddThickLineAA(apex, P(t2), cp, hw, f, oc);
                if (!discVisible)
                    tr.AddThickArcAA(baseCentre, pa, pb, r, t1, t2, cp, hw, f, oc, 48);
                else
                {
                    tr.AddThickArcAA(baseCentre, pa, pb, r, t2, t1 + TwoPi, cp, hw, f, oc, 48);
                    tr.AddThickArcAA(baseCentre, pa, pb, r, t1, t2, cp, hw * 0.8f, f, crease, 48);
                }
            }
            else
            {
                tr.AddThickArcAA(baseCentre, pa, pb, r, 0f, TwoPi, cp, hw, f, oc, 48);
            }
        }

        private static void BoxFaces(TriRenderer tr, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float r, Func<Vector3, Vector4> colourOf)
        {
            var x = ax * r; var y = ay * r; var z = az * r;
            Vector3 P(int sx, int sy, int sz) => c + x * sx + y * sy + z * sz;
            tr.AddQuad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), colourOf(az));
            tr.AddQuad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), colourOf(-az));
            tr.AddQuad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), colourOf(-ay));
            tr.AddQuad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), colourOf(ay));
            tr.AddQuad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), colourOf(-ax));
            tr.AddQuad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), colourOf(ax));
        }

        private static void BoxSilhouette(TriRenderer tr, Camera cam, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float r,
                                          Vector4 col, float alphaMul)
        {
            var cp = cam.Position;
            float wpp = cam.WorldPerPixel(c);
            float hw = 0.8f * wpp, f = FeatherPx * wpp;
            var oc = Fade(MOutline, alphaMul);
            var crease = Fade(A(Darken(col, 0.45f), 0.55f), alphaMul);
            Vector3[] nrm = { ax, -ax, ay, -ay, az, -az };
            bool Front(int i) => Vector3.Dot(nrm[i], cp - (c + nrm[i] * r)) > 0f;
            for (int i = 0; i < 6; i++)
            {
                for (int j = i + 1; j < 6; j++)
                {
                    if (j / 2 == i / 2) continue;
                    int kAxis = 3 - i / 2 - j / 2;
                    var third = kAxis == 0 ? ax : (kAxis == 1 ? ay : az);
                    var mid = c + nrm[i] * r + nrm[j] * r;
                    bool fi = Front(i), fj = Front(j);
                    if (!fi && !fj) continue;
                    var e0 = mid - third * r; var e1 = mid + third * r;
                    if (fi != fj) tr.AddThickLineAA(e0, e1, cp, hw, f, oc);
                    else tr.AddThickLineAA(e0, e1, cp, hw * 0.8f, f, crease);
                }
            }
        }

        private static void CubeModern(TriRenderer tr, Camera cam, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float r,
                                       Vector4 col, float alphaMul, bool hot)
        {
            float wpp = cam.WorldPerPixel(c);
            var sh = PixelOffset(cam, c, 1.0f, 1.4f);
            var shc = Fade(MShadow, alphaMul);
            BoxFaces(tr, c + sh, ax, ay, az, r + 1.0f * wpp, _ => shc);
            if (hot)
            {
                foreach (var (ext, _, alpha) in GlowLayers)
                {
                    var gc = Fade(A(GlowCol, alpha * 0.85f), alphaMul);
                    BoxFaces(tr, c, ax, ay, az, r + ext * wpp, _ => gc);
                }
            }
            var L = LightDir(cam); var V = ViewDir(cam, c);
            var cc = Fade(col, alphaMul);
            BoxFaces(tr, c, ax, ay, az, r, nrm => TriRenderer.LitColour(cc, nrm, L, V, hot ? 0.66f : 0.54f, hot ? 0.55f : 0.45f));
            BoxSilhouette(tr, cam, c, ax, ay, az, r, col, alphaMul);
        }

        private static void PlaneHandleModern(TriRenderer tr, Camera cam, Vector3 pos, Vector3 a, Vector3 b, int normalAxis,
                                              float scale, bool hot, float alphaMul)
        {
            float o0 = scale * PlaneMin, o1 = scale * PlaneMax;
            Vector3 p00 = pos + a * o0 + b * o0, p10 = pos + a * o1 + b * o0, p11 = pos + a * o1 + b * o1, p01 = pos + a * o0 + b * o1;
            float wpp = cam.WorldPerPixel(p11);
            var nc = AxisColour(normalAxis, false);
            var fill = hot ? A(Hot, 0.52f) : A(nc, 0.30f);
            var sh = PixelOffset(cam, p11, 1.0f, 1.4f);
            tr.AddQuadAA(p00 + sh, p10 + sh, p11 + sh, p01 + sh, 2.0f * wpp, Fade(A(MShadow, 0.22f), alphaMul));
            if (hot)
            {
                tr.AddQuadAA(p00, p10, p11, p01, 9.0f * wpp, Fade(A(GlowCol, 0.07f), alphaMul));
                tr.AddQuadAA(p00, p10, p11, p01, 4.5f * wpp, Fade(A(GlowCol, 0.13f), alphaMul));
            }
            tr.AddQuadAA(p00, p10, p11, p01, FeatherPx * wpp, Fade(fill, alphaMul));
            float w = hot ? 2.2f : 1.6f;
            Vector3[] q = { p00, p10, p11, p01 };
            tr.AddThickPolylineAA(q, cam.Position, (w * 0.5f + 0.8f) * wpp, FeatherPx * wpp, Fade(MOutline, alphaMul), true);
            tr.AddThickPolylineAA(q, cam.Position, w * 0.5f * wpp, FeatherPx * wpp, Fade(hot ? Lighten(Hot, 0.25f) : Lighten(nc, 0.12f), alphaMul), true);
        }

        private static void CentreCircleModern(TriRenderer tr, Camera cam, Vector3 pos, float scale, bool hot, float alphaMul)
        {
            var (r, u, _) = ScreenBasis(cam);
            float rad = scale * CentreR;
            float wpp = cam.WorldPerPixel(pos);
            tr.AddDiscAA(pos, r, u, rad, FeatherPx * wpp, Fade(hot ? A(Hot, 0.38f) : A(White, 0.10f), alphaMul), 48);
            RingModern(tr, cam, pos, r, u, rad, hot ? Hot : Centre, alphaMul, hot ? 3.0f : 2.2f, 56, false, hot);
        }

        private static void ViewRotateRingModern(TriRenderer tr, Camera cam, Vector3 pos, float scale, bool hot, float alphaMul)
        {
            var (r, u, _) = ScreenBasis(cam);
            RingModern(tr, cam, pos, r, u, scale * ViewRingR, hot ? Hot : ViewRing, alphaMul, hot ? 3.0f : MViewRingPx, 160, false, hot);
        }

        private static void PieModern(TriRenderer tr, Camera cam, Vector3 centre, Vector3 refA, Vector3 refB, float radius,
                                      float angle, float alphaMul)
        {
            float wpp = cam.WorldPerPixel(centre);
            tr.AddFanAA(centre, refA, refB, radius, 0f, angle, FeatherPx * wpp,
                Fade(A(Hot, 0.16f), alphaMul), Fade(A(Hot, 0.50f), alphaMul), 160);
            var cur = refA * (float)Math.Cos(angle) + refB * (float)Math.Sin(angle);
            StrokeModern(tr, cam, centre, centre + refA * radius, A(White, 0.75f), alphaMul, 1.6f, false);
            StrokeModern(tr, cam, centre, centre + cur * radius, Hot, alphaMul, 2.4f, false);
            ArcModern(tr, cam, centre, refA, refB, radius, 0f, angle, Hot, alphaMul, 2.8f, true);
        }

        private static void DrawTranslateModern(TriRenderer tr, Camera cam, Vector3 pos, Vector3[] basis, float scale,
                                                int hp, int ap, bool dragging, float am)
        {
            letterRadius = 1.21f;
            if (dragging)
            {
                if (ap >= 0 && ap < 3) GuideLine(tr, cam, pos, basis[ap], AxisColour(ap, false), am, scale);
                else if (ap >= PartPlane && ap < PartPlane + 3)
                {
                    var (a, b, _) = PlaneAxes(basis, ap - PartPlane);
                    GuideLine(tr, cam, pos, a, AxisColour(PlaneAxisIndexA(ap - PartPlane), false), am, scale);
                    GuideLine(tr, cam, pos, b, AxisColour(PlaneAxisIndexB(ap - PartPlane), false), am, scale);
                }
            }
            float Alpha(bool live) => dragging && !live ? am * DimOther : am;

            for (int p = 0; p < 3; p++)
            {
                bool hot = hp == PartPlane + p || ap == PartPlane + p;
                var (a, b, _) = PlaneAxes(basis, p);
                PlaneHandleModern(tr, cam, pos, a, b, PlaneNormalIndex(p), scale, hot, Alpha(hot));
            }

            foreach (int i in DepthOrder(cam, pos, basis, scale))
            {
                bool hot = hp == i || ap == i;
                var col = AxisColour(i, hot);
                var axis = basis[i];
                var tip = pos + axis * scale;
                var coneBase = pos + axis * (scale * (1f - MHeadLen));
                float start = scale * (CentreR + 0.07f);
                float alpha = Alpha(hot);
                StrokeModern(tr, cam, pos + axis * start, coneBase, col, alpha, MStrokePx, hot);
                ArrowHeadModern(tr, cam, tip, coneBase, basis[(i + 1) % 3], basis[(i + 2) % 3], scale * MHeadR, col, alpha, hot);
            }

            bool chot = hp == PartCentre || ap == PartCentre;
            CentreCircleModern(tr, cam, pos, scale, chot, Alpha(chot));
        }

        private static void DrawRotateModern(TriRenderer tr, Camera cam, Vector3 pos, Vector3[] basis, float scale,
                                             int hp, int ap, bool dragging, float am, bool[] ringOn, bool viewRing,
                                             Vector3 dragAxis, Vector3 rotStartVec, float angle, float snapDeg)
        {
            var (sr, su, _) = ScreenBasis(cam);
            float wpp = cam.WorldPerPixel(pos);
            float Alpha(bool live) => dragging && !live ? am * DimOther : am;
            letterRadius = viewRing ? ViewRingR + 0.13f : 1.16f;

            for (int i = 0; i < 3; i++)
            {
                if (!ringOn[i]) continue;
                bool hot = hp == i || ap == i;
                if (hot && !dragging)
                    tr.AddDiscAA(pos, basis[(i + 1) % 3], basis[(i + 2) % 3], scale, 3f * wpp, Fade(A(Hot, 0.10f), am), 96);
            }

            if (viewRing)
            {
                bool vhot = hp == PartView || ap == PartView;
                ViewRotateRingModern(tr, cam, pos, scale, vhot, Alpha(vhot));
                if (vhot && snapDeg > 0.5f && dragging) SnapTicks(tr, cam, pos, sr, su, scale * ViewRingR, snapDeg, Hot, am);
            }

            for (int i = 0; i < 3; i++)
            {
                if (!ringOn[i]) continue;
                bool hot = hp == i || ap == i;
                var a = basis[(i + 1) % 3]; var b = basis[(i + 2) % 3];
                RingModern(tr, cam, pos, a, b, scale, AxisColour(i, hot), Alpha(hot), hot ? MRingPx + 0.6f : MRingPx, 128, true, hot);
                if (hot && snapDeg > 0.5f && dragging) SnapTicks(tr, cam, pos, a, b, scale, snapDeg, Hot, am);
            }

            if (dragging && ap >= 0 && rotStartVec != Vector3.Zero)
            {
                var refA = rotStartVec;
                var refB = Vector3.Cross(dragAxis, refA);
                float r = ap == PartView ? scale * ViewRingR : scale;
                PieModern(tr, cam, pos, refA, refB, r, angle, am);
            }
        }

        private static void DrawScaleModern(TriRenderer tr, Camera cam, Vector3 pos, Vector3[] basis, float scale,
                                            int hp, int ap, bool dragging, float am, bool lockXY, bool planes)
        {
            letterRadius = 1.21f;
            bool uniform = ap == PartCentre;
            bool xyHot = hp == 0 || hp == 1 || ap == 0 || ap == 1;
            bool zHot = hp == 2 || ap == 2;
            bool AxisHot(int i) => uniform || (lockXY ? (i < 2 ? xyHot : zHot) : (hp == i || ap == i));
            float Alpha(bool live) => dragging && !live ? am * DimOther : am;

            if (dragging && ap >= 0 && ap < 3)
            {
                if (lockXY && ap < 2) { GuideLine(tr, cam, pos, basis[0], X, am, scale); GuideLine(tr, cam, pos, basis[1], Y, am, scale); }
                else GuideLine(tr, cam, pos, basis[ap], AxisColour(ap, false), am, scale);
            }

            if (planes)
            {
                for (int p = 0; p < 3; p++)
                {
                    if (lockXY && p != 0) continue;
                    bool hot = hp == PartPlane + p || ap == PartPlane + p;
                    var (a, b, _) = PlaneAxes(basis, p);
                    PlaneHandleModern(tr, cam, pos, a, b, PlaneNormalIndex(p), scale, hot, Alpha(hot));
                }
            }

            foreach (int i in DepthOrder(cam, pos, basis, scale))
            {
                bool hot = AxisHot(i);
                var col = AxisColour(i, hot);
                var axis = basis[i];
                float alpha = Alpha(hot);
                StrokeModern(tr, cam, pos + axis * (scale * (MCentreCubeR + 0.06f)), pos + axis * (scale * (1f - MCubeR)), col, alpha, MStrokePx, hot);
                CubeModern(tr, cam, pos + axis * scale, basis[0], basis[1], basis[2], scale * MCubeR, col, alpha, hot);
            }

            bool chot = hp == PartCentre || ap == PartCentre;
            CubeModern(tr, cam, pos, basis[0], basis[1], basis[2], scale * MCentreCubeR, chot ? Hot : Centre, Alpha(chot), chot);
        }

        private static void AxisLettersModern(Camera cam, Vector3 pos, Vector3[] basis, float scale, int hp, int ap, float am, bool[] show)
        {
            if (!ScreenText || am < 0.99f) return;
            var dl = ImGui.GetBackgroundDrawList();
            var font = ImGui.GetFont();
            float baseSize = ImGui.GetFontSize();
            float fs = baseSize * 1.3f;
            string[] names = { "X", "Y", "Z" };
            uint dark = U32(new Vector4(0.02f, 0.02f, 0.03f, 0.92f));
            for (int i = 0; i < 3; i++)
            {
                if (show != null && !show[i]) continue;
                if (ap == i) continue;
                bool hot = hp == i || ap == i;
                if (!Project(cam, pos + basis[i] * (scale * letterRadius), out var p)) continue;
                var size = ImGui.CalcTextSize(names[i]) * (fs / baseSize);
                p.X -= size.X * 0.5f; p.Y -= size.Y * 0.5f;
                var col = hot ? Lighten(Hot, 0.2f) : AxisColour(i, false);
                for (int k = 0; k < 8; k++)
                {
                    double ang = k * Math.PI / 4.0;
                    var o = new System.Numerics.Vector2(p.X + (float)Math.Cos(ang) * 1.5f, p.Y + (float)Math.Sin(ang) * 1.5f);
                    dl.AddText(font, fs, o, dark, names[i]);
                    dl.AddText(font, fs, new System.Numerics.Vector2(o.X + 0.7f, o.Y), dark, names[i]);
                }
                dl.AddText(font, fs, p, U32(col), names[i]);
                dl.AddText(font, fs, new System.Numerics.Vector2(p.X + 0.7f, p.Y), U32(col), names[i]);
            }
        }
    }
}

