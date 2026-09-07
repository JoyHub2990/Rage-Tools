using System;
using System.Linq;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private bool modsDlcBusy_V21;

        partial void OnWorldTick_ModsDlc_V21()
        {
            if (!worldBuilt || modsDlcBusy_V21 || panel == null || gameFiles == null) return;

            panel.LoadModsDlcOptions_V21();
            bool wantMods = panel.WorldEnableMods;
            string wantDlc = panel.SelectedDlcName_V21;

            bool modsChange = wantMods != gameFiles.EnableMods;
            bool dlcChange = !string.IsNullOrEmpty(wantDlc) && wantDlc != (gameFiles.SelectedDlc ?? "");
            if (!modsChange && !dlcChange) return;

            if (WorldEdit != null && WorldEdit.DirtyCount > 0)
            {
                panel.WorldEnableMods = gameFiles.EnableMods;
                panel.MloStatus = $"Save or discard your {WorldEdit.DirtyCount} edited .ymap file(s) first - " +
                                  "changing this re-reads every file from the archives.";
                return;
            }

            var c = gameFiles.Cache;
            if (c == null || !gameFiles.Ready)
            {
                if (modsChange) { gameFiles.EnableMods = wantMods; settings.WorldEnableMods = wantMods; }
                if (dlcChange) { gameFiles.SelectedDlc = wantDlc; settings.WorldDlc = wantDlc; }
                settings.Save();
                return;
            }

            modsDlcBusy_V21 = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool changed = false;
                if (modsChange)
                {
                    changed |= c.SetModsEnabled(wantMods);
                    gameFiles.EnableMods = wantMods;
                    settings.WorldEnableMods = wantMods;
                }
                if (dlcChange)
                {
                    changed |= c.SetDlcLevel(wantDlc, true);
                    gameFiles.SelectedDlc = wantDlc;
                    settings.WorldDlc = wantDlc;
                }
                if (!changed) return;
                settings.Save();

                ReloadWorldFiles_V21();

                int mods = c.RpfMan?.ModRpfs?.Count ?? 0;
                panel.MloStatus = (modsChange ? (wantMods ? $"Mods folder on - {mods} archive(s) from mods\\. " : "Mods folder off - the stock install. ") : "") +
                                  (dlcChange ? $"DLC level {wantDlc}. " : "") +
                                  $"World reloaded in {sw.ElapsedMilliseconds} ms.";
                Console.WriteLine($"MODSDLC mods={c.EnableMods} dlc={c.SelectedDlc} modRpfs={mods} " +
                                  $"nodes={World.NodeCount} in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                panel.MloStatus = "Could not change that: " + ex.Message;
                Console.WriteLine("MODSDLC failed: " + ex);
            }
            finally { modsDlcBusy_V21 = false; }
        }

        private void ReloadWorldFiles_V21()
        {
            WorldHistory?.Clear();
            WorldEdit?.Deselect();
            World.UnloadAll();
            World.Build(gameFiles);
            worldBuilt = World.Ready;
            World.Invalidate();
            ResetWorldCollision_V28();
        }

        private bool modsProbeDone_V21;

        partial void OnWorldTick_ModsProbe_V21()
        {
            if (modsProbeDone_V21 || !worldBuilt) return;
            var c = gameFiles?.Cache;
            var rm = c?.RpfMan;
            if (rm == null || !gameFiles.Ready) return;
            if (Environment.GetEnvironmentVariable("RLE_MODSTEST") != "1") return;
            modsProbeDone_V21 = true;

            Console.WriteLine($"MODSTEST {rm.ModRpfs?.Count ?? 0} archive(s) under mods\\, " +
                              $"{rm.ModEntryDict?.Count ?? 0} entries in them; EnableMods={rm.EnableMods}");

            var shared = (rm.ModEntryDict ?? new System.Collections.Generic.Dictionary<string, CodeWalker.GameFiles.RpfEntry>())
                .Keys.Where(k => !k.StartsWith("mods" + '\\') && rm.EntryDict != null && rm.EntryDict.ContainsKey(k))
                .OrderBy(k => k).Take(6).ToList();
            if (shared.Count == 0)
            {
                Console.WriteLine("MODSTEST nothing under mods\\ replaces a stock file - " +
                                  "the option can only add, not override, on this install");
                return;
            }

            bool was = rm.EnableMods;
            foreach (var path in shared)
            {
                rm.EnableMods = false;
                var stock = rm.GetEntry(path);
                rm.EnableMods = true;
                var modded = rm.GetEntry(path);
                string sp = stock?.File?.Path ?? "(none)";
                string mp = modded?.File?.Path ?? "(none)";
                Console.WriteLine($"MODSTEST {path}{Environment.NewLine}    off -> {sp}{Environment.NewLine}    on  -> {mp}   {(sp == mp ? "*** NO DIFFERENCE ***" : "overridden")}");
            }
            rm.EnableMods = was;
        }

        private void SeqTest_ModsDlc_V21(Action<string, bool, string> check)
        {
            if (panel == null) { Console.WriteLine("  v21 mods: (skipped - no panel)"); return; }

            panel.WorldDlcIndex_V21 = 0;
            check("v21 mods: \"Newest installed\" asks for no particular pack",
                  panel.SelectedDlcName_V21 == "", "'" + panel.SelectedDlcName_V21 + "'");

            bool was = settings.WorldEnableMods;
            string wasDlc = settings.WorldDlc;
            settings.WorldEnableMods = true;
            settings.WorldDlc = "mpchristmas3";
            settings.Save();
            var back = AppSettings.Load();
            check("v21 mods: the mods folder choice is remembered",
                  back.WorldEnableMods && back.WorldDlc == "mpchristmas3",
                  $"mods={back.WorldEnableMods} dlc={back.WorldDlc}");
            settings.WorldEnableMods = was; settings.WorldDlc = wasDlc; settings.Save();

            var probe = new CodeWalker.GameFiles.YmapFile { Name = "v21_modsdlc_probe.ymap" };
            WorldEdit.MarkDirty(probe);
            bool wasMods = panel.WorldEnableMods;
            panel.WorldEnableMods = !wasMods;
            bool wasBuilt = worldBuilt; worldBuilt = true;
            OnWorldTick_ModsDlc_V21();
            check("v21 mods: unsaved world edits stop the switch instead of being lost",
                  panel.WorldEnableMods == wasMods && WorldEdit.DirtyCount > 0,
                  panel.WorldEnableMods == wasMods
                      ? $"switch put back, {WorldEdit.DirtyCount} edit(s) kept"
                      : "the switch went through and the edits were lost");
            worldBuilt = wasBuilt;
            WorldRevertAll_V21();
        }
    }
}

