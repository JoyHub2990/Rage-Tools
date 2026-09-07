# GTA V Texture Decoding for DX11 Upload (CodeWalker research notes)

Sources (CodeWalker-master, exact paths):
- `CodeWalker.Core/GameFiles/Resources/Texture.cs` — TextureDictionary, TextureBase, Texture, TextureData, TextureFormat enums
- `CodeWalker.Core/GameFiles/FileTypes/YtdFile.cs` — YTD load/save API
- `CodeWalker.Core/GameFiles/Utils/DDSIO.cs` — DXGI format mapping, pitch math, DDS import/export (note: it is under `GameFiles/Utils/`, not `Utils/`)
- `CodeWalker/GameFiles/TextureFormats.cs` — renderer-side SharpDX format helper (duplicate of DDSIO mapping)
- `CodeWalker/Rendering/Renderable.cs` lines 1241-1365 — `RenderableTexture` (D3D11 Texture2D creation)
- `CodeWalker.Core/GameFiles/Utils/Jenk.cs` — JenkHash

---

## 1. TextureDictionary structure

Class `TextureDictionary : ResourceFileBase`, `BlockLength => 64`.

Fields read after the 16-byte ResourceFileBase header (legacy/gen7 resource layout):

```csharp
public uint Unknown_10h;   // 0
public uint Unknown_14h;   // 0
public uint Unknown_18h;   // 1
public uint Unknown_1Ch;   // 0
public ResourceSimpleList64_uint TextureNameHashes;   // at struct offset 0x20
public ResourcePointerList64<Texture> Textures;       // at struct offset 0x30
public Dictionary<uint, Texture> Dict;                // built at load time
```

- `TextureNameHashes.data_items` is `uint[]` — Jenkins hashes of lower-cased texture names, **sorted ascending** (see `BuildFromTextureList`, which sorts by NameHash before writing).
- `Textures.data_items` is `Texture[]`, parallel to the hash array (index i of hashes matches index i of textures).

Enumerating: iterate `Textures.data_items` (may be null; each item may be null).

Lookup by hash (`BuildDict` pairs the two parallel arrays):

```csharp
public Texture Lookup(uint hash)
{
    Texture tex = null;
    if (Dict != null) Dict.TryGetValue(hash, out tex);
    return tex;
}

private void BuildDict()
{
    var dict = new Dictionary<uint, Texture>();
    for (int i = 0; (i < Textures.data_items.Length) && (i < TextureNameHashes.data_items.Length); i++)
        dict[TextureNameHashes.data_items[i]] = Textures.data_items[i];
    Dict = dict;
}
```

Name hash = **JenkHash of the lower-cased name** (extension stripped already in resource data). Exact hash function (`Jenk.cs`):

```csharp
public static uint GenHash(string text)
{
    if (text == null) return 0;
    uint h = 0;
    for (int i = 0; i < text.Length; i++)
    {
        h += (byte)text[i];
        h += (h << 10);
        h ^= (h >> 6);
    }
    h += (h << 3);
    h ^= (h >> 11);
    h += (h << 15);
    return h;
}
// usage: NameHash = JenkHash.GenHash(Name.ToLowerInvariant());
```

Embedded texture dictionaries: drawables have `drawable.ShaderGroup.TextureDictionary` (same `TextureDictionary` type); the renderer resolves missing geometry textures with `drawable.ShaderGroup.TextureDictionary.Lookup(tex.NameHash)` (Renderer.cs ~line 3879), and HD/external ones via `ytd.TextureDict.Lookup(tex.NameHash)`.

---

## 2. Texture / TextureBase fields

`TextureBase : ResourceSystemBlock`, `BlockLength => 80`. `Texture : TextureBase`, `BlockLength => 144` (legacy).

Key fields (all live on TextureBase for gen9 compat, but in the **legacy file layout** the base struct is the first 80 bytes and the Texture subclass reads the rest):

Legacy TextureBase read order (offsets from block start):
```
0x00 VFT           uint
0x04 Unknown_4h    uint (=1)
0x08..0x24         8 x uint zeros (Unknown_8h..Unknown_24h)
0x28 NamePointer   ulong  -> Name string (Name = reader.ReadStringAt(NamePointer))
0x30 Unknown_30h   ushort (=1)
0x32 Unknown_32h   ushort (0x2 for base/shaderparam; 0x20/0x28/0x30/0x38/0x40/0x48/0x80/0x90 for real textures)
0x34..0x3C         3 x uint zeros
0x40 UsageData     uint   (Usage = UsageData & 0x1F; UsageFlags = UsageData >> 5)
0x44 Unknown_44h   uint
0x48 ExtraFlags    uint   (0 or 1)
0x4C Unknown_4Ch   uint
```

Legacy `Texture` subclass read order (continues at 0x50):
```
0x50 Width         ushort
0x52 Height        ushort
0x54 Depth         ushort  (=1 normally)
0x56 Stride        ushort  <- bytes per PIXEL row of mip 0 (width * bpp/8), NOT the BC block-row pitch
0x58 Format        uint    (TextureFormat enum, see section 3)
0x5C Unknown_5Ch   byte    (0)
0x5D Levels        byte    <- mip count
0x5E Unknown_5Eh   ushort  (0)
0x60..0x6C         4 x uint zeros
0x70 DataPointer   ulong   -> TextureData (graphics segment)
0x78..0x8C         6 x uint zeros
```

Reference data set at load: `Name` (string), `NameHash` (uint), `Data` (TextureData).

### TextureData — the pixel blob and its size formula

`TextureData : ResourceGraphicsBlock` holds a single `byte[] FullData` containing **all mip levels contiguously, mip 0 first**, tightly packed (no per-mip alignment/padding). Legacy read:

```csharp
// parameters: format, Width, Height, Levels, Stride
int fullLength = 0;
int length = Stride * Height;      // mip 0 byte size (Stride = bytes per pixel row)
for (int i = 0; i < Levels; i++)
{
    fullLength += length;
    length /= 4;                   // each mip is 1/4 the bytes
}
FullData = reader.ReadBytes(fullLength);
```

**CRITICAL CAVEAT:** this `/= 4` progression under-counts tail mips of BC-compressed textures (a 2x2 or 1x1 mip still occupies one full 8/16-byte block, but the formula shrinks below that; integer division can even reach 0). So `FullData.Length` can be **smaller** than the sum of DirectXTex-computed slice pitches over `Levels` mips. The renderer compensates by only uploading as many mips as actually fit (section 4). Do the same in the port: never trust `Levels` blindly; walk mips accumulating `slicePitch` and stop when the offset passes `FullData.Length`.

Where each mip starts (as used by both DDSIO.GetMipmapImages and RenderableTexture.Load):

```
offset(0) = 0
offset(i+1) = offset(i) + slicePitch(format, Width >> i, Height >> i)   // DirectXTex ComputePitch, below
mipWidth(i)  = Width  / (1 << i)   // integer division, no clamp to 1 in CW code
mipHeight(i) = Height / (1 << i)
```

Stride/pitch of a given mip is **recomputed from the format**, never derived from the stored `Stride` field. `TextureBase.CalculateStride()` (used for gen9 conversion) returns DirectXTex `rowPitch` of mip 0 — note this DIFFERS from the legacy in-file `Stride` semantics for BC formats (block-row pitch vs pixel-row bytes).

`Texture.MemoryUsage = Data.FullData.LongLength`.

### TextureUsage / TextureUsageFlags (for completeness)

`Usage = (TextureUsage)(UsageData & 0x1F)`; `UsageFlags = (TextureUsageFlags)(UsageData >> 5)`.

```csharp
public enum TextureUsage : byte
{
    UNKNOWN = 0, DEFAULT = 1, TERRAIN = 2, CLOUDDENSITY = 3, CLOUDNORMAL = 4,
    CABLE = 5, FENCE = 6, ENVEFF = 7, SCRIPT = 8, WATERFLOW = 9, WATERFOAM = 10,
    WATERFOG = 11, WATEROCEAN = 12, WATER = 13, FOAMOPACITY = 14, FOAM = 15,
    DIFFUSEMIPSHARPEN = 16, DIFFUSEDETAIL = 17, DIFFUSEDARK = 18,
    DIFFUSEALPHAOPAQUE = 19, DIFFUSE = 20, DETAIL = 21, NORMAL = 22,
    SPECULAR = 23, EMISSIVE = 24, TINTPALETTE = 25, SKIPPROCESSING = 26,
    DONOTOPTIMIZE = 27, TEST = 28, COUNT = 29,
}
```
Flags: bit0 NOT_HALF, bit1 HD_SPLIT, bits2-17 sizing flags (X2..Y2048), bit18 EMBEDDEDSCRIPTRT, bit22 FLAG_FULL, bit23 MAPS_HALF, bit24 UNK24 ("used by almost everything").

---

## 3. TextureFormat enum (legacy) and DXGI mapping

Exact enum, `Texture.cs` lines 1125-1143:

```csharp
public enum TextureFormat : uint
{
    D3DFMT_A8R8G8B8 = 21,
    D3DFMT_X8R8G8B8 = 22,
    D3DFMT_A1R5G5B5 = 25,
    D3DFMT_A8       = 28,
    D3DFMT_A8B8G8R8 = 32,
    D3DFMT_L8       = 50,
    // fourCC (little-endian fourcc value stored in the uint)
    D3DFMT_DXT1 = 0x31545844,  // 'DXT1'
    D3DFMT_DXT3 = 0x33545844,  // 'DXT3'
    D3DFMT_DXT5 = 0x35545844,  // 'DXT5'
    D3DFMT_ATI1 = 0x31495441,  // 'ATI1' (BC4)
    D3DFMT_ATI2 = 0x32495441,  // 'ATI2' (BC5)
    D3DFMT_BC7  = 0x20374342,  // 'BC7 '
}
```

Mapping to DXGI (both `DDSIO.GetDXGIFormat` and `TextureFormats.GetDXGIFormat` are identical):

| TextureFormat (value) | DXGI_FORMAT (value) | bits/px | notes |
|---|---|---|---|
| D3DFMT_DXT1 (0x31545844) | BC1_UNORM (71) | 4 | 8 B / 4x4 block |
| D3DFMT_DXT3 (0x33545844) | BC2_UNORM (74) | 8 | 16 B / block |
| D3DFMT_DXT5 (0x35545844) | BC3_UNORM (77) | 8 | 16 B / block |
| D3DFMT_ATI1 (0x31495441) | BC4_UNORM (80) | 4 | 8 B / block |
| D3DFMT_ATI2 (0x32495441) | BC5_UNORM (83) | 8 | 16 B / block |
| D3DFMT_BC7 (0x20374342) | BC7_UNORM (98) | 8 | 16 B / block |
| D3DFMT_A1R5G5B5 (25) | B5G5R5A1_UNORM (86) | 16 | |
| D3DFMT_A8 (28) | A8_UNORM (65) | 8 | |
| D3DFMT_A8B8G8R8 (32) | R8G8B8A8_UNORM (28) | 32 | bytes in memory: R,G,B,A |
| D3DFMT_L8 (50) | R8_UNORM (61) | 8 | luminance -> red channel only; shader must swizzle .rrr if grayscale display wanted |
| D3DFMT_A8R8G8B8 (21) | B8G8R8A8_UNORM (87) | 32 | bytes in memory: B,G,R,A (classic D3D9 ARGB) |
| D3DFMT_X8R8G8B8 (22) | B8G8R8X8_UNORM (88) | 32 | alpha byte present but ignored |
| anything else | UNKNOWN (0) | — | fail/skip |

**Byte-order caveats:** D3D9 format names read MSB->LSB of a packed little-endian dword, so `A8R8G8B8` is byte sequence B,G,R,A in memory — upload directly as `DXGI_FORMAT_B8G8R8A8_UNORM`, do NOT swizzle the data. `A8B8G8R8` is byte sequence R,G,B,A — upload as `R8G8B8A8_UNORM`. CodeWalker's CPU decode path (`DDSIO.GetPixels`) outputs BGRA8 and therefore sets `swaprb = true` for R8G8B8A8 input and `false` for B8G8R8A8/B8G8R8X8/A8 — that swap is only for its CPU pixel readback (thumbnails), not the GPU path. The GPU path uploads `FullData` bytes untouched.

sRGB: legacy GTA V formats carry no sRGB flag; CodeWalker uploads everything as UNORM (gamma handled in shaders).

`DXGI_FORMAT` enum in DDSIO is the standard Windows numbering (UNKNOWN=0 ... B4G4R4A4_UNORM=115), verified lines 1591-1726 — safe to use official `DXGI_FORMAT` values / Vortice / SharpDX equivalents directly.

`TextureFormats.ByteSize(fmt)` (renderer helper) returns **bits per pixel**, misleading name; unused in the upload math except being computed.

Gen9 (`TextureFormatG9`, rage::sga::BufferFormat) — only needed if loading gen9 (Enhanced ed.) resources. Values mirror DXGI but with different numbering: BC1_UNORM=0x47, BC2_UNORM=0x4A, BC3_UNORM=0x4D, BC4_UNORM=0x50, BC5_UNORM=0x53, BC6H_UF16=0x5F, BC7_UNORM=0x62, BC7_UNORM_SRGB=0x63, R8G8B8A8_UNORM=0x1C, B8G8R8A8_UNORM=0x57, B5G5R5A1_UNORM=0x56, A8_UNORM=0x41, R8_UNORM=0x3D, R16_UNORM=0x38, etc. Legacy<-gen9 conversion (`GetLegacyFormat`): R8G8B8A8_UNORM->A8B8G8R8, B8G8R8A8_UNORM->A8R8G8B8, A8->A8, R8->L8, B5G5R5A1->A1R5G5B5, BC1->DXT1, BC2->DXT3, BC3->DXT5, BC4->ATI1, BC5->ATI2, BC7(&SRGB)->BC7, BC3_SRGB->DXT5, R16_UNORM->A8 (lossy TODOs in CW). On gen9, `TextureData` length is given by `TextureBase.CalcDataSize()` = sum over `Levels` of DirectXTex `slicePitch(Width/2^i, Height/2^i)` times `Depth` — i.e. the *correct* chain length, unlike legacy.

---

## 4. RenderableTexture: exact D3D11 upload (Renderable.cs 1241-1332)

CodeWalker creates one immutable-style `Texture2D` per game texture with initial data for every mip. Verbatim core of `Load(Device device)`:

```csharp
using (var stream = DataStream.Create(Key.Data.FullData, true, false))
{
    var format = TextureFormats.GetDXGIFormat(Key.Format);
    var width = Key.Width;
    var height = Key.Height;
    int mips = Key.Levels;
    int rowpitch, slicepitch;
    var totlength = Key.Data.FullData.Length;

    //get databoxes for mips
    int offset = 0;
    int level = 1;
    List<DataBox> boxes = new List<DataBox>();
    for (int i = 0; i < mips; i++)
    {
        if (offset >= totlength) break; //only load as many mips as there are..

        var mipw = width / level;
        var miph = height / level;

        TextureFormats.ComputePitch(format, mipw, miph, out rowpitch, out slicepitch, 0);
        var mipbox = new DataBox(stream.DataPointer + offset, rowpitch, slicepitch);
        boxes.Add(mipbox);

        offset += slicepitch;
        level *= 2;
    }
    mips = boxes.Count;

    var desc = new Texture2DDescription()
    {
        ArraySize = 1,
        BindFlags = BindFlags.ShaderResource,
        CpuAccessFlags = CpuAccessFlags.None,
        Format = format,
        Height = Key.Height,
        MipLevels = mips,          // number of boxes actually built, NOT Key.Levels
        OptionFlags = ResourceOptionFlags.None,
        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        Width = Key.Width
    };
    Texture2D = new Texture2D(device, desc, boxes.ToArray());
    ShaderResourceView = new ShaderResourceView(device, Texture2D);
}
```

Per-mip subresource recipe (port checklist):
1. `format = GetDXGIFormat(tex.Format)`; bail if UNKNOWN.
2. `offset = 0`; for i in 0..Levels-1: **if offset >= FullData.Length -> stop** (handles the short-tail FullData described in section 2).
3. `mipw = Width / (1<<i)`, `miph = Height / (1<<i)` (plain integer division; recommend clamping to >=1 in the port for uncompressed formats — CW relies on ComputePitch's block clamp for BC and rarely hits 0-size mips).
4. `ComputePitch(format, mipw, miph, out rowPitch, out slicePitch)`; subresource i = { pSysMem = FullData + offset, SysMemPitch = rowPitch, SysMemSlicePitch = slicePitch }.
5. `offset += slicePitch`.
6. Create Texture2D with `MipLevels = number of subresources actually built`, ArraySize 1, Default usage, ShaderResource bind, sample (1,0); then a default SRV. CW wraps creation in try/catch and fails silently (bad/corrupt textures exist in game data).

`ComputePitch` (identical in `TextureFormats.cs` and `DDSIO.DXTex`, ported from DirectXTex), the cases that matter for GTA V formats:

```csharp
// BC1, BC4 (8 bytes per 4x4 block):
nbw = Math.Max(1, (width + 3) / 4);
nbh = Math.Max(1, (height + 3) / 4);
rowPitch = nbw * 8;  slicePitch = rowPitch * nbh;

// BC2, BC3, BC5, BC6H, BC7 (16 bytes per block):
nbw = Math.Max(1, (width + 3) / 4);
nbh = Math.Max(1, (height + 3) / 4);
rowPitch = nbw * 16; slicePitch = rowPitch * nbh;

// everything uncompressed (default case):
int bpp = BitsPerPixel(fmt);         // 32 for BGRA/RGBA8, 16 for B5G5R5A1, 8 for A8/R8
rowPitch = (width * bpp + 7) / 8;    // byte alignment, no dword padding
slicePitch = rowPitch * height;
```

(The DDSIO.DXTex version adds packed/planar/Xbox cases and CP_FLAGS alignment variants — irrelevant for GTA V legacy textures; always called with flags=0.)

Caching pattern: `RenderableCache.GetRenderableTexture(Texture)` keyed on the `Texture` object; `Init` records `DataSize = FullData.Length`, `Load` runs on the render thread, `Unload` disposes SRV then Texture2D. Binding: `context.PixelShader.SetShaderResource(slot, ShaderResourceView)`.

---

## 5. YtdFile load API (YtdFile.cs)

```csharp
public class YtdFile : GameFile, PackedFile
{
    public TextureDictionary TextureDict { get; set; }

    public YtdFile();                       // GameFileType.Ytd
    public YtdFile(RpfFileEntry entry);

    // A) load from a raw compressed .ytd file (e.g. read off disk):
    public void Load(byte[] data)
    {
        RpfFile.LoadResourceFile(this, data, (uint)GetVersion(RpfManager.IsGen9)); // version 13 legacy, 5 gen9
        Loaded = true;
    }

    // B) load with an RPF entry (from inside an rpf archive):
    public void Load(byte[] data, RpfFileEntry entry)
    {
        // entry must be RpfResourceFileEntry, else throws
        ResourceDataReader rd = new ResourceDataReader(resentry, data);
        // gen9 quirk: if rd.IsGen9 and resentry.Version == 13 -> rd.IsGen9 = false;
        TextureDict = rd.ReadBlock<TextureDictionary>();
    }

    public byte[] Save();                   // ResourceBuilder.Build(TextureDict, version, true, gen9)
    public int GetVersion(bool gen9) => gen9 ? 5 : 13;
}
```

Path A (`Load(byte[])`) is the simplest for a standalone editor: pass the raw file bytes of a `.ytd`; `RpfFile.LoadResourceFile` parses the RSC7 header, decompresses, builds a synthetic resource entry and ends up calling `Load(data, entry)`. Then enumerate `ytd.TextureDict.Textures.data_items` or `ytd.TextureDict.Lookup(hash)`.

XML/DDS round-trip helpers (useful for import/export features):
- `DDSIO.GetDDSFile(Texture)` -> full `.dds` byte[] (writes legacy or DX10 header, then FullData mips; per-mip sizes via ComputePitch).
- `DDSIO.GetTexture(byte[] ddsfile)` -> new `Texture` with Width/Height/Depth/Levels/Format/Stride and `Data.FullData` = everything after the DDS header (`Stride = slicePitch / height` of mip 0; `Format` via `GetTextureFormat(dxgi)` which only accepts the 12 supported formats, else 0).
- `DDSIO.GetPixels(Texture, mip)` -> CPU-decoded BGRA8 byte[] for a single mip (BC1/2/3/4/5 software decoders present; **BC7 decode is NOT implemented** — returns null; A8/L8/A1R5G5B5 are converted up to 32bpp).

---

## 6. Port gotchas summary

1. **Trust ComputePitch, not the stored `Stride`**, for upload pitches; the file's `Stride` is bytes-per-pixel-row of mip 0 (e.g. width/2 for DXT1), used only in the FullData length formula.
2. **FullData can be shorter than the theoretical mip chain** (legacy `/4` size formula truncates BC tail mips). Stop adding subresources at `offset >= FullData.Length` and set `MipLevels` to the count actually added — otherwise CreateTexture2D reads out of bounds or D3D11 rejects mismatched mip count.
3. **No swizzling on the GPU path** — pick the right DXGI format instead (A8R8G8B8->B8G8R8A8_UNORM, A8B8G8R8->R8G8B8A8_UNORM, X8R8G8B8->B8G8R8X8_UNORM).
4. `GetDXGIFormat` returns UNKNOWN for unmapped values — guard against it (CW silently catches texture-creation failures).
5. Mip dims use integer halving without a >=1 clamp in CW; ComputePitch's `Math.Max(1, (w+3)/4)` saves BC formats. Clamp `max(1, w>>i)` for uncompressed to be safe.
6. Name-hash lookups require lower-casing before JenkHash. Dictionary hash arrays are sorted ascending; keep that invariant if writing YTDs back.
7. `Depth` > 1 / cubemaps effectively unhandled by the legacy renderer path (ArraySize always 1, Texture2D only); GTA V drawable/light textures are all 2D.
8. BC7 needs GPU upload (format BC7_UNORM works fine in D3D11); only CodeWalker's CPU preview decoder lacks BC7.
