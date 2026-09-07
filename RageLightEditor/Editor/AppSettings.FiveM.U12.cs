namespace RageLightEditor.Editor
{
    public partial class AppSettings
    {
        public int FiveMPort { get; set; } = FiveMBridge.DefaultPort;
        public string FiveMResourcesFolder { get; set; } = "";
        public bool FiveMAutoStart { get; set; } = true;
        public bool FiveMLive { get; set; }
        public int FiveMFollow { get; set; }
        public bool FiveMRestartAfterSave { get; set; } = true;
        public bool FiveMAnyInterface { get; set; }
    }
}
