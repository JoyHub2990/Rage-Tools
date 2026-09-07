using System;
using System.Collections.Generic;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class TriRenderer
    {
        public void AddConvexPolyAA(IList<Vector3> pts, float feather, Vector4 col)
        {
            if (pts == null || pts.Count < 3) return;
            int n = pts.Count;
            for (int i = 1; i + 1 < n; i++) AddTri(pts[0], pts[i], pts[i + 1], col);
            if (feather <= 0f) return;
            var nrm = PolyNormal(pts);
            if (nrm == Vector3.Zero) return;
            var z = new Vector4(col.X, col.Y, col.Z, 0f);
            var centroid = Vector3.Zero;
            for (int i = 0; i < n; i++) centroid += pts[i];
            centroid /= n;
            for (int i = 0; i < n; i++)
            {
                var p0 = pts[i]; var p1 = pts[(i + 1) % n];
                var e = p1 - p0;
                if (e.LengthSquared() < 1e-14f) continue;
                var outw = Vector3.Cross(e, nrm); outw.Normalize();
                if (Vector3.Dot(outw, (p0 + p1) * 0.5f - centroid) < 0f) outw = -outw;
                var o = outw * feather;
                AddTri(p0, p1, p1 + o, col, col, z);
                AddTri(p0, p1 + o, p0 + o, col, z, z);
            }
        }

        public static Vector3[] OffsetConvexPoly(IList<Vector3> pts, float d)
        {
            int n = pts.Count;
            var res = new Vector3[n];
            var nrm = PolyNormal(pts);
            if (nrm == Vector3.Zero || n < 3) { for (int i = 0; i < n; i++) res[i] = pts[i]; return res; }
            var centroid = Vector3.Zero;
            for (int i = 0; i < n; i++) centroid += pts[i];
            centroid /= n;
            Vector3 EdgeOut(Vector3 a, Vector3 b)
            {
                var e = b - a;
                if (e.LengthSquared() < 1e-14f) return Vector3.Zero;
                var o = Vector3.Cross(e, nrm); o.Normalize();
                if (Vector3.Dot(o, (a + b) * 0.5f - centroid) < 0f) o = -o;
                return o;
            }
            for (int i = 0; i < n; i++)
            {
                var prev = pts[(i - 1 + n) % n]; var p = pts[i]; var next = pts[(i + 1) % n];
                var o0 = EdgeOut(prev, p); var o1 = EdgeOut(p, next);
                var m = o0 + o1;
                if (m.LengthSquared() < 1e-12f) m = o1 == Vector3.Zero ? o0 : o1;
                if (m.LengthSquared() < 1e-12f) { res[i] = p; continue; }
                m.Normalize();
                float k = Math.Max(Vector3.Dot(m, o0 == Vector3.Zero ? o1 : o0), 0.35f);
                res[i] = p + m * (d / k);
            }
            return res;
        }

        private static Vector3 PolyNormal(IList<Vector3> pts)
        {
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n]; var c = pts[(i + 2) % n];
                var nrm = Vector3.Cross(b - a, c - a);
                if (nrm.LengthSquared() > 1e-16f) { nrm.Normalize(); return nrm; }
            }
            return Vector3.Zero;
        }
    }
}

