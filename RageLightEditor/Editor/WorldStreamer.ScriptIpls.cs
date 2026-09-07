using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        public (int added, int removed) SetScriptIplNodes(IEnumerable<MapDataStoreNode> add, IEnumerable<uint> remove)
        {
            int a = 0, r = 0;
            if (remove != null)
                foreach (var h in remove)
                {
                    if (!nodes.TryGetValue(h, out var n)) continue;
                    n.Ymap = null;
                    n.PendingParent = null;
                    n.RawLoaded = false;
                    n.Prepared = false;
                    n.LoadFailed = false;
                    nodes.Remove(h);
                    r++;
                }
            if (add != null)
                foreach (var n in add)
                {
                    if (n == null || nodes.ContainsKey(n.Name)) continue;
                    var min = n.streamingExtentsMin;
                    var max = n.streamingExtentsMax;
                    if (!(max.X > min.X) || !(max.Y > min.Y)) continue;
                    nodes[n.Name] = new MapNode
                    {
                        Hash = n.Name,
                        Name = n.Name.ToString(),
                        Min = new Vector3(min.X, min.Y, min.Z),
                        Max = new Vector3(max.X, max.Y, max.Z),
                        ParentHash = n.ParentName.Hash,
                        ContentFlags = n.ContentFlags,
                        RangeScale = RangeScaleFor(n.ContentFlags),
                    };
                    a++;
                }
            if (a + r > 0)
            {
                near = null;
                residentVersion++;
                worldChanged = true;
            }
            return (a, r);
        }
    }
}

