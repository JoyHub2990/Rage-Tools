# Saving edited YDR/YFT safely — CodeWalker ResourceBuilder notes

Sources (all under `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/CodeWalker.Core/`):
- `GameFiles/Resources/ResourceBuilder.cs` (689 lines)
- `GameFiles/Resources/ResourceData.cs` (786 lines)
- `GameFiles/Resources/ResourceBaseTypes.cs` (1824 lines)
- `GameFiles/Resources/ResourceFile.cs` (ResourceFileBase / ResourcePagesInfo)
- `GameFiles/FileTypes/YdrFile.cs`, `YftFile.cs` (153 lines each)
- `GameFiles/RpfFile.cs` (RpfResourcePageFlags, LoadResourceFile, GetVersionFromFlags)
- `GameFiles/Resources/Drawable.cs` (Drawable, LightAttributes)
- `GameFiles/Resources/Frag.cs` (FragType.LightAttributes)

---

## 1. End-to-end: `ResourceBuilder.Build(fileBase, version, compress = true, gen9 = false)`

Signature (ResourceBuilder.cs:532):

```csharp
public static byte[] Build(ResourceFileBase fileBase, int version, bool compress = true, bool gen9 = false)
```

Steps, in order:

1. **`fileBase.FilePagesInfo = new ResourcePagesInfo();`** — a fresh pages-info block is created on EVERY build (old one discarded). `ResourcePagesInfo` defaults `SystemPagesCount = 128`, `GraphicsPagesCount = 0`, so its `BlockLength = 16 + 8*(sys+gfx)` = 16 + 8*128 = **1040 bytes at layout time** (deliberate over-allocation; comment in ResourceFile.cs:105 — "default sizing to ensure there is enough space allocated when writing files"). After paging, Build overwrites the counts with the real page counts, so the block *writes* fewer bytes than the space it reserved — safe padding.

2. **`GetBlocks(fileBase, out systemBlocks, out graphicBlocks)`** — walks the whole object graph:
   - `addBlock`: an `IResourceSystemBlock` goes to the system set, an `IResourceGraphicsBlock` to the graphics set.
   - `addChildren`: for each system block, recurses through `GetReferences()` (separately-positioned child blocks — each becomes its own block in the sets) and `GetParts()` (blocks *embedded* inside the parent's byte range — parts are NOT added as blocks themselves, only their children are recursed).
   - The root block is added first; `AssignPositions2` relies on `blocks[0]` being the root for the system segment (HashSet preserves insertion order here in practice).
   - **Crucial side effect:** `GetReferences()` is invoked fresh on every block during this walk. This is where "saving-only" blocks are (re)built — e.g. `ResourceSimpleList64<T>` rebuilds its private `data_block` from the *current* `data_items`, `Drawable` rebuilds `NameBlock` from `Name`. So all counts/pointers derive from live data at save time.

3. **`AssignPositions2(systemBlocks, 0x50000000, out systemPageFlags, 128, gen9)`** then
   **`AssignPositions2(graphicBlocks, 0x60000000, out graphicsPageFlags, 128 - systemPageFlags.Count, gen9)`**.
   - System segment base = `0x50000000`, graphics = `0x60000000` (virtual addresses; pointers written into the file keep these bases).
   - Sorting: root block first, remaining blocks sorted descending by `BlockLength` (`BlockLength_Gen9` if gen9). Each block is placed in the smallest page that fits (5 page sizes: `baseSize << 0..4`, `baseSize = 0x2000 << baseShift`, max baseShift 0xF). Per-size max page counts: `0x7F, 0x3F, 0xF, 3, 1`. If it doesn't fit, `baseShift++` and retry; throws `"Unable to pack blocks with largest possible base!"` if 0xF exceeded.
   - Alignment inside a page: `ALIGN_SIZE = 16` (`s += ((ALIGN_SIZE - (s % ALIGN_SIZE)) % ALIGN_SIZE)` before each block).
   - Sets `block.FilePosition = basePosition + blockPosition` for every block. `ResourceSystemBlock.FilePosition` setter propagates to `GetParts()` children: `part.Item2.FilePosition = value + part.Item1` — this is how the embedded `ResourceSimpleList64` (a *part*) gets its position.
   - Produces `RpfResourcePageFlags` packed as:
     ```csharp
     var v = (uint)baseShift & 0xF;
     v += (pageCounts[4] & 0x1) << 4;   // 16x base pages
     v += (pageCounts[3] & 0x3) << 5;
     v += (pageCounts[2] & 0xF) << 7;
     v += (pageCounts[1] & 0x3F) << 11;
     v += (pageCounts[0] & 0x7F) << 17; // 1x base pages
     ```
     (Note: in `RpfResourcePageFlags` (RpfFile.cs:2702) `BaseSize = 0x200 << BaseShift` and the flags' 9-slot page sizes run `baseSize<<8 .. baseSize<<0`; ResourceBuilder's `0x2000 << baseShift` == flags' `(0x200<<4)<<baseShift`, i.e. it only uses the middle 5 slots. `Size` property = total bytes = `baseSize * Σ(count_i << slotweight_i)`.)
   - **Meta special case:** if `blocks[0] is Meta`, falls back to `AssignPositionsForMeta` (naive sequential packing) — not relevant for YDR/YFT.

4. **Update pages info:** `FilePagesInfo.SystemPagesCount = (byte)systemPageFlags.Count; GraphicsPagesCount = (byte)graphicsPageFlags.Count;`

5. **Write both segments:** creates `ResourceDataWriter(systemStream, graphicsStream)` with `resourceWriter.IsGen9 = gen9` (writer default is `false` — ResourceData.cs:461: `public bool IsGen9 = false;//this needs to be specifically set by ResourceBuilder`). For every block: seek to `block.FilePosition`, `block.Write(resourceWriter)`, then **verify** `(pos_after - pos_before) == block.BlockLength` (or `BlockLength_Gen9`), else `throw new Exception("error in system length")` / `"error in graphics length"`. This is a built-in safety net: any BlockLength/Write mismatch (e.g. from bad edits) aborts the save rather than emitting a corrupt file.
   The writer routes by address: `(Position & 0x50000000) == 0x50000000` → system stream at `Position & ~0x50000000`; else `& 0x60000000` → graphics stream; else `throw new Exception("illegal position!")`.

6. **Assemble payload:** system bytes padded out to `systemPageFlags.Size`, graphics bytes to `graphicsPageFlags.Size` (buffers are allocated at full page size and the shorter stream copied in — the padding is zeros). Concatenate `sysData + gfxData`.

7. **Version → flags:** version nibbles are split across the two flag words:
   ```csharp
   uint uv = (uint)version;
   uint sv = (uv >> 4) & 0xF;   // high nibble -> system flags bits 28-31
   uint gv = (uv >> 0) & 0xF;   // low nibble  -> graphics flags bits 28-31
   uint sf = systemPageFlags.Value + (sv << 28);
   uint gf = graphicsPageFlags.Value + (gv << 28);
   ```
   Reverse (RpfFile.cs:2605): `GetVersionFromFlags = (sv << 4) + gv`.

8. **Compress:** `var cdata = compress ? Compress(tdata) : tdata;` — raw **Deflate** (`System.IO.Compression.DeflateStream`, no zlib header, no gzip).

9. **RSC7 header (16 bytes, little-endian):**
   ```
   offset 0: uint 0x37435352      // "RSC7" magic (RESOURCE_IDENT)
   offset 4: int  version         // e.g. 165 for YDR
   offset 8: uint sf              // system page flags + version high nibble
   offset 12: uint gf             // graphics page flags + version low nibble
   offset 16: deflate-compressed (sys+gfx) payload
   ```
   Return value = complete loose-file bytes; write straight to disk as `.ydr`/`.yft` (OpenIV-compatible format).

Constants: `RESOURCE_IDENT = 0x37435352`, `BASE_SIZE = 0x2000`, `SKIP_SIZE = 16`, `ALIGN_SIZE = 16`.

### Versions (confirmed from `GetVersion(bool gen9)` in each FileTypes class)

| Type | gen8 (legacy) | gen9 |
|------|---------------|------|
| YDR (`YdrFile.cs:96`)  | **165** | 159 |
| YDD (`YddFile.cs:125`) | **165** | 159 |
| YFT (`YftFile.cs:94`)  | **162** | 171 |
| YTD (`YtdFile.cs:91`)  | **13**  | 5   |
| YPT (`YptFile.cs:120`) | **68**  | 71  |

`YdrFile.Save()` (YdrFile.cs:80):
```csharp
var gen9 = RpfManager.IsGen9;
if (gen9) { Drawable?.EnsureGen9(); }
byte[] data = ResourceBuilder.Build(Drawable, GetVersion(gen9), true, gen9);
return data;
```
`YftFile.Save()` is identical with `Fragment` / `FragType.EnsureGen9()`. Note `compress` is always `true` and gen9 comes from the global static — see §3.

---

## 2. Does mutating `LightAttributes.data_items` + `Save()` produce a correct file? — YES

`Drawable.LightAttributes` is `ResourceSimpleList64<LightAttributes>` (Drawable.cs:6689); on FragType it's the same type at Frag.cs:89. It is an *embedded part* of the Drawable block — `Drawable.GetParts()` (Drawable.cs:6812):

```csharp
public override Tuple<long, IResourceBlock>[] GetParts()
{
    return new Tuple<long, IResourceBlock>[] {
        new Tuple<long, IResourceBlock>(0xB0, LightAttributes),
    };
}
```
(FragType embeds it at offset `0x110`, Frag.cs:785.) The 16-byte list header lives inside the Drawable's 208-byte block; `Drawable.Write` calls `writer.WriteBlock(this.LightAttributes)` inline at that offset.

`ResourceSimpleList64<T>` (ResourceBaseTypes.cs:739) — verbatim, the load-bearing pieces:

```csharp
public override long BlockLength { get { return 16; } }

// structure data
public ulong EntriesPointer { get; private set; }
public ushort EntriesCount { get; private set; }
public ushort EntriesCapacity { get; private set; }

// reference data
public T[] data_items { get; set; }

private ResourceSimpleArray<T> data_block;//used for saving.
```

**Write — EntriesCount/Pointer/Capacity are all recomputed from `data_block` at write time:**

```csharp
public override void Write(ResourceDataWriter writer, params object[] parameters)
{
    // update structure data //TODO: fix
    this.EntriesPointer = (ulong)(this.data_block != null ? this.data_block.FilePosition : 0);
    this.EntriesCount = (ushort)(this.data_block != null ? this.data_block.Count : 0);
    this.EntriesCapacity = (ushort)(this.data_block != null ? this.data_block.Count : 0);

    // write structure data
    writer.Write(this.EntriesPointer);
    writer.Write(this.EntriesCount);
    writer.Write(this.EntriesCapacity);
    writer.Write((uint)0x00000000);
}
```

**GetReferences — `data_block` is rebuilt from the CURRENT `data_items` on every build:**

```csharp
public override IResourceBlock[] GetReferences()
{
    var list = new List<IResourceBlock>();
    if (data_items?.Length > 0)
    {
        data_block = new ResourceSimpleArray<T>();
        data_block.Data = new List<T>();
        data_block.Data.AddRange(data_items);
        list.Add(data_block);
    }
    else
    {
        data_block = null;
    }
    return list.ToArray();
}
```

Chain during `Build()`: `GetBlocks` → `GetReferences()` recreates `data_block` (a `ResourceSimpleArray<LightAttributes>` whose `BlockLength` = sum of item BlockLengths = `168 * count`; `LightAttributes.BlockLength = 168`, Drawable.cs:5678) → `AssignPositions2` gives `data_block` a `FilePosition` and (via parts propagation) positions each `LightAttributes` item → `Write` stamps pointer/count/capacity from `data_block`.

**Conclusion: after add/remove/edit of `Drawable.LightAttributes.data_items` (or `FragType.LightAttributes.data_items`), calling `ydr.Save()` / `yft.Save()` is sufficient. No other bookkeeping exists for light counts anywhere** — `EntriesCount` even has a *private setter*, you couldn't update it manually if you wanted to (the stale in-memory value between load and save is cosmetic only; it refreshes during Write). Zero lights is handled: `data_block = null` → pointer/count/capacity all write as 0. CodeWalker's own `ModelLightForm.cs` does exactly this pattern (`lights.ToList()` → add/remove → `LightAttributes.data_items = lights.ToArray()`), with no extra fixups.

Caveats:
- Count is a `ushort` — max 65535 lights (practically irrelevant).
- When creating a list from scratch (e.g. new drawable), assign `LightAttributes = new ResourceSimpleList64<LightAttributes>()` and set `data_items` (XML import does `data_items = ... ?? new LightAttributes[0]` for YFT — Frag.cs:513-518; never leave `LightAttributes` itself null, since `Drawable.Write` calls `writer.WriteBlock(this.LightAttributes)` unconditionally → NRE).

Related base types for reference:
- `ResourceSimpleList64_s<T>` (struct variant, ResourceBaseTypes.cs:831): identical pattern via `ResourceSystemStructBlock<T>` (`BlockLength = Items.Length * Marshal.SizeOf(T)`); also fully self-updating.
- `ResourceSimpleList64b_s<T>` (ResourceBaseTypes.cs:912): **exception** — uint count/capacity and `EntriesCount` has a public setter with comment `//this needs to be set manually for this type! make sure it's <= capacity`. Not used for lights.
- `ResourcePointerArray64<T>` (ResourceBaseTypes.cs:1384): `BlockLength = 8 * data_items.Length`; `Write` rebuilds `data_pointers` from `data_items[i].FilePosition` (null → 0); `GetReferences` returns the items themselves unless `ManualReferenceOverride` is set. Its IList mutation methods (`Add`, `RemoveAt`, ...) all `throw NotImplementedException` — mutate by replacing the `data_items` array, same as the lights list.
- `string_r` (ResourceBaseTypes.cs:40): `BlockLength = Value.Length + 1`; `Write` emits ASCII bytes + NUL (`DataWriter.Write(string)` in Utils/Data.cs:436). `Drawable.GetReferences` recreates `NameBlock = (string_r)Name` each build, so renaming also needs no bookkeeping.

---

## 3. Gen9 gotchas (we stay gen8/legacy)

- `RpfManager.IsGen9` is a **static** bool (RpfManager.cs:35: `public static bool IsGen9 { get; set; } //not ideal for this to be static, but it's most convenient for ResourceData`). **Default = false** (no initializer). It is only set by `RpfManager.Init(..., gen9)` and `Gen9Converter`. In our standalone editor: never set it → everything stays gen8. But note it is *global state*: `ResourceDataReader.IsGen9` initializes from it, `YdrFile.Save`/`YftFile.Save` read it, and `ResourceSimpleArray.GetParts()` reads it directly (ResourceBaseTypes.cs:714 `var gen9 = RpfManager.IsGen9;//TODO: this is BAD to have here...`). Keep it `false` for the whole process lifetime.
- `Save()` ignores what generation the file was *loaded* as — it always writes the format selected by `RpfManager.IsGen9`. With it false, a gen8 YDR round-trips as gen8 v165 / YFT v162. Do NOT call `EnsureGen9()` (it stomps VFTs and zeroes `LightAttributes.Unknown_0h/4h`, Drawable.cs:6667-6674 — only needed for gen9 output).
- Loaders are tolerant: `YdrFile.Load` flips `rd.IsGen9 = false` when it sees version 165 even if the global says gen9 (YdrFile.cs:44-57; YFT does the same for 162). So loading legacy files works regardless; saving is where the global matters.
- `BlockLength_Gen9` defaults to `BlockLength` (ResourceData.cs:706) — gen8 path never touches gen9 lengths. `LightAttributes` has no gen9 override (168 bytes both gens).
- Pass `gen9: false` (the default) to `ResourceBuilder.Build` — it must match the version number or the length checks/pointer layout will be wrong.

---

## 4. Risks: stale/duplicated data, and Decompress/Compress pairing

Things that could go stale after editing lights — and why they don't (or when they could):

- **`EntriesCount/EntriesPointer/EntriesCapacity`** — recomputed in `Write` from a `data_block` freshly rebuilt in `GetReferences` (see §2). Safe.
- **`BlockLength` assumptions** — nothing caches block lengths: `ResourceBuilderBlock.Length` is snapshotted from `block.BlockLength` at the start of each Build, and every property (`ResourceSimpleArray.BlockLength` sums items live, `string_r` = len+1, fixed sizes elsewhere) is computed on demand. The write-loop length check (`"error in system length"`) catches any mismatch anyway. The one deliberate mismatch — `ResourcePagesInfo` sized at 128 pages during layout, shrunk before write — is intentional over-allocation and passes the check because `blen` is re-read at write time.
- **`FilePagesInfo`** — replaced with a new object every `Build`; old page counts never leak.
- **Block positions from load** — `FilePosition` values from the reader are fully reassigned by `AssignPositions2`; nothing consumes load-time positions during save. All pointer fields (`NamePointer`, `BoundPointer`, `EntriesPointer`, `data_pointers`, ...) are recomputed in each block's `Write` from the referenced block's new `FilePosition`.
- **Reader-side caches are irrelevant to save** — `ResourceDataReader.blockPool`/`arrayPool` only dedupe during load. `ResourceSimpleList64` and `ResourceSimpleArray` are `IResourceNoCacheBlock`, so light lists are never pooled/shared between objects; each Drawable owns its items.
- **Genuinely stale things to watch in a viewer/editor (not file correctness):**
  - `RpfResourceFileEntry.SystemFlags/GraphicsFlags/FileSize` on the in-memory entry are NOT updated by `Save()` — they describe the file as loaded. If you re-import the saved bytes, build a new entry via `RpfFile.CreateResourceFileEntry` (it re-reads the RSC7 header). CodeWalker's own save-test does `RpfFile.LoadResourceFile(new YdrFile(), bytes, 165)` to verify round-trips (GameFileCache.cs:4049-4055) — a cheap validation we can copy after every save.
  - Renderer-side copies of light data (our DX11 buffers) — that's what `LightAttributes.UpdateRenderable` flag is for in CodeWalker.
  - `FragDrawable`s inside a YFT share the FragType's light list only via `OwnerFragment` — there is exactly ONE light list per YFT (on `FragType`) and one per YDR (on `Drawable`); no duplication to desync.
- **Sharing one `LightAttributes` object instance in two drawables' `data_items`**: safe for output correctness (`ResourceSimpleArray` embeds copies by writing each item at its own position — actually each item is positioned once per owning data_block via parts propagation; the LAST propagation wins for `FilePosition`, but since items are *parts* (written inline by the array at sequential offsets... they are written when the data_block block writes) — avoid sharing instances anyway; clone lights when copying between drawables.

### Decompress/Compress pair for loose files

- Both are raw **DeflateStream** (no zlib/gzip wrapper): `Compress` = `DeflateStream(ms, CompressionMode.Compress, true)`; `Decompress` = `DeflateStream(new MemoryStream(data), CompressionMode.Decompress)` → `CopyTo`. Symmetric; also matches OpenIV loose-resource format.
- **Load path for a loose .ydr** (`RpfFile.LoadResourceFile`, RpfFile.cs:681): `CreateResourceFileEntry(ref data, ver)` checks `BitConverter.ToUInt32(data,0) == 0x37435352`; if RSC7 present it takes version + SystemFlags(+8) + GraphicsFlags(+12) from the header and **strips the 16-byte header from `data`**; if absent it assumes an *uncompressed* resource blob and synthesizes flags from size (`GetFlagsFromSize`). Then `data = ResourceBuilder.Decompress(data);` and `file.Load(data, resentry)`. Note: the header-present path always Decompresses — a loose RSC7 file's payload MUST be deflate-compressed (which `Build(compress:true)` guarantees). Never write `Build(..., compress:false)` output to a loose file with the RSC7 header — it won't load back.
- The reader then slices: `systemStream = new MemoryStream(data, 0, SystemSize); graphicsStream = new MemoryStream(data, SystemSize, GraphicsSize)` where sizes come from the flags' `Size` properties — this is why Build pads both segments to exactly `pageFlags.Size` before compressing.
- Round-trip note: saved files won't be byte-identical to originals (different packer than R*), but CodeWalker's packing is game-accepted and its own save-test reloads them.

### Minimal safe save recipe for our editor (gen8)

```csharp
// after mutating drawable.LightAttributes.data_items (add/remove/edit):
RpfManager.IsGen9 = false;                    // ensure (default)
byte[] bytes = ydr.Save();                    // = ResourceBuilder.Build(Drawable, 165, true, false)
File.WriteAllBytes(path, bytes);              // loose RSC7 .ydr, OpenIV/game compatible
// optional verification (CodeWalker does this in its save tests):
var test = new YdrFile(); RpfFile.LoadResourceFile(test, bytes, 165);
// throws inside Build on any BlockLength mismatch => corrupt output is impossible to write silently
```
Same for YFT with `yft.Save()` (version 162), mutating `yft.Fragment.LightAttributes.data_items`.
