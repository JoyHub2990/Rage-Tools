using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {

        private readonly Dictionary<uint, YmapFile> projectOverridesLast_R2 = new Dictionary<uint, YmapFile>();
        private readonly HashSet<uint> projectOverridesChanged_R2 = new HashSet<uint>();

        private void ForgetSupersededInstances_R2()
        {
            projectOverridesChanged_R2.Clear();
            foreach (var kv in projectOverrides)
                if (!projectOverridesLast_R2.TryGetValue(kv.Key, out var was) || !ReferenceEquals(was, kv.Value))
                    projectOverridesChanged_R2.Add(kv.Key);
            foreach (var kv in projectOverridesLast_R2)
                if (!projectOverrides.ContainsKey(kv.Key))
                    projectOverridesChanged_R2.Add(kv.Key);

            projectOverridesLast_R2.Clear();
            foreach (var kv in projectOverrides) projectOverridesLast_R2[kv.Key] = kv.Value;

            if (projectOverridesChanged_R2.Count == 0) return;
            int dropped = worldRender.ForgetYmapNames_R2(projectOverridesChanged_R2);
            if (dropped > 0)
                System.Console.WriteLine($"PROJECT overrides changed for {projectOverridesChanged_R2.Count} ymap name(s): {dropped} entity instance(s) rebuilt (the rest of the world keeps its geometry)");
        }

        private bool projectAssetsWere_R2;

        private bool ProjectAssetsChangeMatters_R2(Editor.LocalAssetIndex idx, int regs, Editor.CwProject project)
        {
            bool now = (idx?.FileCount ?? 0) > 0 || regs > 0 || (project?.YtypFiles?.Count ?? 0) > 0;
            bool matters = now || projectAssetsWere_R2;
            projectAssetsWere_R2 = now;
            return matters;
        }

        private void RebuildEntityNow_R2(YmapEntityDef e)
        {
            if (e == null || worldRender == null) return;
            worldRender.RebuildNow_R2(e, gameFiles, modelRenderer);
        }
    }
}

