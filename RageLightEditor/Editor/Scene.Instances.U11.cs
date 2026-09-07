using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class Scene
    {
        private List<int> ValidSelection_U11() =>
            SelectedIndices.Where(i => i >= 0 && i < Lights.Count).Distinct().ToList();

        public bool SelectionHasInstance_U11()
        {
            foreach (var i in ValidSelection_U11())
                if (InstanceGroup(Lights[i]) != 0) return true;
            return false;
        }

        public int LinkSelectedAsInstance()
        {
            var sel = ValidSelection_U11();
            if (sel.Count < 2) return 0;
            PushUndo();
            var primary = SelectedLight != null && sel.Contains(Lights.IndexOf(SelectedLight)) ? SelectedLight : Lights[sel[0]];
            var master = primary;
            if (InstanceGroup(master) == 0)
                foreach (var i in sel)
                    if (InstanceGroup(Lights[i]) != 0) { master = Lights[i]; break; }
            int g = InstanceGroup(master);
            if (g == 0)
            {
                g = nextInstanceGroup++;
                instanceGroupOf[master] = g;
            }
            int linked = 0;
            foreach (var i in sel)
            {
                var l = Lights[i];
                if (ReferenceEquals(l, master)) continue;
                instanceGroupOf[l] = g;
                CopyParamsOnly(master, l);
                var f = OwnerFile(l);
                if (f != null) f.Dirty = true;
                linked++;
            }
            var mf = OwnerFile(master);
            if (mf != null) mf.Dirty = true;
            Dirty = true;
            return linked;
        }

        public int UnlinkSelected()
        {
            var sel = ValidSelection_U11();
            if (sel.Count == 0) return 0;
            PushUndo();
            int n = 0;
            foreach (var i in sel)
                if (instanceGroupOf.Remove(Lights[i])) n++;
            if (n > 0) Dirty = true;
            return n;
        }
    }
}
