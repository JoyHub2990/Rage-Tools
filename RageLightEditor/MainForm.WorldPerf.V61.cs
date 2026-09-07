using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public static readonly float LodLightRange_V61 =
            float.TryParse(Environment.GetEnvironmentVariable("RLE_LODLIGHTRANGE"),
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0
                ? r : 450.0f;
    }
}
