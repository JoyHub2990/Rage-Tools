using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class EntityOps
    {
        public static Action<YmapEntityDef> Invalidate;

        public static GameFileCache Cache;

        public static readonly EntityClipboard Clipboard = new EntityClipboard();

        private const uint LodInParentYmapFlag = 8u;

        private static void Touched(YmapEntityDef e)
        {
            if (e != null) Invalidate?.Invoke(e);
        }

        public static Archetype ResolveArchetype(YmapEntityDef e, GameFileCache cache = null, Archetype fallback = null)
        {
            if (e == null) return null;
            Archetype arch = null;
            try { arch = (cache ?? Cache)?.GetArchetype(e._CEntityDef.archetypeName); }
            catch { arch = null; }
            if (arch == null) arch = fallback;

            if (arch != null && !ReferenceEquals(arch, e.Archetype)) e.SetArchetype(arch);
            return e.Archetype;
        }

        public static EntitySnapshot Capture(YmapEntityDef e)
        {
            if (e == null) return null;
            var s = new EntitySnapshot
            {
                Def = e._CEntityDef,
                Extensions = (MetaWrapper[])e.Extensions?.Clone(),
                Archetype = e.Archetype,
                IsMlo = e.IsMlo && e.MloInstance != null,
            };
            if (s.IsMlo)
            {
                s.MloDef = e.MloInstance.Instance;
                s.MloEntitySets = (MetaHash[])e.MloInstance.defaultEntitySets?.Clone();
            }
            return s;
        }

        public static YmapEntityDef Restore(EntitySnapshot s, YmapFile into, GameFileCache cache = null)
        {
            if (s == null || into == null) return null;

            var def = s.Def;
            def.parentIndex = -1;
            def.numChildren = 0;
            def.flags &= ~LodInParentYmapFlag;
            def.guid = NextGuid(into, def.guid);

            int index = into.AllEntities?.Length ?? 0;

            YmapEntityDef e;
            if (s.IsMlo)
            {
                var inst = s.MloDef;
                inst.CEntityDef = def;
                e = new YmapEntityDef(into, index, ref inst);
                if (e.MloInstance != null && s.MloEntitySets != null)
                    e.MloInstance.defaultEntitySets = (MetaHash[])s.MloEntitySets.Clone();
            }
            else
            {
                e = new YmapEntityDef(into, index, ref def);
            }

            e.Extensions = (MetaWrapper[])s.Extensions?.Clone();
            ResolveArchetype(e, cache, s.Archetype);
            return e;
        }

        public static YmapEntityDef Clone(YmapEntityDef src, YmapFile intoYmap)
        {
            return Clone(src, intoYmap, null);
        }

        public static YmapEntityDef Clone(YmapEntityDef src, YmapFile intoYmap, GameFileCache cache)
        {
            if (src == null || intoYmap == null) return null;
            return Restore(Capture(src), intoYmap, cache);
        }

        private static uint NextGuid(YmapFile ymap, uint srcGuid)
        {
            var all = ymap?.AllEntities;
            if (all == null || all.Length == 0) return srcGuid;

            uint max = 0;
            bool any = false;
            foreach (var o in all)
            {
                if (o == null) continue;
                uint g = o._CEntityDef.guid;
                if (g != 0) any = true;
                if (g > max) max = g;
            }
            if (!any && srcGuid == 0) return 0;
            if (max == uint.MaxValue) return srcGuid;
            return max + 1;
        }

        public static bool Add(YmapFile ymap, YmapEntityDef e, GameFileCache cache = null, bool recalcExtents = true)
        {
            if (ymap == null || e == null) return false;
            if (ymap.AllEntities != null && Array.IndexOf(ymap.AllEntities, e) >= 0) return false;

            if (ymap.RootEntities == null) ymap.RootEntities = new YmapEntityDef[0];

            var parent = e.Parent;
            if (parent != null && parent.Ymap != ymap)
            {
                parent = null;
                e.Parent = null;
                e.ParentName = new MetaHash(0);
            }

            ymap.AddEntity(e);
            NormaliseOrder(ymap);

            e._CEntityDef.parentIndex = parent != null ? parent.Index : -1;
            e._CEntityDef.flags &= ~LodInParentYmapFlag;
            if (parent != null) LinkChild(parent, e);

            if (e.IsMlo) AddMloEntity(ymap, e);

            ResolveArchetype(e, cache, e.Archetype);

            if (recalcExtents) RecalcExtents(ymap);

            ymap.HasChanged = true;
            Touched(e);
            return true;
        }

        public static YmapEntityDef Duplicate(YmapEntityDef src, Vector3 offset)
        {
            return Duplicate(src, offset, null);
        }

        public static YmapEntityDef Duplicate(YmapEntityDef src, Vector3 offset, GameFileCache cache)
        {
            var ymap = src?.Ymap;
            if (ymap == null) return null;

            var e = Clone(src, ymap, cache);
            if (e == null) return null;

            if (src.Parent != null && src.Parent.Ymap == ymap) e.Parent = src.Parent;

            if (!Add(ymap, e, cache, false)) return null;
            e.SetPosition(e.Position + offset);
            RecalcExtents(ymap);
            Touched(e);
            return e;
        }

        public static bool Delete(YmapEntityDef e)
        {
            return Remove(e?.Ymap, e);
        }

        public static bool Remove(YmapFile ymap, YmapEntityDef e, GameFileCache cache = null)
        {
            if (ymap?.AllEntities == null || e == null) return false;

            int idx = Array.IndexOf(ymap.AllEntities, e);
            if (idx < 0) return false;
            e.Index = idx;

            if (ymap.RootEntities == null) ymap.RootEntities = new YmapEntityDef[0];
            int oldLen = ymap.AllEntities.Length;

            if (e.Parent != null) UnlinkChild(e.Parent, e);

            foreach (var c in ChildrenOf(e)) Orphan(c);
            e.Children = null;
            e.ChildrenMerged = null;

            ymap.RemoveEntity(e);

            var map = new int[oldLen];
            for (int i = 0; i < oldLen; i++) map[i] = i < idx ? i : (i == idx ? -1 : i - 1);
            RepairParentIndexes(ymap, map);

            if (e.IsMlo) RemoveMloEntity(ymap, e);

            e.Ymap = null;
            e.Parent = null;

            RecalcExtents(ymap);
            ymap.HasChanged = true;
            Touched(e);
            return Array.IndexOf(ymap.AllEntities, e) < 0;
        }

        private static void RepairParentIndexes(YmapFile ymap, int[] map)
        {
            var all = ymap.AllEntities;
            if (all != null)
            {
                foreach (var o in all)
                {
                    if (o == null) continue;
                    if (o.Parent != null && o.Parent.Ymap == ymap)
                    {
                        o._CEntityDef.parentIndex = o.Parent.Index;
                        continue;
                    }
                    if (o.Parent != null) continue;
                    if (o.LodInParentYmap) continue;

                    int pind = o._CEntityDef.parentIndex;
                    if (pind < 0 || pind >= map.Length) continue;
                    o._CEntityDef.parentIndex = map[pind];
                }
            }

            var kids = ymap.ChildYmaps;
            if (kids == null) return;
            foreach (var cy in kids)
            {
                var cents = cy?.AllEntities;
                if (cents == null) continue;
                foreach (var o in cents)
                {
                    if (o?.Parent == null || o.Parent.Ymap != ymap) continue;
                    o._CEntityDef.parentIndex = o.Parent.Index;
                }
            }
        }

        private static bool NormaliseOrder(YmapFile ymap)
        {
            var all = ymap?.AllEntities;
            if (all == null || all.Length < 2) return false;

            int firstMlo = -1;
            bool needs = false;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].MloInstance != null)
                {
                    if (firstMlo < 0) firstMlo = i;
                }
                else if (firstMlo >= 0) { needs = true; break; }
            }
            if (!needs) return false;

            var ordered = new List<YmapEntityDef>(all.Length);
            var map = new int[all.Length];
            for (int i = 0; i < all.Length; i++) map[i] = -1;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].MloInstance == null) { map[i] = ordered.Count; ordered.Add(all[i]); }
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].MloInstance != null) { map[i] = ordered.Count; ordered.Add(all[i]); }
            for (int i = 0; i < ordered.Count; i++) ordered[i].Index = i;

            ymap.AllEntities = ordered.ToArray();
            RepairParentIndexes(ymap, map);
            ymap.LodManagerUpdate = true;
            return true;
        }

        public static bool SetParent(YmapEntityDef child, YmapEntityDef parent)
        {
            if (child == null) return false;
            var ymap = child.Ymap;
            if (ymap == null) return false;

            if (parent == null)
            {
                Orphan(child);
                return true;
            }
            if (parent == child || parent.Ymap != ymap) return false;

            int hops = 0;
            for (var p = parent; p != null && hops < 64; p = p.Parent, hops++)
                if (p == child) return false;

            if (child.Parent != null) UnlinkChild(child.Parent, child);

            LinkChild(parent, child);
            child._CEntityDef.parentIndex = parent.Index;
            child._CEntityDef.flags &= ~LodInParentYmapFlag;
            RemoveRoot(ymap, child);
            ymap.HasChanged = true;
            return true;
        }

        public static void Orphan(YmapEntityDef c)
        {
            if (c == null) return;
            if (c.Parent != null) UnlinkChild(c.Parent, c);

            c.Parent = null;
            c.ParentName = new MetaHash(0);
            c._CEntityDef.parentIndex = -1;
            c._CEntityDef.flags &= ~LodInParentYmapFlag;

            var cy = c.Ymap;
            if (cy == null) return;
            AddRoot(cy, c);
            cy.HasChanged = true;
            cy.LodManagerUpdate = true;
        }

        private static List<YmapEntityDef> ChildrenOf(YmapEntityDef e)
        {
            var list = new List<YmapEntityDef>();
            if (e == null) return list;
            AddDistinct(list, e.Children);
            AddDistinct(list, e.ChildrenMerged);
            return list;
        }

        private static void AddDistinct(List<YmapEntityDef> list, YmapEntityDef[] src)
        {
            if (src == null) return;
            foreach (var c in src) if (c != null && !list.Contains(c)) list.Add(c);
        }

        private static void LinkChild(YmapEntityDef parent, YmapEntityDef child)
        {
            if (parent == null || child == null) return;
            child.Parent = parent;
            child.ParentName = parent._CEntityDef.archetypeName;

            parent.Children = Append(parent.Children, child);
            parent.ChildrenMerged = parent.ChildrenMerged == null
                ? parent.Children
                : Append(parent.ChildrenMerged, child);

            parent._CEntityDef.numChildren = parent._CEntityDef.numChildren + 1;
            if (parent.Ymap != null) parent.Ymap.LodManagerUpdate = true;
        }

        private static void UnlinkChild(YmapEntityDef parent, YmapEntityDef child)
        {
            if (parent == null || child == null) return;
            if (parent.Ymap != null) parent.Ymap.HasChanged = true;
            bool had = false;
            if (parent.Children != null)
            {
                var next = Without(parent.Children, child);
                had |= next.Length != parent.Children.Length;
                parent.Children = next;
            }
            if (parent.ChildrenMerged != null)
            {
                var next = Without(parent.ChildrenMerged, child);
                had |= next.Length != parent.ChildrenMerged.Length;
                parent.ChildrenMerged = next;
            }
            if (had && parent._CEntityDef.numChildren > 0)
                parent._CEntityDef.numChildren = parent._CEntityDef.numChildren - 1;

            parent.LodManagerRemoveChild(child);
            if (child.Parent == parent) child.Parent = null;
            if (parent.Ymap != null) parent.Ymap.LodManagerUpdate = true;
        }

        private static void AddRoot(YmapFile ymap, YmapEntityDef e)
        {
            if (ymap == null || e == null) return;
            if (ymap.RootEntities == null) { ymap.RootEntities = new[] { e }; return; }
            if (Array.IndexOf(ymap.RootEntities, e) >= 0) return;
            ymap.RootEntities = Append(ymap.RootEntities, e);
        }

        private static void RemoveRoot(YmapFile ymap, YmapEntityDef e)
        {
            if (ymap?.RootEntities == null || e == null) return;
            ymap.RootEntities = Without(ymap.RootEntities, e);
        }

        private static void AddMloEntity(YmapFile ymap, YmapEntityDef e)
        {
            if (ymap == null || e == null) return;
            if (ymap.MloEntities == null) { ymap.MloEntities = new[] { e }; return; }
            if (Array.IndexOf(ymap.MloEntities, e) >= 0) return;
            ymap.MloEntities = Append(ymap.MloEntities, e);
        }

        private static void RemoveMloEntity(YmapFile ymap, YmapEntityDef e)
        {
            if (ymap?.MloEntities == null || e == null) return;
            ymap.MloEntities = Without(ymap.MloEntities, e);
        }

        private static T[] Append<T>(T[] src, T item)
        {
            if (src == null) return new[] { item };
            var next = new T[src.Length + 1];
            Array.Copy(src, next, src.Length);
            next[src.Length] = item;
            return next;
        }

        private static T[] Without<T>(T[] src, T item) where T : class
        {
            if (src == null) return null;
            var list = new List<T>(src.Length);
            foreach (var o in src) if (!ReferenceEquals(o, item)) list.Add(o);
            return list.ToArray();
        }

        private static void RecalcExtents(YmapFile ymap)
        {
            if (ymap == null) return;
            if ((ymap.AllEntities?.Length ?? 0) == 0) return;
            try { ymap.CalcExtents(); } catch { }
        }

        public static int SelfTest(string unused)
        {
            var savedCache = Cache;
            var savedInvalidate = Invalidate;
            Cache = null;
            Invalidate = null;
            try
            {
                var ymap = MakeTestYmap("rle_ops_a", 5);
                Link(ymap, 2, 3);
                Link(ymap, 2, 4);

                var arr = ymap.AllEntities;
                if (arr[3]._CEntityDef.parentIndex != 2 || arr[4]._CEntityDef.parentIndex != 2)
                    throw new Exception("test setup: children do not point at entity 2");

                var added = Clone(arr[0], ymap);
                if (!Add(ymap, added)) throw new Exception("Add rejected a fresh clone");
                if (added.Index != 5 || ymap.AllEntities.Length != 6)
                    throw new Exception($"Add did not append: index {added.Index}, length {ymap.AllEntities.Length}");
                if (arr[3]._CEntityDef.parentIndex != 2 || arr[4]._CEntityDef.parentIndex != 2)
                    throw new Exception("Add moved somebody's parentIndex");
                if (Array.IndexOf(ymap.RootEntities, added) < 0)
                    throw new Exception("an unparented add is not a root");
                Console.WriteLine($"add: appended at {added.Index}, {ymap.AllEntities.Length} entities, parents intact");

                var doomed = arr[1];
                if (!Remove(ymap, doomed)) throw new Exception("Remove failed on a root entity");
                if (ymap.AllEntities.Length != 5) throw new Exception("Remove did not shrink the array");

                var parent = arr[2];
                if (parent.Index != 1) throw new Exception($"renumber wrong: entity 2 is now at {parent.Index}");
                if (arr[3]._CEntityDef.parentIndex != 1 || arr[4]._CEntityDef.parentIndex != 1)
                    throw new Exception($"parentIndex not repaired: {arr[3]._CEntityDef.parentIndex}, {arr[4]._CEntityDef.parentIndex}");
                foreach (var o in ymap.AllEntities)
                {
                    int pi = o._CEntityDef.parentIndex;
                    if (pi < 0) continue;
                    if (ymap.AllEntities[pi] != o.Parent)
                        throw new Exception($"entity {o.Index} parentIndex {pi} does not name its parent");
                }
                Console.WriteLine($"remove: {ymap.AllEntities.Length} entities, parent now at {parent.Index}, children repointed");

                if (!Remove(ymap, parent)) throw new Exception("Remove failed on a LOD parent");
                foreach (var c in new[] { arr[3], arr[4] })
                {
                    if (c._CEntityDef.parentIndex != -1) throw new Exception("orphan kept a stale parentIndex");
                    if (c.Parent != null) throw new Exception("orphan kept a Parent pointer");
                    if (Array.IndexOf(ymap.RootEntities, c) < 0) throw new Exception("orphan is unreachable: not a root");
                }
                Console.WriteLine($"remove parent: children re-rooted, {ymap.RootEntities.Length} roots");

                var ymap2 = MakeTestYmap("rle_ops_b", 3);
                Link(ymap2, 0, 1);
                var dup = Duplicate(ymap2.AllEntities[1], new Vector3(10, 0, 0));
                if (dup == null) throw new Exception("Duplicate returned nothing");
                if (dup.Parent != ymap2.AllEntities[0] || dup._CEntityDef.parentIndex != 0)
                    throw new Exception("Duplicate lost the LOD parent");
                if ((dup.Position - (ymap2.AllEntities[1].Position + new Vector3(10, 0, 0))).Length() > 0.001f)
                    throw new Exception($"Duplicate is in the wrong place: {dup.Position}");
                if (dup._CEntityDef.position != dup.Position)
                    throw new Exception("Duplicate did not write its position through to the CEntityDef");
                Console.WriteLine($"duplicate: at {dup.Position}, parent {dup._CEntityDef.parentIndex}, guid {dup._CEntityDef.guid}");

                var clip = new EntityClipboard();
                if (clip.Copy(new[] { ymap2.AllEntities[0], ymap2.AllEntities[1] }) != 2)
                    throw new Exception("Copy did not take both entities");

                var ymap3 = MakeTestYmap("rle_ops_c", 4);
                var pasted = clip.Paste(ymap3, new Vector3(0, 0, 100));
                if (pasted.Count != 2) throw new Exception($"Paste produced {pasted.Count} entities");
                if (pasted[1].Parent != pasted[0])
                    throw new Exception("Paste did not reconnect the copied hierarchy");
                if (pasted[1]._CEntityDef.parentIndex != pasted[0].Index)
                    throw new Exception($"pasted parentIndex {pasted[1]._CEntityDef.parentIndex} != {pasted[0].Index}");
                if (pasted[0].Index != 4 || pasted[1].Index != 5)
                    throw new Exception("Paste did not append after the existing entities");
                if (Math.Abs(pasted[0].Position.Z - (ymap2.AllEntities[0].Position.Z + 100.0f)) > 0.001f)
                    throw new Exception("Paste ignored the offset");
                foreach (var o in ymap3.AllEntities)
                {
                    int pi = o._CEntityDef.parentIndex;
                    if (pi >= 0 && ymap3.AllEntities[pi] != o.Parent)
                        throw new Exception($"pasted ymap: entity {o.Index} parentIndex {pi} mislinks");
                }
                Console.WriteLine($"clipboard: pasted {pasted.Count} into {ymap3.Name} at indexes {pasted[0].Index},{pasted[1].Index}");

                if (ymap2.AllEntities[1].Parent != ymap2.AllEntities[0])
                    throw new Exception("Copy/Paste disturbed the source ymap");

                var bytes = ymap3.Save();
                var rt = new YmapFile();
                RpfFile.LoadResourceFile(rt, bytes, 2);
                if (rt.AllEntities == null || rt.AllEntities.Length != 6)
                    throw new Exception($"round trip read back {rt.AllEntities?.Length ?? 0} entities");
                if (rt.AllEntities[5]._CEntityDef.parentIndex != 4)
                    throw new Exception($"round trip parentIndex {rt.AllEntities[5]._CEntityDef.parentIndex} != 4");
                if (rt.AllEntities[5].Parent != rt.AllEntities[4])
                    throw new Exception("round trip did not rebuild the hierarchy");
                Console.WriteLine($"round trip: {bytes.Length} bytes, {rt.AllEntities.Length} entities, hierarchy intact");

                Console.WriteLine("ENTITY OPS PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ENTITY OPS FAILED: " + ex.Message);
                return 1;
            }
            finally
            {
                Cache = savedCache;
                Invalidate = savedInvalidate;
            }
        }

        private static YmapFile MakeTestYmap(string name, int count)
        {
            var ymap = new YmapFile { Name = name + ".ymap" };
            JenkIndex.Ensure(name);

            var ents = new YmapEntityDef[count];
            for (int i = 0; i < count; i++)
            {
                var def = new CEntityDef
                {
                    archetypeName = JenkHash.GenHash("prop_rle_test"),
                    flags = 32,
                    guid = (uint)(i + 1),
                    position = new Vector3(i * 5.0f, 0.0f, 20.0f),
                    rotation = new Vector4(0, 0, 0, 1),
                    scaleXY = 1.0f,
                    scaleZ = 1.0f,
                    parentIndex = -1,
                    lodDist = 200.0f,
                    childLodDist = 100.0f,
                    lodLevel = rage__eLodType.LODTYPES_DEPTH_HD,
                    priorityLevel = rage__ePriorityLevel.PRI_REQUIRED,
                };
                ents[i] = new YmapEntityDef(ymap, i, ref def);
            }
            JenkIndex.Ensure("prop_rle_test");

            ymap.AllEntities = ents;
            ymap.RootEntities = (YmapEntityDef[])ents.Clone();
            ymap.CMapData = new CMapData { name = JenkHash.GenHash(name), contentFlags = 65 };
            return ymap;
        }

        private static void Link(YmapFile ymap, int parent, int child)
        {
            var p = ymap.AllEntities[parent];
            var c = ymap.AllEntities[child];
            c._CEntityDef.parentIndex = parent;
            c._CEntityDef.lodLevel = rage__eLodType.LODTYPES_DEPTH_HD;
            p._CEntityDef.lodLevel = rage__eLodType.LODTYPES_DEPTH_LOD;
            p.Children = Append(p.Children, c);
            p.ChildrenMerged = p.Children;
            p._CEntityDef.numChildren = p._CEntityDef.numChildren + 1;
            c.Parent = p;
            c.ParentName = p._CEntityDef.archetypeName;
            RemoveRoot(ymap, c);
        }
    }

    public class EntitySnapshot
    {
        public CEntityDef Def;
        public MetaWrapper[] Extensions;
        public bool IsMlo;
        public CMloInstanceDef MloDef;
        public MetaHash[] MloEntitySets;

        public Archetype Archetype;

        public int ParentInSet = -1;

        public Vector3 Position => Def.position;
        public string Name => Def.archetypeName.ToString();
        public override string ToString() => $"{Name}  ({Position.X:0.0}, {Position.Y:0.0}, {Position.Z:0.0})";
    }

    public class EntityClipboard
    {
        private readonly List<EntitySnapshot> items = new List<EntitySnapshot>();

        public int Count => items.Count;
        public bool IsEmpty => items.Count == 0;
        public IReadOnlyList<EntitySnapshot> Items => items;

        public Vector3 Centre { get; private set; }

        public string Summary =>
            items.Count == 0 ? "clipboard empty" :
            items.Count == 1 ? items[0].Name :
            $"{items.Count} entities";

        public void Clear()
        {
            items.Clear();
            Centre = Vector3.Zero;
        }

        public int Copy(YmapEntityDef e)
        {
            return Copy(e == null ? null : new[] { e });
        }

        public int Copy(IEnumerable<YmapEntityDef> ents)
        {
            items.Clear();
            Centre = Vector3.Zero;
            if (ents == null) return 0;

            var src = new List<YmapEntityDef>();
            foreach (var e in ents) if (e != null && !src.Contains(e)) src.Add(e);
            if (src.Count == 0) return 0;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var e in src)
            {
                var s = EntityOps.Capture(e);
                if (s == null) continue;
                s.ParentInSet = e.Parent != null ? src.IndexOf(e.Parent) : -1;
                items.Add(s);
                min = Vector3.Min(min, e.Position);
                max = Vector3.Max(max, e.Position);
            }
            if (items.Count > 0) Centre = (min + max) * 0.5f;
            return items.Count;
        }

        public List<YmapEntityDef> Paste(YmapFile target, Vector3 offset = default, GameFileCache cache = null)
        {
            var made = new List<YmapEntityDef>();
            if (target == null || items.Count == 0) return made;

            var built = new YmapEntityDef[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var e = EntityOps.Restore(items[i], target, cache);
                if (e == null) continue;
                if (!EntityOps.Add(target, e, cache, false)) continue;
                e.SetPosition(e.Position + offset);
                built[i] = e;
                made.Add(e);
            }

            for (int i = 0; i < items.Count; i++)
            {
                int p = items[i].ParentInSet;
                if (p < 0 || p >= built.Length) continue;
                if (built[i] == null || built[p] == null) continue;
                EntityOps.SetParent(built[i], built[p]);
            }

            if (made.Count > 0)
            {
                try { target.CalcExtents(); } catch { }
                target.HasChanged = true;
            }
            return made;
        }

        public List<YmapEntityDef> PasteAt(YmapFile target, Vector3 worldPoint, GameFileCache cache = null)
        {
            return Paste(target, worldPoint - Centre, cache);
        }
    }
}

