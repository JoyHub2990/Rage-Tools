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
        private int k2Tick;
        private bool k2ShadowDumped;

        partial void OnWorldTick_K2()
        {
            if (!worldBuilt || !panel.WorldMode) return;
            k2Tick++;
            if (k2Tick == 1 && Environment.GetEnvironmentVariable("RLE_SUNCASTERS") == "0") { worldRender.CasterRadius = 0.0f; PortalOccludersEnabled = false; }
            TickPortalOccluders_K2();
            TickProjectTimecycleMods_K2();
            if (!k2ShadowDumped && screenshotPath != null && worldWarmup >= 455 && Environment.GetEnvironmentVariable("RLE_SHADOWDUMP") == "1")
            {
                k2ShadowDumped = true;
                Console.WriteLine(ShadowCasterReport_K2(camera.Position, 25.0f));
            }
        }

        private string ShadowCasterReport_K2(Vector3 cam, float radius)
        {
            var sb = new StringBuilder();
            var drawn = new HashSet<RenderMesh>(worldRender.Model.Meshes);
            var meshesOf = new Dictionary<YmapEntityDef, List<RenderMesh>>();
            foreach (var m in worldRender.Model.Meshes)
            {
                var o = worldRender.OwnerOf(m);
                if (o == null) continue;
                if (!meshesOf.TryGetValue(o, out var l)) meshesOf[o] = l = new List<RenderMesh>();
                l.Add(m);
            }
            float texel0 = shadowRenderer.Cascades.Count > 0 ? shadowRenderer.Cascades.TexelWorld[0] : 0.0f;
            float minR = texel0 * shadowRenderer.CascadeMinRadiusTexels;
            sb.AppendLine($"SHADOWDUMP cam {cam.X:0.0},{cam.Y:0.0},{cam.Z:0.0} draw list {worldRender.Model.Meshes.Count} meshes + {worldRender.SunCasterExtras.Count} near culled casters + {worldRender.SunCasterOccluders.Count} portal occluders, cascades {shadowRenderer.Cascades.Count} (texel0 {texel0:0.000} m, min caster radius {minR:0.000} m), last cascade draws {shadowRenderer.LastCascadeDraws} skipped-small {shadowRenderer.CascadeMeshesSkipped}, interior cull {(interiorCull != null ? interiorCull.ToString() : "none")}");
            var near = World.Visible.Where(e => e?.Archetype != null && (e.Position - cam).Length() - Math.Max(e.BSRadius, 0) < radius)
                                    .OrderBy(e => e.Archetype is MloArchetype ? -1.0f : (e.Position - cam).Length()).Take(80).ToList();
            foreach (var e in near)
            {
                var a = e.Archetype;
                var model = worldRender.PeekModel(a.Hash);
                string state = model != null ? $"built {model.Meshes.Count} meshes" : worldRender.IsFailed(a.Hash) ? "FAILED" : worldRender.IsLoading(a.Hash) ? "loading" : "not asked";
                meshesOf.TryGetValue(e, out var inList);
                int listed = inList?.Count ?? 0;
                int casting = inList?.Count(m => m.Visible && (m.AlphaMode == GeomAlphaMode.Opaque || m.AlphaMode == GeomAlphaMode.Cutout) && m.WorldSphere.Radius >= minR) ?? 0;
                string kind = a is MloArchetype ? "MLO" : a._BaseArchetypeDef.assetType.ToString().Replace("ASSET_TYPE_", "");
                sb.AppendLine($"  {(e.Position - cam).Length(),5:0.0} m {a.Name} [{kind}{(e.MloParent != null ? " in " + e.MloParent.Archetype?.Name : "")}] flags {e._CEntityDef.flags} archflags {a._BaseArchetypeDef.flags} lod {e._CEntityDef.lodLevel} {state}; in draw list {listed}, casting {casting}");
                if (model == null) continue;
                foreach (var bm in model.Meshes.Take(12))
                {
                    var inst = inList?.FirstOrDefault(m => ReferenceEquals(m.Shader, bm.Shader) && m.IndexCount == bm.IndexCount && m.ShaderIndex == bm.ShaderIndex);
                    string why;
                    if (inst == null) why = InteriorCullSays_K2(e) ? "NOT IN LIST (interior cull hides it)" : "NOT IN LIST";
                    else if (!inst.Visible) why = "hidden (Visible=false)";
                    else if (inst.AlphaMode != GeomAlphaMode.Opaque && inst.AlphaMode != GeomAlphaMode.Cutout) why = $"skipped: alpha mode {inst.AlphaMode}";
                    else if (inst.WorldSphere.Radius < minR) why = $"skipped: radius {inst.WorldSphere.Radius:0.000} under {minR:0.000}";
                    else why = "casts";
                    sb.AppendLine($"      {bm.ShaderName} {bm.AlphaMode}{(bm.NeverDraw ? " NEVERDRAW" : "")}{(bm.DoubleSided ? " 2sided" : "")} r {(inst ?? bm).WorldSphere.Radius:0.00} tris {bm.IndexCount / 3} -> {why}");
                }
                if (model.Meshes.Count > 12) sb.AppendLine($"      ... {model.Meshes.Count - 12} more meshes");
            }
            var inside = interiorCull?.Inside;
            var arch = interiorCull?.InsideArch;
            if (inside?.MloInstance != null && arch?.portals != null)
            {
                var ents = inside.MloInstance.Entities;
                sb.AppendLine($"  PORTALS of {arch.Name} ({arch.portals.Length}), instance entities {(ents?.Length ?? 0)}:");
                for (int pi = 0; pi < arch.portals.Length; pi++)
                {
                    var p = arch.portals[pi];
                    var c = p.Corners ?? Array.Empty<Vector4>();
                    var centre = Vector3.Zero;
                    foreach (var v in c) centre += Vector3.TransformCoordinate(new Vector3(v.X, v.Y, v.Z), Matrix.RotationQuaternion(inside.Orientation) * Matrix.Translation(inside.Position));
                    if (c.Length > 0) centre /= c.Length;
                    float d = (centre - cam).Length();
                    if (d > radius * 2) continue;
                    var att = new StringBuilder();
                    if (p.AttachedObjects != null)
                        foreach (var ai in p.AttachedObjects)
                        {
                            var ae = ents != null && ai < ents.Length ? ents[ai] : null;
                            if (ae == null) { att.Append($" #{ai}:missing"); continue; }
                            var am = ae.Archetype != null ? worldRender.PeekModel(ae.Archetype.Hash) : null;
                            meshesOf.TryGetValue(ae, out var al);
                            int acast = al?.Count(m => m.Visible && (m.AlphaMode == GeomAlphaMode.Opaque || m.AlphaMode == GeomAlphaMode.Cutout)) ?? 0;
                            att.Append($" #{ai}:{ae._CEntityDef.archetypeName}({(ae.Archetype == null ? "NO ARCHETYPE" : am == null ? (worldRender.IsFailed(ae.Archetype.Hash) ? "FAILED" : "not built") : $"built {am.Meshes.Count}")}, listed {al?.Count ?? 0}, casting {acast}{(World.Visible.Contains(ae) ? "" : ", NOT VISIBLE")})");
                        }
                    sb.AppendLine($"    portal {pi} {p._Data.roomFrom}->{p._Data.roomTo} flags {p._Data.flags} opacity {p._Data.opacity} at {centre.X:0.0},{centre.Y:0.0},{centre.Z:0.0} ({d:0.0} m) attached {(p.AttachedObjects?.Length ?? 0)}:{att}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private int k2ProjYtyps = -1;
        private void TickProjectTimecycleMods_K2()
        {
            var p = ProjWin?.Project;
            int n = p?.YtypFiles?.Count ?? 0;
            if (n == k2ProjYtyps) return;
            k2ProjYtyps = n;
            if (p == null || timecycle == null) return;
            int added = 0;
            foreach (var t in p.YtypFiles) if (!string.IsNullOrEmpty(t?.FilePath)) added += LoadLocalTimecycleMods(t.FilePath);
            if (added > 0) Console.WriteLine($"INTERIORTC project modifiers: +{added} from the project's ytyp folders ({timecycle.Modifiers.Count} loaded)");
        }

        private bool InteriorCullSays_K2(YmapEntityDef e)
        {
            if (interiorCull == null || interiorCull.Inside == null || worldRender.InteriorCull == null) return false;
            var s = new BoundingSphere(e.Position + e.BSCenter, Math.Max(e.BSRadius, 0.1f));
            return interiorCull.IsHidden(e, ref s);
        }
    }
}

