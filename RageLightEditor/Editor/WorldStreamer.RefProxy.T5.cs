using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldStreamer
    {
        public const uint ArchFlagPreReflectedWaterProxy_T5 = 0x100000u;
        public const uint ArchFlagProxyForWaterReflections_T5 = 0x200000u;
        public const float ProxyChildDeadLodDist_T5 = 32.0f;

        public static readonly bool NoRefProxyFix_T5 =
            Environment.GetEnvironmentVariable("RLE_NOREFPROXYFIX") == "1";

        public static int RefProxiesSkipped_T5;

        private readonly Dictionary<uint, bool> refProxyByArch_T5 = new Dictionary<uint, bool>();

        public static bool IsReflectionProxyName_T5(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("refprox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("reflprox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("reflection_proxy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ArchetypeIsReflectionProxy_T5(Archetype arch)
        {
            if (arch == null) return false;
            if (refProxyByArch_T5.TryGetValue(arch.Hash, out bool known)) return known;
            bool p = IsReflectionProxyName_T5(arch.Name.ToString());
            refProxyByArch_T5[arch.Hash] = p;
            return p;
        }

        partial void ReflectionProxy_T5(YmapEntityDef ent, ref bool isreflproxy)
        {
            if (isreflproxy || NoRefProxyFix_T5 || RenderProxies) return;
            var arch = ent?.Archetype;
            if (arch == null) return;

            if (ArchetypeIsReflectionProxy_T5(arch)) { isreflproxy = true; RefProxiesSkipped_T5++; return; }

            if (ent._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_HD ||
                ent._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_ORPHANHD) return;
            if (ent._CEntityDef.numChildren != 1) return;
            var kids = ent.LodManagerChildren;
            if (kids == null || kids.Count != 1) return;
            var kid = kids.First?.Value;
            if (kid?.Archetype == null) return;
            if (kid.LodDist > ProxyChildDeadLodDist_T5) return;
            if (!IsReflectionProxyName_T5(kid.Archetype.Name.ToString())) return;
            isreflproxy = true;
            RefProxiesSkipped_T5++;
        }
    }
}

