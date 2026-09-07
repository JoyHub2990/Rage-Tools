using System;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_FiveM_U15(Action<string, bool, string> check)
        {
            try
            {
                var q = Quaternion.RotationAxis(Vector3.UnitZ, 0.5f);
                var same = EntityJson_U15(3, "prop_bench_01a", -12345, false, new Vector3(1, 2, 3), q, true, new Vector3(1, 2, 3), q, Vector3.One);
                check("fivem world: a selected prop that has not moved is reported in place", same.Contains("\"moved\":false") && same.Contains("\"orig\":{") && same.Contains("\"place\":{") && same.Contains("\"hash\":-12345"), same);
                var moved = EntityJson_U15(3, "prop_bench_01a", -12345, false, new Vector3(1, 2, 3), q, true, new Vector3(4, 2, 3), q, Vector3.One);
                check("fivem world: a moved prop carries its old and new place", moved.Contains("\"moved\":true") && moved.Contains("\"orig\":{\"pos\":[1,2,3]") && moved.Contains("\"place\":{\"pos\":[4,2,3]"), moved);
                var turned = EntityJson_U15(3, "prop_bench_01a", -12345, false, new Vector3(1, 2, 3), q, true, new Vector3(1, 2, 3), Quaternion.RotationAxis(Vector3.UnitZ, 1.5f), Vector3.One);
                check("fivem world: turning a prop counts as moving it", turned.Contains("\"moved\":true"), turned);
                var gone = EntityJson_U15(3, "prop_bench_01a", -12345, false, new Vector3(1, 2, 3), q, false, new Vector3(1, 2, 3), q, Vector3.One);
                check("fivem world: a deleted prop keeps its original place and loses the new one", gone.Contains("\"place\":null") && gone.Contains("\"orig\":{"), gone);
                var copy = EntityJson_U15(4, "prop_bench_01a", -12345, true, new Vector3(1, 2, 3), q, true, new Vector3(1, 2, 3), q, Vector3.One);
                check("fivem world: a duplicate never hides the prop it was copied from", copy.Contains("\"orig\":null") && copy.Contains("\"moved\":true"), copy);
                check("fivem world: a missing entity is not present", !EntityPresent_U15(null), "");
                check("fivem world: two rotations a hair apart count as the same", SameRotation_U15(q, Quaternion.RotationAxis(Vector3.UnitZ, 0.50001f)) && !SameRotation_U15(q, Quaternion.RotationAxis(Vector3.UnitZ, 0.6f)), "");

                check("fivem view: the game process is FiveM's GTAProcess", GameThumbnail_U14.IsGameProcess("FiveM_b3095_GTAProcess") && GameThumbnail_U14.IsGameProcess("FiveM_GTAProcess"), "");
                check("fivem view: a console, the launcher or another game are not the game window",
                      !GameThumbnail_U14.IsGameProcess("cmd") && !GameThumbnail_U14.IsGameProcess("FiveM") && !GameThumbnail_U14.IsGameProcess("GTA5") && !GameThumbnail_U14.IsGameProcess("WindowsTerminal"), "");
                check("fivem view: a console window is never picked even when its title says FiveM", !GameThumbnail_U14.AcceptWindow("ConsoleWindowClass", "FiveM server", "cmd"), "");
                check("fivem view: the FiveM game window is picked by class and process", GameThumbnail_U14.AcceptWindow("grcWindow", "FiveM® - my server", "FiveM_b3095_GTAProcess"), "");
                check("fivem view: another grcWindow (single player) is not picked", !GameThumbnail_U14.AcceptWindow("grcWindow", "Grand Theft Auto V", "GTA5"), "");
            }
            catch (Exception ex) { check("fivem u15: no exception", false, ex.ToString()); }
        }
    }
}
