using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        public IEnumerable<YmapFile> WalkedYmaps => lodYmaps.Values;
        public int WalkedYmapCount => lodYmaps.Count;

        public void SnapshotWalkedYmaps(List<YmapFile> into)
        {
            into.Clear();
            foreach (var y in lodYmaps.Values) if (y != null) into.Add(y);
        }
    }
}

