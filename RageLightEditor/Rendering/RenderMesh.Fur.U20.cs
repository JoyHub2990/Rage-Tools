using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class RenderMesh
    {
        public bool IsFurMask;
        public bool FurHfSrgbView;
        public ShaderResourceView FurMaskSRV;
        public ShaderResourceView FurHfSRV;
    }
}
