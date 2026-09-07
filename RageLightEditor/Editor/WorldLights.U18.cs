using System;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldLights
    {
        private static ResourceSimpleList64<LightAttributes> LightList_U18(DrawableBase db, bool create)
        {
            if (db is Drawable dd)
            {
                if (dd.LightAttributes == null && create) dd.LightAttributes = new ResourceSimpleList64<LightAttributes>();
                return dd.LightAttributes;
            }
            if (db is FragDrawable fd && fd.OwnerFragment != null)
            {
                if (fd.OwnerFragment.LightAttributes == null && create) fd.OwnerFragment.LightAttributes = new ResourceSimpleList64<LightAttributes>();
                return fd.OwnerFragment.LightAttributes;
            }
            return null;
        }

        public static LightAttributes NewLight_U18(DrawableBase db, byte type)
        {
            var centre = db?.BoundingCenter ?? Vector3.Zero;
            float top = db != null ? db.BoundingBoxMax.Z : 0.0f;
            if (float.IsNaN(top) || float.IsInfinity(top)) top = centre.Z;
            return new LightAttributes
            {
                Position = new Vector3(centre.X, centre.Y, top + 0.15f),
                ColorR = 255, ColorG = 255, ColorB = 255,
                Intensity = 5.0f,
                Falloff = 8.0f,
                FalloffExponent = 32.0f,
                Type = (LightType)type,
                Direction = new Vector3(0, 0, -1),
                Tangent = new Vector3(-1, 0, 0),
                ConeInnerAngle = type == 2 ? 10.0f : 0.0f,
                ConeOuterAngle = type == 2 ? 35.0f : 0.0f,
                Extent = new Vector3(1, 1, 1),
                CoronaSize = 0.0f,
                CoronaIntensity = 1.0f,
                CoronaZBias = 0.1f,
                ShadowNearClip = 0.05f,
                VolumeIntensity = 1.0f,
                VolumeSizeScale = 1.0f,
                VolumeOuterColorR = 255, VolumeOuterColorG = 255, VolumeOuterColorB = 255,
                VolumeOuterIntensity = 1.0f,
                VolumeOuterExponent = 1.0f,
                TimeFlags = Scene.AllHoursTimeFlags,
            };
        }

        public static int AppendLight_U18(DrawableBase db, LightAttributes la)
        {
            var list = LightList_U18(db, true);
            if (list == null || la == null) return -1;
            var old = list.data_items ?? Array.Empty<LightAttributes>();
            var arr = new LightAttributes[old.Length + 1];
            Array.Copy(old, arr, old.Length);
            arr[old.Length] = la;
            list.data_items = arr;
            return old.Length;
        }

        public static int InsertLightAt_U18(DrawableBase db, LightAttributes la, int index)
        {
            var list = LightList_U18(db, true);
            if (list == null || la == null) return -1;
            var old = list.data_items ?? Array.Empty<LightAttributes>();
            index = Math.Clamp(index, 0, old.Length);
            var arr = new LightAttributes[old.Length + 1];
            Array.Copy(old, 0, arr, 0, index);
            arr[index] = la;
            Array.Copy(old, index, arr, index + 1, old.Length - index);
            list.data_items = arr;
            return index;
        }

        public static bool RemoveLight_U18(DrawableBase db, LightAttributes la)
        {
            var list = LightList_U18(db, false);
            var old = list?.data_items;
            if (old == null || la == null) return false;
            int at = Array.IndexOf(old, la);
            if (at < 0) return false;
            var arr = new LightAttributes[old.Length - 1];
            Array.Copy(old, 0, arr, 0, at);
            Array.Copy(old, at + 1, arr, at, old.Length - at - 1);
            list.data_items = arr;
            return true;
        }

        public static int LightCount_U18(DrawableBase db) => LightList_U18(db, false)?.data_items?.Length ?? 0;
    }
}
