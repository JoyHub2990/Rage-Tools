using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_MloPortal_V67(Action<string, bool, string> check)
        {
            try
            {
                check("v67 portals: an interior with a portal round-trips through CodeWalker's own loader",
                      MloPortalProbe_V67.Run() == 0, "see PORTALPROBE lines above");

                var dir = Path.Combine(Path.GetTempPath(), "rle_v67_portal");
                var ymapPath = Directory.GetFiles(dir, "*.ymap").FirstOrDefault();
                var ytypPath = Directory.GetFiles(dir, "*.ytyp").FirstOrDefault();
                if (ymapPath == null || ytypPath == null)
                {
                    check("v67 portals: the probe left its export on disk", false, dir);
                    return;
                }

                var p = new CwProject();
                var y = p.AddYmapFile(ymapPath);
                y.Load(File.ReadAllBytes(ymapPath));
                y.FilePath = ymapPath;
                y.RpfFileEntry ??= new RpfResourceFileEntry();
                y.RpfFileEntry.Name = Path.GetFileName(ymapPath);
                y.Name = y.RpfFileEntry.Name;
                p.InitYmapArchetypes(y, null);
                y.Loaded = true;

                var inst = y.MloEntities?.FirstOrDefault() ?? y.AllEntities?.FirstOrDefault();
                check("v67 portals: a ymap opened alone leaves the interior as a bare box",
                      inst != null && inst.Archetype == null,
                      inst == null ? "no instance" : (inst.Archetype?.Name ?? "unresolved, as expected"));

                var (connected, missing, loaded) = ProjectController.ReconnectArchetypes_V67(p, null, null);
                check("v67 portals: ...and the sibling .ytyp is pulled in from beside it",
                      loaded.Count == 1 && connected >= 1 && missing == 0,
                      $"loaded {string.Join(", ", loaded)}; connected {connected}, still missing {missing}");

                check("v67 portals: the interior now stands with its entities",
                      inst != null && inst.Archetype is MloArchetype &&
                      (inst.MloInstance?.Entities?.Length ?? 0) >= 1,
                      $"{inst?.MloInstance?.Entities?.Length ?? 0} instance entities");
            }
            catch (Exception ex) { check("v67 portals", false, ex.Message); }
        }

        private void SeqTest_ShellPortalShift_V67(Action<string, bool, string> check)
        {
            try
            {
                var s = new MloCreatorSession { Name = "rle_v67_shift" };
                if (s.Rooms.Count == 0) s.AddRoom("limbo", new SharpDX.Vector3(-5, -5, -1), new SharpDX.Vector3(5, 5, 3));
                s.Entities.Add(new MloCreatorEntity { ArchetypeName = "prop_a", Include = true });
                s.Entities.Add(new MloCreatorEntity { ArchetypeName = "prop_b", Include = true });
                s.Portals.Add(new MloCreatorPortal { RoomFrom = 0, RoomTo = 0, Attached = { 1 } });

                s.ShellInLimbo_V33 = true;
                s.ShellFile = new LoadedFile { Path = "rle_v67_shell.ydr" };
                s.SyncShellEntity_V33();

                check("v67 shell: inserting the shell at the front moves portal attachments with it",
                      s.Entities.Count == 3 && s.Entities[0].IsShell_V33 &&
                      s.Portals[0].Attached.Count == 1 && s.Portals[0].Attached[0] == 2 &&
                      s.Entities[s.Portals[0].Attached[0]].ArchetypeName == "prop_b",
                      $"attached to index {string.Join(",", s.Portals[0].Attached)}");

                s.ShellInLimbo_V33 = false;
                s.SyncShellEntity_V33();
                check("v67 shell: removing it moves them back",
                      s.Entities.Count == 2 && s.Portals[0].Attached.Count == 1 && s.Portals[0].Attached[0] == 1 &&
                      s.Entities[s.Portals[0].Attached[0]].ArchetypeName == "prop_b",
                      $"attached to index {string.Join(",", s.Portals[0].Attached)}");
            }
            catch (Exception ex) { check("v67 shell shift", false, ex.Message); }
        }
    }
}
