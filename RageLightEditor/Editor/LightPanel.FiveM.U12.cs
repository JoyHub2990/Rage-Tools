using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool FiveMWindowOpen_U12;
        public bool FiveMListening_U12;
        public bool FiveMConnected_U12;
        public bool FiveMLive_U12;
        public int FiveMFollow_U12;
        public bool FiveMRestartAfterSave_U12 = true;
        public bool FiveMAutoStart_U12 = true;
        public bool FiveMAnyInterface_U12;
        public int FiveMPort_U12 = FiveMBridge.DefaultPort;
        public string FiveMResourcesFolder_U12 = "";
        public string FiveMRestartName_U12 = "";
        public string FiveMInstalledPath_U12 = "";
        public string FiveMStatus_U12 = "off";
        public string FiveMPlayerText_U12 = "";
        public string FiveMPickText_U12 = "";
        public readonly List<string> FiveMLog_U12 = new List<string>();

        public bool RequestFiveMToggle_U12;
        public bool RequestFiveMInstall_U12;
        public bool RequestFiveMBrowse_U12;
        public bool RequestFiveMRestart_U12;
        public bool RequestFiveMTeleport_U12;
        public bool RequestFiveMGoToPlayer_U12;
        public bool RequestFiveMGoToPick_U12;
        public bool RequestFiveMResend_U12;
        public string RequestFiveMReveal_U12;

        public static readonly Vector4 FiveMMenuColour_U12 = new Vector4(1.0f, 0.62f, 0.2f, 1.0f);

        public void FiveMSay_U12(string line)
        {
            FiveMLog_U12.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (FiveMLog_U12.Count > 12) FiveMLog_U12.RemoveAt(0);
        }

        private void DrawBridgeMenu_U12()
        {
            if (ForceOpenHeader != null && ("Bridge".Contains(ForceOpenHeader, StringComparison.OrdinalIgnoreCase) || "FiveM".Contains(ForceOpenHeader, StringComparison.OrdinalIgnoreCase)))
                FiveMWindowOpen_U12 = true;
            ImGui.PushStyleColor(ImGuiCol.Text, FiveMConnected_U12 ? UiTheme.Ok : FiveMMenuColour_U12);
            bool menuOpen = ImGui.BeginMenu("FiveM Live Linking###bridgemenu");
            ImGui.PopStyleColor();
            if (!menuOpen) return;
            bool vis = FiveMWindowOpen_U12;
            if (ImGui.MenuItem(FiveMConnected_U12 ? "FiveM live link  (game connected)" : "FiveM live link", null, ref vis)) FiveMWindowOpen_U12 = vis;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Mirror what you edit here in a running FiveM server: lights, camera, time and weather,\nand reload a resource after you save into it.");
            bool gv = GameViewOpen_U13;
            if (ImGui.MenuItem("Game view (bottom left)", null, ref gv)) GameViewOpen_U13 = gv;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A small live picture of the FiveM window with debug info.");
            if (ImGui.MenuItem(FiveMDetached_U14 ? "Attach the panel to the main window" : "Detach the panel into its own window"))
            {
                if (FiveMDetached_U14) RequestFiveMAttach_U14 = true;
                else RequestFiveMDetach_U14 = true;
            }
            ImGui.Separator();
            var mlo = MloCreator;
            bool api = mlo != null && mlo.BridgeEnabled;
            if (ImGui.MenuItem(api ? $"Stop the local API  (port {mlo.BridgePort})" : "Start the local API for scripts") && mlo != null) mlo.RequestBridgeToggle = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A plain JSON socket on 127.0.0.1 that scripts and AI tools can drive.\nNot needed for the game link.");
            ImGui.EndMenu();
        }

        private void DrawFiveMWindow_U12(float displayWidth, float displayHeight)
        {
            if (!FiveMWindowOpen_U12 || FiveMDetached_U14) return;
            ImGui.SetNextWindowSize(new Vector2(760, 560), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new Vector2(Math.Max(displayWidth - 790, 20), Math.Max(TopBarHeight + 20, 20)), ImGuiCond.FirstUseEver);
            bool open = FiveMWindowOpen_U12;
            if (!ImGui.Begin("FiveM Live Linking###fivemlink3", ref open))
            {
                FiveMWindowOpen_U12 = open;
                ImGui.End();
                return;
            }
            FiveMWindowOpen_U12 = open;
            DrawFiveMDetachRow_U14();
            DrawFiveMBody_U12();
            ImGui.End();
        }

        private void FiveMToggle_U12(string label, ref bool value, string tip)
        {
            ImGui.Checkbox(label, ref value);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        }
    }
}
