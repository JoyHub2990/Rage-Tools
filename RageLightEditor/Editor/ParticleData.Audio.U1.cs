namespace RageLightEditor.Editor
{
    public partial class PtfxDocument
    {
        public PtfxAudioDoc_U1 Audio_U1 = new PtfxAudioDoc_U1();

        partial void OnSaved_U1(string path) => Audio_U1?.Save(path);

        partial void OnLoaded_U1(string path) => Audio_U1 = PtfxAudioDoc_U1.Load(path);
    }
}

