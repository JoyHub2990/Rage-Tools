using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_O1(Action<string, bool, string> check)
        {
            RpfEditGuardsTest_O1(check);
            RpfWriteGateTest_U4(check);
            RpfEditArchiveTest_O1(check);
            RpfTreeTest_O1(check);
        }

        private static void RpfEditGuardsTest_O1(Action<string, bool, string> check)
        {
            check("rpf edit name check",
                  RpfEdit.IsFilenameOk("dlc.rpf") && RpfEdit.IsFilenameOk("prop_bench_01.ydr") &&
                  !RpfEdit.IsFilenameOk("") && !RpfEdit.IsFilenameOk("   ") &&
                  !RpfEdit.IsFilenameOk("a\\b") && !RpfEdit.IsFilenameOk("a:b") &&
                  !RpfEdit.IsFilenameOk("a*b"), "");

            var ex = new RpfExplorer();
            var nowhere = new RpfEdit.Target(null, null);
            var off = RpfEdit.NewFolder(false, ex, nowhere, "x");
            check("rpf edit mode off refuses", !off.Ok && off.Message.Contains("edit mode is off"), off.Message);

            var noTarget = RpfEdit.NewFolder(true, ex, nowhere, "x");
            check("rpf no target refuses", !noTarget.Ok, noTarget.Message);

            check("rpf attributes text",
                  RpfExplorer.AttrOf_O1((RpfFile)null) == "" &&
                  RpfExplorer.AttrOf_O1((RpfFileEntry)null) == "", "");
        }

        private static void RpfWriteGateTest_U4(Action<string, bool, string> check)
        {
            check("rpf import stays clickable with edit mode off, so it can offer to turn it on",
                  RpfWriteGate_U4.Enabled(true, false) &&
                  RpfWriteGate_U4.NeedsAsk(false, true, false) &&
                  RpfWriteGate_U4.Why(false, true, false) == RpfWriteGate_U4.NeedEditMode,
                  RpfWriteGate_U4.Why(false, true, false));

            check("rpf import with edit mode on just imports",
                  RpfWriteGate_U4.Enabled(true, false) &&
                  !RpfWriteGate_U4.NeedsAsk(true, true, false) &&
                  RpfWriteGate_U4.Why(true, true, false) == null, "");

            check("rpf import at the top of the tree is off, and says why",
                  !RpfWriteGate_U4.Enabled(false, false) &&
                  !RpfWriteGate_U4.NeedsAsk(true, false, false) &&
                  RpfWriteGate_U4.Why(true, false, false) == RpfWriteGate_U4.NeedFolder,
                  RpfWriteGate_U4.Why(true, false, false));

            check("rpf import during a search is off, and says why",
                  !RpfWriteGate_U4.Enabled(true, true) &&
                  RpfWriteGate_U4.Why(true, true, true) == RpfWriteGate_U4.NeedSearchOff,
                  RpfWriteGate_U4.Why(true, true, true));

            var root = Path.Combine(Path.GetTempPath(), "rle_seq_rpfdrop");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try
            {
                Directory.CreateDirectory(root);
                var arcPath = Path.Combine(root, "drop.rpf");
                var rpf = RpfFile.CreateNew(root, arcPath, RpfEncryption.OPEN);
                var ex = new RpfExplorer();
                RpfEdit.NewFolder(true, ex, new RpfEdit.Target(rpf.Root, null), "data");
                var dir = rpf.Root.Directories.FirstOrDefault(d => d.NameLower == "data");
                var src = Path.Combine(root, "dropped.txt");
                File.WriteAllText(src, "dropped in");

                var off = RpfEdit.ImportFiles(false, ex, new RpfEdit.Target(dir, null), new[] { src }, true);
                check("rpf a dropped file is not written while edit mode is off",
                      !off.Ok && dir.Files.All(f => f.NameLower != "dropped.txt"), off.Message);

                var on = RpfEdit.ImportFiles(true, ex, new RpfEdit.Target(dir, null), new[] { src }, true);
                check("rpf saying yes to edit mode lands the same dropped file in the archive",
                      on.Ok && dir.Files.Any(f => f.NameLower == "dropped.txt"), on.Message);

                var again = new RpfFile(arcPath, "drop.rpf");
                again.ScanStructure(null, null);
                var rdir = again.Root?.Directories?.FirstOrDefault(d => d.NameLower == "data");
                check("rpf the dropped file is still there when the archive is opened again",
                      again.LastException == null && rdir?.Files?.Any(f => f.NameLower == "dropped.txt") == true,
                      again.LastException?.Message ?? "");
            }
            catch (Exception e) { check("rpf drop into an archive", false, e.Message); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private static void RpfEditArchiveTest_O1(Action<string, bool, string> check)
        {
            var root = Path.Combine(Path.GetTempPath(), "rle_seq_rpfedit");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try
            {
                Directory.CreateDirectory(root);
                var arcPath = Path.Combine(root, "seq.rpf");
                var rpf = RpfFile.CreateNew(root, arcPath, RpfEncryption.OPEN);
                var ex = new RpfExplorer();

                var res = RpfEdit.NewFolder(true, ex, new RpfEdit.Target(rpf.Root, null), "data");
                var dir = rpf.Root.Directories.FirstOrDefault(d => d.NameLower == "data");
                check("rpf create folder in an archive", res.Ok && dir != null, res.Message);
                if (dir == null) return;

                var src = Path.Combine(root, "seq.txt");
                var body = "wso1 seq test\r\n";
                File.WriteAllText(src, body);
                res = RpfEdit.ImportFiles(true, ex, new RpfEdit.Target(dir, null), new[] { src }, true);
                check("rpf import a file", res.Ok && dir.Files.Any(f => f.NameLower == "seq.txt"), res.Message);

                check("rpf first write keeps a .bak", File.Exists(arcPath + ".bak"), "");

                var fe = dir.Files.First(f => f.NameLower == "seq.txt");
                res = RpfEdit.Rename(true, ex, new RpfEdit.Item(fe, null, null, false, "seq.txt"), "kept.txt");
                check("rpf rename an entry",
                      res.Ok && dir.Files.Any(f => f.NameLower == "kept.txt"), res.Message);

                RpfEdit.NewFolder(true, ex, new RpfEdit.Target(rpf.Root, null), "gone");
                var goneDir = rpf.Root.Directories.First(d => d.NameLower == "gone");
                res = RpfEdit.Delete(true, ex, new RpfEdit.Item(goneDir, null, null, true, "gone"));
                check("rpf delete a folder", res.Ok && !rpf.Root.Directories.Any(d => d.NameLower == "gone"),
                      res.Message);

                var again = new RpfFile(arcPath, "seq.rpf");
                again.ScanStructure(null, null);
                var rdir = again.Root?.Directories?.FirstOrDefault(d => d.NameLower == "data");
                var rfile = rdir?.Files?.FirstOrDefault(f => f.NameLower == "kept.txt");
                var read = rfile != null ? Encoding.ASCII.GetString(again.ExtractFile(rfile) ?? Array.Empty<byte>()) : "";
                check("rpf re-opened archive is intact",
                      again.LastException == null && rdir != null && rfile != null && read == body,
                      again.LastException?.Message ?? ("read " + read.Length + " chars"));
                check("rpf re-opened archive lost the deleted folder",
                      again.Root?.Directories?.Any(d => d.NameLower == "gone") != true, "");
            }
            catch (Exception e) { check("rpf archive edit round trip", false, e.Message); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private static void RpfTreeTest_O1(Action<string, bool, string> check)
        {
            var root = Path.Combine(Path.GetTempPath(), "rle_seq_rpftree");
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "update", "x64"));
                File.WriteAllText(Path.Combine(root, "ReadMe.txt"), "hello");
                RpfFile.CreateNew(root, Path.Combine(root, "x64a.rpf"), RpfEncryption.OPEN);

                var ex = new RpfExplorer();
                ex.BuildFromGameFolder(root, null);
                check("rpf tree root is the install folder",
                      ex.Ready && ex.GameRoot != null && ex.GameRoot.IsFs && ex.GameRoot.Label == "GTA V",
                      ex.GameRoot?.Label ?? "none");

                ex.Go(ex.GameRoot);
                ex.EnsureList();
                var names = ex.Rows.Select(r => r.Name.ToLowerInvariant()).ToList();
                check("rpf tree lists folders, loose files and archives",
                      names.Contains("update") && names.Contains("readme.txt") && names.Contains("x64a.rpf"),
                      string.Join(",", names));
                check("rpf tree puts folders first", ex.Rows.Count > 0 && ex.Rows[0].IsFolder,
                      ex.Rows.Count > 0 ? ex.Rows[0].Name : "empty");

                var arc = ex.Rows.First(r => r.Name.ToLowerInvariant() == "x64a.rpf");
                check("rpf tree: an archive is a folder with an encryption attribute",
                      arc.IsFolder && arc.Attr == "OPEN encryption", arc.Attr);
                var txt = ex.Rows.First(r => r.Name.ToLowerInvariant() == "readme.txt");
                check("rpf tree: a loose file gets a type and a size",
                      !txt.IsFolder && txt.Type == "Text File" && txt.Size == 5,
                      txt.Type + " / " + txt.Size);

                check("rpf tree: walk into an archive by path",
                      ex.GoToPath("x64a.rpf") && ex.Current?.Archive != null, ex.CurrentDisplayPath);
                check("rpf tree: walk into a subfolder by path",
                      ex.GoToPath("update\\x64") && ex.Current != null && ex.Current.IsFs,
                      ex.CurrentDisplayPath);

                check("rpf tree: inside the install is detected",
                      ex.CurrentIsInGameFolder && !ex.CurrentIsInModsFolder, ex.CurrentPhysicalPath ?? "");

                Directory.CreateDirectory(Path.Combine(root, "mods", "update"));
                ex.RefreshNode_O1(ex.GameRoot);
                check("rpf tree: mods\\ is called out separately",
                      ex.GoToPath("mods\\update") && ex.CurrentIsInModsFolder, ex.CurrentDisplayPath);
                check("rpf tree: the mods folder itself is not base game files",
                      ex.GoToPath("mods") && ex.CurrentIsInModsFolder, ex.CurrentDisplayPath);

                var other = Path.Combine(Path.GetTempPath(), "rle_seq_rpfother");
                Directory.CreateDirectory(other);
                check("rpf tree: open a folder beside the install",
                      ex.AddRootFolder(other) && ex.Roots.Count == 2, ex.Roots.Count.ToString());
                var extra = ex.Roots.Last();
                check("rpf tree: an opened folder is not base game files",
                      ReferenceEquals(ex.Current, extra) && !ex.CurrentIsInGameFolder, ex.CurrentDisplayPath);
                check("rpf tree: close an opened folder",
                      ex.CloseRootFolder(extra) && ex.Roots.Count == 1, ex.Roots.Count.ToString());
                try { Directory.Delete(other, true); } catch { }
                check("rpf tree: the install root cannot be closed",
                      !ex.CloseRootFolder(ex.GameRoot) && ex.Roots.Count == 1, ex.Roots.Count.ToString());
            }
            catch (Exception e) { check("rpf tree", false, e.Message); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}

