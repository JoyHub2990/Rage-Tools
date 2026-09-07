using System;
using System.Collections.Generic;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public partial class ModelViewer
    {
        public class TextureRow_S3
        {
            public AssetTextureInfo Info;
            public string Name = "";
            public string From = "";
            public RpfFileEntry FromEntry;
            public MatTexSource Source = MatTexSource.Missing;
            public string Slots = "";
            public int ExportIndex = -1;
            public bool Exportable => Info != null && Info.HasData;
        }

        private readonly List<TextureRow_S3> texRows_S3 = new List<TextureRow_S3>();
        private AssetPreview texRowsFor_S3;
        private int texRowsDrawable_S3 = -2;
        private string texFilter_S3 = "";

        public int TextureCount_S3 { get { EnsureTextureRows_S3(); return CountRows_S3(true); } }
        public int TextureMissingCount_S3 { get { EnsureTextureRows_S3(); return CountRows_S3(false); } }

        private int CountRows_S3(bool exportable)
        {
            int n = 0;
            foreach (var r in texRows_S3) if (r.Exportable == exportable) n++;
            return n;
        }

        public IReadOnlyList<TextureRow_S3> TextureRows_S3()
        {
            EnsureTextureRows_S3();
            return texRows_S3;
        }

        public List<AssetTextureInfo> ExportableTextures_S3()
        {
            EnsureTextureRows_S3();
            return texExportable_S3;
        }
        private readonly List<AssetTextureInfo> texExportable_S3 = new List<AssetTextureInfo>();

        private void EnsureTextureRows_S3()
        {
            int drawable = Preview?.SelectedDrawable ?? -1;
            if (ReferenceEquals(texRowsFor_S3, Preview) && texRowsDrawable_S3 == drawable) return;
            texRowsFor_S3 = Preview;
            texRowsDrawable_S3 = drawable;
            BuildTextureRows_S3();
        }

        private void BuildTextureRows_S3()
        {
            texRows_S3.Clear();
            texExportable_S3.Clear();
            var p = Preview;
            if (p == null) return;

            var seen = new Dictionary<GameTexture, TextureRow_S3>();
            var slots = new Dictionary<TextureRow_S3, List<string>>();

            void Add(AssetTextureInfo info, string from, RpfFileEntry entry, MatTexSource src, string slot)
            {
                if (info?.Texture == null) return;
                if (seen.TryGetValue(info.Texture, out var have))
                {
                    if (!string.IsNullOrEmpty(slot)) slots[have].Add(slot);
                    return;
                }
                var row = new TextureRow_S3
                {
                    Info = info,
                    Name = info.Name,
                    From = from,
                    FromEntry = entry,
                    Source = src,
                };
                seen[info.Texture] = row;
                slots[row] = new List<string>();
                if (!string.IsNullOrEmpty(slot)) slots[row].Add(slot);
                texRows_S3.Add(row);
            }

            if (p.Kind == AssetKind.TextureDict)
            {
                var self = Entry?.Name ?? Title ?? "this dictionary";
                foreach (var t in p.Textures) Add(t, self, Entry, MatTexSource.Embedded, null);
            }

            foreach (var t in p.Stats.Textures) Add(t, "embedded", Entry, MatTexSource.Embedded, null);

            var missing = new List<TextureRow_S3>();
            var missingSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in ViewerDrawables())
            {
                foreach (var m in p.InspectMaterials(d))
                {
                    foreach (var s in m.Textures)
                    {
                        if (s == null) continue;
                        var slot = string.IsNullOrEmpty(s.SlotName) ? "" : s.SlotName;
                        if (s.Texture == null || s.Info == null)
                        {
                            var want = string.IsNullOrEmpty(s.TextureName) ? "(unnamed)" : s.TextureName;
                            if (!missingSeen.Add(want)) continue;
                            missing.Add(new TextureRow_S3
                            {
                                Name = want,
                                From = "not found",
                                Source = MatTexSource.Missing,
                                Slots = slot,
                            });
                            continue;
                        }
                        var from = p.TextureSourceName_S3(s.Texture, s.Source, out var entry);
                        Add(s.Info, from, entry, s.Source, slot);
                    }
                }
            }

            foreach (var kv in slots)
            {
                kv.Key.Slots = kv.Value.Count == 0 ? "" : string.Join(", ", Dedupe_S3(kv.Value));
            }
            texRows_S3.AddRange(missing);

            texExportable_S3.Clear();
            foreach (var r in texRows_S3)
            {
                if (!r.Exportable) continue;
                r.ExportIndex = texExportable_S3.Count;
                texExportable_S3.Add(r.Info);
            }
        }

        private static List<string> Dedupe_S3(List<string> src)
        {
            var outp = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in src) if (seen.Add(s)) outp.Add(s);
            return outp;
        }

        private bool DrawTexturesTab_S3()
        {
            if (Preview == null) { ImGui.TextDisabled("Nothing open."); return true; }
            EnsureTextureRows_S3();

            if (texRows_S3.Count == 0)
            {
                ImGui.TextWrapped("This file uses no textures at all - no embedded dictionary, and " +
                                  "no material asking for one.");
                return true;
            }

            int usable = CountRows_S3(true), gone = CountRows_S3(false);
            int embedded = 0;
            foreach (var r in texRows_S3) if (r.Exportable && r.Source == MatTexSource.Embedded) embedded++;

            ImGui.TextDisabled($"{usable} texture(s) - {embedded} in this file, " +
                               $"{usable - embedded} resolved from .ytd files" +
                               (gone > 0 ? $", {gone} not found" : ""));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Every texture this model actually uses, resolved the way the game\n" +
                                 "resolves them: the ones packed inside the file, then the ones its\n" +
                                 "materials pull out of texture dictionaries in the archives. The From\n" +
                                 "line on each row says which .ytd it came out of, and all of them\n" +
                                 "export from the buttons above.");

            DrawTextureExportBar_Q1(ExportableTextures_S3());

            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##texfilter", "filter by name...", ref texFilter_S3, 64);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##texfilter")) texFilter_S3 = "";
            ImGui.Separator();

            if (!ImGui.BeginChild("##textures_s3", new Vector2(0, 0))) { ImGui.EndChild(); return true; }
            var filter = (texFilter_S3 ?? "").Trim();
            int shown = 0;
            for (int i = 0; i < texRows_S3.Count; i++)
            {
                var r = texRows_S3[i];
                if (filter.Length > 0 &&
                    (r.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (r.From ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                ImGui.PushID(i);
                DrawTextureRow_S3(r, i);
                ImGui.PopID();
                ImGui.Separator();
            }
            if (shown == 0) ImGui.TextDisabled("Nothing matches \"" + filter + "\".");
            ImGui.EndChild();
            return true;
        }

        private void DrawTextureRow_S3(TextureRow_S3 r, int index)
        {
            var t = r.Info;
            var id = t != null && t.HasData ? (TextureId?.Invoke(t.Texture) ?? IntPtr.Zero) : IntPtr.Zero;
            if (id != IntPtr.Zero) ImGui.Image(id, new Vector2(56, 56));
            else ImGui.Dummy(new Vector2(56, 56));
            ImGui.SameLine();
            ImGui.BeginGroup();

            int exportIndex = r.ExportIndex;
            if (r.Exportable)
            {
                if (ImGui.Selectable(r.Name + "##sel", selTexture == exportIndex)) { selTexture = exportIndex; YtdGrid_V44 = false; }
            }
            else
            {
                ImGui.TextDisabled(r.Name);
            }

            if (t != null)
            {
                ImGui.TextDisabled($"{t.Width}x{t.Height}  {t.Format}" + (t.Levels > 1 ? $"  {t.Levels} mips" : ""));
                ImGui.TextDisabled($"{t.DataBytes / 1024:N0} KB  {t.Usage}");
            }
            else ImGui.TextDisabled("nothing in the install answers this name");

            ImGui.TextDisabled("from ");
            ImGui.SameLine(0, 0);
            var col = r.Exportable
                ? (r.Source == MatTexSource.Embedded ? UiTheme.Ok : UiTheme.Accent)
                : UiTheme.Warn;
            ImGui.PushStyleColor(ImGuiCol.Text, col);
            ImGui.TextUnformatted(r.From);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(r.FromEntry != null
                    ? r.FromEntry.Path
                    : (r.Exportable
                        ? "Resolved the same way the game resolves it."
                        : "No dictionary in this install holds a texture of this name."));

            if (!string.IsNullOrEmpty(r.Slots))
            {
                ImGui.SameLine();
                ImGui.TextDisabled("| " + r.Slots);
            }
            ImGui.EndGroup();
        }

        public IEnumerable<string> TextureDumpLines_S3()
        {
            EnsureTextureRows_S3();
            foreach (var r in texRows_S3)
            {
                var t = r.Info;
                yield return t != null
                    ? $"MVTEX {r.Name}  {t.Width}x{t.Height} {t.Format}  {t.DataBytes / 1024:N0} KB  " +
                      $"from={r.From}  path={r.FromEntry?.Path ?? "-"}  slots={r.Slots}"
                    : $"MVTEX {r.Name}  MISSING  from={r.From}  slots={r.Slots}";
            }
        }
    }
}

