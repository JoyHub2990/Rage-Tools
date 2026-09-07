namespace RageLightEditor.Editor
{
    public partial class MloCreatorPanel
    {
        public bool PropFocused_P2 => FocusKind == 2 && Session != null && SelectedEntity >= 0 && SelectedEntity < Session.Entities.Count;

        public string BoxNote_P2 = "";
    }
}

