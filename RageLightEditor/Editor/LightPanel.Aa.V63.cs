using System;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool AlphaToCoverage_V63 =
            Environment.GetEnvironmentVariable("RLE_A2C") != "0";
    }
}
