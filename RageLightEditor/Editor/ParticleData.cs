using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SDX = SharpDX;

namespace RageLightEditor.Editor
{
    public partial class PtfxDocument
    {
        public string FilePath;
        public string RpfPath;
        public string Name;
        public YptFile Ypt;
        public bool Dirty;

        public readonly List<PtfxEffect> Effects = new List<PtfxEffect>();

        public ParticleEffectsList PtxList => Ypt?.PtfxList;

        public static PtfxDocument FromFile(string path)
        {
            var doc = new PtfxDocument
            {
                FilePath = path,
                Name = Path.GetFileName(path),
                Ypt = new YptFile(),
            };
            doc.Ypt.Load(File.ReadAllBytes(path));
            doc.BuildTree();
            doc.OnLoaded_U1(path);
            return doc;
        }

        public static PtfxDocument FromGame(GameFileManager game, string name, string rpfPath)
        {
            var ypt = game?.Cache?.RpfMan?.GetFile<YptFile>(rpfPath);
            if (ypt == null) return null;
            var doc = new PtfxDocument
            {
                Name = name,
                RpfPath = rpfPath,
                Ypt = ypt,
            };
            doc.BuildTree();
            return doc;
        }

        partial void OnSaved_U1(string path);
        partial void OnLoaded_U1(string path);

        public void BuildTree()
        {
            Effects.Clear();
            var effDict = PtxList?.EffectRuleDictionary;
            var rules = effDict?.EffectRules?.data_items;
            if (rules == null) return;
            foreach (var er in rules)
            {
                if (er == null) continue;
                Effects.Add(new PtfxEffect(this, er));
            }
        }

        public string Save(string asPath = null)
        {
            var path = asPath ?? FilePath;
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("This ypt was loaded from the game archives — use Save As.");
            if (asPath == null && File.Exists(path) && !File.Exists(path + ".bak"))
                File.Copy(path, path + ".bak");
            var data = Ypt.Save();
            File.WriteAllBytes(path, data);
            FilePath = path;
            Name = Path.GetFileName(path);
            Dirty = false;
            OnSaved_U1(path);
            return path;
        }
    }

    public class PtfxEffect
    {
        public readonly PtfxDocument Doc;
        public readonly ParticleEffectRule Rule;
        public readonly List<PtfxEmitter> Emitters = new List<PtfxEmitter>();

        public string Name => Rule?.Name?.Value ?? Rule?.NameHash.ToString() ?? "effect";

        public PtfxEffect(PtfxDocument doc, ParticleEffectRule rule)
        {
            Doc = doc;
            Rule = rule;
            var evs = rule?.EventEmitters?.data_items;
            if (evs != null)
            {
                foreach (var ev in evs)
                {
                    if (ev == null) continue;
                    Emitters.Add(new PtfxEmitter(this, ev));
                }
            }
        }
    }

    public class PtfxEmitter
    {
        public readonly PtfxEffect Effect;
        public readonly ParticleEventEmitter Event;

        public ParticleEmitterRule EmitterRule => Event?.EmitterRule;
        public ParticleRule ParticleRule => Event?.ParticleRule;

        public string Name =>
            Event?.EmitterRuleName?.Value
            ?? EmitterRule?.Name?.Value
            ?? "emitter";
        public string ParticleName =>
            Event?.ParticleRuleName?.Value
            ?? ParticleRule?.Name?.Value
            ?? "particles";

        public PtfxEmitter(PtfxEffect effect, ParticleEventEmitter ev)
        {
            Effect = effect;
            Event = ev;
        }
    }

    public static class PtfxKeyframes
    {
        public static ParticleKeyframeProp Find(ParticleKeyframeProp[] props, string name)
        {
            if (props == null) return null;
            var hash = JenkHash.GenHash(name.ToLowerInvariant());
            foreach (var p in props)
            {
                if (p != null && p.Name.Hash == hash) return p;
            }
            return null;
        }

        public static ParticleKeyframeProp Find(ResourcePointerArray64<ParticleKeyframeProp> list, string name)
        {
            return Find(list?.data_items, name);
        }

        public static bool HasValues(ParticleKeyframeProp p)
        {
            var v = p?.Values?.data_items;
            return v != null && v.Length > 0;
        }

        public static SDX.Vector4 Evaluate(ParticleKeyframeProp p, float t, SDX.Vector4 fallback)
        {
            var vals = p?.Values?.data_items;
            if (vals == null || vals.Length == 0) return fallback;
            if (vals.Length == 1) return vals[0].KeyframeValue;

            if (t <= vals[0].KeyframeTime.X) return vals[0].KeyframeValue;
            var last = vals[vals.Length - 1];
            if (t >= last.KeyframeTime.X) return last.KeyframeValue;

            for (int i = 0; i < vals.Length - 1; i++)
            {
                var a = vals[i];
                var b = vals[i + 1];
                if (t >= a.KeyframeTime.X && t <= b.KeyframeTime.X)
                {
                    var span = b.KeyframeTime.X - a.KeyframeTime.X;
                    var f = span > 1e-6f ? (t - a.KeyframeTime.X) / span : 0f;
                    return SDX.Vector4.Lerp(a.KeyframeValue, b.KeyframeValue, f);
                }
            }
            return last.KeyframeValue;
        }

        public static SDX.Vector4 EvaluateMinMax(ParticleKeyframeProp min, ParticleKeyframeProp max,
                                                 float t, SDX.Vector4 fallback, ref uint rngState)
        {
            var vmin = Evaluate(min, t, fallback);
            var vmax = Evaluate(max, t, vmin);
            return new SDX.Vector4(
                Lerp(vmin.X, vmax.X, NextFloat(ref rngState)),
                Lerp(vmin.Y, vmax.Y, NextFloat(ref rngState)),
                Lerp(vmin.Z, vmax.Z, NextFloat(ref rngState)),
                Lerp(vmin.W, vmax.W, NextFloat(ref rngState)));
        }

        public static SDX.Vector4 EvaluateMinMaxUniform(ParticleKeyframeProp min, ParticleKeyframeProp max,
                                                        float t, SDX.Vector4 fallback, ref uint rngState)
        {
            var vmin = Evaluate(min, t, fallback);
            var vmax = Evaluate(max, t, vmin);
            var f = NextFloat(ref rngState);
            var fa = NextFloat(ref rngState);
            return new SDX.Vector4(
                Lerp(vmin.X, vmax.X, f),
                Lerp(vmin.Y, vmax.Y, f),
                Lerp(vmin.Z, vmax.Z, f),
                Lerp(vmin.W, vmax.W, fa));
        }

        private static float Lerp(float a, float b, float f) => a + (b - a) * f;

        public static float NextFloat(ref uint state)
        {
            if (state == 0) state = 0x9E3779B9;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / 16777216.0f;
        }
    }

    public static class PtfxGameIndex
    {
        public class Entry
        {
            public string Name;
            public string Path;
            public override string ToString() => Name;
        }

        public static List<Entry> ListYpts(GameFileManager game)
        {
            var result = new List<Entry>();
            var rpfman = game?.Cache?.RpfMan;
            if (rpfman?.EntryDict == null) return result;
            foreach (var kv in rpfman.EntryDict)
            {
                if (!(kv.Value is RpfFileEntry fe)) continue;
                var name = fe.NameLower;
                if (name == null || !name.EndsWith(".ypt")) continue;
                result.Add(new Entry
                {
                    Name = name.Substring(0, name.Length - 4),
                    Path = fe.Path,
                });
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

    }
}

