using System;
using System.Linq;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        public bool RequestCopyLightSettings_O3, RequestPasteLightSettings_O3;
        public string LightSettingsStatus_O3 = "";
        private bool lightSettingsStatusBad_O3;

        private string LightLabel_O3(LightAttributes l)
        {
            if (l == null) return "a light";
            if (lightEditorExternal) return "a world light";
            int i = scene?.Lights?.IndexOf(l) ?? -1;
            var f = scene?.OwnerFile(l);
            string where = f != null ? f.Name : (WorldMode ? "the world" : "the scene");
            return i >= 0 ? $"{where} light {i}" : where + " light";
        }

        private void SetLightSettingsStatus_O3(string text, bool bad = false)
        {
            LightSettingsStatus_O3 = text;
            lightSettingsStatusBad_O3 = bad;
            MloStatus = text;
            if (MloMode && MloCreator != null) MloCreator.SetStatus(text, bad);
        }

        public void CopyLightSettings_O3(LightAttributes l)
        {
            if (l == null) { SetLightSettingsStatus_O3("Select a light to copy its settings from.", true); return; }
            LightSettings_O3.Copy(l, LightLabel_O3(l));
            SetLightSettingsStatus_O3($"Copied the settings of {LightSettings_O3.SourceLabel} - {LightSettings_O3.Describe()}");
        }

        public void PasteLightSettings_O3(LightAttributes l)
        {
            if (l == null) { SetLightSettingsStatus_O3("Select a light to paste onto.", true); return; }
            if (!LightSettings_O3.Has) { SetLightSettingsStatus_O3("Nothing copied yet - Copy settings on a light first (Ctrl+Shift+C).", true); return; }
            var changed = LightSettings_O3.Differences(l);
            scene?.PushUndo();
            LightSettings_O3.Apply(l);
            int others = 0;
            if (!lightEditorExternal && scene != null)
            {
                foreach (var i in scene.SelectedIndices)
                {
                    if (i < 0 || i >= scene.Lights.Count) continue;
                    var t = scene.Lights[i];
                    if (ReferenceEquals(t, l)) continue;
                    LightSettings_O3.Apply(t);
                    var f = scene.OwnerFile(t);
                    if (f != null) f.Dirty = true;
                    others++;
                }
                scene.Dirty = true;
                var of = scene.OwnerFile(l);
                if (of != null) of.Dirty = true;
            }
            string what = changed.Count == 0 ? "nothing (they already match)" : string.Join(", ", changed);
            SetLightSettingsStatus_O3(others > 0
                ? $"Pasted {what} from {LightSettings_O3.SourceLabel} onto {others + 1} lights."
                : $"Pasted {what} from {LightSettings_O3.SourceLabel}. Position, direction and the bone are unchanged.");
        }

        private void DrawLightSettingsClipboardRow_O3(LightAttributes l)
        {
            if (RequestCopyLightSettings_O3) { RequestCopyLightSettings_O3 = false; CopyLightSettings_O3(l); }
            if (RequestPasteLightSettings_O3) { RequestPasteLightSettings_O3 = false; PasteLightSettings_O3(l); }

            if (ImGui.SmallButton("Copy settings##o3lcopy")) CopyLightSettings_O3(l);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copy EVERY parameter of this light - colour, intensity, falloff, cone, flags,\n" +
                                 "flashiness, time flags, shadows, corona, volume, culling plane, fades, texture.\n" +
                                 "Not its position, direction, tangent or bone.  (Ctrl+Shift+C)\n" +
                                 "The clipboard is shared by every workspace: copy here, paste onto a world light.");
            ImGui.SameLine();
            bool has = LightSettings_O3.Has;
            if (!has) ImGui.BeginDisabled();
            int n = lightEditorExternal ? 1 : Math.Max(scene?.SelectedIndices?.Count ?? 1, 1);
            if (ImGui.SmallButton((n > 1 ? $"Paste settings ({n})" : "Paste settings") + "##o3lpaste")) PasteLightSettings_O3(l);
            if (!has) ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(has
                    ? $"Apply the copied settings to {(n > 1 ? $"the {n} selected lights" : "this light")}, keeping where they are\n" +
                      $"and which way they point. One Ctrl+Z undoes the lot.  (Ctrl+Shift+V)\n\nOn the clipboard: {LightSettings_O3.SourceLabel} - {LightSettings_O3.Describe()}"
                    : "Nothing copied yet - press Copy settings on the light you want to clone from.");
            if (has)
            {
                ImGui.SameLine();
                var c = LightSettings_O3.Copied;
                ImGui.ColorButton("##o3lsw", new Vector4(c.ColorR / 255.0f, c.ColorG / 255.0f, c.ColorB / 255.0f, 1.0f),
                                  ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(14, 14));
                ImGui.SameLine();
                ImGui.TextDisabled(LightSettings_O3.SourceLabel);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(LightSettings_O3.Describe());
            }
            if (!string.IsNullOrEmpty(LightSettingsStatus_O3))
            {
                if (lightSettingsStatusBad_O3) ImGui.TextColored(UiTheme.Warn, LightSettingsStatus_O3);
                else ImGui.TextDisabled(LightSettingsStatus_O3);
            }
        }
    }
}

