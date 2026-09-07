using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using Vector3 = SharpDX.Vector3;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private GrassRenderer grassRenderer;
        private bool matDiagPrinted;

        private RenderModel GrassModelFor(Archetype a)
        {
            if (a == null) return null;
            if (!worldRender.IsBuilt(a)) return null;
            return worldRender.PeekModel(a.Hash);
        }

        private bool matEnvRead;
        private double matHoldUntil;

        partial void OnWorldTick_Materials()
        {
            if (!matEnvRead)
            {
                matEnvRead = true;
                if (Environment.GetEnvironmentVariable("RLE_NOHD") == "1") panel.WorldHdTextures = false;
                if (Environment.GetEnvironmentVariable("RLE_NOGRASS") == "1") panel.WorldGrass = false;
                if (float.TryParse(Environment.GetEnvironmentVariable("RLE_HOLDSECS"), out float holdSecs) && holdSecs > 0)
                    matHoldUntil = clock.Elapsed.TotalSeconds + holdSecs;
            }
            if (matHoldUntil > 0 && screenshotPath != null && worldBuilt)
            {
                if (clock.Elapsed.TotalSeconds < matHoldUntil) { if (worldWarmup > 100) worldWarmup = 100; }
                else matHoldUntil = 0;
            }
            if (modelRenderer != null && modelRenderer.HdTextures != panel.WorldHdTextures)
            {
                modelRenderer.HdTextures = panel.WorldHdTextures;
                gameFiles.PrewarmHd = panel.WorldHdTextures;
            }
            if (grassRenderer != null)
            {
                grassRenderer.Enabled = panel.WorldGrass;
                grassRenderer.DistanceScale = panel.WorldGrassDistance;
                panel.WorldGrassBatches = grassRenderer.BatchesDrawn;
                panel.WorldGrassInstances = grassRenderer.InstancesDrawn;
                panel.WorldGrassMs = (float)grassRenderer.LastRenderMs;
            }
            panel.WorldHdTextureHits = modelRenderer?.HdTextureHits ?? 0;
            panel.WorldLightTests = sceneRenderer?.LightTestsThisFrame ?? 0;

            if (panel.WorldMode && screenshotPath != null && worldBuilt && worldWarmup >= 459 && !matDiagPrinted)
            {
                matDiagPrinted = true;
                PrintMaterialDiagnostics();
                GrassDebugDump_Q5();
                GrassLodDump_U5();
            }
        }

        private void PrintMaterialDiagnostics()
        {
            int tinted = 0, tintedTree = 0, mirrors = 0, glass = 0, terrain = 0, terrainUv1 = 0, srgbViews = 0, texd = 0;
            foreach (var m in worldRender.Model.Meshes)
            {
                if (m.TintPaletteSRV != null) { tinted++; if ((m.TintMode & 3) == 2) tintedTree++; }
                if (m.IsMirror) mirrors++;
                if (m.AlphaMode == GeomAlphaMode.Glass) glass++;
                if (m.IsTerrain) { terrain++; if (m.TerrainLayersUseUv1) terrainUv1++; }
                if (m.DiffuseSRV != null) { texd++; if (m.DiffuseSrgbView) srgbViews++; }
            }
            Console.WriteLine($"WORLDMATERIALS meshes {worldRender.Model.Meshes.Count} tinted {tinted} (trees {tintedTree}) mirrors {mirrors} glass {glass} " +
                              $"terrain {terrain} (uv1 {terrainUv1}) srgbViews {srgbViews}/{texd} hdTex {modelRenderer.HdTextureHits}/{modelRenderer.HdTextureAsks} " +
                              $"lightTests {sceneRenderer.LightTestsThisFrame} maxPerMesh {ObjectVars.MaxPerMeshLights} " +
                              $"prewarm [{gameFiles.PrewarmPhase}] {gameFiles.PrewarmCount} drawables {gameFiles.PrewarmTextureAsks} asks {gameFiles.PrewarmMs:0} ms hdYtds {gameFiles.HdYtdsResident} ({gameFiles.HdYtdBytesResident / (1024 * 1024)} MB, loaded {gameFiles.HdYtdsLoaded}, released {gameFiles.HdYtdsReleased}) cache {gameFiles.CacheStatus}");
            if (Environment.GetEnvironmentVariable("RLE_MISSTEX") == "1")
            {
                var miss = modelRenderer.MissingTexturesSnapshot_V21();
                Console.WriteLine($"MISSTEX {miss.Length} unresolved: {string.Join(", ", miss)}");
            }
            {
                int gy = 0, gb = 0; float nearest = float.MaxValue; string nearName = "-"; int resident = 0;
                foreach (var y in World.ResidentYmaps)
                {
                    resident++;
                    var b = y.GrassInstanceBatches;
                    if (b == null || b.Length == 0) continue;
                    gy++; gb += b.Length;
                    foreach (var batch in b)
                    {
                        float d = (batch.Position - camera.Position).Length();
                        if (d < nearest) { nearest = d; nearName = $"{y.Name} at {batch.Position.X:0},{batch.Position.Y:0},{batch.Position.Z:0} {batch.Archetype?.Name ?? batch.Batch.archetypeName.ToString()} n {batch.Instances?.Length} lodDist {batch.Batch.lodDist}"; }
                    }
                }
                Console.WriteLine($"WORLDGRASSDATA residentYmaps {resident} withGrass {gy} batches {gb} nearest {(nearest < float.MaxValue ? nearest.ToString("0") : "-")} m [{nearName}]");
            }
            PrintProjectContentDiagnostics_P3();
            if (grassRenderer != null)
                Console.WriteLine($"WORLDGRASS batches {grassRenderer.BatchesDrawn}/{grassRenderer.BatchesInRange} instances {grassRenderer.InstancesDrawn} " +
                                  $"awaiting {grassRenderer.BatchesAwaitingModel} resident {grassRenderer.BatchesResident} {grassRenderer.LastRenderMs:0.00} ms enabled {grassRenderer.Enabled} err {grassRenderer.LastError ?? "-"}");

            if (Environment.GetEnvironmentVariable("RLE_TINTLIST") == "1")
            {
                var seenArch = new HashSet<uint>();
                foreach (var m in worldRender.Model.Meshes.OrderBy(x => (x.WorldSphere.Center - camera.Position).Length()))
                {
                    bool tintB = m.TintPaletteSRV != null && (m.TintMode & 3) == 1;
                    if (!tintB && !m.IsMirror && m.AlphaMode != GeomAlphaMode.Glass) continue;
                    var e = worldRender.OwnerOf(m);
                    uint ah = e?.Archetype?.Hash ?? 0;
                    if (!seenArch.Add(ah ^ (tintB ? 1u : 0u) ^ (m.IsMirror ? 2u : 0u))) continue;
                    if (seenArch.Count > 24) break;
                    Console.WriteLine($"MATNEAR {(tintB ? "TINT" : m.IsMirror ? "MIRROR" : "GLASS")} {e?.Archetype?.Name ?? "?"} at {e?.Position.X:0},{e?.Position.Y:0},{e?.Position.Z:0} d {(m.WorldSphere.Center - camera.Position).Length():0} " +
                                      $"shader {m.ShaderName} tintRow {m.TintPaletteIndex}/{m.TintPaletteHeight} tintValue {e?._CEntityDef.tintValue} diffuse {DescribeSrv(m.DiffuseSRV)} bump {DescribeSrv(m.BumpSRV)} spec {DescribeSrv(m.SpecSRV)} decalKind {m.DecalKind} alphaMode {m.AlphaMode} srgb {m.DiffuseSrgbView}");
                }
            }

            var matArch = Environment.GetEnvironmentVariable("RLE_MATARCH");
            if (!string.IsNullOrEmpty(matArch))
            {
                var mdl = worldRender.PeekModel(JenkHash.GenHash(matArch.ToLowerInvariant()));
                if (mdl == null) Console.WriteLine($"MATARCH {matArch}: no model built");
                else foreach (var m in mdl.Meshes)
                    Console.WriteLine($"MATARCH {matArch} shader {m.ShaderName} kind {m.DecalKind} mode {m.AlphaMode} mirror {m.IsMirror} diffuse '{m.DiffuseName}' {DescribeSrv(m.DiffuseSRV)} bump {DescribeSrv(m.BumpSRV)} spec {DescribeSrv(m.SpecSRV)} srgb {m.DiffuseSrgbView} emissive {m.EmissiveMult} tint '{m.TintPaletteName}' {DescribeSrv(m.TintPaletteSRV)} mode {m.TintMode} row {m.TintPaletteIndex}/{m.TintPaletteHeight} {m.TintPaletteRows}");
            }
            if (Environment.GetEnvironmentVariable("RLE_TINTLIST") == "1")
            {
                var tintEnts = new List<YmapEntityDef>();
                foreach (var y in World.ResidentYmaps)
                    foreach (var e in y.AllEntities ?? Array.Empty<YmapEntityDef>())
                        if (e._CEntityDef.tintValue != 0 && e._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_HD) tintEnts.Add(e);
                foreach (var e in tintEnts.OrderBy(e => (e.Position - camera.Position).Length()).Take(12))
                    Console.WriteLine($"TINTENT {e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString()} at {e.Position.X:0},{e.Position.Y:0},{e.Position.Z:0} d {(e.Position - camera.Position).Length():0} tintValue {e._CEntityDef.tintValue} ymap {e.Ymap?.Name}");
                Console.WriteLine($"TINTENT total {tintEnts.Count} HD entities with tintValue != 0 in the resident ymaps");
            }

            var find = Environment.GetEnvironmentVariable("RLE_FINDARCH");
            if (!string.IsNullOrEmpty(find))
            {
                int shown = 0;
                foreach (var y in World.ResidentYmaps)
                {
                    foreach (var e in y.AllEntities ?? Array.Empty<YmapEntityDef>())
                    {
                        var an = e.Archetype?.Name ?? e._CEntityDef.archetypeName.ToString();
                        if (!string.Equals(an, find, StringComparison.OrdinalIgnoreCase)) continue;
                        var mdl = worldRender.PeekModel(e._CEntityDef.archetypeName.Hash);
                        string mats = mdl == null ? "no model" : string.Join(" ", mdl.Meshes.Take(40).Select(x =>
                            $"{x.ShaderName}{(x.TintPaletteSRV != null ? $":tnt{x.TintMode & 3}h{x.TintPaletteHeight}" : "")}{(x.IsMirror ? ":mirror" : "")}"));
                        Console.WriteLine($"FINDARCH {an} at {e.Position.X:0.0},{e.Position.Y:0.0},{e.Position.Z:0.0} ymap {y.Name} tint {e._CEntityDef.tintValue} lod {e._CEntityDef.lodLevel} dist {(e.Position - camera.Position).Length():0} [{mats}]");
                        if (++shown >= 12) return;
                    }
                }
                if (shown == 0) Console.WriteLine($"FINDARCH {find}: not in any resident ymap");
            }
        }

        private static string DescribeSrv(SharpDX.Direct3D11.ShaderResourceView srv)
        {
            if (srv == null) return "-";
            try
            {
                using var res = srv.Resource;
                using var tex = res.QueryInterface<SharpDX.Direct3D11.Texture2D>();
                var d = tex.Description;
                return $"{d.Format}:{d.Width}x{d.Height}x{d.MipLevels}";
            }
            catch { return "?"; }
        }

        partial void OnAfterWorldDraw_Materials(SharpDX.Direct3D11.DeviceContext context)
        {
            if (!panel.WorldMode || !worldBuilt || !panel.WorldGrass) return;
            if (worldRender.MeshesDrawn <= 0) return;
            grassRenderer ??= new GrassRenderer(deviceResources.Device);
            try
            {
                BeforeGrassDraw_P3();
                grassRenderer.Render(context, camera, World.ContentYmaps, GrassModelFor,
                    sceneRenderer.SceneCBBuffer, panel.WorldLodScale);
            }
            catch (Exception ex)
            {
                if (grassRenderer.LastError == null) Console.WriteLine("GRASS draw failed: " + ex.Message);
                grassRenderer.LastError = ex.Message;
            }
        }

        partial void RunWorldTestExtras_Materials(Action<string, bool, string> check, Action<Vector3> settle)
        {
            {
                RenderMesh Classify(uint sps)
                {
                    var m = new RenderMesh();
                    ModelRenderer.ClassifyDrawPublic(m, new ShaderFX { FileName = new MetaHash(sps), Name = new MetaHash(sps) });
                    return m;
                }
                var md = Classify(1658580369u);
                check("mirror_default classifies opaque + IsMirror", md.AlphaMode == GeomAlphaMode.Opaque && md.IsMirror, $"{md.AlphaMode} mirror {md.IsMirror}");
                var mc = Classify(129155404u);
                check("mirror_crack classifies opaque + IsMirror", mc.AlphaMode == GeomAlphaMode.Opaque && mc.IsMirror, $"{mc.AlphaMode} mirror {mc.IsMirror}");
                var mdc = Classify(2706821972u);
                check("mirror_decal is decal kind 8 (drawn), not kind 3 (never drawn)", mdc.AlphaMode == GeomAlphaMode.Decal && mdc.DecalKind == 8 && mdc.IsMirror, $"{mdc.AlphaMode} kind {mdc.DecalKind}");
                var gb = Classify(916743331u);
                check("grass_batch classifies cutout, double sided", gb.AlphaMode == GeomAlphaMode.Cutout && gb.DoubleSided, $"{gb.AlphaMode}");
                var ge = Classify(1263059426u);
                check("glass_env stays Glass", ge.AlphaMode == GeomAlphaMode.Glass, $"{ge.AlphaMode}");
            }
            {
                var at = new Vector3(-270, -960, 60);
                CameraSequence.ApplyToCamera(camera, at, 2.4f, -0.3f, settings.FovDeg);
                settle(at);
                int tinted = 0, rows = 0, mirrors = 0, glass = 0, srgb = 0, texd = 0;
                var rowsSeen = new HashSet<uint>();
                foreach (var m in worldRender.Model.Meshes)
                {
                    if (m.TintPaletteSRV != null)
                    {
                        tinted++;
                        if (m.TintPaletteHeight > 1) rows++;
                        rowsSeen.Add(m.TintPaletteIndex);
                    }
                    if (m.IsMirror) mirrors++;
                    if (m.AlphaMode == GeomAlphaMode.Glass) glass++;
                    if (m.DiffuseSRV != null) { texd++; if (m.DiffuseSrgbView) srgb++; }
                }
                check("tint palettes resolved for non-terrain _tnt materials", tinted > 50, $"{tinted} tinted meshes, {rows} with a multi-row palette, rows in use {rowsSeen.Count}");
                check("diffuse maps use hardware sRGB views", texd > 0 && srgb * 10 >= texd * 9, $"{srgb}/{texd} sRGB views");
                check("HD (+hi) textures found through the manifests", modelRenderer.HdTextureHits > 0, $"{modelRenderer.HdTextureHits}/{modelRenderer.HdTextureAsks} HD hits");
                check("glass materials classified", glass > 0, $"{glass} glass meshes, {mirrors} mirror meshes");
                check("per-mesh light list is 64 (cbuffer 0.5 KB per draw)", ObjectVars.MaxPerMeshLights == 64, $"{ObjectVars.MaxPerMeshLights}");
            }
            {
                var at = new Vector3(-1300, 150, 60);
                CameraSequence.ApplyToCamera(camera, at, 2.4f, -0.4f, settings.FovDeg);
                settle(at);
                int batches = 0, instances = 0, withArch = 0;
                foreach (var y in World.ResidentYmaps)
                {
                    var gb = y.GrassInstanceBatches;
                    if (gb == null) continue;
                    foreach (var b in gb)
                    {
                        if (b?.Instances == null) continue;
                        if ((b.Position - at).Length() > 400) continue;
                        batches++; instances += b.Instances.Length;
                        if (b.Archetype != null) withArch++;
                    }
                }
                check("grass batches resident at the golf course", batches > 0 && withArch == batches, $"{batches} batches, {instances} instances, {withArch} with an archetype");
                grassRenderer ??= new GrassRenderer(deviceResources.Device);
                check("grass shader compiles (model.hlsl + grass.hlsl)", grassRenderer.ShaderReady, grassRenderer.LastError ?? "ok");
            }
        }
    }
}

