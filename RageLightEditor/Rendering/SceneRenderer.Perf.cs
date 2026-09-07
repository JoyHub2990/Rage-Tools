using System;
using System.Collections.Generic;
using System.Text;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        public static GpuProfiler Profiler;

        public static void ProfBegin(string name) => Profiler?.Begin(name);
        public static void ProfEnd(string name) => Profiler?.End(name);

        public int LastMirrorPlaneCount => heldPasses;
        public SharpDX.Plane LastMirrorPlane(int i) => heldPlanes[i];

        private static readonly string skipBlend = Environment.GetEnvironmentVariable("RLE_SKIPBLEND") ?? "";
        private static bool SkipBlend_J4(RenderMesh mesh)
        {
            if (skipBlend.Length == 0) return false;
            switch (mesh.AlphaMode)
            {
                case GeomAlphaMode.Glass: return skipBlend.Contains("glass");
                case GeomAlphaMode.Decal: return skipBlend.Contains("decal");
                case GeomAlphaMode.Water: return skipBlend.Contains("water");
                case GeomAlphaMode.Additive: return skipBlend.Contains("additive");
                default: return false;
            }
        }

        public struct PassStats
        {
            public int OpaqueDraws, CutoutDraws, BlendDraws;
            public long OpaqueIndices, CutoutIndices, BlendIndices;
            public override string ToString() =>
                $"opaque {OpaqueDraws} draws/{OpaqueIndices / 3000}k tris, cutout {CutoutDraws}/{CutoutIndices / 3000}k, blend {BlendDraws}/{BlendIndices / 3000}k";
        }
        public PassStats MainStats, ReflectionStats;
        private bool statsReflection;
        public static readonly bool PassDump = Environment.GetEnvironmentVariable("RLE_PASSDUMP") == "1";
        private List<RenderMesh> passDumpList;
        public bool PassDumpDone;

        private void ResetPassStats_J4(bool reflection)
        {
            statsReflection = reflection;
            if (reflection) ReflectionStats = default; else MainStats = default;
            if (PassDump && !reflection && !PassDumpDone) passDumpList = new List<RenderMesh>();
        }

        private void CountDraw_J4(RenderMesh mesh)
        {
            ref var s = ref (statsReflection ? ref ReflectionStats : ref MainStats);
            switch (mesh.AlphaMode)
            {
                case GeomAlphaMode.Opaque: s.OpaqueDraws++; s.OpaqueIndices += mesh.IndexCount; break;
                case GeomAlphaMode.Cutout: s.CutoutDraws++; s.CutoutIndices += mesh.IndexCount; break;
                default: s.BlendDraws++; s.BlendIndices += mesh.IndexCount; break;
            }
            if (passDumpList != null && !statsReflection) passDumpList.Add(mesh);
        }

        public string PassDumpReport()
        {
            if (passDumpList == null || passDumpList.Count == 0) return null;
            PassDumpDone = true;
            var sb = new StringBuilder();
            sb.AppendLine($"PASSDUMP main: {MainStats}  reflection: {ReflectionStats}");
            foreach (var mode in new[] { GeomAlphaMode.Cutout, GeomAlphaMode.Opaque, GeomAlphaMode.Decal, GeomAlphaMode.Glass })
            {
                var l = passDumpList.FindAll(m => m.AlphaMode == mode);
                l.Sort((a, b) => b.IndexCount.CompareTo(a.IndexCount));
                sb.AppendLine($"  {mode}: {l.Count} draws");
                for (int i = 0; i < Math.Min(12, l.Count); i++)
                {
                    var m = l[i];
                    sb.AppendLine($"    {m.IndexCount / 3,8} tris  {m.ShaderName,-28} r {m.WorldSphere.Radius,7:0.0}  at {m.WorldSphere.Center.X:0},{m.WorldSphere.Center.Y:0},{m.WorldSphere.Center.Z:0}  {(m.DoubleSided ? "2s" : "  ")} {m.DiffuseName}");
                }
            }
            passDumpList = null;
            return sb.ToString();
        }
    }
}

