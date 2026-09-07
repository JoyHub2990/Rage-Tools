using CodeWalker.GameFiles;

namespace RageLightEditor.Rendering
{
    public partial class GrassRenderer
    {
        public void Forget_R2(YmapGrassInstanceBatch batch)
        {
            if (batch == null) return;
            if (!gpu.TryGetValue(batch, out var g)) return;
            g.Dispose();
            gpu.Remove(batch);
        }
    }
}

