using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        private sealed class ScenChoice
        {
            public bool Vehicle;
            public uint Hash;
            public string ModelName;
            public string TypeName;
            public string SetName;
        }
        private sealed class ScenPlacement
        {
            public RenderModel Model; public Vector3 Pos; public Quaternion Ori; public uint Hash; public bool Selected; public int Frame;
        }
        private readonly Dictionary<ScenarioNode, ScenChoice> scenChoices = new Dictionary<ScenarioNode, ScenChoice>();
        private readonly Dictionary<ScenarioNode, ScenPlacement> scenPlaced = new Dictionary<ScenarioNode, ScenPlacement>();
        private readonly Dictionary<uint, RenderModel> scenPedModels = new Dictionary<uint, RenderModel>();
        private readonly Dictionary<uint, Task<PedModelParts>> scenPedLoads = new Dictionary<uint, Task<PedModelParts>>();
        private readonly HashSet<uint> scenVehicleLogged = new HashSet<uint>();
        private int scenFrame;
        private static readonly Matrix PedBindPoseTurn = Matrix.RotationZ((float)Math.PI);
        private bool scenPending;
        private int scenHoldFrames;

        partial void AddScenarioModels_I4(List<RenderModel> draw)
        {
            if (draw == null || panel == null || !panel.WorldMode || !worldBuilt || photoMode) return;
            scenFrame++;
            scenPending = false;
            FinishScenarioPedLoads();
            DumpScenarioPointsOnce();

            var sel = WorldEdit.Selection.ScenarioNode;
            ScenarioNode hov = null;
            if (panel.ShowScenarioModels && worldHoverSel.HasValue) hov = worldHoverSel.ScenarioNode;
            if (hov != null && ReferenceEquals(hov, sel)) hov = null;
            if (sel != null) { var m = PlaceScenarioModel(sel, true); if (m != null) draw.Add(m); }
            if (hov != null) { var m = PlaceScenarioModel(hov, false); if (m != null) draw.Add(m); }

            if (scenPlaced.Count > 8)
            {
                var drop = new List<ScenarioNode>();
                foreach (var kv in scenPlaced) if (kv.Value.Frame != scenFrame) drop.Add(kv.Key);
                foreach (var d in drop) scenPlaced.Remove(d);
            }

            if (scenPending && screenshotPath != null && screenshotFrames > 0 && screenshotFrames < 3 && scenHoldFrames++ < 900)
                screenshotFrames = 3;
        }

        private RenderModel PlaceScenarioModel(ScenarioNode node, bool selected)
        {
            var c = ScenarioChoice(node);
            if (c == null) { scenPending = true; return null; }
            if (c.Hash == 0) return null;
            var baseModel = c.Vehicle ? CarGenBaseModel(c.Hash) : ScenarioPedModel(c.Hash, c.ModelName);
            if (baseModel == null)
            {
                bool failed = c.Vehicle ? (carGenModels.TryGetValue(c.Hash, out var vm) && vm == null)
                                        : (scenPedModels.TryGetValue(c.Hash, out var pm) && pm == null);
                if (!failed) scenPending = true;
                return null;
            }
            if (scenPlaced.TryGetValue(node, out var pl) && pl.Hash == c.Hash && pl.Selected == selected && pl.Pos == node.Position && pl.Ori == node.Orientation)
            {
                pl.Frame = scenFrame;
                return pl.Model;
            }
            float lift = baseModel.Bounds.Minimum.Z < baseModel.Bounds.Maximum.Z ? -baseModel.Bounds.Minimum.Z : 0.0f;
            var world = Matrix.Translation(0.0f, 0.0f, lift) * (c.Vehicle ? Matrix.Identity : PedBindPoseTurn) *
                        Matrix.RotationQuaternion(node.Orientation) * Matrix.Translation(node.Position);
            var inst = new RenderModel { Name = baseModel.Name };
            foreach (var mesh in baseModel.Meshes)
            {
                if (mesh == null) continue;
                var im = mesh.CreateInstance(world);
                im.Highlight = selected ? 3u : 2u;
                im.FadeAlpha = selected ? 1.0f : 0.55f;
                inst.Meshes.Add(im);
                inst.Bounds = BoundingBox.Merge(inst.Bounds, im.WorldBounds);
            }
            scenPlaced[node] = new ScenPlacement { Model = inst, Pos = node.Position, Ori = node.Orientation, Hash = c.Hash, Selected = selected, Frame = scenFrame };
            if (c.Vehicle && scenVehicleLogged.Add(c.Hash))
                Console.WriteLine($"SCENMODEL vehicle {c.ModelName} placed: {inst.Meshes.Count} meshes, model bbox z {baseModel.Bounds.Minimum.Z:0.00}..{baseModel.Bounds.Maximum.Z:0.00} (lift {lift:0.00}), at {node.Position.X:0.0},{node.Position.Y:0.0},{node.Position.Z:0.0}");
            return inst;
        }

        private ScenChoice ScenarioChoice(ScenarioNode node)
        {
            if (node == null) return null;
            if (scenChoices.TryGetValue(node, out var have)) return have;
            var types = CarGenScenarioTypes() ?? Scenarios.ScenarioTypes;
            var cache = gameFiles?.Cache;
            if (cache == null || !gameFiles.Ready) return null;

            ScenarioTypeRef type = null;
            AmbientModelSet set = null;
            var pt = node.MyPoint ?? node.ClusterMyPoint;
            var sp = node.EntityPoint ?? node.LoadSavePoint ?? node.ClusterLoadSavePoint;
            if (pt != null)
            {
                type = pt.Type; set = pt.ModelSet;
                if (type == null && types != null)
                {
                    var names = pt.Region?.LookUps?.TypeNames;
                    if (names != null && pt.TypeId < names.Length) type = types.GetScenarioTypeRef(names[pt.TypeId]);
                }
            }
            else if (sp != null)
            {
                if (types == null) return null;
                type = types.GetScenarioTypeRef(sp.SpawnType);
                set = (type?.IsVehicle ?? false) ? types.GetVehicleModelSet(sp.PedType) : types.GetPedModelSet(sp.PedType);
            }
            else if (node.ChainingNode != null)
            {
                type = node.ChainingNode.Type;
                if (type == null && types != null && node.ChainingNode._Data.ScenarioType != 0) type = types.GetScenarioTypeRef(node.ChainingNode._Data.ScenarioType);
            }
            else
            {
                var none = new ScenChoice { Hash = 0, TypeName = node.MedTypeName };
                scenChoices[node] = none;
                return none;
            }

            bool vehicle = type != null && (type.IsVehicle || (type.IsGroup && LooksLikeVehicleType(type.Name)));
            if (vehicle && set == null && types != null && type.VehicleModelSetHash != 0) set = types.GetVehicleModelSet(type.VehicleModelSetHash);

            uint hash = 0; string mname = null;
            if (set?.Models != null)
            {
                foreach (var m in set.Models)
                {
                    if (m?.NameLower == null) continue;
                    uint h = JenkHash.GenHash(m.NameLower);
                    bool exists = vehicle ? (cache.YftDict?.ContainsKey(h) ?? false) : (cache.YddDict?.ContainsKey(h) ?? false);
                    if (exists) { hash = h; mname = m.NameLower; break; }
                }
            }
            if (hash == 0)
            {
                if (vehicle) (hash, mname) = DefaultScenarioVehicle(type?.Name);
                else (hash, mname) = DefaultScenarioPed();
            }
            var c = new ScenChoice { Vehicle = vehicle, Hash = hash, ModelName = mname, TypeName = type?.Name ?? "(unknown type)", SetName = set?.Name ?? "(no set)" };
            scenChoices[node] = c;
            Console.WriteLine($"SCENMODEL {node.MedTypeName} type {c.TypeName} -> {(vehicle ? "vehicle" : "ped")} set {c.SetName} -> {(hash != 0 ? mname : "nothing")} ({hash})");
            return c;
        }

        private static bool LooksLikeVehicleType(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToUpperInvariant();
            return n.Contains("VEHICLE") || n.Contains("BOAT") || n.Contains("BICYCLE") || n.Contains("HELI") || n.Contains("PLANE") ||
                   n.Contains("TRAIN") || n.Contains("BIKE") || n.Contains("DRIVE") || n.Contains("TRUCK") || n.Contains("TAXI");
        }

        private (uint, string) DefaultScenarioVehicle(string typeName)
        {
            var n = (typeName ?? "").ToUpperInvariant();
            string pick =
                n.Contains("JETSKI") ? "seashark" :
                n.Contains("BOAT") ? "dinghy" :
                n.Contains("BICYCLE") ? "cruiser" :
                n.Contains("HELI") ? "maverick" :
                n.Contains("PLANE") ? "cuban800" :
                n.Contains("TRAIN") ? null :
                n.Contains("BUS") ? "bus" :
                n.Contains("TAXI") ? "taxi" :
                n.Contains("POLICE") ? "police" :
                n.Contains("AMBULANCE") ? "ambulance" :
                n.Contains("FIRE") ? "firetruk" :
                n.Contains("TRASH") ? "trash" :
                n.Contains("TRACTOR") ? "tractor2" :
                n.Contains("TRUCK") ? "phantom" :
                (n.Contains("BIKE") || n.Contains("MOTOR")) ? "bati" :
                "blista";
            if (pick == null) return (0, null);
            uint h = JenkHash.GenHash(pick);
            if (gameFiles?.Cache?.YftDict != null && !gameFiles.Cache.YftDict.ContainsKey(h)) { h = CarGenDefaultModel; pick = "infernus"; }
            return (h, pick);
        }

        private (uint, string) DefaultScenarioPed()
        {
            foreach (var name in new[] { "a_m_y_business_01", "a_m_m_business_01", "a_m_y_hipster_01", "s_m_y_cop_01", "a_f_y_business_01" })
            {
                uint h = JenkHash.GenHash(name);
                if (gameFiles?.Cache?.YddDict != null && gameFiles.Cache.YddDict.ContainsKey(h)) return (h, name);
            }
            return (0, null);
        }

        private RenderModel ScenarioPedModel(uint hash, string name)
        {
            if (scenPedModels.TryGetValue(hash, out var have)) return have;
            if (scenPedLoads.ContainsKey(hash)) return null;
            if (scenPedLoads.Count > 0) return null;
            if (gameFiles?.Cache == null || !gameFiles.Ready || modelRenderer == null) return null;
            var gf = gameFiles;
            scenPedLoads[hash] = Task.Run(() => PedModelParts.Load(gf, hash, name));
            return null;
        }

        private void FinishScenarioPedLoads()
        {
            if (scenPedLoads.Count == 0) return;
            uint doneHash = 0; Task<PedModelParts> done = null;
            foreach (var kv in scenPedLoads) if (kv.Value.IsCompleted) { doneHash = kv.Key; done = kv.Value; break; }
            if (done == null) return;
            scenPedLoads.Remove(doneHash);
            PedModelParts parts = null;
            try { parts = done.Result; } catch (Exception ex) { Console.WriteLine($"SCENMODEL ped {doneHash}: read failed ({ex.Message})"); }
            RenderModel built = null;
            if (parts != null && parts.Parts.Count > 0 && modelRenderer != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                built = new RenderModel { Name = parts.Name };
                try
                {
                    modelRenderer.TextureContext = parts.Hash;
                    modelRenderer.AssetNameContext = 0;
                    Matrix? pose = parts.HasSkeleton && parts.SkinSpread < 0.05f ? parts.RootSkin : (Matrix?)null;
                    foreach (var part in parts.Parts)
                    {
                        modelRenderer.DiffuseOverride = part.texture;
                        var pm = modelRenderer.BuildFromDrawable(part.drawable, parts.Name + ":" + part.drawableName, pose);
                        if (pm == null) continue;
                        foreach (var mesh in pm.Meshes)
                        {
                            built.Meshes.Add(mesh);
                            built.Bounds = BoundingBox.Merge(built.Bounds, mesh.WorldBounds);
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"SCENMODEL ped {parts.Name}: build failed ({ex.Message})"); }
                finally { modelRenderer.DiffuseOverride = null; modelRenderer.TextureContext = 0; modelRenderer.AssetNameContext = 0; }
                if (built.Meshes.Count == 0) { built.Dispose(); built = null; }
                int textured = 0; foreach (var m in built?.Meshes ?? new List<RenderMesh>()) if (m.DiffuseSRV != null) textured++;
                Console.WriteLine($"SCENMODEL ped {parts.Name} ({parts.Hash}): {(built != null ? "built" : "NOT BUILT")} - {parts.Parts.Count} parts ({(parts.FromYmt ? "ymt" : "by name")}) " +
                                  $"{built?.Meshes.Count ?? 0} meshes, {textured} textured, read {parts.LoadMs:0} ms build {sw.Elapsed.TotalMilliseconds:0} ms" +
                                  (built != null ? $", bbox {built.Bounds.Minimum.X:0.00},{built.Bounds.Minimum.Y:0.00},{built.Bounds.Minimum.Z:0.00}..{built.Bounds.Maximum.X:0.00},{built.Bounds.Maximum.Y:0.00},{built.Bounds.Maximum.Z:0.00}" : "") +
                                  $" [{string.Join(" ", ScenPartNames(parts))}] ydd: {parts.YddNames}" +
                                  $" toes {ScenToeDirection(built, parts)}" +
                                  (parts.HasSkeleton ? $" rootSkin [{parts.RootSkin.M11:0.00} {parts.RootSkin.M12:0.00} {parts.RootSkin.M13:0.00} | {parts.RootSkin.M21:0.00} {parts.RootSkin.M22:0.00} {parts.RootSkin.M23:0.00} | {parts.RootSkin.M31:0.00} {parts.RootSkin.M32:0.00} {parts.RootSkin.M33:0.00} | {parts.RootSkin.M41:0.00} {parts.RootSkin.M42:0.00} {parts.RootSkin.M43:0.00}] spread {parts.SkinSpread:0.000}" : " no skeleton"));
            }
            else
            {
                Console.WriteLine($"SCENMODEL ped {parts?.Name ?? doneHash.ToString()}: NOT BUILT - {parts?.Error ?? "no parts"}");
            }
            scenPedModels[doneHash] = built;
            if (built == null) WorldEdit.LastStatus = $"scenario ped {parts?.Name ?? doneHash.ToString()} not found";
        }

        private static string ScenToeDirection(RenderModel built, PedModelParts parts)
        {
            if (built == null) return "?";
            float minZ = built.Bounds.Minimum.Z;
            double sx = 0, sy = 0; int sn = 0; double ax = 0, ay = 0; int an = 0;
            foreach (var m in built.Meshes)
            {
                if (m?.PickVerts == null) continue;
                foreach (var v in m.PickVerts)
                {
                    var w = Vector3.TransformCoordinate(v, m.Transform);
                    float h = w.Z - minZ;
                    if (h < 0.05f) { sx += w.X; sy += w.Y; sn++; }
                    else if (h > 0.10f && h < 0.20f) { ax += w.X; ay += w.Y; an++; }
                }
            }
            if (sn == 0 || an == 0) return "?";
            double dx = sx / sn - ax / an, dy = sy / sn - ay / an;
            return $"({dx:0.000},{dy:0.000}) -> {(Math.Abs(dy) > Math.Abs(dx) ? (dy > 0 ? "+Y" : "-Y") : (dx > 0 ? "+X" : "-X"))}";
        }

        private static IEnumerable<string> ScenPartNames(PedModelParts p)
        {
            foreach (var part in p.Parts) yield return part.drawableName + (part.texture != null ? "/" + part.texture.Name : "/-");
        }

        private bool scenDumped;
        private int scenDumpWait;
        private void DumpScenarioPointsOnce()
        {
            if (scenDumped) return;
            var want = Environment.GetEnvironmentVariable("RLE_SCENFIND");
            if (string.IsNullOrEmpty(want)) { scenDumped = true; return; }
            if (++scenDumpWait < 60) return;
            var sd = SpaceDataOrNull;
            if (sd == null) return;
            sd.EnsureScenarios();
            if (!sd.ScenariosReady) return;
            scenDumped = true;
            var camPos = camera.Position;
            var found = new List<(float dist, ScenarioNode n, string type)>();
            foreach (var ymt in sd.Scenarios.ScenarioRegions)
            {
                var nodes = ymt?.ScenarioRegion?.Nodes; if (nodes == null) continue;
                foreach (var n in nodes)
                {
                    var pt = n?.MyPoint ?? n?.ClusterMyPoint; if (pt == null) continue;
                    var tn = pt.Type?.Name ?? "";
                    if (tn.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found.Add(((n.Position - camPos).Length(), n, tn));
                }
            }
            found.Sort((a, b) => a.dist.CompareTo(b.dist));
            Console.WriteLine($"SCENFIND '{want}': {found.Count} points, nearest:");
            for (int i = 0; i < found.Count && i < 12; i++)
            {
                var n = found[i].n; var pt = n.MyPoint ?? n.ClusterMyPoint;
                Console.WriteLine($"SCENFIND {found[i].dist:0} m  {found[i].type}  set {pt.ModelSet?.Name ?? "-"}  pos {n.Position.X:0.0},{n.Position.Y:0.0},{n.Position.Z:0.0}  dir {pt.Direction * 57.2958f:0} deg  region {n.Ymt?.Name}");
            }
        }

        partial void ReleaseScenarioModels_I4()
        {
            foreach (var kv in scenPedModels) kv.Value?.Dispose();
            scenPedModels.Clear();
            scenPlaced.Clear();
            scenChoices.Clear();
        }
    }
}

