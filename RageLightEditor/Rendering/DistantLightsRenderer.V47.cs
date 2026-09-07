using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    public class DistantLightsRenderer_V47 : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Vtx
        {
            public Vector3 Centre;
            public Vector2 Corner;
            public uint Colour;
            public const int Stride = 24;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Vars
        {
            public Matrix ViewProj;
            public Vector4 CameraPos;
            public Vector4 Params;
        }

        private sealed class Batch
        {
            public Buffer Vb;
            public int Count;
            public void Dispose() { Vb?.Dispose(); Vb = null; }
        }

        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<Vars> cbuffer;
        private readonly Dictionary<object, Batch> batches = new Dictionary<object, Batch>();
        private readonly HashSet<object> usedThisFrame = new HashSet<object>();
        private readonly List<object> dead = new List<object>();

        public int LightsDrawn, BatchesDrawn;

        private static readonly Vector2[] QuadCorners =
        {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1),
            new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1),
        };

        public DistantLightsRenderer_V47(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "distantlights.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
                new InputElement("COLOR", 0, Format.R32_UInt, 20, 0),
            });
            cbuffer = new ConstantBuffer<Vars>(device);
        }

        public static Vector4 UnpackRgbi_V47(uint c) =>
            new Vector4(((c >> 16) & 0xFF) / 255.0f, ((c >> 8) & 0xFF) / 255.0f,
                        (c & 0xFF) / 255.0f, ((c >> 24) & 0xFF) / 255.0f);

        public static float SpriteRadius_V47(float intensity, float dist) =>
            Math.Min(intensity * Math.Min(dist, 50.0f) * 0.1f, 3.0f);

        public static float NightFade_V47(float hour)
        {
            if (hour < 5.0f || hour >= 21.0f) return 1.0f;
            if (hour < 6.0f) return 6.0f - hour;
            if (hour >= 20.0f) return hour - 20.0f;
            return 0.0f;
        }

        private Batch Build(YmapDistantLODLights key)
        {
            var b = new Batch();
            var pos = key.positions;
            var col = key.colours;
            int n = Math.Min(pos?.Length ?? 0, col?.Length ?? 0);
            if (n == 0) return b;
            var v = new Vtx[n * 6];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                var p = pos[i].ToVector3();
                uint c = col[i];
                for (int q = 0; q < 6; q++)
                    v[k++] = new Vtx { Centre = p, Corner = QuadCorners[q], Colour = c };
            }
            b.Vb = Buffer.Create(device, BindFlags.VertexBuffer, v);
            b.Count = v.Length;
            return b;
        }

        public void Draw(DeviceContext context, IEnumerable<YmapFile> ymaps,
                         Matrix viewProj, Vector3 cameraPos, ShaderResourceView texture, float fade,
                         float pixelScale = 0f)
        {
            LightsDrawn = BatchesDrawn = 0;
            if (ymaps == null || fade <= 0.001f) return;
            usedThisFrame.Clear();

            bool stateSet = false;
            foreach (var y in ymaps)
            {
                var dll = y?.DistantLODLights;
                if (dll == null) continue;
                usedThisFrame.Add(dll);
                if (!batches.TryGetValue(dll, out var b))
                {
                    b = Build(dll);
                    batches[dll] = b;
                }
                if (b.Vb == null || b.Count == 0) continue;

                if (!stateSet)
                {
                    stateSet = true;
                    var vars = new Vars
                    {
                        ViewProj = Matrix.Transpose(viewProj),
                        CameraPos = new Vector4(cameraPos, fade),
                        Params = new Vector4(texture != null ? 1 : 0, pixelScale, 0, 0),
                    };
                    cbuffer.Update(context, ref vars);
                    shader.Apply(context);
                    context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
                    context.PixelShader.SetConstantBuffer(0, cbuffer.Buffer);
                    context.PixelShader.SetShaderResource(0, texture);
                    context.PixelShader.SetSampler(0, CommonStates.LinearClamp);
                    context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
                    context.OutputMerger.SetBlendState(CommonStates.BlendAdditive);
                    context.OutputMerger.SetDepthStencilState(CommonStates.DepthReadOnly);
                    context.Rasterizer.State = CommonStates.RasterSolid;
                }
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.Vb, Vtx.Stride, 0));
                context.Draw(b.Count, 0);
                LightsDrawn += b.Count / 6;
                BatchesDrawn++;
            }
            if (stateSet) context.PixelShader.SetShaderResource(0, null);
            Evict();
        }

        private void Evict()
        {
            dead.Clear();
            foreach (var kv in batches)
                if (!usedThisFrame.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var k in dead)
            {
                batches[k].Dispose();
                batches.Remove(k);
            }
        }

        public void Dispose()
        {
            foreach (var b in batches.Values) b.Dispose();
            batches.Clear();
            cbuffer?.Dispose();
            shader?.Dispose();
        }
    }
}
