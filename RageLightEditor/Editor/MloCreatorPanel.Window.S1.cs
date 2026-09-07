using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public const int LayoutVersion_S1 = 1;

        public int LayoutVersionSeen_S1;
        public bool LayoutVersionPlaced_S1;

        public float RightPanelWidth_S1;

        public void PlaceWindow_S1(float displayW, float displayH)
        {
            float w = Math.Clamp(displayW * 0.36f, 430.0f, 600.0f);
            float h = Math.Max(displayH - 150.0f, 320.0f);
            bool force = LayoutVersionSeen_S1 < LayoutVersion_S1;
            var cond = force ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
            ImGui.SetNextWindowSize(new Vector2(w, h), cond);
            ImGui.SetNextWindowPos(new Vector2(Math.Max(displayW - w - 6.0f, 0.0f), 82.0f), cond);
            ImGui.SetNextWindowSizeConstraints(new Vector2(420, 300), new Vector2(displayW, displayH));
            if (force) LayoutVersionPlaced_S1 = true;
        }

        public void DrawToolbarCompact_S1(Scene scene)
        {
            var s = Session;

            {
                string[] toolNames = { "Select", "Move", "Rotate", "Scale" };
                int ti = Math.Clamp(EntityTool, 0, 3);
                ImGui.TextDisabled("Tool"); ImGui.SameLine();
                ImGui.TextColored(UiTheme.AccentBright, toolNames[ti]); ImGui.SameLine();
                ImGui.TextDisabled("Q W E R/T"); ImGui.SameLine();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Q select, W move, E rotate, T scale - the same keys as the World workspace.");
                ImGui.TextDisabled("|"); ImGui.SameLine();
            }
            DrawEditToolbar_N3();
            if (ImGui.Button("Duplicate")) DuplicateSelected();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A copy of what is selected, beside it  (Ctrl+D)");
            ImGui.SameLine();
            if (LightPanel.DangerButton("Delete", Vector2.Zero)) DeleteSelected();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Delete what is selected  (Del). Ctrl+Z brings it back.");
            ImGui.SameLine();
            var h = History;
            bool cu = h != null && h.CanUndo, cr = h != null && h.CanRedo;
            if (!cu) ImGui.BeginDisabled();
            if (ImGui.Button("Undo")) RequestUndo = true;
            if (ImGui.IsItemHovered() && cu) ImGui.SetTooltip("Undo " + h.UndoName + "  (Ctrl+Z)");
            if (!cu) ImGui.EndDisabled();
            ImGui.SameLine();
            if (!cr) ImGui.BeginDisabled();
            if (ImGui.Button("Redo")) RequestRedo = true;
            if (ImGui.IsItemHovered() && cr) ImGui.SetTooltip("Redo " + h.RedoName + "  (Ctrl+Y)");
            if (!cr) ImGui.EndDisabled();

            DrawNewInteriorButton_T1();
            ImGui.SameLine();
            if (LightPanel.ColourButton_V33("Add room", LightPanel.ColRoom_V33, Vector2.Zero)) RequestAddRoomAtView = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("A room box at the camera's target. Or 'Room from vertices' on the Vertex snap page to click its corners.");
            ImGui.SameLine();
            if (LightPanel.ColourButton_V33("Add portal", LightPanel.ColPortal_V33, Vector2.Zero)) RequestAddPortalAtView = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("A door-sized portal at the view, joining the two rooms either side of it.");
            ImGui.SameLine();
            if (LightPanel.ColourButton_V33("Add entity", LightPanel.ColRoom_V33, Vector2.Zero))
            {
                AddEntityName_V32 = "";
                RequestAddEntityPlace_V32 = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Put a prop in the selected room straight away. Rename it on the Entity page to whatever you actually want.");
            ImGui.SameLine();
            if (ImGui.Button("More...##s1addmore")) ImGui.OpenPopup("##s1add");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rooms, portals, entity sets, props - and building the whole interior out of the shell.");
            if (ImGui.BeginPopup("##s1add"))
            {
                if (ImGui.MenuItem("Room at the view")) RequestAddRoomAtView = true;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("A room box at the camera's target - or 'Room from vertices' on the Vertex snap page to click its corners.");
                if (ImGui.MenuItem("Portal at the view")) RequestAddPortalAtView = true;
                if (ImGui.MenuItem("Entity set")) { s.PushUndo("Add entity set"); s.AddEntitySet(""); SelectSet(s.EntitySets.Count - 1); }
                ImGui.Separator();
                if (ImGui.MenuItem("Prop from a file...")) RequestAddProps = true;
                if (ImGui.MenuItem("Rooms + portals from the shell")) RequestBuildFromShell_R1 = true;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Build the whole interior out of the shell's geometry: a room per enclosed space,\na portal per opening. A first cut you then correct - Ctrl+Z undoes it.");
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            DrawToolbarButtons_L3();
            ImGui.SameLine();
            ImGui.TextDisabled("|"); ImGui.SameLine();

            var problems = s.Validate();
            if (problems.Count > 0) ImGui.BeginDisabled();
            if (LightPanel.AccentButton_V31("Export for the game...", Vector2.Zero)) RequestExportAll_V31 = true;
            if (problems.Count > 0) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(problems.Count > 0 ? "Fix this first: " + problems[0]
                    : "Write the .ytyp, the .ymap that places it AND the _manifest.ymf, into one folder.\n" +
                      "The manifest is what tells the game the .ymap needs the .ytyp - without it the\n" +
                      "interior loads nothing and says nothing. Then it lists what you still have to\n" +
                      "put beside them.");
            ImGui.SameLine();
            ImGui.TextDisabled("|"); ImGui.SameLine();
            if (problems.Count > 0) ImGui.BeginDisabled();
            if (ImGui.Button("Save .ytyp")) RequestSaveYtyp = true;
            if (problems.Count > 0) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(problems.Count > 0 ? "Fix this first: " + problems[0]
                                                    : "Write the interior's .ytyp - its rooms, portals, entities and the archetype itself.");
            ImGui.SameLine();
            if (problems.Count > 0) ImGui.BeginDisabled();
            if (ImGui.Button("Save .ymap")) RequestExportYmap = true;
            if (problems.Count > 0) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(problems.Count > 0 ? "Fix this first: " + problems[0]
                                                    : "Write the .ymap that PLACES one instance of the interior in the world.");
            ImGui.SameLine();
            if (ImGui.Button("More...##s1projmore")) ImGui.OpenPopup("##s1proj");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Put the interior in the World project, or save / open a .mloproj.");
            if (ImGui.BeginPopup("##s1proj"))
            {
                if (ImGui.MenuItem("Add to the World project")) RequestAddToProject = true;
                ImGui.Separator();
                if (ImGui.MenuItem("Save project (.mloproj)")) RequestSaveProject = true;
                if (ImGui.MenuItem("Open project (.mloproj)...")) RequestOpenProject = true;
                ImGui.EndPopup();
            }
            DrawRemoveYtypButton_S1(scene);
            if (problems.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(UiTheme.Warn, "! " + problems[0]);
            }
        }

        public void DrawRemoveYtypButton_S1(Scene scene)
        {
            var imports = scene?.Imports_R1?.Entries;
            if (imports == null || imports.Count == 0) return;
            ImGui.SameLine();
            ImGui.TextDisabled("|"); ImGui.SameLine();
            string label = imports.Count == 1 ? $"Delete .ytyp ({imports[0].Name})" : $"Delete .ytyp ({imports.Count})";
            if (LightPanel.DangerButton(label, Vector2.Zero)) ImGui.OpenPopup("##s1rmytyp");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Take an imported .ytyp back out of this interior: its archetypes, its entities and\n" +
                                 "the props only it brought. Props another import also places stay. Ctrl+Z does not\n" +
                                 "undo this - re-import the file instead.");
            if (ImGui.BeginPopup("##s1rmytyp"))
            {
                ImGui.TextDisabled("REMOVE AN IMPORTED .YTYP");
                foreach (var e in imports)
                {
                    if (e == null) continue;
                    if (ImGui.MenuItem($"{e.Name}   ({e.MeshCount} meshes, {e.Props.Count} props)"))
                        RequestRemoveImportedYtyp_R1 = e.Path;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(e.Path);
                }
                ImGui.EndPopup();
            }
        }

        public void DrawRemoveYtypBlock_S1(Scene scene)
        {
            var imports = scene?.Imports_R1?.Entries;
            if (imports == null || imports.Count == 0) return;
            ImGui.Spacing();
            ImGui.TextDisabled("IMPORTED .YTYPS");
            ImGui.SameLine();
            ImGui.TextDisabled($"({imports.Count})");
            for (int i = 0; i < imports.Count; i++)
            {
                var e = imports[i];
                if (e == null) continue;
                ImGui.PushID("s1rm" + i);
                if (LightPanel.DangerButton($"Delete {e.Name}", new Vector2(-1, 0))) RequestRemoveImportedYtyp_R1 = e.Path;
                if (ImGui.IsItemHovered())
                {
                    var archs = string.Join(", ", e.ArchetypeNames.Take(6));
                    ImGui.SetTooltip(e.Path + "\n" + $"{e.MeshCount} meshes, {e.Props.Count} props, {e.EntityCount} placed" +
                                     (string.IsNullOrEmpty(archs) ? "" : "\narchetypes: " + archs) +
                                     "\n\nRemoves this file's archetypes, entities and the props only it brought.");
                }
                ImGui.PopID();
            }
        }
    }
}

