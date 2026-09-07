using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public static class DrawTest
    {
        public static int Run(string file)
        {
            DrawableBase drawable;
            try
            {
                var data = File.ReadAllBytes(file);
                if (file.EndsWith(".yft", StringComparison.OrdinalIgnoreCase))
                {
                    var yft = new YftFile();
                    yft.Load(data);
                    drawable = yft.Fragment?.Drawable;
                }
                else
                {
                    var ydr = new YdrFile();
                    ydr.Load(data);
                    drawable = ydr.Drawable;
                }
            }
            catch (Exception ex) { Console.WriteLine("DRAWTEST load failed: " + ex.Message); return 1; }

            if (drawable == null) { Console.WriteLine("DRAWTEST no drawable"); return 1; }

            var dm = drawable.DrawableModels;
            int high = dm?.High?.Length ?? 0, med = dm?.Med?.Length ?? 0;
            int low = dm?.Low?.Length ?? 0, vlow = dm?.VLow?.Length ?? 0;
            int all = drawable.AllModels?.Length ?? 0;

            var chosen = ModelRenderer.HighestLod(drawable);
            int drawn = chosen?.Length ?? 0;

            Console.WriteLine($"DRAWTEST lod: high={high} med={med} low={low} vlow={vlow} all={all} drawn={drawn}");
            string lodVerdict = (high > 0 && drawn == high) || (high == 0 && drawn <= all) ? "OK" : "WRONG";
            Console.WriteLine($"DRAWTEST lodpick: {lodVerdict} (extra LOD models skipped: {Math.Max(all - drawn, 0)})");

            int opaque = 0, cutout = 0, blend = 0, dbl = 0, geoms = 0;
            var byShader = new SortedDictionary<string, string>();
            foreach (var model in chosen ?? Array.Empty<DrawableModel>())
            {
                foreach (var geom in model?.Geometries ?? Array.Empty<DrawableGeometry>())
                {
                    var sh = geom?.Shader;
                    if (sh == null) continue;
                    geoms++;
                    var mesh = new RenderMesh();
                    ModelRenderer.ClassifyDrawPublic(mesh, sh);
                    switch (mesh.AlphaMode)
                    {
                        case GeomAlphaMode.Cutout: cutout++; break;
                        case GeomAlphaMode.Decal: blend++; break;
                        default: opaque++; break;
                    }
                    if (mesh.DoubleSided) dbl++;
                    byShader[$"{sh.FileName.Hash}"] = $"{ModeName(mesh.AlphaMode)}{(mesh.DoubleSided ? "+2sided" : "")} bucket={sh.RenderBucket}";
                }
            }

            Console.WriteLine($"DRAWTEST class: geoms={geoms} opaque={opaque} cutout={cutout} blended={blend} doublesided={dbl}");
            foreach (var kv in byShader) Console.WriteLine($"DRAWTEST   sps {kv.Key}: {kv.Value}");
            return 0;
        }

        private static string ModeName(GeomAlphaMode m)
        {
            switch (m)
            {
                case GeomAlphaMode.Cutout: return "Cutout";
                case GeomAlphaMode.Decal: return "Decal";
                case GeomAlphaMode.Additive: return "Additive";
                case GeomAlphaMode.Glass: return "Glass";
                case GeomAlphaMode.Water: return "Water";
                default: return "Opaque";
            }
        }

        public static int RunYmap(string file)
        {
            YmapFile ymap;
            try
            {
                ymap = new YmapFile();
                ymap.Load(File.ReadAllBytes(file));
            }
            catch (Exception ex) { Console.WriteLine("LODTEST load failed: " + ex.Message); return 1; }

            var ents = ymap.AllEntities ?? ymap.RootEntities;
            if (ents == null || ents.Length == 0) { Console.WriteLine("LODTEST no entities"); return 1; }

            var counts = new SortedDictionary<string, int>();
            int kept = 0;
            foreach (var e in ents)
            {
                if (e == null) continue;
                var lod = e._CEntityDef.lodLevel;
                var key = lod.ToString();
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
                if (lod == rage__eLodType.LODTYPES_DEPTH_HD || lod == rage__eLodType.LODTYPES_DEPTH_ORPHANHD) kept++;
            }

            Console.WriteLine($"LODTEST entities={ents.Length} kept={kept} skipped={ents.Length - kept}");
            foreach (var kv in counts) Console.WriteLine($"LODTEST   {kv.Key}: {kv.Value}");
            return 0;
        }
    }
}

