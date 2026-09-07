using CodeWalker.GameFiles;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public partial class AssetPreview
    {
        public string TextureSourceName_S3(GameTexture tex, MatTexSource src, out RpfFileEntry entry)
        {
            entry = null;
            switch (src)
            {
                case MatTexSource.Embedded:
                    return "embedded";
                case MatTexSource.Imported:
                    return "an imported file";
                case MatTexSource.LoadedYtd:
                    return "an open .ytd";
                case MatTexSource.GameArchive:
                    var name = game?.TextureDictEntry_S3(tex, TxdContext, out entry) ?? "";
                    return string.IsNullOrEmpty(name) ? "the game archives" : name;
                default:
                    return "not found";
            }
        }
    }
}

