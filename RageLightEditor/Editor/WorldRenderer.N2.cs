using CodeWalker.GameFiles;
using RageLightEditor.Rendering;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        public readonly InteriorSunResolver InteriorSun_N2 = new InteriorSunResolver();
        public int SunSuppressed_N2 { get; private set; }

        private void StampSun_N2(RenderMesh inst, YmapEntityDef e)
        {
            if (inst == null || e == null) return;
            if (e.MloParent == null && e.MloInstance == null) return;
            float s = InteriorSun_N2.SunFor(e);
            inst.SunScale = s;
            if (s < 0.5f) SunSuppressed_N2++;
        }

        public void RestampSun_N2()
        {
            InteriorSun_N2.ClearCache();
            SunSuppressed_N2 = 0;
            foreach (var kv in ownerByMesh)
            {
                var e = kv.Value;
                if (e == null || (e.MloParent == null && e.MloInstance == null)) continue;
                StampSun_N2(kv.Key, e);
            }
        }
    }
}

