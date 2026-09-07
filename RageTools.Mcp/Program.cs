using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RageTools.Mcp;
using System.Text.Json;
using System.Text.Json.Nodes;

var opt = Options.Parse(args);
var cts = new CancellationTokenSource();

if (args.Contains("--selftest"))
    return await SelfTest.RunAsync(opt, cts.Token);

Bridge bridge;
try
{
    bridge = await Bridge.OpenAsync(opt, cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine("[rage-tools] " + ex.Message);
    return 1;
}

var verbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var tools = new List<Tool>();
try
{
    var described = await bridge.CallAsync("api.describe", new JsonObject(), cts.Token) as JsonObject
                    ?? throw new BridgeError("api.describe answered with nothing");
    var app = described["app"]?.GetValue<string>() ?? "RAGE Tools";
    var version = described["version"]?.GetValue<string>() ?? "?";
    foreach (var v in described["verbs"]?.AsArray() ?? new JsonArray())
    {
        if (v is not JsonObject vo) continue;
        var verb = vo["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(verb)) continue;
        var toolName = ToolNameFor(verb);
        verbs[toolName] = verb;

        var schema = vo["params"] is JsonObject ps
            ? JsonSerializer.Deserialize<JsonElement>(ps.ToJsonString())
            : JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

        var summary = vo["summary"]?.GetValue<string>() ?? "";
        var returns = vo["result"]?.GetValue<string>();
        var mutates = vo["mutates"]?.GetValue<bool>() ?? false;
        var description = summary;
        if (!string.IsNullOrWhiteSpace(returns)) description += "\nReturns: " + returns;
        if (mutates) description += "\n(changes the editor's state; undoable in the app)";

        tools.Add(new Tool
        {
            Name = toolName,
            Title = verb,
            Description = description,
            InputSchema = schema,
        });
    }
    Console.Error.WriteLine($"[rage-tools] {app} {version} on port {opt.Port}: {tools.Count} tools");
}
catch (Exception ex)
{
    Console.Error.WriteLine("[rage-tools] could not read the tool catalogue: " + ex.Message);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new Implementation { Name = "rage-tools", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = tools }))
    .WithCallToolHandler(async (ctx, ct) =>
    {
        var name = ctx.Params?.Name ?? "";
        if (!verbs.TryGetValue(name, out var verb))
            return Error($"no tool called '{name}'");

        var argsObj = new JsonObject();
        foreach (var kv in ctx.Params?.Arguments ?? new Dictionary<string, JsonElement>())
        {
            var node = JsonNode.Parse(kv.Value.GetRawText());
            if (node != null) argsObj[kv.Key] = node;
        }

        try
        {
            if (!bridge.Connected) await bridge.ConnectAsync(ct);
            var result = await bridge.CallAsync(verb, argsObj, ct);
            return Present(result);
        }
        catch (BridgeError be) { return Error(be.Message); }
        catch (OperationCanceledException) { return Error("cancelled"); }
        catch (Exception ex) { return Error(ex.Message); }
    });

await builder.Build().RunAsync();
return 0;

static string ToolNameFor(string verb)
{
    var v = verb.StartsWith("api.", StringComparison.OrdinalIgnoreCase) ? verb[4..] : verb;
    return v.Replace('.', '_');
}

static CallToolResult Error(string message) => new()
{
    IsError = true,
    Content = [new TextContentBlock { Text = message }],
};

static CallToolResult Present(JsonNode? result)
{
    var blocks = new List<ContentBlock>();
    var text = result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    blocks.Add(new TextContentBlock { Text = text });

    foreach (var png in PngPaths(result))
    {
        try
        {
            var bytes = File.ReadAllBytes(png);
            if (bytes.Length > 8 * 1024 * 1024) continue;
            blocks.Add(new ImageContentBlock
            {
                Data = bytes,
                MimeType = "image/png",
            });
        }
        catch { }
    }
    return new CallToolResult { Content = blocks };
}

static IEnumerable<string> PngPaths(JsonNode? result)
{
    if (result is not JsonObject o) yield break;
    if (o["path"]?.GetValue<string>() is string one && one.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(one))
        yield return one;
    if (o["paths"] is JsonArray many)
        foreach (var p in many)
            if (p?.GetValue<string>() is string s && s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(s))
                yield return s;
}

