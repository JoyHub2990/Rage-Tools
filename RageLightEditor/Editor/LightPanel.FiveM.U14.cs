using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool FiveMDetached_U14;
        public bool RequestFiveMDetach_U14;
        public bool RequestFiveMAttach_U14;
        public bool GameViewUseThumb_U14;
        public float GameViewAspect_U14 = 9.0f / 16.0f;
        public int GameViewRectX_U14, GameViewRectY_U14, GameViewRectW_U14, GameViewRectH_U14;

        public void DrawFiveMDetached_U14(float displayW, float displayH, bool formFocused)
        {
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(displayW, displayH), ImGuiCond.Always);
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            bool open = ImGui.Begin("###FiveMDetached14", flags);
            ImGui.PopStyleVar();
            if (open)
            {
                DrawFiveMDetachRow_U14();
                DrawFiveMBody_U12();
            }
            ImGui.End();
        }

        private void DrawFiveMDetachRow_U14()
        {
            bool detached = FiveMDetached_U14;
            ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.AccentDim);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiTheme.Accent);
            if (ImGui.SmallButton(detached ? "Attach to the main window" : "Detach into its own window"))
            {
                if (detached) RequestFiveMAttach_U14 = true;
                else RequestFiveMDetach_U14 = true;
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(detached ? "Puts the panel back inside the main window."
                                          : "Opens this panel as its own window, so it can sit on another monitor next to the game.");
            ImGui.SameLine();
            ImGui.TextColored(FiveMConnected_U12 ? UiTheme.Ok : FiveMMenuColour_U12, "FiveM Live Linking");
            ImGui.Separator();
        }

        private void GameViewThumbArea_U14(float w)
        {
            float aspect = Math.Max(GameViewAspect_U14, 0.01f);
            var avail = ImGui.GetContentRegionAvail();
            float reserve = GameDebug_U13 ? ImGui.GetTextLineHeightWithSpacing() * 6.0f : 4.0f;
            float h = Math.Max(w * aspect, 32);
            float maxH = Math.Max(avail.Y - reserve, 32);
            float fullW = w;
            if (h > maxH) { h = maxH; w = Math.Max(h / aspect, 32); }
            var p0 = ImGui.GetCursorScreenPos();
            var p = new Vector2(p0.X + Math.Max((fullW - w) * 0.5f, 0), p0.Y);
            ImGui.SetCursorScreenPos(p);
            ImGui.GetWindowDrawList().AddRectFilled(p, new Vector2(p.X + w, p.Y + h), ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.03f, 1.0f)));
            ImGui.Dummy(new Vector2(w, h));
            bool shown = ImGui.IsItemVisible();
            GameViewRectX_U14 = (int)Math.Round(p.X);
            GameViewRectY_U14 = (int)Math.Round(p.Y);
            GameViewRectW_U14 = shown ? (int)Math.Round(w) : 0;
            GameViewRectH_U14 = shown ? (int)Math.Round(h) : 0;
        }

        public void ClearGameViewRect_U14()
        {
            GameViewRectW_U14 = 0;
            GameViewRectH_U14 = 0;
        }
    }
}
