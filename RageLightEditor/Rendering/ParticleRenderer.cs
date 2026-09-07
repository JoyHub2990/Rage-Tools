using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using RageLightEditor.Editor;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using GameTexture = CodeWalker.GameFiles.Texture;

namespace RageLightEditor.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleVertex
    {
        public Vector3 Centre;
        public Vector2 Corner;
        public Vector4 Colour;
        public Vector2 Size;
        public float Rotation;

        public SharpDX.Vector4 UvRect;
        public SharpDX.Vector4 UvRect2;
        public Vector2 Anim;
        public const int Stride = 88;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleVars
    {
        public Matrix ViewProj;
        public Vector4 CameraPos;
        public Vector4 Flags;
    }

    public class ParticleRenderer : IDisposable
    {
        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<ParticleVars> cbuffer;
        private readonly TextureLoader textures;
        private Buffer vbuffer;
        private int capacity;
        private ShaderResourceView whiteSrv;

        private class Batch
        {
            public ShaderResourceView Srv;
            public bool Additive;
            public readonly List<ParticleVertex> Verts = new List<ParticleVertex>(512);
            public float SortKey;
        }
        private readonly Dictionary<(ShaderResourceView, bool), Batch> batches
            = new Dictionary<(ShaderResourceView, bool), Batch>();

        private static readonly Vector2[] QuadCorners =
        {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1),
            new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1),
        };

        public ParticleRenderer(Device device, TextureLoader textureLoader)
        {
            this.device = device;
            textures = textureLoader;
            shader = new ShaderSet(device, "particle.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 20, 0),
                new InputElement("TEXCOORD", 1, Format.R32G32_Float, 36, 0),
                new InputElement("TEXCOORD", 2, Format.R32_Float, 44, 0),
                new InputElement("TEXCOORD", 3, Format.R32G32B32A32_Float, 48, 0),
                new InputElement("TEXCOORD", 4, Format.R32G32B32A32_Float, 64, 0),
                new InputElement("TEXCOORD", 5, Format.R32G32_Float, 80, 0),
            });
            cbuffer = new ConstantBuffer<ParticleVars>(device);
            CreateWhiteFallback();
        }

        private void CreateWhiteFallback()
        {
            const int n = 32;
            var pixels = new uint[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n * 2f - 1f;
                    var dy = (y + 0.5f) / n * 2f - 1f;
                    var r = Math.Min(1f, (float)Math.Sqrt(dx * dx + dy * dy));
                    var a = 1f - r * r;
                    a *= a;
                    pixels[y * n + x] = 0x00FFFFFFu | ((uint)(Math.Clamp(a, 0f, 1f) * 255f + 0.5f) << 24);
                }
            var desc = new Texture2DDescription
            {
                Width = n, Height = n, MipLevels = 1, ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
            };
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var tex = new Texture2D(device, desc,
                    new[] { new DataRectangle(handle.AddrOfPinnedObject(), n * 4) });
                whiteSrv = new ShaderResourceView(device, tex);
            }
            finally { handle.Free(); }
        }

        public static bool IsAdditiveBlendSet(int blendSet) =>
            blendSet >= (PtfxSimulator.LegacyLook ? 2 : 1);

        public void Add(List<PtfxSimulator.Sprite> sprites, Func<int, GameTexture> texturePick,
                        Vector3 cameraPos, int blendOverride = -1)
        {
            sprites.Sort((a, b) => (b.Pos - cameraPos).LengthSquared().CompareTo(
                                   (a.Pos - cameraPos).LengthSquared()));
            foreach (var s in sprites)
            {
                var tex = texturePick?.Invoke(s.EmitterIndex);
                var srv = (tex != null ? textures.GetSRV(tex) : null) ?? whiteSrv;
                var additive = blendOverride >= 0 ? blendOverride == 1 : IsAdditiveBlendSet(s.BlendSet);

                if (!batches.TryGetValue((srv, additive), out var batch))
                {
                    batch = new Batch { Srv = srv, Additive = additive, SortKey = float.MaxValue };
                    batches[(srv, additive)] = batch;
                }
                var dist = (s.Pos - cameraPos).LengthSquared();
                if (dist < batch.SortKey) batch.SortKey = dist;

                var uv1 = Inset(s.UvRect);
                var uv2 = Inset(s.UvRect2);
                foreach (var c in QuadCorners)
                {
                    batch.Verts.Add(new ParticleVertex
                    {
                        Centre = s.Pos,
                        Corner = c,
                        Colour = s.Colour,
                        Size = s.Size,
                        Rotation = s.Rotation,
                        UvRect = uv1,
                        UvRect2 = uv2,
                        Anim = new Vector2(s.FrameBlend, s.Emissive),
                    });
                }
            }
        }

        private static Vector4 Inset(Vector4 r)
        {
            if (r.Z >= 0.999f && r.W >= 0.999f) return r;
            var ix = r.Z * 0.002f;
            var iy = r.W * 0.002f;
            return new Vector4(r.X + ix, r.Y + iy, r.Z - 2f * ix, r.W - 2f * iy);
        }

        public void Flush(DeviceContext context, Matrix viewProj, Vector3 cameraPos)
        {
            if (batches.Count == 0) return;

            var vars = new ParticleVars
            {
                ViewProj = Matrix.Transpose(viewProj),
                CameraPos = new Vector4(cameraPos, 0),
                Flags = new Vector4(PtfxSimulator.LegacyLook ? 1f : 0f, 0, 0, 0),
            };
            cbuffer.Update(context, ref vars);

            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, cbuffer.Buffer);
            context.PixelShader.SetSampler(0, CommonStates.LinearClamp);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthReadOnly);
            context.Rasterizer.State = CommonStates.RasterSolid;

            var ordered = new List<Batch>(batches.Values);
            ordered.Sort((a, b) =>
            {
                if (a.Additive != b.Additive) return a.Additive ? 1 : -1;
                return b.SortKey.CompareTo(a.SortKey);
            });

            foreach (var batch in ordered)
            {
                if (batch.Verts.Count == 0) continue;
                UploadVerts(context, batch.Verts);
                context.PixelShader.SetShaderResource(0, batch.Srv);
                context.OutputMerger.SetBlendState(batch.Additive
                    ? CommonStates.BlendAdditiveAlpha : CommonStates.BlendAlpha);
                context.InputAssembler.SetVertexBuffers(0,
                    new VertexBufferBinding(vbuffer, ParticleVertex.Stride, 0));
                context.Draw(batch.Verts.Count, 0);
            }

            context.PixelShader.SetShaderResource(0, null);
            batches.Clear();
        }

        private void UploadVerts(DeviceContext context, List<ParticleVertex> verts)
        {
            if (vbuffer == null || capacity < verts.Count)
            {
                vbuffer?.Dispose();
                capacity = Math.Max(verts.Count + 1024, 4096);
                vbuffer = new Buffer(device, capacity * ParticleVertex.Stride, ResourceUsage.Dynamic,
                    BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
            }
            var box = context.MapSubresource(vbuffer, 0, MapMode.WriteDiscard, MapFlags.None);
            unsafe
            {
                var dst = (ParticleVertex*)box.DataPointer;
                for (int i = 0; i < verts.Count; i++) dst[i] = verts[i];
            }
            context.UnmapSubresource(vbuffer, 0);
        }

        public void Dispose()
        {
            vbuffer?.Dispose();
            cbuffer?.Dispose();
            shader?.Dispose();
            whiteSrv?.Dispose();
        }
    }
}

