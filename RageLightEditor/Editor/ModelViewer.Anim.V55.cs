using System;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        public string[] AnimDictNames_V55 = Array.Empty<string>();
        public string[] AnimClipNames_V55 = Array.Empty<string>();
        public int AnimDictSel_V55 = -1;
        public int AnimClipSel_V55 = -1;
        public bool AnimRootMotion_V55;
        public bool AnimSupported_V55;
        public string AnimInfo_V55 = "";
        public int RequestAnimDict_V55 = -2;
        public int RequestAnimClip_V55 = -2;

        public void ResetAnim_V55(bool supported, string[] dicts)
        {
            AnimSupported_V55 = supported;
            AnimDictNames_V55 = dicts ?? Array.Empty<string>();
            AnimClipNames_V55 = Array.Empty<string>();
            AnimDictSel_V55 = -1;
            AnimClipSel_V55 = -1;
            RequestAnimDict_V55 = -2;
            RequestAnimClip_V55 = -2;
            AnimInfo_V55 = !supported ? ""
                : AnimDictNames_V55.Length == 0 ? "no matching .ycd in the archives for this model"
                : $"{AnimDictNames_V55.Length} clip dictionar{(AnimDictNames_V55.Length == 1 ? "y" : "ies")} match this model's name";
        }

        private void DrawClipRow_V55()
        {
            bool geo = Preview != null && Preview.Kind != AssetKind.TextureDict;
            ImGui.BeginDisabled(!geo || !AnimSupported_V55 || AnimDictNames_V55.Length == 0);

            ImGui.SetNextItemWidth(230);
            string dictShown = AnimDictSel_V55 >= 0 && AnimDictSel_V55 < AnimDictNames_V55.Length
                ? AnimDictNames_V55[AnimDictSel_V55] : "(none)";
            if (ImGui.BeginCombo("Clip Dict", dictShown))
            {
                if (ImGui.Selectable("(none)", AnimDictSel_V55 < 0)) RequestAnimDict_V55 = -1;
                for (int i = 0; i < AnimDictNames_V55.Length; i++)
                    if (ImGui.Selectable(AnimDictNames_V55[i] + "##ad" + i, i == AnimDictSel_V55))
                        RequestAnimDict_V55 = i;
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.BeginDisabled(AnimClipNames_V55.Length == 0);
            ImGui.SetNextItemWidth(230);
            string clipShown = AnimClipSel_V55 >= 0 && AnimClipSel_V55 < AnimClipNames_V55.Length
                ? AnimClipNames_V55[AnimClipSel_V55] : "(none)";
            if (ImGui.BeginCombo("Clip", clipShown))
            {
                if (ImGui.Selectable("(none)", AnimClipSel_V55 < 0)) RequestAnimClip_V55 = -1;
                for (int i = 0; i < AnimClipNames_V55.Length; i++)
                    if (ImGui.Selectable(AnimClipNames_V55[i] + "##ac" + i, i == AnimClipSel_V55))
                        RequestAnimClip_V55 = i;
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.Checkbox("Root motion", ref AnimRootMotion_V55);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("On, the whole model travels with the clip's root translation.\nOff, it animates in place.");
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (!geo) ImGui.TextDisabled("");
            else if (!AnimSupported_V55) ImGui.TextDisabled("this model has no skeleton to animate");
            else ImGui.TextDisabled(AnimInfo_V55 ?? "");
        }
    }
}
