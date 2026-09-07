using System;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void ServiceMloAddEntity_V32(MloCreatorPanel ui)
        {
            if (ui == null || !ui.RequestAddEntityPlace_V32) return;
            ui.RequestAddEntityPlace_V32 = false;

            var s = ui.Session;
            if (s == null) { ui.SetStatus("No interior yet - press New first.", true); return; }

            var name = (ui.AddEntityName_V32 ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name)) name = PlaceholderEntityName_V33(s);

            try
            {
                var origin = MloPlacementPoint_L3(ui);
                s.PushUndo("Add entity " + name);
                var e = new MloCreatorEntity
                {
                    ArchetypeName = name,
                    Position = origin,
                    Rotation = SharpDX.Quaternion.Identity,
                    Scale = SharpDX.Vector3.One,
                    LodDist = 200.0f,
                    Include = true,
                    RoomOverride = ui.SelectedRoom >= 0 && ui.SelectedRoom < s.Rooms.Count ? ui.SelectedRoom : 0,
                };
                s.Entities.Add(e);

                int before = s.Entities.Count(x => x.SourceFile != null);
                ResolveTypedEntities_L3(ui);
                int after = s.Entities.Count(x => x.SourceFile != null);

                string roomName = e.RoomOverride >= 0 && e.RoomOverride < s.Rooms.Count
                    ? s.Rooms[e.RoomOverride].Name : "limbo";
                ui.SelectEntity(s.Entities.Count - 1);
                ui.SetStatus(after > before
                    ? $"Added {name} to {roomName} - model found and placed."
                    : $"Added {name} to {roomName}. No model of that name in the archives or your folders, " +
                      "so it is listed but not drawn - the game will still place it if the name is right.");
                Console.WriteLine($"MLOADDENT {name} at {origin.X:0.##},{origin.Y:0.##},{origin.Z:0.##} resolved={after > before}");
            }
            catch (Exception ex) { ui.SetStatus("Could not add it: " + ex.Message, true); }
        }

        private string PlaceholderEntityName_V33(MloCreatorSession s)
        {
            var real = RandomGameProp_V35();
            if (real != null) return real;
            for (int i = 1; i < 10000; i++)
            {
                var n = "new_entity_" + i;
                if (!s.Entities.Any(e => string.Equals(e.ArchetypeName, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
            return "new_entity";
        }

        private static readonly Random propRng_V35 = new Random();
        private System.Collections.Generic.List<string> propPool_V35;

        private string RandomGameProp_V35()
        {
            try
            {
                if (propPool_V35 == null)
                {
                    var dict = gameFiles?.Cache?.YdrDict;
                    if (dict == null || !gameFiles.Ready) return null;
                    propPool_V35 = dict.Values
                        .Where(e => e?.Name != null)
                        .Select(e => System.IO.Path.GetFileNameWithoutExtension(e.Name).ToLowerInvariant())
                        .Where(n => n.StartsWith("prop_") && n.Length < 34)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                if (propPool_V35.Count == 0) return null;
                return propPool_V35[propRng_V35.Next(propPool_V35.Count)];
            }
            catch { return null; }
        }

        private void SeqTest_MloAddEntity_V32(Action<string, bool, string> check)
        {
            var ui = Creator;
            if (ui == null) { Console.WriteLine("  v32 add entity: (skipped - no creator panel)"); return; }
            var keep = ui.Session;
            try
            {
                ui.Session = new MloCreatorSession { Name = "rle_v32_addent" };
                var s = ui.Session;
                if (s.Rooms.Count == 0) s.AddRoom("limbo", new SharpDX.Vector3(-6, -6, -2), new SharpDX.Vector3(6, 6, 4));
                s.BBMin = s.Rooms[0].Min; s.BBMax = s.Rooms[0].Max;
                int before = s.Entities.Count;

                ui.AddEntityName_V32 = "prop_bench_01a";
                ui.RequestAddEntityPlace_V32 = true;
                ServiceMloAddEntity_V32(ui);

                check("v32 add entity: typing a name adds it to the interior",
                      s.Entities.Count == before + 1 &&
                      s.Entities[s.Entities.Count - 1].ArchetypeName == "prop_bench_01a",
                      $"{before} -> {s.Entities.Count}");

                int n = s.Entities.Count;
                ui.AddEntityName_V32 = "   ";
                ui.RequestAddEntityPlace_V32 = true;
                ServiceMloAddEntity_V32(ui);
                var placed = s.Entities.Count > n ? s.Entities[s.Entities.Count - 1].ArchetypeName ?? "" : "";
                check("v35 add entity: an empty name places a REAL base-game prop you can see and rename",
                      s.Entities.Count == n + 1 && (placed.StartsWith("prop_") || placed.StartsWith("new_entity")),
                      s.Entities.Count > n ? placed : "nothing added");

                check("v33 add entity: it lands in the selected room, or limbo when none is",
                      s.Entities[s.Entities.Count - 1].Room == Math.Max(ui.SelectedRoom, 0),
                      $"room {s.Entities[s.Entities.Count - 1].Room}");

                s.TextureDictionary = "rle_v32_addent";
                var ytyp = s.BuildYtyp("rle_v32_addent.ytyp");
                var arch = ytyp.AllArchetypes?.OfType<CodeWalker.GameFiles.MloArchetype>().FirstOrDefault();
                bool inYtyp = arch?.entities?.Any(x =>
                    x?._Data.archetypeName.Hash == CodeWalker.GameFiles.JenkHash.GenHash("prop_bench_01a")) ?? false;
                check("v32 add entity: ...and the .ytyp it exports names that entity",
                      inYtyp, $"{arch?.entities?.Length ?? 0} entity(ies) in the archetype");

                var s3 = new MloCreatorSession { Name = "rle_v33_shell", TextureDictionary = "rle_v33_shell" };
                if (s3.Rooms.Count == 0) s3.AddRoom("limbo", new SharpDX.Vector3(-9, -9, -3), new SharpDX.Vector3(9, 9, 6));
                s3.BBMin = s3.Rooms[0].Min; s3.BBMax = s3.Rooms[0].Max;
                s3.ShellFile = new LoadedFile { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_v33_shellmodel.ydr") };
                s3.ShellName = "rle_v33_shellmodel";

                s3.AssetLess = true;
                check("v34 shell: the shell goes into limbo as an entity by default",
                      s3.ShellGoesInLimbo_V33, "on");
                s3.AssetLess = false;
                check("v34 shell: ...still on for a drawable archetype, because that is what was asked for",
                      s3.ShellGoesInLimbo_V33, "on");
                check("v34 shell: ...but the double-draw that risks is reported",
                      s3.ShellDrawnTwice_V34 && s3.ShellInLimboNote_V33.Contains("twice"),
                      s3.ShellInLimboNote_V33.Substring(0, Math.Min(60, s3.ShellInLimboNote_V33.Length)) + "...");
                s3.AssetLess = true;

                var yt3 = s3.BuildYtyp("rle_v33_shell.ytyp");
                var arch3 = yt3.AllArchetypes?.OfType<CodeWalker.GameFiles.MloArchetype>().FirstOrDefault();
                bool shellIsEntity = arch3?.entities?.Any(x =>
                    x?._Data.archetypeName.Hash == CodeWalker.GameFiles.JenkHash.GenHash("rle_v33_shellmodel")) ?? false;
                bool inLimbo = (arch3?.rooms?.FirstOrDefault()?.AttachedObjects?.Length ?? 0) > 0;
                check("v33 shell: ...and the exported .ytyp carries it as an entity attached to room 0",
                      shellIsEntity && inLimbo,
                      $"entity {shellIsEntity}, attached to limbo {inLimbo}");

                s3.ShellInLimbo_V33 = false;
                var yt4 = s3.BuildYtyp("rle_v33_shell.ytyp");
                var arch4 = yt4.AllArchetypes?.OfType<CodeWalker.GameFiles.MloArchetype>().FirstOrDefault();
                bool gone = !(arch4?.entities?.Any(x =>
                    x?._Data.archetypeName.Hash == CodeWalker.GameFiles.JenkHash.GenHash("rle_v33_shellmodel")) ?? false);
                check("v33 shell: ...and unticking it takes the shell entity back out",
                      gone, gone ? "gone" : "still there");

                var ybnDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rle_v34_ybn");
                System.IO.Directory.CreateDirectory(ybnDir);
                var notYbn = System.IO.Path.Combine(ybnDir, "rubbish.ybn");
                System.IO.File.WriteAllBytes(notYbn, new byte[64]);
                var s4 = new MloCreatorSession { Name = "rle_v34_col" };
                check("v34 collision: a file that is not really a .ybn is refused, not named in the .ytyp",
                      !s4.ImportShellYbn_V34(notYbn, out var whyNot) && string.IsNullOrEmpty(s4.PhysicsDictionary),
                      whyNot ?? "(no reason given)");
                check("v34 collision: a missing file is refused too",
                      !s4.ImportShellYbn_V34(System.IO.Path.Combine(ybnDir, "nope.ybn"), out _), "refused");

                var cache = gameFiles?.Cache;
                var srcYbn = cache?.YbnDict?.Values?.FirstOrDefault(x => x != null);
                if (srcYbn != null)
                {
                    var realYbn = System.IO.Path.Combine(ybnDir, "rle_v34_collision.ybn");
                    System.IO.File.WriteAllBytes(realYbn, ArchiveBrowser.ExtractForDisk(srcYbn));
                    bool ok4 = s4.ImportShellYbn_V34(realYbn, out var why4);
                    check("v34 collision: importing a real .ybn names it in the archetype",
                          ok4 && s4.PhysicsDictionary == "rle_v34_collision",
                          ok4 ? s4.ShellYbnNote_V34 : "failed: " + why4);
                    check("v34 collision: ...and the export ships it beside the .ytyp",
                          s4.CollisionFiles_V31().Any(f => f.EndsWith("rle_v34_collision.ybn", StringComparison.OrdinalIgnoreCase)),
                          string.Join(", ", s4.CollisionFiles_V31().Select(System.IO.Path.GetFileName)));
                    check("v34 collision: ...so the interior no longer reports missing collision",
                          s4.CollisionWarnings_V31().Count == 0, "quiet");
                }
                try { System.IO.Directory.Delete(ybnDir, true); } catch { }
            }
            catch (Exception ex) { check("v32 add entity: the typed-entity path", false, ex.Message); }
            finally { ui.Session = keep; }
        }
    }
}

