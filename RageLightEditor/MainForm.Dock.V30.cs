using System;
using System.IO;
using System.Linq;
using ImGuiNET;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static string ImGuiIniPath_V30()
        {
            try
            {
                var io = ImGui.GetIO();
                var p = io.IniFilename;
                return string.IsNullOrWhiteSpace(p) ? null : p;
            }
            catch { return null; }
        }

        private void ResetDockLayout_V30()
        {
            var path = ImGuiIniPath_V30();
            try
            {
                if (path != null && File.Exists(path)) File.Delete(path);
                ImGui.LoadIniSettingsFromMemory("");
                panel.MloStatus = "Panels put back where they started.";
                Console.WriteLine("DOCKRESET layout cleared" + (path != null ? " (" + path + ")" : ""));
            }
            catch (Exception ex)
            {
                panel.MloStatus = "Could not reset the layout: " + ex.Message;
                Console.WriteLine("DOCKRESET failed: " + ex.Message);
            }
        }

        private void SeqTest_Dock_V30(Action<string, bool, string> check)
        {
            bool was = settings.DockedLayout;
            try
            {
                settings.DockedLayout = false;
                bool open = true;
                var cf = panel.PanelWindow_V30(0, 24, 250, 800, 180, 700, false, ref open);
                check("v30 layout: classic pins the panels, exactly as it always has",
                      (cf & ImGuiWindowFlags.NoMove) != 0 && (cf & ImGuiWindowFlags.NoTitleBar) != 0 &&
                      !panel.DockedLayout,
                      "NoMove | NoTitleBar | NoCollapse");

                settings.DockedLayout = true;
                var df = panel.PanelWindow_V30(0, 24, 250, 800, 180, 700, false, ref open);
                check("v30 layout: docked gives every panel a title bar and lets it be dragged",
                      (df & ImGuiWindowFlags.NoMove) == 0 && (df & ImGuiWindowFlags.NoTitleBar) == 0 &&
                      panel.DockedLayout,
                      "NoCollapse only");

                panel.ApplyDockConfig_V30();
                bool onNow = (ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0;
                settings.DockedLayout = false;
                panel.ApplyDockConfig_V30();
                bool offAgain = (ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0;
                check("v30 layout: docking turns on and off in the live ImGui io, so no restart is needed",
                      onNow && offAgain, $"on {onNow}, off again {offAgain}");

                settings.DockedLayout = true;
                var spaces = Enum.GetValues(typeof(LightPanel.Space)).Cast<LightPanel.Space>().ToList();
                var names = new System.Collections.Generic.HashSet<string>();
                var wasSpace = panel.Workspace;
                foreach (var sp in spaces) { panel.Workspace = sp; names.Add(panel.WorkspaceName_V30); }
                panel.Workspace = wasSpace;
                check("v30 layout: every workspace names its panels, so the title bar says where you are",
                      names.Count == spaces.Count && !names.Contains(""), $"{names.Count} distinct name(s) for {spaces.Count} workspace(s)");
            }
            finally { settings.DockedLayout = was; panel.ApplyDockConfig_V30(); }
        }
    }
}

