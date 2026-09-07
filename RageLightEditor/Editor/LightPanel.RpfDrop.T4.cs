using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private readonly List<(Vector2 min, Vector2 max, RpfExplorer.Row row)> rpfRowRects_T4 =
            new List<(Vector2, Vector2, RpfExplorer.Row)>();
        private int rpfRowRectFrame_T4 = -1;

        public string LastDropArchive_T4;
        public string LastDropTarget_T4;
        public bool LastDropOk_T4;

        internal void RpfRowRect_T4(in RpfExplorer.Row r)
        {
            if (!r.IsFolder) return;
            int frame = ImGui.GetFrameCount();
            if (frame != rpfRowRectFrame_T4) { rpfRowRects_T4.Clear(); rpfRowRectFrame_T4 = frame; }
            rpfRowRects_T4.Add((ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), r));
        }

        public bool RpfFirstFolderRow_T4(out float x, out float y)
        {
            x = y = 0;
            if (rpfRowRects_T4.Count == 0) return false;
            var (mn, mx, _) = rpfRowRects_T4[0];
            x = (mn.X + mx.X) * 0.5f;
            y = (mn.Y + mx.Y) * 0.5f;
            return true;
        }

        private RpfExplorer.Row? RpfRowAt_T4(float x, float y)
        {
            for (int i = 0; i < rpfRowRects_T4.Count; i++)
            {
                var (mn, mx, row) = rpfRowRects_T4[i];
                if (x >= mn.X && x <= mx.X && y >= mn.Y && y <= mx.Y) return row;
            }
            return null;
        }

        public bool DropRpfFiles_T4(IReadOnlyList<string> paths, float x, float y)
        {
            if (!ArchiveMode) return false;
            if (paths == null || paths.Count == 0) { RpfStatus = "nothing was dropped"; return true; }

            if (ShowLeftPanel && x <= settings.LeftPanelWidth + HandleStripW)
                return DropOnRpfTree_V43(paths);

            if (!RpfEditMode)
            {
                var held = new List<string>(paths);
                float hx = x, hy = y;
                RpfStatus = $"{paths.Count:N0} dropped file{(paths.Count == 1 ? "" : "s")} waiting on edit mode";
                EnsureRpfWritable_U4("Dropping files in", () => LandRpfDrop_T4(held, hx, hy));
                return true;
            }

            return LandRpfDrop_T4(paths, x, y);
        }

        private bool LandRpfDrop_T4(IReadOnlyList<string> paths, float x, float y)
        {
            var hit = RpfRowAt_T4(x, y);
            RpfEdit.Target target;
            string where;
            if (hit.HasValue && TargetOfRow_T4(hit.Value, out target, out where)) { }
            else
            {
                target = RpfTarget_O1();
                where = target.Display;
            }
            if (!target.Valid) { RpfStatus = "walk into a folder first - a drop needs somewhere to land"; UiSound.InvalidDrop(); return true; }

            if (RpfEdit.NeedsEncryptionChange(target))
            {
                var enc = RpfEdit.EncryptionOf(target);
                var pending = new List<string>(paths);
                var t = target; var w = where;
                AskRpf_O1("Change RPF encryption type",
                          $"This archive is currently set to {enc} encryption.\n" +
                          "Nothing but the game itself can write into one, so importing a file means\n" +
                          "changing it to OPEN encryption first. Are you sure?\n\n" +
                          "Loading by the game will require a mod loader such as OpenRPF.asi or OpenIV.asi.",
                          "Change to OPEN and import",
                          () =>
                          {
                              if (!RpfEdit.MakeEncryptionValid(t))
                              {
                                  RpfStatus = "could not change the archive's encryption - nothing was imported";
                                  return;
                              }
                              RunRpfDrop_T4(pending, t, w);
                          });
                RpfStatus = $"{paths.Count:N0} file{(paths.Count == 1 ? "" : "s")} dropped on {where} - waiting on the encryption question";
                return true;
            }

            RunRpfDrop_T4(paths, target, where);
            return true;
        }

        public bool DropRpfFilesForTest_T4(IReadOnlyList<string> paths, string destFolder)
        {
            if (!RpfEditMode)
            {
                RpfStatus = $"edit mode is off - {paths.Count:N0} dropped file{(paths.Count == 1 ? " was" : "s were")} not written";
                return false;
            }
            RunRpfDrop_T4(paths, new RpfEdit.Target(null, destFolder), destFolder);
            return LastDropOk_T4;
        }

        public void ClearRpfSelectionForTest_T4() => ClearRpfSelection_O1();

        public bool SelectFirstRpfRowForTest_T4()
        {
            Rpf.EnsureList();
            if (Rpf.Rows.Count == 0)
            {
                SelectRpfRow_O1(new RpfExplorer.Row(null, null, "test.ydr", "Drawable", "test.ydr",
                                                    16, 16, false, false, false, "", true));
                return true;
            }
            SelectRpfRow_O1(Rpf.Rows[0]);
            return true;
        }

        private bool TargetOfRow_T4(in RpfExplorer.Row r, out RpfEdit.Target target, out string where)
        {
            target = default; where = "";
            if (!r.IsFolder) return false;
            if (r.IsFs)
            {
                if (r.EnterDir != null) { target = new RpfEdit.Target(r.EnterDir, null); where = r.EnterDir.Path; return true; }
                if (!string.IsNullOrEmpty(r.Path) && Directory.Exists(r.Path)) { target = new RpfEdit.Target(null, r.Path); where = r.Path; return true; }
                return false;
            }
            if (r.EnterDir == null) return false;
            target = new RpfEdit.Target(r.EnterDir, null);
            where = r.EnterDir.Path;
            return true;
        }

        private void RunRpfDrop_T4(IReadOnlyList<string> paths, RpfEdit.Target target, string where)
        {
            var files = new List<string>();
            var dirs = new List<string>();
            int missing = 0;
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (File.Exists(p)) files.Add(p);
                else if (Directory.Exists(p)) dirs.Add(p);
                else missing++;
            }

            var parts = new List<string>();
            bool anyOk = false;

            foreach (var d in dirs)
            {
                var r = ImportTree_T4(target, d, out int nfiles, out int nfolders);
                if (r.Ok) anyOk = true;
                parts.Add(r.Ok
                    ? $"{Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar))}\\ ({nfiles:N0} file{(nfiles == 1 ? "" : "s")}" +
                      (nfolders > 0 ? $" in {nfolders:N0} folder{(nfolders == 1 ? "" : "s")}" : "") + ")"
                    : r.Message);
            }

            if (files.Count > 0)
            {
                var r = RpfEdit.ImportFiles(RpfEditMode, Rpf, target, files, false);
                if (r.Ok) anyOk = true;
                parts.Add(r.Message);
            }

            if (missing > 0) parts.Add($"{missing:N0} dropped path{(missing == 1 ? " is" : "s are")} gone");
            if (parts.Count == 0) parts.Add("nothing usable was dropped");

            RpfStatus = (anyOk ? "dropped into " + where + ": " : "dropped on " + where + " - ") + string.Join("; ", parts);
            Console.WriteLine("RPFDROP " + RpfStatus);
            LastDropArchive_T4 = target.PhysicalArchive;
            LastDropTarget_T4 = where;
            LastDropOk_T4 = anyOk;
            if (anyOk) UiSound.Success(); else UiSound.InvalidDrop();

            if (anyOk && target.Dir?.File != null) Archive?.ReindexArchive(target.Dir.File);
            Rpf.RefreshCurrent_O1();
        }

        private RpfEdit.Result ImportTree_T4(RpfEdit.Target parent, string diskDir, out int nfiles, out int nfolders)
        {
            nfiles = 0; nfolders = 0;
            var name = Path.GetFileName(diskDir.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) return RpfEdit.Result.Fail("a folder with no name was dropped");
            if (!RpfEdit.IsFilenameOk(name)) return RpfEdit.Result.Fail("\"" + name + "\" is not a usable folder name");

            RpfEdit.Target inner;
            try
            {
                if (parent.InArchive)
                {
                    RpfDirectoryEntry dir = null;
                    if (parent.Dir.Directories != null)
                        foreach (var d in parent.Dir.Directories)
                            if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) { dir = d; break; }
                    if (dir == null)
                    {
                        RpfEdit.EnsureBackup(parent.PhysicalArchive);
                        dir = RpfFile.CreateDirectory(parent.Dir, name);
                    }
                    if (dir == null) return RpfEdit.Result.Fail("could not create " + name);
                    inner = new RpfEdit.Target(dir, null);
                }
                else
                {
                    var full = Path.Combine(parent.FsFolder, name);
                    Directory.CreateDirectory(full);
                    inner = new RpfEdit.Target(null, full);
                }
                nfolders++;
            }
            catch (Exception e) { return RpfEdit.Result.Fail("could not create " + name + ": " + e.Message); }

            string[] childFiles, childDirs;
            try { childFiles = Directory.GetFiles(diskDir); childDirs = Directory.GetDirectories(diskDir); }
            catch (Exception e) { return RpfEdit.Result.Fail("could not read " + name + ": " + e.Message); }

            if (childFiles.Length > 0)
            {
                var r = RpfEdit.ImportFiles(RpfEditMode, Rpf, inner, childFiles, false);
                if (r.Ok) nfiles += childFiles.Length;
                else if (childDirs.Length == 0) return r;
            }
            foreach (var cd in childDirs)
            {
                var r = ImportTree_T4(inner, cd, out int cf, out int cn);
                nfiles += cf; nfolders += cn;
                if (!r.Ok) return r;
            }
            return RpfEdit.Result.Done("imported " + name);
        }
    }
}

