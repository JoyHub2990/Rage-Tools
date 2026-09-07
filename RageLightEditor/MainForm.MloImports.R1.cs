using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void NoteYtypImport_R1(string path, MloImportResult result, RenderModel model)
        {
            if (scene == null || result == null) return;
            try { scene.Imports_R1.Note(path, result, model, result.Props); }
            catch (Exception ex) { Console.WriteLine("IMPORTREG note failed: " + ex.Message); }
        }

        partial void OnClearImports_R1()
        {
            try { scene?.Imports_R1?.Clear(); } catch { }
        }

        public string RemoveImportedYtyp_R1(string path, out bool ok) => RemoveImportedYtyp_R1(scene, path, out ok);

        public string RemoveImportedYtyp_R1(Scene sc, string path, out bool ok)
        {
            ok = false;
            bool live = ReferenceEquals(sc, scene);
            if (sc == null || string.IsNullOrEmpty(path)) return "nothing to remove";
            var reg = sc.Imports_R1;
            var entry = reg.Find(path);
            if (entry == null) return "that .ytyp is not one of the imports";
            string name = entry.Name;

            int meshes = sc.RemoveMloMeshes_R1(entry.Model?.Meshes);

            int props = 0, lights = 0;
            var doomed = entry.Props.Where(p => p != null && !reg.StillUsed(p, entry)).ToList();
            foreach (var p in doomed)
            {
                string key = p.Path ?? p.Name;
                for (int i = sc.Files.Count - 1; i >= 0; i--)
                {
                    var f = sc.Files[i];
                    if (f == null || !f.FromMlo) continue;
                    if (!string.Equals(f.Path, key, StringComparison.OrdinalIgnoreCase)) continue;
                    lights += sc.Lights.Count(l => sc.OwnerFile(l) == f);
                    sc.RemoveFile(f);
                    props++;
                }
            }

            reg.Remove(entry);
            var creatorEntities = live ? DropCreatorEntities_R1(entry) : 0;
            if (reg.Count == 0)
            {
                if (live) ClearImports();
                else { sc.RemoveMloProps(); sc.ClearMlo(); }
                ok = true;
                return $"Removed {name} - it was the last import, so the scene is empty again ({meshes} meshes, {props} props, {lights} lights).";
            }
            sc.SetMloInfo_R1(reg.MergeRemaining());
            if (live)
            {
                Creator?.Session?.InvalidateShellBounds();
                lastYtypPath = reg.Entries.Count > 0 ? reg.Entries[reg.Entries.Count - 1].Path : null;
                panel.MloStatus = $"Removed {name}: {meshes} meshes, {props} props, {lights} lights.\n" +
                                  $"Still imported: {string.Join(", ", reg.Entries.Select(e => e.Name))}";
            }
            Console.WriteLine($"IMPORTREMOVE {name}: {meshes} meshes, {props} props, {lights} lights, {creatorEntities} creator entities; {reg.Count} import(s) left");
            ok = true;
            return $"Removed {name}: {meshes} meshes, {props} props, {lights} lights. {reg.Count} import(s) left.";
        }

        private int DropCreatorEntities_R1(MloImportRegistry.Entry entry)
        {
            var s = Creator?.Session;
            if (s == null || entry?.Result == null) return 0;
            var infos = new HashSet<MloEntityInfo>(entry.Result.Entities);
            var archetypes = new HashSet<CodeWalker.GameFiles.Archetype>(
                entry.Result.Ytyps.SelectMany(y => y.Archetypes ?? new List<CodeWalker.GameFiles.Archetype>()).Where(a => a != null));
            var gone = s.Entities.Where(e => e.SourceInfo != null && infos.Contains(e.SourceInfo)).ToList();
            bool archGone = s.SourceArchetype != null && archetypes.Contains(s.SourceArchetype);
            if (gone.Count == 0 && !archGone) return 0;
            s.PushUndo("Remove import " + entry.Name);
            foreach (var e in gone) s.Entities.Remove(e);
            if (archGone) s.SourceArchetype = null;
            foreach (var p in s.Portals)
                for (int i = p.Attached.Count - 1; i >= 0; i--)
                    if (p.Attached[i] >= s.Entities.Count) p.Attached.RemoveAt(i);
            s.AutoAssignRooms();
            Creator.ClampSelection();
            return gone.Count;
        }

        public IReadOnlyList<MloImportRegistry.Entry> ImportedYtyps_R1 => scene?.Imports_R1?.Entries ?? Array.Empty<MloImportRegistry.Entry>();

        private string pendingRemoveImport_R1;

        private static readonly string removeYtypEnv_R1 = Environment.GetEnvironmentVariable("RLE_REMOVEYTYP");
        private int removeYtypTick_R1;
        private bool removeYtypDone_R1;

        private void ServiceImportRemoval_R1()
        {
            if (!removeYtypDone_R1 && removeYtypEnv_R1 != null && (DebugMlo == null || debugMloDone) && scene?.Imports_R1?.Count > 0)
            {
                var f = removeYtypEnv_R1.Split(',');
                int at = f.Length > 1 && int.TryParse(f[1], out var n) ? n : 3;
                screenshotFrames = Math.Max(screenshotFrames, 6);
                if (++removeYtypTick_R1 >= at)
                {
                    removeYtypDone_R1 = true;
                    var before = scene.Imports_R1.Entries.Select(e => e.Name).ToList();
                    Console.WriteLine($"REMOVEYTYP before: {scene.MloModel?.Meshes.Count ?? 0} meshes, {scene.Files.Count} props, {scene.Lights.Count} lights, imports [{string.Join(", ", before)}]");
                    pendingRemoveImport_R1 = f[0];
                }
            }
            var req = panel?.RequestRemoveImportedYtyp_R1;
            if (!string.IsNullOrEmpty(req)) { pendingRemoveImport_R1 = req; panel.RequestRemoveImportedYtyp_R1 = null; }
            var cui = Creator;
            if (cui != null && !string.IsNullOrEmpty(cui.RequestRemoveImportedYtyp_R1))
            {
                pendingRemoveImport_R1 = cui.RequestRemoveImportedYtyp_R1;
                cui.RequestRemoveImportedYtyp_R1 = null;
            }
            if (string.IsNullOrEmpty(pendingRemoveImport_R1)) return;
            string path = pendingRemoveImport_R1; pendingRemoveImport_R1 = null;
            string msg;
            bool ok;
            try { msg = RemoveImportedYtyp_R1(path, out ok); }
            catch (Exception ex) { msg = "could not remove it: " + ex.Message; ok = false; }
            Creator?.SetStatus(msg, !ok);
            if (!ok) panel.MloStatus = msg;
            if (removeYtypEnv_R1 != null)
                Console.WriteLine($"REMOVEYTYP after: ok {ok} - {msg}\n  now {scene.MloModel?.Meshes.Count ?? 0} meshes, {scene.Files.Count} props, {scene.Lights.Count} lights, " +
                                  $"imports [{string.Join(", ", scene.Imports_R1.Entries.Select(e => e.Name))}], creator rooms {Creator?.Session?.Rooms.Count ?? 0} entities {Creator?.Session?.Entities.Count ?? 0}");
        }
    }
}

