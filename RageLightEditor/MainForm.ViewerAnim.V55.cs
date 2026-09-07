using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly List<ArchiveBrowser.Entry> mvAnimDicts_V55 = new List<ArchiveBrowser.Entry>();
        private YcdFile mvAnimYcd_V55;
        private ClipMapEntry mvAnimCme_V55;
        private Animation mvAnim_V55;
        private readonly List<(int Slot, byte Track, ushort BoneId)> mvAnimSlots_V55 = new List<(int, byte, ushort)>();
        private float mvAnimDuration_V55;
        private double mvAnimStart_V55;
        private bool mvAnimPosed_V55;
        private float mvAnimFreeze_V55 = -1f;
        private bool mvAnimEnvDone_V55;

        private Skeleton ViewerSkeleton_V55() =>
            modelViewPreview?.Ydr?.Drawable?.Skeleton
            ?? modelViewPreview?.Yft?.Fragment?.Drawable?.Skeleton
            ?? (modelViewPreview?.Ydd?.Drawables != null && modelViewPreview.Ydd.Drawables.Length > 0
                ? modelViewPreview.Ydd.Drawables[0]?.Skeleton : null);

        private void SetupViewerAnim_V55()
        {
            mvAnimDicts_V55.Clear();
            mvAnimYcd_V55 = null;
            mvAnimCme_V55 = null;
            mvAnim_V55 = null;
            mvAnimSlots_V55.Clear();
            mvAnimPosed_V55 = false;

            var skel = ViewerSkeleton_V55();
            bool supported = skel?.Bones?.Items != null && skel.Bones.Items.Length > 0;
            var names = Array.Empty<string>();
            if (supported && panel?.Archive != null && panel.Archive.Ready)
            {
                var stem = (ModelView.Title ?? "").ToLowerInvariant();
                int dot = stem.IndexOf('.');
                if (dot > 0) stem = stem.Substring(0, dot);
                if (stem.Length >= 3)
                {
                    var hits = new List<ArchiveBrowser.Entry>();
                    panel.Archive.Find(stem, new[] { ".ycd" }, hits, 24);
                    if (hits.Count == 0 && stem.Length > 5)
                    {
                        var trimmed = stem.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_', 'a', 'b');
                        if (trimmed.Length >= 4) panel.Archive.Find(trimmed, new[] { ".ycd" }, hits, 24);
                    }
                    mvAnimDicts_V55.AddRange(hits);
                    names = hits.Select(h => h.File?.Name ?? "?").ToArray();
                }
            }
            ModelView.ResetAnim_V55(supported, names);
        }

        private void LoadViewerAnimDict_V55(int index)
        {
            mvAnimYcd_V55 = null;
            mvAnimCme_V55 = null;
            mvAnim_V55 = null;
            mvAnimSlots_V55.Clear();
            mvAnimPosed_V55 = false;
            ModelView.AnimClipNames_V55 = Array.Empty<string>();
            ModelView.AnimClipSel_V55 = -1;
            ModelView.AnimDictSel_V55 = index;
            if (index < 0 || index >= mvAnimDicts_V55.Count) { RestoreViewerPose_V55(); return; }
            try
            {
                var fe = mvAnimDicts_V55[index].File;
                var data = ArchiveBrowser.Extract(fe);
                mvAnimYcd_V55 = RpfFile.GetFile<YcdFile>(fe, data);
                var cmes = mvAnimYcd_V55?.ClipMapEntries ?? Array.Empty<ClipMapEntry>();
                ModelView.AnimClipNames_V55 = cmes
                    .Select(c => c?.Clip?.ShortName ?? c?.Clip?.Name ?? (c?.Hash.ToString() ?? "?"))
                    .ToArray();
                ModelView.AnimInfo_V55 = $"{ModelView.AnimClipNames_V55.Length} clip(s) in {fe.Name} - pick one";
                Console.WriteLine($"MVANIM dict {fe.Name}: {ModelView.AnimClipNames_V55.Length} clip(s)");
            }
            catch (Exception ex)
            {
                ModelView.AnimInfo_V55 = "could not read the dictionary: " + ex.Message;
            }
        }

        private void LoadViewerAnimClip_V55(int index)
        {
            mvAnimCme_V55 = null;
            mvAnim_V55 = null;
            mvAnimSlots_V55.Clear();
            ModelView.AnimClipSel_V55 = index;
            var cmes = mvAnimYcd_V55?.ClipMapEntries;
            if (cmes == null || index < 0 || index >= cmes.Length) { RestoreViewerPose_V55(); return; }
            mvAnimCme_V55 = cmes[index];

            foreach (var anim in UvAnimYcd.AnimationsOf(mvAnimCme_V55?.Clip))
            {
                var ids = anim?.BoneIds?.data_items;
                if (ids == null) continue;
                for (int i = 0; i < ids.Length; i++)
                    mvAnimSlots_V55.Add((i, ids[i].Track, ids[i].BoneId));
                if (mvAnimSlots_V55.Count == 0) continue;
                mvAnim_V55 = anim;
                mvAnimDuration_V55 = anim.Duration > 0.0001f ? anim.Duration : 1f;
                break;
            }
            mvAnimStart_V55 = clock.Elapsed.TotalSeconds;

            int bones = mvAnimSlots_V55.Count(s => s.Track <= 2);
            int uvs = mvAnimSlots_V55.Count(s => s.Track == UvAnimYcd.TrackUV0 || s.Track == UvAnimYcd.TrackUV1);
            int other = mvAnimSlots_V55.Count - bones - uvs;
            ModelView.AnimInfo_V55 = mvAnim_V55 == null
                ? "this clip carries no animation data this build can read"
                : $"{bones} bone track(s), {uvs} UV track(s)" + (other > 0 ? $", {other} other" : "") +
                  $"  ·  {mvAnim_V55.Frames} frames over {mvAnimDuration_V55:0.##} s";
            Console.WriteLine($"MVANIM clip {ModelView.AnimClipNames_V55.ElementAtOrDefault(index)}: {ModelView.AnimInfo_V55}");
        }

        private void RestoreViewerPose_V55()
        {
            if (!mvAnimPosed_V55) return;
            mvAnimPosed_V55 = false;
            var skel = ViewerSkeleton_V55();
            var model = modelViewPreview?.Model;
            if (skel?.Bones?.Items == null || model == null) return;
            foreach (var b in skel.Bones.Items) b?.ResetAnimTransform();
            PoseMeshes_V16(model, skel);
            if (model.Meshes != null)
                foreach (var mesh in model.Meshes)
                {
                    if (mesh == null) continue;
                    mesh.AnimUV0 = new Vector4(1, 0, 0, 0);
                    mesh.AnimUV1 = new Vector4(0, 1, 0, 0);
                }
        }

        private void ServiceViewerAnim_V55()
        {
            if (ModelView == null) return;
            ServiceViewerAnimEnv_V55();
            if (ModelView.RequestAnimDict_V55 != -2)
            {
                int i = ModelView.RequestAnimDict_V55;
                ModelView.RequestAnimDict_V55 = -2;
                LoadViewerAnimDict_V55(i);
            }
            if (ModelView.RequestAnimClip_V55 != -2)
            {
                int i = ModelView.RequestAnimClip_V55;
                ModelView.RequestAnimClip_V55 = -2;
                LoadViewerAnimClip_V55(i);
            }
        }

        private void TickViewerAnim_V55()
        {
            var anim = mvAnim_V55;
            var skel = ViewerSkeleton_V55();
            var model = modelViewPreview?.Model;
            if (anim == null || skel?.Bones?.Items == null || model == null)
            {
                if (mvAnimPosed_V55) RestoreViewerPose_V55();
                return;
            }

            float t = mvAnimFreeze_V55 >= 0f ? mvAnimFreeze_V55
                : (float)((clock.Elapsed.TotalSeconds - mvAnimStart_V55) % Math.Max(mvAnimDuration_V55, 0.0001f));

            var bones = skel.Bones.Items;
            foreach (var b in bones)
            {
                if (b == null) continue;
                b.AnimRotation = b.Rotation;
                b.AnimTranslation = b.Translation;
                b.AnimScale = b.Scale;
            }

            var uv0 = new Vector4(1, 0, 0, 0);
            var uv1 = new Vector4(0, 1, 0, 0);
            bool anyUv = false;
            try
            {
                var pos = anim.GetFramePosition(t);
                foreach (var (slot, track, boneId) in mvAnimSlots_V55)
                {
                    Vector4 v;
                    try { v = anim.EvaluateVector4(pos, slot, track == 1); }
                    catch { continue; }
                    if (track == UvAnimYcd.TrackUV0) { uv0 = v; anyUv = true; continue; }
                    if (track == UvAnimYcd.TrackUV1) { uv1 = v; anyUv = true; continue; }
                    if (track > 2) continue;
                    var bone = bones.FirstOrDefault(b => b != null && b.Tag == boneId)
                            ?? (boneId < bones.Length ? bones[boneId] : null);
                    if (bone == null) continue;
                    if (track == 0) bone.AnimTranslation = new Vector3(v.X, v.Y, v.Z);
                    else if (track == 1)
                    {
                        var q = new Quaternion(v.X, v.Y, v.Z, v.W);
                        if (q.LengthSquared() > 1e-8f) { q.Normalize(); bone.AnimRotation = q; }
                    }
                    else if (track == 2)
                    {
                        var s = new Vector3(v.X, v.Y, v.Z);
                        if (s.LengthSquared() > 1e-8f) bone.AnimScale = s;
                    }
                }
            }
            catch { return; }

            if (!ModelView.AnimRootMotion_V55)
            {
                var root = bones.FirstOrDefault(b => b != null && b.Parent == null);
                if (root != null) root.AnimTranslation = root.Translation;
            }

            foreach (var b in bones) b?.UpdateAnimTransform();
            PoseMeshes_V16(model, skel);

            if (anyUv && model.Meshes != null)
                foreach (var mesh in model.Meshes)
                {
                    if (mesh == null) continue;
                    mesh.AnimUV0 = uv0;
                    mesh.AnimUV1 = uv1;
                }
            if (!mvAnimPosed_V55)
                Console.WriteLine($"MVANIM playing: {mvAnimSlots_V55.Count} track(s) live at t={t:0.###}s" +
                                  (anyUv ? $" uv0=({uv0.X:0.##},{uv0.Y:0.##},{uv0.Z:0.##})" : ""));
            mvAnimPosed_V55 = true;
        }

        private void ServiceViewerAnimEnv_V55()
        {
            if (mvAnimEnvDone_V55) return;
            var spec = Environment.GetEnvironmentVariable("RLE_MVANIM");
            if (string.IsNullOrWhiteSpace(spec)) { mvAnimEnvDone_V55 = true; return; }
            if (ModelView?.Visible != true || ModelView.AnimDictNames_V55.Length == 0) return;
            mvAnimEnvDone_V55 = true;
            var parts = spec.Split('|');
            int dict = 0, clip = 0;
            float t = -1f;
            if (parts.Length > 0) int.TryParse(parts[0], out dict);
            if (parts.Length > 1) int.TryParse(parts[1], out clip);
            if (parts.Length > 2) float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                                 System.Globalization.CultureInfo.InvariantCulture, out t);
            LoadViewerAnimDict_V55(dict);
            LoadViewerAnimClip_V55(clip);
            mvAnimFreeze_V55 = t;
            Console.WriteLine($"MVANIM env: dict {dict} clip {clip} t {t} -> {ModelView.AnimInfo_V55}");
            if (screenshotPath != null) screenshotFrames = Math.Max(screenshotFrames, 6);
        }
    }
}
