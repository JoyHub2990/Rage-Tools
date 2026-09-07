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
        private void SeqTest_U18(Action<string, bool, string> check)
        {
            try
            {
                var dd = new Drawable { BoundingCenter = new Vector3(1, 2, 3), BoundingBoxMin = new Vector3(0, 1, 1), BoundingBoxMax = new Vector3(2, 3, 4.5f) };
                check("u18 add light: a fresh drawable has no lights", WorldLights.LightCount_U18(dd) == 0, WorldLights.LightCount_U18(dd).ToString());
                var la = WorldLights.NewLight_U18(dd, 2);
                check("u18 add light: the new light sits on top of the model, centred",
                      la.Type == LightType.Spot && Math.Abs(la.Position.X - 1) < 1e-4f && Math.Abs(la.Position.Y - 2) < 1e-4f && Math.Abs(la.Position.Z - 4.65f) < 1e-4f && la.ConeOuterAngle > 0 && la.TimeFlags == Scene.AllHoursTimeFlags,
                      la.Position.ToString());
                int at = WorldLights.AppendLight_U18(dd, la);
                check("u18 add light: appending creates the list and returns index 0", at == 0 && WorldLights.LightCount_U18(dd) == 1 && ReferenceEquals(dd.LightAttributes.data_items[0], la), at.ToString());
                var lb = WorldLights.NewLight_U18(dd, 1);
                check("u18 add light: a second one lands at index 1", WorldLights.AppendLight_U18(dd, lb) == 1 && WorldLights.LightCount_U18(dd) == 2, "");
                check("u18 add light: removing takes only that one away", WorldLights.RemoveLight_U18(dd, la) && WorldLights.LightCount_U18(dd) == 1 && ReferenceEquals(dd.LightAttributes.data_items[0], lb), "");
                check("u18 add light: undo puts it back where it was", WorldLights.InsertLightAt_U18(dd, la, 0) == 0 && ReferenceEquals(dd.LightAttributes.data_items[0], la) && ReferenceEquals(dd.LightAttributes.data_items[1], lb), "");
                check("u18 add light: removing a light that is not there is refused", !WorldLights.RemoveLight_U18(dd, new LightAttributes()) && WorldLights.LightCount_U18(dd) == 2, "");

                var frag = new FragType();
                var fd = new FragDrawable { OwnerFragment = frag };
                check("u18 add light: a fragment's lights live on the fragment", WorldLights.AppendLight_U18(fd, WorldLights.NewLight_U18(fd, 4)) == 0 && frag.LightAttributes?.data_items?.Length == 1, "");
                check("u18 add light: a fragment drawable without its owner cannot take one", WorldLights.AppendLight_U18(new FragDrawable(), la) < 0, "");

                check("u18 fog: outside a room the fog is untouched", InteriorFogFactor_U18(false, false) == 1.0f, "");
                check("u18 fog: inside a room it is cut hard", InteriorFogFactor_U18(true, false) < 0.15f, InteriorFogFactor_U18(true, false).ToString());
                check("u18 fog: a modifier that sets its own fog wins", InteriorFogFactor_U18(true, true) == 1.0f, "");

                var list = new List<LightPresets_V20.Preset_V20> { new LightPresets_V20.Preset_V20 { Name = "mine", Type = 1 } };
                int added = LightPresetsBuiltin_V21.Ensure(list);
                check("u18 presets: the built-in library is added once", added >= 20 && list.Count == added + 1 && list.Count(p => p.Builtin) == added, $"{added} added, {list.Count} total");
                check("u18 presets: ...and not again", LightPresetsBuiltin_V21.Ensure(list) == 0 && list.Count == added + 1, list.Count.ToString());
                check("u18 presets: your own stay first", list[0].Name == "mine" && !list[0].Builtin, list[0].Name);
                check("u18 presets: the library has point, spot and capsule lights and a flashing one",
                      list.Any(p => p.Builtin && p.Type == 1) && list.Any(p => p.Builtin && p.Type == 2) && list.Any(p => p.Builtin && p.Type == 4 && p.ExtentX > 0) && list.Any(p => p.Builtin && p.Flashiness != 0), "");
                check("u18 presets: names are unique", list.Select(p => p.Name.ToLowerInvariant()).Distinct().Count() == list.Count, "");

                var tube = list.First(p => p.Builtin && p.Type == 4);
                var target = new LightAttributes { Type = LightType.Point, Extent = new Vector3(1, 1, 1) };
                LightPresets_V20.Apply_V20(tube, target);
                check("u18 presets: a capsule preset sets the type and the length", target.Type == LightType.Capsule && Math.Abs(target.Extent.X - tube.ExtentX) < 1e-4f, $"{target.Type} {target.Extent}");
                var bulb = list.First(p => p.Builtin && p.Type == 1);
                LightPresets_V20.Apply_V20(bulb, target);
                check("u18 presets: a point preset leaves a capsule's length alone", target.Type == LightType.Point && Math.Abs(target.Extent.X - tube.ExtentX) < 1e-4f, target.Extent.ToString());
                var rec = LightPresets_V20.From_V20(new LightAttributes { Type = LightType.Capsule, Extent = new Vector3(2.5f, 1, 1) }, "cap");
                check("u18 presets: saving a capsule keeps its length", Math.Abs(rec.ExtentX - 2.5f) < 1e-4f && !rec.Builtin, rec.ExtentX.ToString());

                LightPresets_V20.Load_V20();
                var candle = LightPresets_V20.Find_V20("Candle");
                int n = LightPresets_V20.All.Count;
                check("u18 presets: the loaded library carries the built-ins", candle != null && candle.Builtin && n >= 21, n.ToString());
                check("u18 presets: a built-in cannot be deleted", !LightPresets_V20.Remove_V20("Candle") && LightPresets_V20.All.Count == n, LightPresets_V20.All.Count.ToString());
                LightPresets_V20.Put_V20(LightPresets_V20.From_V20(target, "u18 probe"));
                check("u18 presets: your own can", LightPresets_V20.Find_V20("u18 probe") != null && LightPresets_V20.Remove_V20("u18 probe") && LightPresets_V20.All.Count == n, LightPresets_V20.All.Count.ToString());

                check("u18 fivem: a single prop is always sent", SendPlacedFile_U18(1, true, false) && SendPlacedFile_U18(0, false, false), "");
                check("u18 fivem: an untouched prop of an imported interior is not", !SendPlacedFile_U18(12, true, false), "");
                check("u18 fivem: a prop whose light you selected is", SendPlacedFile_U18(12, true, true), "");
            }
            catch (Exception ex) { check("u18: no exception", false, ex.ToString()); }
        }
    }
}
