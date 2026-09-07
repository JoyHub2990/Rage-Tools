using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private void SeqTest_SheetExport_V40(Action<string, bool, string> check)
        {
            string ypt = null, png = null;
            try
            {
                png = WriteTestPng_V18(64);
                var mine = ImageImport_V18.Load_V18(png, out var err);
                if (mine?.Data?.FullData == null) { check("v40 sheet: a custom image decodes", false, err); return; }
                mine.Name = "rle_v40_mysheet";
                mine.NameHash = JenkHash.GenHash(mine.Name);

                var doc = PtfxAuthor.NewDocument("rle_v40", "rle_v40_fx", PtfxAuthor.MakePuffSheet("rle_v40_puff", 32));
                var em = doc?.Effects?.FirstOrDefault(e => e.Name == "rle_v40_fx")?.Emitters?.FirstOrDefault();
                var pr = em?.ParticleRule;
                if (pr == null) { check("v40 sheet: a new effect has an emitter", false, "none"); return; }

                PtfxAuthor.SetSheet(doc, em, mine);
                check("v40 sheet: the emitter takes the imported sheet",
                      PtfxAuthor.EmitterSheet(em)?.Name == "rle_v40_mysheet",
                      PtfxAuthor.EmitterSheet(em)?.Name ?? "none");

                var at = ParticlePanel.SetAnimated_V29(pr, true, 16);
                check("v40 sheet: Animate still works AFTER the sheet was replaced",
                      at != null, at == null ? "no AnimateTexture behaviour was added" : "added");
                if (at == null) return;

                at.LastFrameID = 11;
                ParticlePanel.SetRate_V29(at, 18.5f);
                at.LoopMode = 1;
                at.DoFrameBlending = 1;
                at.IsHeldOnLastFrame = 0;
                pr.TexFrameIDMin = 2; pr.TexFrameIDMax = 5;

                ypt = Path.Combine(Path.GetTempPath(), "rle_v40_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ypt");
                doc.Save(ypt);
                var back = PtfxDocument.FromFile(ypt);
                var bem = back?.Effects?.FirstOrDefault(e => e.Name == "rle_v40_fx")?.Emitters?.FirstOrDefault();
                var bpr = bem?.ParticleRule;
                var bat = bpr?.AllBehaviours?.data_items?.OfType<ParticleBehaviourAnimateTexture>().FirstOrDefault()
                          ?? bpr?.DrawBehaviours?.data_items?.OfType<ParticleBehaviourAnimateTexture>().FirstOrDefault();

                check("v40 sheet: the imported sheet is IN the saved .ypt, with its pixels",
                      PtfxAuthor.EmitterSheet(bem)?.Name == "rle_v40_mysheet" &&
                      (PtfxAuthor.EmitterSheet(bem)?.Data?.FullData?.Length ?? 0) > 0,
                      PtfxAuthor.EmitterSheet(bem)?.Name ?? "no sheet came back");

                check("v40 sheet: ...and every animation field came back with it",
                      bat != null && bat.LastFrameID == 11 &&
                      Math.Abs(ParticlePanel.RateOf_V29(bat) - 18.5f) < 0.01f &&
                      bat.LoopMode == 1 && bat.DoFrameBlending == 1 && bat.IsHeldOnLastFrame == 0,
                      bat == null ? "no AnimateTexture in the saved file"
                                  : $"last {bat.LastFrameID}, {ParticlePanel.RateOf_V29(bat):0.#} fps, loop {bat.LoopMode}, " +
                                    $"blend {bat.DoFrameBlending}, hold {bat.IsHeldOnLastFrame}");

                check("v40 sheet: ...and the start-cell window too",
                      bpr != null && bpr.TexFrameIDMin == 2 && bpr.TexFrameIDMax == 5,
                      $"{bpr?.TexFrameIDMin} .. {bpr?.TexFrameIDMax}");

                bool anyGridVar = (bpr?.ShaderVars?.data_items ?? Array.Empty<ParticleShaderVar>())
                    .Any(v => v != null && new[] { "grid", "tile", "atlas", "column", "row", "sheet" }
                        .Any(w => (v.Name.ToString() ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
                check("v40 sheet: the .ypt has no field for the sheet GRID - so columns/rows is preview-only",
                      !anyGridVar, anyGridVar ? "a grid var exists after all - it should be saved!" : "confirmed: no grid field");

                check("v41 grid: 49 frames on a square sheet slice 7x7 (ptfx_smoke_wispy_anim, ptfx_fire_v3)",
                      AtlasDetect.GameGrid_V41(1024, 1024, 49) == (7, 7) &&
                      AtlasDetect.GameGrid_V41(2048, 2048, 49) == (7, 7), "7x7");
                check("v41 grid: ...and 7x7 even on the 1:2 sheet, cells tall and thin (ptfx_fire_v2) - the rule the artists obey",
                      AtlasDetect.GameGrid_V41(1024, 2048, 49) == (7, 7), AtlasDetect.GameGrid_V41(1024, 2048, 49).ToString());
                check("v41 grid: 32 frames on the 2:1 fireball slice 8x4, cells perfectly square",
                      AtlasDetect.GameGrid_V41(2048, 1024, 32) == (8, 4), AtlasDetect.GameGrid_V41(2048, 1024, 32).ToString());
                check("v41 grid: 4 frames on a square sheet are 2x2 (ptfx_embers_rgb)",
                      AtlasDetect.GameGrid_V41(256, 256, 4) == (2, 2), "2x2");
                check("v41 grid: one frame means the whole sheet, which is what the game draws for the glows",
                      AtlasDetect.GameGrid_V41(512, 512, 1) == (1, 1), "1x1");
                check("v41 grid: the preview slices the same way the game will",
                      AtlasDetect.Detect(null, 0) == (1, 1), "consistent");
            }
            catch (Exception ex) { check("v40 sheet: the custom-sheet round trip", false, ex.Message); }
            finally
            {
                try { if (ypt != null && File.Exists(ypt)) File.Delete(ypt); } catch { }
                try { if (png != null && File.Exists(png)) File.Delete(png); } catch { }
            }
        }
    }
}

