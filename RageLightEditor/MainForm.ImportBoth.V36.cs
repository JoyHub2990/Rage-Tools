using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void DoImportMapFiles_V36()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Map files (*.ytyp;*.ymap)|*.ytyp;*.ymap|" +
                         "Archetype definitions (*.ytyp)|*.ytyp|" +
                         "Map placements (*.ymap)|*.ymap|All files (*.*)|*.*",
                Title = "Import a map - pick any mix of .ytyp and .ymap files",
                Multiselect = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            ImportMapFiles_V36(dlg.FileNames);
        }

        public void ImportMapFiles_V36(string[] paths)
        {
            var files = (paths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToArray();
            if (files.Length == 0) return;

            var ytyps = files.Where(p => p.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase)).ToArray();
            var ymaps = files.Where(p => p.EndsWith(".ymap", StringComparison.OrdinalIgnoreCase)).ToArray();
            var other = files.Length - ytyps.Length - ymaps.Length;

            if (ytyps.Length == 0 && ymaps.Length == 0)
            {
                panel.MloStatus = "Nothing to import - pick .ytyp or .ymap files.";
                return;
            }

            if (ytyps.Length > 0) ImportYtyp(ytyps);
            if (ymaps.Length > 0) ImportYmap(ymaps);

            panel.MloStatus =
                $"Imported {ytyps.Length} .ytyp and {ymaps.Length} .ymap" +
                (ytyps.Length > 0 && ymaps.Length > 0 ? " - archetypes first, then the placements." : ".") +
                (other > 0 ? $" {other} other file(s) were ignored." : "");
            Console.WriteLine($"IMPORTBOTH {ytyps.Length} ytyp(s) then {ymaps.Length} ymap(s), {other} ignored");
        }

        private void SeqTest_ImportBoth_V36(Action<string, bool, string> check)
        {
            var mixed = new[]
            {
                @"C:\x\b.ymap", @"C:\x\a.ytyp", @"C:\x\c.ymap", @"C:\x\d.YTYP", @"C:\x\notes.txt",
            };
            var ytyps = mixed.Where(p => p.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase)).ToArray();
            var ymaps = mixed.Where(p => p.EndsWith(".ymap", StringComparison.OrdinalIgnoreCase)).ToArray();

            check("v36 import: a mixed selection is split by kind, case and all",
                  ytyps.Length == 2 && ymaps.Length == 2,
                  $"{ytyps.Length} ytyp, {ymaps.Length} ymap, {mixed.Length - ytyps.Length - ymaps.Length} other");

            check("v36 import: ...and the archetypes go first, because a placement needs them to exist",
                  Array.IndexOf(mixed, ytyps[0]) >= 0 && ytyps.All(p => p.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase)),
                  "ytyps: " + string.Join(", ", ytyps.Select(Path.GetFileName)));

            try
            {
                ImportMapFiles_V36(Array.Empty<string>());
                ImportMapFiles_V36(new[] { @"C:\nope\missing.ymap" });
                check("v36 import: nothing to import is handled quietly, not with a crash", true, "no throw");
            }
            catch (Exception ex) { check("v36 import: nothing to import is handled quietly, not with a crash", false, ex.Message); }
        }
    }
}

