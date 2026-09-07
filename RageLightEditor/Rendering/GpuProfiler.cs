using System;
using System.Collections.Generic;
using System.Text;
using SharpDX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public sealed class GpuProfiler : IDisposable
    {
        public readonly List<string> Sections = new List<string>();
        public readonly List<float> LastMs = new List<float>();
        public readonly List<float> SmoothMs = new List<float>();
        public readonly List<float> MinMs = new List<float>();
        public float FrameMinMs = float.MaxValue;
        public float FrameMs, FrameSmoothMs;
        public bool Enabled = true;
        public int Resolved { get; private set; }

        private const int Ring = 4;
        private readonly Device device;
        private readonly Dictionary<string, int> index = new Dictionary<string, int>();
        private readonly Query[] disjoint = new Query[Ring];
        private readonly List<Query[]> begins = new List<Query[]>();
        private readonly List<Query[]> ends = new List<Query[]>();
        private readonly List<bool[]> used = new List<bool[]>();
        private int slot;
        private bool inFrame;
        private DeviceContext ctx;

        public GpuProfiler(Device device)
        {
            this.device = device;
            for (int i = 0; i < Ring; i++)
                disjoint[i] = new Query(device, new QueryDescription { Type = QueryType.TimestampDisjoint });
        }

        private int SectionIndex(string name)
        {
            if (index.TryGetValue(name, out int i)) return i;
            i = Sections.Count;
            index[name] = i;
            Sections.Add(name); LastMs.Add(0); SmoothMs.Add(0); MinMs.Add(float.MaxValue);
            var b = new Query[Ring]; var e = new Query[Ring];
            for (int r = 0; r < Ring; r++)
            {
                b[r] = new Query(device, new QueryDescription { Type = QueryType.Timestamp });
                e[r] = new Query(device, new QueryDescription { Type = QueryType.Timestamp });
            }
            begins.Add(b); ends.Add(e); used.Add(new bool[Ring]);
            return i;
        }

        public void BeginFrame(DeviceContext context)
        {
            if (!Enabled || inFrame) return;
            ctx = context;
            inFrame = true;
            for (int i = 0; i < used.Count; i++) used[i][slot] = false;
            context.Begin(disjoint[slot]);
        }

        public void Begin(string name)
        {
            if (!Enabled || !inFrame) return;
            int i = SectionIndex(name);
            used[i][slot] = true;
            ctx.End(begins[i][slot]);
        }

        public void End(string name)
        {
            if (!Enabled || !inFrame) return;
            int i = SectionIndex(name);
            ctx.End(ends[i][slot]);
        }

        public void EndFrame()
        {
            if (!Enabled || !inFrame) return;
            inFrame = false;
            ctx.End(disjoint[slot]);
            int read = (slot + 1) % Ring;
            slot = read;
            if (Resolved + 1 < Ring) { Resolved++; return; }
            try
            {
                if (!ctx.GetData(disjoint[read], AsynchronousFlags.None, out QueryDataTimestampDisjoint dj) || dj.Disjoint) return;
                double toMs = 1000.0 / Math.Max((double)dj.Frequency, 1.0);
                long frameMin = long.MaxValue, frameMax = long.MinValue;
                for (int i = 0; i < Sections.Count; i++)
                {
                    if (!used[i][read]) { LastMs[i] = 0; SmoothMs[i] = SmoothMs[i] * 0.9f; continue; }
                    if (!ctx.GetData(begins[i][read], AsynchronousFlags.None, out long t0)) continue;
                    if (!ctx.GetData(ends[i][read], AsynchronousFlags.None, out long t1)) continue;
                    float ms = (float)((t1 - t0) * toMs);
                    if (ms < 0 || ms > 10000) continue;
                    LastMs[i] = ms;
                    SmoothMs[i] = SmoothMs[i] <= 0 ? ms : SmoothMs[i] * 0.9f + ms * 0.1f;
                    if (ms < MinMs[i]) MinMs[i] = ms;
                    if (t0 < frameMin) frameMin = t0;
                    if (t1 > frameMax) frameMax = t1;
                }
                if (frameMax > frameMin)
                {
                    FrameMs = (float)((frameMax - frameMin) * toMs);
                    FrameSmoothMs = FrameSmoothMs <= 0 ? FrameMs : FrameSmoothMs * 0.9f + FrameMs * 0.1f;
                    if (FrameMs < FrameMinMs) FrameMinMs = FrameMs;
                }
                Resolved++;
            }
            catch { }
        }

        public string Report(bool smoothed = true)
        {
            var sb = new StringBuilder();
            sb.Append($"gpu {(smoothed ? FrameSmoothMs : FrameMs):0.0} (min {(FrameMinMs < float.MaxValue ? FrameMinMs : 0):0.0}) ms:");
            for (int i = 0; i < Sections.Count; i++)
            {
                float v = smoothed ? SmoothMs[i] : LastMs[i];
                if (v < 0.05f) continue;
                float mn = MinMs[i] < float.MaxValue ? MinMs[i] : 0;
                sb.Append($" {Sections[i]} {v:0.0}/{mn:0.0}");
                MinMs[i] = float.MaxValue;
            }
            FrameMinMs = float.MaxValue;
            return sb.ToString();
        }

        public void Dispose()
        {
            for (int i = 0; i < Ring; i++) disjoint[i]?.Dispose();
            foreach (var b in begins) foreach (var q in b) q?.Dispose();
            foreach (var e in ends) foreach (var q in e) q?.Dispose();
        }
    }
}

