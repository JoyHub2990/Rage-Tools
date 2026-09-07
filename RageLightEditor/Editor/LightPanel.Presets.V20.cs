using System;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private string presetName_V20 = "";
        private bool presetsLoaded_V20;

        public string RequestApplyPreset_V20;
        public string RequestSavePreset_V20;

        private static string PresetKind_V21(LightPresets_V20.Preset_V20 p) =>
            p.Type == 2 ? $"spot {p.ConeInnerAngle:0.#}/{p.ConeOuterAngle:0.#} deg" : p.Type == 4 ? $"capsule {p.ExtentX:0.##} m long" : "point";

        public void DrawLightPresets_V20(LightAttributes l)
        {
            if (!presetsLoaded_V20) { presetsLoaded_V20 = true; LightPresets_V20.Load_V20(); }
            if (!Header("Presets")) return;

            if (l == null)
            {
                ImGui.TextDisabled("Select a light to use a preset.");
                return;
            }

            ImGui.TextDisabled("One click sets colour, intensity, falloff and the rest.");
            ImGui.TextDisabled("Never where the light is or which way it points.");
            ImGui.TextDisabled("Built-in presets stay; the ones you save get an X.");
            ImGui.Spacing();

            var all = LightPresets_V20.All;
            if (all.Count == 0) ImGui.TextDisabled("(no presets yet)");

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;
                ImGui.PushID("v20lp" + i);

                var col = new Vector4(p.ColorR / 255.0f, p.ColorG / 255.0f, p.ColorB / 255.0f, 1.0f);
                ImGui.ColorButton("##sw", col, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker,
                                  new Vector2(16, 16));
                ImGui.SameLine();

                if (ImGui.Selectable(p.Name, false)) RequestApplyPreset_V20 = p.Name;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip((string.IsNullOrEmpty(p.Note) ? p.Name : p.Note) + "\n\n" +
                                     $"{PresetKind_V21(p)}, rgb {p.ColorR}, {p.ColorG}, {p.ColorB}\n" +
                                     $"intensity {p.Intensity:0.##}, falloff {p.Falloff:0.##} m" + (p.Flashiness != 0 ? $", flashiness {p.Flashiness}" : "") + "\n\n" +
                                     "Click to put it on the selected light.");

                ImGui.SameLine();
                if (p.Builtin) ImGui.TextDisabled("built-in");
                else
                {
                    if (DangerButton("X", Vector2.Zero)) { LightPresets_V20.Remove_V20(p.Name); ImGui.PopID(); break; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete this preset.");
                }
                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.SetNextItemWidth(-70);
            ImGui.InputTextWithHint("##v20pname", "name this light's settings...", ref presetName_V20, 48);
            ImGui.SameLine();
            if (ImGui.Button("Save##v20p") && !string.IsNullOrWhiteSpace(presetName_V20))
            {
                RequestSavePreset_V20 = presetName_V20.Trim();
                presetName_V20 = "";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save the SELECTED light's settings as a preset under that name.\n" +
                                 "An existing preset of the same name is replaced.");
        }
    }
}

