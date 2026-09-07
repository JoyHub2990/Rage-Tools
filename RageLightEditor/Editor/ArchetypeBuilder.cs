using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class ArchetypeDef
    {
        public string Name = "";
        public string TextureDict = "";
        public string PhysicsDict = "";
        public float LodDist = 200.0f;
        public float HdTextureDist = 15.0f;
        public uint Flags = 32;
        public uint SpecialAttribute;
        public bool IsFragment;

        public Vector3 BbMin, BbMax, BsCentre;
        public float BsRadius;

        public int LightCount;

        public string Label => $"{Name}   r={BsRadius:0.00}m  ({LightCount} light{(LightCount == 1 ? "" : "s")})";
    }

    public static class ArchetypeBuilder
    {
        public static BoundingBox LightBounds(LightAttributes l)
        {
            var p = l.Position;
            float fall = Math.Max(l.Falloff, 0.01f);

            switch ((byte)l.Type)
            {
                case 2:
                {
                    var dir = l.Direction;
                    if (dir.LengthSquared() < 1e-6f) dir = -Vector3.UnitZ; else dir.Normalize();
                    float half = MathUtil.Clamp(MathUtil.DegreesToRadians(l.ConeOuterAngle), 0.01f, 1.55f);
                    float rimR = (float)Math.Tan(half) * fall;
                    var baseC = p + dir * fall;
                    var spread = new Vector3(
                        rimR * (float)Math.Sqrt(Math.Max(1.0f - dir.X * dir.X, 0f)),
                        rimR * (float)Math.Sqrt(Math.Max(1.0f - dir.Y * dir.Y, 0f)),
                        rimR * (float)Math.Sqrt(Math.Max(1.0f - dir.Z * dir.Z, 0f)));
                    return new BoundingBox(Vector3.Min(p, baseC - spread),
                                           Vector3.Max(p, baseC + spread));
                }
                case 4:
                {
                    var dir = l.Direction;
                    if (dir.LengthSquared() < 1e-6f) dir = -Vector3.UnitZ; else dir.Normalize();
                    var ext = dir * (Math.Max(l.Extent.X, 0.0f) * 0.5f);
                    var a = p + ext;
                    var b = p - ext;
                    var r = new Vector3(fall);
                    return new BoundingBox(Vector3.Min(a, b) - r, Vector3.Max(a, b) + r);
                }
                default:
                {
                    var r = new Vector3(fall);
                    return new BoundingBox(p - r, p + r);
                }
            }
        }

        public static BoundingBox? LightBounds(IEnumerable<LightAttributes> lights)
        {
            BoundingBox? acc = null;
            foreach (var l in lights ?? Enumerable.Empty<LightAttributes>())
            {
                if (l == null) continue;
                var b = LightBounds(l);
                acc = acc.HasValue ? BoundingBox.Merge(acc.Value, b) : b;
            }
            return acc;
        }

        public static bool ApplyLightBounds(ArchetypeDef def, IReadOnlyList<LightAttributes> lights)
        {
            var b = LightBounds(lights);
            if (!b.HasValue) return false;
            var box = b.Value;

            def.BbMin = box.Minimum;
            def.BbMax = box.Maximum;
            def.BsCentre = (box.Minimum + box.Maximum) * 0.5f;
            def.BsRadius = Math.Max((box.Maximum - box.Minimum).Length() * 0.5f, 0.01f);
            def.LightCount = lights?.Count ?? 0;

            def.LodDist = (float)Math.Round(MathUtil.Clamp(def.BsRadius * 8.0f, 100.0f, 1500.0f) / 10.0) * 10.0f;
            return true;
        }

        public static ArchetypeDef FromLightProp(string name, IReadOnlyList<LightAttributes> lights)
        {
            var def = new ArchetypeDef { Name = LightProxyBuilder.Sanitised(name) };
            if (!ApplyLightBounds(def, lights))
            {
                def.BbMin = new Vector3(-0.5f);
                def.BbMax = new Vector3(0.5f);
                def.BsCentre = Vector3.Zero;
                def.BsRadius = 0.87f;
                def.LodDist = 100.0f;
            }
            return def;
        }

        private static uint Hash(string s) =>
            string.IsNullOrWhiteSpace(s) ? 0 : JenkHash.GenHash(s.Trim().ToLowerInvariant());

        public static Archetype Build(ArchetypeDef def)
        {
            var name = LightProxyBuilder.Sanitised(def.Name);
            JenkIndex.Ensure(name);

            var cbad = new CBaseArchetypeDef
            {
                lodDist = def.LodDist,
                flags = def.Flags,
                specialAttribute = def.SpecialAttribute,
                bbMin = def.BbMin,
                bbMax = def.BbMax,
                bsCentre = def.BsCentre,
                bsRadius = def.BsRadius,
                hdTextureDist = def.HdTextureDist,
                name = new MetaHash(JenkHash.GenHash(name)),
                textureDictionary = new MetaHash(Hash(def.TextureDict)),
                clipDictionary = new MetaHash(0),
                drawableDictionary = new MetaHash(0),
                physicsDictionary = new MetaHash(Hash(def.PhysicsDict)),
                assetType = def.IsFragment
                    ? rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT
                    : rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE,
                assetName = new MetaHash(JenkHash.GenHash(name)),
                extensions = new Array_StructurePointer(),
            };
            if (!string.IsNullOrWhiteSpace(def.TextureDict)) JenkIndex.Ensure(def.TextureDict.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(def.PhysicsDict)) JenkIndex.Ensure(def.PhysicsDict.Trim().ToLowerInvariant());

            var arch = new Archetype();
            arch.Init(null, ref cbad);
            return arch;
        }

        public static YtypFile BuildYtyp(string ytypName, IEnumerable<ArchetypeDef> defs)
        {
            var list = defs?.Where(d => d != null && !string.IsNullOrWhiteSpace(d.Name)).ToList()
                       ?? new List<ArchetypeDef>();
            if (list.Count == 0) throw new Exception("No archetypes to write.");

            ytypName = LightProxyBuilder.Sanitised(
                string.IsNullOrWhiteSpace(ytypName) ? "custom_props" : ytypName);
            JenkIndex.Ensure(ytypName);

            var ytyp = new YtypFile
            {
                Name = ytypName + ".ytyp",
                NameHash = JenkHash.GenHash(ytypName),
                AllArchetypes = list.Select(Build).ToArray(),
            };
            ytyp._CMapTypes.name = new MetaHash(ytyp.NameHash);
            return ytyp;
        }

        public static void Save(string path, string ytypName, IEnumerable<ArchetypeDef> defs)
        {
            var ytyp = BuildYtyp(ytypName, defs);
            File.WriteAllBytes(path, ytyp.Save());
        }

        public static int SelfTest(string unused)
        {
            try
            {
                var pt = new LightAttributes
                {
                    Type = LightType.Point,
                    Position = new Vector3(0, 0, 3),
                    Falloff = 5.0f,
                };
                var spot = new LightAttributes
                {
                    Type = LightType.Spot,
                    Position = new Vector3(0, 0, 3),
                    Direction = new Vector3(0, 0, -1),
                    Falloff = 10.0f,
                    ConeOuterAngle = 30.0f,
                };

                var b1 = LightBounds(pt);
                if (Math.Abs(b1.Minimum.Z - (-2.0f)) > 0.001f || Math.Abs(b1.Maximum.Z - 8.0f) > 0.001f)
                    throw new Exception($"point bounds wrong: {b1.Minimum} .. {b1.Maximum}");

                var b2 = LightBounds(spot);
                float wantR = (float)Math.Tan(MathUtil.DegreesToRadians(30.0f)) * 10.0f;
                if (Math.Abs(b2.Minimum.Z - (-7.0f)) > 0.001f || Math.Abs(b2.Maximum.Z - 3.0f) > 0.001f)
                    throw new Exception($"spot Z wrong: {b2.Minimum.Z} .. {b2.Maximum.Z}");
                if (Math.Abs(b2.Maximum.X - wantR) > 0.001f)
                    throw new Exception($"spot radius wrong: {b2.Maximum.X} != {wantR}");
                Console.WriteLine($"point box {b1.Minimum} .. {b1.Maximum}");
                Console.WriteLine($"spot  box {b2.Minimum} .. {b2.Maximum}  (rim r={wantR:0.000})");

                var def = FromLightProp("rle_test_proxy", new[] { pt, spot });
                Console.WriteLine($"archetype bb {def.BbMin} .. {def.BbMax}  bs {def.BsCentre} r={def.BsRadius:0.000}  lod={def.LodDist}");
                if (def.LightCount != 2) throw new Exception("light count not recorded");

                var path = Path.Combine(Path.GetTempPath(), "rle_archtest.ytyp");
                Save(path, "rle_test", new[] { def });
                var bytes = File.ReadAllBytes(path);
                Console.WriteLine($"wrote {bytes.Length} bytes to {path}");

                var rt = new YtypFile();
                rt.Load(bytes);
                var archs = rt.AllArchetypes;
                if (archs == null || archs.Length != 1) throw new Exception($"read back {archs?.Length ?? 0} archetypes");
                var a = archs[0];
                if (a.Hash != JenkHash.GenHash("rle_test_proxy"))
                    throw new Exception($"name hash mismatch: {a.Hash}");
                if ((a.BBMin - def.BbMin).Length() > 0.001f || (a.BBMax - def.BbMax).Length() > 0.001f)
                    throw new Exception($"bounds changed: {a.BBMin} .. {a.BBMax}");
                if (Math.Abs(a.BSRadius - def.BsRadius) > 0.001f) throw new Exception("bs radius changed");
                if (Math.Abs(a.LodDist - def.LodDist) > 0.001f) throw new Exception("lod dist changed");
                if (a._BaseArchetypeDef.assetType != rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE)
                    throw new Exception("asset type changed");

                Console.WriteLine($"read back {a.Name}: bb {a.BBMin} .. {a.BBMax}, bs r={a.BSRadius:0.000}, lod={a.LodDist}");
                Console.WriteLine("ARCHETYPE round trip PASSED");
                File.Delete(path);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ARCHETYPE round trip FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}

