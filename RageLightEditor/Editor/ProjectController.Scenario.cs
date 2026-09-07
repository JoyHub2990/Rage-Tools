using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.World;

namespace RageLightEditor.Editor
{
    public partial class ProjectController
    {
        public event Action ScenarioFilesChanged;

        private void TickScenario_I2()
        {
            if (win.RequestOpenScenario != null) { var rel = win.RequestOpenScenario; win.RequestOpenScenario = null; OpenScenarioFile(rel); }
            if (win.RequestSaveScenario) { win.RequestSaveScenario = false; SaveScenario(win.CurrentScenario, false); }
            if (win.RequestSaveScenarioAs) { win.RequestSaveScenarioAs = false; SaveScenario(win.CurrentScenario, true); }
            if (win.RequestRemoveScenario) { win.RequestRemoveScenario = false; RemoveScenario(win.CurrentScenario); }
        }

        private void LoadScenarioIntoProject(string file)
        {
            var p = win.Project;
            if (p == null) return;
            if (win.FindScenarioFile(p.GetRelativePath(file)) != null) return;
            try
            {
                var ymt = LoadScenarioYmt(file, cache());
                if (ymt == null) return;
                if (!p.ScenarioFiles.Contains(ymt)) p.ScenarioFiles.Add(ymt);
                ScenarioFilesChanged?.Invoke();
            }
            catch (Exception ex) { Console.WriteLine($"SCENARIO {Path.GetFileName(file)}: {ex.Message}"); }
        }

        private void LoadProjectScenarios(CwProject p, System.Collections.Generic.List<string> problems)
        {
            if (p == null) return;
            foreach (var rel in p.ScenarioFilenames.ToList())
            {
                var full = p.GetFullFilePath(rel);
                try
                {
                    if (!File.Exists(full)) { problems?.Add("missing scenario ymt: " + rel); continue; }
                    var ymt = LoadScenarioYmt(full, cache());
                    if (ymt == null) { problems?.Add(rel + ": not a scenario point region"); continue; }
                    if (!p.ScenarioFiles.Contains(ymt)) p.ScenarioFiles.Add(ymt);
                }
                catch (Exception ex) { problems?.Add($"{rel}: {ex.Message}"); }
            }
        }

        public YmtFile OpenScenarioFile(string rel)
        {
            var p = win.Project;
            if (p == null || string.IsNullOrEmpty(rel)) return null;
            var already = win.FindScenarioFile(rel);
            if (already != null) { win.ShowScenario(already); return already; }
            var full = p.GetFullFilePath(rel);
            if (!File.Exists(full))
            {
                win.Status = "scenario file not found on disk: " + rel;
                return null;
            }
            YmtFile ymt;
            try { ymt = LoadScenarioYmt(full, cache()); }
            catch (Exception ex) { win.Status = Path.GetFileName(full) + ": " + ex.Message; return null; }
            if (ymt == null) { win.Status = Path.GetFileName(full) + " is not a scenario point region"; return null; }
            if (!p.ScenarioFiles.Contains(ymt)) p.AddScenarioFile(ymt);
            var relNow = p.GetRelativePath(full);
            if (!string.Equals(relNow, rel, StringComparison.OrdinalIgnoreCase) && p.ScenarioFilenames.Contains(relNow) && p.ScenarioFilenames.Contains(rel))
                p.ScenarioFilenames.Remove(relNow);
            win.ShowScenario(ymt);
            win.Status = $"opened {ymt.Name}: {ymt.ScenarioRegion?.Nodes?.Count ?? 0} scenario point(s)";
            ScenarioFilesChanged?.Invoke();
            return ymt;
        }

        public static YmtFile LoadScenarioYmt(string full, GameFileCache gfc)
        {
            var ymt = new YmtFile();
            ymt.Load(File.ReadAllBytes(full));
            if (ymt.ContentType != YmtFileContentType.ScenarioPointRegion || ymt.ScenarioRegion == null) return null;
            ymt.FilePath = full;
            ymt.Name = Path.GetFileName(full);
            if (ymt.RpfFileEntry != null) { ymt.RpfFileEntry.Name = ymt.Name; ymt.RpfFileEntry.Path = full; }
            JenkIndex.Ensure(Path.GetFileNameWithoutExtension(ymt.Name));
            ResolveScenarioTypes(ymt, gfc);
            return ymt;
        }

        public static void ResolveScenarioTypes(YmtFile ymt, GameFileCache gfc)
        {
            if (ymt?.ScenarioRegion == null) return;
            try
            {
                if (Scenarios.ScenarioTypes == null && gfc != null && gfc.IsInited) Scenarios.EnsureScenarioTypes(gfc);
                var lu = ymt.ScenarioRegion.Region?.LookUps;
                if (Scenarios.ScenarioTypes != null && lu?.TypeNames != null && lu.PedModelSetNames != null && lu.VehicleModelSetNames != null) ymt.ScenarioRegion.LoadTypes();
            }
            catch (Exception ex) { Console.WriteLine("SCENARIO types: " + ex.Message); }
        }

        public static bool ScenarioSaveSafe(YmtFile ymt, GameFileCache gfc, out string why)
        {
            why = null;
            if (ymt?.ScenarioRegion == null) { why = "no scenario region"; return false; }
            ResolveScenarioTypes(ymt, gfc);
            if (Scenarios.ScenarioTypes == null) { why = "the game's scenario types are not loaded yet (game files still initialising) - saving now would drop every point's type"; return false; }
            return true;
        }

        public void SaveScenario(YmtFile ymt, bool saveas)
        {
            var p = win.Project;
            if (ymt == null || p == null) { win.Status = "select a scenario region first"; return; }
            if (!ScenarioSaveSafe(ymt, cache(), out var why)) { win.Status = ymt.Name + ": not saved - " + why; return; }
            string path = ymt.FilePath;
            if (saveas)
            {
                using var dlg = new System.Windows.Forms.SaveFileDialog { Filter = "Scenario region files|*.ymt", FileName = Path.GetFileName(path ?? ymt.Name ?? "region.ymt") };
                if (!string.IsNullOrEmpty(p.Directory)) dlg.InitialDirectory = p.Directory;
                if (dlg.ShowDialog(owner()) != System.Windows.Forms.DialogResult.OK) return;
                path = dlg.FileName;
                string oldRel = p.GetRelativePath(ymt.FilePath ?? ymt.Name ?? "");
                p.ScenarioFilenames.Remove(oldRel);
                ymt.FilePath = path;
                ymt.Name = Path.GetFileName(path);
                if (ymt.RpfFileEntry != null) ymt.RpfFileEntry.Name = ymt.Name;
                if (!p.ScenarioFilenames.Contains(p.GetRelativePath(path))) p.ScenarioFilenames.Add(p.GetRelativePath(path));
                p.HasChanged = true;
            }
            SaveSpaceFile(ymt.Name, path, "Scenario region files|*.ymt", () => ymt.Save(), pth => { ymt.FilePath = pth; ymt.HasChanged = false; }, p.ScenarioFilenames);
            ScenarioFilesChanged?.Invoke();
        }

        public void RemoveScenario(YmtFile ymt)
        {
            var p = win.Project;
            if (ymt == null || p == null) return;
            p.RemoveScenarioFile(ymt);
            string leaf = Path.GetFileName(ymt.FilePath ?? ymt.Name ?? "");
            for (int i = p.ScenarioFilenames.Count - 1; i >= 0; i--)
                if (string.Equals(Path.GetFileName(p.ScenarioFilenames[i]), leaf, StringComparison.OrdinalIgnoreCase)) p.ScenarioFilenames.RemoveAt(i);
            if (ReferenceEquals(win.CurrentScenario, ymt)) win.Select(p);
            win.Status = "removed " + ymt.Name + " from the project";
            ScenarioFilesChanged?.Invoke();
        }

        public void GoToScenario(YmtFile ymt, ScenarioNode point)
        {
            var sr = ymt?.ScenarioRegion;
            if (sr == null) { win.Status = "no scenario region to go to"; return; }
            if (point != null && !ReferenceEquals(point.Ymt, ymt)) point = null;
            if (point != null) { GoToPosition?.Invoke(point.Position, 6.0f); return; }
            if (sr.BVH != null)
            {
                var b = sr.BVH.Box;
                GoToPosition?.Invoke((b.Minimum + b.Maximum) * 0.5f, Math.Max((b.Maximum - b.Minimum).Length() * 0.5f, 10.0f));
                return;
            }
            var first = sr.Nodes?.FirstOrDefault(n => n != null);
            if (first != null) GoToPosition?.Invoke(first.Position, 20.0f);
            else win.Status = "the region has no points to go to";
        }
    }
}

