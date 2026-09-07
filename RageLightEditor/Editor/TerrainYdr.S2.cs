using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public static partial class TerrainYdr
    {
        public static int EmbedTextures(Drawable d, TerrainEditor te, GameTexture mask, GameTexture palette,
                                        bool includeGame, out string note)
        {
            note = null;
            if (d?.ShaderGroup == null || te == null) return 0;

            var texs = new List<GameTexture>();
            int skippedGame = 0, skippedEmpty = 0;

            void Add(GameTexture t, string source)
            {
                if (t == null) return;
                if (t.Data?.FullData == null || string.IsNullOrEmpty(t.Name)) { skippedEmpty++; return; }
                if (source == "game" && !includeGame) { skippedGame++; return; }
                foreach (var e in texs) if (e.NameHash == t.NameHash) return;
                texs.Add(t);
            }

            foreach (var l in te.Layers) { Add(l.Texture, l.Source); Add(l.BumpTexture, l.Source); }
            Add(mask, "disk");
            Add(palette, "disk");

            if (texs.Count == 0)
            {
                note = skippedGame > 0
                    ? $"{skippedGame} of the layers came from the game's archives and were left out - tick \"include the game's own textures\" to embed them too"
                    : "no layer had pixels to embed";
                return 0;
            }

            var dict = d.ShaderGroup.TextureDictionary;
            if (dict == null)
            {
                dict = new TextureDictionary();
                d.ShaderGroup.TextureDictionary = dict;
            }
            var all = new List<GameTexture>();
            var existing = dict.Textures?.data_items;
            if (existing != null)
                foreach (var t in existing)
                {
                    if (t == null) continue;
                    bool replaced = false;
                    foreach (var n in texs) if (n.NameHash == t.NameHash) { replaced = true; break; }
                    if (!replaced) all.Add(t);
                }
            all.AddRange(texs);
            dict.BuildFromTextureList(all);

            var shaders = d.ShaderGroup.Shaders?.data_items;
            if (shaders != null)
                foreach (var sh in shaders)
                {
                    var ps = sh?.ParametersList?.Parameters;
                    if (ps == null) continue;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (!(ps[i].Data is TextureBase tb)) continue;
                        foreach (var t in texs)
                            if (t.NameHash == tb.NameHash) { ps[i].Data = t; break; }
                    }
                }

            if (skippedGame > 0)
                note = $"{skippedGame} texture(s) from the game's archives were left as name references";
            return texs.Count;
        }

        public static long EmbedSize(TerrainEditor te, bool includeGame)
        {
            long n = 0;
            if (te == null) return 0;
            foreach (var l in te.Layers)
            {
                if (l.Source == null) continue;
                if (l.Source == "game" && !includeGame) continue;
                n += l.Texture?.Data?.FullData?.Length ?? 0;
                n += l.BumpTexture?.Data?.FullData?.Length ?? 0;
            }
            return n;
        }
    }
}

