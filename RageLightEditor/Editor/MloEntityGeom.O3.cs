using System;
using System.Collections.Generic;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloEntityInfo
    {
        public readonly List<RenderMesh> PlacedMeshes_O3 = new List<RenderMesh>();
        public Matrix PlacedAt_O3 = Matrix.Identity;
        public bool HasPlacedMeshes_O3 => PlacedMeshes_O3.Count > 0;

        public void TrackPlacedMeshes_O3(RenderModel target, int count, Matrix world)
        {
            PlacedMeshes_O3.Clear();
            PlacedAt_O3 = world;
            if (target == null || count <= 0) return;
            int first = Math.Max(target.Meshes.Count - count, 0);
            for (int i = first; i < target.Meshes.Count; i++) PlacedMeshes_O3.Add(target.Meshes[i]);
        }

        public bool MovePlacedMeshes_O3(Matrix want)
        {
            if (PlacedMeshes_O3.Count == 0) return false;
            if (SamePlacement_O3(PlacedAt_O3, want)) return false;
            var delta = Matrix.Invert(PlacedAt_O3) * want;
            foreach (var m in PlacedMeshes_O3)
            {
                if (m == null) continue;
                m.Transform = m.Transform * delta;
                m.SetBoundsFromLocal();
            }
            PlacedAt_O3 = want;
            return true;
        }

        private static bool SamePlacement_O3(Matrix a, Matrix b)
        {
            for (int i = 0; i < 16; i++)
                if (Math.Abs(a[i / 4, i % 4] - b[i / 4, i % 4]) > 1e-6f) return false;
            return true;
        }

        public void SetPlacedVisible_O3(bool visible)
        {
            foreach (var m in PlacedMeshes_O3) if (m != null) m.Visible = visible;
        }

        public bool PlacedBounds_O3(out BoundingBox box)
        {
            box = default;
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            bool any = false;
            foreach (var m in PlacedMeshes_O3)
            {
                if (m == null) continue;
                var b = m.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                mn = Vector3.Min(mn, b.Minimum); mx = Vector3.Max(mx, b.Maximum);
                any = true;
            }
            if (any) box = new BoundingBox(mn, mx);
            return any;
        }
    }

    public class MloEntityMeshMap_O3
    {
        private readonly Dictionary<RenderMesh, MloCreatorEntity> map = new Dictionary<RenderMesh, MloCreatorEntity>();
        private MloCreatorSession builtFor;
        private int builtCount = -1, builtVersion = -1;

        public int Count => map.Count;

        public void Rebuild(MloCreatorSession s)
        {
            map.Clear();
            builtFor = s;
            builtCount = s?.Entities.Count ?? -1;
            builtVersion = s?.History?.Version ?? -1;
            if (s == null) return;
            foreach (var e in s.Entities)
            {
                var info = e.SourceInfo;
                if (info == null) continue;
                foreach (var m in info.PlacedMeshes_O3) if (m != null) map[m] = e;
            }
        }

        private void EnsureFresh(MloCreatorSession s)
        {
            if (ReferenceEquals(builtFor, s) && builtCount == (s?.Entities.Count ?? -1) &&
                builtVersion == (s?.History?.Version ?? -1)) return;
            Rebuild(s);
        }

        public MloCreatorEntity Of(MloCreatorSession s, RenderMesh mesh)
        {
            if (mesh == null || s == null) return null;
            EnsureFresh(s);
            return map.TryGetValue(mesh, out var e) ? e : null;
        }

        public MloCreatorEntity Pick(MloCreatorSession s, Scene scene, Ray ray, out float dist)
        {
            dist = float.MaxValue;
            MloCreatorEntity best = null;
            if (s == null || scene == null) return null;
            EnsureFresh(s);
            if (map.Count == 0) return null;
            foreach (var kv in map)
            {
                var m = kv.Key;
                if (m == null || !m.Visible) continue;
                var b = m.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                if (!ray.Intersects(ref b, out float bt) || bt > dist) continue;
                if (m.RayHit(ref ray, out float t) && t < dist) { dist = t; best = kv.Value; }
            }
            return best;
        }
    }
}

