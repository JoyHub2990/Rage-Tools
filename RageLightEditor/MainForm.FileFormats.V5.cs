using System;
using System.Collections.Generic;
using System.Text.Json;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly struct FormatCase_V5
        {
            public readonly string What;
            public readonly object Sample;
            public readonly string[] Keys;
            public FormatCase_V5(string what, object sample, params string[] keys)
            { What = what; Sample = sample; Keys = keys; }
        }

        private static List<FormatCase_V5> FileFormats_V5()
        {
            var mlo = new MloCreatorProject.Dto
            {
                Name = "seqtest_interior",
                ShellName = "v5_shell",
                TextureDictionary = "v5_txd",
                LodDist = 250f,
                BBMin = new MloCreatorProject.V3 { X = -8f, Y = -6f, Z = 0f },
                BBMax = new MloCreatorProject.V3 { X = 8f, Y = 6f, Z = 4f },
                Rooms =
                {
                    new MloCreatorProject.RoomDto
                    {
                        Name = "v5_room", Blend = 0.25f, FloorId = 2, Flags = 96u,
                        Min = new MloCreatorProject.V3 { X = -4f, Y = -3f, Z = 0f },
                        Max = new MloCreatorProject.V3 { X = 4f, Y = 3f, Z = 3f },
                    },
                },
                Portals =
                {
                    new MloCreatorProject.PortalDto
                    {
                        RoomFrom = 0, RoomTo = 1, Opacity = 3u,
                        Corners = new List<MloCreatorProject.V3>
                        {
                            new MloCreatorProject.V3 { X = 1f, Y = 2f, Z = 3f },
                        },
                    },
                },
                Entities =
                {
                    new MloCreatorProject.EntityDto
                    {
                        ArchetypeName = "v5_prop", LodDist = 120f, Include = true, RoomOverride = 1,
                        Position = new MloCreatorProject.V3 { X = 1.5f, Y = -2.5f, Z = 0.75f },
                        Rotation = new MloCreatorProject.V4 { X = 0f, Y = 0f, Z = 0.7071f, W = 0.7071f },
                    },
                },
            };

            var settings = new AppSettings();
            settings.SectionCameras.Add(new SectionCameraPref
            {
                Section = SpaceNames.NameOf(LightPanel.Space.World),
                TargetX = 12f, TargetY = -34f, TargetZ = 56f, Distance = 7f,
            });

            return new List<FormatCase_V5>
            {
                new FormatCase_V5("an MLO Creator project", mlo,
                                  "Name", "Rooms", "Portals", "Entities", "ShellName", "BBMin", "LodDist"),
                new FormatCase_V5("...its rooms", mlo.Rooms[0], "Name", "Min", "Max", "Blend", "FloorId"),
                new FormatCase_V5("...its portals", mlo.Portals[0], "RoomFrom", "RoomTo", "Corners", "Opacity"),
                new FormatCase_V5("...its props", mlo.Entities[0], "ArchetypeName", "Position", "Rotation", "LodDist"),
                new FormatCase_V5("...and a position in it", mlo.BBMin, "X", "Y", "Z"),

                new FormatCase_V5("the settings file", settings, "SectionCameras", "LastWorkspace"),
                new FormatCase_V5("...one section's camera", settings.SectionCameras[0],
                                  "Section", "TargetX", "TargetY", "TargetZ", "Distance"),

                new FormatCase_V5("a prop in the assets library cache",
                                  new LightPropEntry { Name = "v5_prop", Hash = 1234u, Path = "x:/v5.ydr" },
                                  "Name", "Hash", "Path"),
            };
        }

        partial void SeqTest_V5(Action<string, bool, string> check)
        {
            foreach (var f in FileFormats_V5())
            {
                string json;
                try { json = JsonSerializer.Serialize(f.Sample, f.Sample.GetType()); }
                catch (Exception e) { check("v5: " + f.What + " can be written", false, e.Message); continue; }

                var missing = new List<string>();
                foreach (var k in f.Keys)
                    if (!json.Contains("\"" + k + "\"", StringComparison.Ordinal)) missing.Add(k);

                string detail = json.Length <= 2 ? "wrote " + json + " - EVERY name was renamed"
                              : missing.Count > 0 ? "no " + string.Join(", ", missing) + " in " + Head_V5(json)
                              : Head_V5(json);
                check("v5: " + f.What + " keeps its names in the file", missing.Count == 0, detail);
            }

            var mlo = (MloCreatorProject.Dto)FileFormats_V5()[0].Sample;
            var text = JsonSerializer.Serialize(mlo);
            var back = JsonSerializer.Deserialize<MloCreatorProject.Dto>(text);
            check("v5: ...and an MLO Creator project reloads with its rooms, portals and props",
                  back != null && back.Name == mlo.Name && back.Rooms.Count == 1 && back.Portals.Count == 1 &&
                  back.Entities.Count == 1 && back.Rooms[0].Name == "v5_room" &&
                  back.Entities[0].ArchetypeName == "v5_prop" && back.BBMax != null &&
                  Math.Abs(back.BBMax.X - 8f) < 0.001f,
                  back == null ? "nothing came back"
                               : $"{back.Rooms?.Count ?? 0} room(s), {back.Portals?.Count ?? 0} portal(s), {back.Entities?.Count ?? 0} prop(s)");
        }

        private static string Head_V5(string s) =>
            s.Length > 110 ? s.Substring(0, 110).Replace("\n", " ") + "..." : s.Replace("\n", " ");
    }
}

