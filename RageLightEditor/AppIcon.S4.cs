using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RageLightEditor
{
    internal static partial class AppIcon
    {

        public const string AppUserModelId = "Escobar.RAGETools";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        public static void SetAppUserModelId()
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); } catch { }
        }

        private static byte[] icoBytes;
        private static bool icoRead;

        private static byte[] IcoBytes()
        {
            if (icoRead) return icoBytes;
            icoRead = true;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico");
                if (s != null)
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    icoBytes = ms.ToArray();
                }
            }
            catch { }
            return icoBytes;
        }

        public static Icon LoadAt(int px)
        {
            px = Math.Clamp(px, 8, 256);
            var bytes = IcoBytes();
            if (bytes != null)
            {
                try { using var ms = new MemoryStream(bytes); return new Icon(ms, px, px); } catch { }
            }
            var fromExe = FromExecutable(px);
            if (fromExe != null) return fromExe;
            return Load();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint flags);

        private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x0010, LR_DEFAULTSIZE = 0x0040;

        private static Icon FromExecutable(int px)
        {
            string exe = null;
            try { exe = Application.ExecutablePath; } catch { }
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            try
            {
                var h = LoadImage(IntPtr.Zero, exe, IMAGE_ICON, px, px, LR_LOADFROMFILE);
                if (h != IntPtr.Zero) return Icon.FromHandle(h);
            }
            catch { }
            try
            {
                var large = new IntPtr[1]; var small = new IntPtr[1];
                if (ExtractIconEx(exe, 0, large, small, 1) > 0)
                {
                    bool wantSmall = px <= 20 && small[0] != IntPtr.Zero;
                    var h = wantSmall ? small[0] : large[0];
                    var other = wantSmall ? large[0] : small[0];
                    if (other != IntPtr.Zero) DestroyIcon(other);
                    if (h != IntPtr.Zero) return Icon.FromHandle(h);
                }
            }
            catch { }
            return null;
        }

        private const int WM_SETICON = 0x0080, WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
        private const int GCLP_HICONSM = -34, GCLP_HICON = -14;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr", CharSet = CharSet.Auto)]
        private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetClassLong", CharSet = CharSet.Auto)]
        private static extern uint SetClassLong32(IntPtr hWnd, int index, uint value);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);

        private static void SetClassIcon(IntPtr hwnd, int index, IntPtr value)
        {
            try
            {
                if (IntPtr.Size == 8) SetClassLongPtr64(hwnd, index, value);
                else SetClassLong32(hwnd, index, (uint)value.ToInt32());
            }
            catch { }
        }

        public static void Attach(Form form)
        {
            if (form == null) return;
            form.HandleCreated += (s, e) => ApplyToWindow(form);
            form.DpiChanged += (s, e) => ApplyToWindow(form);
            form.Shown += (s, e) => { ApplyToWindow(form); Probe(form); };
            if (form.IsHandleCreated) ApplyToWindow(form);
        }

        private static Icon bigIcon, smallIcon;
        private static int bigPx, smallPx;

        public static void ApplyToWindow(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;
            try
            {
                int dpi = 96;
                try { dpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96; } catch { }
                int big = Math.Max(32, (int)Math.Round(32.0 * dpi / 96.0));
                int small = Math.Max(16, (int)Math.Round(16.0 * dpi / 96.0));
                if (bigIcon == null || bigPx != big) { bigIcon = LoadAt(big); bigPx = big; }
                if (smallIcon == null || smallPx != small) { smallIcon = LoadAt(small); smallPx = small; }
                var hwnd = form.Handle;
                if (smallIcon != null)
                {
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon.Handle);
                    SetClassIcon(hwnd, GCLP_HICONSM, smallIcon.Handle);
                }
                if (bigIcon != null)
                {
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, bigIcon.Handle);
                    SetClassIcon(hwnd, GCLP_HICON, bigIcon.Handle);
                }
            }
            catch { }
        }

        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bm);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int w, int h, uint step, IntPtr brush, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        private static bool probed;

        public static void Probe(Form form)
        {
            if (probed || form == null || !form.IsHandleCreated) return;
            var env = Environment.GetEnvironmentVariable("RLE_ICONPROBE");
            if (string.IsNullOrEmpty(env)) return;
            probed = true;
            string dir = env == "1" ? AppContext.BaseDirectory : env;
            try { Directory.CreateDirectory(dir); } catch { }

            var bytes = IcoBytes();
            Console.WriteLine($"ICONPROBE embedded icon.ico: {(bytes == null ? "MISSING" : bytes.Length + " bytes")}; " +
                              $"exe = {Application.ExecutablePath}; appId = {AppUserModelId}");
            var hwnd = form.Handle;
            int dpi = 96; try { dpi = form.DeviceDpi > 0 ? form.DeviceDpi : 96; } catch { }
            Console.WriteLine($"ICONPROBE window dpi {dpi}, asking for icons at that scale");
            Report(dir, "big", SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_BIG, (IntPtr)dpi));
            Report(dir, "small", SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_SMALL, (IntPtr)dpi));
            Report(dir, "small2", SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_SMALL2, (IntPtr)dpi));
            Report(dir, "class", GetClassLongPtr(hwnd, GCLP_HICON));
            Report(dir, "classsm", GetClassLongPtr(hwnd, GCLP_HICONSM));
        }

        private static void Report(string dir, string slot, IntPtr h)
        {
            if (h == IntPtr.Zero) { Console.WriteLine($"ICONPROBE {slot}: NO ICON - the shell would fall back"); return; }
            int w = 0, ht = 0;
            try
            {
                if (GetIconInfo(h, out var ii))
                {
                    var src = ii.hbmColor != IntPtr.Zero ? ii.hbmColor : ii.hbmMask;
                    if (GetObject(src, Marshal.SizeOf(typeof(BITMAP)), out var bm) != 0)
                    { w = bm.bmWidth; ht = ii.hbmColor != IntPtr.Zero ? bm.bmHeight : bm.bmHeight / 2; }
                    if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                    if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                }
            }
            catch { }
            int draw = Math.Max(16, Math.Max(w, ht));
            string path = Path.Combine(dir, "iconprobe_" + slot + ".png");
            bool wrote = false;
            try
            {
                using var bmp = new Bitmap(draw, draw, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(255, 24, 24, 24));
                    var hdc = g.GetHdc();
                    DrawIconEx(hdc, 0, 0, h, draw, draw, 0, IntPtr.Zero, 3);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(path, ImageFormat.Png);
                wrote = true;
            }
            catch { }
            Console.WriteLine($"ICONPROBE {slot}: handle 0x{h.ToInt64():X} {w}x{ht}" + (wrote ? " -> " + path : " (could not render)"));
        }
    }
}

