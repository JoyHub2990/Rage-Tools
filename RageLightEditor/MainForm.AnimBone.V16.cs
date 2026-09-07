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
        private sealed class BonePose_V16
        {
            public ushort BoneId;
            public List<Vector4> Position;
            public List<Vector4> Rotation;
            public List<Vector4> Scale;
        }

        private List<BonePose_V16> bonePoses_V16;
        private int boneFrames_V16;
        private float boneDuration_V16;
        private bool bonePosed_V16;
        private int boneSkinnedSkipped_V16;

        public string BoneAnimName_V16 { get; private set; } = "";

        private int YcdBonePreview_V16(YcdFile ycd)
        {
            bonePoses_V16 = null;
            boneFrames_V16 = 0;
            boneDuration_V16 = 0f;
            BoneAnimName_V16 = "";
            if (ycd?.ClipMapEntries == null) return 0;

            foreach (var cme in ycd.ClipMapEntries)
            {
                foreach (var anim in UvAnimYcd.AnimationsOf(cme?.Clip))
                {
                    List<YcdBoneAnim_V6.BoneTrack_V6> tracks;
                    try { tracks = YcdBoneAnim_V6.Read(anim); } catch { continue; }
                    if (tracks == null || tracks.Count == 0) continue;
                    if (!tracks.Any(t => t.Track == YcdBoneAnim_V6.TrackPosition ||
                                         t.Track == YcdBoneAnim_V6.TrackRotation ||
                                         t.Track == YcdBoneAnim_V6.TrackScale)) continue;

                    var byBone = new Dictionary<ushort, BonePose_V16>();
                    foreach (var t in tracks)
                    {
                        if (t?.Frames == null || t.Frames.Count == 0) continue;
                        if (!byBone.TryGetValue(t.BoneId, out var bp))
                            byBone[t.BoneId] = bp = new BonePose_V16 { BoneId = t.BoneId };
                        if (t.Track == YcdBoneAnim_V6.TrackPosition) bp.Position = t.Frames;
                        else if (t.Track == YcdBoneAnim_V6.TrackRotation) bp.Rotation = t.Frames;
                        else if (t.Track == YcdBoneAnim_V6.TrackScale) bp.Scale = t.Frames;
                    }
                    if (byBone.Count == 0) continue;

                    bonePoses_V16 = byBone.Values.ToList();
                    boneFrames_V16 = Math.Max((int)anim.Frames, 2);
                    boneDuration_V16 = anim.Duration > 0.0001f ? anim.Duration : 1f;
                    BoneAnimName_V16 = cme?.Clip?.Name ?? cme?.Clip?.ShortName ?? anim.Hash.ToString();

                    if (AnimEd != null)
                    {
                        AnimEd.Clip ??= new UvAnimClip();
                        AnimEd.Clip.Duration = boneDuration_V16;
                        AnimEd.Clip.Fps = Math.Max(1, (int)Math.Round((boneFrames_V16 - 1) / Math.Max(boneDuration_V16, 1e-4f)));
                        AnimEd.Clip.Loop = true;
                        AnimEd.Time = 0;
                    }
                    Console.WriteLine($"YCD bone anim '{BoneAnimName_V16}': {bonePoses_V16.Count} bone(s), " +
                                      $"{boneFrames_V16} frames over {boneDuration_V16:0.###} s");
                    return bonePoses_V16.Count;
                }
            }
            return 0;
        }

        private void SampleBone_V16(BonePose_V16 bp, float t, Bone bone)
        {
            float span = Math.Max(boneDuration_V16, 1e-4f);
            float pos = Math.Max(0f, Math.Min(t, span)) / span * (boneFrames_V16 - 1);
            int f0 = (int)Math.Floor(pos);
            int f1 = Math.Min(f0 + 1, boneFrames_V16 - 1);
            float k = pos - f0;

            Vector4 At(List<Vector4> l, int f) => l == null || l.Count == 0 ? Vector4.Zero : l[Math.Min(f, l.Count - 1)];

            if (bp.Rotation != null && bp.Rotation.Count > 0)
            {
                var a = At(bp.Rotation, f0); var b = At(bp.Rotation, f1);
                var qa = new Quaternion(a.X, a.Y, a.Z, a.W);
                var qb = new Quaternion(b.X, b.Y, b.Z, b.W);
                if (qa.LengthSquared() > 1e-8f && qb.LengthSquared() > 1e-8f)
                {
                    qa.Normalize(); qb.Normalize();
                    var q = Quaternion.Slerp(qa, qb, k);
                    q.Normalize();
                    bone.AnimRotation = q;
                }
            }
            if (bp.Position != null && bp.Position.Count > 0)
            {
                var a = At(bp.Position, f0); var b = At(bp.Position, f1);
                bone.AnimTranslation = Vector3.Lerp(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z), k);
            }
            if (bp.Scale != null && bp.Scale.Count > 0)
            {
                var a = At(bp.Scale, f0); var b = At(bp.Scale, f1);
                var s = Vector3.Lerp(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z), k);
                if (s.LengthSquared() > 1e-8f) bone.AnimScale = s;
            }
        }

        private void AnimApplyBonePose_V16()
        {
            var a = AnimEd;
            var sc = AnimScene_U6;
            var files = sc?.Files;
            if (files == null || files.Count == 0) { bonePosed_V16 = false; return; }

            bool play = bonePoses_V16 != null && bonePoses_V16.Count > 0 && a != null && a.PreviewEnabled;
            float t = a?.Clip != null ? a.Clip.Wrap(a.Time) : (a?.Time ?? 0f);
            bool posedAny = false;

            foreach (var lf in files)
            {
                var skel = lf?.Skeleton;
                var model = lf?.Model;
                if (skel?.Bones?.Items == null || model?.Meshes == null) continue;
                if (PoseOne_V16(skel, model, play, t)) posedAny = true;
            }
            bonePosed_V16 = play && posedAny;
        }

        private bool PoseOne_V16(Skeleton skel, Rendering.RenderModel model, bool play, float t)
        {
            if (!play)
            {
                if (bonePosed_V16)
                {
                    foreach (var b in skel.Bones.Items) b?.ResetAnimTransform();
                    PoseMeshes_V16(model, skel);
                }
                return false;
            }

            var bones = skel.Bones.Items;

            foreach (var b in bones)
            {
                if (b == null) continue;
                b.AnimRotation = b.Rotation;
                b.AnimTranslation = b.Translation;
                b.AnimScale = b.Scale;
            }
            foreach (var bp in bonePoses_V16)
            {
                var bone = bones.FirstOrDefault(b => b != null && b.Tag == bp.BoneId)
                        ?? (bp.BoneId < bones.Length ? bones[bp.BoneId] : null);
                if (bone != null) SampleBone_V16(bp, t, bone);
            }
            foreach (var b in bones) b?.UpdateAnimTransform();

            PoseMeshes_V16(model, skel);
            return true;
        }

        private void PoseMeshes_V16(RenderModel model, Skeleton skel)
        {
            var bones = skel?.Bones?.Items;
            if (bones == null || model?.Meshes == null) return;
            boneSkinnedSkipped_V16 = 0;
            var bounds = new BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            bool any = false;
            foreach (var mesh in model.Meshes)
            {
                if (mesh == null) continue;
                int bi = mesh.BoneIndex_V16;
                if (bi < 0 || bi >= bones.Length) continue;
                var b = bones[bi];
                if (b == null) continue;
                mesh.Transform = b.AnimTransform * mesh.BaseTransform_V16;
                any = true;
            }
            if (!any) return;
            foreach (var mesh in model.Meshes)
                if (mesh != null) bounds = BoundingBox.Merge(bounds, mesh.WorldBounds);
            if (bounds.Minimum.X <= bounds.Maximum.X) model.Bounds = bounds;
        }

        private void SeqTest_AnimBone_V16(Action<string, bool, string> check)
        {
            boneFrames_V16 = 2;
            boneDuration_V16 = 1f;
            var bp = new BonePose_V16
            {
                BoneId = 0,
                Rotation = new List<Vector4>
                {
                    new Vector4(0, 0, 0, 1),
                    new Vector4(0, 0, (float)Math.Sin(Math.PI / 4), (float)Math.Cos(Math.PI / 4)),
                },
            };
            var bone = new Bone { Rotation = Quaternion.Identity, Translation = Vector3.Zero, Scale = Vector3.One };
            bone.ResetAnimTransform();

            SampleBone_V16(bp, 0f, bone);
            check("v16 bone: at t=0 it is the first frame",
                  Math.Abs(bone.AnimRotation.W - 1f) < 1e-4f, $"w {bone.AnimRotation.W:0.####}");

            SampleBone_V16(bp, 0.5f, bone);
            float half = (float)Math.Cos(Math.PI / 8);
            check("v16 bone: halfway between two frames is halfway through the turn",
                  Math.Abs(bone.AnimRotation.W - half) < 1e-3f, $"w {bone.AnimRotation.W:0.####} want {half:0.####}");

            SampleBone_V16(bp, 1f, bone);
            check("v16 bone: at the end it is the last frame",
                  Math.Abs(bone.AnimRotation.Z - (float)Math.Sin(Math.PI / 4)) < 1e-3f,
                  $"z {bone.AnimRotation.Z:0.####}");

            var moved = new Bone { Rotation = Quaternion.Identity, Translation = new Vector3(3, 4, 5), Scale = Vector3.One };
            moved.AnimTranslation = moved.Translation; moved.AnimScale = moved.Scale;
            SampleBone_V16(bp, 0.5f, moved);
            check("v16 bone: a rotation-only track leaves the bone where the model puts it",
                  (moved.AnimTranslation - new Vector3(3, 4, 5)).Length() < 1e-4f, moved.AnimTranslation.ToString());

            bonePoses_V16 = null; boneFrames_V16 = 0; boneDuration_V16 = 0f;
        }
    }
}

