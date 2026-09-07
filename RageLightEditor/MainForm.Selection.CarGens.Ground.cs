using System;
using System.Collections.Generic;
using CodeWalker;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private sealed class CarGenGround
        {
            public Vector3 Pos;
            public float GroundZ;
            public bool Grounded;
            public bool Hit;
            public int CastFrame;
            public int UsedFrame;
            public bool Logged;
        }
        private readonly Dictionary<YmapCarGen, CarGenGround> carGenGround = new Dictionary<YmapCarGen, CarGenGround>();
        private int carGenCastsThisFrame;
        private const int CarGenCastBudget = 3;
        private const int CarGenRetryFrames = 60;
        private const int CarGenNeverCast = -1000000;
        private const int CarGenRefreshFrames = 600;
        private const float CarGenCastUp = 0.5f;
        private const float CarGenCastDown = 5.0f;
        private const float CarGenSlabHeight = 0.2f;
        private const float CarGenCastRange = 250.0f;

        private readonly Dictionary<uint, float> carGenModelMinZ = new Dictionary<uint, float>();

        private float CarGenGroundZ(YmapCarGen cg, out bool hit, bool mayCast = true)
        {
            var pos = cg.Position;
            if (!carGenGround.TryGetValue(cg, out var g))
            {
                g = new CarGenGround { Pos = pos, GroundZ = pos.Z, Grounded = false, Hit = false, CastFrame = CarGenNeverCast };
                carGenGround[cg] = g;
            }
            g.UsedFrame = carGenFrame;
            bool moved = g.Pos != pos;
            int retry = ReferenceEquals(cg, WorldEdit.Selection.CarGenerator) ? CarGenRetryFrames / 6 : CarGenRetryFrames;
            bool due = moved || (!g.Hit && carGenFrame - g.CastFrame >= retry) || (g.Hit && carGenFrame - g.CastFrame >= CarGenRefreshFrames);
            if (due && mayCast && (carGenCastsThisFrame < CarGenCastBudget || moved))
            {
                carGenCastsThisFrame++;
                bool wasGrounded = g.Grounded; float wasZ = g.GroundZ;
                g.Pos = pos;
                g.CastFrame = carGenFrame;
                if (CarGenCastGround(pos, out float gz)) { g.GroundZ = gz; g.Grounded = true; g.Hit = true; }
                else if (moved && wasGrounded && pos.Z >= wasZ - 0.5f && pos.Z <= wasZ + CarGenCastDown)
                {
                    g.Hit = false;
                }
                else if (moved || !g.Grounded) { g.GroundZ = pos.Z; g.Grounded = false; g.Hit = false; }
                else g.Hit = false;
                if (moved || g.Grounded != wasGrounded || Math.Abs(g.GroundZ - wasZ) > 0.01f) g.Logged = false;
            }
            else if (moved)
            {
                g.GroundZ += pos.Z - g.Pos.Z; g.Pos = pos; g.Hit = false; g.CastFrame = CarGenNeverCast;
            }
            hit = g.Grounded;
            return g.GroundZ;
        }

        private void PruneCarGenGround()
        {
            if (carGenFrame % 300 != 0 || carGenGround.Count < 512) return;
            var drop = new List<YmapCarGen>();
            foreach (var kv in carGenGround) if (carGenFrame - kv.Value.UsedFrame > 1800) drop.Add(kv.Key);
            foreach (var d in drop) carGenGround.Remove(d);
        }

        private bool CarGenCastGround(Vector3 pos, out float groundZ)
        {
            groundZ = pos.Z;
            if (worldRender == null) return false;
            var from = new Vector3(pos.X, pos.Y, pos.Z + CarGenCastUp);
            var ray = new Ray(from, -Vector3.UnitZ);
            float maxD = CarGenCastUp + CarGenCastDown;
            float best = float.MaxValue;
            int total = 0, tested = 0;
            foreach (var kv in worldRender.LiveInstances)
            {
                var e = kv.Key; var meshes = kv.Value;
                if (e == null || meshes == null) continue;
                total++;
                var lod = e._CEntityDef.lodLevel;
                if (pos.X < e.BBMin.X || pos.X > e.BBMax.X || pos.Y < e.BBMin.Y || pos.Y > e.BBMax.Y) continue;
                if (e.BBMin.Z > from.Z || e.BBMax.Z < from.Z - maxD) continue;
                if (carGenDumpAll && carGenDumpVerbose) Console.WriteLine($"CARGEN cast candidate {e.Archetype?.Name} lod={lod} z [{e.BBMin.Z:0.0}..{e.BBMax.Z:0.0}] meshes {meshes.Count} ymap {e.Ymap?.Name}");
                if (lod != rage__eLodType.LODTYPES_DEPTH_HD && lod != rage__eLodType.LODTYPES_DEPTH_ORPHANHD) continue;
                foreach (var m in meshes)
                {
                    if (m?.PickVerts == null || m.NeverDraw || !m.Visible) continue;
                    bool decal = m.AlphaMode == GeomAlphaMode.Decal || m.AlphaMode == GeomAlphaMode.Additive;
                    if (decal && !carGenDumpAll) continue;
                    var b = m.WorldBounds;
                    if (b.Maximum.X < b.Minimum.X) continue;
                    if (pos.X < b.Minimum.X || pos.X > b.Maximum.X || pos.Y < b.Minimum.Y || pos.Y > b.Maximum.Y) continue;
                    if (b.Minimum.Z > from.Z || b.Maximum.Z < from.Z - maxD) continue;
                    if (from.Z - b.Maximum.Z > best) continue;
                    tested++;
                    var r = ray;
                    if (!m.RayHit(ref r, out float d)) continue;
                    if (carGenDumpAll) Console.WriteLine($"CARGEN cast hit z {from.Z - d:0.00} {e.Archetype?.Name} shader={m.ShaderName} alpha={m.AlphaMode} lod={lod}");
                    if (decal) continue;
                    if (d < best && d <= maxD) best = d;
                }
            }
            if (carGenDumpAll) Console.WriteLine($"CARGEN cast at {pos.X:0.0},{pos.Y:0.0} from z {from.Z:0.00}: {tested} meshes of {total} entities tested, {(best == float.MaxValue ? "no hit" : "ground z " + (from.Z - best).ToString("0.00"))}");
            if (best == float.MaxValue) return false;
            groundZ = from.Z - best;
            return true;
        }

        private float CarGenModelMinZ(uint hash)
        {
            return carGenModelMinZ.TryGetValue(hash, out float z) ? z : 0.0f;
        }

        private void CarGenRememberMinZ(uint hash, RenderModel built, YftFile yft)
        {
            float visual = float.NaN, physics = float.NaN;
            if (built != null && built.Meshes.Count > 0 && built.Bounds.Maximum.Z >= built.Bounds.Minimum.Z) visual = built.Bounds.Minimum.Z;
            var bound = yft?.Fragment?.PhysicsLODGroup?.PhysicsLOD1?.Bound;
            if (bound != null) physics = bound.BoxMin.Z;
            float minZ = 0.0f;
            if (!float.IsNaN(visual) && visual <= 0.5f && visual >= -4.0f) minZ = visual;
            else if (!float.IsNaN(physics)) minZ = physics;
            carGenModelMinZ[hash] = minZ;
            Console.WriteLine($"CARGEN model {new MetaHash(hash)} bbox z [{(float.IsNaN(visual) ? "?" : built.Bounds.Minimum.Z.ToString("0.00"))} .. {(built != null && built.Meshes.Count > 0 ? built.Bounds.Maximum.Z.ToString("0.00") : "?")}] physics min z {(float.IsNaN(physics) ? "?" : physics.ToString("0.00"))} -> lift {-minZ:0.00} m");
        }

        private Vector3 CarGenVehicleOrigin(YmapCarGen cg, uint hash)
        {
            float gz = CarGenGroundZ(cg, out bool hit);
            float minZ = CarGenModelMinZ(hash);
            var p = cg.Position;
            var origin = new Vector3(p.X, p.Y, hit ? gz - minZ : p.Z);
            if (carGenGround.TryGetValue(cg, out var g) && !g.Logged && (ReferenceEquals(cg, WorldEdit.Selection.CarGenerator) || carGenDumpAll))
            {
                g.Logged = true;
                Console.WriteLine($"CARGEN {cg.NameString()} pos {p.X:0.00},{p.Y:0.00},{p.Z:0.00}  ground z {gz:0.00} ({(hit ? "hit" : "NO HIT, using the position")}, {(gz - p.Z):+0.00;-0.00} m from the position)  model {new MetaHash(hash)} bbox min z {minZ:0.00}  final origin z {origin.Z:0.00}  bay {CarGenBayLength(cg):0.0} x {cg._CCarGen.perpendicularLength:0.0} m");
            }
            return origin;
        }
        private static readonly bool carGenDumpAll = Environment.GetEnvironmentVariable("RLE_DUMPCARGENS") == "1";
        private static readonly bool carGenDumpVerbose = Environment.GetEnvironmentVariable("RLE_DUMPCARGENS_CAST") == "1";

        private static float CarGenBayLength(YmapCarGen cg)
        {
            var d = cg._CCarGen;
            float len = (float)Math.Sqrt(d.orientX * d.orientX + d.orientY * d.orientY);
            if (len < 0.5f) len = Math.Max(d.perpendicularLength * 1.5f, 5.0f);
            return len;
        }

        private void CarGenBayBox(YmapCarGen cg, bool mayCast, out Vector3 mn, out Vector3 mx)
        {
            float gz = CarGenGroundZ(cg, out _, mayCast) - cg.Position.Z;
            float hl = CarGenBayLength(cg) * 0.5f;
            float hw = Math.Max(cg._CCarGen.perpendicularLength, 1.0f) * 0.5f;
            mn = new Vector3(-hl, -hw, gz);
            mx = new Vector3(hl, hw, gz + CarGenSlabHeight);
        }

        private void DrawCarGenFootprint(YmapCarGen cg, Vector4 col, bool full)
        {
            CarGenBayBox(cg, true, out var mn, out var mx);
            var pos = cg.Position; var ori = cg.Orientation;
            DrawOrientedBox(pos, ori, mn, mx, col);
            Vector3 X(float x, float y, float z) => pos + ori.Multiply(new Vector3(x, y, z));
            var fill = new Vector4(col.X, col.Y, col.Z, (full ? 0.22f : 0.10f)) * new Vector4(FillHdr / HelperHdr, FillHdr / HelperHdr, FillHdr / HelperHdr, 1.0f);
            triRenderer.AddQuad(X(mn.X, mn.Y, mx.Z), X(mx.X, mn.Y, mx.Z), X(mx.X, mx.Y, mx.Z), X(mn.X, mx.Y, mx.Z), fill);
            lineRenderer.AddLine(X(mn.X, 0, mx.Z), X(mx.X, 0, mx.Z), new Vector4(col.X, col.Y, col.Z, col.W * 0.5f));
            if (full) DrawOrientedBox(pos, ori, cg.BBMin, cg.BBMax, new Vector4(col.X, col.Y, col.Z, col.W * 0.22f));
        }

        private HelperBox CarGenHelperBox(YmapCarGen cg, Vector3 camPos)
        {
            bool near = (cg.Position - camPos).LengthSquared() < CarGenCastRange * CarGenCastRange;
            CarGenBayBox(cg, near, out var mn, out var mx);
            return new HelperBox { Pos = cg.Position, Ori = cg.Orientation, Min = mn, Max = mx, Col = HelperBlue };
        }

        private void ReleaseCarGenGround()
        {
            carGenGround.Clear();
            carGenModelMinZ.Clear();
        }
    }
}

