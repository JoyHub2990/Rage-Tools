namespace RageLightEditor.Rendering
{
    public static partial class CommonStates
    {
        public const float DecalBiasClampValue = 2.5e-5f;
        public static readonly float DecalBiasClamp =
            System.Environment.GetEnvironmentVariable("RLE_NODECALCLAMP") == "1" ? 0.0f : DecalBiasClampValue;
    }
}

