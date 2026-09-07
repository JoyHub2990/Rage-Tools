using System;
using SharpDX.Direct3D11;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private DistantLightsRenderer_V47 distantLights_V47;
        private ShaderResourceView distantLightSrv_V47;
        private ShaderResourceView coronaSrv_V48;

        private void ResolveDistantLightTex_V47()
        {
            if (gameFiles?.Cache == null || !gameFiles.Ready || textureLoader == null) return;
            if (distantLightSrv_V47 != null && coronaSrv_V48 != null) return;
            try
            {
                var ytd = gameFiles.Cache.GetYtd(3154743001u);
                if (ytd == null || !ytd.Loaded || ytd.TextureDict?.Dict == null) return;
                if (distantLightSrv_V47 == null &&
                    ytd.TextureDict.Dict.TryGetValue(2236244673u, out var tex) && tex != null)
                {
                    distantLightSrv_V47 = textureLoader.GetSRV(tex);
                    if (distantLightSrv_V47 != null) Console.WriteLine("DISTLIGHTS distant_light texture resolved");
                }
                if (coronaSrv_V48 == null)
                {
                    var dict = ytd.TextureDict.Dict;
                    if ((dict.TryGetValue(CodeWalker.GameFiles.JenkHash.GenHash("corona"), out var ct) ||
                         dict.TryGetValue(CodeWalker.GameFiles.JenkHash.GenHash("corona_c"), out ct)) && ct != null)
                    {
                        coronaSrv_V48 = textureLoader.GetSRV(ct);
                        if (coronaSrv_V48 != null) Console.WriteLine("DISTLIGHTS corona texture resolved: " + ct.Name);
                    }
                }
            }
            catch { }
        }

        private void DrawDistantLights_V47(DeviceContext context)
        {
            if (distantLights_V47 == null || panel == null || World == null) return;
            if (!panel.WorldMode || !panel.ShowDistantLights_V47 || panel.RenderMode == 8) return;
            float fade = DistantLightsRenderer_V47.NightFade_V47(panel.PreviewHour);
            if (fade <= 0.001f) return;
            ResolveDistantLightTex_V47();
            float pixelScale = 2.0f * (float)Math.Tan(camera.FieldOfView * 0.5) /
                               Math.Max(camera.ViewportHeight, 1.0f);
            distantLights_V47.Draw(context, World.ResidentYmaps, camera.ViewProjMatrix, camera.Position,
                                   distantLightSrv_V47, fade, pixelScale);
        }
    }
}
