using System;
using System.Collections.Generic;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorSession
    {
        public int FlippedRooms_P2;

        public int NormaliseBoxes_P2()
        {
            int fixedCount = 0;
            for (int i = 0; i < Rooms.Count; i++)
            {
                var r = Rooms[i];
                var mn = Vector3.Min(r.Min, r.Max);
                var mx = Vector3.Max(r.Min, r.Max);
                if (mn != r.Min || mx != r.Max) { r.Min = mn; r.Max = mx; fixedCount++; }
            }
            var bmn = Vector3.Min(BBMin, BBMax);
            var bmx = Vector3.Max(BBMin, BBMax);
            if (bmn != BBMin || bmx != BBMax) { BBMin = bmn; BBMax = bmx; fixedCount++; }
            return fixedCount;
        }

        public bool LimboAuthored_P2;

        public void NormaliseSeededBoxes_P2()
        {
            FlippedRooms_P2 = NormaliseBoxes_P2();
            BBoxManual = BBMax.X > BBMin.X && BBMax.Y > BBMin.Y && BBMax.Z > BBMin.Z;
            LimboAuthored_P2 = Rooms.Count > 0 && Rooms[0].IsValid;
        }

        public bool TryGetRoomUnionBounds_P2(out BoundingBox bounds, float pad = 0.5f)
        {
            bounds = default;
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            bool any = false;
            for (int i = 1; i < Rooms.Count; i++)
            {
                var r = Rooms[i];
                if (!r.IsValid) continue;
                mn = Vector3.Min(mn, r.Min); mx = Vector3.Max(mx, r.Max); any = true;
            }
            if (!any) return false;
            bounds = new BoundingBox(mn - new Vector3(pad), mx + new Vector3(pad));
            return true;
        }
    }
}

