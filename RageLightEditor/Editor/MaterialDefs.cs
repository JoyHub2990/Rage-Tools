using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public enum MatParamKind
    {
        Float,
        Float2,
        Colour,
        Colour4,
        Float4,
        ChannelMask,
        Raw,
    }

    public enum MatTexRole { Diffuse, Bump, Spec, Detail, Tint, Environment, Other }

    public enum MatTexSource { Missing, Embedded, LoadedYtd, Imported, GameArchive }

    public class MatParamInfo
    {
        public string Name;
        public uint Hash;
        public string Label;
        public string Tooltip;
        public MatParamKind Kind = MatParamKind.Raw;
        public float Min, Max = 1.0f;
        public bool Log;
        public string[] SubLabels;
        public Vector4 Default;
    }

    public class MatTexInfo
    {
        public string Name;
        public uint Hash;
        public MatTexRole Role;
        public string Label;
        public string Tooltip;
        public int Order;
    }

    public static partial class MaterialDefs
    {
        private static readonly Dictionary<uint, MatParamInfo> paramsByHash = new Dictionary<uint, MatParamInfo>();
        private static readonly Dictionary<uint, MatTexInfo> texByHash = new Dictionary<uint, MatTexInfo>();

        public static readonly List<MatParamInfo> KnownParams = new List<MatParamInfo>();

        public static readonly List<MatTexInfo> KnownTextures = new List<MatTexInfo>();

        static MaterialDefs()
        {
            AddTex(ShaderParamNames.DiffuseSampler, MatTexRole.Diffuse, 0, "Diffuse",
                "Base colour. Alpha drives the cutout/blend test in the alpha buckets.");
            AddTex(ShaderParamNames.BumpSampler, MatTexRole.Bump, 1, "Normal / bump",
                "Tangent-space normal map. Its strength is the bumpiness parameter.");
            AddTex(ShaderParamNames.SpecSampler, MatTexRole.Spec, 2, "Specular",
                "RAGE reads three separate channels here: intensity (picked by the spec mask),\n" +
                "exponent (scaled by specularFalloffMult) and fresnel.");
            AddTex(ShaderParamNames.DetailSampler, MatTexRole.Detail, 3, "Detail",
                "Close-up normal/darkening tile, scaled by detailSettings and faded by the\n" +
                "spec map's alpha. This is what keeps big surfaces from going flat up close.");
            AddTex(ShaderParamNames.TintPaletteSampler, MatTexRole.Tint, 4, "Tint palette",
                "Palette row lookup used by the _tnt shaders to recolour the diffuse.");
            AddTex(ShaderParamNames.EnvironmentSampler, MatTexRole.Environment, 5, "Environment",
                "Cube/sphere reflection source for the reflective presets.");
            AddTex(ShaderParamNames.PlateBgSampler, MatTexRole.Diffuse, 6, "Plate background",
                "Licence-plate background diffuse (vehicle plate shaders).");
            AddTex(ShaderParamNames.PlateBgBumpSampler, MatTexRole.Bump, 7, "Plate background bump",
                "Licence-plate background normal map.");

            AddParam(ShaderParamNames.bumpiness, "Bumpiness", MatParamKind.Float, 0f, 8f,
                new Vector4(1, 0, 0, 0),
                "Scales the normal map's XY before it's rebuilt into a world normal.\n" +
                "0 flattens the surface; above ~2 the lighting starts to look carved.");

            AddParam(ShaderParamNames.specularIntensityMult, "Specular intensity", MatParamKind.Float, 0f, 32f,
                new Vector4(1, 0, 0, 0),
                "Multiplies the intensity channel picked out of the spec map.\n" +
                "With no spec map the game feeds a constant 0.1, so this still does something.");

            AddParam(ShaderParamNames.specularFalloffMult, "Specular falloff", MatParamKind.Float, 1f, 1024f,
                new Vector4(32, 0, 0, 0), true,
                "Scales the spec map's exponent channel. Low = broad soft highlight,\n" +
                "high = tight sharp highlight. The game then remaps 0..500 -> 0..1500\n" +
                "and 501..512 -> 1500..8192, so the top of this range moves fast.");

            AddParam(ShaderParamNames.specularFalloffMultSpecMap, "Specular falloff (spec map)",
                MatParamKind.Float, 1f, 1024f, new Vector4(32, 0, 0, 0), true,
                "Same as specular falloff, declared by the presets that read it from the map.");

            AddParam(ShaderParamNames.specularFresnel, "Specular fresnel", MatParamKind.Float, 0f, 1f,
                new Vector4(0.96f, 0, 0, 0),
                "How much the highlight strengthens at grazing angles. Used when there is\n" +
                "no spec map; with one, the map's blue channel supplies it instead.");

            AddParam(ShaderParamNames.specularIntensityMultSpecMap, "Specular intensity (spec map)",
                MatParamKind.Float, 0f, 32f, new Vector4(1, 0, 0, 0),
                "Intensity multiplier for the presets that declare the spec-map variant.");

            AddParam(ShaderParamNames.specMapIntMask, "Spec map channel", MatParamKind.ChannelMask, 0f, 1f,
                new Vector4(1, 0, 0, 0),
                "Which channel of the specular texture carries intensity.\n" +
                "Most props use red; packed maps often put it in green or blue.");

            AddParam(ShaderParamNames.matDiffuseColor, "Diffuse colour", MatParamKind.Colour4,
                0f, 1f, new Vector4(1, 1, 1, 1),
                "Flat colour used when the material has no diffuse texture, and a tint\n" +
                "multiplier on the ones that do.");

            AddParam(ShaderParamNames.matDiffuseColor2, "Diffuse colour 2", MatParamKind.Colour4,
                0f, 1f, new Vector4(1, 1, 1, 1),
                "Second diffuse tint, used by the two-layer and terrain blend presets.");

            AddParam(ShaderParamNames.detailSettings, "Detail settings", MatParamKind.Float4,
                0f, 100f, new Vector4(0, 0, 24, 24),
                "Detail map controls. Intensity darkens the diffuse, bump intensity bends\n" +
                "the normal, and the two scales tile the detail texture across UV0.",
                new[] { "Intensity", "Bump intensity", "Scale U", "Scale V" });

            AddParam(ShaderParamNames.emissiveMultiplier, "Emissive multiplier", MatParamKind.Float,
                0f, 64f, new Vector4(1, 0, 0, 0),
                "How much unlit albedo the emissive presets add on top of the lighting.");

            AddParam(ShaderParamNames.wetnessMultiplier, "Wetness", MatParamKind.Float, 0f, 1f,
                new Vector4(1, 0, 0, 0),
                "How strongly the timecycle's wetness darkens and glosses this material.");

            AddParam(ShaderParamNames.reflectivePower, "Reflective power", MatParamKind.Float, 0f, 1f,
                new Vector4(0.5f, 0, 0, 0),
                "Strength of the environment reflection on the reflective presets.");

            AddParam(ShaderParamNames.envEffThickness, "Env-eff thickness", MatParamKind.Float, 0f, 0.02f,
                new Vector4(0.001f, 0, 0, 0),
                "Depth of the environment-effect layer (vehicle_*_enveff: the clear coat over the paint).");
            AddParam(ShaderParamNames.envEffScale, "Env-eff scale", MatParamKind.Float, 0f, 4f,
                new Vector4(1, 0, 0, 0),
                "How far the environment-effect layer is scaled.");
            AddParam(ShaderParamNames.dirtLevelMod, "Dirt level", MatParamKind.Float, 0f, 1f,
                Vector4.Zero,
                "How much of the vehicle's dirt the game blends onto this material.");
            AddParam(ShaderParamNames.dirtColor, "Dirt colour", MatParamKind.Colour4, 0f, 1f,
                new Vector4(0.2f, 0.2f, 0.2f, 0),
                "The colour the dirt is blended in as.");
            AddParam(ShaderParamNames.NumLetters, "Plate letters", MatParamKind.Float, 0f, 16f,
                new Vector4(8, 0, 0, 0),
                "How many characters the licence plate holds.");
            AddParam(ShaderParamNames.LetterSize, "Plate letter size", MatParamKind.Float2, 0f, 1f,
                new Vector4(0.1f, 0.2f, 0, 0),
                "Width and height of one plate character in the font sheet.",
                new[] { "Width", "Height" });
            AddParam(ShaderParamNames.LetterIndex1, "Plate letters 1-4", MatParamKind.Raw, -1f, 255f,
                Vector4.Zero,
                "The first four characters, as indices into the plate font sheet.");
            AddParam(ShaderParamNames.LetterIndex2, "Plate letters 5-8", MatParamKind.Raw, -1f, 255f,
                Vector4.Zero,
                "The last four characters, as indices into the plate font sheet.");
            AddParam(ShaderParamNames.LicensePlateFontExtents, "Plate font extents", MatParamKind.Raw, -4f, 4f,
                new Vector4(0, 0, 1, 1),
                "The rectangle of the font sheet the plate reads its characters from.");
            AddParam(ShaderParamNames.LicensePlateFontTint, "Plate font tint", MatParamKind.Colour4, 0f, 1f,
                new Vector4(0, 0, 0, 1),
                "The colour the plate's characters are drawn in.");

            AddParam(ShaderParamNames.parallaxScaleBias, "Parallax scale/bias", MatParamKind.Float2,
                -1f, 1f, new Vector4(0.03f, 0, 0, 0),
                "Depth and offset of the parallax/POM presets' apparent displacement.",
                new[] { "Scale", "Bias" });

            AddParam(ShaderParamNames.HardAlphaBlend, "Hard alpha blend", MatParamKind.Float, 0f, 1f,
                new Vector4(1, 0, 0, 0),
                "Sharpens the alpha ramp on blended materials: 1 is nearly a cutout.");

            AddParam(ShaderParamNames.alphaTestValue, "Alpha test", MatParamKind.Float, 0f, 1f,
                new Vector4(0.5f, 0, 0, 0),
                "Cutout threshold: pixels below this alpha are discarded.");

            AddParam(ShaderParamNames.globalAnimUV0, "Anim UV - U row", MatParamKind.Float4,
                -8f, 8f, new Vector4(1, 0, 0, 0),
                "How the U coordinate is built: U' = u*ScaleU + v*ShearU + OffsetU.\n" +
                "Scrolls and tiles the texture across this material - move Offset U to\n" +
                "slide it sideways, raise Scale U to tile it more often.\n" +
                "Identity is (1, 0, 0): unscaled, unsheared, unshifted.",
                new[] { "Scale U", "Shear U", "Offset U", "unused" });

            AddParam(ShaderParamNames.globalAnimUV1, "Anim UV - V row", MatParamKind.Float4,
                -8f, 8f, new Vector4(0, 1, 0, 0),
                "How the V coordinate is built: V' = u*ShearV + v*ScaleV + OffsetV.\n" +
                "Identity is (0, 1, 0).",
                new[] { "Shear V", "Scale V", "Offset V", "unused" });

            AddParam(ShaderParamNames.DirtDecalMask, "Dirt decal mask", MatParamKind.Colour4,
                0f, 1f, new Vector4(1, 1, 1, 1),
                "Per-channel mask controlling where the dirt decal presets apply.");

            AddParam(ShaderParamNames.useTessellation, "Use tessellation", MatParamKind.Float, 0f, 1f,
                new Vector4(0, 0, 0, 0),
                "Preset flag. The editor's forward renderer doesn't tessellate, so this\n" +
                "is stored and saved but has no preview effect.");

            AddTex(ShaderParamNames.NoiseSampler, MatTexRole.Detail, 8, "Fur noise",
                "The strand mask. Tiled by Fur noise scale, it decides which hairs survive at each\n" +
                "height up the coat - the texture that makes fur read as hairs and not as fuzz.");
            AddTex(ShaderParamNames.TextureSamplerDiffPal, MatTexRole.Tint, 9, "Fur palette",
                "Palette row lookup for recolouring the coat, as the tint shaders do.");

            AddParam(ShaderParamNames.furLength, "Fur length (m)", MatParamKind.Float, 0f, 0.5f,
                new Vector4(0.15f, 0, 0, 0),
                "How far the coat stands off the skin, in metres. 0.15 on the game's cat.");
            AddParam(ShaderParamNames.furMaxLayers, "Fur layers (near)", MatParamKind.Float, 1f, 32f,
                new Vector4(15f, 0, 0, 0),
                "Shells drawn when the animal is close. Every shell is another pass over\n" +
                "the mesh, so this is the cost as well as the quality.");
            AddParam(ShaderParamNames.furMinLayers, "Fur layers (far)", MatParamKind.Float, 1f, 32f,
                new Vector4(2f, 0, 0, 0),
                "Shells drawn at a distance. The count fades between the two, which is\n" +
                "the LOD - it lives in the material rather than in the renderer.");
            AddParam(ShaderParamNames.furNoiseUVScale, "Fur noise scale", MatParamKind.Float, 0.1f, 64f,
                new Vector4(15f, 0, 0, 0),
                "How many times the noise tiles across the model's own UVs. Higher = finer hairs.");
            AddParam(ShaderParamNames.furAttenCoef, "Fur thinning", MatParamKind.Float2, -2f, 2f,
                new Vector4(1.21f, -0.22f, 0, 0),
                "How the coat thins from root to tip: coverage = x + y x height. The\n" +
                "negative y is what tapers it; a positive one would make it thicken upwards.");
            AddParam(ShaderParamNames.furSelfShadowMin, "Fur root shadow", MatParamKind.Float, 0f, 1f,
                new Vector4(0.45f, 0, 0, 0),
                "How much light reaches the SKIN under the coat. 0.45 = the root sits at\n" +
                "45% and the tip at full - what gives fur its depth rather than looking painted on.");
            AddParam(ShaderParamNames.furAOBlend, "Fur shading amount", MatParamKind.Float, 0f, 1f,
                new Vector4(1f, 0, 0, 0),
                "How much of that root-to-tip shading is applied. 0 turns it off entirely.");
            AddParam(ShaderParamNames.furStiffness, "Fur stiffness", MatParamKind.Float, 0f, 1f,
                new Vector4(0.5f, 0, 0, 0),
                "How much the coat resists the bend from Fur bend. 1 = rigid.");
            AddParam(ShaderParamNames.furBendParams, "Fur bend", MatParamKind.Float4, -1f, 1f,
                new Vector4(0, 0, 0, 0),
                "Gravity and wind lean on the shells. Zero on the game's own animals.",
                new[] { "x", "y", "z", "amount" });
            AddParam(ShaderParamNames.furGlobalParams, "Fur global", MatParamKind.Float4, -1f, 1f,
                new Vector4(0, 0, 0.0039f, 0),
                "The shader's own constants. 0.0039 is 1/256 - leave it unless you are\n" +
                "matching another coat exactly.",
                new[] { "x", "y", "z", "w" });
            AddParam(ShaderParamNames.StubbleControl, "Stubble", MatParamKind.Float2, 0f, 8f,
                new Vector4(2f, 0.6f, 0, 0),
                "Short-hair control for the ped shaders that use it: tile count and strength.");
            AddFurDefs_U20();
        }

        private static void AddTex(ShaderParamNames n, MatTexRole role, int order, string label, string tip)
        {
            var t = new MatTexInfo
            {
                Name = n.ToString(),
                Hash = (uint)n,
                Role = role,
                Label = label,
                Tooltip = tip,
                Order = order,
            };
            texByHash[t.Hash] = t;
            KnownTextures.Add(t);
        }

        private static void AddParam(ShaderParamNames n, string label, MatParamKind kind, float min, float max,
            Vector4 def, string tip, string[] subLabels = null)
            => AddParam(n, label, kind, min, max, def, false, tip, subLabels);

        private static void AddParam(ShaderParamNames n, string label, MatParamKind kind, float min, float max,
            Vector4 def, bool log, string tip, string[] subLabels = null)
        {
            var p = new MatParamInfo
            {
                Name = n.ToString(),
                Hash = (uint)n,
                Label = label,
                Tooltip = tip,
                Kind = kind,
                Min = min,
                Max = max,
                Log = log,
                Default = def,
                SubLabels = subLabels,
            };
            paramsByHash[p.Hash] = p;
            KnownParams.Add(p);
        }

        public static MatParamInfo Param(uint hash) =>
            paramsByHash.TryGetValue(hash, out var p) ? p : null;

        public static MatTexInfo Tex(uint hash) =>
            texByHash.TryGetValue(hash, out var t) ? t : null;

        public static string NameOf(uint hash)
        {
            if (Enum.IsDefined(typeof(ShaderParamNames), hash))
                return ((ShaderParamNames)hash).ToString();
            return "0x" + hash.ToString("X8");
        }

        public static MatParamInfo ParamOrRaw(uint hash)
        {
            var p = Param(hash);
            if (p != null) return p;
            return new MatParamInfo
            {
                Name = NameOf(hash),
                Hash = hash,
                Label = NameOf(hash),
                Kind = MatParamKind.Raw,
                Min = -100f,
                Max = 100f,
                Tooltip = "This preset declares the parameter but the editor has no special\n" +
                          "handling for it. Edits are stored and saved as a raw float4.",
            };
        }

        public static MatTexInfo TexOrOther(uint hash)
        {
            var t = Tex(hash);
            if (t != null) return t;
            return new MatTexInfo
            {
                Name = NameOf(hash),
                Hash = hash,
                Role = MatTexRole.Other,
                Label = NameOf(hash),
                Order = 100,
                Tooltip = "Texture slot the editor doesn't recognise. It can still be reassigned;\n" +
                          "it just isn't wired into the preview.",
            };
        }

        public static readonly string[] BucketNames =
        {
            "0 - opaque", "1 - alpha", "2 - decal", "3 - cutout",
            "4 - unused", "5 - unused", "6 - water", "7 - glass/alpha",
        };

        public static string BucketName(byte b) =>
            b < BucketNames.Length ? BucketNames[b] : b.ToString();
    }
}

