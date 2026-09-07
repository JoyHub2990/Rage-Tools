using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class WorldRenderer
    {
        public sealed class UvClip_U7
        {
            public int Mat;
            public Animation Anim;
            public float Duration = 1.0f;
            public int Slot0 = -1, Slot1 = -1;
            public Vector4 Uv0 = new Vector4(1, 0, 0, 0);
            public Vector4 Uv1 = new Vector4(0, 1, 0, 0);
        }

        public sealed class ArchAnim_U7
        {
            public uint ArchHash;
            public string ArchName;
            public Animation Anim;
            public Skeleton Skel;
            public readonly List<(int Slot, byte Track, ushort BoneId)> Slots = new List<(int, byte, ushort)>();
            public readonly List<UvClip_U7> UvClips = new List<UvClip_U7>();
            public float Duration = 1.0f;
            public bool HasBones;
            public bool HasUv => UvClips.Count > 0;
            public string FoundBy = "";
            public int PosedFrame = -1;
        }

        public bool PlayAnimations = true;

        private readonly Dictionary<uint, (Archetype Arch, Skeleton Skel)> animCandidates =
            new Dictionary<uint, (Archetype, Skeleton)>();
        private readonly Dictionary<uint, ArchAnim_U7> archAnims = new Dictionary<uint, ArchAnim_U7>();
        private readonly Dictionary<uint, int> animTries = new Dictionary<uint, int>();
        private readonly HashSet<uint> animMisses = new HashSet<uint>();
        private readonly Dictionary<YmapEntityDef, ArchAnim_U7> animEntities =
            new Dictionary<YmapEntityDef, ArchAnim_U7>();
        private readonly List<YmapEntityDef> animQueue = new List<YmapEntityDef>();
        private readonly System.Diagnostics.Stopwatch animClock = System.Diagnostics.Stopwatch.StartNew();

        public int AnimatedEntities_U7 => animEntities.Count;
        public int AnimatedArchetypes_U7 => archAnims.Count;

        public IEnumerable<(YmapEntityDef Entity, ArchAnim_U7 Anim)> AnimatedLive_U7()
        {
            foreach (var kv in animEntities) yield return (kv.Key, kv.Value);
        }

        private int animBuildsSeen_U7, animClipSeen_U7;

        private void NoteAnimCandidate_U7(Archetype arch, DrawableBase drawable)
        {
            animBuildsSeen_U7++;
            if (arch == null || arch.ClipDict == 0) return;
            animClipSeen_U7++;
            uint hash = arch.Hash;
            if (archAnims.ContainsKey(hash) || animMisses.Contains(hash) || animCandidates.ContainsKey(hash)) return;
            animCandidates[hash] = (arch, drawable?.Skeleton);
        }

        private void NoteAnimEntity_U7(YmapEntityDef e)
        {
            if (e?.Archetype == null || e.Archetype.ClipDict == 0) return;
            if (animEntities.ContainsKey(e) || animMisses.Contains(e.Archetype.Hash)) return;
            animQueue.Add(e);
        }

        public static float AnimPhase_U7(double seconds, float duration)
        {
            if (duration <= 0.0001f) return 0.0f;
            return (float)(seconds % duration);
        }

        internal static ArchAnim_U7 ResolveArchAnim_U7(uint archHash, string archName, uint assetName,
                                                       YcdFile ycd, Skeleton skel)
        {
            var map = ycd?.ClipMap;
            if (map == null || map.Count == 0) return null;

            var aa = new ArchAnim_U7 { ArchHash = archHash, ArchName = archName, Skel = skel };

            ClipMapEntry cme = null;
            if (map.TryGetValue(archHash, out cme) && cme?.Clip != null) aa.FoundBy = "the archetype name";
            if (cme?.Clip == null && assetName != 0 && assetName != archHash &&
                map.TryGetValue(assetName, out cme) && cme?.Clip != null) aa.FoundBy = "the asset name";

            if (cme?.Clip != null && skel?.Bones?.Items != null)
            {
                foreach (var anim in UvAnimYcd.AnimationsOf(cme.Clip))
                {
                    var ids = anim?.BoneIds?.data_items;
                    if (ids == null) continue;
                    for (int i = 0; i < ids.Length; i++)
                        if (ids[i].Track <= 2) { aa.Slots.Add((i, ids[i].Track, ids[i].BoneId)); aa.HasBones = true; }
                    if (aa.Slots.Count == 0) continue;
                    aa.Anim = anim;
                    aa.Duration = anim.Duration > 0.0001f ? anim.Duration : 1.0f;
                    break;
                }
            }

            {
                for (int mat = 0; mat < 64; mat++)
                {
                    if (!map.TryGetValue((uint)(archHash + 1 + mat), out var uvCme) || uvCme?.Clip == null)
                        continue;
                    foreach (var anim in UvAnimYcd.AnimationsOf(uvCme.Clip))
                    {
                        var ids = anim?.BoneIds?.data_items;
                        if (ids == null) continue;
                        var uc = new UvClip_U7 { Mat = mat, Anim = anim };
                        for (int i = 0; i < ids.Length; i++)
                        {
                            if (ids[i].Track == UvAnimYcd.TrackUV0) uc.Slot0 = i;
                            else if (ids[i].Track == UvAnimYcd.TrackUV1) uc.Slot1 = i;
                        }
                        if (uc.Slot0 < 0 && uc.Slot1 < 0) continue;
                        uc.Duration = anim.Duration > 0.0001f ? anim.Duration : 1.0f;
                        aa.UvClips.Add(uc);
                        break;
                    }
                }
            }

            return aa.HasBones || aa.HasUv ? aa : null;
        }

        private void ServiceAnimCandidates_U7(GameFileManager game)
        {
            if (animCandidates.Count == 0) return;
            var cache = game?.Cache;
            if (cache == null) return;
            List<uint> done = null;
            foreach (var kv in animCandidates)
            {
                var (arch, skel) = kv.Value;
                YcdFile ycd = null;
                try { ycd = cache.GetYcd(arch.ClipDict); } catch { }
                if (ycd != null && !ycd.Loaded) game.EnsureLoaded(ycd);
                if (ycd != null && !ycd.Loaded)
                {
                    animTries.TryGetValue(kv.Key, out int tries);
                    if (tries < 20) { animTries[kv.Key] = tries + 1; continue; }
                }
                (done ??= new List<uint>()).Add(kv.Key);
                animTries.Remove(kv.Key);
                var aa = ycd?.Loaded == true
                    ? ResolveArchAnim_U7(kv.Key, arch.Name, arch._BaseArchetypeDef.assetName, ycd, skel)
                    : null;
                if (aa != null)
                {
                    archAnims[kv.Key] = aa;
                    if (animDbg_U7 && aa.HasBones)
                        try
                        {
                            var p0 = aa.Anim.GetFramePosition(0f);
                            var pm = aa.Anim.GetFramePosition(aa.Duration * 0.37f);
                            var tags = new System.Collections.Generic.HashSet<ushort>();
                            foreach (var b in aa.Skel?.Bones?.Items ?? Array.Empty<Bone>())
                                if (b != null) tags.Add((ushort)b.Tag);
                            foreach (var (slot, tr, bid) in aa.Slots)
                            {
                                if (tr != 1) continue;
                                var v0 = aa.Anim.EvaluateVector4(p0, slot, true);
                                var v1 = aa.Anim.EvaluateVector4(pm, slot, true);
                                Console.WriteLine($"WORLDANIM   {arch.Name} rot slot {slot} bone {bid} " +
                                                  $"inSkeleton={tags.Contains(bid)} moves={(v1 - v0).Length() > 0.001f}");
                            }
                        }
                        catch { }
                    if (animDbg_U7)
                    {
                        var body = aa.Anim ?? (aa.UvClips.Count > 0 ? aa.UvClips[0].Anim : null);
                        var dur = aa.Anim != null ? aa.Duration : (aa.UvClips.Count > 0 ? aa.UvClips[0].Duration : 0f);
                        Console.WriteLine($"WORLDANIM {arch.Name}: found by {(string.IsNullOrEmpty(aa.FoundBy) ? "its material UV keys" : aa.FoundBy)} in {arch.ClipDict} - " +
                                          $"{(aa.HasBones ? "bones" : "")}{(aa.HasBones && aa.HasUv ? " + " : "")}{(aa.HasUv ? aa.UvClips.Count + " UV material(s)" : "")}, " +
                                          $"{body?.Frames ?? 0} frames over {dur:0.##} s");
                    }
                }
                else
                {
                    animMisses.Add(kv.Key);
                    if (animDbg_U7)
                        Console.WriteLine($"WORLDANIM {arch.Name}: clip dictionary {arch.ClipDict} " +
                                          (ycd == null ? "is not in this install" : ycd.Loaded ? "has no clip this build can read" : "never finished loading"));
                }
            }
            if (done != null) foreach (var h in done) animCandidates.Remove(h);
        }

        private void DrainAnimQueue_U7()
        {
            if (animQueue.Count == 0) return;
            for (int i = animQueue.Count - 1; i >= 0; i--)
            {
                var e = animQueue[i];
                uint hash = e?.Archetype?.Hash ?? 0;
                if (hash == 0 || animMisses.Contains(hash) || !byEntity.ContainsKey(e))
                { animQueue.RemoveAt(i); continue; }
                if (!archAnims.TryGetValue(hash, out var aa)) continue;
                animEntities[e] = aa;
                animQueue.RemoveAt(i);
            }
        }

        private static readonly bool animDbg_U7 = Environment.GetEnvironmentVariable("RLE_WORLDANIM") == "1";

        private void PoseArch_U7(ArchAnim_U7 aa, double seconds)
        {
            if (aa.PosedFrame == frame) return;
            aa.PosedFrame = frame;

            var bones = aa.Skel?.Bones?.Items;
            if (aa.HasBones && bones != null && aa.Anim != null)
            {
                foreach (var b in bones)
                {
                    if (b == null) continue;
                    b.AnimRotation = b.Rotation;
                    b.AnimTranslation = b.Translation;
                    b.AnimScale = b.Scale;
                }
                try
                {
                    var pos = aa.Anim.GetFramePosition(AnimPhase_U7(seconds, aa.Duration));
                    foreach (var (slot, track, boneId) in aa.Slots)
                    {
                        Vector4 v;
                        try { v = aa.Anim.EvaluateVector4(pos, slot, track == 1); }
                        catch { continue; }
                        Bone bone = null;
                        foreach (var b in bones) if (b != null && b.Tag == boneId) { bone = b; break; }
                        if (bone == null && boneId < bones.Length) bone = bones[boneId];
                        if (bone == null) continue;
                        if (track == 0) bone.AnimTranslation = new Vector3(v.X, v.Y, v.Z);
                        else if (track == 1)
                        {
                            var q = new Quaternion(v.X, v.Y, v.Z, v.W);
                            if (q.LengthSquared() > 1e-8f) { q.Normalize(); bone.AnimRotation = q; }
                        }
                        else if (track == 2)
                        {
                            var sc = new Vector3(v.X, v.Y, v.Z);
                            if (sc.LengthSquared() > 1e-8f) bone.AnimScale = sc;
                        }
                    }
                }
                catch { }
                foreach (var b in bones) b?.UpdateAnimTransform();
            }

            foreach (var uc in aa.UvClips)
            {
                try
                {
                    var pos = uc.Anim.GetFramePosition(AnimPhase_U7(seconds, uc.Duration));
                    if (uc.Slot0 >= 0) uc.Uv0 = uc.Anim.EvaluateVector4(pos, uc.Slot0, false);
                    if (uc.Slot1 >= 0) uc.Uv1 = uc.Anim.EvaluateVector4(pos, uc.Slot1, false);
                }
                catch { }
            }
        }

        private void UpdateAnimations_U7(GameFileManager game)
        {
            if (animDbg_U7 && frame % 90 == 0)
                Console.WriteLine($"WORLDANIM state: builds {animBuildsSeen_U7} withClip {animClipSeen_U7} " +
                                  $"candidates {animCandidates.Count} anims {archAnims.Count} misses {animMisses.Count} " +
                                  $"queue {animQueue.Count} entities {animEntities.Count}");
            ServiceAnimCandidates_U7(game);
            DrainAnimQueue_U7();
            if (!PlayAnimations) return;

            double seconds = animClock.Elapsed.TotalSeconds;
            if (animEntities.Count == 0) return;
            List<YmapEntityDef> stale = null;
            foreach (var kv in animEntities)
            {
                var e = kv.Key;
                var aa = kv.Value;
                if (!byEntity.TryGetValue(e, out var meshes))
                { (stale ??= new List<YmapEntityDef>()).Add(e); continue; }

                PoseArch_U7(aa, seconds);

                var world = Matrix.Scaling(e.Scale)
                          * Matrix.RotationQuaternion(e.Orientation)
                          * Matrix.Translation(e.Position);
                var bones = aa.Skel?.Bones?.Items;
                foreach (var m in meshes)
                {
                    if (m == null) continue;
                    if (aa.HasBones && bones != null && m.BoneIndex_V16 >= 0 && m.BoneIndex_V16 < bones.Length)
                    {
                        var b = bones[m.BoneIndex_V16];
                        if (b != null)
                        {
                            m.Transform = b.AnimTransform * m.BaseTransform_V16 * world;
                            m.SetBoundsFromLocal();
                        }
                    }
                    foreach (var uc in aa.UvClips)
                        if (uc.Mat == m.ShaderIndex)
                        {
                            if (uc.Slot0 >= 0) m.AnimUV0 = uc.Uv0;
                            if (uc.Slot1 >= 0) m.AnimUV1 = uc.Uv1;
                            break;
                        }
                }
            }
            if (stale != null) foreach (var e in stale) animEntities.Remove(e);

            if (animDbg_U7 && frame % 90 == 0)
                foreach (var kv in animEntities)
                {
                    if (!byEntity.TryGetValue(kv.Key, out var ms) || ms.Count == 0) break;
                    var rows = new System.Text.StringBuilder();
                    int shown = 0;
                    foreach (var m in ms)
                    {
                        if (m == null || m.BoneIndex_V16 < 0 || shown >= 3) continue;
                        var rr = m.Transform.Row1;
                        rows.Append($"[b{m.BoneIndex_V16}]({rr.X:0.###},{rr.Y:0.###},{rr.Z:0.###}) ");
                        shown++;
                    }
                    var u = kv.Value.UvClips.Count > 0 ? kv.Value.UvClips[0].Uv0 : new Vector4(1, 0, 0, 0);
                    Console.WriteLine($"WORLDANIM live: {animEntities.Count} playing, {kv.Value.ArchName} " +
                                      $"rows {rows}uv0=({u.X:0.###},{u.Y:0.###},{u.Z:0.###})");
                    break;
                }
        }

        private void ForgetAnim_U7(YmapEntityDef e) => animEntities.Remove(e);

        private void ClearAnim_U7()
        {
            animEntities.Clear();
            animQueue.Clear();
        }

        public static int SelfTestAnim_U7(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            Chk("u7 anim: the phase walks the clip and wraps at its end",
                Math.Abs(AnimPhase_U7(0.25, 2.0f) - 0.25f) < 0.001f &&
                Math.Abs(AnimPhase_U7(2.25, 2.0f) - 0.25f) < 0.001f &&
                AnimPhase_U7(5.0, 0.0f) == 0.0f,
                $"{AnimPhase_U7(2.25, 2.0f):0.###} at 2.25 s of a 2 s clip");

            var ycd = new YcdFile();
            Chk("u7 anim: an empty dictionary resolves to nothing instead of throwing",
                ResolveArchAnim_U7(1, "x", 0, ycd, null) == null && ResolveArchAnim_U7(1, "x", 0, null, null) == null,
                "no clip map, no anim");
            return fails;
        }
    }
}
