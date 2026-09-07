using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Editor
{
    public class MaterialRef
    {
        public LoadedFile File;
        public DrawableBase Drawable;
        public ShaderGroup Group;
        public int Index;
        public ShaderFX Shader;
        public readonly List<RenderMesh> Meshes = new List<RenderMesh>();

        public string Name => Shader?.Name.ToString() ?? "";
        public string Sps => Shader?.FileName.ToString() ?? "";
        public byte Bucket => Shader?.RenderBucket ?? 0;

        public TextureDictionary EmbeddedDict => Group?.TextureDictionary;

        public uint TxdContext
        {
            get
            {
                foreach (var m in Meshes) if (m != null && m.TxdContext != 0) return m.TxdContext;
                return 0;
            }
        }

        public bool CanSave => Scene.HasWritableResource(File);

        public string Label => $"[{Index}] {Name}";

        public override string ToString() => Label;
    }

    public struct MatTexResolution
    {
        public MatTexSource Source;
        public GameTexture Texture;
        public string SourceName;

        public bool Found => Source != MatTexSource.Missing && Texture?.Data?.FullData != null;
    }

    public static class MaterialEditing
    {

        public static IEnumerable<DrawableBase> Drawables(LoadedFile f)
        {
            if (f == null) yield break;
            if (f.Ydr?.Drawable != null) { yield return f.Ydr.Drawable; yield break; }

            if (f.Yft == null && f.Drawable != null) { yield return f.Drawable; yield break; }

            var frag = f.Yft?.Fragment;
            if (frag == null) yield break;
            if (frag.Drawable != null) yield return frag.Drawable;
            if (frag.DrawableCloth != null) yield return frag.DrawableCloth;

            var children = frag.PhysicsLODGroup?.PhysicsLOD1?.Children?.data_items;
            if (children == null) yield break;
            foreach (var c in children)
            {
                var cd = c?.Drawable1;
                if (cd != null && cd != frag.Drawable && cd.ShaderGroup != null) yield return cd;
            }
        }

        public static List<MaterialRef> ForFile(Scene scene, LoadedFile f)
        {
            var list = new List<MaterialRef>();
            if (f == null) return list;

            var seen = new HashSet<ShaderFX>();
            foreach (var d in Drawables(f))
            {
                var shaders = d.ShaderGroup?.Shaders?.data_items;
                if (shaders == null) continue;
                for (int i = 0; i < shaders.Length; i++)
                {
                    var s = shaders[i];
                    if (s == null || !seen.Add(s)) continue;
                    list.Add(new MaterialRef
                    {
                        File = f,
                        Drawable = d,
                        Group = d.ShaderGroup,
                        Index = i,
                        Shader = s,
                    });
                }
            }

            if (list.Count == 0 && f.Model != null)
            {
                foreach (var m in f.Model.Meshes.OrderBy(m => m.ShaderIndex))
                {
                    if (m.Shader == null || !seen.Add(m.Shader)) continue;
                    list.Add(new MaterialRef
                    {
                        File = f,
                        Index = m.ShaderIndex,
                        Shader = m.Shader,
                    });
                }
            }

            AttachMeshes(scene, list);
            return list;
        }

        public static void AttachMeshes(Scene scene, List<MaterialRef> mats)
        {
            if (scene == null || mats == null || mats.Count == 0) return;
            var byShader = new Dictionary<ShaderFX, MaterialRef>();
            foreach (var m in mats)
            {
                m.Meshes.Clear();
                if (m.Shader != null) byShader[m.Shader] = m;
            }
            foreach (var mesh in scene.AllMeshes)
            {
                if (mesh.Shader != null && byShader.TryGetValue(mesh.Shader, out var mr)) mr.Meshes.Add(mesh);
            }
        }

        public static IEnumerable<RenderMesh> MeshesUsing(Scene scene, ShaderFX shader)
        {
            if (scene == null || shader == null) yield break;
            foreach (var m in scene.AllMeshes)
            {
                if (m.Shader == shader) yield return m;
            }
        }

        public static IEnumerable<uint> ParamHashes(ShaderFX s)
        {
            var plist = s?.ParametersList;
            if (plist?.Parameters == null || plist.Hashes == null) yield break;
            int n = Math.Min(plist.Parameters.Length, plist.Hashes.Length);
            for (int i = 0; i < n; i++) yield return (uint)plist.Hashes[i];
        }

        public static int IndexOf(ShaderFX s, uint hash)
        {
            var plist = s?.ParametersList;
            if (plist?.Parameters == null || plist.Hashes == null) return -1;
            int n = Math.Min(plist.Parameters.Length, plist.Hashes.Length);
            for (int i = 0; i < n; i++)
            {
                if ((uint)plist.Hashes[i] == hash) return i;
            }
            return -1;
        }

        public static bool Has(ShaderFX s, uint hash) => IndexOf(s, hash) >= 0;

        public static bool TryGetValue(ShaderFX s, uint hash, out Vector4 value)
        {
            value = Vector4.Zero;
            int i = IndexOf(s, hash);
            if (i < 0) return false;
            var data = s.ParametersList.Parameters[i].Data;
            if (data is Vector4 v) { value = v; return true; }
            if (data is Vector4[] arr && arr.Length > 0) { value = arr[0]; return true; }
            return false;
        }

        public static Vector4 GetValue(ShaderFX s, uint hash, Vector4 fallback) =>
            TryGetValue(s, hash, out var v) ? v : fallback;

        public static bool SetValue(ShaderFX s, uint hash, Vector4 value)
        {
            int i = IndexOf(s, hash);
            if (i < 0) return false;
            var p = s.ParametersList.Parameters[i];
            if (p.DataType == 0) return false;
            if (p.DataType > 1 && p.Data is Vector4[] arr && arr.Length > 0)
            {
                arr[0] = value;
                return true;
            }
            p.Data = value;
            return true;
        }

        public static TextureBase GetTexture(ShaderFX s, uint hash)
        {
            int i = IndexOf(s, hash);
            if (i < 0) return null;
            return s.ParametersList.Parameters[i].Data as TextureBase;
        }

        public static bool SetTexture(ShaderFX s, uint hash, TextureBase tex)
        {
            int i = IndexOf(s, hash);
            if (i < 0) return false;
            var p = s.ParametersList.Parameters[i];
            if (p.DataType != 0) return false;
            p.Data = tex;
            if (tex != null) textureMemory.GetOrCreateValue(s)[hash] = tex;
            return true;
        }

        public static TextureBase MakeTextureRef(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var clean = name.Trim().ToLowerInvariant();
            return new TextureBase
            {
                Name = clean,
                NameHash = JenkHash.GenHash(clean),
                Unknown_32h = 2,
            };
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShaderFX, Dictionary<uint, TextureBase>>
            textureMemory = new System.Runtime.CompilerServices.ConditionalWeakTable<ShaderFX, Dictionary<uint, TextureBase>>();

        public static void RememberTextures(ShaderFX s)
        {
            if (s == null) return;
            var mem = textureMemory.GetOrCreateValue(s);
            foreach (var h in ParamHashes(s).ToList())
            {
                var tb = GetTexture(s, h);
                if (tb != null) mem[h] = tb;
            }
        }

        public static TextureBase RecallTexture(ShaderFX s, uint hash)
        {
            if (s == null) return null;
            return textureMemory.TryGetValue(s, out var mem) && mem.TryGetValue(hash, out var tb) ? tb : null;
        }

        public static void CopyTextureMemory(ShaderFX from, ShaderFX to)
        {
            if (from == null || to == null) return;
            if (!textureMemory.TryGetValue(from, out var src)) return;
            var dst = textureMemory.GetOrCreateValue(to);
            foreach (var kv in src) dst[kv.Key] = kv.Value;
        }

        public static bool AddParam(ShaderFX s, uint hash, object data, bool isTexture)
        {
            if (s == null) return false;
            if (s.ParametersList == null)
            {
                s.ParametersList = new ShaderParametersBlock { Owner = s };
            }
            var plist = s.ParametersList;
            if (IndexOf(s, hash) >= 0) return false;

            var ps = (plist.Parameters ?? Array.Empty<ShaderParameter>()).ToList();
            var hs = (plist.Hashes ?? Array.Empty<MetaName>()).ToList();

            var p = new ShaderParameter
            {
                DataType = (byte)(isTexture ? 0 : 1),
                Data = isTexture ? data : (object)(data is Vector4 v ? v : Vector4.Zero),
            };

            int at = isTexture ? ps.Count(x => x.DataType == 0) : ps.Count;
            ps.Insert(at, p);
            hs.Insert(at, (MetaName)hash);

            plist.Parameters = ps.ToArray();
            plist.Hashes = hs.ToArray();
            plist.Count = ps.Count;
            Normalise(s);
            return true;
        }

        public static bool RemoveParam(ShaderFX s, uint hash)
        {
            int i = IndexOf(s, hash);
            if (i < 0) return false;
            var plist = s.ParametersList;
            var ps = plist.Parameters.ToList();
            var hs = plist.Hashes.ToList();
            ps.RemoveAt(i);
            hs.RemoveAt(i);
            plist.Parameters = ps.ToArray();
            plist.Hashes = hs.ToArray();
            plist.Count = ps.Count;
            Normalise(s);
            return true;
        }

        public static void Normalise(ShaderFX s)
        {
            var plist = s?.ParametersList;
            if (plist?.Parameters == null) return;

            for (int i = 0; i < plist.Parameters.Length; i++)
            {
                var p = plist.Parameters[i];
                if (p.DataType == 0) p.Unknown_1h = (byte)(i + 2);
            }
            int offset = 160;
            for (int i = plist.Parameters.Length - 1; i >= 0; i--)
            {
                var p = plist.Parameters[i];
                if (p.DataType != 0)
                {
                    p.Unknown_1h = (byte)offset;
                    offset += p.DataType;
                }
            }

            plist.Count = plist.Parameters.Length;
            s.ParameterCount = (byte)plist.Count;
            s.ParameterSize = plist.ParametersSize;
            s.ParameterDataSize = plist.ParametersDataSize;
            s.TextureParametersCount = plist.TextureParamsCount;
        }

        public static void SetBucket(ShaderFX s, byte bucket)
        {
            if (s == null) return;
            s.RenderBucket = bucket;
            s.RenderBucketMask = (1u << bucket) | 0xFF00u;
        }

        public static ShaderFX Clone(ShaderFX src)
        {
            if (src == null) return null;
            var s = new ShaderFX
            {
                Name = src.Name,
                FileName = src.FileName,
                RenderBucket = src.RenderBucket,
                RenderBucketMask = src.RenderBucketMask,
                Unknown_Ch = src.Unknown_Ch,
                Unknown_12h = src.Unknown_12h,
                Unknown_1Ch = src.Unknown_1Ch,
                Unknown_24h = src.Unknown_24h,
                Unknown_26h = src.Unknown_26h,
                Unknown_28h = src.Unknown_28h,
            };

            var sp = src.ParametersList;
            var block = new ShaderParametersBlock { Owner = s };
            if (sp?.Parameters != null)
            {
                var ps = new ShaderParameter[sp.Parameters.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var o = sp.Parameters[i];
                    ps[i] = new ShaderParameter
                    {
                        DataType = o.DataType,
                        Unknown_1h = o.Unknown_1h,
                        Unknown_2h = o.Unknown_2h,
                        Unknown_4h = o.Unknown_4h,
                        Data = o.Data is Vector4[] arr ? arr.ToArray() : o.Data,
                    };
                }
                block.Parameters = ps;
                block.Hashes = sp.Hashes?.ToArray() ?? Array.Empty<MetaName>();
                block.Count = ps.Length;
            }
            else
            {
                block.Parameters = Array.Empty<ShaderParameter>();
                block.Hashes = Array.Empty<MetaName>();
            }
            s.ParametersList = block;
            Normalise(s);
            return s;
        }

        public static void CopyInto(ShaderFX src, ShaderFX dst)
        {
            if (src == null || dst == null) return;
            var copy = Clone(src);
            dst.Name = copy.Name;
            dst.FileName = copy.FileName;
            dst.RenderBucket = copy.RenderBucket;
            dst.RenderBucketMask = copy.RenderBucketMask;
            dst.ParametersList = copy.ParametersList;
            if (dst.ParametersList != null) dst.ParametersList.Owner = dst;
            Normalise(dst);
        }

        public static MaterialRef MakeUnique(Scene scene, MaterialRef mat, IEnumerable<RenderMesh> onlyMeshes)
        {
            if (mat?.Shader == null || mat.Group?.Shaders == null) return null;
            var meshes = onlyMeshes?.Where(m => m?.Geometry != null && m.Shader == mat.Shader).ToList();
            if (meshes == null || meshes.Count == 0) return null;

            var copy = Clone(mat.Shader);
            CopyTextureMemory(mat.Shader, copy);
            var arr = (mat.Group.Shaders.data_items ?? Array.Empty<ShaderFX>()).ToList();
            arr.Add(copy);
            mat.Group.Shaders.data_items = arr.ToArray();
            int newIndex = arr.Count - 1;

            foreach (var m in meshes)
            {
                m.Geometry.Shader = copy;
                m.Geometry.ShaderID = (ushort)newIndex;
                m.Shader = copy;
                m.ShaderIndex = newIndex;
            }

            var result = new MaterialRef
            {
                File = mat.File,
                Drawable = mat.Drawable,
                Group = mat.Group,
                Index = newIndex,
                Shader = copy,
            };
            result.Meshes.AddRange(MeshesUsing(scene, copy));
            return result;
        }

        public static MatTexResolution Resolve(Scene scene, MaterialRef mat, TextureBase tb,
            Func<uint, uint, GameTexture> gameFallback = null, uint txdContext = 0,
            Func<uint, GameTexture> localFallback = null)
        {
            var r = new MatTexResolution { Source = MatTexSource.Missing };
            if (tb == null) return r;

            if (tb is GameTexture gt && gt.Data?.FullData != null)
            {
                r.Source = MatTexSource.Embedded;
                r.Texture = gt;
                r.SourceName = "embedded in this drawable";
                return r;
            }

            var embedded = mat?.EmbeddedDict?.Lookup(tb.NameHash);
            if (embedded?.Data?.FullData != null)
            {
                r.Source = MatTexSource.Embedded;
                r.Texture = embedded;
                r.SourceName = "embedded in this drawable";
                return r;
            }

            if (scene != null)
            {
                foreach (var it in scene.ImportedTextures)
                {
                    if (it != null && it.NameHash == tb.NameHash && it.Data?.FullData != null)
                    {
                        r.Source = MatTexSource.Imported;
                        r.Texture = it;
                        r.SourceName = "imported file";
                        return r;
                    }
                }

                for (int i = 0; i < scene.LoadedYtds.Count; i++)
                {
                    var t = scene.LoadedYtds[i]?.TextureDict?.Lookup(tb.NameHash);
                    if (t?.Data?.FullData != null)
                    {
                        r.Source = MatTexSource.LoadedYtd;
                        r.Texture = t;
                        r.SourceName = i < scene.LoadedYtdPaths.Count
                            ? System.IO.Path.GetFileName(scene.LoadedYtdPaths[i]) : "loaded YTD";
                        return r;
                    }
                }
            }

            var localTex = localFallback?.Invoke(tb.NameHash);
            if (localTex?.Data?.FullData != null)
            {
                r.Source = MatTexSource.LoadedYtd;
                r.Texture = localTex;
                r.SourceName = "YTD beside the import";
                return r;
            }

            var found = gameFallback?.Invoke(tb.NameHash, txdContext);
            if (found?.Data?.FullData != null)
            {
                r.Source = MatTexSource.GameArchive;
                r.Texture = found;
                r.SourceName = "game archives";
                return r;
            }

            r.SourceName = "not found";
            return r;
        }

        public static bool EmbedTexture(MaterialRef mat, GameTexture tex)
        {
            if (mat?.Group == null || tex?.Data?.FullData == null) return false;

            var dict = mat.Group.TextureDictionary;
            if (dict == null)
            {
                dict = new TextureDictionary();
                mat.Group.TextureDictionary = dict;
            }

            var list = (dict.Textures?.data_items ?? Array.Empty<GameTexture>()).ToList();
            list.RemoveAll(t => t != null && t.NameHash == tex.NameHash);
            list.Add(tex);
            dict.BuildFromTextureList(list);

            foreach (var h in ParamHashes(mat.Shader).ToList())
            {
                var tb = GetTexture(mat.Shader, h);
                if (tb != null && tb.NameHash == tex.NameHash) SetTexture(mat.Shader, h, tex);
            }
            return true;
        }

        public static bool UnembedTexture(List<MaterialRef> allInFile, MaterialRef mat, GameTexture tex,
            out string reason)
        {
            reason = null;
            if (mat?.Group == null || tex == null) { reason = "nothing to unembed"; return false; }
            var dict = mat.Group.TextureDictionary;
            if (dict?.Textures?.data_items == null) { reason = "this drawable has no embedded dictionary"; return false; }

            var users = new List<string>();
            foreach (var other in allInFile ?? new List<MaterialRef>())
            {
                if (other == null || other.Shader == mat.Shader) continue;
                foreach (var h in ParamHashes(other.Shader).ToList())
                {
                    var tb = GetTexture(other.Shader, h);
                    if (tb != null && tb.NameHash == tex.NameHash) { users.Add(other.Label); break; }
                }
            }
            if (users.Count > 0)
            {
                reason = $"still used by {string.Join(", ", users.Take(3))}" +
                         (users.Count > 3 ? $" and {users.Count - 3} more" : "");
                return false;
            }

            var list = dict.Textures.data_items.Where(t => t != null && t.NameHash != tex.NameHash).ToList();
            dict.BuildFromTextureList(list);

            foreach (var h in ParamHashes(mat.Shader).ToList())
            {
                var tb = GetTexture(mat.Shader, h);
                if (tb != null && tb.NameHash == tex.NameHash)
                    SetTexture(mat.Shader, h, MakeTextureRef(tex.Name));
            }
            return true;
        }

        public static bool IsEmbedded(MaterialRef mat, uint nameHash) =>
            mat?.EmbeddedDict?.Lookup(nameHash)?.Data?.FullData != null;

        public static void Refresh(Scene scene, ModelRenderer renderer, ShaderFX shader)
        {
            if (renderer == null || shader == null) return;
            foreach (var m in MeshesUsing(scene, shader)) renderer.RefreshMaterial(m);
        }

        public static int EditSerial_U13;

        public static void MarkDirty(MaterialRef mat)
        {
            EditSerial_U13++;
            if (mat?.File != null && !mat.File.ReadOnly) mat.File.Dirty = true;
        }
    }
}

