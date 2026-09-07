using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private static void FiveMStep_U17(string number, string title)
        {
            ImGui.Spacing();
            ImGui.TextColored(UiTheme.Accent, number);
            ImGui.SameLine();
            ImGui.Text(title);
        }

        private static void Tip_U17(string text)
        {
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(text);
        }

        public void DrawFiveMBody_U12()
        {
            DrawFiveMWarning_U16();
            var col = FiveMConnected_U12 ? UiTheme.Ok : (FiveMListening_U12 ? UiTheme.Warn : UiTheme.Muted);
            ImGui.TextColored(col, FiveMConnected_U12 ? "connected" : (FiveMListening_U12 ? "waiting for the game" : "off"));
            if (!string.IsNullOrEmpty(FiveMPlayerText_U12)) { ImGui.SameLine(); ImGui.TextDisabled(FiveMPlayerText_U12); }

            FiveMStep_U17("1", "Install once");
            ImGui.SetNextItemWidth(-200.0f);
            ImGui.InputTextWithHint("##fmfolder", "your server's resources folder", ref FiveMResourcesFolder_U12, 512);
            ImGui.SameLine();
            if (ImGui.Button("Browse##fm")) RequestFiveMBrowse_U12 = true;
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(FiveMResourcesFolder_U12));
            if (ImGui.Button("Install##fm")) RequestFiveMInstall_U12 = true;
            ImGui.EndDisabled();
            Tip_U17("Writes the ragetools_linking resource into that folder. Run it again after an update.");
            ImGui.TextDisabled("server.cfg");
            ImGui.SameLine();
            ImGui.TextColored(UiTheme.Accent, "ensure ragetools_linking");
            ImGui.SameLine();
            ImGui.TextColored(UiTheme.Accent, "set ragetools_dev 1");
            ImGui.SameLine();
            if (ImGui.SmallButton("copy##fmcfg")) ImGui.SetClipboardText("ensure ragetools_linking\nset ragetools_dev 1\n");
            ImGui.TextDisabled("plugin");
            ImGui.SameLine();
            ImGui.TextColored(FiveMAsiInstalled_U16 ? UiTheme.Ok : UiTheme.Muted, FiveMAsiInstalled_U16 ? "installed" : "not installed");
            ImGui.SameLine();
            if (ImGui.SmallButton(FiveMAsiInstalled_U16 ? "reinstall##asi" : "install##asi")) RequestInstallAsi_U16 = true;
            Tip_U17("RageToolsLive.asi in FiveM's plugins folder makes Materials values live in the game.\nRestart FiveM after installing it. Servers in pure mode block plugins.");

            FiveMStep_U17("2", "Start the server and join it");
            ImGui.TextDisabled(FiveMConnected_U12 ? "the game is linked" : (FiveMListening_U12 ? $"listening on port {FiveMPort_U12}" : "not listening - see Details"));

            FiveMStep_U17("3", "Turn it on");
            ImGui.Checkbox("Live##fm", ref FiveMLive_U12);
            Tip_U17("The game follows what you edit: lights, props, timecycle, time and weather.");
            ImGui.SameLine();
            ImGui.Checkbox("restart resource after save##fm", ref FiveMRestartAfterSave_U12);
            Tip_U17("Saving a file that lives in a server resource restarts that resource, so the game reloads it.");
            ImGui.SameLine();
            ImGui.Checkbox("auto-apply materials##fm", ref FiveMAutoApply_U13);
            Tip_U17("A moment after your last Materials edit the file is saved and its resource restarted.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70.0f);
            ImGui.DragFloat("##fmapplydelay", ref FiveMAutoApplySeconds_U13, 0.1f, 0.5f, 15.0f, "%.1f s");

            ImGui.TextDisabled("camera");
            ImGui.SameLine();
            ImGui.RadioButton("off##fmcam", ref FiveMFollow_U12, 0);
            ImGui.SameLine();
            ImGui.RadioButton("game follows editor##fmcam", ref FiveMFollow_U12, 1);
            Tip_U17("The game camera goes where this viewport looks; the world streams in around it.");
            ImGui.SameLine();
            ImGui.RadioButton("editor follows game##fmcam", ref FiveMFollow_U12, 2);
            Tip_U17("Walk around in the game and this viewport follows.");

            ImGui.TextDisabled("game view");
            ImGui.SameLine();
            ImGui.Checkbox("show##gv", ref GameViewOpen_U13);
            Tip_U17("A live picture of the FiveM window, bottom left.");
            ImGui.SameLine();
            ImGui.Checkbox("debug##gv", ref GameDebug_U13);
            DrawGameViewDetachToggle_U16();

            ImGui.BeginDisabled(!FiveMConnected_U12);
            if (ImGui.Button("Player to camera##fm")) RequestFiveMTeleport_U12 = true;
            ImGui.SameLine();
            if (ImGui.Button("Camera to player##fm")) RequestFiveMGoToPlayer_U12 = true;
            ImGui.SameLine();
            if (ImGui.Button("Send lights again##fm")) RequestFiveMResend_U12 = true;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140.0f);
            ImGui.InputTextWithHint("##fmrestartname", "resource", ref FiveMRestartName_U12, 128);
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(FiveMRestartName_U12));
            if (ImGui.Button("Restart##fm")) RequestFiveMRestart_U12 = true;
            ImGui.EndDisabled();
            ImGui.EndDisabled();
            if (!string.IsNullOrEmpty(FiveMPickText_U12))
            {
                ImGui.TextColored(UiTheme.Accent, FiveMPickText_U12);
                ImGui.SameLine();
                if (ImGui.SmallButton("look##fmpick")) RequestFiveMGoToPick_U12 = true;
            }
            ImGui.TextDisabled("F9 in the game picks the prop you look at.");

            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Details##fm"))
            {
                bool on = FiveMListening_U12;
                if (ImGui.Button(on ? "Stop listening##fm" : "Start listening##fm", new Vector2(130, 0))) RequestFiveMToggle_U12 = true;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                if (!on) ImGui.InputInt("port##fivemport", ref FiveMPort_U12, 0, 0);
                else ImGui.TextDisabled($"port {FiveMPort_U12}");
                ImGui.SameLine();
                ImGui.Checkbox("start with the tool##fm", ref FiveMAutoStart_U12);
                if (!on)
                {
                    ImGui.Checkbox("game on another PC##fm", ref FiveMAnyInterface_U12);
                    Tip_U17("Listen on every network interface, then reinstall the resource so it carries this PC's address.");
                }
                ImGui.Checkbox("live material values through the plugin##fm", ref FiveMAsiEnabled_U16);
                ImGui.SameLine();
                ImGui.TextColored(FiveMAsiConnected_U16 ? UiTheme.Ok : UiTheme.Muted, FiveMAsiStatus_U16 ?? "");
                ImGui.BeginDisabled(!FiveMAsiConnected_U16);
                if (ImGui.SmallButton("restore the game's materials##fm")) RequestAsiReset_U16 = true;
                ImGui.EndDisabled();
                if (!string.IsNullOrEmpty(FiveMInstalledPath_U12))
                {
                    ImGui.TextDisabled(FiveMInstalledPath_U12);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("show##fminstalled")) RequestFiveMReveal_U12 = FiveMInstalledPath_U12;
                }
                ImGui.BeginChild("##fivemlog", new Vector2(0, 110), ImGuiChildFlags.Borders);
                foreach (var l in FiveMLog_U12) ImGui.TextWrapped(l);
                if (FiveMLog_U12.Count == 0) ImGui.TextDisabled("nothing yet");
                ImGui.EndChild();
            }
        }
    }
}
