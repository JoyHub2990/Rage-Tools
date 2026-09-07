using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static string WriteTestPng_V18(int size)
        {
            var path = Path.Combine(Path.GetTempPath(),
                "rle_v18_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - size / 2f) / (size / 2f), dy = (y - size / 2f) / (size / 2f);
                        int a = (int)(255 * Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy)));
                        bmp.SetPixel(x, y, Color.FromArgb(a, 255, 255, 255));
                    }
                bmp.SetPixel(1, 1, Color.FromArgb(255, 255, 0, 0));
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        private void SeqTest_PtfxImport_V18(Action<string, bool, string> check)
        {
            string png = null, ypt = null;
            try
            {
                png = WriteTestPng_V18(64);

                var tex = ImageImport_V18.Load_V18(png, out var err);
                check("v18 ptfx: a .png off disk decodes into a texture",
                      tex?.Data?.FullData != null && tex.Width == 64 && tex.Height == 64,
                      tex == null ? ("failed: " + err) : ImageImport_V18.Describe_V18(tex));
                if (tex?.Data?.FullData == null) return;

                int off = (1 * 64 + 1) * 4;
                var d = tex.Data.FullData;
                check("v18 ptfx: ...with its pixels the right way round",
                      d[off + 2] > 200 && d[off + 1] < 60 && d[off + 0] < 60 && d[off + 3] > 200,
                      $"B{d[off]} G{d[off + 1]} R{d[off + 2]} A{d[off + 3]} at (1,1)");

                var doc = PtfxAuthor.NewDocument("rle_v18", "rle_v18_fx", PtfxAuthor.MakePuffSheet("rle_v18_puff", 32));
                var eff = doc?.Effects?.FirstOrDefault(e => e.Name == "rle_v18_fx");
                var em = eff?.Emitters?.FirstOrDefault();
                if (em == null) { check("v18 ptfx: a new effect has an emitter", false, "none"); return; }

                tex.Name = "rle_v18_sheet";
                tex.NameHash = JenkHash.GenHash(tex.Name);
                PtfxAuthor.SetSheet(doc, em, tex);

                var onIt = PtfxAuthor.EmitterSheet(em);
                check("v18 ptfx: the emitter's sprite becomes the imported image",
                      onIt != null && onIt.Name == "rle_v18_sheet",
                      onIt?.Name ?? "none");
                check("v18 ptfx: ...and the document's own dictionary carries it",
                      (doc.PtxList?.TextureDictionary?.Textures?.data_items ?? Array.Empty<Texture>())
                          .Any(t => t?.Name == "rle_v18_sheet"),
                      $"{doc.PtxList?.TextureDictionary?.Textures?.data_items?.Length ?? 0} texture(s) in the dictionary");

                ypt = Path.Combine(Path.GetTempPath(),
                    "rle_v18_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ypt");
                doc.Save(ypt);
                var back = PtfxDocument.FromFile(ypt);
                var bem = back?.Effects?.FirstOrDefault(e => e.Name == "rle_v18_fx")?.Emitters?.FirstOrDefault();
                var bsheet = PtfxAuthor.EmitterSheet(bem);

                check("v18 ptfx: the saved .ypt still points the emitter at that image",
                      bsheet != null && bsheet.Name == "rle_v18_sheet",
                      bsheet == null ? "no sheet came back" : $"{bsheet.Name} {bsheet.Width}x{bsheet.Height}");

                if (bsheet?.Data?.FullData != null && bsheet.Width == 64)
                {
                    var bd = bsheet.Data.FullData;
                    check("v18 ptfx: ...and the pixels that come back are the ones that went in",
                          bd.Length == d.Length && bd[off + 2] > 200 && bd[off + 1] < 60 && bd[off + 3] > 200,
                          $"{bd.Length:N0} bytes, B{bd[off]} G{bd[off + 1]} R{bd[off + 2]} A{bd[off + 3]} at (1,1)");
                }
                else
                {
                    check("v18 ptfx: ...and the pixels that come back are the ones that went in",
                          false, $"{bsheet?.Width ?? 0}x{bsheet?.Height ?? 0}, {bsheet?.Data?.FullData?.Length ?? 0} bytes");
                }

                var unique = UniqueSheetName_V18(doc, "rle_v18_sheet");
                check("v18 ptfx: importing a second image under a used name gets its own slot",
                      unique != null && unique != "rle_v18_sheet", unique ?? "no name");
            }
            catch (Exception ex)
            {
                check("v18 ptfx: the image import runs without throwing", false, ex.Message);
            }
            finally
            {
                try { if (png != null) File.Delete(png); } catch { }
                try { if (ypt != null) File.Delete(ypt); } catch { }
            }
        }
    }
}

