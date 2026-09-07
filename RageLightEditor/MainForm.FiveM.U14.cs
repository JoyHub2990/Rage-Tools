using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using ImGuiNET;
using RageLightEditor.Editor;
using SharpDX;
using Rectangle = System.Drawing.Rectangle;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private GameThumbnail_U14 gameThumb_U14;
        private DateTime gameThumbNextFind_U14 = DateTime.MinValue;
        private ProjectDetachedForm fivemDetached_U14;
        private bool fivemDetachRestored_U14, fivemDetachPlacementDirty_U14;
        private double fivemDetachPlacementAt_U14;

        public static string PlaceJson_U14(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            if (scale.X <= 0.0f || scale.Y <= 0.0f || scale.Z <= 0.0f) scale = Vector3.One;
            return "{\"pos\":" + DccBridgeProtocol.V(pos) + ",\"rot\":" + DccBridgeProtocol.Q(Quaternion.Normalize(rot)) + ",\"scale\":" + DccBridgeProtocol.V(scale) + "}";
        }

        public static string PlaceJson_U14(Matrix placement)
        {
            placement.Decompose(out var scale, out var rot, out var pos);
            return PlaceJson_U14(pos, rot, scale);
        }

        public static string WorldPropJson_U14(string model, Vector3 pos, Quaternion rot, Vector3 scale, WorldLights.LightDef[] defs)
        {
            var sb = new StringBuilder("{\"model\":").Append(DccBridgeProtocol.S(model))
                .Append(",\"place\":").Append(PlaceJson_U14(pos, rot, scale))
                .Append(",\"lights\":[");
            bool first = true;
            for (int i = 0; defs != null && i < defs.Length; i++)
            {
                var d = defs[i];
                if (d.L == null || (d.L.Flags & LightDefs.FlagCoronaOnly) != 0) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(FiveMLightJson_U12(i, d.L, d.Pos, d.Dir, d.Tan));
            }
            return sb.Append("]}").ToString();
        }

        private string WorldLightsJson_U14()
        {
            if (!panel.WorldMode || worldRender == null) return null;
            var sel = WorldEdit.Selection;
            var e = sel.LightEntity;
            if (sel.Light == null || e?.Archetype == null) return null;
            if (!worldRender.Lights.TryGetDefs(e.Archetype.Hash, out var defs) || defs == null) return null;
            return "{\"type\":\"lights\",\"spawn\":false,\"props\":[" +
                   WorldPropJson_U14(e.Archetype.Name.ToString(), e.Position, e.Orientation, e.Scale, defs) + "]}";
        }

        private void PushLightsFiveM_U14(double now)
        {
            string body = null;
            if (panel.Workspace == LightPanel.Space.Light && scene != null && scene.HasModel) body = LightsJson_U12();
            else if (panel.WorldMode) body = WorldLightsJson_U16();
            if (body != null)
            {
                if (now - fivemLightsSentAt_U12 < 0.05) return;
                if (body != fivemLightsSent_U12) { fivemLightsSent_U12 = body; fivemLightsSentAt_U12 = now; BroadcastFiveM_U12(body); }
            }
            else if (fivemLightsSent_U12.Length > 0)
            {
                fivemLightsSent_U12 = "";
                BroadcastFiveM_U12(ClearLightsJson_U12());
            }
        }

        private bool TickGameThumb_U14()
        {
            gameThumb_U14 ??= new GameThumbnail_U14();
            if (!panel.GameViewOpen_U13 || (WindowState == FormWindowState.Minimized && !panel.GameViewDetached_U16))
            {
                gameThumb_U14.Hide();
                panel.GameViewUseThumb_U14 = false;
                return panel.GameViewOpen_U13 && WindowState == FormWindowState.Minimized;
            }
            if (gameThumb_U14.Registered && !gameThumb_U14.SourceExists)
            {
                gameThumb_U14.Unregister();
                panel.GameViewOpen_U13 = false;
                panel.GameViewDetached_U16 = false;
                panel.FiveMSay_U12("the game closed - game view closed");
                return false;
            }
            if (gameThumb_U14.Registered && !gameThumb_U14.SourceAlive)
            {
                gameThumb_U14.Unregister();
                panel.FiveMSay_U12("the FiveM window is minimised - game view paused");
            }
            if (!gameThumb_U14.Registered)
            {
                if (DateTime.UtcNow < gameThumbNextFind_U14) { panel.GameViewUseThumb_U14 = false; return false; }
                gameThumbNextFind_U14 = DateTime.UtcNow.AddSeconds(1.5);
                var h = GameThumbnail_U14.FindFiveMWindow(out var title);
                bool ok = h != IntPtr.Zero && gameThumb_U14.Register(GameViewDestination_U16(), h, title);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RLE_GAMEVIEWSHOT")))
                    Console.WriteLine($"GAMEVIEW find hwnd=0x{h.ToInt64():X} title='{title}' registered={ok} err=0x{gameThumb_U14.LastError:X8} size={gameThumb_U14.SourceWidth}x{gameThumb_U14.SourceHeight}");
                if (!ok)
                {
                    panel.GameViewUseThumb_U14 = false;
                    return false;
                }
                panel.FiveMSay_U12("game view: showing " + title);
            }
            gameThumb_U14.RefreshSourceSize();
            if (gameThumb_U14.SourceWidth > 0 && gameThumb_U14.SourceHeight > 0)
                panel.GameViewAspect_U14 = (float)gameThumb_U14.SourceHeight / gameThumb_U14.SourceWidth;
            panel.GameViewUseThumb_U14 = true;
            panel.GameViewStatus_U13 = "live  " + gameThumb_U14.SourceTitle;
            int x = panel.GameViewRectX_U14, y = panel.GameViewRectY_U14, w = panel.GameViewRectW_U14, h2 = panel.GameViewRectH_U14;
            if (GameViewDetachedRect_U16(out int dx, out int dy, out int dw, out int dh)) { x = dx; y = dy; w = dw; h2 = dh; }
            gameThumb_U14.Update(x, y, w, h2, w > 0 && h2 > 0);
            panel.ClearGameViewRect_U14();
            FillGameDebug_U13();
            GameViewSelfShot_U14(w > 0 && h2 > 0);
            return true;
        }

        private int gameViewShotFrames_U14;
        private bool gameViewShotDone_U14;

        private void GameViewSelfShot_U14(bool shown)
        {
            int w0 = panel.GameViewRectW_U14, h0 = panel.GameViewRectH_U14;
            var path = Environment.GetEnvironmentVariable("RLE_GAMEVIEWSHOT");
            if (string.IsNullOrEmpty(path) || gameViewShotDone_U14) return;
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 3);
            if (gameViewShotFrames_U14 % 30 == 0) Console.WriteLine($"GAMEVIEW tick shown={shown} rect={panel.GameViewRectX_U14},{panel.GameViewRectY_U14} {w0}x{h0}");
            if (!shown || ++gameViewShotFrames_U14 < 90) return;
            gameViewShotDone_U14 = true;
            try
            {
                var f = GameCapture_U13.CaptureWindow(Handle, 1600);
                if (f == null) { Console.WriteLine("GAMEVIEWSHOT no frame"); return; }
                using var bmp = new Bitmap(f.Width, f.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, f.Width, f.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < f.Height; y++) System.Runtime.InteropServices.Marshal.Copy(f.Bgra, y * f.Width * 4, data.Scan0 + y * data.Stride, f.Width * 4); }
                finally { bmp.UnlockBits(data); }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine($"GAMEVIEWSHOT {path} {f.Width}x{f.Height} {f.Source} thumb {gameThumb_U14.SourceWidth}x{gameThumb_U14.SourceHeight} rect {panel.GameViewRectX_U14},{panel.GameViewRectY_U14} err 0x{gameThumb_U14.LastError:X8}");
            }
            catch (Exception ex) { Console.WriteLine("GAMEVIEWSHOT failed " + ex.Message); }
        }

        private void FillGameDebug_U13()
        {
            panel.GameDebugLines_U13.Clear();
            var b = fivem_U12;
            panel.GameDebugLines_U13.Add(b != null && b.Listening
                ? (b.Clients > 0 ? $"link {b.Received} in / {b.Sent} out" + (string.IsNullOrEmpty(fivemPlayer_U12) ? "" : "   " + fivemPlayer_U12) : "link waiting for the game")
                : "link off");
            panel.GameDebugLines_U13.AddRange(fivemStats_U13);
        }

        private void RenderFiveMDetached_U14(bool captureNow, float dt)
        {
            if (deviceResources == null || deviceResources.DeviceLost || settings == null || panel == null) return;
            if (!fivemDetachRestored_U14)
            {
                fivemDetachRestored_U14 = true;
                if (settings.FiveMDetached && screenshotPath == null) panel.FiveMDetached_U14 = true;
            }
            if (panel.RequestFiveMDetach_U14)
            {
                panel.RequestFiveMDetach_U14 = false;
                panel.FiveMDetached_U14 = true;
                panel.FiveMWindowOpen_U12 = true;
                SaveFiveMDetachState_U14();
            }
            if (panel.RequestFiveMAttach_U14)
            {
                panel.RequestFiveMAttach_U14 = false;
                panel.FiveMDetached_U14 = false;
                SaveFiveMDetachState_U14();
                if (!IsHeadless) { try { Activate(); } catch { } }
            }
            bool wantForm = panel.FiveMDetached_U14 && panel.FiveMWindowOpen_U12 && screenshotPath == null;
            if (!wantForm)
            {
                if (fivemDetached_U14 != null && fivemDetached_U14.Visible) fivemDetached_U14.Hide();
                FlushFiveMDetachPlacement_U14();
                return;
            }
            if (fivemDetached_U14 == null) CreateFiveMDetachedForm_U14();
            if (fivemDetached_U14.DeviceLost) return;
            if (!fivemDetached_U14.Visible) fivemDetached_U14.Show(this);
            var caption = panel.FiveMConnected_U12 ? "FiveM Live Linking - game connected" : "FiveM Live Linking";
            if (fivemDetached_U14.Text != caption) fivemDetached_U14.Text = caption;
            float mainScale = DeviceDpi / 96.0f;
            fivemDetached_U14.RenderFrame(dt, ImGui.GetStyle(), mainScale, (w, h, focused) => panel.DrawFiveMDetached_U14(w, h, focused));
            FlushFiveMDetachPlacement_U14();
        }

        private void CreateFiveMDetachedForm_U14()
        {
            fivemDetached_U14 = new ProjectDetachedForm(deviceResources.Device) { LogTag = "FIVEM", Text = "FiveM Live Linking" };
            fivemDetached_U14.AttachRequested += () => panel.RequestFiveMAttach_U14 = true;
            fivemDetached_U14.PlacementChanged += () =>
            {
                if (fivemDetached_U14 == null || !fivemDetached_U14.Visible) return;
                fivemDetachPlacementDirty_U14 = true;
                fivemDetachPlacementAt_U14 = clock.Elapsed.TotalSeconds;
            };
            RestoreFiveMDetachPlacement_U14();
        }

        private void RestoreFiveMDetachPlacement_U14()
        {
            var b = settings.FiveMDetachedBounds;
            Rectangle rect = Rectangle.Empty;
            if (b != null && b.Length == 4 && b[2] >= 300 && b[3] >= 200)
            {
                rect = new Rectangle(b[0], b[1], b[2], b[3]);
                bool onScreen = Screen.AllScreens.Any(s => { var i = Rectangle.Intersect(s.WorkingArea, rect); return i.Width >= 120 && i.Height >= 80; });
                if (!onScreen) rect = Rectangle.Empty;
            }
            if (rect.IsEmpty)
            {
                var mainScreen = Screen.FromControl(this);
                var wa = mainScreen.WorkingArea;
                int w = Math.Min(820, wa.Width - 40), h = Math.Min(900, wa.Height - 80);
                var other = Screen.AllScreens.FirstOrDefault(s => s.DeviceName != mainScreen.DeviceName && s.Bounds != mainScreen.Bounds);
                if (other != null)
                {
                    var ow = other.WorkingArea;
                    w = Math.Min(w, ow.Width - 40); h = Math.Min(h, ow.Height - 80);
                    rect = new Rectangle(ow.Left + 40, ow.Top + 40, w, h);
                }
                else
                {
                    rect = new Rectangle(Math.Max(wa.Left, Math.Min(Bounds.Right - w - 40, wa.Right - w)), Math.Max(wa.Top, Math.Min(Bounds.Top + 80, wa.Bottom - h)), w, h);
                }
            }
            fivemDetached_U14.StartPosition = FormStartPosition.Manual;
            fivemDetached_U14.Bounds = rect;
            if (settings.FiveMDetachedMaximized) fivemDetached_U14.WindowState = FormWindowState.Maximized;
        }

        private void FlushFiveMDetachPlacement_U14()
        {
            if (!fivemDetachPlacementDirty_U14 || fivemDetached_U14 == null) return;
            if (clock.Elapsed.TotalSeconds - fivemDetachPlacementAt_U14 < 1.0) return;
            fivemDetachPlacementDirty_U14 = false;
            SaveFiveMDetachState_U14();
        }

        private void SaveFiveMDetachState_U14()
        {
            settings.FiveMDetached = panel.FiveMDetached_U14;
            if (fivemDetached_U14 != null && fivemDetached_U14.IsHandleCreated)
            {
                var r = fivemDetached_U14.WindowState == FormWindowState.Normal ? fivemDetached_U14.Bounds : fivemDetached_U14.RestoreBounds;
                if (r.Width >= 300 && r.Height >= 200) settings.FiveMDetachedBounds = new[] { r.X, r.Y, r.Width, r.Height };
                settings.FiveMDetachedMaximized = fivemDetached_U14.WindowState == FormWindowState.Maximized;
                try { settings.FiveMDetachedScreen = Screen.FromControl(fivemDetached_U14).DeviceName; } catch { }
            }
            if (screenshotPath == null && !DebugFpsBench) settings.Save();
        }
    }
}
