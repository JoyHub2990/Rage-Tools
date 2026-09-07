using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public partial class ParticlePanel
    {
        public sealed class SheetGrid_V29 { public int Cols, Rows; }
        private readonly Dictionary<object, SheetGrid_V29> sheetGrid_V29 = new Dictionary<object, SheetGrid_V29>();

        public Func<GameTexture, IntPtr> SheetTextureId_V29;

        public (int Cols, int Rows) SheetGridFor_V29(object emitterKey)
            => emitterKey != null && sheetGrid_V29.TryGetValue(emitterKey, out var g) ? (g.Cols, g.Rows) : (0, 0);

        private double sheetClock_V29;
        private bool sheetPreviewPlaying_V29 = true;

        private static readonly bool forceSheetOpen_V29 = Environment.GetEnvironmentVariable("RLE_SHEETOPEN") == "1";

        private static ParticleBehaviourAnimateTexture AnimOf_V29(ParticleRule pr)
        {
            foreach (var bl in new[] { pr?.AllBehaviours?.data_items, pr?.DrawBehaviours?.data_items })
            {
                if (bl == null) continue;
                foreach (var b in bl) if (b is ParticleBehaviourAnimateTexture at) return at;
            }
            return null;
        }

        public static ParticleBehaviourAnimateTexture SetAnimated_V29(ParticleRule pr, bool on, int totalCells)
        {
            var at = AnimOf_V29(pr);
            if (!on)
            {
                if (at != null) PtfxAuthor.RemoveBehaviour(pr, at);
                return null;
            }
            if (at != null) return at;
            at = PtfxAuthor.AddBehaviour(pr, ParticleBehaviourType.AnimateTexture) as ParticleBehaviourAnimateTexture;
            if (at == null) return null;
            at.LastFrameID = Math.Max(0, totalCells - 1);
            at.LoopMode = 1;
            at.IsHeldOnLastFrame = 1;
            at.DoFrameBlending = 1;
            at.IsScaledOverParticleLife = 0;
            at.IsRandomised = 0;
            SetRate_V29(at, 24f);
            return at;
        }

        public static float RateOf_V29(ParticleBehaviourAnimateTexture at)
        {
            var v = at?.AnimRateKFP?.Values?.data_items;
            return v != null && v.Length > 0 ? v[0].KeyframeValue.X : 0f;
        }

        public static void SetRate_V29(ParticleBehaviourAnimateTexture at, float fps)
        {
            if (at == null) return;
            at.AnimRateKFP ??= PtfxAuthor.Kfp("ptxu_AnimateTexture:m_animRateKFP", (0f, new SharpDX.Vector4(fps, fps, 0, 0)));
            at.AnimRateKFP.Values ??= new ResourceSimpleList64<ParticleKeyframePropValue>();
            if (at.AnimRateKFP.Values.data_items == null || at.AnimRateKFP.Values.data_items.Length == 0)
                at.AnimRateKFP.Values.data_items = new[] { new ParticleKeyframePropValue { KeyframeTime = SharpDX.Vector4.Zero, KeyframeValue = SharpDX.Vector4.Zero } };
            foreach (var k in at.AnimRateKFP.Values.data_items)
                k.KeyframeValue = new SharpDX.Vector4(fps, Math.Max(fps, k.KeyframeValue.Y), k.KeyframeValue.Z, k.KeyframeValue.W);
        }

        public void DrawSheetBlock_V29(PtfxEmitter em, GameTexture sheet, ParticleRule pr)
        {
            if (sheet == null || em == null || pr == null) return;
            InstallSheetHook_V29();

            var at = AnimOf_V29(pr);
            bool animated = at != null;
            int frames = animated ? Math.Max(1, at.LastFrameID + 1) : 1;
            int detectFrames = Math.Max(frames, (int)pr.TexFrameIDMax + 1);
            var g = AtlasDetect.Detect(sheet, detectFrames);

            if (!sheetGrid_V29.TryGetValue(em, out var ov)) { ov = new SheetGrid_V29(); sheetGrid_V29[em] = ov; }
            int cols = ov.Cols > 0 ? ov.Cols : Math.Max(1, g.gx);
            int rows = ov.Rows > 0 ? ov.Rows : Math.Max(1, g.gy);
            int total = Math.Max(1, cols * rows);
            int texMin = (int)Math.Min(pr.TexFrameIDMin, (uint)(total - 1));
            int texMax = (int)Math.Min(Math.Max(pr.TexFrameIDMax, pr.TexFrameIDMin), (uint)(total - 1));
            float fps = animated ? RateOf_V29(at) : 0f;
            bool overLife = animated && at.IsScaledOverParticleLife != 0;

            if (sheetPreviewPlaying_V29) sheetClock_V29 += Math.Max(0f, ImGui.GetIO().DeltaTime);
            float previewRate = overLife ? Math.Max(1f, frames) / 2.0f : Math.Max(1f, fps);
            var fr = PtfxSimulator.SheetFrame_V29(texMin, texMax, frames, animated,
                                                  animated ? at.LoopMode : 1, animated && at.IsHeldOnLastFrame != 0,
                                                  false, animated && at.DoFrameBlending != 0,
                                                  0f, (float)sheetClock_V29, previewRate, 0x1234u, total);
            int playing = fr.Cell;

            if (forceSheetOpen_V29) ImGui.SetNextItemOpen(true);
            else ImGui.SetNextItemOpen(true, ImGuiCond.Once);
            if (!ImGui.TreeNodeEx("Sprite sheet###v29sheet", ImGuiTreeNodeFlags.SpanAvailWidth)) return;

            ImGui.TextDisabled($"{sheet.Name}   {sheet.Width}x{sheet.Height}   {cols} x {rows} = {total} cell(s)" +
                               (ov.Cols > 0 || ov.Rows > 0 ? "   (yours)" : "   (read off the sheet)"));

            var id = SheetTextureId_V29?.Invoke(sheet) ?? IntPtr.Zero;
            float avail = Math.Max(120f, ImGui.GetContentRegionAvail().X - 4f);
            float w = avail, h = avail * (sheet.Height / (float)Math.Max(1, (int)sheet.Width));
            float maxH = 420f * UiScale_V17.Scale;
            if (h > maxH) { h = maxH; w = maxH * (sheet.Width / (float)Math.Max(1, (int)sheet.Height)); }

            var p0 = ImGui.GetCursorScreenPos();
            if (id != IntPtr.Zero) ImGui.Image(id, new Vector2(w, h));
            else { ImGui.Dummy(new Vector2(w, h)); ImGui.TextDisabled("(the sheet has no readable pixels)"); }

            var dl = ImGui.GetWindowDrawList();
            uint line = ImGui.GetColorU32(new Vector4(0.85f, 0.85f, 0.85f, 0.45f));
            uint dim = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.35f));
            uint live = ImGui.GetColorU32(new Vector4(1.0f, 0.78f, 0.20f, 1.0f));
            uint run = ImGui.GetColorU32(new Vector4(0.30f, 0.95f, 1.00f, 0.85f));
            uint startCol = ImGui.GetColorU32(new Vector4(0.55f, 1.0f, 0.55f, 0.9f));
            float cw = w / cols, ch = h / rows;
            bool inWindowOnly = animated && at.LoopMode == 2;
            for (int i = 0; i < total; i++)
            {
                int cx = i % cols, cy = i / cols;
                var a = new Vector2(p0.X + cx * cw, p0.Y + cy * ch);
                var b = new Vector2(a.X + cw, a.Y + ch);
                bool inRun = !animated ? (i >= texMin && i <= texMax)
                           : inWindowOnly ? (i >= texMin && i <= texMax)
                           : i < frames;
                if (!inRun) dl.AddRectFilled(a, b, dim);
                dl.AddRect(a, b, line);
                if (i >= texMin && i <= texMax && animated && !inWindowOnly)
                    dl.AddRect(new Vector2(a.X + 1, a.Y + 1), new Vector2(b.X - 1, b.Y - 1), startCol);
                if (i == playing) dl.AddRect(new Vector2(a.X + 1.5f, a.Y + 1.5f), new Vector2(b.X - 1.5f, b.Y - 1.5f), live, 0, 0, 2.5f);
                if (cw > 14 && ch > 12)
                {
                    var lbl = i.ToString();
                    float fs = Math.Clamp(Math.Min(cw, ch) * 0.34f, 12f, 26f * UiScale_V17.Scale);
                    var ts = ImGui.GetFont().CalcTextSizeA(fs, float.MaxValue, 0f, lbl);
                    var tp = new Vector2(a.X + 3f, a.Y + 2f);
                    dl.AddRectFilled(new Vector2(tp.X - 2f, tp.Y - 1f),
                                     new Vector2(tp.X + ts.X + 2f, tp.Y + ts.Y + 1f),
                                     ImGui.GetColorU32(new Vector4(0, 0, 0, 0.62f)), 3f);
                    dl.AddText(ImGui.GetFont(), fs, tp,
                               inRun ? ImGui.GetColorU32(new Vector4(1, 1, 1, 1f))
                                     : ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 1f)), lbl);
                }
            }
            if (ImGui.IsItemHovered())
            {
                var m = ImGui.GetMousePos();
                int cx = (int)((m.X - p0.X) / cw), cy = (int)((m.Y - p0.Y) / ch);
                int cell = Math.Clamp(cy * cols + cx, 0, total - 1);
                ImGui.SetTooltip($"cell {cell}   (click = start cell; ctrl+click = end of the start window)");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    if (ImGui.GetIO().KeyCtrl) pr.TexFrameIDMax = (uint)Math.Max(cell, (int)pr.TexFrameIDMin);
                    else { pr.TexFrameIDMin = (uint)cell; if (pr.TexFrameIDMax < pr.TexFrameIDMin) pr.TexFrameIDMax = pr.TexFrameIDMin; }
                    TouchFromTimeline(true);
                }
            }

            float thumb = 72f * UiScale_V17.Scale;
            if (id != IntPtr.Zero)
            {
                int cx = playing % cols, cy = playing / cols;
                var uv0 = new Vector2(cx / (float)cols, cy / (float)rows);
                var uv1 = new Vector2((cx + 1) / (float)cols, (cy + 1) / (float)rows);
                ImGui.Image(id, new Vector2(thumb, thumb), uv0, uv1);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("The flipbook, playing at the rate below. Click to pause.");
                if (ImGui.IsItemClicked()) sheetPreviewPlaying_V29 = !sheetPreviewPlaying_V29;
            }
            else ImGui.Dummy(new Vector2(thumb, thumb));
            ImGui.SameLine();
            ImGui.BeginGroup();
            {
                bool on = animated;
                if (ImGui.Checkbox("Animate##v29", ref on))
                {
                    SetAnimated_V29(pr, on, total);
                    TouchFromTimeline(true);
                    Status = on ? $"animating {total} cell(s) at 24 fps" : "animation off - one cell per particle";
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Play through the cells over the particle's life. Off, each particle\n" +
                                     "shows one cell, chosen between the start cells below.");
                ImGui.SameLine();
                ImGui.TextDisabled(animated ? $"cell {playing}{(sheetPreviewPlaying_V29 ? "" : "  (paused)")}" : $"cell {playing}");

                if (animated)
                {
                    float stepW = ImGui.GetFrameHeight() * 2f + ImGui.GetStyle().ItemInnerSpacing.X * 2f;
                    float digitsW = ImGui.CalcTextSize(new string((char)48, Math.Max(3, total.ToString().Length) + 1)).X;
                    ImGui.SetNextItemWidth(digitsW + stepW + ImGui.GetStyle().FramePadding.X * 2f);
                    int nf = frames;
                    if (ImGui.InputInt("Frames##v29", ref nf, 1))
                    {
                        at.LastFrameID = Math.Clamp(nf, 1, total) - 1;
                        TouchFromTimeline(true);
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("How many cells the run is, counted from cell 0. A whole sheet = every cell.");
                    ImGui.SameLine();
                    if (ImGui.SmallButton("all##v29frames")) { at.LastFrameID = total - 1; TouchFromTimeline(true); }

                    if (overLife) ImGui.BeginDisabled();
                    ImGui.SetNextItemWidth(Math.Max(84f * UiScale_V17.Scale, ImGui.CalcTextSize("240.0").X * 2.2f));
                    float f2 = fps;
                    if (ImGui.DragFloat("fps##v29", ref f2, 0.5f, 0.5f, 240f, "%.1f"))
                    {
                        SetRate_V29(at, Math.Clamp(f2, 0.5f, 240f));
                        TouchFromTimeline(false);
                    }
                    if (overLife) ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(overLife ? "Not used while the run is scaled over the particle's life."
                                                  : "Cells per second. The game's fire runs at 24, its smoke at 4, explosions near 70.");
                }
            }
            ImGui.EndGroup();

            if (animated)
            {
                int mode = Math.Clamp(at.LoopMode, 0, 2);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##v29loop", ref mode,
                                "play once, then hold the last cell\0loop the run (fire, smoke, explosions)\0cycle inside the start cells only (insects' wings)\0", 3))
                {
                    at.LoopMode = mode;
                    TouchFromTimeline(true);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("What happens at the end of the run. The third mode is how the game's butterflies\n" +
                                     "flap: they never leave their own four cells, whatever the frame count says.");

                bool ol = overLife;
                if (ImGui.Checkbox("Over lifetime##v29", ref ol)) { at.IsScaledOverParticleLife = (byte)(ol ? 1 : 0); TouchFromTimeline(true); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("The run plays exactly once across the particle's life, whatever its length - the game's embers do this.");
                ImGui.SameLine();
                bool bl = at.DoFrameBlending != 0;
                if (ImGui.Checkbox("Blend frames##v29", ref bl)) { at.DoFrameBlending = (byte)(bl ? 1 : 0); TouchFromTimeline(true); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cross-fade each cell into the next instead of cutting. Fire and smoke blend; insects cut.");
                ImGui.SameLine();
                bool hold = at.IsHeldOnLastFrame != 0;
                if (ImGui.Checkbox("Hold last##v29", ref hold)) { at.IsHeldOnLastFrame = (byte)(hold ? 1 : 0); TouchFromTimeline(true); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Stay on the last cell when the run ends. The game's fire and smoke set it; its insects do not.");
            }

            {
                ImGui.SetNextItemWidth(70 * UiScale_V17.Scale);
                int s0 = texMin;
                if (ImGui.InputInt("Start cell##v29", ref s0, 1))
                {
                    pr.TexFrameIDMin = (uint)Math.Clamp(s0, 0, total - 1);
                    if (pr.TexFrameIDMax < pr.TexFrameIDMin) pr.TexFrameIDMax = pr.TexFrameIDMin;
                    TouchFromTimeline(true);
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("The first cell a particle may be born on.");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(70 * UiScale_V17.Scale);
                int s1 = texMax;
                if (ImGui.InputInt("to##v29", ref s1, 1))
                {
                    pr.TexFrameIDMax = (uint)Math.Clamp(s1, texMin, total - 1);
                    TouchFromTimeline(true);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("...and the last. Each particle is born somewhere between the two, so a\n" +
                                     "fire's flames do not all flicker in step. One cell = every particle starts there.");
            }

            {
                ImGui.SetNextItemWidth(60 * UiScale_V17.Scale);
                int c2 = cols;
                if (ImGui.InputInt("Columns##v29", ref c2, 0)) { ov.Cols = Math.Clamp(c2, 0, 64); TouchFromTimeline(true); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("0 = use what was read off the sheet. The game reads the layout off the image\ntoo, so keep every cell the same size.");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60 * UiScale_V17.Scale);
                int r2 = rows;
                if (ImGui.InputInt("Rows##v29", ref r2, 0)) { ov.Rows = Math.Clamp(r2, 0, 64); TouchFromTimeline(true); }
                if (ov.Cols > 0 || ov.Rows > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("read again##v29")) { ov.Cols = ov.Rows = 0; TouchFromTimeline(true); }
                }

                if (animated && frames > 1)
                {
                    var gg = AtlasDetect.GameGrid_V41(sheet.Width, sheet.Height, frames);
                    bool matches = gg.gx == cols && gg.gy == rows;
                    if (matches)
                        ImGui.TextDisabled($"In game: {frames} frames slice this sheet {gg.gx} x {gg.gy} - matches the view above.");
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Warn);
                        ImGui.TextWrapped($"IN GAME this will slice {gg.gx} x {gg.gy}: the game derives the grid from the " +
                            $"frame count ({frames}), and the .ypt has no field to say otherwise. The view above shows " +
                            $"{cols} x {rows}. Lay the image out as {gg.gx} x {gg.gy} equal cells (or pick a frame count " +
                            "that factors to your layout) and the two will agree.");
                        ImGui.PopStyleColor();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Read off the install's own sheets: 49 frames is 7x7 even where the cells come out " +
                                         "tall and thin, 32 frames on a wide sheet is 8x4. Columns/Rows here only correct the preview.");
                }
            }

            ImGui.TreePop();
        }

        private bool sheetHookInstalled_V29;
        private void InstallSheetHook_V29()
        {
            if (sheetHookInstalled_V29 || Sim == null) return;
            sheetHookInstalled_V29 = true;
            Sim.SheetGridOverride_V29 = SheetGridFor_V29;
        }
    }
}

