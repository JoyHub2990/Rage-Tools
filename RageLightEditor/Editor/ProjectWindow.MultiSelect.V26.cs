using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class ProjectWindow
    {
        public readonly List<YmapEntityDef> MultiSelection_V26 = new List<YmapEntityDef>();
        public bool MultiSelectionChanged_V26;

        private YmapEntityDef anchor_V26;
        private YmapFile anchorYmap_V26;

        public bool IsMultiSelected_V26(YmapEntityDef e) => e != null && MultiSelection_V26.Contains(e);

        public bool ClickEntity_V26(YmapEntityDef e, YmapFile owner, YmapEntityDef[] siblings, int index)
        {
            if (e == null) return false;
            var io = ImGui.GetIO();

            if (io.KeyCtrl)
            {
                if (!MultiSelection_V26.Remove(e)) MultiSelection_V26.Add(e);
                Select(MultiSelection_V26.Count > 0 ? (object)MultiSelection_V26[0] : null);
                anchor_V26 = e; anchorYmap_V26 = owner;
                MultiSelectionChanged_V26 = true;
                return true;
            }

            if (io.KeyShift && anchor_V26 != null && ReferenceEquals(anchorYmap_V26, owner) && siblings != null)
            {
                int a = Array.IndexOf(siblings, anchor_V26);
                int b = index;
                if (a >= 0 && b >= 0)
                {
                    MultiSelection_V26.Clear();
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                        if (siblings[i] != null) MultiSelection_V26.Add(siblings[i]);
                    Select(MultiSelection_V26.Count > 0 ? (object)MultiSelection_V26[0] : null);
                    MultiSelectionChanged_V26 = true;
                    return true;
                }
            }

            MultiSelection_V26.Clear();
            MultiSelection_V26.Add(e);
            anchor_V26 = e; anchorYmap_V26 = owner;
            MultiSelectionChanged_V26 = true;
            return false;
        }

        public void PruneMultiSelection_V26()
        {
            for (int i = MultiSelection_V26.Count - 1; i >= 0; i--)
                if (MultiSelection_V26[i]?.Ymap == null) MultiSelection_V26.RemoveAt(i);
        }

        public void ClearMultiSelection_V26()
        {
            if (MultiSelection_V26.Count == 0) return;
            MultiSelection_V26.Clear();
            anchor_V26 = null; anchorYmap_V26 = null;
            MultiSelectionChanged_V26 = true;
        }
    }
}

