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
    public struct ShaftVertex
    {
        public Vector3 Pos;
        public Vector4 Centre;
        public Vector4 AxisX;
        public Vector4 AxisY;
        public Vector4 AxisD;
        public Vector4 Colour;
        public const int Stride = 12 + 16 * 5;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ShaftVars
    {
        public Matrix ViewProj;
        public Vector4 CameraPos;
        public Vector4 CameraFwd;
        public Vector4 ProjParams;
        public Vector4 Params;
    }

    public class LightShaftRenderer : IDisposable
    {
        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<ShaftVars> cbuffer;
        private Buffer vbuffer;
        private int capacity;
        private readonly List<ShaftVertex> verts = new List<ShaftVertex>(36 * 64);
        private readonly RasterizerState rasterBackFaces;

        public float NoiseAmount = 0.35f;
        public int Steps = 14;
        public float Intensity = 1.0f;
        public static readonly bool DebugFlat = Environment.GetEnvironmentVariable("RLE_SHAFTDEBUG") == "1";
        public string LastError;
        public bool Ready => shader != null;
        public int BoxesDrawn { get; private set; }

        public LightShaftRenderer(Device device)
        {
            this.device = device;
            try
            {
                shader = new ShaderSet(device, "lightshaft.hlsl", new[]
                {
                    new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                    new InputElement("TEXCOORD", 0, Format.R32G32B32A32_Float, 12, 0),
                    new InputElement("TEXCOORD", 1, Format.R32G32B32A32_Float, 28, 0),
                    new InputElement("TEXCOORD", 2, Format.R32G32B32A32_Float, 44, 0),
                    new InputElement("TEXCOORD", 3, Format.R32G32B32A32_Float, 60, 0),
                    new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 76, 0),
                });
            }
            catch (Exception ex) { LastError = ex.Message; shader = null; }
            cbuffer = new ConstantBuffer<ShaftVars>(device);
            rasterBackFaces = new RasterizerState(device, new RasterizerStateDescription
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.Front,
                IsFrontCounterClockwise = true,
                IsDepthClipEnabled = false,
                IsMultisampleEnabled = true,
            });
        }

        public void Add(Vector3 centre, Vector3 X, Vector3 Y, Vector3 dirLen, float softness, float spread,
                        Vector3 radiance, float densityExp)
        {
            var f0c = centre + dirLen;
            Vector3 n0 = centre - X - Y, n1 = centre + X - Y, n2 = centre + X + Y, n3 = centre - X + Y;
            Vector3 f0 = f0c + (-X - Y) * spread, f1 = f0c + (X - Y) * spread, f2 = f0c + (X + Y) * spread, f3 = f0c + (-X + Y) * spread;
            var boxCentre = centre + dirLen * 0.5f;
            var v = new ShaftVertex
            {
                Centre = new Vector4(centre, softness),
                AxisX = new Vector4(X, 0), AxisY = new Vector4(Y, 0),
                AxisD = new Vector4(dirLen, spread),
                Colour = new Vector4(radiance, densityExp),
            };
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                var nrm = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(nrm, p0 - boxCentre) < 0) { var t = p1; p1 = p3; p3 = t; }
                v.Pos = p0; verts.Add(v); v.Pos = p1; verts.Add(v); v.Pos = p2; verts.Add(v);
                v.Pos = p0; verts.Add(v); v.Pos = p2; verts.Add(v); v.Pos = p3; verts.Add(v);
            }
            Quad(n0, n1, n2, n3);
            Quad(f0, f1, f2, f3);
            Quad(n0, n1, f1, f0); Quad(n1, n2, f2, f1); Quad(n2, n3, f3, f2); Quad(n3, n0, f0, f3);
            BoxesDrawn++;
        }

        public void Clear() { verts.Clear(); BoxesDrawn = 0; }
        public int Count => verts.Count / 36;

        public void Flush(DeviceContext context, Camera camera, ShaderResourceView depthSrv, int depthMode,
                          int viewportWidth, int viewportHeight, float time)
        {
            if (verts.Count == 0 || shader == null) { verts.Clear(); return; }
            if (vbuffer == null || capacity < verts.Count)
            {
                vbuffer?.Dispose();
                capacity = Math.Max(verts.Count + 36 * 16, 36 * 64);
                vbuffer = new Buffer(device, capacity * ShaftVertex.Stride, ResourceUsage.Dynamic,
                    BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
            }
            var box = context.MapSubresource(vbuffer, 0, MapMode.WriteDiscard, MapFlags.None);
            unsafe
            {
                var dst = (ShaftVertex*)box.DataPointer;
                for (int i = 0; i < verts.Count; i++) dst[i] = verts[i];
            }
            context.UnmapSubresource(vbuffer, 0);

            var pm = camera.ProjMatrix;
            var vars = new ShaftVars
            {
                ViewProj = Matrix.Transpose(camera.ViewProjMatrix),
                CameraPos = new Vector4(camera.Position, time),
                CameraFwd = new Vector4(Vector3.Normalize(camera.GetForward()), depthSrv != null ? depthMode : 0),
                ProjParams = new Vector4(pm.M33, pm.M43, 1.0f / Math.Max(viewportWidth, 1), 1.0f / Math.Max(viewportHeight, 1)),
                Params = new Vector4(NoiseAmount, Steps, Intensity, DebugFlat ? 1 : 0),
            };
            cbuffer.Update(context, ref vars);

            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.PixelShader.SetConstantBuffer(0, cbuffer.Buffer);
            if (depthMode == 2) context.PixelShader.SetShaderResource(26, depthSrv);
            else context.PixelShader.SetShaderResource(25, depthSrv);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vbuffer, ShaftVertex.Stride, 0));
            context.OutputMerger.SetBlendState(CommonStates.BlendAdditive);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthDisabled);
            context.Rasterizer.State = rasterBackFaces;
            context.Draw(verts.Count, 0);
            context.PixelShader.SetShaderResource(25, null);
            context.PixelShader.SetShaderResource(26, null);
            context.Rasterizer.State = CommonStates.RasterSolid;
            verts.Clear();
        }

        public void Dispose()
        {
            vbuffer?.Dispose();
            cbuffer?.Dispose();
            shader?.Dispose();
            rasterBackFaces?.Dispose();
        }
    }
}

