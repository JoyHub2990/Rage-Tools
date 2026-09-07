namespace RageLightEditor.Editor
{
    public static class RpfWriteGate_U4
    {
        public const string NeedFolder = "Walk into a folder or an archive first - there is nowhere to write yet.";
        public const string NeedSearchOff = "Finish the search first - a search list is not a folder to write into.";
        public const string NeedEditMode = "Edit mode is off. Click this and it will offer to turn it on.";

        public static bool Enabled(bool targetValid, bool searching) => targetValid && !searching;

        public static bool NeedsAsk(bool editMode, bool targetValid, bool searching) =>
            Enabled(targetValid, searching) && !editMode;

        public static string Why(bool editMode, bool targetValid, bool searching)
        {
            if (searching) return NeedSearchOff;
            if (!targetValid) return NeedFolder;
            if (!editMode) return NeedEditMode;
            return null;
        }
    }
}
