using System;
using System.IO;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class AssetPreview
    {
        private static uint ResourceVersionOf(string ext)
        {
            switch (ext)
            {
                case ".ydr": return 165;
                case ".ydd": return 165;
                case ".yft": return 162;
                case ".ytd": return 13;
                case ".ybn": return 43;
                default: return 0;
            }
        }

        public static bool CanPreviewDiskFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return ResourceVersionOf((Path.GetExtension(path) ?? "").ToLowerInvariant()) != 0;
        }

        public bool OpenDiskFile(string path)
        {
            Close();
            Error = "";

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Error = "that file is gone"; return false; }
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            uint ver = ResourceVersionOf(ext);
            if (ver == 0) { Error = "nothing here reads " + ext + " yet"; return false; }
            if (game == null || !game.Ready) { Error = "game archives are not open yet"; return false; }

            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) { Error = "could not read " + Path.GetFileName(path) + ": " + ex.Message; return false; }

            var name = Path.GetFileName(path);
            try
            {
                switch (ext)
                {
                    case ".ydr": return OpenDiskYdr(data, ver, name, path);
                    case ".yft": return OpenDiskYft(data, ver, name, path);
                    case ".ydd": return OpenDiskYdd(data, ver, name, path);
                    case ".ytd": return OpenDiskYtd(data, ver, name, path);
                    case ".ybn": return OpenDiskYbn(data, ver, name, path);
                }
            }
            catch (Exception ex)
            {
                var msg = "could not read " + name + ": " + ex.Message;
                Close();
                Error = msg;
                return false;
            }
            return false;
        }

        private void AdoptDiskFile(AssetKind kind, string name, string path, long size)
        {
            var shortName = Path.GetFileNameWithoutExtension(name) ?? "";
            JenkIndex.Ensure(shortName);
            var e = new RpfResourceFileEntry
            {
                Name = name,
                NameLower = name.ToLowerInvariant(),
                NameHash = JenkHash.GenHash(name.ToLowerInvariant()),
                ShortNameHash = JenkHash.GenHash(shortName.ToLowerInvariant()),
                FileSize = (uint)Math.Min(size, uint.MaxValue),
            };
            e.Path = path;
            Entry = e;
            Kind = kind;
            Stats = new AssetStats { Name = name, Path = path, Kind = kind, FileSize = size };
        }

        private bool OpenDiskYdr(byte[] data, uint ver, string name, string path)
        {
            var ydr = new YdrFile();
            RpfFile.LoadResourceFile(ydr, data, ver);
            if (ydr.Drawable == null) { Error = name + " did not parse as a drawable"; return false; }
            AdoptDiskFile(AssetKind.Drawable, name, path, data.LongLength);
            Ydr = ydr;
            Drawable = ydr.Drawable;
            TxdContext = FindTxdContext(Entry, out var archDict);
            MeasureDrawable(Drawable, 1);
            Build(archDict, () => builder.BuildFromDrawable(Drawable, name));
            return true;
        }

        private bool OpenDiskYft(byte[] data, uint ver, string name, string path)
        {
            var yft = new YftFile();
            RpfFile.LoadResourceFile(yft, data, ver);
            if (yft.Fragment == null) { Error = name + " did not parse as a fragment"; return false; }
            AdoptDiskFile(AssetKind.Fragment, name, path, data.LongLength);
            Yft = yft;
            Drawable = yft.Fragment.Drawable;
            TxdContext = FindTxdContext(Entry, out var archDict);

            int drawables = 0;
            foreach (var d in FragmentDrawables(yft.Fragment)) { MeasureDrawable(d, 0); drawables++; }
            Stats.Drawables = drawables;
            if (Drawable != null) Stats.Bounds = DrawableBounds(Drawable);

            Build(archDict, () => builder.BuildFromYft(yft));
            return true;
        }

        private bool OpenDiskYdd(byte[] data, uint ver, string name, string path)
        {
            var ydd = new YddFile();
            RpfFile.LoadResourceFile(ydd, data, ver);
            AdoptDiskFile(AssetKind.DrawableDict, name, path, data.LongLength);
            Ydd = ydd;
            dictDrawables = ydd.Drawables ?? ydd.DrawableDict?.Drawables?.data_items;
            if (dictDrawables == null || dictDrawables.Length == 0)
            {
                Error = name + " holds no drawables";
                return false;
            }
            for (int i = 0; i < dictDrawables.Length; i++)
            {
                var n = dictDrawables[i]?.Name;
                drawableNames.Add(string.IsNullOrEmpty(n) ? "drawable " + i : n);
            }
            Stats.Drawables = dictDrawables.Length;
            TxdContext = FindTxdContext(Entry, out _);
            return SelectDrawable(0);
        }

        private bool OpenDiskYtd(byte[] data, uint ver, string name, string path)
        {
            var ytd = new YtdFile();
            RpfFile.LoadResourceFile(ytd, data, ver);
            if (ytd.TextureDict == null) { Error = name + " did not parse as a texture dictionary"; return false; }
            AdoptDiskFile(AssetKind.TextureDict, name, path, data.LongLength);
            Ytd = ytd;
            var texs = ytd.TextureDict.Textures?.data_items;
            if (texs != null)
                foreach (var t in texs)
                {
                    if (t == null) continue;
                    var info = Describe(t);
                    textureList.Add(info);
                    Stats.Textures.Add(info);
                    Stats.TextureBytes += info.DataBytes;
                }
            return true;
        }

        private bool OpenDiskYbn(byte[] data, uint ver, string name, string path)
        {
            var ybn = new YbnFile();
            RpfFile.LoadResourceFile(ybn, data, ver);
            if (ybn.Bounds == null) { Error = name + " did not parse as a collision"; return false; }
            AdoptDiskFile(AssetKind.Collision, name, path, data.LongLength);
            Ybn = ybn;
            var b = ybn.Bounds;
            Stats.BoundType = b.Type.ToString();
            Stats.Bounds = new BoundingBox(b.BoxMin, b.BoxMax);
            MeasureBounds(b, 0);
            CollisionSink?.Invoke(ybn);
            return true;
        }
    }
}

