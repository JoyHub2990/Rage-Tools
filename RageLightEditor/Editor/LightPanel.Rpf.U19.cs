using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private readonly List<RpfExplorer.Row> rpfMulti_U19 = new List<RpfExplorer.Row>();
        private int rpfAnchor_U19 = -1;

        public List<RpfExplorer.Row> RequestRpfExtractRows_U19;
        public List<RpfExplorer.Row> RequestRpfExportXmlRows_U19;
        public List<RpfExplorer.Row> RequestRpfDragOutMany_U19;

        public int RpfSelectedCount_U19 => rpfMulti_U19.Count;
        public bool RpfMultiSelected_U19 => rpfMulti_U19.Count > 1;

        private static bool SameRow_U19(in RpfExplorer.Row a, in RpfExplorer.Row b)
        {
            if (a.Entry != null || b.Entry != null) return ReferenceEquals(a.Entry, b.Entry);
            return string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private int IndexInMulti_U19(in RpfExplorer.Row r)
        {
            for (int i = 0; i < rpfMulti_U19.Count; i++)
                if (SameRow_U19(rpfMulti_U19[i], r)) return i;
            return -1;
        }

        private bool InMulti_U19(in RpfExplorer.Row r) => IndexInMulti_U19(r) >= 0;

        private void SetPrimary_U19(in RpfExplorer.Row r)
        {
            rpfSelRow = r;
            rpfHasSelRow = true;
            rpfSelNode = Rpf.Current;
            Rpf.Selected = r.Entry;
        }

        private void ClickRpfRow_U19(in RpfExplorer.Row r, int index)
        {
            var io = ImGui.GetIO();
            ClickRpfRow_U19(r, index, io.KeyCtrl, io.KeyShift);
        }

        public void ClickRpfRow_U19(in RpfExplorer.Row r, int index, bool ctrl, bool shift)
        {
            var rows = Rpf.Rows;
            if (shift && rpfAnchor_U19 >= 0 && rpfAnchor_U19 < rows.Count && index >= 0 && index < rows.Count)
            {
                if (!ctrl) rpfMulti_U19.Clear();
                int a = Math.Min(rpfAnchor_U19, index), b = Math.Max(rpfAnchor_U19, index);
                for (int i = a; i <= b; i++)
                    if (!InMulti_U19(rows[i])) rpfMulti_U19.Add(rows[i]);
                SetPrimary_U19(r);
                return;
            }
            if (ctrl)
            {
                int at = IndexInMulti_U19(r);
                if (at >= 0)
                {
                    rpfMulti_U19.RemoveAt(at);
                    if (rpfMulti_U19.Count == 0) { ClearRpfSelection_O1(); return; }
                    SetPrimary_U19(rpfMulti_U19[rpfMulti_U19.Count - 1]);
                }
                else
                {
                    rpfMulti_U19.Add(r);
                    SetPrimary_U19(r);
                }
                rpfAnchor_U19 = index;
                return;
            }
            rpfMulti_U19.Clear();
            rpfMulti_U19.Add(r);
            SetPrimary_U19(r);
            rpfAnchor_U19 = index;
        }

        public int SelectAllRpfRows_U19()
        {
            var rows = Rpf.Rows;
            rpfMulti_U19.Clear();
            if (rows.Count == 0) { ClearRpfSelection_O1(); return 0; }
            foreach (var r in rows) rpfMulti_U19.Add(r);
            SetPrimary_U19(rows[rows.Count - 1]);
            rpfAnchor_U19 = 0;
            RpfStatus = RpfSelectionSummary_U19();
            return rows.Count;
        }

        public string RpfSelectionSummary_U19()
        {
            int files = 0, folders = 0;
            long bytes = 0;
            foreach (var r in rpfMulti_U19)
            {
                if (r.IsFolder) folders++;
                else { files++; bytes += r.Size; }
            }
            var sb = new StringBuilder();
            sb.Append(rpfMulti_U19.Count).Append(" selected");
            if (files > 0) sb.Append(": ").Append(files).Append(files == 1 ? " file" : " files");
            if (folders > 0) sb.Append(files > 0 ? ", " : ": ").Append(folders).Append(folders == 1 ? " folder" : " folders");
            if (bytes > 0) sb.Append("  ").Append(RpfExplorer.SizeText(bytes));
            return sb.ToString();
        }

        private void RpfListKeys_U19()
        {
            if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;
            var io = ImGui.GetIO();
            if (io.WantTextInput) return;
            bool searching = rpfSearchAll && rpfSearch.Trim().Length > 0;
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A, false)) SelectAllRpfRows_U19();
            else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C, false) && rpfHasSelRow) ApplyRpf_O1(RpfEdit.Copy(SelectedRpfRows_O1()));
            else if (ImGui.IsKeyPressed(ImGuiKey.Delete, false) && rpfHasSelRow && RpfEditMode && !searching) AskDeleteRpf_O1();
        }

        private void CopyRpfPaths_U19(List<RpfExplorer.Row> rows, bool namesOnly)
        {
            var sb = new StringBuilder();
            foreach (var r in rows) sb.AppendLine(namesOnly ? (r.Name ?? "") : (r.Path ?? ""));
            ImGui.SetClipboardText(sb.ToString());
            RpfStatus = "copied " + rows.Count + (namesOnly ? " names" : " paths");
        }

        private void CountRpfRows_U19(List<RpfExplorer.Row> rows, out int xmlable, out int convertible, out List<string> convertPaths)
        {
            xmlable = 0; convertible = 0;
            convertPaths = new List<string>();
            foreach (var r in rows)
            {
                if (r.IsFolder) continue;
                if (r.IsFs)
                {
                    if (MainForm.IsConvertibleXml_V52(r.Name)) { convertible++; convertPaths.Add(r.Path); }
                    else xmlable++;
                }
                else if (r.Entry is RpfFileEntry fe && RpfExplorer.ViewKindOf(fe) == "xml") xmlable++;
            }
        }

        private void DrawRpfMultiSelected_U19()
        {
            var rows = SelectedRpfRows_O1();
            ImGui.TextWrapped(RpfSelectionSummary_U19());
            ImGui.TextDisabled("Ctrl+click adds one, Shift+click a range, Ctrl+A everything listed.");
            ImGui.Spacing();
            ImGui.Separator();
            CountRpfRows_U19(rows, out int xmlable, out int convertible, out var convertPaths);

            if (ImGui.Button($"Extract {rows.Count:N0} items...", new Vector2(-1, 0))) RequestRpfExtractRows_U19 = rows;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Everything selected into one folder you pick. Files land flat, in their on-disk form;\n" +
                                 "folders and archives keep their layout underneath a folder of their own name.");
            if (xmlable > 0)
            {
                if (ImGui.Button($"Export {xmlable:N0} as XML...", new Vector2(-1, 0))) RequestRpfExportXmlRows_U19 = rows;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Each file that has an XML converter becomes <name>.xml in a folder you pick.\n" +
                                     "The rest are left out and counted.");
            }
            float halfX = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            if (ImGui.Button("Copy", new Vector2(halfX, 0))) ApplyRpf_O1(RpfEdit.Copy(rows));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copies the files for Paste into another folder or archive (edit mode).");
            ImGui.SameLine();
            if (ImGui.Button("Copy paths", new Vector2(halfX, 0))) CopyRpfPaths_U19(rows, false);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("One path per line onto the Windows clipboard.");
            if (convertible > 0)
            {
                if (ImGui.Button($"Convert {convertible:N0} XML file(s) to game files", new Vector2(-1, 0)))
                    RequestRpfDiskConvertMany_V52 = convertPaths;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Each <name>.ytyp.xml / .ydr.xml / ... becomes the real file beside it.");
            }

            bool searching = rpfSearchAll && rpfSearch.Trim().Length > 0;
            ImGui.Spacing();
            ImGui.BeginDisabled(!RpfEditMode || searching);
            if (DangerButton($"Delete {rows.Count:N0} items...", new Vector2(-1, 0))) AskDeleteRpf_O1();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(!RpfEditMode ? "Turn on Edit mode to delete."
                    : searching ? "Leave the search first - deleting works inside a folder."
                    : "Asks once, then deletes every selected item. No undo.");

            ImGui.Spacing();
            ImGui.Separator();
            DrawRpfFolderActions_N4();
        }

        private void DrawRpfMultiMenu_U19()
        {
            var rows = SelectedRpfRows_O1();
            bool searching = rpfSearchAll && rpfSearch.Trim().Length > 0;
            CountRpfRows_U19(rows, out int xmlable, out int convertible, out var convertPaths);
            ImGui.TextDisabled(RpfSelectionSummary_U19());
            ImGui.Separator();
            if (ImGui.MenuItem($"Extract {rows.Count:N0} items...")) RequestRpfExtractRows_U19 = rows;
            if (xmlable > 0 && ImGui.MenuItem($"Export {xmlable:N0} as XML...")) RequestRpfExportXmlRows_U19 = rows;
            if (convertible > 0 && ImGui.MenuItem($"Convert {convertible:N0} XML file(s) to game files")) RequestRpfDiskConvertMany_V52 = convertPaths;
            ImGui.Separator();
            if (ImGui.MenuItem("Copy")) ApplyRpf_O1(RpfEdit.Copy(rows));
            if (ImGui.MenuItem("Copy paths")) CopyRpfPaths_U19(rows, false);
            if (ImGui.MenuItem("Copy names")) CopyRpfPaths_U19(rows, true);
            ImGui.Separator();
            if (!RpfEditMode)
            {
                if (ImGui.MenuItem("Turn on edit mode...")) EnsureRpfWritable_U4("Editing an archive", () => { });
                return;
            }
            ImGui.BeginDisabled(searching);
            if (ImGui.MenuItem($"Delete {rows.Count:N0} items...")) AskDeleteRpf_O1();
            ImGui.EndDisabled();
        }

        private void AskDeleteRpfMany_U19()
        {
            var items = new List<RpfEdit.Item>();
            foreach (var r in SelectedRpfRows_O1())
            {
                var it = RpfEdit.ItemOf(r);
                if (it.Valid) items.Add(it);
            }
            if (items.Count == 0) return;
            int folders = 0;
            foreach (var it in items)
                if (it.IsFolder || it.Archive != null || it.Entry is RpfDirectoryEntry) folders++;
            var body = new StringBuilder();
            body.Append("Permanently delete ").Append(items.Count).Append(" items?\n");
            for (int i = 0; i < Math.Min(items.Count, 8); i++) body.Append("    ").Append(items[i].Name).Append('\n');
            if (items.Count > 8) body.Append("    ... and ").Append(items.Count - 8).Append(" more\n");
            if (folders > 0)
                body.Append(folders == 1 ? "One of them is a folder or archive - everything inside it goes too.\n"
                                         : folders + " of them are folders or archives - everything inside them goes too.\n");
            body.Append("\nThis cannot be undone.");
            AskRpf_O1("Confirm delete", body.ToString(), "Delete " + items.Count + " items",
                      () => ApplyRpfWrite_O1(() => DeleteRpfItems_U19(items)));
        }

        private RpfEdit.Result DeleteRpfItems_U19(List<RpfEdit.Item> items)
        {
            int done = 0, failed = 0;
            string firstError = null;
            foreach (var it in items)
            {
                var r = RpfEdit.Delete(RpfEditMode, Rpf, it);
                if (r.Ok) done++;
                else { failed++; firstError ??= r.Message; }
            }
            if (done == 0) return RpfEdit.Result.Fail("nothing deleted" + (firstError != null ? ": " + firstError : ""));
            return RpfEdit.Result.Done($"deleted {done:N0} item{(done == 1 ? "" : "s")}" +
                                       (failed > 0 ? $" - {failed:N0} failed ({firstError})" : ""));
        }

        public RpfEdit.Result DeleteSelectedRpfRowsForTest_U19()
        {
            var items = new List<RpfEdit.Item>();
            foreach (var r in SelectedRpfRows_O1())
            {
                var it = RpfEdit.ItemOf(r);
                if (it.Valid) items.Add(it);
            }
            var res = DeleteRpfItems_U19(items);
            ClearRpfSelection_O1();
            Rpf.Invalidate();
            return res;
        }

        public List<RpfExplorer.Row> SelectedRpfRowsForTest_U19() => SelectedRpfRows_O1();
    }
}
