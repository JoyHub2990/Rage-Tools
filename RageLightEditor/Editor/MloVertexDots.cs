using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Editor
{
    public sealed class MloVertexDots : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DotVars
        {
            public Matrix World;
            public Matrix ViewProj;
            public Vector4 CamPull;
            public Vector4 Centre;
            public Vector4 Colour;
            public Vector4 Params;
        }

        private const string Hlsl = @"
cbuffer DotVars : register(b0)
{
    float4x4 World;
    float4x4 ViewProj;
    float4 CamPull;
    float4 Centre;
    float4 Colour;
    float4 Params;
};
struct VS_In { float3 Pos : POSITION; uint Vid : SV_VertexID; };
struct PS_In { float4 Pos : SV_POSITION; float2 UV : TEXCOORD0; float Fade : TEXCOORD1; };
static const float2 Corners[6] = { float2(-1,-1), float2(-1,1), float2(1,-1), float2(1,-1), float2(-1,1), float2(1,1) };
PS_In VSMain(VS_In i)
{
    PS_In o;
    float3 w = mul(float4(i.Pos, 1.0), World).xyz;
    float fade = 1.0;
    if (Centre.w > 0.0)
    {
        float d = distance(w, Centre.xyz);
        fade = saturate((Centre.w - d) / max(Centre.w * 0.33, 0.05));
    }
    //toward the eye a little, so a vertex sitting on the surface it belongs to wins the depth test
    float3 p = w + (CamPull.xyz - w) * CamPull.w;
    float4 clip = mul(float4(p, 1.0), ViewProj);
    if (fade <= 0.0 || clip.w <= 0.0)
    {
        //outside the cloud or behind the eye: a depth outside [0,1] clips the whole quad (all six vertices agree)
        o.Pos = float4(0.0, 0.0, -2.0, 1.0); o.UV = float2(0.0, 0.0); o.Fade = 0.0;
        return o;
    }
    float2 c = Corners[i.Vid % 6];
    float hs = Params.x + Params.w;   //the quad holds the feather too
    clip.xy += c * hs * float2(2.0 / Params.y, 2.0 / Params.z) * clip.w;
    o.Pos = clip; o.UV = c * hs; o.Fade = fade;
    return o;
}
float4 PSMain(PS_In i) : SV_TARGET
{
    float r = length(i.UV);
    float a = saturate((Params.x - r) / max(Params.w, 0.001) + 0.5);
    return float4(Colour.rgb, Colour.a * a * i.Fade);
}";

        private sealed class Batch
        {
            public Buffer VB;
            public int Count;
            public int LastUsedFrame;
        }

        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<DotVars> cbuffer;
        private readonly Dictionary<Vector3[], Batch> batches = new Dictionary<Vector3[], Batch>(ReferenceEqualityComparer.Instance);
        private int frame;

        public int DotsDrawn, MeshesDrawn;

        public MloVertexDots(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, Hlsl, "VSMain", "PSMain",
                new[] { new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerInstanceData, 1) },
                "vertexdots.hlsl");
            cbuffer = new ConstantBuffer<DotVars>(device);
        }

        public void BeginFrame()
        {
            frame++;
            DotsDrawn = 0; MeshesDrawn = 0;
            if ((frame & 255) == 0 && batches.Count > 0)
            {
                var dead = new List<Vector3[]>();
                foreach (var kv in batches) if (frame - kv.Value.LastUsedFrame > 600) dead.Add(kv.Key);
                foreach (var k in dead) { batches[k].VB?.Dispose(); batches.Remove(k); }
            }
        }

        private Batch GetBatch(Vector3[] verts)
        {
            if (!batches.TryGetValue(verts, out var b))
            {
                b = new Batch { Count = verts.Length };
                try { b.VB = Buffer.Create(device, BindFlags.VertexBuffer, verts); }
                catch { b.VB = null; b.Count = 0; }
                batches[verts] = b;
            }
            b.LastUsedFrame = frame;
            return b;
        }

        public void Draw(DeviceContext context, Camera cam, float viewW, float viewH, RenderMesh mesh,
                         Vector3 centre, float radius, float halfSizePx, Vector4 colour, float pull = 0.004f)
        {
            if (mesh?.PickVerts == null || mesh.PickVerts.Length == 0 || cam == null) return;
            var b = GetBatch(mesh.PickVerts);
            if (b.VB == null || b.Count == 0) return;
            var vars = new DotVars
            {
                World = Matrix.Transpose(mesh.Transform),
                ViewProj = Matrix.Transpose(cam.ViewProjMatrix),
                CamPull = new Vector4(cam.Position, pull),
                Centre = new Vector4(centre, radius),
                Colour = colour,
                Params = new Vector4(Math.Max(halfSizePx, 0.5f), Math.Max(viewW, 1f), Math.Max(viewH, 1f), 1.0f),
            };
            cbuffer.Update(context, ref vars);
            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.PixelShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(b.VB, 12, 0));
            context.OutputMerger.SetBlendState(CommonStates.BlendAlpha);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthReadOnly);
            context.Rasterizer.State = CommonStates.RasterSolid;
            context.DrawInstanced(6, b.Count, 0, 0);
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(null, 0, 0));
            DotsDrawn += b.Count; MeshesDrawn++;
        }

        public void Dispose()
        {
            foreach (var kv in batches) kv.Value.VB?.Dispose();
            batches.Clear();
            cbuffer?.Dispose();
            shader?.Dispose();
        }
    }
}

