using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class WorldArea
    {
        public string Name = "Area";
        public readonly List<Vector3> Corners = new List<Vector3>();
        public float ZMin = -2.0f, ZMax = 50.0f;
        public bool Visible = true;

        public int Count => Corners.Count;
        public bool IsValid => Corners.Count >= 3;

        public float BaseZ
        {
            get
            {
                float z = float.MaxValue;
                foreach (var c in Corners) z = Math.Min(z, c.Z);
                return z == float.MaxValue ? 0.0f : z;
            }
        }
        public float BottomZ => BaseZ + ZMin;
        public float TopZ => BaseZ + ZMax;

        public Vector3 Centre
        {
            get
            {
                if (Corners.Count == 0) return Vector3.Zero;
                var s = Vector3.Zero;
                foreach (var c in Corners) s += c;
                return s / Corners.Count;
            }
        }

        public BoundingBox Bounds
        {
            get
            {
                if (Corners.Count == 0) return new BoundingBox(Vector3.Zero, Vector3.Zero);
                var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                foreach (var c in Corners) { mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c); }
                mn.Z = BottomZ; mx.Z = TopZ;
                return new BoundingBox(mn, mx);
            }
        }

        public bool ContainsXY(float x, float y)
        {
            int n = Corners.Count;
            if (n < 3) return false;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = Corners[i]; var b = Corners[j];
                if ((a.Y > y) != (b.Y > y))
                {
                    float xi = (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X;
                    if (x < xi) inside = !inside;
                }
            }
            return inside;
        }

        public bool Contains(Vector3 p) => p.Z >= BottomZ && p.Z <= TopZ && ContainsXY(p.X, p.Y);

        public float Size
        {
            get
            {
                var b = Bounds;
                return Math.Max(b.Maximum.X - b.Minimum.X, b.Maximum.Y - b.Minimum.Y);
            }
        }

        public static WorldArea FromBox(BoundingBox b, float margin, string name)
        {
            var a = new WorldArea { Name = name };
            float z = b.Minimum.Z;
            a.Corners.Add(new Vector3(b.Minimum.X - margin, b.Minimum.Y - margin, z));
            a.Corners.Add(new Vector3(b.Maximum.X + margin, b.Minimum.Y - margin, z));
            a.Corners.Add(new Vector3(b.Maximum.X + margin, b.Maximum.Y + margin, z));
            a.Corners.Add(new Vector3(b.Minimum.X - margin, b.Maximum.Y + margin, z));
            a.ZMin = -1.0f;
            a.ZMax = Math.Max(2.0f, b.Maximum.Z - b.Minimum.Z + 2.0f);
            return a;
        }

        public WorldArea Clone()
        {
            var a = new WorldArea { Name = Name, ZMin = ZMin, ZMax = ZMax, Visible = Visible };
            a.Corners.AddRange(Corners);
            return a;
        }

        public List<int> Triangulate()
        {
            var tris = new List<int>();
            int n = Corners.Count;
            if (n < 3) return tris;
            var idx = new List<int>();
            double area2 = 0;
            for (int i = 0, j = n - 1; i < n; j = i++) area2 += (double)Corners[j].X * Corners[i].Y - (double)Corners[i].X * Corners[j].Y;
            if (area2 >= 0) for (int i = 0; i < n; i++) idx.Add(i);
            else for (int i = n - 1; i >= 0; i--) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4 * n)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    var a = Corners[i0]; var b = Corners[i1]; var c = Corners[i2];
                    float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (cross <= 1e-6f) continue;
                    bool any = false;
                    foreach (int k in idx)
                    {
                        if (k == i0 || k == i1 || k == i2) continue;
                        if (PointInTri(Corners[k], a, b, c)) { any = true; break; }
                    }
                    if (any) continue;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            else if (idx.Count > 3)
            {
                tris.Clear();
                for (int i = 1; i + 1 < n; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            }
            return tris;

            static bool PointInTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
                float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
                float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }
        }
    }

    public class AreaEntry
    {
        public YmapEntityDef Entity;
        public string Name;
        public Vector3 Position;
        public string Source;
        public string LodLevel;
        public float Radius;
        public bool Interior;
        public bool IsMloInstance;
        public override string ToString() => $"{Name} ({Position.X:0.##}, {Position.Y:0.##}, {Position.Z:0.##}) {Source}";
    }

    public sealed class AreaCornerTarget : IWorldGizmoTarget
    {
        public readonly WorldArea Area;
        public readonly int Index;
        public readonly object KeyObject;
        public AreaCornerTarget(WorldArea area, int index, object key) { Area = area; Index = index; KeyObject = key; }
        public object Key => KeyObject;
        public Vector3 Position => Index >= 0 && Index < Area.Corners.Count ? Area.Corners[Index] : Vector3.Zero;
        public Quaternion Orientation => Quaternion.Identity;
        public Vector3 Scale => Vector3.One;
        public WorldWidgetAxis RotationAxes => WorldWidgetAxis.None;
        public bool ScaleLockXY => true;
        public bool CanScale => false;
        public void SetPosition(Vector3 p) { if (Index >= 0 && Index < Area.Corners.Count) Area.Corners[Index] = p; }
        public void SetOrientation(Quaternion q) { }
        public void SetScale(Vector3 s) { }
        public override bool Equals(object obj) => obj is AreaCornerTarget o && ReferenceEquals(o.KeyObject, KeyObject);
        public override int GetHashCode() => KeyObject?.GetHashCode() ?? 0;
    }

    public partial class AreaToolState
    {
        public readonly List<WorldArea> Areas = new List<WorldArea>();
        public int Selected = -1;
        public WorldArea Current => Selected >= 0 && Selected < Areas.Count ? Areas[Selected] : null;

        public bool Drawing;
        public readonly List<Vector3> Draft = new List<Vector3>();
        public Vector3? DraftHover;
        public float DraftZMin = -2.0f, DraftZMax = 50.0f;

        public int SelectedCorner = -1;
        private readonly Dictionary<(WorldArea, int), object> cornerKeys = new Dictionary<(WorldArea, int), object>();
        public AreaCornerTarget CornerTarget(WorldArea a, int i)
        {
            if (a == null || i < 0 || i >= a.Corners.Count) return null;
            if (!cornerKeys.TryGetValue((a, i), out var k)) { k = new object(); cornerKeys[(a, i)] = k; }
            return new AreaCornerTarget(a, i, k);
        }

        public bool IncludeLod;
        public bool IncludeInterior;
        public bool IncludeMloInstances;
        public string NameFilter = "";
        public string ArchetypeList = "";
        public string ExcludeYmaps = "";
        public float MaxRadius = 25.0f;
        public bool UseBoundsCentre = true;

        public readonly List<AreaEntry> Contents = new List<AreaEntry>();
        public int ContentsInteriorSkipped;
        public double ContentsAt;
        public bool Highlight = true;
        public string Status = "";
        public int LastDirtyYmaps;
        public int SelectedEntry = -1;

        public Vector3 MoveOffset = new Vector3(0, 0, -100.0f);
        public float BoxMargin = 1.0f;

        public bool RequestStartDraw, RequestFinishDraw, RequestCancelDraw, RequestUndoCorner;
        public bool RequestBoxFromSelection;
        public bool RequestRefresh;
        public bool RequestDelete, RequestMove, RequestSaveList, RequestFrameArea, RequestUndo;
        public bool RequestNewEmpty, RequestRemoveArea, RequestDuplicateArea;
        public int RequestSelectEntry = -1, RequestFrameEntry = -1;
        public int RequestSelectCorner = -2;
        public bool RequestSaveAreas, RequestLoadAreas;
        public bool RequestRefreshGrass_R2, RequestDeleteGrassOnly_R2;
        public bool RequestShowTab;
        public bool TabActive;

        public bool AreasDirty;
        public string AreasPath;
        public bool HasProjectFile;

        public string NewName()
        {
            for (int i = Areas.Count + 1; ; i++)
            {
                string n = "Area " + i;
                bool taken = false;
                foreach (var a in Areas) if (string.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase)) { taken = true; break; }
                if (!taken) return n;
            }
        }

        public WorldArea Add(WorldArea a)
        {
            if (a == null) return null;
            if (string.IsNullOrWhiteSpace(a.Name)) a.Name = NewName();
            Areas.Add(a);
            Selected = Areas.Count - 1;
            SelectedCorner = -1;
            AreasDirty = true;
            return a;
        }

        public void Remove(int index)
        {
            if (index < 0 || index >= Areas.Count) return;
            Areas.RemoveAt(index);
            if (Selected >= Areas.Count) Selected = Areas.Count - 1;
            SelectedCorner = -1;
            Contents.Clear();
            AreasDirty = true;
        }

        private static readonly char[] ListSeps = { ',', ';', ' ', '\t', '\n', '\r' };
        private string[] archList = Array.Empty<string>(), exclList = Array.Empty<string>();
        private string archListSrc = "", exclListSrc = "";

        private void SyncLists()
        {
            if (!ReferenceEquals(archListSrc, ArchetypeList) && archListSrc != ArchetypeList)
            { archListSrc = ArchetypeList; archList = (ArchetypeList ?? "").Split(ListSeps, StringSplitOptions.RemoveEmptyEntries); }
            if (!ReferenceEquals(exclListSrc, ExcludeYmaps) && exclListSrc != ExcludeYmaps)
            { exclListSrc = ExcludeYmaps; exclList = (ExcludeYmaps ?? "").Split(ListSeps, StringSplitOptions.RemoveEmptyEntries); }
        }

        public bool YmapExcluded(string ymapName)
        {
            SyncLists();
            if (exclList.Length == 0 || string.IsNullOrEmpty(ymapName)) return false;
            foreach (var x in exclList) if (ymapName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public bool Passes(WorldArea area, YmapEntityDef e, bool interior)
        {
            if (e == null || area == null) return false;
            var lvl = e._CEntityDef.lodLevel;
            bool hd = lvl == rage__eLodType.LODTYPES_DEPTH_HD || lvl == rage__eLodType.LODTYPES_DEPTH_ORPHANHD;
            if (!hd && !IncludeLod) return false;
            if (e.MloInstance != null && !IncludeMloInstances) return false;
            if (interior && !IncludeInterior) return false;
            var arch = e.Archetype;
            string name = arch?.Name ?? e._CEntityDef.archetypeName.ToString();
            SyncLists();
            if (!string.IsNullOrEmpty(NameFilter) && (name ?? "").IndexOf(NameFilter, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (archList.Length > 0)
            {
                bool any = false;
                foreach (var a in archList) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) { any = true; break; }
                if (!any) return false;
            }
            float r = arch != null ? arch.BSRadius * Math.Max(Math.Max(Math.Abs(e.Scale.X), Math.Abs(e.Scale.Y)), Math.Abs(e.Scale.Z)) : 0.0f;
            if (MaxRadius > 0.0f && e.MloInstance == null && r > MaxRadius) return false;
            if (area.Contains(e.Position)) return true;
            if (UseBoundsCentre && arch != null)
            {
                var c = (e.BBMin + e.BBMax) * 0.5f;
                if (area.Contains(c)) return true;
            }
            return false;
        }

        public static string LevelName(YmapEntityDef e) => e._CEntityDef.lodLevel.ToString().Replace("LODTYPES_DEPTH_", "");

        public static string SidePathFor(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return null;
            var dir = Path.GetDirectoryName(projectPath) ?? "";
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(projectPath) + ".areas.xml");
        }

        public static void Save(string path, IList<WorldArea> areas)
        {
            var doc = new XmlDocument();
            var root = doc.CreateElement("RageWorldAreas");
            root.SetAttribute("version", "1");
            doc.AppendChild(root);
            foreach (var a in areas)
            {
                var an = doc.CreateElement("Area");
                an.SetAttribute("name", a.Name ?? "");
                an.SetAttribute("zmin", F(a.ZMin));
                an.SetAttribute("zmax", F(a.ZMax));
                an.SetAttribute("visible", a.Visible ? "true" : "false");
                foreach (var c in a.Corners)
                {
                    var cn = doc.CreateElement("Corner");
                    cn.SetAttribute("x", F(c.X)); cn.SetAttribute("y", F(c.Y)); cn.SetAttribute("z", F(c.Z));
                    an.AppendChild(cn);
                }
                root.AppendChild(an);
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            doc.Save(path);
        }

        public static List<WorldArea> Load(string path)
        {
            var list = new List<WorldArea>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            if (root == null) return list;
            foreach (XmlNode an in root.SelectNodes("Area"))
            {
                var a = new WorldArea
                {
                    Name = an.Attributes?["name"]?.Value ?? "Area",
                    ZMin = P(an.Attributes?["zmin"]?.Value, -2.0f),
                    ZMax = P(an.Attributes?["zmax"]?.Value, 50.0f),
                    Visible = !string.Equals(an.Attributes?["visible"]?.Value, "false", StringComparison.OrdinalIgnoreCase),
                };
                foreach (XmlNode cn in an.SelectNodes("Corner"))
                    a.Corners.Add(new Vector3(P(cn.Attributes?["x"]?.Value, 0), P(cn.Attributes?["y"]?.Value, 0), P(cn.Attributes?["z"]?.Value, 0)));
                list.Add(a);
            }
            return list;
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static float P(string s, float def) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        public static string ContentsText(WorldArea area, IList<AreaEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("# Area: ").Append(area?.Name ?? "?").Append("  corners: ");
            if (area != null) foreach (var c in area.Corners) sb.Append($"({c.X:0.##}, {c.Y:0.##}, {c.Z:0.##}) ");
            if (area != null) sb.Append($" z {area.ZMin:0.#}..{area.ZMax:0.#} m from {area.BaseZ:0.##}");
            sb.AppendLine();
            sb.AppendLine($"# {entries.Count} entities  ({DateTime.Now:yyyy-MM-dd HH:mm})");
            sb.AppendLine("archetype\tx\ty\tz\tsource\tlod\tradius");
            foreach (var e in entries)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}\t{1:0.###}\t{2:0.###}\t{3:0.###}\t{4}\t{5}\t{6:0.##}",
                                            e.Name, e.Position.X, e.Position.Y, e.Position.Z, e.Source, e.LodLevel, e.Radius));
            return sb.ToString();
        }

        public static WorldArea ParseEnv(string spec, out float? zmin, out float? zmax)
        {
            zmin = zmax = null;
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var parts = spec.Split(';');
            var pts = new List<Vector2>();
            foreach (var p in parts)
            {
                var xy = p.Split(',');
                if (xy.Length < 2) continue;
                if (!float.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                pts.Add(new Vector2(x, y));
            }
            if (pts.Count >= 4)
            {
                var last = pts[pts.Count - 1];
                var c = Vector2.Zero;
                for (int i = 0; i < pts.Count - 1; i++) c += pts[i];
                c /= pts.Count - 1;
                if (last.X < last.Y && Math.Abs(last.X) < 500 && Math.Abs(last.Y) < 2000 && (last - c).Length() > 100.0f)
                {
                    zmin = last.X; zmax = last.Y; pts.RemoveAt(pts.Count - 1);
                }
            }
            if (pts.Count < 3) return null;
            var a = new WorldArea { Name = "RLE_AREA" };
            foreach (var p in pts) a.Corners.Add(new Vector3(p.X, p.Y, 0));
            if (zmin.HasValue) a.ZMin = zmin.Value;
            if (zmax.HasValue) a.ZMax = zmax.Value;
            return a;
        }

        public static int SelfTest()
        {
            try
            {
                var a = new WorldArea { Name = "T", ZMin = -2, ZMax = 10 };
                a.Corners.Add(new Vector3(0, 0, 5)); a.Corners.Add(new Vector3(10, 0, 5.5f)); a.Corners.Add(new Vector3(10, 10, 6)); a.Corners.Add(new Vector3(0, 10, 5));
                if (!a.ContainsXY(5, 5)) throw new Exception("centre not inside");
                if (a.ContainsXY(15, 5)) throw new Exception("outside counted inside");
                if (!a.Contains(new Vector3(5, 5, 4))) throw new Exception("z within range rejected (base 5, -2)");
                if (a.Contains(new Vector3(5, 5, 2))) throw new Exception("z under range accepted");
                if (a.Contains(new Vector3(5, 5, 16))) throw new Exception("z over range accepted");
                var l = new WorldArea();
                l.Corners.Add(new Vector3(0, 0, 0)); l.Corners.Add(new Vector3(10, 0, 0)); l.Corners.Add(new Vector3(10, 4, 0));
                l.Corners.Add(new Vector3(4, 4, 0)); l.Corners.Add(new Vector3(4, 10, 0)); l.Corners.Add(new Vector3(0, 10, 0));
                if (l.ContainsXY(8, 8)) throw new Exception("L notch counted inside");
                if (!l.ContainsXY(2, 8) || !l.ContainsXY(8, 2)) throw new Exception("L arms not inside");
                var tris = l.Triangulate();
                if (tris.Count != (l.Count - 2) * 3) throw new Exception($"L triangulation gave {tris.Count / 3} triangles, wanted {l.Count - 2}");
                for (int i = 0; i < tris.Count; i += 3)
                {
                    var p0 = l.Corners[tris[i]]; var p1 = l.Corners[tris[i + 1]]; var p2 = l.Corners[tris[i + 2]];
                    var t = new WorldArea(); t.Corners.Add(p0); t.Corners.Add(p1); t.Corners.Add(p2);
                    if (t.ContainsXY(8, 8)) throw new Exception("a cap triangle covers the L notch");
                }
                var env = ParseEnv("440,-980;450,-980;450,-970;440,-970;-3,20", out var zn, out var zx);
                if (env == null || env.Count != 4 || zn != -3 || zx != 20) throw new Exception("RLE_AREA spec did not parse (with range)");
                var env2 = ParseEnv("0,0;10,0;10,10;0,10", out zn, out zx);
                if (env2 == null || env2.Count != 4 || zn.HasValue) throw new Exception("RLE_AREA spec did not parse (no range)");
                var tmp = Path.Combine(Path.GetTempPath(), "rle_areas_test", "t.areas.xml");
                Save(tmp, new[] { a, l });
                var back = Load(tmp);
                if (back.Count != 2 || back[0].Count != 4 || back[1].Count != 6 || Math.Abs(back[0].ZMax - 10) > 0.001f || back[0].Name != "T")
                    throw new Exception("side file round trip lost data");
                if ((back[0].Corners[2] - a.Corners[2]).Length() > 0.001f) throw new Exception("side file corner drifted");
                try { Directory.Delete(Path.GetDirectoryName(tmp), true); } catch { }
                Console.WriteLine("AREA TOOL PASSED (polygon, triangulation, spec, side file)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("AREA TOOL FAILED: " + ex.Message);
                return 1;
            }
        }
    }
}

