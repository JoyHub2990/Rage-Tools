using System;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        public static readonly bool LegacySunShadow_R5 = Environment.GetEnvironmentVariable("RLE_SUNLEGACY_R5") == "1";
    }
}

