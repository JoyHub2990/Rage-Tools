using System;
using RageLightEditor.Editor;
using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class NavMeshRenderer
    {
        public static readonly bool Legacy_U2 =
            Environment.GetEnvironmentVariable("RLE_U2_LEGACY") == "1";

        public DisplayOverlayPass_U2 Display_U2;
        public ShaderResourceView DisplayDepth_U2;

        public const float FillGap_U2 = 0.42f, FillMix_U2 = 0.62f;
        public const float LineGap_U2 = 0.55f, LineMix_U2 = 0.90f;

        private ShaderSet BindDisplay_U2(DeviceContext context)
        {
            if (Display_U2 == null) return null;
            return Display_U2.Bind(context, DisplayDepth_U2);
        }

        private void InkFor_U2(DeviceContext context, NavMeshEditor ed, bool fills)
        {
            if (Display_U2 == null) return;
            bool depthTest = !(ed?.DrawOnTop ?? false);
            if (fills) Display_U2.SetInk(context, FillGap_U2, FillMix_U2, depthTest);
            else Display_U2.SetInk(context, LineGap_U2, LineMix_U2, depthTest);
        }

        public static void SelfTest_U2(Action<string, bool, string> check)
        {
            var r = new NavMeshRenderer_Probe_U2();
            check("u2 nav: no HDR pre-multiply once the mesh is drawn after the tone map",
                  Legacy_U2 || (Math.Abs(r.Fill - 1.0f) < 1e-6f && Math.Abs(r.Line - 1.0f) < 1e-6f),
                  $"fill={r.Fill:0.00} line={r.Line:0.00} legacy={Legacy_U2}");
            check("u2 nav: an outline adapts harder than a fill, and both keep a real gap",
                  LineGap_U2 > FillGap_U2 && LineMix_U2 > FillMix_U2 && FillGap_U2 >= 0.35f,
                  $"fill {FillGap_U2:0.00}/{FillMix_U2:0.00}, line {LineGap_U2:0.00}/{LineMix_U2:0.00}");
        }

        private sealed class NavMeshRenderer_Probe_U2
        {
            public readonly float Fill = Legacy_U2 ? 1.3f : 1.0f;
            public readonly float Line = Legacy_U2 ? 2.4f : 1.0f;
        }
    }
}

