using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class ExtensionWorkspace_V68
    {
        private sealed class ExtSnapshot_V70
        {
            public string Name;
            public Archetype Archetype;
            public MetaWrapper[] Extensions;
            public int Selected;
        }

        private readonly List<ExtSnapshot_V70> undo_V70 = new List<ExtSnapshot_V70>();
        private readonly List<ExtSnapshot_V70> redo_V70 = new List<ExtSnapshot_V70>();

        public int UndoDepth_V70 => undo_V70.Count;
        public int RedoDepth_V70 => redo_V70.Count;
        public string NextUndoName_V70 => undo_V70.Count > 0 ? undo_V70[undo_V70.Count - 1].Name : null;
        public string NextRedoName_V70 => redo_V70.Count > 0 ? redo_V70[redo_V70.Count - 1].Name : null;

        private ExtSnapshot_V70 Capture_V70(string name)
        {
            var arch = CurrentArchetype;
            if (arch == null) return null;
            var list = ArchetypeExtensions_V62.Get(arch);
            var copy = new MetaWrapper[list.Length];
            for (int i = 0; i < list.Length; i++) copy[i] = ArchetypeExtensions_V62.Clone_V69(list[i]);
            return new ExtSnapshot_V70
            {
                Name = name,
                Archetype = arch,
                Extensions = copy,
                Selected = SelectedExtension,
            };
        }

        private static void Restore_V70(ExtSnapshot_V70 s)
        {
            if (s?.Archetype == null) return;
            var back = new MetaWrapper[s.Extensions.Length];
            for (int i = 0; i < s.Extensions.Length; i++) back[i] = ArchetypeExtensions_V62.Clone_V69(s.Extensions[i]);
            s.Archetype.Extensions = back;
        }

        public void PushUndo_V70(string name)
        {
            var snap = Capture_V70(name);
            if (snap == null) return;
            undo_V70.Add(snap);
            if (undo_V70.Count > 64) undo_V70.RemoveAt(0);
            redo_V70.Clear();
        }

        public bool Undo_V70()
        {
            if (undo_V70.Count == 0) { Say("nothing to undo"); return false; }
            var snap = undo_V70[undo_V70.Count - 1];
            undo_V70.RemoveAt(undo_V70.Count - 1);

            var forward = Capture_V70(snap.Name);
            if (forward != null) redo_V70.Add(forward);

            SelectArchetype_V70(snap.Archetype);
            Restore_V70(snap);
            SelectedExtension = Math.Min(snap.Selected, ArchetypeExtensions_V62.Get(snap.Archetype).Length - 1);
            SelectedPointField = null;
            CancelSnap();
            Say("undone: " + snap.Name);
            return true;
        }

        public bool Redo_V70()
        {
            if (redo_V70.Count == 0) { Say("nothing to redo"); return false; }
            var snap = redo_V70[redo_V70.Count - 1];
            redo_V70.RemoveAt(redo_V70.Count - 1);

            var backward = Capture_V70(snap.Name);
            if (backward != null) undo_V70.Add(backward);

            SelectArchetype_V70(snap.Archetype);
            Restore_V70(snap);
            SelectedExtension = Math.Min(snap.Selected, ArchetypeExtensions_V62.Get(snap.Archetype).Length - 1);
            SelectedPointField = null;
            CancelSnap();
            Say("redone: " + snap.Name);
            return true;
        }

        private void SelectArchetype_V70(Archetype a)
        {
            if (a == null || ReferenceEquals(CurrentArchetype, a)) return;
            var t = Targets.FirstOrDefault(x => ReferenceEquals(x.Archetype, a));
            if (t != null) Selected = Targets.IndexOf(t);
        }

        public int PickExtensionAt_V70(Func<SharpDX.Vector3, (bool onScreen, float x, float y)> project,
                                       float mx, float my, float radiusPx)
        {
            var t = Current;
            var arch = t?.Archetype;
            if (arch == null || project == null) return -1;
            var list = ArchetypeExtensions_V62.Get(arch);
            int best = -1;
            float bestD = radiusPx;
            for (int i = 0; i < list.Length; i++)
            {
                foreach (var f in PointFields_V68(list[i]))
                {
                    if (!(ArchetypeExtensions_V62.GetValue(list[i], f) is SharpDX.Vector3 local)) continue;
                    if (f.Prop.Name == "direction" || f.Prop.Name == "normal") continue;
                    var world = t.Placement + SharpDX.Vector3.Transform(local, t.Orientation);
                    var (on, sx, sy) = project(world);
                    if (!on) continue;
                    float d = (float)Math.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        public static int SelfTestHistory_V70(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var ws = new ExtensionWorkspace_V68();
            var t = ws.NewArchetypeFor("rle_v70_hist", new SharpDX.Vector3(-1, -1, 0), new SharpDX.Vector3(1, 1, 2));
            var shaftType = ArchetypeExtensions_V62.Types.FirstOrDefault(x => x.Wrapper == "MCExtensionDefLightShaft");
            if (t == null || shaftType == null) { Chk("v70 undo: set up", false, "no type"); return fails; }

            ws.PushUndo_V70("Add light shaft");
            ArchetypeExtensions_V62.Add(t.Archetype, ArchetypeExtensions_V62.Create(shaftType, "v70_hist_shaft"));
            ws.SelectedExtension = 0;
            Chk("v70 undo: an extension was added", ArchetypeExtensions_V62.Get(t.Archetype).Length == 1, "1");

            ws.Undo_V70();
            Chk("v70 undo: Ctrl+Z takes the extension back off",
                ArchetypeExtensions_V62.Get(t.Archetype).Length == 0,
                ArchetypeExtensions_V62.Get(t.Archetype).Length + " left");

            ws.Redo_V70();
            Chk("v70 undo: Ctrl+Y puts it back",
                ArchetypeExtensions_V62.Get(t.Archetype).Length == 1,
                ArchetypeExtensions_V62.Get(t.Archetype).Length + " back");

            var shaft = ArchetypeExtensions_V62.Get(t.Archetype)[0];
            var lenF = ArchetypeExtensions_V62.Fields(shaft).First(f => f.Prop.Name == "length");
            ArchetypeExtensions_V62.SetValue(shaft, lenF, 3.0f);
            ws.PushUndo_V70("Edit length");
            ArchetypeExtensions_V62.SetValue(ArchetypeExtensions_V62.Get(t.Archetype)[0], lenF, 44.0f);
            ws.Undo_V70();
            var after = (float)ArchetypeExtensions_V62.GetValue(ArchetypeExtensions_V62.Get(t.Archetype)[0], lenF);
            Chk("v70 undo: a value edit comes back to what it was", Math.Abs(after - 3.0f) < 0.001f, after.ToString());

            ws.Redo_V70();
            var again = (float)ArchetypeExtensions_V62.GetValue(ArchetypeExtensions_V62.Get(t.Archetype)[0], lenF);
            Chk("v70 undo: ...and redo puts the edit back", Math.Abs(again - 44.0f) < 0.001f, again.ToString());

            while (ws.Undo_V70()) { }
            Chk("v70 undo: undoing past the start stops instead of throwing",
                ws.UndoDepth_V70 == 0, "history empty");
            return fails;
        }
    }
}
