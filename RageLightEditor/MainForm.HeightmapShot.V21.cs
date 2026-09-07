using System;
using System.Drawing;
using System.Drawing.Imaging;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool hmapShotDone_V21;

        partial void OnWorldTick_HeightmapShot_V21()
        {
            if (hmapShotDone_V21) return;
            var path = Environment.GetEnvironmentVariable("RLE_HMAPSHOT");
            if (string.IsNullOrEmpty(path)) return;
            var sd = SpaceDataOrNull;
            if (sd == null || !gameFiles.Ready) return;

            sd.EnsureHeightmap();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sd.HeightmapLoading && sw.Elapsed.TotalSeconds < 120) System.Threading.Thread.Sleep(100);
            hmapShotDone_V21 = true;

            var surf = HeightmapSurfaceOrNull_V21(sd);
            if (surf == null) { Console.WriteLine("HMAPSHOT nothing built"); return; }
            var verts = surf.GetTriangleVertices();
            if (verts == null || verts.Length < 3) { Console.WriteLine("HMAPSHOT no triangles"); return; }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.Position.X < minX) minX = v.Position.X;
                if (v.Position.X > maxX) maxX = v.Position.X;
                if (v.Position.Y < minY) minY = v.Position.Y;
                if (v.Position.Y > maxY) maxY = v.Position.Y;
            }

            const int W = 1100;
            int H = (int)Math.Round(W * (maxY - minY) / Math.Max(1e-3f, maxX - minX));
            H = Math.Max(64, Math.Min(4000, H));

            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(255, 16, 18, 22));
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                var pts = new PointF[3];
                using (var brush = new SolidBrush(System.Drawing.Color.Black))
                {
                    for (int i = 0; i + 2 < verts.Length; i += 3)
                    {
                        int r = 0, gg = 0, b = 0, a = 0;
                        for (int k = 0; k < 3; k++)
                        {
                            var p = verts[i + k].Position;
                            pts[k] = new PointF(
                                (p.X - minX) / (maxX - minX) * (W - 1),
                                (1.0f - (p.Y - minY) / (maxY - minY)) * (H - 1));
                            uint c = verts[i + k].Colour;
                            r += (int)(c & 0xFF); gg += (int)((c >> 8) & 0xFF);
                            b += (int)((c >> 16) & 0xFF); a += (int)((c >> 24) & 0xFF);
                        }
                        brush.Color = System.Drawing.Color.FromArgb(Math.Min(255, a / 3), r / 3, gg / 3, b / 3);
                        g.FillPolygon(brush, pts);
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine($"HMAPSHOT {path} {W}x{H} from {verts.Length / 3:N0} triangles " +
                              $"over {maxX - minX:0} x {maxY - minY:0} m");
        }
    }
}

