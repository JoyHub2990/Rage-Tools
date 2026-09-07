using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public byte RequestWorldAddLight_U18;

        private const string AddLightTip_U18 =
            "A new light on the prop's model - in its drawable or, for a .yft, its fragment.\n" +
            "Every copy of the prop gets it. Save as... or Add to project keeps it.";

        public void DrawWorldLightAddRow_U18(string propName)
        {
            ImGui.TextDisabled("ADD A LIGHT TO " + (string.IsNullOrEmpty(propName) ? "THIS PROP" : propName.ToUpperInvariant()));
            if (ImGui.Button("+ Point##wl18")) RequestWorldAddLight_U18 = 1;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(AddLightTip_U18);
            ImGui.SameLine();
            if (ImGui.Button("+ Spot##wl18")) RequestWorldAddLight_U18 = 2;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(AddLightTip_U18);
            ImGui.SameLine();
            if (ImGui.Button("+ Capsule##wl18")) RequestWorldAddLight_U18 = 4;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(AddLightTip_U18);
        }

        public void DrawWorldEntityAddLight_U18(YmapEntityDef e)
        {
            if (!EditLightActive || e?.Archetype == null || e.MloInstance != null) return;
            ImGui.Spacing();
            ImGui.Separator();
            DrawWorldLightAddRow_U18(e.Archetype.Name);
        }

        public void DrawWorldLightPresets_U18(LightAttributes l)
        {
            if (l == null) return;
            ImGui.Spacing();
            DrawLightPresets_V20(l);
        }
    }
}
