using System;
using System.Collections.Generic;
using System.Text;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int intAuditTick;
        private static readonly int intAuditAt =
            int.TryParse(Environment.GetEnvironmentVariable("RLE_INTAUDITTICK"), out int t) && t > 0 ? t : 455;

        private void OnWorldTick_N1()
        {
            if (!worldBuilt || !panel.WorldMode) return;
            var want = Environment.GetEnvironmentVariable("RLE_INTAUDIT");
            if (string.IsNullOrEmpty(want)) return;
            if (++intAuditTick != intAuditAt) return;
            Console.Write(InteriorAudit_N1(want, camera.Position, 400.0f));
        }

        private enum AuditCat_N1 { Drawn, SetOff, RoomCulled, FlagHidden, Failed, NotLoaded, NotPlaced }
        private const int AuditCats_N1 = 7;

        private string InteriorAudit_N1(string want, Vector3 at, float radius)
        {
            var sb = new StringBuilder();
            bool all = string.Equals(want, "all", StringComparison.OrdinalIgnoreCase);
            int shown = 0;
            foreach (var shell in World.InteriorsEmitted)
            {
                var arch = shell?.Archetype as MloArchetype;
                var inst = shell?.MloInstance;
                if (arch == null || inst == null) continue;
                var name = arch.Name ?? "";
                if (all) { if ((shell.Position - at).Length() > radius) continue; }
                else if (name.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 8) { sb.AppendLine("INTAUDIT (more interiors match - narrow RLE_INTAUDIT)"); break; }
                AuditOneInterior_N1(sb, shell, arch, inst, at);
            }
            if (shown == 0) sb.AppendLine($"INTAUDIT nothing matches '{want}' among the {World.InteriorsEmitted.Count} interiors emitted (RLE_DUMPMLO=all lists them)");
            return sb.ToString();
        }

        private void AuditOneInterior_N1(StringBuilder sb, YmapEntityDef shell, MloArchetype arch, MloInstanceData inst, Vector3 at)
        {
            int nrooms = Math.Max(arch.rooms?.Length ?? 0, 1);
            var total = new int[nrooms + 1];
            var cats = new int[nrooms + 1, AuditCats_N1];
            var samples = new List<string>[AuditCats_N1];
            for (int i = 0; i < AuditCats_N1; i++) samples[i] = new List<string>();
            bool inside = interiorCull != null && interiorCull.IsInside_N1(shell);
            var twins = new HashSet<(int, uint)>();
            void NoteTwin(YmapEntityDef ie, int room)
            {
                if (ie?.Archetype == null || World.IsInteriorEntityFlagHidden_N1(ie)) return;
                twins.Add((room > 0 && room < nrooms ? room : nrooms, ie.Archetype.Hash));
            }
            int flagTwins = 0;
            var flagHisto = new Dictionary<uint, int>();

            void Count(YmapEntityDef ie, int room, bool setOn)
            {
                int slot = (room > 0 && room < nrooms) ? room : nrooms;
                total[slot]++;
                AuditCat_N1 c;
                if (!setOn) c = AuditCat_N1.SetOff;
                else if (ie?.Archetype == null) c = AuditCat_N1.Failed;
                else if (World.IsInteriorEntityFlagHidden_N1(ie)) c = AuditCat_N1.FlagHidden;
                else if (worldRender.IsFailed(ie.Archetype.Hash)) c = AuditCat_N1.Failed;
                else if (inside && !interiorCull.IsRoomOpen_N1(room)) c = AuditCat_N1.RoomCulled;
                else if (worldRender.PeekModel(ie.Archetype.Hash) == null) c = AuditCat_N1.NotLoaded;
                else if (!worldRender.HasInstances(ie)) c = AuditCat_N1.NotPlaced;
                else c = AuditCat_N1.Drawn;
                cats[slot, (int)c]++;
                if (c == AuditCat_N1.FlagHidden)
                {
                    if (ie?.Archetype != null && twins.Contains((slot, ie.Archetype.Hash))) flagTwins++;
                    uint f = ie?._CEntityDef.flags ?? 0;
                    flagHisto.TryGetValue(f, out int fn); flagHisto[f] = fn + 1;
                }
                if (c != AuditCat_N1.Drawn && samples[(int)c].Count < 6)
                    samples[(int)c].Add((ie?.Archetype?.Name ?? ie?._CEntityDef.archetypeName.ToString() ?? "?") + (slot < nrooms ? "@" + slot : "") +
                                        (c == AuditCat_N1.FlagHidden ? $"(flags {ie?._CEntityDef.flags}, archFlags {ie?.Archetype?._BaseArchetypeDef.flags})" : ""));
            }

            var plainRooms = WorldStreamer.PlainEntityRooms_N1(inst);
            var ents = inst.Entities;
            var sets = inst.EntitySets;
            void EachEntity(Action<YmapEntityDef, int, bool> f)
            {
                if (ents != null)
                    for (int i = 0; i < ents.Length; i++) f(ents[i], i < plainRooms.Length ? plainRooms[i] : -1, true);
                if (sets == null) return;
                foreach (var set in sets)
                {
                    if (set?.Entities == null) continue;
                    bool on = World.IsSetVisible_N1(inst, set);
                    var locs = set.Locations;
                    for (int j = 0; j < set.Entities.Count; j++)
                        f(set.Entities[j], (locs != null && j < locs.Length) ? (int)locs[j] : -1, on);
                }
            }
            EachEntity((ie, room, on) => { if (on) NoteTwin(ie, room); });
            EachEntity(Count);

            sb.AppendLine($"INTAUDIT {arch.Name} at {shell.Position.X:0},{shell.Position.Y:0},{shell.Position.Z:0} d={(shell.Position - at).Length():0} ymap={shell.Ymap?.Name} " +
                          $"rooms {nrooms - 1} sets {sets?.Length ?? 0} plainEnts {ents?.Length ?? 0} ytyp={(arch.Ytyp?.RpfFileEntry?.Path ?? arch.Ytyp?.FilePath ?? "?")} " +
                          $"eyeInside={inside}{(inside ? " room " + interiorCull.Room + " '" + interiorCull.RoomName + "'" : "")}");
            foreach (var line in World.DescribeSets_N1(inst)) sb.AppendLine("INTAUDIT   set " + line);
            if (arch.portals != null)
            {
                int shownP = 0;
                for (int i = 0; i < arch.portals.Length && shownP < 12; i++)
                {
                    var pd = arch.portals[i];
                    var cs = pd?.Corners;
                    if (cs == null || cs.Length < 3) continue;
                    int from = (int)pd._Data.roomFrom, to = (int)pd._Data.roomTo;
                    if ((from == 0) == (to == 0)) continue;
                    var cl = Vector3.Zero;
                    for (int k = 0; k < cs.Length; k++) cl += cs[k].XYZ();
                    cl /= cs.Length;
                    var w = shell.Position + shell.Orientation.Multiply(cl);
                    int inner = from != 0 ? from : to;
                    shownP++;
                    sb.AppendLine($"INTAUDIT   way out: portal {i} room {inner} '{(inner < nrooms ? arch.rooms[inner]?.RoomName : "?")}' at {w.X:0.0},{w.Y:0.0},{w.Z:0.0} d={(w - at).Length():0} flags {pd._Data.flags}");
                }
            }
            var grand = new int[AuditCats_N1];
            int grandTotal = 0;
            for (int r = 1; r <= nrooms; r++)
            {
                if (r == nrooms && total[r] == 0) continue;
                if (total[r] == 0 && r < nrooms) { sb.AppendLine($"INTAUDIT   room {r} '{arch.rooms[r]?.RoomName}' EMPTY IN THE FILE (no entity is attached to it or placed in it)"); continue; }
                string rn = r < nrooms ? $"room {r} '{arch.rooms[r]?.RoomName}'" : "room -/limbo (doors, unattached)";
                string open = inside ? (interiorCull.IsRoomOpen_N1(r < nrooms ? r : -1) ? "open" : "CLOSED by the portal walk") : "-";
                sb.AppendLine($"INTAUDIT   {rn}: ents {total[r]} drawn {cats[r, 0]} setOff {cats[r, 1]} roomCulled {cats[r, 2]} flagHidden {cats[r, 3]} failedModel {cats[r, 4]} notLoadedYet {cats[r, 5]} notPlaced {cats[r, 6]} [{open}]");
                grandTotal += total[r];
                for (int c = 0; c < AuditCats_N1; c++) grand[c] += cats[r, c];
            }
            sb.AppendLine($"INTAUDIT   TOTAL ents {grandTotal} drawn {grand[0]} setOff {grand[1]} roomCulled {grand[2]} flagHidden {grand[3]} (of which {flagTwins} are a second copy of a prop that does draw in the same room) failedModel {grand[4]} notLoadedYet {grand[5]} notPlaced {grand[6]}");
            if (flagHisto.Count > 0)
            {
                var fs = new List<string>();
                foreach (var kv in flagHisto) fs.Add($"{kv.Key} x{kv.Value} [{EntityFlagWords_N1(kv.Key)}]");
                fs.Sort();
                sb.AppendLine("INTAUDIT   flagHidden by entity flags: " + string.Join(" | ", fs));
            }
            string[] catNames = { "drawn", "setOff", "roomCulled", "flagHidden", "failedModel", "notLoadedYet", "notPlaced" };
            for (int c = 1; c < AuditCats_N1; c++)
                if (samples[c].Count > 0) sb.AppendLine($"INTAUDIT   e.g. {catNames[c]}: {string.Join(", ", samples[c])}");
            if (inside) sb.AppendLine("INTAUDIT   culler: " + interiorCull);
        }

        private static string EntityFlagWords_N1(uint f)
        {
            var w = new List<string>();
            if ((f & 0x400000u) != 0) w.Add("no shadows");
            if ((f & 0x800000u) != 0) w.Add("ONLY shadows");
            if ((f & 0x1000000u) != 0) w.Add("no reflections");
            if ((f & 0x2000000u) != 0) w.Add("ONLY reflections");
            if ((f & 0x4000000u) != 0) w.Add("no water reflections");
            if ((f & 0x8000000u) != 0) w.Add("ONLY water reflections");
            if ((f & 0x10000000u) != 0) w.Add("no mirror reflections");
            if ((f & 0x20000000u) != 0) w.Add("ONLY mirror reflections");
            return w.Count == 0 ? "no render-pass flags (timed archetype or proxy by name)" : string.Join(" + ", w);
        }

        private static MCMloRoomDef Room_N1(string name, Vector3 mn, Vector3 mx, params uint[] attached) => new MCMloRoomDef
        {
            RoomName = name,
            _Data = new CMloRoomDef { bbMin = mn, bbMax = mx },
            BBMin_CW = mn, BBMax_CW = mx,
            AttachedObjects = attached,
        };
        private static MCMloPortalDef Portal_N1(int from, int to, params Vector3[] corners) => new MCMloPortalDef
        {
            _Data = new CMloPortalDef { roomFrom = (uint)from, roomTo = (uint)to },
            Corners = Array.ConvertAll(corners, c => new Vector4(c, 1)),
        };
        private static MCMloEntitySet Set_N1(string name, int count, params uint[] rooms)
        {
            JenkIndex.Ensure(name);
            return new MCMloEntitySet
            {
                _Data = new CMloEntitySet { name = new MetaHash(JenkHash.GenHash(name)) },
                Entities = new MCEntityDef[count],
                Locations = rooms,
            };
        }
        private static MloArchetype Mlo_N1(string name, Vector3 bbmin, Vector3 bbmax)
        {
            JenkIndex.Ensure(name);
            var a = new MloArchetype { BBMin = bbmin, BBMax = bbmax };
            a._BaseArchetypeDef.name = new MetaHash(JenkHash.GenHash(name));
            return a;
        }

        private void SeqTest_N1(Action<string, bool, string> check)
        {
            {
                var a = Mlo_N1("v_n1_test", new Vector3(0, 0, 0), new Vector3(75, 10, 4));
                a.rooms = new[]
                {
                    Room_N1("limbo", new Vector3(-1), new Vector3(-1)),
                    Room_N1("hall", new Vector3(0, 0, 0), new Vector3(15, 10, 4), 0),
                    Room_N1("back", new Vector3(15, 0, 0), new Vector3(30, 10, 4), 1),
                    Room_N1("far", new Vector3(60, 0, 0), new Vector3(75, 10, 4), 2),
                    Room_N1("office", new Vector3(0, 10, 0), new Vector3(15, 20, 4), 3),
                };
                a.portals = new[]
                {
                    Portal_N1(1, 0, new Vector3(0, 4, 0), new Vector3(0, 6, 0), new Vector3(0, 6, 2.5f), new Vector3(0, 4, 2.5f)),
                    Portal_N1(1, 0, new Vector3(0, 7, 1), new Vector3(0, 9, 1), new Vector3(0, 9, 2.5f), new Vector3(0, 7, 2.5f)),
                    Portal_N1(1, 4, new Vector3(4, 10, 0), new Vector3(6, 10, 0), new Vector3(6, 10, 2.5f), new Vector3(4, 10, 2.5f)),
                };
                var pos = new Vector3(1000, 1000, 0);
                var shell = new YmapEntityDef { Archetype = a, Position = pos, Orientation = Quaternion.Identity };
                shell.BSRadius = (a.BBMax - a.BBMin).Length();
                var list = new List<YmapEntityDef> { shell };
                var cull = new InteriorCuller();
                Matrix Vp(Vector3 eye, Vector3 target) => Matrix.LookAtRH(eye, target, Vector3.UnitZ) * Matrix.PerspectiveFovRH(1.2f, 1.6f, 0.1f, 4000f);

                var eye = pos + new Vector3(7, 5, 1.5f);
                cull.Update(eye, Vector3.UnitX, Vp(eye, eye + Vector3.UnitX), list, null);
                bool inHall = cull.Inside == shell && cull.Room == 1;
                check("n1 cull: the room next door is open even though no portal joins it (props never vanish from around you)",
                      inHall && cull.IsRoomOpen_N1(2) && cull.RoomsByNear >= 1, $"inside {inHall} room 2 open {cull.IsRoomOpen_N1(2)} byNear {cull.RoomsByNear}");
                check("n1 cull: ...and a far wing 60 m away is open because its box is in view",
                      cull.IsRoomOpen_N1(3) && cull.RoomsByView >= 1, $"room 3 open {cull.IsRoomOpen_N1(3)} byView {cull.RoomsByView}");

                check("n1 cull: no exterior portal in view -> the world is drawn, not hidden",
                      cull.ExteriorOpen, cull.ExteriorWhy);
                var far = new BoundingSphere(pos + new Vector3(400, 0, 20), 30);
                check("n1 cull: ...and a building 400 m away is not hidden while that holds", !cull.IsHidden(new YmapEntityDef { Position = far.Center, BSRadius = 30 }, ref far), "hidden");

                cull.Update(eye, -Vector3.UnitX, Vp(eye, eye - Vector3.UnitX), list, null);
                check("n1 cull: facing the window/door the exterior opens through the portals (no fallback)",
                      !cull.ExteriorOpen && cull.ExteriorRegions > 0, $"open {cull.ExteriorOpen} regions {cull.ExteriorRegions} {cull.ExteriorWhy}");
                check("n1 cull: ...and the far wing behind the eye is closed again (the room cull still does something)",
                      !cull.IsRoomOpen_N1(3), $"room 3 open {cull.IsRoomOpen_N1(3)}");
                var behind = new BoundingSphere(pos + new Vector3(200, 300, 20), 5);
                check("n1 cull: ...and a building off to the side, behind a wall, is still culled",
                      cull.IsHidden(new YmapEntityDef { Position = behind.Center, BSRadius = 5 }, ref behind), "not hidden");

                var b = Mlo_N1("custom_n1_thin", new Vector3(0, 0, 0), new Vector3(50, 10, 4));
                b.rooms = new[]
                {
                    Room_N1("limbo", new Vector3(-1), new Vector3(-1)),
                    Room_N1("r1", new Vector3(0, 0, 0), new Vector3(10, 10, 4), 0),
                    Room_N1("r2", new Vector3(10, 0, 0), new Vector3(20, 10, 4), 1),
                    Room_N1("r3", new Vector3(20, 0, 0), new Vector3(30, 10, 4), 2),
                    Room_N1("r4", new Vector3(30, 0, 0), new Vector3(40, 10, 4), 3),
                    Room_N1("r5", new Vector3(40, 0, 0), new Vector3(50, 10, 4), 4),
                };
                b.portals = new[] { Portal_N1(1, 2, new Vector3(10, 4, 0), new Vector3(10, 6, 0), new Vector3(10, 6, 2.5f), new Vector3(10, 4, 2.5f)) };
                var posB = new Vector3(2000, 2000, 0);
                var shellB = new YmapEntityDef { Archetype = b, Position = posB, Orientation = Quaternion.Identity };
                shellB.BSRadius = (b.BBMax - b.BBMin).Length();
                var eyeB = posB + new Vector3(5, 5, 1.5f);
                var cull2 = new InteriorCuller();
                cull2.Update(eyeB, Vector3.UnitX, Vp(eyeB, eyeB + Vector3.UnitX), new List<YmapEntityDef> { shellB }, null);
                check("n1 cull: an interior whose rooms are barely joined up never culls the world (a rough custom MLO)",
                      cull2.Inside == shellB && cull2.ExteriorOpen, $"inside {(cull2.Inside == shellB)} open {cull2.ExteriorOpen} {cull2.ExteriorWhy}");

                var c = Mlo_N1("vw_n1_sealed", new Vector3(0, 0, 0), new Vector3(20, 10, 4));
                c.rooms = new[]
                {
                    Room_N1("limbo", new Vector3(-1), new Vector3(-1)),
                    Room_N1("r1", new Vector3(0, 0, 0), new Vector3(10, 10, 4), 0),
                    Room_N1("r2", new Vector3(10, 0, 0), new Vector3(20, 10, 4), 1),
                };
                var mirror = Portal_N1(1, 0, new Vector3(0, 4, 0), new Vector3(0, 6, 0), new Vector3(0, 6, 2.5f), new Vector3(0, 4, 2.5f));
                mirror._Data.flags = 4;
                c.portals = new[] { Portal_N1(1, 2, new Vector3(10, 4, 0), new Vector3(10, 6, 0), new Vector3(10, 6, 2.5f), new Vector3(10, 4, 2.5f)), mirror };
                var posC = new Vector3(3000, 3000, 0);
                var shellC = new YmapEntityDef { Archetype = c, Position = posC, Orientation = Quaternion.Identity };
                shellC.BSRadius = (c.BBMax - c.BBMin).Length();
                var eyeC = posC + new Vector3(5, 5, 1.5f);
                var cull3 = new InteriorCuller();
                cull3.Update(eyeC, Vector3.UnitX, Vp(eyeC, eyeC + Vector3.UnitX), new List<YmapEntityDef> { shellC }, null);
                check("n1 cull: a sealed interior (every way out a mirror) keeps culling the city - there is no window there",
                      cull3.Inside == shellC && !cull3.ExteriorOpen, $"inside {(cull3.Inside == shellC)} open {cull3.ExteriorOpen} {cull3.ExteriorWhy}");
            }

            {
                var m = Mlo_N1("ch_n1_dlc", new Vector3(0, 0, 0), new Vector3(20, 10, 4));
                m.rooms = new[]
                {
                    Room_N1("limbo", new Vector3(-1), new Vector3(-1)),
                    Room_N1("main", new Vector3(0, 0, 0), new Vector3(10, 10, 4), 0, 1),
                    Room_N1("locker", new Vector3(10, 0, 0), new Vector3(20, 10, 4)),
                };
                m.entitySets = new[]
                {
                    Set_N1("n1_trophies", 4, 1, 1, 1, 1),
                    Set_N1("n1_locker_stuff", 12, 2, 2, 2, 2),
                };
                var on = WorldStreamer.AutoSetNames(m);
                check("n1 sets: a DLC interior's lone option stays off (one theme is still one theme)",
                      !on.Contains("n1_trophies"), string.Join(", ", on));
                check("n1 sets: ...but the one set that dresses an otherwise empty room goes on",
                      on.Contains("n1_locker_stuff"), string.Join(", ", on));

                var m2 = Mlo_N1("ch_n1_dlc2", new Vector3(0, 0, 0), new Vector3(20, 10, 4));
                m2.rooms = m.rooms;
                m2.entitySets = new[] { Set_N1("n1_locker_a", 12, 2, 2), Set_N1("n1_locker_b", 12, 2, 2) };
                var on2 = WorldStreamer.AutoSetNames(m2);
                check("n1 sets: two sets over one empty room are alternatives - the rule does not stack them",
                      on2.Count <= 1, string.Join(", ", on2));

                var m3 = Mlo_N1("ch_n1_custom", new Vector3(0, 0, 0), new Vector3(20, 10, 4));
                m3.rooms = m.rooms;
                m3.entitySets = new[] { Set_N1("n1_cloth_room", 9, 1, 1) };
                m3.Ytyp = new YtypFile { FilePath = @"C:\some\stream\custom.ytyp" };
                var on3 = WorldStreamer.AutoSetNames(m3);
                check("n1 sets: a project / user interior is dressed like the base game's (its lone sets are on)",
                      on3.Contains("n1_cloth_room"), string.Join(", ", on3));
            }
        }

        private void RunWorldTestExtras_N1(Action<string, bool, string> check, Action<Vector3> settle)
        {
            if (!WorldBlock("intaudit")) return;
            var at = new Vector3(-805, 175, 72);
            settle(at);
            var cull = new InteriorCuller();
            int roomsOpen = 0, drawn = 0, tested = 0;
            bool extOpen = false;
            CameraSequence.ApplyToCamera(camera, at, 1.57f, 0.05f, settings.FovDeg);
            camera.Update();
            cull.Update(camera.Position, camera.GetForward(), camera.ViewProjMatrix, World.InteriorsEmitted, null);
            var saved = worldRender.InteriorCull;
            worldRender.InteriorCull = cull.Inside != null ? cull : null;
            worldRender.Update(World.Visible, gameFiles, modelRenderer, new BoundingFrustum(camera.ViewProjMatrix), true, World.Fade);
            worldRender.InteriorCull = saved;
            roomsOpen = cull.RoomsVisible; tested = cull.OwnTested; drawn = cull.OwnTested - cull.HiddenRooms; extOpen = cull.ExteriorOpen || cull.ExteriorRegions > 0;
            Console.WriteLine($"  INTAUDIT michael: {cull}");
            check("INTAUDIT: the eye in Michael's lounge is inside v_michael", (cull.Inside?.Archetype?.Name ?? "") == "v_michael", cull.ToString());
            check("INTAUDIT: more than one room of the house is open (the house is not one room of furniture)", roomsOpen >= 3, $"rooms open {roomsOpen}");
            check("INTAUDIT: the house's own props are drawn", tested == 0 || drawn > tested / 2, $"{drawn} of {tested}");
            check("INTAUDIT: the world outside is not hidden (a window is not a wall)", extOpen, cull.ExteriorWhy);
        }
    }
}

