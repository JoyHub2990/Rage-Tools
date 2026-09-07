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
        private bool rpfSpaceOpened;

        private string rpfLastExtractDir;

        private void ServiceRpf_N4()
        {
            var p = panel;
            if (p == null) return;

            if (gameFiles != null && !string.IsNullOrEmpty(gameFiles.Folder)) p.RpfGameFolder = gameFiles.Folder;

            if (!rpfSpaceOpened && DebugArchiveSpace && p.Archive != null && p.Archive.Ready)
            {
                rpfSpaceOpened = true;
                if (!p.Rpf.Ready) p.Rpf.BuildFromGameFolder(p.RpfGameFolder, p.Archive);
                PrepareRpfSpace_O1();
                var want = Environment.GetEnvironmentVariable("RLE_RPFPATH");
                if (string.IsNullOrWhiteSpace(want)) want = "common.rpf\\data";
                p.RpfStatus = p.Rpf.GoToPath(want)
                    ? "opened " + want
                    : want + " is not all there - stopped at " + p.Rpf.CurrentDisplayPath;

                var find = Environment.GetEnvironmentVariable("RLE_RPFSEARCH");
                if (!string.IsNullOrWhiteSpace(find)) p.SetRpfSearch(find.Trim(), true);

                var disk = Environment.GetEnvironmentVariable("RLE_RPFVIEWDISK");
                if (!string.IsNullOrWhiteSpace(disk))
                {
                    disk = disk.Trim();
                    bool took = false;
                    OpenDiskFileInModelViewer_P1(disk, ref took);
                    if (!took) OpenRpfDiskFileForTest_P1(disk);

                    var hide = Environment.GetEnvironmentVariable("RLE_MVHIDE");
                    if (took && !string.IsNullOrWhiteSpace(hide) && modelViewPreview?.Model?.Meshes != null)
                    {
                        int gone = modelViewPreview.Model.Meshes
                            .RemoveAll(m => (m?.ShaderName ?? "").IndexOf(hide.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
                        Console.WriteLine($"MVHIDE removed {gone} mesh(es) matching '{hide.Trim()}'");
                    }
                    var mvy = Environment.GetEnvironmentVariable("RLE_MVYTD");
                    if (took && !string.IsNullOrWhiteSpace(mvy))
                        foreach (var one in mvy.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            try
                            {
                                var pth = one.Trim();
                                var ytd = new YtdFile();
                                ytd.Load(System.IO.File.ReadAllBytes(pth));
                                if (ytd.TextureDict == null) { Console.WriteLine("MVYTD no dict in " + pth); continue; }
                                AttachYtdToPreview_V22(ytd, pth);
                                Console.WriteLine("MVYTD attached " + pth + ": " + ModelView.Status);
                            }
                            catch (Exception ex) { Console.WriteLine("MVYTD failed " + one + ": " + ex.Message); }
                        }
                }

                var view = Environment.GetEnvironmentVariable("RLE_RPFVIEW");
                if (!string.IsNullOrWhiteSpace(view))
                {
                    view = view.Trim();
                    var entry = gameFiles?.Cache?.RpfMan?.GetEntry(view) as RpfFileEntry;
                    if (entry == null && !view.Contains('\\'))
                    {
                        var hits = new List<ArchiveBrowser.Entry>();
                        p.Archive.Find(Path.GetFileNameWithoutExtension(view), null, hits, 64);
                        foreach (var h in hits)
                            if (string.Equals(h.File?.Name, view, StringComparison.OrdinalIgnoreCase)) { entry = h.File; break; }
                    }
                    if (entry == null) p.RpfStatus = view + " is not in this install";
                    else
                    {
                        p.Rpf.Reveal(entry);
                        ViewRpfEntry_N4(entry);
                    }
                }
            }

            if (p.RequestRpfView != null)
            {
                var e = p.RequestRpfView;
                p.RequestRpfView = null;
                ViewRpfEntry_N4(e);
            }
            if (p.RequestRpfExportXml_V40 != null)
            { var x = p.RequestRpfExportXml_V40; p.RequestRpfExportXml_V40 = null; DoRpfExportXml_V40(x); }
            if (p.RequestRpfHex_V40 != null)
            { var h = p.RequestRpfHex_V40; p.RequestRpfHex_V40 = null; DoRpfHexView_V40(h); }
            if (p.RequestRpfExportXmlMany_V40 != null)
            { var m = p.RequestRpfExportXmlMany_V40; p.RequestRpfExportXmlMany_V40 = null; DoRpfExportXmlMany_V40(m); }
            if (p.RequestRpfDiskExportXml_V42 != null)
            { var dx = p.RequestRpfDiskExportXml_V42; p.RequestRpfDiskExportXml_V42 = null; DoRpfDiskExportXml_V42(dx); }
            if (p.RequestRpfDiskHex_V42 != null)
            { var dh = p.RequestRpfDiskHex_V42; p.RequestRpfDiskHex_V42 = null; DoRpfDiskHexView_V42(dh); }
            if (p.RequestRpfDiskConvert_V52 != null)
            { var dc = p.RequestRpfDiskConvert_V52; p.RequestRpfDiskConvert_V52 = null; DoRpfDiskConvert_V52(dc); }
            if (p.RequestRpfDiskConvertMany_V52 != null)
            { var dm = p.RequestRpfDiskConvertMany_V52; p.RequestRpfDiskConvertMany_V52 = null; DoRpfDiskConvertMany_V52(dm); }
            ServiceRpfDragOut_V55();

            if (p.RequestRpfExtract != null)
            {
                var e = p.RequestRpfExtract;
                p.RequestRpfExtract = null;
                ExtractRpfEntry_N4(e);
            }
            if (p.RequestRpfExtractFolder != null)
            {
                var d = p.RequestRpfExtractFolder;
                p.RequestRpfExtractFolder = null;
                ExtractRpfFolder_N4(d);
            }
            if (p.RequestRpfExtractMany != null)
            {
                var list = p.RequestRpfExtractMany;
                p.RequestRpfExtractMany = null;
                ExtractRpfMany_N4(list);
            }
            if (p.RequestRpfSpawn != null)
            {
                var e = p.RequestRpfSpawn;
                p.RequestRpfSpawn = null;
                p.SwitchWorkspace(LightPanel.Space.World);
                SpawnArchiveModelInWorld(e);
                p.RpfStatus = p.MloStatus;
            }
            if (p.RequestRpfToMloCreator != null)
            {
                var e = p.RequestRpfToMloCreator;
                p.RequestRpfToMloCreator = null;
                SendRpfEntryToMloCreator_N4(e);
            }

            RpfTextTick_R3();
            ServiceRpfEdit_O1();
            ServiceRpfHandoff_Q1();
        }

        partial void PrepareRpfSpace_O1();
        partial void ServiceRpfEdit_O1();
        partial void OpenInModelViewer_P1(RpfFileEntry e, string kind, ref bool handled);
        partial void OpenDiskFileInModelViewer_P1(string path, ref bool handled);
        partial void OpenMetaFileInExplorer_Q1(string path, ref bool handled);

        private void ViewRpfEntry_N4(RpfFileEntry e)
        {
            if (e == null) return;
            var kind = RpfExplorer.ViewKindOf(e);
            bool viewerTook = false;
            OpenInModelViewer_P1(e, kind, ref viewerTook);
            if (viewerTook) return;
            try
            {
                switch (kind)
                {
                    case "model":
                    case "textures":
                    case "collision":
                        panel.RpfStatus = e.Name + ": the model viewer could not take this file - " +
                                          "extract it and open it in the Lights workspace instead";
                        break;
                    case "particles":
                        OpenYptFromArchive_N4(e);
                        break;
                    case "text":
                        panel.ShowRpfText(e.Name, DecodeText_N4(ArchiveBrowser.Extract(e)));
                        panel.SetRpfViewSource_Q1(e, null);
                        panel.RpfStatus = "opened " + e.Name;
                        break;
                    case "xml":
                        ViewRpfAsXml_N4(e);
                        break;
                    default:
                        panel.RpfStatus = $"nothing here reads .{ArchiveBrowser.KindOf(e)} - extract it instead";
                        break;
                }
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "could not open " + e.Name + ": " + ex.Message;
            }
        }

        private void ViewRpfAsXml_N4(RpfFileEntry e)
        {
            var data = ArchiveBrowser.Extract(e);
            if (data == null || data.Length == 0)
            {
                panel.RpfStatus = e.Name + " came out of the archive empty";
                return;
            }
            var tmp = Path.Combine(Path.GetTempPath(), "rle_rpfxml");
            Directory.CreateDirectory(tmp);
            var xml = MetaXml.GetXml(e, data, out _, tmp);
            if (string.IsNullOrEmpty(xml))
            {
                panel.RpfStatus = e.Name + ": no XML converter for this file type - extract it instead";
                return;
            }
            panel.ShowRpfText(e.Name + ".xml", xml);
            panel.SetRpfViewSource_Q1(e, null);
            panel.RpfStatus = $"{e.Name} converted to XML ({xml.Length / 1024:N0} KB) - nothing was loaded";
            Console.WriteLine($"RPFMETA opened {e.Path} as XML ({xml.Length} chars) - nothing loaded");
            Console.WriteLine(ModelViewIsolationLine_P1());
        }

        private static string DecodeText_N4(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            return new UTF8Encoding(false).GetString(data).TrimStart('﻿');
        }

        private void ExtractRpfEntry_N4(RpfFileEntry e)
        {
            if (e == null) return;
            using var dlg = new SaveFileDialog
            {
                Title = "Extract from the archive",
                FileName = RpfExplorer.SafeName(e.Name),
                InitialDirectory = rpfLastExtractDir,
                Filter = "All files|*.*",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var data = ArchiveBrowser.ExtractForDisk(e);
                if (data == null || data.Length == 0) throw new Exception("the archive returned nothing");
                File.WriteAllBytes(dlg.FileName, data);
                rpfLastExtractDir = Path.GetDirectoryName(dlg.FileName);
                panel.RpfStatus = $"extracted {e.Name} ({data.Length:N0} bytes)";
                Console.WriteLine($"RPF extracted {e.Path} -> {dlg.FileName} ({data.Length} bytes)");
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "extract failed: " + ex.Message;
            }
        }

        private void ExtractRpfFolder_N4(RpfDirectoryEntry dir)
        {
            if (dir == null) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = "Extract " + (dir.Path ?? dir.Name) + " into...",
                UseDescriptionForTitle = true,
                SelectedPath = rpfLastExtractDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                rpfLastExtractDir = dlg.SelectedPath;
                var root = Path.Combine(dlg.SelectedPath, RpfExplorer.SafeName(dir.Name ?? "rpf"));
                int files = 0, failed = 0;
                long bytes = 0;
                RpfExplorer.ExtractFolder(dir, root, ref files, ref bytes, ref failed);
                panel.RpfStatus = $"extracted {files:N0} files ({bytes / 1024 / 1024:N0} MB) to {root}" +
                                  (failed > 0 ? $" - {failed:N0} could not be read" : "");
                Console.WriteLine($"RPF extracted folder {dir.Path} -> {root}: {files} files, {bytes} bytes, {failed} failed");
            }
            catch (Exception ex)
            {
                panel.RpfStatus = "extract failed: " + ex.Message;
            }
            finally { Cursor = Cursors.Default; }
        }

        private void ExtractRpfMany_N4(List<RpfFileEntry> files)
        {
            if (files == null || files.Count == 0) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Extract {files.Count:N0} files into...",
                UseDescriptionForTitle = true,
                SelectedPath = rpfLastExtractDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                rpfLastExtractDir = dlg.SelectedPath;
                Directory.CreateDirectory(dlg.SelectedPath);
                int done = 0, failed = 0;
                long bytes = 0;
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fe in files)
                {
                    try
                    {
                        var data = ArchiveBrowser.ExtractForDisk(fe);
                        if (data == null || data.Length == 0) { failed++; continue; }
                        var name = RpfExplorer.SafeName(fe.Name);
                        if (!taken.Add(name))
                        {
                            var stem = Path.GetFileNameWithoutExtension(name);
                            var ext = Path.GetExtension(name);
                            var from = RpfExplorer.SafeName(Path.GetFileNameWithoutExtension(fe.File?.Name ?? "rpf"));
                            name = stem + "." + from + ext;
                            taken.Add(name);
                        }
                        File.WriteAllBytes(Path.Combine(dlg.SelectedPath, name), data);
                        done++;
                        bytes += data.Length;
                    }
                    catch { failed++; }
                }
                panel.RpfStatus = $"extracted {done:N0} files ({bytes / 1024:N0} KB) to {dlg.SelectedPath}" +
                                  (failed > 0 ? $" - {failed:N0} could not be read" : "");
                Console.WriteLine($"RPF extracted {done} of {files.Count} listed files -> {dlg.SelectedPath}");
            }
            catch (Exception ex) { panel.RpfStatus = "extract failed: " + ex.Message; }
            finally { Cursor = Cursors.Default; }
        }

        private void SendRpfEntryToMloCreator_N4(RpfFileEntry e, string diskPath = null)
        {
            if (e == null) return;
            var shortName = Path.GetFileNameWithoutExtension(e.Name) ?? "";
            panel.SwitchWorkspace(LightPanel.Space.Mlo);
            var lib = Creator?.Assets;
            if (lib == null)
            {
                panel.RpfStatus = "the MLO Creator is not ready yet";
                return;
            }
            lib.Query = shortName;
            lib.SourceFilter = diskPath != null ? 0 : 1;
            lib.Dirty = true;
            Creator.Page = MloCreatorPanel.PageKind.Assets;
            panel.RpfStatus = $"{shortName} is in the MLO Creator's assets - pick a room and place it";
        }
    }
}

