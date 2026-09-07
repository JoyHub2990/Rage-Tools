namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool FrameWorldSelection_M3()
        {
            if (!panel.WorldMode) return false;
            if (WorldEdit.Selected != null || WorldEdit.Selection.HasValue) panel.RequestFrameWorldSelection_M3();
            return true;
        }
    }
}

