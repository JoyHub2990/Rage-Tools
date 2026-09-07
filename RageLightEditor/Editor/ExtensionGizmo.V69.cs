using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public sealed class ExtensionPointTarget_V69 : IWorldGizmoTarget
    {
        public readonly MetaWrapper Wrapper;
        public readonly ArchetypeExtensions_V62.Field Field;
        private readonly Vector3 origin;
        private readonly Quaternion basis;

        public ExtensionPointTarget_V69(MetaWrapper w, ArchetypeExtensions_V62.Field f,
                                        Vector3 origin, Quaternion basis)
        {
            Wrapper = w; Field = f; this.origin = origin; this.basis = basis;
        }

        public object Key => (Wrapper, Field.Prop.Name);

        public Vector3 Local =>
            ArchetypeExtensions_V62.GetValue(Wrapper, Field) is Vector3 v ? v : Vector3.Zero;

        public Vector3 Position => origin + Vector3.Transform(Local, basis);
        public Quaternion Orientation => Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.XYZ;
        public bool ScaleLockXY => true;
        public bool CanScale => false;

        public void SetPosition(Vector3 p)
        {
            var inv = basis; inv.Conjugate();
            ArchetypeExtensions_V62.SetValue(Wrapper, Field, Vector3.Transform(p - origin, inv));
        }

        public void SetOrientation(Quaternion q) { }
        public void SetScale(Vector3 s) { }
    }

    public sealed class ExtensionWholeTarget_V69 : IWorldGizmoTarget
    {
        public readonly MetaWrapper Wrapper;
        private readonly List<ArchetypeExtensions_V62.Field> fields;
        private readonly Vector3 origin;
        private readonly Quaternion basis;

        public ExtensionWholeTarget_V69(MetaWrapper w, List<ArchetypeExtensions_V62.Field> pointFields,
                                        Vector3 origin, Quaternion basis)
        {
            Wrapper = w; fields = pointFields; this.origin = origin; this.basis = basis;
        }

        public object Key => Wrapper;

        private Vector3 LocalCentre()
        {
            var sum = Vector3.Zero;
            int n = 0;
            foreach (var f in fields)
            {
                if (!(ArchetypeExtensions_V62.GetValue(Wrapper, f) is Vector3 v)) continue;
                sum += v; n++;
            }
            return n > 0 ? sum / n : Vector3.Zero;
        }

        public Vector3 Position => origin + Vector3.Transform(LocalCentre(), basis);
        public Quaternion Orientation => Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.XYZ;
        public bool ScaleLockXY => true;
        public bool CanScale => false;

        public void SetPosition(Vector3 p)
        {
            var inv = basis; inv.Conjugate();
            var wantLocal = Vector3.Transform(p - origin, inv);
            var delta = wantLocal - LocalCentre();
            if (delta.LengthSquared() < 1e-12f) return;
            foreach (var f in fields)
            {
                if (!(ArchetypeExtensions_V62.GetValue(Wrapper, f) is Vector3 v)) continue;
                ArchetypeExtensions_V62.SetValue(Wrapper, f, v + delta);
            }
        }

        public void SetOrientation(Quaternion q) { }
        public void SetScale(Vector3 s) { }
    }

    public partial class ExtensionWorkspace_V68
    {
        public string SelectedPointField;

        public static List<ArchetypeExtensions_V62.Field> MovableFields_V69(MetaWrapper w) =>
            PointFields_V68(w)
                .Where(f => f.Prop.Name != "direction" && f.Prop.Name != "normal")
                .ToList();

        public IWorldGizmoTarget GizmoTarget_V69()
        {
            var t = Current;
            var arch = t?.Archetype;
            if (arch == null) return null;
            var list = ArchetypeExtensions_V62.Get(arch);
            if (SelectedExtension < 0 || SelectedExtension >= list.Length) return null;
            var w = list[SelectedExtension];
            var fields = MovableFields_V69(w);
            if (fields.Count == 0) return null;

            if (!string.IsNullOrEmpty(SelectedPointField))
            {
                var f = fields.FirstOrDefault(x => x.Prop.Name == SelectedPointField);
                if (f != null) return new ExtensionPointTarget_V69(w, f, t.Placement, t.Orientation);
            }
            return new ExtensionWholeTarget_V69(w, fields, t.Placement, t.Orientation);
        }

        public MetaWrapper DuplicateExtension_V69(Archetype arch, int index)
        {
            if (arch == null) return null;
            var list = ArchetypeExtensions_V62.Get(arch);
            if (index < 0 || index >= list.Length) return null;
            var copy = ArchetypeExtensions_V62.Clone_V69(list[index]);
            if (copy == null) return null;
            ArchetypeExtensions_V62.Add(arch, copy);
            SelectedExtension = ArchetypeExtensions_V62.Get(arch).Length - 1;
            SelectedPointField = null;
            CancelSnap();
            Say("copied - drag it with the gizmo, or snap its corners");
            return copy;
        }

        public static int SelfTestGizmo_V69(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var ws = new ExtensionWorkspace_V68();
            var t = ws.NewArchetypeFor("rle_v69_giz", new Vector3(-1, -1, 0), new Vector3(1, 1, 2));
            var shaftType = ArchetypeExtensions_V62.Types.FirstOrDefault(x => x.Wrapper == "MCExtensionDefLightShaft");
            if (t == null || shaftType == null) { Chk("v69 gizmo: set up", false, "no shaft type"); return fails; }

            var shaft = ArchetypeExtensions_V62.Create(shaftType, "v69_giz_shaft");
            ArchetypeExtensions_V62.Add(t.Archetype, shaft);
            ws.SelectedExtension = 0;

            var corners = CornerFields_V68(shaft);
            for (int i = 0; i < corners.Count; i++)
                ArchetypeExtensions_V62.SetValue(shaft, corners[i], new Vector3(i, 0, 1));

            var whole = ws.GizmoTarget_V69();
            Chk("v69 gizmo: with no point picked the gizmo moves the whole extension",
                whole is ExtensionWholeTarget_V69, whole?.GetType().Name ?? "none");

            var before = corners.Select(c => (Vector3)ArchetypeExtensions_V62.GetValue(shaft, c)).ToArray();
            whole.SetPosition(whole.Position + new Vector3(0, 0, 3));
            var after = corners.Select(c => (Vector3)ArchetypeExtensions_V62.GetValue(shaft, c)).ToArray();
            bool movedTogether = true;
            for (int i = 0; i < before.Length; i++)
                if ((after[i] - (before[i] + new Vector3(0, 0, 3))).Length() > 0.001f) movedTogether = false;
            Chk("v69 gizmo: ...and every corner moves by the same amount",
                movedTogether, movedTogether ? "all four kept their shape" : "the shape was distorted");

            ws.SelectedPointField = "cornerA";
            var one = ws.GizmoTarget_V69();
            Chk("v69 gizmo: picking one corner puts the gizmo on that corner alone",
                one is ExtensionPointTarget_V69, one?.GetType().Name ?? "none");
            var otherBefore = (Vector3)ArchetypeExtensions_V62.GetValue(shaft, corners[1]);
            one.SetPosition(one.Position + new Vector3(0, 0, 5));
            var aAfter = (Vector3)ArchetypeExtensions_V62.GetValue(shaft, corners[0]);
            var otherAfter = (Vector3)ArchetypeExtensions_V62.GetValue(shaft, corners[1]);
            Chk("v69 gizmo: ...and only that corner moves",
                Math.Abs(aAfter.Z - (after[0].Z + 5)) < 0.001f && (otherAfter - otherBefore).Length() < 0.001f,
                $"cornerA {aAfter}, cornerB unmoved {(otherAfter - otherBefore).Length() < 0.001f}");

            var dup = ws.DuplicateExtension_V69(t.Archetype, 0);
            var all = ArchetypeExtensions_V62.Get(t.Archetype);
            Chk("v69 duplicate: the copy joins the archetype and is selected",
                dup != null && all.Length == 2 && ws.SelectedExtension == 1, all.Length + " extensions");

            var dupCorners = CornerFields_V68(dup);
            var srcA = (Vector3)ArchetypeExtensions_V62.GetValue(all[0], corners[0]);
            var dupA = (Vector3)ArchetypeExtensions_V62.GetValue(dup, dupCorners[0]);
            Chk("v69 duplicate: it starts exactly where the original is",
                (dupA - srcA).Length() < 0.0001f, dupA.ToString());

            ArchetypeExtensions_V62.SetValue(dup, dupCorners[0], new Vector3(99, 99, 99));
            var srcAfter = (Vector3)ArchetypeExtensions_V62.GetValue(all[0], corners[0]);
            Chk("v69 duplicate: moving the copy leaves the original where it was",
                (srcAfter - srcA).Length() < 0.0001f, srcAfter.ToString());
            return fails;
        }
    }
}
