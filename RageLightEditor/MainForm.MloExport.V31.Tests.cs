using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_MloExport_V31(Action<string, bool, string> check)
        {
            string dir = Path.Combine(Path.GetTempPath(), "rle_v31_mlo");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);

                var s = new MloCreatorSession { Name = "rle_v31_interior" };
                s.PhysicsDictionary = "rle_v31_interior";
                s.YmapPosition = new SharpDX.Vector3(120.5f, -430.25f, 42.0f);
                s.YmapHeadingDeg = 35.0f;
                if (s.Rooms.Count == 0) s.AddRoom("limbo", new SharpDX.Vector3(-8, -8, -2), new SharpDX.Vector3(8, 8, 5));
                s.BBMin = s.Rooms[0].Min; s.BBMax = s.Rooms[0].Max;

                var problems = s.Validate();
                check("v31 mlo: an interior with a name, a room and a physics dictionary validates",
                      problems.Count == 0, problems.Count == 0 ? "no problems" : problems[0]);

                var r = s.ExportForGame_V31(dir);
                check("v31 mlo: one export writes the .ytyp, the .ymap AND the _manifest.ymf",
                      r.Ok && r.Written.Any(f => f.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase))
                           && r.Written.Any(f => f.EndsWith(".ymap", StringComparison.OrdinalIgnoreCase))
                           && r.Written.Any(f => f.IndexOf("_manifest", StringComparison.OrdinalIgnoreCase) >= 0),
                      r.Ok ? string.Join(", ", r.Written) : "failed: " + r.Error);
                if (!r.Ok) return;

                var ymapPath = Directory.GetFiles(dir, "*.ymap").FirstOrDefault();
                check("v31 mlo: a .ymap was written", ymapPath != null, ymapPath ?? "none");
                if (ymapPath == null) return;

                var back = new YmapFile();
                back.Load(File.ReadAllBytes(ymapPath));
                int mloCount = back.MloEntities?.Length ?? 0;
                var inst = back.MloEntities?.FirstOrDefault();
                check("v31 mlo: CodeWalker's own loader reads it back AS AN MLO INSTANCE",
                      mloCount == 1 && inst?.MloInstance != null,
                      $"{mloCount} MLO instance(s), {back.AllEntities?.Length ?? 0} entity total");

                check("v31 mlo: ...pointing at this interior's archetype, at the placement asked for",
                      inst != null && inst.CEntityDef.archetypeName.Hash == JenkHash.GenHash("rle_v31_interior") &&
                      Math.Abs(inst.CEntityDef.position.X - 120.5f) < 0.01f,
                      inst == null ? "no instance" : $"{inst.CEntityDef.archetypeName} at {inst.CEntityDef.position.X:0.0}, {inst.CEntityDef.position.Y:0.0}");

                check("v31 mlo: ...and the ymap declares the interior content flag the game looks for",
                      (back.CMapData.contentFlags & 8) != 0 && (back.CMapData.contentFlags & 1) != 0,
                      "contentFlags " + back.CMapData.contentFlags);

                var ytypPath = Directory.GetFiles(dir, "*.ytyp").FirstOrDefault();
                var ytyp = new YtypFile();
                ytyp.Load(File.ReadAllBytes(ytypPath));
                var arch = ytyp.AllArchetypes?.OfType<MloArchetype>().FirstOrDefault();
                check("v31 mlo: the .ytyp names its collision dictionary, so the interior has a floor",
                      arch != null && arch._BaseArchetypeDef.physicsDictionary.Hash == JenkHash.GenHash("rle_v31_interior"),
                      arch == null ? "no MLO archetype" : "physicsDictionary " + arch._BaseArchetypeDef.physicsDictionary);

                var man = File.ReadAllText(Directory.GetFiles(dir, "*_manifest*").First());
                var ymapStem = Path.GetFileNameWithoutExtension(ymapPath);
                check("v31 mlo: the manifest says this .ymap depends on this .ytyp - the line that makes it load",
                      man.Contains("<imapName>" + ymapStem + "</imapName>") &&
                      man.Contains("<Item>rle_v31_interior</Item>"),
                      man.Contains("imapDependencies_2") ? "imapDependencies_2 with the itypDepArray" : "no dependency block");

                var shellYdr = Path.Combine(dir, "rle_v31_shell.ydr");
                File.WriteAllBytes(shellYdr, new byte[16]);
                File.WriteAllBytes(Path.Combine(dir, "rle_v31_shell.ybn"), new byte[16]);
                check("v31 mlo: a .ybn beside the model is found",
                      MloCreatorSession.SiblingYbn_V31(shellYdr) != null,
                      MloCreatorSession.SiblingYbn_V31(shellYdr) ?? "not found");

                var s2 = new MloCreatorSession { Name = "rle_v31_nocol" };
                if (s2.Rooms.Count == 0) s2.AddRoom("limbo", new SharpDX.Vector3(-4, -4, -1), new SharpDX.Vector3(4, 4, 3));
                s2.BBMin = s2.Rooms[0].Min; s2.BBMax = s2.Rooms[0].Max;
                s2.PhysicsDictionary = "";
                s2.ShellFile = new LoadedFile { Path = shellYdr };
                var warn = s2.CollisionWarnings_V31();
                check("v31 mlo: a shell WITH collision and no Physics field set is reported, not exported in silence",
                      warn.Count > 0 && warn[0].Contains("rle_v31_shell.ybn"),
                      warn.Count > 0 ? warn[0].Substring(0, Math.Min(80, warn[0].Length)) + "..." : "said nothing");

                var s3 = new MloCreatorSession { Name = "rle_v31_assetless", PhysicsDictionary = "" };
                check("v31 mlo: ...but an interior with no shell at all is not nagged about it",
                      s3.CollisionWarnings_V31().Count == 0, "quiet");
            }
            catch (Exception ex) { check("v31 mlo: the export round trip", false, ex.Message); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}

