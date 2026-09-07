using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private sealed class MloSpot_U5
        {
            public YmapEntityDef Owner;
            public CEntityDef Def;
            public Archetype Arch;
            public int Room, Portal, EntSet;
            public YmapEntityDef Live;
        }

        private sealed class YmapSpot_U5
        {
            public YmapFile Ymap;
            public EntitySnapshot Snap;
            public YmapEntityDef Live;
        }

        internal static bool IsMloChild_U5(YmapEntityDef e) => e != null && e.Ymap == null && e.MloParent != null;

        private MloSpot_U5 CaptureMloChild_U5(YmapEntityDef e)
        {
            var owner = e?.MloParent;
            var arch = owner?.Archetype as MloArchetype;
            if (arch == null) return null;

            int room = 0, portal = -1, entset = -1;
            var mc = owner.MloInstance?.TryGetArchetypeEntity(e);
            if (mc != null)
            {
                room = arch.GetEntityRoom(mc)?.Index ?? -1;
                portal = arch.GetEntityPortal(mc)?.Index ?? -1;
                entset = arch.GetEntitySet(mc)?.Index ?? -1;
                if (room < 0 && portal < 0 && entset < 0) room = 0;
            }
            return new MloSpot_U5
            {
                Owner = owner,
                Def = e._CEntityDef,
                Arch = e.Archetype,
                Room = room,
                Portal = portal,
                EntSet = entset,
                Live = e,
            };
        }

        private bool RemoveMloChild_U5(YmapEntityDef e)
        {
            var owner = e?.MloParent;
            var arch = owner?.Archetype as MloArchetype;
            if (arch == null) return false;
            bool ok = false;
            try { ok = arch.RemoveEntity(e); } catch { }
            try { owner.MloInstance?.DeleteEntity(e); } catch { }
            WorldEntityChanged(owner);
            return ok;
        }

        private YmapEntityDef RestoreMloChild_U5(MloSpot_U5 s)
        {
            var arch = s?.Owner?.Archetype as MloArchetype;
            var inst = s?.Owner?.MloInstance;
            if (arch == null || inst == null) return null;
            try
            {
                var def = s.Def;
                var ment = new MCEntityDef(ref def, arch);
                var back = new YmapEntityDef(s.Owner, ment, arch.entities?.Length ?? 0);
                arch.AddEntity(back, s.Room, s.Portal, s.EntSet);
                inst.AddEntity(back);
                back.SetArchetype(s.Arch ?? gameFiles?.Cache?.GetArchetype(def.archetypeName));
                inst.UpdateEntity(back);
                WorldEntityChanged(s.Owner);
                return back;
            }
            catch (Exception ex) { WorldEdit.LastStatus = "could not put it back: " + ex.Message; return null; }
        }

        partial void WorldDeleteSelected_U5(ref bool handled)
        {
            var picked = WorldEdit.AllSelected_V20().Where(x => x != null).ToList();
            if (picked.Count == 0) return;
            if (picked.Count < 2 && !IsMloChild_U5(picked[0])) return;

            var inYmaps = new List<YmapSpot_U5>();
            var inMlos = new List<MloSpot_U5>();
            foreach (var e in picked)
            {
                if (IsMloChild_U5(e))
                {
                    var m = CaptureMloChild_U5(e);
                    if (m != null) inMlos.Add(m);
                }
                else if (e.Ymap != null)
                {
                    inYmaps.Add(new YmapSpot_U5 { Ymap = e.Ymap, Snap = EntityOps.Capture(e), Live = e });
                }
            }
            if (inYmaps.Count == 0 && inMlos.Count == 0) return;

            foreach (var y in inYmaps)
            {
                WorldEdit.MarkDirty(y.Live);
                ProjectAutoAddYmap(y.Ymap, null);
            }

            void Kill()
            {
                foreach (var y in inYmaps)
                {
                    if (y.Live == null) continue;
                    EntityOps.Remove(y.Ymap, y.Live);
                    WorldEntityChanged(y.Live);
                    WorldEdit.MarkDirty(y.Ymap);
                }
                foreach (var m in inMlos)
                {
                    if (m.Live == null) continue;
                    RemoveMloChild_U5(m.Live);
                }
                WorldEdit.Deselect();
            }

            void Bring()
            {
                foreach (var y in inYmaps)
                {
                    y.Live = EntityOps.Restore(y.Snap, y.Ymap, gameFiles?.Cache);
                    if (y.Live == null || !EntityOps.Add(y.Ymap, y.Live, gameFiles?.Cache)) continue;
                    WorldEntityChanged(y.Live);
                    WorldEdit.MarkDirty(y.Ymap);
                }
                foreach (var m in inMlos) m.Live = RestoreMloChild_U5(m);
                var back = inYmaps.Select(y => y.Live).Concat(inMlos.Select(m => m.Live))
                                  .Where(x => x != null).ToList();
                if (back.Count > 0) SelectClones_V65(back);
            }

            int n = inYmaps.Count + inMlos.Count;
            string name = n == 1
                ? (picked[0].Archetype?.Name ?? picked[0]._CEntityDef.archetypeName.ToString())
                : n + " props";

            Kill();
            WorldEdit.LastStatus = "deleted " + name;
            WorldHistory.Push(new DelegateCommand("Delete " + name, doIt: Kill, undoIt: Bring));
            handled = true;
        }

        partial void WorldPasteClipboard_U5(ref bool handled)
        {
            if (EntityOps.Clipboard.IsEmpty) return;
            if (!IsMloChild_U5(WorldEdit.Selected)) return;
            WorldEdit.LastStatus = "an interior prop is selected - pasting goes into a ymap, " +
                                   "so select a world prop first, or Shift+drag to copy inside the interior";
            handled = true;
        }

        internal bool ForcePickAdd_U5;

        private bool PickAddsToSelection_U5() =>
            ForcePickAdd_U5 || (System.Windows.Forms.Control.ModifierKeys &
                                (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift)) != 0;

        private void PressDeleteKey_U5()
        {
            bool was = ignoreImGuiKeyboard;
            ignoreImGuiKeyboard = true;
            try { OnKeyDownEv(this, new System.Windows.Forms.KeyEventArgs(settings.GetBind("Delete"))); }
            finally { ignoreImGuiKeyboard = was; }
        }

        private static string Count_U5(YmapEntityDef e)
        {
            if (e == null) return "-";
            if (IsMloChild_U5(e))
            {
                var arch = e.MloParent?.Archetype as MloArchetype;
                return (arch?.entities?.Length ?? -1) + " in the ytyp / " +
                       (e.MloParent?.MloInstance?.Entities?.Length ?? -1) + " placed";
            }
            return (e.Ymap?.AllEntities?.Length ?? -1) + " in " + (e.Ymap?.Name ?? "?");
        }

        partial void AfterWorldAutoSelect_Del_U5()
        {
            if (Environment.GetEnvironmentVariable("RLE_AUTODEL") != "1") return;

            YmapEntityDef mloOwner = null;
            int mloSeen = 0;
            foreach (var y in World.ResidentYmaps)
            {
                var all = y?.AllEntities;
                if (all == null) continue;
                foreach (var e in all)
                {
                    if (e?.MloInstance == null) continue;
                    mloSeen++;
                    if (e.MloInstance.Entities != null && e.MloInstance.Entities.Length >= 3 && mloOwner == null) mloOwner = e;
                }
            }
            Console.WriteLine("AUTODEL interiors loaded: " + mloSeen + (mloOwner != null ? ", using " + (mloOwner.Archetype?.Name ?? "?") : ""));

            var group = new List<YmapEntityDef>();
            string what;
            if (mloOwner != null)
            {
                what = "interior props in " + (mloOwner.Archetype?.Name ?? "an MLO");
                for (int i = 0; i < mloOwner.MloInstance.Entities.Length && group.Count < 3; i++)
                {
                    var c = mloOwner.MloInstance.Entities[i];
                    if (c != null && IsMloChild_U5(c)) group.Add(c);
                }
            }
            else
            {
                what = "world props";
                foreach (var e in World.Visible)
                {
                    if (e?.Ymap == null || e.MloInstance != null || e.MloParent != null) continue;
                    group.Add(e);
                    if (group.Count >= 3) break;
                }
            }

            if (group.Count == 0) { Console.WriteLine("AUTODEL: nothing to work with"); return; }

            var ymap0 = group[0].Ymap;
            var arch0 = group[0].MloParent?.Archetype as MloArchetype;
            var inst0 = group[0].MloParent?.MloInstance;
            string Where() => arch0 != null
                ? (arch0.entities?.Length ?? -1) + " in the ytyp / " + (inst0?.Entities?.Length ?? -1) + " placed"
                : (ymap0?.AllEntities?.Length ?? -1) + " in " + (ymap0?.Name ?? "?");

            WorldEdit.Select(group[0]);
            for (int i = 1; i < group.Count; i++) WorldEdit.Toggle_V20(group[i]);
            Console.WriteLine($"AUTODEL picked {group.Count} {what}: selected={WorldEdit.SelectedCount_V20} " +
                              $"mloChildren={group.Count(IsMloChild_U5)} before {Where()}");

            int copied = 0;
            try { copied = EntityOps.Clipboard.Copy(WorldSelectedEntities()); } catch { }
            Console.WriteLine($"AUTODEL copy took {copied} of {WorldEdit.SelectedCount_V20} selected");

            PressDeleteKey_U5();
            Console.WriteLine($"AUTODEL after Delete: {Where()} status='{WorldEdit.LastStatus}' " +
                              $"undo='{WorldHistory.NextUndoName ?? "none"}' stillSelected={WorldEdit.SelectedCount_V20}");

            TryWorldUndo();
            Console.WriteLine($"AUTODEL after undo:   {Where()} selected={WorldEdit.SelectedCount_V20}");
            TryWorldRedo();
            Console.WriteLine($"AUTODEL after redo:   {Where()}");
            TryWorldUndo();
            Console.WriteLine($"AUTODEL put back:     {Where()}");

            var one = group[0];
            WorldEdit.Select(one);
            PressDeleteKey_U5();
            Console.WriteLine($"AUTODEL one on its own: {Where()} status='{WorldEdit.LastStatus}' undo='{WorldHistory.NextUndoName ?? "none"}'");
            TryWorldUndo();
            Console.WriteLine($"AUTODEL one put back:   {Where()}");

            int px = deviceResources.Width / 2, py = deviceResources.Height / 2;
            WorldEdit.Deselect();
            WorldPickAt(px, py);
            int afterFirst = WorldEdit.SelectedCount_V20;
            ForcePickAdd_U5 = true;
            WorldPickAt(px, py);
            int afterToggleOff = WorldEdit.SelectedCount_V20;
            WorldPickAt(px, py);
            int afterToggleOn = WorldEdit.SelectedCount_V20;
            ForcePickAdd_U5 = false;
            Console.WriteLine($"AUTODEL modifier pick: plain={afterFirst} held(off)={afterToggleOff} held(on)={afterToggleOn} status='{WorldEdit.LastStatus}'");

            EntityOps.Clipboard.Clear();
            screenshotFrames = Math.Max(screenshotFrames, 30);
        }

        private void SeqTest_WorldMulti_U5(Action<string, bool, string> check)
        {
            try
            {
                var ymap = new YmapFile();
                ymap._CMapData.name = JenkHash.GenHash("rle_u5_map");
                ymap.Loaded = true;

                YmapEntityDef Make(string arch, float x)
                {
                    var cent = new CEntityDef
                    {
                        archetypeName = JenkHash.GenHash(arch),
                        position = new SharpDX.Vector3(x, 0, 0),
                        rotation = new SharpDX.Vector4(0, 0, 0, 1),
                        scaleXY = 1.0f,
                        scaleZ = 1.0f,
                        lodDist = 100.0f,
                        parentIndex = -1,
                        lodLevel = rage__eLodType.LODTYPES_DEPTH_ORPHANHD,
                    };
                    var e = new YmapEntityDef(ymap, 0, ref cent);
                    ymap.AddEntity(e);
                    return e;
                }

                var a = Make("prop_bench_01a", 0);
                var b = Make("prop_bin_01a", 5);
                var c = Make("prop_barrier_01a", 10);

                var w = new WorldEditor();
                w.Select(a);
                w.Toggle_V20(b);
                w.Toggle_V20(c);
                check("u5 multi: holding the key adds each prop to the selection",
                      w.SelectedCount_V20 == 3, w.SelectedCount_V20 + " selected");

                w.Toggle_V20(b);
                check("u5 multi: clicking one of them again drops just that one",
                      w.SelectedCount_V20 == 2 && !w.IsSelected_V20(b) && w.IsSelected_V20(a) && w.IsSelected_V20(c),
                      w.SelectedCount_V20 + " left");

                w.Toggle_V20(a);
                check("u5 multi: dropping the first one keeps the rest selected",
                      w.SelectedCount_V20 == 1 && w.IsSelected_V20(c) && !w.IsSelected_V20(a),
                      w.SelectedCount_V20 + " left");

                w.Select(a);
                w.Toggle_V20(b);
                w.Toggle_V20(c);
                int copied = EntityOps.Clipboard.Copy(w.AllSelected_V20());
                check("u5 copy: Copy takes every selected prop, not only the first",
                      copied == 3 && EntityOps.Clipboard.Count == 3, copied + " copied");

                var into = new YmapFile();
                into._CMapData.name = JenkHash.GenHash("rle_u5_target");
                into.Loaded = true;
                var made = EntityOps.Clipboard.Paste(into, new SharpDX.Vector3(0, 0, 10.0f));
                check("u5 copy: Paste puts all three down again, moved by the offset",
                      made.Count == 3 && into.AllEntities?.Length == 3 &&
                      made.All(m => Math.Abs(m.Position.Z - 10.0f) < 0.001f),
                      made.Count + " pasted");

                check("u5 copy: they are the same props that were copied",
                      made.Select(m => m._CEntityDef.archetypeName).OrderBy(h => h.Hash)
                          .SequenceEqual(new[] { a, b, c }.Select(e => e._CEntityDef.archetypeName).OrderBy(h => h.Hash)),
                      string.Join(", ", made.Select(m => m._CEntityDef.archetypeName.ToString())));

                EntityOps.Clipboard.Clear();
            }
            catch (Exception ex) { check("u5 multi select and copy", false, ex.Message); }
        }
    }
}
