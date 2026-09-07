using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public struct GizmoTransform
    {
        public Vector3 Position;
        public Quaternion Orientation;
        public Vector3 Scale;

        public static GizmoTransform Capture(IWorldGizmoTarget t) => new GizmoTransform
        {
            Position = t.Position,
            Orientation = t.Orientation,
            Scale = t.Scale,
        };

        public void ApplyTo(IWorldGizmoTarget t)
        {
            t.SetPosition(Position);
            t.SetOrientation(Orientation);
            if (t.CanScale) t.SetScale(Scale);
        }

        public bool SameAs(GizmoTransform o) =>
            (Position - o.Position).LengthSquared() < 1e-10f &&
            Math.Abs(Quaternion.Dot(Orientation, o.Orientation)) > 1.0f - 1e-7f &&
            (Scale - o.Scale).LengthSquared() < 1e-10f;
    }

    public class GizmoTransformCommand : IEditCommand, IMergeableCommand
    {
        private readonly IWorldGizmoTarget[] targets;
        private readonly GizmoTransform[] before;
        private GizmoTransform[] after;
        private readonly Action<IWorldGizmoTarget> onApplied;

        public string Name { get; }
        public IReadOnlyList<IWorldGizmoTarget> Targets => targets;
        public TimeSpan MergeWindow = TimeSpan.FromMilliseconds(750);
        public object MergeKey;

        public GizmoTransformCommand(string name, IWorldGizmoTarget[] targets, GizmoTransform[] before,
                                     GizmoTransform[] after, Action<IWorldGizmoTarget> onApplied)
        {
            this.targets = targets ?? Array.Empty<IWorldGizmoTarget>();
            this.before = before ?? Array.Empty<GizmoTransform>();
            this.after = after ?? Array.Empty<GizmoTransform>();
            this.onApplied = onApplied;
            if (this.before.Length != this.targets.Length || this.after.Length != this.targets.Length)
                throw new ArgumentException("GizmoTransformCommand: before/after must have exactly one entry per target.");
            Name = string.IsNullOrEmpty(name) ? "Transform" : name;
        }

        public void Do() => Apply(after);
        public void Undo() => Apply(before);

        private void Apply(GizmoTransform[] state)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t == null) continue;
                state[i].ApplyTo(t);
                onApplied?.Invoke(t);
            }
        }

        public bool TryMerge(IEditCommand newer, TimeSpan since)
        {
            if (!(newer is GizmoTransformCommand n)) return false;
            if (n.targets.Length != targets.Length) return false;
            for (int i = 0; i < targets.Length; i++)
                if (!ReferenceEquals(targets[i].Key, n.targets[i].Key)) return false;
            bool sameGesture = MergeKey != null && Equals(MergeKey, n.MergeKey);
            if (!sameGesture && since > MergeWindow) return false;
            after = n.after;
            return true;
        }

        public static Pending Begin(string name, IReadOnlyList<IWorldGizmoTarget> targets, Action<IWorldGizmoTarget> onApplied) =>
            new Pending(name, targets, onApplied);

        public class Pending
        {
            private readonly string name;
            private readonly IWorldGizmoTarget[] targets;
            private readonly GizmoTransform[] before;
            private readonly Action<IWorldGizmoTarget> onApplied;

            internal Pending(string name, IReadOnlyList<IWorldGizmoTarget> ts, Action<IWorldGizmoTarget> onApplied)
            {
                this.name = name;
                this.onApplied = onApplied;
                int n = ts?.Count ?? 0;
                targets = new IWorldGizmoTarget[n];
                before = new GizmoTransform[n];
                for (int i = 0; i < n; i++)
                {
                    targets[i] = ts[i];
                    before[i] = GizmoTransform.Capture(ts[i]);
                }
            }

            public int Count => targets.Length;

            public GizmoTransformCommand Complete()
            {
                if (targets.Length == 0) return null;
                var after = new GizmoTransform[targets.Length];
                bool changed = false;
                for (int i = 0; i < targets.Length; i++)
                {
                    after[i] = GizmoTransform.Capture(targets[i]);
                    if (!after[i].SameAs(before[i])) changed = true;
                }
                if (!changed) return null;
                return new GizmoTransformCommand(name, targets, before, after, onApplied);
            }
        }
    }

    public class SnapshotCommand<T> : IEditCommand, IMergeableCommand
    {
        private readonly object key;
        private readonly T before;
        private T after;
        private readonly Action<T> restore;
        public string Name { get; }
        public TimeSpan MergeWindow = TimeSpan.FromMilliseconds(750);

        public SnapshotCommand(string name, object key, T before, T after, Action<T> restore)
        {
            Name = name; this.key = key; this.before = before; this.after = after; this.restore = restore;
        }
        public void Do() => restore(after);
        public void Undo() => restore(before);
        public bool TryMerge(IEditCommand newer, TimeSpan since)
        {
            if (!(newer is SnapshotCommand<T> n) || !ReferenceEquals(n.key, key)) return false;
            if (since > MergeWindow) return false;
            after = n.after;
            return true;
        }
    }
}

