using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private static readonly Vector4 TreeExternal_V43 = new Vector4(0.36f, 0.72f, 0.86f, 1.0f);

        private void RpfTreeGlyph_V43(RpfExplorer.Node n)
        {
            float h = ImGui.GetTextLineHeight();
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            p.Y += 1;

            if (n.Archive != null)
            {
                var c = ArchiveWorkspaceColour;
                uint col = ImGui.GetColorU32(c);
                uint faint = ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, 0.28f));
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + 1), new Vector2(p.X + h - 1, p.Y + h - 1), faint, 2);
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + h * 0.38f), new Vector2(p.X + h - 1, p.Y + h * 0.38f + 2), col);
                dl.AddRect(new Vector2(p.X + 1, p.Y + 1), new Vector2(p.X + h - 1, p.Y + h - 1), col, 2);
            }
            else
            {
                bool external = false;
                for (var r = n; r != null; r = r.Parent)
                    if (r.IsRoot) { external = !ReferenceEquals(r, Rpf.GameRoot); break; }
                uint col = ImGui.GetColorU32(external ? TreeExternal_V43 : GlyphFolder_V42);
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + 2), new Vector2(p.X + h * 0.5f, p.Y + 6), col, 1.5f);
                dl.AddRectFilled(new Vector2(p.X + 1, p.Y + 4), new Vector2(p.X + h - 1, p.Y + h - 1), col, 2);
            }
            ImGui.Dummy(new Vector2(h + 2, h));
        }

        public RpfExplorer.Row? RequestRpfDragOut_V55;
        private bool rpfDragOutArmed_V55;

        internal void ResetRpfDragOut_V55()
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) rpfDragOutArmed_V55 = false;
        }

        internal void RpfDragOutHook_V55(in RpfExplorer.Row r)
        {
            if (rpfDragOutArmed_V55) return;
            if (r.IsFolder && !r.IsFs) return;
            if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(ImGuiMouseButton.Left, 14f)) return;
            rpfDragOutArmed_V55 = true;
            if (RpfMultiSelected_U19 && InMulti_U19(r)) RequestRpfDragOutMany_U19 = SelectedRpfRows_O1();
            else RequestRpfDragOut_V55 = r;
        }

        private void RpfCreateMenuItems_V46()
        {
            bool searching = rpfSearchAll && rpfSearch.Trim().Length > 0;
            var t = RpfTarget_O1();
            bool can = RpfEditMode && t.Valid && !searching;
            ImGui.BeginDisabled(!can);
            if (ImGui.MenuItem("New folder...")) OpenRpfPrompt_O1(RpfPromptKind.NewFolder, "folder");
            if (ImGui.MenuItem("New RPF...")) OpenRpfPrompt_O1(RpfPromptKind.NewArchive, "new.rpf");
            ImGui.EndDisabled();
            if (!can && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(!RpfEditMode ? "Turn on Edit mode first."
                    : searching ? "Leave the search first - a search result is not a folder."
                    : "Walk into a folder first.");
        }

        private void DrawRpfBackgroundMenu_V46()
        {
            if (!ImGui.BeginPopupContextWindow("##rpfbgctx",
                    ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems)) return;
            RpfCreateMenuItems_V46();
            ImGui.Separator();
            ImGui.BeginDisabled(!RpfEditMode);
            if (ImGui.MenuItem("Paste"))
            {
                if (RpfEdit.Clipboard.Count == 0) RequestRpfPasteOs = true;
                else ApplyRpfWrite_O1(() => RpfEdit.Paste(RpfEditMode, Rpf, RpfTarget_O1(), null));
            }
            ImGui.EndDisabled();
            ImGui.EndPopup();
        }

        public bool DropOnRpfTree_V43(IReadOnlyList<string> paths)
        {
            int added = 0;
            string last = null;
            var sweep = new MainForm.XmlSweep_V53();
            foreach (var pth in paths)
            {
                if (string.IsNullOrWhiteSpace(pth) || !Directory.Exists(pth)) continue;
                if (!Rpf.AddRootFolder(pth)) continue;
                added++;
                last = pth;
                var s = MainForm.SweepConvertXmls_V53(pth);
                sweep.Converted += s.Converted;
                sweep.UpToDate += s.UpToDate;
                sweep.Failed += s.Failed;
                sweep.Remaining += s.Remaining;
                sweep.FirstError ??= s.FirstError;
            }
            if (added > 0)
            {
                if (sweep.Converted > 0) Rpf.RefreshCurrent_O1();
                RpfStatus = (added == 1
                    ? "opened " + Path.GetFileName(last.TrimEnd('\\', '/')) + " in the tree"
                    : $"opened {added} folders in the tree") + MainForm.SweepNote_V53(sweep);
                UiSound.Result(true);
            }
            else
            {
                RpfStatus = "drop a FOLDER on the tree to open it beside the install";
                UiSound.Blocked();
            }
            return true;
        }
    }
}
