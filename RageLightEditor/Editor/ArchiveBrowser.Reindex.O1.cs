using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class ArchiveBrowser
    {
        public void ReindexArchive(RpfFile archive)
        {
            if (!Ready || archive == null) return;
            var top = archive.GetTopParent();
            var path = top?.FilePath;
            if (string.IsNullOrEmpty(path)) return;

            index.RemoveAll(e =>
            {
                var f = e.File?.File;
                if (f == null) return false;
                var p = f.GetTopParent()?.FilePath;
                return string.Equals(p, path, StringComparison.OrdinalIgnoreCase);
            });

            AddArchiveEntries(top);
        }

        private void AddArchiveEntries(RpfFile rpf, int depth = 0)
        {
            if (rpf == null || depth > 32) return;
            if (rpf.AllEntries != null)
            {
                foreach (var e in rpf.AllEntries)
                    if (e is RpfFileEntry fe) index.Add(new Entry(fe));
            }
            if (rpf.Children == null) return;
            foreach (var c in rpf.Children) AddArchiveEntries(c, depth + 1);
        }

        public void IndexNewArchive(RpfFile archive)
        {
            if (archive == null) return;
            var top = archive.GetTopParent();
            if (top == null) return;
            if (!Ready)
            {
                Roots.Add(top);
                AddArchiveEntries(top);
                return;
            }
            bool known = false;
            foreach (var r in Roots)
                if (ReferenceEquals(r, top)) { known = true; break; }
            if (!known) Roots.Add(top);
            ReindexArchive(top);
        }

        public void ForgetArchive(string physicalPath)
        {
            if (string.IsNullOrEmpty(physicalPath)) return;
            index.RemoveAll(e =>
            {
                var p = e.File?.File?.GetTopParent()?.FilePath;
                return string.Equals(p, physicalPath, StringComparison.OrdinalIgnoreCase);
            });
            Roots.RemoveAll(r => string.Equals(r?.FilePath, physicalPath, StringComparison.OrdinalIgnoreCase));
        }
    }
}

