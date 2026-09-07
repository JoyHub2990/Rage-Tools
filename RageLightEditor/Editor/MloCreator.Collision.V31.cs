using System;
using System.Collections.Generic;
using System.IO;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorSession
    {
        public static string SiblingYbn_V31(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(modelPath);
                if (string.IsNullOrEmpty(dir)) return null;
                var ybn = Path.Combine(dir, Path.GetFileNameWithoutExtension(modelPath) + ".ybn");
                return File.Exists(ybn) ? ybn : null;
            }
            catch { return null; }
        }

        public List<string> CollisionWarnings_V31()
        {
            var list = new List<string>();
            bool named = !string.IsNullOrWhiteSpace(PhysicsDictionary);
            if (named && ShellYbnResolves_V34(out _)) return list;

            var shellPath = ShellFile?.Path;
            bool shellHasYbn = SiblingYbn_V31(shellPath) != null;
            var sd = ShellFile != null ? Drawable(ShellFile) : null;
            bool shellEmbedded = (sd as CodeWalker.GameFiles.Drawable)?.Bound != null;

            if (!named && (shellHasYbn || shellEmbedded))
                list.Add("This interior has collision" + (shellHasYbn ? " (" + Path.GetFileName(SiblingYbn_V31(shellPath)) + ")" : "") +
                         " but its Physics field is empty - the .ytyp would name no collision and you would fall through it. " +
                         "Put the collision dictionary's name in Interior > Physics (usually '" + (Name ?? "") + "').");
            else if (!named && ShellFile != null)
                list.Add("No collision found for the shell and the Physics field is empty - the interior will have nothing to stand on. " +
                         "Export a .ybn beside the shell, or fill in Interior > Physics if the collision ships in a dictionary of its own.");

            return list;
        }

        public List<string> CollisionFiles_V31()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            void Add(string p) { var y = SiblingYbn_V31(p); if (y != null && seen.Add(y)) list.Add(y); }
            if (!string.IsNullOrWhiteSpace(ShellYbnPath_V34) && System.IO.File.Exists(ShellYbnPath_V34)
                && seen.Add(ShellYbnPath_V34)) list.Add(ShellYbnPath_V34);
            Add(ShellFile?.Path);
            foreach (var e in Entities) if (e != null && e.Include) Add(e.SourceFile?.Path);
            return list;
        }
    }
}

