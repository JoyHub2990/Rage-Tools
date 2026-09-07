using System;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_M2(Action<string, bool, string> check)
        {
            Editor.WorldLights.HysteresisTest(check);
            float noon = GameExposureStops(1.5f, 0, -3.5f, 3.0f);
            check("m2 exposure: a noon street lands where the CodeWalker operator had it", Math.Abs(noon - (float)Math.Log(0.72 / 1.5, 2.0)) < 0.05f, $"{noon:0.000} stops vs {Math.Log(0.72 / 1.5, 2.0):0.000}");
            float tunnel = GameExposureStops(0.05f, 0, -3.5f, 3.0f);
            check("m2 exposure: a dark tunnel comes up by a third of a stop per halving, not in full", tunnel > noon && tunnel < noon + 2.0f, $"{tunnel:0.00} stops (full compensation would be {Math.Log(0.72 / 0.2, 2.0):0.00})");
            check("m2 exposure: the cycle's clamp holds", GameExposureStops(0.00001f, 0, -1.0f, 0.5f) == 0.5f && GameExposureStops(1000f, 0, -1.0f, 0.5f) == -1.0f, "min -1 / max 0.5");
            check("m2 exposure: the tweak adds stops", Math.Abs(GameExposureStops(1.5f, 1.0f, -3.5f, 3.0f) - (noon + 1.0f)) < 1e-4f, "+1 stop");
            check("m2 exposure: a bright plaza keeps CodeWalker's full compensation (never brighter than before)", Math.Abs(EffectiveExposureStops(4.75f, 0, -3.5f, 5.0f) - (float)Math.Log(0.72 / 4.75, 2.0)) < 1e-4f, $"{EffectiveExposureStops(4.75f, 0, -3.5f, 5.0f):0.00} stops");
            check("m2 exposure: a dark place takes the game's", Math.Abs(EffectiveExposureStops(0.05f, 0, -3.5f, 5.0f) - tunnel) < 1e-4f, $"{EffectiveExposureStops(0.05f, 0, -3.5f, 5.0f):0.00} stops");
        }
    }
}

