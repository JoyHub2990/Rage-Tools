using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        private void DrawAddEntityPopup_V32()
        {
            if (!ImGui.BeginPopup("##v32addent")) return;

            ImGui.TextDisabled("ADD AN ENTITY BY NAME");
            ImGui.Separator();

            ImGui.SetNextItemWidth(280.0f * UiScale_V17.Scale);
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            bool entered = ImGui.InputTextWithHint("##v32entname", "prop_bench_01a", ref AddEntityName_V32, 128,
                                                   ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.TextDisabled("The archetype's name, as the game knows it. Enter places it.");

            bool has = !string.IsNullOrWhiteSpace(AddEntityName_V32);
            if (!has) ImGui.BeginDisabled();
            bool place = ImGui.Button("Place at the view", new Vector2(180.0f * UiScale_V17.Scale, 0));
            if (!has) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel##v32ent")) ImGui.CloseCurrentPopup();

            if ((entered || place) && has)
            {
                RequestAddEntityPlace_V32 = true;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}

