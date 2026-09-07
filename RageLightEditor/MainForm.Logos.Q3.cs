namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void LoadWorkspaceLogos_Q3();

        partial void LoadWorkspaceLogos_Q3()
        {
            var rpfSrv = textureLoader.LoadEmbeddedPng("rpf_logo.png", out int rw, out int rh);
            if (rpfSrv != null)
            {
                panel.RpfLogoTexture = imguiRenderer.RegisterTexture(rpfSrv);
                panel.RpfLogoWidth = rw;
                panel.RpfLogoHeight = rh;
            }
            var ptfxSrv = textureLoader.LoadEmbeddedPng("particles_logo.png", out int pw, out int ph);
            if (ptfxSrv != null)
            {
                panel.ParticlesLogoTexture = imguiRenderer.RegisterTexture(ptfxSrv);
                panel.ParticlesLogoWidth = pw;
                panel.ParticlesLogoHeight = ph;
            }
            var navSrv = textureLoader.LoadEmbeddedPng("navmesh_logo.png", out int nw, out int nh);
            if (navSrv != null)
            {
                panel.NavLogoTexture = imguiRenderer.RegisterTexture(navSrv);
                panel.NavLogoWidth = nw;
                panel.NavLogoHeight = nh;
            }
            var terrSrv = textureLoader.LoadEmbeddedPng("terrain_logo.png", out int tw, out int th);
            if (terrSrv != null)
            {
                panel.TerrainLogoTexture = imguiRenderer.RegisterTexture(terrSrv);
                panel.TerrainLogoWidth = tw;
                panel.TerrainLogoHeight = th;
            }
        }
    }
}

