using System;
using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        public static float MirrorCullPad_U4 = 1.35f;

        public static readonly float MirrorClipFront_U4 =
            float.TryParse(Environment.GetEnvironmentVariable("RLE_MIRRORCLIP"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mcf) ? mcf : 0.02f;

        private readonly Matrix[] mirrorCullVP_U4 = new Matrix[MaxMirrorPasses];
        private int mirrorCullReady_U4;

        public int MirrorCullCount_U4 => Math.Min(heldPasses, mirrorCullReady_U4);

        public Matrix MirrorCullViewProj_U4(int i) => mirrorCullVP_U4[(uint)i < (uint)MaxMirrorPasses ? i : 0];

        private void NoteMirrorCullView_U4(int gi, Matrix reflView, Matrix proj, Vector4 rect)
        {
            if ((uint)gi >= (uint)MaxMirrorPasses) return;
            float hw = Math.Min(Math.Max(rect.Z, 1e-4f) * MirrorCullPad_U4, 1.0f);
            float hh = Math.Min(Math.Max(rect.W, 1e-4f) * MirrorCullPad_U4, 1.0f);
            var crop = new Matrix(
                1.0f / hw, 0, 0, 0,
                0, 1.0f / hh, 0, 0,
                0, 0, 1, 0,
                -rect.X / hw, -rect.Y / hh, 0, 1);
            mirrorCullVP_U4[gi] = reflView * (proj * crop);
            if (gi + 1 > mirrorCullReady_U4) mirrorCullReady_U4 = gi + 1;
        }
    }
}

