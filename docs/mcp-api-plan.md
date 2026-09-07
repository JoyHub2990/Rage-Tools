# RAGE Tools — MCP / API plan

The goal: everything the tool can do, callable by an automated agent. An agent should be able to
search the game files in bulk, open any prop or MLO, read every detail the panels show, edit
(place props, edit lights, portals, materials, collisions), render what it did to a PNG, and save —
without a human at the keyboard. Later, the Blender/Sollumz bridge becomes just another client of
the same surface.

This is a map, not code. Written against the `Rage-Tools-Finished` tree.

---

## 1. Principles

1. **One command bus.** Every operation is a *verb* registered in the app. The UI, the DCC bridge,
   the MCP server and any future HTTP facade all call the same verbs. A panel redesign never breaks
   the API; a new feature gets its API for free when its verb is registered.
2. **Self-describing.** A `describe` verb returns the full catalogue — name, summary, JSON schema of
   params and result, read-only vs mutating — so clients (including the MCP sidecar) build their
   tool list at runtime. Adding a verb in the app requires **zero changes** anywhere else.
3. **Modular adapters.** The MCP server is a thin separate console exe (official
   `ModelContextProtocol` NuGet, stdio transport, .NET 8) that translates MCP tool calls into bridge
   messages. If the protocol or the SDK changes, only the adapter is touched. If the app changes,
   only the verb table is touched.
4. **UI-thread contract.** All state-touching work is enqueued and drained on the UI thread once a
   frame — the pattern `MloBridge` already implements. No verb handler ever runs on a socket thread.
5. **Mutations are commands.** Every editing verb goes through `EditHistory`/`IEditCommand` (or the
   MLO creator's state history), so agent edits are undoable and visible in the UI's undo list.
6. **Localhost only, writes gated.** The bus binds 127.0.0.1. RPF-mutating verbs require edit mode
   and take the existing automatic backups (`RpfEdit.EnsureBackup`). Optional shared-secret token
   for anything beyond loopback later, if ever.

## 2. What already exists (the seams)

| Seam | Where | Why it matters |
|---|---|---|
| DCC bridge transport | `Editor/MloBridge.cs` — TCP 127.0.0.1:27017, one UTF-8 line per message, accept/read threads → `ConcurrentQueue`, drained per frame | The transport is done, tested end-to-end (`SeqTest_P5`), the FiveM link in `docs/fivem.md` uses the same message shapes over WebSocket |
| Bridge protocol v1 | `Editor/DccBridge.cs` — `DccBridgeProtocol.Commands`, JSON or plain-word spelling, events (camera/selection/lights) | Already a versioned, self-advertising verb list with push events |
| Extension hooks | `MloBridge.ExtraParse`, `DccBridgeMessage_P5(ui, m, ref handled)` | New verb family plugs in without touching the original five MLO verbs |
| Request-field pattern | ~120 `Request*` fields across `Editor/` panels, serviced by the host per frame | Setting a field from a verb handler takes the *identical* path as a button press |
| Undo command pattern | `Editor/EditHistory.cs` (`IEditCommand`, groups, merge windows) + `MloCreatorHistory` | Agent edits become first-class, undoable edits |
| File-type manifest | `Editor/XmlIO.cs` — 28 extensions with per-type Can{Export,Import} flags | The capability list for convert/inspect verbs, already in table form |
| Headless render | `MainForm.RenderStill(w,h,path)` / `RenderStillToBitmap` (`--renderout`) | "Render this file to PNG" already works end to end |
| Archive layer | `Editor/GameFileManager.cs` (GameFileCache/RpfManager), `ArchiveBrowser` flat index, `RpfExplorer`, `RpfEdit.O1` (edit mode + backups), `LocalAssetIndex` (loose FiveM resources override archives) | Full RPF read/write with mod-aware resolution |
| Workspace addressing | `Editor/SpaceNames.V1.cs` | Stable, obfuscation-safe workspace names for the API |
| Test registry | `SeqTest_*` partial methods in `MainForm.cs` | The API suite is one more `partial void SeqTest_Api(check)` |
| Headless actions | ~300 `RLE_*` env vars + CLI flags | Each handler body is exactly what a verb should call; env vars are one-shot per launch, the bus makes them per-frame |

Nothing else exists: no HTTP server, no pipes, no scripting host, no file watcher. The bridge is the
only IPC, which is why it becomes the bus.

## 3. Architecture

```
Claude / any MCP client
        │ stdio (MCP)
        ▼
RageTools.Mcp.exe          ← thin adapter, official C# SDK, builds its tool list from `describe`
        │ TCP line protocol, 127.0.0.1
        ▼
RAGE Tools app ── ApiVerbs registry ──► Request* fields / Editor model methods / IEditCommand
        ▲
        │ same TCP protocol
Blender (ragetools_linking.py) · 3ds Max (RageWorldBridge.ms) · scripts/curl-style clients
```

### 3.1 In-app: the verb registry (`Editor/ApiVerbs.*.cs`, new)

- `ApiVerb { Name, Summary, ParamsSchema, ResultSchema, Mutates, Handler }` in one static table,
  grouped by area in partial files (mirrors how everything else in the repo is organised).
- Registered into the existing protocol: `DccBridgeProtocol.Parse` recognises `api.*` /
  `{"type":"api", "verb":..., "id":..., "args":{...}}`; the host handles it in the existing
  first-refusal hook before the MLO verbs. Protocol `Version` bumps to 2; v1 verbs unchanged.
- **Reply correlation:** JSON requests may carry `"id"`; the reply echoes it. Needed for MCP
  (concurrent calls) — today's bridge is strictly request/reply per client. One new plumbing piece:
  reply-to-sender (`SendTo(client, line)`) beside `Broadcast`.
- **Jobs:** long verbs (bulk search, batch extract, video) return `{job: n}` immediately, then push
  `{"type":"event","event":"job","job":n,"progress":...}` and a final `{"event":"job-done"}`.
  Cancellation: `api.job_cancel`.
- **Threading:** handlers run in the per-frame drain (UI thread). Long jobs run on worker threads
  but only touch RPF/read-only data; anything scene-touching posts back to the drain queue.

### 3.2 Sidecar: `RageTools.Mcp` (new console project)

- Official `ModelContextProtocol` NuGet, stdio server. ~300 lines, generic:
  connect → `describe` → register one MCP tool per verb (schema passthrough) → forward calls,
  await reply by id, map job events to MCP progress notifications.
- If the app isn't running, launches it (`--serve`: hidden window, bridge on, no dialogs — the
  existing `IsHeadless` machinery) and waits for the `BRIDGEDEMO listening` line.
- Image-returning verbs (`render.*`, `capture`) reply with a file path; the sidecar reads the PNG
  and returns MCP image content. Keeps big payloads off the line protocol.
- Because the tool list comes from `describe`, the sidecar never needs editing when verbs change.

### 3.3 Optional later: HTTP facade

A small HTTP listener (in the sidecar, not the app) exposing the CodeWalker.API fork's 15 routes
(upstream: flobros/CodeWalker.API) with the same shapes, translated to verbs — so existing scripts
written against that API can point at RAGE Tools unchanged. Deferred: those scripts already have a
working server today; MCP is the priority.

## 4. Verb catalogue

Tiers: **P0** = first ship (agent can see, search, render). **P1** = full editing. **P2** = the
extras. Every verb lists the seam it calls, so implementation is mechanical.

### 4.1 System & session (P0)

| Verb | Args → result | Seam |
|---|---|---|
| `describe` | — → full catalogue + protocol/app version | the registry itself |
| `status` | — → workspace, open files, GTA path, archive-index state, selection, camera, hour, dirty flags | `LightPanel.Workspace`, `Scene`, `GameFileManager`, `ArchiveBrowser.Ready` |
| `switch_workspace` | space name → ok | `SpaceNames.TryParse` + `SwitchWorkspace` |
| `open_file` | path, additive? → summary of what loaded | `MainForm.LoadFile` |
| `save_file` / `save_as` | file?, path? → written path | `Scene.SaveOne/SaveOneAs` + per-workspace `RequestSave` |
| `close_file` | file → ok | `Scene.RemoveFile`, `RequestClose` |
| `undo` / `redo` | — → undone/redone label | `EditHistory`, `MloCreatorHistory` |
| `set_camera` / `get_camera` | pos, dist, yaw, pitch | `Camera` (same spec as `--cam`) |
| `goto` | x,y,z → ok | `RequestWorldGoto` / camera park |
| `set_hour` / `set_weather` / `set_modifier` | value → ok | timecycle state (`--hour`/`--weather`/`--modifier` bodies) |
| `capture` | w?, h? → PNG path | `RenderStillToBitmap` |

### 4.2 Archive & files — CodeWalker-API parity and past it (P0)

| Verb | Args → result | Seam |
|---|---|---|
| `fs.search_names` | query/regex, exts?, limit → matches with full RPF paths | `ArchiveBrowser.Search/Find` (index is in memory — instant) |
| `fs.search_content` | pattern or hash, exts?, max? → **job**; streamed matches with entry + offset/context | new: parallel across archives, sequential within one archive (same grouping CodeWalker.API's batch uses); XML-converts metas on the fly for text patterns |
| `fs.list_dir` | rpf-or-fs path → entries (name, type, size, resource/encrypted flags) | `RpfExplorer.ChildrenOf/GoToPath` — the fork's API can't do this at all |
| `fs.extract` | paths[], textures?, xml? → **job**; written paths | `ArchiveBrowser.Extract`, `XmlIO.ExportFromArchive`, texture export (Q1) |
| `fs.convert_to_xml` / `fs.convert_to_binary` | paths[] → per-item result | `XmlIO.Export/Import` (28-type manifest; a successful export is evidence the bytes parse) |
| `fs.rpf_import` | files[], target rpf dir → per-item result | `RpfEdit.ImportFiles` (edit mode + auto backup) |
| `fs.rpf_replace` / `fs.rpf_new_folder` / `fs.rpf_rename` / `fs.rpf_delete` | … | `RpfEdit.O1` ops |
| `fs.hash` | text → joaat uint/int/hex | `JenkHash` |
| `fs.unhash` | number → known name(s) | `JenkIndex` + `strings.txt` (+ name sidecars) |
| `fs.dependencies` | ymap/ytyp path, recurse-MLO? → archetype → concrete asset paths, missing list | port of the fork's resolver over our `GameFileCache`; extend to ytyp input |
| `fs.reload` | path? → ok | re-import/refresh (there is no file watcher by design — agents mutate a file, then say so) |
| `fs.set_gta` / `fs.get_config` | folder → init state | `GameFileManager.BeginInit/IsValidFolder/GuessFolder` |
| `fs.add_resource` | FiveM resource folder → indexed counts | `LocalAssetIndex` (loose assets override archives) |

### 4.3 Inspect — "the agent sees everything the app sees" (P0)

| Verb | Args → result | Seam |
|---|---|---|
| `inspect.file` | path or rpf path → typed JSON: drawable (models, bounds, shaders, textures, lights), ytyp (archetypes incl. MLO summary), ymap (entities, extents), ytd (textures, formats, sizes), ynv (poly/flag stats), ypt (effects), ycd (clips), ybn (bounds tree) | `XmlIO.Load*` + summarisers; `MeshDump`/`--dumpmesh` body |
| `scene.get` | — → open files, per-file lights/materials counts, selection, MLO summary | `Scene`, bridge `scene` verb (exists) |
| `lights.list` / `lights.get` | file?, index → every `LightAttributes` field, world pos | `Scene.Lights`, `LightDefs` (named flags) |
| `materials.list` / `materials.get` | file, index → shader name, bucket, params (hash+name+value), textures with *resolved source* | `MaterialEditing.ForFile/ParamHashes/Resolve` |
| `mlo.rooms` / `mlo.portals` / `mlo.entities` / `mlo.sets` | → full structures (bridge already serves rooms/portals/entities) | `MloCreatorSession` / `MloEditor` |
| `world.query` | pos, radius, filter? → entities near, archetype + ymap + lod info | `WorldStreamer`/`WorldSelection` |
| `tc.get` | hour, modifier? → sampled variable values | `TimecycleData.Region.Get/FindModifier` |
| `nav.stats` / `anim.clips` / `ptfx.effects` | → per-doc summaries | `NavMeshEditor`, `YcdDocument_V6`, `PtfxDocument` |

### 4.4 Render — the thing no other GTA API has (P0)

| Verb | Args → result | Seam |
|---|---|---|
| `render.prop` | archetype name **or** file path, cam?, hour?, w/h?, rendermode? → PNG | hidden-window app + `LoadModelFile`/archetype fetch + `RenderStill` (the `--renderout` path, made per-call instead of per-launch) |
| `render.view` | w/h? → PNG of current view | `RenderStillToBitmap` |
| `render.mlo` | ytyp, room? (isolate), cams[]?, hour? → PNGs | MLO import + isolate + `RenderStill` |
| `render.orbit` | subject, n views → PNG set (agent gets all sides) | camera math + `RenderStill` loop |
| `render.thumbs` | archetype names[] → **job**; contact-sheet or per-prop PNGs | loop of `render.prop`; the prop-library scan already proves bulk load works |
| `render.video` | shot list, w/h, fps → MP4 (P2) | `CameraSequence` + `VideoWriter` |

Note: the D3D device needs an HWND, so "headless" = hidden window (the `--screenshot` machinery
already runs unattended). A true windowless device is not worth the rewrite.

### 4.5 Edit (P1) — same code paths as the panels

- **Lights:** `lights.add/set/remove/duplicate`, `lights.save` → `Scene` ops + `--selftest`-proven save.
- **Materials:** `materials.set_param`, `set_texture`, `apply_preset`, `add/remove_param`,
  `set_bucket`, `make_unique`, `import_texture`, `export_textures` → `MaterialEditing`, `ShaderPresets`.
- **MLO:** `mlo.add_room/set_room/remove_room`, `add_portal/set_portal/flip_portal/remove_portal`,
  `place_entity/set_entity/remove_entity` (bridge `object` verb exists), `set_entity_room`,
  `entity_set_*`, `tcm_*`, `mlo.save_ytyp`, `mlo.export_ymap` → `MloCreatorSession`/`MloEditor`
  (room deletion **must** go through its dedicated method — portals index rooms by position).
- **World:** `world.select`, `world.set_transform`, `world.add_entity/delete/clone`,
  `world.save_ymaps` → `EntityOps` + `EntityTransformCommand` (already mergeable/undoable).
- **Collisions:** first via `fs.convert_to_xml`/`to_binary` on `.ybn` (round-trip supported);
  structured `ybn` edit verbs are P2.
- **NavMesh / Terrain / Particles / Anim:** `nav.set_flags/save`, `terrain.paint/export`,
  `ptfx.play/save`, `anim.set_track/save` → each editor's `Request*` fields and model methods.

### 4.6 Speculative extras (P2) — things developers would actually use

- `audit.mlo` / `audit.lod` / `audit.interior` — the `RLE_MLOAUDIT`/`RLE_LODAUDIT`/`RLE_INTAUDIT`
  probe bodies as structured results: limbo caps, portal orphans, missing archetypes, lod chains.
- `diff.files` — canonical-XML diff of two versions of any supported file (renders review diffs of
  binary game files readable for the first time).
- `tex.who_uses` — reverse texture lookup across the index: which drawables reference texture X.
- `mlo.closure` — full dependency closure of an interior → ready-to-ship file list (extract in one
  call, or lint a FiveM resource for missing/extra streamed files).
- `fivem.lint_resource` — point at a resource: manifest vs stream contents, duplicate names across
  the index, oversize textures, missing embedded collision.
- `place.snap_ground` / `place.room_of` — drop helper + "which room contains this point"
  (the visibility BFS already computes this).
- `tc.compare` — two modifiers/hours side by side, numeric deltas + optional A/B renders.
- `scene.watchdog` — subscribe to selection/camera/edit events (bridge events exist; MCP maps them
  to notifications) so an agent can follow along while a human drives.

## 5. Testing & versioning

- `SeqTest_Api` partial: starts the bus on port 0, runs a scripted client through every P0 verb
  (against the synthetic `--gentest` scene + a from-scratch MLO), asserts replies against schemas.
- `describe` output is committed as a golden file; CI-style check that schema changes are deliberate.
- Protocol `Version` gates: sidecar refuses to start against an unknown major version.
- The sidecar has its own smoke test: launch app `--serve`, `describe`, one render, one search, exit.

## 5a. What has landed

Steps 1-4 are built, tested and on `Rage-Tools-Finished`:

- **Bus** (`Editor/ApiVerbs.W1.cs`, `MainForm.Api.W1.cs`) - verb registry, `api.*` on the bridge,
  echoed request ids, jobs with progress/`job-done` events, `api.describe/status/workspace/ping`.
- **Read** (`MainForm.Api.Read.W2.cs`) - `fs.search_names/list_dir/hash/unhash/extract/convert_*`,
  `lights.*`, `materials.*`, `mlo.get`, `inspect.file`.
- **Render** (`MainForm.Api.Render.W3.cs`) - `camera.get/set/frame`, `time.set`, `open_file`,
  `render.view/prop/orbit`, and `--serve [port]`.
- **Sidecar** (`RageTools.Mcp/`) - stdio MCP server that builds its tool list from `describe` and
  returns renders as image content. `--selftest` covers it.

Tests: `SeqTest_W1/W2/W3` inside `--seqtest`, plus `RageTools.Mcp --selftest`.

Three things learned the hard way, each now a comment where it matters:

1. `RenderStill` runs frames of its own, so it can never be called from inside a frame. Render verbs
   park a request; `ServiceApiRender_W3` performs it between frames.
2. Extracting a resource from an archive needs `ArchiveBrowser.ExtractForDisk` (re-compress, re-add
   the RSC7 header). A raw extract writes a file nothing can reopen, silently. For parsing in
   memory, use the entry-aware two-argument `Load(data, entry)`.
3. A freshly opened prop has no meshes for a few frames; photographing it at once gives one flat
   colour. Hence settle frames - and tests that assert colour diversity, not just file size.

## 6. Build order

1. **Bus:** verb registry + `api.*` parsing + reply ids + `describe` + `status` + jobs plumbing.
2. **Read:** inspect verbs + `fs.search_names` + `fs.list_dir` + `fs.extract` + convert.
3. **Render:** `render.view` / `render.prop` / `render.mlo` + `--serve` hidden mode.
4. **Sidecar:** MCP exe, auto-launch, image content, progress. *(Agent can now see, find, render.)*
5. **Search+:** multithreaded `fs.search_content`.
6. **Edit:** lights → materials → MLO → world, each landing with its `SeqTest_Api` block.
7. **Extras & facade:** audits, diff, closure; HTTP parity facade if still wanted.

Each step ships alone; nothing depends on a later step. The Blender/Sollumz sync upgrade later is
"teach `ragetools_linking.py` the `api.*` verbs" — same socket it already opens.
