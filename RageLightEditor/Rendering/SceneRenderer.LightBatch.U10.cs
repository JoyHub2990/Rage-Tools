using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;

namespace RageLightEditor.Rendering
{
    public partial class SceneRenderer
    {
        private readonly List<(uint Idx, float Score)> meshLightAll_U10 = new List<(uint, float)>();
        private readonly List<uint> meshLightRest_U10 = new List<uint>();
        private readonly uint[] meshLightFirst_U10 = new uint[ObjectVars.MaxPerMeshLights];

        public int LightBatchesDrawn_U10;
        public int LightBatchesLastFrame_U10;

        private BlendState passBlend_U10;
        private BlendState passAdditive_U10;

        private static readonly bool lightBatchDbg_U10 = Environment.GetEnvironmentVariable("RLE_LIGHTBATCH") == "1";
        private int lightBatchFrame_U10;

        private void SetPassBlend_U10(BlendState pass, BlendState additive)
        {
            passBlend_U10 = pass;
            passAdditive_U10 = additive;
        }

        public static uint SplitLightBatches_U10(List<(uint Idx, float Score)> all, uint[] first, List<uint> rest)
        {
            rest.Clear();
            if (all == null || all.Count == 0) return 0;
            int cap = first.Length;
            if (all.Count > cap) all.Sort((a, b) => b.Score.CompareTo(a.Score));
            int n = Math.Min(all.Count, cap);
            for (int k = 0; k < n; k++) first[k] = all[k].Idx;
            for (int k = n; k < all.Count; k++) rest.Add(all[k].Idx);
            return (uint)n;
        }

        private bool CanBatchLights_U10(RenderMesh mesh)
        {
            if (FrameRenderMode != 0 || Wireframe || passAdditive_U10 == null) return false;
            if (mesh.IsMirror) return false;
            if (mesh.AlphaMode == GeomAlphaMode.Water) return false;
            if (mesh.AlphaMode == GeomAlphaMode.Decal && mesh.DecalKind == 5) return false;
            return true;
        }

        private void DrawLightBatches_U10(DeviceContext context, RenderMesh mesh, ref ObjectVars ov)
        {
            Array.Copy(meshLightScratch, meshLightFirst_U10, ObjectVars.MaxPerMeshLights);
            uint baseCount = ov.MeshLightCount;
            var l2 = ov.L2Params;

            context.OutputMerger.SetBlendState(passAdditive_U10);
            ov.L2Params = new Vector4(l2.X, l2.Y, 1.0f, l2.W);
            for (int start = 0; start < meshLightRest_U10.Count; start += ObjectVars.MaxPerMeshLights)
            {
                int k = Math.Min(ObjectVars.MaxPerMeshLights, meshLightRest_U10.Count - start);
                for (int j = 0; j < k; j++) meshLightScratch[j] = meshLightRest_U10[start + j];
                ov.MeshLightCount = (uint)k;
                objectCB.Update(context, ref ov);
                context.DrawIndexed(mesh.IndexCount, 0, 0);
                LightBatchesDrawn_U10++;
            }

            ov.L2Params = l2;
            Array.Copy(meshLightFirst_U10, meshLightScratch, ObjectVars.MaxPerMeshLights);
            ov.MeshLightCount = baseCount;
            objectCB.Update(context, ref ov);
            context.OutputMerger.SetBlendState(passBlend_U10);
        }

        private void NoteLightStats_U10()
        {
            LightBatchesLastFrame_U10 = LightBatchesDrawn_U10;
            if (!lightBatchDbg_U10) return;
            if (++lightBatchFrame_U10 % 90 != 0) return;
            Console.WriteLine($"LIGHTBATCH frame {lightBatchFrame_U10}: lights {frameLightCount}, " +
                              $"meshes past 64 lights {OverflowMeshes}, most on one mesh {MaxLightsPerMesh}, " +
                              $"extra light passes drawn {LightBatchesDrawn_U10}");
        }

        public static int SelfTestLightBatches_U10(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var all = new List<(uint Idx, float Score)>();
            for (uint i = 0; i < 150; i++) all.Add((i, i % 7 == 0 ? 100.0f + i : 1.0f + i * 0.01f));
            var first = new uint[ObjectVars.MaxPerMeshLights];
            var rest = new List<uint>();
            uint n = SplitLightBatches_U10(all, first, rest);

            Chk("u10 lights: a mesh reached by 150 lights keeps 64 in its first pass and 86 for the extra ones",
                n == 64 && rest.Count == 86, $"first {n}, rest {rest.Count}");

            var seen = new HashSet<uint>();
            for (int k = 0; k < n; k++) seen.Add(first[k]);
            foreach (var r in rest) seen.Add(r);
            Chk("u10 lights: every light is drawn exactly once across the passes",
                seen.Count == 150, seen.Count + " distinct of 150");

            bool strongFirst = true;
            for (int k = 0; k < n; k++) if (first[k] % 7 != 0 && first[k] < 150) { }
            int strongInFirst = 0;
            for (int k = 0; k < n; k++) if (first[k] % 7 == 0) strongInFirst++;
            int strongTotal = 0;
            for (uint i = 0; i < 150; i++) if (i % 7 == 0) strongTotal++;
            strongFirst = strongInFirst == strongTotal;
            Chk("u10 lights: the strongest lights land in the first pass",
                strongFirst, $"{strongInFirst} of {strongTotal} strong lights in pass one");

            var few = new List<(uint Idx, float Score)> { (5, 1f), (9, 2f), (2, 3f) };
            uint m = SplitLightBatches_U10(few, first, rest);
            Chk("u10 lights: three lights need no extra pass and keep their order",
                m == 3 && rest.Count == 0 && first[0] == 5 && first[1] == 9 && first[2] == 2, $"first {m}, rest {rest.Count}");

            Chk("u10 lights: the frame budget is no longer 2048",
                GpuLight.MaxLights >= 8192, GpuLight.MaxLights.ToString());
            return fails;
        }
    }
}
