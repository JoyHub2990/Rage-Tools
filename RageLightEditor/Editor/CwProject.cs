using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public partial class CwProject
    {
        public string Name = "New Project";
        public int Version = 1;
        public string Filepath;
        public bool HasChanged;

        public readonly List<string> YmapFilenames = new List<string>();
        public readonly List<string> YtypFilenames = new List<string>();
        public readonly List<YmapFile> YmapFiles = new List<YmapFile>();
        public readonly List<YtypFile> YtypFiles = new List<YtypFile>();

        public readonly List<string> YbnFilenames = new List<string>();
        public readonly List<string> YndFilenames = new List<string>();
        public readonly List<string> YnvFilenames = new List<string>();
        public readonly List<string> TrainsFilenames = new List<string>();
        public readonly List<string> ScenarioFilenames = new List<string>();
        public readonly List<string> AudioRelFilenames = new List<string>();
        public readonly List<string> YdrFilenames = new List<string>();
        public readonly List<string> YddFilenames = new List<string>();
        public readonly List<string> YftFilenames = new List<string>();
        public readonly List<string> YtdFilenames = new List<string>();

        public readonly List<YndFile> YndFiles = new List<YndFile>();
        public readonly List<YnvFile> YnvFiles = new List<YnvFile>();
        public readonly List<CodeWalker.World.TrainTrack> TrainsFiles = new List<CodeWalker.World.TrainTrack>();
        public readonly List<YmtFile> ScenarioFiles = new List<YmtFile>();
        public readonly List<RelFile> AudioRelFiles = new List<RelFile>();

        public string Filename => string.IsNullOrEmpty(Filepath) ? null : Path.GetFileName(Filepath);
        public string Directory => string.IsNullOrEmpty(Filepath) ? null : Path.GetDirectoryName(Filepath);

        public void Save(string path)
        {
            Filepath = path;
            var doc = new XmlDocument();
            var root = doc.CreateElement("CodeWalkerProject");
            doc.AppendChild(root);
            Xml.AddChild(doc, root, "Name", Name);
            Xml.AddChild(doc, root, "Version", Version.ToString());
            Xml.AddList(doc, root, "YmapFilenames", "Item", YmapFilenames);
            Xml.AddList(doc, root, "YtypFilenames", "Item", YtypFilenames);
            Xml.AddList(doc, root, "YbnFilenames", "Item", YbnFilenames);
            Xml.AddList(doc, root, "YndFilenames", "Item", YndFilenames);
            Xml.AddList(doc, root, "YnvFilenames", "Item", YnvFilenames);
            Xml.AddList(doc, root, "TrainsFilenames", "Item", TrainsFilenames);
            Xml.AddList(doc, root, "ScenarioFilenames", "Item", ScenarioFilenames);
            Xml.AddList(doc, root, "AudioRelFilenames", "Item", AudioRelFilenames);
            Xml.AddList(doc, root, "YdrFilenames", "Item", YdrFilenames);
            Xml.AddList(doc, root, "YddFilenames", "Item", YddFilenames);
            Xml.AddList(doc, root, "YftFilenames", "Item", YftFilenames);
            Xml.AddList(doc, root, "YtdFilenames", "Item", YtdFilenames);
            doc.Save(path);
            HasChanged = false;
        }

        public static CwProject Load(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "CodeWalkerProject")
                throw new InvalidDataException("not a map project file (.cwproj)");
            var p = new CwProject { Filepath = path };
            p.Name = Xml.GetChildString(root, "Name") ?? Path.GetFileNameWithoutExtension(path);
            p.Version = Xml.GetChildInt(root, "Version");
            p.YmapFilenames.AddRange(Xml.GetList(root, "YmapFilenames"));
            p.YtypFilenames.AddRange(Xml.GetList(root, "YtypFilenames"));
            p.YbnFilenames.AddRange(Xml.GetList(root, "YbnFilenames"));
            p.YndFilenames.AddRange(Xml.GetList(root, "YndFilenames"));
            p.YnvFilenames.AddRange(Xml.GetList(root, "YnvFilenames"));
            p.TrainsFilenames.AddRange(Xml.GetList(root, "TrainsFilenames"));
            p.ScenarioFilenames.AddRange(Xml.GetList(root, "ScenarioFilenames"));
            p.AudioRelFilenames.AddRange(Xml.GetList(root, "AudioRelFilenames"));
            p.YdrFilenames.AddRange(Xml.GetList(root, "YdrFilenames"));
            p.YddFilenames.AddRange(Xml.GetList(root, "YddFilenames"));
            p.YftFilenames.AddRange(Xml.GetList(root, "YftFilenames"));
            p.YtdFilenames.AddRange(Xml.GetList(root, "YtdFilenames"));
            return p;
        }

        public List<string> LoadFiles(GameFileCache cache)
        {
            var problems = new List<string>();
            YmapFiles.Clear();
            YtypFiles.Clear();

            foreach (var rel in YtypFilenames)
            {
                var full = GetFullFilePath(rel);
                try
                {
                    if (!File.Exists(full)) { problems.Add("missing ytyp: " + rel); continue; }
                    var t = new YtypFile();
                    t.Load(File.ReadAllBytes(full));
                    t.FilePath = full;
                    t.RpfFileEntry ??= new RpfResourceFileEntry();
                    t.RpfFileEntry.Name = Path.GetFileName(full);
                    t.Name = t.RpfFileEntry.Name;
                    JenkIndex.Ensure(Path.GetFileNameWithoutExtension(t.Name));
                    YtypFiles.Add(t);
                }
                catch (Exception ex) { problems.Add($"{rel}: {ex.Message}"); }
            }

            foreach (var rel in YmapFilenames)
            {
                var full = GetFullFilePath(rel);
                try
                {
                    if (!File.Exists(full)) { problems.Add("missing ymap: " + rel); continue; }
                    var y = new YmapFile();
                    y.Load(File.ReadAllBytes(full));
                    y.FilePath = full;
                    y.RpfFileEntry ??= new RpfResourceFileEntry();
                    y.RpfFileEntry.Name = Path.GetFileName(full);
                    y.Name = y.RpfFileEntry.Name;
                    JenkIndex.Ensure(Path.GetFileNameWithoutExtension(y.Name));
                    InitYmapArchetypes(y, cache);
                    YmapFiles.Add(y);
                }
                catch (Exception ex) { problems.Add($"{rel}: {ex.Message}"); }
            }
            LoadYbnFiles(problems);
            return problems;
        }

        public string GetRelativePath(string filepath)
        {
            if (string.IsNullOrEmpty(filepath)) return "";
            var dir = Directory;
            if (string.IsNullOrEmpty(dir)) return filepath;
            try
            {
                var full = Path.GetFullPath(filepath);
                var baseDir = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(baseDir.Length);
                return full;
            }
            catch { return filepath; }
        }

        public string GetFullFilePath(string relpath)
        {
            if (string.IsNullOrEmpty(relpath)) return relpath;
            if (Path.IsPathRooted(relpath)) return relpath;
            var dir = Directory;
            return string.IsNullOrEmpty(dir) ? relpath : Path.Combine(dir, relpath);
        }

        public YmapFile NewYmap()
        {
            int i = 1;
            string fname;
            do { fname = "map" + i++ + ".ymap"; } while (ContainsYmap(fname));

            var ymap = AddYmapFile(fname);
            if (ymap == null) return null;
            ymap.Loaded = true;
            ymap.HasChanged = true;
            ymap._CMapData.contentFlags = 65;
            HasChanged = true;
            return ymap;
        }

        public YmapFile AddYmapFile(string filename)
        {
            var ymap = new YmapFile();
            ymap.RpfFileEntry = new RpfResourceFileEntry { Name = Path.GetFileName(filename) };
            ymap.FilePath = GetFullFilePath(filename);
            ymap.Name = ymap.RpfFileEntry.Name;
            JenkIndex.Ensure(ymap.Name);
            JenkIndex.Ensure(Path.GetFileNameWithoutExtension(ymap.Name));
            JenkIndex.Ensure(filename);
            ymap._CMapData.name = JenkHash.GenHash(Path.GetFileNameWithoutExtension(ymap.Name));
            ymap._CMapData.parent = new MetaHash(0);
            ymap.AllEntities = Array.Empty<YmapEntityDef>();
            ymap.RootEntities = Array.Empty<YmapEntityDef>();
            if (!AddYmapFile(ymap)) return null;
            return ymap;
        }

        public bool AddYmapFile(YmapFile ymap)
        {
            var rel = GetRelativePath(ymap.FilePath);
            if (string.IsNullOrEmpty(rel)) rel = ymap.Name;
            if (YmapFilenames.Contains(rel)) return false;
            YmapFilenames.Add(rel);
            YmapFiles.Add(ymap);
            HasChanged = true;
            return true;
        }

        public void RemoveYmapFile(YmapFile ymap)
        {
            if (ymap == null) return;
            var rel = GetRelativePath(ymap.FilePath);
            if (string.IsNullOrEmpty(rel)) rel = ymap.Name;
            YmapFilenames.Remove(rel);
            YmapFiles.Remove(ymap);
            HasChanged = true;
        }

        public bool ContainsYmap(string filename)
        {
            foreach (var f in YmapFilenames)
                if (string.Equals(Path.GetFileName(f), Path.GetFileName(filename), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public bool ContainsYmap(YmapFile ymap) => ymap != null && YmapFiles.Contains(ymap);

        public bool RenameYmap(string oldRel, string newRel)
        {
            int i = YmapFilenames.IndexOf(oldRel);
            if (i < 0) return false;
            YmapFilenames[i] = newRel;
            HasChanged = true;
            return true;
        }

        public YtypFile NewYtyp()
        {
            int i = 1;
            string fname;
            do { fname = "types" + i++ + ".ytyp"; } while (ContainsYtyp(fname));

            var ytyp = AddYtypFile(fname);
            if (ytyp == null) return null;
            ytyp.Loaded = true;
            ytyp.HasChanged = true;
            HasChanged = true;
            return ytyp;
        }

        public YtypFile AddYtypFile(string filename)
        {
            var ytyp = new YtypFile();
            ytyp.RpfFileEntry = new RpfResourceFileEntry { Name = Path.GetFileName(filename) };
            ytyp.FilePath = GetFullFilePath(filename);
            ytyp.Name = ytyp.RpfFileEntry.Name;
            ytyp.NameHash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(ytyp.Name));
            JenkIndex.Ensure(ytyp.Name);
            JenkIndex.Ensure(Path.GetFileNameWithoutExtension(ytyp.Name));
            ytyp._CMapTypes.name = ytyp.NameHash;
            ytyp.AllArchetypes = Array.Empty<Archetype>();
            if (!AddYtypFile(ytyp)) return null;
            return ytyp;
        }

        public bool AddYtypFile(YtypFile ytyp)
        {
            var rel = GetRelativePath(ytyp.FilePath);
            if (string.IsNullOrEmpty(rel)) rel = ytyp.Name;
            if (YtypFilenames.Contains(rel)) return false;
            YtypFilenames.Add(rel);
            YtypFiles.Add(ytyp);
            HasChanged = true;
            return true;
        }

        public void RemoveYtypFile(YtypFile ytyp)
        {
            if (ytyp == null) return;
            var rel = GetRelativePath(ytyp.FilePath);
            if (string.IsNullOrEmpty(rel)) rel = ytyp.Name;
            YtypFilenames.Remove(rel);
            YtypFiles.Remove(ytyp);
            HasChanged = true;
        }

        public bool ContainsYtyp(string filename)
        {
            foreach (var f in YtypFilenames)
                if (string.Equals(Path.GetFileName(f), Path.GetFileName(filename), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public bool ContainsYtyp(YtypFile ytyp) => ytyp != null && YtypFiles.Contains(ytyp);

        public bool RenameYtyp(string oldRel, string newRel)
        {
            int i = YtypFilenames.IndexOf(oldRel);
            if (i < 0) return false;
            YtypFilenames[i] = newRel;
            HasChanged = true;
            return true;
        }

        public YmapEntityDef NewEntity(YmapFile ymap, Vector3 spawnPos, GameFileCache cache,
                                       YmapEntityDef copy = null, bool copyPosition = false)
        {
            if (ymap == null) return null;
            bool cp = copyPosition && copy != null;
            var pos = cp ? copy.Position : spawnPos;

            var cent = new CEntityDef();
            if (copy != null)
            {
                cent = copy._CEntityDef;
            }
            else
            {
                cent.archetypeName = new MetaHash(JenkHash.GenHash("v_ind_chickensx3"));
                cent.rotation = new Vector4(0, 0, 0, 1);
                cent.scaleXY = 1.0f;
                cent.scaleZ = 1.0f;
                cent.flags = 32;
                cent.parentIndex = -1;
                cent.lodDist = 200.0f;
                cent.lodLevel = rage__eLodType.LODTYPES_DEPTH_ORPHANHD;
                cent.priorityLevel = rage__ePriorityLevel.PRI_REQUIRED;
                cent.ambientOcclusionMultiplier = 255;
                cent.artificialAmbientOcclusion = 255;
            }
            cent.position = pos;

            var ent = new YmapEntityDef(ymap, 0, ref cent);
            ent.SetArchetype(FindArchetype(cent.archetypeName, cache));
            ymap.AddEntity(ent);
            ymap.HasChanged = true;
            return ent;
        }

        public Archetype NewArchetype(YtypFile ytyp, Archetype copy = null)
        {
            if (ytyp == null) return null;
            var arch = ytyp.AddArchetype();
            var def = arch._BaseArchetypeDef;
            if (copy != null)
            {
                arch.Init(ytyp, ref copy._BaseArchetypeDef);
                def = arch._BaseArchetypeDef;
            }
            else
            {
                def.name = JenkHash.GenHash("v_ind_chickensx3");
                def.assetName = def.name;
                def.assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE;
                def.flags = 32;
                def.lodDist = 60.0f;
                def.hdTextureDist = 60.0f;
                def.bbMin = new Vector3(-0.5f);
                def.bbMax = new Vector3(0.5f);
                def.bsRadius = 0.87f;
                arch.Init(ytyp, ref def);
            }
            arch._BaseArchetypeDef = def;
            ytyp.HasChanged = true;
            return arch;
        }

        public int NewArchetypesFromYdrs(YtypFile ytyp, IEnumerable<string> ydrPaths, out List<string> problems)
        {
            problems = new List<string>();
            int made = 0;
            if (ytyp == null) return 0;
            foreach (var file in ydrPaths)
            {
                try
                {
                    var arch = ytyp.AddArchetype();
                    var ydr = new YdrFile();
                    RpfFile.LoadResourceFile(ydr, File.ReadAllBytes(file), 165);
                    var name = Path.GetFileNameWithoutExtension(file);
                    var hash = JenkHash.GenHash(name);
                    JenkIndex.Ensure(name);
                    var d = ydr.Drawable;
                    var def = arch._BaseArchetypeDef;
                    def.name = hash;
                    def.assetName = hash;
                    def.assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE;
                    def.specialAttribute = 0;
                    def.flags = 32;
                    def.bbMin = d.BoundingBoxMin;
                    def.bbMax = d.BoundingBoxMax;
                    def.bsCentre = d.BoundingCenter;
                    def.bsRadius = d.BoundingSphereRadius;
                    def.hdTextureDist = 60.0f;
                    def.lodDist = 60.0f;
                    if (d.ShaderGroup?.TextureDictionary != null) def.textureDictionary = hash;
                    if (d.Bound != null) def.physicsDictionary = hash;
                    arch.Init(ytyp, ref def);
                    arch._BaseArchetypeDef = def;
                    made++;
                }
                catch (Exception ex) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
            if (made > 0) ytyp.HasChanged = true;
            return made;
        }

        public Archetype FindArchetype(MetaHash name, GameFileCache cache)
        {
            foreach (var t in YtypFiles)
            {
                var a = t?.AllArchetypes;
                if (a == null) continue;
                foreach (var arch in a)
                    if (arch != null && arch.Hash == name.Hash) return arch;
            }
            return cache?.GetArchetype(name.Hash);
        }

        public void InitYmapArchetypes(YmapFile ymap, GameFileCache cache)
        {
            var all = ymap?.AllEntities;
            if (all == null) return;
            foreach (var e in all)
            {
                if (e == null) continue;
                var arch = FindArchetype(e._CEntityDef.archetypeName, cache);
                if (arch != null && !ReferenceEquals(e.Archetype, arch))
                {
                    e.SetArchetype(arch);
                    if (e.IsMlo && e.MloInstance != null) e.MloInstance.InitYmapEntityArchetypes(cache);
                }
            }
        }

        public void OverlayOnto(Dictionary<uint, YmapFile> ymaps)
        {
            foreach (var y in YmapFiles)
            {
                if (y == null || !y.Loaded) continue;
                var re = y.RpfFileEntry;
                if (re == null) continue;
                var nm = !string.IsNullOrEmpty(re.Name) ? re.Name : (y.Name ?? Path.GetFileName(y.FilePath ?? ""));
                if (string.IsNullOrEmpty(nm)) continue;
                re.Name = nm;
                re.NameLower = nm.ToLowerInvariant();
                re.ShortNameHash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(re.NameLower));
                ymaps[re.ShortNameHash] = y;
            }
        }

        public bool AnyUnsaved =>
            HasChanged || YmapFiles.Any(y => y != null && y.HasChanged) || YtypFiles.Any(t => t != null && t.HasChanged) ||
            YndFiles.Any(f => f != null && f.HasChanged) || YnvFiles.Any(f => f != null && f.HasChanged) ||
            TrainsFiles.Any(f => f != null && f.HasChanged) || ScenarioFiles.Any(f => f != null && f.HasChanged) ||
            AudioRelFiles.Any(f => f != null && f.HasChanged);

        private bool AddNamed<T>(List<string> names, List<T> files, T file, string filePath, string name) where T : class
        {
            if (file == null || files.Contains(file)) return false;
            var rel = GetRelativePath(filePath);
            if (string.IsNullOrEmpty(rel)) rel = name;
            if (string.IsNullOrEmpty(rel)) return false;
            if (!names.Contains(rel)) names.Add(rel);
            files.Add(file);
            HasChanged = true;
            return true;
        }
        private void RemoveNamed<T>(List<string> names, List<T> files, T file, string filePath, string name) where T : class
        {
            if (file == null) return;
            var rel = GetRelativePath(filePath);
            if (string.IsNullOrEmpty(rel)) rel = name;
            names.Remove(rel);
            files.Remove(file);
            HasChanged = true;
        }

        public bool AddYndFile(YndFile f) => AddNamed(YndFilenames, YndFiles, f, f?.FilePath, f?.Name);
        public bool AddYnvFile(YnvFile f) => AddNamed(YnvFilenames, YnvFiles, f, f?.FilePath, f?.Name);
        public bool AddTrainsFile(CodeWalker.World.TrainTrack f) => AddNamed(TrainsFilenames, TrainsFiles, f, f?.FilePath, f?.Name);
        public bool AddScenarioFile(YmtFile f) => AddNamed(ScenarioFilenames, ScenarioFiles, f, f?.FilePath, f?.Name);
        public bool AddAudioRelFile(RelFile f) => AddNamed(AudioRelFilenames, AudioRelFiles, f, f?.FilePath, f?.Name);
        public void RemoveYndFile(YndFile f) => RemoveNamed(YndFilenames, YndFiles, f, f?.FilePath, f?.Name);
        public void RemoveYnvFile(YnvFile f) => RemoveNamed(YnvFilenames, YnvFiles, f, f?.FilePath, f?.Name);
        public void RemoveTrainsFile(CodeWalker.World.TrainTrack f) => RemoveNamed(TrainsFilenames, TrainsFiles, f, f?.FilePath, f?.Name);
        public void RemoveScenarioFile(YmtFile f) => RemoveNamed(ScenarioFilenames, ScenarioFiles, f, f?.FilePath, f?.Name);
        public void RemoveAudioRelFile(RelFile f) => RemoveNamed(AudioRelFilenames, AudioRelFiles, f, f?.FilePath, f?.Name);
        public bool ContainsYnd(YndFile f) => f != null && YndFiles.Contains(f);
        public bool ContainsYnv(YnvFile f) => f != null && YnvFiles.Contains(f);
        public bool ContainsTrainTrack(CodeWalker.World.TrainTrack f) => f != null && TrainsFiles.Contains(f);
        public bool ContainsScenario(YmtFile f) => f != null && ScenarioFiles.Contains(f);
        public bool ContainsAudioRel(RelFile f) => f != null && AudioRelFiles.Contains(f);

        private static class Xml
        {
            public static void AddChild(XmlDocument doc, XmlNode parent, string name, string value)
            {
                var e = doc.CreateElement(name);
                e.InnerText = value ?? "";
                parent.AppendChild(e);
            }
            public static void AddList(XmlDocument doc, XmlNode parent, string name, string item, IEnumerable<string> values)
            {
                var e = doc.CreateElement(name);
                foreach (var v in values) AddChild(doc, e, item, v);
                parent.AppendChild(e);
            }
            public static string GetChildString(XmlNode n, string name) => n?[name]?.InnerText;
            public static int GetChildInt(XmlNode n, string name) => int.TryParse(GetChildString(n, name), out var v) ? v : 0;
            public static IEnumerable<string> GetList(XmlNode n, string name)
            {
                var e = n?[name];
                if (e == null) yield break;
                foreach (XmlNode c in e.ChildNodes)
                    if (c.NodeType == XmlNodeType.Element && !string.IsNullOrEmpty(c.InnerText)) yield return c.InnerText;
            }
        }
    }
}

