using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class MloCreatorRoom
    {
        public string Name = "room";
        public Vector3 Min, Max;
        public float Blend = 1.0f;
        public string Timecycle = "";
        public string SecondaryTimecycle = "";
        public uint Flags;
        public int FloorId;
        public int ExteriorVisibilityDepth = -1;

        public Vector3 Centre => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
        public bool IsValid => Max.X > Min.X && Max.Y > Min.Y && Max.Z > Min.Z;
        public float Volume => IsValid ? Size.X * Size.Y * Size.Z : 0.0f;
        public bool Contains(Vector3 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

        public MloCreatorRoom Clone() => (MloCreatorRoom)MemberwiseClone();
    }

    public class MloCreatorPortal
    {
        public int RoomFrom, RoomTo = 1;
        public Vector3[] Corners = new Vector3[4];
        public uint Flags;
        public uint MirrorPriority, Opacity, AudioOcclusion;
        public List<int> Attached = new List<int>();

        public Vector3 Centre
        {
            get
            {
                var c = Vector3.Zero;
                foreach (var v in Corners) c += v;
                return Corners.Length > 0 ? c / Corners.Length : c;
            }
        }
        public Vector3 Normal
        {
            get
            {
                if (Corners.Length < 3) return Vector3.UnitY;
                var n = Vector3.Cross(Corners[1] - Corners[0], Corners[2] - Corners[0]);
                return n.LengthSquared() > 1e-8f ? Vector3.Normalize(n) : Vector3.UnitY;
            }
        }
        public MloCreatorPortal Clone()
        {
            var p = (MloCreatorPortal)MemberwiseClone();
            p.Corners = (Vector3[])Corners.Clone();
            p.Attached = new List<int>(Attached);
            return p;
        }
    }

    public class MloCreatorEntity
    {
        public string ArchetypeName = "";
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Scale = Vector3.One;
        public uint Flags = 0;
        public float LodDist = 100.0f;
        public bool Include = true;
        public int AutoRoom = -1;
        public int RoomOverride = -1;
        public bool IsShell_V33;
        public string EntitySet;
        public LoadedFile SourceFile;
        public MloEntityInfo SourceInfo;
        public bool FromImportedMlo => SourceInfo != null;
        public int Room => RoomOverride >= 0 ? RoomOverride : Math.Max(AutoRoom, 0);
        public string Label => string.IsNullOrEmpty(ArchetypeName) ? "(unnamed)" : ArchetypeName;
        public MloCreatorEntity Clone() => (MloCreatorEntity)MemberwiseClone();
    }

    public partial class MloCreatorSession
    {
        public string Name = "my_interior";
        public string TextureDictionary = "";
        public string PhysicsDictionary = "";
        public float LodDist = 200.0f;
        public float HdTextureDist = 100.0f;
        public uint Flags = 0;
        public uint MloFlags = 0;
        public bool AssetLess;
        public Vector3 BBMin = new Vector3(-5, -5, -1), BBMax = new Vector3(5, 5, 4);
        public bool IncludePropArchetypes = true;
        public LoadedFile ShellFile;
        public string ShellName = "";

        public readonly List<MloCreatorRoom> Rooms = new List<MloCreatorRoom>();
        public readonly List<MloCreatorPortal> Portals = new List<MloCreatorPortal>();
        public readonly List<MloCreatorEntity> Entities = new List<MloCreatorEntity>();
        public readonly List<string> EntitySets = new List<string>();

        public MloArchetype SourceArchetype;
        public string LastSavedPath;
        public string LastYmapPath;

        public string YmapName = "";
        public Vector3 YmapPosition;
        public float YmapHeadingDeg;
        public int YmapGroupId, YmapFloorId;
        public readonly List<string> YmapDefaultSets = new List<string>();

        public readonly MloCreatorHistory History = new MloCreatorHistory();

        public MloCreatorSession()
        {
            Rooms.Add(new MloCreatorRoom { Name = "limbo", Min = BBMin, Max = BBMax, Blend = 1.0f, ExteriorVisibilityDepth = -1 });
            History.Attach(this);
        }

        public void PushUndo(string name = "Edit", string mergeKey = null) => History.Push(name, mergeKey);
        public bool CanUndo => History.CanUndo;
        public void Undo() => History.Undo();

        public static MloCreatorSession FromScene(Scene scene)
        {
            var s = new MloCreatorSession();
            if (scene == null) return s;

            var mlo = scene.MloInfo?.Ytyps?.SelectMany(y => y.Archetypes).OfType<MloArchetype>().FirstOrDefault();
            if (mlo != null) { s.SeedFromArchetype(mlo); s.NameFromImport_R1(scene, mlo); }

            LoadedFile shell = null; float best = -1;
            foreach (var f in scene.Files)
            {
                if (f.FromMlo || f.Model == null) continue;
                var b = ModelBounds(f);
                float sz = (b.Maximum - b.Minimum).LengthSquared();
                if (sz > best) { best = sz; shell = f; }
            }
            if (mlo == null && shell != null)
            {
                s.ShellFile = shell;
                s.Name = Path.GetFileNameWithoutExtension(shell.Name).ToLowerInvariant();
                s.ShellName = s.Name;
                s.TextureDictionary = s.Name;
                var d = Drawable(shell);
                if ((d as Drawable)?.Bound != null) s.PhysicsDictionary = s.Name;
                else if (SiblingYbn_V31(shell?.Path) != null) s.PhysicsDictionary = s.Name;
            }

            if (scene.MloInfo != null)
            {
                foreach (var e in scene.MloInfo.Entities)
                {
                    if (e == null) continue;
                    var ent = new MloCreatorEntity
                    {
                        ArchetypeName = e.ArchetypeName ?? "",
                        Position = e.Position,
                        Rotation = e.Rotation,
                        Scale = e.Scale,
                        Flags = e.Flags,
                        SourceInfo = e,
                        EntitySet = e.EntitySet,
                        RoomOverride = mlo != null && e.RoomIndex >= 0 ? e.RoomIndex : -1,
                    };
                    s.Entities.Add(ent);
                }
            }
            foreach (var f in scene.Files)
            {
                if (f.FromMlo || f == shell) continue;
                s.AddEntityFromFile(f);
            }

            if (mlo?.rooms != null && scene.MloInfo != null)
            {
                var order = new List<MCEntityDef>();
                if (mlo.entities != null) order.AddRange(mlo.entities);
                for (int ri = 0; ri < mlo.rooms.Length; ri++)
                {
                    var att = mlo.rooms[ri]?.AttachedObjects; if (att == null) continue;
                    foreach (var idx in att)
                    {
                        if (idx >= order.Count) continue;
                        var def = order[(int)idx]._Data;
                        var hit = s.Entities.FirstOrDefault(x => x.SourceInfo != null && x.EntitySet == null &&
                            x.RoomOverride < 0 && x.SourceInfo.ArchetypeHash == def.archetypeName.Hash &&
                            (x.Position - def.position).LengthSquared() < 1e-4f);
                        if (hit != null) hit.RoomOverride = ri;
                    }
                }
                if (mlo.portals != null)
                {
                    for (int pi = 0; pi < mlo.portals.Length && pi < s.Portals.Count; pi++)
                    {
                        var att = mlo.portals[pi]?.AttachedObjects; if (att == null) continue;
                        foreach (var idx in att)
                        {
                            if (idx >= order.Count) continue;
                            var def = order[(int)idx]._Data;
                            int ei = s.Entities.FindIndex(x => x.SourceInfo != null && x.EntitySet == null &&
                                x.SourceInfo.ArchetypeHash == def.archetypeName.Hash &&
                                (x.Position - def.position).LengthSquared() < 1e-4f);
                            if (ei >= 0 && !s.Portals[pi].Attached.Contains(ei)) s.Portals[pi].Attached.Add(ei);
                        }
                    }
                }
            }

            if (mlo == null || s.BBMax.X <= s.BBMin.X || s.BBMax.Y <= s.BBMin.Y || s.BBMax.Z <= s.BBMin.Z) s.FitBoundsToScene(scene);
            s.AutoAssignRooms();
            return s;
        }

        public void SeedFromArchetype(MloArchetype mlo)
        {
            if (mlo == null) return;
            SourceArchetype = mlo;
            Name = mlo.Name ?? Name;
            Name = SafeInteriorName_R1(Name, mlo);
            var bd = mlo._BaseArchetypeDef;
            TextureDictionary = HashText(bd.textureDictionary);
            PhysicsDictionary = HashText(bd.physicsDictionary);
            LodDist = bd.lodDist; HdTextureDist = bd.hdTextureDist;
            Flags = bd.flags;
            MloFlags = mlo._MloArchetypeDef._MloArchetypeDef.mloFlags;
            AssetLess = bd.assetType == rage__fwArchetypeDef__eAssetType.ASSET_TYPE_ASSETLESS;
            BBMin = bd.bbMin; BBMax = bd.bbMax;
            BBoxManual = BBMax.X > BBMin.X && BBMax.Y > BBMin.Y && BBMax.Z > BBMin.Z;
            ShellName = mlo.Name ?? "";
            Rooms.Clear();
            if (mlo.rooms != null)
            {
                foreach (var r in mlo.rooms)
                {
                    if (r == null) continue;
                    Rooms.Add(new MloCreatorRoom
                    {
                        Name = r.RoomName ?? "", Min = r._Data.bbMin, Max = r._Data.bbMax, Blend = r._Data.blend,
                        Timecycle = HashText(r._Data.timecycleName), SecondaryTimecycle = HashText(r._Data.secondaryTimecycleName),
                        Flags = r._Data.flags, FloorId = r._Data.floorId, ExteriorVisibilityDepth = r._Data.exteriorVisibiltyDepth,
                    });
                }
            }
            if (Rooms.Count == 0) Rooms.Add(new MloCreatorRoom { Name = "limbo", Min = BBMin, Max = BBMax });
            Portals.Clear();
            if (mlo.portals != null)
            {
                foreach (var p in mlo.portals)
                {
                    if (p == null) continue;
                    var cp = new MloCreatorPortal
                    {
                        RoomFrom = (int)p._Data.roomFrom, RoomTo = (int)p._Data.roomTo, Flags = p._Data.flags,
                        MirrorPriority = p._Data.mirrorPriority, Opacity = p._Data.opacity, AudioOcclusion = p._Data.audioOcclusion,
                    };
                    var n = p.Corners?.Length ?? 0;
                    cp.Corners = new Vector3[Math.Max(n, 3)];
                    for (int i = 0; i < n; i++) cp.Corners[i] = p.Corners[i].XYZ();
                    Portals.Add(cp);
                }
            }
            EntitySets.Clear();
            if (mlo.entitySets != null)
                foreach (var es in mlo.entitySets)
                    if (es != null) EntitySets.Add(HashText(es._Data.name));
            NormaliseSeededBoxes_P2();
        }

        public void AddEntityFromFile(LoadedFile f)
        {
            if (f == null) return;
            var name = Path.GetFileNameWithoutExtension(f.Name ?? "").ToLowerInvariant();
            void Add(Matrix m)
            {
                m.Decompose(out var sc, out var rot, out var pos);
                Entities.Add(new MloCreatorEntity
                {
                    ArchetypeName = name, Position = pos, Rotation = rot, Scale = sc, SourceFile = f,
                });
            }
            Add(f.HasPlacement ? f.Placement : Matrix.Identity);
            foreach (var m in f.ExtraPlacements) Add(m);
        }

        public void FitBoundsToScene(Scene scene)
        {
            BoundingBox b;
            if (ShellFile?.Model != null) b = ModelBounds(ShellFile);
            else if (TryGetRoomUnionBounds_P2(out var roomUnion)) b = roomUnion;
            else
            {
                var sb = scene?.GetSceneBounds();
                if (sb == null) return;
                b = sb.Value;
            }
            if (b.Maximum.X <= b.Minimum.X) return;
            BBMin = b.Minimum; BBMax = b.Maximum;
            if (Rooms.Count > 0 && !LimboAuthored_P2) { Rooms[0].Min = BBMin; Rooms[0].Max = BBMax; }
        }

        public MloCreatorRoom AddRoom(string name, Vector3 min, Vector3 max)
        {
            var r = new MloCreatorRoom { Name = string.IsNullOrWhiteSpace(name) ? "room_" + Rooms.Count : name.Trim(), Min = Vector3.Min(min, max), Max = Vector3.Max(min, max) };
            Rooms.Add(r);
            return r;
        }

        public MloCreatorRoom AddRoomAround(IEnumerable<LoadedFile> files, string name, float pad = 0.05f)
        {
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue); bool any = false;
            foreach (var f in files)
            {
                if (f?.Model == null) continue;
                var b = ModelBounds(f);
                if (b.Maximum.X <= b.Minimum.X) continue;
                min = Vector3.Min(min, b.Minimum); max = Vector3.Max(max, b.Maximum); any = true;
            }
            if (!any) return null;
            return AddRoom(name, min - new Vector3(pad), max + new Vector3(pad));
        }

        public void RemoveRoom(int index)
        {
            if (index <= 0 || index >= Rooms.Count) return;
            Rooms.RemoveAt(index);
            Portals.RemoveAll(p => p.RoomFrom == index || p.RoomTo == index);
            foreach (var p in Portals)
            {
                if (p.RoomFrom > index) p.RoomFrom--;
                if (p.RoomTo > index) p.RoomTo--;
            }
            foreach (var e in Entities)
            {
                if (e.RoomOverride == index) e.RoomOverride = -1;
                else if (e.RoomOverride > index) e.RoomOverride--;
            }
            AutoAssignRooms();
        }

        public MloCreatorPortal AddPortal(int from, int to, Vector3[] corners)
        {
            var p = new MloCreatorPortal { RoomFrom = from, RoomTo = to };
            if (corners != null && corners.Length >= 3) p.Corners = (Vector3[])corners.Clone();
            else
            {
                var c = (BBMin + BBMax) * 0.5f;
                p.Corners = new[]
                {
                    c + new Vector3(-0.6f, 0, -1.0f), c + new Vector3(-0.6f, 0, 1.2f),
                    c + new Vector3(0.6f, 0, 1.2f), c + new Vector3(0.6f, 0, -1.0f),
                };
            }
            Portals.Add(p);
            return p;
        }

        public static Vector3[] RectangleFromPoints(Vector3 a, Vector3 b, Vector3 c)
        {
            var d = a + (c - b);
            return new[] { a, d, c, b };
        }

        public static Vector3[] RectangleAt(Vector3 centre, Vector3 along, float halfW, float halfH)
        {
            if (along.LengthSquared() < 1e-8f) along = Vector3.UnitX;
            along = Vector3.Normalize(new Vector3(along.X, along.Y, 0));
            var up = Vector3.UnitZ;
            return new[]
            {
                centre - along * halfW - up * halfH, centre - along * halfW + up * halfH,
                centre + along * halfW + up * halfH, centre + along * halfW - up * halfH,
            };
        }

        public void RemovePortal(int index)
        {
            if (index < 0 || index >= Portals.Count) return;
            Portals.RemoveAt(index);
        }

        public void RemoveEntity(int index)
        {
            if (index < 0 || index >= Entities.Count) return;
            Entities.RemoveAt(index);
            foreach (var p in Portals)
            {
                p.Attached.Remove(index);
                for (int i = 0; i < p.Attached.Count; i++) if (p.Attached[i] > index) p.Attached[i]--;
            }
        }

        public void AddEntitySet(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "set_" + EntitySets.Count : name.Trim().ToLowerInvariant();
            if (!EntitySets.Contains(name)) EntitySets.Add(name);
        }

        public void RemoveEntitySet(string name)
        {
            if (!EntitySets.Remove(name)) return;
            foreach (var e in Entities) if (e.EntitySet == name) e.EntitySet = null;
        }

        public void AutoAssignRooms()
        {
            foreach (var e in Entities)
            {
                int best = 0; float bestVol = float.MaxValue;
                for (int i = 1; i < Rooms.Count; i++)
                {
                    var r = Rooms[i];
                    if (!r.IsValid || !r.Contains(e.Position)) continue;
                    if (r.Volume < bestVol) { bestVol = r.Volume; best = i; }
                }
                e.AutoRoom = best;
                if (e.RoomOverride >= Rooms.Count) e.RoomOverride = -1;
            }
        }

        public int CountInRoom(int room) => Entities.Count(e => e.Include && e.Room == room);

        public sealed class State
        {
            public string Name, TextureDictionary, PhysicsDictionary, ShellName;
            public float LodDist, HdTextureDist;
            public uint Flags, MloFlags;
            public bool AssetLess, IncludePropArchetypes;
            public Vector3 BBMin, BBMax;
            public LoadedFile ShellFile;
            public List<MloCreatorRoom> Rooms;
            public List<MloCreatorPortal> Portals;
            public List<MloCreatorEntity> Entities;
            public List<string> EntitySets;
            public string YmapName; public Vector3 YmapPosition; public float YmapHeadingDeg; public int YmapGroupId, YmapFloorId;
            public List<string> YmapDefaultSets;
        }

        public State Capture() => new State
        {
            Name = Name, TextureDictionary = TextureDictionary, PhysicsDictionary = PhysicsDictionary, ShellName = ShellName,
            LodDist = LodDist, HdTextureDist = HdTextureDist, Flags = Flags, MloFlags = MloFlags,
            AssetLess = AssetLess, IncludePropArchetypes = IncludePropArchetypes, BBMin = BBMin, BBMax = BBMax, ShellFile = ShellFile,
            Rooms = Rooms.Select(r => r.Clone()).ToList(), Portals = Portals.Select(p => p.Clone()).ToList(),
            Entities = Entities.Select(e => e.Clone()).ToList(), EntitySets = new List<string>(EntitySets),
            YmapName = YmapName, YmapPosition = YmapPosition, YmapHeadingDeg = YmapHeadingDeg, YmapGroupId = YmapGroupId, YmapFloorId = YmapFloorId,
            YmapDefaultSets = new List<string>(YmapDefaultSets),
        };

        public void Restore(State st)
        {
            if (st == null) return;
            Name = st.Name; TextureDictionary = st.TextureDictionary; PhysicsDictionary = st.PhysicsDictionary; ShellName = st.ShellName;
            LodDist = st.LodDist; HdTextureDist = st.HdTextureDist; Flags = st.Flags; MloFlags = st.MloFlags;
            AssetLess = st.AssetLess; IncludePropArchetypes = st.IncludePropArchetypes; BBMin = st.BBMin; BBMax = st.BBMax; ShellFile = st.ShellFile;
            Rooms.Clear(); Rooms.AddRange(st.Rooms.Select(r => r.Clone()));
            Portals.Clear(); Portals.AddRange(st.Portals.Select(p => p.Clone()));
            Entities.Clear(); Entities.AddRange(st.Entities.Select(e => e.Clone()));
            EntitySets.Clear(); EntitySets.AddRange(st.EntitySets);
            YmapName = st.YmapName; YmapPosition = st.YmapPosition; YmapHeadingDeg = st.YmapHeadingDeg; YmapGroupId = st.YmapGroupId; YmapFloorId = st.YmapFloorId;
            YmapDefaultSets.Clear(); YmapDefaultSets.AddRange(st.YmapDefaultSets);
        }

        public static bool BoxFromPoints(IEnumerable<Vector3> points, out Vector3 min, out Vector3 max, out string note)
        {
            min = new Vector3(float.MaxValue); max = new Vector3(float.MinValue); note = ""; int n = 0;
            foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); n++; }
            if (n < 2) return false;
            if (max.Z - min.Z < 0.05f) { max.Z = min.Z + 2.8f; note = " (all points at one height: 2.8 m tall)"; }
            if (max.X - min.X < 0.05f) max.X = min.X + 0.05f;
            if (max.Y - min.Y < 0.05f) max.Y = min.Y + 0.05f;
            return true;
        }

        public int RoomAt(Vector3 p)
        {
            int best = 0; float bestVol = float.MaxValue;
            for (int i = 1; i < Rooms.Count; i++)
            {
                var r = Rooms[i];
                if (!r.IsValid || !r.Contains(p)) continue;
                if (r.Volume < bestVol) { bestVol = r.Volume; best = i; }
            }
            return best;
        }

        public bool OrientPortal(MloCreatorPortal p)
        {
            if (p == null || p.Corners == null || p.Corners.Length < 3) return false;
            var c = p.Centre; var n = p.Normal;
            float side = 0;
            bool fromReal = p.RoomFrom > 0 && p.RoomFrom < Rooms.Count && Rooms[p.RoomFrom].IsValid;
            bool toReal = p.RoomTo > 0 && p.RoomTo < Rooms.Count && Rooms[p.RoomTo].IsValid;
            if (toReal) side += Vector3.Dot(n, Rooms[p.RoomTo].Centre - c);
            if (fromReal) side -= Vector3.Dot(n, Rooms[p.RoomFrom].Centre - c);
            if (side >= 0) return false;
            Array.Reverse(p.Corners);
            return true;
        }

        public MloCreatorPortal AddPortalFromPoints(IList<Vector3> pts)
        {
            if (pts == null || pts.Count < 3) return null;
            var corners = pts.Count >= 4 ? MloBridge.OrderQuadLoop(pts.Take(4).ToArray()) : pts.ToArray();
            var p = AddPortal(0, 0, corners);
            var c = p.Centre; var n = p.Normal;
            p.RoomFrom = RoomAt(c - n * 0.3f);
            p.RoomTo = RoomAt(c + n * 0.3f);
            if (p.RoomFrom == p.RoomTo)
            {
                p.RoomTo = p.RoomFrom == 0 ? Math.Min(1, Rooms.Count - 1) : 0;
            }
            OrientPortal(p);
            return p;
        }

        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(Name)) problems.Add("The interior needs a name.");
            if (Rooms.Count == 0) problems.Add("There is no room 0 (limbo).");
            for (int i = 0; i < Rooms.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(Rooms[i].Name)) problems.Add($"Room {i} has no name.");
                if (!Rooms[i].IsValid) problems.Add($"Room {i} '{Rooms[i].Name}' has an empty box.");
            }
            for (int i = 0; i < Portals.Count; i++)
            {
                var p = Portals[i];
                if (p.RoomFrom < 0 || p.RoomFrom >= Rooms.Count) problems.Add($"Portal {i}: room from {p.RoomFrom} does not exist.");
                if (p.RoomTo < 0 || p.RoomTo >= Rooms.Count) problems.Add($"Portal {i}: room to {p.RoomTo} does not exist.");
                if (p.RoomFrom == p.RoomTo) problems.Add($"Portal {i}: joins room {p.RoomFrom} to itself.");
                if (p.Corners == null || p.Corners.Length < 3) problems.Add($"Portal {i}: fewer than three corners.");
            }
            if (BBMax.X <= BBMin.X || BBMax.Y <= BBMin.Y || BBMax.Z <= BBMin.Z) problems.Add("The archetype's bounding box is empty.");
            foreach (var w in CollisionWarnings_V31()) problems.Add(w);
            return problems;
        }

        public YtypFile BuildYtyp(string fileName = null)
        {
            var name = Name.Trim().ToLowerInvariant();
            fileName = string.IsNullOrWhiteSpace(fileName) ? name + ".ytyp" : Path.GetFileName(fileName);
            var ytyp = new YtypFile();
            ytyp.RpfFileEntry = new RpfResourceFileEntry { Name = fileName };
            ytyp.Name = fileName;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            ytyp.NameHash = JenkHash.GenHash(stem.ToLowerInvariant());
            JenkIndex.Ensure(fileName); JenkIndex.Ensure(stem.ToLowerInvariant());
            ytyp._CMapTypes.name = ytyp.NameHash;
            ytyp.AllArchetypes = Array.Empty<Archetype>();
            ytyp.Loaded = true;

            var mlo = new MloArchetype();
            var def = new CMloArchetypeDef();
            var bd = new CBaseArchetypeDef
            {
                name = MloEditor.NameToHash(name),
                assetName = MloEditor.NameToHash(name),
                assetType = AssetLess ? rage__fwArchetypeDef__eAssetType.ASSET_TYPE_ASSETLESS : rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE,
                textureDictionary = MloEditor.NameToHash(TextureDictionary),
                physicsDictionary = MloEditor.NameToHash(PhysicsDictionary),
                lodDist = LodDist, hdTextureDist = HdTextureDist, flags = Flags, specialAttribute = 0,
                bbMin = BBMin, bbMax = BBMax,
                bsCentre = (BBMin + BBMax) * 0.5f, bsRadius = (BBMax - BBMin).Length() * 0.5f,
            };
            def._BaseArchetypeDef = bd;
            var md = def._MloArchetypeDef; md.mloFlags = MloFlags; def._MloArchetypeDef = md;
            mlo.Init(ytyp, ref def);
            mlo._BaseArchetypeDef = bd;
            mlo.entities = Array.Empty<MCEntityDef>();
            mlo.rooms = Array.Empty<MCMloRoomDef>();
            mlo.portals = Array.Empty<MCMloPortalDef>();
            mlo.entitySets = Array.Empty<MCMloEntitySet>();
            mlo.timeCycleModifiers = Array.Empty<CMloTimeCycleModifier>();
            ytyp.AddArchetype(mlo);

            foreach (var r in Rooms)
            {
                var room = MloEditor.AddRoom(mlo, r.Name);
                room._Data.bbMin = r.Min; room._Data.bbMax = r.Max;
                room._Data.blend = r.Blend;
                room._Data.timecycleName = MloEditor.NameToHash(r.Timecycle);
                room._Data.secondaryTimecycleName = MloEditor.NameToHash(r.SecondaryTimecycle);
                room._Data.flags = r.Flags; room._Data.floorId = r.FloorId; room._Data.exteriorVisibiltyDepth = r.ExteriorVisibilityDepth;
                room.AttachedObjects = Array.Empty<uint>();
            }
            foreach (var p in Portals)
            {
                var portal = MloEditor.AddPortal(mlo, (uint)Math.Max(p.RoomFrom, 0), (uint)Math.Max(p.RoomTo, 0), p.Corners);
                portal._Data.flags = p.Flags; portal._Data.mirrorPriority = p.MirrorPriority;
                portal._Data.opacity = p.Opacity; portal._Data.audioOcclusion = p.AudioOcclusion;
                portal.AttachedObjects = Array.Empty<uint>();
            }
            foreach (var sn in EntitySets) MloEditor.AddEntitySet(mlo, sn);

            SyncShellEntity_V33();

            var roomEnts = new List<MCEntityDef>();
            var roomAtt = new List<uint>[Rooms.Count];
            for (int i = 0; i < roomAtt.Length; i++) roomAtt[i] = new List<uint>();
            var portalAtt = new List<uint>[Portals.Count];
            for (int i = 0; i < portalAtt.Length; i++) portalAtt[i] = new List<uint>();
            var setEnts = new Dictionary<string, (List<MCEntityDef>, List<uint>)>();
            foreach (var sn in EntitySets) setEnts[sn] = (new List<MCEntityDef>(), new List<uint>());

            for (int ei = 0; ei < Entities.Count; ei++)
            {
                var e = Entities[ei];
                if (!e.Include || string.IsNullOrWhiteSpace(e.ArchetypeName)) continue;
                var mce = MloEditor.NewEntity(mlo, e.ArchetypeName, e.Position, e.Rotation, e.Scale, e.LodDist);
                mce._Data.flags = e.Flags;
                int room = Math.Clamp(e.Room, 0, Rooms.Count - 1);
                if (!string.IsNullOrEmpty(e.EntitySet) && setEnts.TryGetValue(e.EntitySet, out var se))
                {
                    se.Item1.Add(mce); se.Item2.Add((uint)room);
                }
                else
                {
                    uint idx = (uint)roomEnts.Count;
                    roomEnts.Add(mce);
                    roomAtt[room].Add(idx);
                    for (int pi = 0; pi < Portals.Count; pi++)
                        if (Portals[pi].Attached.Contains(ei)) portalAtt[pi].Add(idx);
                }
            }
            mlo.entities = roomEnts.ToArray();
            for (int i = 0; i < mlo.entities.Length; i++) mlo.entities[i].Index = i;
            for (int i = 0; i < Rooms.Count; i++) mlo.rooms[i].AttachedObjects = roomAtt[i].ToArray();
            for (int i = 0; i < Portals.Count; i++) mlo.portals[i].AttachedObjects = portalAtt[i].ToArray();
            for (int i = 0; i < EntitySets.Count; i++)
            {
                var (ents, locs) = setEnts[EntitySets[i]];
                mlo.entitySets[i].Entities = ents.ToArray();
                mlo.entitySets[i].Locations = locs.ToArray();
            }
            mlo.UpdatePortalCounts();

            if (IncludePropArchetypes)
            {
                var done = new HashSet<string>();
                foreach (var e in Entities)
                {
                    if (!e.Include || e.SourceFile == null || e.SourceFile.ReadOnly || done.Contains(e.ArchetypeName)) continue;
                    if (string.Equals(e.ArchetypeName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    var d = Drawable(e.SourceFile);
                    if (d == null) continue;
                    done.Add(e.ArchetypeName);
                    var arch = ytyp.AddArchetype();
                    var hash = MloEditor.NameToHash(e.ArchetypeName);
                    var pd = arch._BaseArchetypeDef;
                    pd.name = hash; pd.assetName = hash;
                    pd.assetType = e.SourceFile.IsYft ? rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT : rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE;
                    pd.flags = 32; pd.specialAttribute = 0;
                    pd.bbMin = d.BoundingBoxMin; pd.bbMax = d.BoundingBoxMax;
                    pd.bsCentre = d.BoundingCenter; pd.bsRadius = d.BoundingSphereRadius;
                    pd.hdTextureDist = 60.0f; pd.lodDist = Math.Max(e.LodDist, 60.0f);
                    if (d.ShaderGroup?.TextureDictionary != null) pd.textureDictionary = hash;
                    if ((d as Drawable)?.Bound != null || SiblingYbn_V31(e.SourceFile?.Path) != null)
                        pd.physicsDictionary = hash;
                    arch.Init(ytyp, ref pd);
                    arch._BaseArchetypeDef = pd;
                }
            }

            ytyp.HasChanged = true;
            return ytyp;
        }

        public YtypFile SaveYtyp(string path)
        {
            var problems = Validate();
            if (problems.Count > 0) throw new InvalidOperationException(problems[0]);
            var ytyp = BuildYtyp(Path.GetFileName(path));
            var err = MloEditor.Validate(ytyp);
            if (!string.IsNullOrEmpty(err)) throw new InvalidOperationException(err);
            var data = ytyp.Save();
            File.WriteAllBytes(path, data);
            ytyp.FilePath = path;
            LastSavedPath = path;
            return ytyp;
        }

        public YmapFile BuildYmap(string ymapName, Vector3 position, Quaternion rotation, uint groupId, uint floorId,
                                  IEnumerable<string> defaultEntitySets, float lodDist = -1)
        {
            var name = Name.Trim().ToLowerInvariant();
            ymapName = string.IsNullOrWhiteSpace(ymapName) ? name + "_placement" : Path.GetFileNameWithoutExtension(ymapName).ToLowerInvariant();
            var ymap = new YmapFile();
            ymap.Name = ymapName + ".ymap";
            ymap.RpfFileEntry = new RpfResourceFileEntry { Name = ymap.Name };
            JenkIndex.Ensure(ymapName);

            var q = rotation; if (q.LengthSquared() < 1e-6f) q = Quaternion.Identity; else q.Normalize();
            var cent = new CEntityDef
            {
                archetypeName = MloEditor.NameToHash(name),
                flags = 1572864,
                guid = 1,
                position = position,
                rotation = new Vector4(q.X, q.Y, q.Z, q.W),
                scaleXY = 1.0f, scaleZ = 1.0f,
                parentIndex = -1,
                lodDist = lodDist > 0 ? lodDist : LodDist,
                childLodDist = 0,
                lodLevel = rage__eLodType.LODTYPES_DEPTH_ORPHANHD,
                numChildren = 0,
                priorityLevel = rage__ePriorityLevel.PRI_REQUIRED,
                ambientOcclusionMultiplier = 255,
                artificialAmbientOcclusion = 255,
                tintValue = 0,
            };
            var mdef = new CMloInstanceDef
            {
                CEntityDef = cent, groupId = groupId, floorId = floorId,
                numExitPortals = (uint)Portals.Count(p => p.RoomFrom == 0 || p.RoomTo == 0),
                MLOInstflags = 0,
            };
            var ent = new YmapEntityDef(ymap, 0, ref mdef);
            ent.MloInstance.defaultEntitySets = (defaultEntitySets ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => MloEditor.NameToHash(s)).ToArray();
            ymap.AllEntities = new[] { ent };
            ymap.RootEntities = new[] { ent };
            ymap.MloEntities = new[] { ent };

            var corners = new Vector3[8];
            int k = 0;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                corners[k++] = position + q.Multiply(new Vector3(x == 0 ? BBMin.X : BBMax.X, y == 0 ? BBMin.Y : BBMax.Y, z == 0 ? BBMin.Z : BBMax.Z));
            var emin = new Vector3(float.MaxValue); var emax = new Vector3(float.MinValue);
            foreach (var c in corners) { emin = Vector3.Min(emin, c); emax = Vector3.Max(emax, c); }
            float ld = cent.lodDist;
            ymap._CMapData.name = JenkHash.GenHash(ymapName);
            ymap._CMapData.parent = 0;
            ymap._CMapData.flags = 0;
            ymap._CMapData.contentFlags = 1 | 8;
            ymap._CMapData.entitiesExtentsMin = emin; ymap._CMapData.entitiesExtentsMax = emax;
            ymap._CMapData.streamingExtentsMin = emin - new Vector3(ld); ymap._CMapData.streamingExtentsMax = emax + new Vector3(ld);
            ymap.HasChanged = true;
            return ymap;
        }

        public YmapFile SaveYmap(string path, Vector3 position, Quaternion rotation, uint groupId, uint floorId,
                                 IEnumerable<string> defaultEntitySets)
        {
            var ymap = BuildYmap(Path.GetFileNameWithoutExtension(path), position, rotation, groupId, floorId, defaultEntitySets);
            File.WriteAllBytes(path, ymap.Save());
            ymap.FilePath = path;
            LastYmapPath = path;
            return ymap;
        }

        public static string HashText(MetaHash h)
        {
            if (h.Hash == 0) return "";
            var str = h.ToString();
            if (string.IsNullOrEmpty(str) || (uint.TryParse(str, out var v) && v == h.Hash)) return $"0x{h.Hash:X8}";
            return str;
        }

        public static DrawableBase Drawable(LoadedFile f) =>
            f == null ? null : (f.Ydr?.Drawable ?? (DrawableBase)f.Yft?.Fragment?.Drawable ?? f.Drawable);

        public static BoundingBox ModelBounds(LoadedFile f)
        {
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            if (f?.Model != null)
                foreach (var m in f.Model.Meshes)
                {
                    var b = m.WorldBounds;
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    min = Vector3.Min(min, b.Minimum); max = Vector3.Max(max, b.Maximum);
                }
            return new BoundingBox(min, max);
        }

        public static bool RayHitScene(Scene scene, Ray ray, out Vector3 hit, out float dist)
        {
            hit = Vector3.Zero; dist = float.MaxValue; bool any = false;
            if (scene == null) return false;
            foreach (var mesh in scene.AllMeshes)
            {
                if (mesh == null || !mesh.Visible) continue;
                var b = mesh.WorldBounds;
                if (b.Maximum.X <= b.Minimum.X) continue;
                if (!ray.Intersects(ref b, out float bt) || bt > dist) continue;
                if (mesh.RayHit(ref ray, out float t) && t < dist) { dist = t; any = true; }
            }
            if (any) hit = ray.Position + ray.Direction * dist;
            return any;
        }
    }
}

