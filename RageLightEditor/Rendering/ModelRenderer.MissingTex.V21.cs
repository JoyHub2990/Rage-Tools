using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class ModelRenderer
    {
        public readonly HashSet<string> MissingTextures_V21 = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public void ClearMissingTextures_V21() { lock (MissingTextures_V21) MissingTextures_V21.Clear(); }

        public string[] MissingTexturesSnapshot_V21()
        {
            lock (MissingTextures_V21)
            {
                var a = new string[MissingTextures_V21.Count];
                MissingTextures_V21.CopyTo(a);
                System.Array.Sort(a, System.StringComparer.OrdinalIgnoreCase);
                return a;
            }
        }

        private void NoteMissing_V21(TextureBase tb, ShaderResourceView got)
        {
            if (got != null || tb == null) return;
            var n = tb.Name;
            if (string.IsNullOrEmpty(n)) return;
            lock (MissingTextures_V21) MissingTextures_V21.Add(n);
        }
    }
}

