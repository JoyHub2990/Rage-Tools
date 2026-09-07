using System;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void DeleteKey_T4(ref bool handled)
        {
            if (panel == null) return;
            if (panel.ArchiveMode) { handled = panel.DeleteSelectedRpf_T4(); return; }
            if (panel.CineMode) { handled = panel.DeleteSelectedShot_T4(); return; }
        }
    }
}

