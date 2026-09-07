using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public bool AssetViewChosen_P2;

        public Vector2 WindowSizeOverride_P2;

        public static int AssetViewFor_P2(AppSettings s) =>
            s != null && s.MloAssetViewChosen_P2 ? s.MloAssetView : 1;

        public void DrawAssetCategoryRow_P2(MloAssetLibrary lib, bool compact)
        {
            if (lib == null) return;
            int before = lib.Category_P2;
            ImGui.TextDisabled("Category:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(compact ? -1 : 260);
            string cur = Shelf_P2(lib, lib.Category_P2);
            if (ImGui.BeginCombo("##p2assetcat", cur))
            {
                for (int i = 0; i < MloAssetCategories_P2.Count; i++)
                    if (ImGui.Selectable(Shelf_P2(lib, i), lib.Category_P2 == i)) lib.Category_P2 = i;
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Narrow the library to one kind of prop. The shelf is guessed from the model's\n" +
                                 "name and the archive it lives in - there is no category in the game files - so a\n" +
                                 "model it cannot place lands in Misc. The search box narrows within the shelf.");

            if (!compact)
            {
                ImGui.SameLine();
                Chip_P2(lib, 0, "All");
                Chip_P2(lib, 1, "Lights");
                Chip_P2(lib, 5, "Seating");
                Chip_P2(lib, 4, "Tables");
                Chip_P2(lib, 6, "Storage");
                Chip_P2(lib, 2, "Electronics");
            }
            if (lib.Category_P2 != before)
            {
                lib.ResetPaging_P2();
                lib.Dirty = true;
            }
        }

        private static string Shelf_P2(MloAssetLibrary lib, int i)
        {
            string n = MloAssetCategories_P2.Names[i];
            if (!lib.CategoryCountsKnown_P2) return n;
            return $"{n}  ({lib.CategoryCounts_P2[i]:N0})";
        }

        private void Chip_P2(MloAssetLibrary lib, int cat, string label)
        {
            bool on = lib.Category_P2 == cat;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            if (ImGui.SmallButton(label + "##p2chip" + cat)) { lib.Category_P2 = cat; }
            if (on) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered() && lib.CategoryCountsKnown_P2)
                ImGui.SetTooltip($"{MloAssetCategories_P2.Names[cat]}: {lib.CategoryCounts_P2[cat]:N0} models");
            ImGui.SameLine();
        }

        public static void RequestMoreIfNearEnd_P2(MloAssetLibrary lib)
        {
            if (lib == null || lib.AllLoaded_P2) return;
            float y = ImGui.GetScrollY(), max = ImGui.GetScrollMaxY();
            if (max <= 0 || y >= max - ImGui.GetWindowHeight()) lib.RequestMore_P2 = true;
        }

        public static string ResultsNote_P2(MloAssetLibrary lib)
        {
            if (lib == null) return "";
            if (lib.AllLoaded_P2 || lib.Results.Count >= lib.ResultTotal)
                return $"{lib.Results.Count:N0} match{(lib.Results.Count == 1 ? "" : "es")}";
            return $"{lib.Results.Count:N0} of {lib.ResultTotal:N0} - scroll for more";
        }
    }
}

