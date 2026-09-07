using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public readonly TextEditView_R3 RpfText_R3 = new TextEditView_R3();

        partial void NewRpfText_R3(string title, string text);

        partial void RpfTextWindow_R3(float displayWidth, float displayHeight, ref bool handled);

        partial void NewRpfText_R3(string title, string text)
        {
            RpfText_R3.Load(title, text, false, "");
        }

        partial void RpfTextWindow_R3(float displayWidth, float displayHeight, ref bool handled)
        {
            handled = true;
            if (!RpfViewOpen) return;

            var view = RpfText_R3;

            var src = RpfViewFrom_Q1;
            if (src == null)
            {
                view.CanSave = false;
                view.CannotSaveWhy = "This text was not opened from a file - there is nothing to write it\n" +
                                     "back to. Save as... writes it wherever you like.";
            }
            else if (src.DiskPath != null)
            {
                view.CanSave = true;
                view.CannotSaveWhy = "";
            }
            else if (src.Entry != null)
            {
                view.CanSave = RpfEditMode;
                view.CannotSaveWhy = RpfEditMode
                    ? ""
                    : "Writing back into a game archive needs Edit mode, which is the switch in the\n" +
                      "right panel. It is off by default because it edits your installed game.";
            }
            else { view.CanSave = false; view.CannotSaveWhy = "There is nowhere to write this back to."; }

            ImGui.SetNextWindowSize(new Vector2(Math.Min(1100, displayWidth * 0.7f),
                                                Math.Min(720, displayHeight * 0.78f)), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new Vector2(displayWidth * 0.2f, displayHeight * 0.14f), ImGuiCond.FirstUseEver);
            bool open = RpfViewOpen;
            var flags = view.Dirty ? ImGuiWindowFlags.UnsavedDocument : ImGuiWindowFlags.None;
            if (!ImGui.Begin((RpfViewTitle ?? "file") + "##rpftext", ref open, flags))
            {
                RpfViewOpen = open;
                ImGui.End();
                return;
            }
            RpfViewOpen = open;

            view.DrawToolbar();

            ImGui.TextDisabled(src?.Name ?? "unsaved text");
            DrawRpfViewActions_Q1();

            ImGui.Separator();
            view.DrawBody(Math.Max(ImGui.GetContentRegionAvail().Y, 80.0f));
            ImGui.End();
        }
    }
}

