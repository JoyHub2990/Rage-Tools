using System.Text.Json.Nodes;

namespace RageTools.Mcp;

public static class SelfTest
{
    public static async Task<int> RunAsync(Options opt, CancellationToken ct)
    {
        int fails = 0;
        void Check(string what, bool ok, string detail)
        {
            Console.Error.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what}  {detail}");
            if (!ok) fails++;
        }

        Bridge? bridge = null;
        try
        {
            bridge = await Bridge.OpenAsync(opt, ct);
            Check("connected to RAGE Tools", bridge.Connected, $"port {opt.Port}");

            var described = await bridge.CallAsync("api.describe", new JsonObject(), ct) as JsonObject;
            var verbs = described?["verbs"]?.AsArray();
            Check("api.describe answers with verbs", verbs is { Count: > 0 }, (verbs?.Count ?? 0) + " verbs");

            bool named = true, schemad = true;
            var toolNames = new List<string>();
            foreach (var v in verbs ?? new JsonArray())
            {
                var name = (v as JsonObject)?["name"]?.GetValue<string>() ?? "";
                if (!name.StartsWith("api.")) named = false;
                if ((v as JsonObject)?["params"] is not JsonObject) schemad = false;
                toolNames.Add(name.StartsWith("api.") ? name[4..].Replace('.', '_') : name.Replace('.', '_'));
            }
            Check("every verb is in the api family", named, "prefix");
            Check("every verb carries an input schema", schemad, "params objects");
            var legal = toolNames.All(n => n.Length > 0 && n.All(c => char.IsLetterOrDigit(c) || c == '_'));
            Check("verb names map to legal MCP tool names", legal, string.Join(", ", toolNames.Take(4)) + " ...");
            Check("the names are unique after mapping", toolNames.Distinct().Count() == toolNames.Count, toolNames.Count + " names");

            var status = await bridge.CallAsync("api.status", new JsonObject(), ct) as JsonObject;
            Check("api.status comes back as an object", status?["workspace"] != null, status?["workspace"]?.ToString() ?? "(none)");

            var hashed = await bridge.CallAsync("api.fs.hash", new JsonObject { ["text"] = "Adder" }, ct) as JsonObject;
            Check("arguments reach the tool", hashed?["hash"]?.GetValue<long>() == 3078201489L, hashed?["hash"]?.ToString() ?? "(none)");

            bool refused = false; string why = "";
            try { await bridge.CallAsync("api.nonesuch", new JsonObject(), ct); }
            catch (BridgeError be) { refused = true; why = be.Message; }
            Check("an unknown verb is refused, with the reason", refused, why);

            bool archives = (status?["archive"] as JsonObject)?["ready"]?.GetValue<bool>() == true;
            if (archives)
            {
                var dir = Path.Combine(Path.GetTempPath(), "rage_tools_mcp_selftest");
                Directory.CreateDirectory(dir);
                var png = Path.Combine(dir, "shot.png");
                if (File.Exists(png)) File.Delete(png);

                var shot = await bridge.CallAsync("api.render.prop", new JsonObject
                {
                    ["name"] = "prop_barrel_02a",
                    ["width"] = 256,
                    ["height"] = 192,
                    ["out"] = png,
                }, ct) as JsonObject;

                bool followed = shot?["path"] != null && shot["job"] == null;
                Check("a long verb is followed to its result, not its ticket", followed, shot?.ToJsonString() ?? "(none)");

                var made = shot?["path"]?.GetValue<string>();
                bool real = made != null && File.Exists(made) && new FileInfo(made).Length > 4096;
                Check("the render arrives as a real PNG", real, real ? new FileInfo(made!).Length + " bytes" : made ?? "(no path)");

                if (real)
                {
                    var bytes = File.ReadAllBytes(made!);
                    bool png8 = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
                    Check("...that a client can be handed as image content", png8, "PNG signature");
                }
                try { Directory.Delete(dir, true); } catch { }
            }
            else
            {
                Check("render skipped (no game archives indexed yet)", true, "start with --gta for the full pass");
            }
        }
        catch (Exception ex)
        {
            Check("no exception", false, ex.Message);
        }
        finally { bridge?.Dispose(); }

        Console.Error.WriteLine($"MCPTEST: failures={fails} result={(fails == 0 ? "OK" : "FAILED")}");
        return fails == 0 ? 0 : 1;
    }
}

