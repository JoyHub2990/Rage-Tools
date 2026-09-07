using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        public InteriorAmbientResolver InteriorAmbient_L2;
        private bool l2StampPending;
        public bool InteriorAmbientStampPending_L2 => l2StampPending;
        public int InteriorAmbientStamped_L2 { get; private set; }

        private void StampInstance_L2(RenderMesh inst, YmapEntityDef e)
        {
            var r = InteriorAmbient_L2;
            if (r == null || e == null || (e.MloParent == null && e.MloInstance == null)) return;
            if (!r.Ready) { l2StampPending = true; return; }
            Apply_L2(inst, r.Resolve(e));
        }

        private void Apply_L2(RenderMesh inst, in InteriorAmbientResolver.EntityAmbient a)
        {
            InteriorAmbientResolver.Apply(inst, a);
            if (a.InInterior > 0.5f) InteriorAmbientStamped_L2++;
        }

        public void RestampInteriorAmbient_L2()
        {
            var r = InteriorAmbient_L2;
            if (r == null || !r.Ready) return;
            l2StampPending = false;
            InteriorAmbientStamped_L2 = 0;
            foreach (var kv in ownerByMesh)
            {
                var e = kv.Value;
                if (e == null || (e.MloParent == null && e.MloInstance == null)) continue;
                Apply_L2(kv.Key, r.Resolve(e));
            }
        }
    }
}

