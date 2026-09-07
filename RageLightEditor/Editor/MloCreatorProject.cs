using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class MloCreatorProject
    {
        public const string Extension = ".mloproj";
        public const int Version = 1;

        public sealed class V3 { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
        public sealed class V4 { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } public float W { get; set; } }
        public sealed class RoomDto
        {
            public string Name { get; set; } public V3 Min { get; set; } public V3 Max { get; set; }
            public float Blend { get; set; } public string Timecycle { get; set; } public string SecondaryTimecycle { get; set; }
            public uint Flags { get; set; } public int FloorId { get; set; } public int ExteriorVisibilityDepth { get; set; }
        }
        public sealed class PortalDto
        {
            public int RoomFrom { get; set; } public int RoomTo { get; set; } public List<V3> Corners { get; set; }
            public uint Flags { get; set; } public uint MirrorPriority { get; set; } public uint Opacity { get; set; } public uint AudioOcclusion { get; set; }
            public List<int> Attached { get; set; }
        }
        public sealed class EntityDto
        {
            public string ArchetypeName { get; set; } public V3 Position { get; set; } public V4 Rotation { get; set; } public V3 Scale { get; set; }
            public uint Flags { get; set; } public float LodDist { get; set; } public bool Include { get; set; }
            public int RoomOverride { get; set; } public string EntitySet { get; set; } public string SourceFile { get; set; }
        }
        public sealed class Dto
        {
            public int Version { get; set; } = MloCreatorProject.Version;
            public string App { get; set; } = "RAGE Tools MLO Creator";
            public string Name { get; set; } public string TextureDictionary { get; set; } public string PhysicsDictionary { get; set; } public string ShellName { get; set; }
            public float LodDist { get; set; } public float HdTextureDist { get; set; } public uint Flags { get; set; } public uint MloFlags { get; set; }
            public bool AssetLess { get; set; } public bool IncludePropArchetypes { get; set; }
            public V3 BBMin { get; set; } public V3 BBMax { get; set; }
            public bool BBoxManual { get; set; }
            public string ShellFile { get; set; }
            public List<string> SceneFiles { get; set; } = new List<string>();
            public string ImportedYtyp { get; set; }
            public List<RoomDto> Rooms { get; set; } = new List<RoomDto>();
            public List<PortalDto> Portals { get; set; } = new List<PortalDto>();
            public List<EntityDto> Entities { get; set; } = new List<EntityDto>();
            public List<string> EntitySets { get; set; } = new List<string>();
            public string YmapName { get; set; } public V3 YmapPosition { get; set; } public float YmapHeadingDeg { get; set; }
            public int YmapGroupId { get; set; } public int YmapFloorId { get; set; } public List<string> YmapDefaultSets { get; set; } = new List<string>();
            public string LastSavedPath { get; set; } public string LastYmapPath { get; set; }
        }

        private static V3 P(Vector3 v) => new V3 { X = v.X, Y = v.Y, Z = v.Z };
        private static V4 P(Quaternion q) => new V4 { X = q.X, Y = q.Y, Z = q.Z, W = q.W };
        private static Vector3 U(V3 v) => v == null ? Vector3.Zero : new Vector3(v.X, v.Y, v.Z);
        private static Quaternion U(V4 q) => q == null ? Quaternion.Identity : new Quaternion(q.X, q.Y, q.Z, q.W);

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static Dto ToDto(MloCreatorSession s, Scene scene)
        {
            var d = new Dto
            {
                Name = s.Name, TextureDictionary = s.TextureDictionary, PhysicsDictionary = s.PhysicsDictionary, ShellName = s.ShellName,
                LodDist = s.LodDist, HdTextureDist = s.HdTextureDist, Flags = s.Flags, MloFlags = s.MloFlags,
                AssetLess = s.AssetLess, IncludePropArchetypes = s.IncludePropArchetypes,
                BBMin = P(s.BBMin), BBMax = P(s.BBMax), BBoxManual = s.BBoxManual, ShellFile = s.ShellFile?.Path,
                YmapName = s.YmapName, YmapPosition = P(s.YmapPosition), YmapHeadingDeg = s.YmapHeadingDeg,
                YmapGroupId = s.YmapGroupId, YmapFloorId = s.YmapFloorId, YmapDefaultSets = new List<string>(s.YmapDefaultSets),
                LastSavedPath = s.LastSavedPath, LastYmapPath = s.LastYmapPath,
                EntitySets = new List<string>(s.EntitySets),
            };
            if (scene != null)
            {
                foreach (var f in scene.Files) if (f != null && !f.FromMlo && !string.IsNullOrEmpty(f.Path)) d.SceneFiles.Add(f.Path);
                d.ImportedYtyp = scene.MloInfo?.Ytyps?.FirstOrDefault(y => !string.IsNullOrEmpty(y.Path))?.Path;
            }
            foreach (var r in s.Rooms)
                d.Rooms.Add(new RoomDto
                {
                    Name = r.Name, Min = P(r.Min), Max = P(r.Max), Blend = r.Blend, Timecycle = r.Timecycle, SecondaryTimecycle = r.SecondaryTimecycle,
                    Flags = r.Flags, FloorId = r.FloorId, ExteriorVisibilityDepth = r.ExteriorVisibilityDepth,
                });
            foreach (var p in s.Portals)
                d.Portals.Add(new PortalDto
                {
                    RoomFrom = p.RoomFrom, RoomTo = p.RoomTo, Corners = p.Corners.Select(P).ToList(), Flags = p.Flags,
                    MirrorPriority = p.MirrorPriority, Opacity = p.Opacity, AudioOcclusion = p.AudioOcclusion, Attached = new List<int>(p.Attached),
                });
            foreach (var e in s.Entities)
                d.Entities.Add(new EntityDto
                {
                    ArchetypeName = e.ArchetypeName, Position = P(e.Position), Rotation = P(e.Rotation), Scale = P(e.Scale), Flags = e.Flags,
                    LodDist = e.LodDist, Include = e.Include, RoomOverride = e.RoomOverride, EntitySet = e.EntitySet, SourceFile = e.SourceFile?.Path,
                });
            return d;
        }

        public static MloCreatorSession FromDto(Dto d, Scene scene)
        {
            var s = new MloCreatorSession();
            s.Rooms.Clear();
            s.Name = d.Name ?? "my_interior"; s.TextureDictionary = d.TextureDictionary ?? ""; s.PhysicsDictionary = d.PhysicsDictionary ?? ""; s.ShellName = d.ShellName ?? "";
            s.LodDist = d.LodDist; s.HdTextureDist = d.HdTextureDist; s.Flags = d.Flags; s.MloFlags = d.MloFlags;
            s.AssetLess = d.AssetLess; s.IncludePropArchetypes = d.IncludePropArchetypes;
            s.BBMin = U(d.BBMin); s.BBMax = U(d.BBMax); s.BBoxManual = d.BBoxManual;
            s.YmapName = d.YmapName ?? ""; s.YmapPosition = U(d.YmapPosition); s.YmapHeadingDeg = d.YmapHeadingDeg;
            s.YmapGroupId = d.YmapGroupId; s.YmapFloorId = d.YmapFloorId;
            if (d.YmapDefaultSets != null) s.YmapDefaultSets.AddRange(d.YmapDefaultSets);
            s.LastSavedPath = d.LastSavedPath; s.LastYmapPath = d.LastYmapPath;
            if (d.EntitySets != null) s.EntitySets.AddRange(d.EntitySets);
            LoadedFile ByPath(string path)
            {
                if (scene == null || string.IsNullOrEmpty(path)) return null;
                return scene.Files.FirstOrDefault(f => f != null && !string.IsNullOrEmpty(f.Path) &&
                    (string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(f.Path), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)));
            }
            s.ShellFile = ByPath(d.ShellFile);
            foreach (var r in d.Rooms ?? new List<RoomDto>())
                s.Rooms.Add(new MloCreatorRoom
                {
                    Name = r.Name ?? "", Min = U(r.Min), Max = U(r.Max), Blend = r.Blend, Timecycle = r.Timecycle ?? "", SecondaryTimecycle = r.SecondaryTimecycle ?? "",
                    Flags = r.Flags, FloorId = r.FloorId, ExteriorVisibilityDepth = r.ExteriorVisibilityDepth,
                });
            if (s.Rooms.Count == 0) s.Rooms.Add(new MloCreatorRoom { Name = "limbo", Min = s.BBMin, Max = s.BBMax });
            foreach (var p in d.Portals ?? new List<PortalDto>())
            {
                var cp = new MloCreatorPortal
                {
                    RoomFrom = p.RoomFrom, RoomTo = p.RoomTo, Flags = p.Flags, MirrorPriority = p.MirrorPriority, Opacity = p.Opacity, AudioOcclusion = p.AudioOcclusion,
                    Corners = (p.Corners ?? new List<V3>()).Select(U).ToArray(),
                };
                if (cp.Corners.Length < 3) cp.Corners = new Vector3[4];
                if (p.Attached != null) cp.Attached.AddRange(p.Attached);
                s.Portals.Add(cp);
            }
            foreach (var e in d.Entities ?? new List<EntityDto>())
                s.Entities.Add(new MloCreatorEntity
                {
                    ArchetypeName = e.ArchetypeName ?? "", Position = U(e.Position), Rotation = U(e.Rotation), Scale = e.Scale == null ? Vector3.One : U(e.Scale),
                    Flags = e.Flags, LodDist = e.LodDist > 0 ? e.LodDist : 100.0f, Include = e.Include, RoomOverride = e.RoomOverride,
                    EntitySet = string.IsNullOrEmpty(e.EntitySet) ? null : e.EntitySet, SourceFile = ByPath(e.SourceFile),
                });
            s.AutoAssignRooms();
            return s;
        }

        public static void Save(string path, MloCreatorSession s, Scene scene)
        {
            var json = JsonSerializer.Serialize(ToDto(s, scene), Options);
            File.WriteAllText(path, json);
        }

        public static Dto Read(string path) => JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Options);

        public static MloCreatorSession Load(string path, Scene scene) => FromDto(Read(path), scene);

        public static string DefaultPath(MloCreatorSession s, Scene scene)
        {
            string stem = string.IsNullOrWhiteSpace(s?.Name) ? "interior" : s.Name.Trim().ToLowerInvariant();
            string dir = null;
            if (!string.IsNullOrEmpty(s?.LastSavedPath)) dir = Path.GetDirectoryName(s.LastSavedPath);
            else if (!string.IsNullOrEmpty(s?.ShellFile?.Path)) dir = Path.GetDirectoryName(s.ShellFile.Path);
            else if (!string.IsNullOrEmpty(scene?.FilePath)) dir = Path.GetDirectoryName(scene.FilePath);
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RAGE World");
            return Path.Combine(dir, stem + Extension);
        }
    }
}

