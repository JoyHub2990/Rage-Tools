using System;

namespace RageLightEditor.Rendering
{
    public partial class ModelRenderer
    {
        private static readonly bool NoShadowBake_P3 = Environment.GetEnvironmentVariable("RLE_NOSHADOWBAKE") == "1";

        public static int ShadowBakeDecals_P3;

        private static void ClassifyShadowBake_P3(RenderMesh mesh)
        {
            if (NoShadowBake_P3) return;
            if (mesh == null || mesh.AlphaMode != GeomAlphaMode.Decal || mesh.DecalKind != 1) return;
            if (!IsShadowBakeTexture_P3(mesh.DiffuseName)) return;
            mesh.DecalKind = 5;
            ShadowBakeDecals_P3++;
        }

        internal static bool IsShadowBakeTexture_P3(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return HasNamePart_P3(name, "shadow") || HasNamePart_P3(name, "shadows") ||
                   HasNamePart_P3(name, "shadowbake") || HasNamePart_P3(name, "aobake") ||
                   HasNamePart_P3(name, "bakedshadow") || HasNamePart_P3(name, "bakedao") ||
                   (HasNamePart_P3(name, "bake") && HasNamePart_P3(name, "ao"));
        }

        private static bool HasNamePart_P3(string name, string word)
        {
            int i = 0;
            while (true)
            {
                i = name.IndexOf(word, i, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                bool startOk = i == 0 || name[i - 1] == '_';
                int e = i + word.Length, len = name.Length, j = e;
                int k = j;
                if (k < len && name[k] == '_') k++;
                int d = k;
                while (d < len && name[d] >= '0' && name[d] <= '9') d++;
                if (d > k) j = d;
                else if (j < len && char.IsLetter(name[j]) && j + 1 == len) j++;
                bool endOk = j == len || name[j] == '_';
                if (startOk && endOk) return true;
                i = e;
            }
        }

        public static void ShadowBakeTest_P3(Action<string, bool, string> check)
        {
            check("p3 bake: the fire station's own bake texture is recognised",
                  IsShadowBakeTexture_P3("tl_v_office_shadowtl_v_office_shadow_a") ||
                  IsShadowBakeTexture_P3("tl_v_office_shadow"), "tl_v_office_shadow");
            check("p3 bake: the usual exporter spellings are recognised",
                  IsShadowBakeTexture_P3("prop_bench_shadow") && IsShadowBakeTexture_P3("floor_shadows_01") &&
                  IsShadowBakeTexture_P3("mlo_ao_bake") && IsShadowBakeTexture_P3("locker_shadow01"),
                  "shadow / shadows_01 / ao_bake / shadow01");
            check("p3 bake: a name that merely contains the letters is NOT a bake",
                  !IsShadowBakeTexture_P3("meshadowplant") && !IsShadowBakeTexture_P3("shadowboxer_d") &&
                  !IsShadowBakeTexture_P3("im_roadblends001"), "meshadowplant / shadowboxer / roadblends");
            var m = new RenderMesh { AlphaMode = GeomAlphaMode.Decal, DecalKind = 1, DiffuseName = "tl_v_office_shadow" };
            ClassifyShadowBake_P3(m);
            check("p3 bake: a baked shadow on the plain decal preset multiplies", m.DecalKind == 5, "kind " + m.DecalKind);
            var dirt = new RenderMesh { AlphaMode = GeomAlphaMode.Decal, DecalKind = 2, DiffuseName = "road_shadow" };
            ClassifyShadowBake_P3(dirt);
            check("p3 bake: a preset that already has its own maths is left alone", dirt.DecalKind == 2, "kind " + dirt.DecalKind);
            var sign = new RenderMesh { AlphaMode = GeomAlphaMode.Decal, DecalKind = 1, DiffuseName = "im_roadblends001" };
            ClassifyShadowBake_P3(sign);
            check("p3 bake: an ordinary decal keeps alpha blending", sign.DecalKind == 1, "kind " + sign.DecalKind);
        }
    }
}

