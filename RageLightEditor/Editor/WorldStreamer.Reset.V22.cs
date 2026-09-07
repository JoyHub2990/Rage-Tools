using System.Collections.Generic;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        public int UnloadYmaps_V22(HashSet<uint> hashes)
        {
            if (hashes == null || hashes.Count == 0) return 0;
            int n = 0;
            foreach (var h in hashes)
            {
                if (!nodes.TryGetValue(h, out var node) || node == null) continue;
                if (node.Ymap == null && !node.RawLoaded && !node.Prepared) continue;
                node.Ymap = null;
                node.PendingParent = null;
                node.RawLoaded = false;
                node.Prepared = false;
                node.LoadFailed = false;
                n++;
            }
            if (n == 0) return 0;
            Visible.Clear();
            Fade.Clear();
            int open = 0, resident = 0;
            foreach (var nd in nodes.Values) { if (nd.Ymap != null) open++; if (nd.Prepared) resident++; }
            YmapsOpen = open;
            YmapsResident = resident;
            residentVersion++;
            near = null;
            lastSelectPos = new Vector3(float.MaxValue);
            worldChanged = true;
            return n;
        }
    }
}

