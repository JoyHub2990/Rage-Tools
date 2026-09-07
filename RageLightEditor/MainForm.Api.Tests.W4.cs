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
using SharpDX;

namespace RageLightEditor
{
    public partial class MainForm
    {
        private static bool NamesObfuscated_V21 =>
            typeof(Editor.Scene).Name != "Scene" || typeof(Editor.LightPanel).Name != "LightPanel";

        partial void SeqTest_W4(Action<string, bool, string> check)
        {
            if (NamesObfuscated_V21)
            {
                Console.WriteLine("  api4: (state.* checks skipped - this build's type names are obfuscated, " +
                                  "so a by-name object walk cannot resolve anything. See NamesObfuscated_V21.)");
                return;
            }
            var ui = Creator;
            var was = panel.Workspace;
            var wasHour = panel.PreviewHour;
            TcpClient tcp = null;
            try
            {
                int port = StartMloBridge(ui, 0);
                if (port <= 0) { check("api4: listening", false, ui.BridgeStatus); return; }
                tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                tcp.ReceiveTimeout = 8000;
                var stream = tcp.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                var reader = new StreamReader(stream, Encoding.UTF8);

                string Ask(string line)
                {
                    int n = mloBridge.Received;
                    writer.WriteLine(line);
                    for (int i = 0; i < 600 && mloBridge.Received <= n; i++) { TickMloBridge(ui); Tick_W1(); Thread.Sleep(2); }
                    try
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            var l = reader.ReadLine();
                            if (l == null) return null;
                            if (l.Contains("\"type\":\"event\"")) continue;
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
                bool Ok(string reply) => reply != null && reply.Contains("\"ok\":true");

                var roots = Ask("{\"cmd\":\"api.state.roots\",\"id\":1}");
                bool okRoots = Ok(roots);
                if (okRoots)
                {
                    var names = Result(roots).GetProperty("roots").EnumerateArray()
                        .Select(r => r.GetProperty("name").GetString()).ToList();
                    okRoots = names.Contains("scene") && names.Contains("session") && names.Contains("form") && names.Contains("panel");
                }
                check("api4: state.roots names the ways in", okRoots, roots == null ? "(no reply)" : roots.Substring(0, Math.Min(150, roots.Length)));

                var listed = Ask("{\"cmd\":\"api.state.list\",\"path\":\"scene\",\"filter\":\"Light\",\"id\":2}");
                bool okList = Ok(listed);
                if (okList)
                {
                    var r = Result(listed);
                    var props = r.GetProperty("properties").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
                    var fields = r.GetProperty("fields").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
                    var methods = r.GetProperty("methods").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
                    okList = (props.Contains("Lights") || fields.Contains("Lights")) && methods.Count > 0;
                }
                check("api4: state.list finds the members behind a filter", okList,
                      listed == null ? "(no reply)" : listed.Substring(0, Math.Min(150, listed.Length)));

                if (scene.Lights.Count > 0)
                {
                    var got = Ask("{\"cmd\":\"api.state.get\",\"path\":\"scene.Lights[0].Falloff\",\"id\":3}");
                    bool okGet = Ok(got) && Math.Abs(Result(got).GetSingle() - scene.Lights[0].Falloff) < 0.001f;
                    check("api4: state.get reads a field through a path", okGet, got ?? "(no reply)");

                    float before = scene.Lights[0].Intensity;
                    var set = Ask("{\"cmd\":\"api.state.set\",\"path\":\"scene.Lights[0].Intensity\",\"value\":12.25,\"id\":4}");
                    bool okSet = Ok(set) && Math.Abs(scene.Lights[0].Intensity - 12.25f) < 0.001f;
                    check("api4: state.set writes a float and the light really has it", okSet,
                          $"{before} -> {scene.Lights[0].Intensity}");

                    var kind = Ask("{\"cmd\":\"api.state.set\",\"path\":\"scene.Lights[0].Type\",\"value\":\"Spot\",\"id\":5}");
                    check("api4: an enum can be set by name", Ok(kind) && scene.Lights[0].Type == LightType.Spot,
                          scene.Lights[0].Type.ToString());

                    var col = Ask("{\"cmd\":\"api.state.set\",\"path\":\"scene.Lights[0].Direction\",\"value\":[0,0,-1],\"id\":6}");
                    check("api4: a vector can be set as an array", Ok(col) && Math.Abs(scene.Lights[0].Direction.Z + 1.0f) < 0.001f,
                          scene.Lights[0].Direction.ToString());
                }

                var ro = Ask("{\"cmd\":\"api.state.set\",\"path\":\"scene.Files\",\"value\":5,\"id\":7}");
                check("api4: a read-only member is refused with the reason",
                      ro != null && ro.Contains("\"ok\":false"), ro ?? "(no reply)");
                var nope = Ask("{\"cmd\":\"api.state.get\",\"path\":\"scene.NoSuchThing\",\"id\":8}");
                check("api4: a name that does not exist points at state.list",
                      nope != null && nope.Contains("\"ok\":false") && nope.Contains("state.list"), nope ?? "(no reply)");

                var hour = Ask("{\"cmd\":\"api.state.set\",\"path\":\"panel.PreviewHour\",\"value\":3.5,\"id\":9}");
                check("api4: the UI's own state is reachable too", Ok(hour) && Math.Abs(panel.PreviewHour - 3.5f) < 0.001f,
                      panel.PreviewHour.ToString("0.##"));

                panel.SwitchWorkspace(LightPanel.Space.Mlo);
                if (ui.Session == null && scene.HasModel) ui.Session = MloCreatorSession.FromScene(scene);
                var s = ui.Session;
                if (s != null)
                {
                    int rooms0 = s.Rooms.Count;
                    var added = Ask("{\"cmd\":\"api.state.call\",\"path\":\"session.AddRoom\",\"args\":[\"w4_room\",[-3,-3,0],[3,3,4]],\"id\":10}");
                    bool okAdd = Ok(added) && s.Rooms.Count == rooms0 + 1 && s.Rooms[s.Rooms.Count - 1].Name == "w4_room";
                    check("api4: state.call runs a real method and the room appears", okAdd,
                          okAdd ? $"{rooms0} -> {s.Rooms.Count}" : (added ?? "(no reply)"));

                    if (okAdd)
                    {
                        int last = s.Rooms.Count - 1;
                        var renamed = Ask("{\"cmd\":\"api.state.set\",\"path\":\"session.Rooms[" + last + "].Name\",\"value\":\"w4_renamed\",\"id\":11}");
                        check("api4: ...and what it made is editable by path", Ok(renamed) && s.Rooms[last].Name == "w4_renamed",
                              s.Rooms[last].Name);

                        int before = s.Rooms.Count;
                        var removed = Ask("{\"cmd\":\"api.state.call\",\"path\":\"session.RemoveRoom\",\"args\":[" + last + "],\"id\":12}");
                        check("api4: the invariant-keeping method is what gets called for removal",
                              Ok(removed) && s.Rooms.Count == before - 1, $"{before} -> {s.Rooms.Count}");
                    }

                    var listSession = Ask("{\"cmd\":\"api.state.list\",\"path\":\"session\",\"filter\":\"Room\",\"id\":13}");
                    check("api4: the session's own methods are discoverable",
                          Ok(listSession) && listSession.Contains("AddRoom"), listSession == null ? "(no reply)" : "listed");
                }

                var noMethod = Ask("{\"cmd\":\"api.state.call\",\"path\":\"scene.Wibble\",\"id\":14}");
                check("api4: an unknown method is refused", noMethod != null && noMethod.Contains("\"ok\":false"), noMethod ?? "(no reply)");
                var badEnum = Ask("{\"cmd\":\"api.state.set\",\"path\":\"panel.Workspace\",\"value\":\"Attic\",\"id\":15}");
                check("api4: an enum value that is not one of them lists what is",
                      badEnum != null && badEnum.Contains("\"ok\":false") && badEnum.Contains("Material"), badEnum ?? "(no reply)");

                var deep = Ask("{\"cmd\":\"api.state.get\",\"path\":\"form.camera.FieldOfView\",\"id\":16}");
                check("api4: a private field of the form is reachable from 'form'",
                      Ok(deep) && Math.Abs(Result(deep).GetSingle() - camera.FieldOfView) < 0.0001f, deep ?? "(no reply)");
            }
            catch (Exception ex)
            {
                check("api4: no exception", false, ex.ToString());
            }
            finally
            {
                try { tcp?.Close(); } catch { }
                try { StopMloBridge(ui); } catch { }
                panel.PreviewHour = wasHour;
                if (screenshotPath == null) panel.SwitchWorkspace(was);
            }
        }
    }
}

