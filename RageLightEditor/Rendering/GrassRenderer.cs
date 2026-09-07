using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CodeWalker.GameFiles;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using D3DBuffer = SharpDX.Direct3D11.Buffer;

namespace RageLightEditor.Rendering
{
    public sealed class GrassBatchGpu : IDisposable
    {
        public D3DBuffer Buffer;
        public ShaderResourceView SRV;
        public int Count;
        public int LastUsedFrame;
        public void Dispose() { SRV?.Dispose(); Buffer?.Dispose(); SRV = null; Buffer = null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GrassBatchVars
    {
        public Vector3 AabbMin; public float LodDist;
        public Vector3 AabbDelta; public float LodFadeStart;
        public Vector3 ScaleRange; public float LodInstFadeRange;
        public Vector3 CamPos; public float OrientToTerrain;
        public float AlphaScale; public float Legacy; public float FadeRange, FadePower;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public Vector4[] InstRot;
    }

    public partial class GrassRenderer : IDisposable
    {
        private readonly Device device;
        private ShaderSet shader;
        private readonly ConstantBuffer<GrassBatchVars> batchCB;
        private readonly Dictionary<YmapGrassInstanceBatch, GrassBatchGpu> gpu = new Dictionary<YmapGrassInstanceBatch, GrassBatchGpu>();
        private readonly List<YmapGrassInstanceBatch> evict = new List<YmapGrassInstanceBatch>();
        private readonly Vector4[] instRot = new Vector4[24];
        private int frame;
        public string LastError;

        public bool Enabled = true;
        public float DistanceScale = 1.0f;
        public int KeepFrames = 600;
        public static bool LegacyDefault => Environment.GetEnvironmentVariable("RLE_GRASSOLD") == "1";
        public bool Legacy = LegacyDefault;

        public int BatchesDrawn, InstancesDrawn, BatchesInRange, BatchesResident, BatchesAwaitingModel;
        public double LastRenderMs;
        public Func<CodeWalker.GameFiles.YmapFile, Vector3, bool> BatchHidden;

        public GrassRenderer(Device device)
        {
            this.device = device;
            batchCB = new ConstantBuffer<GrassBatchVars>(device);
            for (int k = 0; k < 8; k++)
            {
                var m = Matrix3x3.RotationZ(k * 0.25f * (float)Math.PI);
                instRot[k * 3 + 0] = new Vector4(m.Row1, 1);
                instRot[k * 3 + 1] = new Vector4(m.Row2, 1);
                instRot[k * 3 + 2] = new Vector4(m.Row3, 1);
            }
        }

        public bool ShaderReady => EnsureShader();

        private bool EnsureShader()
        {
            if (shader != null) return true;
            if (LastError != null) return false;
            try
            {
                string src = ShaderSet.Source("model.hlsl") + "\n" + ShaderSet.Source("grasslod.hlsl")
                                                            + "\n" + ShaderSet.Source("grass.hlsl");
                shader = new ShaderSet(device, src, "VSGrass", "PSGrass", new[]
                {
                    new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                    new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                    new InputElement("TANGENT", 0, Format.R32G32B32A32_Float, 24, 0),
                    new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 40, 0),
                    new InputElement("COLOR", 1, Format.R32G32B32A32_Float, 56, 0),
                    new InputElement("TEXCOORD", 0, Format.R32G32_Float, 72, 0),
                    new InputElement("TEXCOORD", 1, Format.R32G32_Float, 80, 0),
                }, "grass.hlsl");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.WriteLine("GRASS shader failed: " + ex.Message);
                return false;
            }
        }

        private GrassBatchGpu GetOrUpload(YmapGrassInstanceBatch batch)
        {
            if (gpu.TryGetValue(batch, out var g)) { g.LastUsedFrame = frame; return g; }
            var inst = batch.Instances;
            if (inst == null || inst.Length == 0) return null;
            int n = inst.Length;
            var raw = new uint[n * 4];
            for (int i = 0; i < n; i++)
            {
                var r = inst[i];
                raw[i * 4 + 0] = (uint)r.Position.u0 | ((uint)r.Position.u1 << 16);
                raw[i * 4 + 1] = (uint)r.Position.u2 | ((uint)r.NormalX << 16) | ((uint)r.NormalY << 24);
                raw[i * 4 + 2] = (uint)r.Color.b0 | ((uint)r.Color.b1 << 8) | ((uint)r.Color.b2 << 16) | ((uint)r.Scale << 24);
                raw[i * 4 + 3] = (uint)r.Ao | ((uint)r.Pad.b0 << 8) | ((uint)r.Pad.b1 << 16) | ((uint)r.Pad.b2 << 24);
            }
            g = new GrassBatchGpu { Count = n, LastUsedFrame = frame };
            try
            {
                g.Buffer = D3DBuffer.Create(device, BindFlags.ShaderResource, raw, 0, ResourceUsage.Immutable, CpuAccessFlags.None,
                    ResourceOptionFlags.BufferStructured, 16);
                g.SRV = new ShaderResourceView(device, g.Buffer, new ShaderResourceViewDescription
                {
                    Format = Format.Unknown,
                    Dimension = ShaderResourceViewDimension.Buffer,
                    Buffer = new ShaderResourceViewDescription.BufferResource { FirstElement = 0, ElementCount = n },
                });
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                g.Dispose();
                return null;
            }
            gpu[batch] = g;
            return g;
        }

        public void Render(DeviceContext ctx, Camera camera, IEnumerable<YmapFile> residentYmaps,
            Func<Archetype, RenderModel> modelFor, D3DBuffer sceneCB, float lodScale)
        {
            frame++;
            BatchesDrawn = InstancesDrawn = BatchesInRange = BatchesAwaitingModel = 0;
            BatchesResident = gpu.Count;
            if (!Enabled || residentYmaps == null || !EnsureShader()) { Evict(); return; }
            if (SceneRenderer.FrameRenderMode == 8) return;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            var camPos = camera.Position;
            var frustum = new BoundingFrustum(camera.ViewProjMatrix);
            bool stateSet = false;

            foreach (var ymap in residentYmaps)
            {
                var batches = ymap?.GrassInstanceBatches;
                if (batches == null || batches.Length == 0) continue;
                foreach (var batch in batches)
                {
                    if (batch?.Instances == null || batch.Instances.Length == 0) continue;
                    float lodDist = batch.Batch.lodDist * lodScale * DistanceScale * CullFudge_U5;
                    if (camPos.X < batch.AABBMin.X - lodDist) continue;
                    if (camPos.Y < batch.AABBMin.Y - lodDist) continue;
                    if (camPos.Z < batch.AABBMin.Z - lodDist) continue;
                    if (camPos.X > batch.AABBMax.X + lodDist) continue;
                    if (camPos.Y > batch.AABBMax.Y + lodDist) continue;
                    if (camPos.Z > batch.AABBMax.Z + lodDist) continue;
                    var sphere = new BoundingSphere(batch.Position, batch.Radius);
                    if (frustum.Contains(ref sphere) == ContainmentType.Disjoint) continue;
                    if (BatchHidden != null && BatchHidden(ymap, batch.Position)) continue;
                    BatchesInRange++;

                    var arch = batch.Archetype;
                    if (arch == null) continue;
                    var model = modelFor(arch);
                    if (model == null || model.Meshes.Count == 0) { BatchesAwaitingModel++; continue; }
                    var g = GetOrUpload(batch);
                    if (g == null) continue;

                    if (!stateSet)
                    {
                        shader.Apply(ctx);
                        ctx.VertexShader.SetConstantBuffer(0, sceneCB);
                        ctx.PixelShader.SetConstantBuffer(0, sceneCB);
                        ctx.VertexShader.SetConstantBuffer(2, batchCB.Buffer);
                        ctx.PixelShader.SetConstantBuffer(2, batchCB.Buffer);
                        ctx.PixelShader.SetSampler(0, CommonStates.LinearWrap);
                        ctx.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                        ctx.Rasterizer.State = CommonStates.RasterSolid;
                        ctx.OutputMerger.SetBlendState(CommonStates.BlendAlpha);
                        ctx.OutputMerger.SetDepthStencilState(CommonStates.DepthDefault);
                        stateSet = true;
                    }

                    var vars = new GrassBatchVars
                    {
                        AabbMin = batch.AABBMin,
                        AabbDelta = batch.AABBMax - batch.AABBMin,
                        ScaleRange = batch.Batch.ScaleRange,
                        CamPos = camPos,
                        LodDist = lodDist,
                        LodFadeStart = FadeStartFor_U5(batch.Batch.LodFadeStartDist * lodScale * DistanceScale * CullFudge_U5, lodDist),
                        LodInstFadeRange = batch.Batch.LodInstFadeRange,
                        OrientToTerrain = batch.Batch.OrientToTerrain,
                        AlphaScale = 7.0f,
                        Legacy = Legacy ? 1.0f : 0.0f,
                        FadeRange = FadeRange_U5,
                        FadePower = FadePower_U5,
                        InstRot = instRot,
                    };
                    batchCB.Update(ctx, ref vars);
                    ctx.VertexShader.SetShaderResource(2, g.SRV);

                    foreach (var mesh in model.Meshes)
                    {
                        if (mesh.NeverDraw || mesh.VB == null || mesh.IB == null) continue;
                        ctx.PixelShader.SetShaderResource(0, mesh.DiffuseSRV);
                        ctx.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(mesh.VB, MeshVertex.Stride, 0));
                        ctx.InputAssembler.SetIndexBuffer(mesh.IB, Format.R16_UInt, 0);
                        ctx.DrawIndexedInstanced(mesh.IndexCount, g.Count, 0, 0, 0);
                    }
                    BatchesDrawn++;
                    InstancesDrawn += g.Count;
                }
            }

            if (stateSet)
            {
                ctx.VertexShader.SetShaderResource(2, null);
                ctx.VertexShader.SetConstantBuffer(2, null);
                ctx.PixelShader.SetConstantBuffer(2, null);
            }
            Evict();
            LastRenderMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private void Evict()
        {
            if ((frame & 63) != 0) return;
            evict.Clear();
            foreach (var kv in gpu) if (frame - kv.Value.LastUsedFrame > KeepFrames) evict.Add(kv.Key);
            foreach (var k in evict) { gpu[k].Dispose(); gpu.Remove(k); }
        }

        public void Clear()
        {
            foreach (var g in gpu.Values) g.Dispose();
            gpu.Clear();
        }

        public void Dispose()
        {
            Clear();
            batchCB?.Dispose();
            shader?.Dispose();
        }
    }
}

