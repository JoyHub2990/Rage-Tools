using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public static class UiFit_V32
    {
        public static float SmallButtonW(string label)
        {
            var t = ImGui.CalcTextSize(StripId(label));
            return t.X + ImGui.GetStyle().FramePadding.X * 2.0f;
        }

        public static float ButtonW(string label)
        {
            var t = ImGui.CalcTextSize(StripId(label));
            return t.X + ImGui.GetStyle().FramePadding.X * 2.0f;
        }

        public static float TextW(string s) => ImGui.CalcTextSize(s ?? "").X;

        public static float Reserve(params float[] widths)
        {
            if (widths == null || widths.Length == 0) return 0.0f;
            float sp = ImGui.GetStyle().ItemSpacing.X;
            float total = 0.0f;
            foreach (var w in widths) total += w + sp;
            return total + sp * 0.5f;
        }

        public static float LeadingWidth(float reserve, float min = 60.0f)
            => Math.Max(min, ImGui.GetContentRegionAvail().X - reserve);

        private static string StripId(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            int i = label.IndexOf("##", StringComparison.Ordinal);
            return i >= 0 ? label.Substring(0, i) : label;
        }
    }
}

