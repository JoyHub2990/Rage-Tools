using System;
using System.IO;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;

namespace RageLightEditor
{
    public static class MeshDump
    {
        public static int Run(string file)
        {
            try
            {
                var data = File.ReadAllBytes(file);
                DrawableBase drawable;
                if (file.EndsWith(".yft", StringComparison.OrdinalIgnoreCase))
                {
                    var yft = new YftFile();
                    yft.Load(data);
                    drawable = yft.Fragment?.Drawable;
                }
                else
                {
                    var ydr = new YdrFile();
                    ydr.Load(data);
                    drawable = ydr.Drawable;
                }
                if (drawable == null) { Console.WriteLine("no drawable"); return 1; }

                var models = RageLightEditor.Rendering.ModelRenderer.HighestLod(drawable);
                Console.WriteLine($"models: {models?.Length}");
                int mi = 0;
                foreach (var model in models)
                {
                    Console.WriteLine($"model {mi++}: geoms={model?.Geometries?.Length}, boneIndex={model.BoneIndex}, hasSkin={model.HasSkin}");
                    if (model?.Geometries == null) continue;
                    foreach (var geom in model.Geometries)
                    {
                        var vd = geom.VertexData;
                        var inds = geom.IndexBuffer?.Indices;
                        Console.WriteLine($"  geom: shaderID={geom.ShaderID}, sps={geom.Shader?.FileName.Hash}, bucket={geom.Shader?.RenderBucket}, " +
                            $"vformat={vd?.VertexType}, stride={vd?.VertexStride}, verts={vd?.VertexCount}, inds={inds?.Length}, " +
                            $"declFlags={vd?.Info?.Flags}, declTypes={(ulong?)vd?.Info?.Types:X}, declStride={vd?.Info?.Stride}");

                        var verts = VertexDecoder.Decode(vd, inds);
                        if (verts == null) { Console.WriteLine("  DECODE FAILED"); continue; }
                        int n = Math.Min(4, verts.Length);
                        for (int i = 0; i < n; i++)
                        {
                            var v = verts[i];
                            Console.WriteLine($"    v{i}: pos=({v.Position.X:0.##},{v.Position.Y:0.##},{v.Position.Z:0.##}) " +
                                $"nrm=({v.Normal.X:0.##},{v.Normal.Y:0.##},{v.Normal.Z:0.##}) col=({v.Colour0.X:0.##},{v.Colour0.Y:0.##},{v.Colour0.Z:0.##},{v.Colour0.W:0.##}) " +
                                $"uv=({v.UV0.X:0.##},{v.UV0.Y:0.##})");
                        }
                        var min = new SharpDX.Vector3(float.MaxValue);
                        var max = new SharpDX.Vector3(float.MinValue);
                        foreach (var v in verts)
                        {
                            min = SharpDX.Vector3.Min(min, v.Position);
                            max = SharpDX.Vector3.Max(max, v.Position);
                        }
                        Console.WriteLine($"    decoded bounds: ({min.X:0.##},{min.Y:0.##},{min.Z:0.##}) .. ({max.X:0.##},{max.Y:0.##},{max.Z:0.##})");
                        if (inds != null && inds.Length >= 6)
                        {
                            Console.Write("    first tris:");
                            for (int i = 0; i < Math.Min(12, inds.Length); i++) Console.Write($" {inds[i]}");
                            Console.WriteLine();
                        }
                    }
                }

                var texdict = drawable.ShaderGroup?.TextureDictionary;
                if (texdict?.Textures?.data_items != null)
                {
                    foreach (var tex in texdict.Textures.data_items)
                    {
                        Console.WriteLine($"texture: {tex.Name}, {tex.Width}x{tex.Height}, levels={tex.Levels}, fmt={tex.Format}, " +
                            $"stride={tex.Stride}, dataLen={tex.Data?.FullData?.Length}");
                        var d = tex.Data?.FullData;
                        if (d != null && d.Length >= 8)
                        {
                            Console.WriteLine($"  first bytes: {d[0]},{d[1]},{d[2]},{d[3]} {d[4]},{d[5]},{d[6]},{d[7]}");
                        }
                    }
                }
                ProjectionCheck();
                BufferUploadCheck();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DUMP FAILED: " + ex);
                return 1;
            }
        }

        private static void ProjectionCheck()
        {
            var cam = new Camera { Target = SharpDX.Vector3.Zero, Distance = 30, Yaw = 0.8f, Pitch = 0.45f };
            cam.SetAspect(1.5f);
            cam.Update();
            var vp = cam.ViewProjMatrix;

            void Probe(string name, SharpDX.Vector3 world)
            {
                var v4 = SharpDX.Vector3.Transform(world, vp);
                Console.WriteLine($"  {name}: world=({world.X},{world.Y},{world.Z}) clip z/w = {v4.Z / v4.W:0.########} (w={v4.W:0.###})");
            }
            Console.WriteLine("Projection check (camera at distance 30):");
            Probe("near point (5m in front)", cam.Position + SharpDX.Vector3.Normalize(cam.Target - cam.Position) * 5);
            Probe("target (30m)", cam.Target);
            Probe("far point (100m)", cam.Position + SharpDX.Vector3.Normalize(cam.Target - cam.Position) * 100);
        }

        private static unsafe void BufferUploadCheck()
        {
            using var device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware);
            var verts = new MeshVertex[100];
            var rnd = new Random(1234);
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = new MeshVertex
                {
                    Position = new SharpDX.Vector3(i, i * 2, i * 3),
                    Normal = new SharpDX.Vector3(1, 0, 0),
                    Tangent = new SharpDX.Vector4(0, 1, 0, 1),
                    Colour0 = new SharpDX.Vector4(1, 1, 1, 1),
                    UV0 = new SharpDX.Vector2(i * 0.1f, i * 0.2f),
                    UV1 = new SharpDX.Vector2(0, 0),
                };
            }
            Console.WriteLine($"MeshVertex marshal size: {System.Runtime.InteropServices.Marshal.SizeOf<MeshVertex>()}, " +
                $"SharpDX Utilities.SizeOf: {SharpDX.Utilities.SizeOf<MeshVertex>()}");

            using var vb = SharpDX.Direct3D11.Buffer.Create(device, SharpDX.Direct3D11.BindFlags.VertexBuffer, verts);
            Console.WriteLine($"created buffer size: {vb.Description.SizeInBytes} (expected {verts.Length * MeshVertex.Stride})");

            var desc = new SharpDX.Direct3D11.BufferDescription
            {
                SizeInBytes = vb.Description.SizeInBytes,
                Usage = SharpDX.Direct3D11.ResourceUsage.Staging,
                CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read,
            };
            using var staging = new SharpDX.Direct3D11.Buffer(device, desc);
            device.ImmediateContext.CopyResource(vb, staging);
            var box = device.ImmediateContext.MapSubresource(staging, 0, SharpDX.Direct3D11.MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            int mismatches = 0;
            var src = (byte*)box.DataPointer;
            fixed (MeshVertex* pv = verts)
            {
                var expect = (byte*)pv;
                for (int i = 0; i < verts.Length * MeshVertex.Stride; i++)
                {
                    if (src[i] != expect[i])
                    {
                        if (mismatches < 8) Console.WriteLine($"  byte {i} (vert {i / MeshVertex.Stride} off {i % MeshVertex.Stride}): gpu={src[i]} cpu={expect[i]}");
                        mismatches++;
                    }
                }
            }
            device.ImmediateContext.UnmapSubresource(staging, 0);
            Console.WriteLine(mismatches == 0 ? "GPU upload check: OK, bytes identical" : $"GPU upload check: {mismatches} byte mismatches!");
        }
    }
}

