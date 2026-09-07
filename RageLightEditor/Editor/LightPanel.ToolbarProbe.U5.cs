using System.Numerics;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public Vector2 SnapMinusMin_U5, SnapMinusMax_U5, SnapPlusMin_U5, SnapPlusMax_U5;

        public string SnapLabelDrawn_U5 = "";

        public bool SnapButtonRect_U5(bool plus, out Vector2 centre)
        {
            var mn = plus ? SnapPlusMin_U5 : SnapMinusMin_U5;
            var mx = plus ? SnapPlusMax_U5 : SnapMinusMax_U5;
            centre = (mn + mx) * 0.5f;
            return mx.X > mn.X && mx.Y > mn.Y;
        }
    }
}
