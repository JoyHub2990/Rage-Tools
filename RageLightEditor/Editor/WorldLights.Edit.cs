using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldLights
    {
        private readonly Dictionary<uint, DrawableBase> drawableOf = new Dictionary<uint, DrawableBase>();
        public int Invalidations { get; private set; }

        private void RememberDrawable(uint archetypeHash, DrawableBase drawable)
        {
            if (drawable != null) drawableOf[archetypeHash] = drawable;
        }

        private void ForgetDrawables() => drawableOf.Clear();

        public bool TryGetDefs(uint archetypeHash, out LightDef[] defs) => byArchetype.TryGetValue(archetypeHash, out defs);

        public DrawableBase GetDrawable(uint archetypeHash) => drawableOf.TryGetValue(archetypeHash, out var d) ? d : null;

        public int Invalidate(uint archetypeHash)
        {
            if (!drawableOf.TryGetValue(archetypeHash, out var d) || d == null) return 0;
            var defs = Extract(d);
            Invalidations++;
            if (defs == null || defs.Length == 0) { byArchetype.Remove(archetypeHash); return 0; }
            byArchetype[archetypeHash] = defs;
            return defs.Length;
        }

        public static LightAttributes[] LightsOf(DrawableBase db)
        {
            if (db is Drawable dd) return dd.LightAttributes?.data_items;
            if (db is FragDrawable fd) return fd.OwnerFragment?.LightAttributes?.data_items;
            return null;
        }

        public static Skeleton SkeletonOf(DrawableBase db)
        {
            if (db == null) return null;
            if (db is FragDrawable fd) return fd.Skeleton ?? fd.OwnerFragment?.Drawable?.Skeleton;
            return db.Skeleton;
        }

        public static bool CanSave(DrawableBase db) => db is Drawable || (db is FragDrawable fd && fd.OwnerFragment != null);

        public static string SaveExtension(DrawableBase db) => db is FragDrawable ? ".yft" : ".ydr";

        public static string SaveDrawableAs(DrawableBase db, string path)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("no path", nameof(path));
            byte[] data;
            string ext = SaveExtension(db);
            if (!string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase))
                path = Path.ChangeExtension(path, ext);
            if (db is Drawable d)
            {
                var ydr = new YdrFile { Drawable = d };
                data = ydr.Save();
            }
            else if (db is FragDrawable fd && fd.OwnerFragment != null)
            {
                var yft = new YftFile { Fragment = fd.OwnerFragment };
                data = yft.Save();
            }
            else throw new Exception("this drawable is not a Drawable or a fragment's drawable - nothing to write");
            if (data == null || data.Length == 0) throw new Exception("the resource builder produced no bytes");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, data);
            return path;
        }

        public static LightAttributes ReloadLight(string path, int index, out int count)
        {
            count = 0;
            var bytes = File.ReadAllBytes(path);
            LightAttributes[] arr = null;
            if (path.EndsWith(".yft", StringComparison.OrdinalIgnoreCase))
            {
                var yft = new YftFile();
                yft.Load(bytes);
                arr = yft.Fragment?.LightAttributes?.data_items;
            }
            else
            {
                var ydr = new YdrFile();
                ydr.Load(bytes);
                arr = ydr.Drawable?.LightAttributes?.data_items;
            }
            count = arr?.Length ?? 0;
            return arr != null && index >= 0 && index < arr.Length ? arr[index] : null;
        }

        public static void CopyAllInto(LightAttributes from, LightAttributes to)
        {
            if (from == null || to == null) return;
            to.Unknown_0h = from.Unknown_0h; to.Unknown_4h = from.Unknown_4h;
            to.Position = from.Position; to.Unknown_14h = from.Unknown_14h;
            to.ColorR = from.ColorR; to.ColorG = from.ColorG; to.ColorB = from.ColorB;
            to.Flashiness = from.Flashiness; to.Intensity = from.Intensity; to.Flags = from.Flags;
            to.BoneId = from.BoneId; to.Type = from.Type; to.GroupId = from.GroupId; to.TimeFlags = from.TimeFlags;
            to.Falloff = from.Falloff; to.FalloffExponent = from.FalloffExponent;
            to.CullingPlaneNormal = from.CullingPlaneNormal; to.CullingPlaneOffset = from.CullingPlaneOffset;
            to.ShadowBlur = from.ShadowBlur; to.Unknown_45h = from.Unknown_45h; to.Unknown_46h = from.Unknown_46h; to.Unknown_48h = from.Unknown_48h;
            to.VolumeIntensity = from.VolumeIntensity; to.VolumeSizeScale = from.VolumeSizeScale;
            to.VolumeOuterColorR = from.VolumeOuterColorR; to.VolumeOuterColorG = from.VolumeOuterColorG; to.VolumeOuterColorB = from.VolumeOuterColorB;
            to.LightHash = from.LightHash; to.VolumeOuterIntensity = from.VolumeOuterIntensity;
            to.CoronaSize = from.CoronaSize; to.VolumeOuterExponent = from.VolumeOuterExponent;
            to.LightFadeDistance = from.LightFadeDistance; to.ShadowFadeDistance = from.ShadowFadeDistance;
            to.SpecularFadeDistance = from.SpecularFadeDistance; to.VolumetricFadeDistance = from.VolumetricFadeDistance;
            to.ShadowNearClip = from.ShadowNearClip; to.CoronaIntensity = from.CoronaIntensity; to.CoronaZBias = from.CoronaZBias;
            to.Direction = from.Direction; to.Tangent = from.Tangent;
            to.ConeInnerAngle = from.ConeInnerAngle; to.ConeOuterAngle = from.ConeOuterAngle;
            to.Extent = from.Extent; to.ProjectedTextureHash = from.ProjectedTextureHash; to.Unknown_A4h = from.Unknown_A4h;
        }

        public static bool AllFieldsEqual(LightAttributes a, LightAttributes b)
        {
            if (a == null || b == null) return a == b;
            return Scene.LightParamsEqual(a, b) && a.Position == b.Position && a.Direction == b.Direction &&
                   a.Tangent == b.Tangent && a.BoneId == b.BoneId;
        }
    }

    public static class WorldLightMath
    {
        private static bool IsZero(in Matrix m) => m.M44 == 0.0f && m.M11 == 0.0f && m.M22 == 0.0f && m.M33 == 0.0f;

        public static Vector3 WorldPos(YmapEntityDef e, in Matrix bone, Vector3 local)
        {
            var b = IsZero(bone) ? Matrix.Identity : bone;
            var bl = b.Multiply(local);
            if (e == null) return bl;
            var scale = e.Scale; if (scale.X <= 0.0f) scale = Vector3.One;
            return e.Orientation.Multiply(bl * scale) + e.Position;
        }

        public static Vector3 WorldDir(YmapEntityDef e, in Matrix bone, Vector3 local)
        {
            var b = IsZero(bone) ? Matrix.Identity : bone;
            var d = b.MultiplyRot(local);
            if (e != null) d = e.Orientation.Multiply(d);
            if (d.LengthSquared() > 1e-12f) d.Normalize();
            return d;
        }

        public static Vector3 LocalPos(YmapEntityDef e, in Matrix bone, Vector3 world)
        {
            var b = IsZero(bone) ? Matrix.Identity : bone;
            var bl = world;
            if (e != null)
            {
                var scale = e.Scale; if (scale.X <= 0.0f) scale = Vector3.One;
                bl = Quaternion.Invert(e.Orientation).Multiply(world - e.Position);
                bl = new Vector3(bl.X / scale.X, bl.Y / scale.Y, bl.Z / scale.Z);
            }
            return Matrix.Invert(b).Multiply(bl);
        }

        public static Vector3 LocalDir(YmapEntityDef e, in Matrix bone, Vector3 world)
        {
            var b = IsZero(bone) ? Matrix.Identity : bone;
            var d = world;
            if (e != null) d = Quaternion.Invert(e.Orientation).Multiply(d);
            d = Matrix.Invert(b).MultiplyRot(d);
            if (d.LengthSquared() > 1e-12f) d.Normalize();
            return d;
        }

        public static Vector3 OrthoTangent(Vector3 dir, Vector3 tan)
        {
            var t = tan - dir * Vector3.Dot(tan, dir);
            if (t.LengthSquared() < 1e-6f)
            {
                var reference = Math.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
                t = Vector3.Cross(dir, reference);
            }
            if (t.LengthSquared() > 1e-12f) t.Normalize();
            return t;
        }

        public static Quaternion Rotation(Vector3 worldDir, Vector3 worldTan)
        {
            var z = worldDir; if (z.LengthSquared() < 1e-12f) z = -Vector3.UnitZ; z.Normalize();
            var x = OrthoTangent(z, worldTan);
            var y = Vector3.Cross(z, x);
            var m = Matrix.Identity;
            m.Row1 = new Vector4(x, 0); m.Row2 = new Vector4(y, 0); m.Row3 = new Vector4(z, 0);
            var q = Quaternion.RotationMatrix(m);
            q.Normalize();
            return q;
        }

        public static void Axes(Quaternion q, out Vector3 worldDir, out Vector3 worldTan)
        {
            worldDir = q.Multiply(Vector3.UnitZ);
            worldTan = q.Multiply(Vector3.UnitX);
            if (worldDir.LengthSquared() > 1e-12f) worldDir.Normalize();
            worldTan = OrthoTangent(worldDir, worldTan);
        }
    }
}

