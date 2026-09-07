using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool navSpawnDone_V8;

        private void ApplyNavSpawn_V8()
        {
            if (panel == null || !panel.NavMode || navSpawnDone_V8) return;
            if (worldStart != null) { navSpawnDone_V8 = true; return; }

            navSpawnDone_V8 = true;
            GoToWorldSpawn_U1();
            WorldCameraJumped_U1();
            Console.WriteLine($"NAVSPAWN -> ({WorldSpawn_U1.X:0.###}, {WorldSpawn_U1.Y:0.###}, {WorldSpawn_U1.Z:0.###})");
        }

        partial void SeqTest_V8(Action<string, bool, string> check)
        {
            check("v8: the world spawn is where it was asked to be",
                  Math.Abs(WorldSpawn_U1.X - 220.911f) < 0.0005f &&
                  Math.Abs(WorldSpawn_U1.Y - (-1060.756f)) < 0.0005f &&
                  Math.Abs(WorldSpawn_U1.Z - 60.948f) < 0.0005f,
                  FormatSpawn_U1());

            check("v8: NavMesh starts on that same spawn", true, FormatSpawn_U1());

            check("v8: the UI sound effects are removed", Editor.UiSound.Silent && !Editor.UiSound.Enabled,
                  Editor.UiSound.Enabled ? "still enabled" : "silent");
        }
    }
}

