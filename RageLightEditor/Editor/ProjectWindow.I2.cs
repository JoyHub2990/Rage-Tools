using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using CodeWalker.GameFiles;
using CodeWalker.World;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ProjectWindow
    {

        public int SelectSerial { get; private set; }

        partial void OnSelected_I2(object item)
        {
            SelectSerial++;
        }

        private MCEntityDef RevealMloEntityOf(MloArchetype mlo)
        {
            if (mlo == null) return null;
            if (revealItem is MCEntityDef me) return ReferenceEquals(me.OwnerMlo, mlo) ? me : null;
            if (!(revealItem is YmapEntityDef e) || e.MloParent == null) return null;
            var arch = e.MloParent.Archetype as MloArchetype;
            if (arch == null || (!ReferenceEquals(arch, mlo) && arch.Hash != mlo.Hash)) return null;
            var inst = e.MloParent.MloInstance;
            var def = inst?.TryGetArchetypeEntity(e);
            if (def == null && mlo.entities != null && e.Index >= 0 && e.Index < mlo.entities.Length && e.MloEntitySet == null) def = mlo.entities[e.Index];
            return def;
        }
        private MCMloRoomDef RevealRoomOf(MloArchetype mlo) { var me = RevealMloEntityOf(mlo); return me != null ? mlo.GetEntityRoom(me) : null; }
        private MCMloPortalDef RevealPortalOf(MloArchetype mlo) { var me = RevealMloEntityOf(mlo); return me != null ? mlo.GetEntityPortal(me) : null; }
        private MCMloEntitySet RevealSetOf(MloArchetype mlo) { var me = RevealMloEntityOf(mlo); return me != null ? mlo.GetEntitySet(me) : null; }
        private bool RevealIsMloEntity(MCEntityDef me) => me != null && ReferenceEquals(RevealMloEntityOf(me.OwnerMlo), me);

        public static YmapEntityDef LiveEntityByIndex(MloInstanceData inst, MCEntityDef me)
        {
            var mlo = me?.OwnerMlo;
            if (inst == null || mlo == null) return null;
            if (mlo.entities != null && inst.Entities != null)
            {
                int idx = Array.IndexOf(mlo.entities, me);
                if (idx >= 0 && idx < inst.Entities.Length)
                {
                    var live = inst.Entities[idx];
                    if (live != null && live._CEntityDef.archetypeName == me._Data.archetypeName) return live;
                }
            }
            if (mlo.entitySets != null && inst.EntitySets != null)
            {
                for (int s = 0; s < mlo.entitySets.Length && s < inst.EntitySets.Length; s++)
                {
                    var set = mlo.entitySets[s]; var iset = inst.EntitySets[s];
                    if (set?.Entities == null || iset?.Entities == null) continue;
                    int idx = Array.IndexOf(set.Entities, me);
                    if (idx >= 0 && idx < iset.Entities.Count)
                    {
                        var live = iset.Entities[idx];
                        if (live != null && live._CEntityDef.archetypeName == me._Data.archetypeName) return live;
                    }
                }
            }
            return null;
        }

        public YmtFile CurrentScenario => currentItem as YmtFile;
        public string RequestOpenScenario;
        public bool RequestSaveScenario, RequestSaveScenarioAs, RequestRemoveScenario;
        public bool RequestGoToScenario;
        public ScenarioNode ScenarioNodeClicked;
        public bool ScenarioOverlayRequested;
        private string scenarioFilter = "";
        private YmtFile scenarioFilterFor;

        public YmtFile FindScenarioFile(string rel)
        {
            var p = Project;
            if (p == null || string.IsNullOrEmpty(rel)) return null;
            string leaf = Path.GetFileName(rel);
            foreach (var f in p.ScenarioFiles)
            {
                if (f == null) continue;
                if (string.Equals(f.FilePath, rel, StringComparison.OrdinalIgnoreCase)) return f;
                if (string.Equals(p.GetRelativePath(f.FilePath ?? ""), rel, StringComparison.OrdinalIgnoreCase)) return f;
                if (string.Equals(f.Name, rel, StringComparison.OrdinalIgnoreCase)) return f;
                if (string.Equals(Path.GetFileName(f.FilePath ?? f.Name ?? ""), leaf, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        private void DrawScenarioFolder()
        {
            var p = Project;
            var names = p?.ScenarioFilenames;
            if (names == null || names.Count == 0) return;
            const string title = "Scenario Files";
            bool reveal = revealItem is YmtFile;
            if (reveal) ImGui.SetNextItemOpen(true);
            if (!ImGui.TreeNodeEx($"{title} ({names.Count})###{title}", ImGuiTreeNodeFlags.SpanAvailWidth)) return;
            for (int i = 0; i < names.Count; i++)
            {
                var n = names[i];
                var ymt = FindScenarioFile(n);
                var lf = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
                if (ymt != null && ReferenceEquals(currentItem, ymt)) lf |= ImGuiTreeNodeFlags.Selected;
                string label = (ymt != null && ymt.HasChanged ? "*" : "") + Path.GetFileName(n);
                ImGui.TreeNodeEx($"{label}###{title}_{i}", lf);
                if (reveal && ymt != null && ReferenceEquals(revealItem, ymt)) { ImGui.SetScrollHereY(); revealItem = null; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(n + (ymt == null ? "\n(click to open the scenario region)" : $"\n{ymt.ScenarioRegion?.Nodes?.Count ?? 0} points"));
                if (ImGui.IsItemClicked()) OpenScenarioEntry(n, ymt);
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { OpenScenarioEntry(n, ymt); RequestGoToScenario = true; }
                if (ImGui.BeginPopupContextItem($"##filectx_{title}_{i}"))
                {
                    if (ImGui.MenuItem("Open")) OpenScenarioEntry(n, ymt);
                    if (ImGui.MenuItem("Go to", null, false, ymt != null)) { OpenScenarioEntry(n, ymt); RequestGoToScenario = true; }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Save", null, false, ymt != null)) { OpenScenarioEntry(n, ymt); RequestSaveScenario = true; }
                    if (ImGui.MenuItem("Save As...", null, false, ymt != null)) { OpenScenarioEntry(n, ymt); RequestSaveScenarioAs = true; }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Remove from Project"))
                    {
                        if (ymt != null) { Select(ymt); RequestRemoveScenario = true; }
                        else { names.RemoveAt(i); if (p != null) p.HasChanged = true; }
                        ImGui.EndPopup();
                        break;
                    }
                    ImGui.EndPopup();
                }
            }
            ImGui.TreePop();
        }

        private void OpenScenarioEntry(string rel, YmtFile loaded)
        {
            if (loaded != null) { Select(loaded); ScenarioOverlayRequested = true; }
            else RequestOpenScenario = rel;
        }

        public void ShowScenario(YmtFile ymt)
        {
            if (ymt == null) return;
            Select(ymt);
            Reveal(ymt);
            ScenarioOverlayRequested = true;
        }

        private void DrawScenarioPage(YmtFile ymt)
        {
            var sr = ymt.ScenarioRegion;
            var r = sr?.Region;
            ImGui.TextDisabled("SCENARIO REGION  (.ymt)");
            ImGui.Text(ymt.Name ?? "region");
            ImGui.TextDisabled("Path: " + (string.IsNullOrEmpty(Path.GetDirectoryName(ymt.FilePath ?? "")) ? (ymt.RpfFileEntry?.Path ?? ymt.FilePath ?? "(game archive - Save asks where)") : ymt.FilePath));
            if (r == null || sr == null)
            {
                ImGui.TextDisabled("This ymt holds no scenario point region (content type " + ymt.ContentType + ").");
                return;
            }
            int points = r.Points?.MyPoints?.Length ?? 0;
            int loadSave = r.Points?.LoadSavePoints?.Length ?? 0;
            int pathNodes = r.Paths?.Nodes?.Length ?? 0;
            int edges = r.Paths?.Edges?.Length ?? 0;
            int chains = r.Paths?.Chains?.Length ?? 0;
            int clusters = r.Clusters?.Length ?? 0;
            int overrides = r.EntityOverrides?.Length ?? 0;
            ImGui.Text($"{points} point(s){(loadSave > 0 ? $" + {loadSave} load/save" : "")},  {pathNodes} chain node(s),  {edges} edge(s) in {chains} chain(s),  {clusters} cluster(s),  {overrides} entity override(s)");
            if (sr.BVH != null)
            {
                var mn = sr.BVH.Box.Minimum; var mx = sr.BVH.Box.Maximum;
                ImGui.TextDisabled($"Extents: ({mn.X:0.#}, {mn.Y:0.#}, {mn.Z:0.#}) - ({mx.X:0.#}, {mx.Y:0.#}, {mx.Z:0.#})");
            }
            var grid = r._Data.AccelGrid;
            ImGui.TextDisabled($"Version {r.VersionNumber}   accel grid {grid.Dimensions.X} x {grid.Dimensions.Y} cells of {grid.CellDimX:0.#} m");

            ImGui.Spacing();
            if (ImGui.Button("Go to")) RequestGoToScenario = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fly the camera to the region (or to the selected point)");
            ImGui.SameLine();
            if (ImGui.Button("Save")) RequestSaveScenario = true;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Write the region back to its .ymt (YmtFile.Save). A game region asks where.");
            ImGui.SameLine();
            if (ImGui.Button("Save As...")) RequestSaveScenarioAs = true;
            ImGui.SameLine();
            if (LightPanel.DangerButton("Remove from Project", Vector2.Zero)) RequestRemoveScenario = true;

            if (overrides > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled($"ENTITY OVERRIDES ({overrides})");
                ImGui.BeginChild("##scovr", new Vector2(0, Math.Min(22.0f * overrides + 8.0f, 120.0f)), ImGuiChildFlags.Borders);
                for (int i = 0; i < r.EntityOverrides.Length; i++)
                {
                    var o = r.EntityOverrides[i];
                    if (o == null) continue;
                    ImGui.TextUnformatted($"{i}: {o.TypeName}   at ({o.Position.X:0.##}, {o.Position.Y:0.##}, {o.Position.Z:0.##})   {o.ScenarioPoints?.Length ?? 0} point(s)");
                }
                ImGui.EndChild();
            }

            var nodes = sr.Nodes;
            int n = nodes?.Count ?? 0;
            ImGui.Spacing();
            ImGui.TextDisabled($"POINTS ({n})   click one to select it in the world");
            if (scenarioFilterFor != ymt) { scenarioFilter = ""; scenarioFilterFor = ymt; }
            ImGui.SetNextItemWidth(-160);
            ImGui.InputTextWithHint("##scfilter", "filter by type / model set / kind", ref scenarioFilter, 64);
            ImGui.SameLine(); ImGui.TextDisabled("Filter");
            if (n > 0)
            {
                ImGui.BeginChild("##scpoints", new Vector2(0, -1), ImGuiChildFlags.Borders);
                var selNode = WorldScenarioSelection;
                string f = scenarioFilter.Trim();
                for (int i = 0; i < n; i++)
                {
                    var node = nodes[i];
                    if (node == null) continue;
                    var pt = node.MyPoint ?? node.ClusterMyPoint;
                    string type = pt?.Type?.Name ?? node.ChainingNode?.Type?.Name ?? node.EntityPoint?.SpawnType.ToString() ?? node.Entity?.TypeName.ToString() ?? (pt != null ? "type " + pt.TypeId : "");
                    string model = pt?.ModelSet?.Name ?? node.EntityPoint?.PedType.ToString() ?? (pt != null && pt.ModelSetId != 0 ? "modelset " + pt.ModelSetId : "");
                    string kind = node.MedTypeName;
                    if (f.Length > 0 && type.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && model.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                        kind.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var p = node.Position;
                    string label = $"{i}: {kind}   {type}{(model.Length > 0 ? "   " + model : "")}   ({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})##scn{i}";
                    bool sel = ReferenceEquals(selNode, node);
                    if (ImGui.Selectable(label, sel)) ScenarioNodeClicked = node;
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { ScenarioNodeClicked = node; RequestGoToScenario = true; }
                    if (sel && scenarioScrollTo) { ImGui.SetScrollHereY(); scenarioScrollTo = false; }
                }
                ImGui.EndChild();
            }
        }

        public ScenarioNode WorldScenarioSelection;
        private bool scenarioScrollTo;

        public void ShowWorldScenarioSelection(ScenarioNode node)
        {
            if (node?.Ymt == null) return;
            if (!ReferenceEquals(currentItem, node.Ymt)) Select(node.Ymt);
            Reveal(node.Ymt);
            scenarioScrollTo = true;
        }
    }
}

