namespace RageLightEditor.Editor
{
    public partial class AppSettings
    {
        public string MloCreatorLastProject { get; set; }
        public float MloCreatorExplorerWidth { get; set; } = 300.0f;
        public bool MloCreatorDetached { get; set; }
        public int[] MloCreatorDetachedBounds { get; set; }
        public bool MloCreatorDetachedMaximized { get; set; }
        public string MloCreatorDetachedScreen { get; set; }
        public int MloCreatorBridgePort { get; set; }
    }
}

