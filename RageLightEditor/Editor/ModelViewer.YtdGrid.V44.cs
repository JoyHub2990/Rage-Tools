using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        public bool YtdGrid_V44 = true;
        public float YtdCell_V44 = 128f;

        public static int YtdGridColumns_V44(float availW, float cell, float spacing) =>
            Math.Max(1, (int)((availW + spacing) / (cell + spacing)));

        private static string FitName_V44(string s, float px)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (ImGui.CalcTextSize(s).X <= px) return s;
            int lo = 0, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (ImGui.CalcTextSize(s.Substring(0, mid) + "..").X <= px) lo = mid; else hi = mid - 1;
            }
            return lo <= 0 ? ".." : s.Substring(0, lo) + "..";
        }

        private void DrawYtdCell_V44(AssetTextureInfo t, int i, float cell, float textH)
        {
            ImGui.PushID(i);
            var start = ImGui.GetCursorPos();
            bool sel = i == selTexture;
            if (ImGui.Selectable("##cell", sel, ImGuiSelectableFlags.None, new Vector2(cell, cell + textH)))
            {
                selTexture = i;
                YtdGrid_V44 = false;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{t.Name}\n{t.Width}x{t.Height}  {t.Format}" +
                                 (t.Levels > 1 ? $"  {t.Levels} mips" : "") + "\nClick to open it on its own.");
            if (sel)
            {
                var mn = ImGui.GetItemRectMin();
                var mx = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddRect(mn, mx, ImGui.GetColorU32(UiTheme.Accent), 3f, 0, 1.5f);
            }

            var id = t.HasData ? (TextureId?.Invoke(t.Texture) ?? IntPtr.Zero) : IntPtr.Zero;
            if (id != IntPtr.Zero && t.Width > 0 && t.Height > 0)
            {
                float s = Math.Min(cell / t.Width, cell / t.Height);
                float w = Math.Max(1, t.Width * s), h = Math.Max(1, t.Height * s);
                ImGui.SetCursorPos(new Vector2(start.X + (cell - w) * 0.5f, start.Y + (cell - h) * 0.5f));
                ImGui.Image(id, new Vector2(w, h));
            }
            else
            {
                ImGui.SetCursorPos(new Vector2(start.X + cell * 0.4f, start.Y + cell * 0.45f));
                ImGui.TextDisabled("?");
            }

            ImGui.SetCursorPos(new Vector2(start.X + 2, start.Y + cell));
            ImGui.TextDisabled(FitName_V44(t.Name, cell - 4));

            ImGui.SetCursorPos(start);
            ImGui.Dummy(new Vector2(cell, cell + textH));
            ImGui.PopID();
        }

        private void DrawYtdGrid_V44(IReadOnlyList<AssetTextureInfo> list)
        {
            YtdCell_V44 = Math.Clamp(YtdCell_V44, 64f, 320f);
            float cell = YtdCell_V44;
            float textH = ImGui.GetTextLineHeightWithSpacing();
            const float spacing = 10f;

            if (!ImGui.BeginChild("##ytdgrid", new Vector2(0, 0))) { ImGui.EndChild(); return; }
            int cols = YtdGridColumns_V44(ImGui.GetContentRegionAvail().X, cell, spacing);
            int rows = (list.Count + cols - 1) / cols;
            float rowH = cell + textH + spacing;

            unsafe
            {
                var clip = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                clip.Begin(rows, rowH);
                while (clip.Step())
                {
                    for (int r = clip.DisplayStart; r < clip.DisplayEnd; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            int i = r * cols + c;
                            if (i >= list.Count) break;
                            if (c > 0) ImGui.SameLine(0, spacing);
                            DrawYtdCell_V44(list[i], i, cell, textH);
                        }
                        ImGui.Dummy(new Vector2(1, spacing - 4));
                    }
                }
                clip.End();
                clip.Destroy();
            }
            ImGui.EndChild();
        }
    }
}
