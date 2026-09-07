using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public bool Detached;
        public bool RequestDetach;
        public bool RequestAttach;
        public bool DetachedFormFocused;

        public Vector2 AttachButtonMin, AttachButtonMax;
        public bool AttachButtonHovered;

        public void DrawDetached(float displayW, float displayH, bool formFocused, bool mloMode, Scene scene, TimecycleData timecycle)
        {
            DetachedFormFocused = formFocused;
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(displayW, displayH), ImGuiCond.Always);
            var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            bool open = ImGui.Begin("###MloCreatorWindowDetached", flags);
            ImGui.PopStyleVar();
            if (open)
            {
                if (mloMode)
                {
                    HandleWindowShortcuts();
                    DrawWindowContents(scene, timecycle);
                }
                else
                {
                    ImGui.TextDisabled(WindowTitle());
                    DrawDetachButton_M1(true, inline: true);
                    var avail = ImGui.GetContentRegionAvail();
                    const string msg = "The MLO Creator belongs to the MLO workspace.";
                    const string msg2 = "Switch the main window to MLO to work in it here.";
                    var ts = ImGui.CalcTextSize(msg);
                    ImGui.SetCursorPos(new Vector2((displayW - ts.X) * 0.5f, ImGui.GetCursorPosY() + avail.Y * 0.5f - ts.Y));
                    ImGui.TextDisabled(msg);
                    var ts2 = ImGui.CalcTextSize(msg2);
                    ImGui.SetCursorPosX((displayW - ts2.X) * 0.5f);
                    ImGui.TextDisabled(msg2);
                }
            }
            ImGui.End();
        }

        private void DrawDetachButton_M1(bool rightAlign, bool inline = false)
        {
            string label = Detached ? "Attach" : "Detach";
            float w = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + 4;
            if (rightAlign && !inline)
            {
                float right = ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - 4;
                ImGui.SameLine(Math.Max(right - w, ImGui.GetCursorPosX() + 8));
            }
            else if (inline) ImGui.SameLine();
            if (ImGui.SmallButton(label + "##mlocdetach"))
            {
                if (Detached) RequestAttach = true; else RequestDetach = true;
            }
            AttachButtonHovered = ImGui.IsItemHovered();
            AttachButtonMin = ImGui.GetItemRectMin();
            AttachButtonMax = ImGui.GetItemRectMax();
            if (AttachButtonHovered)
                ImGui.SetTooltip(Detached ? "Dock back into the main window" : "Detach into its own window (drag it to another monitor)");
        }
    }
}

