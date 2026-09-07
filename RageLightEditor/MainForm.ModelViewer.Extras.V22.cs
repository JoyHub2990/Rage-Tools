using System;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool mvIndexWasReady_V23;
        private int mvWeapTestStage_V22;
        private readonly System.Collections.Generic.Queue<string> mvWeapTestQueue_V22 = new System.Collections.Generic.Queue<string>();

        private void ServiceModelViewerExtras_V22()
        {
            var mv = ModelView;
            var pv = modelViewPreview;
            if (mv == null) return;
            var c = gameFiles?.Cache;

            if (Environment.GetEnvironmentVariable("RLE_MVWEAPTEST") == "1" && mvWeapTestStage_V22 < 3)
                screenshotFrames = Math.Max(screenshotFrames, 30);
            if (Environment.GetEnvironmentVariable("RLE_MVWEAPTEST") == "1" && mvWeapTestStage_V22 < 3 && pv != null && pv.IsWeapon && pv.Model != null)
            {
                if (mvWeapTestStage_V22 == 0) { mvWeapTestStage_V22 = 1; mv.RequestWeaponSearch_V22 = true; }
                else if (mvWeapTestStage_V22 == 1 && mv.WeaponComps_V22.Count > 0)
                {
                    mvWeapTestStage_V22 = 2;
                    foreach (var wc in mv.WeaponComps_V22) if (!wc.IsVariant) mvWeapTestQueue_V22.Enqueue(wc.Name);
                }
                else if (mvWeapTestStage_V22 == 2)
                {
                    if (mvWeapTestQueue_V22.Count > 0) mv.RequestWeaponAttach_V22 = mvWeapTestQueue_V22.Dequeue();
                    else
                    {
                        mvWeapTestStage_V22 = 3;
                        ModelView.SetTab(5);
                        screenshotFrames = 12;
                        Console.WriteLine($"MVWEAPTEST assembled: {pv.Attachments.Count} attached - " +
                            string.Join(", ", pv.Attachments.Select(a => $"{a.Name} on {a.Bone}")) +
                            $"; missing textures {pv.MissingTextures.Length}");
                    }
                }
            }

            bool idxReady = gameFiles != null && gameFiles.TextureIndexReady;
            if (idxReady && !mvIndexWasReady_V23 && pv?.Model != null && pv.MissingTextures.Length > 0)
            {
                int fixedUp = pv.RefreshTextures();
                ClearModelViewTextures_P1();
                mv.Rebuild();
                Console.WriteLine($"MODELVIEW index ready: {fixedUp} texture(s) resolved, {pv.MissingTextures.Length} still missing");
                if (fixedUp > 0) mv.Status = $"{fixedUp} texture(s) found now that the archive index is ready";
            }
            mvIndexWasReady_V23 = idxReady;

            if (mv.RequestYtdDialog_V22)
            {
                mv.RequestYtdDialog_V22 = false;
                if (pv?.Model != null)
                {
                    using var dlg = new OpenFileDialog { Filter = "Texture dictionary (*.ytd)|*.ytd", Title = "Attach a texture dictionary",
                                                         InitialDirectory = YtdDialogStart_V24(), RestoreDirectory = true };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        try
                        {
                            var ytd = new YtdFile();
                            ytd.Load(System.IO.File.ReadAllBytes(dlg.FileName));
                            AttachYtdToPreview_V22(ytd, dlg.FileName);
                        }
                        catch (Exception ex) { mv.Status = "Could not read that .ytd: " + ex.Message; }
                    }
                }
            }

            if (!string.IsNullOrEmpty(mv.RequestYtdFromArchive_V22))
            {
                var name = mv.RequestYtdFromArchive_V22;
                mv.RequestYtdFromArchive_V22 = null;
                if (pv?.Model == null) { }
                else if (c?.YtdDict == null || !gameFiles.Ready) mv.Status = "No game folder is loaded.";
                else
                {
                    uint h = JenkHash.GenHash(System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant());
                    if (!c.YtdDict.TryGetValue(h, out var fe) || fe == null) mv.Status = $"No .ytd called '{name}' in the archives.";
                    else
                    {
                        try
                        {
                            var ytd = c.RpfMan.GetFile<YtdFile>(fe);
                            if (ytd?.TextureDict == null) throw new Exception("the file would not read");
                            AttachYtdToPreview_V22(ytd, "gta:" + fe.Path);
                        }
                        catch (Exception ex) { mv.Status = $"Could not load {name}: {ex.Message}"; }
                    }
                }
            }

            if (mv.RequestTextureVariant_V23 != '\0')
            {
                char v = mv.RequestTextureVariant_V23;
                mv.RequestTextureVariant_V23 = '\0';
                if (pv != null && pv.SetTextureVariant(v))
                {
                    ClearModelViewTextures_P1();
                    mv.Rebuild();
                    mv.Status = pv.MissingTextures.Length > 0
                        ? $"variant {v}: {pv.MissingTextures.Length} texture(s) not found - " + string.Join(", ", pv.MissingTextures.Take(3))
                        : $"variant {v}";
                }
            }

            if (mv.RequestDetachYtd_V22 >= 0)
            {
                int i = mv.RequestDetachYtd_V22;
                mv.RequestDetachYtd_V22 = -1;
                if (pv != null && pv.DetachYtd(i))
                {
                    ClearModelViewTextures_P1();
                    mv.Rebuild();
                    mv.Status = pv.MissingTextures.Length > 0 ? $"Dictionary unloaded - {pv.MissingTextures.Length} texture(s) now missing." : "Dictionary unloaded.";
                }
            }

            if (mv.RequestWeaponDetach_V22 >= 0)
            {
                int i = mv.RequestWeaponDetach_V22;
                mv.RequestWeaponDetach_V22 = -1;
                pv?.DetachComponent(i);
            }

            if (mv.RequestWeaponSearch_V22)
            {
                mv.RequestWeaponSearch_V22 = false;
                mv.WeaponComps_V22.Clear();
                if (pv?.Drawable == null) mv.WeaponStatus_V22 = "nothing open";
                else
                {
                    var baseName = System.IO.Path.GetFileNameWithoutExtension(pv.Stats?.Name ?? "");
                    if (baseName.EndsWith("_hi", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(0, baseName.Length - 3);
                    var prefix = baseName + "_";
                    int variants = 0, fromMeta = 0, missingModel = 0;

                    var meta = WeaponMetaFor_V26(mv.DiskPath);
                    var declared = meta?.For(baseName);
                    if (declared != null && declared.Count > 0)
                    {
                        foreach (var d in declared)
                        {
                            if (string.IsNullOrEmpty(d.Model)) continue;
                            if (mv.WeaponComps_V22.Any(w => string.Equals(w.Name, d.Model, StringComparison.OrdinalIgnoreCase))) continue;
                            bool found = FindComponentModel_V26(d.Model, mv.DiskPath, out var entry, out var diskPath);
                            mv.WeaponComps_V22.Add(new ModelViewer.CompRow
                            {
                                Name = d.Model,
                                Component = d.Component,
                                MetaBone = d.Bone,
                                Txd = d.Txd,
                                DiskPath = diskPath,
                                ModelMissing = !found,
                                Slot = d.Bone != null && d.Bone.StartsWith("WAP", StringComparison.OrdinalIgnoreCase) && d.Bone.Length > 3
                                       ? d.Bone.Substring(3) : (d.Bone ?? "Root"),
                            });
                            fromMeta++;
                            if (!found) missingModel++;
                        }
                    }

                    int looked = 0;
                    string where;

                    void Consider(string stem, Func<Skeleton> read, string diskPath)
                    {
                        if (stem.EndsWith("_hi", StringComparison.OrdinalIgnoreCase)) return;
                        if (string.Equals(stem, baseName, StringComparison.OrdinalIgnoreCase)) return;
                        if (mv.WeaponComps_V22.Any(w => string.Equals(w.Name, stem, StringComparison.OrdinalIgnoreCase))) return;
                        Skeleton skel = null;
                        try { skel = read(); } catch { }
                        bool isWeapon = Scene.IsWeaponSkeleton_V22(skel);
                        var aap = Scene.AapBoneOf_V21(skel);
                        if (!isWeapon && aap == null && diskPath != null) return;
                        if (isWeapon) variants++;
                        mv.WeaponComps_V22.Add(new ModelViewer.CompRow
                        {
                            Name = stem,
                            IsVariant = isWeapon,
                            DiskPath = diskPath,
                            Slot = isWeapon ? "" : (aap != null && aap.Name.Length > 3 ? aap.Name.Substring(3) : "Root"),
                        });
                    }

                    if (mv.DiskPath != null)
                    {
                        var dir = System.IO.Path.GetDirectoryName(mv.DiskPath);
                        where = "in " + (System.IO.Path.GetFileName(dir) ?? dir);
                        try
                        {
                            foreach (var f in System.IO.Directory.EnumerateFiles(dir ?? ".", prefix + "*.ydr"))
                            {
                                looked++;
                                var stem = System.IO.Path.GetFileNameWithoutExtension(f);
                                var path = f;
                                Consider(stem, () => { var y = new YdrFile(); y.Load(System.IO.File.ReadAllBytes(path)); return y.Drawable?.Skeleton; }, path);
                            }
                        }
                        catch (Exception ex) { mv.WeaponStatus_V22 = "could not read that folder: " + ex.Message; }
                    }
                    else if (c?.YdrDict != null && gameFiles.Ready)
                    {
                        where = "in the archives";
                        foreach (var fe2 in c.YdrDict.Values.ToList())
                        {
                            var nm = fe2?.Name;
                            if (nm == null || !nm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                            looked++;
                            var entry = fe2;
                            Consider(System.IO.Path.GetFileNameWithoutExtension(nm),
                                     () => c.RpfMan.GetFile<YdrFile>(entry)?.Drawable?.Skeleton, null);
                        }
                    }
                    else where = mv.WeaponStatus_V22 == null ? "in the archives" : null;

                    var sorted = mv.WeaponComps_V22.OrderBy(w => w.IsVariant).ThenBy(w => w.ModelMissing).ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    mv.WeaponComps_V22.Clear();
                    mv.WeaponComps_V22.AddRange(sorted);
                    int comps = mv.WeaponComps_V22.Count - variants;
                    mv.WeaponStatus_V22 = comps == 0 && variants == 0
                        ? (meta != null && meta.Knows(baseName) ? "the meta names no components for this weapon" : $"nothing named {prefix}* found, and no meta names this model")
                        : $"{comps} component(s), {variants} variant(s)" +
                          (fromMeta > 0 ? $" - {fromMeta} from the weapon's meta" : "") +
                          (missingModel > 0 ? $", {missingModel} whose model is nowhere" : "");
                    Console.WriteLine($"WEAPONFIND {baseName}: {comps} component(s) ({fromMeta} from meta, {missingModel} with no model), {variants} variant(s), {looked} file(s) by name");
                }
            }

            if (!string.IsNullOrEmpty(mv.RequestWeaponOpen_V22))
            {
                var nm = mv.RequestWeaponOpen_V22;
                mv.RequestWeaponOpen_V22 = null;
                var row = mv.WeaponComps_V22.FirstOrDefault(w => w.Name == nm);
                if (row?.DiskPath != null) { bool done = false; OpenDiskFileInModelViewer_P1(row.DiskPath, ref done); }
                else if (c?.YdrDict != null && c.YdrDict.TryGetValue(JenkHash.GenHash(nm.ToLowerInvariant()), out var fe) && fe != null)
                    panel.RequestOpenArchiveFile = fe;
            }

            if (!string.IsNullOrEmpty(mv.RequestWeaponAttach_V22))
            {
                var nm = mv.RequestWeaponAttach_V22;
                mv.RequestWeaponAttach_V22 = null;
                var row = mv.WeaponComps_V22.FirstOrDefault(w => w.Name == nm);
                if (pv != null)
                {
                    try
                    {
                        YdrFile ydr = null;
                        if (row?.DiskPath != null)
                        { ydr = new YdrFile(); ydr.Load(System.IO.File.ReadAllBytes(row.DiskPath)); }
                        else if (c?.YdrDict != null && c.YdrDict.TryGetValue(JenkHash.GenHash(nm.ToLowerInvariant()), out var fe) && fe != null)
                            ydr = c.RpfMan.GetFile<YdrFile>(fe);
                        if (ydr == null) mv.WeaponStatus_V22 = $"could not find {nm}";
                        else
                        {
                            AttachComponentTxd_V26(row, pv);
                            var bone = pv.AttachComponent(ydr, nm + ".ydr", row?.MetaBone, out var why);
                            mv.WeaponStatus_V22 = bone != null ? $"{nm} on {bone}" : why;
                            mv.Status = mv.WeaponStatus_V22;
                        }
                    }
                    catch (Exception ex) { mv.WeaponStatus_V22 = ex.Message; }
                }
            }
        }

        private void AttachYtdToPreview_V22(YtdFile ytd, string name)
        {
            var pv = modelViewPreview;
            if (pv == null) return;
            int before = pv.MissingTextures.Length;
            if (!pv.AttachYtd(ytd, name)) { ModelView.Status = "That dictionary is already attached."; return; }
            ClearModelViewTextures_P1();
            ModelView.Rebuild();
            int after = pv.MissingTextures.Length;
            int count = ytd.TextureDict?.Textures?.data_items?.Length ?? 0;
            ModelView.Status = $"{System.IO.Path.GetFileName(name.StartsWith("gta:") ? name.Substring(4) : name)} attached ({count} textures)" +
                               (before > after ? $" - {before - after} missing texture(s) resolved." : ".");
        }

        private void SeqTest_RpfExtras_V22(Action<string, bool, string> check)
        {
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) { Console.WriteLine("  v22 rpf: (skipped - no game folder)"); return; }
            if (!c.YdrDict.TryGetValue(JenkHash.GenHash("w_ar_carbinerifle"), out var wfe) || wfe == null)
            { check("v22 rpf: the carbine is in the archives", false, "not found"); return; }

            var pv = new AssetPreview(gameFiles, modelRenderer, textureLoader);
            try
            {
                bool opened = pv.Open(wfe);
                check("v22 rpf: the viewer opens the carbine and knows it is a weapon",
                      opened && pv.Model != null && pv.IsWeapon, opened ? (pv.IsWeapon ? "WAP bones present" : "no WAP bones") : pv.Error);

                YdrFile mag = null;
                if (c.YdrDict.TryGetValue(JenkHash.GenHash("w_ar_carbinerifle_mag1"), out var mfe))
                    try { mag = c.RpfMan.GetFile<YdrFile>(mfe); } catch { }
                var bone = mag != null ? pv.AttachComponent(mag, "w_ar_carbinerifle_mag1.ydr", out var why) : null;
                check("v22 rpf: the mag attaches on WAPClip", bone == "WAPClip", bone ?? "(none)");
                check("v22 rpf: ...and joins what the viewer draws", pv.RenderList_V22().Length == 2 && pv.Attachments.Count == 1,
                      $"{pv.RenderList_V22().Length} model(s) to draw, {pv.Attachments.Count} attachment(s)");
                var wap = pv.Drawable.Skeleton.Bones.Items.First(b => b?.Name == "WAPClip");
                var mesh0 = pv.Attachments.Count > 0 ? pv.Attachments[0].Model.Meshes.FirstOrDefault() : null;
                bool placed = mesh0 != null && (mesh0.Transform.TranslationVector - wap.AbsTransform.TranslationVector).Length() < 0.01f;
                check("v22 rpf: the mag's geometry sits at the bone", placed,
                      mesh0 == null ? "no mesh" : $"mesh at {mesh0.Transform.TranslationVector}, bone at {wap.AbsTransform.TranslationVector}");

                YtdFile md = null;
                if (c.YtdDict.TryGetValue(JenkHash.GenHash("mapdetail"), out var dfe))
                    try { md = c.RpfMan.GetFile<YtdFile>(dfe); } catch { }
                bool attached = md != null && pv.AttachYtd(md, "gta:" + dfe.Path);
                check("v22 rpf: a named .ytd attaches and the model rebuilds with it",
                      attached && pv.Model != null && pv.AttachedYtds.Count == 1, $"{pv.AttachedYtds.Count} attached, model {(pv.Model != null ? "built" : "missing")}");
                check("v22 rpf: ...and the attached component survives the rebuild", pv.Attachments.Count == 1 && pv.Attachments[0].Model?.Meshes.Count > 0,
                      $"{pv.Attachments.Count} attachment(s)");
                check("v22 rpf: the carbine's own textures all resolve, so nothing is reported missing",
                      pv.MissingTextures.Length == 0, pv.MissingTextures.Length + " missing: " + string.Join(", ", pv.MissingTextures.Take(4)));
                pv.DetachYtd(0);
                pv.DetachComponent(0);
                check("v22 rpf: detaching takes both off again", pv.AttachedYtds.Count == 0 && pv.Attachments.Count == 0 && pv.Model != null,
                      $"{pv.AttachedYtds.Count} dict(s), {pv.Attachments.Count} attachment(s)");
            }
            finally { pv.Dispose(); }
        }
    }
}

