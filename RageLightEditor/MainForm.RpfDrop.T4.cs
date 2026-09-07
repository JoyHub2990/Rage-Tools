using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void RpfDropFiles_T4(string[] paths, int screenX, int screenY, ref bool handled)
        {
            if (panel == null || !panel.ArchiveMode) return;
            if (rpfDragOutBusy_V55) { handled = true; return; }
            var p = PointToClient(new System.Drawing.Point(screenX, screenY));
            handled = panel.DropRpfFiles_T4(paths, p.X, p.Y);
        }

        private bool rpfDropDone_T4;
        private const char chrBS_T4 = '\\';
        private int rpfDropWait_T4 = 12;
        private int rpfDropSettle_T4 = 240;

        private void ServiceRpfDrop_T4()
        {
            if (rpfDropDone_T4) return;
            var spec = Environment.GetEnvironmentVariable("RLE_RPFDROP");
            if (string.IsNullOrWhiteSpace(spec)) { rpfDropDone_T4 = true; return; }
            var p = panel;
            if (p == null || !p.ArchiveMode || !p.Rpf.Ready || !rpfSpaceOpened) return;
            if (rpfDropWait_T4-- > 0)
            {
                if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3);
                return;
            }
            rpfDropDone_T4 = true;

            var wantPath = Environment.GetEnvironmentVariable("RLE_RPFPATH");
            if (!string.IsNullOrWhiteSpace(wantPath))
            {
                bool went = p.Rpf.GoToPath(wantPath.Trim());
                Console.WriteLine("RPFDROP navigate to " + wantPath.Trim() + " -> " + (went ? "there" : "NOT THERE") + ", now at " + p.Rpf.CurrentDisplayPath);
                p.Rpf.EnsureList();
            }
            var tgt = RpfEdit.TargetOf(p.Rpf);
            Console.WriteLine("RPFDROP target: archiveDir=" + (tgt.Dir?.Path ?? "-") + " diskFolder=" + (tgt.FsFolder ?? "-") + " inArchive=" + tgt.InArchive + " valid=" + tgt.Valid);

            var hereNow = p.Rpf.CurrentDisplayPath ?? "";
            if (!string.IsNullOrEmpty(DebugGtaFolder) &&
                hereNow.Replace(chrBS_T4, '/').IndexOf(DebugGtaFolder.Replace(chrBS_T4, '/'), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("RPFDROP REFUSED: the explorer is inside the game install ('" + hereNow + "'). A test never writes there.");
                return;
            }

            bool onRow = false, noEdit = false;
            var body = spec.Trim();
            if (body.StartsWith("row:", StringComparison.OrdinalIgnoreCase)) { onRow = true; body = body.Substring(4); }
            else if (body.StartsWith("noedit:", StringComparison.OrdinalIgnoreCase)) { noEdit = true; body = body.Substring(7); }
            if (!noEdit) p.RpfEditMode = true;
            var paths = body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < paths.Length; i++) paths[i] = paths[i].Trim();

            System.Drawing.Point client;
            if (onRow && p.RpfFirstFolderRow_T4(out float rx, out float ry))
                client = new System.Drawing.Point((int)rx, (int)ry);
            else
                client = new System.Drawing.Point(ClientSize.Width / 2, ClientSize.Height / 2);
            var screen = PointToScreen(client);

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, paths);
            var args = new DragEventArgs(data, 0, screen.X, screen.Y,
                                         DragDropEffects.Copy, DragDropEffects.Copy);
            Console.WriteLine($"RPFDROP dropping {paths.Length:N0} path(s) at client {client.X},{client.Y} " +
                              $"({(onRow ? "on a folder row" : "on the list background")}), folder '{p.Rpf.CurrentDisplayPath}'");
            OnDragDrop(this, args);
            Console.WriteLine($"RPFDROP status: {p.RpfStatus}");
            Console.WriteLine($"RPFDROP landed in '{p.LastDropTarget_T4 ?? "-"}' ok={p.LastDropOk_T4} " +
                              $"archive={p.LastDropArchive_T4 ?? "(a folder on disk)"}");
            var pending = p.PendingRpfConfirm_U4;
            if (pending != null)
            {
                Console.WriteLine("RPFDROP the tool asked first: " + pending);
                Console.WriteLine("RPFDROP said yes -> " + p.AcceptRpfConfirmForTest_U4());
                Console.WriteLine("RPFDROP status: " + p.RpfStatus);
                Console.WriteLine("RPFDROP landed in " + (p.LastDropTarget_T4 ?? "-") + " ok=" + p.LastDropOk_T4 +
                                  " archive=" + (p.LastDropArchive_T4 ?? "(a folder on disk)"));
            }
            if (!string.IsNullOrEmpty(p.LastDropArchive_T4)) ReopenArchiveFromDisk_O1(p.LastDropArchive_T4);
            else ListDroppedFolder_T4(p.LastDropTarget_T4);
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 6);
        }

        private bool rpfDelKeyDone_T4;
        private int rpfDelKeyWait_T4 = 14;

        private void ServiceRpfDeleteKey_T4()
        {
            if (rpfDelKeyDone_T4) return;
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RLE_RPFDELKEY"))) { rpfDelKeyDone_T4 = true; return; }
            var p = panel;
            if (p == null || !p.ArchiveMode || !p.Rpf.Ready || !rpfSpaceOpened) return;
            if (rpfDelKeyWait_T4-- > 0)
            {
                if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3);
                return;
            }
            rpfDelKeyDone_T4 = true;
            p.RpfEditMode = true;
            bool picked = p.SelectFirstRpfRowForTest_T4();
            bool was = ignoreImGuiKeyboard;
            ignoreImGuiKeyboard = true;
            try { OnKeyDownEv(this, new KeyEventArgs(settings.GetBind("Delete"))); }
            finally { ignoreImGuiKeyboard = was; }
            Console.WriteLine($"RPFDELKEY selected={picked} status='{p.RpfStatus}'");
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 8);
        }

        private void ListDroppedFolder_T4(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { Console.WriteLine("RPFDROP no folder to list"); return; }
            foreach (var d in Directory.GetDirectories(dir))
            {
                Console.WriteLine($"RPFDROP disk   [D] {Path.GetFileName(d)}");
                foreach (var f in Directory.GetFiles(d))
                    Console.WriteLine($"RPFDROP disk     [F] {Path.GetFileName(f)}  {new FileInfo(f).Length:N0} bytes");
            }
            foreach (var f in Directory.GetFiles(dir))
                Console.WriteLine($"RPFDROP disk   [F] {Path.GetFileName(f)}  {new FileInfo(f).Length:N0} bytes");
        }
    }
}

