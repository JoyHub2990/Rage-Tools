using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace RageLightEditor.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ShadowPassVars
    {
        public Matrix LightViewProj;
        public Vector4 LightPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ShadowObjVars
    {
        public Matrix World;
    }

    public partial class ShadowRenderer : IDisposable
    {
        public const int SpotSize = 1024;
        public const int CubeSize = 512;

        private readonly Device device;
        private readonly ShaderSet shader;
        private readonly ConstantBuffer<ShadowPassVars> passCB;
        private readonly ConstantBuffer<ShadowObjVars> objCB;

        private Texture2D spotArray, spotDepth;
        public ShaderResourceView SpotArraySRV { get; private set; }
        private readonly RenderTargetView[] spotRTVs = new RenderTargetView[GpuLight.MaxShadowSpots];
        private DepthStencilView spotDSV;

        private Texture2D cubeArray, cubeDepth;
        public ShaderResourceView CubeArraySRV { get; private set; }
        private readonly RenderTargetView[] cubeFaceRTVs = new RenderTargetView[GpuLight.MaxShadowCubes * 6];
        private DepthStencilView cubeDSV;

        public readonly Matrix[] SpotMatrices = new Matrix[GpuLight.MaxShadowSpots];

        public ShadowRenderer(Device device)
        {
            this.device = device;
            shader = new ShaderSet(device, "shadowdist.hlsl", new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElement("TANGENT", 0, Format.R32G32B32A32_Float, 24, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 40, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 56, 0),
                new InputElement("TEXCOORD", 1, Format.R32G32_Float, 64, 0),
            });
            passCB = new ConstantBuffer<ShadowPassVars>(device);
            objCB = new ConstantBuffer<ShadowObjVars>(device);

            CreateTargets();
        }

        private void CreateTargets()
        {
            spotArray = new Texture2D(device, new Texture2DDescription
            {
                Width = SpotSize, Height = SpotSize, MipLevels = 1, ArraySize = GpuLight.MaxShadowSpots,
                Format = Format.R32_Float, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            for (int i = 0; i < GpuLight.MaxShadowSpots; i++)
            {
                spotRTVs[i] = new RenderTargetView(device, spotArray, new RenderTargetViewDescription
                {
                    Format = Format.R32_Float,
                    Dimension = RenderTargetViewDimension.Texture2DArray,
                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                    {
                        FirstArraySlice = i, ArraySize = 1, MipSlice = 0,
                    },
                });
            }
            SpotArraySRV = new ShaderResourceView(device, spotArray, new ShaderResourceViewDescription
            {
                Format = Format.R32_Float,
                Dimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                {
                    ArraySize = GpuLight.MaxShadowSpots, FirstArraySlice = 0, MipLevels = 1, MostDetailedMip = 0,
                },
            });
            spotDepth = new Texture2D(device, new Texture2DDescription
            {
                Width = SpotSize, Height = SpotSize, MipLevels = 1, ArraySize = 1,
                Format = Format.D32_Float, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
            });
            spotDSV = new DepthStencilView(device, spotDepth);

            cubeArray = new Texture2D(device, new Texture2DDescription
            {
                Width = CubeSize, Height = CubeSize, MipLevels = 1, ArraySize = GpuLight.MaxShadowCubes * 6,
                Format = Format.R32_Float, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                OptionFlags = ResourceOptionFlags.TextureCube,
            });
            for (int i = 0; i < GpuLight.MaxShadowCubes * 6; i++)
            {
                cubeFaceRTVs[i] = new RenderTargetView(device, cubeArray, new RenderTargetViewDescription
                {
                    Format = Format.R32_Float,
                    Dimension = RenderTargetViewDimension.Texture2DArray,
                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                    {
                        FirstArraySlice = i, ArraySize = 1, MipSlice = 0,
                    },
                });
            }
            CubeArraySRV = new ShaderResourceView(device, cubeArray, new ShaderResourceViewDescription
            {
                Format = Format.R32_Float,
                Dimension = ShaderResourceViewDimension.TextureCubeArray,
                TextureCubeArray = new ShaderResourceViewDescription.TextureCubeArrayResource
                {
                    CubeCount = GpuLight.MaxShadowCubes, First2DArrayFace = 0, MipLevels = 1, MostDetailedMip = 0,
                },
            });
            cubeDepth = new Texture2D(device, new Texture2DDescription
            {
                Width = CubeSize, Height = CubeSize, MipLevels = 1, ArraySize = 1,
                Format = Format.D32_Float, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
            });
            cubeDSV = new DepthStencilView(device, cubeDepth);

            sunMap = new Texture2D(device, new Texture2DDescription
            {
                Width = SunSize, Height = SunSize, MipLevels = 1, ArraySize = SunCascades.MaxCascades,
                Format = Format.R32_Float, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            for (int i = 0; i < SunCascades.MaxCascades; i++)
            {
                sunRTVs[i] = new RenderTargetView(device, sunMap, new RenderTargetViewDescription
                {
                    Format = Format.R32_Float,
                    Dimension = RenderTargetViewDimension.Texture2DArray,
                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                    {
                        FirstArraySlice = i, ArraySize = 1, MipSlice = 0,
                    },
                });
            }
            SunMapSRV = new ShaderResourceView(device, sunMap, new ShaderResourceViewDescription
            {
                Format = Format.R32_Float,
                Dimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                {
                    ArraySize = SunCascades.MaxCascades, FirstArraySlice = 0, MipLevels = 1, MostDetailedMip = 0,
                },
            });
            sunDepth = new Texture2D(device, new Texture2DDescription
            {
                Width = SunSize, Height = SunSize, MipLevels = 1, ArraySize = 1,
                Format = Format.D32_Float, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
            });
            sunDSV = new DepthStencilView(device, sunDepth);
        }

        public const int SunSize = 2048;
        private Texture2D sunMap, sunDepth;
        private readonly RenderTargetView[] sunRTVs = new RenderTargetView[SunCascades.MaxCascades];
        private DepthStencilView sunDSV;
        public ShaderResourceView SunMapSRV { get; private set; }

        public Matrix SunMatrix = Matrix.Identity;
        public Vector3 SunPos;
        public float SunTexelWorld = 0.05f;
        public readonly SunCascades Cascades = new SunCascades();
        public int LastCascadeDraws;

        public void RenderSunCascades(DeviceContext context, IEnumerable<RenderMesh> meshes, Camera camera,
            Vector3 sunDir, float[] intervals, int count, Vector3 sceneMin, Vector3 sceneMax)
        {
            sunPass_R5 = true;
            Cascades.Fit(camera, sunDir, intervals, count, SunSize, sceneMin, sceneMax);
            SunPos = Cascades.SunPos;
            SunMatrix = Cascades.ViewProj[0];
            LastCascadeDraws = 0;
            CascadeMeshesSkipped = 0;
            var list = meshes as IList<RenderMesh> ?? new List<RenderMesh>(meshes);
            for (int i = 0; i < Cascades.Count; i++)
            {
                float minRadius = Cascades.TexelWorld[i] * CascadeMinRadiusTexels;
                LastCascadeDraws += RenderFace(context, list, sunRTVs[i], sunDSV, SunSize, Cascades.ViewProj[i], SunPos, Cascades.Cull[i], minRadius);
            }
            sunPass_R5 = false;
        }

        public void RenderSun(DeviceContext context, IEnumerable<RenderMesh> meshes,
            Vector3 sunDir, BoundingBox sceneBounds)
        {
            var centre = (sceneBounds.Minimum + sceneBounds.Maximum) * 0.5f;
            float radius = Math.Max((sceneBounds.Maximum - sceneBounds.Minimum).Length() * 0.5f, 1.0f);

            var dir = sunDir.LengthSquared() > 1e-6f ? Vector3.Normalize(sunDir) : Vector3.UnitZ;
            SunPos = centre + dir * (radius * 2.0f);
            var up = Math.Abs(dir.Z) > 0.95f ? Vector3.UnitX : Vector3.UnitZ;

            var view = Matrix.LookAtLH(SunPos, centre, up);
            var proj = Matrix.OrthoLH(radius * 2.2f, radius * 2.2f, 0.05f, radius * 4.5f);
            SunMatrix = view * proj;
            SunTexelWorld = radius * 2.2f / SunSize;

            var cull = new BoundingSphere(centre, radius * 1.5f);
            sunPass_R5 = true;
            RenderFace(context, meshes, sunRTVs[0], sunDSV, SunSize, SunMatrix, SunPos, cull);
            sunPass_R5 = false;
        }

        public void RenderSpotSlot(DeviceContext context, IEnumerable<RenderMesh> meshes, int slot,
            Vector3 pos, Vector3 dir, Vector3 tangent, float outerAngleRad, float falloff, float nearClip)
        {
            var up = tangent.LengthSquared() > 1e-6f ? tangent :
                (Math.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
            var view = Matrix.LookAtLH(pos, pos + dir, up);
            float fov = MathUtil.Clamp(outerAngleRad * 2.0f, 0.05f, 3.05f);
            var proj = Matrix.PerspectiveFovLH(fov, 1.0f, Math.Max(nearClip, 0.02f), Math.Max(falloff, 0.1f));
            SpotMatrices[slot] = view * proj;

            var cull = new BoundingSphere(pos, Math.Max(falloff, 0.1f));
            RenderFace(context, meshes, spotRTVs[slot], spotDSV, SpotSize, SpotMatrices[slot], pos, cull);
        }

        public void RenderCubeSlot(DeviceContext context, IEnumerable<RenderMesh> meshes, int cubeSlot,
            Vector3 pos, float falloff)
        {
            var dirs = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
            var ups = new[] { Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY };
            var proj = Matrix.PerspectiveFovLH(MathUtil.PiOverTwo, 1.0f, 0.03f, Math.Max(falloff, 0.1f));
            var cull = new BoundingSphere(pos, Math.Max(falloff, 0.1f));

            for (int f = 0; f < 6; f++)
            {
                var view = Matrix.LookAtLH(pos, pos + dirs[f], ups[f]);
                RenderFace(context, meshes, cubeFaceRTVs[cubeSlot * 6 + f], cubeDSV, CubeSize, view * proj, pos, cull);
            }
        }

        public float CascadeMinRadiusTexels = 1.5f;
        public int CascadeMeshesSkipped;

        private int RenderFace(DeviceContext context, IEnumerable<RenderMesh> meshes,
            RenderTargetView rtv, DepthStencilView dsv, int size, Matrix viewProj, Vector3 lightPos, BoundingSphere cull, float minRadius = 0.0f)
        {
            int drawn = 0;
            context.OutputMerger.SetTargets(dsv, rtv);
            context.Rasterizer.SetViewport(0, 0, size, size);
            context.ClearRenderTargetView(rtv, new Color4(1e9f, 0, 0, 0));
            context.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1.0f, 0);

            var pv = new ShadowPassVars
            {
                LightViewProj = Matrix.Transpose(viewProj),
                LightPos = new Vector4(lightPos, 1),
            };
            passCB.Update(context, ref pv);

            shader.Apply(context);
            context.VertexShader.SetConstantBuffer(0, passCB.Buffer);
            context.VertexShader.SetConstantBuffer(1, objCB.Buffer);
            context.PixelShader.SetConstantBuffer(0, passCB.Buffer);
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            context.OutputMerger.SetBlendState(CommonStates.BlendOpaque);
            context.OutputMerger.SetDepthStencilState(CommonStates.DepthLessDefault);

            context.Rasterizer.State = CasterRaster_R5();

            foreach (var mesh in meshes)
            {
                if (!mesh.Visible) continue;
                if (mesh.AlphaMode != GeomAlphaMode.Opaque && mesh.AlphaMode != GeomAlphaMode.Cutout) continue;
                if (CommonStates.CullingEnabled && !mesh.WorldSphere.Intersects(ref cull)) continue;
                if (mesh.WorldSphere.Radius < minRadius) { CascadeMeshesSkipped++; continue; }
                var ov = new ShadowObjVars { World = Matrix.Transpose(mesh.Transform) };
                objCB.Update(context, ref ov);
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(mesh.VB, MeshVertex.Stride, 0));
                context.InputAssembler.SetIndexBuffer(mesh.IB, Format.R16_UInt, 0);
                context.DrawIndexed(mesh.IndexCount, 0, 0);
                drawn++;
            }
            return drawn;
        }

        public void Dispose()
        {
            DisposeProbe_R5();
            sunDSV?.Dispose();
            sunDepth?.Dispose();
            SunMapSRV?.Dispose();
            foreach (var r in sunRTVs) r?.Dispose();
            sunMap?.Dispose();
            cubeDSV?.Dispose();
            cubeDepth?.Dispose();
            foreach (var r in cubeFaceRTVs) r?.Dispose();
            CubeArraySRV?.Dispose();
            cubeArray?.Dispose();
            spotDSV?.Dispose();
            spotDepth?.Dispose();
            SpotArraySRV?.Dispose();
            foreach (var r in spotRTVs) r?.Dispose();
            spotArray?.Dispose();
            objCB?.Dispose();
            passCB?.Dispose();
            shader?.Dispose();
        }
    }
}

