using System;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor
{
    public static class SelfTest
    {
        public static int Run(string file)
        {
            try
            {
                Console.WriteLine("RAGE Tools self-test");
                YdrFile ydr;

                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    Console.WriteLine($"Loading {file}...");
                    ydr = new YdrFile();
                    ydr.Load(File.ReadAllBytes(file));
                }
                else
                {
                    Console.WriteLine("Building synthetic drawable...");
                    ydr = BuildSyntheticYdr();
                }

                if (ydr.Drawable == null) throw new Exception("no drawable");
                var lights0 = ydr.Drawable.LightAttributes?.data_items ?? Array.Empty<LightAttributes>();
                Console.WriteLine($"Drawable '{ydr.Drawable.Name}': {lights0.Length} lights, " +
                    $"{ydr.Drawable.AllModels?.Length ?? 0} models");

                var newLight = new LightAttributes
                {
                    Position = new Vector3(1.5f, -2.0f, 3.0f),
                    ColorR = 255, ColorG = 180, ColorB = 90,
                    Intensity = 7.5f,
                    Falloff = 12.0f,
                    FalloffExponent = 32.0f,
                    Type = LightType.Spot,
                    Direction = new Vector3(0, 0, -1),
                    Tangent = new Vector3(-1, 0, 0),
                    ConeInnerAngle = 15.0f,
                    ConeOuterAngle = 40.0f,
                    Extent = new Vector3(1, 1, 1),
                    CoronaSize = 2.0f,
                    CoronaIntensity = 1.5f,
                    CoronaZBias = 0.1f,
                    ShadowNearClip = 0.05f,
                    TimeFlags = 0xF0007F,
                    Flags = 0x580,
                    Flashiness = 0,
                    VolumeIntensity = 1, VolumeSizeScale = 1,
                    VolumeOuterColorR = 255, VolumeOuterColorG = 255, VolumeOuterColorB = 255,
                    VolumeOuterIntensity = 1, VolumeOuterExponent = 1,
                    LightHash = 7,
                };
                var lights1 = lights0.Concat(new[] { newLight }).ToArray();
                if (ydr.Drawable.LightAttributes == null)
                    ydr.Drawable.LightAttributes = new ResourceSimpleList64<LightAttributes>();
                ydr.Drawable.LightAttributes.data_items = lights1;

                Console.WriteLine("Saving...");
                var saved = ydr.Save();
                Console.WriteLine($"Saved {saved.Length} bytes");

                Console.WriteLine("Reloading...");
                var ydr2 = new YdrFile();
                ydr2.Load(saved);
                var lights2 = ydr2.Drawable?.LightAttributes?.data_items;
                if (lights2 == null) throw new Exception("reloaded file has no lights!");
                if (lights2.Length != lights1.Length)
                    throw new Exception($"light count mismatch: wrote {lights1.Length}, read {lights2.Length}");

                for (int i = 0; i < lights1.Length; i++)
                {
                    var a = LightBytes(lights1[i]);
                    var b = LightBytes(lights2[i]);
                    if (!a.SequenceEqual(b))
                        throw new Exception($"light {i} byte mismatch after round-trip");
                }
                Console.WriteLine($"OK: {lights2.Length} lights round-tripped byte-identical.");

                var saved2 = ydr2.Save();
                var ydr3 = new YdrFile();
                ydr3.Load(saved2);
                if (ydr3.Drawable?.LightAttributes?.data_items?.Length != lights1.Length)
                    throw new Exception("second round-trip failed");
                Console.WriteLine("OK: second round-trip stable.");

                PlacementTest();
                CameraTest();
                GpuLayoutTest();
                if (Editor.AreaToolState.SelfTest() != 0) throw new Exception("area tool self-test failed");
                if (Editor.NavMeshEditor.SelfTest() != 0) throw new Exception("nav mesh editor self-test failed");
                if (Editor.UvAnimation.SelfTest() != 0) throw new Exception("UV animation self-test failed");
                if (Editor.AudioEngine_U1.SelfTest() != 0) throw new Exception("audio engine self-test failed");
                if (Editor.SpaceNames.SelfTest() != 0) throw new Exception("workspace name table self-test failed");

                Console.WriteLine("SELF-TEST PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELF-TEST FAILED: " + ex);
                return 1;
            }
        }

        private static void GpuLayoutTest()
        {
            void Same(string what, int declared, int actual, bool row16)
            {
                if (declared != actual)
                    throw new Exception($"{what}: declared {declared} bytes, the struct is {actual}");
                if (row16 && (actual % 16) != 0)
                    throw new Exception($"{what}: {actual} bytes is not a whole number of float4 rows");
            }
            Same("GpuLight.SizeInBytes", Rendering.GpuLight.SizeInBytes, SharpDX.Utilities.SizeOf<Rendering.GpuLight>(), true);
            Same("MeshVertex.Stride", Rendering.MeshVertex.Stride, SharpDX.Utilities.SizeOf<Rendering.MeshVertex>(), false);
            Same("LineVertex.Stride", Rendering.LineVertex.Stride, SharpDX.Utilities.SizeOf<Rendering.LineVertex>(), false);
            Same("CoronaVertex.Stride", Rendering.CoronaVertex.Stride, SharpDX.Utilities.SizeOf<Rendering.CoronaVertex>(), false);
            Same("SceneVars", System.Runtime.InteropServices.Marshal.SizeOf<Rendering.SceneVars>(),
                 System.Runtime.InteropServices.Marshal.SizeOf<Rendering.SceneVars>(), true);
            Same("ObjectVars", System.Runtime.InteropServices.Marshal.SizeOf<Rendering.ObjectVars>(),
                 System.Runtime.InteropServices.Marshal.SizeOf<Rendering.ObjectVars>(), true);
            Console.WriteLine($"OK: GPU layouts match ({SharpDX.Utilities.SizeOf<Rendering.GpuLight>()} B per light, " +
                              $"{SharpDX.Utilities.SizeOf<Rendering.MeshVertex>()} B per vertex).");
        }

        private static void PlacementTest()
        {
            var scene = new Editor.Scene(null, null);
            var light = new LightAttributes
            {
                Position = new Vector3(1, 0, 0),
                Direction = new Vector3(0, 0, -1),
                Tangent = new Vector3(-1, 0, 0),
                ColorR = 255, ColorG = 255, ColorB = 255,
                Intensity = 5.0f,
                Falloff = 10.0f,
                FalloffExponent = 32.0f,
                Type = LightType.Point,
                Extent = new Vector3(1, 1, 1),
                TimeFlags = 0,
            };

            scene.AddImportedProp("placementtest", null, null, null, null, new[] { light },
                Matrix.Translation(10, 0, 0), 3, true, "placementtest",
                new[] { Matrix.Translation(20, 0, 0), Matrix.Translation(30, 0, 0) });

            if (scene.Lights.Count != 1)
                throw new Exception($"expected 1 editable light, got {scene.Lights.Count}");

            var gpu = new Rendering.GpuLight[Rendering.GpuLight.MaxLights];
            int n = scene.BuildGpuLights(gpu, 12, false, 0f, null, Vector3.Zero);
            if (n != 3)
                throw new Exception($"expected 3 lit copies (1 editable + 2 ghosts), got {n}");
            if (scene.GhostGpuIndices.Count != 2)
                throw new Exception($"expected 2 copies flagged as render-only, got {scene.GhostGpuIndices.Count}");

            var xs = Enumerable.Range(0, n).Select(i => gpu[i].Position.X).OrderBy(x => x).ToArray();
            var want = new[] { 11f, 21f, 31f };
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(xs[i] - want[i]) > 0.001f)
                    throw new Exception($"copy {i} at x={xs[i]:0.###}, expected {want[i]}");
            }
            Console.WriteLine($"OK: prop placed 3x lights at x={string.Join(", ", xs.Select(x => x.ToString("0.#")))} " +
                              $"({scene.GhostGpuIndices.Count} render-only copies, 1 editable).");
        }

        private static void CameraTest()
        {
            var cam = new Rendering.Camera { Target = Vector3.Zero, Yaw = 0.4f, Pitch = 0.3f };
            cam.SetAspect(1.6f);
            cam.TargetDistance = cam.Distance = 25.0f;
            cam.SnapSmoothing();
            cam.Update(1.0f / 60.0f);

            var eye0 = cam.Position;
            var look0 = cam.GetForward();

            cam.Orbit(220.0f, -90.0f);
            for (int i = 0; i < 200; i++) cam.Update(1.0f / 60.0f);

            float moved = Vector3.Distance(eye0, cam.Position);
            if (moved > 0.01f)
                throw new Exception($"rotating moved the camera {moved:0.###} m - it must turn in place");

            var look1 = cam.GetForward();
            if (Vector3.Dot(look0, look1) > 0.99f)
                throw new Exception("rotating didn't change where the camera looks");

            var eyeBeforeZoom = cam.Position;
            cam.Zoom(-120.0f);
            for (int i = 0; i < 200; i++) cam.Update(1.0f / 60.0f);
            if (Vector3.Distance(eyeBeforeZoom, cam.Position) < 0.01f)
                throw new Exception("the wheel should dolly the camera, and didn't");

            var eyeBeforeFly = cam.Position;
            cam.Orbit(40.0f, 10.0f);
            for (int i = 0; i < 30; i++)
            {
                cam.Translate(new Vector3(0, 0, 0.1f));
                cam.Update(1.0f / 60.0f);
            }
            float climbed = cam.Position.Z - eyeBeforeFly.Z;
            if (Math.Abs(climbed - 3.0f) > 0.05f)
                throw new Exception($"flying while the view turns moved {climbed:0.###} m of the 3 m asked for");

            Console.WriteLine("OK: mouse look turns the camera in place, the wheel dollies it, " +
                              "and flying still works while the view is turning.");
        }

        private static byte[] LightBytes(LightAttributes l)
        {
            using var sys = new MemoryStream();
            using var gfx = new MemoryStream();
            var writer = new ResourceDataWriter(sys, gfx);
            writer.Position = 0x50000000;
            l.Write(writer);
            return sys.ToArray();
        }

        private static YdrFile BuildSyntheticYdr()
        {
            var ydr = new YdrFile();
            ydr.Drawable = new Drawable
            {
                Name = "selftest_drawable",
                ShaderGroup = new ShaderGroup(),
                LightAttributes = new ResourceSimpleList64<LightAttributes>(),
                BoundingCenter = Vector3.Zero,
                BoundingSphereRadius = 5.0f,
                BoundingBoxMin = new Vector3(-5),
                BoundingBoxMax = new Vector3(5),
            };
            return ydr;
        }
    }
}

