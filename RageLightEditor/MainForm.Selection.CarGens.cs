using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using CodeWalker.World;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private readonly Dictionary<uint, RenderModel> carGenModels = new Dictionary<uint, RenderModel>();
        private sealed class CarGenPlacement
        {
            public RenderModel Model; public Vector3 Pos; public Quaternion Ori; public uint Hash; public int Frame;
            public Vector3 Origin;
        }
        private readonly Dictionary<YmapCarGen, CarGenPlacement> carGenPlaced = new Dictionary<YmapCarGen, CarGenPlacement>();
        private readonly List<RenderModel> carGenDraw = new List<RenderModel>();
        private readonly List<YmapCarGen> carGenScratch = new List<YmapCarGen>();
        private int carGenFrame;
        private int carGenLoadsThisFrame;
        private bool carGenEnvApplied;
        private const float CarGenModelsRange = 100.0f;
        private static readonly Quaternion CarGenTurn = Quaternion.RotationAxis(Vector3.UnitZ, (float)Math.PI * -0.5f);

        private List<RenderModel> WorldExtraModels_Selection()
        {
            carGenDraw.Clear();
            if (panel == null || !panel.WorldMode || !worldBuilt || photoMode) return carGenDraw;
            carGenFrame++;
            carGenLoadsThisFrame = 0;
            carGenCastsThisFrame = 0;
            PruneCarGenGround();
            if (!carGenEnvApplied) { carGenEnvApplied = true; if (Environment.GetEnvironmentVariable("RLE_CARGENMODELS") == "1") panel.ShowCarGenModels = true; }
            var camPos = camera.Position;
            DumpCarGensOnce(camPos);
            CarGenScenarioTypes();

            carGenScratch.Clear();
            var sel = WorldEdit.Selection.CarGenerator;
            if (sel != null) carGenScratch.Add(sel);
            if (panel.ShowCarGenModels)
            {
                World.SnapshotWalkedYmaps(selYmaps);
                foreach (var ymap in selYmaps)
                {
                    var cgs = ymap.CarGenerators; if (cgs == null) continue;
                    foreach (var cg in cgs)
                    {
                        if (cg == null || ReferenceEquals(cg, sel)) continue;
                        if ((cg.Position - camPos).LengthSquared() > CarGenModelsRange * CarGenModelsRange) continue;
                        carGenScratch.Add(cg);
                    }
                }
            }
            foreach (var cg in carGenScratch)
            {
                var m = PlaceCarGenModel(cg);
                if (m != null) carGenDraw.Add(m);
            }
            if (carGenPlaced.Count > carGenScratch.Count + 32)
            {
                var drop = new List<YmapCarGen>();
                foreach (var kv in carGenPlaced) if (kv.Value.Frame != carGenFrame) drop.Add(kv.Key);
                foreach (var d in drop) carGenPlaced.Remove(d);
            }
            return carGenDraw;
        }

        private RenderModel PlaceCarGenModel(YmapCarGen cg)
        {
            uint hash = CarGenModelHash(cg);
            if (hash == 0) return null;
            var baseModel = CarGenBaseModel(hash);
            if (baseModel == null) return null;
            var origin = CarGenVehicleOrigin(cg, hash);
            if (carGenPlaced.TryGetValue(cg, out var pl) && pl.Hash == hash && pl.Pos == cg.Position && pl.Ori == cg.Orientation && pl.Origin == origin)
            {
                pl.Frame = carGenFrame;
                return pl.Model;
            }
            var world = Matrix.RotationQuaternion(Quaternion.Multiply(cg.Orientation, CarGenTurn)) * Matrix.Translation(origin);
            var inst = new RenderModel { Name = baseModel.Name };
            foreach (var mesh in baseModel.Meshes)
            {
                if (mesh == null) continue;
                var im = mesh.CreateInstance(world);
                inst.Meshes.Add(im);
                inst.Bounds = BoundingBox.Merge(inst.Bounds, im.WorldBounds);
            }
            carGenPlaced[cg] = new CarGenPlacement { Model = inst, Pos = cg.Position, Ori = cg.Orientation, Hash = hash, Frame = carGenFrame, Origin = origin };
            return inst;
        }

        private uint CarGenModelHash(YmapCarGen cg)
        {
            var d = cg._CCarGen;
            uint hash = d.carModel;
            if (hash != 0) return hash;
            var types = CarGenScenarioTypes();
            if (types == null) return 0;
            if (d.popGroup != 0)
            {
                var set = types.GetVehicleModelSet(d.popGroup);
                if (set?.Models != null && set.Models.Length > 0 && set.Models[0] != null)
                    hash = JenkHash.GenHash(set.Models[0].NameLower);
            }
            if (hash == 0) hash = CarGenDefaultModel;
            if (Environment.GetEnvironmentVariable("RLE_DUMPCARGENS") == "1" && carGenHashLogged.Add(d.popGroup))
                Console.WriteLine($"CARGEN pop group {d.popGroup} -> model {new MetaHash(hash)} (set {(types.GetVehicleModelSet(d.popGroup)?.Name ?? "none")})");
            return hash;
        }
        private const uint CarGenDefaultModel = 418536135u;
        private readonly HashSet<uint> carGenHashLogged = new HashSet<uint>();

        private static ScenarioTypes carGenTypes;
        private static int carGenTypesState;
        private ScenarioTypes CarGenScenarioTypes()
        {
            if (carGenTypesState == 2) return carGenTypes;
            if (carGenTypesState != 0 || gameFiles?.Cache == null || !gameFiles.Ready) return null;
            carGenTypesState = 1;
            var cache = gameFiles.Cache;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var t = new ScenarioTypes();
                    t.Load(cache);
                    carGenTypes = t;
                    carGenTypesState = 2;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("car generator model sets: " + ex.Message);
                    carGenTypesState = 3;
                }
            });
            return null;
        }

        private RenderModel CarGenBaseModel(uint hash)
        {
            if (carGenModels.TryGetValue(hash, out var have)) return have;
            if (gameFiles?.Cache == null || !gameFiles.Ready || modelRenderer == null) return null;
            if (carGenLoadsThisFrame > 0) return null;
            carGenLoadsThisFrame++;
            RenderModel built = null;
            try
            {
                var yft = gameFiles.Cache.GetYft(hash);
                if (yft != null && gameFiles.EnsureLoaded(yft) && yft.Fragment != null)
                {
                    modelRenderer.TextureContext = hash;
                    modelRenderer.AssetNameContext = hash;
                    built = modelRenderer.BuildFromYft(yft);
                    modelRenderer.TextureContext = 0;
                    modelRenderer.AssetNameContext = 0;
                    if (built != null && built.Meshes.Count == 0) { built.Dispose(); built = null; }
                    CarGenRememberMinZ(hash, built, yft);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"car generator model {hash}: {ex.Message}");
                modelRenderer.TextureContext = 0; modelRenderer.AssetNameContext = 0;
                built = null;
            }
            carGenModels[hash] = built;
            if (built == null) WorldEdit.LastStatus = $"car generator model {new MetaHash(hash)} not found";
            Console.WriteLine($"CARGEN model {new MetaHash(hash)} ({hash}): {(built != null ? built.Meshes.Count + " meshes" : "NOT BUILT")}");
            return built;
        }

        private void DrawCarGenPlaceholder(YmapCarGen cg, bool selected)
        {
            var col = selected ? SelColourSoft : TF(UiTheme.Accent, 0.16f);
            var line = selected ? SelColour : HelperBlue;
            float gz = CarGenGroundZ(cg, out _) - cg.Position.Z;
            var mn = new Vector3(-2.3f, -0.95f, gz);
            var mx = new Vector3(2.3f, 0.95f, gz + 1.5f);
            Vector3 X(float x, float y, float z) => cg.Orientation.Multiply(new Vector3(x, y, z)) + cg.Position;
            var v0 = X(mn.X, mn.Y, mn.Z); var v1 = X(mn.X, mn.Y, mx.Z); var v2 = X(mn.X, mx.Y, mn.Z); var v3 = X(mn.X, mx.Y, mx.Z);
            var v4 = X(mx.X, mn.Y, mn.Z); var v5 = X(mx.X, mn.Y, mx.Z); var v6 = X(mx.X, mx.Y, mn.Z); var v7 = X(mx.X, mx.Y, mx.Z);
            triRenderer.AddQuad(v0, v1, v3, v2, col); triRenderer.AddQuad(v4, v6, v7, v5, col);
            triRenderer.AddQuad(v0, v4, v5, v1, col); triRenderer.AddQuad(v2, v3, v7, v6, col);
            triRenderer.AddQuad(v0, v2, v6, v4, col); triRenderer.AddQuad(v1, v5, v7, v3, col);
            DrawOrientedBox(cg.Position, cg.Orientation, mn, mx, line);
            lineRenderer.AddLine(X(0.6f, mn.Y, mx.Z), X(1.4f, mn.Y, gz + 0.9f), line);
            lineRenderer.AddLine(X(0.6f, mx.Y, mx.Z), X(1.4f, mx.Y, gz + 0.9f), line);
        }

        private void DrawCarGenPlaceholders(SharpDX.Direct3D11.DeviceContext context)
        {
            var sel = WorldEdit.Selection.CarGenerator;
            bool any = false;
            if (sel != null && CarGenNeedsPlaceholder(sel)) { DrawCarGenPlaceholder(sel, true); any = true; }
            if (panel.ShowCarGenModels)
            {
                var camPos = camera.Position;
                World.SnapshotWalkedYmaps(selYmaps);
                foreach (var ymap in selYmaps)
                {
                    var cgs = ymap.CarGenerators; if (cgs == null) continue;
                    foreach (var cg in cgs)
                    {
                        if (cg == null || ReferenceEquals(cg, sel)) continue;
                        if ((cg.Position - camPos).LengthSquared() > CarGenModelsRange * CarGenModelsRange) continue;
                        if (!CarGenNeedsPlaceholder(cg)) continue;
                        DrawCarGenPlaceholder(cg, false); any = true;
                    }
                }
            }
            if (any) triRenderer.Flush(context, camera.ViewProjMatrix, CommonStates.BlendAlpha, CommonStates.DepthReadOnly);
        }

        private bool CarGenNeedsPlaceholder(YmapCarGen cg)
        {
            if (carGenPlaced.TryGetValue(cg, out var pl) && pl.Frame == carGenFrame && pl.Model != null) return false;
            return true;
        }

        private bool carGenDumped;
        private int carGenDumpWait;
        private void DumpCarGensOnce(Vector3 camPos)
        {
            if (carGenDumped || Environment.GetEnvironmentVariable("RLE_DUMPCARGENS") != "1") return;
            if (++carGenDumpWait < 120) return;
            carGenDumped = true;
            var named = new List<(float dist, YmapCarGen cg)>();
            int total = 0, unnamed = 0;
            foreach (var ymap in World.ResidentYmaps)
            {
                var cgs = ymap?.CarGenerators; if (cgs == null) continue;
                foreach (var cg in cgs)
                {
                    if (cg == null) continue;
                    total++;
                    if (cg._CCarGen.carModel == 0) { unnamed++; continue; }
                    named.Add(((cg.Position - camPos).Length(), cg));
                }
            }
            named.Sort((a, b) => a.dist.CompareTo(b.dist));
            Console.WriteLine($"CARGENS resident {total}, without a model {unnamed}, with a model {named.Count}");
            for (int i = 0; i < named.Count && i < 12; i++)
            {
                var cg = named[i].cg; var d = cg._CCarGen;
                bool inDict = gameFiles?.Cache?.YftDict != null && gameFiles.Cache.YftDict.ContainsKey(d.carModel);
                var o = WorldEditor.ToEulerDegrees(cg.Orientation);
                Console.WriteLine($"CARGEN {named[i].dist:0} m  {d.carModel} ({cg.NameString()}) yft={(inDict ? "found" : "MISSING")}  pos {cg.Position.X:0.0},{cg.Position.Y:0.0},{cg.Position.Z:0.0}  heading {o.Z:0}  len {d.perpendicularLength:0.#}  ymap {cg.Ymap?.Name}");
            }
        }

        private void ReleaseCarGenModels()
        {
            foreach (var kv in carGenModels) kv.Value?.Dispose();
            carGenModels.Clear();
            carGenPlaced.Clear();
            carGenDraw.Clear();
            ReleaseCarGenGround();
        }
    }
}

