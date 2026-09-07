using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class CwProject
    {
        public readonly List<YbnFile> YbnFiles = new List<YbnFile>();

        public YbnFile LoadYbn(string fullPath, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(fullPath)) { problem = "missing ybn: " + fullPath; return null; }
                var b = new YbnFile();
                b.Load(File.ReadAllBytes(fullPath));
                if (b.Bounds == null) { problem = "not a collision file: " + Path.GetFileName(fullPath); return null; }
                b.FilePath = fullPath;
                b.RpfFileEntry ??= new RpfResourceFileEntry();
                b.RpfFileEntry.Name = Path.GetFileName(fullPath);
                b.Name = b.RpfFileEntry.Name;
                var stem = Path.GetFileNameWithoutExtension(fullPath);
                JenkIndex.Ensure(stem);
                b.RpfFileEntry.ShortNameHash = JenkHash.GenHash(stem.ToLowerInvariant());
                b.RpfFileEntry.NameHash = JenkHash.GenHash(b.Name.ToLowerInvariant());
                b.Loaded = true;
                YbnFiles.Add(b);
                return b;
            }
            catch (Exception ex) { problem = Path.GetFileName(fullPath) + ": " + ex.Message; return null; }
        }

        public void LoadYbnFiles(List<string> problems)
        {
            YbnFiles.Clear();
            foreach (var rel in YbnFilenames)
            {
                LoadYbn(GetFullFilePath(rel), out var problem);
                if (problem != null) problems?.Add(problem);
            }
        }

        public bool SyncYbnFiles()
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in YbnFilenames)
            {
                var n = Path.GetFileName(rel);
                if (!string.IsNullOrEmpty(n)) keep.Add(n);
            }
            return YbnFiles.RemoveAll(b => b?.RpfFileEntry?.Name == null || !keep.Contains(b.RpfFileEntry.Name)) > 0;
        }

        public YbnFile FindYbn(uint shortNameHash)
        {
            foreach (var b in YbnFiles)
                if (b?.RpfFileEntry != null && b.RpfFileEntry.ShortNameHash == shortNameHash) return b;
            return null;
        }
    }
}

