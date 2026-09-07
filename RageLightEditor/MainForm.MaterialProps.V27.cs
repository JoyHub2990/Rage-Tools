using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_MaterialProps_V27(Action<string, bool, string> check)
        {
            string dir = Path.Combine(Path.GetTempPath(), "rle_v27_matprops");
            string path = Path.Combine(dir, "matprops.ydr");
            string outPath = Path.Combine(dir, "matprops_saved.ydr");
            try
            {
                Directory.CreateDirectory(dir);
                ShaderPresets.Seed();
                TestSceneGenerator.Run(path);
                if (!File.Exists(path))
                { check("v27 material properties: the fixture was written", false, path); return; }

                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(path));
                var d = ydr.Drawable;
                var shaders = d?.ShaderGroup?.Shaders?.data_items;
                var tex = d?.ShaderGroup?.TextureDictionary?.Textures?.data_items?.FirstOrDefault();
                check("v27 texture properties: the fixture carries an embedded texture and two materials",
                      tex != null && shaders != null && shaders.Length >= 2,
                      tex == null ? "(no texture)" : $"{tex.Name}, {shaders?.Length ?? 0} material(s)");
                if (tex == null || shaders == null || shaders.Length < 2) return;

                check("v27 texture properties: as written it is a DIFFUSE texture, which is what the editor showed",
                      tex.Usage == TextureUsage.DIFFUSE && tex.ExtraFlags == 0,
                      $"{tex.Usage}, flags {tex.UsageFlags}, extra {tex.ExtraFlags}");

                var wantFlags = tex.UsageFlags | TextureUsageFlags.NOT_HALF | TextureUsageFlags.HD_SPLIT;
                tex.Usage = TextureUsage.NORMAL;
                tex.UsageFlags = wantFlags;
                tex.ExtraFlags = 1;

                foreach (var dd in MaterialEditing.Drawables(new LoadedFile { Ydr = ydr, Path = path }))
                    ShaderPresets.HarvestAll(dd);
                var mats = MaterialEditing.ForFile(null, new LoadedFile { Ydr = ydr, Path = path });
                check("v27 copy material: both materials are readable off the file", mats.Count >= 2,
                      string.Join(", ", mats.Select(x => x.Name)));
                if (mats.Count < 2) return;

                string fromName = mats[1].Name, ontoName = mats[0].Name;
                var clip = MaterialPanel.CopyMaterial_V27(mats[1]);
                check("v27 copy material: the copy carries the shader, the pass and every parameter",
                      clip != null && clip.Params.Count > 0 && clip.ShaderName == fromName,
                      clip == null ? "(nothing)" : clip.Summary);

                bool pasted = MaterialPanel.PasteMaterial_V27(clip, mats[0]);
                MaterialEditing.TryGetValue(mats[0].Shader, (uint)ShaderParamNames.emissiveMultiplier, out var em);
                check("v27 paste material: the target becomes the copied shader, with its values",
                      pasted && mats[0].Name == fromName && Math.Abs(em.X - 4f) < 1e-3f,
                      $"{ontoName} -> {mats[0].Name}, emissiveMultiplier {em.X:0.###}");

                File.WriteAllBytes(outPath, ydr.Save());
                var back = new YdrFile();
                back.Load(File.ReadAllBytes(outPath));
                var t2 = back.Drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items?.FirstOrDefault();
                check("v27 texture properties: usage, streaming flags and extra flags survive a save and reload",
                      t2 != null && t2.Usage == TextureUsage.NORMAL && t2.UsageFlags == wantFlags && t2.ExtraFlags == 1,
                      t2 == null ? "(no texture)" : $"{t2.Usage}, flags {t2.UsageFlags}, extra {t2.ExtraFlags}");

                var mats2 = MaterialEditing.ForFile(null, new LoadedFile { Ydr = back, Path = outPath });
                MaterialEditing.TryGetValue(mats2.Count > 0 ? mats2[0].Shader : null,
                                            (uint)ShaderParamNames.emissiveMultiplier, out var em2);
                check("v27 paste material: ...and so does the pasted material",
                      mats2.Count == mats.Count && mats2[0].Name == fromName && Math.Abs(em2.X - 4f) < 1e-3f,
                      $"{mats2.Count} material(s), first is {(mats2.Count > 0 ? mats2[0].Name : "(none)")}, " +
                      $"emissiveMultiplier {em2.X:0.###}");

                check("v27 texture properties: a slot says what belongs in it",
                      MaterialPanel.UsageForRole_V27(MatTexRole.Bump) == TextureUsage.NORMAL &&
                      MaterialPanel.UsageForRole_V27(MatTexRole.Spec) == TextureUsage.SPECULAR &&
                      MaterialPanel.UsageForRole_V27(MatTexRole.Tint) == TextureUsage.TINTPALETTE &&
                      MaterialPanel.UsageForRole_V27(MatTexRole.Diffuse) == TextureUsage.DIFFUSE,
                      "bump/spec/tint/diffuse all map");

                check("v27 texture properties: only a texture this file will write is editable",
                      MaterialPanel.CanEditTextureProps_V27(MatTexSource.Embedded) &&
                      MaterialPanel.CanEditTextureProps_V27(MatTexSource.Imported) &&
                      !MaterialPanel.CanEditTextureProps_V27(MatTexSource.GameArchive) &&
                      !MaterialPanel.CanEditTextureProps_V27(MatTexSource.LoadedYtd) &&
                      MaterialPanel.TexturePropsReason_V27(MatTexSource.GameArchive) != null,
                      "embedded and imported yes, archives and read-only ytds no");

                var all = MaterialPanel.AllSamplers_V27;
                bool terrain = all.Any(s => s.Name == "TextureSampler_layer0") &&
                               all.Any(s => s.Name == "BumpSampler_layer3");
                bool noDupes = !all.Any(s => s.Hash == (uint)ShaderParamNames.DiffuseSampler);
                check("v27 add parameter: every sampler in the format is offered, not just the eight with labels",
                      all.Length > 200 && terrain && noDupes,
                      $"{all.Length} sampler(s), terrain layers {terrain}, no duplicates of the labelled eight {noDupes}");

                materialPanel?.TexPropsUndoSelfTest_V27(check);
            }
            catch (Exception ex)
            {
                check("v27 material properties: the fixture round-trips", false, ex.Message);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
            }
        }
    }
}

