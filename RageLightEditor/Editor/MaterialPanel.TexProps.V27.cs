using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public partial class MaterialPanel
    {
        private static readonly TextureUsageFlags[] AllUsageFlags_V27 =
            Enum.GetValues(typeof(TextureUsageFlags)).Cast<TextureUsageFlags>().ToArray();
        private static readonly string[] AllUsages_V27 = Enum.GetNames(typeof(TextureUsage));

        public static readonly bool ForceOpen_V27 =
            Environment.GetEnvironmentVariable("RLE_TEXPROPS") == "1";

        private void PushUndoTexture_V27(MaterialRef m, GameTexture t, string label)
        {
            if (t == null) return;
            undo.Add(new MatUndo
            {
                Target = m?.Shader,
                Tex = t,
                TexUsage = t.UsageData,
                TexExtra = t.ExtraFlags,
                Label = label,
            });
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            redo.Clear();
        }

        public static TextureUsage UsageForRole_V27(MatTexRole role)
        {
            switch (role)
            {
                case MatTexRole.Bump: return TextureUsage.NORMAL;
                case MatTexRole.Spec: return TextureUsage.SPECULAR;
                case MatTexRole.Detail: return TextureUsage.DETAIL;
                case MatTexRole.Tint: return TextureUsage.TINTPALETTE;
                case MatTexRole.Diffuse: return TextureUsage.DIFFUSE;
                default: return TextureUsage.DEFAULT;
            }
        }

        public static bool CanEditTextureProps_V27(MatTexSource source) =>
            source == MatTexSource.Embedded || source == MatTexSource.Imported;

        public static string TexturePropsReason_V27(MatTexSource source)
        {
            switch (source)
            {
                case MatTexSource.Embedded:
                case MatTexSource.Imported:
                    return null;
                case MatTexSource.GameArchive:
                    return "Read from the game archives, which nothing here writes to. Embed it to make it yours.";
                case MatTexSource.LoadedYtd:
                    return "This texture lives in a loaded .ytd, which this editor reads but does not write. " +
                           "Embed it to save these along with the drawable.";
                default:
                    return "No texture in this slot.";
            }
        }

        private void DrawTextureProps_V27(MaterialRef m, MatTexInfo slot, MatTexResolution res)
        {
            if (!res.Found || res.Texture == null) return;
            var t = res.Texture;

            if (ForceOpen_V27) ImGui.SetNextItemOpen(true);
            if (!ImGui.TreeNodeEx($"Texture properties###texprops{slot.Hash}", ImGuiTreeNodeFlags.SpanAvailWidth))
                return;

            long bytes = t.Data?.FullData?.Length ?? 0;
            ImGui.TextDisabled($"{t.Format}   {t.Width}x{t.Height}   {t.Levels} mip(s)   " +
                               (bytes >= 1024 ? $"{bytes / 1024:N0} KB" : $"{bytes} bytes"));

            bool editable = CanEditTextureProps_V27(res.Source);
            var reason = TexturePropsReason_V27(res.Source);
            if (!editable && reason != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.AccentBright);
                ImGui.TextWrapped(reason);
                ImGui.PopStyleColor();
            }

            if (!editable) ImGui.BeginDisabled();

            float x0 = ImGui.GetCursorPosX();
            float labelCol = x0 + 84f * UiScale_V17.Scale;

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Usage");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("What kind of texture this is. The game reads it, and a normal map " +
                                 "marked DIFFUSE is exactly the sort of thing you would otherwise " +
                                 "re-export the whole drawable to correct.");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelCol);
            int usage = (int)t.Usage;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo($"##usage{slot.Hash}", ref usage, AllUsages_V27, AllUsages_V27.Length) && editable)
            {
                PushUndoTexture_V27(m, t, $"{t.Name} usage");
                t.Usage = (TextureUsage)usage;
                MaterialEditing.MarkDirty(m);
                Status = $"{t.Name} usage = {t.Usage}";
            }

            var want = UsageForRole_V27(slot.Role);
            if (t.Usage != want)
            {
                ImGui.SetCursorPosX(labelCol);
                if (ImGui.SmallButton($"Set to {want} - what this slot is for##fromslot{slot.Hash}") && editable)
                {
                    PushUndoTexture_V27(m, t, $"{t.Name} usage");
                    t.Usage = want;
                    MaterialEditing.MarkDirty(m);
                    Status = $"{t.Name} usage = {want}";
                }
            }

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Extra flags");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("A second word the game keeps beside the usage. 0 on almost everything " +
                                 "in V, 1 on a few. Leave it unless you are matching a file that has it.");
            ImGui.SameLine();
            ImGui.SetCursorPosX(labelCol);
            int extra = (int)t.ExtraFlags;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt($"##extraflags{slot.Hash}", ref extra, 1) && editable)
            {
                PushUndoTexture_V27(m, t, $"{t.Name} extra flags");
                t.ExtraFlags = (uint)Math.Max(0, extra);
                MaterialEditing.MarkDirty(m);
                Status = $"{t.Name} extra flags = {t.ExtraFlags}";
            }

            var set = t.UsageFlags;
            var setNames = AllUsageFlags_V27.Where(f => (set & f) != 0).Select(f => f.ToString()).ToList();
            string summary = setNames.Count == 0 ? "(none)"
                           : setNames.Count <= 3 ? string.Join(", ", setNames)
                           : $"{setNames[0]}, {setNames[1]} and {setNames.Count - 2} more";
            if (ForceOpen_V27) ImGui.SetNextItemOpen(true);
            if (ImGui.TreeNodeEx($"Usage flags: {summary}###usageflags{slot.Hash}", ImGuiTreeNodeFlags.SpanAvailWidth))
            {
                ImGui.TextWrapped("How the streamer may treat this texture. The size flags are what the game " +
                                  "shipped and are best left as read; NOT_HALF and MAPS_HALF are the two worth " +
                                  "setting by hand.");

                float avail = ImGui.GetContentRegionAvail().X;
                int cols = avail > 300f * UiScale_V17.Scale ? 2 : 1;
                int half = (AllUsageFlags_V27.Length + cols - 1) / cols;
                if (ImGui.BeginTable($"uf{slot.Hash}", cols, ImGuiTableFlags.SizingStretchSame))
                {
                    for (int r = 0; r < half; r++)
                    {
                        ImGui.TableNextRow();
                        for (int c = 0; c < cols; c++)
                        {
                            ImGui.TableSetColumnIndex(c);
                            int i = c * half + r;
                            if (i >= AllUsageFlags_V27.Length) continue;
                            var f = AllUsageFlags_V27[i];
                            bool on = (set & f) != 0;
                            if (ImGui.Checkbox($"{f}##uf{slot.Hash}_{i}", ref on) && editable)
                            {
                                PushUndoTexture_V27(m, t, $"{t.Name} {f}");
                                t.UsageFlags = on ? (t.UsageFlags | f) : (t.UsageFlags & ~f);
                                MaterialEditing.MarkDirty(m);
                                Status = $"{t.Name} {f} {(on ? "on" : "off")}";
                            }
                        }
                    }
                    ImGui.EndTable();
                }
                ImGui.TreePop();
            }

            if (!editable) ImGui.EndDisabled();

            int users = CountMaterialsUsing_V27(t);
            if (users > 1)
                ImGui.TextDisabled($"Shared by {users} materials in this prop - these apply to all of them.");

            ImGui.TreePop();
        }

        public void TexPropsUndoSelfTest_V27(Action<string, bool, string> check)
        {
            var t = new GameTexture { Name = "v27_undo_probe", Usage = TextureUsage.DIFFUSE, ExtraFlags = 0 };
            t.UsageFlags = TextureUsageFlags.UNK24;
            uint usage0 = t.UsageData, extra0 = t.ExtraFlags;

            PushUndoTexture_V27(null, t, "v27 probe usage");
            t.Usage = TextureUsage.NORMAL;
            t.ExtraFlags = 1;

            Undo();
            check("v27 texture properties: undo puts the texture's own words back",
                  t.UsageData == usage0 && t.ExtraFlags == extra0,
                  $"{t.Usage}, flags {t.UsageFlags}, extra {t.ExtraFlags}");

            Redo();
            check("v27 texture properties: ...and redo sets them again",
                  t.Usage == TextureUsage.NORMAL && t.ExtraFlags == 1,
                  $"{t.Usage}, extra {t.ExtraFlags}");

            Undo();
        }

        private int CountMaterialsUsing_V27(GameTexture t)
        {
            if (t == null) return 0;
            int n = 0;
            foreach (var other in Materials)
            {
                foreach (var h in MaterialEditing.ParamHashes(other.Shader).ToList())
                {
                    var tb = MaterialEditing.GetTexture(other.Shader, h);
                    if (tb != null && tb.NameHash == t.NameHash) { n++; break; }
                }
            }
            return n;
        }
    }
}

