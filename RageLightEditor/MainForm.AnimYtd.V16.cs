using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void AnimOpenYtd_V16()
        {
            var a = AnimEd;
            using var d = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Open a texture dictionary",
                Filter = "Texture dictionary (*.ytd)|*.ytd|All files (*.*)|*.*",
                Multiselect = true,
            };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var sc = AnimScene_U6;
            if (sc == null) { a.Status = "No scene to load into."; return; }

            int ok = 0, textures = 0;
            foreach (var path in d.FileNames)
            {
                try
                {
                    var ytd = new YtdFile();
                    ytd.Load(File.ReadAllBytes(path));
                    if (ytd.TextureDict == null) continue;
                    if (!modelRenderer.ExternalTextureDicts.Contains(ytd.TextureDict))
                        modelRenderer.ExternalTextureDicts.Add(ytd.TextureDict);
                    ok++;
                    textures += ytd.TextureDict.Textures?.data_items?.Length ?? 0;
                    Console.WriteLine($"ANIM ytd {Path.GetFileName(path)}: " +
                                      $"{ytd.TextureDict.Textures?.data_items?.Length ?? 0} texture(s)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ANIM ytd failed " + Path.GetFileName(path) + ": " + ex.Message);
                }
            }

            if (ok == 0) { a.Status = "Nothing in those files read as a texture dictionary."; return; }

            AnimRebuildModel_V16();
            a.Status = $"Loaded {ok} texture dictionar{(ok == 1 ? "y" : "ies")} ({textures} texture(s)) - " +
                       "the model is rebuilt against them.";
        }

        private void AnimRebuildModel_V16()
        {
            var sc = AnimScene_U6;
            var a = AnimEd;
            if (sc?.Files == null) return;

            foreach (var f in sc.Files)
            {
                if (f == null) continue;
                Matrix? world = f.HasPlacement ? f.Placement : (Matrix?)null;
                Rendering.RenderModel rebuilt = null;
                try
                {
                    if (f.IsYft && f.Yft != null) rebuilt = modelRenderer.BuildFromYft(f.Yft, world);
                    else if (!f.IsYft && f.Ydr != null) rebuilt = modelRenderer.BuildFromYdr(f.Ydr, world);
                    else if (f.Drawable != null)
                        rebuilt = modelRenderer.BuildFromDrawable(f.Drawable, f.Path ?? "model", world ?? Matrix.Identity);
                }
                catch (Exception ex) { Console.WriteLine("ANIM rebuild failed: " + ex.Message); }

                if (rebuilt == null || rebuilt.Meshes.Count == 0) { rebuilt?.Dispose(); continue; }
                f.Model?.Dispose();
                f.Model = rebuilt;
            }
            sc.GeometryChanged_V16();
            animListedVersion_U6 = -1;
            AnimSyncMaterials_U6();
            bonePosed_V16 = false;
            if (a != null) a.LiveMeshes = 0;
        }
    }
}

