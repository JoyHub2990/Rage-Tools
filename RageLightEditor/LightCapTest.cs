using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public static class LightCapTest
    {
        public static int Run()
        {
            int failures = 0;
            Console.WriteLine("RAGE Tools GPU light-cap test");

            const int copies = 160;
            const int masters = 60;
            var scene = new Scene(null);
            var file = new LoadedFile { Path = "capped_prop.ydr", FromMlo = true, HasPlacement = true };
            for (int i = 1; i < copies; i++)
                file.ExtraPlacements.Add(Matrix.Translation(i * 4.0f, 0, 0));
            scene.Files.Add(file);

            for (int i = 0; i < masters; i++) scene.Lights.Add(MakeLight(i * 1.5f, 0));

            var justCreated = MakeLight(0, 5000.0f);
            scene.Lights.Add(justCreated);

            int wanted = masters + 1;
            Console.WriteLine($"  {wanted} editable lights x {copies} placements = " +
                              $"{wanted * copies} emitted, cap is {GpuLight.MaxLights}");
            if (wanted * copies <= GpuLight.MaxLights)
            {
                Console.WriteLine("  FAIL: test does not exceed the cap, so it proves nothing");
                failures++;
            }

            var outLights = new GpuLight[GpuLight.MaxLights];
            var sources = new List<LightAttributes>();
            int n = scene.BuildGpuLights(outLights, 12, false, 0f, null, Vector3.Zero,
                                         volumesOut: null, sourcesOut: sources);

            Console.WriteLine($"  emitted={n} dropped={scene.LastDroppedLights} " +
                              $"copies={scene.GhostGpuIndices.Count}");

            if (scene.LastDroppedLights <= 0)
            {
                Console.WriteLine("  FAIL: nothing was dropped, so the cap was never under pressure");
                failures++;
            }

            int mastersEmitted = n - scene.GhostGpuIndices.Count;
            if (mastersEmitted != wanted)
            {
                Console.WriteLine($"  FAIL: {mastersEmitted}/{wanted} editable lights reached the GPU");
                failures++;
            }
            else
            {
                Console.WriteLine($"  OK   all {wanted} editable lights reached the GPU");
            }

            if (!sources.Contains(justCreated))
            {
                Console.WriteLine("  FAIL: the light added last never reached the GPU " +
                                  "(this is the bug: a new light does not render)");
                failures++;
            }
            else
            {
                Console.WriteLine("  OK   the light added last reached the GPU");
            }

            Console.WriteLine($"LIGHTCAPTEST: failures={failures} result={(failures == 0 ? "OK" : "FAILED")}");
            return failures == 0 ? 0 : 1;
        }

        private static LightAttributes MakeLight(float x, float y)
        {
            return new LightAttributes
            {
                Position = new Vector3(x, y, 2.0f),
                ColorR = 255, ColorG = 200, ColorB = 150,
                Intensity = 5.0f,
                Falloff = 6.0f,
                FalloffExponent = 32.0f,
                Type = LightType.Point,
                Direction = new Vector3(0, 0, -1),
                Tangent = new Vector3(1, 0, 0),
                Extent = new Vector3(1, 1, 1),
                TimeFlags = 0xFFFFFF,
                VolumeSizeScale = 1,
            };
        }
    }
}

