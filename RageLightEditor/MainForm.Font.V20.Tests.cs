using System;
using System.IO;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_Font_V20(Action<string, bool, string> check)
        {
            try
            {
                var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var segoe = FontCatalog_V20.Read(Path.Combine(fontsDir, "segoeui.ttf"));
                check("font: a TrueType file gives its real family and style", segoe != null && segoe.Family == "Segoe UI" && FontCatalog_V20.IsRegular(segoe.Style), segoe == null ? "null" : segoe.Family + " / " + segoe.Style);
                var bold = FontCatalog_V20.Read(Path.Combine(fontsDir, "arialbd.ttf"));
                check("font: a bold file is recognised as not regular", bold != null && !FontCatalog_V20.IsRegular(bold.Style), bold == null ? "null" : bold.Family + " / " + bold.Style);
                var all = FontCatalog_V20.All(true);
                check("font: the catalog lists installed families once each, regular weights only",
                      all.Count > 10 && all.Any(f => f.Family == "Consolas") && all.Any(f => f.Family == "Arial") && all.Select(f => f.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count && all.All(f => FontCatalog_V20.IsRegular(f.Style)),
                      $"{all.Count} families");
                check("font: the list is alphabetical", all.Zip(all.Skip(1), (a, b) => string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase) <= 0).All(x => x), "");
                var tmp = Path.Combine(Path.GetTempPath(), "rle_v20_notafont.ttf");
                File.WriteAllText(tmp, "this is not a font");
                check("font: a file that is not a font is skipped", FontCatalog_V20.Read(tmp) == null, "");
                try { File.Delete(tmp); } catch { }
                check("font: the built-in font stays the default", string.IsNullOrEmpty(new AppSettings().UiFontV20) && new AppSettings().UiFontPxV20 == 0f, "");
            }
            catch (Exception ex) { check("font v20: no exception", false, ex.ToString()); }
        }
    }
}
