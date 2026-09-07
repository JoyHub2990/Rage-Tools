using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class Scene
    {
        public string[] MissingTextures_V21() => modelRenderer.MissingTexturesSnapshot_V21();

        public IReadOnlyList<(string Path, int Textures)> LoadedYtdList_V21()
        {
            var list = new List<(string, int)>(LoadedYtds.Count);
            for (int i = 0; i < LoadedYtds.Count; i++)
            {
                var name = i < LoadedYtdPaths.Count ? LoadedYtdPaths[i] : "(unknown)";
                list.Add((name, LoadedYtds[i]?.TextureDict?.Textures?.data_items?.Length ?? 0));
            }
            return list;
        }

        public bool UnloadYtd_V21(int index)
        {
            if (index < 0 || index >= LoadedYtds.Count) return false;
            var ytd = LoadedYtds[index];
            LoadedYtds.RemoveAt(index);
            if (index < LoadedYtdPaths.Count) LoadedYtdPaths.RemoveAt(index);
            if (ytd?.TextureDict != null) modelRenderer.ExternalTextureDicts.Remove(ytd.TextureDict);
            RebuildModel();
            return true;
        }

        public bool LoadYtdFromArchive_V21(YtdFile ytd, string archivePath)
        {
            if (ytd?.TextureDict == null) return false;
            if (!RegisterYtd(ytd, "gta:" + archivePath)) return false;
            RebuildModel();
            return true;
        }
    }
}

