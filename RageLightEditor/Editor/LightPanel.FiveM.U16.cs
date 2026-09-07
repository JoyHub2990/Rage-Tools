using System;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool GameViewDetached_U16;
        public bool RequestGameViewDetach_U16, RequestGameViewAttach_U16;
        public string FiveMWarning_U16 = "";
        public bool FiveMAsiEnabled_U16 = true;
        public string FiveMAsiStatus_U16 = "off";
        public bool FiveMAsiConnected_U16;
        public bool FiveMAsiInstalled_U16;
        public string FiveMAsiPath_U16 = "";
        public bool RequestInstallAsi_U16, RequestAsiReset_U16;

        private void DrawFiveMWarning_U16()
        {
            if (string.IsNullOrEmpty(FiveMWarning_U16)) return;
            ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Danger);
            ImGui.TextWrapped(FiveMWarning_U16);
            ImGui.PopStyleColor();
        }

        private void DrawFiveMAsiSection_U16()
        {
            ImGui.Spacing();
            ImGui.TextDisabled("LIVE MATERIALS  (plugin)");
            FiveMToggle_U12("Use the RageToolsLive plugin for live material values", ref FiveMAsiEnabled_U16,
                            "A small plugin inside FiveM lets the tool write shader values (specular, bump, emissive...)\nstraight into the loaded model, so Materials sliders are live for every copy of the prop.\nTextures still go through save and reload. Servers in pure mode block plugins.");
            ImGui.TextColored(FiveMAsiConnected_U16 ? UiTheme.Ok : UiTheme.Muted, FiveMAsiStatus_U16 ?? "");
            if (ImGui.Button("Install the plugin into FiveM")) RequestInstallAsi_U16 = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Writes RageToolsLive.asi into FiveM's plugins folder. Start (or restart) FiveM afterwards.");
            ImGui.SameLine();
            ImGui.BeginDisabled(!FiveMAsiConnected_U16);
            if (ImGui.Button("Restore the game's materials")) RequestAsiReset_U16 = true;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Puts every value the plugin changed back to what the game loaded.");
            if (!string.IsNullOrEmpty(FiveMAsiPath_U16))
                ImGui.TextDisabled(FiveMAsiPath_U16 + (FiveMAsiInstalled_U16 ? "   (installed)" : "   (not installed)"));
        }

        private void DrawGameViewDetachRow_U16()
        {
            if (ImGui.SmallButton("Detach##gvdetach")) RequestGameViewDetach_U16 = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Put the game view in its own window: drag it to another screen, resize it freely.");
            ImGui.SameLine();
            ImGui.TextDisabled("game view");
        }

        private void DrawGameViewDetachToggle_U16()
        {
            ImGui.SameLine();
            bool det = GameViewDetached_U16;
            if (ImGui.Checkbox("own window##gvdet", ref det))
            {
                if (det) RequestGameViewDetach_U16 = true;
                else RequestGameViewAttach_U16 = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show the game view in a separate window instead of inside the tool.");
        }
    }
}
