using System;
using System.Collections.Generic;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class Scene
    {
        public readonly MloImportRegistry Imports_R1 = new MloImportRegistry();

        public int RemoveMloMeshes_R1(IReadOnlyList<RenderMesh> meshes)
        {
            if (MloModel == null || meshes == null || meshes.Count == 0) return 0;
            var drop = new HashSet<RenderMesh>(meshes);
            int removed = 0;
            for (int i = MloModel.Meshes.Count - 1; i >= 0; i--)
            {
                var m = MloModel.Meshes[i];
                if (m == null || !drop.Contains(m)) continue;
                MloModel.Meshes.RemoveAt(i);
                try { m.Dispose(); } catch { }
                removed++;
            }
            if (removed > 0)
            {
                var b = new BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                bool any = false;
                foreach (var m in MloModel.Meshes)
                {
                    if (m == null) continue;
                    var wb = m.WorldBounds;
                    if (wb.Maximum.X <= wb.Minimum.X) continue;
                    b = any ? BoundingBox.Merge(b, wb) : wb;
                    any = true;
                }
                MloModel.Bounds = b;
                GeometryVersion++;
            }
            return removed;
        }

        public void SetMloInfo_R1(MloImportResult info)
        {
            MloInfo = info;
            GeometryVersion++;
        }
    }
}

