using System;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Vector4 Colour0;
        public Vector4 Colour1;
        public Vector2 UV0;
        public Vector2 UV1;

        public const int Stride = 88;
    }

    public static class VertexDecoder
    {
        public static MeshVertex[] Decode(VertexData vdata, ushort[] indices)
        {
            if (vdata?.VertexBytes == null || vdata.Info == null) return null;

            var bytes = vdata.VertexBytes;
            var decl = vdata.Info;
            int stride = decl.Stride;
            int count = vdata.VertexCount;
            if (count <= 0 || stride <= 0) return null;
            count = Math.Min(count, bytes.Length / stride);

            uint flags = decl.Flags;
            ulong types = (ulong)decl.Types;

            var slotOffset = new int[16];
            var slotType = new VertexComponentType[16];
            int off = 0;
            for (int k = 0; k < 16; k++)
            {
                slotOffset[k] = -1;
                if (((flags >> k) & 0x1) == 0) continue;
                var ct = (VertexComponentType)((types >> (k * 4)) & 0xF);
                slotType[k] = ct;
                slotOffset[k] = off;
                off += VertexComponentTypes.GetSizeInBytes(ct);
            }

            bool hasNormal = slotOffset[3] >= 0;
            bool hasTangent = slotOffset[14] >= 0;
            bool hasColour = slotOffset[4] >= 0;
            bool hasColour1 = slotOffset[5] >= 0;
            bool hasUV0 = slotOffset[6] >= 0;
            bool hasUV1 = slotOffset[7] >= 0;

            var verts = new MeshVertex[count];
            for (int v = 0; v < count; v++)
            {
                int vbase = v * stride;
                var mv = new MeshVertex
                {
                    Normal = Vector3.Zero,
                    Tangent = new Vector4(1, 0, 0, 1),
                    Colour0 = Vector4.One,
                    Colour1 = Vector4.One,
                    UV0 = Vector2.Zero,
                    UV1 = Vector2.Zero,
                };

                mv.Position = ReadVector3(bytes, vbase + slotOffset[0], slotType[0]);
                if (hasNormal) mv.Normal = ReadVector3(bytes, vbase + slotOffset[3], slotType[3]);
                if (hasTangent)
                {
                    var t = ReadVector4(bytes, vbase + slotOffset[14], slotType[14]);
                    mv.Tangent = t;
                }
                if (hasColour) mv.Colour0 = ReadVector4(bytes, vbase + slotOffset[4], slotType[4]);
                if (hasColour1) mv.Colour1 = ReadVector4(bytes, vbase + slotOffset[5], slotType[5]);
                if (hasUV0) { var t = ReadVector4(bytes, vbase + slotOffset[6], slotType[6]); mv.UV0 = new Vector2(t.X, t.Y); }
                if (hasUV1) { var t = ReadVector4(bytes, vbase + slotOffset[7], slotType[7]); mv.UV1 = new Vector2(t.X, t.Y); }

                verts[v] = mv;
            }

            if (!hasNormal && indices != null)
            {
                ComputeNormals(verts, indices);
            }

            return verts;
        }

        private static Vector3 ReadVector3(byte[] b, int o, VertexComponentType t)
        {
            var v = ReadVector4(b, o, t);
            return new Vector3(v.X, v.Y, v.Z);
        }

        private static Vector4 ReadVector4(byte[] b, int o, VertexComponentType t)
        {
            switch (t)
            {
                case VertexComponentType.Float:
                    return new Vector4(F32(b, o), 0, 0, 0);
                case VertexComponentType.Float2:
                    return new Vector4(F32(b, o), F32(b, o + 4), 0, 0);
                case VertexComponentType.Float3:
                    return new Vector4(F32(b, o), F32(b, o + 4), F32(b, o + 8), 0);
                case VertexComponentType.Float4:
                    return new Vector4(F32(b, o), F32(b, o + 4), F32(b, o + 8), F32(b, o + 12));
                case VertexComponentType.Half2:
                    return new Vector4(F16(b, o), F16(b, o + 2), 0, 0);
                case VertexComponentType.Half4:
                    return new Vector4(F16(b, o), F16(b, o + 2), F16(b, o + 4), F16(b, o + 6));
                case VertexComponentType.Colour:
                    return new Vector4(b[o] / 255.0f, b[o + 1] / 255.0f, b[o + 2] / 255.0f, b[o + 3] / 255.0f);
                case VertexComponentType.UByte4:
                    return new Vector4(b[o], b[o + 1], b[o + 2], b[o + 3]);
                case VertexComponentType.RGBA8SNorm:
                    return ReadDec3N(b, o);
                default:
                    return Vector4.Zero;
            }
        }

        private static Vector4 ReadDec3N(byte[] b, int o)
        {
            uint u = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            int ix = SignExtend10((int)(u & 0x3FF));
            int iy = SignExtend10((int)((u >> 10) & 0x3FF));
            int iz = SignExtend10((int)((u >> 20) & 0x3FF));
            const float s = 1.0f / 511.0f;
            return new Vector4(ix * s, iy * s, iz * s, 0);
        }

        private static int SignExtend10(int v)
        {
            return (v & 0x200) != 0 ? v - 0x400 : v;
        }

        private static float F32(byte[] b, int o)
        {
            return BitConverter.ToSingle(b, o);
        }

        private static float F16(byte[] b, int o)
        {
            ushort h = (ushort)(b[o] | (b[o + 1] << 8));
            return HalfToFloat(h);
        }

        private static float HalfToFloat(ushort h)
        {
            int sign = (h >> 15) & 1;
            int exp = (h >> 10) & 0x1F;
            int mant = h & 0x3FF;
            float f;
            if (exp == 0)
            {
                f = mant * (1.0f / 16777216.0f) * 16.0f;
            }
            else if (exp == 31)
            {
                f = mant == 0 ? float.PositiveInfinity : float.NaN;
            }
            else
            {
                f = (float)((1.0 + mant / 1024.0) * Math.Pow(2, exp - 15));
            }
            return sign == 1 ? -f : f;
        }

        private static void ComputeNormals(MeshVertex[] verts, ushort[] indices)
        {
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;
                var e1 = verts[i1].Position - verts[i0].Position;
                var e2 = verts[i2].Position - verts[i0].Position;
                var n = Vector3.Cross(e1, e2);
                verts[i0].Normal += n;
                verts[i1].Normal += n;
                verts[i2].Normal += n;
            }
            for (int i = 0; i < verts.Length; i++)
            {
                var n = verts[i].Normal;
                var len = n.Length();
                verts[i].Normal = len > 1e-6f ? n / len : Vector3.UnitZ;
            }
        }
    }
}

