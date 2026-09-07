using System;
using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public partial class ParticlePanel
    {
        public PtfxEffect EditedEffect =>
            Doc != null && selEffect >= 0 && selEffect < Doc.Effects.Count ? Doc.Effects[selEffect] : null;

        public int SelectedEmitter
        {
            get => selEmitter;
            set => selEmitter = value;
        }

        public int TimelineCurve;

        public void TouchFromTimeline(bool structural) => Touch(structural);

        public static void CollectTimelineCurves(PtfxEmitter em,
            System.Collections.Generic.List<(string name, ParticleKeyframeProp kfp)> into)
        {
            into.Clear();
            if (em == null) return;
            var er = em.EmitterRule;
            if (er?.KeyframeProps != null)
                foreach (var k in er.KeyframeProps)
                    if (PtfxKeyframes.HasValues(k)) into.Add((ShortName(k.Name.ToString()), k));
            var pr = em.ParticleRule;
            foreach (var bh in pr?.AllBehaviours?.data_items ?? Array.Empty<ParticleBehaviour>())
                foreach (var k in bh?.KeyframeProps?.data_items ?? Array.Empty<ParticleKeyframeProp>())
                    if (PtfxKeyframes.HasValues(k)) into.Add((ShortName(k.Name.ToString()), k));
        }

        public static string CollectTimelineCurvesLabel_R6(string raw) => ShortName(raw);

        private static string ShortName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "curve";
            var colon = raw.IndexOf(':');
            var owner = colon > 0 ? raw.Substring(0, colon) : "";
            var field = colon > 0 ? raw.Substring(colon + 1) : raw;
            if (field.StartsWith("m_", StringComparison.Ordinal)) field = field.Substring(2);
            if (field.EndsWith("KFP", StringComparison.OrdinalIgnoreCase)) field = field.Substring(0, field.Length - 3);
            owner = owner.Replace("ptxu_", "").Replace("ptxEmitterRule", "emitter");
            return owner.Length > 0 ? owner + " " + field : field;
        }
    }
}

