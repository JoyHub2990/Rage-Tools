using System;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void OnTick_ProjectMultiSelect_V26()
        {
            if (ProjWin == null || WorldEdit == null) return;
            ProjWin.PruneMultiSelection_V26();
            if (!ProjWin.MultiSelectionChanged_V26) return;
            ProjWin.MultiSelectionChanged_V26 = false;

            var picked = ProjWin.MultiSelection_V26;
            if (picked.Count == 0) { WorldEdit.Deselect(); return; }

            WorldEdit.Select(picked[0]);
            WorldEdit.ClearExtra_V20();
            for (int i = 1; i < picked.Count; i++) WorldEdit.Toggle_V20(picked[i]);
            WorldEdit.LastStatus = picked.Count > 1
                ? $"{picked.Count} selected from the project - the gizmo moves them together"
                : WorldEdit.LastStatus;
            Console.WriteLine($"PROJECTSEL {picked.Count} entity(ies) from the tree -> world selection {WorldEdit.SelectedCount_V20}");
        }

        private void SeqTest_ProjectMultiSelect_V26(Action<string, bool, string> check)
        {
            var y = new YmapFile { Name = "v26_multi.ymap" };
            var ents = new[] { new YmapEntityDef(), new YmapEntityDef(), new YmapEntityDef(), new YmapEntityDef() };
            foreach (var e in ents) { e.Ymap = y; }

            ProjWin.ClearMultiSelection_V26();

            bool handled = ProjWin.ClickEntity_V26(ents[1], y, ents, 1);
            check("v26 project select: a plain click picks one and lets the page follow it",
                  !handled && ProjWin.MultiSelection_V26.Count == 1 && ProjWin.MultiSelection_V26[0] == ents[1],
                  ProjWin.MultiSelection_V26.Count + " picked");

            ProjWin.MultiSelectionChanged_V26 = false;
            handled = ProjWin.ClickEntity_V26(ents[3], y, ents, 3);
            check("v26 project select: without a modifier the next click starts again",
                  ProjWin.MultiSelection_V26.Count == 1 && ProjWin.MultiSelection_V26[0] == ents[3],
                  ProjWin.MultiSelection_V26.Count + " picked");

            ProjWin.MultiSelection_V26.Clear();
            ProjWin.MultiSelection_V26.AddRange(new[] { ents[0], ents[2], ents[3] });
            ProjWin.MultiSelectionChanged_V26 = true;
            OnTick_ProjectMultiSelect_V26();
            check("v26 project select: the tree's set becomes the world's, so the gizmo moves them together",
                  WorldEdit.SelectedCount_V20 == 3 && ReferenceEquals(WorldEdit.Selected, ents[0]) &&
                  WorldEdit.IsSelected_V20(ents[2]) && WorldEdit.IsSelected_V20(ents[3]),
                  WorldEdit.SelectedCount_V20 + " selected in the world");

            ents[2].Ymap = null;
            ProjWin.PruneMultiSelection_V26();
            check("v26 project select: an entity whose ymap unloaded leaves the selection",
                  ProjWin.MultiSelection_V26.Count == 2, ProjWin.MultiSelection_V26.Count + " left");

            ProjWin.ClearMultiSelection_V26();
            OnTick_ProjectMultiSelect_V26();
            WorldEdit.Deselect();

            var paint = Editor.ShaderPresets.Template("vehicle_paint1");
            bool hasEnv = paint != null && paint.Params.Any(x => x.Hash == (uint)ShaderParamNames.EnvironmentSampler);
            bool hasDirt = paint != null && paint.Params.Any(x => x.Hash == (uint)ShaderParamNames.dirtLevelMod);
            check("v26 vehicle params: a vehicle preset carries the environment reflection",
                  hasEnv && hasDirt, paint == null ? "no template" : $"{paint.Params.Count} parameter(s), env {hasEnv}, dirt {hasDirt}");

            var plate = Editor.ShaderPresets.Template("vehicle_licenseplate");
            bool bg = plate != null && plate.Params.Any(x => x.Hash == (uint)ShaderParamNames.PlateBgSampler);
            bool bgBump = plate != null && plate.Params.Any(x => x.Hash == (uint)ShaderParamNames.PlateBgBumpSampler);
            bool letters = plate != null && plate.Params.Any(x => x.Hash == (uint)ShaderParamNames.NumLetters);
            check("v26 vehicle params: the licence-plate preset carries its plate slots and letter block",
                  bg && bgBump && letters, plate == null ? "no template" : $"bg {bg}, bgBump {bgBump}, letters {letters}");

            var named = new[] { ShaderParamNames.envEffThickness, ShaderParamNames.dirtLevelMod,
                                ShaderParamNames.NumLetters, ShaderParamNames.LetterSize,
                                ShaderParamNames.LicensePlateFontTint };
            var raw = named.Where(x => MaterialDefs.Param((uint)x) == null).ToList();
            check("v26 vehicle params: ...and the editor names them rather than showing raw float4s",
                  raw.Count == 0, raw.Count == 0 ? "all named" : "raw: " + string.Join(", ", raw));
        }
    }
}

