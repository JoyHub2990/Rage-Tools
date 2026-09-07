using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldEditor
    {
        public bool ForgetDirty(YmapFile y) => y != null && dirty.Remove(y);
    }
}

