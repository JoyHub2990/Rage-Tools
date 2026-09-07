using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public struct WorldFindHit_V55
        {
            public string Name;
            public Vector3 Pos;
            public float Dist;
            public string Where;
            public CodeWalker.GameFiles.YmapEntityDef Entity;
            public bool InMlo;
        }

        public string WorldFindText_V55 = "";
        public string WorldFindQuery_V55;
        public readonly List<WorldFindHit_V55> WorldFindHits_V55 = new List<WorldFindHit_V55>();
        public int WorldFindTotal_V55;
        public int RequestWorldFindGoto_V55 = -1;
        public int RequestWorldFindView_V55 = -1;
        public string WorldFindStatus_V55 = "";

        public bool WorldSearchOpen_V56;

        private void DrawWorldFindEntry_V56()
        {
            if (!WorldMode) return;
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("WORLD SEARCH");
            if (ImGui.Button(WorldFindTotal_V55 > 0 ? $"World search ({WorldFindTotal_V55:N0} found)" : "World search",
                             new Vector2(-1, 0)))
                WorldSearchOpen_V56 = !WorldSearchOpen_V56;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Search the loaded world's props by archetype name - ymap entities and\n" +
                                 "the props inside MLO interiors. Click a hit to select it and fly there;\n" +
                                 "View opens its model in the viewer.");
            DrawWorldFindWindow_V56();
        }

        private void DrawWorldFindWindow_V56()
        {
            if (!WorldSearchOpen_V56 || !WorldMode) return;
            ImGui.SetNextWindowSize(new Vector2(440, 540), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new Vector2(360, 120), ImGuiCond.FirstUseEver);
            bool open = WorldSearchOpen_V56;
            if (!ImGui.Begin("World search###worldsearch", ref open))
            {
                WorldSearchOpen_V56 = open;
                ImGui.End();
                return;
            }
            WorldSearchOpen_V56 = open;

            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            if (ImGui.InputTextWithHint("##wfind", "prop name (ymap and MLO props)...",
                                        ref WorldFindText_V55, 96))
                WorldFindQuery_V55 = WorldFindText_V55;

            float footer = ImGui.GetTextLineHeightWithSpacing() + 8;
            if (ImGui.BeginChild("##wfindrows", new Vector2(0, -footer), ImGuiChildFlags.Borders))
            {
                float rowH = ImGui.GetTextLineHeightWithSpacing();
                for (int i = 0; i < WorldFindHits_V55.Count; i++)
                {
                    var hit = WorldFindHits_V55[i];
                    ImGui.PushID(i);
                    float viewW = ImGui.CalcTextSize("View").X + 18;
                    float distW = 64;
                    float selW = Math.Max(80, ImGui.GetContentRegionAvail().X - viewW - 8);
                    if (ImGui.Selectable("##wf", false, ImGuiSelectableFlags.None, new Vector2(selW, rowH)))
                        RequestWorldFindGoto_V55 = i;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{hit.Name}\n{hit.Where}\nat {hit.Pos.X:0.##}, {hit.Pos.Y:0.##}, {hit.Pos.Z:0.##}" +
                                         "\nClick: select it and fly there.");
                    ImGui.SameLine(6);
                    if (hit.InMlo) ImGui.TextColored(UiTheme.Accent, "M");
                    else ImGui.TextDisabled("·");
                    ImGui.SameLine(0, 6);
                    ImGui.TextUnformatted(hit.Name);
                    ImGui.SameLine(Math.Max(20, selW - distW));
                    ImGui.TextDisabled($"{hit.Dist:0} m");
                    ImGui.SameLine(selW + 8);
                    if (ImGui.SmallButton("View")) RequestWorldFindView_V55 = i;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open this prop's model in the model viewer.");
                    ImGui.PopID();
                }
                if (WorldFindHits_V55.Count == 0)
                    ImGui.TextDisabled(string.IsNullOrEmpty(WorldFindText_V55)
                        ? "Type a prop name - prop_palm, bench, v_med..."
                        : "Nothing loaded matches that.");
            }
            ImGui.EndChild();
            ImGui.TextDisabled(string.IsNullOrEmpty(WorldFindStatus_V55) ? " " : WorldFindStatus_V55);
            ImGui.End();
        }
    }
}
