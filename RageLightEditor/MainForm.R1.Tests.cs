using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_R1(Action<string, bool, string> check)
        {
            var ui = Creator;
            MloShellBuildTest_R1(check, ui);
            YtypRemovalTest_R1(check);
            ProjectYtypRemovalTest_R1(check);
            CreatorEndToEndTest_R1(check, ui);
        }

        private void CreatorEndToEndTest_R1(Action<string, bool, string> check, MloCreatorPanel ui)
        {
            try
            {
                if (ui == null) { check("creator e2e: the panel exists", false, "no creator"); return; }
                if (ui.Session == null && scene.HasModel) ui.Session = MloCreatorSession.FromScene(scene);
                var s = ui.Session;
                if (s == null) { check("creator e2e: a session to work in", false, "none"); return; }
                int rooms0 = s.Rooms.Count, portals0 = s.Portals.Count;

                ui.RequestAddRoomAtView = true;
                ServiceMloCreatorRequests(ui);
                check("creator e2e: + Room adds one and selects it",
                    s.Rooms.Count == rooms0 + 1 && ui.SelectedRoom == s.Rooms.Count - 1 && ui.Page == MloCreatorPanel.PageKind.Room,
                    $"{s.Rooms.Count} rooms, selected {ui.SelectedRoom}, page {ui.Page}");
                var room = ui.CurrentRoom;
                check("creator e2e: the new room has a real box", room != null && room.IsValid, room == null ? "none" : $"{room.Min} .. {room.Max}");

                string was = room.Name;
                s.PushUndo("Rename room", "room.name");
                room.Name = "r1_renamed";
                s.History.Seal("room.name");
                check("creator e2e: rename is one undo step named for it", s.History.CanUndo && s.History.UndoName.Contains("Rename"), s.History.UndoName ?? "none");
                s.Undo();
                check("creator e2e: undo puts the old name back", s.Rooms[s.Rooms.Count - 1].Name == was, s.Rooms[s.Rooms.Count - 1].Name);
                s.History.Redo();
                check("creator e2e: redo renames it again", s.Rooms[s.Rooms.Count - 1].Name == "r1_renamed", s.Rooms[s.Rooms.Count - 1].Name);

                ui.SelectRoom(s.Rooms.Count - 1);
                ui.InvokeDelete_R1();
                check("creator e2e: Delete removes the room and clears the page",
                    s.Rooms.Count == rooms0 && ui.Page == MloCreatorPanel.PageKind.Interior, $"{s.Rooms.Count} rooms, page {ui.Page}");
                s.Undo(); ui.ClampSelection();
                check("creator e2e: undo brings the room back with its name",
                    s.Rooms.Count == rooms0 + 1 && s.Rooms[s.Rooms.Count - 1].Name == "r1_renamed", $"{s.Rooms.Count} rooms, last '{s.Rooms[s.Rooms.Count - 1].Name}'");
                s.History.Redo(); ui.ClampSelection();
                check("creator e2e: redo deletes it again", s.Rooms.Count == rooms0, $"{s.Rooms.Count} rooms");

                ui.RequestAddPortalAtView = true;
                ServiceMloCreatorRequests(ui);
                check("creator e2e: + Portal adds one, selected, with four corners",
                    s.Portals.Count == portals0 + 1 && ui.SelectedPortal == s.Portals.Count - 1 && (ui.CurrentPortal?.Corners?.Length ?? 0) == 4,
                    $"{s.Portals.Count} portals, selected {ui.SelectedPortal}");
                var p = ui.CurrentPortal;
                check("creator e2e: the new portal joins two different rooms",
                    p != null && p.RoomFrom != p.RoomTo && p.RoomFrom >= 0 && p.RoomTo >= 0 && p.RoomFrom < s.Rooms.Count && p.RoomTo < s.Rooms.Count,
                    p == null ? "none" : $"{p.RoomFrom} -> {p.RoomTo}");
                ui.InvokeDelete_R1();
                check("creator e2e: Delete removes the portal", s.Portals.Count == portals0, $"{s.Portals.Count} portals");
                s.Undo(); ui.ClampSelection();
                check("creator e2e: undo brings the portal back", s.Portals.Count == portals0 + 1, $"{s.Portals.Count} portals");
                s.RemovePortal(s.Portals.Count - 1);

                if (s.Rooms.Count > 1)
                {
                    var target = s.Rooms[1];
                    s.Entities.Add(new MloCreatorEntity { ArchetypeName = "prop_rle_r1", Position = target.Centre + new Vector3(0, 0, 0.2f) });
                    s.AutoAssignRooms();
                    int ei = s.Entities.Count - 1;
                    ui.SelectEntity(ei);
                    int autoWas = s.Entities[ei].AutoRoom;
                    check("creator e2e: a prop dropped inside a room joins it by containment",
                        s.Entities[ei].RoomOverride < 0 && autoWas > 0 && s.Rooms[autoWas].Contains(s.Entities[ei].Position),
                        $"room {s.Entities[ei].Room} (auto {autoWas}, pinned {s.Entities[ei].RoomOverride})");
                    ui.RequestAssignEntityRoom_O3 = 0;
                    ServiceMloCreatorRequests(ui);
                    check("creator e2e: pinning it to limbo takes, and is one undo step",
                        s.Entities[ei].RoomOverride == 0 && s.History.CanUndo && s.History.UndoName.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0,
                        $"pinned {s.Entities[ei].RoomOverride}, undo '{s.History.UndoName}'");
                    s.Undo(); ui.ClampSelection();
                    check("creator e2e: undo hands it back to containment", s.Entities.Count > ei && s.Entities[ei].RoomOverride < 0 && s.Entities[ei].Room == autoWas,
                        $"pinned {s.Entities[ei].RoomOverride} room {s.Entities[ei].Room} (was {autoWas})");
                    s.Entities.RemoveAt(ei);
                    s.AutoAssignRooms();
                }

                var ytypForName = new YtypFile { Name = "rle_r1_named.ytyp" };
                var mloForName = new MloArchetype();
                var defForName = new CMloArchetypeDef();
                mloForName.Init(ytypForName, ref defForName);
                check("creator: a hash-only archetype name becomes the .ytyp's own name",
                    MloCreatorSession.SafeInteriorName_R1("2573682904", mloForName) == "rle_r1_named", MloCreatorSession.SafeInteriorName_R1("2573682904", mloForName));
                check("creator: CodeWalker's hash_ spelling too", MloCreatorSession.SafeInteriorName_R1("hash_995a1b58", mloForName) == "rle_r1_named", MloCreatorSession.SafeInteriorName_R1("hash_995a1b58", mloForName));
                check("creator: a real name is left alone", MloCreatorSession.SafeInteriorName_R1("v_firestation", mloForName) == "v_firestation", MloCreatorSession.SafeInteriorName_R1("v_firestation", mloForName));
                check("creator: with no ytyp behind it, a hash falls back to my_interior", MloCreatorSession.SafeInteriorName_R1("2573682904", null) == "my_interior", MloCreatorSession.SafeInteriorName_R1("2573682904", null));

                check("creator e2e: the interior is still valid after all of that", s.Validate().Count == 0, string.Join("; ", s.Validate()));
                ui.SetStatus("--seqtest: the creator's round trip checked.");
            }
            catch (Exception ex)
            {
                check("creator e2e: no exception", false, ex.ToString());
            }
        }

        private void YtypRemovalTest_R1(Action<string, bool, string> check)
        {
            Scene sc = null;
            try
            {
                sc = new Scene(modelRenderer, null);
                var reg = sc.Imports_R1;

                var propShared = new MloProp { Name = "prop_shared", Path = @"C:\rle_r1\prop_shared.ydr" };
                var propA = new MloProp { Name = "prop_only_a", Path = @"C:\rle_r1\prop_only_a.ydr" };
                var modelA = new RenderModel { Name = "a" };
                modelA.Meshes.Add(FakeMesh_R1(new Vector3(0, 0, 0)));
                modelA.Meshes.Add(FakeMesh_R1(new Vector3(2, 0, 0)));
                var modelB = new RenderModel { Name = "b" };
                modelB.Meshes.Add(FakeMesh_R1(new Vector3(20, 0, 0)));

                var resA = new MloImportResult { MloName = "interior_a", Placed = 2 };
                resA.Props.Add(propA); resA.Props.Add(propShared);
                resA.Ytyps.Add(new MloYtypInfo { Name = "a.ytyp", Path = @"C:\rle_r1\a.ytyp" });
                resA.Entities.Add(new MloEntityInfo { ArchetypeName = "ent_a" });
                var resB = new MloImportResult { MloName = "interior_b", Placed = 1 };
                resB.Props.Add(propShared);
                resB.Ytyps.Add(new MloYtypInfo { Name = "b.ytyp", Path = @"C:\rle_r1\b.ytyp" });
                resB.Entities.Add(new MloEntityInfo { ArchetypeName = "ent_b" });

                var combined = new RenderModel { Name = "combined" };
                combined.Meshes.AddRange(modelA.Meshes);
                combined.Meshes.AddRange(modelB.Meshes);
                sc.SetMlo(combined, resA);
                foreach (var p in new[] { propA, propShared })
                    sc.AddImportedProp(p.Path, null, null, new RenderModel { Name = p.Name }, null, null, Matrix.Identity, 1, true, p.Name);
                reg.Note(@"C:\rle_r1\a.ytyp", resA, modelA, resA.Props);
                reg.Note(@"C:\rle_r1\b.ytyp", resB, modelB, resB.Props);
                sc.SetMloInfo_R1(reg.MergeRemaining());

                check("ytyp remove: both imports listed", reg.Count == 2 && sc.MloModel.Meshes.Count == 3 && sc.Files.Count == 2,
                    $"{reg.Count} imports, {sc.MloModel.Meshes.Count} meshes, {sc.Files.Count} props");
                check("ytyp remove: the merged info carries both files' archetypes and entities",
                    sc.MloInfo.Ytyps.Count == 2 && sc.MloInfo.Entities.Count == 2 && sc.MloInfo.Placed == 3,
                    $"{sc.MloInfo.Ytyps.Count} ytyps, {sc.MloInfo.Entities.Count} entities, placed {sc.MloInfo.Placed}");

                string msg = RemoveImportedYtyp_R1(sc, @"C:\rle_r1\a.ytyp", out bool ok);
                Console.WriteLine("  YTYPREMOVE " + msg);
                check("ytyp remove: it reported success", ok, msg);
                check("ytyp remove: only A's meshes left the scene",
                    sc.MloModel.Meshes.Count == 1 && ReferenceEquals(sc.MloModel.Meshes[0], modelB.Meshes[0]),
                    $"{sc.MloModel.Meshes.Count} meshes left");
                check("ytyp remove: A's own prop went, the shared one stayed",
                    sc.Files.Count == 1 && sc.Files[0].Path.EndsWith("prop_shared.ydr", StringComparison.OrdinalIgnoreCase),
                    string.Join(", ", sc.Files.Select(f => f.Name)));
                check("ytyp remove: MloInfo is rebuilt from what is left",
                    sc.MloInfo != null && sc.MloInfo.Ytyps.Count == 1 && sc.MloInfo.Ytyps[0].Name == "b.ytyp" &&
                    sc.MloInfo.Entities.Count == 1 && sc.MloInfo.Entities[0].ArchetypeName == "ent_b" && sc.MloInfo.Placed == 1,
                    $"{sc.MloInfo?.Ytyps.Count} ytyps, {sc.MloInfo?.Entities.Count} entities, placed {sc.MloInfo?.Placed}");
                check("ytyp remove: the scene's bounds no longer cover the removed wing",
                    sc.MloModel.Bounds.Minimum.X > 10.0f, $"{sc.MloModel.Bounds.Minimum} .. {sc.MloModel.Bounds.Maximum}");
                check("ytyp remove: one import left in the list", reg.Count == 1 && reg.Entries[0].Name == "b.ytyp",
                    string.Join(", ", reg.Entries.Select(e => e.Name)));

                string msg2 = RemoveImportedYtyp_R1(sc, @"C:\rle_r1\not_here.ytyp", out bool ok2);
                check("ytyp remove: an unknown file is refused, not obeyed", !ok2 && reg.Count == 1, msg2);

                string msg3 = RemoveImportedYtyp_R1(sc, @"C:\rle_r1\b.ytyp", out bool ok3);
                check("ytyp remove: the last import empties the scene",
                    ok3 && reg.Count == 0 && sc.MloModel == null && sc.Files.Count == 0,
                    $"{msg3} -> {reg.Count} imports, model {(sc.MloModel == null ? "null" : sc.MloModel.Meshes.Count + " meshes")}, {sc.Files.Count} props");
            }
            catch (Exception ex)
            {
                check("ytyp remove: no exception", false, ex.ToString());
            }
            finally
            {
                try { sc?.Dispose(); } catch { }
            }
        }

        private static RenderMesh FakeMesh_R1(Vector3 at)
        {
            var m = new RenderMesh
            {
                Transform = Matrix.Translation(at),
                LocalBounds = new BoundingBox(new Vector3(-1), new Vector3(1)),
                OwnsBuffers = false,
            };
            m.SetBoundsFromLocal();
            return m;
        }

        private void ProjectYtypRemovalTest_R1(Action<string, bool, string> check)
        {
            try
            {
                if (projCtl == null || ProjWin == null) { Console.WriteLine("  SKIP project ytyp removal (no project window)"); return; }
                var saved = ProjWin.Project;
                var p = new CwProject { Name = "rle_r1_ytypremove" };
                var t1 = MakeYtyp_R1("rle_r1_one");
                var t2 = MakeYtyp_R1("rle_r1_two");
                check("project ytyp: two ytyps added", p.AddYtypFile(t1) && p.AddYtypFile(t2) && p.YtypFiles.Count == 2, $"{p.YtypFiles.Count}");
                ProjWin.Project = p;
                ProjWin.Select(t1);
                check("project ytyp: the first one is the current item", ReferenceEquals(ProjWin.CurrentYtyp, t1), ProjWin.CurrentYtyp?.Name ?? "none");
                projCtl.RemoveYtyp();
                check("project ytyp: Remove from Project takes that file and only that file",
                    p.YtypFiles.Count == 1 && ReferenceEquals(p.YtypFiles[0], t2) && !p.ContainsYtyp(t1),
                    string.Join(", ", p.YtypFiles.Select(x => x.Name)));
                if (gameFiles?.Cache != null)
                {
                    int regs = projCtl.RegisterProjectArchetypes(true);
                    check("project ytyp: only the remaining file's archetypes are registered", regs == 1, $"{regs} registered");
                }
                else Console.WriteLine("  SKIP project ytyp archetype registration (no game cache)");
                ProjWin.Project = saved;
            }
            catch (Exception ex)
            {
                check("project ytyp: no exception", false, ex.ToString());
            }
        }

        private static YtypFile MakeYtyp_R1(string name)
        {
            var t = new YtypFile { Name = name + ".ytyp", FilePath = Path.Combine(Path.GetTempPath(), name + ".ytyp") };
            t.NameHash = JenkHash.GenHash(name);
            t._CMapTypes.name = t.NameHash;
            t.Loaded = true;
            var arch = new Archetype();
            var def = new CBaseArchetypeDef { name = new MetaHash(JenkHash.GenHash(name + "_arch")), lodDist = 100 };
            arch.Init(t, ref def);
            t.AllArchetypes = new[] { arch };
            return t;
        }
    }
}

