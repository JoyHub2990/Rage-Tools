using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using CodeWalker;
using SharpDX.Direct3D11;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private int extShaftsDrawn, extMarkersDrawn;

        partial void OnAfterWorldDraw_H3(DeviceContext context)
        {
            if (photoMode) return;
            if (!panel.ShowLightShafts && !panel.ShowExtensions) return;
            if (panel.RenderMode == 8) return;
            extShaftsDrawn = 0; extMarkersDrawn = 0;
            var camPos = camera.Position;
            float sunUp = 0.5f;
            if (sceneRenderer.GlobalLight.HasValue)
            {
                var d = sceneRenderer.GlobalLight.Value.LightDir;
                sunUp = d.LengthSquared() > 1e-6f ? Math.Max(0.0f, Vector3.Normalize(d).Z) : 0.0f;
            }
            shaftSun = BuildShaftSun_J4();
            if (shaftRenderer == null) shaftRenderer = new LightShaftRenderer(deviceResources.Device);
            shaftRenderer.Clear();
            shaftRenderer.Intensity = 1.0f;
            float range2 = ExtensionHelpers.DefaultShaftRange * ExtensionHelpers.DefaultShaftRange;
            bool anyTri = false;
            try
            {
                if (panel.ExtensionMode)
                {
                    var t = panel.Ext.Current;
                    var arch = t?.Archetype;
                    if (arch?.Extensions != null && arch.Extensions.Length > 0)
                        foreach (var ext in arch.Extensions)
                            if (ext is MCExtensionDefLightShaft ls2 && panel.ShowLightShafts)
                            {
                                if (shaftRenderer != null && shaftRenderer.Ready && !ShaftsFlat_J4)
                                    ExtensionHelpers.AddLightShaftVolume(shaftRenderer, in ls2._Data, t.Placement,
                                        t.Orientation, camPos, in shaftSun, panel.PreviewHour, panel.LightShaftIntensity);
                                else
                                    anyTri |= true & DrawShaftFlat_V72(ls2, t, camPos, sunUp);
                                extShaftsDrawn++;
                            }
                }
                else if (panel.WorldMode)
                {
                    if (!worldBuilt) return;
                    var vis = World.Visible;
                    for (int i = 0; i < vis.Count; i++)
                    {
                        var e = vis[i];
                        if (e == null) continue;
                        if (Vector3.DistanceSquared(e.Position, camPos) > range2) continue;
                        var arch = e.Archetype;
                        if (arch?.Extensions != null && arch.Extensions.Length > 0)
                            anyTri |= DrawExtensions(arch.Extensions, e.Position, e.Orientation, camPos, sunUp);
                        if (e.Extensions != null && e.Extensions.Length > 0)
                            anyTri |= DrawExtensions(e.Extensions, e.Position, e.Orientation, camPos, sunUp);
                    }
                }
                else if (scene.MloInfo != null && scene.MloVisible && gameFiles?.Cache != null)
                {
                    foreach (var info in scene.MloInfo.Entities)
                    {
                        if (info == null) continue;
                        if (Vector3.DistanceSquared(info.Position, camPos) > range2) continue;
                        var arch = gameFiles.Cache.GetArchetype(info.ArchetypeHash);
                        if (arch?.Extensions == null || arch.Extensions.Length == 0) continue;
                        anyTri |= DrawExtensions(arch.Extensions, info.Position, info.Rotation, camPos, sunUp);
                    }
                }
            }
            catch (Exception ex)
            {
                if (extDrawError != ex.Message) { extDrawError = ex.Message; Console.WriteLine("EXTENSIONS draw: " + ex.Message); }
            }
            if (anyTri)
                triRenderer.Flush(context, camera.ViewProjMatrix, CommonStates.BlendAdditiveAlpha, CommonStates.DepthReadOnly);
            FlushShafts_J4(context);
            panel.WorldLightShaftsDrawn = extShaftsDrawn;
            panel.WorldExtensionsDrawn = extMarkersDrawn;
            if (extShaftsDrawn != extLastReported && Environment.GetEnvironmentVariable("RLE_FINDEXT") == "1")
            {
                extLastReported = extShaftsDrawn;
                Console.WriteLine($"LIGHTSHAFTS drawn {extShaftsDrawn} markers {extMarkersDrawn} sun travel {shaftSun.Travel} colour {shaftSun.Colour} bright {shaftSun.Brightness:0.00} elev {shaftSun.Elevation:0.00} hour {panel.PreviewHour:0.0}");
            }
            if (!extListDone && panel.WorldMode && worldBuilt && Environment.GetEnvironmentVariable("RLE_FINDEXT") == "1")
            {
                extListDone = true;
                int shown = 0, total = 0;
                foreach (var y in World.ResidentYmaps)
                {
                    foreach (var e in y?.AllEntities ?? Array.Empty<YmapEntityDef>())
                    {
                        int n = 0;
                        foreach (var x in e.Archetype?.Extensions ?? Array.Empty<MetaWrapper>()) if (x is MCExtensionDefLightShaft) n++;
                        foreach (var x in e.Extensions ?? Array.Empty<MetaWrapper>()) if (x is MCExtensionDefLightShaft) n++;
                        if (n == 0) continue;
                        total += n;
                        if (shown++ < 25) Console.WriteLine($"LIGHTSHAFT {e.Archetype?.Name} x{n} at {e.Position.X:0},{e.Position.Y:0},{e.Position.Z:0} ymap {y.Name} d {Vector3.Distance(e.Position, camPos):0}");
                    }
                }
                Console.WriteLine($"LIGHTSHAFTS total {total} in resident ymaps; drawn now {extShaftsDrawn}");
                int archs = 0, listed = 0;
                if (gameFiles?.Cache?.YtypDict != null)
                    foreach (var yt in gameFiles.Cache.YtypDict.Values)
                        foreach (var a in yt?.AllArchetypes ?? Array.Empty<Archetype>())
                        {
                            int n = 0;
                            foreach (var x in a?.Extensions ?? Array.Empty<MetaWrapper>()) if (x is MCExtensionDefLightShaft) n++;
                            if (n == 0) continue;
                            archs++;
                            if (listed++ < 30) Console.WriteLine($"LIGHTSHAFTARCH {a.Name} x{n} ytyp {yt.Name} mlo {(a is MloArchetype)}");
                        }
                Console.WriteLine($"LIGHTSHAFTARCHS {archs} archetypes carry light shafts");
            }
        }
        private bool DrawShaftFlat_V72(MCExtensionDefLightShaft ls, RageLightEditor.Editor.ExtensionTarget_V68 t,
                                       Vector3 camPos, float sunUp)
        {
            ExtensionHelpers.AddLightShaft(triRenderer, in ls._Data, t.Placement, t.Orientation, camPos, sunUp,
                                           panel.LightShaftIntensity);
            return true;
        }

        private string extDrawError;
        private bool extListDone;
        private int extLastReported = -1;

        private bool DrawExtensions(MetaWrapper[] exts, Vector3 pos, Quaternion ori, Vector3 camPos, float sunUp)
        {
            bool any = false;
            foreach (var ext in exts)
            {
                if (ext is MCExtensionDefLightShaft ls)
                {
                    if (!panel.ShowLightShafts) continue;
                    if (shaftRenderer != null && shaftRenderer.Ready && !ShaftsFlat_J4)
                        ExtensionHelpers.AddLightShaftVolume(shaftRenderer, in ls._Data, pos, ori, camPos, in shaftSun, panel.PreviewHour, panel.LightShaftIntensity);
                    else
                        ExtensionHelpers.AddLightShaft(triRenderer, in ls._Data, pos, ori, camPos, sunUp, panel.LightShaftIntensity);
                    if (extShaftsDrawn < 6 && extLastReported <= 0 && Environment.GetEnvironmentVariable("RLE_FINDEXT") == "1")
                    {
                        var d = ls._Data;
                        var c = ori.Multiply((d.cornerA + d.cornerB + d.cornerC + d.cornerD) * 0.25f) + pos;
                        Console.WriteLine($"  SHAFT at {c.X:0.0},{c.Y:0.0},{c.Z:0.0} dir {d.direction} amt {d.directionAmount} len {d.length} col {d.color:X8} inten {d.intensity} flags {d.flags} sun {d.scaleBySunIntensity} dens {d.densityType} vol {d.volumeType} soft {d.softness} A {d.cornerA} B {d.cornerB} fade {d.fadeDistanceStart}-{d.fadeDistanceEnd} time {d.fadeInTimeStart}-{d.fadeInTimeEnd}/{d.fadeOutTimeStart}-{d.fadeOutTimeEnd} entity {pos}");
                    }
                    extShaftsDrawn++; any = true;
                }
                else if (panel.ShowExtensions && ExtensionHelpers.TryGetOffset(ext, out var ofs))
                {
                    var wp = ori.Multiply(ofs) + pos;
                    ExtensionHelpers.AddMarkerBox(triRenderer, wp, 0.25f, ExtensionHelpers.MarkerColour(ext));
                    extMarkersDrawn++; any = true;
                }
            }
            return any;
        }
    }
}

