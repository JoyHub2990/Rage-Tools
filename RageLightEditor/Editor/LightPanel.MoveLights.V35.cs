using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private bool moveKeepWorld_V35 = true;
        private string movePropFilter_V35 = "";

        private void DrawMoveLightsButton_V35()
        {
            int n = scene.SelectedIndices.Count;
            if (n == 0) return;

            var others = scene.Files.Where(f => f != null).ToList();
            if (others.Count < 2) return;

            if (ImGui.Button(n == 1 ? "Move light to..." : $"Move {n} lights to...", new Vector2(-1, 0)))
            {
                movePropFilter_V35 = "";
                ImGui.OpenPopup("##v35movelights");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Give the selected light(s) to another prop. Every setting is kept -\n" +
                                 "only which .ydr they are saved into changes.");

            if (!ImGui.BeginPopup("##v35movelights")) return;

            ImGui.TextDisabled(n == 1 ? "MOVE THIS LIGHT ONTO" : $"MOVE {n} LIGHTS ONTO");
            ImGui.Separator();

            if (ImGui.Checkbox("Keep them where they are in the world##v35mk", ref moveKeepWorld_V35)) { }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("On: the light keeps lighting the same spot - only the file it is saved into changes.\n" +
                                 "Off: it keeps its offset, so it lands in the same place ON the new prop -\n" +
                                 "which is what you want when copying a lamp's lighting onto an identical lamp.");

            ImGui.SetNextItemWidth(260.0f * UiScale_V17.Scale);
            ImGui.InputTextWithHint("##v35mf", "search props...", ref movePropFilter_V35, 64);

            var sel = scene.SelectedLightList_V35();
            var current = sel.Select(scene.OwnerFile).Distinct().ToList();
            if (current.Count == 1 && current[0] != null)
                ImGui.TextDisabled("now on: " + current[0].Name);
            else if (current.Count > 1)
                ImGui.TextDisabled($"now on {current.Count} different props");

            if (ImGui.BeginChild("##v35mlist", new Vector2(300.0f * UiScale_V17.Scale, 200.0f * UiScale_V17.Scale),
                                 ImGuiChildFlags.Borders))
            {
                foreach (var f in others)
                {
                    if (movePropFilter_V35.Length > 0 &&
                        (f.Name ?? "").IndexOf(movePropFilter_V35, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    bool isCurrent = current.Count == 1 && ReferenceEquals(current[0], f);
                    int has = scene.LightsOf_V35(f).Count;
                    if (isCurrent) ImGui.BeginDisabled();
                    if (ImGui.Selectable($"{f.Name}   ({has} light(s))##v35m{f.Path}"))
                    {
                        int moved = scene.MoveLightsToProp_V35(sel, f, moveKeepWorld_V35);
                        MloStatus = moved > 0
                            ? $"Moved {moved} light(s) onto {f.Name}" +
                              (moveKeepWorld_V35 ? " - they stayed where they were." : " - they kept their offset on the prop.")
                            : "Nothing moved - they are already on that prop.";
                        ImGui.CloseCurrentPopup();
                    }
                    if (isCurrent) ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(isCurrent ? "They are already on this prop." : f.Path);
                }
            }
            ImGui.EndChild();

            ImGui.EndPopup();
        }
    }
}

