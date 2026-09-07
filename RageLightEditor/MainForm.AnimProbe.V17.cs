using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void AnimProbe_V17(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            string key = modelName.Trim().ToLowerInvariant();
            if (key.EndsWith(".ydr")) key = key.Substring(0, key.Length - 4);
            Console.WriteLine("ANIMPROBE ==================== " + key);

            if (gameFiles?.Cache == null) { Console.WriteLine("ANIMPROBE archives not open"); return; }

            DrawableBase drawable = null;
            string diskYdr = null, diskYcd = null;
            foreach (var root in new[] { AppContext.BaseDirectory, "C:\\Users\\GS\\Desktop\\RAGE_Tools" })
            {
                var a = System.IO.Path.Combine(root, key + ".ydr");
                var b = System.IO.Path.Combine(root, "clip@" + key + ".ycd");
                if (diskYdr == null && System.IO.File.Exists(a)) diskYdr = a;
                if (diskYcd == null && System.IO.File.Exists(b)) diskYcd = b;
            }
            if (diskYdr != null)
            {
                try
                {
                    var yd = new YdrFile();
                    yd.Load(System.IO.File.ReadAllBytes(diskYdr));
                    drawable = yd.Drawable;
                    Console.WriteLine($"ANIMPROBE ydr from disk: {diskYdr} -> {(drawable == null ? "NULL" : "ok")}");
                }
                catch (Exception ex) { Console.WriteLine("ANIMPROBE disk ydr failed: " + ex.Message); }
            }
            if (drawable == null)
            {
                try { drawable = gameFiles.GetDrawable(JenkHash.GenHash(key), out _); } catch (Exception ex)
                { Console.WriteLine("ANIMPROBE drawable failed: " + ex.Message); }
            }
            if (drawable == null)
            {
                var ydrEntry = FindArchiveEntry_V17(key + ".ydr");
                if (ydrEntry != null)
                {
                    try
                    {
                        var yd = RpfFile.GetFile<YdrFile>(ydrEntry, ydrEntry.File.ExtractFile(ydrEntry));
                        drawable = yd?.Drawable;
                        Console.WriteLine("ANIMPROBE ydr straight from " + ydrEntry.Path);
                    }
                    catch (Exception ex) { Console.WriteLine("ANIMPROBE ydr read failed: " + ex.Message); }
                }
            }
            if (drawable == null) { Console.WriteLine("ANIMPROBE no drawable called " + key); return; }

            var skel = drawable.Skeleton;
            var bones = skel?.Bones?.Items;
            Console.WriteLine($"ANIMPROBE ydr: skeleton={(skel == null ? "NONE" : bones?.Length + " bone(s)")}");
            if (bones != null)
                foreach (var b in bones.Take(24))
                    Console.WriteLine($"ANIMPROBE   bone[{b.Index}] tag={b.Tag} parent={b.ParentIndex} '{b.Name}'");

            var models = Rendering.ModelRenderer.HighestLod(drawable);
            if (models != null)
                foreach (var m in models)
                {
                    Console.WriteLine($"ANIMPROBE   model boneIndex={m.BoneIndex} hasSkin={m.HasSkin} geoms={m.Geometries?.Length ?? 0}");
                    foreach (var g in m.Geometries ?? Array.Empty<DrawableGeometry>())
                        Console.WriteLine($"ANIMPROBE     geom verts={g?.VertexData?.VertexCount ?? 0} " +
                                          $"stride={g?.VertexData?.VertexStride ?? 0} type={g?.VertexData?.VertexType} " +
                                          $"boneIds={(g?.BoneIds?.Length ?? 0)}");
                }

            YcdFile ycd = null;
            if (diskYcd != null)
            {
                try
                {
                    ycd = new YcdFile();
                    RpfFile.LoadResourceFile(ycd, System.IO.File.ReadAllBytes(diskYcd), 46);
                    Console.WriteLine($"ANIMPROBE ycd from disk: {diskYcd} -> {(ycd.ClipMapEntries == null ? "NULL" : ycd.ClipMapEntries.Length + " clips")}");
                }
                catch (Exception ex) { Console.WriteLine("ANIMPROBE disk ycd failed: " + ex.Message); }
            }
            if (ycd?.ClipMapEntries == null)
            {
                var ycdEntry = FindArchiveEntry_V17("clip@" + key + ".ycd") ?? FindArchiveEntry_V17(key + ".ycd");
                if (ycdEntry == null) { Console.WriteLine("ANIMPROBE no clip@" + key + ".ycd anywhere"); return; }
                try { ycd = RpfFile.GetFile<YcdFile>(ycdEntry, ycdEntry.File.ExtractFile(ycdEntry)); }
                catch (Exception ex) { Console.WriteLine("ANIMPROBE ycd read failed: " + ex.Message); return; }
                Console.WriteLine($"ANIMPROBE ycd: {ycdEntry.Path}");
            }
            Console.WriteLine($"ANIMPROBE ycd has {ycd?.ClipMapEntries?.Length ?? 0} clip(s)");

            foreach (var cme in ycd.ClipMapEntries ?? Array.Empty<ClipMapEntry>())
            {
                string cn = cme?.Clip?.Name ?? cme?.Clip?.ShortName ?? "?";
                foreach (var anim in UvAnimYcd.AnimationsOf(cme?.Clip))
                {
                    var ids = anim?.BoneIds?.data_items;
                    Console.WriteLine($"ANIMPROBE   clip '{cn}' anim {anim.Hash}: frames={anim.Frames} " +
                                      $"dur={anim.Duration:0.###} seqLimit={anim.SequenceFrameLimit} " +
                                      $"slots={(ids?.Length ?? 0)}");
                    if (ids == null) continue;

                    for (int i = 0; i < ids.Length; i++)
                    {
                        Vector4 e0 = Vector4.Zero, eMid = Vector4.Zero;
                        try
                        {
                            e0 = anim.EvaluateVector4(anim.GetFramePosition(0f), i, false);
                            eMid = anim.EvaluateVector4(anim.GetFramePosition(anim.Duration * 0.37f), i, false);
                        }
                        catch (Exception ex) { Console.WriteLine("ANIMPROBE     evaluate threw: " + ex.Message); }

                        Vector4 r0 = Vector4.Zero, rMid = Vector4.Zero;
                        try
                        {
                            r0 = YcdDocument_V6.ValueAt_V6(anim, i, 0);
                            rMid = YcdDocument_V6.ValueAt_V6(anim, i, (int)(anim.Frames * 0.37f));
                        }
                        catch (Exception ex) { Console.WriteLine("ANIMPROBE     ValueAt threw: " + ex.Message); }

                        bool evalMoves = (e0 - eMid).Length() > 1e-5f;
                        bool rawMoves = (r0 - rMid).Length() > 1e-5f;
                        string trackName = ids[i].Track == 0 ? "position" : ids[i].Track == 1 ? "rotation"
                                         : ids[i].Track == 2 ? "scale" : ids[i].Track == 17 ? "UV0"
                                         : ids[i].Track == 18 ? "UV1" : ids[i].Track.ToString();
                        bool haveBone = bones != null && (bones.Any(b => b != null && b.Tag == ids[i].BoneId));
                        Console.WriteLine(
                            $"ANIMPROBE     slot {i}: boneId={ids[i].BoneId} ({(haveBone ? "MATCHES a bone tag" : "no bone with that tag")}) " +
                            $"track={ids[i].Track} ({trackName})");
                        Console.WriteLine(
                            $"ANIMPROBE       evaluator: {Fmt_V17(e0)} -> {Fmt_V17(eMid)}  {(evalMoves ? "MOVES" : "still")}");
                        Console.WriteLine(
                            $"ANIMPROBE       ValueAt  : {Fmt_V17(r0)} -> {Fmt_V17(rMid)}  {(rawMoves ? "MOVES" : "still")}");
                    }
                }
            }

            int found = YcdBonePreview_V16(ycd);
            Console.WriteLine($"ANIMPROBE preview would drive {found} bone(s); name '{BoneAnimName_V16}'");
            var uv = UvAnimImport_V16.FromYcd_V16(ycd, key);
            Console.WriteLine($"ANIMPROBE uv import: {uv.Message}");
            if (diskYdr != null && diskYcd != null)
            {
                try
                {
                    bool opened = AnimOpenPath_U6(diskYdr);
                    var lf2 = AnimScene_U6?.ActiveFile ?? AnimScene_U6?.Files?.FirstOrDefault();
                    Console.WriteLine($"ANIMPROBE ui: open={opened} file={(lf2 == null ? "NULL" : lf2.Name)} " +
                                      $"skeleton={(lf2?.Skeleton == null ? "NULL" : lf2.Skeleton.Bones?.Items?.Length + " bones")} " +
                                      $"meshes={lf2?.Model?.Meshes?.Count ?? -1} " +
                                      $"boneIdx=[{string.Join(",", (lf2?.Model?.Meshes ?? new List<Rendering.RenderMesh>()).Select(m => m.BoneIndex_V16))}]");

                    if ((lf2?.Model?.Meshes?.Count ?? 0) == 0)
                    {
                        var d2 = lf2?.Ydr?.Drawable;
                        var lod = d2 == null ? null : Rendering.ModelRenderer.HighestLod(d2);
                        Console.WriteLine($"ANIMPROBE ui: lf.Ydr={(lf2?.Ydr == null ? "NULL" : "ok")} drawable={(d2 == null ? "NULL" : "ok")} " +
                                          $"highestLod={(lod == null ? "NULL" : lod.Length + " models")}");
                        if (d2 != null)
                        {
                            var again = modelRenderer.BuildFromDrawable(d2, "retry", Matrix.Identity);
                            Console.WriteLine($"ANIMPROBE ui: a direct rebuild of that same drawable gives {again?.Meshes?.Count ?? -1} mesh(es)");
                            again?.Dispose();
                        }
                    }

                    bool ok2 = OpenYcd_V6(diskYcd, out var msg2);
                    Console.WriteLine($"ANIMPROBE ui: ycd open={ok2} '{msg2}' poses={bonePoses_V16?.Count ?? -1} " +
                                      $"dur={boneDuration_V16:0.###} frames={boneFrames_V16}");

                    var meshes2 = lf2?.Model?.Meshes;
                    if (meshes2 != null && meshes2.Count > 0)
                    {
                        AnimEd.Time = 0f;
                        AnimApplyPreview_U6();
                        var t0 = meshes2.Select(m => m.Transform).ToArray();
                        AnimEd.Time = 2.5f;
                        AnimApplyPreview_U6();
                        var t1 = meshes2.Select(m => m.Transform).ToArray();
                        int moved2 = 0;
                        for (int i = 0; i < t0.Length; i++)
                        {
                            float d = 0;
                            for (int r = 0; r < 16; r++) d += Math.Abs(t0[i][r] - t1[i][r]);
                            if (d > 1e-4f) moved2++;
                        }
                        Console.WriteLine($"ANIMPROBE ui: {moved2} of {t0.Length} mesh(es) moved through the REAL per-frame path");
                    }
                }
                catch (Exception ex) { Console.WriteLine("ANIMPROBE ui threw: " + ex); }
            }

            try
            {
                var model = modelRenderer.BuildFromDrawable(drawable, key, Matrix.Identity);
                if (model == null || model.Meshes.Count == 0) { Console.WriteLine("ANIMPROBE e2e: no meshes built"); }
                else
                {
                    var sc = AnimScene_U6;
                    sc.CloseAllFiles();
                    var lf = sc.AddImportedProp(null, null, null, model, drawable.Skeleton, null,
                        Matrix.Identity, 1, true, key, null, fromMlo: false, drawable: drawable);
                    sc.ActiveFile = lf;
                    Console.WriteLine($"ANIMPROBE e2e: {model.Meshes.Count} mesh(es), skeleton={(lf?.Skeleton == null ? "NULL" : "ok")}, " +
                                      $"boneIdx=[{string.Join(",", model.Meshes.Select(m => m.BoneIndex_V16))}]");

                    AnimEd.PreviewEnabled = true;
                    AnimEd.Clip.Duration = boneDuration_V16 > 0.01f ? boneDuration_V16 : 4f;

                    AnimEd.Time = 0f;
                    AnimApplyBonePose_V16();
                    var at0 = model.Meshes.Select(m => m.Transform).ToArray();

                    AnimEd.Time = AnimEd.Clip.Duration * 0.37f;
                    AnimApplyBonePose_V16();
                    var atMid = model.Meshes.Select(m => m.Transform).ToArray();

                    int moved = 0;
                    for (int i = 0; i < at0.Length; i++)
                    {
                        float d = 0;
                        for (int r = 0; r < 16; r++) d += Math.Abs(at0[i][r] - atMid[i][r]);
                        if (d > 1e-4f) moved++;
                    }
                    Console.WriteLine($"ANIMPROBE e2e: {moved} of {at0.Length} mesh transform(s) CHANGED between t=0 and t={AnimEd.Time:0.##}");
                    if (moved == 0)
                    {
                        var eb = lf?.Skeleton?.Bones?.Items;
                        Console.WriteLine($"ANIMPROBE e2e: poses={bonePoses_V16?.Count ?? -1} frames={boneFrames_V16} dur={boneDuration_V16:0.###} " +
                                          $"posed={bonePosed_V16} previewEnabled={AnimEd.PreviewEnabled}");
                        if (eb != null)
                            foreach (var b in eb)
                                Console.WriteLine($"ANIMPROBE e2e:   bone[{b.Index}] tag={b.Tag} animRot=({b.AnimRotation.X:0.###},{b.AnimRotation.Y:0.###},{b.AnimRotation.Z:0.###},{b.AnimRotation.W:0.###})");
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("ANIMPROBE e2e threw: " + ex); }

            Console.WriteLine("ANIMPROBE ==================== end");
        }

        private void AnimFind_V17(int want)
        {
            var cache = gameFiles?.Cache;
            if (cache?.AllRpfs == null) { Console.WriteLine("ANIMFIND archives not open"); return; }

            var ydrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ycds = new List<RpfFileEntry>();
            foreach (var r in cache.AllRpfs)
                foreach (var e in r?.AllEntries ?? new List<RpfEntry>())
                {
                    if (!(e is RpfFileEntry fe) || fe.NameLower == null) continue;
                    if (fe.NameLower.EndsWith(".ydr")) ydrs.Add(fe.NameLower.Substring(0, fe.NameLower.Length - 4));
                    else if (fe.NameLower.EndsWith(".ycd")) ycds.Add(fe);
                }
            Console.WriteLine($"ANIMFIND {ydrs.Count:N0} .ydr, {ycds.Count:N0} .ycd in the archives");

            int hits = 0, read = 0, fails = 0;
            foreach (var fe in ycds)
            {
                if (hits >= want || read > 4000) break;
                string bare = fe.NameLower.Substring(0, fe.NameLower.Length - 4);
                string model = bare.StartsWith("clip@") ? bare.Substring(5) : bare;
                bool haveModel = ydrs.Contains(model);

                YcdFile ycd;
                try
                {
                    var data = fe.File?.ExtractFile(fe);
                    if (data == null)
                    {
                        if (fails++ < 5) Console.WriteLine($"ANIMFIND  no bytes for {fe.Name} (File={(fe.File == null ? "null" : fe.File.Name)})");
                        continue;
                    }
                    ycd = RpfFile.GetFile<YcdFile>(fe, data);
                    if (ycd?.ClipMapEntries == null) continue;
                    read++;
                }
                catch (Exception ex) { if (fails++ < 5) Console.WriteLine($"ANIMFIND  {fe.Name} threw: {ex.Message}"); continue; }

                int boneSlots = 0, frames = 0;
                foreach (var cme in ycd.ClipMapEntries ?? Array.Empty<ClipMapEntry>())
                    foreach (var anim in UvAnimYcd.AnimationsOf(cme?.Clip))
                        foreach (var b in anim?.BoneIds?.data_items ?? Array.Empty<AnimationBoneId>())
                            if (b.Track <= 2) { boneSlots++; frames = Math.Max(frames, (int)anim.Frames); }
                if (boneSlots == 0) continue;

                hits++;
                Console.WriteLine($"ANIMFIND HIT {fe.Name}: model '{model}' {(haveModel ? "HAS a .ydr" : "no .ydr of that name")}, " +
                                  $"{boneSlots} bone slot(s), {frames} frames - {fe.Path}");
            }
            Console.WriteLine($"ANIMFIND done: {read} read, {fails} failed, {hits} with bone tracks");
        }

        private static string Fmt_V17(Vector4 v) =>
            $"({v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}, {v.W:0.####})";

        private RpfFileEntry FindArchiveEntry_V17(string nameLower)
        {
            foreach (var r in gameFiles?.Cache?.AllRpfs ?? new List<RpfFile>())
                foreach (var e in r?.AllEntries ?? new List<RpfEntry>())
                    if (e is RpfFileEntry fe && fe.NameLower == nameLower) return fe;
            return null;
        }
    }
}

