# YTYP / MLO placement + GTA V game-file access (CodeWalker port notes)

Everything needed lives in **CodeWalker.Core**. Paths relative to `CodeWalker-master\`.

## 1. YTYP parsing
- `CodeWalker.Core\GameFiles\FileTypes\YtypFile.cs:12` — `YtypFile : GameFile`.
  - `Load(byte[])` (:173) → `RpfFile.LoadResourceFile(this, data, 2)` — **ytyp resource version = 2**.
  - `AllArchetypes` is the archetype list; dispatch on `block.StructureNameHash`:
    `CBaseArchetypeDef`→`Archetype`, `CTimeArchetypeDef`→`TimeArchetype`, `CMloArchetypeDef`→`MloArchetype`.
- `Archetype.cs:12` `Archetype`, `:144` `MloArchetype`.
  - **`Archetype.Hash = arch.assetName; if (Hash==0) Hash = arch.name;`** (`Archetype.cs:50-63`) — this is the drawable-resolution key.
  - `MloArchetype.LoadChildren(Meta)` (`:461-507`) inflates `entities[]`, `rooms[]`, `portals[]`, `entitySets[]`.

### Property paths
```
YtypFile.AllArchetypes[i] as MloArchetype
  .entities[j]._Data.archetypeName / .position / .rotation / .scaleXY / .scaleZ   (MCEntityDef -> CEntityDef)
  .entitySets[k].Name / .Locations[j] (room index) / .Entities[j]
  .rooms[r].RoomName / .AttachedObjects[]   (indices into .entities[] ONLY)
  .portals[p].Corners[] / .roomFrom / .roomTo
```

## 2. Placement math (THE critical part)
**Inversion asymmetry is the #1 source of wrong rotations:**
- `CMloInstanceDef` quaternion is used **as-is** (`YmapFile.cs:1757-1777`, invert deliberately commented out).
- `CEntityDef` quaternions (incl. MLO child entities) **are inverted** (`YmapFile.cs:1779-1801`).

```
q_inst  = Quaternion(CMloInstanceDef.CEntityDef.rotation)      // NOT inverted
p_inst  = CMloInstanceDef.CEntityDef.position
q_local = Invert(Quaternion(MCEntityDef._Data.rotation))       // inverted unless identity
worldPos = p_inst + rotate(q_inst, MCEntityDef._Data.position)
worldRot = q_inst * q_local
world    = Matrix.Transformation(0, Identity, scale, 0, worldRot, worldPos)   // YmapFile.cs:1937
```
Re-transform on move: `MloInstanceData.UpdateEntity` (`Archetype.cs:880-887`).
With no .ymap instance, use identity instance transform (entity coords are interior-local).

## 3. archetypeName hash -> drawable file
- `GameFileCache.GetArchetype(hash)` (`GameFileCache.cs:2062`) — from `archetypeDict`, built by
  `InitArchetypeDicts()` (:1277) which parses **every ytyp in the game** at startup (the expensive part).
- `GameFileCache.TryGetDrawable(Archetype)` (`:2681`):
  priority **ydd (if `DrawableDict != 0`) → ydr → yft**, all keyed on `Archetype.Hash`.
- Entry dicts (`YdrDict/YddDict/YftDict/...`) built by `InitGlobalDicts()` (`:964-1008`) keyed on
  `entry.ShortNameHash` (filename minus extension, jenkins-lowercase).
- **Returns unloaded objects**: `GetYdr` etc. return immediately and enqueue a load; callers must
  check `.Loaded` (that's what `TryGetDrawable` does). Pump `ContentThreadProc()` or `LoadFile()` sync.

## 4. Game files / RPF7 / keys
- Folder check: `gta5.exe` (legacy) or `gta5_enhanced.exe` (gen9) — `GTAFolder.cs:20-25`.
- `GTA5Keys.LoadFromPath(folder, gen9, cachedBase64Key)` (`GTAKeys.cs:168`) — derives the AES key by
  SHA1-scanning the user's own **gta5.exe**, then decrypts the embedded `magic.dat` to get NG keys/tables.
  **No keys ship with CodeWalker** — a legit local install is required. Cache the key (base64) to avoid rescanning.
- `RpfManager.Init(folder, gen9, status, err)` (`RpfManager.cs:37`) — scans `*.rpf` recursively, `ScanStructure`, `AddRpfFile`.
- Lookup by path: `RpfMan.GetFile<T>(path)` (`:303`); `common:` maps to `update\update.rpf\common`.
- `RpfManager.IsGen9` is a **static** read by `ResourceData` — set before parsing any resource.
- Licensing: crypto is (c) 2015 Neodymium, MIT — reproduce the notice. **Avoid CodeWalker's FBX paths (GPL).**

### Minimal init
```csharp
GTA5Keys.LoadFromPath(folder, gen9, null);
var gfc = new GameFileCache(2L<<30, 10.0, folder, gen9, "", false, "");
gfc.LoadVehicles = gfc.LoadPeds = gfc.LoadAudio = false;   // big init savings
gfc.Init(status, err);        // RpfMan.Init -> InitGlobalDicts -> InitArchetypeDicts
```

## 5. Performance strategy to copy
- `Cache<TKey,TVal>` (`Utils\Cache.cs:10`): LRU + memory budget (CW uses ~2GB), `CacheTime` 10s, `Compact()` per frame.
- `requestQueue` (ConcurrentQueue, capped at 10) + background `ContentThreadProc` with
  **`MaxItemsPerLoop = 1`** — one file per iteration to avoid frame hitches; stale requests (>0.5s) dropped.
- `archetypeDict` / `YtypDict` are fully resident (not cached) so `GetArchetype` is O(1).
- Interior entities frustum-culled per frame (`Renderer.cs:2279-2327`), entity sets gated on `VisibleOrForced`.

## Gotchas
1. Instance quaternion NOT inverted, entity quaternion IS. (#1 bug source)
2. `Archetype.Hash` = assetName w/ fallback to name — not name.
3. `rooms[].AttachedObjects` index into `entities[]` only; entity-set entities are a separate index space.
4. Interior child entities have `Ymap == null`.
5. MLO archetypes are usually `assetType == ASSET_TYPE_ASSETLESS (4)` with no drawable of their own.

## 6. Why an interior is "missing" in the world view (List I, WS-I5)
Two different mechanisms, both verified with `RLE_MLOAUDIT=1` (every active CMloInstanceDef:
778 interiors, all archetypes resolve, all shells' entities resolve) and `RLE_MLOSETS=1`:

1. **The placement ymap left the active set.** The MP DLCs' `contentChangeSets/mapChangeSetData`
   INVALIDATE whole base rpfs (mpheist: `platform:/levels/gta5/_citye/downtown_01/downtown_01_metadata.rpf`,
   225 files) and ENABLE their own copy (161 files). 138 ymaps come back under a DLC prefix
   (`dt1_02` -> `hei_dt1_02`), 52 do not: the SP mission-state variants (`carshowroom_broken`,
   `dt1_05_hc_*`, `fib_heist_*` - no interior, correctly gone) and the script-requested IPL
   interiors nobody re-supplied: `shr_int` (PDM, v_carshowroom), `fiblobby`, `finbank`,
   `rc12b_hospitalinterior`, `facelobby`, `farmint`, `trevorstrailer`, `coroner_int_on`,
   `post_hiest_unload`, `refit_unload`, `id2_14_during1`, `cs1_02_cf_onmission1..4` (+ their `_lod`
   parents). Rescue = `GameFileCache.ScriptIpls.cs` (Options > World "Script IPLs (FiveM)"): main
   cache node (gta5_cache_y.dat MapDataStore + InteriorProxies) + not in YmapDict + in an archive +
   places an interior + no ACTIVE interior proxy within 3 m (221 candidates refused that way, e.g.
   `dt1_19_interior_v_policehub_milo_` because `hei_dt1_19_interior_0_heist_police_dlc_milo_` stands
   at 0.0 m) + one per pivot among the dropped (trevorstrailer over trevorstrailertidy/trash).
   The newest overlay copy is used (patchday27ng > patchday2ng > patchday1ng > x64i).
2. **The shell is in entity sets.** Online interiors are a bare skeleton of base entities plus
   sets a script switches on: auto shop `entity_set_style_1..9` (~100 each), nightclub
   `int01_ba_style01..03` (133), arcade `entity_set_constant_geometry` (110) + one of three
   ceilings. `WorldStreamer.EntitySets.cs` (Options > World "Int. sets": As placed / Auto / All):
   Auto turns on the first of every numbered family (>= 8 entities) and the first set per shell word
   (constant / geometry / default / shell / base / wall / floor / ceiling).
