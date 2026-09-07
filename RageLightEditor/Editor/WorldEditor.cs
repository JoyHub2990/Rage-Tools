using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldEditor
    {
        public WorldSelection Selection = WorldSelection.Empty;

        public YmapEntityDef Selected => Selection.EntityDef;

        private readonly HashSet<YmapFile> dirty = new HashSet<YmapFile>();
        public int DirtyCount => dirty.Count;
        public IEnumerable<YmapFile> Dirty => dirty;

        public string OutputFolder;

        public string LastStatus = "";

        public void Select(YmapEntityDef e) { ClearExtra_V20(); Selection = WorldSelection.FromProjectObject(e); }
        public void Select(in WorldSelection s) { ClearExtra_V20(); Selection = s.HasValue ? s : WorldSelection.Empty; }
        public void Deselect() { ClearExtra_V20(); Selection = WorldSelection.Empty; }
        public bool IsDirty(YmapFile y) => y != null && dirty.Contains(y);

        public YmapEntityDef PickBox(Ray ray, IReadOnlyList<YmapEntityDef> candidates, float maxDist, out float hitDist)
        {
            hitDist = float.MaxValue;
            if (candidates == null) return null;
            YmapEntityDef hit = null; float hitRadius = float.MaxValue;
            foreach (var e in candidates)
            {
                var arche = e?.Archetype;
                if (arche == null) continue;
                if (e.MloInstance != null) continue;

                var orientation = e.Orientation;
                var scale = e.Scale;
                var camrel = e.Position - ray.Position;
                var bsph = new BoundingSphere(camrel + orientation.Multiply(arche.BSCenter), arche.BSRadius * Math.Max(scale.X, Math.Max(scale.Y, scale.Z)));
                var localRay = new Ray(Vector3.Zero, ray.Direction);
                if (!localRay.Intersects(ref bsph)) continue;

                var orinv = Quaternion.Invert(orientation);
                var mray = new Ray(orinv.Multiply(-camrel), orinv.Multiply(ray.Direction));
                var bbox = new BoundingBox(arche.BBMin * scale, arche.BBMax * scale);
                if (bbox.Maximum.X <= bbox.Minimum.X) { var h = new Vector3(0.5f); bbox = new BoundingBox(-h, h); }
                if (!mray.Intersects(ref bbox, out float d)) continue;
                bool firsthit = hit == null;
                if (!(firsthit || d > 0.0f)) continue;
                if (d > maxDist) continue;
                float r = arche.BSRadius;
                bool radsm = r <= hitRadius;
                if (!radsm) continue;
                hit = e; hitRadius = r; hitDist = d > 0.0f ? d : hitDist;
            }
            return hit;
        }

        public YmapEntityDef Pick(Ray ray, IReadOnlyList<YmapEntityDef> candidates)
        {
            if (candidates == null) return null;
            YmapEntityDef best = null, bestInside = null;
            float bestDist = float.MaxValue, bestInsideVol = float.MaxValue;

            foreach (var e in candidates)
            {
                if (e?.Archetype == null) continue;
                var box = new BoundingBox(e.BBMin, e.BBMax);
                if (box.Maximum.X <= box.Minimum.X)
                {
                    var h = new Vector3(0.5f);
                    box = new BoundingBox(e.Position - h, e.Position + h);
                }
                if (!ray.Intersects(ref box, out float d)) continue;

                if (d <= 0.001f)
                {
                    var ext = box.Maximum - box.Minimum;
                    float vol = Math.Abs(ext.X * ext.Y * ext.Z);
                    if (vol < bestInsideVol) { bestInsideVol = vol; bestInside = e; }
                    continue;
                }

                if (d >= bestDist) continue;
                bestDist = d;
                best = e;
            }
            return best ?? bestInside;
        }

        public void SetPosition(Vector3 p)
        {
            if (Selected == null) return;
            Selected.SetPosition(p);
            MarkDirty();
        }

        public void SetOrientation(Quaternion q)
        {
            if (Selected == null) return;
            q.Normalize();
            Selected.SetOrientation(q);
            MarkDirty();
        }

        public void SetScale(Vector3 s)
        {
            if (Selected == null) return;
            s.X = Math.Max(s.X, 0.001f);
            s.Y = Math.Max(s.Y, 0.001f);
            s.Z = Math.Max(s.Z, 0.001f);
            Selected.SetScale(s);
            MarkDirty();
        }

        public void SetLodDist(float d)
        {
            if (Selected == null) return;
            Selected.LodDist = Math.Max(d, 0.0f);
            Selected._CEntityDef.lodDist = Selected.LodDist;
            MarkDirty();
        }

        public void MarkDirty()
        {
            var y = Selection.OwnerYmap;
            if (y != null) dirty.Add(y);
        }

        public void MarkDirty(YmapEntityDef e)
        {
            if (e?.Ymap != null) dirty.Add(e.Ymap);
        }

        public void MarkDirty(YmapFile y)
        {
            if (y != null) dirty.Add(y);
        }

        public static Vector3 ToEulerDegrees(Quaternion q)
        {
            q.Normalize();
            float sinp = 2.0f * (q.W * q.Y - q.Z * q.X);
            float pitch = Math.Abs(sinp) >= 1.0f
                ? (float)(Math.PI / 2.0) * Math.Sign(sinp)
                : (float)Math.Asin(sinp);
            float roll = (float)Math.Atan2(2.0f * (q.W * q.X + q.Y * q.Z),
                                           1.0f - 2.0f * (q.X * q.X + q.Y * q.Y));
            float yaw = (float)Math.Atan2(2.0f * (q.W * q.Z + q.X * q.Y),
                                          1.0f - 2.0f * (q.Y * q.Y + q.Z * q.Z));
            return new Vector3(MathUtil.RadiansToDegrees(roll),
                               MathUtil.RadiansToDegrees(pitch),
                               MathUtil.RadiansToDegrees(yaw));
        }

        public static Quaternion FromEulerDegrees(Vector3 deg) =>
            Quaternion.RotationYawPitchRoll(MathUtil.DegreesToRadians(deg.Z),
                                            MathUtil.DegreesToRadians(deg.Y),
                                            MathUtil.DegreesToRadians(deg.X));

        public string SaveOne(YmapFile ymap)
        {
            if (ymap == null) { LastStatus = "nothing to save"; return null; }
            if (string.IsNullOrEmpty(OutputFolder))
            {
                LastStatus = "choose an output folder first";
                return null;
            }
            try
            {
                Directory.CreateDirectory(OutputFolder);
                var name = ymap.Name;
                if (string.IsNullOrEmpty(name)) name = ymap.RpfFileEntry?.Name ?? "edited";
                if (!name.EndsWith(".ymap", StringComparison.OrdinalIgnoreCase)) name += ".ymap";

                var path = Path.Combine(OutputFolder, name);
                var data = ymap.Save();
                if (data == null || data.Length == 0)
                {
                    LastStatus = name + ": save produced nothing";
                    return null;
                }
                File.WriteAllBytes(path, data);
                dirty.Remove(ymap);
                LastStatus = $"wrote {name} ({data.Length / 1024} KB)";
                return path;
            }
            catch (Exception ex)
            {
                LastStatus = "save failed: " + ex.Message;
                return null;
            }
        }

        public int SaveAll()
        {
            var list = new List<YmapFile>(dirty);
            int done = 0;
            foreach (var y in list) if (SaveOne(y) != null) done++;
            if (done > 0) LastStatus = $"wrote {done} ymap{(done == 1 ? "" : "s")} to {OutputFolder}";
            return done;
        }

        public void DiscardAll()
        {
            dirty.Clear();
            LastStatus = "edits discarded - reload the world to see the originals";
        }
    }
}

