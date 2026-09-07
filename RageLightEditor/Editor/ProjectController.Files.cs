using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using CodeWalker.World;

namespace RageLightEditor.Editor
{
    public partial class ProjectController
    {
        private CwProject ProjectForAdd()
        {
            if (win.Project == null) win.Project = new CwProject { Name = "New Project", HasChanged = true };
            return win.Project;
        }

        private bool AddedFile(string name)
        {
            win.Status = name + " has been added to the project";
            return true;
        }

        public bool AddGameFileToProject(YndFile ynd)
        {
            if (ynd == null) return false;
            var p = ProjectForAdd();
            if (p.ContainsYnd(ynd)) return false;
            if (string.IsNullOrEmpty(ynd.FilePath)) ynd.FilePath = ynd.Name;
            if (!p.AddYndFile(ynd)) return false;
            ynd.HasChanged = true;
            return AddedFile(ynd.Name);
        }

        public bool AddGameFileToProject(YnvFile ynv)
        {
            if (ynv == null) return false;
            var p = ProjectForAdd();
            if (p.ContainsYnv(ynv)) return false;
            if (string.IsNullOrEmpty(ynv.FilePath)) ynv.FilePath = ynv.Name;
            if (!p.AddYnvFile(ynv)) return false;
            ynv.HasChanged = true;
            return AddedFile(ynv.Name);
        }

        public bool AddGameFileToProject(TrainTrack track)
        {
            if (track == null) return false;
            var p = ProjectForAdd();
            if (p.ContainsTrainTrack(track)) return false;
            if (string.IsNullOrEmpty(track.FilePath)) track.FilePath = track.Name;
            if (!p.AddTrainsFile(track)) return false;
            track.HasChanged = true;
            return AddedFile(track.Name);
        }

        public bool AddGameFileToProject(YmtFile ymt)
        {
            if (ymt == null) return false;
            var p = ProjectForAdd();
            if (p.ContainsScenario(ymt)) return false;
            if (string.IsNullOrEmpty(ymt.FilePath)) ymt.FilePath = ymt.Name;
            if (!p.AddScenarioFile(ymt)) return false;
            ymt.HasChanged = true;
            return AddedFile(ymt.Name);
        }

        public bool AddGameFileToProject(RelFile rel)
        {
            if (rel == null) return false;
            var p = ProjectForAdd();
            if (p.ContainsAudioRel(rel)) return false;
            if (string.IsNullOrEmpty(rel.FilePath)) rel.FilePath = rel.Name;
            if (!p.AddAudioRelFile(rel)) return false;
            rel.HasChanged = true;
            return AddedFile(rel.Name);
        }

        public void SaveSpaceFiles()
        {
            var p = win.Project;
            if (p == null) return;
            foreach (var f in p.YndFiles.ToList()) if (f.HasChanged) SaveSpaceFile(f.Name, f.FilePath, "Ynd files|*.ynd", () => f.Save(), path => { f.FilePath = path; f.HasChanged = false; }, p.YndFilenames);
            foreach (var f in p.YnvFiles.ToList()) if (f.HasChanged) SaveSpaceFile(f.Name, f.FilePath, "Ynv files|*.ynv", () => f.Save(), path => { f.FilePath = path; f.HasChanged = false; }, p.YnvFilenames);
            foreach (var f in p.TrainsFiles.ToList()) if (f.HasChanged) SaveSpaceFile(f.Name, f.FilePath, "Train track files|*.dat", () => f.Save(), path => { f.FilePath = path; f.HasChanged = false; }, p.TrainsFilenames);
            foreach (var f in p.ScenarioFiles.ToList()) if (f.HasChanged) SaveSpaceFile(f.Name, f.FilePath, "Scenario region files|*.ymt", () => f.Save(), path => { f.FilePath = path; f.HasChanged = false; }, p.ScenarioFilenames);
            foreach (var f in p.AudioRelFiles.ToList()) if (f.HasChanged) SaveSpaceFile(f.Name, f.FilePath, "Audio rel files|*.rel", () => f.Save(), path => { f.FilePath = path; f.HasChanged = false; }, p.AudioRelFilenames);
        }

        private void SaveSpaceFile(string name, string filepath, string filter, Func<byte[]> save, Action<string> saved, System.Collections.Generic.List<string> names)
        {
            var p = win.Project;
            if (string.IsNullOrEmpty(filepath)) filepath = name;
            string origRel = p?.GetRelativePath(filepath) ?? filepath;
            bool saveas = !File.Exists(filepath) || string.IsNullOrEmpty(Path.GetDirectoryName(filepath));
            if (saveas)
            {
                using var dlg = new System.Windows.Forms.SaveFileDialog { Filter = filter, FileName = Path.GetFileName(filepath) };
                var dir = p?.Directory;
                if (!string.IsNullOrEmpty(dir)) dlg.InitialDirectory = dir;
                if (dlg.ShowDialog(owner()) != System.Windows.Forms.DialogResult.OK) return;
                filepath = dlg.FileName;
            }
            try
            {
                var data = save();
                if (data == null || data.Length == 0) { win.Status = name + ": save produced no data"; return; }
                File.WriteAllBytes(filepath, data);
                saved(filepath);
                if (p != null)
                {
                    var newRel = p.GetRelativePath(filepath);
                    int i = names.IndexOf(origRel);
                    if (i >= 0) names[i] = newRel; else if (!names.Contains(newRel)) names.Add(newRel);
                    p.HasChanged = true;
                }
                win.Status = "saved " + Path.GetFileName(filepath);
            }
            catch (Exception ex) { win.Status = name + " save failed: " + ex.Message; }
        }
    }
}

