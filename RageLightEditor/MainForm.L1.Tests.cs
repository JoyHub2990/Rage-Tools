using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static MCMloRoomDef Room_L1(string name, Vector3 mn, Vector3 mx) => new MCMloRoomDef
        {
            RoomName = name,
            _Data = new CMloRoomDef { bbMin = mn, bbMax = mx },
            BBMin_CW = mn, BBMax_CW = mx,
        };
        private static MCMloPortalDef Portal_L1(int from, int to, params Vector3[] corners) => new MCMloPortalDef
        {
            _Data = new CMloPortalDef { roomFrom = (uint)from, roomTo = (uint)to },
            Corners = corners.Select(c => new Vector4(c, 1)).ToArray(),
        };
        private static YmapEntityDef Shell_L1(MloArchetype arch, Vector3 pos)
        {
            var e = new YmapEntityDef { Archetype = arch, Position = pos, Orientation = Quaternion.Identity };
            e.BSRadius = (arch.BBMax - arch.BBMin).Length();
            return e;
        }

        partial void InteriorCullerTest_L1(Action<string, bool, string> check)
        {
            var a = new MloArchetype { BBMin = new Vector3(0, 0, 0), BBMax = new Vector3(20, 10, 4) };
            a.rooms = new[]
            {
                Room_L1("limbo", new Vector3(-1), new Vector3(-1)),
                Room_L1("hall", new Vector3(0, 0, 0), new Vector3(10, 10, 4)),
                Room_L1("office", new Vector3(10.15f, 0, 0), new Vector3(20, 10, 4)),
                Room_L1("closet", new Vector3(2, 2, 0), new Vector3(5, 5, 4)),
            };
            a.portals = new[]
            {
                Portal_L1(1, 0, new Vector3(0, 4, 0), new Vector3(0, 6, 0), new Vector3(0, 6, 2.5f), new Vector3(0, 4, 2.5f)),
                Portal_L1(1, 2, new Vector3(10, 4, 0), new Vector3(10, 6, 0), new Vector3(10, 6, 2.5f), new Vector3(10, 4, 2.5f)),
            };
            var pos = new Vector3(500, 500, 0);
            var shellA = Shell_L1(a, pos);
            var b = new MloArchetype { BBMin = new Vector3(0, 0, 0), BBMax = new Vector3(20, 10, 4) };
            b.rooms = new[] { Room_L1("limbo", new Vector3(-1), new Vector3(-1)), Room_L1("room", new Vector3(0, 0, 0), new Vector3(20, 10, 4)) };
            b.portals = new MCMloPortalDef[0];
            var shellB = Shell_L1(b, new Vector3(600, 600, 0));
            var c = new MloArchetype { BBMin = new Vector3(0, 0, 0), BBMax = new Vector3(20, 10, 4) };
            c.rooms = new[] { Room_L1("limbo", new Vector3(-1), new Vector3(-1)), Room_L1("cell", new Vector3(0, 0, 0), new Vector3(1, 1, 4)) };
            c.portals = new[] { Portal_L1(1, 0, new Vector3(0, 0.2f, 0), new Vector3(0, 0.8f, 0), new Vector3(0, 0.8f, 2), new Vector3(0, 0.2f, 2)) };
            var shellC = Shell_L1(c, new Vector3(700, 700, 0));

            var cull = new InteriorCuller();
            var all = new List<YmapEntityDef> { shellA, shellB, shellC };
            var vp = Matrix.LookAtRH(pos + new Vector3(5, 5, 1.5f), pos + new Vector3(15, 5, 1.5f), Vector3.UnitZ) * Matrix.PerspectiveFovRH(1.2f, 1.6f, 0.1f, 1000f);
            void At(Vector3 local) => cull.Update(pos + local, Vector3.UnitX, vp, all, null);

            At(new Vector3(3, 3, 1.5f));
            check("l1 cull: eye in two overlapping room boxes - the smallest names the room", cull.Inside == shellA && cull.Room == 3, $"inside {(cull.Inside == shellA)} room {cull.Room} '{cull.RoomName}'");
            check("l1 cull: ...and the enclosing room is open too (never hides the room you stand in)", cull.RoomsVisible >= 2, $"rooms visible {cull.RoomsVisible}");

            int outsideSteps = 0, roomSwitches = 0, last = -1; float firstOffice = -1;
            for (float x = 5.0f; x <= 15.0f; x += 0.05f)
            {
                At(new Vector3(x, 7, 1.5f));
                if (cull.Inside == null) outsideSteps++;
                else
                {
                    if (last > 0 && cull.Room != last) roomSwitches++;
                    if (cull.Room == 2 && firstOffice < 0) firstOffice = x;
                    last = cull.Room;
                }
            }
            check("l1 cull: a walk across the gap between two room boxes never goes outside", outsideSteps == 0, $"{outsideSteps} steps outside");
            check("l1 cull: ...and changes room exactly once, once the new room held for a few frames", roomSwitches == 1 && cull.Room == 2 && firstOffice > 10.4f, $"switches {roomSwitches} final room {cull.Room} office from x={firstOffice:0.00}");

            At(new Vector3(0.5f, 5, 1.5f));
            bool inAtDoor = cull.Inside == shellA;
            At(new Vector3(-0.5f, 5, 1.5f));
            check("l1 cull: a step out through the street door is outside", inAtDoor && cull.Inside == null, $"inside at the door {inAtDoor}, outside after {(cull.Inside == null)}");
            At(new Vector3(5, 5, 1.5f));
            At(new Vector3(5, 10.5f, 1.5f));
            check("l1 cull: a hair over a wall (no door there) still counts as the room", cull.Inside == shellA && cull.Room == 1, $"inside {(cull.Inside == shellA)} room {cull.Room}");
            At(new Vector3(5, 12.0f, 1.5f));
            check("l1 cull: a metre and more past the wall is outside", cull.Inside == null, cull.Inside == null ? "outside" : $"room {cull.Room}");

            cull.Update(new Vector3(610, 605, 1.5f), Vector3.UnitX, vp, all, null);
            check("l1 cull: an interior with no portals is refused (never culls the city to nothing)", cull.Inside == null && cull.IsRefused(b), $"inside {(cull.Inside != null)} refused {cull.IsRefused(b)}");
            cull.Update(new Vector3(700.5f, 700.5f, 1.5f), Vector3.UnitX, vp, all, null);
            check("l1 cull: an interior whose rooms cover almost none of its shell is refused", cull.Inside == null && cull.IsRefused(c), $"inside {(cull.Inside != null)} refused {cull.IsRefused(c)}");
            check("l1 cull: the good interior is not refused", !cull.IsRefused(a) && cull.InteriorsRefused == 2, $"refused {cull.InteriorsRefused}");
        }

        partial void RunWorldTestExtras_L1(Action<string, bool, string> check, Action<Vector3> settle)
        {
            if (WorldBlock("intcull"))
            {
                var from = new Vector3(-46, -1112, 27); var to = new Vector3(-46, -1085, 27);
                settle((from + to) * 0.5f);
                var cull = new InteriorCuller();
                CameraSequence.ApplyToCamera(camera, from, 4.71f, 0.1f, settings.FovDeg);
                var savedCull = worldRender.InteriorCull;
                int insideSteps = 0, emptySteps = 0, minOwn = int.MaxValue, flips = 0, wasIn = 0;
                var rooms = new HashSet<string>();
                for (float t = 0; t <= 1.0f; t += 0.005f)
                {
                    var p = Vector3.Lerp(from, to, t);
                    CameraSequence.ApplyToCamera(camera, p, 4.71f, 0.1f, settings.FovDeg);
                    camera.Update();
                    cull.Update(camera.Position, camera.GetForward(), camera.ViewProjMatrix, World.InteriorsEmitted, null);
                    worldRender.InteriorCull = cull.Inside != null ? cull : null;
                    worldRender.Update(World.Visible, gameFiles, modelRenderer, new BoundingFrustum(camera.ViewProjMatrix), true, World.Fade);
                    int nowIn = cull.Inside != null ? 1 : 0;
                    if (wasIn != nowIn && t > 0) flips++;
                    wasIn = nowIn;
                    if (cull.Inside == null) continue;
                    insideSteps++;
                    rooms.Add(cull.RoomName);
                    if (cull.OwnTested > 0)
                    {
                        int own = cull.OwnTested - cull.HiddenRooms;
                        minOwn = Math.Min(minOwn, own);
                        if (own == 0) emptySteps++;
                    }
                }
                worldRender.InteriorCull = savedCull;
                Console.WriteLine($"  INTCULL walk PDM: {insideSteps} steps inside, rooms [{string.Join(", ", rooms)}], own props drawn min {(minOwn == int.MaxValue ? -1 : minOwn)}, empty steps {emptySteps}, in/out flips {flips}, room switches {cull.RoomSwitches}, refused {cull.InteriorsRefused}");
                check("INTCULL: the walk through PDM is inside v_carshowroom for a stretch", insideSteps > 20 && cull.InteriorsRefused == 0, $"{insideSteps} steps inside, refused {cull.InteriorsRefused}");
                check("INTCULL: the interior's own props never draw EMPTY while the eye is inside", emptySteps == 0 && minOwn > 0, $"min own drawn {minOwn}, {emptySteps} empty steps");
                check("INTCULL: in and out once each, no flicker (2 flips), few room changes", flips <= 2 && cull.RoomSwitches <= 6, $"flips {flips} switches {cull.RoomSwitches}");
            }

            if (WorldBlock("mloworld"))
            {
                string ytyp = Environment.GetEnvironmentVariable("RLE_MLOWORLD_YTYP");
                if (string.IsNullOrEmpty(ytyp)) ytyp = @"C:\Users\GS\Desktop\m26_1_int_01.ytyp";
                if (!File.Exists(ytyp)) Console.WriteLine($"  MLOWORLD skipped: {ytyp} not found (RLE_MLOWORLD_YTYP=<file>)");
                else
                {
                    var at = new Vector3(-70, -1103, 120);
                    var savedWs = panel.Workspace;
                    panel.SwitchWorkspace(LightPanel.Space.World);
                    CameraSequence.ApplyToCamera(camera, at, 1.4f, 0.9f, settings.FovDeg); camera.Update();
                    settle(at);
                    int Meshes()
                    {
                        UpdateInteriorCull_J4();
                        worldRender.Update(World.Visible, gameFiles, modelRenderer, new BoundingFrustum(camera.ViewProjMatrix), true, World.Fade);
                        return worldRender.MeshesDrawn;
                    }
                    int before = Meshes();
                    var camBefore = camera.Capture();
                    int modBefore = timecycle?.SelectedModifier ?? -1;
                    panel.SwitchWorkspace(LightPanel.Space.Mlo);
                    try { ImportYtyp(ytyp); } catch (Exception ex) { Console.WriteLine("  MLOWORLD import failed: " + ex.Message); }
                    var info = scene.MloInfo;
                    for (int i = 0; i < 3; i++) TickInteriorTimecycle_K2();
                    panel.SwitchWorkspace(LightPanel.Space.World);
                    TickInteriorTimecycle_J3();
                    settle(at);
                    int after = Meshes();
                    var camAfter = camera.Capture();
                    Console.WriteLine($"  MLOWORLD meshes before {before} after {after}; interior cull {(worldRender.InteriorCull == null ? "none" : interiorCull.ToString())}; modifier {timecycle?.SelectedModifier ?? -1} (was {modBefore}); imported {(info?.MloName ?? "nothing")} with {info?.Interiors?.Count ?? 0} interior(s); camera {camera.Position}");
                    check("MLOWORLD: an MLO imported in the MLO workspace was placed", info != null && (info.Interiors?.Count ?? 0) > 0, $"{info?.MloName ?? "none"}");
                    check("MLOWORLD: back in World the world draws the same mesh count as before (within 10%)", before > 100 && Math.Abs(after - before) <= Math.Max(before, after) * 0.10f, $"before {before} after {after}");
                    check("MLOWORLD: no interior cull is active over the city (the imported interior is not the world's)", worldRender.InteriorCull == null && (interiorCull == null || interiorCull.Inside == null), interiorCull?.ToString() ?? "none");
                    check("MLOWORLD: the world's timecycle carries no room modifier from the import", (timecycle?.SelectedModifier ?? -1) == -1, $"modifier {timecycle?.SelectedModifier ?? -1}");
                    check("MLOWORLD: the world camera is where it was", camAfter.SameAs(camBefore), $"{camAfter.Target} vs {camBefore.Target}");
                    panel.SwitchWorkspace(savedWs);
                }
            }
        }
    }
}

