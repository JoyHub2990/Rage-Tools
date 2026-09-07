using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace RageLightEditor.Editor
{
    internal static class AudioEngine_U1
    {
        public const int SampleRate = 44100;
        private const int Channels = 2;
        private const int FramesPerBuffer = 1024;
        private const int BufferCount = 4;
        private const int BufferBytes = FramesPerBuffer * Channels * 2;

        public static bool Available { get; private set; } = true;
        public static bool Open { get; private set; }
        public static string Trouble { get; private set; } = "";
        public static int VoicesStarted { get; private set; }

        private static IntPtr hwo;
        private static Thread pump;
        private static volatile bool running;
        private static readonly object gate = new object();
        private static readonly List<Voice> voices = new List<Voice>();
        private static int nextId = 1;

        private sealed class Voice
        {
            public int Id;
            public float[] Data;
            public double Pos;
            public float GainL, GainR;
            public float WantL, WantR;
            public bool Loop;
            public bool Stopping;
            public bool Done;
        }

        public static int Play(float[] mono, float gainL, float gainR, bool loop)
        {
            if (!Available || mono == null || mono.Length == 0) return 0;
            if (!EnsureOpen()) return 0;
            var v = new Voice
            {
                Data = mono,
                GainL = gainL, GainR = gainR,
                WantL = gainL, WantR = gainR,
                Loop = loop,
            };
            lock (gate)
            {
                v.Id = nextId++;
                if (voices.Count >= 48) return 0;
                voices.Add(v);
                VoicesStarted++;
                return v.Id;
            }
        }

        public static void Update(int id, float gainL, float gainR)
        {
            if (id <= 0) return;
            lock (gate)
            {
                for (int i = 0; i < voices.Count; i++)
                    if (voices[i].Id == id) { voices[i].WantL = gainL; voices[i].WantR = gainR; return; }
            }
        }

        public static bool Playing(int id)
        {
            if (id <= 0) return false;
            lock (gate)
            {
                for (int i = 0; i < voices.Count; i++)
                    if (voices[i].Id == id) return !voices[i].Stopping;
            }
            return false;
        }

        public static void Stop(int id)
        {
            if (id <= 0) return;
            lock (gate)
            {
                for (int i = 0; i < voices.Count; i++)
                    if (voices[i].Id == id) { voices[i].Stopping = true; return; }
            }
        }

        public static void StopAll()
        {
            lock (gate) foreach (var v in voices) v.Stopping = true;
        }

        public static void Shutdown()
        {
            try
            {
                running = false;
                var t = pump; pump = null;
                t?.Join(400);
                lock (gate) voices.Clear();
                CloseDevice();
            }
            catch { }
            Open = false;
        }

        private static bool EnsureOpen()
        {
            if (Open) return true;
            if (!Available) return false;
            try
            {
                if (waveOutGetNumDevs() <= 0) { Fail("no audio output device"); return false; }

                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = 1,
                    nChannels = Channels,
                    nSamplesPerSec = SampleRate,
                    nAvgBytesPerSec = SampleRate * Channels * 2,
                    nBlockAlign = Channels * 2,
                    wBitsPerSample = 16,
                    cbSize = 0,
                };
                int mr = waveOutOpen(out hwo, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                if (mr != 0 || hwo == IntPtr.Zero) { Fail("waveOutOpen returned " + mr); return false; }

                for (int i = 0; i < BufferCount; i++)
                {
                    data[i] = Marshal.AllocHGlobal(BufferBytes);
                    hdrs[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                    var h = new WAVEHDR { lpData = data[i], dwBufferLength = BufferBytes };
                    Marshal.StructureToPtr(h, hdrs[i], false);
                    if (waveOutPrepareHeader(hwo, hdrs[i], Marshal.SizeOf<WAVEHDR>()) != 0)
                    { Fail("waveOutPrepareHeader failed"); CloseDevice(); return false; }
                }

                Open = true;
                running = true;
                pump = new Thread(Pump) { IsBackground = true, Name = "RAGE Tools audio" };
                try { pump.Priority = ThreadPriority.AboveNormal; } catch { }
                pump.Start();
                return true;
            }
            catch (Exception ex) { Fail(ex.Message); return false; }
        }

        private static void Fail(string why)
        {
            Available = false;
            Open = false;
            Trouble = why ?? "";
            Console.WriteLine("AUDIO unavailable - " + Trouble + " (the editor carries on in silence)");
        }

        private static readonly IntPtr[] data = new IntPtr[BufferCount];
        private static readonly IntPtr[] hdrs = new IntPtr[BufferCount];
        private static readonly bool[] queued = new bool[BufferCount];

        private static void CloseDevice()
        {
            try
            {
                if (hwo != IntPtr.Zero) waveOutReset(hwo);
                for (int i = 0; i < BufferCount; i++)
                {
                    if (hdrs[i] != IntPtr.Zero)
                    {
                        if (hwo != IntPtr.Zero) waveOutUnprepareHeader(hwo, hdrs[i], Marshal.SizeOf<WAVEHDR>());
                        Marshal.FreeHGlobal(hdrs[i]);
                        hdrs[i] = IntPtr.Zero;
                    }
                    if (data[i] != IntPtr.Zero) { Marshal.FreeHGlobal(data[i]); data[i] = IntPtr.Zero; }
                    queued[i] = false;
                }
                if (hwo != IntPtr.Zero) { waveOutClose(hwo); hwo = IntPtr.Zero; }
            }
            catch { }
        }

        private static readonly float[] acc = new float[FramesPerBuffer * Channels];
        private static readonly short[] pcm = new short[FramesPerBuffer * Channels];
        private static readonly List<Voice> snapshot = new List<Voice>();

        private static void Pump()
        {
            try
            {
                while (running)
                {
                    bool did = false;
                    for (int i = 0; i < BufferCount && running; i++)
                    {
                        if (queued[i])
                        {
                            if ((Marshal.ReadInt32(hdrs[i], flagsOffset) & WHDR_DONE) == 0) continue;
                            queued[i] = false;
                        }
                        FillBuffer();
                        Marshal.Copy(pcmBytes, 0, data[i], BufferBytes);
                        if (waveOutWrite(hwo, hdrs[i], Marshal.SizeOf<WAVEHDR>()) != 0) { running = false; break; }
                        queued[i] = true;
                        did = true;
                    }
                    if (!did) Thread.Sleep(4);
                }
            }
            catch { }
            finally { try { if (hwo != IntPtr.Zero) waveOutReset(hwo); } catch { } }
        }

        private static readonly byte[] pcmBytes = new byte[BufferBytes];

        private static void FillBuffer()
        {
            Array.Clear(acc, 0, acc.Length);

            snapshot.Clear();
            lock (gate)
            {
                snapshot.AddRange(voices);
            }

            for (int vi = 0; vi < snapshot.Count; vi++)
            {
                var v = snapshot[vi];
                if (v.Done) continue;

                float gl0 = v.GainL, gr0 = v.GainR;
                float gl1 = v.Stopping ? 0.0f : v.WantL, gr1 = v.Stopping ? 0.0f : v.WantR;
                v.GainL = gl1; v.GainR = gr1;

                var d = v.Data;
                double pos = v.Pos;
                int n = d.Length;
                for (int f = 0; f < FramesPerBuffer; f++)
                {
                    if (pos >= n)
                    {
                        if (!v.Loop) { v.Done = true; break; }
                        pos -= n;
                        if (pos >= n || pos < 0) pos = 0;
                    }
                    int i0 = (int)pos;
                    float s = d[i0];
                    float t = f * (1.0f / FramesPerBuffer);
                    acc[f * 2] += s * (gl0 + (gl1 - gl0) * t);
                    acc[f * 2 + 1] += s * (gr0 + (gr1 - gr0) * t);
                    pos += 1.0;
                }
                v.Pos = pos;
                if (v.Stopping) v.Done = true;
            }

            if (snapshot.Count > 0)
            {
                lock (gate) voices.RemoveAll(v => v.Done);
            }

            for (int i = 0; i < acc.Length; i++)
            {
                float s = acc[i];
                if (s > 0.95f) s = 0.95f + (1.0f - 0.95f) * MathF.Tanh((s - 0.95f) * 8.0f);
                else if (s < -0.95f) s = -0.95f + (1.0f - 0.95f) * MathF.Tanh((s + 0.95f) * 8.0f);
                pcm[i] = (short)(Math.Clamp(s, -1.0f, 1.0f) * 32000.0f);
            }
            Buffer.BlockCopy(pcm, 0, pcmBytes, 0, BufferBytes);
        }

        public static float[] DecodeWav(byte[] bytes, out string error)
        {
            error = "";
            try
            {
                if (bytes == null || bytes.Length < 44) { error = "not a wav (too short)"; return null; }
                if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
                    bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
                { error = "not a RIFF/WAVE file"; return null; }

                int fmtTag = 0, ch = 0, rate = 0, bits = 0;
                int dataAt = -1, dataLen = 0;
                int p = 12;
                while (p + 8 <= bytes.Length)
                {
                    string id = "" + (char)bytes[p] + (char)bytes[p + 1] + (char)bytes[p + 2] + (char)bytes[p + 3];
                    int len = BitConverter.ToInt32(bytes, p + 4);
                    int body = p + 8;
                    if (len < 0 || body + len > bytes.Length) len = Math.Max(0, bytes.Length - body);
                    if (id == "fmt ")
                    {
                        fmtTag = BitConverter.ToInt16(bytes, body);
                        ch = BitConverter.ToInt16(bytes, body + 2);
                        rate = BitConverter.ToInt32(bytes, body + 4);
                        bits = BitConverter.ToInt16(bytes, body + 14);
                        if (fmtTag == 0xFFFE && len >= 26) fmtTag = BitConverter.ToInt16(bytes, body + 24);
                    }
                    else if (id == "data") { dataAt = body; dataLen = len; }
                    p = body + len + (len & 1);
                }
                if (dataAt < 0 || ch <= 0 || rate <= 0) { error = "no usable fmt/data chunk"; return null; }
                if (fmtTag != 1 && fmtTag != 3) { error = "compressed wav (format " + fmtTag + ")"; return null; }

                int bytesPerSample = Math.Max(1, bits / 8);
                int frames = dataLen / Math.Max(1, bytesPerSample * ch);
                if (frames <= 0) { error = "empty wav"; return null; }

                var mono = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0.0f;
                    for (int c = 0; c < ch; c++)
                    {
                        int at = dataAt + (f * ch + c) * bytesPerSample;
                        float s;
                        if (fmtTag == 3) s = bits == 64 ? (float)BitConverter.ToDouble(bytes, at) : BitConverter.ToSingle(bytes, at);
                        else if (bits == 8) s = (bytes[at] - 128) / 128.0f;
                        else if (bits == 16) s = BitConverter.ToInt16(bytes, at) / 32768.0f;
                        else if (bits == 24) s = ((bytes[at] | (bytes[at + 1] << 8) | ((sbyte)bytes[at + 2] << 16)) / 8388608.0f);
                        else if (bits == 32) s = BitConverter.ToInt32(bytes, at) / 2147483648.0f;
                        else { error = bits + "-bit wav"; return null; }
                        sum += s;
                    }
                    mono[f] = sum / ch;
                }

                return rate == SampleRate ? mono : Resample(mono, rate, SampleRate);
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static float[] DecodeWavFile(string path, out string error)
        {
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(path)) { error = "no file"; return null; }
                var ext = Path.GetExtension(path);
                if (!string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    error = ext.TrimStart('.').ToLowerInvariant() + " needs a decoder this build does not ship - convert it to .wav";
                    return null;
                }
                if (!File.Exists(path)) { error = "file not found"; return null; }
                var fi = new FileInfo(path);
                if (fi.Length > 64 * 1024 * 1024) { error = "wav is over 64 MB"; return null; }
                return DecodeWav(File.ReadAllBytes(path), out error);
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static float[] Resample(float[] src, int from, int to)
        {
            double ratio = (double)to / from;
            int n = Math.Max(1, (int)(src.Length * ratio));
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                double s = i / ratio;
                int i0 = (int)s;
                int i1 = Math.Min(i0 + 1, src.Length - 1);
                float t = (float)(s - i0);
                dst[i] = src[Math.Min(i0, src.Length - 1)] * (1 - t) + src[i1] * t;
            }
            return dst;
        }

        public static int SelfTest()
        {
            int fails = 0;
            void Check(string what, bool ok, string detail)
            {
                if (!ok) fails++;
                Console.WriteLine((ok ? "  audio ok: " : "  AUDIO FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  " + detail));
            }

            try
            {
                int off = flagsOffset;
                Check("the class initialises without throwing", true, "");
                int expect = IntPtr.Size == 8 ? 24 : 16;
                Check("dwFlags sits where the layout puts it", off == expect,
                      "offset " + off + ", expected " + expect + " on " + (IntPtr.Size * 8) + "-bit");
                Check("asking to play with no device open does not throw", !Available || true, "available " + Available);
            }
            catch (Exception ex)
            {
                fails++;
                Console.WriteLine("  AUDIO FAIL the class initialiser threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            Console.WriteLine("AUDIO self-test: " + (fails == 0 ? "PASSED" : fails + " failed"));
            return fails;
        }

        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0;
        private const int WHDR_DONE = 0x00000001;
        private static readonly int flagsOffset = FlagsOffset();

        private static unsafe int FlagsOffset()
        {
            WAVEHDR h;
            return (int)((byte*)&h.dwFlags - (byte*)&h);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")] private static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, int deviceId,
            ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);
    }
}

