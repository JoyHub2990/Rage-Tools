using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void RegisterApiVerbs_W2(ApiVerbs api)
        {

            api.Add("api.fs.search_names",
                "Substring search over every file in every archive (the flat index - instant). Case-insensitive.",
                ApiVerbs.Schema(("query", "string", "substring of the file name", true),
                                ("ext", "string", "limit to one extension, e.g. \".ydr\"", false),
                                ("max", "integer", "cap on returned matches (default 100)", false)),
                "{total, matches:[{name, path, size}]}",
                false, m =>
                {
                    var archive = RequireArchive_W2();
                    var query = m.Text("query", 0, "");
                    if (string.IsNullOrWhiteSpace(query)) throw new ApiRefused("query is empty");
                    var ext = m.Text("ext", 1, "").Trim().ToLowerInvariant();
                    int max = Math.Clamp(m.Int("max", 2, 100), 1, 2000);
                    var exts = string.IsNullOrEmpty(ext) ? null : new[] { ext.StartsWith(".") ? ext : "." + ext };
                    var into = new List<ArchiveBrowser.Entry>();
                    int total = archive.Find(query, exts, into, max);
                    var sb = new StringBuilder("{\"total\":").Append(total).Append(",\"matches\":[");
                    for (int i = 0; i < into.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"name\":").Append(DccBridgeProtocol.S(into[i].File?.Name ?? ""))
                          .Append(",\"path\":").Append(DccBridgeProtocol.S(into[i].Path))
                          .Append(",\"size\":").Append(into[i].File?.GetFileSize() ?? 0)
                          .Append('}');
                    }
                    return sb.Append("]}").ToString();
                });

            api.Add("api.fs.list_dir",
                "List one directory inside the archives. Empty path lists the top-level archives; a path may walk into nested .rpf files.",
                ApiVerbs.Schema(("path", "string", "archive path like \"x64a.rpf\\\\levels\\\\...\"; empty for the roots", false)),
                "{path, dirs:[...], files:[{name, size, resource, encrypted}]}",
                false, m =>
                {
                    var archive = RequireArchive_W2();
                    var path = m.Text("path", 0, "").Trim().Trim('\\', '/').Replace('/', '\\');
                    var sb = new StringBuilder("{\"path\":").Append(DccBridgeProtocol.S(path));
                    if (path.Length == 0)
                    {
                        sb.Append(",\"dirs\":[");
                        for (int i = 0; i < archive.Roots.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(DccBridgeProtocol.S(archive.Roots[i].Path));
                        }
                        return sb.Append("],\"files\":[]}").ToString();
                    }
                    var dir = ResolveArchiveDir_W2(path);
                    if (dir == null) throw new ApiRefused("no directory at '" + path + "'");
                    sb.Append(",\"dirs\":[");
                    bool first = true;
                    if (dir.Directories != null)
                        foreach (var d in dir.Directories)
                        {
                            if (!first) sb.Append(','); first = false;
                            sb.Append(DccBridgeProtocol.S(d.Name));
                        }
                    sb.Append("],\"files\":[");
                    first = true;
                    if (dir.Files != null)
                        foreach (var f in dir.Files)
                        {
                            if (!first) sb.Append(','); first = false;
                            sb.Append("{\"name\":").Append(DccBridgeProtocol.S(f.Name))
                              .Append(",\"size\":").Append(f.GetFileSize())
                              .Append(",\"resource\":").Append(f is RpfResourceFileEntry ? "true" : "false")
                              .Append(",\"encrypted\":").Append(f.IsEncrypted ? "true" : "false")
                              .Append('}');
                        }
                    return sb.Append("]}").ToString();
                });

            api.Add("api.fs.hash",
                "Jenkins/joaat hash of a name, lowercased first - the convention every game name uses.",
                ApiVerbs.Schema(("text", "string", "the name to hash", true)),
                "{text, hash, hex, signed}",
                false, m =>
                {
                    var text = m.Text("text", 0, null);
                    if (string.IsNullOrEmpty(text)) throw new ApiRefused("text is empty");
                    uint h = JenkHash.GenHash(text.ToLowerInvariant());
                    return "{\"text\":" + DccBridgeProtocol.S(text) + ",\"hash\":" + h +
                           ",\"hex\":\"0x" + h.ToString("X8") + "\",\"signed\":" + (int)h + "}";
                });

            api.Add("api.fs.unhash",
                "The name behind a Jenkins hash, when the editor's string tables know it. Accepts decimal or 0x hex.",
                ApiVerbs.Schema(("hash", "string", "the hash, decimal or 0x hex", true)),
                "{hash, name|null}",
                false, m =>
                {
                    var raw = m.Text("hash", 0, "").Trim();
                    uint h;
                    if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!uint.TryParse(raw.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out h))
                            throw new ApiRefused("not a hash: " + raw);
                    }
                    else if (!uint.TryParse(raw, out h))
                    {
                        if (int.TryParse(raw, out var signed)) h = unchecked((uint)signed);
                        else throw new ApiRefused("not a hash: " + raw);
                    }
                    var name = JenkIndex.TryGetString(h);
                    return "{\"hash\":" + h + ",\"name\":" + (string.IsNullOrEmpty(name) ? "null" : DccBridgeProtocol.S(name)) + "}";
                });

            api.Add("api.fs.extract",
                "Pull files out of the archives to a folder, raw bytes as the game stores them; xml:true also writes the .xml conversion beside each. A job - follow its events.",
                ApiVerbs.Schema(("paths", "array", "archive paths of the files to extract", true),
                                ("out", "string", "folder on disk to write into (created if missing)", true),
                                ("xml", "boolean", "also convert each to XML where the format supports it", false)),
                "{job} then job-done {written:[...], failed:[{path, error}]}",
                false, m =>
                {
                    var paths = PathsOf_W2(m);
                    if (paths.Count == 0) throw new ApiRefused("no paths");
                    var outDir = m.Text("out", 1, "");
                    if (string.IsNullOrWhiteSpace(outDir)) throw new ApiRefused("no out folder");
                    bool xml = m.Flag("xml", 2, false);
                    RequireArchive_W2();
                    var game = gameFiles;
                    return ApiVerbs.StartedJson(api.StartJob(m, "api.fs.extract", job =>
                    {
                        Directory.CreateDirectory(outDir);
                        var written = new List<string>(); var failed = new List<(string p, string e)>();
                        for (int i = 0; i < paths.Count; i++)
                        {
                            if (job.CancelRequested) { job.Fail("cancelled after " + i + " of " + paths.Count); return; }
                            job.Percent = (i * 100) / paths.Count;
                            job.Note = Path.GetFileName(paths[i]);
                            try
                            {
                                if (!(game.Cache?.RpfMan?.GetEntry(paths[i]) is RpfFileEntry fe))
                                    throw new Exception("no file at that path");
                                var data = ExtractStandalone_W2(fe);
                                if (data == null) throw new Exception("could not be read");
                                var to = Path.Combine(outDir, fe.Name);
                                File.WriteAllBytes(to, data);
                                written.Add(to);
                                if (xml)
                                {
                                    try { written.Add(XmlIO.ExportFromArchive(game, paths[i], outDir)); }
                                    catch (Exception xe) { failed.Add((paths[i] + " (xml)", xe.Message)); }
                                }
                            }
                            catch (Exception ex) { failed.Add((paths[i], ex.Message)); }
                        }
                        job.Percent = 100;
                        var sb = new StringBuilder("{\"written\":[");
                        for (int i = 0; i < written.Count; i++) { if (i > 0) sb.Append(','); sb.Append(DccBridgeProtocol.S(written[i])); }
                        sb.Append("],\"failed\":[");
                        for (int i = 0; i < failed.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append("{\"path\":").Append(DccBridgeProtocol.S(failed[i].p))
                              .Append(",\"error\":").Append(DccBridgeProtocol.S(failed[i].e)).Append('}');
                        }
                        job.Finish(sb.Append("]}").ToString());
                    }));
                });

            api.Add("api.fs.convert_to_xml",
                "Convert loose game files on disk to their XML form, next to them or into a folder. Inline - use fs.extract with xml:true for archive files.",
                ApiVerbs.Schema(("paths", "array", "disk paths of .ydr/.ytyp/.ymap/... files", true),
                                ("out", "string", "folder to write into; each file's own folder when omitted", false)),
                "{written:[...], failed:[{path, error}]}",
                false, m => ConvertLoop_W2(PathsOf_W2(m), m.Text("out", 1, ""), true, m.Text("assets", 2, "")));

            api.Add("api.fs.convert_to_binary",
                "Convert .xml files back to game binaries (the .ytyp.xml naming carries the target type).",
                ApiVerbs.Schema(("paths", "array", "disk paths of *.<type>.xml files", true),
                                ("out", "string", "folder to write into; each file's own folder when omitted", false),
                                ("assets", "string", "texture/asset folder for types that embed them", false)),
                "{written:[...], failed:[{path, error}]}",
                false, m => ConvertLoop_W2(PathsOf_W2(m), m.Text("out", 1, ""), false, m.Text("assets", 2, "")));

            api.Add("api.lights.list",
                "Every light in the open scene, DCC-shaped: world position, colour 0..1, half-angles in degrees.",
                ApiVerbs.Schema(),
                "{count, lights:[{index, kind, pos, dir, colour, intensity, range, inner, outer, owner}]}",
                false, m =>
                {
                    var sb = new StringBuilder("{\"count\":").Append(scene.Lights.Count).Append(",\"lights\":[");
                    for (int i = 0; i < scene.Lights.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(BridgeLight_P5(i).ToJson());
                    }
                    return sb.Append("]}").ToString();
                });

            api.Add("api.lights.get",
                "One light in full: everything lights.list carries plus the raw fields (flags, time flags, corona, volume, fades).",
                ApiVerbs.Schema(("index", "integer", "the light's index from lights.list", true)),
                "{...lights.list entry, raw:{...}}",
                false, m =>
                {
                    int i = m.Int("index", 0, -1);
                    if (i < 0 || i >= scene.Lights.Count) throw new ApiRefused("no light " + i + " (there are " + scene.Lights.Count + ")");
                    var l = scene.Lights[i];
                    var d = BridgeLight_P5(i).ToJson();
                    var sb = new StringBuilder(d.Substring(0, d.Length - 1));
                    sb.Append(",\"raw\":{\"flags\":").Append(l.Flags)
                      .Append(",\"timeFlags\":").Append(l.TimeFlags)
                      .Append(",\"flashiness\":").Append((int)l.Flashiness)
                      .Append(",\"falloffExponent\":").Append(DccBridgeProtocol.N(l.FalloffExponent))
                      .Append(",\"coronaSize\":").Append(DccBridgeProtocol.N(l.CoronaSize))
                      .Append(",\"coronaIntensity\":").Append(DccBridgeProtocol.N(l.CoronaIntensity))
                      .Append(",\"coronaZBias\":").Append(DccBridgeProtocol.N(l.CoronaZBias))
                      .Append(",\"volumeIntensity\":").Append(DccBridgeProtocol.N(l.VolumeIntensity))
                      .Append(",\"volumeSizeScale\":").Append(DccBridgeProtocol.N(l.VolumeSizeScale))
                      .Append(",\"extent\":").Append(DccBridgeProtocol.V(new SharpDX.Vector3(l.Extent.X, l.Extent.Y, l.Extent.Z)))
                      .Append(",\"lightFadeDistance\":").Append(l.LightFadeDistance)
                      .Append(",\"shadowBlur\":").Append(l.ShadowBlur)
                      .Append(",\"projectedTexture\":").Append(l.ProjectedTextureHash)
                      .Append("}}");
                    return sb.ToString();
                });

            api.Add("api.materials.list",
                "The materials of one open file: shader, preset name, bucket, geometry count.",
                ApiVerbs.Schema(("file", "integer", "index into api.status files (default 0)", false)),
                "{file, count, materials:[{index, name, sps, bucket, meshes}]}",
                false, m =>
                {
                    var f = FileAt_W2(m.Int("file", 0, 0));
                    var mats = MaterialEditing.ForFile(scene, f);
                    var sb = new StringBuilder("{\"file\":").Append(DccBridgeProtocol.S(Path.GetFileName(f.Path ?? "")))
                        .Append(",\"count\":").Append(mats.Count).Append(",\"materials\":[");
                    for (int i = 0; i < mats.Count; i++)
                    {
                        var mt = mats[i];
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"index\":").Append(i)
                          .Append(",\"name\":").Append(DccBridgeProtocol.S(mt.Name))
                          .Append(",\"sps\":").Append(DccBridgeProtocol.S(mt.Sps))
                          .Append(",\"bucket\":").Append(mt.Bucket)
                          .Append(",\"meshes\":").Append(mt.Meshes.Count)
                          .Append('}');
                    }
                    return sb.Append("]}").ToString();
                });

            api.Add("api.materials.get",
                "One material in full: every parameter with its value, every texture slot with what it resolves to.",
                ApiVerbs.Schema(("index", "integer", "the material's index from materials.list", true),
                                ("file", "integer", "index into api.status files (default 0)", false)),
                "{name, sps, bucket, params:[{hash, name, value}], textures:[{hash, name, texture}]}",
                false, m =>
                {
                    var f = FileAt_W2(m.Int("file", 1, 0));
                    var mats = MaterialEditing.ForFile(scene, f);
                    int i = m.Int("index", 0, -1);
                    if (i < 0 || i >= mats.Count) throw new ApiRefused("no material " + i + " (there are " + mats.Count + ")");
                    var mt = mats[i];
                    var s = mt.Shader;
                    var sb = new StringBuilder("{\"name\":").Append(DccBridgeProtocol.S(mt.Name))
                        .Append(",\"sps\":").Append(DccBridgeProtocol.S(mt.Sps))
                        .Append(",\"bucket\":").Append(mt.Bucket)
                        .Append(",\"params\":[");
                    bool first = true;
                    foreach (var hash in MaterialEditing.ParamHashes(s))
                    {
                        if (!MaterialEditing.TryGetValue(s, hash, out var v)) continue;
                        if (!first) sb.Append(','); first = false;
                        sb.Append("{\"hash\":").Append(hash)
                          .Append(",\"name\":").Append(ParamName_W2(hash))
                          .Append(",\"value\":[").Append(DccBridgeProtocol.N(v.X)).Append(',').Append(DccBridgeProtocol.N(v.Y))
                          .Append(',').Append(DccBridgeProtocol.N(v.Z)).Append(',').Append(DccBridgeProtocol.N(v.W)).Append("]}");
                    }
                    sb.Append("],\"textures\":[");
                    first = true;
                    var plist = s?.ParametersList;
                    if (plist?.Parameters != null && plist.Hashes != null)
                        for (int p = 0; p < plist.Parameters.Length && p < plist.Hashes.Length; p++)
                        {
                            if (plist.Parameters[p].DataType != 0) continue;
                            if (!first) sb.Append(','); first = false;
                            var tex = plist.Parameters[p].Data as TextureBase;
                            sb.Append("{\"hash\":").Append((uint)plist.Hashes[p])
                              .Append(",\"name\":").Append(ParamName_W2((uint)plist.Hashes[p]))
                              .Append(",\"texture\":").Append(tex == null ? "null" : DccBridgeProtocol.S(tex.Name ?? ""))
                              .Append('}');
                        }
                    return sb.Append("]}").ToString();
                });

            api.Add("api.inspect.file",
                "Describe any game file without opening it in the editor: a disk path or an archive path. Models, archetypes, placements, textures, lights.",
                ApiVerbs.Schema(("path", "string", "a path on disk, or an archive path from fs.search_names", true)),
                "{kind, name, size, ...detail by type}",
                false, m => InspectFile_W2(m.Text("path", 0, "")));

            api.Add("api.mlo.get",
                "The open interior in one object: rooms, portals, entities, entity sets - the same numbers the .ytyp will carry.",
                ApiVerbs.Schema(),
                "{name, rooms:[...], portals:[...], entities:[...], sets:[...]}",
                false, m =>
                {
                    var s = Creator?.Session;
                    if (s == null) throw new ApiRefused("no interior - start or import one in the MLO workspace first");
                    var sb = new StringBuilder("{\"name\":").Append(DccBridgeProtocol.S(s.Name ?? "")).Append(",\"rooms\":[");
                    for (int i = 0; i < s.Rooms.Count; i++)
                    {
                        var r = s.Rooms[i];
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"index\":").Append(i).Append(",\"name\":").Append(DccBridgeProtocol.S(r.Name ?? ""))
                          .Append(",\"min\":").Append(DccBridgeProtocol.V(r.Min)).Append(",\"max\":").Append(DccBridgeProtocol.V(r.Max))
                          .Append(",\"flags\":").Append(r.Flags)
                          .Append(",\"timecycle\":").Append(DccBridgeProtocol.S(r.Timecycle ?? ""))
                          .Append('}');
                    }
                    sb.Append("],\"portals\":[");
                    for (int i = 0; i < s.Portals.Count; i++)
                    {
                        var p = s.Portals[i];
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"index\":").Append(i).Append(",\"from\":").Append(p.RoomFrom).Append(",\"to\":").Append(p.RoomTo)
                          .Append(",\"flags\":").Append(p.Flags).Append(",\"mirrorPriority\":").Append(p.MirrorPriority)
                          .Append(",\"opacity\":").Append(p.Opacity).Append(",\"corners\":[");
                        for (int k = 0; k < p.Corners.Length; k++) { if (k > 0) sb.Append(','); sb.Append(DccBridgeProtocol.V(p.Corners[k])); }
                        sb.Append("]}");
                    }
                    sb.Append("],\"entities\":[");
                    for (int i = 0; i < s.Entities.Count; i++)
                    {
                        var e = s.Entities[i];
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"index\":").Append(i).Append(",\"name\":").Append(DccBridgeProtocol.S(e.ArchetypeName ?? ""))
                          .Append(",\"pos\":").Append(DccBridgeProtocol.V(e.Position))
                          .Append(",\"rot\":").Append(DccBridgeProtocol.Q(e.Rotation))
                          .Append(",\"scale\":").Append(DccBridgeProtocol.V(e.Scale))
                          .Append(",\"room\":").Append(e.Room);
                        if (!string.IsNullOrEmpty(e.EntitySet)) sb.Append(",\"set\":").Append(DccBridgeProtocol.S(e.EntitySet));
                        sb.Append('}');
                    }
                    sb.Append("],\"sets\":[");
                    for (int i = 0; i < s.EntitySets.Count; i++) { if (i > 0) sb.Append(','); sb.Append(DccBridgeProtocol.S(s.EntitySets[i])); }
                    return sb.Append("]}").ToString();
                });
        }

        private static byte[] ExtractStandalone_W2(RpfFileEntry fe) =>
            fe == null ? null : ArchiveBrowser.ExtractForDisk(fe);

        private ArchiveBrowser RequireArchive_W2()
        {
            var a = panel?.Archive;
            if (a == null || !a.Ready)
                throw new ApiRefused(gameFiles?.Ready == true
                    ? "the archive index is still warming - retry shortly (api.status carries its state)"
                    : "no game folder is loaded - fs.* needs one (start with --gta, or set it in the app)");
            return a;
        }

        private RpfDirectoryEntry ResolveArchiveDir_W2(string path)
        {
            var rpfMan = gameFiles?.Cache?.RpfMan;
            if (rpfMan == null) return null;
            var entry = rpfMan.GetEntry(path);
            if (entry is RpfDirectoryEntry dir) return dir;
            if (entry is RpfFileEntry fe && fe.NameLower?.EndsWith(".rpf") == true)
                return rpfMan.FindRpfFile(fe.Path)?.Root;
            if (path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                return rpfMan.FindRpfFile(path)?.Root;
            return null;
        }

        private static List<string> PathsOf_W2(DccMessage m)
        {
            var list = new List<string>();
            if (m.Json && m.Root.ValueKind == JsonValueKind.Object)
            {
                if (m.Root.TryGetProperty("paths", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString());
                    return list;
                }
                if (m.Root.TryGetProperty("path", out var one) && one.ValueKind == JsonValueKind.String)
                { list.Add(one.GetString()); return list; }
            }
            var arg = m.Text("path", 0, "");
            if (!string.IsNullOrEmpty(arg)) list.Add(arg);
            return list;
        }

        private LoadedFile FileAt_W2(int index)
        {
            if (index < 0 || index >= scene.Files.Count)
                throw new ApiRefused("no open file " + index + " (there are " + scene.Files.Count + ")");
            return scene.Files[index];
        }

        private static string ParamName_W2(uint hash)
        {
            var name = JenkIndex.TryGetString(hash);
            return string.IsNullOrEmpty(name) ? "null" : DccBridgeProtocol.S(name);
        }

        private string InspectFile_W2(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ApiRefused("no path");
            byte[] data; string fileName; string source;
            RpfFileEntry entry = null;
            if (File.Exists(path))
            {
                data = File.ReadAllBytes(path);
                fileName = Path.GetFileName(path);
                source = "disk";
            }
            else
            {
                var rpfMan = gameFiles?.Cache?.RpfMan;
                if (rpfMan == null) throw new ApiRefused("no such file on disk, and no game folder is loaded to look in the archives");
                if (!(rpfMan.GetEntry(path.Replace('/', '\\')) is RpfFileEntry fe))
                    throw new ApiRefused("no file at '" + path + "' - on disk or in the archives");
                data = fe.File?.ExtractFile(fe) ?? throw new ApiRefused("the archive entry could not be read");
                entry = fe;
                fileName = fe.Name;
                source = "archive";
            }
            if (data.Length == 0) throw new ApiRefused(fileName + " is empty");

            var head = new StringBuilder("{\"name\":").Append(DccBridgeProtocol.S(fileName))
                .Append(",\"source\":").Append(DccBridgeProtocol.S(source))
                .Append(",\"size\":").Append(data.Length);

            CodeWalker.GameFiles.GameFile gf;
            try { gf = entry != null ? LoadFromEntry_W2(entry, data) : XmlIO.LoadResource(fileName, data); }
            catch (Exception ex)
            {
                return head.Append(",\"kind\":").Append(DccBridgeProtocol.S(Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant()))
                           .Append(",\"parsed\":false,\"error\":").Append(DccBridgeProtocol.S(ex.Message)).Append('}').ToString();
            }
            head.Append(",\"parsed\":true");

            switch (gf)
            {
                case YdrFile ydr:
                    return head.Append(",\"kind\":\"ydr\",\"drawable\":")
                               .Append(DrawableJson_W2(ydr.Drawable, ydr.Drawable?.LightAttributes?.data_items?.Length ?? 0))
                               .Append('}').ToString();
                case YftFile yft:
                    head.Append(",\"kind\":\"yft\",\"drawable\":")
                        .Append(DrawableJson_W2(yft.Fragment?.Drawable, yft.Fragment?.LightAttributes?.data_items?.Length ?? 0));
                    head.Append(",\"hasCloth\":").Append(yft.Fragment?.DrawableCloth != null ? "true" : "false");
                    return head.Append('}').ToString();
                case YtdFile ytd:
                {
                    var texs = ytd.TextureDict?.Textures?.data_items;
                    head.Append(",\"kind\":\"ytd\",\"textures\":[");
                    for (int i = 0; texs != null && i < texs.Length; i++)
                    {
                        var t = texs[i];
                        if (i > 0) head.Append(',');
                        head.Append("{\"name\":").Append(DccBridgeProtocol.S(t?.Name ?? ""))
                            .Append(",\"width\":").Append(t?.Width ?? 0).Append(",\"height\":").Append(t?.Height ?? 0)
                            .Append(",\"format\":").Append(DccBridgeProtocol.S(t?.Format.ToString() ?? ""))
                            .Append(",\"mips\":").Append(t?.Levels ?? 0).Append('}');
                    }
                    return head.Append("]}").ToString();
                }
                case YtypFile ytyp:
                {
                    var archs = ytyp.AllArchetypes;
                    head.Append(",\"kind\":\"ytyp\",\"archetypeCount\":").Append(archs?.Length ?? 0).Append(",\"archetypes\":[");
                    for (int i = 0; archs != null && i < archs.Length; i++)
                    {
                        var a = archs[i];
                        if (a == null) continue;
                        if (i > 0) head.Append(',');
                        head.Append("{\"name\":").Append(DccBridgeProtocol.S(a.Name ?? ""))
                            .Append(",\"assetName\":").Append(DccBridgeProtocol.S(a.AssetName ?? ""))
                            .Append(",\"type\":").Append(DccBridgeProtocol.S(a.Type.ToString()))
                            .Append(",\"lodDist\":").Append(DccBridgeProtocol.N(a.LodDist))
                            .Append(",\"textureDict\":").Append(DccBridgeProtocol.S(a.TextureDict.ToString()))
                            .Append(",\"bbMin\":").Append(DccBridgeProtocol.V(a.BBMin))
                            .Append(",\"bbMax\":").Append(DccBridgeProtocol.V(a.BBMax));
                        if (a is MloArchetype mlo)
                            head.Append(",\"mlo\":{\"rooms\":").Append(mlo.rooms?.Length ?? 0)
                                .Append(",\"portals\":").Append(mlo.portals?.Length ?? 0)
                                .Append(",\"entities\":").Append(mlo.entities?.Length ?? 0)
                                .Append(",\"entitySets\":").Append(mlo.entitySets?.Length ?? 0).Append('}');
                        head.Append('}');
                    }
                    return head.Append("]}").ToString();
                }
                case YmapFile ymap:
                {
                    var ents = ymap.AllEntities;
                    head.Append(",\"kind\":\"ymap\",\"entityCount\":").Append(ents?.Length ?? 0);
                    var cm = ymap.CMapData;
                    head.Append(",\"extentsMin\":").Append(DccBridgeProtocol.V(cm.entitiesExtentsMin))
                        .Append(",\"extentsMax\":").Append(DccBridgeProtocol.V(cm.entitiesExtentsMax))
                        .Append(",\"parent\":").Append(DccBridgeProtocol.S(cm.parent.ToString()))
                        .Append(",\"entities\":[");
                    int max = Math.Min(ents?.Length ?? 0, 200);
                    for (int i = 0; i < max; i++)
                    {
                        var e = ents[i];
                        if (i > 0) head.Append(',');
                        head.Append("{\"archetype\":").Append(DccBridgeProtocol.S(e._CEntityDef.archetypeName.ToString()))
                            .Append(",\"pos\":").Append(DccBridgeProtocol.V(e.Position))
                            .Append(",\"lodDist\":").Append(DccBridgeProtocol.N(e._CEntityDef.lodDist))
                            .Append('}');
                    }
                    head.Append(']');
                    if ((ents?.Length ?? 0) > max) head.Append(",\"truncated\":").Append(ents.Length - max);
                    return head.Append('}').ToString();
                }
                default:
                    return head.Append(",\"kind\":").Append(DccBridgeProtocol.S(Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant()))
                               .Append(",\"note\":\"parsed, but this type has no detailed summary yet\"}").ToString();
            }
        }

        private static CodeWalker.GameFiles.GameFile LoadFromEntry_W2(RpfFileEntry entry, byte[] data)
        {
            CodeWalker.GameFiles.GameFile file = Path.GetExtension(entry.Name ?? "").ToLowerInvariant() switch
            {
                ".ydr" => new YdrFile(),
                ".yft" => new YftFile(),
                ".ydd" => new YddFile(),
                ".ytd" => new YtdFile(),
                ".ytyp" => new YtypFile(),
                ".ymap" => new YmapFile(),
                ".ybn" => new YbnFile(),
                ".ynv" => new YnvFile(),
                ".ypt" => new YptFile(),
                ".ycd" => new YcdFile(),
                _ => null,
            };
            if (file == null) throw new Exception("no reader for this file type");
            ((PackedFile)file).Load(data, entry);
            return file;
        }

        private static string DrawableJson_W2(DrawableBase d, int lightCount)
        {
            if (d == null) return "null";
            var sb = new StringBuilder("{\"name\":").Append(DccBridgeProtocol.S((d as Drawable)?.Name ?? ""));
            sb.Append(",\"bbMin\":").Append(DccBridgeProtocol.V(new SharpDX.Vector3(d.BoundingBoxMin.X, d.BoundingBoxMin.Y, d.BoundingBoxMin.Z)))
              .Append(",\"bbMax\":").Append(DccBridgeProtocol.V(new SharpDX.Vector3(d.BoundingBoxMax.X, d.BoundingBoxMax.Y, d.BoundingBoxMax.Z)));

            int models = 0, geoms = 0, verts = 0, tris = 0;
            var lods = Rendering.ModelRenderer.HighestLod(d);
            if (lods != null)
                foreach (var mdl in lods)
                {
                    if (mdl?.Geometries == null) continue;
                    models++;
                    foreach (var g in mdl.Geometries)
                    {
                        geoms++;
                        verts += (int)(g?.VertexData?.VertexCount ?? 0);
                        tris += (g?.IndexBuffer?.Indices?.Length ?? 0) / 3;
                    }
                }
            sb.Append(",\"models\":").Append(models).Append(",\"geometries\":").Append(geoms)
              .Append(",\"vertices\":").Append(verts).Append(",\"triangles\":").Append(tris);

            sb.Append(",\"shaders\":[");
            var shaders = d.ShaderGroup?.Shaders?.data_items;
            for (int i = 0; shaders != null && i < shaders.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"index\":").Append(i)
                  .Append(",\"name\":").Append(DccBridgeProtocol.S(shaders[i].Name.ToString()))
                  .Append(",\"sps\":").Append(DccBridgeProtocol.S(shaders[i].FileName.ToString()))
                  .Append(",\"bucket\":").Append(shaders[i].RenderBucket).Append('}');
            }
            sb.Append(']');

            sb.Append(",\"lights\":").Append(lightCount);
            var embedded = d.ShaderGroup?.TextureDictionary?.Textures?.data_items;
            sb.Append(",\"embeddedTextures\":").Append(embedded?.Length ?? 0);
            return sb.Append('}').ToString();
        }

        private static string ConvertLoop_W2(List<string> paths, string outDir, bool toXml, string assetFolder)
        {
            if (paths.Count == 0) throw new ApiRefused("no paths");
            var written = new List<string>(); var failed = new List<(string p, string e)>();
            foreach (var p in paths)
            {
                try
                {
                    var into = string.IsNullOrWhiteSpace(outDir) ? Path.GetDirectoryName(Path.GetFullPath(p)) : outDir;
                    Directory.CreateDirectory(into);
                    written.Add(toXml ? XmlIO.ExportBinaryFile(p, into)
                                      : XmlIO.Import(p, into, string.IsNullOrWhiteSpace(assetFolder) ? null : assetFolder));
                }
                catch (Exception ex) { failed.Add((p, ex.Message)); }
            }
            var sb = new StringBuilder("{\"written\":[");
            for (int i = 0; i < written.Count; i++) { if (i > 0) sb.Append(','); sb.Append(DccBridgeProtocol.S(written[i])); }
            sb.Append("],\"failed\":[");
            for (int i = 0; i < failed.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"path\":").Append(DccBridgeProtocol.S(failed[i].p))
                  .Append(",\"error\":").Append(DccBridgeProtocol.S(failed[i].e)).Append('}');
            }
            return sb.Append("]}").ToString();
        }
    }
}

