using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class Scene
    {
        public static List<(string Name, Drawable Drawable)> DrawablesInYdd_V38(YddFile ydd)
        {
            var list = new List<(string, Drawable)>();
            if (ydd?.Drawables == null) return list;
            var hashes = ydd.DrawableDict?.Hashes;
            for (int i = 0; i < ydd.Drawables.Length; i++)
            {
                var d = ydd.Drawables[i];
                if (d == null) continue;
                string nm = d.Name;
                if (string.IsNullOrWhiteSpace(nm) && hashes != null && i < hashes.Length)
                    nm = hashes[i].ToString();
                if (string.IsNullOrWhiteSpace(nm)) nm = "drawable_" + i;
                list.Add((nm, d));
            }
            return list;
        }

        public bool LoadYddFile_V38(string path, bool additive = false)
        {
            LoadError = null;
            try
            {
                ShaderPresets.Seed();
                var data = File.ReadAllBytes(path);
                var ydd = new YddFile();
                ydd.Load(data);

                var drawables = DrawablesInYdd_V38(ydd);
                if (drawables.Count == 0) { LoadError = "That .ydd holds no drawables."; return false; }

                if (!additive) CloseAll();
                foreach (var stale in Files.Where(f => f != null && PathIsYdd_V38(f.Path, path)).ToList())
                    RemoveFile(stale);

                foreach (var (name, d) in drawables)
                {
                    var lf = new LoadedFile
                    {
                        Path = path + "|" + name,
                        Drawable = d,
                        Skeleton = d.Skeleton,
                        ReadOnly = true,
                        Model = modelRenderer.BuildFromDrawable(d, name),
                    };
                    Files.Add(lf);
                    AppendLights(lf, d.LightAttributes?.data_items);
                    ShaderPresets.HarvestAll(d);
                }

                GeometryVersion++;
                undoStack.Clear();
                redoStack.Clear();

                var ytdPath = Path.ChangeExtension(path, ".ytd");
                if (File.Exists(ytdPath) && LoadYtdFileInternal(ytdPath)) RebuildModel();

                return true;
            }
            catch (Exception ex) { LoadError = ex.Message; return false; }
        }

        public static bool PathIsYdd_V38(string loadedPath, string yddPath)
        {
            if (string.IsNullOrEmpty(loadedPath) || string.IsNullOrEmpty(yddPath)) return false;
            int bar = loadedPath.IndexOf('|');
            if (bar < 0) return false;
            return string.Equals(loadedPath.Substring(0, bar), yddPath, StringComparison.OrdinalIgnoreCase);
        }

        public static string YddMemberName_V38(string loadedPath)
        {
            if (string.IsNullOrEmpty(loadedPath)) return null;
            int bar = loadedPath.IndexOf('|');
            return bar < 0 ? null : loadedPath.Substring(bar + 1);
        }
    }
}

