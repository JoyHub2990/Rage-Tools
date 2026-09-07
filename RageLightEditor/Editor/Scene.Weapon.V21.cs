using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class Scene
    {
        public readonly List<(LoadedFile Comp, LoadedFile Weapon, string Bone)> WeaponAttachments_V21
            = new List<(LoadedFile, LoadedFile, string)>();

        public LoadedFile ActiveWeapon_V21()
        {
            if (IsWeapon_V21(ActiveFile)) return ActiveFile;
            LoadedFile only = null;
            foreach (var f in Files)
                if (IsWeapon_V21(f))
                {
                    if (only != null) return null;
                    only = f;
                }
            return only;
        }

        public static bool IsWeapon_V21(LoadedFile lf) => IsWeaponSkeleton_V22(lf?.Skeleton);

        public static bool IsWeaponSkeleton_V22(Skeleton skel) =>
            skel?.Bones?.Items?.Any(b => b?.Name?.StartsWith("WAP", StringComparison.OrdinalIgnoreCase) == true) == true;

        public static Bone SlotBoneOf_V22(Skeleton weapon, string aapName) => SlotBoneOf_V26(weapon, aapName, null);

        public static Bone SlotBoneOf_V26(Skeleton weapon, string aapName, string metaBone)
        {
            var bones = weapon?.Bones?.Items;
            if (bones == null) return null;

            Bone Exact(string want) => string.IsNullOrEmpty(want) ? null
                : bones.FirstOrDefault(b => string.Equals(b?.Name, want, StringComparison.OrdinalIgnoreCase));

            var hit = Exact(metaBone);
            if (hit != null) return hit;

            string suffix = null;
            if (!string.IsNullOrEmpty(aapName) && aapName.Length > 3) suffix = aapName.Substring(3);
            else if (!string.IsNullOrEmpty(metaBone) && metaBone.StartsWith("WAP", StringComparison.OrdinalIgnoreCase) && metaBone.Length > 3)
                suffix = metaBone.Substring(3);
            hit = Exact("WAP" + suffix);
            if (hit != null) return hit;

            if (!string.IsNullOrEmpty(suffix))
            {
                hit = bones.FirstOrDefault(b => b?.Name != null && b.Name.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
                hit = bones.FirstOrDefault(b => b?.Name != null && b.Name.Length >= 3 &&
                                                suffix.IndexOf(b.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
            }
            if (!string.IsNullOrEmpty(metaBone))
            {
                hit = bones.FirstOrDefault(b => b?.Name != null && b.Name.IndexOf(metaBone, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
            }

            return Exact("Gun_Root") ?? bones.FirstOrDefault(b => b != null);
        }

        public static Matrix AttachMatrixOf_V22(Bone wapBone, Skeleton compSkel)
        {
            var m = wapBone?.AbsTransform ?? Matrix.Identity;
            var aap = AapBoneOf_V21(compSkel);
            if (aap != null) m = Matrix.Invert(aap.AbsTransform) * m;
            return m;
        }

        public static Bone AapBoneOf_V21(Skeleton skel)
        {
            var bones = skel?.Bones?.Items;
            if (bones == null) return null;
            return bones.FirstOrDefault(b => b?.Name?.StartsWith("AAP", StringComparison.OrdinalIgnoreCase) == true);
        }

        public static Bone SlotBone_V21(LoadedFile weapon, string aapName)
        {
            var bones = weapon?.Skeleton?.Bones?.Items;
            if (bones == null) return null;
            if (!string.IsNullOrEmpty(aapName) && aapName.Length > 3)
            {
                var want = "WAP" + aapName.Substring(3);
                var hit = bones.FirstOrDefault(b => string.Equals(b?.Name, want, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return bones.FirstOrDefault(b => string.Equals(b?.Name, "Gun_Root", StringComparison.OrdinalIgnoreCase))
                ?? bones.FirstOrDefault(b => b != null);
        }

        public static Matrix AttachMatrix_V21(LoadedFile weapon, Bone wapBone, Skeleton compSkel)
        {
            var m = wapBone?.AbsTransform ?? Matrix.Identity;
            var aap = AapBoneOf_V21(compSkel);
            if (aap != null) m = Matrix.Invert(aap.AbsTransform) * m;
            if (weapon != null && weapon.HasPlacement) m = m * weapon.Placement;
            return m;
        }

        public string AttachWeaponComponent_V21(LoadedFile weapon, YdrFile compYdr, string displayName, out string why)
        {
            why = null;
            if (weapon == null || !IsWeapon_V21(weapon)) { why = "the active file has no WAP attachment bones"; return null; }
            var drawable = compYdr?.Drawable;
            if (drawable == null) { why = "the component would not read"; return null; }

            var compSkel = drawable.Skeleton;
            var aap = AapBoneOf_V21(compSkel);
            var bone = SlotBone_V21(weapon, aap?.Name);
            if (bone == null) { why = "the weapon has no bone to hang this on"; return null; }

            var place = AttachMatrix_V21(weapon, bone, compSkel);
            var model = modelRenderer.BuildFromYdr(compYdr);
            if (model == null) { why = "the component's model would not build"; return null; }

            var lf = AddImportedProp(null, compYdr, null, model, compSkel,
                                     compYdr.Drawable?.LightAttributes?.data_items,
                                     place, 1, readOnly: true, displayName: displayName,
                                     fromMlo: false, drawable: drawable);
            WeaponAttachments_V21.Add((lf, weapon, bone.Name));
            GeometryVersion++;
            return bone.Name;
        }

        public bool DetachWeaponComponent_V21(LoadedFile comp)
        {
            int i = WeaponAttachments_V21.FindIndex(a => ReferenceEquals(a.Comp, comp));
            if (i < 0) return false;
            WeaponAttachments_V21.RemoveAt(i);
            RemoveFile(comp);
            return true;
        }

        public void PruneWeaponAttachments_V21()
        {
            for (int i = WeaponAttachments_V21.Count - 1; i >= 0; i--)
            {
                var a = WeaponAttachments_V21[i];
                if (!Files.Contains(a.Comp)) { WeaponAttachments_V21.RemoveAt(i); continue; }
                if (!Files.Contains(a.Weapon))
                {
                    WeaponAttachments_V21.RemoveAt(i);
                    RemoveFile(a.Comp);
                }
            }
        }
    }
}

