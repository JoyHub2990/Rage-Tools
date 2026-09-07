using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public int WorldRevertAll_V21() => WorldRevertAll_V22();

        private void SeqTest_WorldReset_V21(Action<string, bool, string> check)
        {
            var y = new YmapFile();
            y.Name = "v21_reset_probe.ymap";
            WorldEdit.MarkDirty(y);
            check("v21 reset: a touched .ymap counts as edited", WorldEdit.IsDirty(y),
                  WorldEdit.DirtyCount + " edited");

            int before = WorldEdit.DirtyCount;
            WorldRevertAll_V21();
            check("v21 reset: resetting the world clears every pending edit",
                  WorldEdit.DirtyCount == 0 && !WorldEdit.IsDirty(y),
                  $"{before} -> {WorldEdit.DirtyCount}");
            check("v21 reset: ...and nothing stays selected from the world that was thrown away",
                  !WorldEdit.Selection.HasValue, "deselected");
            check("v21 reset: ...and the panel stops saying there is work to save",
                  panel == null || panel.WorldDirtyCount == 0,
                  (panel?.WorldDirtyCount ?? 0).ToString());
        }
    }
}

