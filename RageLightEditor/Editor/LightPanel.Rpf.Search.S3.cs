using System;
using System.Collections.Generic;
using System.Diagnostics;
using ImGuiNET;

namespace RageLightEditor.Editor
{
    public partial class LightPanel
    {
        private bool rpfIndexReady_S3;
        private string rpfSearchNote_S3 = "";
        private int rpfSearchMs_S3 = -1;
        private string rpfWarmSaid_S3 = "";

        private bool DrawRpfSearchScope_S3()
        {
            bool ready = Archive != null && Archive.Ready;
            bool rerun = false;
            rerun |= ApplyScopeEnv_S3();

            if (ready && !rpfIndexReady_S3 && rpfSearchAll && rpfSearch.Trim().Length > 0)
                rerun = true;
            rpfIndexReady_S3 = ready;

            ImGui.SameLine(0, 12);
            if (!ready)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Warn);
                ImGui.TextUnformatted("indexing the install for search...");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Every file in every archive is being flattened into one list so that\n" +
                                     "searching the whole install answers instantly. It runs in the\n" +
                                     "background - nothing is frozen - and takes a moment on a cold start.\n" +
                                     "Anything typed before it lands is searched again the moment it does.");
                if (rpfWarmSaid_S3 != "warming")
                {
                    rpfWarmSaid_S3 = "warming";
                    RpfStatus = "indexing every archive in the install so search can cover all of it - " +
                                "this runs in the background and takes a moment on a cold start";
                }
                return rerun;
            }

            if (rpfWarmSaid_S3 == "warming")
            {
                rpfWarmSaid_S3 = "warm";
                RpfStatus = $"search is ready - {Archive.FileCount:N0} files across " +
                            $"{Archive.Roots.Count} archives, and it covers all of them by default";
            }

            if (rpfSearchAll && rpfSearchBranch && Rpf.CurrentBranchPrefix_V23() != null)
            {
                ImGui.TextDisabled(rpfSearchMs_S3 >= 0
                    ? $"under {Rpf.CurrentBranchLabel_V23()} ({rpfSearchMs_S3} ms)"
                    : $"under {Rpf.CurrentBranchLabel_V23()}");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Everything under the tree node you are on, however deep. Walk to\n" +
                                     "another node and the search follows; pick Whole install to search\n" +
                                     "every archive instead.");
            }
            else if (rpfSearchAll)
            {
                ImGui.TextDisabled(rpfSearchMs_S3 >= 0
                    ? $"whole install ({Archive.FileCount:N0} files, {rpfSearchMs_S3} ms)"
                    : $"whole install ({Archive.FileCount:N0} files)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Every archive in the install. Pick Selected branch to keep the search\n" +
                                     "under the tree node you are on, or This folder only for a filter\n" +
                                     "over this listing.");
            }
            else
            {
                ImGui.TextDisabled("this folder only");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Narrowed: the box filters the folder you are in. Tick 'all archives'\n" +
                                     "to go back to searching the whole install, which is the default.");
            }
            return rerun;
        }

        private bool RpfIndexWarmingNote_S3()
        {
            if (Game == null || !Game.Ready) return false;
            ImGui.PushStyleColor(ImGuiCol.Text, UiTheme.Warn);
            ImGui.TextWrapped("Indexing every archive in the install...");
            ImGui.PopStyleColor();
            ImGui.TextWrapped("The archives are open. The flat index the whole-install search runs " +
                              "on is being built in the background - a moment on a cold start - and " +
                              "the tree appears with it.");
            return true;
        }

        private bool rpfScopeEnvDone_S3;

        private bool ApplyScopeEnv_S3()
        {
            if (rpfScopeEnvDone_S3) return false;
            var scope = Environment.GetEnvironmentVariable("RLE_RPFSCOPE");
            if (string.IsNullOrWhiteSpace(scope)) { rpfScopeEnvDone_S3 = true; return false; }
            if (rpfSearch.Trim().Length > 0) rpfScopeEnvDone_S3 = true;
            bool all = !scope.Trim().Equals("folder", StringComparison.OrdinalIgnoreCase);
            if (all == rpfSearchAll) return false;
            rpfSearchAll = all;
            return true;
        }

        private int RunRpfFindAll_S3(string query, List<ArchiveBrowser.Entry> hits) => RunRpfFindAll_S3(query, hits, null, null);

        private int RunRpfFindAll_S3(string query, List<ArchiveBrowser.Entry> hits, string branchPrefix, string branchLabel)
        {
            if (Archive == null || !Archive.Ready)
            {
                hits.Clear();
                RpfStatus = "still indexing the archives - \"" + (query ?? "").Trim() +
                            "\" will be searched again as soon as that finishes";
                return 0;
            }

            var sw = Stopwatch.StartNew();
            int total = Archive.Find(query, null, hits, 2000, branchPrefix);
            sw.Stop();
            rpfSearchMs_S3 = (int)sw.ElapsedMilliseconds;

            var q = (query ?? "").Trim();
            string where = branchPrefix != null ? $"under {branchLabel ?? branchPrefix}"
                         : (branchLabel != null && Rpf.CurrentBranchOutsideInstall_V23()) ? "in the install (that folder is outside it - use This folder only)"
                         : "anywhere in the install";
            rpfSearchNote_S3 = total == 0
                ? $"nothing {where} is called \"{q}\" ({Archive.FileCount:N0} files searched in {rpfSearchMs_S3} ms)"
                : $"\"{q}\": {total:N0} file(s) {where}" +
                  (total > hits.Count ? $" - showing the first {hits.Count:N0}" : "") +
                  $" ({rpfSearchMs_S3} ms)";
            RpfStatus = rpfSearchNote_S3;
            return total;
        }
    }
}

