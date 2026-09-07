using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloEntityInfo
    {
        public string ArchetypeName;
        public uint ArchetypeHash;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public uint Flags;
        public string EntitySet;
        public int RoomIndex = -1;
        public bool Resolved;
    }

    public class MloProp
    {
        public uint Hash;
        public string Name = "";
        public string Path;
        public YdrFile Ydr;
        public YftFile Yft;
        public Skeleton Skeleton;
        public bool ReadOnly;

        public DrawableBase Drawable;

        public LightAttributes[] Lights;
        public bool HasLights => Lights != null && Lights.Length > 0;

        public RenderModel BaseModel;
        public RenderModel Model;

        public readonly List<Matrix> Placements = new List<Matrix>();

        public Matrix FirstWorld => Placements.Count > 0 ? Placements[0] : Matrix.Identity;
        public int Placed => Placements.Count;

        public int LightCount => Lights?.Length ?? 0;
    }

    public class MloYtypInfo
    {
        public string Name = "";
        public string Path = "";
        public readonly List<Archetype> Archetypes = new List<Archetype>();
    }

    public partial class MloImportResult
    {
        public readonly List<MloProp> Props = new List<MloProp>();

        public readonly List<MloYtypInfo> Ytyps = new List<MloYtypInfo>();

        public readonly List<string> RoomTimecycles = new List<string>();
        public readonly List<uint> RoomTimecycleHashes = new List<uint>();

        public string MloName = "";
        public int EntityCount;
        public int Placed;
        public int Missing;
        public int UniqueProps;
        public int LocalFiles;
        public int LocalArchetypes;
        public int LightCount;
        public int Oversized;
        public int Proxies;
        public int LodSkipped;
        public readonly List<string> FailedNames = new List<string>();
        public int VegetationSkipped;
        public int MloInstances;
        public readonly List<string> MloInstanceNames = new List<string>();
        public double Seconds;
        public BoundingBox FocusBounds;
        public bool HasFocus;
        public int ShellMeshes;
        public string ShellShaders = "";
        public int UntexturedMeshes;
        public readonly List<string> UntexturedNames = new List<string>();
        public readonly List<string> OversizedNames = new List<string>();
        public readonly List<string> MissingNames = new List<string>();
        public readonly List<MloEntityInfo> Entities = new List<MloEntityInfo>();
    }

    public class MloImporter
    {
        private readonly GameFileManager game;
        private readonly ModelRenderer modelRenderer;

        private readonly Dictionary<uint, MloProp> propCache = new Dictionary<uint, MloProp>();
        private readonly HashSet<uint> failedProps = new HashSet<uint>();

        public bool ImportPropLights = true;

        public bool ImportAllProps;
        private LocalAssetIndex local;

        public InteriorAmbientResolver InteriorAmbient_L2;

        public readonly InteriorSunResolver InteriorSun_N2 = new InteriorSunResolver();

        public Texture FindLocalTexture(uint nameHash) => local?.FindTexture(nameHash);

        public IEnumerable<(YtdFile Ytd, string Path)> LoadLocalTextureDicts()
        {
            if (local == null) yield break;
            foreach (var kv in local.TextureDictFiles.ToList())
            {
                YtdFile ytd = null;
                try { ytd = local.GetTextureDict(kv.Key); } catch { }
                if (ytd?.TextureDict != null) yield return (ytd, kv.Value);
            }
        }

        private string localRootKey;

        private void EnsureLocalIndex(string root, Action<string> progress)
        {
            var key = root + "|" + string.Join("|", ExtraFolders ?? Enumerable.Empty<string>());
            if (local != null && localRootKey == key)
            {
                progress?.Invoke("Reusing the local file index...");
                return;
            }
            local = new LocalAssetIndex();
            local.Build(root, progress, ExtraFolders);
            localRootKey = key;
        }

        public void InvalidateLocalIndex() => localRootKey = null;

        public float MaxPropSize = 300.0f;

        public List<string> ExtraFolders = new List<string>();

        public bool IncludeShell = true;

        private const uint FlagOnlyInReflections = 0x2000000;

        private static bool IsHdLod(rage__eLodType lod) =>
            lod == rage__eLodType.LODTYPES_DEPTH_HD || lod == rage__eLodType.LODTYPES_DEPTH_ORPHANHD;

        public bool ImportVegetation = false;

        private static readonly string[] VegetationWords =
        {
            "bush", "bushes", "tree", "trees", "grass", "weed", "weeds", "hedge", "hedges",
            "shrub", "plant", "plants", "fern", "palm", "leaf", "leaves", "foliage", "ivy",
            "flower", "flowers", "vine", "vines", "sapling", "conifer", "cactus", "reeds",
        };

        private static bool IsVegetation(string archetypeName)
        {
            if (string.IsNullOrEmpty(archetypeName)) return false;
            var n = archetypeName;
            foreach (var w in VegetationWords)
            {
                int i = n.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                while (i >= 0)
                {
                    bool startOk = i == 0 || !char.IsLetter(n[i - 1]);
                    int e = i + w.Length;
                    bool endOk = e >= n.Length || !char.IsLetter(n[e]);
                    if (startOk && endOk) return true;
                    i = n.IndexOf(w, i + 1, StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        public MloImporter(GameFileManager game, ModelRenderer modelRenderer)
        {
            this.game = game;
            this.modelRenderer = modelRenderer;
        }

        public IEnumerable<RenderModel> CachedModels => propCache.Values.Select(p => p.BaseModel).Where(m => m != null);

        public void ClearCache()
        {
            foreach (var p in propCache.Values) p.BaseModel?.Dispose();
            propCache.Clear();
            failedProps.Clear();
        }

        public MloImportResult Import(string ytypPath, out RenderModel placedModel,
            out List<LightAttributes> lights, Action<string> progress = null)
        {
            var sw = Stopwatch.StartNew();
            var result = new MloImportResult();
            placedModel = new RenderModel { Name = Path.GetFileNameWithoutExtension(ytypPath) };
            lights = new List<LightAttributes>();

            progress?.Invoke("Indexing local files...");
            EnsureLocalIndex(LocalAssetIndex.FindRoot(ytypPath), progress);
            modelRenderer.TextureFallback = (tex, txd) => local.FindTexture(tex) ?? game.FindTexture(tex, txd);

            var ytyp = new YtypFile();
            ytyp.Load(File.ReadAllBytes(ytypPath));
            if (ytyp.AllArchetypes == null || ytyp.AllArchetypes.Length == 0)
                throw new Exception("No archetypes in this .ytyp");

            var info = new MloYtypInfo { Name = Path.GetFileNameWithoutExtension(ytypPath), Path = ytypPath };
            info.Archetypes.AddRange(ytyp.AllArchetypes.Where(a => a != null));
            result.Ytyps.Add(info);

            var mlos = ytyp.AllArchetypes.OfType<MloArchetype>().ToList();
            if (mlos.Count == 0)
            {
                ImportLooseArchetypes(ytyp, result, placedModel, lights, progress);
                result.MloName = Path.GetFileNameWithoutExtension(ytypPath);
                result.Seconds = sw.Elapsed.TotalSeconds;
                return result;
            }

            foreach (var mlo in mlos)
            {
                PlaceMlo(mlo, Vector3.Zero, Quaternion.Identity, placedModel, result, progress);
            }

            RecomputeBounds(placedModel);
            foreach (var p in result.Props) RecomputeBounds(p.Model);
            CollectUntextured(result);

            result.UniqueProps = propCache.Count;
            result.LocalFiles = local?.FileCount ?? 0;
            result.LocalArchetypes = local?.ArchetypeCount ?? 0;
            result.LightCount = lights.Count;
            result.Seconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        private void PlaceMlo(MloArchetype mlo, Vector3 instPos, Quaternion instRot,
            RenderModel placedModel, MloImportResult result, Action<string> progress)
        {
            {
                result.MloName = mlo.Name ?? mlo.Hash.ToString();
                result.NoteInterior_K2(mlo, instPos, instRot);

                if (mlo.BBMax.X > mlo.BBMin.X)
                {
                    var box = new BoundingBox(instPos + Rotate(mlo.BBMin, instRot),
                                              instPos + Rotate(mlo.BBMax, instRot));
                    result.FocusBounds = result.HasFocus ? BoundingBox.Merge(result.FocusBounds, box) : box;
                    result.HasFocus = true;
                }

                if (mlo.rooms != null)
                {
                    foreach (var room in mlo.rooms)
                    {
                        var tc = room?._Data.timecycleName ?? default;
                        if (tc.Hash == 0 || result.RoomTimecycleHashes.Contains(tc.Hash)) continue;
                        result.RoomTimecycleHashes.Add(tc.Hash);
                        var n = tc.ToString();
                        result.RoomTimecycles.Add(
                            (string.IsNullOrEmpty(n) || n.StartsWith("hash_", StringComparison.OrdinalIgnoreCase))
                                ? null : n);
                    }
                }

                var all = new List<(MCEntityDef ent, string set, int room)>();
                if (mlo.entities != null)
                    foreach (var e in mlo.entities) if (e != null) all.Add((e, null, -1));
                if (mlo.entitySets != null)
                {
                    foreach (var set in mlo.entitySets)
                    {
                        if (set?.Entities == null) continue;
                        for (int i = 0; i < set.Entities.Length; i++)
                        {
                            int room = (set.Locations != null && i < set.Locations.Length) ? (int)set.Locations[i] : -1;
                            if (set.Entities[i] != null) all.Add((set.Entities[i], set.Name, room));
                        }
                    }
                }

                result.EntityCount += all.Count;
                int done = 0;
                foreach (var (ent, setName, room) in all)
                {
                    done++;
                    if ((done % 64) == 0) progress?.Invoke($"Placing {done}/{all.Count}...");

                    try
                    {

                    var cd = ent._Data;

                    if (!IsHdLod(cd.lodLevel)) { result.LodSkipped++; continue; }

                    if (!ImportVegetation && IsVegetation(cd.archetypeName.ToString()))
                    { result.VegetationSkipped++; continue; }

                    var info = new MloEntityInfo
                    {
                        ArchetypeHash = cd.archetypeName.Hash,
                        ArchetypeName = cd.archetypeName.ToString(),
                        EntitySet = setName,
                        RoomIndex = room,
                        Flags = cd.flags,
                        Scale = new Vector3(cd.scaleXY, cd.scaleXY, cd.scaleZ),
                    };

                    var qLocal = new Quaternion(cd.rotation.X, cd.rotation.Y, cd.rotation.Z, cd.rotation.W);
                    if (qLocal != Quaternion.Identity) qLocal = Quaternion.Invert(qLocal);
                    var pLocal = new Vector3(cd.position.X, cd.position.Y, cd.position.Z);
                    info.Position = instPos + Rotate(pLocal, instRot);
                    info.Rotation = Quaternion.Multiply(instRot, qLocal);

                    var world = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, info.Scale,
                                                      Vector3.Zero, info.Rotation, info.Position);

                    if ((cd.flags & FlagOnlyInReflections) != 0)
                    {
                        result.Proxies++;
                        result.Entities.Add(info);
                        continue;
                    }

                    var prop = GetProp(info.ArchetypeHash);
                    if (prop?.BaseModel != null && prop.BaseModel.Meshes.Count > 0)
                    {
                        var src = prop.BaseModel;
                        var size = (src.Bounds.Maximum - src.Bounds.Minimum).Length();
                        if (size > MaxPropSize)
                        {
                            result.Oversized++;
                            if (result.OversizedNames.Count < 20)
                                result.OversizedNames.Add($"{info.ArchetypeName} ({size:0}m)");
                            result.Entities.Add(info);
                            continue;
                        }

                        var target = placedModel;
                        if ((prop.HasLights && ImportPropLights) || ImportAllProps)
                        {
                            if (prop.Placements.Count == 0) result.Props.Add(prop);
                            prop.Placements.Add(world);
                            target = prop.Model;
                        }
                        foreach (var mesh in src.Meshes)
                        {
                            var inst = mesh.CreateInstance(world);
                            inst.TintPaletteIndex = cd.tintValue;
                            InteriorAmbient_L2?.Stamp(inst, mlo, ent, room);
                            inst.SunScale = InteriorSun_N2.SunForRoom(mlo, room);
                            target.Meshes.Add(inst);
                        }
                        info.TrackPlacedMeshes_O3(target, src.Meshes.Count, world);
                        info.Resolved = true;
                        result.Placed++;
                    }
                    else
                    {
                        result.Missing++;
                        if (result.MissingNames.Count < 40 && !result.MissingNames.Contains(info.ArchetypeName))
                            result.MissingNames.Add(info.ArchetypeName);
                    }
                    result.Entities.Add(info);

                    }
                    catch (Exception ex)
                    {
                        result.Missing++;
                        if (result.FailedNames.Count < 20)
                            result.FailedNames.Add($"{ent._Data.archetypeName}: {ex.GetType().Name}");
                    }
                }

                if (IncludeShell)
                {
                    var shell = TryGetDrawableModel(mlo.Hash);
                    if (shell != null)
                    {
                        result.ShellMeshes = shell.Meshes.Count;
                        result.ShellShaders = string.Join(", ", shell.Meshes.Select(x => x.ShaderName).Distinct().Take(6));
                        var shellWorld = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, Vector3.One,
                                                               Vector3.Zero, instRot, instPos);
                        foreach (var mesh in shell.Meshes) { var si = mesh.CreateInstance(shellWorld); si.IsMloShell = true; placedModel.Meshes.Add(si); }
                    }
                }
            }
        }

        private void CollectUntextured(MloImportResult result)
        {
            foreach (var kv in propCache)
            {
                if (kv.Value.BaseModel == null) continue;
                foreach (var mesh in kv.Value.BaseModel.Meshes)
                {
                    if (mesh.DiffuseSRV != null) continue;
                    result.UntexturedMeshes++;
                    if (result.UntexturedNames.Count < 20)
                    {
                        var tag = $"{kv.Value.Name}/{mesh.ShaderName}";
                        if (!result.UntexturedNames.Contains(tag)) result.UntexturedNames.Add(tag);
                    }
                }
            }
        }

        public MloImportResult ImportYmap(string ymapPath, out RenderModel placedModel,
            Action<string> progress = null)
        {
            var sw = Stopwatch.StartNew();
            var result = new MloImportResult { MloName = Path.GetFileNameWithoutExtension(ymapPath) };
            placedModel = new RenderModel { Name = result.MloName };

            progress?.Invoke("Indexing local files...");
            EnsureLocalIndex(LocalAssetIndex.FindRoot(ymapPath), progress);
            modelRenderer.TextureFallback = (tex, txd) => local.FindTexture(tex) ?? game.FindTexture(tex, txd);

            var ymap = new YmapFile();
            ymap.Load(File.ReadAllBytes(ymapPath));
            if (game.Ready && game.Cache != null)
            {
                try { ymap.InitYmapEntityArchetypes(game.Cache); } catch { }
            }

            var entities = ymap.AllEntities ?? ymap.RootEntities;
            if (entities == null || entities.Length == 0)
                throw new Exception("No entities in this .ymap");

            result.EntityCount = entities.Length;
            int done = 0;
            foreach (var ent in entities)
            {
                if (ent == null) continue;
                if ((++done % 64) == 0) progress?.Invoke($"Placing {done}/{entities.Length}...");

                try
                {

                var cd = ent._CEntityDef;

                if (!IsHdLod(cd.lodLevel)) { result.LodSkipped++; continue; }

                if (!ImportVegetation && IsVegetation(cd.archetypeName.ToString()))
                { result.VegetationSkipped++; continue; }

                var rawRot = new Quaternion(cd.rotation.X, cd.rotation.Y, cd.rotation.Z, cd.rotation.W);
                if (rawRot.LengthSquared() < 1e-6f) rawRot = Quaternion.Identity; else rawRot.Normalize();
                var rot = Quaternion.Invert(rawRot);

                var scale = new Vector3(cd.scaleXY, cd.scaleXY, cd.scaleZ);
                if (!(scale.X > 0)) scale.X = 1;
                if (!(scale.Y > 0)) scale.Y = 1;
                if (!(scale.Z > 0)) scale.Z = 1;

                var pos = new Vector3(cd.position.X, cd.position.Y, cd.position.Z);
                if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsNaN(pos.Z)) continue;

                var info = new MloEntityInfo
                {
                    ArchetypeHash = cd.archetypeName.Hash,
                    ArchetypeName = cd.archetypeName.ToString(),
                    Flags = cd.flags,
                    Position = pos,
                    Rotation = rot,
                    Scale = scale,
                };

                if ((info.Flags & FlagOnlyInReflections) != 0)
                {
                    result.Proxies++;
                    result.Entities.Add(info);
                    continue;
                }

                var mloArch = (local?.GetArchetype(info.ArchetypeHash)
                               ?? (game.Ready ? game.Cache?.GetArchetype(info.ArchetypeHash) : null)) as MloArchetype;
                if (mloArch != null)
                {
                    result.MloInstances++;
                    if (!result.MloInstanceNames.Contains(info.ArchetypeName))
                        result.MloInstanceNames.Add(info.ArchetypeName);
                    int before = result.Placed;
                    PlaceMlo(mloArch, info.Position, rawRot, placedModel, result, progress);
                    info.Resolved = result.Placed > before;
                    result.Entities.Add(info);
                    continue;
                }

                var prop = GetProp(info.ArchetypeHash);
                if (prop?.BaseModel != null && prop.BaseModel.Meshes.Count > 0)
                {
                    var world = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, info.Scale,
                                                      Vector3.Zero, info.Rotation, info.Position);
                    var target = placedModel;
                    if ((prop.HasLights && ImportPropLights) || ImportAllProps)
                    {
                        if (prop.Placements.Count == 0) result.Props.Add(prop);
                        prop.Placements.Add(world);
                        target = prop.Model;
                    }
                    foreach (var mesh in prop.BaseModel.Meshes)
                    {
                        var inst = mesh.CreateInstance(world);
                        inst.TintPaletteIndex = cd.tintValue;
                        target.Meshes.Add(inst);
                    }
                    info.TrackPlacedMeshes_O3(target, prop.BaseModel.Meshes.Count, world);
                    info.Resolved = true;
                    result.Placed++;
                }
                else
                {
                    result.Missing++;
                    if (result.MissingNames.Count < 40 && !result.MissingNames.Contains(info.ArchetypeName))
                        result.MissingNames.Add(info.ArchetypeName);
                }
                result.Entities.Add(info);

                }
                catch (Exception ex)
                {
                    result.Missing++;
                    if (result.FailedNames.Count < 20)
                        result.FailedNames.Add($"{ent._CEntityDef.archetypeName}: {ex.GetType().Name}");
                }
            }

            RecomputeBounds(placedModel);
            foreach (var p in result.Props) RecomputeBounds(p.Model);
            CollectUntextured(result);

            {
                var placedPos = result.Entities.Where(e => e.Resolved).Select(e => e.Position).ToList();
                if (placedPos.Count > 0)
                {
                    const float worldFloor = -500f, worldCeil = 1500f;
                    var keep = placedPos.Where(p => p.Z > worldFloor && p.Z < worldCeil).ToList();
                    if (keep.Count == 0) keep = placedPos;

                    var lo = keep[0]; var hi = keep[0];
                    foreach (var p in keep) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                    var pad = Vector3.Max((hi - lo) * 0.15f, new Vector3(8f, 8f, 4f));
                    result.FocusBounds = new BoundingBox(lo - pad, hi + pad);
                    result.HasFocus = true;
                }
            }

            result.UniqueProps = propCache.Count;
            result.LocalFiles = local?.FileCount ?? 0;
            result.Seconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        private static float Pct(List<Vector3> pts, Func<Vector3, float> axis, float p)
        {
            var vals = pts.Select(axis).OrderBy(v => v).ToList();
            int i = Math.Clamp((int)(vals.Count * p), 0, vals.Count - 1);
            return vals[i];
        }

        private static Vector3 Rotate(Vector3 v, Quaternion q)
        {
            Vector3.Transform(ref v, ref q, out Vector3 r);
            return r;
        }

        private static void RecomputeBounds(RenderModel m)
        {
            if (m == null) return;
            bool first = true;
            foreach (var mesh in m.Meshes)
            {
                m.Bounds = first ? mesh.WorldBounds : BoundingBox.Merge(m.Bounds, mesh.WorldBounds);
                first = false;
            }
        }

        private void ImportLooseArchetypes(YtypFile ytyp, MloImportResult result,
            RenderModel placedModel, List<LightAttributes> lights, Action<string> progress)
        {
            var archetypes = ytyp.AllArchetypes.Where(a => a != null && !(a is MloArchetype)).ToList();
            result.EntityCount = archetypes.Count;

            var built = new List<MloProp>();
            for (int i = 0; i < archetypes.Count; i++)
            {
                if ((i & 7) == 0) progress?.Invoke($"Loading props {i}/{archetypes.Count}...");
                var prop = GetProp(archetypes[i].Hash);
                if (prop == null)
                {
                    result.Missing++;
                    if (result.MissingNames.Count < 40)
                        result.MissingNames.Add(archetypes[i].Name);
                    continue;
                }
                built.Add(prop);
            }

            float cell = 2.0f;
            foreach (var p in built)
            {
                var b = p.BaseModel.Bounds;
                if (b.Maximum.X > b.Minimum.X)
                    cell = Math.Max(cell, (b.Maximum - b.Minimum).Length() * 0.8f);
            }
            cell = Math.Min(cell, 30.0f);
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(built.Count)));

            for (int i = 0; i < built.Count; i++)
            {
                var prop = built[i];
                var world = Matrix.Translation((i % cols) * cell, (i / cols) * cell, 0.0f);

                var target = placedModel;
                if ((prop.HasLights && ImportPropLights) || ImportAllProps)
                {
                    if (prop.Placements.Count == 0) result.Props.Add(prop);
                    target = prop.Model;
                }
                prop.Placements.Add(world);
                foreach (var mesh in prop.BaseModel.Meshes) target.Meshes.Add(mesh.CreateInstance(world));
                result.Placed++;
            }

            result.UniqueProps = built.Count;
            result.LocalFiles = local?.FileCount ?? 0;
            if (built.Count > 0)
            {
                float w = cols * cell;
                float h = ((built.Count + cols - 1) / cols) * cell;
                result.FocusBounds = new BoundingBox(
                    new Vector3(-cell, -cell, -cell), new Vector3(w, h, cell * 2));
                result.HasFocus = true;
            }
        }

        private MloProp GetProp(uint archetypeHash)
        {
            if (propCache.TryGetValue(archetypeHash, out var cached)) return cached;
            if (failedProps.Contains(archetypeHash)) return null;

            var prop = TryResolveProp(archetypeHash);
            if (prop?.BaseModel == null)
            {
                if (game == null || game.Ready) failedProps.Add(archetypeHash);
                return null;
            }
            propCache[archetypeHash] = prop;
            return prop;
        }

        private RenderModel TryGetDrawableModel(uint archetypeHash) => TryResolveProp(archetypeHash)?.BaseModel;

        private MloProp TryResolveProp(uint archetypeHash)
        {
            Archetype arch = null;
            DrawableBase drawable = local?.GetDrawable(archetypeHash, out arch);
            bool fromLocal = drawable != null;
            var prop = new MloProp { Hash = archetypeHash };
            if (fromLocal)
            {
                prop.Path = local.LastPath;
                prop.Ydr = local.LastYdr;
                prop.Yft = local.LastYft;
            }
            if (drawable == null)
            {
                drawable = game.GetDrawable(archetypeHash, out arch);
            }
            if (drawable == null) return null;

            prop.Name = arch?.Name ?? archetypeHash.ToString();
            prop.Skeleton = drawable.Skeleton;
            prop.Drawable = drawable;
            prop.ReadOnly = prop.Ydr == null && prop.Yft == null;
            prop.Lights = prop.Yft?.Fragment?.LightAttributes?.data_items
                ?? (drawable as Drawable)?.LightAttributes?.data_items
                ?? (drawable as FragDrawable)?.OwnerFragment?.LightAttributes?.data_items;

            if (arch != null && arch.TextureDict != 0)
            {
                var ytd = (fromLocal ? local.GetTextureDict(arch.TextureDict) : null)
                          ?? game.GetTextureDict(arch.TextureDict)
                          ?? local?.GetTextureDict(arch.TextureDict);
                if (ytd?.TextureDict != null && !modelRenderer.ExternalTextureDicts.Contains(ytd.TextureDict))
                    modelRenderer.ExternalTextureDicts.Add(ytd.TextureDict);
            }

            modelRenderer.TextureContext = arch?.TextureDict ?? 0;
            var m = modelRenderer.BuildFromDrawable(drawable, prop.Name);
            modelRenderer.TextureContext = 0;
            if (m.Meshes.Count == 0) return null;

            prop.BaseModel = m;
            prop.Model = new RenderModel { Name = prop.Name };
            return prop;
        }
    }
}

