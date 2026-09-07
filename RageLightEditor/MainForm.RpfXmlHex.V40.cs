using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        public const int HexViewCap_V40 = 4 * 1024 * 1024;

        public static string HexDump_V40(byte[] data, int cap = HexViewCap_V40)
        {
            if (data == null || data.Length == 0) return "(empty)";
            int n = Math.Min(data.Length, cap);
            var sb = new StringBuilder(n / 16 * 80 + 128);
            var ascii = new char[16];
            for (int off = 0; off < n; off += 16)
            {
                sb.Append(off.ToString("X8")).Append("  ");
                int run = Math.Min(16, n - off);
                for (int i = 0; i < 16; i++)
                {
                    if (i == 8) sb.Append(' ');
                    if (i < run)
                    {
                        sb.Append(data[off + i].ToString("X2")).Append(' ');
                        byte b = data[off + i];
                        ascii[i] = b >= 0x20 && b < 0x7F ? (char)b : '.';
                    }
                    else { sb.Append("   "); ascii[i] = ' '; }
                }
                sb.Append(' ').Append(ascii, 0, 16).Append('\n');
            }
            if (data.Length > n)
                sb.Append($"\n... {data.Length - n:N0} more byte(s) not shown ({data.Length:N0} in all).\n");
            return sb.ToString();
        }

        private void DoRpfHexView_V40(RpfFileEntry e)
        {
            if (e == null) return;
            try
            {
                var data = ArchiveBrowser.Extract(e);
                if (data == null || data.Length == 0)
                {
                    panel.RpfStatus = e.Name + " came out of the archive empty";
                    return;
                }
                panel.ShowRpfText(e.Name + "  (hex)", HexDump_V40(data));
                panel.SetRpfViewSource_Q1(e, null);
                panel.RpfStatus = $"{e.Name}: {data.Length:N0} bytes" +
                                  (data.Length > HexViewCap_V40 ? $" - showing the first {HexViewCap_V40 / 1024 / 1024} MB" : "");
                Console.WriteLine($"RPFHEX {e.Path} ({data.Length} bytes)");
            }
            catch (Exception ex) { panel.RpfStatus = "Could not read it: " + ex.Message; }
        }

        private void DoRpfExportXml_V40(RpfFileEntry e)
        {
            if (e == null) return;
            try
            {
                var data = ArchiveBrowser.Extract(e);
                if (data == null || data.Length == 0)
                {
                    panel.RpfStatus = e.Name + " came out of the archive empty";
                    return;
                }
                var tmp = Path.Combine(Path.GetTempPath(), "rle_rpfxml");
                Directory.CreateDirectory(tmp);
                var xml = MetaXml.GetXml(e, data, out _, tmp);
                if (string.IsNullOrEmpty(xml))
                {
                    panel.RpfStatus = e.Name + ": no XML converter for this file type - Extract it instead";
                    return;
                }

                using var dlg = new SaveFileDialog
                {
                    Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*",
                    FileName = e.Name + ".xml",
                    Title = "Export " + e.Name + " as XML",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                File.WriteAllText(dlg.FileName, xml, new UTF8Encoding(false));
                panel.RpfStatus = $"Wrote {Path.GetFileName(dlg.FileName)} ({xml.Length / 1024:N0} KB)";
                Console.WriteLine($"RPFXML wrote {dlg.FileName} ({xml.Length} chars)");
            }
            catch (Exception ex) { panel.RpfStatus = "Export failed: " + ex.Message; }
        }

        private void DoRpfExportXmlMany_V40(System.Collections.Generic.List<RpfFileEntry> files)
        {
            if (files == null || files.Count == 0) return;
            try
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = $"A folder for {files.Count} file(s) as XML",
                    UseDescriptionForTitle = true,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var tmp = Path.Combine(Path.GetTempPath(), "rle_rpfxml");
                Directory.CreateDirectory(tmp);
                int wrote = 0, skipped = 0;
                foreach (var e in files)
                {
                    if (e == null) continue;
                    try
                    {
                        var data = ArchiveBrowser.Extract(e);
                        if (data == null || data.Length == 0) { skipped++; continue; }
                        var xml = MetaXml.GetXml(e, data, out _, tmp);
                        if (string.IsNullOrEmpty(xml)) { skipped++; continue; }
                        File.WriteAllText(Path.Combine(dlg.SelectedPath, e.Name + ".xml"), xml, new UTF8Encoding(false));
                        wrote++;
                    }
                    catch { skipped++; }
                }
                panel.RpfStatus = $"Wrote {wrote} XML file(s)" +
                                  (skipped > 0 ? $"; {skipped} had no converter and were left out" : "");
                Console.WriteLine($"RPFXML wrote {wrote}, skipped {skipped}, into {dlg.SelectedPath}");
            }
            catch (Exception ex) { panel.RpfStatus = "Export failed: " + ex.Message; }
        }

        private void SeqTest_RpfXmlHex_V40(Action<string, bool, string> check)
        {
            var data = new byte[] { 0x52, 0x53, 0x43, 0x37, 0x00, 0x01, 0x7F, 0x80, 0xFF, 0x41, 0x42, 0x43 };
            var dump = HexDump_V40(data);
            check("v40 hex: the bytes are shown as hex, with their offset and their characters",
                  dump.Contains("00000000") && dump.Contains("52 53 43 37") && dump.Contains("RSC7"),
                  dump.Split('\n')[0]);

            check("v40 hex: unprintable bytes show as dots rather than as control characters",
                  dump.Contains("...") || dump.Contains(".ABC"),
                  "non-printables masked");

            var big = new byte[HexViewCap_V40 + 4096];
            var bigDump = HexDump_V40(big);
            check("v40 hex: a file past the cap says how much was left out",
                  bigDump.Contains("more byte(s) not shown"), "capped and reported");

            check("v40 hex: an empty file is handled", HexDump_V40(Array.Empty<byte>()) == "(empty)", "(empty)");
        }
    }
}

