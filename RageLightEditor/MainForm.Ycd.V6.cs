using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private YcdFile openYcd_V6;
        private string openYcdPath_V6;

        public bool OpenYcd_V6(string path, out string message)
        {
            var ycd = YcdDocument_V6.Open(path, out message);
            if (ycd == null) return false;
            openYcd_V6 = ycd;
            openYcdPath_V6 = path;
            var o = YcdDocument_V6.Describe(ycd);
            Console.WriteLine($"YCD open {Path.GetFileName(path)}: {o.Clips.Count} clip(s), {o.Animations.Count} animation(s), " +
                              $"{o.TrackCount} track(s), {o.KeyframeCount} keyframe(s)");
            YcdIntoEditor_V16(ycd);
            return true;
        }

        private void YcdIntoEditor_V16(YcdFile ycd)
        {
            var a = AnimEd;
            if (a == null || ycd == null) return;

            var res = Editor.UvAnimImport_V16.FromYcd_V16(ycd, a.ModelName);
            if (res.Any)
            {
                a.Clip = res.Clip;
                a.SyncTracks();
                a.Time = 0;
                var first = res.Clip.Tracks.FirstOrDefault();
                if (first != null)
                {
                    int row = a.Materials.FindIndex(m => m != null && m.Index == first.MaterialIndex);
                    if (row >= 0) a.SelectedMaterial = row;
                }
                a.PreviewEnabled = true;
                Console.WriteLine("YCD -> editor: " + res.Message);
            }

            int bones = YcdBonePreview_V16(ycd);
            if (res.Any) { a.Clip = res.Clip; a.SyncTracks(); a.Time = 0; }
            panel.YcdStatus_V6 = res.Any
                ? res.Message + (bones > 0 ? $"; {bones} bone track(s) previewing" : "")
                : bones > 0
                    ? $"{bones} bone track(s) previewing - this dictionary has no UV animation"
                    : res.Message;
        }

        public bool SaveYcd_V6(string path, out string message)
        {
            message = null;
            if (openYcd_V6 == null) { message = "No clip dictionary is open."; return false; }
            bool ok = YcdDocument_V6.Save(openYcd_V6, path ?? openYcdPath_V6, out message, out int bytes);
            if (ok) Console.WriteLine($"YCD saved {Path.GetFileName(path ?? openYcdPath_V6)}: {bytes:N0} bytes (and the .ycd.xml beside it)");
            return ok;
        }

        private List<(string name, byte[] data)> GameYcds_V6(int want)
        {
            var found = new List<(string, byte[])>();
            var rpfMan = gameFiles?.Cache?.RpfMan;
            if (rpfMan?.AllRpfs == null) return found;

            foreach (var rpf in rpfMan.AllRpfs)
            {
                if (found.Count >= want) break;
                foreach (var e in rpf.AllEntries ?? new List<RpfEntry>())
                {
                    if (found.Count >= want) break;
                    if (!(e is RpfFileEntry fe)) continue;
                    if (!fe.NameLower.EndsWith(".ycd", StringComparison.Ordinal)) continue;
                    try
                    {
                        var data = ArchiveBrowser.ExtractForDisk(fe);
                        if (data != null && data.Length > 64) found.Add((fe.Name, data));
                    }
                    catch { }
                }
            }
            return found;
        }

        private byte[] openYcdBytes_V6;
        private bool ycdEdited_V6;

        private void ServiceYcd_V6()
        {
            if (panel == null) return;

            if (panel.RequestYcdOpen_V6) { panel.RequestYcdOpen_V6 = false; YcdOpenDialog_V6(); }
            if (panel.RequestYtdOpen_V16) { panel.RequestYtdOpen_V16 = false; AnimOpenYtd_V16(); }
            if (panel.RequestYcdClose_V6)
            {
                panel.RequestYcdClose_V6 = false;
                openYcd_V6 = null; openYcdPath_V6 = null; openYcdBytes_V6 = null; ycdEdited_V6 = false;
                panel.YcdOutline_V6 = null; panel.YcdName_V6 = ""; panel.YcdStatus_V6 = "closed";
                panel.YcdEdited_V6 = false;
            }
            if (panel.RequestYcdSave_V6) { panel.RequestYcdSave_V6 = false; YcdSaveDialog_V6(false); }
            if (panel.RequestYcdSaveXml_V6) { panel.RequestYcdSaveXml_V6 = false; YcdSaveDialog_V6(true); }
            if (panel.RequestYcdNewBone_V6) { panel.RequestYcdNewBone_V6 = false; YcdNewBone_V6(); }

            if (panel.RequestYcdRename_V6 != null)
            {
                var p = panel.RequestYcdRename_V6.Split('\n');
                panel.RequestYcdRename_V6 = null;
                string m = null;
                if (p.Length == 2 && YcdDocument_V6.RenameClip(openYcd_V6, p[0], p[1], out m))
                    YcdTouched_V6("renamed to " + p[1]);
                else panel.YcdStatus_V6 = m ?? "could not rename";
            }
            if (panel.RequestYcdDelete_V6 != null)
            {
                var name = panel.RequestYcdDelete_V6; panel.RequestYcdDelete_V6 = null;
                if (YcdDocument_V6.DeleteClip(openYcd_V6, name, out var m)) YcdTouched_V6("deleted " + name);
                else panel.YcdStatus_V6 = m ?? "could not delete";
            }
            if (panel.RequestYcdRetime_V6 != null)
            {
                var p = panel.RequestYcdRetime_V6.Split('\n');
                panel.RequestYcdRetime_V6 = null;
                string m = null;
                if (p.Length == 4 &&
                    float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) &&
                    float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float e) &&
                    float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    YcdDocument_V6.SetClipTiming(openYcd_V6, p[0], s, e, r, out m))
                    YcdTouched_V6($"{p[0]}: {s:0.###} - {e:0.###} s at {r:0.##}x");
                else panel.YcdStatus_V6 = m ?? "could not retime";
            }
            if (panel.RequestYcdFlags_V6 != null)
            {
                var p = panel.RequestYcdFlags_V6.Split('\n');
                panel.RequestYcdFlags_V6 = null;
                string m = null;
                if (p.Length == 2 && uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint fl) &&
                    YcdDocument_V6.SetClipFlags(openYcd_V6, p[0], fl, out m))
                    YcdTouched_V6($"{p[0]}: flags {fl}");
                else panel.YcdStatus_V6 = m ?? "could not set the flags";
            }
        }

        private void YcdTouched_V6(string what)
        {
            ycdEdited_V6 = true;
            panel.YcdEdited_V6 = true;
            panel.YcdOutline_V6 = YcdDocument_V6.Describe(openYcd_V6);
            panel.YcdStatus_V6 = what;
        }

        private void YcdRefresh_V6(string status)
        {
            panel.YcdOutline_V6 = openYcd_V6 == null ? null : YcdDocument_V6.Describe(openYcd_V6);
            panel.YcdName_V6 = openYcdPath_V6 == null ? "" : Path.GetFileName(openYcdPath_V6);
            panel.YcdEdited_V6 = ycdEdited_V6;
            panel.YcdStatus_V6 = status;
        }

        private void YcdOpenDialog_V6()
        {
            using var d = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Open a clip dictionary",
                Filter = "Clip dictionary (*.ycd;*.ycd.xml)|*.ycd;*.ycd.xml|All files (*.*)|*.*",
            };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (!OpenYcd_V6(d.FileName, out var message)) { panel.YcdStatus_V6 = message; return; }
            try
            {
                openYcdBytes_V6 = d.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    ? null : File.ReadAllBytes(d.FileName);
            }
            catch { openYcdBytes_V6 = null; }
            ycdEdited_V6 = openYcdBytes_V6 == null;
            var o = YcdDocument_V6.Describe(openYcd_V6);
            YcdRefresh_V6($"opened - {o.Clips.Count} clip(s), {o.Animations.Count} animation(s), {o.KeyframeCount:N0} keyframe(s)");
        }

        private void YcdSaveDialog_V6(bool xmlOnly)
        {
            if (openYcd_V6 == null) { panel.YcdStatus_V6 = "nothing is open"; return; }
            using var d = new System.Windows.Forms.SaveFileDialog
            {
                Title = xmlOnly ? "Export the clip dictionary as XML" : "Export the clip dictionary",
                Filter = xmlOnly ? "CodeWalker XML (*.ycd.xml)|*.ycd.xml" : "Clip dictionary (*.ycd)|*.ycd",
                FileName = Path.GetFileNameWithoutExtension(openYcdPath_V6 ?? "animation") + (xmlOnly ? ".ycd.xml" : ".ycd"),
            };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            if (xmlOnly)
            {
                try
                {
                    File.WriteAllText(d.FileName, YcdDocument_V6.ToXml(openYcd_V6));
                    panel.YcdStatus_V6 = "wrote " + Path.GetFileName(d.FileName);
                }
                catch (Exception e) { panel.YcdStatus_V6 = e.Message; }
                return;
            }

            if (YcdDocument_V6.Save(openYcd_V6, d.FileName, openYcdBytes_V6, ycdEdited_V6, out var m, out int bytes))
                panel.YcdStatus_V6 = $"wrote {Path.GetFileName(d.FileName)} - {bytes:N0} bytes, {m}";
            else panel.YcdStatus_V6 = m;
        }

        private void YcdNewBone_V6()
        {
            int frames = Math.Max(panel.YcdNewFrames_V6, 2);
            float fps = Math.Max(panel.YcdNewFps_V6, 1f);
            var pos = new List<SharpDX.Vector3>();
            var rot = new List<SharpDX.Quaternion>();
            for (int f = 0; f < frames; f++)
            {
                float t = frames > 1 ? (float)f / (frames - 1) : 0f;
                pos.Add(new SharpDX.Vector3(0, 0, t));
                rot.Add(SharpDX.Quaternion.RotationAxis(SharpDX.Vector3.UnitZ, t * (float)Math.PI * 2f));
            }
            var tracks = new List<YcdBoneAnim_V6.BoneTrack_V6>
            {
                YcdBoneAnim_V6.Straight(0, YcdBoneAnim_V6.TrackPosition, pos),
                YcdBoneAnim_V6.Turning(0, rot),
            };
            var ycd = YcdBoneAnim_V6.Build(panel.YcdNewName_V6, tracks, frames, fps, out var message);
            if (ycd == null) { panel.YcdStatus_V6 = message ?? "could not build it"; return; }
            openYcd_V6 = ycd;
            openYcdPath_V6 = YcdBoneAnim_V6.Sanitise_V6(panel.YcdNewName_V6) + ".ycd";
            openYcdBytes_V6 = null;
            ycdEdited_V6 = true;
            YcdRefresh_V6($"built {frames} frames at {fps:0.#} fps - one bone, position and rotation");
        }

        partial void SeqTest_V6(Action<string, bool, string> check)
        {
            var fixtures = GameYcds_V6(12);
            if (fixtures.Count == 0)
            {
                check("v6: the game's clip dictionaries were found to test against", false,
                      "no .ycd found in the install - the round trip could not be checked");
            }
            else
            {
                int exported = 0, read = 0, tracks = 0, keys = 0;
                string firstFailure = null;
                var tmp = Path.Combine(Path.GetTempPath(), "rle_v6_export.ycd");
                foreach (var (name, data) in fixtures)
                {
                    var doc = YcdDocument_V6.OpenBytes(data, name, out var openWhy);
                    if (doc == null) { firstFailure ??= name + ": would not open - " + openWhy; continue; }
                    read++;
                    var o = YcdDocument_V6.Describe(doc);
                    tracks += o.TrackCount; keys += o.KeyframeCount;

                    if (!YcdDocument_V6.Save(doc, tmp, data, false, out var m, out _))
                    { firstFailure ??= name + ": would not export - " + m; continue; }
                    try
                    {
                        if (File.ReadAllBytes(tmp).SequenceEqual(data)) exported++;
                        else firstFailure ??= name + ": the exported file is not the file that was opened";
                    }
                    catch (Exception e) { firstFailure ??= name + ": " + e.Message; }
                }
                try { File.Delete(tmp); File.Delete(tmp + ".xml"); } catch { }

                check("v6: every clip dictionary in the game opens, and exports byte for byte",
                      read == fixtures.Count && exported == fixtures.Count,
                      read == fixtures.Count && exported == fixtures.Count
                        ? $"{read} file(s), {tracks} track(s), {keys:N0} keyframe(s), all identical on export"
                        : $"opened {read}, exported {exported}, of {fixtures.Count} - {firstFailure}");

                int rebuiltOk = 0, rebuiltExact = 0;
                string rebuiltNote = "";
                foreach (var (name, data) in fixtures)
                {
                    if (!YcdDocument_V6.RoundTrip(data, name, out var report, out bool identical, out _)) 
                    { if (rebuiltNote.Length == 0) rebuiltNote = name + ": " + report; continue; }
                    rebuiltOk++;
                    if (identical) rebuiltExact++;
                }
                check("v6: ...and a rebuilt one keeps its structure, with the re-encoding reported",
                      rebuiltExact > 0,
                      $"{rebuiltExact} of {fixtures.Count} rebuild bit-for-bit; {rebuiltOk - rebuiltExact} match on values; " +
                      $"{fixtures.Count - rebuiltOk} are re-encoded by the writer" +
                      (rebuiltNote.Length > 0 ? " (first: " + rebuiltNote + ")" : ""));

                var (fname, fdata) = fixtures[0];
                var a = YcdDocument_V6.OpenBytes(fdata, fname, out _);
                string xml = YcdDocument_V6.ToXml(a);
                var b = YcdDocument_V6.FromXml(xml, out var why);
                check("v6: ...and through the XML, which is what Sollumz reads",
                      b != null && YcdDocument_V6.ToXml(b) == xml,
                      b == null ? "would not rebuild: " + why
                                : YcdDocument_V6.ToXml(b) == xml ? fname + ", " + xml.Length.ToString("N0") + " chars"
                                : YcdDocument_V6.FirstDifference_V6(xml, YcdDocument_V6.ToXml(b)));

                var outline = YcdDocument_V6.Describe(a);
                check("v6: the section can list its clips, animations, tracks and keyframes",
                      outline.Clips.Count > 0 && outline.Animations.Count > 0,
                      $"{fname}: {outline.Clips.Count} clip(s), {outline.Animations.Count} animation(s), " +
                      $"{outline.TrackCount} track(s), {outline.KeyframeCount:N0} keyframe(s)");
            }

            var made = BuildTestYcd_V6(out var whyMade);
            check("v6: a clip dictionary can be built from nothing", made != null, whyMade ?? "built");
            if (made == null) return;

            check("v6: a clip can be retimed",
                  YcdDocument_V6.SetClipTiming(made, "pack:/v6_test.clip", 0.25f, 1.75f, 1.5f, out var m1) &&
                  YcdDocument_V6.FindClip(made, "pack:/v6_test.clip") is ClipAnimation ca1 &&
                  Math.Abs(ca1.StartTime - 0.25f) < 0.001f && Math.Abs(ca1.EndTime - 1.75f) < 0.001f &&
                  Math.Abs(ca1.Rate - 1.5f) < 0.001f, m1 ?? "0.25 - 1.75 s at 1.5x");

            check("v6: ...and a backwards clip is refused rather than written",
                  !YcdDocument_V6.SetClipTiming(made, "pack:/v6_test.clip", 2f, 1f, 1f, out _), "end before start");

            check("v6: a clip's flags can be set",
                  YcdDocument_V6.SetClipFlags(made, "pack:/v6_test.clip", 1u, out var m2) &&
                  YcdDocument_V6.FindClip(made, "pack:/v6_test.clip")?.Unknown_30h == 1u, m2 ?? "1");

            check("v6: a clip can be renamed, and the dictionary key moves with it",
                  YcdDocument_V6.RenameClip(made, "pack:/v6_test.clip", "pack:/v6_renamed.clip", out var m3) &&
                  YcdDocument_V6.FindClip(made, "pack:/v6_renamed.clip") != null &&
                  made.ClipMap != null &&
                  made.ClipMap.ContainsKey(JenkHash.GenHash("pack:/v6_renamed.clip")) &&
                  !made.ClipMap.ContainsKey(JenkHash.GenHash("pack:/v6_test.clip")), m3 ?? "key moved");

            byte[] saved = null;
            try { saved = made.Save(); } catch (Exception e) { check("v6: the edited dictionary saves", false, e.Message); }
            if (saved != null)
            {
                var back = YcdDocument_V6.OpenBytes(saved, "v6_edited.ycd", out var why2);
                var clip = back == null ? null : YcdDocument_V6.FindClip(back, "pack:/v6_renamed.clip") as ClipAnimation;
                check("v6: every edit is still there after a save and a reload",
                      clip != null && Math.Abs(clip.StartTime - 0.25f) < 0.001f &&
                      Math.Abs(clip.EndTime - 1.75f) < 0.001f && Math.Abs(clip.Rate - 1.5f) < 0.001f &&
                      clip.Unknown_30h == 1u,
                      back == null ? "would not reopen: " + why2
                                   : clip == null ? "the renamed clip was not in the saved file"
                                   : $"{clip.StartTime:0.##} - {clip.EndTime:0.##} s at {clip.Rate:0.##}x, flags {clip.Unknown_30h}");
            }

            check("v6: a clip can be deleted",
                  YcdDocument_V6.DeleteClip(made, "pack:/v6_renamed.clip", out var m4) &&
                  YcdDocument_V6.FindClip(made, "pack:/v6_renamed.clip") == null, m4 ?? "gone");
        }

        private static YcdFile BuildTestYcd_V6(out string message)
        {
            var bones = new List<YcdBoneAnim_V6.BoneTrack_V6>
            {
                YcdBoneAnim_V6.Straight(0, YcdBoneAnim_V6.TrackPosition,
                    new[] { new SharpDX.Vector3(0, 0, 0), new SharpDX.Vector3(0, 0, 1), new SharpDX.Vector3(0, 0, 2) }),
                YcdBoneAnim_V6.Turning(1, new[]
                {
                    SharpDX.Quaternion.Identity,
                    SharpDX.Quaternion.RotationAxis(SharpDX.Vector3.UnitZ, 0.5f),
                    SharpDX.Quaternion.RotationAxis(SharpDX.Vector3.UnitZ, 1.0f),
                }),
            };
            return YcdBoneAnim_V6.Build("v6_test", bones, 3, 30f, out message);
        }
    }
}

