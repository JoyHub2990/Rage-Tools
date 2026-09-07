using System;
using System.IO;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_Q1(Action<string, bool, string> check)
        {
            RpfFlatTest_Q1(check);
            RpfMetaViewTest_Q1(check);
            TextureExportTest_Q1(check);
        }

        private void RpfFlatTest_Q1(Action<string, bool, string> check)
        {
            var was = panel.Workspace;

            panel.SwitchWorkspace(LightPanel.Space.Archive);
            bool inRpf = RpfExplorerOnly_Q1 && PostFxPassthrough_Render() >= 0.5f;
            var line = RpfFlatLine_Q1();
            panel.SwitchWorkspace(LightPanel.Space.Light);
            bool outOfRpf = !RpfExplorerOnly_Q1;
            panel.SwitchWorkspace(LightPanel.Space.World);
            outOfRpf &= !RpfExplorerOnly_Q1;
            panel.SwitchWorkspace(was);

            check("rpf explorer draws no scene", inRpf && outOfRpf,
                  $"in RPF: flat={inRpf}; Light/World unaffected={outOfRpf}");

            check("rpf flat report", line.StartsWith("RPFFLAT") && line.Contains("ymapsResident") &&
                  line.Contains("modelsBuilt") && line.Contains("sceneDrawn=0"), line);
        }

        private void RpfMetaViewTest_Q1(Action<string, bool, string> check)
        {
            check("rpf meta routing",
                  IsMetaExtension_Q1(".ytyp") && IsMetaExtension_Q1(".ymap") && IsMetaExtension_Q1(".YMT") &&
                  !IsMetaExtension_Q1(".ydr") && !IsMetaExtension_Q1(".ytd") && !IsMetaExtension_Q1(".txt") &&
                  !IsMetaExtension_Q1(null),
                  "ytyp/ymap/ymt -> the explorer's own text view; models and text unchanged");

            int props0 = lightScene?.Files.Count ?? 0;
            int lights0 = lightScene?.Lights.Count ?? 0;
            int mlo0 = mloScene?.Files.Count ?? 0;
            var space0 = panel.Workspace;
            bool viewWas = panel.RpfViewOpen;

            string ytyp = FindProbeYtyp_Q1();
            bool real = ytyp != null;
            if (ytyp == null)
            {
                ytyp = Path.Combine(Path.GetTempPath(), "rle_q1_probe.ytyp");
                try { File.WriteAllBytes(ytyp, new byte[64]); } catch { }
            }

            bool took = false;
            try { OpenMetaFileInExplorer_Q1(ytyp, ref took); } catch { }

            bool untouched = (lightScene?.Files.Count ?? 0) == props0 &&
                             (lightScene?.Lights.Count ?? 0) == lights0 &&
                             (mloScene?.Files.Count ?? 0) == mlo0 &&
                             panel.Workspace == space0;
            check("rpf ytyp opens as a view, not a load", took && untouched,
                  $"{Path.GetFileName(ytyp)}{(real ? "" : " (empty probe)")}: claimed={took}, " +
                  $"props {props0}->{lightScene?.Files.Count ?? 0}, lights {lights0}->{lightScene?.Lights.Count ?? 0}, " +
                  $"mlo {mlo0}->{mloScene?.Files.Count ?? 0}, space {space0}->{panel.Workspace}");

            if (real)
                check("rpf ytyp shows as XML with a hand-off",
                      panel.RpfViewOpen && panel.RpfViewFrom_Q1 != null &&
                      panel.RpfViewFrom_Q1.Kind == "ytyp" && panel.RpfViewFrom_Q1.CanHandOff,
                      $"open={panel.RpfViewOpen} kind={panel.RpfViewFrom_Q1?.Kind} " +
                      $"handOff={panel.RpfViewFrom_Q1?.CanHandOff}");

            check("rpf hand-off never happens on its own",
                  panel.RequestRpfToLight_Q1 == null && panel.RequestRpfToMlo_Q1 == null,
                  "light/mlo requests both null after opening a ytyp");

            panel.ShowRpfText("something else.txt", "hello");
            check("rpf view source is per file", panel.RpfViewFrom_Q1 == null,
                  "a view nobody claimed has no hand-off buttons");
            panel.RpfViewOpen = viewWas;
        }

        private static string FindProbeYtyp_Q1()
        {
            var candidates = new[]
            {
                @"C:\Users\GS\Desktop\m26_1_int_01.ytyp",
                @"C:\Users\GS\Desktop\3D\Prompt_FireStation\prompt-ss-firedept\stream\escobar_sandy_fd_int.ytyp",
            };
            foreach (var c in candidates)
                try { if (File.Exists(c)) return c; } catch { }
            return null;
        }

        private void TextureExportTest_Q1(Action<string, bool, string> check)
        {
            var mv = new ModelViewer();
            check("texture export raises nothing on its own",
                  mv.RequestTextureExport_Q1 == null && mv.ViewerTextures_Q1().Count == 0 &&
                  mv.SelectedTexture_Q1() == null,
                  "a viewer with nothing open has no textures and no request");

            check("texture export formats",
                  ModelViewer.TextureExportFormats_Q1.Length == 2 &&
                  ModelViewer.TextureExportFormats_Q1[0] == "PNG" &&
                  ModelViewer.TextureExportFormats_Q1[1] == "DDS",
                  string.Join("/", ModelViewer.TextureExportFormats_Q1));

            var empty = new AssetTextureInfo { Name = "no_pixels", Width = 4, Height = 4, Format = "DXT5" };
            var bytes = EncodeTexture_Q1(empty, "png", out string why1);
            var bytes2 = EncodeTexture_Q1(empty, "dds", out string why2);
            check("texture export refuses a texture with no pixels",
                  bytes == null && bytes2 == null && !string.IsNullOrEmpty(why1) && !string.IsNullOrEmpty(why2),
                  $"png: {why1}; dds: {why2}");

            check("texture export names",
                  SafeTextureName_Q1("prop/bench:01") == "prop_bench_01" &&
                  SafeTextureName_Q1(null) == "texture" &&
                  SafeTextureName_Q1("  ") == "texture",
                  SafeTextureName_Q1("prop/bench:01"));

            if (panel.Archive != null && panel.Archive.Ready && gameFiles != null && gameFiles.Ready)
            {
                panel.Archive.Search("prop_", ".ytd", 8);
                var ytd = panel.Archive.Results.Select(r => r.File).FirstOrDefault();
                if (ytd != null)
                {
                    bool took = false;
                    OpenInModelViewer_P1(ytd, "textures", ref took);
                    var list = ModelView.ViewerTextures_Q1();
                    var dir = Path.Combine(Path.GetTempPath(), "rle_q1_texexport");
                    try { Directory.Delete(dir, true); } catch { }
                    if (list.Count > 0)
                    {
                        WriteTextures_Q1(new ModelViewer.TextureExportRequest_Q1
                        {
                            Textures = list.ToList(), Format = "dds", WholeSet = true, SourceName = ytd.Name,
                        }, dir);
                        int written = 0;
                        try { written = Directory.GetFiles(dir, "*.dds").Length; } catch { }
                        check("texture export writes files", written > 0,
                              $"{ytd.Name}: {list.Count} texture(s) -> {written} .dds in {dir}");
                    }
                }
            }
        }
    }
}

