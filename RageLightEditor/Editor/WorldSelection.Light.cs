using System;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial struct WorldSelection
    {
        public LightAttributes Light;
        public YmapEntityDef LightEntity;
        public int LightIndex;
        public Matrix LightBone;

        private void ClearLight()
        {
            Light = null;
            LightEntity = null;
            LightIndex = -1;
            LightBone = Matrix.Identity;
        }

        public string LightNameString()
        {
            if (Light == null) return "";
            var arch = LightEntity?.Archetype?.Name ?? LightEntity?._CEntityDef.archetypeName.ToString() ?? "?";
            return $"{arch}: light {LightIndex} ({Light.Type})";
        }

        public Vector3 LightWorldPosition => Light == null ? Vector3.Zero : WorldLightMath.WorldPos(LightEntity, LightBone, Light.Position);
        public Vector3 LightWorldDirection => Light == null ? -Vector3.UnitZ : WorldLightMath.WorldDir(LightEntity, LightBone, Light.Direction);
        public Vector3 LightWorldTangent => Light == null ? Vector3.UnitX : WorldLightMath.WorldDir(LightEntity, LightBone, Light.Tangent);
        public Quaternion LightWorldRotation => Light == null ? Quaternion.Identity : WorldLightMath.Rotation(LightWorldDirection, LightWorldTangent);

        public void LightSetWorldPosition(Vector3 world)
        {
            if (Light == null) return;
            Light.Position = WorldLightMath.LocalPos(LightEntity, LightBone, world);
        }

        public void LightSetWorldRotation(Quaternion world)
        {
            if (Light == null) return;
            WorldLightMath.Axes(world, out var wd, out var wt);
            var ld = WorldLightMath.LocalDir(LightEntity, LightBone, wd);
            var lt = WorldLightMath.LocalDir(LightEntity, LightBone, wt);
            Light.Direction = ld;
            Light.Tangent = WorldLightMath.OrthoTangent(ld, lt);
        }

        public static WorldSelection ForLight(YmapEntityDef owner, LightAttributes light, int index, in Matrix bone, float pickRadius = 0.25f)
        {
            var s = Empty;
            if (light == null) return s;
            s.Light = light;
            s.LightEntity = owner;
            s.LightIndex = index;
            s.LightBone = bone;
            s.AABB = new BoundingBox(new Vector3(-pickRadius), new Vector3(pickRadius));
            return s;
        }
    }
}

