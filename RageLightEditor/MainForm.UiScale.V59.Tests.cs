using System;
using ImGuiNET;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_UiScaleDrag_V59(Action<string, bool, string> check)
        {
            float was = UiScale_V17.Scale;
            try
            {
                var style = ImGui.GetStyle();

                UiScale_V17.ApplyStyleScale(1.0f);
                var pad1 = style.WindowPadding;
                var min1 = style.WindowMinSize;

                // a slider dragged from 1x to 4x and back, a frame at a time
                for (int i = 0; i <= 120; i++) UiScale_V17.ApplyStyleScale(1.0f + i * 0.025f);
                for (int i = 120; i >= 0; i--) UiScale_V17.ApplyStyleScale(1.0f + i * 0.025f);

                check("v59 scale: dragging the interface size never drives WindowMinSize below 1",
                      style.WindowMinSize.X >= 1.0f && style.WindowMinSize.Y >= 1.0f,
                      $"WindowMinSize {style.WindowMinSize.X} x {style.WindowMinSize.Y}");

                check("v59 scale: ...and coming back to 1x restores the sizes it started with",
                      Math.Abs(style.WindowPadding.X - pad1.X) < 0.01f &&
                      Math.Abs(style.WindowPadding.Y - pad1.Y) < 0.01f &&
                      Math.Abs(style.WindowMinSize.X - min1.X) < 0.01f,
                      $"padding {style.WindowPadding.X} (was {pad1.X}), min {style.WindowMinSize.X} (was {min1.X})");

                UiScale_V17.ApplyStyleScale(2.0f);
                var pad2 = style.WindowPadding;
                check("v59 scale: 2x really is twice 1x, not whatever the last drag left behind",
                      Math.Abs(pad2.X - pad1.X * 2.0f) < 1.01f,
                      $"{pad1.X} -> {pad2.X}");

                for (int i = 0; i < 50; i++) UiScale_V17.ApplyStyleScale(2.0f);
                check("v59 scale: re-applying the same scale does not drift",
                      Math.Abs(style.WindowPadding.X - pad2.X) < 0.01f,
                      $"{pad2.X} -> {style.WindowPadding.X} after 50 more applies");
            }
            catch (Exception ex) { check("v59 scale", false, ex.Message); }
            finally
            {
                try { UiScale_V17.ApplyStyleScale(was); } catch { }
            }
        }
    }
}
