using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class Scene
    {
        public int MoveLightsToProp_V35(IEnumerable<LightAttributes> lights, LoadedFile target, bool keepWorldPosition = true)
        {
            if (target == null || lights == null) return 0;
            var list = lights.Where(l => l != null).Distinct().ToList();
            if (list.Count == 0) return 0;

            var touched = new HashSet<LoadedFile>();
            int moved = 0;
            foreach (var l in list)
            {
                var from = OwnerFile(l);
                if (ReferenceEquals(from, target)) continue;

                if (keepWorldPosition)
                {
                    var world = GetInstance(l).WorldPosition;
                    ownerOf[l] = target;
                    var back = GetInstance(l);
                    var delta = world - back.WorldPosition;
                    l.Position += delta;
                }
                else ownerOf[l] = target;

                moved++;
                if (from != null) touched.Add(from);
                touched.Add(target);
            }

            foreach (var f in touched) if (f != null && !f.ReadOnly) f.Dirty = true;
            if (moved > 0) InvalidateLightBuffers_V35();
            return moved;
        }

        public List<LightAttributes> SelectedLightList_V35()
        {
            var list = new List<LightAttributes>();
            foreach (var i in SelectedIndices)
                if (i >= 0 && i < Lights.Count && Lights[i] != null) list.Add(Lights[i]);
            return list;
        }

        public List<LightAttributes> LightsOf_V35(LoadedFile f)
            => Lights.Where(l => l != null && ReferenceEquals(OwnerFile(l), f)).ToList();

        private void InvalidateLightBuffers_V35()
        {
            GeometryVersion++;
        }
    }
}

