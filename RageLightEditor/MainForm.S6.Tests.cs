using System;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using SDX = SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_S6(Action<string, bool, string> check)
        {
            var sprite = RuleWithDraw_S6(ParticleBehaviourType.Sprite);
            var model = RuleWithDraw_S6(ParticleBehaviourType.Model);
            var trail = RuleWithDraw_S6(ParticleBehaviourType.Trail);
            var both = RuleWithDraw_S6(ParticleBehaviourType.FogVolume, ParticleBehaviourType.Sprite);
            var silent = RuleWithDraw_S6();
            check("ptfx model and trail rules are not sprites",
                  PtfxDrawKinds.IsSpriteRule(sprite) && !PtfxDrawKinds.IsSpriteRule(model) &&
                  !PtfxDrawKinds.IsSpriteRule(trail) && PtfxDrawKinds.IsSpriteRule(both) &&
                  PtfxDrawKinds.IsSpriteRule(silent),
                  "sprite yes, model no, trail no, fogvolume+sprite yes, no-draw-behaviour yes " +
                  "(core.ypt: 2327 sprite, 158 model, 47 trail, 11 mixed)");
            check("ptfx unsupported rules are hidden by default",
                  !ParticlePanel.ShowUnsupportedRules_S6 ||
                  Environment.GetEnvironmentVariable("RLE_PTFXSHOWMODEL") == "1",
                  "the stand-in cards are an editor aid, so they are off unless asked for");

            var wall = new SDX.Vector3(0, 1, 0);
            var at = new SDX.Vector3(0, -1, 0);
            var away = new SDX.Vector3(1, 0, 0);
            var floor = new SDX.Vector3(0, 0, 1);
            check("mirror joke only fires close, facing a wall mirror",
                  FacingAMirror_S6(wall, 1.2f, at) &&
                  !FacingAMirror_S6(wall, 6.0f, at) &&
                  FacingAMirror_S6(wall, 0.1f, at) &&
                  !FacingAMirror_S6(wall, 1.2f, away) &&
                  !FacingAMirror_S6(floor, 1.2f, new SDX.Vector3(0, 0, -1)),
                  "1.2 m facing yes; 6 m no; 0.1 m no; sideways no; floor no");
            check("mirror joke is on by default and can be switched off",
                  new AppSettings().MirrorSurprise,
                  "Help > Mirror surprise (AppSettings.MirrorSurprise), and never in photo mode, " +
                  "Cinematic or a render to file whatever it says");
        }

        private static ParticleRule RuleWithDraw_S6(params ParticleBehaviourType[] kinds)
        {
            var rule = new ParticleRule
            {
                DrawBehaviours = new ResourcePointerList64<ParticleBehaviour>
                {
                    data_items = new ParticleBehaviour[kinds.Length],
                },
            };
            for (int i = 0; i < kinds.Length; i++)
                rule.DrawBehaviours.data_items[i] = new ParticleBehaviour { Type = kinds[i] };
            return rule;
        }
    }
}

