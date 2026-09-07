using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using CodeWalker.GameFiles;
using RageLightEditor.Editor;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_W2(Action<string, bool, string> check)
        {
            var ui = Creator;
            var was = panel.Workspace;
            TcpClient tcp = null;
            string tempDir = Path.Combine(Path.GetTempPath(), "rle_api_w2");
            try
            {
                var api = Api_W1();
                int port = StartMloBridge(ui, 0);
                if (port <= 0) { check("api2: listening", false, ui.BridgeStatus); return; }
                tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                tcp.ReceiveTimeout = 5000;
                var stream = tcp.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                var reader = new StreamReader(stream, Encoding.UTF8);

                void Pump(Func<bool> until)
                {
                    for (int i = 0; i < 1200 && !until(); i++) { TickMloBridge(ui); Tick_W1(); Thread.Sleep(5); }
                }
                var strays = new List<string>();
                string Ask(string line)
                {
                    int n = mloBridge.Received;
                    writer.WriteLine(line);
                    Pump(() => mloBridge.Received > n);
                    try
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            var l = reader.ReadLine();
                            if (l == null) return null;
                            if (l.Contains("\"type\":\"event\"")) { strays.Add(l); continue; }
                            return l;
                        }
                    }
                    catch { }
                    return null;
                }
                JsonElement Result(string reply)
                {
                    using var doc = JsonDocument.Parse(reply);
                    return doc.RootElement.GetProperty("result").Clone();
                }
                static int IntAt(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;
                static string TextAt(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                static bool FlagAt(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
                static int LenAt(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : -1;

                var hashed = Ask("{\"cmd\":\"api.fs.hash\",\"text\":\"Adder\",\"id\":1}");
                uint want = JenkHash.GenHash("adder");
                check("api2: fs.hash lowercases and hashes", hashed != null && hashed.Contains("\"hash\":" + want)
                      && hashed.Contains("0x" + want.ToString("X8")), hashed ?? "(no reply)");
                JenkIndex.Ensure("api_w2_test_name");
                uint testHash = JenkHash.GenHash("api_w2_test_name");
                var unhashed = Ask("{\"cmd\":\"api.fs.unhash\",\"hash\":\"0x" + testHash.ToString("X8") + "\",\"id\":2}");
                check("api2: fs.unhash finds a known name from hex", unhashed != null && unhashed.Contains("api_w2_test_name"), unhashed ?? "(no reply)");
                var nameless = Ask("{\"cmd\":\"api.fs.unhash\",\"hash\":\"305419896\",\"id\":3}");
                check("api2: an unknown hash answers null, not an error",
                      nameless != null && nameless.Contains("\"ok\":true") && nameless.Contains("\"name\":null"), nameless ?? "(no reply)");

                var lights = Ask("{\"cmd\":\"api.lights.list\",\"id\":4}");
                check("api2: lights.list counts what the scene has",
                      lights != null && lights.Contains("\"count\":" + scene.Lights.Count), lights == null ? "(no reply)" : lights.Substring(0, Math.Min(120, lights.Length)));
                if (scene.Lights.Count > 0)
                {
                    var one = Ask("{\"cmd\":\"api.lights.get\",\"index\":0,\"id\":5}");
                    check("api2: lights.get carries the DCC shape and the raw block",
                          one != null && one.Contains("\"kind\"") && one.Contains("\"raw\"") && one.Contains("\"timeFlags\""),
                          one == null ? "(no reply)" : one.Substring(0, Math.Min(160, one.Length)));
                }
                var badLight = Ask("{\"cmd\":\"api.lights.get\",\"index\":9999,\"id\":6}");
                check("api2: a light that does not exist is refused with the count",
                      badLight != null && badLight.Contains("\"ok\":false") && badLight.Contains("there are"), badLight ?? "(no reply)");

                if (scene.Files.Count > 0)
                {
                    var mats = Ask("{\"cmd\":\"api.materials.list\",\"id\":7}");
                    bool listed = mats != null && mats.Contains("\"ok\":true") && mats.Contains("\"materials\"");
                    check("api2: materials.list answers for the open file", listed, mats == null ? "(no reply)" : mats.Substring(0, Math.Min(160, mats.Length)));
                    if (listed && Result(mats).GetProperty("count").GetInt32() > 0)
                    {
                        var mat = Ask("{\"cmd\":\"api.materials.get\",\"index\":0,\"id\":8}");
                        bool okMat = mat != null && mat.Contains("\"params\"") && mat.Contains("\"textures\"");
                        if (okMat)
                        {
                            var ps = Result(mat).GetProperty("params");
                            okMat = ps.ValueKind == JsonValueKind.Array &&
                                    (ps.GetArrayLength() == 0 || ps[0].GetProperty("value").GetArrayLength() == 4);
                        }
                        check("api2: materials.get carries params as [x,y,z,w] and the texture slots", okMat,
                              mat == null ? "(no reply)" : mat.Substring(0, Math.Min(160, mat.Length)));
                    }
                }

                panel.SwitchWorkspace(LightPanel.Space.Mlo);
                if (ui.Session == null && scene.HasModel) ui.Session = MloCreatorSession.FromScene(scene);
                var s = ui.Session;
                if (s != null)
                {
                    if (s.Rooms.Count == 0) s.AddRoom("api_w2_room", new SharpDX.Vector3(-2, -2, 0), new SharpDX.Vector3(2, 2, 3));
                    var mlo = Ask("{\"cmd\":\"api.mlo.get\",\"id\":9}");
                    bool okMlo = mlo != null && mlo.Contains("\"ok\":true");
                    if (okMlo)
                    {
                        var r = Result(mlo);
                        okMlo = r.GetProperty("rooms").GetArrayLength() == s.Rooms.Count
                             && r.GetProperty("portals").GetArrayLength() == s.Portals.Count
                             && r.GetProperty("entities").GetArrayLength() == s.Entities.Count;
                    }
                    check("api2: mlo.get mirrors the session's rooms, portals and entities", okMlo,
                          mlo == null ? "(no reply)" : mlo.Substring(0, Math.Min(160, mlo.Length)));
                }

                {
                    var onDisk = scene.Files.FirstOrDefault(f => !string.IsNullOrEmpty(f.Path) && File.Exists(f.Path))?.Path;
                    if (onDisk != null)
                    {
                        var ins = Ask("{\"cmd\":\"api.inspect.file\",\"path\":" + DccBridgeProtocol.S(onDisk) + ",\"id\":20}");
                        bool okIns = ins != null && ins.Contains("\"ok\":true");
                        if (okIns)
                        {
                            var r = Result(ins);
                            okIns = FlagAt(r, "parsed") && TextAt(r, "source") == "disk"
                                 && r.TryGetProperty("drawable", out var dr) && IntAt(dr, "triangles") > 0;
                        }
                        check("api2: inspect.file describes a drawable on disk", okIns,
                              ins == null ? "(no reply)" : ins.Substring(0, Math.Min(200, ins.Length)));
                    }
                    var missing = Ask("{\"cmd\":\"api.inspect.file\",\"path\":\"C:\\\\nope\\\\nothing.ydr\",\"id\":21}");
                    check("api2: inspect.file refuses a path that is nowhere",
                          missing != null && missing.Contains("\"ok\":false"), missing ?? "(no reply)");
                }

                bool haveGame = !string.IsNullOrEmpty(gameFiles?.Folder);
                if (haveGame)
                {
                    Pump(() => gameFiles.Ready || !string.IsNullOrEmpty(gameFiles.Error));
                    if (gameFiles.Ready && panel.Archive?.Ready != true)
                        BuildArchiveIndex_S3(gameFiles.Cache?.RpfMan, true);
                }
                if (panel.Archive?.Ready == true)
                {
                    var found = Ask("{\"cmd\":\"api.fs.search_names\",\"query\":\"adder\",\"ext\":\".yft\",\"id\":10}");
                    bool okFound = found != null && found.Contains("\"ok\":true");
                    string firstPath = null;
                    if (okFound)
                    {
                        var r = Result(found);
                        okFound = r.GetProperty("total").GetInt32() >= 1 && r.GetProperty("matches").GetArrayLength() >= 1;
                        if (okFound) firstPath = r.GetProperty("matches")[0].GetProperty("path").GetString();
                    }
                    check("api2: fs.search_names finds adder.yft in the index", okFound && !string.IsNullOrEmpty(firstPath),
                          found == null ? "(no reply)" : found.Substring(0, Math.Min(160, found.Length)));

                    var roots = Ask("{\"cmd\":\"api.fs.list_dir\",\"id\":11}");
                    bool okRoots = roots != null && roots.Contains("\"ok\":true") && Result(roots).GetProperty("dirs").GetArrayLength() > 0;
                    string firstRoot = okRoots ? Result(roots).GetProperty("dirs")[0].GetString() : null;
                    check("api2: fs.list_dir with no path lists the top archives", okRoots, roots == null ? "(no reply)" : roots.Substring(0, Math.Min(140, roots.Length)));
                    if (okRoots)
                    {
                        var inside = Ask("{\"cmd\":\"api.fs.list_dir\",\"path\":" + DccBridgeProtocol.S(firstRoot) + ",\"id\":12}");
                        bool okInside = inside != null && inside.Contains("\"ok\":true");
                        if (okInside)
                        {
                            var r = Result(inside);
                            okInside = r.GetProperty("dirs").GetArrayLength() + r.GetProperty("files").GetArrayLength() > 0;
                        }
                        check("api2: fs.list_dir walks into an archive", okInside, inside == null ? "(no reply)" : inside.Substring(0, Math.Min(140, inside.Length)));
                    }

                    if (!string.IsNullOrEmpty(firstPath))
                    {
                        var ins = Ask("{\"cmd\":\"api.inspect.file\",\"path\":" + DccBridgeProtocol.S(firstPath) + ",\"id\":22}");
                        bool okIns = ins != null && ins.Contains("\"ok\":true");
                        if (okIns)
                        {
                            var r = Result(ins);
                            okIns = TextAt(r, "source") == "archive" && FlagAt(r, "parsed")
                                 && r.TryGetProperty("drawable", out var dr) && LenAt(dr, "shaders") > 0;
                        }
                        check("api2: inspect.file reads an archive path directly", okIns,
                              ins == null ? "(no reply)" : ins.Substring(0, Math.Min(200, ins.Length)));
                    }

                    var ytyps = Ask("{\"cmd\":\"api.fs.search_names\",\"query\":\"v_int_\",\"ext\":\".ytyp\",\"max\":1,\"id\":13}");
                    string ytypPath = ytyps != null && ytyps.Contains("\"ok\":true") && Result(ytyps).GetProperty("matches").GetArrayLength() > 0
                        ? Result(ytyps).GetProperty("matches")[0].GetProperty("path").GetString() : null;
                    if (ytypPath == null && firstPath != null) ytypPath = firstPath;
                    if (ytypPath != null && ytypPath.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase))
                    {
                        var ins = Ask("{\"cmd\":\"api.inspect.file\",\"path\":" + DccBridgeProtocol.S(ytypPath) + ",\"id\":23}");
                        bool okYtyp = ins != null && ins.Contains("\"ok\":true");
                        if (okYtyp)
                        {
                            var r = Result(ins);
                            okYtyp = TextAt(r, "kind") == "ytyp" && IntAt(r, "archetypeCount") > 0;
                        }
                        check("api2: inspect.file lists a .ytyp's archetypes (mlo rooms/portals when it has them)", okYtyp,
                              ins == null ? "(no reply)" : ins.Substring(0, Math.Min(200, ins.Length)));
                    }
                    if (ytypPath != null)
                    {
                        try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                        var started = Ask("{\"cmd\":\"api.fs.extract\",\"paths\":[" + DccBridgeProtocol.S(ytypPath) + "],\"out\":" + DccBridgeProtocol.S(tempDir) + ",\"xml\":true,\"id\":14}");
                        int jobId = started != null && started.Contains("\"job\":") ? Result(started).GetProperty("job").GetInt32() : -1;
                        var events = new List<string>();
                        var job = Api_W1().FindJob(jobId);
                        Pump(() => job != null && job.Done && job.Announced);
                        events.AddRange(strays); strays.Clear();
                        bool IsDone(string l) => l != null && l.Contains("\"event\":\"job-done\"") && l.Contains("\"job\":" + jobId);
                        for (int i = 0; i < 64 && !events.Any(IsDone); i++)
                        {
                            string l;
                            try { l = reader.ReadLine(); } catch { break; }
                            if (l == null) break;
                            events.Add(l);
                        }
                        var done = events.FirstOrDefault(IsDone);
                        bool okDone = done != null && done.Contains("\"ok\":true") && done.Contains("\"written\"");
                        string binPath = okDone ? Path.Combine(tempDir, Path.GetFileName(ytypPath.Replace('\\', Path.DirectorySeparatorChar))) : null;
                        check("api2: fs.extract writes the raw file and its xml",
                              okDone && File.Exists(binPath) && Directory.GetFiles(tempDir, "*.xml").Length > 0,
                              done ?? "(no done event)");

                        var xmls = okDone ? Directory.GetFiles(tempDir, "*.xml") : Array.Empty<string>();
                        if (xmls.Length > 0)
                        {
                            var back = Ask("{\"cmd\":\"api.fs.convert_to_binary\",\"paths\":[" + DccBridgeProtocol.S(xmls[0]) + "],\"out\":" + DccBridgeProtocol.S(Path.Combine(tempDir, "back")) + ",\"id\":15}");
                            bool okBack = back != null && back.Contains("\"ok\":true");
                            if (okBack)
                            {
                                var written = Result(back).GetProperty("written");
                                okBack = written.GetArrayLength() == 1 && File.Exists(written[0].GetString()) && new FileInfo(written[0].GetString()).Length > 0;
                            }
                            check("api2: fs.convert_to_binary turns the xml back into a game file", okBack, back ?? "(no reply)");
                        }
                    }
                }
                else
                {
                    var refused = Ask("{\"cmd\":\"api.fs.search_names\",\"query\":\"adder\",\"id\":16}");
                    check("api2: without a game folder the archive verbs refuse with the reason",
                          refused != null && refused.Contains("\"ok\":false") && (refused.Contains("game folder") || refused.Contains("warming")),
                          refused ?? "(no reply)");
                    check("api2: archive round-trip skipped (no game folder given)", true, "run with --gta for the full pass");
                }
            }
            catch (Exception ex)
            {
                check("api2: no exception", false, ex.ToString());
            }
            finally
            {
                try { tcp?.Close(); } catch { }
                try { StopMloBridge(ui); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                if (screenshotPath == null) panel.SwitchWorkspace(was);
            }
        }
    }
}

