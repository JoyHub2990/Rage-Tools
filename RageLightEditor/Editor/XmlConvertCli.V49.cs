using System;
using System.IO;

namespace RageLightEditor.Editor
{
    public static class XmlConvertCli_V49
    {
        public static byte[] Convert(string inPath, ref string name, out string why)
        {
            RpfEdit.XmlImportBegin_S3();
            int converted = 0;
            var data = RpfEdit.ReadForImport_S3(inPath, false, ref name, ref converted, out why);
            if (data == null) return null;
            if (converted == 0)
            {
                why = "no converter for " + name + " - the file went in unchanged";
                return null;
            }
            return data;
        }

        public static int RunToXml(string inPath, string outDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(inPath) || !File.Exists(inPath))
                {
                    Console.WriteLine("CONVERTTOXML FAILED: no such file " + inPath);
                    return 2;
                }
                if (string.IsNullOrWhiteSpace(outDir))
                    outDir = Path.GetDirectoryName(Path.GetFullPath(inPath)) ?? ".";
                // loose binary -> CodeWalker XML, embedded textures in the
                // sidecar folder (the layout MaxYDR and the Assets Library read)
                var path = XmlIO.ExportBinaryFile(inPath, outDir);
                Console.WriteLine($"CONVERTTOXML wrote {path}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("CONVERTTOXML FAILED: " + ex.Message);
                return 1;
            }
        }

        public static int Run(string inPath, string outPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(inPath) || !File.Exists(inPath))
                {
                    Console.WriteLine("CONVERTXML FAILED: no such file " + inPath);
                    return 2;
                }
                string name = Path.GetFileName(inPath);
                var data = Convert(inPath, ref name, out var why);
                if (data == null)
                {
                    Console.WriteLine("CONVERTXML FAILED: " + (why ?? "could not convert"));
                    return 3;
                }
                if (string.IsNullOrWhiteSpace(outPath))
                    outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inPath)) ?? "", name);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
                File.WriteAllBytes(outPath, data);
                Console.WriteLine($"CONVERTXML wrote {outPath} ({data.Length:N0} bytes)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("CONVERTXML FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}
