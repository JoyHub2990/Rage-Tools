using System;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class Camera
    {
        public bool OrbitSubject_U3;

        public bool AnchorOnSubject_U3(Vector3 centre)
        {
            var toSubject = centre - Position;
            float len = toSubject.Length();
            if (!float.IsFinite(len) || len < 1e-4f) return false;
            float fwd = Vector3.Dot(toSubject, GetForward());
            float want = fwd > 0.05f ? fwd : len;
            want = MathUtil.Clamp(want, MinDistance, MaxDistance);
            if (Math.Abs(want - Distance) < 0.01f) return false;
            ReanchorPivot(want);
            return true;
        }
    }
}

