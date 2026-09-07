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
        private int j3Tick;

        partial void OnWorldTick_J3()
        {
            if (!worldBuilt || !panel.WorldMode) return;
            j3Tick++;
            if (j3Tick == 432 && Environment.GetEnvironmentVariable("RLE_DUMPFAMILY") is string fam && fam.Length > 0)
                Console.WriteLine(WorldFamilyReport(fam, camera.Position));
            if (j3Tick == 432 && screenshotPath != null)
                Console.WriteLine($"STATES sites {World.StateSites} hidden {World.StateHiddenYmaps}" + (World.StateReport.Length > 0 ? Environment.NewLine + World.StateReport : ""));
            if (j3Tick == 200 && Environment.GetEnvironmentVariable("RLE_SETSURVEY") == "1")
                Console.WriteLine(EntitySetSurvey());
            TickInteriorTimecycle_J3();
            if (j3Tick == 432 && Environment.GetEnvironmentVariable("RLE_INTTC") is string rooms && rooms.StartsWith("rooms", StringComparison.Ordinal))
            {
                float within = 60.0f;
                int colon = rooms.IndexOf(':');
                if (colon > 0) float.TryParse(rooms.Substring(colon + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out within);
                Console.WriteLine(InteriorRoomsReport(camera.Position, within));
            }
            if (j3Tick == 432 && screenshotPath != null)
                Console.WriteLine($"INTERIORTC status: room '{InteriorTcRoomName}' modifier '{InteriorTcModifierName}' strength {InteriorTcStrengthNow:0.00} option {(panel.WorldInteriorTimecycle ? "on" : "off")}{(Environment.GetEnvironmentVariable("RLE_INTTC") == "0" ? " (RLE_INTTC=0)" : "")} modifiers loaded {timecycle?.Modifiers.Count ?? 0}");
        }

        private string InteriorRoomsReport(Vector3 at, float within)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var shell in World.InteriorsEmitted)
            {
                var mi = shell?.MloInstance;
                var rooms = mi?.MloArch?.rooms;
                if (rooms == null) continue;
                if ((shell.Position - at).Length() > within) continue;
                sb.AppendLine($"INTROOMS {shell.Archetype?.Name} @ {shell.Position.X:0.0},{shell.Position.Y:0.0},{shell.Position.Z:0.0} rooms {rooms.Length}");
                for (int r = 0; r < rooms.Length; r++)
                {
                    var room = rooms[r];
                    if (room == null) continue;
                    var d = room._Data;
                    var wmin = shell.Position + Vector3.Transform(d.bbMin, shell.Orientation);
                    var wmax = shell.Position + Vector3.Transform(d.bbMax, shell.Orientation);
                    var c = (wmin + wmax) * 0.5f;
                    int mi1 = timecycle?.FindModifier(d.timecycleName.Hash, d.timecycleName.ToString()) ?? -1;
                    int mi2 = timecycle?.FindModifier(d.secondaryTimecycleName.Hash, d.secondaryTimecycleName.ToString()) ?? -1;
                    sb.AppendLine($"  ROOM {r} '{room.RoomName}' centre {c.X:0.0},{c.Y:0.0},{c.Z:0.0} size {Math.Abs(wmax.X - wmin.X):0.0}x{Math.Abs(wmax.Y - wmin.Y):0.0}x{Math.Abs(wmax.Z - wmin.Z):0.0} flags {d.flags} blend {d.blend:0.00} tc {d.timecycleName} ({(mi1 >= 0 ? timecycle.Modifiers[mi1].Name : "-")}) 2nd {d.secondaryTimecycleName} ({(mi2 >= 0 ? timecycle.Modifiers[mi2].Name : "-")}) portals {d.portalCount} extVis {d.exteriorVisibiltyDepth}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private string EntitySetSurvey()
        {
            var sb = new System.Text.StringBuilder();
            var c = gameFiles?.Cache;
            if (c?.YtypDict == null) return "SETSURVEY no cache";
            int mlos = 0;
            foreach (var ytyp in c.YtypDict.Values)
            {
                if (ytyp?.AllArchetypes == null) continue;
                foreach (var a in ytyp.AllArchetypes)
                {
                    if (!(a is MloArchetype m) || m.entitySets == null || m.entitySets.Length < 2) continue;
                    mlos++;
                    sb.Append($"SETSURVEY {m.Name} [{m.entitySets.Length}] ytyp={ytyp.Name}:");
                    foreach (var es in m.entitySets) sb.Append($" {es?.Name ?? es?._Data.name.ToString()}({es?.Entities?.Length ?? 0})");
                    sb.AppendLine();
                    sb.AppendLine("  " + WorldStreamer.DescribeAutoSets(m));
                }
            }
            sb.AppendLine($"SETSURVEY {mlos} interiors with 2+ sets");
            return sb.ToString().TrimEnd();
        }

        partial void RunWorldTestExtras_J3(Action<string, bool, string> check, Action<Vector3> settle)
        {
            if (WorldBlock("mansion"))
            {
                var site = new Vector3(-1760, 478, 160);
                settle(site);
                uint hOrig = JenkHash.GenHash("hei_ch1_06e_mansion_original"), hOrigLod = JenkHash.GenHash("hei_ch1_06e_mansion_original_lod");
                uint hProps = JenkHash.GenHash("hei_ch1_06e_props_original"), hShared = JenkHash.GenHash("hei_ch1_06e_mansion_shared"), hSharedLod = JenkHash.GenHash("hei_ch1_06e_mansion_shared_lod");
                Console.WriteLine("  MANSION " + World.StateReport.Replace(Environment.NewLine, " | ").Replace("\n", " | "));
                check("MANSION: the site's ORIGINAL state (old terrain, its props) is out of the tree while SHARED is resident",
                      World.IsStateHiddenYmap(hOrig) && World.IsStateHiddenYmap(hOrigLod) && World.IsStateHiddenYmap(hProps) && !World.IsInLodTree(hOrig) && World.IsInLodTree(hShared),
                      $"original hidden {World.IsStateHiddenYmap(hOrig)} inTree {World.IsInLodTree(hOrig)}; shared inTree {World.IsInLodTree(hShared)}; sites {World.StateSites}");
                var far = new Vector3(-2100, 470, 215);
                settle(far);
                bool groundLod = false, houseLod = false, origLod = false;
                foreach (var e in World.Visible)
                {
                    var n = e?.Archetype?.Name ?? "";
                    if (n == "hei_ch1_06e_mansion_ground_lod" || n == "hei_ch1_06e_mansion_ground_slod1") groundLod = true;
                    else if (n == "hei_ch1_06e_mansion_5_lod" || n == "hei_ch1_06e_mansion_5_slod1") houseLod = true;
                    else if (n == "hei_ch1_06e_original_terrain_lod" || n == "hei_ch1_06e_original_slod1" || n == "hei_ch1_06e_original_terrain") origLod = true;
                }
                check("MANSION: from 450 m the mansion's ground LOD and house LOD draw, and no ORIGINAL terrain",
                      groundLod && houseLod && !origLod && World.IsInLodTree(hSharedLod), $"groundLod {groundLod} houseLod {houseLod} originalTerrain {origLod} sharedLod inTree {World.IsInLodTree(hSharedLod)}");
            }
            if (WorldBlock("intsets"))
            {
                MloArchetype FindMlo(string name)
                {
                    uint h = JenkHash.GenHash(name);
                    var c = gameFiles?.Cache;
                    if (c?.YtypDict == null) return null;
                    foreach (var ytyp in c.YtypDict.Values)
                    {
                        if (ytyp?.AllArchetypes == null) continue;
                        foreach (var a in ytyp.AllArchetypes) if (a is MloArchetype m && a.Hash == h) return m;
                    }
                    return null;
                }
                void OneOf(string mlo, string familyPrefix, string alsoOn)
                {
                    var arch = FindMlo(mlo);
                    var on = arch != null ? WorldStreamer.AutoSetNames(arch) : new List<string>();
                    int members = on.Count(n => n.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase) && n.Length > familyPrefix.Length && n.Substring(familyPrefix.Length).All(char.IsDigit));
                    bool also = alsoOn == null || on.Contains(alsoOn);
                    check($"INTSETS {mlo}: Auto turns on exactly ONE of the {familyPrefix}* sets" + (alsoOn != null ? $" and {alsoOn}" : ""),
                          arch != null && members == 1 && also, arch == null ? "archetype not in the cache" : $"{members} of the family on, {on.Count} sets on: {string.Join(", ", on)}");
                }
                OneOf("tr_tuner_mod_garage", "entity_set_style_", "entity_set_car_lift_default");
                OneOf("ba_dlc_int_01_ba", "int01_ba_style", null);
                {
                    var arch = FindMlo("ch_dlc_arcade");
                    var on = arch != null ? WorldStreamer.AutoSetNames(arch) : new List<string>();
                    int ceilings = on.Count(n => n.StartsWith("entity_set_arcade_set_ceiling_", StringComparison.OrdinalIgnoreCase));
                    check("INTSETS ch_dlc_arcade: one ceiling (beams / flat / mirror), the constant geometry, no derelict", arch != null && ceilings == 1 && on.Contains("entity_set_constant_geometry") && !on.Contains("entity_set_arcade_set_derelict"),
                          $"ceilings {ceilings}: {string.Join(", ", on)}");
                }
                {
                    var arch = FindMlo("vw_dlc_casino_main");
                    var on = arch != null ? WorldStreamer.AutoSetNames(arch) : new List<string>();
                    check("INTSETS vw_dlc_casino_main: only casino_manager_default", arch != null && on.Count == 1 && on[0] == "casino_manager_default", string.Join(", ", on));
                }
            }
            if (WorldBlock("interiortc"))
            {
                if (timecycle != null && timecycle.Modifiers.Count == 0 && gameFiles != null && gameFiles.Ready) RefreshGameTimecycles();
                var pdm = new Vector3(-46, -1097, 27);
                settle(pdm);
                var room = FindCameraRoom(pdm, out var inst, out int ri);
                int mi = room != null ? FindRoomModifier(room, out _) : -1;
                string mloName = inst?.Owner?.Archetype?.Name ?? "-";
                check("INTERIORTC: the camera at PDM is in a v_carshowroom room whose modifier int_carshowroom is loaded",
                      room != null && mloName == "v_carshowroom" && mi >= 0 && timecycle.Modifiers[mi].Name.Equals("int_carshowroom", StringComparison.OrdinalIgnoreCase),
                      $"mlo {mloName} room {ri} '{room?.RoomName ?? "-"}' modifier {(mi >= 0 ? timecycle.Modifiers[mi].Name : "none")} ({timecycle?.Modifiers.Count ?? 0} modifiers loaded)");
                var street = new Vector3(-30, -1080, 27);
                var outside = FindCameraRoom(street, out _, out _);
                check("INTERIORTC: on the street outside PDM no room contains the camera", outside == null, outside == null ? "outside" : $"in '{outside.RoomName}'");
            }
        }

        private string WorldFamilyReport(string subs, Vector3 at)
        {
            var sb = new System.Text.StringBuilder();
            var wants = subs.ToLowerInvariant().Split(';', StringSplitOptions.RemoveEmptyEntries);
            var vis = new HashSet<YmapEntityDef>(World.Visible);
            foreach (var y in World.ResidentYmaps.OrderBy(y => y.Name))
            {
                var nm = (y.Name ?? "").ToLowerInvariant();
                if (!wants.Any(w => nm.Contains(w))) continue;
                uint h = y.RpfFileEntry?.ShortNameHash ?? 0;
                sb.AppendLine($"FAMILY {y.Name} scripted={y.IsScripted} ents={(y.AllEntities?.Length ?? 0)} parent={y._CMapData.parent} parentObj={y.Parent?.Name ?? "-"} flags={y._CMapData.flags} content={y._CMapData.contentFlags} " +
                              $"candidate={World.IsCandidate(h)} inTree={World.IsInLodTree(h)} hiddenVariant={World.IsHiddenVariant(h)} stateHidden={World.IsStateHiddenYmap(h)} path={y.RpfFileEntry?.Path}");
                var all = y.AllEntities;
                if (all == null) continue;
                int shown = 0;
                foreach (var e in all)
                {
                    if (e == null) continue;
                    if (shown++ >= 40) { sb.AppendLine("  ..."); break; }
                    sb.AppendLine($"  ENT {e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString()} lod={e._CEntityDef.lodLevel} pos={e.Position.X:0.0},{e.Position.Y:0.0},{e.Position.Z:0.0} d={(e.Position - at).Length():0} bsR={e.BSRadius:0.0} lodDist={e.LodDist:0} childLod={e.ChildLodDist:0} numChildren={e._CEntityDef.numChildren} linked={(e.LodManagerChildren?.Count ?? 0)} " +
                                  $"parent={(e.Parent?.Archetype?.Name ?? (e._CEntityDef.parentIndex >= 0 ? "#" + e._CEntityDef.parentIndex : "-"))} visible={vis.Contains(e)} variantHidden={World.IsVariantHiddenEnt(e)} mlo={(e.MloInstance != null)} flags={e._CEntityDef.flags}");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}

