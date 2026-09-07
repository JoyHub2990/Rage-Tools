using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public class ExtensionSource_V68
    {
        public string Path;
        public string Name;
        public YtypFile Ytyp;
        public YmapFile Ymap;
        public bool FromGame;
        public bool ReadOnly => FromGame || Ytyp == null;
        public string Kind => Ytyp != null ? "ytyp" : Ymap != null ? "ymap" : "model";
    }

    public class ExtensionTarget_V68
    {
        public Archetype Archetype;
        public ExtensionSource_V68 Source;
        public string Name => Archetype?.Name ?? Archetype?.Hash.ToString() ?? "?";
        public bool Editable => Source != null && !Source.ReadOnly;
        public Vector3 Placement = Vector3.Zero;
        public Quaternion Orientation = Quaternion.Identity;
    }

    public class ExtensionProp_V68
    {
        public uint ArchHash;
        public string Name => new MetaHash(ArchHash).ToString();
        public Vector3 Position;
        public Quaternion Orientation = Quaternion.Identity;
        public string From;
        public bool Interior;
    }

    public partial class ExtensionWorkspace_V68
    {
        public readonly List<ExtensionProp_V68> Props = new List<ExtensionProp_V68>();
        public int SelectedProp = -1;
        public string PropFilter = "";
        public readonly List<ExtensionSource_V68> Sources = new List<ExtensionSource_V68>();
        public readonly List<ExtensionTarget_V68> Targets = new List<ExtensionTarget_V68>();
        public int Selected = -1;
        public int SelectedExtension = -1;
        public string Status = "";
        public bool StatusIsProblem;
        public string Filter = "";

        public ExtensionTarget_V68 Current =>
            Selected >= 0 && Selected < Targets.Count ? Targets[Selected] : null;

        public Archetype CurrentArchetype => Current?.Archetype;

        public void Say(string s, bool problem = false) { Status = s; StatusIsProblem = problem; }

        public ExtensionSource_V68 AddYtyp(string path)
        {
            var ytyp = new YtypFile();
            ytyp.Load(File.ReadAllBytes(path));
            ytyp.FilePath = path;
            ytyp.RpfFileEntry ??= new RpfResourceFileEntry();
            ytyp.RpfFileEntry.Name = System.IO.Path.GetFileName(path);
            ytyp.Name = ytyp.RpfFileEntry.Name;
            ytyp.Loaded = true;
            var src = new ExtensionSource_V68
            {
                Path = path,
                Name = System.IO.Path.GetFileName(path),
                Ytyp = ytyp,
            };
            Sources.Add(src);
            int before = Targets.Count;
            int props = 0;
            foreach (var a in ytyp.AllArchetypes ?? Array.Empty<Archetype>())
            {
                if (a == null) continue;
                Targets.Add(new ExtensionTarget_V68 { Archetype = a, Source = src });
                props += AddInteriorProps_V68(a, src.Name);
            }
            if (Selected < 0 && Targets.Count > before) Selected = before;
            Say(props > 0
                ? $"{src.Name}: {Targets.Count - before} archetype(s), {props} prop(s) inside"
                : $"{src.Name}: {Targets.Count - before} archetype(s)");
            return src;
        }

        public ExtensionSource_V68 AddYmap(string path, Func<uint, Archetype> lookup)
        {
            var ymap = new YmapFile();
            ymap.Load(File.ReadAllBytes(path));
            ymap.FilePath = path;
            ymap.RpfFileEntry ??= new RpfResourceFileEntry();
            ymap.RpfFileEntry.Name = System.IO.Path.GetFileName(path);
            ymap.Name = ymap.RpfFileEntry.Name;
            ymap.Loaded = true;
            var src = new ExtensionSource_V68
            {
                Path = path,
                Name = System.IO.Path.GetFileName(path),
                Ymap = ymap,
            };
            Sources.Add(src);

            int added = 0, missing = 0;
            var seen = new HashSet<uint>();
            foreach (var e in ymap.AllEntities ?? Array.Empty<YmapEntityDef>())
            {
                if (e == null) continue;
                Props.Add(new ExtensionProp_V68
                {
                    ArchHash = e._CEntityDef.archetypeName.Hash,
                    Position = e.Position,
                    Orientation = e.Orientation,
                    From = src.Name,
                });
                uint h = e._CEntityDef.archetypeName.Hash;
                if (!seen.Add(h)) continue;
                var existing = Targets.FirstOrDefault(t => t.Archetype != null && t.Archetype.Hash == h);
                if (existing != null)
                {
                    existing.Placement = e.Position;
                    existing.Orientation = e.Orientation;
                    continue;
                }
                var arch = lookup?.Invoke(h);
                if (arch == null) { missing++; continue; }
                Targets.Add(new ExtensionTarget_V68
                {
                    Archetype = arch,
                    Source = new ExtensionSource_V68 { Name = "game archetype", FromGame = true },
                    Placement = e.Position,
                    Orientation = e.Orientation,
                });
                AddInteriorProps_V68(arch, src.Name);
                added++;
            }
            Say(missing == 0
                ? $"{src.Name}: {added} archetype(s) from its entities"
                : $"{src.Name}: {added} archetype(s); {missing} are not in the game or the loaded ytyps",
                missing > 0);
            return src;
        }

        public int AddInteriorProps_V68(Archetype a, string from)
        {
            if (!(a is MloArchetype mlo)) return 0;
            int n = 0;
            foreach (var e in mlo.entities ?? Array.Empty<MCEntityDef>())
            {
                if (e == null) continue;
                var h = e._Data.archetypeName;
                Props.Add(new ExtensionProp_V68
                {
                    ArchHash = h.Hash,
                    Position = e._Data.position,
                    Orientation = new Quaternion(e._Data.rotation.X, e._Data.rotation.Y, e._Data.rotation.Z, e._Data.rotation.W),
                    From = (a.Name ?? from) + " (interior)",
                    Interior = true,
                });
                n++;
            }
            return n;
        }

        public ExtensionTarget_V68 AddGameArchetype(Archetype a)
        {
            if (a == null) return null;
            var found = Targets.FirstOrDefault(t => t.Archetype != null && t.Archetype.Hash == a.Hash);
            if (found != null) { Selected = Targets.IndexOf(found); return found; }
            var t2 = new ExtensionTarget_V68
            {
                Archetype = a,
                Source = new ExtensionSource_V68 { Name = "game archetype", FromGame = true },
            };
            Targets.Add(t2);
            Selected = Targets.Count - 1;
            return t2;
        }

        public ExtensionTarget_V68 NewArchetypeFor(string modelName, Vector3 bbMin, Vector3 bbMax)
        {
            var name = (modelName ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name)) return null;
            var ytyp = new YtypFile();
            var file = name + "_extensions.ytyp";
            ytyp.RpfFileEntry = new RpfResourceFileEntry { Name = file };
            ytyp.Name = file;
            ytyp.NameHash = JenkHash.GenHash(System.IO.Path.GetFileNameWithoutExtension(file));
            ytyp._CMapTypes.name = ytyp.NameHash;
            ytyp.AllArchetypes = Array.Empty<Archetype>();
            ytyp.Loaded = true;

            JenkIndex.Ensure(name);
            var arch = ytyp.AddArchetype();
            var bd = arch._BaseArchetypeDef;
            bd.name = new MetaHash(JenkHash.GenHash(name));
            bd.assetName = bd.name;
            bd.assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE;
            bd.flags = 32;
            bd.lodDist = 200.0f;
            bd.hdTextureDist = 60.0f;
            bd.bbMin = bbMin; bd.bbMax = bbMax;
            bd.bsCentre = (bbMin + bbMax) * 0.5f;
            bd.bsRadius = (bbMax - bbMin).Length() * 0.5f;
            arch.Init(ytyp, ref bd);
            arch._BaseArchetypeDef = bd;
            arch.Extensions = Array.Empty<MetaWrapper>();

            var src = new ExtensionSource_V68 { Name = file, Ytyp = ytyp };
            Sources.Add(src);
            var t = new ExtensionTarget_V68 { Archetype = arch, Source = src };
            Targets.Add(t);
            Selected = Targets.Count - 1;
            Say($"new archetype {name} - add extensions, then Save .ytyp");
            return t;
        }

        public string SaveCurrentYtyp(string path)
        {
            var src = Current?.Source;
            if (src?.Ytyp == null) return "this archetype came from the game, not from a .ytyp you can save";
            var ytyp = src.Ytyp;
            var err = MloEditor.Validate(ytyp);
            if (!string.IsNullOrEmpty(err)) return err;
            if (string.IsNullOrWhiteSpace(path)) path = src.Path;
            if (string.IsNullOrWhiteSpace(path)) return "no path";
            ytyp.HasChanged = true;
            File.WriteAllBytes(path, ytyp.Save());
            src.Path = path;
            src.Name = System.IO.Path.GetFileName(path);
            ytyp.FilePath = path;
            return null;
        }

        public IEnumerable<ExtensionProp_V68> VisibleProps()
        {
            if (string.IsNullOrWhiteSpace(PropFilter)) return Props;
            return Props.Where(p => (p.Name ?? "").IndexOf(PropFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public ExtensionTarget_V68 PickProp(int index, Func<uint, Archetype> lookup)
        {
            if (index < 0 || index >= Props.Count) return null;
            SelectedProp = index;
            var p = Props[index];
            var found = Targets.FirstOrDefault(t => t.Archetype != null && t.Archetype.Hash == p.ArchHash);
            if (found == null)
            {
                var arch = lookup?.Invoke(p.ArchHash);
                if (arch == null)
                {
                    Say($"{p.Name}: its archetype is not in the game or in any .ytyp you have opened", true);
                    return null;
                }
                found = AddGameArchetype(arch);
            }
            Selected = Targets.IndexOf(found);
            SelectedExtension = -1;
            CancelSnap();
            Say($"{p.Name} - {ArchetypeExtensions_V62.Get(found.Archetype).Length} extension(s) on its archetype");
            return found;
        }

        public IEnumerable<ExtensionTarget_V68> Visible()
        {
            if (string.IsNullOrWhiteSpace(Filter)) return Targets;
            return Targets.Where(t => (t.Name ?? "").IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsPointField_V68(PropertyInfo p)
        {
            if (p == null || p.PropertyType != typeof(Vector3)) return false;
            var n = p.Name.ToLowerInvariant();
            return n.StartsWith("corner") || n == "bottom" || n == "top" ||
                   n == "offsetposition" || n == "position" || n == "normal" || n == "direction";
        }

        public static bool IsCornerField_V68(PropertyInfo p) =>
            p != null && p.PropertyType == typeof(Vector3) &&
            p.Name.StartsWith("corner", StringComparison.OrdinalIgnoreCase);

        public static List<ArchetypeExtensions_V62.Field> PointFields_V68(MetaWrapper w) =>
            ArchetypeExtensions_V62.Fields(w).Where(f => IsPointField_V68(f.Prop)).ToList();

        public static List<ArchetypeExtensions_V62.Field> CornerFields_V68(MetaWrapper w) =>
            ArchetypeExtensions_V62.Fields(w)
                .Where(f => IsCornerField_V68(f.Prop))
                .OrderBy(f => f.Prop.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public int SnapExtension = -1;
        public string SnapFieldName;
        public readonly List<string> SnapChain = new List<string>();
        public bool SnapArmed => SnapExtension >= 0 && !string.IsNullOrEmpty(SnapFieldName);

        public void ArmSnap(int extIndex, string fieldName)
        {
            SnapExtension = extIndex;
            SnapFieldName = fieldName;
            SnapChain.Clear();
        }

        public void ArmCornerChain(int extIndex, MetaWrapper w)
        {
            var corners = CornerFields_V68(w);
            if (corners.Count == 0) { CancelSnap(); return; }
            SnapExtension = extIndex;
            SnapChain.Clear();
            foreach (var c in corners) SnapChain.Add(c.Prop.Name);
            SnapFieldName = SnapChain[0];
            SnapChain.RemoveAt(0);
        }

        public void CancelSnap()
        {
            SnapExtension = -1;
            SnapFieldName = null;
            SnapChain.Clear();
        }

        public bool ApplySnap(Archetype arch, Vector3 localPoint)
        {
            if (arch == null || !SnapArmed) return false;
            var list = ArchetypeExtensions_V62.Get(arch);
            if (SnapExtension < 0 || SnapExtension >= list.Length) { CancelSnap(); return false; }
            var w = list[SnapExtension];
            var field = ArchetypeExtensions_V62.Fields(w)
                        .FirstOrDefault(f => f.Prop.Name == SnapFieldName);
            if (field == null) { CancelSnap(); return false; }
            ArchetypeExtensions_V62.SetValue(w, field, localPoint);
            if (SnapChain.Count > 0)
            {
                SnapFieldName = SnapChain[0];
                SnapChain.RemoveAt(0);
                Say($"{ArchetypeExtensions_V62.Spaced(field.Prop.Name)} set - now click {ArchetypeExtensions_V62.Spaced(SnapFieldName)}");
            }
            else
            {
                Say($"{ArchetypeExtensions_V62.Spaced(field.Prop.Name)} snapped to the vertex");
                CancelSnap();
            }
            return true;
        }

        public static int SelfTest_V68(Action<string, bool, string> check)
        {
            int fails = 0;
            void Chk(string what, bool ok, string d) { if (!ok) fails++; check(what, ok, d); }

            var ws = new ExtensionWorkspace_V68();
            var t = ws.NewArchetypeFor("rle_v68_prop", new Vector3(-2, -2, 0), new Vector3(2, 2, 3));
            Chk("v68 extensions: a loose model can be given an archetype to hang extensions on",
                t != null && ws.Targets.Count == 1 && t.Editable && t.Archetype?.Name == "rle_v68_prop",
                t?.Name ?? "none");
            if (t == null) return fails;

            var shaftType = ArchetypeExtensions_V62.Types.FirstOrDefault(x => x.Wrapper == "MCExtensionDefLightShaft");
            var ladderType = ArchetypeExtensions_V62.Types.FirstOrDefault(x => x.Wrapper == "MCExtensionDefLadder");
            if (shaftType == null || ladderType == null) { Chk("v68 extensions: shaft and ladder types", false, "missing"); return fails; }

            ArchetypeExtensions_V62.Add(t.Archetype, ArchetypeExtensions_V62.Create(shaftType, "v68_shaft"));
            ArchetypeExtensions_V62.Add(t.Archetype, ArchetypeExtensions_V62.Create(ladderType, "v68_ladder"));
            Chk("v68 extensions: they attach to it", ArchetypeExtensions_V62.Get(t.Archetype).Length == 2,
                ArchetypeExtensions_V62.Get(t.Archetype).Length + " on the archetype");

            var shaft = ArchetypeExtensions_V62.Get(t.Archetype)[0];
            var corners = CornerFields_V68(shaft);
            Chk("v68 extensions: a light shaft offers exactly its four corners to snap",
                corners.Count == 4 && corners[0].Prop.Name == "cornerA" && corners[3].Prop.Name == "cornerD",
                string.Join(", ", corners.Select(c => c.Prop.Name)));

            ws.ArmCornerChain(0, shaft);
            var pts = new[]
            {
                new Vector3(1, 0, 0), new Vector3(1, 0, 2),
                new Vector3(2, 0, 2), new Vector3(2, 0, 0),
            };
            bool chained = true;
            for (int i = 0; i < pts.Length; i++)
            {
                if (!ws.SnapArmed) { chained = false; break; }
                ws.ApplySnap(t.Archetype, pts[i]);
            }
            Chk("v68 extensions: snapping walks corner A to D and then disarms itself",
                chained && !ws.SnapArmed, chained ? "four clicks, then done" : "chain broke early");

            var back = ArchetypeExtensions_V62.Fields(shaft).Where(f => IsCornerField_V68(f.Prop))
                       .OrderBy(f => f.Prop.Name, StringComparer.OrdinalIgnoreCase)
                       .Select(f => (Vector3)ArchetypeExtensions_V62.GetValue(shaft, f)).ToArray();
            bool kept = back.Length == 4;
            for (int i = 0; kept && i < 4; i++) kept = (back[i] - pts[i]).Length() < 0.0001f;
            Chk("v68 extensions: every snapped corner is the vertex that was clicked", kept,
                kept ? string.Join(" ", back.Select(v => $"{v.X:0.#},{v.Y:0.#},{v.Z:0.#}")) : "corners do not match");

            var ladder = ArchetypeExtensions_V62.Get(t.Archetype)[1];
            var lpts = PointFields_V68(ladder);
            Chk("v68 extensions: a ladder offers its bottom and top to snap",
                lpts.Any(f => f.Prop.Name == "bottom") && lpts.Any(f => f.Prop.Name == "top"),
                string.Join(", ", lpts.Select(f => f.Prop.Name)));

            ws.ArmSnap(1, "top");
            ws.ApplySnap(t.Archetype, new Vector3(0, 0, 4));
            var topField = ArchetypeExtensions_V62.Fields(ladder).First(f => f.Prop.Name == "top");
            var topVal = (Vector3)ArchetypeExtensions_V62.GetValue(ladder, topField);
            Chk("v68 extensions: a single field snaps on its own and disarms",
                Math.Abs(topVal.Z - 4.0f) < 0.0001f && !ws.SnapArmed, topVal.ToString());

            var game = new Archetype();
            var gd = game._BaseArchetypeDef;
            gd.name = new MetaHash(JenkHash.GenHash("rle_v68_gameprop"));
            game.Init(null, ref gd);
            game._BaseArchetypeDef = gd;
            var g1 = ws.AddGameArchetype(game);
            var g2 = ws.AddGameArchetype(game);
            Chk("v68 extensions: a prop taken from the game or the World comes in once, already picked",
                g1 != null && ReferenceEquals(g1, g2) && ws.Targets.Count == 2 &&
                ws.Current == g1 && !g1.Editable,
                ws.Targets.Count + " targets, editable " + g1.Editable);
            ws.Selected = 0;

            var preset = ExtensionPresets_V70.All.First(x => x.Wrapper == "MCExtensionDefLightShaft");
            bool applied = ExtensionPresets_V70.Apply(shaft, preset);
            var lenF = ArchetypeExtensions_V62.Fields(shaft).First(f => f.Prop.Name == "length");
            var intF = ArchetypeExtensions_V62.Fields(shaft).First(f => f.Prop.Name == "intensity");
            var densF = ArchetypeExtensions_V62.Fields(shaft).First(f => f.Prop.Name == "densityType");
            var cornerAfter = (Vector3)ArchetypeExtensions_V62.GetValue(shaft,
                ArchetypeExtensions_V62.Fields(shaft).First(f => f.Prop.Name == "cornerA"));
            Chk("v69 shafts: a preset sets the look from the game's own numbers",
                applied &&
                Math.Abs((float)ArchetypeExtensions_V62.GetValue(shaft, lenF) - 1.68f) < 0.001f &&
                Math.Abs((float)ArchetypeExtensions_V62.GetValue(shaft, intF) - 10.0f) < 0.001f &&
                ArchetypeExtensions_V62.GetValue(shaft, densF).ToString() == "LIGHTSHAFT_DENSITYTYPE_QUADRATIC_GRADIENT",
                $"length {ArchetypeExtensions_V62.GetValue(shaft, lenF)}, " +
                $"intensity {ArchetypeExtensions_V62.GetValue(shaft, intF)}, " +
                ArchetypeExtensions_V62.GetValue(shaft, densF));

            Chk("v69 shafts: ...and leaves the corners you snapped alone",
                (cornerAfter - pts[0]).Length() < 0.0001f, cornerAfter.ToString());

            var dir = Path.Combine(Path.GetTempPath(), "rle_v68_ext");
            try { Directory.CreateDirectory(dir); } catch { }
            var outPath = Path.Combine(dir, "rle_v68_prop_extensions.ytyp");
            var err = ws.SaveCurrentYtyp(outPath);
            Chk("v68 extensions: the archetype saves as a .ytyp", err == null && File.Exists(outPath), err ?? outPath);
            if (err != null) return fails;

            var ws2 = new ExtensionWorkspace_V68();
            ws2.AddYtyp(outPath);
            var backArch = ws2.Targets.FirstOrDefault()?.Archetype;
            var backExts = ArchetypeExtensions_V62.Get(backArch);
            Chk("v68 extensions: ...and reads back with its extensions and their snapped corners",
                backExts.Length == 2 &&
                ArchetypeExtensions_V62.TypeLabel(backExts[0]) == "Light Shaft" &&
                Math.Abs(((Vector3)ArchetypeExtensions_V62.GetValue(backExts[0],
                    ArchetypeExtensions_V62.Fields(backExts[0]).First(f => f.Prop.Name == "cornerC"))).Z - 2.0f) < 0.001f,
                backExts.Length + " extension(s) back");
            return fails;
        }
    }
}
