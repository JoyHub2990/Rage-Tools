using System;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private object mvTexDumpedFor_S3;

        private void ServiceModelViewTextureDump_S3()
        {
            RunMetaXmlExport_S3();
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RLE_MVTEX"))) return;
            var p = ModelView?.Preview;
            if (p == null || ReferenceEquals(p, mvTexDumpedFor_S3)) return;
            mvTexDumpedFor_S3 = p;

            int usable = ModelView.TextureCount_S3, missing = ModelView.TextureMissingCount_S3;
            Console.WriteLine($"MVTEX {ModelView.Title}: {usable} texture(s) usable, {missing} missing, " +
                              $"{p.Stats.Textures.Count} embedded in the file itself");
            foreach (var line in ModelView.TextureDumpLines_S3()) Console.WriteLine(line);
        }

        private bool metaXmlDone_S3;

        private void RunMetaXmlExport_S3()
        {
            if (metaXmlDone_S3) return;
            var spec = Environment.GetEnvironmentVariable("RLE_METAXML");
            if (string.IsNullOrWhiteSpace(spec)) { metaXmlDone_S3 = true; return; }
            metaXmlDone_S3 = true;

            var bits = spec.Split(',');
            var src = bits[0].Trim();
            var dst = bits.Length > 1 ? bits[1].Trim() : src + ".xml";
            try
            {
                var data = System.IO.File.ReadAllBytes(src);
                var name = System.IO.Path.GetFileName(src);
                var ext = (System.IO.Path.GetExtension(src) ?? "").ToLowerInvariant();
                var xml = MetaXmlOfDiskFile_Q1(ext, name, data, out var why);
                if (string.IsNullOrEmpty(xml))
                {
                    Console.WriteLine($"METAXML {src}: no XML converter for this file type" +
                                      (string.IsNullOrEmpty(why) ? "" : " - " + why));
                    return;
                }
                System.IO.File.WriteAllText(dst, xml, new System.Text.UTF8Encoding(false));
                Console.WriteLine($"METAXML {src} -> {dst} ({xml.Length:N0} chars)");
            }
            catch (Exception ex) { Console.WriteLine($"METAXML {src} FAILED: {ex.Message}"); }
        }
    }
}

