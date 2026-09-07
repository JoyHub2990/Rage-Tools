using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        public List<RenderMesh> InstancesOf(YmapEntityDef e) => e != null && byEntity.TryGetValue(e, out var list) ? list : null;
    }
}

