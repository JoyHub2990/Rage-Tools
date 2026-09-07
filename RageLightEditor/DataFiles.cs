using System;
using System.IO;
using System.Reflection;

namespace RageLightEditor
{
    internal static class DataFiles
    {
        private static readonly string[] Names = { "ShadersGen9Conversion.xml", "strings.txt" };

        public static void EnsureExtracted()
        {
            string dir;
            try { dir = AppContext.BaseDirectory; }
            catch { return; }
            if (string.IsNullOrEmpty(dir)) return;

            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in Names)
            {
                try
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path)) continue;

                    using var src = asm.GetManifestResourceStream(name);
                    if (src == null) continue;
                    using var dst = File.Create(path);
                    src.CopyTo(dst);
                }
                catch
                {
                }
            }
        }
    }
}

