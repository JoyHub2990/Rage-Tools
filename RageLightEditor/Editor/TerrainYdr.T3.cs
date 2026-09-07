using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public static partial class TerrainYdr
    {
        public struct LayerCheck
        {
            public string Param;
            public string Texture;
            public bool InFile;
            public int Width, Height;
        }

        public static List<LayerCheck> Verify(byte[] bytes, out string shaderName, out int blendedVerts,
                                              out int totalVerts, out bool hasColour1)
        {
            shaderName = "?";
            blendedVerts = 0; totalVerts = 0; hasColour1 = false;
            var result = new List<LayerCheck>();
            if (bytes == null || bytes.Length == 0) return result;

            var ydr = new YdrFile();
            ydr.Load(bytes);
            var d = ydr.Drawable;
            var shader = d?.ShaderGroup?.Shaders?.data_items?.Length > 0 ? d.ShaderGroup.Shaders.data_items[0] : null;
            if (shader == null) return result;
            shaderName = ShaderPresets.NameOf(shader.Name.Hash) ?? shader.Name.ToString();

            var want = new[]
            {
                ShaderParamNames.TextureSampler_layer0, ShaderParamNames.TextureSampler_layer1,
                ShaderParamNames.TextureSampler_layer2, ShaderParamNames.TextureSampler_layer3,
            };
            var ps = shader.ParametersList?.Parameters;
            var hs = shader.ParametersList?.Hashes;
            for (int s = 0; s < want.Length; s++)
            {
                var chk = new LayerCheck { Param = want[s].ToString(), Texture = null };
                for (int i = 0; ps != null && hs != null && i < ps.Length && i < hs.Length; i++)
                {
                    if ((uint)hs[i] != (uint)want[s]) continue;
                    if (ps[i].Data is GameTexture t)
                    { chk.Texture = t.Name; chk.InFile = true; chk.Width = t.Width; chk.Height = t.Height; }
                    else if (ps[i].Data is TextureBase tb) chk.Texture = tb.Name;
                    break;
                }
                result.Add(chk);
            }

            var geoms = d.DrawableModels?.High?.Length > 0 ? d.DrawableModels.High[0]?.Geometries : null;
            if (geoms != null)
                foreach (var g in geoms)
                {
                    var info = g?.VertexBuffer?.Info ?? g?.VertexData?.Info;
                    if (info != null && ((info.Flags >> (int)VertexSemantics.Colour1) & 1) == 1) hasColour1 = true;
                    var dv = Rendering.VertexDecoder.Decode(g?.VertexData, g?.IndexBuffer?.Indices);
                    if (dv == null) continue;
                    totalVerts += dv.Length;
                    foreach (var v in dv) if (v.Colour1.Y > 0.004f || v.Colour1.Z > 0.004f) blendedVerts++;
                }
            return result;
        }

        public static string Describe(List<LayerCheck> checks, Func<string, bool> archiveHas, out int distinct)
        {
            distinct = 0;
            var seen = new List<string>();
            int inFile = 0, named = 0, dead = 0, missing = 0;
            foreach (var c in checks)
            {
                if (string.IsNullOrEmpty(c.Texture)) { missing++; continue; }
                if (!seen.Contains(c.Texture)) seen.Add(c.Texture);
                if (c.InFile) { inFile++; continue; }
                if (archiveHas != null && archiveHas(c.Texture)) named++; else dead++;
            }
            distinct = seen.Count;

            var parts = new List<string>();
            if (inFile > 0) parts.Add(inFile + " layer(s) carry their pixels in the .ydr");
            if (named > 0) parts.Add(named + " layer(s) name a texture the archives answer");
            if (dead > 0) parts.Add(dead + " layer(s) name a texture NOTHING WILL RESOLVE");
            if (missing > 0) parts.Add(missing + " layer(s) have no texture at all");
            if (distinct == 1 && checks.Count > 1)
                parts.Add("all four layers name the SAME texture, so no paint can show");
            return string.Join("; ", parts);
        }

        public static Dictionary<uint, GameTexture> LocalTextures(Drawable d, string ydrPath)
        {
            var map = new Dictionary<uint, GameTexture>();
            void Add(GameTexture t)
            {
                if (t?.Data?.FullData == null || t.NameHash == 0) return;
                map[t.NameHash] = t;
            }
            var items = d?.ShaderGroup?.TextureDictionary?.Textures?.data_items;
            if (items != null) foreach (var t in items) Add(t);

            try
            {
                if (!string.IsNullOrEmpty(ydrPath))
                {
                    var side = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ydrPath)) ?? ".",
                                            Path.GetFileNameWithoutExtension(ydrPath) + ".ytd");
                    if (File.Exists(side))
                    {
                        var ytd = new YtdFile();
                        ytd.Load(File.ReadAllBytes(side));
                        var sitems = ytd.TextureDict?.Textures?.data_items;
                        if (sitems != null) foreach (var t in sitems) Add(t);
                    }
                }
            }
            catch { }
            return map;
        }
    }
}

