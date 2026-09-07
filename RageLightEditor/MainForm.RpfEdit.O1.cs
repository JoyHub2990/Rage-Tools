using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool rpfEditScriptDone;
        private string rpfLastImportDir;

        partial void PrepareRpfSpace_O1()
        {
            var p = panel;
            if (p == null) return;

            var make = Environment.GetEnvironmentVariable("RLE_RPFMAKE");
            if (!string.IsNullOrWhiteSpace(make))
            {
                try
                {
                    var dir = make.Trim();
                    Directory.CreateDirectory(dir);
                    var arc = Path.Combine(dir, "made.rpf");
                    if (File.Exists(arc)) File.Delete(arc);
                    var made = CodeWalker.GameFiles.RpfFile.CreateNew(dir, arc, CodeWalker.GameFiles.RpfEncryption.OPEN);
                    RpfEdit.NewFolder(true, p.Rpf, new RpfEdit.Target(made.Root, null), "data");
                    Console.WriteLine($"RPFMAKE created {arc} with a data folder");
                }
                catch (Exception ex) { Console.WriteLine("RPFMAKE failed: " + ex.Message); }
            }

            var roots = Environment.GetEnvironmentVariable("RLE_RPFROOT");
            if (!string.IsNullOrWhiteSpace(roots))
            {
                foreach (var dir in roots.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var d = dir.Trim();
                    bool ok = p.Rpf.AddRootFolder(d);
                    Console.WriteLine($"RPFROOT {(ok ? "added" : "could not add")} {d}");
                }
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RLE_RPFEDIT")))
                p.RpfEditMode = true;
        }

        partial void ServiceRpfEdit_O1()
        {
            var p = panel;
            if (p == null) return;

            if (p.RequestRpfImport) { p.RequestRpfImport = false; ImportRpfFiles_O1(false); }
            if (p.RequestRpfImportRaw) { p.RequestRpfImportRaw = false; ImportRpfFiles_O1(true); }
            if (p.RequestRpfPasteOs) { p.RequestRpfPasteOs = false; PasteRpfFromClipboard_O1(); }
            if (p.RequestRpfOpenFolder) { p.RequestRpfOpenFolder = false; OpenRpfRootFolder_O1(); }
            if (p.RequestRpfOpenDiskFile != null)
            {
                var f = p.RequestRpfOpenDiskFile;
                p.RequestRpfOpenDiskFile = null;
                OpenRpfDiskFile_O1(f);
            }
            if (p.RequestRpfShowInExplorer != null)
            {
                var f = p.RequestRpfShowInExplorer;
                p.RequestRpfShowInExplorer = null;
                ShowInExplorer_O1(f);
            }

            if (!rpfEditScriptDone && rpfSpaceOpened && p.Rpf.Ready)
            {
                rpfEditScriptDone = true;
                RunRpfEditScript_O1(Environment.GetEnvironmentVariable("RLE_RPFEDIT"));
            }
        }

        private void ImportRpfFiles_O1(bool raw)
        {
            var p = panel;
            var target = RpfEdit.TargetOf(p.Rpf);
            if (!target.Valid) { p.RpfStatus = "walk into a folder first"; return; }
            if (!p.RpfEditMode) { p.RpfStatus = "edit mode is off - nothing was written"; return; }

            using var dlg = new OpenFileDialog
            {
                Title = raw ? "Import files (raw)" : "Import files",
                Multiselect = true,
                InitialDirectory = rpfLastImportDir,
                Filter = "All files|*.*",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            rpfLastImportDir = Path.GetDirectoryName(dlg.FileNames.Length > 0 ? dlg.FileNames[0] : "");

            try
            {
                Cursor = Cursors.WaitCursor;
                var res = RpfEdit.ImportFiles(p.RpfEditMode, p.Rpf, target, dlg.FileNames, raw);
                p.RpfStatus = res.Message;
                if (res.Ok && target.Dir?.File != null) p.Archive?.ReindexArchive(target.Dir.File);
                Console.WriteLine("RPFEDIT import: " + res.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void PasteRpfFromClipboard_O1()
        {
            var p = panel;
            var target = RpfEdit.TargetOf(p.Rpf);
            if (!target.Valid) { p.RpfStatus = "walk into a folder first"; return; }

            var files = new List<string>();
            try
            {
                var list = Clipboard.GetFileDropList();
                foreach (string f in list)
                {
                    if (f == null) continue;
                    if (File.Exists(f)) files.Add(f);
                    else if (Directory.Exists(f))
                    {
                        try { files.AddRange(Directory.GetFiles(f)); } catch { }
                    }
                }
            }
            catch (Exception ex) { p.RpfStatus = "could not read the clipboard: " + ex.Message; return; }

            if (files.Count == 0) { p.RpfStatus = "no files on the Windows clipboard"; return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                var res = RpfEdit.Paste(p.RpfEditMode, p.Rpf, target, files);
                p.RpfStatus = res.Message;
                if (res.Ok && target.Dir?.File != null) p.Archive?.ReindexArchive(target.Dir.File);
                Console.WriteLine("RPFEDIT paste: " + res.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void OpenRpfRootFolder_O1()
        {
            var p = panel;
            using var dlg = new FolderBrowserDialog
            {
                Description = "Show this folder in the tree beside the GTA V install",
                UseDescriptionForTitle = true,
                SelectedPath = rpfLastImportDir,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (!p.Rpf.AddRootFolder(dlg.SelectedPath))
            {
                p.RpfStatus = "could not open " + dlg.SelectedPath;
                return;
            }
            var sweep = SweepConvertXmls_V53(dlg.SelectedPath);
            if (sweep.Converted > 0) p.Rpf.RefreshCurrent_O1();
            p.RpfStatus = "opened " + dlg.SelectedPath + SweepNote_V53(sweep);
        }

        private void OpenRpfDiskFile_O1(string path)
        {
            var p = panel;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { p.RpfStatus = "that file is gone"; return; }
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();

            bool viewerTook = false;
            OpenDiskFileInModelViewer_P1(path, ref viewerTook);
            if (viewerTook) return;

            bool metaTook = false;
            OpenMetaFileInExplorer_Q1(path, ref metaTook);
            if (metaTook) return;

            try
            {
                switch (ext)
                {
                    default:
                        var fi = new FileInfo(path);
                        if (fi.Length > 8 * 1024 * 1024)
                        {
                            p.RpfStatus = Path.GetFileName(path) + " is too big to show as text - " +
                                          RpfExplorer.SizeText(fi.Length);
                            break;
                        }
                        p.ShowRpfText(Path.GetFileName(path), File.ReadAllText(path));
                        p.SetRpfViewSource_Q1(null, path);
                        p.RpfStatus = "opened " + Path.GetFileName(path);
                        break;
                }
            }
            catch (Exception ex) { p.RpfStatus = "could not open " + Path.GetFileName(path) + ": " + ex.Message; }
        }

        private void ShowInExplorer_O1(string path)
        {
            try
            {
                if (Directory.Exists(path)) Process.Start("explorer", "\"" + path + "\"");
                else Process.Start("explorer", "/select, \"" + path + "\"");
                panel.RpfStatus = "showed " + Path.GetFileName(path) + " in Windows Explorer";
            }
            catch (Exception ex) { panel.RpfStatus = "could not open Explorer: " + ex.Message; }
        }

        private void RunRpfEditScript_O1(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return;
            var p = panel;
            Console.WriteLine("RPFEDIT script: " + script);

            foreach (var raw in script.Split('|'))
            {
                var step = raw.Trim();
                if (step.Length == 0) continue;
                int colon = step.IndexOf(':');
                if (colon <= 0) { Console.WriteLine("RPFEDIT bad step: " + step); continue; }
                var op = step.Substring(0, colon).Trim().ToLowerInvariant();
                var rest = step.Substring(colon + 1).Trim();

                try { RunRpfEditStep_O1(op, rest); }
                catch (Exception ex) { Console.WriteLine($"RPFEDIT {op} threw: {ex.Message}"); }
            }
            Console.WriteLine("RPFEDIT done: " + p.RpfStatus);
        }

        private void RunRpfEditStep_O1(string op, string args)
        {
            var p = panel;
            var parts = SplitEditArgs_O1(args);

            switch (op)
            {
                case "edit":
                    p.RpfEditMode = !string.Equals(parts[0], "off", StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine("RPFEDIT edit mode " + (p.RpfEditMode ? "on" : "off"));
                    return;

                case "goto":
                    Console.WriteLine($"RPFEDIT goto {parts[0]} -> {(p.Rpf.GoToPath(parts[0]) ? "ok" : "FAILED")} " +
                                      $"[{p.Rpf.CurrentDisplayPath}]");
                    return;

                case "list":
                    if (parts[0].Length > 0 && !p.Rpf.GoToPath(parts[0]))
                    {
                        Console.WriteLine("RPFEDIT list FAILED to walk to " + parts[0]);
                        return;
                    }
                    p.Rpf.Invalidate();
                    p.Rpf.EnsureList();
                    Console.WriteLine($"RPFEDIT list {p.Rpf.CurrentDisplayPath}: {p.Rpf.Rows.Count} rows");
                    foreach (var r in p.Rpf.Rows)
                        Console.WriteLine($"RPFEDIT   {(r.IsFolder ? "[D]" : "[F]")} {r.Name} | {r.Type} | " +
                                          $"{RpfExplorer.SizeText(r.Size)} | {r.Attr}");
                    return;

                case "dialog":
                    Console.WriteLine($"RPFEDIT dialog {parts[0]}: " +
                                      (p.OpenRpfDialog_O1(parts[0]) ? "shown" : "NOT AVAILABLE"));
                    return;

                case "reopen":
                    ReopenArchiveFromDisk_O1(parts[0]);
                    return;
            }

            if (parts[0].Length > 0 && !p.Rpf.GoToPath(parts[0]))
            {
                if (op == "rename" || op == "delete" || op == "copy")
                {
                    var parent = ParentPath_O1(parts[0]);
                    if (parent.Length == 0 || !p.Rpf.GoToPath(parent))
                    {
                        Console.WriteLine($"RPFEDIT {op} FAILED to walk to {parts[0]}");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"RPFEDIT {op} FAILED to walk to {parts[0]}");
                    return;
                }
            }
            else if (op == "rename" || op == "delete" || op == "copy")
            {
                var parent = ParentPath_O1(parts[0]);
                if (parent.Length > 0) p.Rpf.GoToPath(parent);
            }

            var target = RpfEdit.TargetOf(p.Rpf);
            var archive = target.Dir?.File;

            if (RpfEdit.NeedsEncryptionChange(target))
            {
                var enc = RpfEdit.EncryptionOf(target);
                bool ok = RpfEdit.MakeEncryptionValid(target);
                Console.WriteLine($"RPFEDIT encryption {enc} -> OPEN: {(ok ? "done" : "FAILED")}");
                if (!ok) return;
            }

            RpfEdit.Result res;
            switch (op)
            {
                case "newfolder":
                    res = RpfEdit.NewFolder(p.RpfEditMode, p.Rpf, target, LastName_O1(parts));
                    break;
                case "newrpf":
                    res = RpfEdit.NewArchive(p.RpfEditMode, p.Rpf, target, LastName_O1(parts));
                    break;
                case "import":
                case "importraw":
                    res = RpfEdit.ImportFiles(p.RpfEditMode, p.Rpf, target,
                                              new[] { parts.Length > 1 ? parts[1] : "" }, op == "importraw");
                    break;
                case "paste":
                    res = RpfEdit.Paste(p.RpfEditMode, p.Rpf, target, null);
                    break;
                case "copy":
                    res = RpfEdit.Copy(FindRows_O1(NameOf_O1(parts[0])));
                    break;
                case "rename":
                    res = RpfEdit.Rename(p.RpfEditMode, p.Rpf,
                                         FindItem_O1(NameOf_O1(parts[0])),
                                         parts.Length > 1 ? parts[1] : "");
                    break;
                case "delete":
                    res = RpfEdit.Delete(p.RpfEditMode, p.Rpf, FindItem_O1(NameOf_O1(parts[0])));
                    break;
                default:
                    Console.WriteLine("RPFEDIT unknown op: " + op);
                    return;
            }

            if (res.Ok && archive != null) p.Archive?.ReindexArchive(archive);
            p.RpfStatus = res.Message;
            Console.WriteLine($"RPFEDIT {op} {(res.Ok ? "OK" : "FAILED")}: {res.Message}");
        }

        private void ReopenArchiveFromDisk_O1(string path)
        {
            var full = path;
            if (!File.Exists(full))
            {
                var gf = panel.Rpf.GameFolder;
                if (!string.IsNullOrEmpty(gf)) full = Path.Combine(gf, path);
            }
            if (!File.Exists(full)) { Console.WriteLine("RPFEDIT reopen: no such file " + path); return; }

            try
            {
                var rpf = new RpfFile(full, Path.GetFileName(full));
                rpf.ScanStructure(null, null);
                if (rpf.LastException != null || rpf.Root == null)
                {
                    Console.WriteLine("RPFEDIT reopen FAILED: " + (rpf.LastException?.Message ?? "no root"));
                    return;
                }
                Console.WriteLine($"RPFEDIT reopen {Path.GetFileName(full)} ({rpf.FileSize:N0} bytes, " +
                                  $"{rpf.Encryption} encryption)");
                DumpDir_O1(rpf.Root, 0);
            }
            catch (Exception ex) { Console.WriteLine("RPFEDIT reopen threw: " + ex.Message); }
        }

        private static void DumpDir_O1(RpfDirectoryEntry dir, int depth)
        {
            if (dir == null || depth > 8) return;
            var pad = new string(' ', depth * 2);
            foreach (var d in dir.Directories ?? new List<RpfDirectoryEntry>())
            {
                Console.WriteLine($"RPFEDIT   {pad}[D] {d.Name}");
                DumpDir_O1(d, depth + 1);
            }
            foreach (var f in dir.Files ?? new List<RpfFileEntry>())
                Console.WriteLine($"RPFEDIT   {pad}[F] {f.Name}  {f.GetFileSize():N0} bytes  " +
                                  $"{(f is RpfResourceFileEntry ? "resource" : "binary")}");
        }

        private static string[] SplitEditArgs_O1(string args)
        {
            var parts = (args ?? "").Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts.Length == 0 ? new[] { "" } : parts;
        }

        private static string ParentPath_O1(string path)
        {
            var p = (path ?? "").Replace('/', '\\').TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            return i > 0 ? p.Substring(0, i) : "";
        }

        private static string NameOf_O1(string path)
        {
            var p = (path ?? "").Replace('/', '\\').TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            return i >= 0 ? p.Substring(i + 1) : p;
        }

        private static string LastName_O1(string[] parts) =>
            parts.Length > 1 && parts[1].Length > 0 ? parts[1] : NameOf_O1(parts[0]);

        private List<RpfExplorer.Row> FindRows_O1(string name)
        {
            var list = new List<RpfExplorer.Row>();
            panel.Rpf.Invalidate();
            panel.Rpf.EnsureList();
            foreach (var r in panel.Rpf.Rows)
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) list.Add(r);
            return list;
        }

        private RpfEdit.Item FindItem_O1(string name)
        {
            var rows = FindRows_O1(name);
            return rows.Count > 0 ? RpfEdit.ItemOf(rows[0]) : default;
        }
    }
}

