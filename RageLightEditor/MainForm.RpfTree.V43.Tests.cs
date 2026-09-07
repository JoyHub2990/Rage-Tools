using System;
using System.IO;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_ConvertXmlCli_V49(Action<string, bool, string> check)
        {
            string xml = null, ymap = null;
            try
            {
                xml = Path.Combine(Path.GetTempPath(), "rle_v49_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ymap.xml");
                ymap = xml.Substring(0, xml.Length - 4);
                File.WriteAllText(xml,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<CMapData>\n" +
                    " <name>rle_v49</name>\n <parent/>\n <flags value=\"0\"/>\n <contentFlags value=\"1\"/>\n" +
                    " <streamingExtentsMin x=\"-100\" y=\"-100\" z=\"-50\"/>\n <streamingExtentsMax x=\"100\" y=\"100\" z=\"50\"/>\n" +
                    " <entitiesExtentsMin x=\"-10\" y=\"-10\" z=\"-5\"/>\n <entitiesExtentsMax x=\"10\" y=\"10\" z=\"5\"/>\n" +
                    " <entities>\n  <Item type=\"CEntityDef\">\n" +
                    "   <archetypeName>prop_bench_01a</archetypeName>\n   <flags value=\"1572864\"/>\n   <guid value=\"12345\"/>\n" +
                    "   <position x=\"10.5\" y=\"-20.25\" z=\"3.75\"/>\n   <rotation x=\"0\" y=\"0\" z=\"0.7071068\" w=\"0.7071068\"/>\n" +
                    "   <scaleXY value=\"1\"/>\n   <scaleZ value=\"1\"/>\n   <parentIndex value=\"-1\"/>\n" +
                    "   <lodDist value=\"120\"/>\n   <childLodDist value=\"0\"/>\n   <lodLevel>LODTYPES_DEPTH_ORPHANHD</lodLevel>\n" +
                    "   <numChildren value=\"0\"/>\n   <priorityLevel>PRI_REQUIRED</priorityLevel>\n   <extensions/>\n" +
                    "   <ambientOcclusionMultiplier value=\"255\"/>\n   <artificialAmbientOcclusion value=\"255\"/>\n   <tintValue value=\"0\"/>\n" +
                    "  </Item>\n </entities>\n</CMapData>\n");

                int code = XmlConvertCli_V49.Run(xml, ymap);
                check("v49 convertxml: a CMapData .ymap.xml converts to a binary .ymap",
                      code == 0 && File.Exists(ymap) && new FileInfo(ymap).Length > 100,
                      code == 0 ? $"{new FileInfo(ymap).Length:N0} bytes" : "exit " + code);

                var back = new CodeWalker.GameFiles.YmapFile();
                CodeWalker.GameFiles.RpfFile.LoadResourceFile(back, File.ReadAllBytes(ymap), 2);
                var e = back.AllEntities != null && back.AllEntities.Length > 0 ? back.AllEntities[0] : null;
                check("v49 convertxml: ...and reads back with the entity intact",
                      e != null &&
                      e._CEntityDef.archetypeName.Hash == CodeWalker.GameFiles.JenkHash.GenHash("prop_bench_01a") &&
                      Math.Abs(e.Position.X - 10.5f) < 0.01f && Math.Abs(e.Position.Z - 3.75f) < 0.01f &&
                      Math.Abs(e._CEntityDef.lodDist - 120f) < 0.01f,
                      e == null ? "no entities came back"
                                : $"{e._CEntityDef.archetypeName} at {e.Position}, lodDist {e._CEntityDef.lodDist}");

                check("v49 convertxml: a missing input is a clean exit code",
                      XmlConvertCli_V49.Run(xml + ".gone", null) != 0, "non-zero");
            }
            catch (Exception ex) { check("v49 convertxml", false, ex.Message); }
            finally
            {
                try { if (xml != null && File.Exists(xml)) File.Delete(xml); } catch { }
                try { if (ymap != null && File.Exists(ymap)) File.Delete(ymap); } catch { }
            }
        }

        private void SeqTest_LodLightsLit_V48(Action<string, bool, string> check)
        {
            try
            {
                var wl = new WorldLights { LodLightsEnabled = true, MaxDistance = 3000f };
                check("v48 lod lights: the GPU budget matches the renderer's buffer",
                      wl.MaxLights == Rendering.GpuLight.MaxLights && Rendering.GpuLight.MaxLights >= 2048,
                      $"{wl.MaxLights} / {Rendering.GpuLight.MaxLights}");

                var ym = new CodeWalker.GameFiles.YmapFile { LODLights = new CodeWalker.GameFiles.YmapLODLights() };
                var near = new CodeWalker.GameFiles.YmapLODLight
                {
                    Position = new SharpDX.Vector3(20, 0, 0),
                    Colour = new SharpDX.Color(255, 220, 180, 128),
                    Falloff = 25f,
                    FalloffExponent = 8f,
                    TimeAndStateFlags = 0x00FFFFFFu | (1u << 26),
                };
                var far = new CodeWalker.GameFiles.YmapLODLight
                {
                    Position = new SharpDX.Vector3(900, 0, 0),
                    Colour = new SharpDX.Color(255, 220, 180, 128),
                    Falloff = 25f,
                    FalloffExponent = 8f,
                    TimeAndStateFlags = 0x00FFFFFFu | (1u << 26),
                };
                ym.LODLights.LodLights = new[] { near, far };

                var outL = new Rendering.GpuLight[64];
                var none = new System.Collections.Generic.List<CodeWalker.GameFiles.YmapEntityDef>();

                wl.LodLightRange = 100f;
                int n1 = wl.Build(none, outL, null, SharpDX.Vector3.Zero, 23, false, 0f, null, new[] { ym }, 1, null);
                check("v48 lod lights: a LOD light inside the range is emitted as a REAL light",
                      n1 == 1 && Math.Abs(outL[0].Falloff - 25f) < 0.001f && outL[0].Type == 1,
                      $"{n1} emitted, falloff {outL[0].Falloff}, type {outL[0].Type}");
                check("v48 lod lights: ...with CodeWalker's brightness (colour.a x 96)",
                      Math.Abs(outL[0].Intensity - 128f / 255f * 96f) < 0.5f,
                      outL[0].Intensity.ToString("0.0"));

                wl.LodLightRange = 2000f;
                int n2 = wl.Build(none, outL, null, SharpDX.Vector3.Zero, 23, false, 0f, null, new[] { ym }, 2, null);
                check("v48 lod lights: raising the range brings the far street light in",
                      n2 == 2, $"{n2} emitted at 2000 m");

                wl.LodLightRange = 0f;
                int n0 = wl.Build(none, outL, null, SharpDX.Vector3.Zero, 23, false, 0f, null, new[] { ym }, 3, null);
                check("v48 lod lights: range 0 still means none, honestly",
                      n0 == 0, $"{n0} emitted");
            }
            catch (Exception ex) { check("v48 lod lights", false, ex.Message); }
        }

        private void SeqTest_RpfTreeDrop_V43(Action<string, bool, string> check)
        {
            var p = panel;
            if (p == null) { Console.WriteLine("  v43 tree: (skipped - no panel)"); return; }
            var was = p.Workspace;
            string dir = null;
            try
            {
                p.SwitchWorkspace(LightPanel.Space.Archive);
                if (!p.Rpf.Ready) p.Rpf.BuildFromGameFolder(p.RpfGameFolder, p.Archive);

                dir = Path.Combine(Path.GetTempPath(), "rle_v43_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "note.txt");
                File.WriteAllText(file, "x");
                var sub = Path.Combine(dir, "props");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "rle_v53.ymap.xml"),
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<CMapData>\n <name>rle_v53</name>\n <parent/>\n" +
                    " <flags value=\"0\"/>\n <contentFlags value=\"1\"/>\n" +
                    " <streamingExtentsMin x=\"-10\" y=\"-10\" z=\"-10\"/>\n <streamingExtentsMax x=\"10\" y=\"10\" z=\"10\"/>\n" +
                    " <entitiesExtentsMin x=\"-1\" y=\"-1\" z=\"-1\"/>\n <entitiesExtentsMax x=\"1\" y=\"1\" z=\"1\"/>\n" +
                    " <entities/>\n</CMapData>\n");

                bool handled = p.DropRpfFiles_T4(new[] { dir }, 8, 300);
                var root = p.Rpf.Roots.LastOrDefault();
                check("v43 tree drop: a folder dropped on the tree becomes its own root",
                      handled && root != null && root.IsRoot && !ReferenceEquals(root, p.Rpf.GameRoot) &&
                      string.Equals(root.FsPath, dir, StringComparison.OrdinalIgnoreCase),
                      root?.FsPath ?? "no root was added");

                check("v53 tree drop: the XML files inside converted to game files by themselves",
                      File.Exists(Path.Combine(sub, "rle_v53.ymap")) &&
                      (p.RpfStatus ?? "").Contains("converted"),
                      p.RpfStatus ?? "");
                check("v54 tree drop: ...and the old XML files are gone",
                      !File.Exists(Path.Combine(sub, "rle_v53.ymap.xml")), "deleted after converting");

                check("v43 tree drop: ...and the list walks straight into it",
                      ReferenceEquals(p.Rpf.Current, root), p.Rpf.CurrentDisplayPath ?? "-");

                bool again = p.DropRpfFiles_T4(new[] { dir }, 8, 300);
                check("v43 tree drop: dropping the same folder twice does not duplicate it",
                      again && p.Rpf.Roots.Count(r => string.Equals(r.FsPath, dir, StringComparison.OrdinalIgnoreCase)) == 1,
                      p.Rpf.Roots.Count + " roots");

                p.DropRpfFiles_T4(new[] { file }, 8, 300);
                check("v43 tree drop: a FILE dropped on the tree says what to do instead",
                      (p.RpfStatus ?? "").IndexOf("FOLDER", StringComparison.OrdinalIgnoreCase) >= 0,
                      p.RpfStatus ?? "");

                var from = Path.Combine(Path.GetTempPath(), "rle_u4_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(from);
                var bring = Path.Combine(from, "bring.txt");
                File.WriteAllText(bring, "brought in");
                try
                {
                    p.RpfEditMode = false;
                    p.Rpf.Go(root);
                    p.DropRpfFiles_T4(new[] { bring }, settings.LeftPanelWidth + 400, 300);
                    check("u4 drop: with edit mode off nothing is written yet",
                          !File.Exists(Path.Combine(dir, "bring.txt")), p.RpfStatus ?? "");
                    check("u4 drop: ...the tool asks to turn edit mode on instead of going quiet",
                          p.PendingRpfConfirm_U4 != null, p.PendingRpfConfirm_U4 ?? "it asked nothing");
                    check("u4 drop: saying yes turns edit mode on and lands the file",
                          p.AcceptRpfConfirmForTest_U4() && p.RpfEditMode &&
                          File.Exists(Path.Combine(dir, "bring.txt")), p.RpfStatus ?? "");
                }
                finally { try { Directory.Delete(from, true); } catch { } }

                check("v44 ytd grid: columns fit the width and never reach zero",
                      ModelViewer.YtdGridColumns_V44(1000, 128, 10) == 7 &&
                      ModelViewer.YtdGridColumns_V44(100, 320, 10) == 1,
                      $"{ModelViewer.YtdGridColumns_V44(1000, 128, 10)} at 1000px, " +
                      $"{ModelViewer.YtdGridColumns_V44(100, 320, 10)} at 100px");

                var rgbi = Rendering.DistantLightsRenderer_V47.UnpackRgbi_V47(0xAABBCCDDu);
                check("v47 distant lights: colour unpacks as 0xIIRRGGBB, the layout Color.FromBgra reads",
                      Math.Abs(rgbi.X - 0xBB / 255f) < 0.002f && Math.Abs(rgbi.Y - 0xCC / 255f) < 0.002f &&
                      Math.Abs(rgbi.Z - 0xDD / 255f) < 0.002f && Math.Abs(rgbi.W - 0xAA / 255f) < 0.002f,
                      rgbi.ToString());

                var sodium = Rendering.DistantLightsRenderer_V47.UnpackRgbi_V47(0x80FFA040u);
                check("v47 distant lights: a sodium street light decodes ORANGE, not green",
                      sodium.X > sodium.Y && sodium.Y > sodium.Z && Math.Abs(sodium.W - 0x80 / 255f) < 0.002f,
                      $"r {sodium.X:0.00} g {sodium.Y:0.00} b {sodium.Z:0.00} i {sodium.W:0.00}");

                check("v47 distant lights: sprite radius follows intensity and caps at 3 m",
                      Math.Abs(Rendering.DistantLightsRenderer_V47.SpriteRadius_V47(1f, 10f) - 1f) < 0.001f &&
                      Math.Abs(Rendering.DistantLightsRenderer_V47.SpriteRadius_V47(1f, 1000f) - 3f) < 0.001f &&
                      Math.Abs(Rendering.DistantLightsRenderer_V47.SpriteRadius_V47(0.4f, 50f) - 2f) < 0.001f,
                      "1 m at 10 m, capped 3 m far away, 2 m at half intensity");

                check("v47 distant lights: on at night, off at noon, fading through dusk",
                      Rendering.DistantLightsRenderer_V47.NightFade_V47(23f) == 1f &&
                      Rendering.DistantLightsRenderer_V47.NightFade_V47(12f) == 0f &&
                      Math.Abs(Rendering.DistantLightsRenderer_V47.NightFade_V47(20.5f) - 0.5f) < 0.001f &&
                      Math.Abs(Rendering.DistantLightsRenderer_V47.NightFade_V47(5.5f) - 0.5f) < 0.001f,
                      "23h on, noon off, 20:30 and 5:30 half");
            }
            catch (Exception ex) { check("v43 tree drop", false, ex.Message); }
            finally
            {
                try
                {
                    var node = p.Rpf.Roots.LastOrDefault(r =>
                        dir != null && string.Equals(r.FsPath, dir, StringComparison.OrdinalIgnoreCase));
                    if (node != null) p.Rpf.CloseRootFolder(node);
                }
                catch { }
                try { if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                p.SwitchWorkspace(was);
            }
        }
    }
}
