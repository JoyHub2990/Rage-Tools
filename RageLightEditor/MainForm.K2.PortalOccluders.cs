using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;
using RageLightEditor.Rendering;
using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private YmapEntityDef occluderInterior;
        private readonly Dictionary<int, RenderMesh> portalOccluders = new Dictionary<int, RenderMesh>();
        private int occluderTick;
        private string portalOccluderStatus = "";
        private string portalOccluderLogged = "";
        public bool PortalOccludersEnabled = true;

        private void TickPortalOccluders_K2()
        {
            worldRender.CasterEye = camera.Position;
            var inside = interiorCull?.Inside;
            if (!PortalOccludersEnabled || inside == null || interiorCull.InsideArch?.portals == null || inside.MloInstance?.Entities == null)
            {
                if (worldRender.SunCasterOccluders.Count > 0 || occluderInterior != null) ClearPortalOccluders_K2();
                return;
            }
            if (!ReferenceEquals(inside, occluderInterior)) { ClearPortalOccluders_K2(); occluderInterior = inside; occluderTick = 0; }
            if (occluderTick++ % 30 != 0) return;

            var arch = interiorCull.InsideArch;
            var ents = inside.MloInstance.Entities;
            var want = new HashSet<int>();
            int missingDoors = 0, doorsPresent = 0;
            for (int pi = 0; pi < arch.portals.Length; pi++)
            {
                var p = arch.portals[pi];
                if (p?.AttachedObjects == null || p.AttachedObjects.Length == 0) continue;
                if (p.Corners == null || p.Corners.Length < 3) continue;
                bool anyDrawable = false;
                foreach (var ai in p.AttachedObjects)
                {
                    var ae = ai < ents.Length ? ents[ai] : null;
                    if (ae?.Archetype == null) continue;
                    if (worldRender.IsFailed(ae.Archetype.Hash)) continue;
                    anyDrawable = true;
                    break;
                }
                if (anyDrawable) { doorsPresent++; continue; }
                missingDoors++;
                want.Add(pi);
                if (!portalOccluders.ContainsKey(pi))
                {
                    var q = BuildPortalOccluder_K2(inside, p);
                    if (q != null) portalOccluders[pi] = q;
                }
            }
            foreach (var k in portalOccluders.Keys.Where(k => !want.Contains(k)).ToList())
            {
                portalOccluders[k].Dispose();
                portalOccluders.Remove(k);
            }
            worldRender.SunCasterOccluders.Clear();
            worldRender.SunCasterOccluders.AddRange(portalOccluders.Values);
            portalOccluderStatus = missingDoors == 0 ? "" : $"{missingDoors} door portal{(missingDoors == 1 ? "" : "s")} without a door: closed for the sun";
            if (screenshotPath != null)
            {
                string line = $"PORTALOCC {inside.Archetype?.Name}: {arch.portals.Length} portals, {doorsPresent} with a drawable door, {missingDoors} occluded (no door to draw), extras {worldRender.SunCasterExtras.Count}";
                if (line != portalOccluderLogged) { portalOccluderLogged = line; Console.WriteLine(line); }
            }
        }

        private readonly List<RenderMesh> sceneOccluders = new List<RenderMesh>();
        private int sceneOccludersVersion = -1;
        private List<RenderMesh> sceneCasterScratch;
        public string SceneOccluderStatus { get; private set; } = "";

        private IEnumerable<RenderMesh> WithPortalOccluders_K2(List<RenderMesh> meshes)
        {
            if (!PortalOccludersEnabled) return meshes;
            var info = scene?.MloInfo;
            if (info == null || info.Interiors.Count == 0 || !scene.MloVisible) return meshes;
            if (sceneOccludersVersion != scene.GeometryVersion)
            {
                sceneOccludersVersion = scene.GeometryVersion;
                foreach (var m in sceneOccluders) m.Dispose();
                sceneOccluders.Clear();
                var unresolved = new HashSet<uint>();
                var resolved = new HashSet<uint>();
                foreach (var e in info.Entities) { if (e.Resolved) resolved.Add(e.ArchetypeHash); else unresolved.Add(e.ArchetypeHash); }
                int doorPortals = 0, occluded = 0;
                foreach (var it in info.Interiors)
                {
                    var arch = it?.Arch;
                    if (arch?.portals == null) continue;
                    foreach (var p in arch.portals)
                    {
                        if (p?.AttachedObjects == null || p.AttachedObjects.Length == 0 || p.Corners == null || p.Corners.Length < 3) continue;
                        doorPortals++;
                        bool anyDrawable = false;
                        foreach (var ai in p.AttachedObjects)
                        {
                            var ent = arch.entities != null && ai < arch.entities.Length ? arch.entities[ai] : null;
                            if (ent == null) continue;
                            uint h = ent._Data.archetypeName.Hash;
                            if (resolved.Contains(h) || !unresolved.Contains(h)) { anyDrawable = true; break; }
                        }
                        if (anyDrawable) continue;
                        var q = BuildPortalOccluderAt_K2(it.Position, it.Orientation, p);
                        if (q != null) { sceneOccluders.Add(q); occluded++; }
                    }
                }
                SceneOccluderStatus = doorPortals == 0 ? "" : $"{doorPortals} door portals, {occluded} without a door: closed for the sun";
                if (screenshotPath != null && doorPortals > 0) Console.WriteLine($"PORTALOCC scene: {doorPortals} door portals, {occluded} occluded (door archetype missing)");
            }
            if (sceneOccluders.Count == 0) return meshes;
            sceneCasterScratch ??= new List<RenderMesh>();
            sceneCasterScratch.Clear();
            sceneCasterScratch.AddRange(meshes);
            sceneCasterScratch.AddRange(sceneOccluders);
            return sceneCasterScratch;
        }

        private void ClearPortalOccluders_K2()
        {
            foreach (var m in portalOccluders.Values) m.Dispose();
            portalOccluders.Clear();
            worldRender.SunCasterOccluders.Clear();
            occluderInterior = null;
            portalOccluderStatus = "";
        }

        private RenderMesh BuildPortalOccluder_K2(YmapEntityDef mlo, MCMloPortalDef p) => BuildPortalOccluderAt_K2(mlo.Position, mlo.Orientation, p);

        private RenderMesh BuildPortalOccluderAt_K2(Vector3 mloPos, Quaternion mloRot, MCMloPortalDef p)
        {
            var device = deviceResources?.Device;
            if (device == null) return null;
            var m = Matrix.RotationQuaternion(mloRot) * Matrix.Translation(mloPos);
            int n = Math.Min(p.Corners.Length, 4);
            var pos = new Vector3[n];
            for (int i = 0; i < n; i++) pos[i] = Vector3.TransformCoordinate(new Vector3(p.Corners[i].X, p.Corners[i].Y, p.Corners[i].Z), m);
            var normal = Vector3.Cross(pos[1] - pos[0], pos[2] - pos[0]);
            if (normal.LengthSquared() < 1e-8f) return null;
            normal.Normalize();
            var verts = new MeshVertex[n];
            for (int i = 0; i < n; i++)
                verts[i] = new MeshVertex { Position = pos[i], Normal = normal, Tangent = new Vector4(1, 0, 0, 1), Colour0 = Vector4.One, Colour1 = Vector4.One };
            var idx = new List<ushort>();
            for (int i = 1; i + 1 < n; i++) { idx.Add(0); idx.Add((ushort)i); idx.Add((ushort)(i + 1)); }
            for (int i = 1; i + 1 < n; i++) { idx.Add(0); idx.Add((ushort)(i + 1)); idx.Add((ushort)i); }
            var indices = idx.ToArray();
            var mesh = new RenderMesh
            {
                Transform = Matrix.Identity,
                VB = Buffer.Create(device, BindFlags.VertexBuffer, verts),
                IB = Buffer.Create(device, BindFlags.IndexBuffer, indices),
                IndexCount = indices.Length,
                AlphaMode = GeomAlphaMode.Opaque,
                DoubleSided = true,
                ShaderName = "portal_occluder",
                NeverDraw = true,
            };
            var min = pos[0]; var max = pos[0];
            for (int i = 1; i < n; i++) { min = Vector3.Min(min, pos[i]); max = Vector3.Max(max, pos[i]); }
            mesh.WorldBounds = new BoundingBox(min, max);
            mesh.WorldSphere = BoundingSphere.FromBox(mesh.WorldBounds);
            return mesh;
        }
    }
}

