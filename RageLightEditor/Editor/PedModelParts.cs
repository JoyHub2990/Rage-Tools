using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public sealed class PedModelParts
    {
        public uint Hash;
        public string Name;
        public YddFile Ydd;
        public YtdFile Ytd;
        public PedFile Ymt;
        public readonly List<(string component, string drawableName, Drawable drawable, Texture texture)> Parts =
            new List<(string, string, Drawable, Texture)>();
        public bool FromYmt;
        public string Error;
        public double LoadMs;
        public string YddNames = "";
        public bool HasSkeleton;
        public SharpDX.Matrix RootSkin = SharpDX.Matrix.Identity;
        public float SkinSpread;

        public static readonly string[] ComponentNames = { "head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib" };
        private static readonly string[] KindSuffixes = { "u", "r", "m" };
        private static readonly string[] TexSuffixes = { "uni", "whi", "bla", "chi", "lat", "ara", "bal", "jap", "kor", "pak", "ind", "mid", "ita", "mex", "gsg", "afr", "asi", "eas", "sea", "spa", "chn", "gbr" };

        public static PedModelParts Load(GameFileManager gf, uint hash, string name)
        {
            var p = new PedModelParts { Hash = hash, Name = name ?? hash.ToString() };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var cache = gf?.Cache;
                if (cache == null || !gf.Ready) { p.Error = "game files not ready"; return p; }
                if (cache.YddDict == null || !cache.YddDict.TryGetValue(hash, out var yddEntry) || yddEntry == null) { p.Error = "no .ydd of that name"; return p; }

                p.Ydd = cache.GetYdd(hash);
                if (!gf.EnsureLoaded(p.Ydd) || p.Ydd?.Dict == null) { p.Error = "the .ydd did not load"; return p; }
                p.Ytd = cache.GetYtd(hash);
                if (p.Ytd != null && !gf.EnsureLoaded(p.Ytd)) p.Ytd = null;
                try { ReadSkeletonPose(p, gf, hash); } catch (Exception ex) { Console.WriteLine($"SCENMODEL ped {p.Name}: skeleton not read ({ex.Message})"); }

                try
                {
                    var files = yddEntry.Parent?.Files;
                    if (files != null)
                    {
                        string want = (yddEntry.GetShortNameLower() ?? "") + ".ymt";
                        foreach (var f in files)
                        {
                            if (f?.NameLower != want) continue;
                            p.Ymt = cache.RpfMan.GetFile<PedFile>(f);
                            break;
                        }
                    }
                }
                catch (Exception ex) { p.Ymt = null; Console.WriteLine($"SCENMODEL ped {p.Name}: ymt not read ({ex.Message})"); }

                if (p.Ymt?.VariationInfo != null) p.FromYmt = PickFromYmt(p);
                if (p.Parts.Count == 0) { p.FromYmt = false; PickByName(p); }
                if (p.Parts.Count == 0) p.Error = "no component drawables found in the .ydd";
                if (p.Ydd.Dict != null)
                {
                    var names = new List<string>();
                    foreach (var d in p.Ydd.Dict.Values) names.Add(d?.Name ?? "?");
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    p.YddNames = string.Join(" ", names);
                }
            }
            catch (Exception ex) { p.Error = ex.Message; }
            p.LoadMs = sw.Elapsed.TotalMilliseconds;
            return p;
        }

        private static void ReadSkeletonPose(PedModelParts p, GameFileManager gf, uint hash)
        {
            var yft = gf.Cache.GetYft(hash);
            if (yft == null || !gf.EnsureLoaded(yft)) return;
            var skel = yft.Fragment?.Drawable?.Skeleton;
            var bones = skel?.Bones?.Items;
            if (bones == null || bones.Length == 0) return;
            p.HasSkeleton = true;
            var root = bones[0].SkinTransform;
            root.M44 = 1.0f;
            float spread = 0.0f;
            var probe = new SharpDX.Vector3(0.3f, 0.2f, 1.0f);
            var rp = SharpDX.Vector3.TransformCoordinate(probe, root);
            for (int i = 1; i < bones.Length; i += Math.Max(1, bones.Length / 12))
            {
                var m = bones[i].SkinTransform; m.M44 = 1.0f;
                spread = Math.Max(spread, (SharpDX.Vector3.TransformCoordinate(probe, m) - rp).Length());
            }
            p.RootSkin = root;
            p.SkinSpread = spread;
        }

        private static bool PickFromYmt(PedModelParts p)
        {
            var vi = p.Ymt.VariationInfo;
            bool any = false;
            for (int i = 0; i < 12; i++)
            {
                MCPVDrawblData item = null;
                try
                {
                    var cd = vi.GetComponentData(i);
                    if (cd?.DrawblData3 != null && cd.DrawblData3.Length > 0) item = cd.DrawblData3[0];
                }
                catch { item = null; }
                if (item == null) continue;
                string dname = item.GetDrawableName(0);
                string tname = null;
                try { if (item.TexData != null && item.TexData.Length > 0) tname = item.GetTextureName(0); } catch { tname = null; }
                var d = FindDrawable(p.Ydd, dname);
                if (d == null)
                {
                    foreach (var k in KindSuffixes)
                    {
                        d = FindDrawable(p.Ydd, $"{ComponentNames[i]}_000_{k}");
                        if (d != null) { dname = $"{ComponentNames[i]}_000_{k}"; break; }
                    }
                }
                if (d == null) continue;
                var t = FindTexture(p.Ytd, tname) ?? GuessTexture(p.Ytd, ComponentNames[i]);
                p.Parts.Add((ComponentNames[i], dname, d, t));
                any = true;
            }
            return any;
        }

        private static void PickByName(PedModelParts p)
        {
            for (int i = 0; i < 12; i++)
            {
                foreach (var k in KindSuffixes)
                {
                    string dname = $"{ComponentNames[i]}_000_{k}";
                    var d = FindDrawable(p.Ydd, dname);
                    if (d == null) continue;
                    p.Parts.Add((ComponentNames[i], dname, d, GuessTexture(p.Ytd, ComponentNames[i])));
                    break;
                }
            }
        }

        private static Drawable FindDrawable(YddFile ydd, string name)
        {
            if (ydd?.Dict == null || string.IsNullOrEmpty(name)) return null;
            ydd.Dict.TryGetValue(JenkHash.GenHash(name.ToLowerInvariant()), out var d);
            return d;
        }

        private static Texture FindTexture(YtdFile ytd, string name)
        {
            if (ytd?.TextureDict == null || string.IsNullOrEmpty(name)) return null;
            var t = ytd.TextureDict.Lookup(JenkHash.GenHash(name.ToLowerInvariant()));
            return t?.Data?.FullData != null ? t : null;
        }

        private static Texture GuessTexture(YtdFile ytd, string component)
        {
            if (ytd?.TextureDict == null) return null;
            foreach (var s in TexSuffixes)
            {
                var t = FindTexture(ytd, $"{component}_diff_000_a_{s}");
                if (t != null) return t;
            }
            var dict = ytd.TextureDict.Dict;
            if (dict != null)
            {
                string prefix = component + "_diff_000_";
                foreach (var kv in dict)
                {
                    var n = kv.Value?.Name;
                    if (n != null && n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kv.Value.Data?.FullData != null) return kv.Value;
                }
            }
            return null;
        }
    }
}

