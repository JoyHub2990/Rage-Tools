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
        public static RpfFileEntry DiskFileEntry_V42(string name, string path, ref byte[] data)
        {
            RpfFileEntry e;
            uint magic = data?.Length > 4 ? BitConverter.ToUInt32(data, 0) : 0;
            if (magic == 0x37435352)
            {
                e = RpfFile.CreateResourceFileEntry(ref data, 0);
                data = ResourceBuilder.Decompress(data);
            }
            else
            {
                var be = new RpfBinaryFileEntry();
                be.FileSize = (uint)(data?.Length ?? 0);
                be.FileUncompressedSize = be.FileSize;
                e = be;
            }
            e.Name = name;
            e.NameLower = name?.ToLowerInvariant();
            e.NameHash = JenkHash.GenHash(e.NameLower);
            e.ShortNameHash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(e.NameLower));
            e.Path = path;
            return e;
        }

        public static bool DiskFileXml_V42(string path, out string xml, out string error)
        {
            xml = null;
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "that file is gone"; return false; }
                var data = File.ReadAllBytes(path);
                if (data.Length == 0) { error = Path.GetFileName(path) + " is empty"; return false; }
                var entry = DiskFileEntry_V42(Path.GetFileName(path), path, ref data);
                var tmp = Path.Combine(Path.GetTempPath(), "rle_rpfxml");
                Directory.CreateDirectory(tmp);
                xml = MetaXml.GetXml(entry, data, out _, tmp);
                if (string.IsNullOrEmpty(xml))
                {
                    xml = null;
                    error = Path.GetFileName(path) + ": no XML converter for this file type";
                    return false;
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private void DoRpfDiskExportXml_V42(string path)
        {
            if (!DiskFileXml_V42(path, out var xml, out var error))
            {
                panel.RpfStatus = error;
                return;
            }
            try
            {
                using var dlg = new SaveFileDialog
                {
                    Filter = "XML (*.xml)|*.xml|All files (*.*)|*.*",
                    FileName = Path.GetFileName(path) + ".xml",
                    Title = "Export " + Path.GetFileName(path) + " as XML",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, xml, new UTF8Encoding(false));
                panel.RpfStatus = $"Wrote {Path.GetFileName(dlg.FileName)} ({xml.Length / 1024:N0} KB)";
                Console.WriteLine($"RPFXML wrote {dlg.FileName} ({xml.Length} chars)");
            }
            catch (Exception ex) { panel.RpfStatus = "Export failed: " + ex.Message; }
        }

        private void DoRpfDiskHexView_V42(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { panel.RpfStatus = "that file is gone"; return; }
                var fi = new FileInfo(path);
                int take = (int)Math.Min(fi.Length, HexViewCap_V40);
                var data = new byte[take];
                using (var fs = File.OpenRead(path))
                {
                    int off = 0;
                    while (off < take)
                    {
                        int n = fs.Read(data, off, take - off);
                        if (n <= 0) break;
                        off += n;
                    }
                }
                var dump = HexDump_V40(data);
                if (fi.Length > take)
                    dump += $"\n... {fi.Length - take:N0} more byte(s) not shown ({fi.Length:N0} in all).\n";
                panel.ShowRpfText(Path.GetFileName(path) + "  (hex)", dump);
                panel.RpfStatus = $"{Path.GetFileName(path)}: {fi.Length:N0} bytes" +
                                  (fi.Length > take ? $" - showing the first {HexViewCap_V40 / 1024 / 1024} MB" : "");
                Console.WriteLine($"RPFHEX {path} ({fi.Length} bytes)");
            }
            catch (Exception ex) { panel.RpfStatus = "Could not read it: " + ex.Message; }
        }

        private void SeqTest_RpfDiskXmlHex_V42(Action<string, bool, string> check)
        {
            string ytyp = null, txt = null;
            try
            {
                ytyp = Path.Combine(Path.GetTempPath(), "rle_v42_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ytyp");
                ArchetypeBuilder.Save(ytyp, "rle_v42_props", new[]
                {
                    new ArchetypeDef
                    {
                        Name = "rle_v42_prop",
                        BbMin = new SharpDX.Vector3(-1, -1, 0),
                        BbMax = new SharpDX.Vector3(1, 1, 2),
                        BsCentre = new SharpDX.Vector3(0, 0, 1),
                        BsRadius = 1.8f,
                    },
                });

                bool ok = DiskFileXml_V42(ytyp, out var xml, out var err);
                check("v42 disk xml: a loose .ytyp on disk converts to XML",
                      ok && xml != null && xml.Contains("rle_v42_prop") && xml.Contains("CMapTypes"),
                      ok ? $"{xml.Length:N0} chars" : err ?? "failed");

                var head = new byte[16];
                using (var fs = File.OpenRead(ytyp)) fs.Read(head, 0, 16);
                check("v42 disk hex: the same file's bytes start with the resource magic",
                      HexDump_V40(head).Contains("RSC7"), HexDump_V40(head).Split('\n')[0]);

                txt = Path.Combine(Path.GetTempPath(), "rle_v42_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bin");
                File.WriteAllBytes(txt, Encoding.ASCII.GetBytes("not a game file"));
                bool okTxt = DiskFileXml_V42(txt, out _, out var errTxt);
                check("v42 disk xml: a file with no converter says so instead of writing junk",
                      !okTxt && (errTxt ?? "").Contains("no XML converter"), errTxt ?? "no error");

                check("v42 disk xml: a missing file is a clean error",
                      !DiskFileXml_V42(ytyp + ".gone", out _, out var errGone) && errGone != null,
                      errGone ?? "no error");
            }
            catch (Exception ex) { check("v42 disk xml/hex", false, ex.Message); }
            finally
            {
                try { if (ytyp != null && File.Exists(ytyp)) File.Delete(ytyp); } catch { }
                try { if (txt != null && File.Exists(txt)) File.Delete(txt); } catch { }
            }
        }
    }
}
