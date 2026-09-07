using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class NavMeshEditor
    {

        public enum Shelf { Type = 0, More = 1, Advanced = 2 }

        public sealed class NamedFlag
        {
            public readonly int Bit;
            public readonly string Name;
            public readonly string Tip;
            public readonly Shelf Where;
            public NamedFlag(int bit, Shelf where, string name, string tip)
            { Bit = bit; Where = where; Name = name; Tip = tip; }
            public bool Get(YnvPoly p) => p != null && Flags[Bit].Get(p);
        }

        private static NamedFlag[] namedCache;

        public static NamedFlag[] Named => namedCache ??= BuildNamed();

        private static NamedFlag[] BuildNamed()
        {
            var list = new List<NamedFlag>();
            void N(int bit, Shelf s, string name, string tip) => list.Add(new NamedFlag(bit, s, name, tip));

            N(2,  Shelf.Type, "Pavement / footpath", "Peds walking down a street stay on these.");
            N(18, Shelf.Type, "Road",                "Carriageway. Peds cross it rather than walk along it.");
            N(7,  Shelf.Type, "Deep water (swim)",   "Peds swim here instead of walking.");
            N(21, Shelf.Type, "Shallow water (wade)","Wadeable - a river edge, a flooded floor.");
            N(6,  Shelf.Type, "Too steep to walk",   "Peds refuse it. What a wall or a cliff face carries.");
            N(3,  Shelf.Type, "Underground",         "Under something - tunnels, subways, car parks.");
            N(14, Shelf.Type, "Inside a building",   "Interior space, what a floor inside an MLO carries.");
            N(20, Shelf.Type, "Train track",         "Rail. Peds keep off it.");
            N(17, Shelf.More, "Flat ground (peds stand and wander here)",
                              "Flat enough to stand on and mill about on, rather than only cross.");
            N(13, Shelf.More, "A vehicle path node crosses it",
                              "There is a road path node over this polygon - crossings and traffic use it.");
            N(19, Shelf.More, "On the edge of the 150 m cell",
                              "The polygon touches the cell border, where it stitches to the next .ynv.");
            N(24, Shelf.More, "Mall / plaza footpath",
                              "The wide paved areas - Vinewood Blvd, the mall.");

            var taken = new HashSet<int>(list.Select(n => n.Bit));
            for (int i = 0; i < Flags.Length; i++)
                if (!taken.Contains(i)) list.Add(new NamedFlag(i, Shelf.Advanced, Flags[i].Name, Flags[i].Tip));
            return list.ToArray();
        }

        public static IEnumerable<NamedFlag> OnShelf(Shelf s)
        {
            foreach (var n in Named) if (n.Where == s) yield return n;
        }

        private static readonly (NavCat cat, int bit)[] TypeBits =
        {
            (NavCat.Pavement, 2), (NavCat.Underground, 3), (NavCat.Steep, 6), (NavCat.Water, 7),
            (NavCat.Interior, 14), (NavCat.Road, 18), (NavCat.Train, 20), (NavCat.Shallow, 21),
        };

        public static readonly NavCat[] TypeChoices =
        {
            NavCat.Walk, NavCat.Pavement, NavCat.Road, NavCat.Water, NavCat.Shallow,
            NavCat.Steep, NavCat.Underground, NavCat.Interior, NavCat.Train,
        };

        private readonly struct FlagBits : IEquatable<FlagBits>
        {
            public readonly ushort F0; public readonly uint F1, F2;
            public FlagBits(YnvPoly p)
            { F0 = p._RawData.PolyFlags0; F1 = p._RawData.PolyFlags1; F2 = p._RawData.PolyFlags2; }
            public void RestoreTo(YnvPoly p)
            { p._RawData.PolyFlags0 = F0; p._RawData.PolyFlags1 = F1; p._RawData.PolyFlags2 = F2; }
            public bool Equals(FlagBits o) => F0 == o.F0 && F1 == o.F1 && F2 == o.F2;
        }

        public int SetType(EditHistory hist, IList<YnvPoly> polys, NavCat cat)
        {
            if (polys == null || cat == NavCat.Isolated) return 0;
            var list = polys.Where(p => p != null).Distinct().ToArray();
            if (list.Length == 0) return 0;

            var before = list.Select(p => new FlagBits(p)).ToArray();
            foreach (var p in list) ApplyType(p, cat);
            var after = list.Select(p => new FlagBits(p)).ToArray();

            int changed = 0;
            for (int i = 0; i < list.Length; i++) if (!before[i].Equals(after[i])) changed++;
            if (changed == 0) return 0;

            var docs = list.Select(p => DocOf(p.Ynv)).Where(d => d != null).Distinct().ToArray();
            void Set(FlagBits[] to)
            {
                for (int i = 0; i < list.Length; i++) to[i].RestoreTo(list[i]);
                foreach (var d in docs) { d.Version++; d.Dirty = true; }
            }
            Set(after);
            hist?.Push(new DelegateCommand(
                $"Make {changed} polygon{(changed == 1 ? "" : "s")} {CatNames[(int)cat]}",
                () => Set(after), () => Set(before)));
            Status = $"{changed} polygon{(changed == 1 ? "" : "s")} set to {CatNames[(int)cat]}";
            return changed;
        }

        private static void ApplyType(YnvPoly p, NavCat cat)
        {
            foreach (var (c, bit) in TypeBits) Flags[bit].Set(p, c == cat);
            p.B17_IsFlatGround = cat != NavCat.Steep && cat != NavCat.Water;
        }

        public bool DrawOnTop = System.Environment.GetEnvironmentVariable("RLE_NAVONTOP") == "1";

        public string ClickHint()
        {
            if (PlacingPoly)
                return PendingPoly.Count < 3
                    ? $"Drawing a polygon: click the ground for each corner  ({PendingPoly.Count} placed)  -  Backspace takes one back, Esc gives up"
                    : $"Drawing a polygon: {PendingPoly.Count} corners placed  -  Enter closes it, Backspace takes one back, Esc gives up";
            if (SelectedPoint != null)
                return $"Point {SelectedPoint.Index} selected: drag the gizmo to move it, Del deletes it  -  right-click a polygon to go back to polygons";
            if (SelectedPortal != null)
                return $"Portal {SelectedPortal.Index} selected: drag the gizmo to move it, Del deletes it  -  right-click a polygon to go back to polygons";
            if (SelectedPolys.Count > 1)
                return $"{SelectedPolys.Count} polygons selected: the gizmo moves them all, Del deletes them, F frames them  -  clicks only ever hit the nav mesh, never the world";
            if (SelectedPolys.Count == 1)
                return "Polygon selected: the gizmo moves it (pick a corner on the right to move one corner), Del deletes it, P draws a new one";
            return "RIGHT-click a polygon to select it, Ctrl+right-click adds one, P draws a new one  -  the left button flies the camera; clicks only ever hit the nav mesh: no prop, light or scenario point can be selected here";
        }

        public static int SelfTestQ2()
        {
            int fails = 0;
            void Check(string what, bool ok, string detail = "")
            {
                if (!ok) { fails++; Console.WriteLine($"  NAVMESH FAIL: {what}   {detail}"); }
                else Console.WriteLine($"  navmesh ok: {what}");
            }

            var bits = Named.Select(n => n.Bit).ToArray();
            Check("every poly flag is on exactly one shelf",
                  bits.Length == Flags.Length && bits.Distinct().Count() == Flags.Length,
                  $"{bits.Length} entries for {Flags.Length} bits");

            var front = Named.Where(n => n.Where != Shelf.Advanced).ToArray();
            var vague = front.Where(n => n.Name.IndexOf("unk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         n.Name.IndexOf("unused", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            Check("no 'unknown' or 'unused' bit is on the front shelves", vague.Length == 0,
                  vague.Length == 0 ? "" : string.Join(", ", vague.Select(v => v.Name)));
            var numbered = front.Where(n => n.Name.Length > 1 && char.IsDigit(n.Name[0])).ToArray();
            Check("and none of them is named by its bit number", numbered.Length == 0,
                  numbered.Length == 0 ? "" : string.Join(", ", numbered.Select(v => v.Name)));
            Check("twelve bits are named in plain English", front.Length == 12, $"{front.Length}");

            var cats = TypeChoices.ToHashSet();
            var missing = Enum.GetValues(typeof(NavCat)).Cast<NavCat>()
                              .Where(c => c != NavCat.Isolated && !cats.Contains(c)).ToArray();
            Check("every legend colour except the fault one has a type button", missing.Length == 0,
                  missing.Length == 0 ? "" : string.Join(", ", missing));

            var ed = new NavMeshEditor();
            ed.HighlightIsolated = false;
            var hist = new EditHistory();
            var ynv = NewFile(50, 20, new SharpDX.Vector3(0, 0, -20), new SharpDX.Vector3(150, 150, 130));
            var doc = ed.Add(ynv, null, "q2-self-test");
            var poly = ed.AddPoly(hist, doc, new[]
            {
                new SharpDX.Vector3(1, 1, 0), new SharpDX.Vector3(3, 1, 0), new SharpDX.Vector3(3, 3, 0)
            }, null);
            Check("a new polygon starts as pavement", ed.CategoryOf(poly) == NavCat.Pavement, ed.CategoryOf(poly).ToString());
            ed.SetType(hist, new[] { poly }, NavCat.Water);
            Check("one click makes it water", poly.B07_IsWater && ed.CategoryOf(poly) == NavCat.Water, ed.CategoryOf(poly).ToString());
            Check("and the type it was is cleared", !poly.B02_IsFootpath, "still footpath");
            ed.SetType(hist, new[] { poly }, NavCat.Interior);
            Check("changing type again clears the water bit", !poly.B07_IsWater && poly.B14_IsInterior, "");
            hist.Undo();
            Check("undo takes it back to water", poly.B07_IsWater && !poly.B14_IsInterior, ed.CategoryOf(poly).ToString());
            hist.Redo();
            Check("redo makes it interior again", poly.B14_IsInterior, "");
            Check("setting the same type twice is not an undo step", ed.SetType(hist, new[] { poly }, NavCat.Interior) == 0, "");
            ed.SetType(hist, new[] { poly }, NavCat.Walk);
            Check("'Walkable' is the absence of every type bit",
                  ed.CategoryOf(poly) == NavCat.Walk && !poly.B14_IsInterior && !poly.B02_IsFootpath && poly.B17_IsFlatGround,
                  ed.CategoryOf(poly).ToString());

            var hint = ed.ClickHint();
            Check("the hint bar is one line", !string.IsNullOrWhiteSpace(hint) && !hint.Contains('\n'), hint);
            ed.ClearSelection();
            Check("with nothing selected it promises the world is not selectable",
                  ed.ClickHint().IndexOf("nav mesh", StringComparison.OrdinalIgnoreCase) >= 0, ed.ClickHint());
            ed.PlacingPoly = true;
            Check("and it changes with the tool", ed.ClickHint() != hint, ed.ClickHint());
            ed.PlacingPoly = false;

            return fails;
        }
    }
}

