using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        public int InstanceCountOf_R2(YmapEntityDef e)
        {
            if (e == null) return -1;
            return byEntity.TryGetValue(e, out var list) ? (list?.Count ?? 0) : 0;
        }

        private readonly List<YmapEntityDef> forgetScratch_R2 = new List<YmapEntityDef>();

        public int ForgetYmapNames_R2(HashSet<uint> shortNameHashes)
        {
            if (shortNameHashes == null || shortNameHashes.Count == 0) return 0;
            forgetScratch_R2.Clear();
            foreach (var kv in byEntity)
            {
                var y = kv.Key?.Ymap;
                var re = y?.RpfFileEntry;
                if (re == null) continue;
                if (shortNameHashes.Contains(re.ShortNameHash)) forgetScratch_R2.Add(kv.Key);
            }
            foreach (var e in forgetScratch_R2) Forget(e);
            return forgetScratch_R2.Count;
        }

        public bool RebuildNow_R2(YmapEntityDef e, GameFileManager game, Rendering.ModelRenderer builder)
        {
            if (e?.Archetype == null || game == null || builder == null) return false;
            if (byEntity.ContainsKey(e)) return true;
            if (!byArchetype.ContainsKey(e.Archetype.Hash)) return false;
            var list = BuildEntity(e, game, builder);
            if (list == null) return false;
            byEntity[e] = list;
            return true;
        }
    }
}

