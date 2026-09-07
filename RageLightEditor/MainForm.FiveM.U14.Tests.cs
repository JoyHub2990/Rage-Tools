using System;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_FiveM_U14(Action<string, bool, string> check)
        {
            try
            {
                check("fivem view: only the FiveM game window qualifies",
                      GameThumbnail_U14.AcceptWindow("grcWindow", "FiveM® - My Server", "FiveM_b3095_GTAProcess") &&
                      !GameThumbnail_U14.AcceptWindow("Chrome_WidgetWin_1", "FiveM", "FiveM") &&
                      !GameThumbnail_U14.AcceptWindow("grcWindow", "Grand Theft Auto V", "GTA5") &&
                      !GameThumbnail_U14.AcceptWindow("ConsoleWindowClass", "FiveM server", "cmd"), "");

                var thumb = new GameThumbnail_U14();
                bool reg = thumb.Register(Handle, Handle, "self");
                thumb.Update(10, 10, 100, 60, true);
                thumb.Unregister();
                check("fivem view: registering and releasing a compositor thumbnail does not throw", !thumb.Registered, reg ? "registered" : $"refused 0x{thumb.LastError:X8}");

                var q = Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(90));
                var pj = PlaceJson_U14(new Vector3(1, 2, 3), q, Vector3.Zero);
                check("fivem place: a placement carries position, quaternion and a sane scale",
                      pj.Contains("\"pos\":[1,2,3]") && pj.Contains("\"rot\":[0,0,0.70711,0.70711]") && pj.Contains("\"scale\":[1,1,1]"), pj);
                var m = Matrix.Scaling(2.0f) * Matrix.RotationQuaternion(q) * Matrix.Translation(5, 6, 7);
                var pm = PlaceJson_U14(m);
                check("fivem place: a placement matrix decomposes the same way", pm.Contains("\"pos\":[5,6,7]") && pm.Contains("\"scale\":[2,2,2]") && pm.Contains("0.70711"), pm);

                var l = new LightAttributes { Type = LightType.Point, ColorR = 10, ColorG = 20, ColorB = 30, Intensity = 4, Falloff = 6, TimeFlags = Scene.AllHoursTimeFlags };
                var corona = new LightAttributes { Type = LightType.Point, Flags = LightDefs.FlagCoronaOnly, Intensity = 1, Falloff = 1 };
                var defs = new[]
                {
                    new WorldLights.LightDef { L = l, Pos = new Vector3(0, 0, 3), Dir = new Vector3(0, 0, -1), Tan = Vector3.UnitX },
                    new WorldLights.LightDef { L = corona, Pos = Vector3.Zero, Dir = new Vector3(0, 0, -1), Tan = Vector3.UnitX },
                };
                var wj = WorldPropJson_U14("prop_streetlight_01", new Vector3(100, 200, 30), Quaternion.Identity, Vector3.One, defs);
                check("fivem world: a placed prop's lights go out with its placement, coronas left out",
                      wj.Contains("\"model\":\"prop_streetlight_01\"") && wj.Contains("\"place\":{\"pos\":[100,200,30]") && wj.Contains("\"pos\":[0,0,3]") && wj.Split("\"kind\"").Length == 2, wj);
            }
            catch (Exception ex) { check("fivem u14: no exception", false, ex.ToString()); }
        }
    }
}
