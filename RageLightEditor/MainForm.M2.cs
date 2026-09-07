using System;
using System.Collections.Generic;
using System.Globalization;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static readonly bool lightSetDump = Environment.GetEnvironmentVariable("RLE_LIGHTSETDUMP") == "1";
        private static readonly int multiShot = int.TryParse(Environment.GetEnvironmentVariable("RLE_MULTISHOT"), out int ms) ? ms : 0;
        private ulong m2LastHash; private int m2LastCount = -1;
        private int m2FlyAdds, m2FlyRems, m2FlyFrames, m2TailAdds, m2TailRems, m2TailFrames, m2GenuineFly, m2GenuineTail;
        private bool m2Reported;

        partial void OnWorldTick_M2()
        {
            UpdateEyeRoom_M2();
            if (Editor.ExtensionHelpers.ShaftDebug && worldWarmup % 60 == 30) Editor.ExtensionHelpers.ShaftDebugLeft = 12;
            if (!worldBuilt || !panel.WorldMode || screenshotPath == null) return;
            const int Settle = 120, Arrive = 300, Total = 460;
            if (worldWarmup == 200 && !m2FindDone) { m2FindDone = true; FindMlo_M2(); }
            if (worldWarmup == 300) Console.WriteLine($"MSAA world wanted {worldMsaa} granted {deviceResources.SampleCount} (RLE_MSAA={Environment.GetEnvironmentVariable("RLE_MSAA") ?? "unset"}) mode {panel.RenderMode}");
            if (worldWarmup == 440 && Environment.GetEnvironmentVariable("RLE_M2ROOMS") == "1") RoomReport_M2();
            if (multiShot > 0 && worldWarmup >= Arrive - multiShot && worldWarmup < Arrive && probeShotPending == null)
                probeShotPending = System.IO.Path.ChangeExtension(screenshotPath, null) + $".f{worldWarmup - (Arrive - multiShot)}.png";

            if (!lightSetDump) return;
            var wl = worldRender.Lights;
            bool changed = wl.LitSetHash != m2LastHash || wl.LightsEmitted != m2LastCount;
            if (worldWarmup > Arrive - 60 && worldWarmup <= Arrive)
            {
                m2FlyFrames++; m2FlyAdds += wl.LitAdded; m2FlyRems += wl.LitRemoved;
                if (wl.LitCandidates != m2LastCandidates) m2GenuineFly++;
            }
            else if (worldWarmup > Arrive && worldWarmup < Total)
            {
                m2TailFrames++; m2TailAdds += wl.LitAdded; m2TailRems += wl.LitRemoved;
                if (wl.LitCandidates != m2LastCandidates) m2GenuineTail++;
            }
            if (changed && worldWarmup > Settle)
                Console.WriteLine($"LIGHTSET frame {worldWarmup} emitted {wl.LightsEmitted} candidates {wl.LitCandidates} hash {wl.LitSetHash:X16} +{wl.LitAdded} -{wl.LitRemoved} displaced {wl.LitDisplaced} reserved {wl.LitReserved} inView {wl.LightsInView} hyst {(Editor.WorldLights.HysteresisDisabled ? "OFF" : "on")}");
            m2LastHash = wl.LitSetHash; m2LastCount = wl.LightsEmitted; m2LastCandidates = wl.LitCandidates;
            if (worldWarmup == Total - 1 && !m2Reported)
            {
                m2Reported = true;
                Console.WriteLine($"LIGHTSETSUMMARY fly(last 60 frames) +{m2FlyAdds} -{m2FlyRems} over {m2FlyFrames} frames (candidate pool changed in {m2GenuineFly}) | settled +{m2TailAdds} -{m2TailRems} over {m2TailFrames} frames (pool changed in {m2GenuineTail}) | emitted {wl.LightsEmitted} candidates {wl.LitCandidates} hyst {(Editor.WorldLights.HysteresisDisabled ? "OFF" : "on")}");
            }
        }
        private int m2LastCandidates = -1;
        private bool m2FindDone;

        private static readonly int worldMsaa = int.TryParse(Environment.GetEnvironmentVariable("RLE_MSAA"), out int m) && m >= 1 ? Math.Min(m, 8) : 4;
        private DepthCopyPass depthCopy_M2;

        private int WantedSampleCount_M2(int cineWant)
        {
            if (panel.RenderMode == 7) return cineWant;
            if (panel.WorldMode && panel.RenderMode == 0 && !renderingStill) return worldMsaa;
            return 1;
        }

        private void ResolveWorldFrame_M2(SharpDX.Direct3D11.DeviceContext context)
        {
            if (!deviceResources.Multisampled || panel.RenderMode == 7 || cinematic == null) return;
            deviceResources.ResolveFrame((ms, rtv) => cinematic.ResolveDepth(context, ms, rtv, deviceResources.Width, deviceResources.Height));
            depthCopy_M2 ??= new DepthCopyPass(deviceResources.Device);
            deviceResources.ResolveDepthIntoDsv((src, dsv, w, h) => depthCopy_M2.Copy(context, src, dsv, w, h));
        }

        private static readonly int gameExposureMode = int.TryParse(Environment.GetEnvironmentVariable("RLE_EXPGAME"), out int ge) ? ge : -1;
        private TimecycleData.PostFxState m2LastPostFx; private bool m2HaveCycle;

        private void ApplyGameExposure_M2(TimecycleData.PostFxState pf, bool haveCycle)
        {
            m2LastPostFx = pf; m2HaveCycle = haveCycle;
            bool on = gameExposureMode == 1 || (gameExposureMode < 0 && panel != null && panel.WorldMode);
            if (!on) { postFx.Vars.ExposureGame = Vector4.Zero; return; }
            if (!haveCycle) { postFx.Vars.ExposureGame = new Vector4(1.0f, 0.0f, -3.5f, 3.0f); return; }
            postFx.Vars.ExposureGame = new Vector4(1.0f, pf.ExposureTweakStops, pf.ExposureMinStops, pf.ExposureMaxStops);
        }

        public static float GameExposureStops(float avgLum, float tweak, float min, float max)
        {
            const double A = -106.68907449987120, B = 0.0041897015173052825, Off = 104.60364172443734, LumToGame = 0.0667;
            double e = A * Math.Pow(Math.Max(avgLum * LumToGame, 1e-6), B) + Off + tweak;
            return (float)Math.Clamp(e, Math.Min(min, max), max);
        }
        public static float EffectiveExposureStops(float avgLum, float tweak, float min, float max)
            => Math.Min(GameExposureStops(avgLum, tweak, min, max), (float)Math.Log(0.72 / Math.Clamp(avgLum, 0.2, 10.0), 2.0));

        private void LogExposure_M2()
        {
            var eg = postFx.Vars.ExposureGame;
            float lum = postFx.LastAvgLum;
            if (eg.X < 0.5f)
            {
                Console.WriteLine($"EXPOSURE rule=codewalker avgLum={lum:0.0000} scale={0.72f / (Math.Clamp(lum * panel.Exposure, 0.2f, 10.0f) + 0.001f):0.000} (RLE_EXPGAME={gameExposureMode})");
                return;
            }
            float stops = EffectiveExposureStops(Math.Max(lum, 0.0f), eg.Y, eg.Z, eg.W);
            float raw = GameExposureStops(Math.Max(lum, 0.0f), eg.Y, -99.0f, 99.0f);
            string mod = timecycle?.CurrentModifier != null && timecycle.ModifierStrength > 0.001f ? $"{timecycle.CurrentModifier.Name} @ {timecycle.ModifierStrength:0.00}" : "none";
            Console.WriteLine($"EXPOSURE rule=game avgLum={lum:0.0000} stops={stops:0.00} (game curve unclamped {raw:0.00}, codewalker {Math.Log(0.72 / Math.Clamp(lum, 0.2, 10.0), 2.0):0.00}) scale={Math.Pow(2.0, stops) * panel.Exposure:0.000} tweak={eg.Y:0.00} min={eg.Z:0.00} max={eg.W:0.00} modifier={mod} cycle={timecycle?.Name} hour={panel.PreviewHour:0.0}");
        }

        private static readonly bool eyeRoomOff = Environment.GetEnvironmentVariable("RLE_NOEYEROOM") == "1";
        public string EyeRoomStatus_M2 { get; private set; } = "";

        private void UpdateEyeRoom_M2()
        {
            if (sceneRenderer == null) return;
            var r = l2Resolver;
            CodeWalker.GameFiles.MloArchetype arch = null; int room = -1; float w = 0.0f;
            if (panel != null && panel.WorldMode) { arch = interiorTcInst?.MloArch; room = interiorTcRoom; w = interiorTcStrength; }
            else { arch = k2TcInterior?.Arch; room = k2TcRoom; w = k2TcStrength; }
            InteriorAmbientResolver.EntityAmbient a = default;
            bool inside = !eyeRoomOff && r != null && r.Ready && arch != null && room > 0 && w > 0.001f;
            if (inside) { a = r.Resolve(arch, room); inside = a.InInterior > 0.5f; }
            if (!inside)
            {
                sceneRenderer.EyeRoomParams = Vector4.Zero; sceneRenderer.EyeRoomScales = Vector4.Zero;
                sceneRenderer.EyeRoomAmbUp = Vector4.Zero; sceneRenderer.EyeRoomAmbDown = Vector4.Zero;
                EyeRoomStatus_M2 = "";
                return;
            }
            sceneRenderer.EyeRoomParams = new Vector4(w, a.ReflectIntAmb, 0, 0);
            sceneRenderer.EyeRoomScales = new Vector4(a.NaturalScale, a.ArtificialScale, 0, 0);
            sceneRenderer.EyeRoomAmbUp = a.ArtIntAmbUp;
            sceneRenderer.EyeRoomAmbDown = a.ArtIntAmbDown;
            EyeRoomStatus_M2 = $"{arch.Name}/{room} '{a.Modifier}' w {w:0.00}";
        }

        private void RoomReport_M2()
        {
            var r = worldRender?.InteriorAmbient_L2;
            var cam = camera.Position;
            Console.WriteLine($"M2ROOMS cam {cam.X:0.0},{cam.Y:0.0},{cam.Z:0.0} resolver {(r != null ? (r.Ready ? "ready" : "not ready") : "none")}");
            var rows = new List<(float d, string s)>();
            foreach (var e in World.Visible)
            {
                if (e?.Archetype == null) continue;
                float d = (e.Position - cam).Length();
                if (d > 20) continue;
                var parent = e.MloParent;
                var arch = parent?.MloInstance?.MloArch ?? e.MloInstance?.MloArch;
                if (arch == null) continue;
                string att = "-";
                if (parent != null)
                {
                    var ents = parent.MloInstance.Entities;
                    int idx = ents != null ? Array.IndexOf(ents, e) : -1;
                    att = $"idx {idx}";
                    if (idx >= 0 && arch.rooms != null)
                        for (int ri = 0; ri < arch.rooms.Length; ri++)
                            if (arch.rooms[ri]?.AttachedObjects != null && Array.IndexOf(arch.rooms[ri].AttachedObjects, (uint)idx) >= 0) att += $" room {ri} '{arch.rooms[ri].RoomName}' tc {arch.rooms[ri]._Data.timecycleName}";
                    if (idx >= 0 && arch.portals != null)
                        for (int pi = 0; pi < arch.portals.Length; pi++)
                            if (arch.portals[pi]?.AttachedObjects != null && Array.IndexOf(arch.portals[pi].AttachedObjects, (uint)idx) >= 0) att += $" portal {pi} {arch.portals[pi]._Data.roomFrom}->{arch.portals[pi]._Data.roomTo}";
                    if (idx < 0)
                    {
                        var sets = parent.MloInstance.EntitySets;
                        if (sets != null) foreach (var s in sets) if (s?.Entities != null && s.Entities.Contains(e)) att += $" set '{s.EntitySet?.Name}'";
                    }
                }
                else att = "SHELL (the MLO's own drawable)";
                var amb = r != null ? r.Resolve(e) : default;
                rows.Add((d, $"  {d,5:0.0} m {e.Archetype.Name} [{arch.Name}] {att} -> nat {amb.NaturalScale:0.00} art {amb.ArtificialScale:0.00} int {amb.InInterior:0} mod '{amb.Modifier}'"));
            }
            rows.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var row in rows) Console.WriteLine(row.s);
        }

        private void FindMlo_M2()
        {
            var want = Environment.GetEnvironmentVariable("RLE_FINDMLO");
            if (string.IsNullOrEmpty(want)) return;
            var subs = want.ToLowerInvariant().Split(';', StringSplitOptions.RemoveEmptyEntries);
            var c = gameFiles?.Cache;
            if (c?.YtypDict == null || c.AllYmapsDict == null) { Console.WriteLine("FINDMLO: no cache"); return; }
            var hashes = new Dictionary<uint, string>();
            foreach (var ytyp in c.YtypDict.Values)
            {
                if (ytyp?.AllArchetypes == null) continue;
                foreach (var a in ytyp.AllArchetypes)
                {
                    if (!(a is CodeWalker.GameFiles.MloArchetype m)) continue;
                    var n = (a.Name ?? "").ToLowerInvariant();
                    foreach (var s in subs) if (n.Contains(s)) { hashes[a.Hash] = a.Name; Console.WriteLine($"FINDMLO archetype {a.Name} ytyp {ytyp.Name} rooms {m.rooms?.Length ?? 0} portals {m.portals?.Length ?? 0} entities {m.entities?.Length ?? 0} sets {m.entitySets?.Length ?? 0}"); break; }
                }
            }
            if (hashes.Count == 0)
            {
                int others = 0;
                foreach (var ytyp in c.YtypDict.Values)
                {
                    if (ytyp?.AllArchetypes == null) continue;
                    foreach (var a in ytyp.AllArchetypes)
                    {
                        var n = (a?.Name ?? "").ToLowerInvariant();
                        foreach (var sub in subs) if (n.Contains(sub)) { Console.WriteLine($"FINDMLO other archetype {a.Name} ytyp {ytyp.Name} type {a.GetType().Name} bs {a.BSRadius:0.0}"); others++; break; }
                        if (others >= 40) break;
                    }
                    if (others >= 40) break;
                }
                int yn = 0;
                foreach (var kv in c.AllYmapsDict)
                {
                    var n = (kv.Value?.Name ?? "").ToLowerInvariant();
                    foreach (var sub in subs) if (n.Contains(sub))
                    {
                        CodeWalker.GameFiles.YmapFile ym = null;
                        try { ym = c.RpfMan.GetFile<CodeWalker.GameFiles.YmapFile>(kv.Value); } catch { }
                        var ents = ym?.AllEntities;
                        string first = ents != null && ents.Length > 0 && ents[0] != null ? $"{ents[0].Position.X:0.0},{ents[0].Position.Y:0.0},{ents[0].Position.Z:0.0} ({ents[0]._CEntityDef.archetypeName})" : "-";
                        Console.WriteLine($"FINDMLO ymap {kv.Value.Name} node {(World.NodeOf(kv.Value.ShortNameHash) != null)} entities {ents?.Length ?? -1} mlos {ym?.CMloInstanceDefs?.Length ?? 0} first {first} parent {ym?._CMapData.parent ?? 0} flags {ym?._CMapData.flags ?? 0} {kv.Value.Path}");
                        yn++; break;
                    }
                    if (yn >= 40) break;
                }
                Console.WriteLine($"FINDMLO no MLO archetype matched; {others} other archetypes, {yn} ymaps by name");
            }
            int shown = 0, scanned = 0;
            foreach (var kv in c.AllYmapsDict)
            {
                var fe = kv.Value;
                if (fe == null) continue;
                CodeWalker.GameFiles.YmapFile ym = null;
                try { ym = c.RpfMan.GetFile<CodeWalker.GameFiles.YmapFile>(fe); } catch { }
                scanned++;
                if (ym?.CMloInstanceDefs == null) continue;
                foreach (var md in ym.CMloInstanceDefs)
                {
                    var an = md.CEntityDef.archetypeName;
                    if (!hashes.TryGetValue(an.Hash, out var name)) continue;
                    bool active = c.YmapDict.TryGetValue(fe.ShortNameHash, out var ae) && ReferenceEquals(ae, fe);
                    Console.WriteLine($"FINDMLO placement {name} ymap {fe.Name} active {active}{(active ? "" : " (active entry: " + (ae?.Path ?? "NONE") + ")")} pos {md.CEntityDef.position.X:0.0},{md.CEntityDef.position.Y:0.0},{md.CEntityDef.position.Z:0.0} lodDist {md.CEntityDef.lodDist:0} flags {md.CEntityDef.flags} node {(World.NodeOf(fe.ShortNameHash) != null)} path {fe.Path}");
                    if (++shown >= 60) { Console.WriteLine("FINDMLO ... (60 shown)"); return; }
                }
            }
            Console.WriteLine($"FINDMLO {hashes.Count} archetypes, {shown} placements, {scanned} ymaps scanned");
        }
    }
}

