using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLightEditor.Rendering;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class MloCreatorSession
    {
        public static string SafeInteriorName_R1(string name, MloArchetype mlo)
        {
            bool bad = string.IsNullOrWhiteSpace(name) ||
                       name.StartsWith("hash_", StringComparison.OrdinalIgnoreCase) ||
                       name.All(char.IsDigit);
            if (!bad) return name;
            var file = mlo?.Ytyp?.Name;
            if (!string.IsNullOrWhiteSpace(file))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(stem) && !stem.All(char.IsDigit)) return stem.ToLowerInvariant();
            }
            return "my_interior";
        }

        public void NameFromImport_R1(Scene scene, MloArchetype mlo)
        {
            if (!string.IsNullOrWhiteSpace(Name) && Name != "my_interior") return;
            var ytyps = scene?.MloInfo?.Ytyps;
            if (ytyps == null) return;
            var info = ytyps.FirstOrDefault(y => y?.Archetypes != null && y.Archetypes.Any(a => ReferenceEquals(a, mlo)))
                       ?? ytyps.FirstOrDefault(y => !string.IsNullOrEmpty(y?.Path) || !string.IsNullOrEmpty(y?.Name));
            var file = info?.Path ?? info?.Name;
            if (string.IsNullOrWhiteSpace(file)) return;
            var stem = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(stem) || stem.All(char.IsDigit)) return;
            Name = stem.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ShellName) || ShellName.All(char.IsDigit)) ShellName = Name;
            if (string.IsNullOrWhiteSpace(TextureDictionary) || TextureDictionary.All(char.IsDigit)) TextureDictionary = Name;
        }

        public string ShellBuildReport_R1 = "";
        public int ShellBuiltRooms_R1, ShellBuiltPortals_R1;

        public static List<MloShellAnalysis.Tri> ShellTriangles_R1(Scene scene, LoadedFile shellFile, out string source)
        {
            source = "";
            var tris = new List<MloShellAnalysis.Tri>();
            if (shellFile?.Model != null)
            {
                AddMeshes_R1(tris, shellFile.Model.Meshes);
                if (tris.Count > 0) { source = "shell " + shellFile.Name; return tris; }
            }
            if (scene?.MloModel != null)
            {
                AddMeshes_R1(tris, scene.MloModel.Meshes.Where(m => m != null && m.IsMloShell));
                if (tris.Count > 0) { source = "the imported interior's shell"; return tris; }
            }
            var big = scene?.Files.Where(f => f != null && !f.FromMlo && f.Model != null)
                .OrderByDescending(f => (ModelBounds(f).Maximum - ModelBounds(f).Minimum).LengthSquared()).FirstOrDefault();
            if (big?.Model != null)
            {
                AddMeshes_R1(tris, big.Model.Meshes);
                if (tris.Count > 0) { source = "the biggest model loaded (" + big.Name + ")"; return tris; }
            }
            var named = scene?.Files.Where(f => f?.Model != null && (f.Name ?? "").IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(f => (ModelBounds(f).Maximum - ModelBounds(f).Minimum).LengthSquared()).FirstOrDefault();
            if (named?.Model != null)
            {
                AddMeshes_R1(tris, named.Model.Meshes);
                if (tris.Count > 0) { source = "the imported prop named like a shell (" + named.Name + ")"; return tris; }
            }
            var any = scene?.Files.Where(f => f?.Model != null)
                .OrderByDescending(f => (ModelBounds(f).Maximum - ModelBounds(f).Minimum).LengthSquared()).FirstOrDefault();
            if (any?.Model != null)
            {
                AddMeshes_R1(tris, any.Model.Meshes);
                if (tris.Count > 0) { source = "the biggest placed model (" + any.Name + ")"; return tris; }
            }
            return tris;
        }

        private static void AddMeshes_R1(List<MloShellAnalysis.Tri> tris, IEnumerable<RenderMesh> meshes)
        {
            if (meshes == null) return;
            foreach (var m in meshes)
            {
                if (m == null || m.PickVerts == null || m.PickIndices == null) continue;
                if (m.NeverDraw) continue;
                var t = m.Transform;
                var v = m.PickVerts; var ix = m.PickIndices;
                var world = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) world[i] = Vector3.TransformCoordinate(v[i], t);
                for (int i = 0; i + 2 < ix.Length; i += 3)
                    tris.Add(new MloShellAnalysis.Tri { A = world[ix[i]], B = world[ix[i + 1]], C = world[ix[i + 2]] });
            }
        }

        public string ApplyShellAnalysis_R1(MloShellAnalysis a, BoundingBox shellBounds, string source)
        {
            if (a == null) return "nothing to apply";
            if (a.Rooms.Count == 0)
            {
                ShellBuildReport_R1 = a.Describe();
                return a.Describe();
            }

            int oldRooms = Math.Max(Rooms.Count - 1, 0), oldPortals = Portals.Count;

            Rooms.Clear();
            Rooms.Add(new MloCreatorRoom { Name = "limbo", Min = shellBounds.Minimum, Max = shellBounds.Maximum });
            LimboAuthored_P2 = false;
            Portals.Clear();

            var order = a.Rooms
                .Select((r, i) => new { r, i })
                .OrderBy(x => (int)Math.Round(x.r.Box.Minimum.Z / 2.5f))
                .ThenByDescending(x => x.r.Volume)
                .ToList();
            var remap = new int[a.Rooms.Count + 1];
            float floorRef = order.Count > 0 ? order[0].r.Box.Minimum.Z : 0;
            for (int k = 0; k < order.Count; k++)
            {
                var src = order[k].r;
                int floor = (int)Math.Round((src.Box.Minimum.Z - floorRef) / 2.6f);
                var room = new MloCreatorRoom
                {
                    Name = "room_" + (k + 1),
                    Min = Vector3.Max(src.Box.Minimum, a.Bounds.Minimum),
                    Max = Vector3.Min(src.Box.Maximum, a.Bounds.Maximum),
                    FloorId = Math.Max(floor, 0),
                };
                NormaliseRoom(room);
                Rooms.Add(room);
                remap[order[k].i + 1] = Rooms.Count - 1;
            }

            int made = 0, outside = 0;
            foreach (var p in a.Portals)
            {
                int from = p.RoomA >= 1 && p.RoomA <= a.Rooms.Count ? remap[p.RoomA] : 0;
                int to = p.RoomB >= 1 && p.RoomB <= a.Rooms.Count ? remap[p.RoomB] : 0;
                if (from == to) continue;
                if (from == 0 && to == 0) continue;
                var portal = AddPortal(from, to, p.Corners);
                OrientPortal(portal);
                made++;
                if (p.ToOutside) outside++;
            }

            AutoAssignRooms();
            NormaliseBoxes_P2();
            ShellBuiltRooms_R1 = Rooms.Count - 1;
            ShellBuiltPortals_R1 = made;

            var sb = new System.Text.StringBuilder();
            sb.Append($"Built {Rooms.Count - 1} room{(Rooms.Count - 1 == 1 ? "" : "s")} and {made} portal{(made == 1 ? "" : "s")} ");
            sb.Append($"({made - outside} between rooms, {outside} out to limbo) from {source}. ");
            sb.Append($"{a.TriangleCount:n0} triangles at {a.VoxelSize:0.00} m in {a.Seconds:0.0}s. ");
            if (oldRooms > 0 || oldPortals > 0) sb.Append($"Replaced {oldRooms} room(s) and {oldPortals} portal(s) - Ctrl+Z puts them back. ");
            float shellVol = Math.Max((shellBounds.Maximum.X - shellBounds.Minimum.X) *
                                      (shellBounds.Maximum.Y - shellBounds.Minimum.Y) *
                                      (shellBounds.Maximum.Z - shellBounds.Minimum.Z), 0.001f);
            int biggest = 1; float biggestVol = 0;
            for (int i = 1; i < Rooms.Count; i++) if (Rooms[i].Volume > biggestVol) { biggestVol = Rooms[i].Volume; biggest = i; }
            if (Rooms.Count > 1 && biggestVol > shellVol * 0.5f)
                sb.Append($"NOTE: '{Rooms[biggest].Name}' is {100.0f * biggestVol / shellVol:0}% of the whole shell - that part of the interior is open enough that the geometry does not divide it. " +
                          "Raise 'Widest doorway' and build again to split it at its archways, or size it by hand. ");
            sb.Append("CHECK: room boxes are axis-aligned, so an L-shaped space became one box; drag the corners or use Vertex snap to correct it, and delete any portal that is not really a door.");
            ShellBuildReport_R1 = sb.ToString();
            return ShellBuildReport_R1;
        }
    }
}

