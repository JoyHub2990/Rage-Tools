using System;
using System.IO;

namespace RageLightEditor.Editor
{
    public partial class RpfExplorer
    {
        public string CurrentBranchPrefix_V23()
        {
            var n = Current;
            if (n == null || n.IsRoot) return null;
            if (n.Dir != null) return n.Dir.Path;
            if (n.Archive != null) return n.Archive.Path;
            if (n.IsFs && !string.IsNullOrEmpty(n.FsPath) && GameRoot != null && !string.IsNullOrEmpty(GameRoot.FsPath))
            {
                var root = GameRoot.FsPath.TrimEnd('\\', '/');
                if (n.FsPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) && n.FsPath.Length > root.Length)
                    return n.FsPath.Substring(root.Length).TrimStart('\\', '/');
            }
            return null;
        }

        public bool CurrentBranchOutsideInstall_V23()
        {
            var n = Current;
            if (n == null || !n.IsFs || n.IsRoot) return false;
            return CurrentBranchPrefix_V23() == null;
        }

        public string CurrentBranchLabel_V23() => Current?.Label ?? "";
    }
}

