using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RageLightEditor.Editor
{
    public class GameThumbnail_U14 : IDisposable
    {
        public bool Registered => thumb != IntPtr.Zero;
        public IntPtr Source { get; private set; }
        public string SourceTitle { get; private set; } = "";
        public int SourceWidth { get; private set; }
        public int SourceHeight { get; private set; }
        public int LastError { get; private set; }

        private IntPtr thumb;
        private int lastX = int.MinValue, lastY, lastW, lastH;
        private bool lastVisible;

        public bool Register(IntPtr destination, IntPtr source, string title)
        {
            Unregister();
            if (destination == IntPtr.Zero || source == IntPtr.Zero) return false;
            int hr = DwmRegisterThumbnail(destination, source, out var t);
            LastError = hr;
            if (hr != 0 || t == IntPtr.Zero) { thumb = IntPtr.Zero; return false; }
            thumb = t;
            Source = source;
            SourceTitle = title ?? "";
            lastX = int.MinValue;
            RefreshSourceSize();
            return true;
        }

        public bool SourceAlive => Source != IntPtr.Zero && IsWindow(Source) && !IsIconic(Source);
        public bool SourceExists => Source != IntPtr.Zero && IsWindow(Source);

        public void RefreshSourceSize()
        {
            if (thumb == IntPtr.Zero) return;
            if (DwmQueryThumbnailSourceSize(thumb, out var size) == 0 && size.cx > 0 && size.cy > 0)
            {
                SourceWidth = size.cx;
                SourceHeight = size.cy;
            }
        }

        public void Update(int x, int y, int w, int h, bool visible)
        {
            if (thumb == IntPtr.Zero) return;
            if (x == lastX && y == lastY && w == lastW && h == lastH && visible == lastVisible) return;
            lastX = x; lastY = y; lastW = w; lastH = h; lastVisible = visible;
            var p = new DwmThumbnailProperties
            {
                dwFlags = TnpRectDestination | TnpVisible | TnpOpacity | TnpSourceClientAreaOnly,
                rcDestination = new Rect { Left = x, Top = y, Right = x + Math.Max(w, 1), Bottom = y + Math.Max(h, 1) },
                opacity = 255,
                fVisible = visible && w > 0 && h > 0,
                fSourceClientAreaOnly = true,
            };
            LastError = DwmUpdateThumbnailProperties(thumb, ref p);
        }

        public void Hide()
        {
            if (thumb == IntPtr.Zero || !lastVisible) return;
            Update(lastX == int.MinValue ? 0 : lastX, lastY, lastW, lastH, false);
        }

        public void Unregister()
        {
            if (thumb != IntPtr.Zero)
            {
                try { DwmUnregisterThumbnail(thumb); } catch { }
                thumb = IntPtr.Zero;
            }
            Source = IntPtr.Zero;
            SourceWidth = SourceHeight = 0;
            lastX = int.MinValue;
            lastVisible = false;
        }

        public void Dispose() => Unregister();

        public static readonly string TitleOverride = Environment.GetEnvironmentVariable("RLE_GAMEVIEWTITLE");

        public static bool IsGameProcess(string processName) =>
            !string.IsNullOrEmpty(processName) &&
            processName.IndexOf("FiveM", StringComparison.OrdinalIgnoreCase) >= 0 &&
            processName.IndexOf("GTAProcess", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool AcceptWindow(string className, string title, string processName)
        {
            if (className == "ConsoleWindowClass") return false;
            if (!string.IsNullOrEmpty(TitleOverride))
                return !string.IsNullOrEmpty(title) && title.IndexOf(TitleOverride, StringComparison.OrdinalIgnoreCase) >= 0 && title.IndexOf("RAGE Tools", StringComparison.OrdinalIgnoreCase) < 0;
            return className == "grcWindow" && IsGameProcess(processName);
        }

        private static string ProcessNameOf(uint pid, Dictionary<uint, string> cache)
        {
            if (cache.TryGetValue(pid, out var known)) return known;
            string name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName ?? ""; } catch { }
            cache[pid] = name;
            return name;
        }

        public static IntPtr FindFiveMWindow(out string title)
        {
            IntPtr found = IntPtr.Zero;
            string foundTitle = "";
            uint self = (uint)Environment.ProcessId;
            var sbText = new StringBuilder(256);
            var sbClass = new StringBuilder(256);
            var names = new Dictionary<uint, string>();
            EnumWindows((h, l) =>
            {
                if (found != IntPtr.Zero || !IsWindowVisible(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == self) return true;
                sbText.Clear(); sbClass.Clear();
                GetWindowText(h, sbText, sbText.Capacity);
                GetClassName(h, sbClass, sbClass.Capacity);
                string t = sbText.ToString(), c = sbClass.ToString();
                if (!AcceptWindow(c, t, ProcessNameOf(pid, names))) return true;
                found = h;
                foundTitle = t;
                return true;
            }, IntPtr.Zero);
            title = foundTitle;
            return found;
        }


        private const int TnpRectDestination = 0x1;
        private const int TnpOpacity = 0x4;
        private const int TnpVisible = 0x8;
        private const int TnpSourceClientAreaOnly = 0x10;

        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Size { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmThumbnailProperties
        {
            public int dwFlags;
            public Rect rcDestination;
            public Rect rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DwmThumbnailProperties props);
        [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out Size size);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    }
}
