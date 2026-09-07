using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RageLightEditor.Editor
{
    public class FontEntry_V20
    {
        public string Path;
        public string Family;
        public string Style;
        public string Label => FontCatalog_V20.IsRegular(Style) ? Family : Family + " " + Style;
    }

    public static class FontCatalog_V20
    {
        private static List<FontEntry_V20> cache;

        public static IEnumerable<string> FontFolders()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        }

        public static List<FontEntry_V20> All(bool refresh = false)
        {
            if (cache != null && !refresh) return cache;
            var list = new List<FontEntry_V20>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in FontFolders())
            {
                if (!Directory.Exists(dir)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*.ttf"); }
                catch { continue; }
                foreach (var f in files)
                {
                    var e = Read(f);
                    if (e == null || !IsRegular(e.Style) || !seen.Add(e.Family)) continue;
                    list.Add(e);
                }
            }
            list.Sort((a, b) => string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase));
            cache = list;
            return list;
        }

        public static bool IsRegular(string style) =>
            string.IsNullOrWhiteSpace(style) ||
            style.Equals("Regular", StringComparison.OrdinalIgnoreCase) ||
            style.Equals("Normal", StringComparison.OrdinalIgnoreCase) ||
            style.Equals("Book", StringComparison.OrdinalIgnoreCase) ||
            style.Equals("Roman", StringComparison.OrdinalIgnoreCase);

        public static FontEntry_V20 Read(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);
                uint tag = ReadU32(br);
                if (tag != 0x00010000 && tag != 0x74727565) return null;
                ushort numTables = ReadU16(br);
                fs.Seek(6, SeekOrigin.Current);
                uint nameOff = 0;
                for (int i = 0; i < numTables; i++)
                {
                    uint t = ReadU32(br);
                    ReadU32(br);
                    uint off = ReadU32(br);
                    ReadU32(br);
                    if (t == 0x6E616D65) nameOff = off;
                }
                if (nameOff == 0 || nameOff >= fs.Length) return null;
                fs.Seek(nameOff, SeekOrigin.Begin);
                ReadU16(br);
                ushort count = ReadU16(br);
                ushort strOff = ReadU16(br);
                string family = null, style = null, familyPlain = null, stylePlain = null;
                for (int i = 0; i < count; i++)
                {
                    ushort platform = ReadU16(br), encoding = ReadU16(br), lang = ReadU16(br), nameId = ReadU16(br), len = ReadU16(br), off = ReadU16(br);
                    if (nameId != 1 && nameId != 2 && nameId != 16 && nameId != 17) continue;
                    if (platform == 3 && lang != 0x0409) continue;
                    long back = fs.Position;
                    long at = nameOff + strOff + off;
                    if (at + len > fs.Length) continue;
                    fs.Seek(at, SeekOrigin.Begin);
                    var bytes = br.ReadBytes(len);
                    fs.Seek(back, SeekOrigin.Begin);
                    string s = (platform == 3 || platform == 0) ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
                    s = s.Trim();
                    if (s.Length == 0) continue;
                    if (nameId == 16) family ??= s;
                    else if (nameId == 1) familyPlain ??= s;
                    else if (nameId == 17) style ??= s;
                    else if (nameId == 2) stylePlain ??= s;
                }
                family ??= familyPlain;
                style ??= stylePlain;
                if (string.IsNullOrWhiteSpace(family)) return null;
                return new FontEntry_V20 { Path = path, Family = family, Style = style ?? "" };
            }
            catch { return null; }
        }

        private static ushort ReadU16(BinaryReader br)
        {
            var b = br.ReadBytes(2);
            if (b.Length < 2) throw new EndOfStreamException();
            return (ushort)((b[0] << 8) | b[1]);
        }

        private static uint ReadU32(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }
    }
}
