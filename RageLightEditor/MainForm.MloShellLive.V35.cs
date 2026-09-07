using System;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private LoadedFile shellSeen_V35;
        private bool shellWanted_V35 = true;

        private void SyncShellEntityLive_V35(MloCreatorPanel ui, MloCreatorSession s)
        {
            if (ui == null || s == null) return;

            bool wanted = s.ShellGoesInLimbo_V33;
            bool has = s.Entities.Any(e => e != null && e.IsShell_V33);
            if (ReferenceEquals(shellSeen_V35, s.ShellFile) && wanted == shellWanted_V35 && has == wanted) return;

            shellSeen_V35 = s.ShellFile;
            shellWanted_V35 = wanted;

            int before = s.Entities.Count;
            s.SyncShellEntity_V33();
            int after = s.Entities.Count;

            var shell = s.Entities.FirstOrDefault(e => e != null && e.IsShell_V33);
            if (after > before && shell != null)
            {
                if (shell.SourceFile != null) mloEntityFiles_N3.Add(shell.SourceFile);
                ui.SetStatus($"{shell.ArchetypeName} added to limbo as the shell entity - it will be written into the .ytyp.");
                Console.WriteLine($"MLOSHELL {shell.ArchetypeName} -> limbo (room 0) as an entity");
            }
            else if (after < before)
            {
                ui.SetStatus("The shell entity was taken back out of limbo.");
                Console.WriteLine("MLOSHELL shell entity removed");
            }
        }

        private void SeqTest_MloShellLive_V35(Action<string, bool, string> check)
        {
            var ui = Creator;
            if (ui == null) { Console.WriteLine("  v35 shell: (skipped - no creator panel)"); return; }
            var keep = ui.Session;
            try
            {
                ui.Session = new MloCreatorSession { Name = "rle_v35" };
                var s = ui.Session;
                if (s.Rooms.Count == 0) s.AddRoom("limbo", new SharpDX.Vector3(-9, -9, -3), new SharpDX.Vector3(9, 9, 6));
                s.BBMin = s.Rooms[0].Min; s.BBMax = s.Rooms[0].Max;

                check("v35 shell: with no shell open there is no shell entity",
                      !s.Entities.Any(e => e.IsShell_V33), "none");

                s.ShellFile = new LoadedFile { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "neonix_mlo_3dm.ydr") };
                s.ShellName = "neonix_mlo_3dm";
                shellSeen_V35 = null;
                SyncShellEntityLive_V35(ui, s);

                var shell = s.Entities.FirstOrDefault(e => e.IsShell_V33);
                check("v35 shell: opening a shell puts it in LIMBO immediately, before any export",
                      shell != null && shell.Room == 0 && shell.ArchetypeName == "neonix_mlo_3dm",
                      shell == null ? "no shell entity" : $"{shell.ArchetypeName} in room {shell.Room}");

                int n = s.Entities.Count;
                shellSeen_V35 = null;
                SyncShellEntityLive_V35(ui, s);
                check("v35 shell: ...and it is not added twice",
                      s.Entities.Count == n, $"{n} -> {s.Entities.Count}");

                s.TextureDictionary = "rle_v35";
                var ytyp = s.BuildYtyp("rle_v35.ytyp");
                var arch = ytyp.AllArchetypes?.OfType<CodeWalker.GameFiles.MloArchetype>().FirstOrDefault();
                int shellEnts = arch?.entities?.Count(x =>
                    x?._Data.archetypeName.Hash == CodeWalker.GameFiles.JenkHash.GenHash("neonix_mlo_3dm")) ?? 0;
                check("v35 shell: ...and the exported .ytyp has exactly one of it",
                      shellEnts == 1, $"{shellEnts} shell entity(ies) written");
            }
            catch (Exception ex) { check("v35 shell: the live shell entity", false, ex.Message); }
            finally { ui.Session = keep; shellSeen_V35 = null; }
        }
    }
}

