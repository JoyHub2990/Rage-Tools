using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int navReadyFrame_T5 = -1, navReadyCells_T5, navReadyPolys_T5;
        private int navWorldSettledFrame_T5 = -1, navWorldMeshes_T5;
        private bool navReadySaid_T5;

        private void NavReady_T5()
        {
            if (navReadySaid_T5 || Environment.GetEnvironmentVariable("RLE_NAVREADY") != "1") return;
            if (panel == null || !panel.NavMode || NavEd == null) return;

            int cells = NavEd.Docs.Count;
            int polys = 0;
            foreach (var d in NavEd.Docs) polys += d?.Ynv?.Polys?.Count ?? 0;
            if (navReadyFrame_T5 < 0 && cells > 0 && NavEd.StreamStatus_S4 != null &&
                NavEd.StreamStatus_S4.StartsWith("following", StringComparison.OrdinalIgnoreCase))
            { navReadyFrame_T5 = worldWarmup; navReadyCells_T5 = cells; navReadyPolys_T5 = polys; }

            int meshes = worldRender?.MeshesDrawn ?? 0;
            if (meshes > navWorldMeshes_T5) { navWorldMeshes_T5 = meshes; navWorldSettledFrame_T5 = worldWarmup; }

            if (worldWarmup < 300) return;
            navReadySaid_T5 = true;
            Console.WriteLine($"NAVREADY cells live at warm-up frame {navReadyFrame_T5} ({navReadyCells_T5} cells, {navReadyPolys_T5} polys); " +
                              $"the world's mesh count was still climbing at frame {navWorldSettledFrame_T5} ({navWorldMeshes_T5} meshes). " +
                              $"{(navReadyFrame_T5 >= 0 && navReadyFrame_T5 <= navWorldSettledFrame_T5 ? "The mesh is there before the ground is." : "THE GROUND WON THE RACE.")}");
        }
    }
}

