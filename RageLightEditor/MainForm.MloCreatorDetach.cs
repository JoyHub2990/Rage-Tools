using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ImGuiNET;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public bool DebugDetachMlo;

        private ProjectDetachedForm mloDetached;
        private bool mloDetachRestored, mloDetachDebugApplied;
        private bool mloDetachDefaultChecked, mloDetachDefaultToSecondMonitor;
        private bool mloDetachPlacementDirty;
        private double mloDetachPlacementAt;
        private bool mloDetachDebugShotDone;

        partial void RenderDetachedWindows_M1(bool captureNow, float dt)
        {
            if (deviceResources == null || deviceResources.DeviceLost || settings == null || panel == null) return;
            var ui = Creator;
            if (ui == null) return;

            if (!mloDetachRestored)
            {
                mloDetachRestored = true;
                if (settings.MloCreatorDetached && screenshotPath == null) ui.Detached = true;
            }
            if (DebugDetachMlo && !mloDetachDebugApplied && panel.MloMode)
            {
                mloDetachDebugApplied = true;
                ui.WindowVisible = true;
                ui.RequestDetach = true;
            }
            if (panel.MloMode && ui.WindowVisible && !mloDetachDefaultChecked && screenshotPath == null && !IsHeadless)
            {
                mloDetachDefaultChecked = true;
                bool neverChosen = !settings.MloCreatorDetached && settings.MloCreatorDetachedBounds == null && string.IsNullOrEmpty(settings.MloCreatorDetachedScreen);
                if (neverChosen && !ui.Detached && Screen.AllScreens.Length > 1)
                {
                    mloDetachDefaultToSecondMonitor = true;
                    ui.RequestDetach = true;
                    Console.WriteLine($"MLODETACH default: {Screen.AllScreens.Length} monitors, the creator window opens detached on the second one");
                }
            }

            if (ui.RequestDetach)
            {
                ui.RequestDetach = false;
                ui.Detached = true;
                SaveMloDetachState();
            }
            if (ui.RequestAttach)
            {
                ui.RequestAttach = false;
                ui.Detached = false;
                SaveMloDetachState();
                if (!IsHeadless) { try { Activate(); } catch { } }
            }
            MloDetachProbeObserve(ui);

            bool wantForm = ui.Detached && ui.WindowVisible;
            if (!wantForm)
            {
                if (mloDetached != null && mloDetached.Visible) mloDetached.Hide();
                FlushMloDetachPlacement();
                return;
            }

            if (mloDetached == null) CreateMloDetachedForm();
            if (mloDetached.DeviceLost) return;
            if (!mloDetached.Visible) mloDetached.Show(this);
            var caption = ui.WindowTitle();
            if (mloDetached.Text != caption) mloDetached.Text = caption;

            float mainScale = DeviceDpi / 96.0f;
            bool mloMode = panel.MloMode;
            var sc = scene; var tc = panel.Timecycle;
            mloDetached.RenderFrame(dt, ImGui.GetStyle(), mainScale,
                (w, h, focused) => ui.DrawDetached(w, h, focused, mloMode, sc, tc));
            RunMloDetachProbe(ui);

            if (captureNow && DebugDetachMlo && screenshotPath != null && !mloDetachDebugShotDone)
            {
                mloDetachDebugShotDone = true;
                var path = System.IO.Path.ChangeExtension(screenshotPath, null) + ".detached.png";
                mloDetached.PendingScreenshot = path;
                mloDetached.RenderFrame(dt, ImGui.GetStyle(), mainScale,
                    (w, h, focused) => ui.DrawDetached(w, h, focused, mloMode, sc, tc));
                Console.WriteLine($"DETACHED MLOCREATOR frames={mloDetached.FramesRendered} bounds={mloDetached.Bounds} " +
                                  $"dpi={mloDetached.DeviceDpi} screen={Screen.FromControl(mloDetached).DeviceName} page={ui.Page} detached={ui.Detached}");
            }

            FlushMloDetachPlacement();
        }

        private void CreateMloDetachedForm()
        {
            mloDetached = new ProjectDetachedForm(deviceResources.Device) { LogTag = "MLOCREATOR", Text = "MLO Creator" };
            mloDetached.AttachRequested += () => Creator.RequestAttach = true;
            mloDetached.PlacementChanged += () =>
            {
                if (mloDetached == null || !mloDetached.Visible) return;
                mloDetachPlacementDirty = true;
                mloDetachPlacementAt = clock.Elapsed.TotalSeconds;
            };
            RestoreMloDetachPlacement();
        }

        private void RestoreMloDetachPlacement()
        {
            var b = settings.MloCreatorDetachedBounds;
            Rectangle rect = Rectangle.Empty;
            if (b != null && b.Length == 4 && b[2] >= 200 && b[3] >= 150)
            {
                rect = new Rectangle(b[0], b[1], b[2], b[3]);
                bool onScreen = Screen.AllScreens.Any(s =>
                {
                    var wa = s.WorkingArea;
                    var i = Rectangle.Intersect(wa, rect);
                    return i.Width >= 120 && i.Height >= 80;
                });
                if (!onScreen) rect = Rectangle.Empty;
            }
            if (rect.IsEmpty)
            {
                var mainScreen = Screen.FromControl(this);
                var wa = mainScreen.WorkingArea;
                int w = Math.Min(1000, wa.Width - 40), h = Math.Min(680, wa.Height - 80);
                var right = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Left >= wa.Right - 8 && s.DeviceName != mainScreen.DeviceName)
                         ?? Screen.AllScreens.FirstOrDefault(s => s.DeviceName != mainScreen.DeviceName && s.Bounds != mainScreen.Bounds);
                var mainRect = Bounds;
                if (right != null)
                {
                    var rw = right.WorkingArea;
                    if (mloDetachDefaultToSecondMonitor)
                    {
                        w = Math.Max(rw.Width - 80, 560); h = Math.Max(rw.Height - 120, 360);
                        rect = new Rectangle(rw.Left + 40, rw.Top + 60, w, h);
                        settings.MloCreatorDetachedMaximized = true;
                    }
                    else
                    {
                        w = Math.Min(w, rw.Width - 40); h = Math.Min(h, rw.Height - 80);
                        rect = new Rectangle(rw.Left + 60, rw.Top + 80, w, h);
                    }
                }
                else
                {
                    rect = new Rectangle(
                        Math.Max(wa.Left, Math.Min(mainRect.Left + 160, wa.Right - w)),
                        Math.Max(wa.Top, Math.Min(mainRect.Top + 120, wa.Bottom - h)), w, h);
                }
            }
            mloDetached.StartPosition = FormStartPosition.Manual;
            mloDetached.Bounds = rect;
            if (settings.MloCreatorDetachedMaximized) mloDetached.WindowState = FormWindowState.Maximized;
        }

        private void FlushMloDetachPlacement()
        {
            if (!mloDetachPlacementDirty || mloDetached == null) return;
            if (clock.Elapsed.TotalSeconds - mloDetachPlacementAt < 1.0) return;
            mloDetachPlacementDirty = false;
            SaveMloDetachState();
        }

        private void SaveMloDetachState()
        {
            settings.MloCreatorDetached = Creator.Detached;
            if (mloDetached != null && mloDetached.IsHandleCreated)
            {
                var r = mloDetached.WindowState == FormWindowState.Normal ? mloDetached.Bounds : mloDetached.RestoreBounds;
                if (r.Width >= 200 && r.Height >= 150)
                    settings.MloCreatorDetachedBounds = new[] { r.X, r.Y, r.Width, r.Height };
                settings.MloCreatorDetachedMaximized = mloDetached.WindowState == FormWindowState.Maximized;
                try { settings.MloCreatorDetachedScreen = Screen.FromControl(mloDetached).DeviceName; } catch { }
            }
            if (screenshotPath == null && !DebugFpsBench) settings.Save();
        }

        private int mloProbeStep = -1;
        private int mloProbeFrame;

        private void RunMloDetachProbe(MloCreatorPanel ui)
        {
            if (mloProbeStep < 0)
            {
                if (Environment.GetEnvironmentVariable("RLE_MLODETACH_PROBE") != "1") { mloProbeStep = 99; return; }
                mloProbeStep = 0;
                ImGuiBackend.ImGuiInput.SuppressMouseLeave = true;
            }
            if (mloProbeStep >= 99 || mloDetached == null || !mloDetached.IsHandleCreated) return;
            mloProbeFrame++;
            if (mloProbeFrame < 30) return;
            var mn = ui.AttachButtonMin; var mx = ui.AttachButtonMax;
            int cx = (int)((mn.X + mx.X) * 0.5f), cy = (int)((mn.Y + mx.Y) * 0.5f);
            IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
            switch (mloProbeStep)
            {
                case 0:
                    SendMessage(mloDetached.Handle, WM_MOUSEMOVE, IntPtr.Zero, lp);
                    Console.WriteLine($"MLODETACHPROBE move to Attach button at {cx},{cy} (rect {mn.X:0},{mn.Y:0}-{mx.X:0},{mx.Y:0}) form {mloDetached.ClientSize.Width}x{mloDetached.ClientSize.Height}");
                    mloProbeStep = 1;
                    break;
                case 1:
                    Console.WriteLine($"MLODETACHPROBE hover {(ui.AttachButtonHovered ? "OK" : "FAIL")}  hovered={ui.AttachButtonHovered} imguiMouse={mloDetached.LastMousePos} capture={mloDetached.LastWantCaptureMouse} moves={mloDetached.MouseMoveEvents}");
                    SendMessage(mloDetached.Handle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lp);
                    mloProbeStep = 2;
                    break;
                case 2:
                    SendMessage(mloDetached.Handle, WM_LBUTTONUP, IntPtr.Zero, lp);
                    mloProbeStep = 3;
                    break;
                case 3:
                    mloProbeStep = 4;
                    break;
            }
        }

        private void MloDetachProbeObserve(MloCreatorPanel ui)
        {
            if (mloProbeStep != 4) return;
            bool attached = !ui.Detached;
            Console.WriteLine($"MLODETACHPROBE click {(attached ? "OK" : "FAIL")}  detached={ui.Detached} formVisible={mloDetached?.Visible}");
            mloProbeStep = 5;
            if (attached) ui.RequestDetach = true;
        }

        partial void DisposeDetachedWindows_M1()
        {
            if (mloDetached == null) return;
            try
            {
                if (deviceResources != null && deviceResources.DeviceLost) mloDetached.AbandonResources();
                else mloDetached.ReleaseResources();
                mloDetached.Dispose();
            }
            catch { }
            mloDetached = null;
        }
    }
}

