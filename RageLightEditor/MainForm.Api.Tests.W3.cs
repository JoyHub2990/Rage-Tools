using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using RageLightEditor.Editor;
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        partial void SeqTest_W3(Action<string, bool, string> check)
        {
            var ui = Creator;
            var was = panel.Workspace;
            var wasHour = panel.PreviewHour;
            TcpClient tcp = null;
            string outDir = Path.Combine(Path.GetTempPath(), "rle_api_w3");
            try
            {
                int port = StartMloBridge(ui, 0);
                if (port <= 0) { check("api3: listening", false, ui.BridgeStatus); return; }
                tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                tcp.ReceiveTimeout = 8000;
                var stream = tcp.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                var reader = new StreamReader(stream, Encoding.UTF8);

                var strays = new List<string>();
                void Pump(Func<bool> until)
                {
                    for (int i = 0; i < 900 && !until(); i++)
                    {
                        TickMloBridge(ui);
                        ServiceApiRender_W3();
                        Tick_W1();
                        Thread.Sleep(2);
                    }
                }
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
                static float NumAt(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : float.NaN;
                var api = Api_W1();
                string AwaitJob(string startReply, out JsonElement result)
                {
                    result = default;
                    if (startReply == null || !startReply.Contains("\"job\":")) return "no job in the reply";
                    int id = Result(startReply).GetProperty("job").GetInt32();
                    var job = api.FindJob(id);
                    Pump(() => job != null && job.Done && job.Announced);
                    if (job == null || !job.Done) return "the job never finished";

                    var seen = new List<string>(strays);
                    strays.Clear();
                    bool IsDone(string l) => l != null && l.Contains("\"event\":\"job-done\"") && l.Contains("\"job\":" + id);
                    tcp.ReceiveTimeout = 3000;
                    for (int i = 0; i < 64 && !seen.Any(IsDone); i++)
                    {
                        string l;
                        try { l = reader.ReadLine(); } catch { break; }
                        if (l == null) break;
                        seen.Add(l);
                    }
                    tcp.ReceiveTimeout = 8000;
                    var done = seen.FirstOrDefault(IsDone);
                    if (done == null) return "the job finished but its event never arrived";
                    using var doc = JsonDocument.Parse(done);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                        return root.TryGetProperty("error", out var e) ? e.GetString() : "the job failed";
                    result = root.GetProperty("result").Clone();
                    return null;
                }

                try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
                Directory.CreateDirectory(outDir);

                var set = Ask("{\"cmd\":\"api.camera.set\",\"target\":[1,2,3],\"distance\":7.5,\"yaw\":45,\"pitch\":30,\"fov\":55,\"id\":1}");
                bool okCam = set != null && set.Contains("\"ok\":true");
                if (okCam)
                {
                    var r = Result(set);
                    okCam = Math.Abs(NumAt(r, "distance") - 7.5f) < 0.01f
                         && Math.Abs(NumAt(r, "yaw") - 45.0f) < 0.1f
                         && Math.Abs(NumAt(r, "pitch") - 30.0f) < 0.1f
                         && Math.Abs(NumAt(r, "fov") - 55.0f) < 0.1f;
                }
                check("api3: camera.set puts it exactly where it was told", okCam, set ?? "(no reply)");
                var got = Ask("{\"cmd\":\"api.camera.get\",\"id\":2}");
                bool okGet = got != null && got.Contains("\"eye\"");
                if (okGet)
                {
                    var r = Result(got);
                    var eye = new Vector3(r.GetProperty("eye")[0].GetSingle(), r.GetProperty("eye")[1].GetSingle(), r.GetProperty("eye")[2].GetSingle());
                    var tgt = new Vector3(r.GetProperty("target")[0].GetSingle(), r.GetProperty("target")[1].GetSingle(), r.GetProperty("target")[2].GetSingle());
                    okGet = Math.Abs((eye - tgt).Length() - 7.5f) < 0.2f;
                }
                check("api3: camera.get reports an eye that really is that far off the target", okGet, got ?? "(no reply)");

                var hour = Ask("{\"cmd\":\"api.time.set\",\"hour\":21.5,\"id\":3}");
                check("api3: time.set moves the preview clock",
                      hour != null && hour.Contains("\"ok\":true") && Math.Abs(panel.PreviewHour - 21.5f) < 0.01f,
                      hour ?? "(no reply)");
                var badHour = Ask("{\"cmd\":\"api.time.set\",\"hour\":49,\"id\":4}");
                check("api3: an hour off the clock is refused", badHour != null && badHour.Contains("\"ok\":false"), badHour ?? "(no reply)");

                var onDisk = scene.Files.FirstOrDefault(f => !string.IsNullOrEmpty(f.Path) && File.Exists(f.Path))?.Path;
                if (onDisk != null)
                {
                    var opened = Ask("{\"cmd\":\"api.open_file\",\"path\":" + DccBridgeProtocol.S(onDisk) + ",\"id\":5}");
                    check("api3: open_file loads a model and says what is in it",
                          opened != null && opened.Contains("\"ok\":true") && scene.HasModel,
                          opened ?? "(no reply)");
                }
                var noFile = Ask("{\"cmd\":\"api.open_file\",\"path\":\"C:\\\\nope\\\\nothing.ydr\",\"id\":6}");
                check("api3: open_file refuses a path that is not there",
                      noFile != null && noFile.Contains("\"ok\":false"), noFile ?? "(no reply)");

                if (scene.HasModel)
                {
                    var framed = Ask("{\"cmd\":\"api.camera.frame\",\"id\":7}");
                    check("api3: camera.frame puts the subject in view",
                          framed != null && framed.Contains("\"ok\":true") && camera.Distance > 0.0f, framed ?? "(no reply)");

                    var shot = Path.Combine(outDir, "view.png");
                    var start = Ask("{\"cmd\":\"api.render.view\",\"width\":320,\"height\":240,\"out\":" + DccBridgeProtocol.S(shot) + ",\"id\":8}");
                    var err = AwaitJob(start, out var res);
                    bool wrote = err == null && File.Exists(shot) && new FileInfo(shot).Length > 1024;
                    int pw = 0, ph = 0;
                    if (wrote)
                    {
                        using var img = System.Drawing.Image.FromFile(shot);
                        pw = img.Width; ph = img.Height;
                    }
                    check("api3: render.view writes a real PNG at the size asked for",
                          wrote && pw == 320 && ph == 240,
                          err ?? (wrote ? $"{pw}x{ph}, {new FileInfo(shot).Length} bytes" : "no file"));

                    if (wrote)
                    {
                        using var img = new System.Drawing.Bitmap(shot);
                        var seen = new HashSet<int>();
                        for (int y = 0; y < img.Height; y += 4)
                            for (int x = 0; x < img.Width; x += 4)
                                seen.Add(img.GetPixel(x, y).ToArgb());
                        check("api3: ...and it has a rendered scene in it, not one flat colour", seen.Count > 8, seen.Count + " distinct colours");
                    }
                }

                if (onDisk != null)
                {
                    var propShot = Path.Combine(outDir, "prop.png");
                    var start = Ask("{\"cmd\":\"api.render.prop\",\"name\":" + DccBridgeProtocol.S(onDisk) +
                                    ",\"width\":256,\"height\":256,\"hour\":12,\"out\":" + DccBridgeProtocol.S(propShot) + ",\"keep\":true,\"id\":9}");
                    var err = AwaitJob(start, out var res);
                    bool wrote = err == null && File.Exists(propShot) && new FileInfo(propShot).Length > 1024;
                    check("api3: render.prop opens a prop by path, frames it and photographs it", wrote,
                          err ?? (wrote ? new FileInfo(propShot).Length + " bytes" : "no file"));
                    if (wrote)
                    {
                        using var img = new System.Drawing.Bitmap(propShot);
                        var seen = new HashSet<int>();
                        for (int y = 0; y < img.Height; y += 4)
                            for (int x = 0; x < img.Width; x += 4)
                                seen.Add(img.GetPixel(x, y).ToArgb());
                        check("api3: ...and the prop is in the picture, not an empty frame", seen.Count > 8, seen.Count + " distinct colours");
                    }
                }

                var missingProp = Ask("{\"cmd\":\"api.render.prop\",\"name\":\"no_such_prop_xyz\",\"id\":10}");
                check("api3: a prop that exists nowhere is refused rather than guessed",
                      missingProp != null && missingProp.Contains("\"ok\":false"), missingProp ?? "(no reply)");

                if (scene.HasModel)
                {
                    var start = Ask("{\"cmd\":\"api.render.orbit\",\"views\":3,\"width\":160,\"height\":120,\"out\":" + DccBridgeProtocol.S(outDir) + ",\"id\":11}");
                    var err = AwaitJob(start, out var res);
                    int made = 0;
                    if (err == null && res.TryGetProperty("paths", out var paths))
                        foreach (var p in paths.EnumerateArray())
                            if (File.Exists(p.GetString()) && new FileInfo(p.GetString()).Length > 512) made++;
                    check("api3: render.orbit writes one PNG per view", err == null && made == 3, err ?? made + " of 3 written");
                }

                check("api3: --serve is off in a normal run", !ServeMode_W3, ServeMode_W3 ? "on" : "off");
            }
            catch (Exception ex)
            {
                check("api3: no exception", false, ex.ToString());
            }
            finally
            {
                try { tcp?.Close(); } catch { }
                try { StopMloBridge(ui); } catch { }
                try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
                panel.PreviewHour = wasHour;
                if (screenshotPath == null) panel.SwitchWorkspace(was);
            }
        }
    }
}

