using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using CodeWalker.GameFiles;
using SharpDX;

namespace RageLightEditor.Editor
{
    public static class XmlIO
    {
        public static string LastStatus = "";

        public sealed class TypeInfo
        {
            public string Extension;
            public bool CanExport;
            public bool CanImport;
            public bool NeedsAssetFolder;
            public bool LoadsAsObject;
            public bool CanExportLoose;
            public string Note;

            public string DisplayName => Extension.TrimStart('.').ToUpperInvariant();
            public override string ToString() => Extension;
        }

        private static readonly TypeInfo[] typeTable =
        {
            new TypeInfo { Extension = ".ymap", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "map placements; exported through Save() so live edits are included" },
            new TypeInfo { Extension = ".ytyp", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "archetypes; exported through Save() so live edits are included" },
            new TypeInfo { Extension = ".ybn", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "collision bounds" },
            new TypeInfo { Extension = ".ydr", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                NeedsAssetFolder = true, Note = "drawable; embedded textures live in the sidecar folder" },
            new TypeInfo { Extension = ".ydd", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                NeedsAssetFolder = true, Note = "drawable dictionary; embedded textures in the sidecar folder" },
            new TypeInfo { Extension = ".yft", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                NeedsAssetFolder = true, Note = "fragment; embedded textures in the sidecar folder" },
            new TypeInfo { Extension = ".ytd", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                NeedsAssetFolder = true, Note = "texture dictionary; every texture is a .dds in the sidecar folder" },
            new TypeInfo { Extension = ".ypt", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                NeedsAssetFolder = true, Note = "particle effects; embedded textures in the sidecar folder" },
            new TypeInfo { Extension = ".ycd", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "clip dictionary" },
            new TypeInfo { Extension = ".ynd", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "vehicle path nodes" },
            new TypeInfo { Extension = ".ynv", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "navigation mesh" },
            new TypeInfo { Extension = ".yld", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "cloth dictionary" },
            new TypeInfo { Extension = ".yed", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "expression dictionary" },
            new TypeInfo { Extension = ".ywr", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "waypoint recording" },
            new TypeInfo { Extension = ".yvr", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "vehicle recording" },
            new TypeInfo { Extension = ".ypdb", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "pose matcher database" },
            new TypeInfo { Extension = ".yfd", CanExport = true, CanImport = true, LoadsAsObject = true, CanExportLoose = true,
                Note = "frame filter dictionary" },
            new TypeInfo { Extension = ".ymt", CanExport = true, CanImport = true,
                Note = "meta/pso data; imports as bytes only - the core library has no YmtFile writer" },
            new TypeInfo { Extension = ".ymf", CanExport = true, CanImport = true,
                Note = "manifest; imports as bytes only - the core library has no YmfFile writer" },
            new TypeInfo { Extension = ".rel", CanExport = true, CanImport = true,
                Note = "audio data; imports as bytes only" },
            new TypeInfo { Extension = ".awc", CanExport = true, CanImport = true, NeedsAssetFolder = true,
                Note = "audio wave container; imports as bytes only, streams live in the sidecar folder" },
            new TypeInfo { Extension = ".fxc", CanExport = true, CanImport = true, NeedsAssetFolder = true,
                Note = "compiled shaders; imports as bytes only, programs live in the sidecar folder" },
            new TypeInfo { Extension = ".mrf", CanExport = true, CanImport = true,
                Note = "move network; imports as bytes only" },
            new TypeInfo { Extension = ".cut", CanExport = true, CanImport = true,
                Note = "cutscene (PSO); exports as .cut.pso.xml, imports as bytes only" },
            new TypeInfo { Extension = ".pso", CanExport = true, CanImport = true,
                Note = "loose PSO; exports as .pso.pso.xml, imports as bytes only" },
            new TypeInfo { Extension = ".dat", CanExport = true, CanImport = true,
                Note = "only cache_y.dat and heightmap*.dat; imports as bytes only" },
        };

        public static IReadOnlyList<TypeInfo> Types => typeTable;

        public static TypeInfo Find(string extensionOrFileName)
        {
            if (string.IsNullOrEmpty(extensionOrFileName)) return null;
            var s = extensionOrFileName.ToLowerInvariant();
            if (s.EndsWith(".xml", StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - 4);
                if (s.EndsWith(".pso", StringComparison.Ordinal) || s.EndsWith(".rbf", StringComparison.Ordinal))
                    s = s.Substring(0, s.Length - 4);
            }
            foreach (var t in typeTable)
                if (s.EndsWith(t.Extension, StringComparison.Ordinal) || s == t.Extension) return t;
            return null;
        }

        public static string GetYmapXml(YmapFile ymap, string nameOverride = null)
        {
            if (ymap == null) throw new ArgumentNullException(nameof(ymap));
            var name = Name(nameOverride, ymap.Name, ymap.RpfFileEntry?.Name, ".ymap");

            if (!string.Equals(ymap.Name, name, StringComparison.Ordinal)) ymap.Name = name;

            var data = ymap.Save();
            if (data == null || data.Length == 0)
                throw new Exception(name + ": Save() produced no data.");

            var fresh = new YmapFile();
            fresh.Load(data);
            SetFileName(fresh, name);
            return MetaXml.GetXml(fresh, out _);
        }

        public static string GetYtypXml(YtypFile ytyp, string nameOverride = null)
        {
            if (ytyp == null) throw new ArgumentNullException(nameof(ytyp));
            var name = Name(nameOverride, ytyp.Name, ytyp.RpfFileEntry?.Name, ".ytyp");
            if (!string.Equals(ytyp.Name, name, StringComparison.Ordinal)) ytyp.Name = name;

            if (ytyp.NameHash == 0)
                ytyp.NameHash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(name).ToLowerInvariant());

            var data = ytyp.Save();
            if (data == null || data.Length == 0)
                throw new Exception(name + ": Save() produced no data.");

            var fresh = new YtypFile();
            fresh.Load(data);
            SetFileName(fresh, name);
            return MetaXml.GetXml(fresh, out _);
        }

        public static string Export(object file, string outputFolder, string nameOverride = null)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (string.IsNullOrEmpty(outputFolder)) throw new ArgumentException("No output folder.");

            try
            {
                Directory.CreateDirectory(outputFolder);
                var xml = BuildXml(file, outputFolder, nameOverride, out var xmlName);
                if (string.IsNullOrEmpty(xml))
                    throw new Exception(xmlName + ": nothing was written (the file has no data of a kind that converts).");

                var path = Path.Combine(outputFolder, xmlName);
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                LastStatus = $"exported {xmlName} ({xml.Length / 1024} KB)";
                return path;
            }
            catch (Exception ex) { LastStatus = "export failed: " + ex.Message; throw; }
        }

        public static string ExportFromArchive(GameFileManager game, string rpfPath, string outputFolder)
        {
            if (game?.Cache?.RpfMan == null) throw new Exception("Game files are not loaded.");
            if (string.IsNullOrEmpty(outputFolder)) throw new ArgumentException("No output folder.");

            try
            {
                var entry = game.Cache.RpfMan.GetEntry(rpfPath) as RpfFileEntry;
                if (entry == null) throw new Exception("Not found in the archives: " + rpfPath);
                var data = game.Cache.RpfMan.GetFileData(rpfPath);
                if (data == null || data.Length == 0) throw new Exception("Could not read " + rpfPath);

                Directory.CreateDirectory(outputFolder);
                var xml = MetaXml.GetXml(entry, data, out var xmlName, outputFolder);
                if (string.IsNullOrEmpty(xml))
                    throw new Exception(entry.Name + ": no XML converter for this file type.");
                if (string.IsNullOrEmpty(xmlName) || xmlName.StartsWith(".", StringComparison.Ordinal))
                    xmlName = entry.Name + ".xml";

                var path = Path.Combine(outputFolder, xmlName);
                File.WriteAllText(path, xml, new UTF8Encoding(false));
                LastStatus = $"exported {xmlName} ({xml.Length / 1024} KB)";
                return path;
            }
            catch (Exception ex) { LastStatus = "export failed: " + ex.Message; throw; }
        }

        public static string ExportBinaryFile(string binPath, string outputFolder)
        {
            var ext = Path.GetExtension(binPath)?.ToLowerInvariant() ?? "";
            var info = Types.FirstOrDefault(t => t.Extension == ext);
            if (info != null && info.CanExport && !info.CanExportLoose)
                throw new NotSupportedException(
                    $"{Path.GetFileName(binPath)}: {info.DisplayName} files can be exported from " +
                    "inside an archive, but not from a loose file on disk - open it through the " +
                    "archive browser instead.");
            var file = LoadBinary(binPath);
            return Export(file, outputFolder, Path.GetFileName(binPath));
        }

        private static string BuildXml(object file, string outputFolder, string nameOverride, out string xmlName)
        {
            string baseName;
            string metaName;
            string xml;

            switch (file)
            {
                case YmapFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ymap");
                    xml = GetYmapXml(f, baseName); metaName = null; break;
                case YtypFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ytyp");
                    xml = GetYtypXml(f, baseName); metaName = null; break;

                case YmtFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ymt");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YmfFile f:
                    baseName = Name(nameOverride, null, f.FileEntry?.Name, ".ymf");
                    xml = MetaXml.GetXml(f, out metaName); break;

                case YbnFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ybn");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YcdFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ycd");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YndFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ynd");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YnvFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ynv");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YpdbFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ypdb");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YfdFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".yfd");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YldFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".yld");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YedFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".yed");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YwrFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ywr");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case YvrFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".yvr");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case RelFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".rel");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case MrfFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".mrf");
                    xml = MetaXml.GetXml(f, out metaName, null); break;
                case CacheDatFile f:
                    baseName = Name(nameOverride, null, f.FileEntry?.Name, ".dat");
                    xml = MetaXml.GetXml(f, out metaName, null); break;
                case HeightmapFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".dat");
                    xml = MetaXml.GetXml(f, out metaName, null); break;
                case JPsoFile f:
                    baseName = Name(nameOverride, null, f.FileEntry?.Name, ".pso");
                    xml = MetaXml.GetXml(f, out metaName); break;
                case CutFile f:
                    baseName = Name(nameOverride, null, f.FileEntry?.Name, ".cut");
                    xml = MetaXml.GetXml(f, out metaName); break;

                case YdrFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ydr");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;
                case YddFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ydd");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;
                case YftFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".yft");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;
                case YtdFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ytd");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;
                case YptFile f:
                    baseName = Name(nameOverride, f.Name, f.RpfFileEntry?.Name, ".ypt");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;
                case AwcFile f:
                    baseName = Name(nameOverride, f.Name, null, ".awc");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;
                case FxcFile f:
                    baseName = Name(nameOverride, f.Name, null, ".fxc");
                    xml = MetaXml.GetXml(f, out metaName, AssetFolder(outputFolder, baseName)); break;

                default:
                    throw new Exception("No XML exporter for " + file.GetType().Name + ".");
            }

            string suffix = ".xml";
            if (!string.IsNullOrEmpty(metaName))
            {
                if (metaName.EndsWith(".pso.xml", StringComparison.OrdinalIgnoreCase)) suffix = ".pso.xml";
                else if (metaName.EndsWith(".rbf.xml", StringComparison.OrdinalIgnoreCase)) suffix = ".rbf.xml";
            }
            xmlName = baseName + suffix;

            return xml;
        }

        public static byte[] ImportData(string xmlPath, out string fileName,
                                        out List<string> missingAssets, string assetFolder = null)
        {
            fileName = null;
            missingAssets = new List<string>();
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
                throw new FileNotFoundException("XML file not found.", xmlPath);

            try
            {
                var xmlName = Path.GetFileName(xmlPath);
                var format = MetaFormatOf(xmlName, out var outName);
                fileName = outName;

                var info = Find(outName);
                if (info == null || !info.CanImport)
                {
                    var ext = Path.GetExtension(outName);
                    throw new Exception(string.IsNullOrEmpty(ext)
                        ? $"{xmlName}: no game file type in the name - it should read like \"{Path.GetFileNameWithoutExtension(outName)}.ymap.xml\"."
                        : $"No importer for {ext} files.");
                }

                if (string.IsNullOrEmpty(assetFolder))
                    assetFolder = DefaultAssetFolder(xmlPath, outName);

                var doc = LoadXmlDocument(xmlPath);
                if (info.NeedsAssetFolder) missingAssets = FindMissingAssets(doc, assetFolder);

                if (missingAssets.Count > 0)
                    throw new Exception($"{outName}: {missingAssets.Count} sidecar asset file(s) " +
                                        $"missing from {assetFolder}: {string.Join(", ", missingAssets)}");

                var data = XmlMeta.GetData(doc, format, assetFolder);
                if (data == null || data.Length == 0)
                    throw new Exception(outName + ": the XML produced no data. " +
                                        "Check that its root element matches the file type.");

                LastStatus = $"imported {outName} ({data.Length / 1024} KB)";
                return data;
            }
            catch (Exception ex) { LastStatus = "import failed: " + ex.Message; throw; }
        }

        public static string Import(string xmlPath, string outputFolder, string assetFolder = null)
        {
            if (string.IsNullOrEmpty(outputFolder)) throw new ArgumentException("No output folder.");
            var data = ImportData(xmlPath, out var fileName, out var missing, assetFolder);
            try
            {
                Directory.CreateDirectory(outputFolder);
                var path = Path.Combine(outputFolder, fileName);
                File.WriteAllBytes(path, data);
                LastStatus = $"wrote {fileName} ({data.Length / 1024} KB)" +
                             (missing.Count > 0 ? $" - {missing.Count} asset file(s) MISSING" : "");
                return path;
            }
            catch (Exception ex) { LastStatus = "import failed: " + ex.Message; throw; }
        }

        public static YmapFile ImportYmap(string xmlPath, string assetFolder = null)
        {
            var data = ImportData(xmlPath, out var fileName, out _, assetFolder);
            if (!fileName.EndsWith(".ymap", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Not a ymap XML: " + Path.GetFileName(xmlPath));
            return LoadYmap(data, fileName);
        }

        public static YmapFile ImportYmapFromXml(string xml, string name)
        {
            if (string.IsNullOrEmpty(xml)) throw new ArgumentException("Empty XML.");
            var doc = ParseXml(xml);
            var data = XmlMeta.GetRSCData(doc);
            if (data == null || data.Length == 0) throw new Exception("The XML produced no ymap data.");
            return LoadYmap(data, EnsureExt(FirstNonEmpty(name, "imported"), ".ymap"));
        }

        public static YtypFile ImportYtyp(string xmlPath, string assetFolder = null)
        {
            var data = ImportData(xmlPath, out var fileName, out _, assetFolder);
            if (!fileName.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Not a ytyp XML: " + Path.GetFileName(xmlPath));
            var ytyp = new YtypFile();
            ytyp.Load(data);
            SetFileName(ytyp, fileName);
            ytyp.Loaded = true;
            return ytyp;
        }

        public static GameFile ImportGameFile(string xmlPath, string assetFolder = null)
        {
            var data = ImportData(xmlPath, out var fileName, out _, assetFolder);
            var info = Find(fileName);
            if (info == null || !info.LoadsAsObject)
                throw new Exception($"{Path.GetExtension(fileName)} imports as bytes only" +
                                    (info?.Note != null ? " (" + info.Note + ")" : "") + ".");
            return LoadResource(fileName, data);
        }

        public static GameFile LoadBinary(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("File not found.", path);
            var data = File.ReadAllBytes(path);
            if (data.Length == 0) throw new Exception(Path.GetFileName(path) + " is empty.");
            return LoadResource(Path.GetFileName(path), data);
        }

        public static GameFile LoadResource(string fileName, byte[] data)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("No data.");
            var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
            GameFile file;

            switch (ext)
            {
                case ".ymap": { var f = new YmapFile(); f.Load(data); file = f; break; }
                case ".ytyp": { var f = new YtypFile(); f.Load(data); file = f; break; }
                case ".ybn": { var f = new YbnFile(); f.Load(data); file = f; break; }
                case ".ytd": { var f = new YtdFile(); f.Load(data); file = f; break; }
                case ".ydr": { var f = new YdrFile(); f.Load(data); file = f; break; }
                case ".ydd": { var f = new YddFile(); f.Load(data); file = f; break; }
                case ".yft": { var f = new YftFile(); f.Load(data); file = f; break; }
                case ".ypt": { var f = new YptFile(); f.Load(data); file = f; break; }
                case ".ynd": { var f = new YndFile(); f.Load(data); file = f; break; }
                case ".ynv": { var f = new YnvFile(); f.Load(data); file = f; break; }

                case ".ycd": file = RpfFile.GetResourceFile<YcdFile>(data); break;
                case ".yld": file = RpfFile.GetResourceFile<YldFile>(data); break;
                case ".yed": file = RpfFile.GetResourceFile<YedFile>(data); break;
                case ".ywr": file = RpfFile.GetResourceFile<YwrFile>(data); break;
                case ".yvr": file = RpfFile.GetResourceFile<YvrFile>(data); break;
                case ".ypdb": file = RpfFile.GetResourceFile<YpdbFile>(data); break;
                case ".yfd": file = RpfFile.GetResourceFile<YfdFile>(data); break;

                default: throw new Exception($"No loader for {ext} files.");
            }

            if (file == null) throw new Exception(fileName + ": the data did not parse.");
            SetFileName(file, fileName);
            file.Loaded = true;
            return file;
        }

        private static YmapFile LoadYmap(byte[] data, string fileName)
        {
            var ymap = new YmapFile();
            ymap.Load(data);
            SetFileName(ymap, fileName);
            ymap.Loaded = true;
            return ymap;
        }

        public static bool VerifyRoundTrip(YmapFile ymap, out string detail)
        {
            detail = "";
            if (ymap == null) { detail = "no ymap"; return false; }
            try
            {
                var name = EnsureExt(FirstNonEmpty(ymap.Name, ymap.RpfFileEntry?.Name, "roundtrip"), ".ymap");
                var xml = GetYmapXml(ymap);
                var rt = ImportYmapFromXml(xml, name);

                var a = ymap.AllEntities ?? Array.Empty<YmapEntityDef>();
                var b = rt.AllEntities ?? Array.Empty<YmapEntityDef>();
                if (a.Length != b.Length)
                {
                    detail = $"{a.Length} entities exported, {b.Length} came back";
                    return false;
                }

                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i]._CEntityDef.archetypeName.Hash != b[i]._CEntityDef.archetypeName.Hash)
                    { detail = $"entity {i}: archetype changed"; return false; }

                    var d = (a[i].Position - b[i].Position).Length();
                    if (d > 0.001f) { detail = $"entity {i}: position off by {d:0.0000}"; return false; }

                    var dot = Math.Abs(Quaternion.Dot(a[i].Orientation, b[i].Orientation));
                    if (dot < 0.999f) { detail = $"entity {i}: orientation off (dot {dot:0.0000})"; return false; }
                }

                detail = $"{a.Length} entities survived ymap -> XML -> ymap";
                return true;
            }
            catch (Exception ex) { detail = ex.Message; return false; }
        }

        private static MetaFormat MetaFormatOf(string xmlFileName, out string outputFileName)
        {
            var lower = (xmlFileName ?? "").ToLowerInvariant();
            var format = XmlMeta.GetXMLFormat(lower, out var trim);
            if (format == MetaFormat.XML || trim >= xmlFileName.Length)
                throw new Exception(xmlFileName + " is not a recognised game XML file " +
                                    "(the name must be like \"vb_01.ymap.xml\").");
            outputFileName = xmlFileName.Substring(0, xmlFileName.Length - trim);
            return format;
        }

        private static string DefaultAssetFolder(string xmlPath, string outputFileName)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(xmlPath));
            if (string.IsNullOrEmpty(dir)) dir = ".";
            var sidecar = Path.Combine(dir, Path.GetFileNameWithoutExtension(outputFileName));
            var sidecarCw = Path.Combine(dir, outputFileName);
            return Directory.Exists(sidecar) ? sidecar
                 : Directory.Exists(sidecarCw) ? sidecarCw : dir;
        }

        private static string AssetFolder(string outputFolder, string baseName)
        {
            var shortName = Path.GetFileNameWithoutExtension(baseName);
            if (string.IsNullOrEmpty(shortName)) shortName = "assets";
            return Path.Combine(outputFolder ?? ".", shortName);
        }

        private static List<string> FindMissingAssets(XmlDocument doc, string assetFolder)
        {
            var missing = new List<string>();
            try
            {
                var nodes = doc.GetElementsByTagName("FileName");
                foreach (XmlNode n in nodes)
                {
                    var f = n.InnerText;
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    if (!File.Exists(Path.Combine(assetFolder ?? ".", f)) && !missing.Contains(f))
                        missing.Add(f);
                }
            }
            catch { }
            return missing;
        }

        private static XmlDocument LoadXmlDocument(string path)
        {
            return ParseXml(File.ReadAllText(path));
        }

        private static XmlDocument ParseXml(string text)
        {
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var doc = new XmlDocument();
            doc.XmlResolver = null;
            doc.LoadXml(text);
            return doc;
        }

        private static void SetFileName(GameFile file, string name)
        {
            if (file == null || string.IsNullOrEmpty(name)) return;
            file.Name = name;
            var e = file.RpfFileEntry;
            if (e == null) return;
            e.Name = name;
            e.NameLower = name.ToLowerInvariant();
            e.NameHash = JenkHash.GenHash(name);
            e.ShortNameHash = JenkHash.GenHash(e.GetShortNameLower());
        }

        private static string Name(string overrideName, string objectName, string entryName, string ext)
            => EnsureExt(FirstNonEmpty(overrideName, objectName, entryName, "untitled"), ext);

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values) if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "untitled";
        }

        private static string EnsureExt(string name, string ext)
            => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
    }
}

