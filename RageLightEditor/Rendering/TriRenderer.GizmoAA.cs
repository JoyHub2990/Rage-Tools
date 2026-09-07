using System;
using System.Collections.Generic;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class TriRenderer
    {
        public void AddFanAA(Vector3 centre, Vector3 axisA, Vector3 axisB, float radius, float a0, float a1,
                             float feather, Vector4 centreCol, Vector4 rimCol, int segments = 32)
        {
            float span = a1 - a0;
            if (Math.Abs(span) < 1e-6f) return;
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(span) / (Math.PI * 2.0) * segments));
            float ri = Math.Max(radius - feather * 0.5f, 0f), ro = radius + feather * 0.5f;
            var z = new Vector4(rimCol.X, rimCol.Y, rimCol.Z, 0f);
            Vector3 P(float ang, float r) => centre + (axisA * (float)Math.Cos(ang) + axisB * (float)Math.Sin(ang)) * r;
            for (int i = 0; i < n; i++)
            {
                float t0 = a0 + span * i / n, t1 = a0 + span * (i + 1) / n;
                if (ri > 1e-7f) AddTri(centre, P(t0, ri), P(t1, ri), centreCol, rimCol, rimCol);
                AddTri(P(t0, ri), P(t0, ro), P(t1, ro), rimCol, z, z);
                AddTri(P(t0, ri), P(t1, ro), P(t1, ri), rimCol, z, rimCol);
            }
        }

        public void AddCapsuleAA(Vector3 a, Vector3 b, Vector3 camPos, float halfWidth, float feather, Vector4 col)
        {
            var d = b - a;
            if (d.LengthSquared() < 1e-12f) return;
            var mid = (a + b) * 0.5f;
            var side = SideVector(d, camPos - mid);
            var dir = Vector3.Normalize(d);
            AddRibbonAA(a, b, side, side, halfWidth, feather, col, col);
            const float Half = (float)(Math.PI * 0.5);
            AddFanAA(a, -dir, side, halfWidth, -Half, Half, feather, col, col, 28);
            AddFanAA(b, dir, side, halfWidth, -Half, Half, feather, col, col, 28);
        }

        public void AddThickPolylineAA(IList<Vector3> pts, Vector3 camPos, float halfWidth, float feather, Vector4 col, bool closed = false)
        {
            if (pts == null || pts.Count < 2) return;
            int n = pts.Count;
            int segs = closed ? n : n - 1;
            Vector3 prevP = Vector3.Zero, prevS = Vector3.Zero;
            for (int i = 0; i <= segs; i++)
            {
                var p = pts[i % n];
                var before = pts[((i - 1) % n + n) % n];
                var after = pts[(i + 1) % n];
                Vector3 sideV;
                bool corner = closed || (i > 0 && i < n - 1);
                if (corner)
                {
                    var d0 = p - before; var d1 = after - p;
                    if (d0.LengthSquared() > 1e-14f) d0.Normalize();
                    if (d1.LengthSquared() > 1e-14f) d1.Normalize();
                    var t = d0 + d1;
                    if (t.LengthSquared() < 1e-10f) t = d1;
                    sideV = SideVector(t, camPos - p);
                    var side0 = SideVector(d0.LengthSquared() > 1e-14f ? d0 : d1, camPos - p);
                    float k = Math.Abs(Vector3.Dot(sideV, side0));
                    sideV *= 1f / Math.Max(k, 0.5f);
                }
                else
                {
                    var tangent = i == 0 ? after - p : p - before;
                    sideV = SideVector(tangent, camPos - p);
                }
                if (i > 0) AddRibbonAA(prevP, p, prevS, sideV, halfWidth, feather, col, col);
                prevP = p; prevS = sideV;
            }
        }

        public void AddConeLit(Vector3 apex, Vector3 baseCentre, Vector3 axisA, Vector3 axisB, float radius,
            Vector4 col, Vector3 lightDir, Vector3 viewDir, float ambient = 0.42f, float specular = 0.45f, int segments = 32)
        {
            var axis = apex - baseCentre;
            float h = axis.Length();
            if (h < 1e-9f) return;
            var n = axis / h;
            Vector3 prev = baseCentre + axisA * radius;
            Vector4 prevC = col;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)(i * Math.PI * 2.0 / segments);
                var radial = axisA * (float)Math.Cos(t) + axisB * (float)Math.Sin(t);
                Vector3 p = baseCentre + radial * radius;
                var fn = Vector3.Normalize(radial * h + n * radius);
                var c = LitColour(col, fn, lightDir, viewDir, ambient, specular);
                if (i > 0)
                {
                    var ac = (c + prevC) * 0.5f;
                    AddTri(apex, prev, p, ac, prevC, c);
                }
                prev = p; prevC = c;
            }
            var bc = LitColour(col, -n, lightDir, viewDir, ambient, specular);
            AddDisc(baseCentre, axisA, axisB, radius, bc, bc, segments);
        }

        public static Vector4 LitColour(Vector4 col, Vector3 nrm, Vector3 lightDir, Vector3 viewDir, float ambient, float specular)
        {
            float diff = Math.Max(0f, Vector3.Dot(nrm, lightDir));
            float k = ambient + (1f - ambient) * diff;
            var half = lightDir + viewDir;
            float spec = 0f;
            if (half.LengthSquared() > 1e-8f)
            {
                half.Normalize();
                float nh = Math.Max(0f, Vector3.Dot(nrm, half));
                spec = (float)Math.Pow(nh, 24.0) * specular;
            }
            return new Vector4(Math.Min(1f, col.X * k + spec), Math.Min(1f, col.Y * k + spec), Math.Min(1f, col.Z * k + spec), col.W);
        }
    }
}

