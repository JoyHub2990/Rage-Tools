using System;
using System.Numerics;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public IntPtr RpfLogoTexture = IntPtr.Zero;
        public int RpfLogoWidth, RpfLogoHeight;
        public IntPtr ParticlesLogoTexture = IntPtr.Zero;
        public int ParticlesLogoWidth, ParticlesLogoHeight;
        public IntPtr NavLogoTexture = IntPtr.Zero;
        public int NavLogoWidth, NavLogoHeight;

        partial void WorkspaceLogo_Q3(ref IntPtr tex, ref float w, ref float h);

        partial void WorkspaceLogo_Q3(ref IntPtr tex, ref float w, ref float h)
        {
            if (RpfMode && RpfLogoTexture != IntPtr.Zero)
            {
                tex = RpfLogoTexture; w = RpfLogoWidth; h = RpfLogoHeight;
            }
            else if (ParticlesMode && ParticlesLogoTexture != IntPtr.Zero)
            {
                tex = ParticlesLogoTexture; w = ParticlesLogoWidth; h = ParticlesLogoHeight;
            }
            else if (NavMode && NavLogoTexture != IntPtr.Zero)
            {
                tex = NavLogoTexture; w = NavLogoWidth; h = NavLogoHeight;
            }
        }

        public static Vector4 WorkspaceColour_Q3(Space space)
        {
            switch (space)
            {
                case Space.Material: return MaterialWorkspaceColour;
                case Space.Cinematic: return CineWorkspaceColour;
                case Space.Archive: return ArchiveWorkspaceColour;
                case Space.Mlo: return MloWorkspaceColour;
                case Space.World: return WorldWorkspaceColour;
                case Space.Particles: return ParticlesWorkspaceColour;
                case Space.NavMesh: return NavWorkspaceColour;
                case Space.Terrain: return TerrainWorkspaceColour;
                case Space.Animation: return AnimWorkspaceColour;
                case Space.Extension: return ExtWorkspaceColour;
                default: return LightWorkspaceColour;
            }
        }
    }
}

