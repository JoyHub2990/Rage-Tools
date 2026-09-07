using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int snapProbeStep_U5 = -1;
        private int snapProbeFrame_U5;
        private int snapProbeClicks_U5;
        private bool snapProbePlus_U5 = true;
        private int snapProbeWant_U5 = 3;

        partial void OnWorldTick_SnapProbe_U5()
        {
            if (snapProbeStep_U5 < 0)
            {
                var spec = Environment.GetEnvironmentVariable("RLE_TBCLICK");
                if (string.IsNullOrWhiteSpace(spec)) { snapProbeStep_U5 = 99; return; }
                spec = spec.Trim().ToLowerInvariant();
                snapProbePlus_U5 = !spec.StartsWith("minus");
                var bits = spec.Split(':');
                if (bits.Length > 1 && int.TryParse(bits[1], out int n)) snapProbeWant_U5 = Math.Max(1, n);
                snapProbeStep_U5 = 0;
                ImGuiBackend.ImGuiInput.SuppressMouseLeave = true;
            }
            if (snapProbeStep_U5 >= 99 || panel == null || !panel.WorldMode) return;
            snapProbeFrame_U5++;
            if (snapProbeFrame_U5 < 40) return;
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 4);

            if (!panel.SnapButtonRect_U5(snapProbePlus_U5, out var centre))
            {
                if (snapProbeFrame_U5 > 300)
                {
                    Console.WriteLine("TBCLICK the snap buttons were never drawn");
                    snapProbeStep_U5 = 99;
                }
                return;
            }

            int cx = (int)centre.X, cy = (int)centre.Y;
            IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));

            switch (snapProbeStep_U5)
            {
                case 0:
                    Console.WriteLine($"TBCLICK clicking {(snapProbePlus_U5 ? "+" : "-")} at {cx},{cy}, " +
                                      $"toolbar shows '{panel.SnapLabelDrawn_U5}' (setting {settings.RotateSnapDeg:0.##})");
                    SendMessage(Handle, WM_MOUSEMOVE, IntPtr.Zero, lp);
                    snapProbeStep_U5 = 1;
                    break;
                case 1:
                    SendMessage(Handle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lp);
                    snapProbeStep_U5 = 2;
                    break;
                case 2:
                    SendMessage(Handle, WM_LBUTTONUP, IntPtr.Zero, lp);
                    snapProbeStep_U5 = 3;
                    break;
                case 3:
                    snapProbeClicks_U5++;
                    Console.WriteLine($"TBCLICK after click {snapProbeClicks_U5}: toolbar shows '{panel.SnapLabelDrawn_U5}' (gizmo {worldGizmo.RotateSnapDeg:0.##}, setting {settings.RotateSnapDeg:0.##})");
                    snapProbeStep_U5 = snapProbeClicks_U5 >= snapProbeWant_U5 ? 4 : 1;
                    break;
                case 4:
                    Console.WriteLine($"TBCLICK done: {snapProbeClicks_U5} click(s) on {(snapProbePlus_U5 ? "+" : "-")}, " +
                                      $"toolbar ended on '{panel.SnapLabelDrawn_U5}'");
                    snapProbeStep_U5 = 99;
                    if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 10);
                    break;
            }
        }
    }
}
