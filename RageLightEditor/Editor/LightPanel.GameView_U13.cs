using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool GameViewOpen_U13;
        public bool GameDebug_U13 = true;
        public bool FiveMAutoApply_U13;
        public float FiveMAutoApplySeconds_U13 = 2.0f;
        public bool RequestFiveMApplyNow_U13;
        public IntPtr GameViewTexture_U13;
        public int GameViewWidth_U13, GameViewHeight_U13;
        public string GameViewStatus_U13 = "";
        public readonly List<string> GameDebugLines_U13 = new List<string>();

        public int TimecycleEditorTab_U13 => tcTab;

        private void DrawGameView_U13(float displayWidth, float displayHeight)
        {
            if (!GameViewOpen_U13 || GameViewDetached_U16) { ClearGameViewRect_U14(); return; }
            ImGui.SetNextWindowSize(new Vector2(400, 420), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new Vector2(8, Math.Max(displayHeight - 432, 40)), ImGuiCond.FirstUseEver);
            bool open = GameViewOpen_U13;
            if (!ImGui.Begin("Game###gameview13", ref open, ImGuiWindowFlags.NoScrollbar))
            {
                ClearGameViewRect_U14();
                GameViewOpen_U13 = open;
                ImGui.End();
                return;
            }
            GameViewOpen_U13 = open;
            DrawGameViewDetachRow_U16();
            float w = Math.Max(ImGui.GetContentRegionAvail().X, 64);
            if (GameViewUseThumb_U14) GameViewThumbArea_U14(w);
            else if (GameViewTexture_U13 != IntPtr.Zero && GameViewWidth_U13 > 0 && GameViewHeight_U13 > 0)
            {
                float h = w * GameViewHeight_U13 / GameViewWidth_U13;
                ImGui.Image(GameViewTexture_U13, new Vector2(w, h));
            }
            else
            {
                var p = ImGui.GetCursorScreenPos();
                float h = w * 9.0f / 16.0f;
                ImGui.GetWindowDrawList().AddRectFilled(p, new Vector2(p.X + w, p.Y + h), ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.06f, 1.0f)));
                var msg = string.IsNullOrEmpty(GameViewStatus_U13) ? "waiting for the game" : GameViewStatus_U13;
                var ts = ImGui.CalcTextSize(msg);
                ImGui.GetWindowDrawList().AddText(new Vector2(p.X + Math.Max((w - ts.X) * 0.5f, 4), p.Y + (h - ts.Y) * 0.5f), ImGui.GetColorU32(UiTheme.Muted), msg);
                ImGui.Dummy(new Vector2(w, h));
            }
            if (GameDebug_U13)
            {
                if (!string.IsNullOrEmpty(GameViewStatus_U13)) ImGui.TextDisabled(GameViewStatus_U13);
                foreach (var l in GameDebugLines_U13) ImGui.Text(l);
            }
            ImGui.End();
        }

        private void DrawFiveMExtras_U13()
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("MATERIALS & TIMECYCLE");
            FiveMToggle_U12("Auto-apply materials: save and reload the resource after a change", ref FiveMAutoApply_U13,
                            "The game cannot change a material while it runs. With this on, a couple of seconds after your last\nedit in Materials the file is saved and its resource restarted, so the game shows the change on its own.\nThe file has to live inside a server resource.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.DragFloat("##fmapplydelay", ref FiveMAutoApplySeconds_U13, 0.1f, 0.5f, 15.0f, "%.1f s");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("How long to wait after the last edit before saving and reloading.");
            ImGui.BeginDisabled(!FiveMConnected_U12);
            if (ImGui.Button("Apply materials to the game now")) RequestFiveMApplyNow_U13 = true;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Saves the open file and restarts its resource right away.");
            ImGui.TextDisabled("Timecycle edits are live on their own: Live on, open the Timecycle editor and drag a value.");

            DrawFiveMAsiSection_U16();

            ImGui.Spacing();
            ImGui.TextDisabled("GAME VIEW");
            FiveMToggle_U12("Show the game view (bottom left)", ref GameViewOpen_U13,
                            "A small live picture of the FiveM window inside the tool. The game must be running and not minimised.");
            ImGui.SameLine();
            FiveMToggle_U12("debug info", ref GameDebug_U13, "Game fps, ping, player position, interior and room, preview lights, link traffic.");
            DrawGameViewDetachToggle_U16();
        }
    }
}
