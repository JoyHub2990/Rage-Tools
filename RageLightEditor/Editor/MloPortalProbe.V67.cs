using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class MloPortalProbe_V67
    {
        public static int Run()
        {
            int fails = 0;
            fails += RunOne(false);
            fails += RunOne(true);
            Console.WriteLine(fails == 0 ? "PORTALPROBE PASSED" : $"PORTALPROBE FAILED ({fails})");
            return fails == 0 ? 0 : 1;
        }

        private static int RunOne(bool withPortal)
        {
            int fails = 0;
            void Say(string what, bool ok, string detail)
            {
                if (!ok) fails++;
                Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} [{(withPortal ? "portal" : "plain ")}] {what}  {detail}");
            }

            string dir = Path.Combine(Path.GetTempPath(), withPortal ? "rle_v67_portal" : "rle_v67_plain");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);

                var s = new MloCreatorSession { Name = "rle_v67_interior" };
                s.PhysicsDictionary = "rle_v67_interior";
                s.YmapPosition = new Vector3(250.0f, -800.0f, 40.0f);
                if (s.Rooms.Count == 0) s.AddRoom("limbo", new Vector3(-10, -10, -2), new Vector3(10, 10, 6));
                s.AddRoom("lounge", new Vector3(-6, -6, 0), new Vector3(6, 6, 4));
                s.BBMin = new Vector3(-10, -10, -2);
                s.BBMax = new Vector3(10, 10, 6);
                s.Entities.Add(new MloCreatorEntity
                {
                    ArchetypeName = "prop_bench_01a",
                    Position = new Vector3(1, 1, 0.5f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                    LodDist = 150.0f,
                    Include = true,
                    RoomOverride = 1,
                });
                if (withPortal)
                {
                    s.Portals.Add(new MloCreatorPortal
                    {
                        RoomFrom = 0,
                        RoomTo = 1,
                        Corners = new[]
                        {
                            new Vector3(-0.6f, 6.0f, 0.0f),
                            new Vector3(-0.6f, 6.0f, 2.2f),
                            new Vector3(0.6f, 6.0f, 2.2f),
                            new Vector3(0.6f, 6.0f, 0.0f),
                        },
                    });
                }

                var problems = s.Validate();
                Say("session validates", problems.Count == 0, problems.Count == 0 ? "clean" : problems[0]);

                var r = s.ExportForGame_V31(dir);
                Say("export writes files", r.Ok, r.Ok ? string.Join(", ", r.Written.Select(Path.GetFileName)) : r.Error);
                if (!r.Ok) return fails;

                var ytypPath = Directory.GetFiles(dir, "*.ytyp").FirstOrDefault();
                var ymapPath = Directory.GetFiles(dir, "*.ymap").FirstOrDefault();
                Say("ytyp and ymap on disk", ytypPath != null && ymapPath != null,
                    $"{Path.GetFileName(ytypPath)} {Path.GetFileName(ymapPath)}");
                if (ytypPath == null || ymapPath == null) return fails;

                YtypFile ytyp = null;
                string loadErr = null;
                try
                {
                    ytyp = new YtypFile();
                    ytyp.Load(File.ReadAllBytes(ytypPath));
                }
                catch (Exception ex) { loadErr = ex.Message; ytyp = null; }
                Say("CodeWalker.Core loads the ytyp back", ytyp != null, loadErr ?? "loaded");
                if (ytyp == null) return fails;

                var mlo = ytyp.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
                Say("the MLO archetype survives the round trip", mlo != null,
                    $"{ytyp.AllArchetypes?.Length ?? 0} archetypes");
                if (mlo == null) return fails;

                Say("rooms survive", (mlo.rooms?.Length ?? 0) == 2,
                    $"{mlo.rooms?.Length ?? 0} rooms: {string.Join(", ", (mlo.rooms ?? Array.Empty<MCMloRoomDef>()).Select(x => x.RoomName))}");
                Say("entities survive", (mlo.entities?.Length ?? 0) >= 1,
                    $"{mlo.entities?.Length ?? 0} entities");
                if (withPortal)
                {
                    Say("the portal survives", (mlo.portals?.Length ?? 0) == 1, $"{mlo.portals?.Length ?? 0} portals");
                    var p = mlo.portals?.FirstOrDefault();
                    if (p != null)
                    {
                        Say("portal rooms and corners intact",
                            p._Data.roomFrom == 0 && p._Data.roomTo == 1 && (p.Corners?.Length ?? 0) == 4,
                            $"from {p._Data.roomFrom} to {p._Data.roomTo}, {p.Corners?.Length ?? 0} corners");
                        Say("room portal counts updated",
                            mlo.rooms[0]._Data.portalCount >= 1 && mlo.rooms[1]._Data.portalCount >= 1,
                            $"limbo {mlo.rooms[0]._Data.portalCount}, lounge {mlo.rooms[1]._Data.portalCount}");
                    }
                }

                YmapFile ymap = null;
                try
                {
                    ymap = new YmapFile();
                    ymap.Load(File.ReadAllBytes(ymapPath));
                }
                catch (Exception ex) { loadErr = ex.Message; ymap = null; }
                Say("CodeWalker.Core loads the ymap back", ymap != null, loadErr ?? "loaded");
                if (ymap == null) return fails;

                var inst = ymap.MloEntities?.FirstOrDefault();
                Say("the ymap places one MLO instance", inst != null,
                    $"{ymap.MloEntities?.Length ?? 0} instances");
                if (inst == null) return fails;

                string setErr = null;
                try { inst.SetArchetype(mlo); }
                catch (Exception ex) { setErr = ex.Message; }
                Say("the instance binds to the archetype the way the world does", setErr == null, setErr ?? "bound");

                var built = inst.MloInstance?.Entities?.Length ?? 0;
                Say("the interior entities come to life", built >= 1, $"{built} instance entities");
            }
            catch (Exception ex)
            {
                Say("probe crashed", false, ex.ToString());
            }
            return fails;
        }
    }
}
