using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public interface IEditCommand
    {
        string Name { get; }

        void Do();

        void Undo();
    }

    public interface IMergeableCommand : IEditCommand
    {
        bool TryMerge(IEditCommand newer, TimeSpan since);
    }

    public class EditHistory
    {
        private readonly List<IEditCommand> undo = new List<IEditCommand>();
        private readonly List<IEditCommand> redo = new List<IEditCommand>();

        public int Limit = 256;

        public event Action Changed;

        public int Count => undo.Count;
        public int RedoCount => redo.Count;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public string NextUndoName => undo.Count > 0 ? undo[undo.Count - 1].Name : null;
        public string NextRedoName => redo.Count > 0 ? redo[redo.Count - 1].Name : null;

        public bool IsApplying { get; private set; }

        private DateTime lastPush = DateTime.MinValue;

        private bool mergeBarrier;

        private readonly List<IEditCommand> group = new List<IEditCommand>();
        private string groupName;
        private int groupDepth;

        public bool InGroup => groupDepth > 0;

        public void BeginGroup(string name)
        {
            if (groupDepth++ == 0)
            {
                group.Clear();
                groupName = name;
            }
        }

        public void EndGroup()
        {
            if (groupDepth == 0) return;
            if (--groupDepth > 0) return;

            if (group.Count == 0) { groupName = null; return; }

            var c = group.Count == 1 ? group[0] : new CompositeCommand(groupName, group.ToArray());
            group.Clear();
            groupName = null;
            PushEntry(c);
        }

        public void CancelGroup()
        {
            if (groupDepth == 0) return;
            groupDepth = 0;
            group.Clear();
            groupName = null;
        }

        public void Push(IEditCommand c)
        {
            if (c == null) return;
            if (IsApplying) return;

            if (groupDepth > 0)
            {
                if (group.Count > 0 && group[group.Count - 1] is IMergeableCommand gm &&
                    gm.TryMerge(c, TimeSpan.Zero))
                    return;
                group.Add(c);
                return;
            }

            if (mergeBarrier) { mergeBarrier = false; PushEntry(c); return; }
            if (undo.Count > 0 && undo[undo.Count - 1] is IMergeableCommand m &&
                m.TryMerge(c, DateTime.UtcNow - lastPush))
            {
                redo.Clear();
                lastPush = DateTime.UtcNow;
                Changed?.Invoke();
                return;
            }

            PushEntry(c);
        }

        private void PushEntry(IEditCommand c)
        {
            mergeBarrier = false;
            undo.Add(c);
            while (undo.Count > Math.Max(1, Limit)) undo.RemoveAt(0);
            redo.Clear();
            lastPush = DateTime.UtcNow;
            Changed?.Invoke();
        }

        public void Execute(IEditCommand c)
        {
            if (c == null) return;
            var prev = IsApplying;
            IsApplying = true;
            try { c.Do(); }
            finally { IsApplying = prev; }
            if (!prev) Push(c);
        }

        public void Push(string name, Action doIt, Action undoIt) =>
            Push(new DelegateCommand(name, doIt, undoIt));

        public void Undo()
        {
            if (groupDepth > 0) { groupDepth = 1; EndGroup(); }
            if (undo.Count == 0) return;

            var c = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            var prev = IsApplying;
            IsApplying = true;
            bool ok = false;
            try { c.Undo(); ok = true; }
            finally
            {
                IsApplying = prev;
                mergeBarrier = true;
                if (ok) redo.Add(c); else undo.Add(c);
                Changed?.Invoke();
            }
        }

        public void Redo()
        {
            if (groupDepth > 0) { groupDepth = 1; EndGroup(); }
            if (redo.Count == 0) return;

            var c = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            var prev = IsApplying;
            IsApplying = true;
            bool ok = false;
            try { c.Do(); ok = true; }
            finally
            {
                IsApplying = prev;
                mergeBarrier = true;
                if (ok) undo.Add(c); else redo.Add(c);
                Changed?.Invoke();
            }
        }

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
            group.Clear();
            groupDepth = 0;
            groupName = null;
            lastPush = DateTime.MinValue;
            Changed?.Invoke();
        }
    }

    public class DelegateCommand : IEditCommand
    {
        private readonly Action doIt;
        private readonly Action undoIt;

        public string Name { get; }

        public DelegateCommand(string name, Action doIt, Action undoIt)
        {
            Name = string.IsNullOrEmpty(name) ? "Edit" : name;
            this.doIt = doIt;
            this.undoIt = undoIt;
        }

        public void Do() => doIt?.Invoke();
        public void Undo() => undoIt?.Invoke();
    }

    public class CompositeCommand : IEditCommand
    {
        private readonly IEditCommand[] parts;

        public string Name { get; }
        public int PartCount => parts.Length;

        public CompositeCommand(string name, params IEditCommand[] parts)
        {
            this.parts = parts ?? Array.Empty<IEditCommand>();
            Name = string.IsNullOrEmpty(name)
                 ? (this.parts.Length > 0 ? this.parts[0].Name : "Edit")
                 : name;
        }

        public void Do()
        {
            List<Exception> errs = null;
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i]?.Do(); }
                catch (Exception ex) { (errs ??= new List<Exception>()).Add(ex); }
            }
            if (errs != null) throw new AggregateException(Name, errs);
        }

        public void Undo()
        {
            List<Exception> errs = null;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                try { parts[i]?.Undo(); }
                catch (Exception ex) { (errs ??= new List<Exception>()).Add(ex); }
            }
            if (errs != null) throw new AggregateException(Name, errs);
        }
    }

    public struct EntityTransform
    {
        public Vector3 Position;
        public Quaternion Orientation;
        public float ScaleXY;
        public float ScaleZ;
        public float LodDist;

        public Vector3 Scale => new Vector3(ScaleXY, ScaleXY, ScaleZ);

        public static Vector3 Representable(Vector3 s) => new Vector3(s.X, s.X, s.Z);

        public static EntityTransform Capture(YmapEntityDef e)
        {
            if (e == null) return new EntityTransform { Orientation = Quaternion.Identity, ScaleXY = 1.0f, ScaleZ = 1.0f };
            return new EntityTransform
            {
                Position = e.Position,
                Orientation = e.Orientation,
                ScaleXY = e._CEntityDef.scaleXY,
                ScaleZ = e._CEntityDef.scaleZ,
                LodDist = e._CEntityDef.lodDist,
            };
        }

        public void ApplyTo(YmapEntityDef e)
        {
            if (e == null) return;

            e._CEntityDef.lodDist = LodDist;

            bool scaleChanged = e._CEntityDef.scaleXY != ScaleXY || e._CEntityDef.scaleZ != ScaleZ;
            bool posChanged = e.Position != Position;
            bool oriChanged = Orientation.LengthSquared() > 1e-9f && e.Orientation != Orientation;

            if (oriChanged)
            {
                var q = Orientation;
                q.Normalize();
                e.SetOrientation(q);
            }

            if (scaleChanged)
            {
                e.SetScale(new Vector3(ScaleXY, ScaleXY, ScaleZ));
            }

            if (posChanged || (oriChanged && e.MloParent != null)) e.SetPosition(Position);

            e.LodDist = LodDist > 0.0f ? LodDist : (e.Archetype != null ? e.Archetype.LodDist : 0.0f);
        }

        public bool SameAs(EntityTransform o) =>
            Position == o.Position && Orientation == o.Orientation &&
            ScaleXY == o.ScaleXY && ScaleZ == o.ScaleZ && LodDist == o.LodDist;
    }

    public class EntityTransformCommand : IEditCommand, IMergeableCommand
    {
        private readonly YmapEntityDef[] entities;
        private readonly EntityTransform[] before;
        private EntityTransform[] after;
        private readonly Action<YmapEntityDef> onApplied;

        public string Name { get; }

        public IReadOnlyList<YmapEntityDef> Entities => entities;

        public TimeSpan MergeWindow = TimeSpan.FromMilliseconds(750);

        public object MergeKey;

        public bool Sealed;

        public EntityTransformCommand(string name,
                                      YmapEntityDef[] entities,
                                      EntityTransform[] before,
                                      EntityTransform[] after,
                                      Action<YmapEntityDef> onApplied)
        {
            this.entities = entities ?? Array.Empty<YmapEntityDef>();
            this.before = before ?? Array.Empty<EntityTransform>();
            this.after = after ?? Array.Empty<EntityTransform>();
            this.onApplied = onApplied;
            if (this.before.Length != this.entities.Length || this.after.Length != this.entities.Length)
                throw new ArgumentException(
                    "EntityTransformCommand: before/after must have exactly one entry per entity.");
            Name = string.IsNullOrEmpty(name)
                 ? (this.entities.Length == 1 ? "Transform entity"
                                              : $"Transform {this.entities.Length} entities")
                 : name;
        }

        public EntityTransformCommand(string name, YmapEntityDef e,
                                      EntityTransform before, EntityTransform after,
                                      Action<YmapEntityDef> onApplied)
            : this(name, new[] { e }, new[] { before }, new[] { after }, onApplied)
        {
        }

        public void Do() => Apply(after);
        public void Undo() => Apply(before);

        private void Apply(EntityTransform[] state)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e == null) continue;
                state[i].ApplyTo(e);
                onApplied?.Invoke(e);
            }
        }

        public bool TryMerge(IEditCommand newer, TimeSpan since)
        {
            if (!(newer is EntityTransformCommand n)) return false;
            if (!SameTargets(n)) return false;
            if (n.after.Length != entities.Length) return false;

            bool sameGesture = MergeKey != null && Equals(MergeKey, n.MergeKey);
            if (!sameGesture && Sealed) return false;
            if (!sameGesture && since > MergeWindow) return false;

            after = n.after;
            return true;
        }

        private bool SameTargets(EntityTransformCommand o)
        {
            if (o.entities.Length != entities.Length) return false;
            for (int i = 0; i < entities.Length; i++)
                if (!ReferenceEquals(entities[i], o.entities[i])) return false;
            return true;
        }

        public static Pending Begin(string name, IReadOnlyList<YmapEntityDef> entities,
                                    Action<YmapEntityDef> onApplied) =>
            new Pending(name, entities, onApplied);

        public static Pending Begin(string name, YmapEntityDef e, Action<YmapEntityDef> onApplied) =>
            new Pending(name, e == null ? Array.Empty<YmapEntityDef>() : new[] { e }, onApplied);

        public class Pending
        {
            private readonly string name;
            private readonly YmapEntityDef[] entities;
            private readonly EntityTransform[] before;
            private readonly Action<YmapEntityDef> onApplied;

            internal Pending(string name, IReadOnlyList<YmapEntityDef> ents, Action<YmapEntityDef> onApplied)
            {
                this.name = name;
                this.onApplied = onApplied;
                int n = ents?.Count ?? 0;
                entities = new YmapEntityDef[n];
                before = new EntityTransform[n];
                for (int i = 0; i < n; i++)
                {
                    entities[i] = ents[i];
                    before[i] = EntityTransform.Capture(ents[i]);
                }
            }

            public int Count => entities.Length;

            public EntityTransformCommand Complete()
            {
                if (entities.Length == 0) return null;
                var after = new EntityTransform[entities.Length];
                bool changed = false;
                for (int i = 0; i < entities.Length; i++)
                {
                    after[i] = EntityTransform.Capture(entities[i]);
                    if (!after[i].SameAs(before[i])) changed = true;
                }
                if (!changed) return null;
                return new EntityTransformCommand(name, entities, before, after, onApplied) { Sealed = true };
            }
        }
    }
}

