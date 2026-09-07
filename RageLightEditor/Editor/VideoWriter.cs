using System;
using System.Runtime.InteropServices;

namespace RageLightEditor.Editor
{
    public sealed class VideoWriter : IDisposable
    {
        private IMFSinkWriter writer;
        private int streamIndex;
        private long frameDuration;
        private long timestamp;
        private readonly int width, height;
        private bool started;

        public VideoWriter(string path, int width, int height, int fps, int bitrate)
        {
            this.width = width & ~1;
            this.height = height & ~1;
            fps = Math.Max(1, fps);

            Check(MFStartup(MF_VERSION, 0), "MFStartup");
            started = true;

            Check(MFCreateAttributes(out IMFAttributes attrs, 2), "MFCreateAttributes");
            attrs.SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            attrs.SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, 1);

            Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out writer), "MFCreateSinkWriterFromURL");
            Marshal.ReleaseComObject(attrs);

            Check(MFCreateMediaType(out IMFMediaType outType), "MFCreateMediaType(out)");
            outType.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            outType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
            outType.SetUINT32(MF_MT_AVG_BITRATE, (uint)bitrate);
            outType.SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_High);
            outType.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            outType.SetUINT64(MF_MT_FRAME_SIZE, Pack(this.width, this.height));
            outType.SetUINT64(MF_MT_FRAME_RATE, Pack(fps, 1));
            outType.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1));
            Check(writer.AddStream(outType, out streamIndex), "AddStream");
            Marshal.ReleaseComObject(outType);

            Check(MFCreateMediaType(out IMFMediaType inType), "MFCreateMediaType(in)");
            inType.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            inType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            inType.SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            inType.SetUINT64(MF_MT_FRAME_SIZE, Pack(this.width, this.height));
            inType.SetUINT64(MF_MT_FRAME_RATE, Pack(fps, 1));
            inType.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1));
            Check(writer.SetInputMediaType(streamIndex, inType, null), "SetInputMediaType");
            Marshal.ReleaseComObject(inType);

            Check(writer.BeginWriting(), "BeginWriting");
            frameDuration = 10_000_000L / fps;
        }

        public void WriteFrame(IntPtr bgra, int stride)
        {
            int frameBytes = width * height * 4;
            Check(MFCreateMemoryBuffer(frameBytes, out IMFMediaBuffer buffer), "MFCreateMemoryBuffer");
            Check(buffer.Lock(out IntPtr dst, out _, out _), "Buffer.Lock");
            try
            {
                Check(MFCopyImage(dst + (height - 1) * width * 4, -width * 4,
                                  bgra, stride, width * 4, height), "MFCopyImage");
            }
            finally { buffer.Unlock(); }
            buffer.SetCurrentLength(frameBytes);

            Check(MFCreateSample(out IMFSample sample), "MFCreateSample");
            sample.AddBuffer(buffer);
            sample.SetSampleTime(timestamp);
            sample.SetSampleDuration(frameDuration);
            Check(writer.WriteSample(streamIndex, sample), "WriteSample");
            timestamp += frameDuration;

            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
        }

        public void Dispose()
        {
            if (writer != null)
            {
                try { writer.Finalize_(); } catch { }
                Marshal.ReleaseComObject(writer);
                writer = null;
            }
            if (started) { try { MFShutdown(); } catch { } started = false; }
            GC.SuppressFinalize(this);
        }

        public static bool IsAvailable()
        {
            try
            {
                if (MFStartup(MF_VERSION, 0) != 0) return false;
                MFShutdown();
                return true;
            }
            catch { return false; }
        }

        private static ulong Pack(int hi, int lo) => ((ulong)(uint)hi << 32) | (uint)lo;

        private static void Check(int hr, string what)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        private const uint MF_VERSION = 0x00020070;
        private const uint MFVideoInterlace_Progressive = 2;

        private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
        private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
        private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
        private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        private static readonly Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
        private const uint eAVEncH264VProfile_High = 100;
        private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
        private static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateAttributes(out IMFAttributes attrs, uint initialSize);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCopyImage(IntPtr dest, int destStride, IntPtr src, int srcStride,
                                              int widthInBytes, int lines);
        [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream,
                                                            IMFAttributes attrs, out IMFSinkWriter writer);

        [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            int GetItem(ref Guid key, IntPtr value);
            int GetItemType(ref Guid key, out int type);
            int CompareItem(ref Guid key, IntPtr value, out bool result);
            int Compare(IMFAttributes theirs, int matchType, out bool result);
            int GetUINT32(ref Guid key, out uint value);
            int GetUINT64(ref Guid key, out ulong value);
            int GetDouble(ref Guid key, out double value);
            int GetGUID(ref Guid key, out Guid value);
            int GetStringLength(ref Guid key, out uint length);
            int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);
            int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            int GetBlobSize(ref Guid key, out uint size);
            int GetBlob(ref Guid key, IntPtr buf, uint bufSize, IntPtr blobSize);
            int GetAllocatedBlob(ref Guid key, out IntPtr buf, out uint size);
            int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
            int SetItem(ref Guid key, IntPtr value);
            int DeleteItem(ref Guid key);
            int DeleteAllItems();
            int SetUINT32(ref Guid key, uint value);
            int SetUINT64(ref Guid key, ulong value);
            int SetDouble(ref Guid key, double value);
            int SetGUID(ref Guid key, ref Guid value);
            int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            int SetBlob(ref Guid key, IntPtr buf, uint size);
            int SetUnknown(ref Guid key, IntPtr unknown);
            int LockStore();
            int UnlockStore();
            int GetCount(out uint count);
            int GetItemByIndex(uint index, out Guid key, IntPtr value);
            int CopyAllItems(IMFAttributes dest);
        }

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType : IMFAttributes
        {
            new int GetItem(ref Guid key, IntPtr value);
            new int GetItemType(ref Guid key, out int type);
            new int CompareItem(ref Guid key, IntPtr value, out bool result);
            new int Compare(IMFAttributes theirs, int matchType, out bool result);
            new int GetUINT32(ref Guid key, out uint value);
            new int GetUINT64(ref Guid key, out ulong value);
            new int GetDouble(ref Guid key, out double value);
            new int GetGUID(ref Guid key, out Guid value);
            new int GetStringLength(ref Guid key, out uint length);
            new int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);
            new int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            new int GetBlobSize(ref Guid key, out uint size);
            new int GetBlob(ref Guid key, IntPtr buf, uint bufSize, IntPtr blobSize);
            new int GetAllocatedBlob(ref Guid key, out IntPtr buf, out uint size);
            new int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
            new int SetItem(ref Guid key, IntPtr value);
            new int DeleteItem(ref Guid key);
            new int DeleteAllItems();
            new int SetUINT32(ref Guid key, uint value);
            new int SetUINT64(ref Guid key, ulong value);
            new int SetDouble(ref Guid key, double value);
            new int SetGUID(ref Guid key, ref Guid value);
            new int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            new int SetBlob(ref Guid key, IntPtr buf, uint size);
            new int SetUnknown(ref Guid key, IntPtr unknown);
            new int LockStore();
            new int UnlockStore();
            new int GetCount(out uint count);
            new int GetItemByIndex(uint index, out Guid key, IntPtr value);
            new int CopyAllItems(IMFAttributes dest);
            int GetMajorType(out Guid type);
            int IsCompressedFormat(out bool compressed);
            int IsEqual(IMFMediaType other, out uint flags);
            int GetRepresentation(Guid rep, out IntPtr ppv);
            int FreeRepresentation(Guid rep, IntPtr pv);
        }

        [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
            int Unlock();
            int GetCurrentLength(out int length);
            int SetCurrentLength(int length);
            int GetMaxLength(out int length);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample : IMFAttributes
        {
            new int GetItem(ref Guid key, IntPtr value);
            new int GetItemType(ref Guid key, out int type);
            new int CompareItem(ref Guid key, IntPtr value, out bool result);
            new int Compare(IMFAttributes theirs, int matchType, out bool result);
            new int GetUINT32(ref Guid key, out uint value);
            new int GetUINT64(ref Guid key, out ulong value);
            new int GetDouble(ref Guid key, out double value);
            new int GetGUID(ref Guid key, out Guid value);
            new int GetStringLength(ref Guid key, out uint length);
            new int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);
            new int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            new int GetBlobSize(ref Guid key, out uint size);
            new int GetBlob(ref Guid key, IntPtr buf, uint bufSize, IntPtr blobSize);
            new int GetAllocatedBlob(ref Guid key, out IntPtr buf, out uint size);
            new int GetUnknown(ref Guid key, ref Guid riid, out IntPtr ppv);
            new int SetItem(ref Guid key, IntPtr value);
            new int DeleteItem(ref Guid key);
            new int DeleteAllItems();
            new int SetUINT32(ref Guid key, uint value);
            new int SetUINT64(ref Guid key, ulong value);
            new int SetDouble(ref Guid key, double value);
            new int SetGUID(ref Guid key, ref Guid value);
            new int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            new int SetBlob(ref Guid key, IntPtr buf, uint size);
            new int SetUnknown(ref Guid key, IntPtr unknown);
            new int LockStore();
            new int UnlockStore();
            new int GetCount(out uint count);
            new int GetItemByIndex(uint index, out Guid key, IntPtr value);
            new int CopyAllItems(IMFAttributes dest);
            int GetSampleFlags(out uint flags);
            int SetSampleFlags(uint flags);
            int GetSampleTime(out long time);
            int SetSampleTime(long time);
            int GetSampleDuration(out long duration);
            int SetSampleDuration(long duration);
            int GetBufferCount(out int count);
            int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
            int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            int AddBuffer(IMFMediaBuffer buffer);
            int RemoveBufferByIndex(int index);
            int RemoveAllBuffers();
            int GetTotalLength(out int length);
            int CopyToBuffer(IMFMediaBuffer buffer);
        }

        [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSinkWriter
        {
            int AddStream(IMFMediaType targetType, out int streamIndex);
            int SetInputMediaType(int streamIndex, IMFMediaType inputType, IMFAttributes parameters);
            int BeginWriting();
            int WriteSample(int streamIndex, IMFSample sample);
            int SendStreamTick(int streamIndex, long timestamp);
            int PlaceMarker(int streamIndex, IntPtr context);
            int NotifyEndOfSegment(int streamIndex);
            int Flush(int streamIndex);
            [PreserveSig] int Finalize_();
            int GetServiceForStream(int streamIndex, ref Guid service, ref Guid riid, out IntPtr ppv);
            int GetStatistics(int streamIndex, IntPtr stats);
        }
    }
}

