using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        public class CompRow
        {
            public string Name;
            public string Slot;
            public bool IsVariant;
            public string DiskPath;
            public string Component;
            public string MetaBone;
            public string Txd;
            public bool ModelMissing;
        }

        public bool RequestYtdDialog_V22;
        public string RequestYtdFromArchive_V22;
        public int RequestDetachYtd_V22 = -1;
        public char RequestTextureVariant_V23;
        public bool RequestWeaponSearch_V22;
        public string RequestWeaponAttach_V22;
        public string RequestWeaponOpen_V22;
        public int RequestWeaponDetach_V22 = -1;
        public readonly List<CompRow> WeaponComps_V22 = new List<CompRow>();
        public string WeaponStatus_V22 = "";

        public void ClearWeaponSearch_V23()
        {
            WeaponComps_V22.Clear();
            WeaponStatus_V22 = "";
        }

        private int AttachedIndexOf_V23(string stem)
        {
            var att = Preview?.Attachments;
            if (att == null || string.IsNullOrEmpty(stem)) return -1;
            for (int i = 0; i < att.Count; i++)
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(att[i].Name), stem, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private bool texCardOpen_V22 = true;
        private string ytdName_V22 = "";
        private Vector2 imageMin_V22, imageMax_V22;
        private bool imageShown_V22;

        private void NoteImageRect_V22()
        {
            imageMin_V22 = ImGui.GetItemRectMin();
            imageMax_V22 = ImGui.GetItemRectMax();
            imageShown_V22 = true;
        }

        private void DrawTexCard_V22()
        {
            if (!imageShown_V22 || Preview == null || Preview.Kind == AssetKind.TextureDict) return;
            imageShown_V22 = false;
            if (Preview.Model == null) return;

            var missing = Preview.MissingTextures ?? Array.Empty<string>();
            var dicts = Preview.AttachedYtds;
            float s = UiScale_V17.Scale;
            float lineH = ImGui.GetTextLineHeightWithSpacing();
            var variants = Preview.TextureVariants;
            float w = Math.Min(340.0f * s, imageMax_V22.X - imageMin_V22.X - 16.0f * s);
            float letterW = ImGui.CalcTextSize("m").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
            int perRow = Math.Max(1, (int)((w - 16.0f * s - ImGui.CalcTextSize("Variant: ").X) / letterW));
            int variantRows = variants.Count > 1 ? (variants.Count + perRow - 1) / perRow : 0;
            int rows = texCardOpen_V22 ? Math.Min(missing.Length, 6) + (missing.Length > 6 ? 1 : 0) + dicts.Count + 1 + variantRows + (dicts.Count > 1 ? 1 : 0) : 0;
            float h = lineH * (1 + rows) + 22.0f * s;
            var pos = new Vector2(imageMin_V22.X + 8.0f * s, imageMax_V22.Y - h - 8.0f * s);

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.86f);
            var wf = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
                   | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                   | ImGuiWindowFlags.NoSavedSettings;
            if (!ImGui.Begin("##v22mvtexcard", wf)) { ImGui.End(); return; }

            ImGui.SetNextItemOpen(texCardOpen_V22, ImGuiCond.Always);
            string head = missing.Length > 0
                ? $"Textures: {missing.Length} MISSING  ({dicts.Count} attached)###v22mvhead"
                : $"Textures: all found  ({dicts.Count} attached)###v22mvhead";
            if (missing.Length > 0) ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Warn);
            texCardOpen_V22 = ImGui.TreeNodeEx(head, ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.NoTreePushOnOpen);
            if (missing.Length > 0) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The texture names this model asks for and the viewer could not find.\n" +
                                 "Attach the .ytd that carries them - from disk, or out of the game\n" +
                                 "archives by name - and the model rebuilds textured.");
            if (!texCardOpen_V22) { ImGui.End(); return; }

            for (int i = 0; i < Math.Min(missing.Length, 6); i++) ImGui.TextDisabled("  " + missing[i]);
            if (missing.Length > 6) ImGui.TextDisabled($"  ...and {missing.Length - 6} more");

            int soloAt = Preview.SoloYtdIndex_U6();
            for (int i = 0; i < dicts.Count; i++)
            {
                var (name, ytd) = dicts[i];
                bool fromGame = name.StartsWith("gta:");
                var shown = System.IO.Path.GetFileName(fromGame ? name.Substring(4) : name);
                int count = ytd?.TextureDict?.Textures?.data_items?.Length ?? 0;
                if (ImGui.SmallButton($"x##v22mvun{i}")) RequestDetachYtd_V22 = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Unload this dictionary.");
                ImGui.SameLine();
                bool on = !Preview.IsYtdMuted_U6(name);
                if (dicts.Count > 1)
                {
                    if (ImGui.Checkbox($"##v22mvon{i}", ref on)) Preview.SetYtdMuted_U6(name, !on);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(on ? "On - this dictionary's textures are used." : "Off - skipped, the others show through.");
                    ImGui.SameLine();
                }
                if (i == soloAt) ImGui.TextColored(UiTheme.Accent, $"{shown}  ({count} tex{(fromGame ? ", archives" : "")})");
                else if (!on) ImGui.TextDisabled($"{shown}  ({count} tex{(fromGame ? ", archives" : "")})");
                else ImGui.Text($"{shown}  ({count} tex{(fromGame ? ", archives" : "")})");
            }

            if (dicts.Count > 1)
            {
                if (ImGui.SmallButton("<##v22mvsolo")) Preview.StepYtdSolo_U6(-1);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show the previous dictionary on its own.");
                ImGui.SameLine(0, 3);
                if (ImGui.SmallButton(">##v22mvsolo")) Preview.StepYtdSolo_U6(+1);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show the next dictionary on its own.");
                ImGui.SameLine(0, 6);
                if (soloAt >= 0)
                {
                    ImGui.Text($"showing {soloAt + 1} of {dicts.Count}");
                    ImGui.SameLine(0, 6);
                    if (ImGui.SmallButton("All on##v22mvsolo")) Preview.UnmuteAllYtds_U6();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Stop stepping - use every attached dictionary again.");
                }
                else ImGui.TextDisabled("step through the dictionaries one at a time");
            }

            if (variants.Count > 1)
            {
                ImGui.TextDisabled("Variant:");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("This model's textures come in lettered sets (_a_, _b_, _c_...) -\n" +
                                     "the ped's variations. These are the ones found beside the file.");
                char shown = Preview.TextureVariant == '\0' ? Preview.AuthoredVariant : Preview.TextureVariant;
                int col = 0;
                foreach (var v in variants)
                {
                    if (col++ % perRow != 0 || col == 1) ImGui.SameLine();
                    bool on = v == shown;
                    if (on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                    if (ImGui.SmallButton($"{v}##v23var{v}")) RequestTextureVariant_V23 = v;
                    if (on) ImGui.PopStyleColor();
                }
            }

            if (ImGui.SmallButton("Load YTD...##v22mv")) OpenYtdPicker_V24();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Browse for a .ytd - in the archives or on the disk - starting in this model's own folder.");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(70.0f * s, ImGui.GetContentRegionAvail().X - 82.0f * s));
            bool go = ImGui.InputTextWithHint("##v22mvytd", "ytd name in the game files", ref ytdName_V22, 64,
                                              ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.SmallButton("From game##v22mv") || go) && !string.IsNullOrWhiteSpace(ytdName_V22))
                RequestYtdFromArchive_V22 = ytdName_V22.Trim();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Pull a named .ytd out of the game archives and attach it\n(vehshare, mapdetail, a mod's shared dictionary...).");
            ImGui.End();
        }

        private void DrawWeaponTab_V22()
        {
            if (Preview == null || !Preview.IsWeapon) { ImGui.TextDisabled("The open model has no WAP attachment bones."); return; }

            var att = Preview.Attachments;
            ImGui.TextWrapped("Components snap to the weapon's WAP bones - their own AAP bone says which. Find this weapon's components in the archives, then attach any of them.");
            ImGui.Separator();

            int loose = 0;
            for (int i = 0; i < att.Count; i++)
            {
                var stem = System.IO.Path.GetFileNameWithoutExtension(att[i].Name);
                if (WeaponComps_V22.Any(w => !w.IsVariant && string.Equals(w.Name, stem, StringComparison.OrdinalIgnoreCase))) continue;
                if (ImGui.SmallButton($"x##v22wdet{i}")) RequestWeaponDetach_V22 = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Take this component off.");
                ImGui.SameLine();
                ImGui.Text($"{att[i].Name}  on {att[i].Bone}");
                loose++;
            }
            if (loose > 0) ImGui.Separator();

            if (ImGui.Button("Find components")) RequestWeaponSearch_V22 = true;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Search the archives for this weapon's own components -\n" +
                                 "they live beside it, named " + (Preview.Stats?.Name ?? "").Replace(".ydr", "") + "_*.");
            if (!string.IsNullOrEmpty(WeaponStatus_V22)) { ImGui.SameLine(); ImGui.TextDisabled(WeaponStatus_V22); }

            if (!ImGui.BeginChild("##v22wlist", new Vector2(0, 0))) { ImGui.EndChild(); return; }
            foreach (var wc in WeaponComps_V22)
            {
                if (wc.IsVariant)
                {
                    if (ImGui.SmallButton($"Open##v22wo{wc.Name}")) RequestWeaponOpen_V22 = wc.Name;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("A full weapon variant, not a component - opens as its own model.");
                }
                else if (wc.ModelMissing)
                {
                    ImGui.BeginDisabled();
                    ImGui.SmallButton($"Attach##v22wa{wc.Name}");
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"{wc.Component}\nThe weapon's meta asks for the model '{wc.Name}',\nand no .ydr of that name is in the archives or beside this file.");
                }
                else
                {
                    int at = AttachedIndexOf_V23(wc.Name);
                    if (at >= 0)
                    {
                        if (ImGui.SmallButton($"Detach##v22wd{wc.Name}")) RequestWeaponDetach_V22 = at;
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"On WAP{wc.Slot}. Take it off.");
                    }
                    else
                    {
                        if (ImGui.SmallButton($"Attach##v22wa{wc.Name}")) RequestWeaponAttach_V22 = wc.Name;
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Snaps to WAP{wc.Slot} - the component's own AAP bone says so.");
                    }
                }
                ImGui.SameLine();
                if (wc.ModelMissing) ImGui.TextDisabled(wc.Name); else ImGui.Text(wc.Name);
                ImGui.SameLine();
                ImGui.TextDisabled(wc.IsVariant ? "(variant)" : $"-> {wc.Slot}");
                if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(wc.Component))
                    ImGui.SetTooltip($"{wc.Component}\nfrom the weapon's meta, bone {wc.MetaBone ?? "(none named)"}" +
                                     (string.IsNullOrEmpty(wc.Txd) ? "" : $"\ntexture dictionary {wc.Txd}"));
            }
            ImGui.EndChild();
        }
    }
}

