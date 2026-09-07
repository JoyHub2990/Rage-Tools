# RAGE Light Parameter Semantics (GTA V) — Flags, TimeFlags, Flashiness, Volumes, Coronas

Source of truth: Sollumz (Blender addon) + bundled GIMS Evo reference scripts.
Files examined (all under `C:/Users/GS/Desktop/MAX_Light_Editor/Sollumz-main/`):

- `ydr/light_flashiness.py` — Flashiness enum 0..20 with labels/descriptions
- `ydr/properties.py` (lines 257-461) — `LightProperties`, `LightTimeFlags`, `LightFlags` (bit order = declaration order)
- `sollumz_properties.py` (lines 345-432) — `FlagPropertyGroup` bit packing, `TimeFlagsMixin` 24 hour bits
- `cwxml/drawable.py` (lines 330-373) — `Light` XML element (CodeWalker YDR XML tag names)
- `cwxml/ymap.py` (lines 91-141) — `LightInstance` (`CLightAttrDef` used by `CExtensionDefLightEffect`)
- `ydr/lights.py` — import/export conversions Blender <-> XML (units, scale factors)
- `ydr/lights_io.py` — same conversions for the newer szio backend
- `versioning/versioning_230.py` (lines 252-291) — old flag names -> new flag names (confirms bit indices)
- `ydr/light_presets.xml` — the presets Sollumz applies to newly created lights
- `ydr/operators/lights.py` — light creation flow (creates light, then applies selected preset; "Default" preset = defaults)
- `GIMS/Shared/Files/I_V_oFLight.ms` — GIMS Evo "GTA V model light" plugin: defaults + flag tooltips
- `GIMS/V/Files/02_TypeLibrary.ms` (lines 4227-4261) — GIMS `strLight` struct defaults

---

## 1. LightAttributes `Flags` — complete 32-bit table

Bit packing (Sollumz `FlagPropertyGroup`): **bit index = order of property declaration**, value = `1 << index`.
Confirmed twice: the `LightFlags` class annotation order in `ydr/properties.py` and the `unk1..unk32 -> name`
migration table in `versioning/versioning_230.py` (old Sollumz called bit N "unk(N+1)").

| Bit | Hex value | Sollumz name (current) | Old Sollumz name | UI label / meaning |
|----:|----------:|------------------------|------------------|--------------------|
| 0 | 0x1 | `interior_only` | unk1 | Interior Only — light only rendered when camera is inside an interior |
| 1 | 0x2 | `exterior_only` | unk2 | Exterior Only — light only rendered when NOT inside an interior |
| 2 | 0x4 | `dont_use_in_cutscene` | unk3 | Don't Use In Cutscene — not rendered in cutscenes |
| 3 | 0x8 | `vehicle` | unk4 | Vehicle — light rendered on vehicles |
| 4 | 0x10 | `ignore_light_state` | unk5 | Ignore Artificial Lights State — ignores `SET_ARTIFICIAL_LIGHTS_STATE(FALSE)` (blackout script) and keeps rendering; a.k.a. "FX" flag |
| 5 | 0x20 | `texture_projection` | unk6 | Texture Projection — enables projected texture (uses `ProjectedTextureHash`) |
| 6 | 0x40 | `cast_shadows` | unk7 | Cast Shadows |
| 7 | 0x80 | `static_shadows` | shadows | Cast Static Shadows (static geometry casts shadow from this light; GIMS tooltip: "Static object will cast shadow") |
| 8 | 0x100 | `dynamic_shadows` | shadowd | Cast Dynamic Shadows (movable objects cast shadow; GIMS: "Mouvable object will cast shadow") |
| 9 | 0x200 | `calc_from_sun` | sunlight | Calculate From Sun — light calculated from sun position (GIMS: "changes color/intensity depending on time of day") |
| 10 | 0x400 | `enable_buzzing` | unk11 | Enable Buzzing — electrical hum sound 1 |
| 11 | 0x800 | `force_buzzing` | electric | Force Buzzing — electrical hum sound 2 (the "electric" flag) |
| 12 | 0x1000 | `draw_volume` | volume | Draw Volume — force-enable volumetric light rendering, ignoring timecycle (GIMS: "Enable Light Volume") |
| 13 | 0x2000 | `no_specular` | specoff | No Specular — light does not produce specular reflections on materials |
| 14 | 0x4000 | `both_int_and_ext` | unk15 | Both Interior And Exterior — rendered inside and outside |
| 15 | 0x8000 | `corona_only` | lightoff | Corona Only — only the corona is rendered, actual lighting disabled (GIMS: "Disables the light") |
| 16 | 0x10000 | `not_in_reflection` | prxoff | Not In Reflection — not rendered in reflections/mirror portals (GIMS: "Deactivates the light in a mirror portal") |
| 17 | 0x20000 | `only_in_reflection` | unk18 | Only In Reflection — only rendered in reflections. (NOTE: GIMS tooltip disagrees and says "Disable corona"; Sollumz naming is the current community consensus) |
| 18 | 0x40000 | `enable_culling_plane` | culling | Enable Culling Plane — activates `CullingPlaneNormal`/`CullingPlaneOffset` (light clipped on one side of the plane) |
| 19 | 0x80000 | `enable_vol_outer_color` | unk20 | Enable Volume Outer Color — activates `VolumeOuterColour`/`VolumeOuterIntensity`/`VolumeOuterExponent` |
| 20 | 0x100000 | `higher_res_shadows` | unk21 | Higher Res Shadows |
| 21 | 0x200000 | `only_low_res_shadows` | unk22 | Only Low Res Shadows |
| 22 | 0x400000 | `far_lod_light` | unk23 | Far LOD Light |
| 23 | 0x800000 | `dont_light_alpha` | glassoff | Don't Light Alpha — does not affect transparent geometry such as glass panes (GIMS: "GlassOff") |
| 24 | 0x1000000 | `cast_shadows_if_possible` | unk25 | Cast Shadows If Possible |
| 25 | 0x2000000 | `cutscene` | unk26 | Cutscene — rendered in cutscenes |
| 26 | 0x4000000 | `moving_light_source` | unk27 | Moving Light Source |
| 27 | 0x8000000 | `use_vehicle_twin` | unk28 | Use Vehicle Twin |
| 28 | 0x10000000 | `force_medium_lod_light` | unk29 | Force Medium LOD Light |
| 29 | 0x20000000 | `corona_only_lod_light` | unk30 | Corona Only LOD Light |
| 30 | 0x40000000 | `delayed_render` | unk31 | Delay Render — create shadow-casting light early in the frame to avoid visible shadow pop-in |
| 31 | 0x80000000 | `already_tested_for_occlusion` | unk32 | Already Tested For Occlusion (runtime flag) |

Sanity checks from bundled presets (`ydr/light_presets.xml`):
- "Spot: Streetlight 1": `Flags = 1408 = 0x580` = static_shadows | dynamic_shadows | enable_buzzing.
- "Spot: Wall Light 1/2": `Flags = 384 = 0x180` = static_shadows | dynamic_shadows.
- "Point: Wall Light 2": `Flags = 2097152 = 0x200000` = only_low_res_shadows.

Note: the Sollumz Culling Plane UI panel (`ydr/ui.py` line 335) toggles bit 18 as the enable checkbox and
greys out normal/offset when it is clear — replicate that behavior in the editor.

---

## 2. TimeFlags — which bit is which hour

`TimeFlagsMixin` in `sollumz_properties.py`: `size = 24`, `flag_names = ["hour1" .. "hour24"]` in declaration order,
so **bit N (value `1 << N`, N = 0..23) = light is ON during game hour N to N+1, 0 = midnight**:

| Bit | Hour window | | Bit | Hour window |
|----:|-------------|-|----:|-------------|
| 0 | 12:00 AM – 1:00 AM | | 12 | 12:00 PM – 1:00 PM |
| 1 | 1:00 AM – 2:00 AM | | 13 | 1:00 PM – 2:00 PM |
| 2 | 2:00 AM – 3:00 AM | | 14 | 2:00 PM – 3:00 PM |
| 3 | 3:00 AM – 4:00 AM | | 15 | 3:00 PM – 4:00 PM |
| 4 | 4:00 AM – 5:00 AM | | 16 | 4:00 PM – 5:00 PM |
| 5 | 5:00 AM – 6:00 AM | | 17 | 5:00 PM – 6:00 PM |
| 6 | 6:00 AM – 7:00 AM | | 18 | 6:00 PM – 7:00 PM |
| 7 | 7:00 AM – 8:00 AM | | 19 | 7:00 PM – 8:00 PM |
| 8 | 8:00 AM – 9:00 AM | | 20 | 8:00 PM – 9:00 PM |
| 9 | 9:00 AM – 10:00 AM | | 21 | 9:00 PM – 10:00 PM |
| 10 | 10:00 AM – 11:00 AM | | 22 | 10:00 PM – 11:00 PM |
| 11 | 11:00 AM – 12:00 PM | | 23 | 11:00 PM – 12:00 AM |

- Bits 24..31 are unused by Sollumz (GIMS shows 32 checkboxes but the game only uses 24).
- `TimeFlags = 0` conventionally means "always on" (Sollumz Default preset uses 0; the "always on" preset value
  seen in the wild is also `16777215 = 0xFFFFFF`).
- Real-world examples from presets: `15728767 = 0xF0007F` = bits 0-6 + 20-23 = ON 8PM→7AM;
  `15728703 = 0xF0003F` = bits 0-5 + 20-23 = ON 8PM→6AM. Classic streetlight schedules.
- Sollumz "select range" helper (`sollumz_operators.py` `SelectTimeFlagsRange`): given `start`,`end` hours
  (0..23): if `start < end` set bits `[start, end)`; if `start > end` set bits `[start,24) ∪ [0,end)` (wraps
  midnight); if `start == end == 0` set all 24 bits. Useful UX to copy.

---

## 3. Flashiness enum (0..20)

Verbatim from `ydr/light_flashiness.py`:

```python
class Flashiness(IntEnum):
    CONSTANT = 0                 # "Constant lighting without flashing"
    RANDOM = 1                   # "Light flashes randomly"
    RANDOM_OVERRIDE_IF_WET = 2   # "Randomly flash if the light is wet"
    ONCE_PER_SECOND = 3          # "Flash once per second"
    TWICE_PER_SECOND = 4         # "Flash twice per second"
    FIVE_PER_SECOND = 5          # "Flash five times per second"
    RANDOM_FLASHINESS = 6        # "Flashes Randomly"
    OFF = 7                      # "Turns Light Off"
    UNUSED = 8                   # "Unused"
    ALARM = 9                    # "Flash like an alarm siren"
    ON_WHEN_RAINING = 10         # "Flash only when raining" (on during rain)
    CYCLE_1 = 11                 # "Cycle 1"  \
    CYCLE_2 = 12                 # "Cycle 2"   } phased cycling groups (e.g. traffic-light style)
    CYCLE_3 = 13                 # "Cycle 3"  /
    DISCO = 14                   # "Flash like a disco light"
    CANDLE = 15                  # "Flash like a candle flame"
    PLANE = 16                   # "Flash like a plane landing strip(?)"
    FIRE = 17                    # "Flash like a flame"
    THRESHOLD = 18               # "Threshold"
    ELECTRIC = 19                # "Electric"
    STROBE = 20                  # "Flash like a strobe light"
```

Stored in the YDR XML as integer `<Flashiness value="..."/>`; GIMS treats it as 0..255 int, default 0.

---

## 4. Parameter meanings, units, and conversion formulas

XML tag names below are CodeWalker YDR XML (from `cwxml/drawable.py` `Light`); the `CLightAttrDef`
(ymap/ytyp light-effect extension) equivalents are given in section 6.

### Core
| Field | XML tag | Type/units | Meaning |
|-------|---------|------------|---------|
| Position | `Position` | vec3, meters, model space (or bone space when `BoneId != 0`) | light origin |
| Colour | `Colour` | RGB bytes 0..255 | light color |
| Intensity | `Intensity` | float, game units | brightness. Sollumz Blender mapping: `blender_energy = intensity * 500` (`INTENSITY_SCALE_FACTOR = 500`); GIMS 3ds Max multiplier = `Intensity / 25`. Typical values 1..~50 |
| Flags | `Flags` | uint32 bitfield | see section 1 |
| BoneId | `BoneId` | uint16 bone tag | attaches light to skeleton bone (0 = none) |
| Type | `Type` | string in YDR XML: `"Point"` / `"Spot"` / `"Capsule"` | binary/`CLightAttrDef` numeric type: **Point = 1, Spot = 2, Capsule = 4** (bit flags; conversion in `ydr/lights.py` lines 274-279 & 325-330). Blender-internal enum in Sollumz is 1/2/3 — do NOT confuse with the file format value |
| GroupId | `GroupId` | int | light group; Sollumz UI comments it out: "this property is unused" (`ydr/ui.py` line 309). Keep round-trip |
| TimeFlags | `TimeFlags` | uint (24 bits used) | see section 2 |
| Falloff | `Falloff` | float, meters | light radius / max attenuation distance (Blender `cutoff_distance`). For a capsule GIMS renders reach as `Extents/2 + Falloff` |
| FalloffExponent | `FalloffExponent` | float, unitless exponent | distance-attenuation curve exponent; higher = light hugs the source. Typical 8..64 (GIMS default 45, GIMS V struct default 32). Attenuation ≈ `saturate(1 - (d/falloff))^exponent` family; CodeWalker shader uses `pow(1 - saturate(d/falloff), falloffExponent)`. Sollumz’s Blender viz mapping is arbitrary: `shadow_soft_size = falloff_exponent / 5` |
| Extent | `Extent` | vec3, meters | **Capsule only: `Extent.X` = capsule length** (distance between the two hemisphere centers along the light's direction axis). Sollumz UI exposes only index 0 (`ydr/ui.py` line 303: `box.prop(light.light_properties, "extent", index=0)`); Y/Z stay 1.0. GIMS uses a single float `TheExtents` |
| ProjectedTextureHash | `ProjectedTextureHash` | string (`"hash_XXXXXXXX"` or texture name) | texture projected by spot lights when flag bit 5 set; stored as joaat uint32 (`projectedTextureKey`) in `CLightAttrDef` — Sollumz converts `f"hash_{key:08X}"` <-> `jenkhash.name_to_hash(str)` |
| LightHash | `LightHash` | int 0..255 | "Light ID": links the light in the drawable with the matching light in a `CExtensionDefLightEffect` entity extension (per-entity overrides). Not a joaat hash — just a small id |

### Spot cone
| Field | XML tag | Units | Meaning |
|-------|---------|-------|---------|
| ConeInnerAngle | `ConeInnerAngle` | **degrees, half-angle** | full-intensity inner cone. Blender mapping: `inner = degrees(abs(spot_blend*pi - pi))`, i.e. `spot_blend = 1 - inner/outer_full` style; GIMS: `HotSpot = ConeInnerAngle * 2` |
| ConeOuterAngle | `ConeOuterAngle` | **degrees, half-angle** | cone edge. Blender mapping: `outer = degrees(spot_size)/2` (spot_size is the FULL cone angle in radians). GIMS: `Falloff(cone) = ConeOuterAngle * 2`. GIMS spinner range [0.01, 179] |

Both are 0 for non-spot lights on export (`lights_io.py` lines 203-204).

### Culling plane
| Field | XML tag | Units | Meaning |
|-------|---------|-------|---------|
| CullingPlaneNormal | `CullingPlaneNormal` | vec3, unit normal | plane that clips the light's influence; active only when flag bit 18 (0x40000) set |
| CullingPlaneOffset | `CullingPlaneOffset` | float, meters (plane d) | plane distance term. `CLightAttrDef` stores the plane as one vec4 `cullingPlane = [nx, ny, nz, offset]` |

GIMS default plane: `#(0, 0, 1, 10)`.

### Volume (volumetric cone/sphere rendering)
| Field | XML tag | Units | Meaning |
|-------|---------|-------|---------|
| VolumeIntensity | `VolumeIntensity` | float (0..~10) | intensity of the volumetric fog cone; volume drawn when flag bit 12 (`draw_volume`) set (or timecycle enables it). Blender `volume_factor` |
| VolumeSizeScale | `VolumeSizeScale` | float scale (0..~10) | scales the size/length of the rendered volume relative to falloff |
| VolumeOuterColour | `VolumeOuterColour` | RGB bytes 0..255 | color at the outer edge of the volume; requires flag bit 19 (`enable_vol_outer_color`) |
| VolumeOuterIntensity | `VolumeOuterIntensity` | float 0..1 | intensity at the outer edge of the volume |
| VolumeOuterExponent | `VolumeOuterExponent` | float (1..512) | falloff exponent for inner->outer volume gradient |
| VolumetricFadeDistance | `VolumetricFadeDistance` | int meters, 0..255 (byte) | camera distance at which the volume stops rendering; 0 = engine default |

### Corona (sprite drawn at light position)
| Field | XML tag | Units | Meaning |
|-------|---------|-------|---------|
| CoronaSize | `CoronaSize` | float (0..~100) | corona billboard size |
| CoronaIntensity | `CoronaIntensity` | float (0..~10) | corona HDR brightness |
| CoronaZBias | `CoronaZBias` | float 0..1 (typ. 0.1) | depth bias pulling the corona toward the camera so nearby geometry doesn't clip it |

Corona-only rendering: flag bit 15; corona-only LOD light: bit 29.

### Fades / shadows
| Field | XML tag | Units | Meaning |
|-------|---------|-------|---------|
| LightFadeDistance | `LightFadeDistance` | int meters, byte 0..255; 0 = never/engine default | camera distance where the light fades out |
| ShadowFadeDistance | `ShadowFadeDistance` | int meters, byte 0..255 | distance where its shadows fade out |
| SpecularFadeDistance | `SpecularFadeDistance` | int meters, byte 0..255 | distance where specular contribution fades |
| ShadowNearClip | `ShadowNearClip` | float meters (default 0.01) | near clip plane of the shadow camera |
| ShadowBlur | `ShadowBlur` | **byte 0..255 in file**; Sollumz UI shows 0..1 factor (`xml = round(ui * 255)`) | shadow blur amount |

### Direction frame
- `Direction`: unit vec3 — spot/capsule axis. **Sollumz negates on import and export** (Blender lights point down
  -Z; RAGE stores the direction the light points). Export: `direction = -normalize(matrix.col[2].xyz)`.
- `Tangent`: unit vec3 orthogonal to direction (`matrix.col[0]`); used to build the light basis
  (bitangent = direction × tangent). GIMS defaults: Direction `[0,0,-1]`, Tangent `[-1,0,0]`.

---

## 5. Defaults when creating a new light

Sollumz flow (`ydr/operators/lights.py` `SOLLUMZ_OT_create_light`): a Blender light of the chosen sollum type
(POINT / SPOT / CAPSULE — capsule uses a Blender SPOT lamp for viz) is created, then the currently selected
preset (default: **"Default"**) is applied. So effective new-light values, expressed in RAGE terms:

| RAGE field | Value | Derivation |
|------------|-------|-----------|
| Intensity | **0.02** | Default preset Energy=10, `10 / 500` |
| Colour | 255,255,255 | Color 1,1,1 |
| Falloff | **40** | CutoffDistance=40 |
| FalloffExponent | **0** | ShadowSoftSize=0 × 5 |
| TimeFlags | 0 | (always on) |
| Flags | 0 | |
| Flashiness | CONSTANT (0) | |
| VolumeIntensity | 1 | VolumeFactor=1 |
| VolumeSizeScale | 1 | |
| VolumeOuterColour | 255,255,255 | |
| VolumeOuterIntensity | 1 | |
| VolumeOuterExponent | 1 | |
| Light/Shadow/Specular/Volumetric FadeDistance | 0 | |
| CullingPlaneNormal / Offset | (0,0,0) / 0 | flag bit 18 clear |
| CoronaSize | 0 | |
| CoronaIntensity | 1 | |
| CoronaZBias | **0.1** | |
| ShadowBlur | 0 | |
| ShadowNearClip | **0.05** | ShadowBufferClipStart=0.05 |
| Extent | (1,1,1) | capsule length 1 |
| ConeInner/OuterAngle (spot) | 0 / 0 | SpotSize=0, SpotBlend=0 in Default preset (degenerate — user must widen) |
| GroupId, LightHash, BoneId | 0 | |
| ProjectedTextureHash | "" | |

`LightProperties` PropertyGroup fallback defaults (used if no preset applied): volume_size_scale=1,
volume_outer_color=(1,1,1), volume_outer_intensity=1, volume_outer_exponent=1, corona_intensity=1,
corona_z_bias=0.1, extent=(1,1,1), everything else 0.

GIMS Evo defaults (alternative reference, `I_V_oFLight.ms` params): Type=Dot(point), Intensity=1, Falloff=10,
FalloffExponent=45, CoronaSize=3, CoronaIntensity=2, CoronaZBias=0.1, ConeInnerAngle=10, ConeOuterAngle=25,
TheExtents=1, ShadowNearClip=0.01, CPDistance=10, all volume params 1, fade distances 0.
GIMS `strLight` struct defaults (`GIMS/V/Files/02_TypeLibrary.ms`): Falloff=3.5, FalloffExponent=32,
CoronaSize=1, CoronaIntensity=1, LightType=1, CullingPlane=(0,0,1,10), ShadowNearClip=0.01.

Sensible "realistic" starting values can also be copied from the bundled presets, e.g.
"Spot: Streetlight 1": Intensity 32 (`16000/500`), Falloff 18, FalloffExponent 96 (`19.2*5`), TimeFlags 0xF0003F,
Flags 0x580, CoronaSize 3, CoronaIntensity 0.1, ShadowFade/SpecularFade 40, cone outer 70°
(`degrees(2.443461)/2`), cone inner ≈ 10° (`spot_blend 0.9444`).

---

## 6. CLightAttrDef (ymap/ytyp light instance) field map

`cwxml/ymap.py` `LightInstance` — same data, different tag names, used inside
`CExtensionDefLightEffect > instances (itemType="CLightAttrDef")`. Order as declared:

`posn`(vec3 as 3 floats) `colour`(3 bytes) `flashiness` `intensity` `flags` `boneTag` `lightType`(1/2/4)
`groupId` `timeFlags` `falloff` `falloffExponent` `cullingPlane`(4 floats: normal.xyz, offset) `shadowBlur`
`padding1` `padding2` `padding3` `volIntensity` `volSizeScale` `volOuterColour`(3 bytes) `lightHash`
`volOuterIntensity` `coronaSize` `volOuterExponent` `lightFadeDistance` `shadowFadeDistance`
`specularFadeDistance` `volumetricFadeDistance` `shadowNearClip` `coronaIntensity` `coronaZBias`
`direction`(3f) `tangent`(3f) `coneInnerAngle` `coneOuterAngle` `extents`(3f) `projectedTextureKey`(uint32 joaat)

This ordering mirrors the in-memory `CLightAttr` binary layout (note the three padding bytes after the
`shadowBlur` byte — shadowBlur/fade distances are bytes in the 160-byte binary struct).

---

## 7. Flag-packing code (for exact port behavior)

`tools/utils.py`:

```python
def flag_list_to_int(flag_list):        # bit i = list[i]
    flags = 0
    for i, enabled in enumerate(flag_list):
        if enabled == True:
            flags += (1 << i)
    return flags

def int_to_bool_list(num, size=None):
    return [bool(num & (1 << n)) for n in range(size or 32)]
```

`FlagPropertyGroup.size = 32` for light flags, `TimeFlagsMixin.size = 24` for time flags.
Flag names come from `__annotations__` declaration order — the table in section 1 is that order.

---

## 8. Gotchas for the port

1. **Type enum mismatch**: YDR XML uses strings; binary + CLightAttrDef use 1/2/4 (Point/Spot/Capsule).
   Sollumz's internal Blender enum (1/2/3) is irrelevant to the file format.
2. **Cone angles are half-angles in degrees** (max meaningful ~90; GIMS allows up to 179).
3. **Direction is negated** between Blender convention and file data; keep RAGE convention in the editor:
   `Direction` points from the light toward the lit area.
4. **Capsule** uses only `Extent.X` (length); render as sphere swept along ±direction·(X/2). Falloff acts as
   the radius around the capsule segment.
5. **Byte-valued fields** in the binary struct: ShadowBlur, LightFadeDistance, ShadowFadeDistance,
   SpecularFadeDistance, VolumetricFadeDistance, LightHash, GroupId — clamp to 0..255 on export.
6. **TimeFlags=0** must be treated as always-on when previewing, otherwise imported props with 0 go dark.
7. Bit 18 (0x40000) gates the culling plane; bit 19 gates the volume outer color set; bit 12 gates volume
   drawing; bit 5 gates texture projection — grey out dependent fields when the gate bit is clear.
8. GIMS vs Sollumz disagree on bit 17 ("only in reflection" vs "disable corona") — expose the Sollumz name but
   consider a tooltip noting the ambiguity.
