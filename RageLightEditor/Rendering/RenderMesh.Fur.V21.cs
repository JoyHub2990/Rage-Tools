using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class RenderMesh
    {
        public bool IsFur;

        public bool IsPedFur_V38;
        public int PedFurMinLayers_V38 = 2, PedFurMaxLayers_V38 = 15;
        public SharpDX.Vector4 PedFurAtten_V38 = new SharpDX.Vector4(1.21f, -0.22f, 0, 0);
        public float PedFurSelfShadowMin_V38 = 0.45f, PedFurAOBlend_V38 = 1.0f, PedFurStiffness_V38 = 0.5f;
        public SharpDX.Vector4 PedFurBend_V38;

        public int FurLayers = 8;

        public Vector4 FurLayerParams;

        public Vector4 FurAlphaClip03, FurAlphaClip47;

        public Vector4 FurShadow03, FurShadow47;

        public Vector2 FurAlphaDistance = new Vector2(15f, 25f);

        public Vector4 FurUvScales = new Vector4(1.25f, 1f, 0.5f, 1f);

        public readonly ShaderResourceView[] FurComboSRV = new ShaderResourceView[4];
    }
}

