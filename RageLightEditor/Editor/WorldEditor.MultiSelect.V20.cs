using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class WorldEditor
    {
        public readonly List<YmapEntityDef> Extra_V20 = new List<YmapEntityDef>();

        public int SelectedCount_V20 => (Selected != null ? 1 : 0) + Extra_V20.Count;

        public bool IsMulti_V20 => Extra_V20.Count > 0;

        public IEnumerable<YmapEntityDef> AllSelected_V20()
        {
            if (Selected != null) yield return Selected;
            foreach (var e in Extra_V20) if (e != null) yield return e;
        }

        public bool IsSelected_V20(YmapEntityDef e) =>
            e != null && (ReferenceEquals(e, Selected) || Extra_V20.Contains(e));

        public void Toggle_V20(YmapEntityDef e)
        {
            if (e == null) return;

            if (ReferenceEquals(e, Selected))
            {
                if (Extra_V20.Count > 0)
                {
                    var next = Extra_V20[0];
                    var rest = new List<YmapEntityDef>(Extra_V20);
                    rest.RemoveAt(0);
                    Select(next);
                    Extra_V20.AddRange(rest);
                }
                else Deselect();
                return;
            }

            if (Extra_V20.Remove(e)) return;

            if (Selected == null) { Select(e); return; }
            Extra_V20.Add(e);
        }

        public void ClearExtra_V20() => Extra_V20.Clear();

        public void PruneExtra_V20()
        {
            for (int i = Extra_V20.Count - 1; i >= 0; i--)
            {
                var e = Extra_V20[i];
                if (e == null || (e.Ymap == null && e.MloParent == null)) Extra_V20.RemoveAt(i);
            }
        }

        public static int MultiSelectSelfTest_V20(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var w = new WorldEditor();
            var a = new YmapEntityDef();
            var b = new YmapEntityDef();
            var c = new YmapEntityDef();

            w.Select(a);
            Chk("v20 multi: one click selects one thing", w.SelectedCount_V20 == 1 && !w.IsMulti_V20,
                w.SelectedCount_V20.ToString());

            w.Toggle_V20(b);
            w.Toggle_V20(c);
            Chk("v20 multi: ctrl+click adds to the selection",
                w.SelectedCount_V20 == 3 && w.IsMulti_V20 && w.IsSelected_V20(b) && w.IsSelected_V20(c),
                w.SelectedCount_V20 + " selected");
            Chk("v20 multi: ...with the first one still the primary, so the panel has something to show",
                ReferenceEquals(w.Selected, a), "primary is the first");

            w.Toggle_V20(b);
            Chk("v20 multi: ctrl+click again takes one out",
                w.SelectedCount_V20 == 2 && !w.IsSelected_V20(b), w.SelectedCount_V20 + " selected");

            w.Toggle_V20(a);
            Chk("v20 multi: ctrl+clicking the primary promotes another rather than blanking the panel",
                w.SelectedCount_V20 == 1 && ReferenceEquals(w.Selected, c) && !w.IsSelected_V20(a),
                w.Selected == null ? "nothing" : "promoted");

            w.Select(a);
            w.Toggle_V20(b);
            w.ClearExtra_V20();
            Chk("v20 multi: a plain click clears the extras",
                w.SelectedCount_V20 == 1 && ReferenceEquals(w.Selected, a), w.SelectedCount_V20.ToString());

            w.Toggle_V20(b);
            w.PruneExtra_V20();
            Chk("v20 multi: an entity whose ymap unloaded is dropped, not moved into a lost file",
                w.Extra_V20.Count == 0, w.Extra_V20.Count.ToString());

            w.Select(a); w.Toggle_V20(c);
            var order = w.AllSelected_V20().ToList();
            Chk("v20 multi: the gizmo walks the primary first, then the extras",
                order.Count == 2 && ReferenceEquals(order[0], a) && ReferenceEquals(order[1], c),
                order.Count + " in order");
            return fails;
        }
    }
}

