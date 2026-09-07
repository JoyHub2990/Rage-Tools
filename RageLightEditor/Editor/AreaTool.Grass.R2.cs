using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class AreaToolState
    {
        public bool IncludeGrass_R2 = true;

        public struct GrassHit_R2
        {
            public YmapGrassInstanceBatch Batch;
            public string Ymap;
            public string Archetype;
            public int Inside;
            public int Total;
        }

        public readonly List<GrassHit_R2> GrassInside_R2 = new List<GrassHit_R2>();
        public int GrassInstancesInside_R2;
        public int GrassBatchesEmptied_R2;

        public string DeleteLabel_R2 =>
            IncludeGrass_R2 && GrassInstancesInside_R2 > 0
                ? $"Delete inside ({Contents.Count} + {GrassInstancesInside_R2} grass)"
                : $"Delete inside ({Contents.Count})";

        public bool CanDelete_R2 => Contents.Count > 0 || (IncludeGrass_R2 && GrassInstancesInside_R2 > 0);
    }
}

