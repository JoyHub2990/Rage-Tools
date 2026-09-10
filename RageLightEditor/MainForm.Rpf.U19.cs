using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public struct ExtractTally_U19
        {
            public int Files, Folders, Failed;
            public long Bytes;
        }

        private bool rpfSelectProbeDone_U19;

        private void ServiceRpfMulti_U19()
        {
            var p = panel;
            if (p == null) return;
            if (!rpfSelectProbeDone_U19 && rpfSpaceOpened && p.Rpf.Ready && Environment.GetEnvironmentVariable("RLE_RPFSELECT") == "all")
            {
                rpfSelectProbeDone_U19 = true;
                p.Rpf.EnsureList();
                int n = p.SelectAllRpfRows_U19();
                Console.WriteLine($"RPFSELECT {n} rows selected: {p.RpfSelectionSummary_U19()}");
            }
            if (p.RequestRpfExtractRows_U19 != null)
            {
                var rows = p.RequestRpfExtractRows_U19;
                p.RequestRpfExtractRows_U19 = null;
                ExtractRpfRows_U19(rows);
            }
            if (p.RequestRpfExportXmlRows_U19 != null)
            {
                var rows = p.RequestRpfExportXmlRows_U19;
                p.RequestRpfExportXmlRows_U19 = null;
                ExportRpfRowsXml_U19(rows);
            }
        }

        private void ExtractRpfRows_U19(List<RpfExplorer.Row> rows)
        {
            if (rows == null || rows.Count == 0) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Extract {rows.Count:N0} items into...",
                UseDescriptionForTitle = true,
                SelectedPath = rpfLastExtractDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                rpfLastExtractDir = dlg.SelectedPath;
                var t = ExtractRows_U19(rows, dlg.SelectedPath);
                panel.RpfStatus = ExtractTallyText_U19(t, dlg.SelectedPath);
                Console.WriteLine($"RPF extracted {t.Files} files in {t.Folders} folders ({t.Bytes} bytes, {t.Failed} failed) -> {dlg.SelectedPath}");
            }
            catch (Exception ex) { panel.RpfStatus = "extract failed: " + ex.Message; }
            finally { Cursor = Cursors.Default; }
        }

        public static string ExtractTallyText_U19(in ExtractTally_U19 t, string dest)
        {
            var sb = new StringBuilder();
            sb.Append("extracted ").Append(t.Files.ToString("N0")).Append(t.Files == 1 ? " file" : " files");
            if (t.Folders > 0) sb.Append(" in ").Append(t.Folders.ToString("N0")).Append(t.Folders == 1 ? " folder" : " folders");
            sb.Append(" (").Append(RpfExplorer.SizeText(t.Bytes).Length > 0 ? RpfExplorer.SizeText(t.Bytes) : "0 B").Append(") to ").Append(dest);
            if (t.Failed > 0) sb.Append(" - ").Append(t.Failed.ToString("N0")).Append(" could not be read");
            return sb.ToString();
        }

        public static ExtractTally_U19 ExtractRows_U19(IReadOnlyList<RpfExplorer.Row> rows, string dest)
        {
            var t = new ExtractTally_U19();
            if (rows == null || string.IsNullOrEmpty(dest)) return t;
            Directory.CreateDirectory(dest);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                try
                {
                    if (r.IsFolder)
                    {
                        var sub = Path.Combine(dest, RpfExplorer.SafeName(r.Name));
                        if (r.EnterDir != null)
                        {
                            int files = 0, failed = 0;
                            long bytes = 0;
                            RpfExplorer.ExtractFolder(r.EnterDir, sub, ref files, ref bytes, ref failed);
                            t.Files += files; t.Bytes += bytes; t.Failed += failed; t.Folders++;
                        }
                        else if (r.IsFs && Directory.Exists(r.Path))
                        {
                            CopyTree_U19(r.Path, sub, ref t);
                            t.Folders++;
                        }
                        else t.Failed++;
                        continue;
                    }

                    var name = RpfExplorer.SafeName(r.Name);
                    if (!taken.Add(name))
                    {
                        var stem = Path.GetFileNameWithoutExtension(name);
                        var ext = Path.GetExtension(name);
                        var from = r.Entry is RpfFileEntry fe0
                            ? RpfExplorer.SafeName(Path.GetFileNameWithoutExtension(fe0.File?.Name ?? "rpf"))
                            : "disk";
                        name = stem + "." + from + ext;
                        taken.Add(name);
                    }
                    var target = Path.Combine(dest, name);
                    if (r.IsFs)
                    {
                        if (!File.Exists(r.Path)) { t.Failed++; continue; }
                        if (string.Equals(Path.GetFullPath(r.Path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) { t.Files++; t.Bytes += r.Size; continue; }
                        File.Copy(r.Path, target, true);
                        t.Files++; t.Bytes += r.Size;
                    }
                    else if (r.Entry is RpfFileEntry fe)
                    {
                        var data = ArchiveBrowser.ExtractForDisk(fe);
                        if (data == null || data.Length == 0) { t.Failed++; continue; }
                        File.WriteAllBytes(target, data);
                        t.Files++; t.Bytes += data.Length;
                    }
                    else t.Failed++;
                }
                catch { t.Failed++; }
            }
            return t;
        }

        private static void CopyTree_U19(string src, string dst, ref ExtractTally_U19 t, int depth = 0)
        {
            if (depth > 24) return;
            Directory.CreateDirectory(dst);
            string[] files, dirs;
            try { files = Directory.GetFiles(src); } catch { t.Failed++; return; }
            try { dirs = Directory.GetDirectories(src); } catch { dirs = Array.Empty<string>(); }
            foreach (var f in files)
            {
                try
                {
                    var to = Path.Combine(dst, Path.GetFileName(f));
                    File.Copy(f, to, true);
                    t.Files++;
                    t.Bytes += new FileInfo(to).Length;
                }
                catch { t.Failed++; }
            }
            foreach (var d in dirs)
            {
                CopyTree_U19(d, Path.Combine(dst, Path.GetFileName(d)), ref t, depth + 1);
                t.Folders++;
            }
        }

        private void ExportRpfRowsXml_U19(List<RpfExplorer.Row> rows)
        {
            if (rows == null || rows.Count == 0) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = $"A folder for the XML of {rows.Count:N0} items",
                UseDescriptionForTitle = true,
                SelectedPath = rpfLastExtractDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                rpfLastExtractDir = dlg.SelectedPath;
                var tmp = Path.Combine(Path.GetTempPath(), "rle_rpfxml");
                Directory.CreateDirectory(tmp);
                int wrote = 0, skipped = 0;
                foreach (var r in rows)
                {
                    if (r.IsFolder) { skipped++; continue; }
                    try
                    {
                        string xml = null;
                        if (r.IsFs) { if (!DiskFileXml_V42(r.Path, out xml, out _)) xml = null; }
                        else if (r.Entry is RpfFileEntry fe)
                        {
                            var data = ArchiveBrowser.Extract(fe);
                            if (data != null && data.Length > 0) xml = MetaXml.GetXml(fe, data, out _, tmp);
                        }
                        if (string.IsNullOrEmpty(xml)) { skipped++; continue; }
                        File.WriteAllText(Path.Combine(dlg.SelectedPath, RpfExplorer.SafeName(r.Name) + ".xml"), xml, new UTF8Encoding(false));
                        wrote++;
                    }
                    catch { skipped++; }
                }
                panel.RpfStatus = $"wrote {wrote:N0} XML file(s) to {dlg.SelectedPath}" +
                                  (skipped > 0 ? $" - {skipped:N0} had no converter and were left out" : "");
                Console.WriteLine($"RPFXML wrote {wrote}, skipped {skipped}, into {dlg.SelectedPath}");
            }
            catch (Exception ex) { panel.RpfStatus = "export failed: " + ex.Message; }
            finally { Cursor = Cursors.Default; }
        }

        private bool ServiceRpfDragOutMany_U19()
        {
            var p = panel;
            if (p?.RequestRpfDragOutMany_U19 == null || rpfDragOutBusy_V55) return false;
            var rows = p.RequestRpfDragOutMany_U19;
            p.RequestRpfDragOutMany_U19 = null;
            p.RequestRpfDragOut_V55 = null;
            if (rows.Count == 0) return true;

            var paths = new List<string>();
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "rle_dragout");
                Directory.CreateDirectory(dir);
                var stage = new List<RpfExplorer.Row>();
                foreach (var r in rows)
                {
                    if (r.IsFs && (File.Exists(r.Path) || Directory.Exists(r.Path))) paths.Add(r.Path);
                    else if (!r.IsFs) stage.Add(r);
                }
                if (stage.Count > 0)
                {
                    var t = ExtractRows_U19(stage, dir);
                    foreach (var r in stage)
                    {
                        var pth = Path.Combine(dir, RpfExplorer.SafeName(r.Name));
                        if (File.Exists(pth) || Directory.Exists(pth)) paths.Add(pth);
                    }
                    if (t.Failed > 0) p.RpfStatus = $"{t.Failed:N0} of the selected items could not be extracted";
                }
            }
            catch (Exception ex)
            {
                p.RpfStatus = "could not extract the selection: " + ex.Message;
                return true;
            }
            if (paths.Count == 0) { p.RpfStatus = "nothing in the selection can be dragged out"; return true; }

            rpfDragOutBusy_V55 = true;
            int n = paths.Count;
            p.RpfStatus = $"dragging {n:N0} items - drop them in Explorer or on the desktop";
            BeginInvoke(new Action(() =>
            {
                try
                {
                    var obj = new DataObject(DataFormats.FileDrop, paths.ToArray());
                    var effect = DoDragDrop(obj, DragDropEffects.Copy);
                    panel.RpfStatus = effect == DragDropEffects.None
                        ? $"drag cancelled - {n:N0} items went nowhere"
                        : $"dropped {n:N0} items";
                }
                catch (Exception ex) { panel.RpfStatus = "drag failed: " + ex.Message; }
                finally
                {
                    rpfDragOutBusy_V55 = false;
                    try { ImGuiNET.ImGui.GetIO().AddMouseButtonEvent(0, false); } catch { }
                }
            }));
            return true;
        }
    }
}
