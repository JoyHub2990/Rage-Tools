using System;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {

        partial void WorldPickBlocked_Q2(ref bool blocked);

        partial void WorldPickBlocked_Q2(ref bool blocked)
        {
            if (panel != null && panel.NavMode) blocked = true;
        }

        public bool NavBlocksWorldPicking_Q2
        {
            get { bool b = false; WorldPickBlocked_Q2(ref b); return b; }
        }

        private void NavWorkspaceGuard_Q2()
        {
            if (panel == null || !panel.NavMode) return;
            if (WorldEdit.Selection.HasValue) WorldEdit.Deselect();
        }

        private void NavProjectAdd_Q2(YnvFile ynv, string name)
        {
            if (ynv == null) return;
            bool wasVisible = ProjWin != null && ProjWin.Visible;
            ProjectAutoAddSpaceFile(ynv, name);
            if (ProjWin != null && !wasVisible && panel != null && panel.NavMode) ProjWin.Visible = false;
        }

        private bool navClickDone_Q2;

        private void NavHeadlessClick_Q2()
        {
            if (navClickDone_Q2) return;
            var env = Environment.GetEnvironmentVariable("RLE_NAVCLICK");
            if (env == null || !worldBuilt || deviceResources == null || panel == null) return;
            if (screenshotPath != null && worldWarmup < 380) return;
            navClickDone_Q2 = true;

            int x = deviceResources.Width / 2, y = deviceResources.Height / 2;
            var bits = env.Split(',');
            if (bits.Length >= 2 && int.TryParse(bits[0].Trim(), out int px) && int.TryParse(bits[1].Trim(), out int py))
            { x = px; y = py; }

            var wasWorld = WorldEdit.Selection;
            Click_T1(System.Windows.Forms.MouseButtons.Right, x, y);

            string nav = NavEd.SelectedPoly != null
                ? $"poly {NavEd.SelectedPoly.Index} ({NavMeshEditor.CatNames[(int)NavEd.CategoryOf(NavEd.SelectedPoly)]})"
                : NavEd.SelectedPoint != null ? $"point {NavEd.SelectedPoint.Index}"
                : NavEd.SelectedPortal != null ? $"portal {NavEd.SelectedPortal.Index}" : "nothing";
            Console.WriteLine($"NAVCLICK at {x},{y}: nav = {nav}; world = {WorldEdit.Selection.GetNameString("nothing")}" +
                              (wasWorld.HasValue ? "  (a world selection was cleared)" : ""));
            Console.WriteLine(WorldEdit.Selection.HasValue
                ? "NAVCLICK WORLD SELECTED - the NavMesh workspace picked something in the world"
                : "NAVCLICK WORLD CLEAN");
        }

        private void SeqTest_Q2(Action<string, bool, string> check)
        {
            check("the nav mesh's plain-English flag shelves and one-click types",
                  NavMeshEditor.SelfTestQ2() == 0, "see the NAVMESH lines above");

            var was = panel.Workspace;
            panel.SwitchWorkspace(LightPanel.Space.NavMesh);
            NavWorkspaceGuard_Q2();

            check("a world entity cannot be picked in the NavMesh workspace", NavBlocksWorldPicking_Q2, "");

            if (deviceResources != null && camera != null)
            {
                WorldEdit.LastStatus = "Q2 sentinel";
                WorldPickAt(8, 8);
                check("...and a viewport click never reaches the world picker at all",
                      WorldEdit.LastStatus == "Q2 sentinel" && !WorldEdit.Selection.HasValue,
                      WorldEdit.LastStatus);

                bool handled = false;
                NavMouseClick_P4(8, 8, ref handled);
                check("a click in this workspace belongs to the nav mesh", handled, "");
            }

            check("no world selection survives in the NavMesh workspace",
                  !WorldEdit.Selection.HasValue, WorldEdit.Selection.GetNameString("empty"));

            check("the NavMesh workspace hides the world's entity tools", panel.NavHidesWorldTools, "");

            panel.SwitchWorkspace(LightPanel.Space.World);
            NavWorkspaceGuard_Q2();
            check("and back in the World workspace entities can be picked again",
                  !NavBlocksWorldPicking_Q2, "");
            if (was != LightPanel.Space.World && screenshotPath == null)
            {
                panel.SwitchWorkspace(was);
                NavWorkspaceGuard_Q2();
            }
        }
    }
}

