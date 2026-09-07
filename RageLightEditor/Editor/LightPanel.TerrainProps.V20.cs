using System;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public void DrawTerrainProps_V20()
        {
            var te = Terrain;
            if (te == null || te.Props_V20.Count == 0) return;

            ImGui.Spacing();
            int dirty = te.DirtyPropCount_V20;
            ImGui.TextDisabled($"PROPS  ({te.Props_V20.Count})" + (dirty > 0 ? $"   {dirty} unsaved" : ""));

            for (int i = 0; i < te.Props_V20.Count; i++)
            {
                var p = te.Props_V20[i];
                ImGui.PushID("v20p" + i);

                bool vis = p.Visible;
                if (ImGui.Checkbox("##vis", ref vis))
                {
                    p.Visible = vis;
                    te.RequestRebuildVisible_V20 = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(vis ? "Hide it - the brush stops hitting it too."
                                         : "Show it again.");
                ImGui.SameLine();

                bool active = te.ActiveProp_V20 == i;
                int count = 0;
                foreach (var _ in te.PartsOf_V20(i)) count++;
                var label = p.Name + (p.Dirty ? " *" : "") + $"   ({count})";
                if (ImGui.Selectable(label, active)) te.ActiveProp_V20 = i;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip((string.IsNullOrEmpty(p.SourcePath) ? "(built here)" : p.SourcePath) +
                                     (p.Dirty ? "\n\nPainted since it was last written." : "") +
                                     (string.IsNullOrEmpty(p.LastExport) ? "" : "\n\nLast written to:\n" + p.LastExport));

                ImGui.SameLine();
                if (ImGui.SmallButton("Save##one")) te.RequestExportProp_V20 = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Write this prop on its own to a .ydr.");
                ImGui.SameLine();
                if (DangerButton("X", Vector2.Zero)) te.RequestRemoveProp_V20 = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Take this prop out of the painter. The file on disk is untouched.");

                ImGui.PopID();
            }

            ImGui.Spacing();
            if (ImGui.Button("Save every prop...", new Vector2(-1, 0))) te.RequestExportAll_V20 = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("One .ydr per prop, into a folder you pick once.\n" +
                                 "Each keeps its own name.");
        }

        public void DrawTerrainAddButton_V20()
        {
            var te = Terrain;
            if (te == null || !te.HasMesh) return;
            if (ImGui.Button("Add another mesh...", new Vector2(-1, 0)))
            {
                te.RequestImportAdd_V20 = true;
                te.RequestImportFile = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Bring in another prop BESIDE what is loaded, instead of replacing it.\n\n" +
                                 "For matching the blend where two pieces meet - a road and its verge -\n" +
                                 "which is the thing you cannot do one file at a time.");
        }
    }
}

