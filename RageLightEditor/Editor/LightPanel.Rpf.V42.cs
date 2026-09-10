using System;
using System.Numerics;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private bool rpfDiskResults_V42;

        private static void RpfToolbarFit_V42(float groupWidth)
        {
            ImGui.SameLine(0, 12);
            if (ImGui.GetContentRegionAvail().X < groupWidth) ImGui.NewLine();
        }

        private static readonly Vector4 GlyphFolder_V42 = new Vector4(0.92f, 0.74f, 0.36f, 1.0f);
        private static readonly Vector4 GlyphModel_V42 = new Vector4(0.64f, 0.56f, 0.96f, 1.0f);
        private static readonly Vector4 GlyphTexture_V42 = new Vector4(0.95f, 0.60f, 0.30f, 1.0f);
        private static readonly Vector4 GlyphMeta_V42 = new Vector4(0.45f, 0.80f, 0.50f, 1.0f);
        private static readonly Vector4 GlyphCollision_V42 = new Vector4(0.90f, 0.42f, 0.40f, 1.0f);
        private static readonly Vector4 GlyphParticle_V42 = new Vector4(0.90f, 0.50f, 0.75f, 1.0f);
        private static readonly Vector4 GlyphAnim_V42 = new Vector4(0.42f, 0.76f, 0.86f, 1.0f);
        private static readonly Vector4 GlyphAudio_V42 = new Vector4(0.36f, 0.72f, 0.68f, 1.0f);
        private static readonly Vector4 GlyphScript_V42 = new Vector4(0.88f, 0.80f, 0.42f, 1.0f);
        private static readonly Vector4 GlyphOther_V42 = new Vector4(0.58f, 0.62f, 0.68f, 1.0f);

        private static Vector4 KindTint_V42(string name)
        {
            var n = name ?? "";
            int dot = n.LastIndexOf('.');
            var ext = dot >= 0 ? n.Substring(dot + 1).ToLowerInvariant() : "";
            switch (ext)
            {
                case "ydr": case "ydd": case "yft": return GlyphModel_V42;
                case "ytd": case "dds": case "png": case "jpg": case "jpeg": case "gif": return GlyphTexture_V42;
                case "ytyp": case "ymap": case "ymt": case "meta": case "xml": case "ymf":
                case "pso": case "ide": case "ipl": case "ynd": case "ynv": return GlyphMeta_V42;
                case "ybn": return GlyphCollision_V42;
                case "ypt": return GlyphParticle_V42;
                case "ycd": case "yed": case "yld": case "yfd": case "ypdb": case "mrf": case "cut": return GlyphAnim_V42;
                case "awc": case "rel": return GlyphAudio_V42;
                case "ysc": case "lua": case "gfx": case "fxc": return GlyphScript_V42;
                case "rpf": return ArchiveWorkspaceColour;
                default: return GlyphOther_V42;
            }
        }

        private void RpfGlyph_V42(in RpfExplorer.Row r)
        {
            float h = ImGui.GetTextLineHeight();
            float w = h;
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            p.Y += 1;

            bool archive = (r.Name ?? "").EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
            if (archive)
            {
                var c = ArchiveWorkspaceColour;
                uint col = ImGui.GetColorU32(c);
                uint faint = ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, 0.28f));
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + 1), new Vector2(p.X + w - 1, p.Y + h - 1), faint, 2);
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + h * 0.38f), new Vector2(p.X + w - 1, p.Y + h * 0.38f + 2), col);
                dl.AddRect(new Vector2(p.X + 1, p.Y + 1), new Vector2(p.X + w - 1, p.Y + h - 1), col, 2);
            }
            else if (r.IsFolder)
            {
                uint col = ImGui.GetColorU32(GlyphFolder_V42);
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + 2), new Vector2(p.X + w * 0.5f, p.Y + 6), col, 1.5f);
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + 4), new Vector2(p.X + w - 1, p.Y + h - 1), col, 2);
            }
            else
            {
                var c = KindTint_V42(r.Name);
                uint col = ImGui.GetColorU32(c);
                uint faint = ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, 0.30f));
                float fold = w * 0.32f;
                dl.AddRectFilled(new Vector2(p.X + 2, p.Y), new Vector2(p.X + w - 2, p.Y + h - 1), faint, 2);
                dl.AddTriangleFilled(new Vector2(p.X + w - 2 - fold, p.Y),
                                     new Vector2(p.X + w - 2, p.Y),
                                     new Vector2(p.X + w - 2, p.Y + fold), col);
                dl.AddRectFilled(new Vector2(p.X + 2, p.Y + h - 4), new Vector2(p.X + w - 2, p.Y + h - 1), col, 1);
            }
            ImGui.Dummy(new Vector2(w + 2, h));
        }

        private void DrawRpfRows_N4()
        {
            bool searching = rpfSearchAll && rpfSearch.Trim().Length > 0;
            if (!searching) Rpf.EnsureList();
            ResetRpfDragOut_V55();
            var rows = Rpf.Rows;

            float footer = ImGui.GetTextLineHeightWithSpacing() * 2 + 12;
            var flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable
                      | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterH;

            var acc = UiTheme.Accent;
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, 3));
            ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(1, 1, 1, 0.025f));
            ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(1, 1, 1, 0.05f));
            ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(1, 1, 1, 0.06f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(acc.X, acc.Y, acc.Z, 0.30f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1, 1, 1, 0.07f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(acc.X, acc.Y, acc.Z, 0.42f));

            BeginRpfReveal_U1();
            if (ImGui.BeginTable("##rpftable", 5, flags, new Vector2(0, -footer)))
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort, 0.34f);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortDescending, 76);
                ImGui.TableSetupColumn("Attributes", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 0.46f);
                ImGui.TableHeadersRow();

                unsafe
                {
                    var specs = ImGui.TableGetSortSpecs();
                    if (specs.NativePtr != null && specs.SpecsDirty && specs.SpecsCount > 0)
                    {
                        var s0 = specs.Specs;
                        int col = s0.ColumnIndex;
                        bool asc = s0.SortDirection != ImGuiSortDirection.Descending;
                        if (col != Rpf.SortColumn || asc != Rpf.SortAscending)
                        {
                            Rpf.SortColumn = col;
                            Rpf.SortAscending = asc;
                            if (searching)
                            {
                                if (rpfDiskResults_V42) Rpf.ShowDiskSearchResults_V35(rpfDiskHits_V35);
                                else Rpf.ShowSearchResults(rpfHits);
                            }
                            else
                            {
                                Rpf.Invalidate();
                                Rpf.EnsureList();
                            }
                            rows = Rpf.Rows;
                        }
                        specs.SpecsDirty = false;
                    }
                }

                if (rows.Count == 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextDisabled("Nothing here.");
                }
                else
                {
                    unsafe
                    {
                        var clip = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                        clip.Begin(rows.Count);
                        while (clip.Step())
                            for (int i = clip.DisplayStart; i < clip.DisplayEnd; i++)
                                DrawRpfTableRow_V42(rows[i], i);
                        clip.End();
                        clip.Destroy();
                    }
                }
                DrawRpfBackgroundMenu_V46();
                ImGui.EndTable();
            }
            RpfListKeys_U19();
            EndRpfReveal_U1();
            ImGui.PopStyleColor(7);
            ImGui.PopStyleVar();

            ImGui.TextDisabled(searching
                ? (rpfHitTotal > rows.Count
                    ? $"{rows.Count:N0} of {rpfHitTotal:N0} matches across every archive"
                    : $"{rpfHitTotal:N0} match{(rpfHitTotal == 1 ? "" : "es")} across every archive")
                : (Rpf.Filter.Length > 0
                    ? $"{rows.Count:N0} of {Rpf.RowTotal:N0} entries"
                    : $"{Rpf.RowTotal:N0} entries"));
            if (RpfMultiSelected_U19)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextUnformatted(RpfSelectionSummary_U19());
            }
            else if (rpfHasSelRow)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextUnformatted(rpfSelRow.Name ?? "");
                if (!rpfSelRow.IsFolder && rpfSelRow.Size > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(RpfExplorer.SizeText(rpfSelRow.Size));
                }
            }
        }

        private void DrawRpfTableRow_V42(in RpfExplorer.Row r, int i)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.PushID(i);

            bool sel = RpfRowSelected_O1(r);
            float rowH = ImGui.GetTextLineHeight() + 2;
            if (ImGui.Selectable("##row", sel,
                                 ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
                                 new Vector2(0, rowH)))
            {
                ClickRpfRow_U19(r, i);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ActivateRpfRow_N4(r);
            }
            RpfRowRect_T4(r);
            RpfDragOutHook_V55(r);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal) && !string.IsNullOrEmpty(r.Path))
                ImGui.SetTooltip(r.Path);
            if (ImGui.BeginPopupContextItem("##rowctx"))
            {
                if (!RpfRowSelected_O1(r)) SelectRpfRow_O1(r);
                DrawRpfRowMenu_N4(r);
                ImGui.EndPopup();
            }

            ImGui.SameLine(0, 2);
            RpfGlyph_V42(r);
            ImGui.SameLine(0, 6);
            ImGui.TextUnformatted(r.Name ?? "");

            ImGui.TableSetColumnIndex(1);
            ImGui.TextDisabled(r.Type ?? "");
            ImGui.TableSetColumnIndex(2);
            var size = RpfExplorer.SizeText(r.Size);
            if (size.Length > 0)
            {
                float pad = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(size).X;
                if (pad > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + pad);
                ImGui.TextDisabled(size);
            }
            ImGui.TableSetColumnIndex(3);
            ImGui.TextDisabled(r.Attr ?? "");
            ImGui.TableSetColumnIndex(4);
            ImGui.TextDisabled(Rpf.DisplayPath_O1(r));
            ImGui.PopID();
        }
    }
}
