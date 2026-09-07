using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public readonly AnimEditor AnimEd = new AnimEditor();

        private bool animWired_U6;
        private string animLastDir_U6;
        private List<MaterialRef> animMats_U6 = new List<MaterialRef>();
        private int animListedVersion_U6 = -1;
        private LoadedFile animListedFile_U6;
        private double animLastTick_U6;

        private Scene AnimScene_U6 => SceneFor_L3(LightPanel.Space.Animation);

        private int animEnvFrame_U6;
        private bool animEnvOpened_U6, animEnvEdited_U6, animEnvExported_U6, animEnvScanned_U6;

        private void ApplyAnimSpaceFlag_U6()
        {
            if (panel != null && panel.Anim == null) { panel.Anim = AnimEd; animWired_U6 = true; }
            AnimLoadLogo_U6();
            if (Environment.GetEnvironmentVariable("RLE_ANIMSPACE") != "1") return;
            panel.Workspace = LightPanel.Space.Animation;
            panel.ApplyThemeFromSettings(false);
        }

        private void AnimLoadLogo_U6()
        {
            if (panel == null || panel.AnimLogoTexture != IntPtr.Zero || textureLoader == null || imguiRenderer == null) return;
            var srv = textureLoader.LoadEmbeddedPng("animations_logo.png", out int w, out int h);
            if (srv == null) return;
            panel.AnimLogoTexture = imguiRenderer.RegisterTexture(srv);
            panel.AnimLogoWidth = w;
            panel.AnimLogoHeight = h;
        }

        private void OnWorldTick_Anim_U6()
        {
            if (panel == null) return;
            if (!animWired_U6) { animWired_U6 = true; panel.Anim = AnimEd; }
            AnimHeadless_U6();
            if (!panel.AnimMode) return;
            var a = AnimEd;

            if (a.RequestOpenFile) { a.RequestOpenFile = false; AnimOpenDialog_U6(); }
            if (a.RequestTakeOpenModel) { a.RequestTakeOpenModel = false; AnimTakeFromLights_U6(); }
            if (a.RequestOpenArchive != null) { var n = a.RequestOpenArchive; a.RequestOpenArchive = null; AnimOpenArchive_U6(n); }
            if (a.RequestClose) { a.RequestClose = false; AnimClose_U6(); }
            if (a.RequestFrame) { a.RequestFrame = false; FrameModel(); }
            if (a.RequestAddTrack >= 0) { int i = a.RequestAddTrack; a.RequestAddTrack = -1; AnimAddTrack_U6(i); }
            if (a.RequestRemoveTrack >= 0) { int i = a.RequestRemoveTrack; a.RequestRemoveTrack = -1; AnimRemoveTrack_U6(i); }
            if (a.RequestPreset >= 0) { int i = a.RequestPreset; a.RequestPreset = -1; AnimApplyPreset_U6(i); }
            if (a.RequestBake) { a.RequestBake = false; AnimBake_U6(); }
            if (a.RequestExport) { a.RequestExport = false; AnimExportDialog_U6(); }
            if (a.RequestSaveProject) { a.RequestSaveProject = false; AnimSaveProjectDialog_U6(); }
            if (a.RequestOpenProject) { a.RequestOpenProject = false; AnimOpenProjectDialog_U6(); }
            ServiceYcd_V6();

            AnimSyncMaterials_U6();

            double now = clock.Elapsed.TotalSeconds;
            float dt = animLastTick_U6 > 0 ? (float)Math.Min(now - animLastTick_U6, 0.25) : 0.0f;
            animLastTick_U6 = now;
            a.Advance(dt);

            AnimApplyPreview_U6();
            panel.MloStatus = a.Summary();
        }

        private void AnimOpenDialog_U6()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "GTA V models (*.ydr;*.yft;*.ydd)|*.ydr;*.yft;*.ydd|All files (*.*)|*.*",
                Title = "Open a model to animate",
                InitialDirectory = Directory.Exists(animLastDir_U6 ?? "") ? animLastDir_U6 : null,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            animLastDir_U6 = Path.GetDirectoryName(dlg.FileName);
            AnimOpenPath_U6(dlg.FileName);
        }

        private bool AnimOpenPath_U6(string path)
        {
            var a = AnimEd;
            try
            {
                var sc = AnimScene_U6;
                if (sc == null) { a.Status = "No scene to open into yet."; return false; }
                if (!sc.LoadModelFile(path, additive: false))
                {
                    a.Status = "Could not open " + Path.GetFileName(path);
                    Console.WriteLine("ANIM open FAILED " + path);
                    return false;
                }
                sc.ActiveFile = sc.Files.LastOrDefault() ?? sc.ActiveFile;
                a.ModelPath = path;
                a.ModelName = Path.GetFileName(path);
                a.Clip.ModelPath = path;
                if (string.IsNullOrWhiteSpace(a.Clip.Name) || a.Clip.Name == "uv_anim")
                    a.Clip.Name = Path.GetFileNameWithoutExtension(path);
                animListedVersion_U6 = -1;
                AnimSyncMaterials_U6();
                FrameModel();
                a.Status = $"Opened {a.ModelName}: {a.Materials.Count} material(s), {a.AnimUvCount} animatable.";
                Console.WriteLine($"ANIM opened {path} mats {a.Materials.Count} animUv {a.AnimUvCount}");
                AnimLogMaterials_U6();
                return true;
            }
            catch (Exception ex)
            {
                a.Status = "Could not open: " + ex.Message;
                Console.WriteLine("ANIM open threw: " + ex.Message);
                return false;
            }
        }

        private bool AnimOpenArchive_U6(string name)
        {
            var a = AnimEd;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (gameFiles == null || !gameFiles.Ready)
            {
                a.Status = "The game archives are not up yet.";
                return false;
            }
            try
            {
                var key = name.Trim().ToLowerInvariant();
                uint hash = JenkHash.GenHash(key);
                var drawable = gameFiles.GetDrawable(hash, out var arch);
                if (drawable == null)
                {
                    a.Status = $"No drawable called '{name}' in the archives.";
                    Console.WriteLine("ANIM archive miss " + name);
                    return false;
                }

                var sc = AnimScene_U6;
                sc.CloseAllFiles();
                if (arch != null && arch.TextureDict != 0)
                {
                    var ytd = gameFiles.GetTextureDict(arch.TextureDict);
                    if (ytd?.TextureDict != null && !modelRenderer.ExternalTextureDicts.Contains(ytd.TextureDict))
                        modelRenderer.ExternalTextureDicts.Add(ytd.TextureDict);
                }
                modelRenderer.TextureContext = arch?.TextureDict ?? 0;
                var model = modelRenderer.BuildFromDrawable(drawable, key, Matrix.Identity);
                modelRenderer.TextureContext = 0;
                if (model == null || model.Meshes.Count == 0)
                {
                    model?.Dispose();
                    a.Status = $"'{name}' has no geometry to animate.";
                    return false;
                }
                var lf = sc.AddImportedProp(null, null, null, model, drawable.Skeleton, null,
                    Matrix.Identity, 1, true, key, null, fromMlo: false, drawable: drawable);
                sc.ActiveFile = lf;
                a.ModelPath = "";
                a.ModelName = key + " (archives)";
                a.Clip.ModelPath = key;
                if (string.IsNullOrWhiteSpace(a.Clip.Name) || a.Clip.Name == "uv_anim") a.Clip.Name = key;
                animListedVersion_U6 = -1;
                AnimSyncMaterials_U6();
                FrameModel();
                a.Status = $"Opened {key} from the archives: {a.Materials.Count} material(s), {a.AnimUvCount} animatable.";
                Console.WriteLine($"ANIM opened archive {key} mats {a.Materials.Count} animUv {a.AnimUvCount}");
                AnimLogMaterials_U6();
                return true;
            }
            catch (Exception ex)
            {
                a.Status = "Could not open from the archives: " + ex.Message;
                return false;
            }
        }

        private void AnimTakeFromLights_U6()
        {
            var a = AnimEd;
            var src = lightScene?.ActiveFile ?? lightScene?.Files.FirstOrDefault();
            if (src == null || string.IsNullOrEmpty(src.Path) || !File.Exists(src.Path))
            {
                a.Status = src == null
                    ? "The Lights workspace has nothing open."
                    : "That model has no file behind it to re-open here (it came from the archives).";
                return;
            }
            AnimOpenPath_U6(src.Path);
        }

        private void AnimClose_U6()
        {
            var a = AnimEd;
            AnimRestoreAll_U6();
            AnimScene_U6?.CloseAllFiles();
            animMats_U6.Clear();
            animListedVersion_U6 = -1;
            animListedFile_U6 = null;
            a.Clear();
            a.Status = "Closed.";
        }

        private void AnimLogMaterials_U6()
        {
            var a = AnimEd;
            foreach (var m in a.Materials)
                Console.WriteLine($"ANIMMAT [{m.Index}] {m.Name} ({m.Sps}) animUv {m.AnimUv} params {m.HasParams} meshes {m.MeshCount}");
        }

        private void AnimSyncMaterials_U6()
        {
            var a = AnimEd;
            var sc = AnimScene_U6;
            if (sc == null) return;
            var file = sc.ActiveFile ?? sc.SelectedFiles.FirstOrDefault() ?? sc.Files.FirstOrDefault();
            if (file == null)
            {
                if (a.HasModel) { a.Materials.Clear(); a.HasModel = false; }
                animMats_U6.Clear();
                animListedFile_U6 = null;
                animListedVersion_U6 = -1;
                return;
            }
            if (file == animListedFile_U6 && sc.GeometryVersion == animListedVersion_U6)
            {
                MaterialEditing.AttachMeshes(sc, animMats_U6);
                for (int i = 0; i < animMats_U6.Count && i < a.Materials.Count; i++)
                    a.Materials[i].MeshCount = animMats_U6[i].Meshes.Count;
                return;
            }

            animListedFile_U6 = file;
            animListedVersion_U6 = sc.GeometryVersion;
            animMats_U6 = MaterialEditing.ForFile(sc, file);
            a.Materials.Clear();
            foreach (var m in animMats_U6)
            {
                a.Materials.Add(new AnimEditor.MatRow
                {
                    Index = m.Index,
                    Name = m.Name,
                    Sps = m.Sps,
                    AnimUv = ShaderPresets.SupportsAnimatedUvs(m.Name) || ShaderPresets.SupportsAnimatedUvs(m.Sps),
                    HasParams = MaterialEditing.Has(m.Shader, (uint)ShaderParamNames.globalAnimUV0) &&
                                MaterialEditing.Has(m.Shader, (uint)ShaderParamNames.globalAnimUV1),
                    MeshCount = m.Meshes.Count,
                });
            }
            a.HasModel = true;
            if (string.IsNullOrEmpty(a.ModelName)) a.ModelName = file.Name ?? "model";
            a.SyncTracks();
            if (a.SelectedMaterial >= a.Materials.Count) a.SelectedMaterial = a.Materials.Count - 1;
            if (a.SelectedMaterial < 0 && a.Materials.Count > 0)
                a.SelectedMaterial = Math.Max(a.Materials.FindIndex(m => m.AnimUv), 0);
        }

        private void AnimApplyPreview_U6()
        {
            var a = AnimEd;
            int live = 0;
            var selIndex = a.SelectedRow?.Index ?? -1;
            for (int i = 0; i < animMats_U6.Count; i++)
            {
                var m = animMats_U6[i];
                if (m?.Meshes == null || m.Meshes.Count == 0) continue;

                Vector4 uv0, uv1;
                bool animated = a.TryEvaluate(m.Index, out uv0, out uv1) && a.PreviewEnabled;
                if (animated) live += m.Meshes.Count;
                else
                {
                    uv0 = MaterialEditing.GetValue(m.Shader, (uint)ShaderParamNames.globalAnimUV0, new Vector4(1, 0, 0, 0));
                    uv1 = MaterialEditing.GetValue(m.Shader, (uint)ShaderParamNames.globalAnimUV1, new Vector4(0, 1, 0, 0));
                }

                foreach (var mesh in m.Meshes)
                {
                    if (mesh == null) continue;
                    mesh.AnimUV0 = uv0;
                    mesh.AnimUV1 = uv1;
                }
                if (m.Index == selIndex) { a.LiveUv0 = uv0; a.LiveUv1 = uv1; }
            }
            a.LiveMeshes = live;
            AnimApplyBonePose_V16();
        }

        private void AnimRestoreAll_U6()
        {
            foreach (var m in animMats_U6)
            {
                if (m?.Meshes == null) continue;
                var uv0 = MaterialEditing.GetValue(m.Shader, (uint)ShaderParamNames.globalAnimUV0, new Vector4(1, 0, 0, 0));
                var uv1 = MaterialEditing.GetValue(m.Shader, (uint)ShaderParamNames.globalAnimUV1, new Vector4(0, 1, 0, 0));
                foreach (var mesh in m.Meshes) { if (mesh != null) { mesh.AnimUV0 = uv0; mesh.AnimUV1 = uv1; } }
            }
            AnimEd.LiveMeshes = 0;
        }

        private void AnimAddTrack_U6(int row)
        {
            var a = AnimEd;
            if (row < 0 || row >= a.Materials.Count) return;
            var m = a.Materials[row];
            if (a.Clip.Find(m.Index) != null) return;
            var t = a.Clip.Add(m.Index, m.Name);
            a.SelectedMaterial = row;
            a.Status = m.AnimUv
                ? $"Added a track on {m.Name}. Pick a preset, or key the values yourself."
                : $"Added a track on {m.Name} - but its preset has no animated UVs, so the game will ignore it.";
        }

        private void AnimRemoveTrack_U6(int row)
        {
            var a = AnimEd;
            if (row < 0 || row >= a.Materials.Count) return;
            var m = a.Materials[row];
            if (a.Clip.Remove(m.Index)) a.Status = "Removed the track on " + m.Name;
        }

        private void AnimApplyPreset_U6(int index)
        {
            var a = AnimEd;
            var t = a.SelectedTrack;
            if (t == null) { a.Status = "Add a track first."; return; }
            if (index < 0 || index >= UvAnimPresets.All.Length) return;
            var p = UvAnimPresets.All[index];
            p.Apply(t, a.Clip.Duration);
            a.Status = $"{p.Name} applied to {t.MaterialName}.";
            Console.WriteLine($"ANIM preset '{p.Name}' -> {t.MaterialName} [{t.MaterialIndex}]");
        }

        private void AnimBake_U6()
        {
            var a = AnimEd;
            var t = a.SelectedTrack;
            if (t == null) return;
            t.BakeToRaw(a.Clip.Duration, a.Clip.Fps);
            a.Status = $"Baked {t.MaterialName} onto the six shader numbers, {a.Clip.FrameCount} keys per channel.";
        }

        private void AnimExportDialog_U6()
        {
            var a = AnimEd;
            using var dlg = new SaveFileDialog
            {
                Filter = "Clip dictionary (*.ycd)|*.ycd|All files (*.*)|*.*",
                Title = "Export the UV animation",
                FileName = (string.IsNullOrWhiteSpace(a.Clip.Name) ? "uv_anim" : a.Clip.Name) + ".ycd",
                InitialDirectory = Directory.Exists(animLastDir_U6 ?? "") ? animLastDir_U6 : null,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            animLastDir_U6 = Path.GetDirectoryName(dlg.FileName);
            AnimExportTo_U6(dlg.FileName);
        }

        private bool AnimExportTo_U6(string path)
        {
            var a = AnimEd;
            bool ok = UvAnimYcd.RoundTrip(a.Clip, path, out var msg, out float err);
            a.ExportStatus = msg;
            a.LastExportPath = ok ? path : "";
            Console.WriteLine("ANIMEXPORT " + (ok ? "OK" : "FAILED") + " " + path + " err " +
                              err.ToString("0.#######", CultureInfo.InvariantCulture) + "\n  " + msg.Replace("\n", "\n  "));
            return ok;
        }

        private void AnimSaveProjectDialog_U6()
        {
            var a = AnimEd;
            using var dlg = new SaveFileDialog
            {
                Filter = UvAnimClip.ProjectFilter,
                Title = "Save the UV animation project",
                FileName = (string.IsNullOrWhiteSpace(a.Clip.Name) ? "uv_anim" : a.Clip.Name) + ".rleuv",
                InitialDirectory = Directory.Exists(animLastDir_U6 ?? "") ? animLastDir_U6 : null,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            animLastDir_U6 = Path.GetDirectoryName(dlg.FileName);
            a.Clip.Save(dlg.FileName, out var msg);
            a.Status = msg;
        }

        private void AnimOpenProjectDialog_U6()
        {
            var a = AnimEd;
            using var dlg = new OpenFileDialog
            {
                Filter = UvAnimClip.ProjectFilter,
                Title = "Open a UV animation project",
                InitialDirectory = Directory.Exists(animLastDir_U6 ?? "") ? animLastDir_U6 : null,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            animLastDir_U6 = Path.GetDirectoryName(dlg.FileName);
            var clip = UvAnimClip.Load(dlg.FileName, out var msg);
            a.Status = msg;
            if (clip == null) return;
            a.Clip = clip;
            a.Time = 0; a.Playing = false;
            if (!a.HasModel && !string.IsNullOrEmpty(clip.ModelPath) && File.Exists(clip.ModelPath))
                AnimOpenPath_U6(clip.ModelPath);
            a.SyncTracks();
        }

        private bool AnimKeyDown_U6(Keys combo)
        {
            if (panel == null || !panel.AnimMode || AnimEd == null) return false;
            var a = AnimEd;
            switch (combo)
            {
                case Keys.Space: a.Playing = !a.Playing; return true;
                case Keys.Home: a.Playing = false; a.SeekTo(0f); return true;
                case Keys.End: a.Playing = false; a.SeekTo(a.Clip.Duration); return true;
                case Keys.Left: a.Playing = false; a.SeekFrame(a.FrameOf(a.Time) - 1); return true;
                case Keys.Right: a.Playing = false; a.SeekFrame(a.FrameOf(a.Time) + 1); return true;
                case Keys.K:
                    var t = a.SelectedTrack;
                    if (t == null) { a.Status = "No track to key - add one on a material first."; return true; }
                    panel.KeyAllChannels_U6(t, a.Clip.Wrap(a.Time));
                    a.Status = $"Keyed every channel of {t.MaterialName} at {a.Time:0.000} s.";
                    return true;
                default: return false;
            }
        }

        private void AnimHeadless_U6()
        {
            animEnvFrame_U6++;
            var a = AnimEd;

            if (!animEnvScanned_U6)
            {
                var scan = Environment.GetEnvironmentVariable("RLE_ANIMSCAN");
                if (!string.IsNullOrEmpty(scan) && gameFiles != null && gameFiles.Ready && !gameFiles.Initialising)
                {
                    animEnvScanned_U6 = true;
                    var bits = scan.Split(',');
                    int want = int.TryParse(bits[0], out int n) ? n : 8;
                    string filter = bits.Length > 1 ? bits[1].Trim() : null;
                    try { AnimScanArchives_U6(want, filter); }
                    catch (Exception ex) { Console.WriteLine("ANIMSCAN threw: " + ex.Message); }
                }
            }

            if (!panel.AnimMode) return;

            if (!animEnvOpened_U6)
            {
                var open = Environment.GetEnvironmentVariable("RLE_ANIMOPEN");
                if (string.IsNullOrEmpty(open)) { animEnvOpened_U6 = true; }
                else if (animEnvFrame_U6 > 2)
                {
                    bool isPath = open.IndexOf('\\') >= 0 || open.IndexOf('/') >= 0 || open.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase);
                    if (!isPath && gameFiles != null && gameFiles.Initialising) return;
                    animEnvOpened_U6 = true;
                    if (open.Equals("test", StringComparison.OrdinalIgnoreCase))
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "rle_anim");
                        Directory.CreateDirectory(dir);
                        string ydr = Path.Combine(dir, "anim_test_scene.ydr");
                        if (!File.Exists(ydr)) TestSceneGenerator.Run(ydr);
                        AnimOpenPath_U6(ydr);
                    }
                    else if (isPath) AnimOpenPath_U6(open);
                    else AnimOpenArchive_U6(open);
                }
            }

            if (animEnvOpened_U6 && !animEnvEdited_U6 && a.HasModel)
            {
                animEnvEdited_U6 = true;

                var matSpec = Environment.GetEnvironmentVariable("RLE_ANIMMAT");
                int row = -1;
                if (string.Equals(matSpec, "all", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < a.Materials.Count; i++) if (a.Materials[i].AnimUv) AnimAddTrack_U6(i);
                    row = a.Materials.FindIndex(m => m.AnimUv);
                    matSpec = null;
                }
                if (!string.IsNullOrEmpty(matSpec))
                {
                    if (int.TryParse(matSpec, out int mi)) row = a.Materials.FindIndex(m => m.Index == mi);
                    if (row < 0) row = a.Materials.FindIndex(m => m.Name.IndexOf(matSpec, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (row < 0) row = Math.Max(a.Materials.FindIndex(m => m.AnimUv), a.Materials.Count > 0 ? 0 : -1);
                if (row >= 0)
                {
                    a.SelectedMaterial = row;
                    AnimAddTrack_U6(row);
                }

                if (float.TryParse(Environment.GetEnvironmentVariable("RLE_ANIMLENGTH"),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out float len) && len > 0.05f)
                    a.Clip.Duration = len;

                var presetSpec = Environment.GetEnvironmentVariable("RLE_ANIMPRESET");
                if (!string.IsNullOrEmpty(presetSpec))
                {
                    int pi = int.TryParse(presetSpec, out int p) ? p
                           : Array.FindIndex(UvAnimPresets.All, x => x.Name.IndexOf(presetSpec, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (pi >= 0)
                        foreach (var tk in a.Clip.Tracks) { UvAnimPresets.All[pi].Apply(tk, a.Clip.Duration); }
                    else Console.WriteLine("ANIM no preset called " + presetSpec);
                    if (pi >= 0) Console.WriteLine($"ANIM preset '{UvAnimPresets.All[pi].Name}' -> {a.Clip.Tracks.Count} track(s)");
                }

                var scroll = Environment.GetEnvironmentVariable("RLE_ANIMSCROLL");
                if (!string.IsNullOrEmpty(scroll) && a.Clip.Tracks.Count > 0)
                {
                    var bits = scroll.Split(',');
                    if (bits.Length >= 2 &&
                        float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float su) &&
                        float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sv))
                    {
                        foreach (var tk in a.Clip.Tracks)
                        {
                            UvAnimPresets.Scroll(tk, a.Clip.Duration, su, sv);
                            tk.Preset = $"scroll {su:0.###}, {sv:0.###} /s";
                        }
                        Console.WriteLine($"ANIM scroll {su},{sv} on {a.Clip.Tracks.Count} track(s)");
                    }
                }

                if (Environment.GetEnvironmentVariable("RLE_ANIMBAKE") == "1") AnimBake_U6();

                if (Environment.GetEnvironmentVariable("RLE_ANIMPLAY") == "1") a.Playing = true;
                if (float.TryParse(Environment.GetEnvironmentVariable("RLE_ANIMTIME"),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out float t0))
                {
                    a.Playing = false;
                    a.SeekTo(t0);
                }

                Console.WriteLine($"ANIMSETUP {a.Summary()}");
            }

            if (animEnvEdited_U6 && !animEnvExported_U6)
            {
                var exp = Environment.GetEnvironmentVariable("RLE_ANIMEXPORT");
                if (string.IsNullOrEmpty(exp)) animEnvExported_U6 = true;
                else if (animEnvFrame_U6 > 6)
                {
                    animEnvExported_U6 = true;
                    AnimExportTo_U6(exp);
                }
            }

            if (screenshotPath != null && (animEnvFrame_U6 % 60) == 0)
            {
                var t = a.SelectedTrack;
                Console.WriteLine($"ANIMDRAW t {a.Time:0.000} playing {a.Playing} mats {a.Materials.Count} " +
                                  $"animUv {a.AnimUvCount} tracks {a.Clip.Tracks.Count} live {a.LiveMeshes} " +
                                  $"sel [{a.SelectedRow?.Label ?? "-"}] " +
                                  $"uv0 ({a.LiveUv0.X:0.####},{a.LiveUv0.Y:0.####},{a.LiveUv0.Z:0.####}) " +
                                  $"uv1 ({a.LiveUv1.X:0.####},{a.LiveUv1.Y:0.####},{a.LiveUv1.Z:0.####}) " +
                                  $"mode {(t == null ? "-" : t.Raw ? "raw" : "composed")} meshesDrawn {sceneRenderer?.DrawnMeshes}");
            }
        }

        private void AnimScanArchives_U6(int want, string filter = null)
        {
            int files = 0, hits = 0, printed = 0, nameRule = 0, nameRuleOk = 0;
            var boneIds = new Dictionary<int, int>();
            var unk1c = new Dictionary<uint, int>();
            var unk10 = new Dictionary<byte, int>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var rpf in gameFiles.Cache.AllRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var e in rpf.AllEntries)
                {
                    if (!(e is RpfFileEntry fe) || fe.NameLower == null || !fe.NameLower.EndsWith(".ycd")) continue;
                    if (!string.IsNullOrEmpty(filter) &&
                        fe.NameLower.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    files++;
                    YcdFile ycd;
                    try
                    {
                        var data = ArchiveBrowser.Extract(fe);
                        if (data == null) continue;
                        ycd = new YcdFile();
                        ycd.Load(data, fe);
                    }
                    catch { continue; }
                    var found = UvAnimYcd.Describe(ycd);
                    if (found.Count == 0) continue;
                    hits++;
                    foreach (var f in found)
                    {
                        boneIds[f.BoneId] = boneIds.TryGetValue(f.BoneId, out var bc) ? bc + 1 : 1;
                        nameRule++;
                        var shortName = f.ClipName ?? "";
                        int slash = shortName.LastIndexOf('/');
                        if (slash >= 0) shortName = shortName.Substring(slash + 1);
                        int dot = shortName.IndexOf('.');
                        if (dot > 0) shortName = shortName.Substring(0, dot);
                        int uv = shortName.LastIndexOf("_uv_", StringComparison.OrdinalIgnoreCase);
                        if (uv > 0 && int.TryParse(shortName.Substring(uv + 4), out int mi) &&
                            UvAnimYcd.UvClipHash(shortName.Substring(0, uv), mi) == f.ClipHash) nameRuleOk++;
                    }
                    foreach (var cme in ycd.ClipMapEntries ?? Array.Empty<ClipMapEntry>())
                        foreach (var an in UvAnimYcd.AnimationsOf(cme?.Clip))
                        {
                            if (an == null) continue;
                            unk1c[an.Unknown_1Ch] = unk1c.TryGetValue(an.Unknown_1Ch, out var uc) ? uc + 1 : 1;
                            unk10[an.Unknown_10h] = unk10.TryGetValue(an.Unknown_10h, out var u0) ? u0 + 1 : 1;
                        }
                    if (printed < want)
                    {
                        printed++;
                        Console.WriteLine($"ANIMSCAN {fe.Path} ({found.Count} UV track(s))");
                        foreach (var f in found.Take(6)) Console.WriteLine("    " + f);
                    }
                    if (hits >= want * 4) break;
                }
                if (hits >= want * 4) break;
            }
            Console.WriteLine($"ANIMSCAN done in {sw.Elapsed.TotalSeconds:0.0} s: {files} .ycd scanned, {hits} with UV tracks. " +
                              $"name rule (hash(base)+N+1) holds for {nameRuleOk}/{nameRule}. " +
                              "BoneIds " + string.Join(" ", boneIds.OrderBy(k => k.Key).Take(16).Select(k => $"{k.Key}x{k.Value}")) +
                              " | Unknown1C " + string.Join(" ", unk1c.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key:X8}x{k.Value}")) +
                              " | Unknown10 " + string.Join(" ", unk10.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key}x{k.Value}")));
        }
    }
}

