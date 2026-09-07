using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_R3(Action<string, bool, string> check);

        partial void SeqTest_R3(Action<string, bool, string> check)
        {
            NavCellsTest_R3(check);
            ModelViewCameraTest_R3(check);
            RpfTextEditTest_R3(check);
        }
    }
}

