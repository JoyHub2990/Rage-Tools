using SharpDX;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        public BoundingBox ReflectionMirrorBounds_T6;

        private static readonly string MirrorScanMode_T6 = System.Environment.GetEnvironmentVariable("RLE_MIRRORSCAN");
        private static readonly bool MirrorScan_T6 = MirrorScanMode_T6 == "1" || MirrorScanMode_T6 == "2";
        private static int mirrorScanLeft_T6 = 40;
        private static readonly System.Collections.Generic.HashSet<string> mirrorSeenOnce_T6 = new System.Collections.Generic.HashSet<string>();

        private void ScanAllMirrors_T6(System.Collections.Generic.IEnumerable<RenderModel> models, Vector3 eye)
        {
            if (MirrorScanMode_T6 != "2" || models == null) return;
            foreach (var m in models)
                foreach (var mesh in m.Meshes)
                {
                    if (mesh == null || !mesh.IsMirror) continue;
                    var c = mesh.WorldSphere.Center;
                    var key = $"{c.X:0},{c.Y:0},{c.Z:0}";
                    if (!mirrorSeenOnce_T6.Add(key) || mirrorSeenOnce_T6.Count > 60) continue;
                    System.Console.WriteLine($"MIRRORALL at {c.X:0.00},{c.Y:0.00},{c.Z:0.00} r {mesh.WorldSphere.Radius:0.##} m, " +
                                             $"{Vector3.Distance(eye, c):0.#} m from the eye");
                }
        }

        private static BoundingBox MirrorWorldBounds_T6(System.Collections.Generic.List<RenderMesh> meshes)
        {
            var box = new BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            if (meshes != null)
                foreach (var m in meshes)
                {
                    var b = m?.WorldBounds ?? default;
                    if (b.Maximum.X <= b.Minimum.X) continue;
                    box.Minimum = Vector3.Min(box.Minimum, b.Minimum);
                    box.Maximum = Vector3.Max(box.Maximum, b.Maximum);
                }
            if (MirrorScan_T6 && mirrorScanLeft_T6-- > 0 && box.Maximum.X > box.Minimum.X)
            {
                var c = (box.Minimum + box.Maximum) * 0.5f;
                var s = box.Maximum - box.Minimum;
                System.Console.WriteLine($"MIRRORSCAN centre {c.X:0.00},{c.Y:0.00},{c.Z:0.00} " +
                                         $"size {s.X:0.##}x{s.Y:0.##}x{s.Z:0.##} m, {meshes?.Count ?? 0} mesh(es)");
            }
            return box;
        }
    }
}

