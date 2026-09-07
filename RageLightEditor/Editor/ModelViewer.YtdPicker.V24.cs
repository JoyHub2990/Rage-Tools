using System;
using System.Collections.Generic;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        public class PickItem
        {
            public string Name;
            public bool IsDir;
            public object Node;
            public RpfFileEntry Entry;
            public string DiskPath;
        }

        public Func<object, List<PickItem>> PickerList_V24;
        public Func<object, object> PickerUp_V24;
        public Func<object, string> PickerLabel_V24;

        public RpfFileEntry RequestYtdEntry_V24;
        public string RequestYtdDisk_V24;

        private bool pickerOpen_V24, pickerJustOpened_V24;
        private object pickerNode_V24;
        private string pickerFilter_V24 = "";
        private List<PickItem> pickerItems_V24;
        private object pickerListedFor_V24;

        private object PickerStartNode_V24()
        {
            if (Preview?.Entry == null) return null;
            if (Preview.Entry.Parent != null) return Preview.Entry.Parent;
            var p = DiskPath ?? Preview.Entry.Path;
            if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p)) return System.IO.Path.GetDirectoryName(p);
            return null;
        }

        public void OpenYtdPicker_V24()
        {
            pickerNode_V24 = PickerStartNode_V24();
            pickerItems_V24 = null;
            pickerListedFor_V24 = null;
            pickerFilter_V24 = "";
            pickerOpen_V24 = true;
            pickerJustOpened_V24 = true;
        }

        private void DrawYtdPicker_V24()
        {
            if (!pickerOpen_V24) return;
            if (pickerJustOpened_V24) { ImGui.OpenPopup("Attach a texture dictionary##v24ytd"); pickerJustOpened_V24 = false; }

            float s = UiScale_V17.Scale;
            ImGui.SetNextWindowSize(new Vector2(620 * s, 460 * s), ImGuiCond.Appearing);
            bool open = true;
            if (!ImGui.BeginPopupModal("Attach a texture dictionary##v24ytd", ref open, ImGuiWindowFlags.NoSavedSettings))
            { pickerOpen_V24 = false; return; }
            if (!open) { pickerOpen_V24 = false; ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }

            string label = pickerNode_V24 == null ? "(no folder - the file did not come from anywhere the browser can see)" : (PickerLabel_V24?.Invoke(pickerNode_V24) ?? "");
            var up = pickerNode_V24 != null ? PickerUp_V24?.Invoke(pickerNode_V24) : null;
            if (up == null) ImGui.BeginDisabled();
            if (ImGui.Button("Up")) { pickerNode_V24 = up; pickerItems_V24 = null; }
            if (up == null) ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled(label);
            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(label)) ImGui.SetTooltip(label);

            ImGui.SetNextItemWidth(-140 * s);
            ImGui.InputTextWithHint("##v24pickfilter", "filter by name...", ref pickerFilter_V24, 96);
            ImGui.SameLine();
            if (ImGui.Button("Browse disk...")) { RequestYtdDialog_V22 = true; pickerOpen_V24 = false; ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The OS file dialog, for a .ytd that is not in the archives -\nit opens on this model's own folder.");

            if (pickerNode_V24 != null && !ReferenceEquals(pickerListedFor_V24, pickerNode_V24))
            {
                pickerItems_V24 = PickerList_V24?.Invoke(pickerNode_V24) ?? new List<PickItem>();
                pickerListedFor_V24 = pickerNode_V24;
            }

            if (ImGui.BeginChild("##v24picklist", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() - 4 * s), ImGuiChildFlags.Borders))
            {
                var items = pickerItems_V24 ?? new List<PickItem>();
                int shown = 0;
                foreach (var it in items)
                {
                    if (pickerFilter_V24.Length > 0 && !it.IsDir &&
                        it.Name.IndexOf(pickerFilter_V24, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    shown++;
                    string text = it.IsDir ? "[ " + it.Name + " ]" : it.Name;
                    if (it.IsDir) ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.AccentBright);
                    bool clicked = ImGui.Selectable(text + "##v24pick" + it.Name, false, ImGuiSelectableFlags.AllowDoubleClick);
                    if (it.IsDir) ImGui.PopStyleColor();
                    if (clicked && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        if (it.IsDir) { pickerNode_V24 = it.Node; pickerItems_V24 = null; }
                        else
                        {
                            if (it.Entry != null) RequestYtdEntry_V24 = it.Entry; else RequestYtdDisk_V24 = it.DiskPath;
                            pickerOpen_V24 = false;
                            ImGui.CloseCurrentPopup();
                        }
                    }
                    else if (clicked && !it.IsDir)
                    {
                        if (it.Entry != null) RequestYtdEntry_V24 = it.Entry; else RequestYtdDisk_V24 = it.DiskPath;
                        pickerOpen_V24 = false;
                        ImGui.CloseCurrentPopup();
                    }
                }
                if (shown == 0) ImGui.TextDisabled(pickerNode_V24 == null ? "Use Browse disk..." : "No .ytd here. Double-click a folder, or Up.");
            }
            ImGui.EndChild();
            ImGui.TextDisabled("Click a .ytd to attach it. Double-click a folder or an archive to open it.");
            ImGui.EndPopup();
        }
    }
}

