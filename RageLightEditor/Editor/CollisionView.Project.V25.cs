using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class CollisionView
    {
        private readonly Dictionary<uint, YbnFile> projectYbns_V25 = new Dictionary<uint, YbnFile>();
        public int ProjectVersion_V25 { get; private set; }

        public void SetProjectYbns_V25(IEnumerable<YbnFile> files)
        {
            var next = new Dictionary<uint, YbnFile>();
            if (files != null)
                foreach (var b in files)
                {
                    uint h = b?.RpfFileEntry?.ShortNameHash ?? 0;
                    if (h != 0 && b.Bounds != null) next[h] = b;
                }
            var changed = new List<uint>();
            lock (sync)
            {
                foreach (var kv in next)
                    if (!projectYbns_V25.TryGetValue(kv.Key, out var was) || !ReferenceEquals(was, kv.Value)) changed.Add(kv.Key);
                foreach (var h in projectYbns_V25.Keys)
                    if (!next.ContainsKey(h)) changed.Add(h);
                projectYbns_V25.Clear();
                foreach (var kv in next) projectYbns_V25[kv.Key] = kv.Value;
            }
            foreach (var h in changed) Forget(h);
            if (changed.Count > 0) ProjectVersion_V25++;
        }

        private YbnFile ProjectYbn_V25(uint hash)
        {
            lock (sync) return projectYbns_V25.TryGetValue(hash, out var b) ? b : null;
        }

        public List<(uint Hash, BoundingBox Box)> ProjectBounds_V25()
        {
            var list = new List<(uint, BoundingBox)>();
            lock (sync)
            {
                foreach (var kv in projectYbns_V25)
                {
                    var bd = kv.Value?.Bounds;
                    if (bd == null) continue;
                    var mn = bd.BoxMin; var mx = bd.BoxMax;
                    if (!(mx.X > mn.X) || !(mx.Y > mn.Y)) continue;
                    list.Add((kv.Key, new BoundingBox(new Vector3(mn.X, mn.Y, mn.Z), new Vector3(mx.X, mx.Y, mx.Z))));
                }
            }
            return list;
        }

        public bool HasProjectYbns_V25 { get { lock (sync) return projectYbns_V25.Count > 0; } }
    }
}

