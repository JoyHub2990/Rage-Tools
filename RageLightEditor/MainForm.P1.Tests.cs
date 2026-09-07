using System;
using System.Linq;
using System.Numerics;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_P1(Action<string, bool, string> check)
        {
            ModelViewerTest_P1(check);
            WorkspaceOrderTest_P1(check, panel);
            RpfIsolationTest_P1(check);
        }

        private static void ModelViewerTest_P1(Action<string, bool, string> check)
        {
            var mv = new ModelViewer();

            mv.ViewMode = 0; mv.Wireframe = false;
            bool modes = mv.RenderMode == 0;
            mv.ViewMode = 4; modes &= mv.RenderMode == 6;
            mv.ViewMode = 5; modes &= mv.RenderMode == 9;
            mv.Wireframe = true; modes &= mv.RenderMode == 8;
            mv.Wireframe = false;
            mv.ViewMode = 99; modes &= mv.RenderMode == 9;
            check("modelviewer shading modes", modes, $"mode {mv.RenderMode}, {ModelViewer.ViewModeLabels.Length} labels");

            mv.ViewMode = 0;
            mv.FrameModel();
            mv.ApplyCamera(1.6f);
            bool safe = mv.ModelRadius > 0.0f && !float.IsNaN(mv.Cam.Distance) &&
                        mv.Cam.Distance > 0.0f && mv.Cam.NearClip > 0.0f && mv.Cam.FarClip > mv.Cam.NearClip;
            check("modelviewer empty framing", safe,
                  $"radius {mv.ModelRadius:0.###}, dist {mv.Cam.Distance:0.###}, near {mv.Cam.NearClip:0.####}");

            float smallStep = mv.GridStep;
            check("modelviewer grid step", smallStep > 0.0f && smallStep <= 1.0f &&
                  new[] { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 25f, 50f, 100f }.Contains(smallStep),
                  smallStep.ToString("0.##"));

            mv.SetTab(4);
            bool tabs = mv.Tab == 4 && ModelViewer.TabNames.Length == 6 &&
                        ModelViewer.TabNames[0] == "Models" && ModelViewer.TabNames[4] == "Textures" && ModelViewer.TabNames[5] == "Weapon";
            mv.SetTab(-3);
            tabs &= mv.Tab == 0;
            check("modelviewer tabs", tabs, string.Join("/", ModelViewer.TabNames));

            check("modelviewer no automatic hand-off",
                  mv.RequestSpawnInWorld == null && mv.RequestToMloCreator == null &&
                  mv.RequestExtract == null && mv.RequestSelectDrawable < 0,
                  "spawn/mlo/extract all null");
        }

        private static void WorkspaceOrderTest_P1(Action<string, bool, string> check, LightPanel panel)
        {
            check("workspace enum values unchanged",
                  (int)LightPanel.Space.Light == 0 && (int)LightPanel.Space.Material == 1 &&
                  (int)LightPanel.Space.Cinematic == 2 && (int)LightPanel.Space.Archive == 3 &&
                  (int)LightPanel.Space.World == 4 && (int)LightPanel.Space.Mlo == 5 &&
                  (int)LightPanel.Space.Particles == 6,
                  string.Join(",", Enum.GetValues<LightPanel.Space>().Select(s => $"{s}={(int)s}")));

            var order = LightPanel.WorkspaceTabOrder_P1;
            check("world tab is first, cinematic last",
                  order.Length >= 8 && order[0] == LightPanel.Space.World &&
                  order[order.Length - 1] == LightPanel.Space.Cinematic &&
                  order[1] == LightPanel.Space.Light && order[2] == LightPanel.Space.Material &&
                  order[3] == LightPanel.Space.Mlo && order[4] == LightPanel.Space.Archive &&
                  order[5] == LightPanel.Space.Particles && order[6] == LightPanel.Space.NavMesh &&
                  order.Distinct().Count() == order.Length,
                  string.Join(" ", order));

            static bool Blue(Vector4 c) => c.Z > 0.55f && c.Z > c.X + 0.25f && c.Z > c.Y + 0.25f;
            static bool Teal(Vector4 c) => c.Y > 0.55f && c.Y > c.X + 0.25f && c.Z > c.X + 0.15f;
            static bool Red(Vector4 c) => c.X > 0.55f && c.X > c.Y + 0.25f && c.X > c.Z + 0.25f;
            static bool Pink(Vector4 c) => c.X > 0.55f && c.Z > 0.35f && c.X > c.Y + 0.25f && c.Z > c.Y + 0.15f;
            static bool White(Vector4 c) => c.X > 0.80f && c.Y > 0.80f && c.Z > 0.80f &&
                                            Math.Abs(c.X - c.Y) < 0.12f && Math.Abs(c.Y - c.Z) < 0.12f;
            static bool Amber(Vector4 c) => c.X > 0.70f && c.Y > 0.40f && c.Z < 0.35f && c.X > c.Y;
            static bool Purple(Vector4 c) => c.Z > 0.55f && c.X > 0.35f && c.X > c.Y + 0.25f && c.Z > c.Y + 0.25f;
            var cols = Enum.GetValues<LightPanel.Space>()
                           .ToDictionary(s => s, LightPanel.WorkspaceColour_Q3);
            check("workspace colours",
                  Blue(cols[LightPanel.Space.World]) && Teal(cols[LightPanel.Space.Archive]) &&
                  Red(cols[LightPanel.Space.Cinematic]) && Pink(cols[LightPanel.Space.Particles]) &&
                  White(cols[LightPanel.Space.NavMesh]) && Amber(cols[LightPanel.Space.Light]) &&
                  Purple(cols[LightPanel.Space.Material]) && Teal(cols[LightPanel.Space.Mlo]) &&
                  cols.Values.Select(c => $"{c.X:0.00},{c.Y:0.00},{c.Z:0.00}").Distinct().Count() == cols.Count,
                  string.Join(" ", cols.Select(kv => $"{kv.Key}=#{(int)(kv.Value.X * 255):X2}{(int)(kv.Value.Y * 255):X2}{(int)(kv.Value.Z * 255):X2}")));

            var drawn = panel?.WorkspaceTabsDrawn_P1;
            check("tab order drawn matches the declared one",
                  drawn == null || drawn.Length == 0 || drawn.SequenceEqual(order),
                  drawn == null || drawn.Length == 0 ? "no bar drawn yet in this run" : string.Join(" ", drawn));
        }

        private void RpfIsolationTest_P1(Action<string, bool, string> check)
        {
            check("rpf view routing",
                  ModelViewerTakes_P1("model") && ModelViewerTakes_P1("textures") && ModelViewerTakes_P1("collision") &&
                  !ModelViewerTakes_P1("xml") && !ModelViewerTakes_P1("text") &&
                  !ModelViewerTakes_P1("particles") && !ModelViewerTakes_P1(null),
                  "model/textures/collision -> the viewer window; xml/text/particles unchanged");

            check("rpf view routing, loose files on disk",
                  Editor.AssetPreview.CanPreviewDiskFile("a.ydr") && Editor.AssetPreview.CanPreviewDiskFile("a.YDD") &&
                  Editor.AssetPreview.CanPreviewDiskFile("a.yft") && Editor.AssetPreview.CanPreviewDiskFile("a.ytd") &&
                  Editor.AssetPreview.CanPreviewDiskFile("a.ybn") &&
                  !Editor.AssetPreview.CanPreviewDiskFile("a.ytyp") && !Editor.AssetPreview.CanPreviewDiskFile("a.ymap") &&
                  !Editor.AssetPreview.CanPreviewDiskFile("a.txt") && !Editor.AssetPreview.CanPreviewDiskFile(null),
                  "ydr/ydd/yft/ytd/ybn -> the viewer window; ytyp/ymap/text unchanged");

            int props0 = lightScene?.Files.Count ?? 0;
            int lights0 = lightScene?.Lights.Count ?? 0;
            int ytds0 = lightScene?.LoadedYtds.Count ?? 0;
            int mlo0 = mloScene?.Files.Count ?? 0;
            var space0 = panel.Workspace;

            CodeWalker.GameFiles.RpfFileEntry ydr = null;
            if (panel.Archive != null && panel.Archive.Ready)
            {
                panel.Archive.Search("prop_", ".ydr", 8);
                foreach (var r in panel.Archive.Results) { ydr = r.File; break; }
            }
            bool real = ydr != null;
            ydr ??= new CodeWalker.GameFiles.RpfBinaryFileEntry
            { Name = "p1_routing_probe.ydr", NameLower = "p1_routing_probe.ydr", Path = "p1_routing_probe.ydr" };

            ViewRpfEntry_N4(ydr);

            bool untouched = (lightScene?.Files.Count ?? 0) == props0 &&
                             (lightScene?.Lights.Count ?? 0) == lights0 &&
                             (lightScene?.LoadedYtds.Count ?? 0) == ytds0 &&
                             (mloScene?.Files.Count ?? 0) == mlo0 &&
                             panel.Workspace == space0 &&
                             panel.ArchivePreview == null;
            check("rpf browse leaves the light workspace alone", untouched,
                  $"{(real ? ydr.Name : ydr.Name + " (synthetic - no archive index yet)")}: " +
                  $"props {props0}->{lightScene?.Files.Count ?? 0}, lights {lights0}->{lightScene?.Lights.Count ?? 0}, " +
                  $"ytds {ytds0}->{lightScene?.LoadedYtds.Count ?? 0}, mlo {mlo0}->{mloScene?.Files.Count ?? 0}, " +
                  $"space {space0}->{panel.Workspace}, sharedPreview={(panel.ArchivePreview != null)}");

            if (real)
                check("rpf browse opens the viewer window", ModelView.Visible && ModelView.Entry == ydr,
                      $"visible={ModelView.Visible} title={ModelView.Title} models={ModelView.Models.Count}");

            ModelView.Visible = true;
            ModelView.RequestClose = true;
            ServiceModelViewer_P1();
            check("rpf viewer closes cleanly", !ModelView.Visible && !ModelView.RequestClose,
                  $"visible={ModelView.Visible}");
        }
    }
}

