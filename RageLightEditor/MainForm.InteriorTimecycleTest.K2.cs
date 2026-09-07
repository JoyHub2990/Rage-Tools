using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void InteriorTimecycleTest_K2(Action<string, bool, string> check)
        {
            if (timecycle == null || scene == null || panel == null) return;
            var savedWs = panel.Workspace;
            var savedInfo = scene.MloInfo;
            var savedOpt = panel.WorldInteriorTimecycle;
            int savedSel = timecycle.SelectedModifier; float savedStr = timecycle.ModifierStrength;
            var savedCam = camera.Capture();
            try
            {
                panel.SwitchWorkspace(LightPanel.Space.Light);
                panel.LoadInteriorTimecycleOption();
                panel.WorldInteriorTimecycle = true;
                string xml = "<timecycle_modifier_data><modifier name=\"k2_test_room_a\" numMods=\"1\" userFlags=\"0\"><light_dir_mult>0.5 0</light_dir_mult></modifier>" +
                             "<modifier name=\"k2_test_room_b\" numMods=\"1\" userFlags=\"0\"><light_dir_mult>0.2 0</light_dir_mult></modifier></timecycle_modifier_data>";
                int ia = timecycle.FindModifier(JenkHash.GenHash("k2_test_room_a"), "k2_test_room_a");
                if (ia < 0) timecycle.LoadModifiersXmlText(xml, "seqtest", out _);
                ia = timecycle.FindModifier(JenkHash.GenHash("k2_test_room_a"), "k2_test_room_a");
                int ib = timecycle.FindModifier(JenkHash.GenHash("k2_test_room_b"), "k2_test_room_b");
                check("k2 inttc: test modifiers loaded", ia >= 0 && ib >= 0, $"a {ia} b {ib} of {timecycle.Modifiers.Count}");

                var arch = new MloArchetype();
                arch.BBMin = new Vector3(0, 0, 0); arch.BBMax = new Vector3(20, 10, 4);
                arch.rooms = new[]
                {
                    new MCMloRoomDef { RoomName = "limbo", _Data = new CMloRoomDef { bbMin = new Vector3(-1), bbMax = new Vector3(-1) } },
                    new MCMloRoomDef { RoomName = "roomA", _Data = new CMloRoomDef { bbMin = new Vector3(0, 0, 0), bbMax = new Vector3(10, 10, 4), timecycleName = new MetaHash(JenkHash.GenHash("k2_test_room_a")), flags = 4 } },
                    new MCMloRoomDef { RoomName = "roomB", _Data = new CMloRoomDef { bbMin = new Vector3(10, 0, 0), bbMax = new Vector3(20, 10, 4), timecycleName = new MetaHash(JenkHash.GenHash("k2_test_room_b")), flags = 0 } },
                };
                var placed = new MloPlacedInterior { Arch = arch, Position = new Vector3(1000, 1000, 0), Orientation = Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.PiOverTwo) };
                var info = new MloImportResult();
                info.Interiors.Add(placed);
                scene.SetMlo(new Rendering.RenderModel { Name = "k2test" }, info);

                void Park(Vector3 local)
                {
                    var w = placed.ToWorld(local);
                    camera.Target = w; camera.Distance = 0.01f; camera.SnapSmoothing(); camera.Update();
                }
                void Ticks(int n) { for (int i = 0; i < n; i++) { k2TcLastTime = clock.Elapsed.TotalSeconds - 0.1; TickInteriorTimecycle_K2(); } }

                Park(new Vector3(5, 5, 1.5f)); Ticks(12);
                check("k2 inttc: room A found", InteriorTcRoomName_K2.EndsWith("/roomA"), InteriorTcRoomName_K2);
                check("k2 inttc: room A's modifier applied", timecycle.SelectedModifier == ia && timecycle.ModifierStrength > 0.99f, $"sel {timecycle.SelectedModifier} str {timecycle.ModifierStrength:0.00}");
                check("k2 inttc: room A flag 4 turns the sun off", timecycle.SuppressDirectionalIndoors, timecycle.SuppressDirectionalIndoors.ToString());
                Park(new Vector3(15, 5, 1.5f)); Ticks(12);
                check("k2 inttc: room B found", InteriorTcRoomName_K2.EndsWith("/roomB"), InteriorTcRoomName_K2);
                check("k2 inttc: room B's modifier applied", timecycle.SelectedModifier == ib && timecycle.ModifierStrength > 0.99f, $"sel {timecycle.SelectedModifier} str {timecycle.ModifierStrength:0.00}");
                check("k2 inttc: room B keeps the sun", !timecycle.SuppressDirectionalIndoors, timecycle.SuppressDirectionalIndoors.ToString());
                timecycle.SelectedModifier = ia; Ticks(3);
                check("k2 inttc: a hand-picked modifier is left alone", timecycle.SelectedModifier == ia, $"sel {timecycle.SelectedModifier}");
                Park(new Vector3(5, 5, 1.5f)); Ticks(12);
                check("k2 inttc: the next room takes over again", timecycle.SelectedModifier == ia && InteriorTcRoomName_K2.EndsWith("/roomA") && timecycle.ModifierStrength > 0.99f, $"sel {timecycle.SelectedModifier} {InteriorTcRoomName_K2}");
                Park(new Vector3(40, 40, 1.5f)); Ticks(20);
                check("k2 inttc: outside every room fades out to none", timecycle.SelectedModifier == -1 && InteriorTcRoomName_K2 == "", $"sel {timecycle.SelectedModifier} [{InteriorTcRoomName_K2}]");
                Park(new Vector3(5, 5, 1.5f)); Ticks(12);
                panel.WorldInteriorTimecycle = false; Ticks(3);
                check("k2 inttc: option off restores", timecycle.SelectedModifier == -1, $"sel {timecycle.SelectedModifier}");
                panel.ShowGrid = true;
                bool g1 = panel.ShowGridEffective(true);
                panel.ShowGrid = true; bool g2 = panel.ShowGridEffective(true);
                bool g3 = panel.ShowGridEffective(false);
                check("k2 grid: hidden by an import, hand override holds, back when empty", !g1 && g2 && g3, $"{g1} {g2} {g3}");
            }
            finally
            {
                scene.ClearMlo();
                if (savedInfo != null) scene.SetMlo(new Rendering.RenderModel { Name = "k2restore" }, savedInfo);
                panel.WorldInteriorTimecycle = savedOpt;
                timecycle.SelectedModifier = savedSel; timecycle.ModifierStrength = savedStr;
                camera.Restore(savedCam);
                panel.SwitchWorkspace(savedWs);
            }
        }
    }
}

