using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RageTools.Mcp;

public sealed class Bridge : IDisposable
{
    private readonly string host;
    private readonly int port;
    private TcpClient? tcp;
    private StreamWriter? writer;
    private Task? reader;
    private CancellationTokenSource? life;

    private int nextId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> pending = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> jobs = new();
    public readonly ConcurrentDictionary<int, string> JobNotes = new();

    public Bridge(string host, int port) { this.host = host; this.port = port; }

    public bool Connected => tcp?.Connected == true;

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (Connected) return;
        Dispose();
        var c = new TcpClient();
        await c.ConnectAsync(host, port, ct);
        c.NoDelay = true;
        tcp = c;
        var stream = c.GetStream();
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        life = new CancellationTokenSource();
        reader = Task.Run(() => ReadLoop(new StreamReader(stream, Encoding.UTF8), life.Token));
    }

    private void ReadLoop(StreamReader r, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = r.ReadLine();
                if (line == null) break;
                JsonNode? node;
                try { node = JsonNode.Parse(line); } catch { continue; }
                if (node is not JsonObject o) continue;

                var type = o["type"]?.GetValue<string>();
                if (type == "api")
                {
                    if (o["id"] is JsonValue idv && idv.TryGetValue<int>(out int id) &&
                        pending.TryRemove(id, out var waiter))
                        waiter.TrySetResult(o);
                }
                else if (type == "event")
                {
                    var ev = o["event"]?.GetValue<string>();
                    if (o["job"] is JsonValue jv && jv.TryGetValue<int>(out int job))
                    {
                        if (ev == "job-done")
                        {
                            JobNotes.TryRemove(job, out _);
                            if (jobs.TryRemove(job, out var jw)) jw.TrySetResult(o);
                        }
                        else if (ev == "job")
                        {
                            var pct = o["percent"]?.GetValue<int>() ?? 0;
                            var note = o["note"]?.GetValue<string>();
                            JobNotes[job] = string.IsNullOrEmpty(note) ? $"{pct}%" : $"{pct}% {note}";
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            var gone = new IOException("the connection to RAGE Tools closed");
            foreach (var kv in pending) kv.Value.TrySetException(gone);
            pending.Clear();
            foreach (var kv in jobs) kv.Value.TrySetException(gone);
            jobs.Clear();
        }
    }

    public async Task<JsonNode?> CallAsync(string verb, JsonObject args, CancellationToken ct)
    {
        if (!Connected) throw new IOException("not connected to RAGE Tools");
        int id = Interlocked.Increment(ref nextId);
        var msg = new JsonObject { ["cmd"] = verb, ["id"] = id };
        foreach (var kv in args) msg[kv.Key] = kv.Value?.DeepClone();

        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = tcs;
        lock (writer!) writer.WriteLine(msg.ToJsonString());

        using var reg = ct.Register(() => { if (pending.TryRemove(id, out var w)) w.TrySetCanceled(); });
        var reply = (JsonObject)await tcs.Task;

        if (reply["ok"]?.GetValue<bool>() != true)
            throw new BridgeError(reply["error"]?.GetValue<string>() ?? "the tool refused the call");

        var result = reply["result"];
        if (result is JsonObject ro && ro.Count == 1 && ro["job"] is JsonValue jv && jv.TryGetValue<int>(out int job))
        {
            var jt = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs[job] = jt;
            using var jreg = ct.Register(() =>
            {
                if (jobs.TryRemove(job, out var w))
                {
                    try { lock (writer!) writer.WriteLine(new JsonObject { ["cmd"] = "api.job_cancel", ["job"] = job }.ToJsonString()); } catch { }
                    w.TrySetCanceled();
                }
            });
            var done = (JsonObject)await jt.Task;
            if (done["ok"]?.GetValue<bool>() != true)
                throw new BridgeError(done["error"]?.GetValue<string>() ?? "the job failed");
            return done["result"];
        }
        return result;
    }

    public void Dispose()
    {
        try { life?.Cancel(); } catch { }
        try { tcp?.Close(); } catch { }
        tcp = null; writer = null; life = null; reader = null;
    }

    public static async Task<Bridge> OpenAsync(Options opt, CancellationToken ct)
    {
        var bridge = new Bridge("127.0.0.1", opt.Port);
        try { await bridge.ConnectAsync(ct); return bridge; }
        catch { }

        if (!opt.Launch)
            throw new BridgeError($"nothing is listening on 127.0.0.1:{opt.Port}. Start RAGE Tools with --serve {opt.Port}, or drop --no-launch.");

        var exe = opt.Exe ?? FindExe();
        if (exe == null)
            throw new BridgeError("could not find RAGE Tools. Pass --exe <path to RAGE Tools.exe>, or set RAGE_TOOLS_EXE.");

        var args = $"--serve {opt.Port}";
        if (!string.IsNullOrEmpty(opt.Gta)) args += $" --gta \"{opt.Gta}\"";
        Console.Error.WriteLine($"[rage-tools] starting {exe} {args}");
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception ex) { throw new BridgeError("could not start RAGE Tools: " + ex.Message); }

        for (int i = 0; i < 120 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(500, ct);
            try { await bridge.ConnectAsync(ct); return bridge; } catch { }
        }
        throw new BridgeError($"RAGE Tools was started but never listened on port {opt.Port}");
    }

    private static string? FindExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RAGE_TOOLS_EXE");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
        var here = AppContext.BaseDirectory;
        foreach (var name in new[] { "RAGE Tools.exe", "RageLightEditor.exe" })
        {
            foreach (var dir in new[] { here, Path.Combine(here, ".."), Path.Combine(here, "..", "..") })
            {
                var p = Path.GetFullPath(Path.Combine(dir, name));
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }
}

public sealed class BridgeError : Exception
{
    public BridgeError(string message) : base(message) { }
}

public sealed class Options
{
    public int Port = 27017;
    public string? Exe;
    public string? Gta;
    public bool Launch = true;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        var envPort = Environment.GetEnvironmentVariable("RAGE_TOOLS_PORT");
        if (int.TryParse(envPort, out var ep)) o.Port = ep;
        o.Exe = Environment.GetEnvironmentVariable("RAGE_TOOLS_EXE");
        o.Gta = Environment.GetEnvironmentVariable("RAGE_TOOLS_GTA");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port": if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) o.Port = p; break;
                case "--exe": if (i + 1 < args.Length) o.Exe = args[++i]; break;
                case "--gta": if (i + 1 < args.Length) o.Gta = args[++i]; break;
                case "--no-launch": o.Launch = false; break;
            }
        }
        return o;
    }
}

