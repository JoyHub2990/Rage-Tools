using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool o2Dumped;

        partial void OnWorldTick_O2()
        {
            if (!worldBuilt || !panel.WorldMode) return;
            if (o2Dumped || screenshotPath == null || worldWarmup < 455) return;
            var s = Environment.GetEnvironmentVariable("RLE_O2DUMP");
            if (string.IsNullOrEmpty(s) ||
                !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) || radius <= 0) return;
            o2Dumped = true;
            Console.Write(InteriorAmbientReport_O2(camera.Position, radius));
        }

        private string InteriorAmbientReport_O2(Vector3 at, float radius)
        {
            var sb = new StringBuilder();
            var res = worldRender?.InteriorAmbient_L2;
            sb.AppendLine($"O2DUMP cam {at.X:0.0},{at.Y:0.0},{at.Z:0.0} radius {radius} hour {timecycle?.CurrentHour ?? -1:0.0} resolver {(res == null ? "none" : res.Ready ? "ready" : "NOT READY")}");
            if (timecycle != null)
            {
                sb.AppendLine($"  CYCLE artInt up {timecycle.Peek("light_artificial_int_up_col_r"):0.###},{timecycle.Peek("light_artificial_int_up_col_g"):0.###},{timecycle.Peek("light_artificial_int_up_col_b"):0.###} x{timecycle.Peek("light_artificial_int_up_intensity"):0.###}" +
                              $" dn {timecycle.Peek("light_artificial_int_down_col_r"):0.###},{timecycle.Peek("light_artificial_int_down_col_g"):0.###},{timecycle.Peek("light_artificial_int_down_col_b"):0.###} x{timecycle.Peek("light_artificial_int_down_intensity"):0.###}" +
                              $" | ext up {timecycle.Peek("light_artificial_ext_up_col_r"):0.###},{timecycle.Peek("light_artificial_ext_up_col_g"):0.###},{timecycle.Peek("light_artificial_ext_up_col_b"):0.###} x{timecycle.Peek("light_artificial_ext_up_intensity"):0.###}" +
                              $" | natural up x{timecycle.Peek("light_natural_amb_up_intensity"):0.####} dn x{timecycle.Peek("light_natural_amb_down_intensity"):0.####}");
                float Raw(string n) => timecycle.Current == null ? -1f : timecycle.Current.Get(n, timecycle.CurrentSampleIndex, timecycle.CurrentSampleBlend, -1f);
                sb.AppendLine($"  RAW   artInt up {Raw("light_artificial_int_up_col_r"):0.###},{Raw("light_artificial_int_up_col_g"):0.###},{Raw("light_artificial_int_up_col_b"):0.###} x{Raw("light_artificial_int_up_intensity"):0.###}" +
                              $" dn {Raw("light_artificial_int_down_col_r"):0.###},{Raw("light_artificial_int_down_col_g"):0.###},{Raw("light_artificial_int_down_col_b"):0.###} x{Raw("light_artificial_int_down_intensity"):0.###}" +
                              $" | natMult {Raw("natural_ambient_multiplier"):0.###} artIntMult {Raw("artificial_int_ambient_multiplier"):0.###}" +
                              $" (has: {timecycle.Has("light_artificial_int_up_col_r")}/{timecycle.Has("natural_ambient_multiplier")}/{timecycle.Has("artificial_int_ambient_multiplier")})");
            }
            int shown = 0;
            foreach (var shell in World.InteriorsEmitted)
            {
                var arch = shell?.Archetype as MloArchetype;
                if (arch?.rooms == null) continue;
                if ((shell.Position - at).Length() > radius) continue;
                if (++shown > 4) { sb.AppendLine("O2DUMP (more interiors in range - narrow the radius)"); break; }
                sb.AppendLine($"  INTERIOR {arch.Name} at {shell.Position.X:0.0},{shell.Position.Y:0.0},{shell.Position.Z:0.0} rooms {arch.rooms.Length} archFlags {arch._BaseArchetypeDef.flags}");
                for (int r = 0; r < arch.rooms.Length; r++)
                {
                    var rm = arch.rooms[r];
                    if (rm == null) continue;
                    var d = rm._Data;
                    var a = res != null ? res.Resolve(arch, r) : InteriorAmbientResolver.Exterior;
                    sb.AppendLine($"    room {r,2} '{rm.RoomName}' tc '{d.timecycleName}' tc2 '{d.secondaryTimecycleName}' blend {d.blend:0.##} flags {d.flags} objs {(rm.AttachedObjects?.Length ?? 0)}" +
                                  $" -> nat {a.NaturalScale:0.00} art {a.ArtificialScale:0.00} int {a.InInterior:0} up {a.ArtIntAmbUp.X:0.###},{a.ArtIntAmbUp.Y:0.###},{a.ArtIntAmbUp.Z:0.###} dn {a.ArtIntAmbDown.X:0.###},{a.ArtIntAmbDown.Y:0.###},{a.ArtIntAmbDown.Z:0.###} mod '{a.Modifier}'");
                }
            }
            if (shown == 0) sb.AppendLine("O2DUMP no interior in range");
            if (worldRender != null)
            {
                int zero = 0, ungraded = 0, ext = 0, inter = 0, tot = 0;
                foreach (var m in worldRender.Model.Meshes)
                {
                    var o = worldRender.OwnerOf(m);
                    if (o == null || (o.MloParent == null && o.MloInstance == null)) continue;
                    if ((o.Position - at).Length() > radius) continue;
                    tot++;
                    bool sentinel = m.NaturalAmbientScale < 0.0f || m.ArtificialAmbientScale < 0.0f;
                    if (m.InInterior <= 0.5f) ext++;
                    else if (sentinel) ungraded++;
                    else inter++;
                    if (!sentinel && m.ArtificialAmbientScale <= 0.001f && m.NaturalAmbientScale <= 0.001f) zero++;
                }
                sb.AppendLine($"  MESHES in range {tot}: graded room {inter}, ungraded (cycle default) {ungraded}, exterior rule {ext}, graded to nothing {zero}");
            }
            return sb.ToString();
        }
    }
}

