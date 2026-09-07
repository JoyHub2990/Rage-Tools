# RAGE Tools — MCP server

Lets an AI agent drive RAGE Tools: search the game archives, inspect any file, move the camera,
and get real RAGE-engine renders back as pictures it can actually look at.

It is a thin adapter. Every tool it offers is read from the editor at start-up (`api.describe`),
so a verb added to RAGE Tools shows up here with no change to this program and no new release.

```
MCP client (Claude, etc.)
      | stdio
RageTools.Mcp.exe
      | TCP 127.0.0.1:27017, one JSON line per message
RAGE Tools  (--serve)
```

## Build

```
dotnet publish RageTools.Mcp/RageTools.Mcp.csproj -c Release -r win-x64 --self-contained false -o out/mcp
```

## Register it

Anything that speaks MCP over stdio works. For Claude Code:

```
claude mcp add rage-tools -- "C:\path\to\RageTools.Mcp.exe" --gta "C:\Program Files\Rockstar Games\Grand Theft Auto V"
```

or by hand, in an MCP client config:

```json
{
  "mcpServers": {
    "rage-tools": {
      "command": "C:\\path\\to\\RageTools.Mcp.exe",
      "args": ["--gta", "C:\\Program Files\\Rockstar Games\\Grand Theft Auto V"],
      "env": { "RAGE_TOOLS_EXE": "C:\\path\\to\\RAGE Tools.exe" }
    }
  }
}
```

If RAGE Tools is not already running, the server starts it in `--serve` mode and waits for it. If
it IS running (started by hand with `--serve`, or by a previous session), it just connects - so the
window you are watching is the window the agent is driving.

## Options

| Flag | Environment | Default | What it does |
|---|---|---|---|
| `--port N` | `RAGE_TOOLS_PORT` | 27017 | The bridge port to talk to. |
| `--exe <path>` | `RAGE_TOOLS_EXE` | found beside this exe | Which RAGE Tools to start. |
| `--gta <folder>` | `RAGE_TOOLS_GTA` | whatever the app remembers | Game folder, passed on when starting it. |
| `--no-launch` | — | off | Never start the app; fail if nothing is listening. |
| `--selftest` | — | — | Run the checks below and exit 0/1. |

## Checking it works

```
RageTools.Mcp.exe --selftest --gta "C:\Program Files\Rockstar Games\Grand Theft Auto V"
```

Connects (starting the app if needed), reads the catalogue, checks every verb has a schema and a
legal tool name, calls a verb with arguments, confirms a refusal is reported as an error, renders a
prop and confirms the PNG is real. Prints `MCPTEST: failures=N result=OK|FAILED`.

## Notes

- **The archives take a moment.** After the app starts, `status` reports
  `archive.ready`; anything that searches or names a prop needs it. Poll `status` rather than
  guessing.
- **Renders come back as images**, plus the JSON. Files over 8 MB are left on disk and only the
  path is returned - a 4K render is not a chat message.
- **Long verbs are followed for you.** `fs_extract`, `render_*` and friends return a job inside the
  editor; this server waits for the finished work and hands back the result, so a caller never sees
  a ticket. Cancelling the call cancels the job.
- **stdout belongs to the protocol.** Everything this program says goes to stderr.
