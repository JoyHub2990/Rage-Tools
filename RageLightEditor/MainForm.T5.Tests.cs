using System;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_T5(Action<string, bool, string> check)
        {
            Rendering.NavMeshRenderer.SelfTest_T5(check);
            ExtensionHelpers.ShaftCalibrationTest_T5(check);
            RefProxyTest_T5(check);
        }

        private static void RefProxyTest_T5(Action<string, bool, string> check)
        {
            check("t5 proxy: the game's reflection-proxy names are recognised",
                  WorldStreamer.IsReflectionProxyName_T5("cs3_06_refprox07_ch") &&
                  WorldStreamer.IsReflectionProxyName_T5("apa_ch2_superyacht_refproxy006") &&
                  WorldStreamer.IsReflectionProxyName_T5("vb_ca_prop_tree_reflprox_2"), "");
            check("t5 proxy: an ordinary map name is not",
                  !WorldStreamer.IsReflectionProxyName_T5("cs3_06_06_land_05") &&
                  !WorldStreamer.IsReflectionProxyName_T5("prop_bush_lrg_03") &&
                  !WorldStreamer.IsReflectionProxyName_T5("dt1_13_build2"), "");
            check("t5 proxy: the two archetype flags are the game's own water-reflection ones",
                  WorldStreamer.ArchFlagPreReflectedWaterProxy_T5 == 1048576u &&
                  WorldStreamer.ArchFlagProxyForWaterReflections_T5 == 2097152u,
                  $"{WorldStreamer.ArchFlagPreReflectedWaterProxy_T5} / {WorldStreamer.ArchFlagProxyForWaterReflections_T5}");
            check("t5 proxy: the dead-child threshold is short enough to mean 'never'",
                  WorldStreamer.ProxyChildDeadLodDist_T5 > 0.0f && WorldStreamer.ProxyChildDeadLodDist_T5 <= 50.0f,
                  $"{WorldStreamer.ProxyChildDeadLodDist_T5:0} m");
        }
    }
}

