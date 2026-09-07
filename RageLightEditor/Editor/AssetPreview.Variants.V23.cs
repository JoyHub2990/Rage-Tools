using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class AssetPreview
    {
        private static readonly Regex VariantRx_V23 =
            new Regex(@"^(?<p>.+_diff_\d+_)(?<v>[a-z])(?<s>(_[a-z0-9]+)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public readonly List<char> TextureVariants = new List<char>();
        public char AuthoredVariant { get; private set; }
        public char TextureVariant { get; private set; }
        private readonly Dictionary<char, string> variantDiskYtd_V23 = new Dictionary<char, string>();
        private YtdFile variantYtd_V23;

        private void ScanTextureVariants_V23()
        {
            TextureVariants.Clear();
            variantDiskYtd_V23.Clear();
            AuthoredVariant = '\0';
            var shaders = Drawable?.ShaderGroup?.Shaders?.data_items;
            if (shaders == null) return;

            Match m = null;
            foreach (var sh in shaders)
            {
                var ps = sh?.ParametersList?.Parameters; var hs = sh?.ParametersList?.Hashes;
                if (ps == null || hs == null) continue;
                for (int i = 0; i < ps.Length && i < hs.Length; i++)
                {
                    if ((uint)hs[i] != (uint)ShaderParamNames.DiffuseSampler) continue;
                    if (!(ps[i].Data is TextureBase tb) || string.IsNullOrEmpty(tb.Name)) continue;
                    var mm = VariantRx_V23.Match(tb.Name);
                    if (mm.Success) { m = mm; break; }
                }
                if (m != null) break;
            }
            if (m == null) return;
            AuthoredVariant = char.ToLowerInvariant(m.Groups["v"].Value[0]);
            string prefix = m.Groups["p"].Value.ToLowerInvariant(), suffix = m.Groups["s"].Value.ToLowerInvariant();

            var letters = new SortedSet<char>();
            if (Entry?.Parent != null)
            {
                foreach (var f in Entry.Parent.Files)
                {
                    var n = f?.NameLower ?? f?.Name?.ToLowerInvariant();
                    if (n == null || !n.EndsWith(".ytd")) continue;
                    var stem = n.Substring(0, n.Length - 4);
                    if (stem.Length == prefix.Length + 1 + suffix.Length &&
                        stem.StartsWith(prefix) && stem.EndsWith(suffix) && char.IsLetter(stem[prefix.Length]))
                        letters.Add(stem[prefix.Length]);
                }
            }
            else if (!string.IsNullOrEmpty(Entry?.Path) && File.Exists(Entry.Path))
            {
                var dir = Path.GetDirectoryName(Entry.Path);
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir ?? ".", prefix + "?" + suffix + ".ytd"))
                    {
                        var stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (stem.Length != prefix.Length + 1 + suffix.Length) continue;
                        char v = stem[prefix.Length];
                        letters.Add(v);
                        variantDiskYtd_V23[v] = f;
                    }
                }
                catch { }
            }
            letters.Add(AuthoredVariant);
            TextureVariants.AddRange(letters);
        }

        public bool SetTextureVariant(char v)
        {
            v = char.ToLowerInvariant(v);
            if (v == AuthoredVariant) v = '\0';
            if (v == TextureVariant) return false;
            TextureVariant = v;
            variantYtd_V23 = null;
            if (v != '\0' && variantDiskYtd_V23.TryGetValue(v, out var path))
            {
                try { var y = new YtdFile(); y.Load(File.ReadAllBytes(path)); variantYtd_V23 = y; }
                catch { variantYtd_V23 = null; }
            }
            Rebuild_V22();
            return true;
        }

        private Func<TextureBase, TextureBase> VariantRemap_V23()
        {
            char v = TextureVariant;
            if (v == '\0') return null;
            return tb =>
            {
                if (tb?.Name == null) return null;
                var m = VariantRx_V23.Match(tb.Name);
                if (!m.Success) return null;
                var name = m.Groups["p"].Value + v + m.Groups["s"].Value;
                return new TextureBase { Name = name, NameHash = JenkHash.GenHash(name.ToLowerInvariant()) };
            };
        }
    }
}

