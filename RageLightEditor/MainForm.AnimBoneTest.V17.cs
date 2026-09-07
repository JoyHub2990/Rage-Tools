using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static (string ydr, string ycd) FindBoneFixture_V17()
        {
            foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var ycd in Directory.GetFiles(dir, "clip@*.ycd"))
                {
                    var name = Path.GetFileNameWithoutExtension(ycd).Substring("clip@".Length);
                    var ydr = Path.Combine(dir, name + ".ydr");
                    if (File.Exists(ydr)) return (ydr, ycd);
                }
            }
            return (null, null);
        }

        private void SeqTest_AnimBoneEndToEnd_V17(Action<string, bool, string> check)
        {
            var (ydrPath, ycdPath) = FindBoneFixture_V17();
            if (ydrPath == null)
            {
                Console.WriteLine("  v17 fan: (skipped - no <model>.ydr with a clip@<model>.ycd beside it)");
                return;
            }
            string name = Path.GetFileNameWithoutExtension(ydrPath);

            try
            {
                if (!AnimOpenPath_U6(ydrPath))
                { check($"v17 fan ({name}): the model opens", false, "open failed"); return; }

                var lf = AnimScene_U6?.ActiveFile ?? AnimScene_U6?.Files?.FirstOrDefault();
                var meshes = lf?.Model?.Meshes;
                check($"v17 fan ({name}): the model opens, with its skeleton and its meshes",
                      lf?.Skeleton?.Bones?.Items != null && meshes != null && meshes.Count > 0,
                      $"{lf?.Skeleton?.Bones?.Items?.Length ?? 0} bone(s), {meshes?.Count ?? 0} mesh(es)");
                if (meshes == null || meshes.Count == 0) return;

                check($"v17 fan ({name}): ...and the scene knows which file that is",
                      AnimScene_U6?.ActiveFile != null, "ActiveFile set");

                check($"v17 fan ({name}): ...and its meshes know which bone they ride on",
                      meshes.Any(m => m.BoneIndex_V16 >= 0),
                      $"bone indices [{string.Join(",", meshes.Select(m => m.BoneIndex_V16))}]");

                if (!OpenYcd_V6(ycdPath, out var msg))
                { check($"v17 fan ({name}): the clip dictionary opens", false, msg); return; }

                check($"v17 fan ({name}): the clip dictionary opens and has bone tracks",
                      bonePoses_V16 != null && bonePoses_V16.Count > 0,
                      $"{bonePoses_V16?.Count ?? 0} bone(s) over {boneDuration_V16:0.##} s");
                if (bonePoses_V16 == null || bonePoses_V16.Count == 0) return;

                AnimEd.PreviewEnabled = true;
                AnimEd.Time = 0f;
                AnimApplyPreview_U6();
                var before = meshes.Select(m => m.Transform).ToArray();

                AnimEd.Time = Math.Max(boneDuration_V16, 0.1f) * 0.37f;
                AnimApplyPreview_U6();
                var after = meshes.Select(m => m.Transform).ToArray();

                int moved = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    float d = 0;
                    for (int r = 0; r < 16; r++) d += Math.Abs(before[i][r] - after[i][r]);
                    if (d > 1e-4f) moved++;
                }
                check($"v17 fan ({name}): A MESH ACTUALLY MOVES through the real per-frame preview",
                      moved > 0, $"{moved} of {before.Length} mesh(es) moved");

                AnimEd.PreviewEnabled = false;
                AnimApplyPreview_U6();
                var off = meshes.Select(m => m.Transform).ToArray();
                int restored = 0;
                for (int i = 0; i < off.Length; i++)
                {
                    float d = 0;
                    for (int r = 0; r < 16; r++) d += Math.Abs(off[i][r] - before[i][r]);
                    if (d < 1e-3f) restored++;
                }
                check($"v17 fan ({name}): ...and turning the preview off puts it back to its rest pose",
                      restored == off.Length, $"{restored} of {off.Length} back where they started");
                AnimEd.PreviewEnabled = true;
            }
            catch (Exception ex)
            {
                check($"v17 fan ({name}): the end-to-end preview runs without throwing", false, ex.Message);
            }
        }
    }
}

