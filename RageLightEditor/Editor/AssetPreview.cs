using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public enum AssetKind
    {
        Unsupported,
        Drawable,
        Fragment,
        DrawableDict,
        TextureDict,
        Collision,
    }

    public class AssetTextureInfo
    {
        public string Name = "";
        public uint NameHash;
        public int Width, Height, Levels;
        public string Format = "";
        public string Usage = "";
        public long DataBytes;
        public GameTexture Texture;

        public bool HasData => Texture?.Data?.FullData != null;

        public override string ToString() =>
            $"{Name}  {Width}x{Height}  {Format}" + (Levels > 1 ? $"  {Levels} mips" : "");
    }

    public class AssetTextureSlot
    {
        public uint SlotHash;
        public string SlotName = "";
        public string TextureName = "";
        public uint TextureNameHash;
        public MatTexSource Source = MatTexSource.Missing;
        public GameTexture Texture;
        public AssetTextureInfo Info;

        public bool Resolved => Texture?.Data?.FullData != null;

        public override string ToString() =>
            $"{SlotName} = {TextureName}" + (Resolved ? "" : "  (unresolved)");
    }

    public class AssetMaterialParam
    {
        public uint Hash;
        public string Name = "";
        public Vector4 Value;
        public int ArrayLength;

        public override string ToString() => ArrayLength > 0
            ? $"{Name} = float4[{ArrayLength}]"
            : $"{Name} = ({Value.X:0.###}, {Value.Y:0.###}, {Value.Z:0.###}, {Value.W:0.###})";
    }

    public class AssetMaterialInfo
    {
        public int Index = -1;
        public string ShaderName = "";
        public string Sps = "";
        public byte Bucket;
        public string BucketName = "";
        public GeomAlphaMode DrawMode;
        public bool DoubleSided;
        public bool NeverDrawn;
        public int GeometryCount;
        public readonly List<AssetMaterialParam> Params = new List<AssetMaterialParam>();
        public readonly List<AssetTextureSlot> Textures = new List<AssetTextureSlot>();
        public ShaderFX Shader;

        public override string ToString() => $"[{Index}] {ShaderName} ({Sps})";
    }

    public class AssetStats
    {
        public string Name = "";
        public string Path = "";
        public AssetKind Kind = AssetKind.Unsupported;
        public long FileSize;

        public int Drawables;
        public int Models;
        public int Geometries;
        public int Vertices;
        public int Triangles;
        public int AllLodGeometries;
        public int AllLodTriangles;

        public int Shaders;
        public bool HasSkeleton;
        public int Bones;
        public BoundingBox Bounds;

        public readonly List<AssetTextureInfo> Textures = new List<AssetTextureInfo>();
        public long TextureBytes;

        public readonly List<string> ShaderNames = new List<string>();

        public string BoundType = "";
        public int CollisionChildren;
        public int CollisionPolygons;
        public int CollisionVertices;
        public int CollisionMaterials;

        public string Summary()
        {
            switch (Kind)
            {
                case AssetKind.TextureDict:
                    return $"{Textures.Count} texture(s), {TextureBytes / 1024:N0} KB";
                case AssetKind.Collision:
                    return $"{BoundType}, {CollisionChildren} part(s), " +
                           $"{CollisionPolygons:N0} poly, {CollisionVertices:N0} vert";
                case AssetKind.Unsupported:
                    return "not previewable";
                default:
                    return $"{Geometries} geom, {Vertices:N0} vert, {Triangles:N0} tri, " +
                           $"{Shaders} material(s), {Textures.Count} embedded texture(s)";
            }
        }
    }

    public partial class AssetPreview : IDisposable
    {
        public static readonly object ModelBuildLock = new object();

        private readonly GameFileManager game;
        private readonly ModelRenderer builder;
        private readonly TextureLoader textures;

        public AssetPreview(GameFileManager game, ModelRenderer modelRenderer, TextureLoader textureLoader)
        {
            this.game = game;
            this.builder = modelRenderer;
            this.textures = textureLoader;
        }

        public RpfFileEntry Entry { get; private set; }
        public AssetKind Kind { get; private set; } = AssetKind.Unsupported;
        public string Error { get; private set; } = "";
        public AssetStats Stats { get; private set; } = new AssetStats();

        public YdrFile Ydr { get; private set; }
        public YftFile Yft { get; private set; }
        public YddFile Ydd { get; private set; }
        public YtdFile Ytd { get; private set; }
        public YbnFile Ybn { get; private set; }

        public RenderModel Model { get; private set; }

        public DrawableBase Drawable { get; private set; }

        public IReadOnlyList<string> DrawableNames => drawableNames;
        private readonly List<string> drawableNames = new List<string>();
        private Drawable[] dictDrawables;

        public int SelectedDrawable { get; private set; } = -1;

        public IReadOnlyList<AssetTextureInfo> Textures => textureList;
        private readonly List<AssetTextureInfo> textureList = new List<AssetTextureInfo>();

        public Action<YbnFile> CollisionSink;

        public uint TxdContext { get; private set; }

        public static AssetKind KindOf(RpfEntry e)
        {
            switch (ArchiveBrowser.KindOf(e))
            {
                case "ydr": return AssetKind.Drawable;
                case "yft": return AssetKind.Fragment;
                case "ydd": return AssetKind.DrawableDict;
                case "ytd": return AssetKind.TextureDict;
                case "ybn": return AssetKind.Collision;
                default: return AssetKind.Unsupported;
            }
        }

        public static bool CanPreview(RpfEntry e) => KindOf(e) != AssetKind.Unsupported;

        public bool Open(RpfFileEntry e)
        {
            Close();
            Error = "";
            Entry = e;
            Kind = KindOf(e);

            if (e == null) { Error = "no file"; return false; }
            if (Kind == AssetKind.Unsupported)
            {
                Error = $"nothing here reads .{ArchiveBrowser.KindOf(e)} yet";
                return false;
            }
            if (game == null || !game.Ready)
            {
                Error = "game archives are not open yet";
                return false;
            }

            Stats = new AssetStats
            {
                Name = e.Name ?? "",
                Path = e.Path ?? "",
                Kind = Kind,
                FileSize = e.GetFileSize(),
            };

            try
            {
                switch (Kind)
                {
                    case AssetKind.Drawable: return OpenYdr(e);
                    case AssetKind.Fragment: return OpenYft(e);
                    case AssetKind.DrawableDict: return OpenYdd(e);
                    case AssetKind.TextureDict: return OpenYtd(e);
                    case AssetKind.Collision: return OpenYbn(e);
                }
            }
            catch (Exception ex)
            {
                var msg = $"could not read {e.Name}: {ex.Message}";
                Close();
                Error = msg;
                return false;
            }
            return false;
        }

        private bool OpenYdr(RpfFileEntry e)
        {
            var ydr = new YdrFile(e);
            if (!game.EnsureLoaded(ydr) || ydr.Drawable == null)
            {
                Error = $"{e.Name} did not parse as a drawable";
                return false;
            }
            Ydr = ydr;
            Drawable = ydr.Drawable;
            TxdContext = FindTxdContext(e, out var archDict);
            MeasureDrawable(Drawable, 1);
            Build(archDict, () => builder.BuildFromDrawable(Drawable, e.Name));
            return true;
        }

        private bool OpenYft(RpfFileEntry e)
        {
            var yft = new YftFile(e);
            if (!game.EnsureLoaded(yft) || yft.Fragment == null)
            {
                Error = $"{e.Name} did not parse as a fragment";
                return false;
            }
            Yft = yft;
            Drawable = yft.Fragment.Drawable;
            TxdContext = FindTxdContext(e, out var archDict);

            int drawables = 0;
            foreach (var d in FragmentDrawables(yft.Fragment)) { MeasureDrawable(d, 0); drawables++; }
            Stats.Drawables = drawables;
            if (Drawable != null) Stats.Bounds = DrawableBounds(Drawable);

            Build(archDict, () => builder.BuildFromYft(yft));
            return true;
        }

        private bool OpenYdd(RpfFileEntry e)
        {
            var ydd = new YddFile(e);
            if (!game.EnsureLoaded(ydd))
            {
                Error = $"{e.Name} did not parse as a drawable dictionary";
                return false;
            }
            Ydd = ydd;
            dictDrawables = ydd.Drawables ?? ydd.DrawableDict?.Drawables?.data_items;
            if (dictDrawables == null || dictDrawables.Length == 0)
            {
                Error = $"{e.Name} holds no drawables";
                return false;
            }

            for (int i = 0; i < dictDrawables.Length; i++)
            {
                var n = dictDrawables[i]?.Name;
                drawableNames.Add(string.IsNullOrEmpty(n) ? $"drawable {i}" : n);
            }
            Stats.Drawables = dictDrawables.Length;
            TxdContext = FindTxdContext(e, out _);
            return SelectDrawable(0);
        }

        private bool OpenYtd(RpfFileEntry e)
        {
            var ytd = new YtdFile(e);
            if (!game.EnsureLoaded(ytd) || ytd.TextureDict == null)
            {
                Error = $"{e.Name} did not parse as a texture dictionary";
                return false;
            }
            Ytd = ytd;

            var texs = ytd.TextureDict.Textures?.data_items;
            if (texs != null)
            {
                foreach (var t in texs)
                {
                    if (t == null) continue;
                    var info = Describe(t);
                    textureList.Add(info);
                    Stats.Textures.Add(info);
                    Stats.TextureBytes += info.DataBytes;
                }
            }
            return true;
        }

        private bool OpenYbn(RpfFileEntry e)
        {
            var ybn = new YbnFile(e);
            if (!game.EnsureLoaded(ybn) || ybn.Bounds == null)
            {
                Error = $"{e.Name} did not parse as collision";
                return false;
            }
            Ybn = ybn;

            var b = ybn.Bounds;
            Stats.BoundType = b.Type.ToString();
            Stats.Bounds = new BoundingBox(b.BoxMin, b.BoxMax);
            MeasureBounds(b, 0);

            CollisionSink?.Invoke(ybn);
            return true;
        }

        public bool SelectDrawable(int index)
        {
            if (dictDrawables == null || index < 0 || index >= dictDrawables.Length) return false;
            var d = dictDrawables[index];
            if (d == null) return false;

            DisposeModel();
            SelectedDrawable = index;
            Drawable = d;
            Error = "";

            int dictCount = Stats.Drawables;
            Stats.Geometries = Stats.Vertices = Stats.Triangles = 0;
            Stats.AllLodGeometries = Stats.AllLodTriangles = 0;
            Stats.Models = Stats.Shaders = 0;
            Stats.Textures.Clear();
            Stats.ShaderNames.Clear();
            Stats.TextureBytes = 0;
            Stats.HasSkeleton = false;
            Stats.Bones = 0;
            Stats.Bounds = new BoundingBox();
            MeasureDrawable(d, dictCount);

            string name = index < drawableNames.Count ? drawableNames[index] : Entry?.Name;
            var archDict = TxdContext != 0 ? game.GetTextureDict(TxdContext) : null;
            Build(archDict, () => builder.BuildFromDrawable(d, name));
            return true;
        }

        public bool SelectDrawable(string name)
        {
            for (int i = 0; i < drawableNames.Count; i++)
            {
                if (string.Equals(drawableNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return SelectDrawable(i);
            }
            return false;
        }

        private void Build(YtdFile archDict, Func<RenderModel> build)
        {
            lastBuild_V22 = build;
            lastArchDict_V22 = archDict;
            lock (ModelBuildLock)
            {
                var dict = archDict?.TextureDict;
                bool added = false;
                var extra = new List<TextureDictionary>();
                try
                {
                    if (dict != null && !builder.ExternalTextureDicts.Contains(dict))
                    {
                        builder.ExternalTextureDicts.Add(dict);
                        added = true;
                    }
                    foreach (var (nm, y) in AttachedYtds)
                        if (!IsYtdMuted_U6(nm) && y?.TextureDict != null && !builder.ExternalTextureDicts.Contains(y.TextureDict))
                        { builder.ExternalTextureDicts.Add(y.TextureDict); extra.Add(y.TextureDict); }
                    if (variantYtd_V23?.TextureDict != null && !builder.ExternalTextureDicts.Contains(variantYtd_V23.TextureDict))
                    { builder.ExternalTextureDicts.Add(variantYtd_V23.TextureDict); extra.Add(variantYtd_V23.TextureDict); }
                    builder.DiffuseRemap_V23 = VariantRemap_V23();
                    builder.TextureContext = TxdContext;
                    builder.ClearMissingTextures_V21();
                    Model = build();
                    MissingTextures = builder.MissingTexturesSnapshot_V21();
                }
                catch (Exception ex)
                {
                    Error = "could not build the preview: " + ex.Message;
                    Model = null;
                }
                finally
                {
                    builder.TextureContext = 0;
                    builder.DiffuseRemap_V23 = null;
                    if (added) builder.ExternalTextureDicts.Remove(dict);
                    foreach (var d in extra) builder.ExternalTextureDicts.Remove(d);
                }
            }
            if (TextureVariants.Count == 0) ScanTextureVariants_V23();

            if (Model != null && Model.Meshes.Count == 0)
            {
                Error = $"{Stats.Name} has no drawable geometry";
            }
        }

        public int RefreshTextures()
        {
            if (Model == null) return 0;
            int before = MissingTextures.Length;
            lock (ModelBuildLock)
            {
                var dicts = new List<TextureDictionary>();
                try
                {
                    if (lastArchDict_V22?.TextureDict != null && !builder.ExternalTextureDicts.Contains(lastArchDict_V22.TextureDict))
                    { builder.ExternalTextureDicts.Add(lastArchDict_V22.TextureDict); dicts.Add(lastArchDict_V22.TextureDict); }
                    foreach (var (nm, y) in AttachedYtds)
                        if (!IsYtdMuted_U6(nm) && y?.TextureDict != null && !builder.ExternalTextureDicts.Contains(y.TextureDict))
                        { builder.ExternalTextureDicts.Add(y.TextureDict); dicts.Add(y.TextureDict); }
                    if (variantYtd_V23?.TextureDict != null && !builder.ExternalTextureDicts.Contains(variantYtd_V23.TextureDict))
                    { builder.ExternalTextureDicts.Add(variantYtd_V23.TextureDict); dicts.Add(variantYtd_V23.TextureDict); }
                    builder.DiffuseRemap_V23 = VariantRemap_V23();
                    builder.TextureContext = TxdContext;
                    builder.ClearMissingTextures_V21();
                    foreach (var mesh in Model.Meshes)
                    {
                        if (mesh?.Shader == null) continue;
                        builder.RefreshMaterial(mesh);
                    }
                    foreach (var a in Attachments)
                        foreach (var mesh in a?.Model?.Meshes ?? new List<RenderMesh>())
                            if (mesh?.Shader != null) builder.RefreshMaterial(mesh);
                    MissingTextures = builder.MissingTexturesSnapshot_V21();
                }
                finally
                {
                    builder.TextureContext = 0;
                    builder.DiffuseRemap_V23 = null;
                    foreach (var d in dicts) builder.ExternalTextureDicts.Remove(d);
                }
            }
            return Math.Max(0, before - MissingTextures.Length);
        }

        public ShaderResourceView GetTextureView(AssetTextureInfo t)
        {
            if (textures == null || t == null || !t.HasData) return null;
            return textures.GetSRV(t.Texture, false);
        }

        public ShaderResourceView GetTextureView(AssetTextureSlot slot)
        {
            if (textures == null || slot?.Texture?.Data?.FullData == null) return null;
            return textures.GetSRV(slot.Texture, false);
        }

        public List<AssetMaterialInfo> InspectMaterials(DrawableBase drawable)
        {
            var list = new List<AssetMaterialInfo>();
            var shaders = drawable?.ShaderGroup?.Shaders?.data_items;
            if (shaders == null) return list;

            var embedded = drawable.ShaderGroup.TextureDictionary;
            var useCount = new Dictionary<int, int>();
            foreach (var m in AllModels(drawable))
            {
                if (m?.Geometries == null) continue;
                foreach (var g in m.Geometries)
                {
                    if (g == null) continue;
                    useCount.TryGetValue(g.ShaderID, out int n);
                    useCount[g.ShaderID] = n + 1;
                }
            }

            for (int i = 0; i < shaders.Length; i++)
            {
                if (shaders[i] == null) continue;
                var info = Inspect(shaders[i], embedded, TxdContext);
                info.Index = i;
                useCount.TryGetValue(i, out int used);
                info.GeometryCount = used;
                list.Add(info);
            }
            return list;
        }

        public List<AssetMaterialInfo> InspectMaterials() => InspectMaterials(Drawable);

        public AssetMaterialInfo Inspect(RenderMesh mesh)
        {
            if (mesh?.Shader == null) return null;
            var info = Inspect(mesh.Shader, mesh.EmbeddedDict, mesh.TxdContext);
            info.Index = mesh.ShaderIndex;
            info.GeometryCount = 1;
            info.DrawMode = mesh.AlphaMode;
            info.DoubleSided = mesh.DoubleSided;
            info.NeverDrawn = mesh.NeverDraw;
            return info;
        }

        private AssetMaterialInfo Inspect(ShaderFX shader, TextureDictionary embedded, uint txd)
        {
            var info = new AssetMaterialInfo
            {
                Shader = shader,
                ShaderName = shader.Name.ToString(),
                Sps = shader.FileName.ToString(),
                Bucket = shader.RenderBucket,
                BucketName = MaterialDefs.BucketName(shader.RenderBucket),
            };

            var probe = new RenderMesh();
            ModelRenderer.ClassifyDrawPublic(probe, shader);
            info.DrawMode = probe.AlphaMode;
            info.DoubleSided = probe.DoubleSided;
            info.NeverDrawn = probe.NeverDraw;

            var plist = shader.ParametersList;
            var prms = plist?.Parameters;
            var hashes = plist?.Hashes;
            if (prms == null || hashes == null) return info;

            int n = Math.Min(prms.Length, hashes.Length);
            for (int i = 0; i < n; i++)
            {
                uint hash = (uint)hashes[i];
                var data = prms[i].Data;

                if (data is TextureBase tb)
                {
                    var slot = new AssetTextureSlot
                    {
                        SlotHash = hash,
                        SlotName = MaterialDefs.NameOf(hash),
                        TextureName = tb.Name ?? "",
                        TextureNameHash = tb.NameHash,
                    };
                    slot.Texture = ResolveTexture(tb, embedded, txd, out var src);
                    slot.Source = src;
                    if (slot.Texture != null) slot.Info = Describe(slot.Texture);
                    info.Textures.Add(slot);
                }
                else if (data is Vector4 v)
                {
                    info.Params.Add(new AssetMaterialParam
                    {
                        Hash = hash,
                        Name = MaterialDefs.NameOf(hash),
                        Value = v,
                    });
                }
                else if (data is Vector4[] arr)
                {
                    info.Params.Add(new AssetMaterialParam
                    {
                        Hash = hash,
                        Name = MaterialDefs.NameOf(hash),
                        Value = arr.Length > 0 ? arr[0] : Vector4.Zero,
                        ArrayLength = arr.Length,
                    });
                }
            }
            return info;
        }

        private GameTexture ResolveTexture(TextureBase tb, TextureDictionary embedded, uint txd,
            out MatTexSource source)
        {
            source = MatTexSource.Missing;
            if (tb == null) return null;

            if (tb is GameTexture gt && gt.Data?.FullData != null)
            {
                source = MatTexSource.Embedded;
                return gt;
            }

            var remap = VariantRemap_V23();
            if (remap != null) { var r = remap(tb); if (r != null) tb = r; }

            var tex = embedded?.Lookup(tb.NameHash);
            if (tex?.Data?.FullData != null) { source = MatTexSource.Embedded; return tex; }

            foreach (var (nm, y) in AttachedYtds)
            {
                if (IsYtdMuted_U6(nm)) continue;
                tex = y?.TextureDict?.Lookup(tb.NameHash);
                if (tex?.Data?.FullData != null) { source = MatTexSource.LoadedYtd; return tex; }
            }
            tex = variantYtd_V23?.TextureDict?.Lookup(tb.NameHash);
            if (tex?.Data?.FullData != null) { source = MatTexSource.LoadedYtd; return tex; }
            tex = lastArchDict_V22?.TextureDict?.Lookup(tb.NameHash);
            if (tex?.Data?.FullData != null) { source = MatTexSource.GameArchive; return tex; }

            var imported = builder?.ImportedTextures;
            if (imported != null)
            {
                foreach (var it in imported)
                {
                    if (it != null && it.NameHash == tb.NameHash && it.Data?.FullData != null)
                    {
                        source = MatTexSource.Imported;
                        return it;
                    }
                }
            }

            if (builder != null)
            {
                foreach (var dict in builder.ExternalTextureDicts)
                {
                    tex = dict?.Lookup(tb.NameHash);
                    if (tex?.Data?.FullData != null) { source = MatTexSource.LoadedYtd; return tex; }
                }
            }

            tex = game?.FindTexture(tb.NameHash, txd);
            if (tex?.Data?.FullData != null) { source = MatTexSource.GameArchive; return tex; }
            return null;
        }

        private void MeasureDrawable(DrawableBase d, int drawables)
        {
            if (d == null) return;
            if (drawables > 0) Stats.Drawables = drawables;

            var lod = ModelRenderer.HighestLod(d);
            if (lod != null)
            {
                Stats.Models += lod.Length;
                foreach (var m in lod)
                {
                    if (m?.Geometries == null) continue;
                    foreach (var g in m.Geometries)
                    {
                        if (g == null) continue;
                        Stats.Geometries++;
                        Stats.Vertices += g.VertexData?.VertexCount ?? g.VerticesCount;
                        Stats.Triangles += TriangleCount(g);
                    }
                }
            }

            foreach (var m in AllModels(d))
            {
                if (m?.Geometries == null) continue;
                foreach (var g in m.Geometries)
                {
                    if (g == null) continue;
                    Stats.AllLodGeometries++;
                    Stats.AllLodTriangles += TriangleCount(g);
                }
            }

            var shaders = d.ShaderGroup?.Shaders?.data_items;
            if (shaders != null)
            {
                Stats.Shaders += shaders.Length;
                foreach (var s in shaders)
                {
                    if (s == null) continue;
                    Stats.ShaderNames.Add($"{s.Name} ({s.FileName})");
                }
            }

            var embedded = d.ShaderGroup?.TextureDictionary?.Textures?.data_items;
            if (embedded != null)
            {
                foreach (var t in embedded)
                {
                    if (t == null) continue;
                    var info = Describe(t);
                    Stats.Textures.Add(info);
                    Stats.TextureBytes += info.DataBytes;
                }
            }

            var bones = d.Skeleton?.Bones?.Items;
            if (bones != null && bones.Length > 0)
            {
                Stats.HasSkeleton = true;
                Stats.Bones = bones.Length;
            }

            if (Stats.Bounds.Minimum == Vector3.Zero && Stats.Bounds.Maximum == Vector3.Zero)
                Stats.Bounds = DrawableBounds(d);
        }

        private static int TriangleCount(DrawableGeometry g)
        {
            int indices = g.IndexBuffer?.Indices?.Length ?? (int)g.IndicesCount;
            return indices / 3;
        }

        private static BoundingBox DrawableBounds(DrawableBase d) =>
            new BoundingBox(d.BoundingBoxMin, d.BoundingBoxMax);

        private static IEnumerable<DrawableModel> AllModels(DrawableBase d)
        {
            var all = d?.AllModels;
            if (all != null)
            {
                foreach (var m in all) yield return m;
                yield break;
            }
            var lod = ModelRenderer.HighestLod(d);
            if (lod == null) yield break;
            foreach (var m in lod) yield return m;
        }

        private static IEnumerable<DrawableBase> FragmentDrawables(FragType frag)
        {
            if (frag == null) yield break;
            if (frag.Drawable != null) yield return frag.Drawable;
            if (frag.DrawableCloth != null) yield return frag.DrawableCloth;

            var children = frag.PhysicsLODGroup?.PhysicsLOD1?.Children?.data_items;
            if (children == null) yield break;
            foreach (var c in children)
            {
                var cd = c?.Drawable1;
                if (cd != null && cd != frag.Drawable &&
                    cd.AllModels != null && cd.AllModels.Length > 0)
                    yield return cd;
            }
        }

        private void MeasureBounds(Bounds b, int depth)
        {
            if (b == null || depth > 8) return;

            if (b is BoundGeometry bg)
            {
                Stats.CollisionPolygons += bg.Polygons?.Length ?? (int)bg.PolygonsCount;
                Stats.CollisionVertices += bg.Vertices?.Length ?? (int)bg.VerticesCount;
                Stats.CollisionMaterials += bg.Materials?.Length ?? bg.MaterialsCount;
            }

            if (b is BoundComposite bc)
            {
                var children = bc.Children?.data_items;
                if (children == null) return;
                foreach (var c in children)
                {
                    if (c == null) continue;
                    if (depth == 0) Stats.CollisionChildren++;
                    MeasureBounds(c, depth + 1);
                }
            }
            else if (depth == 0)
            {
                Stats.CollisionChildren = 1;
            }
        }

        private static AssetTextureInfo Describe(GameTexture t)
        {
            var fmt = t.Format.ToString();
            if (fmt.StartsWith("D3DFMT_", StringComparison.Ordinal)) fmt = fmt.Substring(7);
            return new AssetTextureInfo
            {
                Name = t.Name ?? "",
                NameHash = t.NameHash,
                Width = t.Width,
                Height = t.Height,
                Levels = t.Levels,
                Format = fmt,
                Usage = t.Usage.ToString(),
                DataBytes = t.Data?.FullData?.LongLength ?? 0,
                Texture = t,
            };
        }

        private uint FindTxdContext(RpfFileEntry e, out YtdFile dict)
        {
            dict = null;
            uint nameHash = e?.ShortNameHash ?? 0;
            if (nameHash == 0) return 0;

            uint txd = game.Cache?.GetArchetype(nameHash)?.TextureDict ?? 0;
            if (txd == 0) txd = nameHash;

            dict = game.GetTextureDict(txd);
            return dict != null ? txd : (txd == nameHash ? 0u : txd);
        }

        public void Close()
        {
            ClearExtras_V22();
            TextureVariants.Clear(); variantDiskYtd_V23.Clear(); variantYtd_V23 = null;
            TextureVariant = '\0'; AuthoredVariant = '\0';
            DisposeModel();
            Entry = null;
            Kind = AssetKind.Unsupported;
            Ydr = null; Yft = null; Ydd = null; Ytd = null; Ybn = null;
            Drawable = null;
            dictDrawables = null;
            drawableNames.Clear();
            textureList.Clear();
            SelectedDrawable = -1;
            TxdContext = 0;
            Stats = new AssetStats();
            Error = "";
        }

        private void DisposeModel()
        {
            Model?.Dispose();
            Model = null;
        }

        public void Dispose() => Close();
    }
}

