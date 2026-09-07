using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private (int group, int firstPart) TerrainBeginImport_V20(string name, string path)
        {
            var te = TerrainEd;
            bool add = te.RequestImportAdd_V20 && te.Parts.Count > 0;
            te.RequestImportAdd_V20 = false;
            int g = te.BeginImport_V20(name, path, replace: !add);
            return (g, te.Parts.Count);
        }

        private void TerrainEndImport_V20(int group, int firstPart)
        {
            var te = TerrainEd;
            for (int i = firstPart; i < te.Parts.Count; i++) te.Parts[i].Group_V20 = group;
            te.EnsureGroups_V20(te.Name);
            te.RecomputeBounds_V20();
        }

        private void TerrainServiceProps_V20()
        {
            var te = TerrainEd;
            if (te == null) return;

            if (te.RequestRebuildVisible_V20)
            {
                te.RequestRebuildVisible_V20 = false;
                TerrainRebuildModel_V20();
            }

            if (te.RequestRemoveProp_V20 >= 0)
            {
                int i = te.RequestRemoveProp_V20;
                te.RequestRemoveProp_V20 = -1;
                var nm = i < te.Props_V20.Count ? te.Props_V20[i].Name : "?";
                if (te.RemoveProp_V20(i))
                {
                    TerrainRebuildModel_V20();
                    te.Status = "removed " + nm;
                }
            }

            if (te.RequestExportProp_V20 >= 0)
            {
                int i = te.RequestExportProp_V20;
                te.RequestExportProp_V20 = -1;
                TerrainExportOneProp_V20(i);
            }

            if (te.RequestExportAll_V20)
            {
                te.RequestExportAll_V20 = false;
                TerrainExportAllProps_V20();
            }
        }

        private bool TerrainExportOneProp_V20(int prop)
        {
            var te = TerrainEd;
            if (prop < 0 || prop >= te.Props_V20.Count) { te.Status = "no such prop"; return false; }
            var p = te.Props_V20[prop];
            var mine = te.PartsOf_V20(prop).ToList();
            if (mine.Count == 0) { te.Status = p.Name + " has no geometry"; return false; }

            using var dlg = new SaveFileDialog
            {
                Filter = "Drawable (*.ydr)|*.ydr|All files|*.*",
                FileName = TerrainSafeName_V20(p.Name) + ".ydr",
            };
            if (!string.IsNullOrEmpty(terrainLastDir_R4) && Directory.Exists(terrainLastDir_R4))
                dlg.InitialDirectory = terrainLastDir_R4;
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;

            return TerrainExportScoped_V20(prop, dlg.FileName);
        }

        private void TerrainExportAllProps_V20()
        {
            var te = TerrainEd;
            if (te.Props_V20.Count == 0) { te.Status = "nothing to export"; return; }

            using var dlg = new FolderBrowserDialog { Description = "Where the .ydr files go" };
            if (!string.IsNullOrEmpty(terrainLastDir_R4) && Directory.Exists(terrainLastDir_R4))
                dlg.SelectedPath = terrainLastDir_R4;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            int ok = 0, skipped = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < te.Props_V20.Count; i++)
            {
                if (!te.PartsOf_V20(i).Any()) { skipped++; continue; }
                var baseName = TerrainSafeName_V20(te.Props_V20[i].Name);
                var name = baseName;
                for (int n = 2; !names.Add(name); n++) name = baseName + "_" + n;
                if (TerrainExportScoped_V20(i, Path.Combine(dlg.SelectedPath, name + ".ydr"))) ok++;
            }
            te.Status = $"wrote {ok} of {te.Props_V20.Count} prop(s) into {Path.GetFileName(dlg.SelectedPath)}" +
                        (skipped > 0 ? $" ({skipped} empty)" : "");
            Console.WriteLine("TERRAIN exported " + ok + " prop(s) -> " + dlg.SelectedPath);
        }

        private bool TerrainExportScoped_V20(int prop, string path)
        {
            var te = TerrainEd;
            var all = te.Parts.ToList();
            var mine = te.PartsOf_V20(prop).ToList();
            if (mine.Count == 0) return false;
            var savedName = te.Name;
            bool ok = false;
            try
            {
                te.Parts.Clear();
                te.Parts.AddRange(mine);
                te.Name = te.Props_V20[prop].Name;
                ok = TerrainExportTo_R4(path, reloadIntoView: false);
            }
            catch (Exception ex)
            {
                te.Status = "export failed: " + ex.Message;
                Console.WriteLine("TERRAIN export failed " + path + ": " + ex.Message);
            }
            finally
            {
                te.Parts.Clear();
                te.Parts.AddRange(all);
                te.Name = savedName;
            }
            if (ok)
            {
                te.Props_V20[prop].Dirty = false;
                te.Props_V20[prop].LastExport = path;
            }
            return ok;
        }

        private static string TerrainSafeName_V20(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "terrain";
            var bad = Path.GetInvalidFileNameChars();
            var chars = s.Trim().Select(c => bad.Contains(c) || c == ' ' ? '_' : c).ToArray();
            var outp = new string(chars).Trim('_');
            return outp.Length == 0 ? "terrain" : outp.ToLowerInvariant();
        }

        private void TerrainRebuildModel_V20()
        {
            TerrainDropModel_R4();
            foreach (var p in TerrainEd.Parts) p.Dirty = true;
        }
    }
}

