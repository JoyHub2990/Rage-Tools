using System;
using System.Linq;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private Scene mloScene;
        public Scene MloScene
        {
            get => mloScene;
            set
            {
                mloScene = value;
                MloCreator.LightsPageDrawer = () => DrawMloLightsPage(false);
                MloCreator.Assets.ThumbnailOf = e => Thumbs?.Get(e) ?? IntPtr.Zero;
                MloCreator.Assets.BeginDrag = e => DraggedLibraryProp = e;
            }
        }
        public Scene ActiveScene => scene;

        public void DrawMloLightsPage(bool compact = false)
        {
            var ui = MloCreator;
            if (ui.RequestSelectPropLights != null)
            {
                var f = ui.RequestSelectPropLights; ui.RequestSelectPropLights = null;
                if (scene.Files.Contains(f))
                {
                    scene.SelectFile(f, false, false);
                    ScrollToActiveProp = true;
                    int first = scene.Lights.FindIndex(l => scene.OwnerFile(l) == f);
                    scene.SelectedIndex = first;
                }
            }
            ImGui.TextDisabled("LIGHTS");
            ImGui.SameLine();
            ImGui.Text($"({scene.Lights.Count})");
            if (scene.SelectedIndices.Count > 1) { ImGui.SameLine(); ImGui.TextColored(AccentText(), $"{scene.SelectedIndices.Count} selected"); }
            if (!compact) { ImGui.SameLine(); ImGui.TextDisabled("- the props of this interior; the Lights workspace's scene is untouched"); }
            if (!scene.HasModel)
            {
                ImGui.TextWrapped("Open a shell, add props or place assets from the library first - a light belongs to a prop.");
                return;
            }

            if (!WalkMode)
            {
                DrawModeButton("Select (Q)", GizmoMode.Select);
                ImGui.SameLine();
                DrawModeButton("Move (W)", GizmoMode.Translate);
                ImGui.SameLine();
                DrawModeButton("Rotate (E)", GizmoMode.Rotate);
                if (!compact) { ImGui.SameLine(); ImGui.TextDisabled("Shift+drag: copy   Alt+drag: instance   click a light in the viewport to pick it"); }
            }

            if (ImGui.Button("+ Point")) scene.AddLight(1);
            ImGui.SameLine();
            if (ImGui.Button("+ Spot")) scene.AddLight(2);
            ImGui.SameLine();
            if (ImGui.Button("+ Capsule")) scene.AddLight(4);
            if (!compact) { ImGui.SameLine(); ImGui.TextDisabled("|"); ImGui.SameLine(); }
            bool selAny = scene.SelectedIndices.Count > 0;
            if (!selAny) ImGui.BeginDisabled();
            if (ImGui.Button("Duplicate")) scene.DuplicateSelected(false);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Independent copies");
            ImGui.SameLine();
            if (ImGui.Button("Instance")) scene.DuplicateSelected(true);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Linked duplicates: parameters stay in sync (position / rotation stay independent).");
            ImGui.SameLine();
            if (DangerButton("Delete", Vector2.Zero)) AskDeleteSelected();
            if (!compact) ImGui.SameLine();
            if (ImGui.Button("Copy")) scene.CopySettings();
            if (!selAny) ImGui.EndDisabled();
            ImGui.SameLine();
            bool canPaste = selAny && scene.HasClipboard;
            if (!canPaste) ImGui.BeginDisabled();
            if (ImGui.Button("Paste")) scene.PasteSettings();
            if (!canPaste) ImGui.EndDisabled();

            var target = scene.ActiveFile ?? scene.OwnerFile(scene.SelectedLight) ?? (scene.Files.Count == 1 ? scene.Files[0] : null);
            ImGui.TextDisabled("Prop:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(compact ? -1 : 220);
            if (ImGui.BeginCombo("##mlolightprop", target != null ? target.Name : "(pick a prop)"))
            {
                foreach (var f in scene.Files)
                {
                    int n = scene.Lights.Count(l => scene.OwnerFile(l) == f);
                    if (ImGui.Selectable($"{f.Name}  ({n} light{(n == 1 ? "" : "s")}){(f.ReadOnly ? "  [game]" : "")}##mlp{f.GetHashCode()}", f == target))
                    {
                        scene.SelectFile(f, false, false);
                        ScrollToActiveProp = true;
                    }
                }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("New lights are added to this prop; Save / Save As write it. Click a prop in the viewport or the Props list to change it.");
            if (!compact) ImGui.SameLine();
            if (target == null) ImGui.BeginDisabled();
            if (ImGui.Button("Save As...##mlolsa")) RequestSaveAs?.Invoke(target);
            if (ImGui.IsItemHovered() && target != null) ImGui.SetTooltip($"Write {target.Name} with its lights to a .ydr / .yft of your own" + (target.ReadOnly ? " (a game prop: Save As is the only way out)." : "."));
            ImGui.SameLine();
            if (target != null && target.ReadOnly) ImGui.BeginDisabled();
            if (ImGui.Button("Save##mlols")) RequestSave?.Invoke();
            if (target != null && target.ReadOnly) ImGui.EndDisabled();
            if (target == null) ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.Checkbox("This prop only##mlolpo", ref SelectedPropLightsOnly);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("List and light only the selected prop's lights.");
            if (target != null && ui.Session != null)
            {
                int ei = ui.Session.Entities.FindIndex(e => e.SourceFile == target);
                if (ei >= 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"entity {ei} (room {ui.Session.Entities[ei].Room})##mlolent")) { ui.SelectEntity(ei); ui.RevealSelection = true; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open this prop's entity page (room, flags, position).");
                }
            }

            float listH = compact ? 140.0f : Math.Clamp(ImGui.GetContentRegionAvail().Y * 0.28f, 90.0f, 200.0f);
            if (ImGui.BeginListBox("##mlolightlist", new Vector2(-1, listH)))
            {
                var onlyFile = SelectedPropLightsOnly ? (scene.ActiveFile ?? scene.OwnerFile(scene.SelectedLight)) : null;
                for (int i = 0; i < scene.Lights.Count; i++)
                {
                    var l = scene.Lights[i];
                    if (onlyFile != null && scene.OwnerFile(l) != onlyFile) continue;
                    string typeName = LightDefs.TypeNames[LightDefs.TypeToIndex((byte)l.Type)];
                    bool active = LightDefs.IsActiveAtHour(l.TimeFlags, (int)PreviewHour);
                    int ig = scene.InstanceGroup(l);
                    string fileTag = scene.Files.Count > 1 ? $"  [{scene.OwnerFile(l)?.Name}]" : "";
                    var col = new Vector4(0.35f + 0.65f * l.ColorR / 255.0f, 0.35f + 0.65f * l.ColorG / 255.0f, 0.35f + 0.65f * l.ColorB / 255.0f, 1);
                    ImGui.PushStyleColor(ImGuiCol.Text, active ? col : UiTheme.Muted);
                    if (ImGui.Selectable($"{i}: {typeName}{(ig > 0 ? $" (i{ig})" : "")}{fileTag}##mlolight{i}", scene.IsSelected(i)))
                    {
                        var io = ImGui.GetIO();
                        if (io.KeyCtrl) scene.ToggleSelect(i);
                        else if (io.KeyShift && scene.SelectedIndices.Count > 0) scene.RangeSelectTo(i);
                        else scene.SelectedIndex = i;
                        var owner = scene.OwnerFile(l);
                        if (owner != null && !io.KeyCtrl && !io.KeyShift) { scene.SelectFile(owner, false, false); ScrollToActiveProp = true; }
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) RequestFrameLight?.Invoke(l);
                }
                if (scene.Lights.Count == 0) ImGui.TextDisabled("No lights yet - + Point / + Spot / + Capsule adds one to the prop above.");
                ImGui.EndListBox();
            }
            ImGui.TextDisabled("Ctrl/Shift+click: multi-select   double-click: frame   Delete key: remove");

            ImGui.Separator();
            var sel = scene.SelectedLight;
            string title = scene.SelectedIndices.Count > 1 ? $"LIGHT PARAMETERS - {scene.SelectedIndices.Count} lights selected"
                         : sel != null ? $"LIGHT PARAMETERS - {sel.Type} #{scene.SelectedIndex}" : "LIGHT PARAMETERS";
            ImGui.TextDisabled(title);
            if (sel != null)
            {
                var owner = scene.OwnerFile(sel);
                if (owner != null) { ImGui.SameLine(); ImGui.TextDisabled($"on {owner.Name}"); }
                if (compact) DrawLightEditor(sel);
                else
                {
                    ImGui.BeginChild("##mlolighteditor", new Vector2(0, 0), ImGuiChildFlags.None);
                    DrawLightEditor(sel);
                    ImGui.EndChild();
                }
            }
            else ImGui.TextDisabled(scene.Lights.Count == 0 ? "Add a light above." : "Select a light in the list, or click one in the viewport.");
        }
    }
}

