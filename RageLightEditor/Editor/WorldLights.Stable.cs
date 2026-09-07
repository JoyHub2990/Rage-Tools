using System;
using System.Collections.Generic;
using SharpDX;
using RageLightEditor.Rendering;

namespace RageLightEditor.Editor
{
    public partial class WorldLights
    {
        public static readonly bool HysteresisDisabled = Environment.GetEnvironmentVariable("RLE_NOLIGHTHYST") == "1";
        public float HysteresisFraction = 0.15f;
        public float HysteresisMetres = 6.0f;
        public float ReserveSeconds = 3.5f;

        private struct LitEntry
        {
            public int LastFrame;
            public float LastTime;
            public int Order;
        }
        private readonly Dictionary<ulong, LitEntry> lit = new Dictionary<ulong, LitEntry>();
        private readonly List<int> keptIdx = new List<int>(512), newIdx = new List<int>(512), emitIdx = new List<int>(512);
        private readonly HashSet<ulong> seenKeys = new HashSet<ulong>(512);
        private readonly List<ulong> expired = new List<ulong>();
        private HashSet<ulong> lastEmitted = new HashSet<ulong>(), thisEmitted = new HashSet<ulong>();
        private int litFrame, litOrder;

        public int LitAdded { get; private set; }
        public int LitRemoved { get; private set; }
        public int LitReserved { get; private set; }
        public ulong LitSetHash { get; private set; }
        public int LitCandidates { get; private set; }
        public int LitDisplaced { get; private set; }

        public static ulong KeyOf(Vector3 pos, int index)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                h = (h ^ (ulong)(long)Math.Round(pos.X * 100.0f)) * 1099511628211UL;
                h = (h ^ (ulong)(long)Math.Round(pos.Y * 100.0f)) * 1099511628211UL;
                h = (h ^ (ulong)(long)Math.Round(pos.Z * 100.0f)) * 1099511628211UL;
                h = (h ^ (ulong)(uint)index) * 1099511628211UL;
                h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32;
                return h;
            }
        }

        private static long RankOf(in Candidate c) => ((long)c.Prio << 52) | ((long)Math.Sqrt(Math.Max(c.Dist, 0.0f)) << 32) | (long)(uint)(c.Key & 0xFFFFFFFF);

        public void ResetLitSet()
        {
            lit.Clear(); lastEmitted.Clear(); litOrder = 0;
        }

        private int SelectLit(int cap, float time)
        {
            emitIdx.Clear();
            LitCandidates = candidates.Count;
            LitDisplaced = 0;
            if (HysteresisDisabled)
            {
                if (candidates.Count > cap) candidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                for (int k = 0; k < candidates.Count && k < cap; k++) emitIdx.Add(k);
                FinishLitStats();
                return emitIdx.Count;
            }

            litFrame++;
            keptIdx.Clear(); newIdx.Clear();
            seenKeys.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!seenKeys.Add(candidates[i].Key)) continue;
                if (lit.TryGetValue(candidates[i].Key, out var e))
                {
                    e.LastFrame = litFrame; e.LastTime = time; lit[candidates[i].Key] = e;
                    keptIdx.Add(i);
                }
                else newIdx.Add(i);
            }
            int reserved = 0;
            expired.Clear();
            foreach (var kv in lit)
            {
                if (kv.Value.LastFrame == litFrame) continue;
                if (time - kv.Value.LastTime < ReserveSeconds) reserved++; else expired.Add(kv.Key);
            }
            foreach (var k in expired) lit.Remove(k);

            keptIdx.Sort((a, b) => RankOf(candidates[a]).CompareTo(RankOf(candidates[b])));
            newIdx.Sort((a, b) => RankOf(candidates[a]).CompareTo(RankOf(candidates[b])));

            while (keptIdx.Count > cap)
            {
                lit.Remove(candidates[keptIdx[keptIdx.Count - 1]].Key);
                keptIdx.RemoveAt(keptIdx.Count - 1);
            }
            int free = Math.Max(cap - keptIdx.Count - reserved, 0);
            int admitted = 0;
            for (; admitted < newIdx.Count && admitted < free; admitted++) Admit(newIdx[admitted], time);
            int p = keptIdx.Count - 1;
            for (int n = admitted; n < newIdx.Count && p >= 0; n++)
            {
                float dn = (float)Math.Sqrt(Math.Max(candidates[newIdx[n]].Dist, 0f));
                float df = (float)Math.Sqrt(Math.Max(candidates[keptIdx[p]].Dist, 0f));
                if (candidates[newIdx[n]].Prio > candidates[keptIdx[p]].Prio) break;
                bool nearerBand = candidates[newIdx[n]].Prio < candidates[keptIdx[p]].Prio;
                if (nearerBand || (dn < df * (1.0f - HysteresisFraction) && dn < df - HysteresisMetres))
                {
                    lit.Remove(candidates[keptIdx[p]].Key);
                    keptIdx.RemoveAt(p); p--;
                    Admit(newIdx[n], time);
                    LitDisplaced++;
                }
                else break;
            }

            emitIdx.AddRange(keptIdx);
            emitIdx.Sort((a, b) =>
            {
                int oa = lit.TryGetValue(candidates[a].Key, out var ea) ? ea.Order : int.MaxValue;
                int ob = lit.TryGetValue(candidates[b].Key, out var eb) ? eb.Order : int.MaxValue;
                int c = oa.CompareTo(ob);
                return c != 0 ? c : a.CompareTo(b);
            });
           LitReserved = reserved;
            FinishLitStats();
            return emitIdx.Count;
        }

        public static void HysteresisTest(Action<string, bool, string> check)
        {
            var wl = new WorldLights { MaxLights = 8 };
            var rnd = new Random(7);
            var pos = new Vector3[12];
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12.0f * MathUtil.TwoPi;
                float r = 10.0f + (float)rnd.NextDouble() * 0.05f;
                pos[i] = new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0);
            }
            void Frame(Vector3 cam, float time, Func<int, bool> lit)
            {
                wl.candidates.Clear();
                for (int i = 0; i < pos.Length; i++)
                {
                    if (!lit(i)) continue;
                    wl.candidates.Add(new Candidate { Dist = Vector3.DistanceSquared(pos[i], cam), Key = KeyOf(pos[i], i), G = new GpuLight { Position = pos[i] } });
                }
                wl.SelectLit(wl.MaxLights, time);
            }
            wl.ResetLitSet();
            Frame(Vector3.Zero, 0.0f, i => true);
            check("m2 hyst: the cap fills", wl.emitIdx.Count == 8, $"{wl.emitIdx.Count} of 8");
            var first = new List<ulong>(); foreach (var k in wl.emitIdx) first.Add(wl.candidates[k].Key);
            int adds = 0, rems = 0;
            for (int f = 1; f <= 60; f++)
            {
                Frame(new Vector3(0.001f * f, 0.0005f * f, 0), f / 60.0f, i => true);
                adds += wl.LitAdded; rems += wl.LitRemoved;
            }
            check("m2 hyst: 60 frames of drift, no churn", adds == 0 && rems == 0, $"+{adds} -{rems}");
            var after = new List<ulong>(); foreach (var k in wl.emitIdx) after.Add(wl.candidates[k].Key);
            bool sameOrder = after.Count == first.Count; for (int i = 0; sameOrder && i < after.Count; i++) sameOrder = after[i] == first[i];
            check("m2 hyst: the emitted order is stable", sameOrder, "admission order");
            wl.ResetLitSet();
            Frame(Vector3.Zero, 2.0f, i => true);
            ulong flick = wl.candidates[wl.emitIdx[3]].Key; int flickIdx = -1;
            for (int i = 0; i < pos.Length; i++) if (KeyOf(pos[i], i) == flick) flickIdx = i;
            Frame(Vector3.Zero, 2.02f, i => i != flickIdx);
            check("m2 hyst: a light in its off phase keeps its slot", wl.emitIdx.Count == 7 && wl.LitReserved == 1 && wl.LitAdded == 0, $"emitted {wl.emitIdx.Count} reserved {wl.LitReserved} +{wl.LitAdded}");
            Frame(Vector3.Zero, 2.04f, i => true);
            check("m2 hyst: and comes back into it", wl.emitIdx.Count == 8 && wl.LitAdded == 1 && wl.LitRemoved == 0 && wl.candidates[wl.emitIdx[3]].Key == flick, $"emitted {wl.emitIdx.Count} +{wl.LitAdded} -{wl.LitRemoved} slot3 same {wl.candidates[wl.emitIdx[3]].Key == flick}");
            Frame(Vector3.Zero, 3.0f, i => i != flickIdx);
            Frame(Vector3.Zero, 3.0f + wl.ReserveSeconds + 0.1f, i => i != flickIdx);
            check("m2 hyst: a light gone past the reserve frees its slot", wl.emitIdx.Count == 8 && wl.LitReserved == 0, $"emitted {wl.emitIdx.Count} reserved {wl.LitReserved}");
            wl.ResetLitSet();
            var ring = pos; pos = new Vector3[13]; Array.Copy(ring, pos, 12); pos[12] = new Vector3(9.5f, 0, 0);
            Frame(Vector3.Zero, 10.0f, i => i < 12);
            Frame(Vector3.Zero, 10.02f, i => true);
            check("m2 hyst: a hair closer does not displace", wl.LitDisplaced == 0 && wl.LitAdded == 0, $"displaced {wl.LitDisplaced} +{wl.LitAdded}");
            pos[12] = new Vector3(2.0f, 0, 0);
            Frame(Vector3.Zero, 10.04f, i => true);
            check("m2 hyst: markedly closer displaces the farthest", wl.LitDisplaced == 1 && wl.LitAdded == 1 && wl.LitRemoved == 1, $"displaced {wl.LitDisplaced} +{wl.LitAdded} -{wl.LitRemoved}");
            var wl2 = new WorldLights { MaxLights = 8 };
            pos = ring;
            int churn = 0;
            for (int f = 0; f <= 60; f++)
            {
                wl2.candidates.Clear();
                var cam = new Vector3(0.001f * f, 0.0005f * f, 0);
                for (int i = 0; i < pos.Length; i++) wl2.candidates.Add(new Candidate { Dist = Vector3.DistanceSquared(pos[i], cam), Key = KeyOf(pos[i], i) });
                wl2.candidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                wl2.emitIdx.Clear(); for (int k = 0; k < 8; k++) wl2.emitIdx.Add(k);
                wl2.FinishLitStats();
                if (f > 0) churn += wl2.LitAdded + wl2.LitRemoved;
            }
            check("m2 hyst: (for the record) nearest-first churns on the same drift", churn > 0, $"{churn} swaps in 60 frames without hysteresis");

            DuplicateKeyTest(check);
        }

        private static void DuplicateKeyTest(Action<string, bool, string> check)
        {
            var wl = new WorldLights { MaxLights = 6 };
            wl.ResetLitSet();
            var rnd = new Random(11);
            var pos = new Vector3[14];
            for (int i = 0; i < pos.Length; i++) pos[i] = new Vector3(i * 2.0f, (float)rnd.NextDouble(), 0);
            bool threw = false;
            string how = "";
            int emitted = 0, dupes = 0;
            try
            {
                for (int f = 0; f < 40; f++)
                {
                    wl.candidates.Clear();
                    var cam = new Vector3(f * 0.35f, 0, 0);
                    for (int i = 0; i < pos.Length; i++)
                    {
                        var p = pos[i];
                        var c = new Candidate { Dist = Vector3.DistanceSquared(p, cam), Key = KeyOf(p, i), G = new GpuLight { Position = p } };
                        wl.candidates.Add(c);
                        if (i % 3 == 0) { wl.candidates.Add(c); dupes++; }
                    }
                    emitted = wl.SelectLit(wl.MaxLights, 1.0f + f * 0.02f);
                }
            }
            catch (Exception ex) { threw = true; how = ex.GetType().Name + ": " + ex.Message; }

            check("m2 hyst: a light placed twice never breaks the emit sort", !threw,
                  threw ? how : $"{dupes} duplicate keys over 40 frames, {emitted} emitted, no throw");

            var keys = new HashSet<ulong>();
            bool once = true;
            foreach (var i in wl.emitIdx) if (!keys.Add(wl.candidates[i].Key)) once = false;
            check("m2 hyst: and is lit once, not twice", once, $"{wl.emitIdx.Count} emitted, {keys.Count} distinct keys");
        }

        private void Admit(int candidateIndex, float time)
        {
            lit[candidates[candidateIndex].Key] = new LitEntry { LastFrame = litFrame, LastTime = time, Order = litOrder++ };
            keptIdx.Add(candidateIndex);
        }

        private void FinishLitStats()
        {
            thisEmitted.Clear();
            ulong h = 0;
            for (int k = 0; k < emitIdx.Count; k++)
            {
                ulong key = candidates[emitIdx[k]].Key;
                thisEmitted.Add(key);
                h ^= key * 0x9E3779B97F4A7C15UL;
            }
            int added = 0, removed = 0;
            foreach (var k in thisEmitted) if (!lastEmitted.Contains(k)) added++;
            foreach (var k in lastEmitted) if (!thisEmitted.Contains(k)) removed++;
            LitAdded = added; LitRemoved = removed; LitSetHash = h;
            var t = lastEmitted; lastEmitted = thisEmitted; thisEmitted = t;
        }
    }
}

