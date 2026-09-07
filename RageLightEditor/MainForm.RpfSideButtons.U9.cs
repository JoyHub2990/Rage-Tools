using System;
using System.IO;
using System.Windows.Forms;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private const uint WM_XBUTTONDOWN_U9 = 0x020B, WM_XBUTTONUP_U9 = 0x020C;

        partial void RpfSideButtons_U9(MouseEventArgs e, ref bool handled)
        {
            if (panel == null || !panel.ArchiveMode || panel.Rpf == null || !panel.Rpf.Ready) return;
            if (e.Button == MouseButtons.XButton1) { StepRpfHistory_U9(-1); handled = true; }
            else if (e.Button == MouseButtons.XButton2) { StepRpfHistory_U9(+1); handled = true; }
        }

        private bool StepRpfHistory_U9(int dir)
        {
            var p = panel;
            var ex = p.Rpf;
            if (dir < 0)
            {
                if (!ex.CanGoBack) { p.RpfStatus = "nothing to go back to"; UiSound.Blocked(); return false; }
                ex.GoBack();
                p.RpfStatus = "back to " + ShortRpfPlace_U9(ex.CurrentDisplayPath);
                return true;
            }
            if (!ex.CanGoForward) { p.RpfStatus = "nothing to go forward to"; UiSound.Blocked(); return false; }
            ex.GoForward();
            p.RpfStatus = "forward to " + ShortRpfPlace_U9(ex.CurrentDisplayPath);
            return true;
        }

        private static string ShortRpfPlace_U9(string path)
        {
            if (string.IsNullOrEmpty(path)) return "the top";
            var trimmed = path.TrimEnd('\\', '/');
            int cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            return cut >= 0 && cut < trimmed.Length - 1 ? trimmed.Substring(cut + 1) : trimmed;
        }

        private int rpfXbtnStep_U9 = -1;
        private int rpfXbtnWait_U9;

        private void ServiceRpfSideButtonProbe_U9()
        {
            if (rpfXbtnStep_U9 >= 99) return;
            if (rpfXbtnStep_U9 < 0)
            {
                if (Environment.GetEnvironmentVariable("RLE_RPFXBTN") != "1") { rpfXbtnStep_U9 = 99; return; }
                rpfXbtnStep_U9 = 0;
            }
            var p = panel;
            if (p == null || !p.ArchiveMode || !p.Rpf.Ready || !rpfSpaceOpened) return;
            if (++rpfXbtnWait_U9 < 30) { if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3); return; }
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 4);

            int cx = ClientSize.Width / 2, cy = ClientSize.Height / 2;
            IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
            switch (rpfXbtnStep_U9)
            {
                case 0:
                    Console.WriteLine($"RPFXBTN start at '{p.Rpf.CurrentDisplayPath}' canBack={p.Rpf.CanGoBack} canForward={p.Rpf.CanGoForward}");
                    SendMessage(Handle, WM_XBUTTONDOWN_U9, (IntPtr)((1 << 16) | 0x0020), lp);
                    SendMessage(Handle, WM_XBUTTONUP_U9, (IntPtr)(1 << 16), lp);
                    rpfXbtnStep_U9 = 1;
                    break;
                case 1:
                    Console.WriteLine($"RPFXBTN after side button 1 (back): '{p.Rpf.CurrentDisplayPath}' status='{p.RpfStatus}'");
                    SendMessage(Handle, WM_XBUTTONDOWN_U9, (IntPtr)((2 << 16) | 0x0040), lp);
                    SendMessage(Handle, WM_XBUTTONUP_U9, (IntPtr)(2 << 16), lp);
                    rpfXbtnStep_U9 = 2;
                    break;
                case 2:
                    Console.WriteLine($"RPFXBTN after side button 2 (forward): '{p.Rpf.CurrentDisplayPath}' status='{p.RpfStatus}'");
                    rpfXbtnStep_U9 = 99;
                    if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 8);
                    break;
            }
        }

        private void SeqTest_RpfSideButtons_U9(Action<string, bool, string> check)
        {
            var p = panel;
            if (p == null) { Console.WriteLine("  u9 side buttons: (skipped - no panel)"); return; }
            var was = p.Workspace;
            string dir = null;
            try
            {
                p.SwitchWorkspace(LightPanel.Space.Archive);
                if (!p.Rpf.Ready) p.Rpf.BuildFromGameFolder(p.RpfGameFolder, p.Archive);

                dir = Path.Combine(Path.GetTempPath(), "rle_u9_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var inside = Path.Combine(dir, "inside");
                Directory.CreateDirectory(inside);
                File.WriteAllText(Path.Combine(inside, "note.txt"), "x");

                p.DropRpfFiles_T4(new[] { dir }, 8, 300);
                bool walked = p.Rpf.GoToPath(Path.GetFileName(dir) + "\\inside");
                var deep = p.Rpf.CurrentDisplayPath ?? "";
                check("u9 side buttons: the test walks into a folder so there is history to step through",
                      walked && deep.IndexOf("inside", StringComparison.OrdinalIgnoreCase) >= 0 && p.Rpf.CanGoBack,
                      deep);

                bool handled = false;
                RpfSideButtons_U9(new MouseEventArgs(MouseButtons.XButton1, 1, 0, 0, 0), ref handled);
                var back = p.Rpf.CurrentDisplayPath ?? "";
                check("u9 side buttons: the back button steps out to the folder above",
                      handled && back.IndexOf("inside", StringComparison.OrdinalIgnoreCase) < 0 &&
                      (p.RpfStatus ?? "").StartsWith("back to"),
                      $"{back}  ({p.RpfStatus})");

                handled = false;
                RpfSideButtons_U9(new MouseEventArgs(MouseButtons.XButton2, 1, 0, 0, 0), ref handled);
                var fwd = p.Rpf.CurrentDisplayPath ?? "";
                check("u9 side buttons: the forward button walks back into it",
                      handled && fwd.IndexOf("inside", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      (p.RpfStatus ?? "").StartsWith("forward to"),
                      $"{fwd}  ({p.RpfStatus})");

                handled = false;
                RpfSideButtons_U9(new MouseEventArgs(MouseButtons.XButton2, 1, 0, 0, 0), ref handled);
                check("u9 side buttons: with nothing ahead it says so instead of moving",
                      handled && string.Equals(p.Rpf.CurrentDisplayPath, fwd, StringComparison.OrdinalIgnoreCase) &&
                      (p.RpfStatus ?? "").Contains("nothing to go forward"),
                      p.RpfStatus ?? "");

                handled = false;
                RpfSideButtons_U9(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0), ref handled);
                check("u9 side buttons: the ordinary buttons are left alone", !handled, "left click not taken");

                p.SwitchWorkspace(LightPanel.Space.World);
                handled = false;
                RpfSideButtons_U9(new MouseEventArgs(MouseButtons.XButton1, 1, 0, 0, 0), ref handled);
                check("u9 side buttons: outside the RPF explorer the side buttons do nothing", !handled, "world left alone");
            }
            catch (Exception ex) { check("u9 side buttons", false, ex.Message); }
            finally
            {
                p.SwitchWorkspace(was);
                try { if (dir != null) Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
