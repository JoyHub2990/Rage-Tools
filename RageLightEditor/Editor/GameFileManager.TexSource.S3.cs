using System;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class GameFileManager
    {
        public string TextureDictName_S3(Texture tex, uint txdHash)
        {
            return TextureDictEntry_S3(tex, txdHash, out _);
        }

        public string TextureDictEntry_S3(Texture tex, uint txdHash, out RpfFileEntry entry)
        {
            entry = null;
            if (tex == null || !Ready || Cache == null) return "";
            uint nameHash = tex.NameHash;
            if (nameHash == 0) return "";

            try
            {
                uint h = txdHash;
                for (int guard = 0; h != 0 && guard < 16; guard++)
                {
                    var ytd = GetTextureDict(h);
                    if (ReferenceEquals(ytd?.TextureDict?.Lookup(nameHash), tex))
                        return YtdLabel_S3(ytd, h, out entry);
                    h = Cache.TryGetParentYtdHash(h);
                }

                var pa = ProjectAssets;
                if (pa != null && ReferenceEquals(pa.FindTexture(nameHash), tex))
                    return "the open project";

                var known = Cache.TryGetTextureDictForTexture(nameHash);
                if (known != null && ReferenceEquals(known.TextureDict?.Lookup(nameHash), tex))
                    return YtdLabel_S3(known, 0, out entry);

                uint indexed;
                lock (textureIndexLock)
                {
                    if (!textureIndex.TryGetValue(nameHash, out indexed)) indexed = 0;
                }
                if (indexed != 0)
                {
                    var ytd = Cache.GetYtd(indexed);
                    if (ReferenceEquals(ytd?.TextureDict?.Lookup(nameHash), tex))
                        return YtdLabel_S3(ytd, indexed, out entry);
                }
            }
            catch { }
            return "";
        }

        private static string YtdLabel_S3(YtdFile ytd, uint hash, out RpfFileEntry entry)
        {
            entry = ytd?.RpfFileEntry;
            var name = entry?.Name;
            if (!string.IsNullOrEmpty(name)) return name;
            name = ytd?.Name;
            if (!string.IsNullOrEmpty(name)) return name;
            return hash != 0 ? "#" + hash.ToString("X8") : "a .ytd";
        }
    }
}

