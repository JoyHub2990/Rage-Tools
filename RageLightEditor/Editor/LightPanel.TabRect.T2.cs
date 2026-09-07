using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private readonly Vector4[] tabRect_T2 = new Vector4[Enum.GetValues(typeof(Space)).Length];

        public Vector4 TabRect_T2(Space s) => tabRect_T2[(int)s];

        public Vector2 TabCentre_T2(Space s)
        {
            var r = tabRect_T2[(int)s];
            return new Vector2((r.X + r.Z) * 0.5f, (r.Y + r.W) * 0.5f);
        }

        public bool TabDrawn_T2(Space s)
        {
            var r = tabRect_T2[(int)s];
            return r.Z > r.X + 0.5f && r.W > r.Y + 0.5f;
        }

        partial void NoteWorkspaceTabRect_T2(Space space);
        partial void NoteWorkspaceTabRect_T2(Space space)
        {
            var a = ImGui.GetItemRectMin();
            var b = ImGui.GetItemRectMax();
            tabRect_T2[(int)space] = new Vector4(a.X, a.Y, b.X, b.Y);
        }
    }
}

