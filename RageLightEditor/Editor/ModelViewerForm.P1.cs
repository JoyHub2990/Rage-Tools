using System;
using System.IO;
using System.Windows.Forms;
using ImGuiNET;
using RageLightEditor.ImGuiBackend;
using RageLightEditor.Rendering;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Editor
{
    public sealed class ModelViewerForm : Form
    {
        private readonly Device device;
        private SecondaryWindow window;
        private IntPtr context;
        private ImGuiRenderer renderer;
        private ImGuiInput input;
        private bool fontsDirty = true;
        private float fontScale = 1.0f;
        private int fontVersion_V20 = -1;

        public event Action CloseRequested;
        public event Action PlacementChanged;
        public string PendingScreenshot;
        public string LastScreenshotError;
        public bool DeviceLost => window != null && window.DeviceLost;
        public int FramesRendered { get; private set; }
        public string LogTag = "MODELVIEW";

        public ModelViewerForm(Device device)
        {
            this.device = device;
            Text = "Model viewer";
            Icon = AppIcon.Load();
            AppIcon.Attach(this);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new System.Drawing.Size(1280, 800);
            MinimumSize = new System.Drawing.Size(720, 420);
            KeyPreview = true;
            ShowInTaskbar = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            DoubleBuffered = false;

            HandleCreated += (s, e) => CreateResources();
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) return;
                window?.Resize(ClientSize.Width, ClientSize.Height);
                PlacementChanged?.Invoke();
            };
            LocationChanged += (s, e) => PlacementChanged?.Invoke();
            DpiChanged += (s, e) => fontsDirty = true;
            FormClosing += (s, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                Hide();
                CloseRequested?.Invoke();
            };
        }

        public float DpiScale => DeviceDpi / 96.0f;

        public IntPtr RegisterTexture(ShaderResourceView srv)
        {
            if (renderer == null || srv == null) return IntPtr.Zero;
            var prev = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(context);
            try { return renderer.RegisterTexture(srv); }
            finally { ImGui.SetCurrentContext(prev); }
        }

        public void UnregisterTexture(IntPtr id)
        {
            if (renderer == null || id == IntPtr.Zero) return;
            var prev = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(context);
            try { renderer.UnregisterTexture(id); }
            finally { ImGui.SetCurrentContext(prev); }
        }

        public bool Ready => window != null && renderer != null;

        private void CreateResources()
        {
            if (window != null) return;
            window = new SecondaryWindow(device, Handle, ClientSize.Width, ClientSize.Height);
            var prev = ImGui.GetCurrentContext();
            context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            try
            {
                var io = ImGui.GetIO();
                io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
                unsafe { io.NativePtr->IniFilename = null; }
                BuildFonts();
                renderer = new ImGuiRenderer(device, device.ImmediateContext);
                fontsDirty = false;
                input?.Detach();
                input = new ImGuiInput(this, context);
            }
            finally
            {
                ImGui.SetCurrentContext(prev);
            }
        }

        private void BuildFonts()
        {
            var io = ImGui.GetIO();
            fontScale = DpiScale;
            io.Fonts.Clear();
            fontVersion_V20 = UiScale_V17.FontVersion;
            UiScale_V17.AddUiFont(io, fontScale);
        }

        public void RenderFrame(float dt, ImGuiStylePtr mainStyle, float mainScale, Action<float, float, bool> drawBody)
        {
            if (!IsHandleCreated || window == null || window.DeviceLost) return;
            if (WindowState == FormWindowState.Minimized) return;
            if (ClientSize.Width < 8 || ClientSize.Height < 8) return;
            window.Resize(ClientSize.Width, ClientSize.Height);

            var prev = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(context);
            try
            {
                if (fontsDirty || Math.Abs(fontScale - DpiScale) > 0.001f || fontVersion_V20 != UiScale_V17.FontVersion)
                {
                    BuildFonts();
                    renderer.RecreateFontTexture();
                    fontsDirty = false;
                }
                CopyStyle(mainStyle);
                RescaleStyle(mainScale);
                var wb = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];

                input.NewFrame(dt);
                ImGui.NewFrame();
                bool active = ContainsFocus || ActiveForm == this;
                var io = ImGui.GetIO();
                try { drawBody(io.DisplaySize.X, io.DisplaySize.Y, active); }
                catch (Exception ex)
                {
                    if (drawError != ex.ToString())
                    {
                        drawError = ex.ToString();
                        Console.WriteLine($"DETACHED {LogTag} DRAW FAILED: " + ex);
                    }
                }
                ImGui.Render();

                window.Begin(new SharpDX.Color4(wb.X, wb.Y, wb.Z, 1.0f));
                renderer.Render(ImGui.GetDrawData());
                FramesRendered++;
                if (PendingScreenshot != null)
                {
                    LastScreenshotError = window.SaveScreenshot(PendingScreenshot);
                    Console.WriteLine(LastScreenshotError == null
                        ? $"{LogTag} screenshot {window.Width}x{window.Height} -> {PendingScreenshot}"
                        : $"{LogTag} screenshot FAILED: {LastScreenshotError}");
                    PendingScreenshot = null;
                }
                window.Present();
            }
            finally
            {
                ImGui.SetCurrentContext(prev);
            }
        }

        private string drawError;

        private static unsafe void CopyStyle(ImGuiStylePtr mainStyle)
        {
            var mine = ImGui.GetStyle();
            if (mainStyle.NativePtr == null || mine.NativePtr == mainStyle.NativePtr) return;
            int size = sizeof(ImGuiStyle);
            System.Buffer.MemoryCopy(mainStyle.NativePtr, mine.NativePtr, size, size);
        }

        private void RescaleStyle(float mainScale)
        {
            float mine = DpiScale;
            if (mainScale <= 0.0f || Math.Abs(mine - mainScale) < 0.001f) return;
            ImGui.GetStyle().ScaleAllSizes(mine / mainScale);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if (k == Keys.Tab || k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down ||
                k == Keys.Home || k == Keys.End || k == Keys.PageUp || k == Keys.PageDown ||
                k == Keys.Enter || k == Keys.Escape)
                return false;
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        public void AbandonResources()
        {
            input?.Detach(); input = null;
            renderer = null;
            window = null;
            if (context != IntPtr.Zero)
            {
                var prev = ImGui.GetCurrentContext();
                if (prev == context) prev = IntPtr.Zero;
                ImGui.DestroyContext(context);
                context = IntPtr.Zero;
                if (prev != IntPtr.Zero) ImGui.SetCurrentContext(prev);
            }
        }

        public void ReleaseResources()
        {
            if (window == null) return;
            input?.Detach(); input = null;
            var prev = ImGui.GetCurrentContext();
            if (prev == context) prev = IntPtr.Zero;
            renderer?.Dispose(); renderer = null;
            window.Dispose(); window = null;
            if (context != IntPtr.Zero)
            {
                ImGui.DestroyContext(context);
                context = IntPtr.Zero;
                if (prev != IntPtr.Zero) ImGui.SetCurrentContext(prev);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseResources();
            base.Dispose(disposing);
        }
    }
}

