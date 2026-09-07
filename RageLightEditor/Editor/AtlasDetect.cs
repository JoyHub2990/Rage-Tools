using System;
using System.Collections.Generic;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public static class AtlasDetect
    {
        private static readonly Dictionary<string, (int gx, int gy)> cache =
            new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

        public static (int gx, int gy) Detect(GameTexture tex, int frames)
        {
            if (tex == null) return (1, 1);
            var key = (tex.Name ?? "?") + "|" + tex.Width + "x" + tex.Height + "|" + frames;
            lock (cache)
                if (cache.TryGetValue(key, out var hit)) return hit;

            var res = Compute(tex, frames);
            lock (cache) cache[key] = res;
            return res;
        }

        public static (int gx, int gy) GameGrid_V41(int width, int height, int frames)
        {
            if (frames <= 1 || width <= 0 || height <= 0) return (1, 1);
            int bestX = frames, bestY = 1;
            float bestD = float.MaxValue;
            for (int cy = 1; cy <= frames; cy++)
            {
                if (frames % cy != 0) continue;
                int cx = frames / cy;
                float d = Math.Abs((float)width / cx - (float)height / cy);
                if (d < bestD) { bestD = d; bestX = cx; bestY = cy; }
            }
            return (bestX, bestY);
        }

        private static (int gx, int gy) Compute(GameTexture tex, int frames)
        {
            int w = tex.Width, h = tex.Height;
            if (w <= 0 || h <= 0 || frames <= 1) return (1, 1);

            var game = GameGrid_V41(w, h, frames);
            if (game.gx * game.gy == frames && frames > 1) return game;

            if (Math.Min(w, h) <= 128) return SquareCells(w, h, frames);

            var (cols, rows) = ProjectBands(tex);
            if (cols > 0 && rows > 0)
            {
                float cw = (float)w / cols, ch = (float)h / rows;
                var aspect = cw / ch;
                if (cols * rows >= frames && aspect >= 0.4f && aspect <= 2.5f)
                    return (cols, rows);

                int gx = Math.Max(1, cols), gy = Math.Max(1, rows);
                while (gx * gy < frames)
                {
                    if ((float)w / (gx + 1) >= (float)h / (gy + 1)) gx++;
                    else gy++;
                }
                return (gx, gy);
            }
            return SquareCells(w, h, frames);
        }

        private static (int gx, int gy) SquareCells(int w, int h, int frames)
        {
            int g = Gcd(w, h);
            int rx = Math.Max(1, w / g), ry = Math.Max(1, h / g);
            int k = 1;
            while ((long)(rx * k) * (ry * k) < frames) k++;
            return (rx * k, ry * k);
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) { var t = b; b = a % b; a = t; }
            return Math.Max(1, a);
        }

        private static (int cols, int rows) ProjectBands(GameTexture tex)
        {
            try
            {
                var data = tex.Data?.FullData;
                if (data == null || data.Length < 64) return (0, 0);
                int w = tex.Width, h = tex.Height;
                int bw = Math.Max(1, w / 4), bh = Math.Max(1, h / 4);

                var fmt = tex.Format.ToString();
                int bs = fmt.Contains("DXT1") ? 8 : 16;
                long need = (long)bw * bh * bs;
                if (data.Length < need) return (0, 0);

                var colSum = new float[bw];
                var rowSum = new float[bh];
                for (int by = 0; by < bh; by++)
                {
                    long baseOff = (long)by * bw * bs;
                    for (int bx = 0; bx < bw; bx++)
                    {
                        var o = baseOff + (long)bx * bs;
                        float v = (data[o] + data[o + 1]) * 0.5f;
                        colSum[bx] += v;
                        rowSum[by] += v;
                    }
                }
                return (CountBands(colSum), CountBands(rowSum));
            }
            catch { return (0, 0); }
        }

        private static int CountBands(float[] v)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var x in v)
            {
                if (x < min) min = x;
                if (x > max) max = x;
            }
            if (max - min < 1e-3f) return 0;
            var thr = min + (max - min) * 0.35f;
            int bands = 0;
            bool inBand = false;
            foreach (var x in v)
            {
                if (x > thr && !inBand) { bands++; inBand = true; }
                else if (x <= thr) inBand = false;
            }
            return bands;
        }
    }
}

