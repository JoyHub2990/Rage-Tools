using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private string texExportLastDir_Q1;
        private bool texExportEnvDone_Q1;

        private void ServiceModelViewTextureExport_Q1()
        {
            var req = ModelView.RequestTextureExport_Q1;
            if (req != null)
            {
                ModelView.RequestTextureExport_Q1 = null;
                if (req.WholeSet) ExportTexturesToFolder_Q1(req);
                else ExportOneTexture_Q1(req);
            }
            RunTextureExportEnv_Q1();
        }

        private void RunTextureExportEnv_Q1()
        {
            if (texExportEnvDone_Q1) return;
            var spec = Environment.GetEnvironmentVariable("RLE_TEXEXPORT");
            if (string.IsNullOrWhiteSpace(spec)) { texExportEnvDone_Q1 = true; return; }
            var list = ModelView.ViewerTextures_Q1();
            if (list.Count == 0) return;
            texExportEnvDone_Q1 = true;

            var bits = spec.Split(',');
            var dir = bits[0].Trim();
            var fmt = bits.Length > 1 ? bits[1].Trim().ToLowerInvariant() : "png";
            foreach (var f in fmt == "both" ? new[] { "png", "dds" } : new[] { fmt == "dds" ? "dds" : "png" })
            {
                var req = new ModelViewer.TextureExportRequest_Q1
                {
                    Textures = new List<AssetTextureInfo>(list),
                    Format = f,
                    WholeSet = true,
                    SourceName = ModelView.Title,
                };
                WriteTextures_Q1(req, dir);
            }
        }

        private void ExportOneTexture_Q1(ModelViewer.TextureExportRequest_Q1 req)
        {
            var tex = req.Textures.Count > 0 ? req.Textures[0] : null;
            if (tex == null) return;
            using var dlg = new SaveFileDialog
            {
                Title = "Export " + tex.Name,
                FileName = SafeTextureName_Q1(tex.Name) + "." + req.Format,
                InitialDirectory = texExportLastDir_Q1,
                Filter = req.Format == "dds"
                    ? "DirectDraw surface (*.dds)|*.dds|All files|*.*"
                    : "PNG image (*.png)|*.png|All files|*.*",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            texExportLastDir_Q1 = Path.GetDirectoryName(dlg.FileName);

            try
            {
                Cursor = Cursors.WaitCursor;
                var bytes = EncodeTexture_Q1(tex, req.Format, out string why);
                if (bytes == null)
                {
                    SayTextureExport_Q1($"could not export {tex.Name}: {why}");
                    return;
                }
                File.WriteAllBytes(dlg.FileName, bytes);
                SayTextureExport_Q1($"exported {tex.Name} as {req.Format.ToUpperInvariant()} " +
                                    $"({bytes.Length / 1024:N0} KB) to {Path.GetDirectoryName(dlg.FileName)}");
                Console.WriteLine($"TEXEXPORT one {tex.Name} -> {dlg.FileName} ({bytes.Length} bytes)");
            }
            catch (Exception ex) { SayTextureExport_Q1("export failed: " + ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private void ExportTexturesToFolder_Q1(ModelViewer.TextureExportRequest_Q1 req)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Export {req.Textures.Count} texture(s) as {req.Format.ToUpperInvariant()} into...",
                UseDescriptionForTitle = true,
                SelectedPath = texExportLastDir_Q1,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            texExportLastDir_Q1 = dlg.SelectedPath;
            WriteTextures_Q1(req, dlg.SelectedPath);
        }

        private void WriteTextures_Q1(ModelViewer.TextureExportRequest_Q1 req, string dir)
        {
            if (req == null || string.IsNullOrEmpty(dir)) return;
            int ok = 0;
            long bytes = 0;
            var failed = new List<string>();
            try
            {
                Cursor = Cursors.WaitCursor;
                Directory.CreateDirectory(dir);
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tex in req.Textures)
                {
                    if (tex == null) continue;
                    var data = EncodeTexture_Q1(tex, req.Format, out string why);
                    if (data == null) { failed.Add($"{tex.Name} ({why})"); continue; }
                    var name = SafeTextureName_Q1(tex.Name);
                    var file = name + "." + req.Format;
                    for (int n = 2; !taken.Add(file); n++) file = $"{name}_{n}.{req.Format}";
                    File.WriteAllBytes(Path.Combine(dir, file), data);
                    ok++;
                    bytes += data.Length;
                }
            }
            catch (Exception ex) { failed.Add(ex.Message); }
            finally { Cursor = Cursors.Default; }

            SayTextureExport_Q1($"exported {ok:N0} texture(s) as {req.Format.ToUpperInvariant()} " +
                                $"({bytes / 1024:N0} KB) to {dir}" +
                                (failed.Count > 0 ? $" - {failed.Count} could not be written" : ""));
            Console.WriteLine($"TEXEXPORT {ok}/{req.Textures.Count} as {req.Format} from {req.SourceName} -> {dir} " +
                              $"({bytes} bytes)" + (failed.Count > 0 ? " failures=" + string.Join(";", failed) : ""));
        }

        private void SayTextureExport_Q1(string text)
        {
            ModelView.Status = text;
            if (panel != null) panel.RpfStatus = text;
        }

        private static byte[] EncodeTexture_Q1(AssetTextureInfo tex, string format, out string why)
        {
            why = null;
            if (tex?.Texture == null) { why = "no texture"; return null; }
            if (!tex.HasData) { why = "came without its pixels"; return null; }
            try
            {
                if (format == "dds")
                {
                    var dds = CodeWalker.Utils.DDSIO.GetDDSFile(tex.Texture);
                    if (dds == null || dds.Length == 0) { why = "the DDS writer returned nothing"; return null; }
                    return dds;
                }
                return TexturePng_Q1(tex, out why);
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        private static byte[] TexturePng_Q1(AssetTextureInfo tex, out string why)
        {
            why = null;
            var px = CodeWalker.Utils.DDSIO.GetPixels(tex.Texture, 0);
            int w = Math.Max(tex.Width, 1), h = Math.Max(tex.Height, 1);
            if (px == null || px.Length < w * h * 4)
            {
                why = $"{tex.Format} cannot be decoded to pixels here - export it as DDS instead";
                return null;
            }

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, w, h);
            var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(px, y * w * 4, bits.Scan0 + y * bits.Stride, w * 4);
            }
            finally { bmp.UnlockBits(bits); }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static string SafeTextureName_Q1(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "texture";
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }
    }
}

