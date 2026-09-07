using System;
using System.Windows.Forms;
using ImGuiNET;

namespace RageLightEditor.ImGuiBackend
{
    public class ImGuiInput
    {
        private readonly Control control;
        private readonly IntPtr context;
        private bool detached;

        public ImGuiInput(Control control) : this(control, IntPtr.Zero) { }

        public ImGuiInput(Control control, IntPtr context)
        {
            this.control = control;
            this.context = context;
            control.MouseDown += OnMouseDown;
            control.MouseUp += OnMouseUp;
            control.MouseMove += OnMouseMove;
            control.MouseWheel += OnMouseWheel;
            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.KeyPress += OnKeyPress;
            control.MouseLeave += OnMouseLeave;
            control.LostFocus += OnLostFocus;
            control.GotFocus += OnGotFocus;
        }

        public void Detach()
        {
            detached = true;
            control.MouseDown -= OnMouseDown;
            control.MouseUp -= OnMouseUp;
            control.MouseMove -= OnMouseMove;
            control.MouseWheel -= OnMouseWheel;
            control.KeyDown -= OnKeyDown;
            control.KeyUp -= OnKeyUp;
            control.KeyPress -= OnKeyPress;
            control.MouseLeave -= OnMouseLeave;
            control.LostFocus -= OnLostFocus;
            control.GotFocus -= OnGotFocus;
        }

        private ContextScope Enter() => new ContextScope(context);

        private readonly struct ContextScope : IDisposable
        {
            private readonly IntPtr previous;
            private readonly bool switched;
            public ContextScope(IntPtr ctx)
            {
                previous = IntPtr.Zero; switched = false;
                if (ctx == IntPtr.Zero) return;
                previous = ImGui.GetCurrentContext();
                if (previous == ctx) return;
                ImGui.SetCurrentContext(ctx);
                switched = true;
            }
            public void Dispose() { if (switched) ImGui.SetCurrentContext(previous); }
        }

        public void NewFrame(float dt)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(control.ClientSize.Width, control.ClientSize.Height);
            io.DeltaTime = dt > 0 ? dt : 1.0f / 60.0f;
        }

        private static int ButtonIndex(MouseButtons b)
        {
            switch (b)
            {
                case MouseButtons.Left: return 0;
                case MouseButtons.Right: return 1;
                case MouseButtons.Middle: return 2;
                case MouseButtons.XButton1: return 3;
                case MouseButtons.XButton2: return 4;
                default: return -1;
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (detached) return;
            using var _ = Enter();
            int i = ButtonIndex(e.Button);
            if (i >= 0) ImGui.GetIO().AddMouseButtonEvent(i, true);
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (detached) return;
            using var _ = Enter();
            int i = ButtonIndex(e.Button);
            if (i >= 0) ImGui.GetIO().AddMouseButtonEvent(i, false);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (detached) return;
            using var _ = Enter();
            ImGui.GetIO().AddMousePosEvent(e.X, e.Y);
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            if (detached) return;
            using var _ = Enter();
            ImGui.GetIO().AddMouseWheelEvent(0, e.Delta / 120.0f);
        }

        public static bool SuppressMouseLeave;

        private void OnMouseLeave(object sender, EventArgs e)
        {
            if (context == IntPtr.Zero || detached || SuppressMouseLeave) return;
            using var _ = Enter();
            ImGui.GetIO().AddMousePosEvent(-float.MaxValue, -float.MaxValue);
        }

        private void OnLostFocus(object sender, EventArgs e)
        {
            if (context == IntPtr.Zero || detached) return;
            using var _ = Enter();
            ImGui.GetIO().AddFocusEvent(false);
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            if (context == IntPtr.Zero || detached) return;
            using var _ = Enter();
            ImGui.GetIO().AddFocusEvent(true);
        }

        private void OnKeyPress(object sender, KeyPressEventArgs e)
        {
            if (detached) return;
            using var _ = Enter();
            ImGui.GetIO().AddInputCharacter(e.KeyChar);
        }

        private void OnKeyDown(object sender, KeyEventArgs e) { SendKey(e, true); }
        private void OnKeyUp(object sender, KeyEventArgs e) { SendKey(e, false); }

        private void SendKey(KeyEventArgs e, bool down)
        {
            if (detached) return;
            using var _ = Enter();
            var io = ImGui.GetIO();
            io.AddKeyEvent(ImGuiKey.ModCtrl, e.Control);
            io.AddKeyEvent(ImGuiKey.ModShift, e.Shift);
            io.AddKeyEvent(ImGuiKey.ModAlt, e.Alt);
            var k = MapKey(e.KeyCode);
            if (k != ImGuiKey.None) io.AddKeyEvent(k, down);
        }

        private static ImGuiKey MapKey(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return ImGuiKey.A + (key - Keys.A);
            if (key >= Keys.D0 && key <= Keys.D9) return ImGuiKey._0 + (key - Keys.D0);
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return ImGuiKey.Keypad0 + (key - Keys.NumPad0);
            if (key >= Keys.F1 && key <= Keys.F12) return ImGuiKey.F1 + (key - Keys.F1);
            switch (key)
            {
                case Keys.Tab: return ImGuiKey.Tab;
                case Keys.Left: return ImGuiKey.LeftArrow;
                case Keys.Right: return ImGuiKey.RightArrow;
                case Keys.Up: return ImGuiKey.UpArrow;
                case Keys.Down: return ImGuiKey.DownArrow;
                case Keys.PageUp: return ImGuiKey.PageUp;
                case Keys.PageDown: return ImGuiKey.PageDown;
                case Keys.Home: return ImGuiKey.Home;
                case Keys.End: return ImGuiKey.End;
                case Keys.Insert: return ImGuiKey.Insert;
                case Keys.Delete: return ImGuiKey.Delete;
                case Keys.Back: return ImGuiKey.Backspace;
                case Keys.Space: return ImGuiKey.Space;
                case Keys.Enter: return ImGuiKey.Enter;
                case Keys.Escape: return ImGuiKey.Escape;
                case Keys.OemQuotes: return ImGuiKey.Apostrophe;
                case Keys.Oemcomma: return ImGuiKey.Comma;
                case Keys.OemMinus: return ImGuiKey.Minus;
                case Keys.OemPeriod: return ImGuiKey.Period;
                case Keys.OemQuestion: return ImGuiKey.Slash;
                case Keys.OemSemicolon: return ImGuiKey.Semicolon;
                case Keys.Oemplus: return ImGuiKey.Equal;
                case Keys.OemOpenBrackets: return ImGuiKey.LeftBracket;
                case Keys.OemPipe: return ImGuiKey.Backslash;
                case Keys.OemCloseBrackets: return ImGuiKey.RightBracket;
                case Keys.Oemtilde: return ImGuiKey.GraveAccent;
                case Keys.CapsLock: return ImGuiKey.CapsLock;
                case Keys.ShiftKey: return ImGuiKey.LeftShift;
                case Keys.ControlKey: return ImGuiKey.LeftCtrl;
                case Keys.Menu: return ImGuiKey.LeftAlt;
                default: return ImGuiKey.None;
            }
        }
    }
}

