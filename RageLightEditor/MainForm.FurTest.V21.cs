using System;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_Fur_V21(Action<string, bool, string> check)
        {
            var m = new RenderMesh
            {
                IsFur = true,
                FurLayers = 8,
                FurShadow03 = new Vector4(0.352941f, 0.462745f, 0.580392f, 0.658824f),
                FurShadow47 = new Vector4(0.784314f, 0.862745f, 0.933333f, 1.0f),
                FurAlphaClip03 = new Vector4(0f, 0.039216f, 0.039216f, 0.058824f),
                FurAlphaClip47 = new Vector4(0.078431f, 0.098039f, 0.117647f, 0.133333f),
                FurAlphaDistance = new Vector2(15f, 25f),
                FurLayerParams = new Vector4(0.012f, 0.001f, 0.001f, 0.7f),
            };

            var clips = new float[8];
            var shades = new float[8];
            for (int i = 0; i < 8; i++) ModelRenderer.FurLayer_V21(m, i, out clips[i], out shades[i]);

            check("v21 fur: layers 4-7 come from the ...47 row, not a second read of ...03",
                  Math.Abs(clips[4] - 0.078431f) < 1e-4 && Math.Abs(shades[4] - 0.784314f) < 1e-4,
                  $"layer 4 clip {clips[4]:0.###} shadow {shades[4]:0.###}");

            bool clipRises = true, shadeRises = true;
            for (int i = 1; i < 8; i++)
            {
                if (clips[i] < clips[i - 1]) clipRises = false;
                if (shades[i] < shades[i - 1]) shadeRises = false;
            }
            check("v21 fur: the alpha clip rises with height, so the pile tapers instead of stacking sheets",
                  clipRises, string.Join(" ", Array.ConvertAll(clips, c => c.ToString("0.###"))));
            check("v21 fur: the self-shadow rises with height - the root is in shade, the tip in the sun",
                  shadeRises && shades[0] < 0.5f && shades[7] > 0.95f,
                  string.Join(" ", Array.ConvertAll(shades, c => c.ToString("0.##"))));

            m.WorldSphere = new BoundingSphere(Vector3.Zero, 0f);
            float Near = FurFadeAt_V21(m, 5f), Mid = FurFadeAt_V21(m, 20f), Far = FurFadeAt_V21(m, 30f);
            check("v21 fur: full strength inside furalphadistance.x", Math.Abs(Near - 1f) < 1e-3, Near.ToString("0.##"));
            check("v21 fur: half way out it is half gone, not on or off",
                  Mid > 0.4f && Mid < 0.6f, Mid.ToString("0.##"));
            check("v21 fur: past furalphadistance.y no shells are drawn at all - this is what makes it affordable",
                  Far == 0f, Far.ToString("0.##"));

            var noComb = new RenderMesh { IsFur = false, FurLayerParams = new Vector4(0.012f, 0, 0, 0) };
            noComb.WorldSphere = new BoundingSphere(Vector3.Zero, 0f);
            check("v21 fur: a material whose comb did not resolve stays the flat surface it was",
                  FurFadeAt_V21(noComb, 1f) == 0f, "no shells");

            check("v21 fur: the grass_fur family is recognised and nothing else is",
                  ModelRenderer.IsFurShader_V21("grass_fur") &&
                  ModelRenderer.IsFurShader_V21("grass_fur_mask") &&
                  ModelRenderer.IsFurShader_V21("grass_fur_lod") &&
                  !ModelRenderer.IsFurShader_V21("grass_batch") &&
                  !ModelRenderer.IsFurShader_V21("terrain_cb_4lyr"),
                  "grass_fur* yes, grass_batch / terrain no");
        }

        private float FurFadeAt_V21(RenderMesh m, float distance)
        {
            var was = m.WorldSphere;
            m.WorldSphere = new BoundingSphere(new Vector3(distance, 0, 0), 0f);
            float f = sceneRenderer.FurFadeForTest_V21(m, Vector3.Zero);
            m.WorldSphere = was;
            return f;
        }
    }
}

