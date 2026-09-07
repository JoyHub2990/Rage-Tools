using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ProjectWindow
    {
        public bool Detached;
        public bool RequestDetach;
        public bool RequestAttach;

        public void DrawDetached(float displayW, float displayH, bool formFocused, bool worldMode)
        {
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(displayW, displayH), ImGuiCond.Always);
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                        ImGuiWindowFlags.MenuBar;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            bool open = ImGui.Begin("###ProjectWindowDetached", flags);
            ImGui.PopStyleVar();
            if (open)
            {
                if (worldMode)
                {
                    DrawContents();
                }
                else
                {
                    if (ImGui.BeginMenuBar())
                    {
                        ImGui.TextDisabled(Project == null ? "Project" : (Project.AnyUnsaved ? "*" : "") + Project.Name);
                        DrawDetachedWindowButtons();
                        ImGui.EndMenuBar();
                    }
                    var avail = ImGui.GetContentRegionAvail();
                    const string msg = "The Project window belongs to the World workspace.";
                    const string msg2 = "Switch the main window to World to work in it here.";
                    var ts = ImGui.CalcTextSize(msg);
                    ImGui.SetCursorPos(new Vector2((displayW - ts.X) * 0.5f, ImGui.GetCursorPosY() + avail.Y * 0.5f - ts.Y));
                    ImGui.TextDisabled(msg);
                    var ts2 = ImGui.CalcTextSize(msg2);
                    ImGui.SetCursorPosX((displayW - ts2.X) * 0.5f);
                    ImGui.TextDisabled(msg2);
                }
            }
            ImGui.End();
            Focused = formFocused;
        }

        private void DrawDetachedWindowButtons()
        {
            float right = ImGui.GetWindowWidth() - 8;
            var label = "Attach";
            float w = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + 4;
            ImGui.SameLine(Math.Max(right - w, ImGui.GetCursorPosX() + 8));
            if (ImGui.SmallButton(label + "##pwattach")) RequestAttach = true;
            AttachButtonHovered = ImGui.IsItemHovered();
            AttachButtonMin = ImGui.GetItemRectMin();
            AttachButtonMax = ImGui.GetItemRectMax();
            if (AttachButtonHovered) ImGui.SetTooltip("Dock back into the main window");
        }

        public Vector2 AttachButtonMin, AttachButtonMax;
        public bool AttachButtonHovered;
    }
}

