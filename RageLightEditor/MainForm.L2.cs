using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool l2Dumped;
        private int l2ModifiersSeen = -1;

        private InteriorAmbientResolver l2Resolver;

        partial void OnTick_L2()
        {
            if (timecycle == null) return;
            l2Resolver ??= new InteriorAmbientResolver(timecycle);
            if (worldRender != null && worldRender.InteriorAmbient_L2 == null) worldRender.InteriorAmbient_L2 = l2Resolver;
            if (mloImporter != null && mloImporter.InteriorAmbient_L2 == null) mloImporter.InteriorAmbient_L2 = l2Resolver;
        }

        partial void OnWorldTick_L2()
        {
            if (!worldBuilt || !panel.WorldMode) return;
            if (worldRender.InteriorAmbient_L2 == null) return;
            int mods = timecycle?.Modifiers.Count ?? 0;
            if (mods != l2ModifiersSeen || worldRender.InteriorAmbientStampPending_L2)
            {
                l2ModifiersSeen = mods;
                worldRender.InteriorAmbient_L2.ClearCache();
                if (mods > 0) worldRender.RestampInteriorAmbient_L2();
            }
            if (!l2Dumped && screenshotPath != null && worldWarmup >= 455 &&
                float.TryParse(Environment.GetEnvironmentVariable("RLE_L2DUMP"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float radius) && radius > 0)
            {
                l2Dumped = true;
                Console.WriteLine(InteriorPropReport_L2(camera.Position, radius));
            }
        }

        private static string MeshVertexColourStats_L2(RenderMesh m, out bool hasColour)
        {
            hasColour = false;
            try
            {
                var vdata = m.Geometry?.VertexData;
                if (vdata?.Info == null) return "-";
                hasColour = ((vdata.Info.Flags >> 4) & 1) != 0;
                if (!hasColour) return "no COLOR0";
                var verts = VertexDecoder.Decode(vdata, null);
                if (verts == null || verts.Length == 0) return "-";
                Vector4 sum = Vector4.Zero; Vector4 min = new Vector4(9), max = new Vector4(-9);
                foreach (var v in verts) { sum += v.Colour0; min = Vector4.Min(min, v.Colour0); max = Vector4.Max(max, v.Colour0); }
                sum /= verts.Length;
                return $"avg {sum.X:0.00},{sum.Y:0.00},{sum.Z:0.00},{sum.W:0.00} min r{min.X:0.00} g{min.Y:0.00} max r{max.X:0.00} g{max.Y:0.00} n{verts.Length}";
            }
            catch (Exception ex) { return "err " + ex.Message; }
        }

        private string InteriorPropReport_L2(Vector3 cam, float radius)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"L2DUMP cam {cam.X:0.0},{cam.Y:0.0},{cam.Z:0.0} radius {radius} draw list {worldRender.Model.Meshes.Count} interior '{InteriorTcRoomName}' modifier '{InteriorTcModifierName}' strength {InteriorTcStrengthNow:0.00}");
            if (timecycle != null && interiorTcModifier >= 0 && interiorTcModifier < timecycle.Modifiers.Count)
            {
                var mod = timecycle.Modifiers[interiorTcModifier];
                sb.Append($"  MODIFIER {mod.Name}:");
                foreach (var kv in mod.Values) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine();
                if (sceneRenderer?.GlobalLight is TimecycleData.GlobalLightState g)
                    sb.AppendLine($"  GLOBALS natUp {g.NaturalAmbUp} natDn {g.NaturalAmbDown} artUp {g.ArtificialAmbUp} artDn {g.ArtificialAmbDown} dir {g.LightDirColour} dirAmb {g.LightDirAmbColour}");
            }
            var meshesOf = new Dictionary<YmapEntityDef, List<RenderMesh>>();
            foreach (var m in worldRender.Model.Meshes)
            {
                var o = worldRender.OwnerOf(m);
                if (o == null) continue;
                if (!meshesOf.TryGetValue(o, out var l)) meshesOf[o] = l = new List<RenderMesh>();
                l.Add(m);
            }
            var seenArch = new HashSet<uint>();
            foreach (var kv in meshesOf.OrderBy(k => (k.Key.Position - cam).Length()))
            {
                var e = kv.Key;
                float d = (e.Position - cam).Length();
                if (d > radius) continue;
                var a = e.Archetype;
                if (a == null) continue;
                var def = e._CEntityDef;
                bool first = seenArch.Add(a.Hash);
                var m0 = kv.Value[0];
                sb.AppendLine($"  {d,5:0.0} m {a.Name} {(e.MloParent != null ? "[in " + (e.MloParent.Archetype?.Name ?? "mlo") + "]" : "[world]")} aoMult {def.ambientOcclusionMultiplier} artAO {def.artificialAmbientOcclusion} tint {def.tintValue} flags {def.flags} lod {def.lodLevel} scale {e.Scale.X:0.00} meshes {kv.Value.Count} " +
                              $"ambScales nat {m0.NaturalAmbientScale:0.00} art {m0.ArtificialAmbientScale:0.00} int {m0.InInterior:0} artInt up {m0.ArtIntAmbUp.X:0.00},{m0.ArtIntAmbUp.Y:0.00},{m0.ArtIntAmbUp.Z:0.00} dn {m0.ArtIntAmbDown.X:0.00},{m0.ArtIntAmbDown.Y:0.00},{m0.ArtIntAmbDown.Z:0.00} refl {m0.ReflectIntAmb:0.0}");
                if (!first) continue;
                foreach (var m in kv.Value.Take(16))
                {
                    string vc = MeshVertexColourStats_L2(m, out _);
                    sb.AppendLine($"      {m.ShaderName} mode {m.AlphaMode} kind {m.DecalKind} spec {(m.SpecSRV != null ? "MAP" : "none")} int {m.SpecIntensity:0.00} mask {m.SpecMapIntMask.X:0.##},{m.SpecMapIntMask.Y:0.##},{m.SpecMapIntMask.Z:0.##} falloff {m.SpecFalloffMult:0.#} fresnel {m.SpecFresnel:0.00} emissive {m.EmissiveMult:0.##} bump {(m.BumpSRV != null ? "MAP" : "none")} diffuse '{m.DiffuseName}' {(m.DiffuseSRV != null ? "ok" : "MISSING")} tint {(m.TintPaletteSRV != null ? $"mode{m.TintMode} row{m.TintPaletteIndex}/{m.TintPaletteHeight}" : "-")} vc0 [{vc}]");
                }
            }
            return sb.ToString();
        }
    }
}

