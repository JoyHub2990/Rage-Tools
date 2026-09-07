using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorSession
    {
        public string ShellYbnPath_V34;
        public string ShellYbnNote_V34;

        public bool ImportShellYbn_V34(string path, out string problem)
        {
            problem = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                { problem = "no such file"; return false; }

                var ybn = new YbnFile();
                ybn.Load(File.ReadAllBytes(path));
                if (ybn.Bounds == null)
                { problem = Path.GetFileName(path) + " is not a collision file."; return false; }

                ShellYbnPath_V34 = path;
                PhysicsDictionary = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

                var mn = ybn.Bounds.BoxMin; var mx = ybn.Bounds.BoxMax;
                ShellYbnNote_V34 =
                    $"{Path.GetFileName(path)} - {ybn.Bounds.Type}, " +
                    $"{mx.X - mn.X:0.#} x {mx.Y - mn.Y:0.#} x {mx.Z - mn.Z:0.#} m. " +
                    $"The .ytyp will name '{PhysicsDictionary}'.";
                return true;
            }
            catch (Exception ex) { problem = Path.GetFileName(path) + ": " + ex.Message; return false; }
        }

        public void ClearShellYbn_V34()
        {
            ShellYbnPath_V34 = null;
            ShellYbnNote_V34 = null;
        }

        public bool ShellYbnResolves_V34(out string where)
        {
            where = null;
            if (!string.IsNullOrWhiteSpace(ShellYbnPath_V34) && File.Exists(ShellYbnPath_V34))
            { where = ShellYbnPath_V34; return true; }
            var sib = SiblingYbn_V31(ShellFile?.Path);
            if (sib != null) { where = sib; return true; }
            return false;
        }
    }
}

