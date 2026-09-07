using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public static partial class TerrainYdr
    {
        public static readonly (string Name, string Tip)[] Presets =
        {
            ("terrain_cb_w_4lyr", "The map's own ground shader: four layers blended by the painted vertex colours."),
            ("terrain_cb_4lyr", "The same blend without the wetness pass - drier, and one parameter less."),
            ("terrain_cb_w_4lyr_2tex", "Layer 0 tiles on UV0 and layers 1-3 on UV1, so the two sets can tile differently."),
            ("terrain_cb_w_4lyr_cm_tnt", "Blend from a baked mask texture (not the vertex colours), plus a tint palette."),
        };

        public static bool UsesMask(string preset) => preset != null && preset.Contains("_cm");
        public static bool UsesTint(string preset) => preset != null && preset.EndsWith("_tnt", StringComparison.OrdinalIgnoreCase);
        public static bool UsesTwoUvSets(string preset) => preset != null && preset.Contains("_2tex");

        private const uint DeclFlags = (1u << 0) | (1u << 3) | (1u << 4) | (1u << 5) | (1u << 6) | (1u << 7) | (1u << 14);
        private const int DeclStride = 12 + 12 + 4 + 4 + 8 + 8 + 16;

        private static VertexDeclaration BuildDeclaration()
        {
            return new VertexDeclaration
            {
                Types = VertexDeclarationTypes.GTAV1,
                Flags = DeclFlags,
                Stride = DeclStride,
                Count = 7,
            };
        }

        private static byte[] PackVertices(MeshVertex[] verts)
        {
            var bytes = new byte[verts.Length * DeclStride];
            for (int i = 0; i < verts.Length; i++)
            {
                int o = i * DeclStride;
                var v = verts[i];
                WriteF(bytes, o + 0, v.Position.X); WriteF(bytes, o + 4, v.Position.Y); WriteF(bytes, o + 8, v.Position.Z);
                WriteF(bytes, o + 12, v.Normal.X); WriteF(bytes, o + 16, v.Normal.Y); WriteF(bytes, o + 20, v.Normal.Z);
                WriteC(bytes, o + 24, v.Colour0);
                WriteC(bytes, o + 28, v.Colour1);
                WriteF(bytes, o + 32, v.UV0.X); WriteF(bytes, o + 36, v.UV0.Y);
                WriteF(bytes, o + 40, v.UV1.X); WriteF(bytes, o + 44, v.UV1.Y);
                WriteF(bytes, o + 48, v.Tangent.X); WriteF(bytes, o + 52, v.Tangent.Y);
                WriteF(bytes, o + 56, v.Tangent.Z); WriteF(bytes, o + 60, v.Tangent.W);
            }
            return bytes;
        }

        private static void WriteF(byte[] b, int o, float f) => BitConverter.GetBytes(f).CopyTo(b, o);

        private static void WriteC(byte[] b, int o, Vector4 c)
        {
            b[o + 0] = Clamp255(c.X); b[o + 1] = Clamp255(c.Y);
            b[o + 2] = Clamp255(c.Z); b[o + 3] = Clamp255(c.W);
        }

        private static byte Clamp255(float f) => (byte)Math.Max(0, Math.Min(255, (int)(f * 255.0f + 0.5f)));

        private static TextureBase TexParam(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "givemechecker";
            name = name.ToLowerInvariant();
            return new TextureBase
            {
                Name = name,
                NameHash = JenkHash.GenHash(name),
                Unknown_4h = 1,
                Unknown_30h = 1,
                Unknown_32h = 2,
            };
        }

        public static ShaderFX BuildShader(TerrainEditor te, string preset, string maskName, string paletteName)
        {
            bool bump = false;
            for (int i = 0; i < TerrainEditor.LayerCount; i++) bump |= !string.IsNullOrEmpty(te.Layers[i].BumpName);

            var names = new List<ShaderParamNames>();
            var vals = new List<ShaderParameter>();

            void AddTex(ShaderParamNames n, string tex) =>
                Add(names, vals, n, TexParam(tex));
            void AddVal(ShaderParamNames n, Vector4 v) =>
                Add(names, vals, n, v);

            AddTex(ShaderParamNames.TextureSampler_layer0, te.Layers[0].Name);
            AddTex(ShaderParamNames.TextureSampler_layer1, te.Layers[1].Name ?? te.Layers[0].Name);
            AddTex(ShaderParamNames.TextureSampler_layer2, te.Layers[2].Name ?? te.Layers[0].Name);
            AddTex(ShaderParamNames.TextureSampler_layer3, te.Layers[3].Name ?? te.Layers[0].Name);
            if (bump)
            {
                AddTex(ShaderParamNames.BumpSampler_layer0, te.Layers[0].BumpName);
                AddTex(ShaderParamNames.BumpSampler_layer1, te.Layers[1].BumpName ?? te.Layers[0].BumpName);
                AddTex(ShaderParamNames.BumpSampler_layer2, te.Layers[2].BumpName ?? te.Layers[0].BumpName);
                AddTex(ShaderParamNames.BumpSampler_layer3, te.Layers[3].BumpName ?? te.Layers[0].BumpName);
            }
            if (UsesMask(preset)) AddTex(ShaderParamNames.lookupSampler, maskName);
            if (UsesTint(preset)) AddTex(ShaderParamNames.TintPaletteSampler, paletteName);

            AddVal(ShaderParamNames.HardAlphaBlend, new Vector4(1, 0, 0, 0));
            AddVal(ShaderParamNames.useTessellation, Vector4.Zero);
            AddVal(ShaderParamNames.wetnessMultiplier, new Vector4(1, 0, 0, 0));
            if (bump) AddVal(ShaderParamNames.bumpiness, new Vector4(1, 0, 0, 0));
            AddVal(ShaderParamNames.specularIntensityMult, new Vector4(0.35f, 0, 0, 0));
            AddVal(ShaderParamNames.specularFalloffMult, new Vector4(64, 0, 0, 0));
            AddVal(ShaderParamNames.specularFresnel, new Vector4(0.96f, 0, 0, 0));
            AddVal(ShaderParamNames.globalAnimUV0, new Vector4(1, 0, 0, 0));
            AddVal(ShaderParamNames.globalAnimUV1, new Vector4(0, 1, 0, 0));

            var shader = new ShaderFX
            {
                Name = new MetaHash(JenkHash.GenHash(preset)),
                FileName = new MetaHash(JenkHash.GenHash(preset + ".sps")),
                Unknown_Ch = 0,
                Unknown_12h = 32768,
                Unknown_1Ch = 0,
                Unknown_24h = 0,
                Unknown_26h = 0,
                Unknown_28h = 0,
            };
            var block = new ShaderParametersBlock
            {
                Owner = shader,
                Parameters = vals.ToArray(),
                Hashes = names.ConvertAll(n => (MetaName)n).ToArray(),
                Count = vals.Count,
            };
            shader.ParametersList = block;
            MaterialEditing.SetBucket(shader, 0);
            MaterialEditing.Normalise(shader);
            return shader;
        }

        private static void Add(List<ShaderParamNames> names, List<ShaderParameter> vals, ShaderParamNames name, object value)
        {
            vals.Add(new ShaderParameter { Data = value, DataType = (byte)(value is TextureBase ? 0 : 1) });
            names.Add(name);
        }

        public static YdrFile Build(TerrainEditor te, string name, string maskName, string paletteName)
        {
            if (te == null || !te.HasMesh) return null;
            var decl = BuildDeclaration();
            var shader = BuildShader(te, te.ExportPreset, maskName, paletteName);

            var geoms = new List<DrawableGeometry>();
            var aabbs = new List<AABB_s>();
            var bbMin = new Vector3(float.MaxValue);
            var bbMax = new Vector3(float.MinValue);

            foreach (var part in te.Parts)
            {
                var vbytes = PackVertices(part.Verts);
                var vdata = new VertexData
                {
                    Info = decl,
                    VertexType = (VertexType)decl.Flags,
                    VertexStride = decl.Stride,
                    VertexCount = part.Verts.Length,
                    VertexBytes = vbytes,
                };
                var vbuf = new VertexBuffer
                {
                    Data1 = vdata,
                    Data2 = vdata,
                    Info = decl,
                    VertexCount = (uint)part.Verts.Length,
                    VertexStride = decl.Stride,
                    VFT = 1080153064,
                    Unknown_4h = 1,
                };
                var ibuf = new IndexBuffer
                {
                    IndicesCount = (uint)part.Indices.Length,
                    Indices = (ushort[])part.Indices.Clone(),
                    VFT = 1080111576,
                    Unknown_4h = 1,
                };
                var geom = new DrawableGeometry
                {
                    Shader = shader,
                    VertexData = vdata,
                    VertexBuffer = vbuf,
                    IndexBuffer = ibuf,
                    VFT = 1080133736,
                    Unknown_4h = 1,
                    IndicesCount = (uint)part.Indices.Length,
                    TrianglesCount = (uint)(part.Indices.Length / 3),
                    VerticesCount = (ushort)part.Verts.Length,
                    Unknown_62h = 3,
                    VertexStride = decl.Stride,
                    BoneIdsCount = 0,
                };
                geoms.Add(geom);

                var pmin = new Vector3(float.MaxValue);
                var pmax = new Vector3(float.MinValue);
                foreach (var v in part.Verts) { pmin = Vector3.Min(pmin, v.Position); pmax = Vector3.Max(pmax, v.Position); }
                bbMin = Vector3.Min(bbMin, pmin);
                bbMax = Vector3.Max(bbMax, pmax);
                aabbs.Add(new AABB_s { Min = new Vector4(pmin, 0), Max = new Vector4(pmax, 0) });
            }

            var model = new DrawableModel
            {
                VFT = 1080101496,
                Unknown_4h = 1,
                RenderMaskFlags = 0x00FF,
                Geometries = geoms.ToArray(),
                GeometriesCount1 = (ushort)geoms.Count,
                GeometriesCount2 = (ushort)geoms.Count,
                GeometriesCount3 = (ushort)geoms.Count,
                BoundsData = geoms.Count > 1 ? BoundsWithTotal(aabbs, bbMin, bbMax) : aabbs.ToArray(),
            };
            model.ShaderMapping = new ushort[geoms.Count];

            var sgrp = new ShaderGroup
            {
                Shaders = new ResourcePointerArray64<ShaderFX> { data_items = new[] { shader } },
                ShadersCount1 = 1,
                ShadersCount2 = 1,
                VFT = 1080113376,
                Unknown_4h = 1,
            };

            var centre = (bbMin + bbMax) * 0.5f;
            float radius = 0;
            foreach (var part in te.Parts)
                foreach (var v in part.Verts)
                    radius = Math.Max(radius, (v.Position - centre).Length());

            var d = new Drawable
            {
                Name = name + ".#dr",
                ShaderGroup = sgrp,
                BoundingCenter = centre,
                BoundingSphereRadius = radius,
                BoundingBoxMin = bbMin,
                BoundingBoxMax = bbMax,
                LodDistHigh = 9998,
                LodDistMed = 9998,
                LodDistLow = 9998,
                LodDistVlow = 9998,
                FileVFT = 1079446584,
                FileUnknown = 1,
                DrawableModels = new DrawableModelsBlock(),
                FlagsHigh = 1,
            };
            d.DrawableModels.High = new[] { model };
            d.BuildRenderMasks();
            d.LightAttributes = new ResourceSimpleList64<LightAttributes>();

            return new YdrFile { Drawable = d, Name = name + ".ydr" };
        }

        private static AABB_s[] BoundsWithTotal(List<AABB_s> per, Vector3 min, Vector3 max)
        {
            var all = new AABB_s[per.Count + 1];
            all[0] = new AABB_s { Min = new Vector4(min, 0), Max = new Vector4(max, 0) };
            for (int i = 0; i < per.Count; i++) all[i + 1] = per[i];
            return all;
        }

        public static YtdFile BuildYtd(TerrainEditor te, string name, GameTexture mask, GameTexture palette,
                                       out int written, bool includeGame = false)
        {
            written = 0;
            var texs = new List<GameTexture>();
            void AddTex(GameTexture t)
            {
                if (t?.Data?.FullData == null || string.IsNullOrEmpty(t.Name)) return;
                foreach (var e in texs) if (e.NameHash == t.NameHash) return;
                texs.Add(t);
            }
            foreach (var l in te.Layers)
            {
                if (l.Source == "disk" || (includeGame && l.Source == "game"))
                { AddTex(l.Texture); AddTex(l.BumpTexture); }
            }
            AddTex(mask);
            AddTex(palette);
            if (texs.Count == 0) return null;

            var dict = new TextureDictionary();
            dict.BuildFromTextureList(texs);
            written = texs.Count;
            return new YtdFile { TextureDict = dict, Name = name + ".ytd" };
        }

        public static GameTexture RasteriseMask(TerrainEditor te, int size, string name, out string problem)
        {
            problem = null;
            if (te == null || !te.HasMesh) { problem = "no mesh"; return null; }
            size = Math.Max(64, Math.Min(2048, size));

            var uvMin = new Vector2(float.MaxValue);
            var uvMax = new Vector2(float.MinValue);
            foreach (var p in te.Parts)
                foreach (var v in p.Verts) { uvMin = Vector2.Min(uvMin, v.UV1); uvMax = Vector2.Max(uvMax, v.UV1); }
            if ((uvMax - uvMin).Length() < 1e-4f)
            {
                problem = "the mesh has no second UV set, which is where a _cm mask is sampled";
                return null;
            }

            var px = new byte[size * size * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 0; px[i + 1] = 0; px[i + 2] = 0; px[i + 3] = 255; }

            foreach (var part in te.Parts)
            {
                var verts = part.Verts;
                var idx = part.Indices;
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    var a = verts[idx[i]]; var b = verts[idx[i + 1]]; var c = verts[idx[i + 2]];
                    RasteriseTri(px, size, a, b, c);
                }
            }

            for (int i = 0; i < px.Length; i += 4) { var t = px[i]; px[i] = px[i + 2]; px[i + 2] = t; }

            var lower = (name ?? "terrain_mask").ToLowerInvariant();
            return new GameTexture
            {
                Name = lower,
                NameHash = JenkHash.GenHash(lower),
                Width = (ushort)size,
                Height = (ushort)size,
                Depth = 1,
                Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = (ushort)(size * 4),
                Data = new TextureData { FullData = px },
            };
        }

        private static void RasteriseTri(byte[] px, int size, MeshVertex a, MeshVertex b, MeshVertex c)
        {
            var pa = new Vector2(a.UV1.X * size, a.UV1.Y * size);
            var pb = new Vector2(b.UV1.X * size, b.UV1.Y * size);
            var pc = new Vector2(c.UV1.X * size, c.UV1.Y * size);
            float area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y);
            if (Math.Abs(area) < 1e-9f) return;

            int x0 = (int)Math.Floor(Math.Min(pa.X, Math.Min(pb.X, pc.X))) - 1;
            int x1 = (int)Math.Ceiling(Math.Max(pa.X, Math.Max(pb.X, pc.X))) + 1;
            int y0 = (int)Math.Floor(Math.Min(pa.Y, Math.Min(pb.Y, pc.Y))) - 1;
            int y1 = (int)Math.Ceiling(Math.Max(pa.Y, Math.Max(pb.Y, pc.Y))) + 1;
            for (int y = Math.Max(0, y0); y <= Math.Min(size - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(size - 1, x1); x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    float w0 = ((pb.X - sx) * (pc.Y - sy) - (pc.X - sx) * (pb.Y - sy)) / area;
                    float w1 = ((pc.X - sx) * (pa.Y - sy) - (pa.X - sx) * (pc.Y - sy)) / area;
                    float w2 = 1.0f - w0 - w1;
                    const float eps = -0.02f;
                    if (w0 < eps || w1 < eps || w2 < eps) continue;
                    var col = a.Colour1 * w0 + b.Colour1 * w1 + c.Colour1 * w2;
                    int o = (y * size + x) * 4;
                    px[o + 0] = Clamp255(col.X);
                    px[o + 1] = Clamp255(col.Y);
                    px[o + 2] = Clamp255(col.Z);
                    px[o + 3] = 255;
                }
        }

        public static GameTexture BuildTintPalette(string name)
        {
            const int w = 16, h = 1;
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i++) px[i] = 255;
            var lower = (name ?? "terrain_tint").ToLowerInvariant();
            return new GameTexture
            {
                Name = lower,
                NameHash = JenkHash.GenHash(lower),
                Width = w,
                Height = h,
                Depth = 1,
                Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = w * 4,
                Data = new TextureData { FullData = px },
            };
        }
    }
}

