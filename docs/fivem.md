# FiveM live link

RAGE Tools can mirror what you edit in a running FiveM server. The game keeps its own window;
the tool gets a small panel under **Bridge > FiveM live link**.

## Install (once per server)

1. In the panel, pick your server's `resources` folder and press **Install / update the resource**.
   It writes `resources/ragetools_linking/` with the tool's port inside it.
2. Add to `server.cfg`:
   ```
   ensure ragetools_linking
   set ragetools_dev 1
   ```
   `ragetools_dev` lets the tool restart resources. Development servers only.
3. Start the server, join it. The panel's dot turns green.

The resource connects to the tool over a WebSocket (default `127.0.0.1:27018`). If the game runs on
another PC, tick *game on another PC* before installing so the resource carries this PC's address.

## What is live

| Workspace | Live in the game | Needs a save (the resource restarts on its own) |
|---|---|---|
| Lights | every light of the open prop, drawn on the same prop in the game: position, direction, colour, intensity, range, cone, shadows, time flags by game clock. The prop's own lights are switched off meanwhile. No copy near you: a preview copy is placed in front of you. From an imported interior only the props you edited or selected are stood in; the rest stay the game's own | coronas, volumes, flags |
| World, Edit Light | the selected vanilla prop's lights with its real placement: the game hides the original at that spot, puts a stand-in copy there and draws your lights on it (or at the world position when no model can stand in) | |
| Time & weather | the game clock and weather follow the tool | |
| Timecycle editor | edited cycle variables are pushed as a `ragetools_live` modifier at the tool's hour; edited interior modifiers are updated in the game by name, and the one on the Interior modifier tab is previewed on the player | |
| Materials | with the RageToolsLive plugin installed in FiveM, shader values (specular, bump, emissive, colours, fur shells and shading...) are written into the loaded model as you drag them, for every copy of the prop; *Auto-apply materials* still saves and reloads for textures | textures |
| Camera | game follows the editor (world streams around it), or editor follows the game | |
| World props | select a prop and move, turn, delete or duplicate it: the game hides the original at its old place and shows a stand-in copy where you put it (`entity` messages) | scale, new archetypes not streamed by the server |
| Everything else | | save into a resource's `stream` folder; that resource is restarted |

In the game, **F9** picks the prop you look at and reports it to the panel.

## Messages

Tool to game (JSON, one object per WebSocket text frame):

| type | fields |
|---|---|
| `hello` | `version`, `protocol` |
| `lights` | `spawn`, `props: [{model, place?: {pos, rot, scale}, lights: [{i, kind, pos, dir, tan, rgb, intensity, range, exp, inner, outer, extent, time, shadow, flash}]}]` (positions in the prop's own frame; `place` is the entity's world placement) |
| `camera` | `pos`, `target`, `fov` or `off: true` |
| `follow` | `on` (the game starts reporting the player) |
| `time` | `hour`, `minute` |
| `weather` | `name` (game weather name) |
| `restart` | `resource` |
| `goto` | `pos` (teleports the player) |
| `where` | asks for one `player` report |
| `timecycle` | `vars: {name: value}` or `off: true` - applied as the `ragetools_live` modifier |
| `entity` | `id`, `model`, `hash`, `orig: {pos, rot}` or null (a duplicate), `place: {pos, rot, scale}` or null (deleted), `moved`; `clear: true` drops every stand-in |
| `tcmod` | `mods: [{name, vars}]`, `apply`, `strength` or `off: true` - updates modifiers by name, previews one as the extra modifier |

Game to tool:

| type | fields |
|---|---|
| `hello` | `app`, `player`, `resource` |
| `player` | `pos`, `heading`, `campos`, `camrot`, `fov` |
| `pick` | `name`, `hash`, `pos`, `rot`, `hit`, `interior` |
| `say` | `text` |
| `stats` | `fps`, `ping`, `pos`, `heading`, `interior`, `room`, `lights`, `cam`, `tc`, `mod` (twice a second) |

`api.*` verbs (see `api.describe` on the local API) work over this socket too.

## Game view

*Game view (bottom left)* shows the FiveM window live inside the tool through the desktop compositor's own thumbnail (pixel-exact, no capture cost; a window capture is the fallback) with the `stats` the resource reports. The game must be running and not minimised. The panel itself can be detached into its own window from the menu.

## RageToolsLive plugin

`Install the plugin into FiveM` writes `RageToolsLive.asi` into `%LOCALAPPDATA%\FiveM\FiveM.app\plugins`. FiveM loads it on start
(servers running pure mode block plugins). The plugin opens the named pipe `\\.\pipe\RageToolsLive`; the tool sends one line per
edit - `set <model> <dict|-> <shader index> <parameter hash> <float4 count> <floats...>` - and the plugin finds the loaded drawable through
FiveM's own exported streaming lookups (`FindSlot`/`GetPtr` on the ydr, yft or ydd store), then writes the values into the shader's
parameter block in place. Every value it changes is remembered and restored with `reset`, when the tool disconnects, or when it exits.
Source: `native/ragetools_asi`, built with `build.cmd` (Visual Studio C++ tools).

The game also keeps a heartbeat: if the tool stops answering for eight seconds, or says `bye` on exit, everything is put back:
camera, lights, stand-ins, timecycle, weather and clock.
