using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using RageLightEditor.Editor;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private GameCapture_U13 gameCapture_U13;
        private Texture2D gameTex_U13;
        private ShaderResourceView gameSrv_U13;
        private IntPtr gameTexId_U13;
        private int gameTexW_U13, gameTexH_U13, gameFrameSerial_U13;
        private bool fivemWired_U13;
        private string fivemTcSent_U13 = "", fivemTcModSent_U13 = "";
        private double fivemTcSentAt_U13, matEditAt_U13;
        private int matSerialSeen_U13;
        private bool matApplyPending_U13;
        private readonly List<string> fivemStats_U13 = new List<string>();

        private void EnsureFiveMWired_U13()
        {
            if (fivemWired_U13 || panel == null || settings == null) return;
            fivemWired_U13 = true;
            panel.FiveMAutoApply_U13 = settings.FiveMAutoApply;
            panel.FiveMAutoApplySeconds_U13 = Math.Clamp(settings.FiveMAutoApplySeconds > 0 ? settings.FiveMAutoApplySeconds : 2.0f, 0.5f, 15.0f);
            panel.GameViewOpen_U13 = settings.FiveMGameView && screenshotPath == null;
            panel.GameDebug_U13 = settings.FiveMGameDebug;
            matSerialSeen_U13 = MaterialEditing.EditSerial_U13;
        }

        private void Tick_FiveM_U13()
        {
            EnsureFiveMWired_U13();
            if (settings != null)
            {
                settings.FiveMAutoApply = panel.FiveMAutoApply_U13;
                settings.FiveMAutoApplySeconds = panel.FiveMAutoApplySeconds_U13;
                settings.FiveMGameView = panel.GameViewOpen_U13;
                settings.FiveMGameDebug = panel.GameDebug_U13;
            }
            if (Environment.GetEnvironmentVariable("RLE_GAMEVIEW") == "1") panel.GameViewOpen_U13 = true;
            TickGameView_U13();
            TickMaterialsApply_U13();
            if (panel.RequestFiveMApplyNow_U13)
            {
                panel.RequestFiveMApplyNow_U13 = false;
                ApplyMaterialsNow_U13();
            }
        }

        private void ResetFiveMSent_U13()
        {
            fivemTcSent_U13 = "";
            fivemTcModSent_U13 = "";
        }

        private void PushFiveMOff_U13()
        {
            BroadcastFiveM_U12("{\"type\":\"timecycle\",\"off\":true}");
            BroadcastFiveM_U12("{\"type\":\"tcmod\",\"off\":true}");
            ResetFiveMSent_U13();
        }

        private void PushFiveMSync_U13()
        {
            double now = fivemClock_U12.Elapsed.TotalSeconds;
            if (now - fivemTcSentAt_U13 < 0.1) return;
            fivemTcSentAt_U13 = now;
            var tc = TimecycleJson_U13(timecycle, panel.PreviewHour);
            if (tc != fivemTcSent_U13) { fivemTcSent_U13 = tc; BroadcastFiveM_U12(tc); }
            var mod = TimecycleModJson_U13(timecycle, panel.ShowTimecycleEditor && panel.TimecycleEditorTab_U13 == 1);
            if (mod != fivemTcModSent_U13) { fivemTcModSent_U13 = mod; BroadcastFiveM_U12(mod); }
        }

        public static bool SameValues_U13(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 1e-6f) return false;
            return true;
        }

        public static string TimecycleJson_U13(TimecycleData tc, float hour)
        {
            var r = tc?.Current;
            if (r == null || r.Original == null) return "{\"type\":\"timecycle\",\"off\":true}";
            tc.SetTime(hour);
            var sb = new StringBuilder("{\"type\":\"timecycle\",\"vars\":{");
            bool any = false;
            foreach (var kv in r.Values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (r.Original.TryGetValue(kv.Key, out var orig) && SameValues_U13(orig, kv.Value)) continue;
                float v = r.Get(kv.Key, tc.CurrentSampleIndex, tc.CurrentSampleBlend);
                if (any) sb.Append(',');
                any = true;
                sb.Append(DccBridgeProtocol.S(kv.Key)).Append(':').Append(DccBridgeProtocol.N(v));
            }
            if (!any) return "{\"type\":\"timecycle\",\"off\":true}";
            return sb.Append("}}").ToString();
        }

        public static string TimecycleModJson_U13(TimecycleData tc, bool preview)
        {
            if (tc == null) return "{\"type\":\"tcmod\",\"off\":true}";
            var current = preview ? tc.CurrentModifier : null;
            var sb = new StringBuilder("{\"type\":\"tcmod\",\"mods\":[");
            bool any = false;
            foreach (var m in tc.Modifiers)
            {
                bool changed = m.Original == null || m.Values.Count != m.Original.Count ||
                               m.Values.Any(kv => !m.Original.TryGetValue(kv.Key, out var o) || Math.Abs(o - kv.Value) > 1e-6f);
                if (!changed && !ReferenceEquals(m, current)) continue;
                if (any) sb.Append(',');
                any = true;
                sb.Append("{\"name\":").Append(DccBridgeProtocol.S(m.Name)).Append(",\"vars\":{");
                bool first = true;
                foreach (var kv in m.Values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(DccBridgeProtocol.S(kv.Key)).Append(':').Append(DccBridgeProtocol.N(kv.Value));
                }
                sb.Append("}}");
            }
            if (!any) return "{\"type\":\"tcmod\",\"off\":true}";
            sb.Append("],\"apply\":").Append(current != null ? DccBridgeProtocol.S(current.Name) : "null")
              .Append(",\"strength\":").Append(DccBridgeProtocol.N(tc.ModifierStrength));
            return sb.Append('}').ToString();
        }

        private bool HandleFiveMMessage_U13(MloBridgeMessage m, DccMessage d)
        {
            if (m.Command != "stats" || d == null) return false;
            var p = d.Vec("pos", -1, fivemPlayerPos_U12);
            fivemPlayerPos_U12 = p;
            fivemHasPlayer_U12 = true;
            fivemStats_U13.Clear();
            fivemStats_U13.Add($"game {d.Num("fps", -1, 0):0} fps   ping {d.Num("ping", -1, 0):0} ms");
            fivemStats_U13.Add($"player {p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}   heading {d.Num("heading", -1, 0):0}");
            int interior = d.Int("interior", -1, 0);
            fivemStats_U13.Add(interior != 0 ? $"interior {interior}   room {d.Int("room", -1, 0)}" : "outside");
            fivemStats_U13.Add($"preview lights {d.Int("lights", -1, 0)}   camera {(d.Flag("cam", -1) ? "editor" : "game")}   timecycle {(d.Flag("tc", -1) ? "live" : "game")}" +
                               (string.IsNullOrEmpty(d.Text("mod", -1, "")) ? "" : "   modifier " + d.Text("mod", -1, "")));
            return true;
        }

        private void TickMaterialsApply_U13()
        {
            int serial = MaterialEditing.EditSerial_U13;
            double now = fivemClock_U12.Elapsed.TotalSeconds;
            if (serial != matSerialSeen_U13)
            {
                matSerialSeen_U13 = serial;
                matEditAt_U13 = now;
                matApplyPending_U13 = true;
            }
            if (!matApplyPending_U13 || !panel.FiveMLive_U12 || !panel.FiveMAutoApply_U13) return;
            if (panel.Workspace != LightPanel.Space.Material) return;
            if (now - matEditAt_U13 < panel.FiveMAutoApplySeconds_U13) return;
            matApplyPending_U13 = false;
            ApplyMaterialsNow_U13();
        }

        public static bool AutoApplyDue_U13(double editedAt, double now, float delay, bool pending) =>
            pending && now - editedAt >= delay;

        private void ApplyMaterialsNow_U13()
        {
            if (scene == null || !scene.HasModel) { panel.FiveMSay_U12("nothing open to apply"); return; }
            var res = scene.Files.Select(f => string.IsNullOrEmpty(f.Path) ? null : ResourceNameForPath_U12(f.Path)).FirstOrDefault(r => r != null);
            if (res == null) { panel.FiveMSay_U12("the open file is not inside a server resource - File > Save As into one first"); return; }
            if (!scene.Files.Any(f => f.Dirty)) { RestartFiveMResource_U12(res); return; }
            DoSave();
        }

        private void TickGameView_U13()
        {
            gameCapture_U13 ??= new GameCapture_U13();
            bool want = panel.GameViewOpen_U13;
            gameCapture_U13.Enabled = want;
            if (!want) { gameThumb_U14?.Hide(); panel.GameViewUseThumb_U14 = false; panel.GameViewStatus_U13 = ""; return; }
            if (TickGameThumb_U14()) { gameCapture_U13.Enabled = false; return; }
            gameCapture_U13.Start();
            panel.GameViewStatus_U13 = gameCapture_U13.Status + (gameCapture_U13.FramesPerSecond > 0 ? $"  {gameCapture_U13.FramesPerSecond} fps" : "");
            var f = gameCapture_U13.TakeLatest(gameFrameSerial_U13);
            if (f != null)
            {
                gameFrameSerial_U13 = f.Serial;
                UploadGameFrame_U13(f);
            }
            panel.GameViewTexture_U13 = gameTexId_U13;
            panel.GameViewWidth_U13 = gameTexW_U13;
            panel.GameViewHeight_U13 = gameTexH_U13;
            FillGameDebug_U13();
        }

        private void UploadGameFrame_U13(GameFrame_U13 f)
        {
            var dev = deviceResources?.Device;
            var ctx = deviceResources?.Context;
            if (dev == null || ctx == null || imguiRenderer == null || f.Bgra == null) return;
            if (gameTex_U13 == null || gameTexW_U13 != f.Width || gameTexH_U13 != f.Height)
            {
                if (gameTexId_U13 != IntPtr.Zero) imguiRenderer.UnregisterTexture(gameTexId_U13);
                gameSrv_U13?.Dispose();
                gameTex_U13?.Dispose();
                gameTex_U13 = new Texture2D(dev, new Texture2DDescription
                {
                    Width = f.Width, Height = f.Height, MipLevels = 1, ArraySize = 1,
                    Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ShaderResource, CpuAccessFlags = CpuAccessFlags.Write,
                });
                gameSrv_U13 = new ShaderResourceView(dev, gameTex_U13);
                gameTexId_U13 = imguiRenderer.RegisterTexture(gameSrv_U13);
                gameTexW_U13 = f.Width;
                gameTexH_U13 = f.Height;
            }
            var box = ctx.MapSubresource(gameTex_U13, 0, MapMode.WriteDiscard, MapFlags.None);
            try
            {
                int row = f.Width * 4;
                for (int y = 0; y < f.Height; y++) Marshal.Copy(f.Bgra, y * row, box.DataPointer + y * box.RowPitch, row);
            }
            finally { ctx.UnmapSubresource(gameTex_U13, 0); }
        }
    }
}
