using System;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void MarkerOcta_U18(Vector3 p, float r, Vector4 col)
        {
            var top = p + Vector3.UnitZ * r;
            var bot = p - Vector3.UnitZ * r;
            var e0 = p + Vector3.UnitX * r;
            var e1 = p + Vector3.UnitY * r;
            var e2 = p - Vector3.UnitX * r;
            var e3 = p - Vector3.UnitY * r;
            triRenderer.AddTri(top, e0, e1, col); triRenderer.AddTri(top, e1, e2, col);
            triRenderer.AddTri(top, e2, e3, col); triRenderer.AddTri(top, e3, e0, col);
            triRenderer.AddTri(bot, e1, e0, col); triRenderer.AddTri(bot, e2, e1, col);
            triRenderer.AddTri(bot, e3, e2, col); triRenderer.AddTri(bot, e0, e3, col);
        }

        private void DrawLightMarkers()
        {
            var fwd = camera.GetForward();
            if (fwd.LengthSquared() < 1e-6f) fwd = Vector3.UnitY;
            var camRight = Vector3.Cross(fwd, Vector3.UnitZ);
            if (camRight.LengthSquared() < 1e-6f) camRight = Vector3.UnitX;
            camRight.Normalize();
            var camUp = Vector3.Normalize(Vector3.Cross(camRight, fwd));
            for (int i = 0; i < scene.Lights.Count; i++)
            {
                var l = scene.Lights[i];
                var ownerFile = scene.OwnerFile(l);
                if (ownerFile != null && !ownerFile.Visible) continue;
                var inst = scene.GetInstance(l);
                var p = inst.WorldPosition;
                float r = Vector3.Distance(camera.Position, p) * 0.010f;
                bool sel = scene.IsSelected(i);
                var col = new Vector4(l.ColorR / 255.0f * 0.7f + 0.3f, l.ColorG / 255.0f * 0.7f + 0.3f,
                                      l.ColorB / 255.0f * 0.7f + 0.3f, sel ? 1.0f : 0.85f);
                var line = new Vector4(col.X, col.Y, col.Z, sel ? 1.0f : 0.7f);
                var dir = inst.WorldDirection.LengthSquared() > 1e-6f ? Vector3.Normalize(inst.WorldDirection) : -Vector3.UnitZ;
                var tan = inst.WorldTangent.LengthSquared() > 1e-6f ? Vector3.Normalize(inst.WorldTangent) : Vector3.UnitX;
                switch (l.Type)
                {
                    case LightType.Spot:
                        MarkerOcta_U18(p, r * 0.55f, col);
                        lineRenderer.AddCone(p, dir, tan, MathUtil.DegreesToRadians(Math.Clamp(l.ConeOuterAngle, 6.0f, 80.0f)), r * 3.2f, line);
                        break;
                    case LightType.Capsule:
                    {
                        float half = Math.Max(Math.Abs(l.Extent.X) * 0.5f, r);
                        var a = p - dir * half;
                        var b = p + dir * half;
                        MarkerOcta_U18(a, r * 0.45f, col);
                        MarkerOcta_U18(b, r * 0.45f, col);
                        lineRenderer.AddCapsule(a, b, r * 0.7f, line);
                        break;
                    }
                    default:
                        MarkerOcta_U18(p, r, col);
                        lineRenderer.AddCircle(p, camRight, camUp, r * 1.7f, line, 24);
                        for (int k = 0; k < 4; k++)
                        {
                            float ang = k * MathUtil.PiOverTwo + MathUtil.PiOverFour;
                            var d = camRight * (float)Math.Cos(ang) + camUp * (float)Math.Sin(ang);
                            lineRenderer.AddLine(p + d * r * 1.9f, p + d * r * 2.6f, line);
                        }
                        break;
                }
                if (sel) lineRenderer.AddSphere(p, r * 1.6f, new Vector4(1f, 1f, 1f, 0.9f), 16);
            }
        }
    }
}
