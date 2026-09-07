using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace RageLightEditor.Editor
{
    public static class ImageImport_V18
    {
        public const string Filter =
            "Images (*.dds;*.png;*.jpg;*.jpeg;*.bmp)|*.dds;*.png;*.jpg;*.jpeg;*.bmp|" +
            "DirectDraw Surface (*.dds)|*.dds|All files (*.*)|*.*";

        public static bool CanRead(string path)
        {
            var e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return e == ".dds" || e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp";
        }

        public static Texture Load_V18(string path, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "no such file"; return null; }
                var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var ext = Path.GetExtension(path).ToLowerInvariant();

                Texture tex = ext == ".dds"
                    ? DDSIO.GetTexture(File.ReadAllBytes(path))
                    : FromBitmap_V18(path, name);

                if (tex?.Data?.FullData == null) { error = "nothing decoded out of " + Path.GetFileName(path); return null; }
                tex.Name = name;
                tex.NameHash = JenkHash.GenHash(name);
                return tex;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static Texture FromBitmap_V18(string path, string name)
        {
            using var bmp = new Bitmap(path);
            int w = bmp.Width, h = bmp.Height;
            var data = new byte[w * h * 4];
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(bd.Scan0, data, 0, data.Length); }
            finally { bmp.UnlockBits(bd); }

            return new Texture
            {
                Name = name,
                Width = (ushort)w,
                Height = (ushort)h,
                Depth = 1,
                Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = (ushort)(w * 4),
                Data = new TextureData { FullData = data },
            };
        }

        public static string Describe_V18(Texture t)
        {
            if (t == null) return "";
            long bytes = t.Data?.FullData?.LongLength ?? 0;
            string size = bytes > 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB" : $"{bytes / 1024.0:0} KB";
            return $"{t.Width}x{t.Height} {t.Format} ({size})";
        }
    }
}

