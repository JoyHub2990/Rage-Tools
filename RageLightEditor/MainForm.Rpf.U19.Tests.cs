using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_Rpf_U19(Action<string, bool, string> check)
        {
            var p = panel;
            if (p == null) { Console.WriteLine("  u19 rpf multi-select: (skipped - no panel)"); return; }
            var was = p.Workspace;
            bool wasEdit = p.RpfEditMode;
            string dir = null;
            try
            {
                p.SwitchWorkspace(LightPanel.Space.Archive);
                if (!p.Rpf.Ready) p.Rpf.BuildFromGameFolder(p.RpfGameFolder, p.Archive);

                dir = Path.Combine(Path.GetTempPath(), "rle_u19_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(Path.Combine(dir, "sub"));
                File.WriteAllText(Path.Combine(dir, "sub", "inner.txt"), "inner");
                foreach (var n in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
                    File.WriteAllText(Path.Combine(dir, n), n + " body");
                var arcPath = Path.Combine(dir, "pack.rpf");
                var rpf = RpfFile.CreateNew(dir, arcPath, RpfEncryption.OPEN);
                var ex = new RpfExplorer();
                RpfEdit.ImportFiles(true, ex, new RpfEdit.Target(rpf.Root, null),
                                    new[] { Path.Combine(dir, "a.txt"), Path.Combine(dir, "b.txt"), Path.Combine(dir, "c.txt") }, true);

                check("u19 rpf: the test folder opens in the tree", p.Rpf.AddRootFolder(dir) && p.Rpf.GoToPath(dir), p.Rpf.CurrentDisplayPath);
                p.ClearRpfSelectionForTest_T4();
                p.Rpf.Invalidate();
                p.Rpf.EnsureList();
                var rows = p.Rpf.Rows;
                check("u19 rpf: the listing has the folder, the archive, its backup and the four files", rows.Count == 7 && File.Exists(arcPath + ".bak"), rows.Count.ToString());
                int First(string name) { for (int i = 0; i < rows.Count; i++) if (string.Equals(rows[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
                int ia = First("a.txt"), ib = First("b.txt"), ic = First("c.txt"), id = First("d.txt"), isub = First("sub");

                p.ClickRpfRow_U19(rows[ia], ia, false, false);
                check("u19 rpf: a plain click selects one", p.RpfSelectedCount_U19 == 1 && p.Rpf.Selected == null, p.RpfSelectedCount_U19.ToString());
                p.ClickRpfRow_U19(rows[ic], ic, true, false);
                check("u19 rpf: Ctrl+click adds a second", p.RpfSelectedCount_U19 == 2 && p.RpfMultiSelected_U19, p.RpfSelectionSummary_U19());
                p.ClickRpfRow_U19(rows[ic], ic, true, false);
                check("u19 rpf: Ctrl+click again takes it away", p.RpfSelectedCount_U19 == 1, p.RpfSelectionSummary_U19());
                p.ClickRpfRow_U19(rows[id], id, false, true);
                check("u19 rpf: Shift+click ranges from the row clicked last", p.RpfSelectedCount_U19 == Math.Abs(id - ic) + 1, p.RpfSelectionSummary_U19());
                p.ClickRpfRow_U19(rows[ia], ia, false, false);
                p.ClickRpfRow_U19(rows[id], id, false, true);
                int span = Math.Abs(id - ia) + 1;
                check("u19 rpf: a plain click then Shift+click takes the whole run between them", p.RpfSelectedCount_U19 == span && span == 4, p.RpfSelectionSummary_U19());
                p.ClickRpfRow_U19(rows[isub], isub, true, false);
                check("u19 rpf: Ctrl+click adds a folder on top of the range", p.RpfSelectedCount_U19 == span + 1 && p.RpfSelectionSummary_U19().Contains("folder"), p.RpfSelectionSummary_U19());
                int all = p.SelectAllRpfRows_U19();
                check("u19 rpf: Ctrl+A takes everything listed", all == rows.Count && p.RpfSelectedCount_U19 == rows.Count, p.RpfSelectionSummary_U19());
                p.ClickRpfRow_U19(rows[ib], ib, false, false);
                check("u19 rpf: a plain click collapses it to one again", p.RpfSelectedCount_U19 == 1 && string.Equals(p.SelectedRpfRowsForTest_U19()[0].Name, "b.txt", StringComparison.OrdinalIgnoreCase), p.RpfSelectionSummary_U19());

                var pick = rows.Where(r => r.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || r.IsFolder).ToList();
                var dest = Path.Combine(dir, "out");
                var tally = ExtractRows_U19(pick, dest);
                check("u19 rpf: extracting a mixed selection writes the files, the folder and the archive's contents",
                      tally.Files == 4 + 1 + 3 && tally.Folders == 2 && tally.Failed == 0 &&
                      File.Exists(Path.Combine(dest, "a.txt")) && File.Exists(Path.Combine(dest, "sub", "inner.txt")) && File.Exists(Path.Combine(dest, "pack.rpf", "b.txt")),
                      $"{tally.Files} files, {tally.Folders} folders, {tally.Failed} failed");
                check("u19 rpf: the extract report reads well", ExtractTallyText_U19(tally, dest).StartsWith("extracted 8 files in 2 folders"), ExtractTallyText_U19(tally, dest));

                check("u19 rpf: the archive walks like a folder", p.Rpf.GoToPath(Path.Combine(dir, "pack.rpf")), p.Rpf.CurrentDisplayPath);
                p.Rpf.Invalidate();
                p.Rpf.EnsureList();
                int inArc = p.SelectAllRpfRows_U19();
                check("u19 rpf: inside the archive select-all takes its three files", inArc == 3, inArc.ToString());
                var arcRows = p.SelectedRpfRowsForTest_U19();
                p.ClickRpfRow_U19(arcRows[0], 0, false, false);
                p.ClickRpfRow_U19(arcRows[1], 1, true, false);
                p.RpfEditMode = true;
                var res = p.DeleteSelectedRpfRowsForTest_U19();
                var again = new RpfFile(arcPath, "pack.rpf");
                again.ScanStructure(null, null);
                check("u19 rpf: deleting two of them leaves one in the archive on disk", res.Ok && again.Root?.Files?.Count == 1, res.Message + " / " + (again.Root?.Files?.Count ?? -1));

                p.RpfEditMode = false;
                var offRes = RpfEdit.Delete(false, p.Rpf, RpfEdit.ItemOf(p.Rpf.Rows.Count > 0 ? p.Rpf.Rows[0] : default));
                check("u19 rpf: with edit mode off a delete writes nothing", !offRes.Ok, offRes.Message);
            }
            catch (Exception ex) { check("u19 rpf multi-select", false, ex.ToString()); }
            finally
            {
                p.RpfEditMode = wasEdit;
                try
                {
                    var root = p.Rpf.Roots.FirstOrDefault(r => r.IsRoot && string.Equals(r.FsPath, dir, StringComparison.OrdinalIgnoreCase));
                    if (root != null) p.Rpf.CloseRootFolder(root);
                    p.ClearRpfSelectionForTest_T4();
                }
                catch { }
                p.SwitchWorkspace(was);
                try { if (dir != null) Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
