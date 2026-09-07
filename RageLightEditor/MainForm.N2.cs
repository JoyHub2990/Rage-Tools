using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {

        partial void BeforeWorldLights_N2()
        {
            var wl = worldRender?.Lights;
            if (wl == null) return;
            WireSunResolver_N2();
            var cull = interiorCull;
            if (cull != null && cull.Inside != null)
            {
                wl.CameraInterior = cull.Inside;
                wl.CameraRoom = cull.Room;
                wl.RoomOfEntity = n2RoomOf ??= (e => interiorCull != null ? interiorCull.RoomOf(e) : -1);
            }
            else
            {
                wl.CameraInterior = null;
                wl.CameraRoom = -1;
                wl.RoomOfEntity = null;
            }
        }
        private Func<YmapEntityDef, int> n2RoomOf;

        private void WireSunResolver_N2()
        {
            var r = worldRender?.InteriorSun_N2;
            if (r == null) return;
            if (r.RoomNaturalAmbient == null && l2Resolver != null)
                r.RoomNaturalAmbient = n2Natural ??= ((arch, room) =>
                {
                    if (l2Resolver == null || !l2Resolver.Ready) return -1.0f;
                    var a = l2Resolver.Resolve(arch, room);
                    return string.IsNullOrEmpty(a.Modifier) || a.Modifier == "limbo" ? -1.0f : a.NaturalScale;
                });
            if (mloImporter != null && mloImporter.InteriorSun_N2.RoomNaturalAmbient == null && n2Natural != null)
                mloImporter.InteriorSun_N2.RoomNaturalAmbient = n2Natural;
            int mods = timecycle?.Modifiers.Count ?? 0;
            if (mods > 0 && mods != n2ModifiersSeen)
            {
                n2ModifiersSeen = mods;
                worldRender.RestampSun_N2();
            }
        }
        private Func<CodeWalker.GameFiles.MloArchetype, int, float> n2Natural;
        private int n2ModifiersSeen = -1;

        partial void OnWorldTick_N2()
        {
            if (!panel.WorldMode || worldRender == null) return;
            var wl = worldRender.Lights;
            string key = wl.InteriorLightStatus_N2;
            if (key != n2LastLine && (WorldLights.InteriorDump || WorldLights.CompareArch.Length > 0 || InteriorSunResolver.Dump))
            {
                n2LastLine = key;
                Console.WriteLine($"WORLDINTLIGHT {key} | sun-suppressed instances {worldRender.SunSuppressed_N2} (mode {InteriorSunResolver.Mode})");
            }
        }
        private string n2LastLine = "";

        partial void AfterLightsBuilt_N2(int lightCount)
        {
            WireSunResolver_N2();
            if (WorldLights.CompareArch.Length == 0 || panel.WorldMode || n2CompareDone) return;
            if (scene == null || scene.Lights.Count == 0) return;
            if (n2CompareFrames++ < 3) return;
            n2CompareDone = true;
            int printed = 0;
            for (int i = 0; i < scene.Lights.Count; i++)
            {
                var la = scene.Lights[i];
                if (la == null) continue;
                var owner = scene.OwnerFile(la);
                string name = owner?.Name ?? owner?.Path ?? "";
                if (WorldLights.CompareArch != "*" &&
                    name.IndexOf(WorldLights.CompareArch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var inst = scene.GetInstance(la);
                if (printed == 0) Console.WriteLine($"LIGHTCOMPARE light-workspace '{WorldLights.CompareArch}' - {scene.Lights.Count} lights in the scene, {lightCount} on the GPU");
                Console.WriteLine("  " + WorldLights.DescribeLight(i, la, la.Position, inst.WorldPosition, (int)panel.PreviewHour, Vector3.Distance(inst.WorldPosition, camera.Position)));
                for (int g = 0; g < lightCount && g < gpuLightSources.Count; g++)
                    if (ReferenceEquals(gpuLightSources[g], la)) { Console.WriteLine(WorldLights.DescribeGpuLight(g, in gpuLights[g])); break; }
                printed++;
            }
            if (printed == 0) Console.WriteLine($"LIGHTCOMPARE light-workspace: nothing matched '{WorldLights.CompareArch}' among {scene.Lights.Count} lights");
        }
        private int n2CompareFrames;
        private bool n2CompareDone;

        partial void SeqTest_N2(Action<string, bool, string> check)
        {
            WorldLights.InteriorPriorityTest_N2(check);
            InteriorSunResolver.Test_N2(check);

            var m = new RenderMesh();
            check("n2 sun: an unstamped mesh takes the full sun", m.SunScale == 1.0f, m.SunScale.ToString());
            var clone = m.CreateInstance(Matrix.Identity);
            check("n2 sun: an instance inherits the room's sun scale", clone.SunScale == 1.0f, clone.SunScale.ToString());
            m.SunScale = 0.0f;
            var dark = m.CreateInstance(Matrix.Identity);
            check("n2 sun: ...and a darkened room's too", dark.SunScale == 0.0f, dark.SunScale.ToString());
        }

        partial void RunWorldTestExtras_N2(Action<string, bool, string> check, Action<Vector3> settle)
        {
            void Frame(Vector3 at, float yaw, float pitch)
            {
                settle(at);
                CameraSequence.ApplyToCamera(camera, at, yaw, pitch, settings.FovDeg);
                camera.Update();
                UpdateInteriorCull_J4();
                worldRender.Update(World.Visible, gameFiles, modelRenderer, new BoundingFrustum(camera.ViewProjMatrix), true, World.Fade);
                BeforeWorldLights_N2();
                worldRender.Lights.Build(World.Visible, gpuLights, gpuLightSources, camera.Position,
                    12, false, 0.0f, null, World.ResidentYmaps, World.ResidentVersion, new BoundingFrustum(camera.ViewProjMatrix));
            }

            if (WorldBlock("n2bank"))
            {
                Frame(new Vector3(253, 222, 101), 4.71f, 0.0f);
                var wl = worldRender.Lights;
                bool inside = interiorCull?.Inside != null;
                Console.WriteLine($"  N2BANK {wl.InteriorLightStatus_N2} | sun-suppressed instances {worldRender.SunSuppressed_N2}");
                check("n2 world: the eye is inside the bank", inside, interiorCull?.ToString() ?? "outside");
                if (inside)
                {
                    check("n2 world: the interior's own lights are lit", wl.InteriorEmitted > 0,
                          $"room {wl.RoomEmitted}/{wl.RoomCandidates} interior {wl.InteriorEmitted}/{wl.InteriorCandidates}");
                    check("n2 world: every light of the camera's own room made the cap",
                          wl.RoomEmitted >= wl.RoomCandidates, $"{wl.RoomEmitted} of {wl.RoomCandidates}");
                    if (InteriorSunResolver.Mode > 0)
                        check("n2 world: the bank has rooms the sun may not reach", worldRender.SunSuppressed_N2 > 0,
                              $"{worldRender.SunSuppressed_N2} instances");
                }
            }

            if (WorldBlock("n2mrpd") && InteriorSunResolver.Mode > 0)
            {
                Frame(new Vector3(464, -984, 30.7f), 3.14f, 0.05f);
                int dark = 0, own = 0;
                foreach (var mesh in worldRender.Model.Meshes)
                {
                    var e = worldRender.OwnerOf(mesh);
                    if (e?.MloParent == null) continue;
                    own++;
                    if (mesh.SunScale < 0.5f) dark++;
                }
                Console.WriteLine($"  N2MRPD interior meshes drawn {own}, of which out of the sun {dark} ({interiorCull})");
                check("n2 world: MRPD's inner rooms take no direct sun", own == 0 || dark > 0, $"{dark} of {own} interior meshes darkened");
            }

            if (WorldBlock("n2street"))
            {
                Frame(new Vector3(-200, -900, 40), 4.71f, 0.05f);
                int lit = 0, exterior = 0;
                foreach (var mesh in worldRender.Model.Meshes)
                {
                    var e = worldRender.OwnerOf(mesh);
                    if (e != null && (e.MloParent != null || e.MloInstance != null)) continue;
                    exterior++;
                    if (mesh.SunScale > 0.5f) lit++;
                }
                check("n2 world: downtown keeps every exterior mesh in the sun", exterior == 0 || lit == exterior,
                      $"{lit} of {exterior} exterior meshes at full sun");
            }
        }
    }
}

