using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class YmapEntry
    {
        public string ArchetypeName = "";
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.Identity;
        public float ScaleXY = 1.0f;
        public float ScaleZ = 1.0f;
        public float LodDist = 200.0f;
        public bool Include = true;

        public string Label => $"{ArchetypeName}  ({Position.X:0.0}, {Position.Y:0.0}, {Position.Z:0.0})";
    }

    public static class YmapBuilder
    {
        public static YmapFile Build(string name, IEnumerable<YmapEntry> entries)
        {
            var list = entries?.Where(e => e != null && e.Include &&
                                           !string.IsNullOrWhiteSpace(e.ArchetypeName)).ToList()
                       ?? new List<YmapEntry>();
            if (list.Count == 0) throw new Exception("No props selected for the ymap.");

            name = string.IsNullOrWhiteSpace(name) ? "custom_props" : name.Trim().ToLowerInvariant();

            var ymap = new YmapFile();
            ymap.Name = name + ".ymap";

            var defs = new CEntityDef[list.Count];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                var q = e.Rotation;
                if (q.LengthSquared() < 1e-6f) q = Quaternion.Identity; else q.Normalize();
                q = Quaternion.Invert(q);

                defs[i] = new CEntityDef
                {
                    archetypeName = JenkHash.GenHash(e.ArchetypeName.ToLowerInvariant()),
                    flags = 32,
                    guid = (uint)(i + 1),
                    position = e.Position,
                    rotation = new Vector4(q.X, q.Y, q.Z, q.W),
                    scaleXY = e.ScaleXY,
                    scaleZ = e.ScaleZ,
                    parentIndex = -1,
                    lodDist = e.LodDist,
                    childLodDist = 0,
                    lodLevel = rage__eLodType.LODTYPES_DEPTH_ORPHANHD,
                    numChildren = 0,
                    priorityLevel = rage__ePriorityLevel.PRI_REQUIRED,
                    ambientOcclusionMultiplier = 255,
                    artificialAmbientOcclusion = 255,
                    tintValue = 0,
                };
                JenkIndex.Ensure(e.ArchetypeName.ToLowerInvariant());

                min = Vector3.Min(min, e.Position);
                max = Vector3.Max(max, e.Position);
            }

            var ents = new YmapEntityDef[defs.Length];
            for (int i = 0; i < defs.Length; i++) ents[i] = new YmapEntityDef(ymap, i, ref defs[i]);
            ymap.CEntityDefs = defs;
            ymap.AllEntities = ents;
            ymap.RootEntities = ents;

            var pad = new Vector3(50.0f);
            var cmap = new CMapData
            {
                name = JenkHash.GenHash(name),
                parent = 0,
                flags = 0,
                contentFlags = 65,
                streamingExtentsMin = min - pad,
                streamingExtentsMax = max + pad,
                entitiesExtentsMin = min - pad,
                entitiesExtentsMax = max + pad,
            };
            ymap.CMapData = cmap;
            JenkIndex.Ensure(name);

            return ymap;
        }

        public static void Save(string path, string name, IEnumerable<YmapEntry> entries)
        {
            var ymap = Build(name, entries);
            var data = ymap.Save();
            File.WriteAllBytes(path, data);
        }

        public static int SelfTest(string unused)
        {
            try
            {
                var src = new List<YmapEntry>
                {
                    new YmapEntry { ArchetypeName = "prop_rle_lightbox", Position = new Vector3(120.5f, -33.25f, 29.0f),
                                    Rotation = Quaternion.RotationAxis(Vector3.UnitZ, 0.75f) },
                    new YmapEntry { ArchetypeName = "prop_streetlight_01", Position = new Vector3(-40.0f, 811.5f, 3.5f) },
                    new YmapEntry { ArchetypeName = "skipped_one", Position = Vector3.Zero, Include = false },
                };

                var path = Path.Combine(Path.GetTempPath(), "rle_ymaptest.ymap");
                Save(path, "rle_test", src);
                var bytes = File.ReadAllBytes(path);
                Console.WriteLine($"wrote {bytes.Length} bytes to {path}");

                var rt = new YmapFile();
                RpfFile.LoadResourceFile(rt, bytes, 2);
                var defs = rt.CEntityDefs;
                if (defs == null || defs.Length != 2)
                    throw new Exception($"expected 2 entities, read back {defs?.Length ?? 0}");

                for (int i = 0; i < 2; i++)
                {
                    var want = src[i];
                    var got = defs[i];
                    var wantHash = JenkHash.GenHash(want.ArchetypeName);
                    if (got.archetypeName != wantHash)
                        throw new Exception($"entity {i}: archetype hash {got.archetypeName} != {wantHash}");

                    var dp = (got.position - want.Position).Length();
                    if (dp > 0.001f) throw new Exception($"entity {i}: position off by {dp}");

                    var q = Quaternion.Invert(new Quaternion(got.rotation.X, got.rotation.Y, got.rotation.Z, got.rotation.W));
                    var wq = want.Rotation; wq.Normalize();
                    var dot = Math.Abs(Quaternion.Dot(q, wq));
                    if (dot < 0.999f) throw new Exception($"entity {i}: rotation off (dot {dot})");

                    Console.WriteLine($"  ok {want.ArchetypeName}  pos {got.position}  rot dot {dot:0.0000}");
                }

                var ext = rt.CMapData.entitiesExtentsMax - rt.CMapData.entitiesExtentsMin;
                if (ext.X <= 0 || ext.Y <= 0 || ext.Z <= 0) throw new Exception("extents are degenerate");
                Console.WriteLine($"extents {rt.CMapData.entitiesExtentsMin} .. {rt.CMapData.entitiesExtentsMax}");
                Console.WriteLine("YMAP round trip PASSED");
                File.Delete(path);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("YMAP round trip FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}

