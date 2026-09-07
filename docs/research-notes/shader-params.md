# RAGE shader parameters: textures + material params per geometry (CodeWalker reference)

Sources (CodeWalker-master):
- `CodeWalker.Core/GameFiles/Resources/Drawable.cs` — `ShaderGroup` (line ~14), `ShaderFX` (~178), `ShaderParameter` (~537), `ShaderParametersBlock` (~570), `DrawableGeometry` (~3665), `DrawableBase.AssignGeometryShaders` (~6438)
- `CodeWalker.Core/GameFiles/Resources/ShaderParams.cs` — `public enum ShaderParamNames : uint` (line 15)
- `CodeWalker/Rendering/Renderable.cs` — `RenderableGeometry` (~813), `GetTextureSamplerList` (~866), `Init(DrawableGeometry)` (~909)
- `CodeWalker/Rendering/ShaderManager.cs` — blend states (~160), `Enqueue` (~864), `ShaderRenderBucket.GroupBatches` (~1085)
- `CodeWalker/Rendering/Shaders/BasicShader.cs` — `SetGeomVars` (~604)
- `CodeWalker.Shaders/BasicPS.hlsl` — decal/alpha/emissive pixel logic
- `CodeWalker/Rendering/Renderer.cs` (~3800) — TextureBase → Texture resolution at render time

All of this is the **legacy (gen8 / pre-gen9)** layout, which is what YDR/YFT editing targets. Gen9 differences are noted at the end.

---

## 1. Object graph: how a geometry gets its material

```
DrawableBase
 ├─ ShaderGroup                       (64-byte block)
 │   ├─ TextureDictionary             (embedded textures, may be null)
 │   └─ Shaders : ResourcePointerArray64<ShaderFX>   (ShadersCount1 entries)
 └─ DrawableModels -> DrawableModel[] -> DrawableGeometry[]
       DrawableGeometry.ShaderID  : ushort  (index into ShaderGroup.Shaders; comes from
                                             DrawableModel.ShaderMapping[geomIndex])
       DrawableGeometry.Shader    : ShaderFX (assigned post-load)
```

`DrawableBase.AssignGeometryShaders(ShaderGroup)` (Drawable.cs ~6446):

```csharp
var shaders = shaderGrp.Shaders.data_items;
foreach (DrawableModel model in AllModels)
    for (int i = 0; i < model.Geometries.Length; i++) {
        var geom = model.Geometries[i];
        var sid = geom.ShaderID;
        geom.Shader = (sid < shaders.Length) ? shaders[sid] : null;
    }
```

`DrawableModel.ShaderMapping` is a `ushort[GeometriesCount1]` read from `ShaderMappingPointer`; during model read each `geom.ShaderID = ShaderMapping[i]`.

---

## 2. ShaderFX — legacy struct layout (BlockLength = 48)

Read order / offsets (Drawable.cs ~255):

| Off | Size | Field | Notes |
|-----|------|-------|-------|
| 0x00 | 8 | ParametersPointer | → ShaderParametersBlock |
| 0x08 | 4 | Name (MetaHash) | shader name hash, e.g. `emissive`, `spec` |
| 0x0C | 4 | Unknown_Ch | 0 |
| 0x10 | 1 | **ParameterCount** (byte) | number of params in ParametersList |
| 0x11 | 1 | **RenderBucket** (byte) | rage draw bucket (0, 1, 2, 3, 6, 7 seen) |
| 0x12 | 2 | Unknown_12h | 32768 (0x8000) normally |
| 0x14 | 2 | ParameterSize | == ParametersList.ParametersSize |
| 0x16 | 2 | ParameterDataSize | == ParametersList.ParametersDataSize (+pad diffs 32..44) |
| 0x18 | 4 | **FileName** (MetaHash) | hash of `<name>.sps` — the .sps preset; drives all renderer classification |
| 0x1C | 4 | Unknown_1Ch | 0 |
| 0x20 | 4 | RenderBucketMask | **always `(1 << RenderBucket) | 0xFF00`** (verified "no hit" test in CW) |
| 0x24 | 2 | Unknown_24h | 0 |
| 0x26 | 1 | Unknown_26h | 0 |
| 0x27 | 1 | TextureParametersCount | count of params with DataType==0 |
| 0x28 | 8 | Unknown_28h | 0 |

`ParametersList` is read with: `reader.ReadBlockAt<ShaderParametersBlock>(ParametersPointer, ParameterCount, this)` — i.e. the count is NOT stored in the block itself; it comes from ShaderFX.

---

## 3. ShaderParameter — 16-byte param header

```csharp
public class ShaderParameter {
    public byte   DataType;     // 0: texture, 1: one Vector4, N>1: array of N Vector4s
    public byte   Unknown_1h;   // register index (see below)
    public ushort Unknown_2h;   // 0
    public uint   Unknown_4h;   // 0
    public ulong  DataPointer;  // ptr to TextureBase block (DataType==0) or to Vector4 data
    public object Data;         // filled after read: TextureBase / Vector4 / Vector4[]
}
```

`Unknown_1h` reconstruction rule (from `ShaderParametersBlock.ReadXml`, needed when writing):
- texture params: `Unknown_1h = (byte)(i + 2)` (i = param index)
- vector/array params, iterating **backwards** from the last param with `offset = 160`:
  `param.Unknown_1h = (byte)offset; offset += param.DataType;`

Also from XML import: texture params created from XML get `tex.Unknown_32h = 2` on the TextureBase (marks it as a texture *reference*).

## 4. ShaderParametersBlock — memory layout & API

In-file layout (legacy):

```
[ ShaderParameter headers ]   Count * 16 bytes
[ Vector4 data blobs      ]   sum(16 * DataType) bytes for every param with DataType != 0
                              (embedded right after the headers; DataPointer also points here)
[ Name hashes             ]   Count * 4 bytes (uint32 Jenkins hashes, ShaderParamNames values)
[ zero padding            ]   32 + ParametersDataSize*4 bytes written at save time
```

Size formulas (exact CW code):

```csharp
BaseSize = 32 + Σ(16 + 16*DataType) + Count*4;           // headers + vec data + hashes + 32
ParametersSize = Count*16 + Σ(16*DataType);              // ushort, stored in ShaderFX 0x14
ParametersDataSize = BaseSize rounded up to 16;          // ushort, stored in ShaderFX 0x16
BlockLength = BaseSize + ParametersDataSize*4;
TextureParamsCount = count of params where DataType==0;  // byte, ShaderFX 0x27
```

Read logic (legacy path, Drawable.cs ~779):

```csharp
// 1) read Count 16-byte headers
for (i in 0..Count) { p = new ShaderParameter(); p.Read(reader); }

// 2) resolve data per header + track embedded-data length
int offset = 0;
foreach p:
    switch (p.DataType) {
        case 0:  p.Data = reader.ReadBlockAt<TextureBase>(p.DataPointer); break;   // texture
        case 1:  offset += 16;             p.Data = reader.ReadStructAt<Vector4>((long)p.DataPointer); break;
        default: offset += 16*p.DataType;  p.Data = reader.ReadStructsAt<Vector4>(p.DataPointer, p.DataType); break; // Vector4[]
    }

// 3) skip over the embedded vector data, then read Count uint32 name hashes
reader.Position += offset;
for (i in 0..Count) Hashes[i] = reader.ReadUInt32();
```

**Iteration API**: `Parameters[i]` pairs with `Hashes[i]` (parallel arrays, same length `Count`).
- `param.DataType == 0` → `param.Data is TextureBase` (may be null if DataPointer was 0 — happens for texture refs resolved externally)
- `param.DataType == 1` → `param.Data is Vector4` — scalar float params store the value in `.X` (Y/Z/W often garbage/zero)
- `param.DataType == N > 1` → `param.Data is Vector4[N]` (e.g. terrain layer arrays, `matDiffuseColor`-style arrays in some shaders)

### TextureBase vs embedded Texture

- `reader.ReadBlockAt<TextureBase>(ptr)` consults a **blockPool** keyed by file position (`ResourceData.cs` ~55, ~179). The drawable's `ShaderGroup.TextureDictionary` is read *before* the shaders, so if the param's DataPointer targets a texture already read from the embedded dictionary, the pool returns that existing **`Texture`** instance (full texture: has pixel `Data`, subclass of `TextureBase`).
- If the pointer targets a standalone 80-byte TextureBase record (name-only), you get a plain **`TextureBase`**: just `Name`/`NameHash` + `Usage` flags — a *texture reference* to be resolved in an external .ytd.
- Runtime test used everywhere in CW: `var ttex = tex as Texture; if (ttex == null) → it's a ref, look up by tex.NameHash` (Renderer.cs ~3816). Resolution order in CW's renderer: ypt-embedded dict → parent SD ytd hierarchy (`texDict` + parents) → `gameFileCache.TryGetTextureDictForTexture(hash)` → drawable's own `ShaderGroup.TextureDictionary` as last resort. Once found, the resolved `Texture` is cached back into `geom.Textures[i]`.

---

## 5. ShaderParamNames — numeric hash values (uint32, Jenkins one-at-a-time of the lowercase name)

`ShaderParams.cs`, `public enum ShaderParamNames : uint`:

### Texture samplers
| Name | Value |
|------|-------|
| DiffuseSampler | **4059966321** (0xF1FE2B71) |
| BumpSampler | **1186448975** (0x46B7C64F) |
| SpecSampler | **1619499462** (0x608799C6) |
| DiffuseSampler2 | **181641832** (0x0AD3A268) |
| DiffuseSampler3 | 1429813046 |
| TextureSampler_layer0 | **3576369631** (0xD52B11DF) |
| TextureSampler_layer1 | **606121937** (0x2420AFD1) |
| TextureSampler_layer2 | **831736502** (0x31934AB6) |
| TextureSampler_layer3 | **2025281789** (0x78B758FD) |
| BumpSampler_layer0 | 1073714531 |
| BumpSampler_layer1 | 1422769919 |
| BumpSampler_layer2 | 2745359528 |
| BumpSampler_layer3 | 2975430677 |
| heightMapSamplerLayer0 | 781078585 |
| heightMapSamplerLayer1 | 2570495372 |
| heightMapSamplerLayer2 | 2346748640 |
| heightMapSamplerLayer3 | 2242969217 |
| TintPaletteSampler | **4131954791** (0xF648A067) |
| TextureSamplerDiffPal | 2878898974 |
| DetailSampler | 3393362404 |
| lookupSampler | 2295086480 |
| heightSampler | 4049987115 |
| EnvironmentSampler | 3317411368 |
| FlowSampler | 1214194352 |
| FogSampler | 2568933054 |
| FoamSampler | 3266349336 |
| DirtSampler | 2124031998 |
| DirtBumpSampler | 3157820509 |
| DiffuseHfSampler | 2946270081 |
| DiffuseExtraSampler | 58635929 |
| distanceMapSampler | 1616890976 |
| textureSamp | 485865047 |
| PlateBgSampler | 1342317448 |
| PlateBgBumpSampler | 1709116366 |
| StippleSampler | 3178703610 |
| FurMaskSampler | 3794875320 |
| ComboHeightSamplerFur01 | 1709265783 |
| ComboHeightSamplerFur23 | 2930074258 |
| ComboHeightSamplerFur45 | 2509158229 |
| ComboHeightSamplerFur67 | 1396547512 |

### Float / vector params (all stored as Vector4, scalar value in .X)
| Name | Value |
|------|-------|
| emissiveMultiplier | **1592520008** (0x5EEBED48; note lowercase 'e' in the enum!) |
| bumpiness | **4134611841** (0xF6712B81) |
| specularIntensityMult | **4095226703** (0xF418334F) |
| specularFalloffMult | **2272544384** (0x87744680) |
| specularFresnel | **666481402** (0x27B9B2FA) |
| HardAlphaBlend | **3913511942** (0xE9437406) |
| useTessellation | **1176544093** (0x4620A35D) |
| matDiffuseColor | **408880252** (0x185F047C) |
| matDiffuseColor2 | 1577977996 |
| matDiffuseColorTint | 99676333 |
| wetnessMultiplier | 853385205 |
| detailSettings | 3038654095 (float4: x,y = detail intensity/bump, z,w = UV tiling) |
| specMapIntMask | 4279333149 (float3 in xyz) |
| DirtDecalMask | 1050016400 |
| umGlobalParams | 570415642 |
| umGlobalOverrideParams | 3341722211 |
| WindGlobalParams | 208642390 |
| globalAnimUV0 | 3617324062 |
| globalAnimUV1 | 3126116752 |
| RippleSpeed | 1172914979 |
| RippleScale | 3553443429 |
| RippleBumpiness | 3108440880 |
| WaveOffset | 2296487471 |
| WaterHeight | 276101176 |
| WaveMovement | 683816830 |
| HeightOpacity | 2935469584 |
| orderNumber | 1617153586 |
| specularIntensityMultSpecMap | 1058366663 |
| specularFalloffMultSpecMap | 2748867194 |

(Hex renderings above are aids; the decimal values are copied verbatim from ShaderParams.cs and are authoritative.)

---

## 6. RenderableGeometry.Init(DrawableGeometry) — extraction pseudocode

CW fields with defaults: `bumpiness=1.0`, `specularIntensityMult=0`, `specularFalloffMult=0`, `specularFresnel=0`, `HardAlphaBlend=0`, `useTessellation=0`, `detailSettings=0`, `specMapIntMask=0`, `IsEmissive=false`, `EnableWind=false`, `SpecOnly=false`, `isHair=false`.

```
Init(dgeom):
  shader = dgeom.Shader                       // may be null!
  if shader == null or shader.ParametersList == null: return

  if shader.FileName.Hash == 3854885487:      // cable.sps
      Topology = LineList                     // else TriangleList

  classify by shader.FileName.Hash:           // FULL lists below
      in EMISSIVE_SPS set   -> IsEmissive = true
      in WIND_SPS set       -> EnableWind = true
      in SPECONLY_SPS set   -> SpecOnly = true      (decal_spec_only/normal_only/amb_only)
      == 100720695          -> isHair = true        (ped_hair_spiked.sps)

  pl = shader.ParametersList.Parameters
  hl = shader.ParametersList.Hashes
  texs = [];  phashes = []
  for i in 0 .. min(pl.len, hl.len)-1:
      pName = (ShaderParamNames)hl[i];  param = pl[i]
      if param.Data is TextureBase:           // any texture param, in file order
          texs.add(param.Data); phashes.add(pName)
      switch pName:                           // all reads are ((Vector4)param.Data)
          HardAlphaBlend        -> HardAlphaBlend = v.X
          useTessellation       -> useTessellation = v.X
          wetnessMultiplier     -> wetnessMultiplier = v.X
          bumpiness             -> bumpiness = v.X
          detailSettings        -> detailSettings = v          (full float4)
          specMapIntMask        -> specMapIntMask = v.XYZ
          specularIntensityMult -> specularIntensityMult = v.X
          specularFalloffMult   -> specularFalloffMult = v.X
          specularFresnel       -> specularFresnel = v.X
          umGlobalParams        -> WindGlobalParams = v
          globalAnimUV0/1       -> globalAnimUV0/1 = v; globalAnimUVEnable = true
          RippleSpeed/Scale/Bumpiness, WaveOffset, WaterHeight,
          WaveMovement, HeightOpacity, DirtDecalMask -> (water/terrain extras, .X or full v)
          orderNumber           -> if (isHair && v.X > 0) disableRendering = true
  Textures = texs; TextureParamHashes = phashes
```

**Note:** CW's Init does NOT read `emissiveMultiplier` — emissive-ness is purely from the .sps filename hash, and the CW pixel shader just adds the unlit diffuse (`if (IsEmissive==1) c.rgb += fc.rgb;` in BasicPS.hlsl ~167). For a nicer port, read `emissiveMultiplier` (hash 1592520008, `.X`) as an intensity scale.

### .sps filename hash sets used by Init (verbatim from Renderable.cs ~932)

EnableWind: `2245870123` trees_normal_diffspec_tnt, `3334613197` trees_tnt, `1229591973` trees_normal_spec_tnt, `2322653400` trees, `3192134330` trees_normal, `1224713457` trees_normal_spec, `4265705004` trees_normal_diffspec, `1581835696` default_um, `3326705511` normal_um, `3085209681` normal_spec_um, `3190732435` cutout_um, `748520668` normal_cutout_um

IsEmissive: `1332909972` normal_spec_emissive, `2072061694` normal_spec_reflect_emissivenight, `2635608835` emissive, `443538781` emissive_clip, `2049580179` emissive_speclum, `1193295596` emissive_tnt, `1434302180` emissivenight, `1897917258` emissivenight_geomnightonly, `140448747` emissivestrong, `1436689415` normal_spec_reflect_emissivenight_alpha, `179247185` emissive_alpha, `1314864030` emissive_alpha_tnt, `1478174766` emissive_additive_alpha, `3733846327` emissivenight_alpha, `3174327089` emissivestrong_alpha, `3924045432` glass_emissive, `837003310` glass_emissivenight, `485710087` glass_emissivenight_alpha, `2055615352` glass_emissive_alpha, `2918136469` decal_emissive_only, `2698880237` decal_emissivenight_only

SpecOnly: `3880384844` decal_spec_only, `341123999` decal_normal_only, `600733812` decal_amb_only

Hair: `100720695` ped_hair_spiked  |  Cable (LineList): `3854885487` cable

---

## 7. Texture selection priority — GetTextureSamplerList + SetGeomVars

`RenderableGeometry.GetTextureSamplerList()` returns the sampler-priority order used by CW's UI/texture pickers (WorldForm/ModelForm etc.), first = most important:

```
DiffuseSampler, SpecSampler, BumpSampler, TintPaletteSampler, DetailSampler,
FlowSampler, FogSampler,
TextureSampler_layer0, BumpSampler_layer0, heightMapSamplerLayer0,
TextureSampler_layer1, BumpSampler_layer1, heightMapSamplerLayer1,
TextureSampler_layer2, BumpSampler_layer2, heightMapSamplerLayer2,
TextureSampler_layer3, BumpSampler_layer3, heightMapSamplerLayer3,
lookupSampler, heightSampler, FoamSampler, DirtSampler, DirtBumpSampler,
DiffuseSampler2, DiffuseSampler3, DiffuseHfSampler,
ComboHeightSamplerFur01, ComboHeightSamplerFur23, ComboHeightSamplerFur45, ComboHeightSamplerFur67,
StippleSampler, FurMaskSampler, EnvironmentSampler, distanceMapSampler, textureSamp
```

At draw time `BasicShader.SetGeomVars` walks `geom.RenderableTextures[i]` / `geom.TextureParamHashes[i]` and slots them (skipping textures whose `NameHash == 1678728908` /*"blank"*/):

| Param hash | Slot |
|---|---|
| DiffuseSampler, PlateBgSampler | diffuse (`texture`) |
| BumpSampler, PlateBgBumpSampler | normal map (`bumptex`) |
| SpecSampler | spec map (`spectex`) |
| DetailSampler | detail map |
| TintPaletteSampler, TextureSamplerDiffPal | tint palette; `tintYVal = (TintPaletteIndex + 0.5) / palette.Height` |
| distanceMapSampler | diffuse + `isdistmap=true` |
| DiffuseSampler2, DiffuseExtraSampler | secondary diffuse (`texture2`) |
| heightSampler, EnvironmentSampler | ignored |
| FlowSampler, FogSampler, FoamSampler, and **any other** hash | fallback: becomes diffuse only if none picked yet (`if (texture == null) texture = itex`) |

So the practical port rule: **diffuse = first of {DiffuseSampler, PlateBgSampler, distanceMapSampler, any-other-texture-param}; bump = BumpSampler; spec = SpecSampler** — matched by hash from `TextureParamHashes`, not by array position.

PS flags derived there: `EnableTexture = usediff?1:0 + usediff2?2:0`, `EnableNormalMap`, `EnableSpecMap`, `EnableDetailMap`, `IsDecal = DecalMode?1:0` (with per-sps overrides: decal_normal_only/mirror_decal/reflect_decal → 3, decal_spec_only/spec_decal → 4, decal_dirt → 2 + `TextureAlphaMask = geom.DirtDecalMask`), `IsEmissive = geom.IsEmissive?1:0`, `bumpiness = geom.bumpiness`, `specularIntensityMult` (zeroed if spec disabled globally), `specularFalloffMult`, `specularFresnel`. NB: CW sends `HardAlphaBlend = 0.0f //todo: cutouts flag!` rather than the geom value.

---

## 8. RenderBucket → draw pass / blend mode in CW

`ShaderFX.RenderBucket` (byte @0x11; `RenderBucketMask = (1<<bucket)|0xFF00`). RAGE bucket convention:

- **0** = opaque/solid (most geometry)
- **1** = double-sided / alpha-tested foliage-ish (rarely differs in CW)
- **2** = decals (no depth write)
- **3** = cutout / alpha-clip
- **4/5** = no-splash / no-water (vehicle)
- **6** = water surface
- **7** = glass / displacement / lens distortion

**Important CW mechanics:** CW does *not* pick blend state from RenderBucket. `ShaderManager.Enqueue` uses the bucket number only as an *ordering index* (`RenderBuckets[shader.RenderBucket]`, list auto-grows; batches keyed by `{shader.Name, shader.FileName}`). Within each bucket, `GroupBatches()` classifies every batch into a pass list **by the .sps FileName hash** (huge switch, ShaderManager.cs ~1105): `BasicBatches`, `DecalBatches`, `AlphaBatches`, `GlassBatches`, `CutoutBatches`, `WaterBatches`, `Water2Batches`, `TerrainBatches`, `TreesLodBatches`, `CableBatches`, `ClothBatches`; unknown sps → BasicBatches.

Pass order and GPU state (`RenderQueued`, ShaderManager.cs ~457):

| Pass (per bucket, ascending) | Blend | Depth | Raster | Notes |
|---|---|---|---|---|
| Terrain, Basic | bsDefault | dsEnabled (write on) | solid | shader forces a=1 when IsDecal==0, discards a≤0.33 (alpha test) |
| TreesLod, Cutout, Cloth | bsDefault | dsEnabled | **double-sided** | |
| Cable | bsDefault | dsEnabled | solid | LineList topology |
| Decal | bsDefault | **dsDisableWrite** (test on, write off) | solid | `Basic.DecalMode = true` → IsDecal=1: keep alpha, discard a≤0, a *= vertexColour0.a |
| grass instanced | bsAlpha | dsEnabled | double-sided | alpha-to-coverage, AlphaScale=7 |
| WaterQuads + WaterBatches | bsDefault | dsEnabled | double-sided | |
| Water2 (foam/decal water) | bsDefault | dsDisableWrite | double-sided | |
| Alpha + Glass batches | bsDefault | **dsDisableWrite** | double-sided | DecalMode=true (so alpha is kept and blended) |
| LOD/deferred lights | **bsAdd** | dsDisableWriteRev | solid | additive |

Blend state descriptors (ShaderManager.cs ~162):
- `bsDefault`: blending **enabled**: `Src=SrcAlpha, Dst=InvSrcAlpha, Op=Add; SrcAlpha=Zero(→A), DstAlpha=One`, AlphaToCoverage off — i.e. standard premultiplied-style alpha over; opaque objects work because the pixel shader sets `c.a = 1` when IsDecal==0.
- `bsAlpha`: same + `AlphaToCoverageEnable = true`.
- `bsAdd`: same but `Dst = One` (additive).

Pixel-shader alpha rules (BasicPS.hlsl ~34):
```hlsl
if ((IsDecal == 0) && (c.a <= 0.33)) discard;   // alpha test for "opaque"/cutout
if ((IsDecal == 1) && (c.a <= 0.0)) discard;    // decal/alpha-blended
if (IsDecal == 0) c.a = 1;
...
if (IsDecal == 1) c.a *= input.Colour0.a;       // vertex alpha modulates decals
...
if (IsEmissive == 1) c.rgb += fc.rgb;           // emissive = add unlit albedo after lighting
```

**Port recipe** (simplified): treat sps names containing `_alpha`/glass as alpha-blended+no-depth-write; `decal*`/`normal*_decal*`/`spec_decal` as decal (no depth write, keep alpha); `cutout*`/`trees*` as alpha-test double-sided; otherwise opaque with a≤0.33 discard. RenderBucket alone is a decent fallback: 0→opaque, 2→decal, 3→cutout, 6→water, 1/7→alpha-blend.

---

## 9. Full extraction pseudocode for a port (per DrawableGeometry)

```
struct GeomMaterial {
    TextureBase diffuse, bump, spec, diffuse2, tintPal;
    float bumpiness = 1, specIntensity = 0, specFalloff = 0 /*use ~100 default*/,
          specFresnel = 0 /*~0.9 default*/, emissiveMult = 1, hardAlphaBlend = 0;
    Vector4 detailSettings; Vector3 specMapIntMask; Vector4 matDiffuseColor = (1,1,1,1);
    bool isEmissive, isDecal, isAlpha, isCutout, doubleSided;
    byte bucket;
}

GeomMaterial Extract(DrawableGeometry g):
    m = new GeomMaterial()
    s = g.Shader; if s == null: return m
    m.bucket = s.RenderBucket
    fn = s.FileName.Hash            // classification driver
    m.isEmissive = fn in EMISSIVE_SPS
    m.isDecal    = fn in DECAL_SPS  or s.RenderBucket == 2
    m.isAlpha    = fn in ALPHA_SPS or fn in GLASS_SPS
    m.isCutout   = fn in CUTOUT_SPS or s.RenderBucket == 3
    m.doubleSided = m.isCutout or fn in TREES_SPS

    pl = s.ParametersList.Parameters;  hl = s.ParametersList.Hashes
    for i in 0..s.ParameterCount-1:
        h = (uint)hl[i]; p = pl[i]
        if p.DataType == 0:                       // texture
            t = p.Data as TextureBase             // null if unresolved ref
            switch h:
                4059966321: m.diffuse = t         // DiffuseSampler
                1186448975: m.bump = t            // BumpSampler
                1619499462: m.spec = t            // SpecSampler
                 181641832: m.diffuse2 = t        // DiffuseSampler2
                4131954791: m.tintPal = t         // TintPaletteSampler
                3576369631: if m.diffuse==null: m.diffuse = t   // TextureSampler_layer0 (terrain)
                default:    if m.diffuse==null: m.diffuse = t   // CW fallback behaviour
        else:
            v = (p.DataType == 1) ? (Vector4)p.Data : ((Vector4[])p.Data)[0]
            switch h:
                4134611841: m.bumpiness    = v.X   // bumpiness
                4095226703: m.specIntensity= v.X   // specularIntensityMult
                2272544384: m.specFalloff  = v.X   // specularFalloffMult
                 666481402: m.specFresnel  = v.X   // specularFresnel
                1592520008: m.emissiveMult = v.X   // emissiveMultiplier
                3913511942: m.hardAlphaBlend = v.X // HardAlphaBlend
                1176544093: /* useTessellation */  = v.X
                 408880252: m.matDiffuseColor = v  // matDiffuseColor
    // resolve texture refs: if (tex as Texture) == null,
    //   look up tex.NameHash in drawable.ShaderGroup.TextureDictionary, then external YTDs.
    return m
```

---

## 10. Gen9 side notes (skip for legacy YDR work)

- ShaderFX gen9 block = 64 bytes: Name+Preset first, then 4 pointers (params / textureRefs / unknownParams / paramInfos), RenderBucket at 0x39, ParameterDataSize u16, RenderBucketMask u32. `FileName` is synthesized: `JenkHash(Name + ".sps")`.
- Params live in cbuffers described by `ShaderParamInfosG9` (`NumBuffers/NumTextures/NumUnknowns/NumSamplers/NumParams` + `ShaderParamInfoG9[]` where a packed uint holds `Type(2b) | index(≤8b) | ParamOffset(12b@8) | ParamLength(12b@20)`; Type: 0=Texture, 1=Unknown, 2=Sampler, 3=CBuffer). CW converts these back into the legacy `Parameters`/`Hashes` arrays on load (padding scalars into Vector4), so downstream code (Renderable.Init etc.) is identical.

## 11. Gotchas

- `Hashes` array type is `MetaName[]` but values are raw uint32 — cast through `(ShaderParamNames)(uint)hash`.
- Scalar params: only `.X` is meaningful; don't trust Y/Z/W.
- `param.Data` can be null for DataType==0 (unresolved texture ref with DataPointer==0) — always null-check.
- `TextureParametersCount` (ShaderFX 0x27) counts DataType==0 params; texture params always come **first** in the params array in practice, but CW never relies on that — it matches by hash.
- Geometry index buffer is ushort (`IndexDataSize = IndexCount * 2`), topology TriangleList except cable.sps.
- "blank" texture NameHash to ignore: `1678728908`.
- CW checks `shader.ParametersList != null` before everything — corrupt/stub shaders exist in the wild.
