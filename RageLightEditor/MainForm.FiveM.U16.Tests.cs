using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_FiveM_U16(Action<string, bool, string> check)
        {
            try
            {
                var l = new LightAttributes { Type = LightType.Point, ColorR = 255, ColorG = 255, ColorB = 255, Intensity = 4f, Falloff = 6f, TimeFlags = Scene.AllHoursTimeFlags };
                var defs = new[] { new WorldLights.LightDef { L = l, Pos = new Vector3(0, 0, 2), Dir = new Vector3(0, 0, -1), Tan = new Vector3(1, 0, 0) } };
                var places = new List<(Vector3 pos, Quaternion rot, Vector3 scale)>
                {
                    (new Vector3(10, 0, 0), Quaternion.Identity, Vector3.One),
                    (new Vector3(20, 0, 0), Quaternion.Identity, Vector3.One),
                    (new Vector3(30, 0, 0), Quaternion.Identity, Vector3.One),
                };
                var json = WorldLightsJsonFor_U16("prop_lamp", places, defs);
                check("fivem world lights: every copy of the prop gets its own entry", json.Contains("\"key\":\"prop_lamp#0\"") && json.Contains("\"key\":\"prop_lamp#2\"") && json.Split("\"model\":\"prop_lamp\"").Length == 4, json.Length > 300 ? json.Substring(0, 300) : json);
                check("fivem world lights: each entry carries its own place", json.Contains("\"pos\":[10,0,0]") && json.Contains("\"pos\":[30,0,0]"), "");

                FitRect_U16(400, 1000, 9f / 16f, out int w1, out int h1);
                FitRect_U16(400, 100, 9f / 16f, out int w2, out int h2);
                check("fivem view: the picture fits the width when the window is tall and the height when it is short", w1 == 400 && h1 == 225 && h2 == 100 && w2 == 178, $"{w1}x{h1}  {w2}x{h2}");

                var s = new ShaderFX();
                s.ParametersList = new ShaderParametersBlock
                {
                    Parameters = new[]
                    {
                        new ShaderParameter { DataType = 1, Data = new Vector4(0.5f, 0.25f, 1f, 2f) },
                        new ShaderParameter { DataType = 0 },
                        new ShaderParameter { DataType = 2, Data = new[] { new Vector4(1, 2, 3, 4), new Vector4(5, 6, 7, 8) } },
                    },
                    Hashes = new[] { (MetaName)111u, (MetaName)222u, (MetaName)333u },
                };
                var lines = AsiLines_U16("prop_x", "", 3, s).ToList();
                check("fivem plugin: a vector parameter becomes one set line and textures are skipped",
                      lines.Count == 2 && lines[0].line == "set prop_x - 3 111 1 0.5 0.25 1 2" && lines[0].key == "prop_x/3/111", lines.Count > 0 ? lines[0].line : "none");
                check("fivem plugin: an array parameter carries every float4", lines.Count == 2 && lines[1].line == "set prop_x - 3 333 2 1 2 3 4 5 6 7 8", lines.Count > 1 ? lines[1].line : "none");
                var dl = AsiLines_U16("thing", "my_dict", 0, s).First().line;
                check("fivem plugin: a drawable dictionary name rides along", dl.StartsWith("set thing my_dict 0 "), dl);

                check("fivem plugin: the plugin binary is embedded in this build",
                      System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("fivem/RageToolsLive.asi") != null, "");
                check("fivem resource: the resource version the tool expects is stamped into the installer", FiveMResourceVersion_U16 >= 4, FiveMResourceVersion_U16.ToString());
            }
            catch (Exception ex) { check("fivem u16: no exception", false, ex.ToString()); }
        }
    }
}
