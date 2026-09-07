using System;
using System.Collections.Generic;
using System.IO;
using RageLightEditor.Editor;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_N4(Action<string, bool, string> check)
        {
            RpfExplorerTest_N4(check);
            ParticleTest_N4(check);
        }

        private static void RpfExplorerTest_N4(Action<string, bool, string> check)
        {
            check("rpf type names", RpfTypeName_N4("x.ydr") == "Drawable" &&
                                    RpfTypeName_N4("x.ytd") == "Texture Dictionary" &&
                                    RpfTypeName_N4("x.rpf") == "Rage Package File",
                  RpfTypeName_N4("x.ydr") + " / " + RpfTypeName_N4("x.ytd"));
            check("rpf unknown type", RpfTypeName_N4("x.zzz") == "ZZZ File", RpfTypeName_N4("x.zzz"));

            check("rpf view kinds", RpfViewKind_N4("a.ydr") == "model" && RpfViewKind_N4("a.ytd") == "textures" &&
                                    RpfViewKind_N4("a.ymap") == "xml" && RpfViewKind_N4("a.meta") == "text" &&
                                    RpfViewKind_N4("a.ypt") == "particles" && RpfViewKind_N4("a.exe") == null,
                  $"{RpfViewKind_N4("a.ymap")} / {RpfViewKind_N4("a.meta")} / {RpfViewKind_N4("a.ypt")}");

            check("rpf size text", RpfExplorer.SizeText(0) == "" && RpfExplorer.SizeText(900) == "900 B" &&
                                   RpfExplorer.SizeText(1536) == "1.5 KB" && RpfExplorer.SizeText(5L * 1024 * 1024) == "5 MB",
                  RpfExplorer.SizeText(1536) + " / " + RpfExplorer.SizeText(5L * 1024 * 1024));

            var safe = RpfExplorer.SafeName("we:ird*name?.ydr");
            check("rpf safe file name", safe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && safe.EndsWith(".ydr"), safe);

            var ex = new RpfExplorer();
            ex.Build(null);
            check("rpf empty index is inert", !ex.Ready && ex.ArchiveCount == 0 && !ex.CanGoBack && !ex.GoToPath("x64a.rpf"),
                  $"ready {ex.Ready}, roots {ex.ArchiveCount}");
        }

        private static CodeWalker.GameFiles.RpfBinaryFileEntry FakeEntry_N4(string name) =>
            new CodeWalker.GameFiles.RpfBinaryFileEntry { Name = name, NameLower = name.ToLowerInvariant(), Path = name };
        private static string RpfTypeName_N4(string name) => RpfExplorer.TypeNameOf(FakeEntry_N4(name));
        private static string RpfViewKind_N4(string name) => RpfExplorer.ViewKindOf(FakeEntry_N4(name));

        private static void ParticleTest_N4(Action<string, bool, string> check)
        {
            var families = ParticlePanel.Families;
            check("ptfx catalogue loaded", families.Count > 0 && ParticlePanel.EffectCount > 1000,
                  $"{families.Count} assets, {ParticlePanel.EffectCount} effects");
            check("ptfx effect -> asset", ParticlePanel.EffectAsset.Count == 0 ||
                  (ParticlePanel.EffectAsset.TryGetValue(FirstEffect_N4(), out var asset) && !string.IsNullOrEmpty(asset)),
                  FirstEffect_N4());

            var prop = MakeCurve_N4((0f, 0f), (1f, 10f));
            var mid = PtfxKeyframes.Evaluate(prop, 0.5f, SDX.Vector4.Zero).X;
            check("ptfx curve interpolates", Math.Abs(mid - 5f) < 0.001f, mid.ToString("0.###"));
            var before = PtfxKeyframes.Evaluate(prop, -3f, SDX.Vector4.Zero).X;
            var after = PtfxKeyframes.Evaluate(prop, 9f, SDX.Vector4.Zero).X;
            check("ptfx curve clamps", Math.Abs(before) < 0.001f && Math.Abs(after - 10f) < 0.001f,
                  $"{before:0.###} .. {after:0.###}");
            var fb = PtfxKeyframes.Evaluate(null, 0.5f, new SDX.Vector4(7, 0, 0, 0)).X;
            check("ptfx curve fallback", Math.Abs(fb - 7f) < 0.001f, fb.ToString("0.###"));

            uint a = 12345, b = 12345;
            bool same = true, inRange = true;
            for (int i = 0; i < 64; i++)
            {
                var x = PtfxKeyframes.NextFloat(ref a);
                var y = PtfxKeyframes.NextFloat(ref b);
                if (Math.Abs(x - y) > 1e-6f) same = false;
                if (x < 0f || x >= 1f) inRange = false;
            }
            check("ptfx rng is deterministic and bounded", same && inRange, same ? "stable" : "diverged");

            var sim = new PtfxSimulator();
            sim.Update(0.016f);
            var sprites = new List<PtfxSimulator.Sprite>();
            sim.CollectSprites(sprites);
            check("ptfx empty sim is inert", sim.AliveCount == 0 && sprites.Count == 0,
                  $"{sim.AliveCount} alive, {sprites.Count} sprites");

            var grid = AtlasDetect.Detect(null, 16);
            check("ptfx atlas fallback", grid.gx == 1 && grid.gy == 1, $"{grid.gx}x{grid.gy}");
        }

        private static string FirstEffect_N4() =>
            ParticlePanel.Families.Count > 0 && ParticlePanel.Families[0].fx.Count > 0
                ? ParticlePanel.Families[0].fx[0] : "";

        private static CodeWalker.GameFiles.ParticleKeyframeProp MakeCurve_N4(params (float t, float v)[] keys)
        {
            var vals = new CodeWalker.GameFiles.ParticleKeyframePropValue[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                vals[i] = new CodeWalker.GameFiles.ParticleKeyframePropValue
                {
                    KeyframeTime = new SDX.Vector4(keys[i].t, 0, 0, 0),
                    KeyframeValue = new SDX.Vector4(keys[i].v, 0, 0, 0),
                };
            var prop = new CodeWalker.GameFiles.ParticleKeyframeProp();
            prop.Values = new CodeWalker.GameFiles.ResourceSimpleList64<CodeWalker.GameFiles.ParticleKeyframePropValue>
            {
                data_items = vals,
            };
            return prop;
        }
    }
}

