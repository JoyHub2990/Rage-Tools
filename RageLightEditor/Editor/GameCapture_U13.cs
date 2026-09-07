using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RageLightEditor.Editor
{
    public class GameFrame_U13
    {
        public int Width, Height;
        public byte[] Bgra;
        public int Serial;
        public string Source = "";
    }

    public class GameCapture_U13 : IDisposable
    {
        public int MaxWidth = 640;
        public double IntervalSeconds = 0.1;
        public volatile bool Enabled;
        public string WindowTitle { get; private set; } = "";
        public string Status { get; private set; } = "off";
        public int FramesPerSecond { get; private set; }
        public IntPtr Window { get; private set; }

        private Thread thread;
        private readonly object gate = new object();
        private GameFrame_U13 latest;
        private int serial;
        private int fpsCount;
        private DateTime fpsAt = DateTime.UtcNow;

        public void Start()
        {
            if (thread != null) return;
            var t = new Thread(Loop) { IsBackground = true, Name = "game view capture" };
            thread = t;
            t.Start();
        }

        public void Stop()
        {
            thread = null;
        }

        public void Dispose() => Stop();

        public GameFrame_U13 TakeLatest(int lastSerial)
        {
            lock (gate) return latest != null && latest.Serial != lastSerial ? latest : null;
        }

        private void Loop()
        {
            var me = Thread.CurrentThread;
            DateTime nextFind = DateTime.MinValue;
            while (ReferenceEquals(thread, me))
            {
                if (!Enabled) { Status = "off"; Thread.Sleep(200); continue; }
                if (Window == IntPtr.Zero || !IsWindow(Window))
                {
                    Window = IntPtr.Zero;
                    if (DateTime.UtcNow > nextFind)
                    {
                        Window = FindGameWindow(out var title);
                        WindowTitle = title;
                        nextFind = DateTime.UtcNow.AddSeconds(2);
                    }
                    if (Window == IntPtr.Zero) { Status = "no FiveM window found - start the game"; Thread.Sleep(250); continue; }
                }
                var t0 = DateTime.UtcNow;
                try
                {
                    var f = CaptureWindow(Window, MaxWidth);
                    if (f != null)
                    {
                        f.Serial = ++serial;
                        lock (gate) latest = f;
                        Status = f.Source + "  " + WindowTitle;
                        fpsCount++;
                        if ((DateTime.UtcNow - fpsAt).TotalSeconds >= 1.0) { FramesPerSecond = fpsCount; fpsCount = 0; fpsAt = DateTime.UtcNow; }
                    }
                    else Status = IsIconic(Window) ? "FiveM is minimised - restore its window" : "capture failed";
                }
                catch (Exception ex) { Status = ex.Message; }
                double spent = (DateTime.UtcNow - t0).TotalSeconds;
                Thread.Sleep((int)Math.Max(5.0, (IntervalSeconds - spent) * 1000.0));
            }
        }

        public static IntPtr FindGameWindow(out string title) => GameThumbnail_U14.FindFiveMWindow(out title);

        public static GameFrame_U13 CaptureWindow(IntPtr hwnd, int maxWidth)
        {
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rc)) return null;
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w < 16 || h < 16) return null;
            using var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bool ok;
            using (var g = Graphics.FromImage(full))
            {
                IntPtr hdc = g.GetHdc();
                try { ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
            }
            string source = "window capture";
            if (!ok || LooksBlank(full))
            {
                var pt = new POINT();
                ClientToScreen(hwnd, ref pt);
                try
                {
                    using var g2 = Graphics.FromImage(full);
                    g2.CopyFromScreen(pt.X, pt.Y, 0, 0, new Size(w, h));
                    source = "screen capture";
                }
                catch { return null; }
            }
            int ow = w, oh = h;
            if (ow > maxWidth) { oh = Math.Max(1, (int)((long)h * maxWidth / w)); ow = maxWidth; }
            Bitmap small = full;
            if (ow != w)
            {
                small = new Bitmap(ow, oh, PixelFormat.Format32bppArgb);
                using var g3 = Graphics.FromImage(small);
                g3.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g3.DrawImage(full, new Rectangle(0, 0, ow, oh));
            }
            var frame = new GameFrame_U13 { Width = ow, Height = oh, Bgra = new byte[ow * oh * 4], Source = source };
            var data = small.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int row = ow * 4;
                for (int y = 0; y < oh; y++) Marshal.Copy(data.Scan0 + y * data.Stride, frame.Bgra, y * row, row);
            }
            finally { small.UnlockBits(data); }
            if (!ReferenceEquals(small, full)) small.Dispose();
            for (int i = 3; i < frame.Bgra.Length; i += 4) frame.Bgra[i] = 255;
            return frame;
        }

        private static bool LooksBlank(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            for (int y = 1; y < 8; y++)
                for (int x = 1; x < 8; x++)
                {
                    var c = bmp.GetPixel(x * w / 8, y * h / 8);
                    if (c.R + c.G + c.B > 12) return false;
                }
            return true;
        }

        private const uint PW_CLIENTONLY = 1;
        private const uint PW_RENDERFULLCONTENT = 2;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    }
}
