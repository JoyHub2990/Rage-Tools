using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool weapScanDone_V21;

        partial void OnWorldTick_WeaponScan_V21()
        {
            if (weapScanDone_V21) return;
            var filter = Environment.GetEnvironmentVariable("RLE_WEAPSCAN");
            if (string.IsNullOrEmpty(filter)) return;
            var c = gameFiles?.Cache;
            if (c?.YdrDict == null || !gameFiles.Ready) return;
            weapScanDone_V21 = true;

            void Scan(string kind, Dictionary<uint, RpfFileEntry> dict)
            {
                int shown = 0;
                foreach (var kv in dict.ToList())
                {
                    var fe = kv.Value;
                    if (fe == null) continue;
                    var nm = fe.Name ?? "";
                    if (nm.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++shown > 10) { Console.WriteLine($"WEAPSCAN ... more {kind} matches not shown"); break; }

                    Skeleton skel = null;
                    DrawableBase dr = null;
                    try
                    {
                        if (kind == "ydr") dr = c.RpfMan.GetFile<YdrFile>(fe)?.Drawable;
                        else dr = c.RpfMan.GetFile<YftFile>(fe)?.Fragment?.Drawable;
                        skel = dr?.Skeleton;
                    }
                    catch { }
                    if (shown == 1 && dr?.ShaderGroup?.Shaders?.data_items != null)
                    {
                        var models = Rendering.ModelRenderer.HighestLod(dr);
                        foreach (var sh in dr.ShaderGroup.Shaders.data_items)
                        {
                            var probe = new Rendering.RenderMesh();
                            try { Rendering.ModelRenderer.ClassifyDrawPublic(probe, sh); } catch { }
                            Console.WriteLine($"WEAPSCAN   material {sh.Name} ({sh.FileName}) bucket {sh.RenderBucket} -> mode {probe.AlphaMode} decal {probe.DecalKind} never {probe.NeverDraw}");
                        }
                        if (models != null)
                            foreach (var dm in models)
                            {
                                if (dm?.Geometries == null) continue;
                                Console.WriteLine($"WEAPSCAN   model bone {dm.BoneIndex} skin {dm.HasSkin} geoms {dm.Geometries.Length}: " +
                                    string.Join(", ", dm.Geometries.Select(g => $"shader#{g.ShaderID} v{g.VertexData?.VertexCount ?? 0}")));
                            }
                    }
                    var bones = skel?.Bones?.Items;
                    if (bones == null || bones.Length == 0)
                    {
                        Console.WriteLine($"WEAPSCAN {fe.Name} [{kind}] no skeleton  path={fe.Path}");
                        continue;
                    }
                    var names = bones.Select(b => b?.Name ?? "?").ToList();
                    var wap = names.Where(n => n.StartsWith("WAP", StringComparison.OrdinalIgnoreCase) ||
                                               n.StartsWith("gun_", StringComparison.OrdinalIgnoreCase)).ToList();
                    Console.WriteLine($"WEAPSCAN {fe.Name} [{kind}] {bones.Length} bone(s): {string.Join(", ", names.Take(24))}" +
                                      (names.Count > 24 ? " ..." : ""));
                    if (wap.Count > 0)
                        Console.WriteLine($"WEAPSCAN   attach bones: {string.Join(", ", wap)}");
                }
            }

            Scan("ydr", c.YdrDict);
            Scan("yft", c.YftDict);
        }
    }
}

