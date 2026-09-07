using System;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        public static readonly float InteriorAmbientFloor_O2 = ReadFloor_O2();

        public static readonly bool IgnoreVertexBake_O2 = Environment.GetEnvironmentVariable("RLE_NOVC") == "1";

        private static float ReadFloor_O2()
        {
            if (Environment.GetEnvironmentVariable("RLE_NOINTFLOOR") == "1") return 0.0f;
            var s = Environment.GetEnvironmentVariable("RLE_INTFLOOR");
            if (s != null && float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v) && v >= 0.0f && v <= 1.0f) return v;
            return 0.35f;
        }

        public static readonly bool BakeAdjustOff_O2 = Environment.GetEnvironmentVariable("RLE_NOBAKEADJ") == "1";

        private static Vector4 InteriorAmbParams_O2() =>
            new Vector4(InteriorAmbientFloor_O2, IgnoreVertexBake_O2 ? 1.0f : 0.0f, BakeAdjustOff_O2 ? 0.0f : 1.0f, 0.0f);
    }
}

