using System;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public enum MloLabels_V19
        {
            Selected = 0,
            All = 1,
            Off = 2,
        }

        public static readonly string[] MloLabelNames_V19 = { "Selected only", "Everything", "Off" };

        public MloLabels_V19 LabelMode_V19 = MloLabels_V19.Selected;

        public bool LabelFor_V19(bool isSelected) => LabelMode_V19 switch
        {
            MloLabels_V19.Off => false,
            MloLabels_V19.All => true,
            _ => isSelected,
        };

        public bool ShowLabels
        {
            get => LabelMode_V19 == MloLabels_V19.All;
            set => LabelMode_V19 = value ? MloLabels_V19.All : MloLabels_V19.Selected;
        }

        public void DrawLabelModeControl_V19()
        {
            int m = (int)LabelMode_V19;
            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.Combo("##v19labels", ref m, MloLabelNames_V19, MloLabelNames_V19.Length))
                LabelMode_V19 = (MloLabels_V19)Math.Clamp(m, 0, MloLabelNames_V19.Length - 1);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Which names are drawn in the viewport.\n\n" +
                                 "Selected only - just the room, portal or entity you have picked.\n" +
                                 "Everything - every one of them, which on a big interior is a wall of text.\n" +
                                 "Off - none, for a clean look at the geometry.");
        }
    }
}

