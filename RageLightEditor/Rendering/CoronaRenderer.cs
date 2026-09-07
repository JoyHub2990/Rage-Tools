using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace RageLightEditor.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CoronaVertex
    {
        public Vector3 Centre;
        public Vector2 Corner;
        public Vector4 Colour;
        public float Size;
        public const int Stride = 40;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CoronaVars
    {
        public Matrix ViewProj;
        public Vector4 CameraPos;
        public Vector4 Params;
    }

    public class CoronaRenderer : IDisposable
    {
        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<CoronaVars> cbuffer;
        private Buffer vbuffer;
        private int capacity;
        private readonly List<CoronaVertex> verts = new List<CoronaVertex>(256);

        private static readonly Vector2[] QuadCorners =
        {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1),
            new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1),
        };

        public CoronaRenderer(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "corona.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 20, 0),
                new InputElement("TEXCOORD", 1, Format.R32_Float, 36, 0),
            });
            cbuffer = new ConstantBuffer<CoronaVars>(device);
        }

        public void Add(Vector3 worldPos, Vector3 colour, float size, float intensity)
        {
            if (size <= 0.0001f || intensity <= 0.0001f) return;
            var col = new Vector4(colour * intensity, 1.0f);
            foreach (var c in QuadCorners)
            {
                verts.Add(new CoronaVertex { Centre = worldPos, Corner = c, Colour = col, Size = size });
            }
        }

        public void Flush(DeviceContext context, Matrix viewProj, Vector3 cameraPos, ShaderResourceView texture = null)
        {
            if (verts.Count == 0) return;

            if (vbuffer == null || capacity < verts.Count)
            {
                vbuffer?.Dispose();
                capacity = Math.Max(verts.Count + 256, 1024);
                vbuffer = new Buffer(device, capacity * CoronaVertex.Stride, ResourceUsage.Dynamic,
                    BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
            }

            var box = context.MapSubresource(vbuffer, 0, MapMode.WriteDiscard, MapFlags.None);
            unsafe
            {
                var dst = (CoronaVertex*)box.DataPointer;
                for (int i = 0; i < verts.Count; i++) dst[i] = verts[i];
            }
            context.UnmapSubresource(vbuffer, 0);

            var vars = new CoronaVars
            {
                ViewProj = Matrix.Transpose(viewProj),
                CameraPos = new Vector4(cameraPos, 0),
                Params = new Vector4(texture != null ? 1 : 0, 0, 0, 0),
            };
            cbuffer.Update(context, ref vars);

            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.PixelShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.PixelShader.SetShaderResource(0, texture);
            context.PixelShader.SetSampler(0, CommonStates.LinearClamp);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vbuffer, CoronaVertex.Stride, 0));
            context.OutputMerger.SetBlendState(CommonStates.BlendAdditive);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthReadOnly);
            context.Rasterizer.State = CommonStates.RasterSolid;
            context.Draw(verts.Count, 0);
            if (texture != null) context.PixelShader.SetShaderResource(0, null);

            verts.Clear();
        }

        public void Dispose()
        {
            vbuffer?.Dispose();
            cbuffer?.Dispose();
            shader?.Dispose();
        }
    }
}

