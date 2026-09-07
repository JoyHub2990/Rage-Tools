# YFT Fragment structure — renderable drawables + lights (CodeWalker research notes)

Sources (CodeWalker-master):
- `CodeWalker.Core/GameFiles/FileTypes/YftFile.cs` (all 154 lines)
- `CodeWalker.Core/GameFiles/Resources/Frag.cs` (FragType L41-788, FragDrawable L790-1077, FragBoneTransforms L1079-1173, FragPhysicsLODGroup L2064-2175, FragPhysicsLOD L2177-2827, FragPhysTransforms L3316-3389, FragPhysArchetype L3391+, FragPhysTypeChild L3666-3951, FragPhysTypeGroup L4016+, FragPhysNameStruct_s L4404)
- `CodeWalker/Rendering/Renderable.cs` (Renderable.Init L100-336, InitLights L338-349, RenderableLight L1367-1416)
- `CodeWalker/Rendering/Renderer.cs` (RenderFragment L2803-3074, RenderRenderable lights L3308-3345)
- `CodeWalker/Rendering/Shaders/DeferredScene.cs` (RenderLights L511-608 — bone transform applied to light)
- `CodeWalker.Core/GameFiles/Resources/ResourceBuilder.cs` (Build L532-637)
- `CodeWalker.Core/GameFiles/RpfFile.cs` (LoadResourceFile L681-747)

---

## 1. YftFile load/save API

```csharp
public class YftFile : GameFile, PackedFile
{
    public FragType Fragment { get; set; }   // the ONLY content property

    public void Load(byte[] data);                      // raw compressed .yft (RSC7 header)
    public void Load(byte[] data, RpfFileEntry entry);  // decompressed body + resource entry
    public byte[] Save();                                // returns RSC7-headered deflate-compressed bytes
    public int GetVersion(bool gen9) => gen9 ? 171 : 162;
}
```

### RSC7 version numbers
- **YFT legacy (gen8/PC classic): version 162**
- **YFT gen9 (enhanced/PC next-gen): version 171**
- (For reference, YDR uses 165, gen9 = 159+... — not this file's topic.)
- RSC7 magic = `0x37435352` ("RSC7" little-endian) at file offset 0.

Raw .yft file layout: `[0]=0x37435352, [4]=version(int32), [8]=SystemFlags(uint), [12]=GraphicsFlags(uint), [16..]=deflate-compressed payload`. Version stored in header wins over the value passed to `LoadResourceFile`. Top 4 bits of SystemFlags = `(version>>4)&0xF`, top 4 bits of GraphicsFlags = `version&0xF` (see ResourceBuilder.Build L607-611).

### Load path (`Load(byte[] data, RpfFileEntry entry)`, YftFile.cs L32-77)

```csharp
ResourceDataReader rd = new ResourceDataReader(resentry, data);
if (rd.IsGen9)
{
    switch (resentry.Version)
    {
        case 171: break;                 // real gen9 file
        case 162: rd.IsGen9 = false; break; // legacy file inside gen9 install — read as legacy
    }
}
Fragment = rd.ReadBlock<FragType>();
if (Fragment != null)
{
    Fragment.Yft = this;
    if (Fragment.Drawable != null) Fragment.Drawable.Owner = this;
    if (Fragment.DrawableCloth != null) Fragment.DrawableCloth.Owner = this;
}
```

`Load(byte[] data)` (raw file) simply calls `RpfFile.LoadResourceFile(this, data, (uint)GetVersion(RpfManager.IsGen9))` which parses the RSC7 header, sets SystemFlags/GraphicsFlags on the entry, strips the 16-byte header, `ResourceBuilder.Decompress`es, and calls the other Load.

### Save path (YftFile.cs L79-90)

```csharp
public byte[] Save()
{
    var gen9 = RpfManager.IsGen9;      // global static flag
    if (gen9) Fragment?.EnsureGen9();  // converts all drawables' VertexDeclarations etc.
    byte[] data = ResourceBuilder.Build(Fragment, GetVersion(gen9), true, gen9); // compress=true
    return data;
}
```

`ResourceBuilder.Build` walks `GetReferences()`/`GetParts()` of every block, assigns file positions in system segment (base 0x50000000) and graphics segment (0x60000000), writes each block (asserting written length == `BlockLength` / `BlockLength_Gen9`), computes page flags, deflate-compresses, prepends the 16-byte RSC7 header. Block instances referenced multiple times (e.g. children sharing a skeleton block) are written **once** — dedup by block identity.

---

## 2. FragType structure (Frag.cs L41-788)

`FragType : ResourceFileBase`, `BlockLength = 304` (0x130). Field offsets (after ResourceFileBase: 0x00 VFT+Unknown_04h, 0x08 FilePagesInfoPointer):

| Offset | Field |
|--------|-------|
| 0x10, 0x18 | Unknown (0) |
| 0x20 | `BoundingSphereCenter` (Vector3) |
| 0x2C | `BoundingSphereRadius` (float) |
| 0x30 | `DrawablePointer` → **`FragDrawable Drawable`** (main drawable) |
| 0x38 | `DrawableArrayPointer` → `ResourcePointerArray64<FragDrawable> DrawableArray` (extra drawables, e.g. vehicle mods) |
| 0x40 | `DrawableArrayNamesPointer` → `ResourcePointerArray64<string_r> DrawableArrayNames` |
| 0x48 | `DrawableArrayCount` (uint) |
| 0x4C | `DrawableArrayFlag` (int: 0 when ArrayCount>0, else -1) |
| 0x50 | Unknown (0) |
| 0x58 | `NamePointer` → `string Name` |
| 0x60 | `Cloths` — inline `ResourcePointerList64<EnvironmentCloth>` (16 bytes: ptr, ushort count, ushort capacity, pad) |
| 0x70–0xA7 | Unknowns (0) |
| 0xA8 | `BoneTransformsPointer` → **`FragBoneTransforms BoneTransforms`** (default/rest pose!) |
| 0xB0 | `Unknown_B0h` int (0, 944, 1088, 1200) |
| 0xB4 | 0 |
| 0xB8 | `Unknown_B8h` int (multiple of 16) |
| 0xBC | `Unknown_BCh` int (0 or -1) |
| 0xC0 | `Unknown_C0h` int (0/256/512/768/1024/65280) |
| 0xC4 | `Unknown_C4h` int (1/3/65/67) |
| 0xC8 | `Unknown_C8h` int = -1 always |
| 0xCC | `Unknown_CCh` float |
| 0xD0 | `GravityFactor` float |
| 0xD4 | `BuoyancyFactor` float |
| 0xD8 | `Unknown_D8h` byte (0) |
| 0xD9 | `GlassWindowsCount` byte |
| 0xDA/0xDC | 0 |
| 0xE0 | `GlassWindowsPointer` → `ResourcePointerArray64<FragGlassWindow> GlassWindows` |
| 0xE8 | 0 |
| 0xF0 | `PhysicsLODGroupPointer` → **`FragPhysicsLODGroup PhysicsLODGroup`** |
| 0xF8 | `DrawableClothPointer` → **`FragDrawable DrawableCloth`** |
| 0x100, 0x108 | 0 |
| **0x110** | **`LightAttributes` — inline `ResourceSimpleList64<LightAttributes>`** (16 bytes: ulong EntriesPointer, ushort EntriesCount, ushort EntriesCapacity, 4 pad) |
| 0x120 | `VehicleGlassWindowsPointer` → `FragVehicleGlassWindows` |
| 0x128 | 0 |

`GetParts()` confirms the two inline parts: `(0x60, Cloths)` and `(0x110, LightAttributes)` (Frag.cs L781-787).

On binary read, ownership is wired: `Drawable.OwnerFragment = this`, `DrawableCloth.OwnerFragment = this`, every `DrawableArray` item `.OwnerFragment = this`; then `AssignChildrenShaders()` and `AssignGlassWindowsGroups()` run (L173-201).

`FileVFT` when creating from XML: FragType = **1079456040**; FragDrawable = **1080060872**.
Other VFT defaults: FragPhysicsLODGroup=1080055472, FragPhysicsLOD=1080055512, FragPhysTransforms=1080043536, FragPhysArchetype=1080215944, FragPhysTypeChild=1080061712.

### FragDrawable (Frag.cs L790-1077)

`FragDrawable : DrawableBase` (NOT `Drawable` — has **no own LightAttributes property**). BlockLength = 336 (0xA8 DrawableBase + frag extension). Extension fields (from 0xA8):

- 0xA8: Unknown (0)
- 0xB0: **`Matrix4F_s FragMatrix`** (48 bytes; 3x4 column matrix — Column1/2/3/4 Vector3 each followed by uint flag `0x7f800001`)
- 0xE0: `BoundPointer` → `Bounds Bound`
- 0xE8: `FragMatricesIndsPointer` → `ulong[] FragMatricesInds` (count = `FragMatricesIndsCount` ushort @0xF0)
- 0xF2: `FragMatricesCapacity` ushort — array read with **Capacity**, not Count
- 0xF8: `FragMatricesPointer` → `Matrix4F_s[] FragMatrices`; 0x100: `FragMatricesCount` ushort; 0x102: ushort = 1
- 0x130: `NamePointer` → `string Name`

Runtime-only helpers (not serialized): `OwnerFragment` (FragType), `OwnerCloth`, `OwnerFragmentPhys` (FragPhysTypeChild), `OwnerDrawable` (parent FragDrawable when inheriting shaders/skeleton/bounds).

### FragBoneTransforms (Frag.cs L1079-1173) — the rest pose

BlockLength = `32 + Items.Length*48`. Header: 16 zero bytes, `ItemCount1` byte, `ItemCount2` byte (equal), `Unknown_12h` ushort (0/1), pad, 8 zero bytes; then `Matrix3_s[] Items` — one **3x4 matrix (3 Vector4 rows)** per skeleton bone index. These are **absolute object-space bone transforms** (no parent-chain multiplication needed). Translation is in the `.W` components of the three rows.

### FragPhysicsLODGroup (L2064) / FragPhysicsLOD (L2177)

```
FragPhysicsLODGroup (48 bytes): VFT, 1, 0, PhysicsLOD1Pointer, PhysicsLOD2Pointer, PhysicsLOD3Pointer, 0
```

`FragPhysicsLOD` (304 bytes) key members:
- `Vector3 PositionOffset` (@0x30) — **added to child frag transforms when rendering**
- `GroupNamesPointer`→`FragPhysGroupNamesBlock GroupNames`; `GroupsPointer`→`ResourcePointerArray64<FragPhysTypeGroup> Groups` (count=`GroupsCount` byte)
- `ChildrenPointer` → **`ResourcePointerArray64<FragPhysTypeChild> Children`** (count = `ChildrenCount` byte, `ChildrenCount2` mirrors it)
- `Archetype1Pointer`/`Archetype2Pointer` → `FragPhysArchetype` (Name, Mass/MassInv, InertiaTensor(+Inv), Bound)
- `BoundPointer` → `Bounds Bound` (the composite bound; `lod.Bound == lod.Archetype1.Bound` in vanilla)
- `ChildrenInertiaTensorsPointer`/`ChildrenUnkVecsPointer`/`ChildrenUnkFloatsPointer` → per-child Vector4/float arrays (count = ChildrenCount)
- **`FragTransformsPointer` → `FragPhysTransforms FragTransforms`** — the per-child rest matrices
- `UnknownData1/2` byte arrays, `RootGroupsCount` byte, damping vectors, NaN-flag uints `0x7f800001`/`0x7fc00001`

On read (L2326-2345) each child gets: `OwnerFragPhysLod = this`, `OwnerFragPhysIndex = i`, `UnkFloatFromParent/UnkVecFromParent/InertiaTensorFromParent` from the parallel arrays, and `child.Group = Groups[child.GroupIndex]`.

### FragPhysTransforms (L3316-3389)

BlockLength = `32 + Matrices.Length*64`. Header: VFT, 1, 0, `MatricesCount` uint, 0, 0; then full 4x4 `Matrix[] Matrices` inline — **one matrix per physics child, indexed by child index** (`OwnerFragPhysIndex`). These matrices are the child's rest transform in object space (rotation part + translation in Row4; add `PositionOffset`).

### FragPhysTypeChild (L3666-3951)

BlockLength = 256. Layout: VFT, 1, `PristineMass` float, `DamagedMass` float, **`GroupIndex` ushort (@0x10), `BoneTag` ushort (@0x12)**, then zeros until:
- 0xA0: `Drawable1Pointer` → **`FragDrawable Drawable1`** (pristine/undamaged mesh)
- 0xA8: `Drawable2Pointer` → **`FragDrawable Drawable2`** (damaged mesh)
- 0xB0: `EvtSetPointer` → `FragPhysEvtSet EvtSet` (empty block)
- rest zeros.

`BoneTag` is the skeleton bone tag this child is attached to (e.g. wheel bone tags below).

### FragPhysTypeGroup (L4016)

176 bytes; physics tuning floats (Strength, ForceTransmissionScaleUp/Down, JointStiffness, soft angles, RotationSpeed/Strength, RestoringStrength/MaxTorque, LatchStrength, Mass, MinDamageForce, DamageHealth …) plus bytes: `ChildGroupIndex`, `ParentIndex`, `ChildIndex` (first BoundComposite/fragment child), `ChildCount`, `ChildGroupCount`, `UnkByte51=255`, `GlassWindowIndex`, `GlassFlags` (bit 2 = has glass window). `Name` is an inline 40-char `FragPhysNameStruct_s` (10 uints).

### Shader/skeleton inheritance for children — `AssignChildrenShaders` (L572-639)

Child drawables (wheels etc.) often have an empty ShaderGroup; CW fixes this after load:

```csharp
var pdrwbl = Drawable ?? DrawableCloth;
void assigndr(FragDrawable dr, BoundComposite pbcmp, int i)
{
    dr.OwnerDrawable = pdrwbl;              // also signals XML export to skip skeleton/bounds
    dr.AssignGeometryShaders(pdrwbl.ShaderGroup);
}
// applied to Drawable1+Drawable2 of every child of PhysicsLOD1/2/3, plus DrawableArray items
```

In vanilla binary files the child's `Skeleton` block pointer equals the main drawable's skeleton block (CW's commented test `if (dr.Skeleton != pskel) //no hit`). `AssignChildrenSkeletonsAndBounds` (XML import only) sets `dr.Skeleton = pskel` and `dr.Bound = compositeBound.Children[i]`.

---

## 3. Every drawable worth rendering + its rest-pose transform

CodeWalker's authoritative enumeration, `Renderer.RenderFragment(arch, ent, FragType f, txdhash, animClip)` (Renderer.cs L2803-3074):

1. **`f.Drawable`** — always rendered (main drawable, carries skeleton, shaders, and the lights).
2. **`f.DrawableCloth`** — rendered if non-null (environment cloth drawable).
3. **Physics children of LOD1 only** — `f.PhysicsLODGroup.PhysicsLOD1.Children.data_items[i]`:
   - `child.Drawable1` rendered if `AllModels.Length != 0`, EXCEPT wheels (see below).
   - `child.Drawable2` (damaged) rendered if `AllModels.Length != 0` — CW renders both; a viewer may prefer to skip Drawable2.
   - LOD2/LOD3 children are NOT rendered.
4. **`f.DrawableArray`** items — only rendered when the fragment is selected in CW (they are mod parts / extras; usually skip).
5. Glass windows — debug outline only (no meshes) via `FragGlassWindow` projection rows; optional.

### Vehicle wheel instancing hack (L2820-2942)

Only ONE wheel mesh pair exists (front + rear); other wheel children have empty models and reuse it:

```csharp
switch (pch.BoneTag)
{
    case 27922: /*wheel_lf*/ case 26418: /*wheel_rf*/ wheel_f = pch.Drawable1; break;
    case 29921: case 29922: case 29923: /*wheel_lm1-3*/
    case 27902: /*wheel_lr*/
    case 5857: case 5858: case 5859: /*wheel_rm1-3*/
    case 26398: /*wheel_rr*/ wheel_r = pch.Drawable1; break;
    default: RenderDrawable(pch.Drawable1, ...); break;
}
// second pass: for each wheel child whose own Drawable1.AllModels.Length == 0:
dwbl.Owner = dwblcopy; dwbl.AllModels = dwblcopy.AllModels;  // front falls back to rear and vice versa
RenderDrawable(dwbl, ...);
```

### Rest-pose transform per model — `Renderable.Init(DrawableBase)` (Renderable.cs L100-336)

For each drawable a `Renderable` is built. Transform resolution logic:

```csharp
var fd = drawable as FragDrawable;
Skeleton skeleton = drawable.Skeleton;
Matrix[] modeltransforms = skeleton.Transformations;   // parent-RELATIVE bind matrices
bool usepose = false;
if (fd != null)
{
    var pose = fd.OwnerFragment?.BoneTransforms;       // FragBoneTransforms = default pose
    if (pose?.Items != null)
    {
        // Matrix3_s (3 rows of Vector4) -> 4x4, TRANSPOSED, translation from row .W comps:
        var p = pose.Items[i];
        Vector4 r1 = p.Row1; Vector4 r2 = p.Row2; Vector4 r3 = p.Row3;
        modeltransforms[i] = new Matrix(r1.X, r2.X, r3.X, 0, r1.Y, r2.Y, r3.Y, 0,
                                        r1.Z, r2.Z, r3.Z, 0, r1.W, r2.W, r3.W, 1);
        usepose = true;                                // these are ABSOLUTE transforms
    }
    var phys = fd.OwnerFragmentPhys;                   // set => this is a physics child drawable
    if (phys?.OwnerFragPhysLod != null)
    {
        fragtransforms  = phys.OwnerFragPhysLod.FragTransforms?.Matrices;
        fragtransformid = phys.OwnerFragPhysIndex;     // child index in LOD
        fragoffset      = new Vector4(phys.OwnerFragPhysLod.PositionOffset, 0);
        // right-side wheel flip: BoneTag 26418/5857/5858/5859/26398 =>
        // fragtransforms[id] rotation part := diag(-1, 1, -1)
    }
}
// per model (model.BoneIndex = DrawableModel bone index):
Matrix trans = (boneidx < modeltransforms.Length) ? modeltransforms[boneidx] : Matrix.Identity;
if (fragtransforms != null) {                          // physics child: ONE matrix for whole drawable
    trans = fragtransforms[fragtransformid];
    trans.Row4 += fragoffset;                          // add lod.PositionOffset
}
else if (!usepose) {                                   // plain skeleton: multiply up parent chain
    trans.Column4 = Vector4.UnitW;
    for (parentind = skeleton.ParentIndices[boneidx]; parentind >= 0; ...)
        trans = trans * parentMatrixWithColumn4UnitW;
}
model.Transform = model.IsSkinMesh ? Matrix.Identity : trans;
```

Summary of rest-pose transforms:
- **Main drawable models**: `FragType.BoneTransforms.Items[model.BoneIndex]` converted as above (absolute). Fallback: skeleton parent-chain product of `Skeleton.Transformations`. Skinned models: identity (bones applied in shader).
- **Physics child drawable (whole drawable)**: `PhysicsLOD1.FragTransforms.Matrices[childIndex]` with `Row4.xyz += PhysicsLOD1.PositionOffset`. (`FragDrawable.FragMatrix` itself is NOT used by CW's renderer.)
- **DrawableCloth**: treated like a main drawable (no OwnerFragmentPhys).
- Bone-tag→model map: for HD models with a valid bone, `ModelBoneLinks[bone.Tag] = model` (used for anim bone updates).

---

## 4. Frag lights: storage + rendering

### Storage

Lights live ONLY on the **FragType** itself: `FragType.LightAttributes` (`ResourceSimpleList64<LightAttributes>`, inline at struct offset **0x110**). `FragDrawable` has no light list (it's a `DrawableBase`, and only `Drawable` — the YDR class — has its own `LightAttributes`).

`LightAttributes` (Drawable.cs L5676, BlockLength = 168) — key fields: `Position` (Vector3 @0x08), `ColorR/G/B`+`Flashiness` bytes, `Intensity` float, `Flags` uint, **`BoneId` ushort (@0x20)**, `Type` (LightType byte enum: Point=1, Spot=2, Capsule=4), `GroupId`, `TimeFlags`, `Falloff`, `FalloffExponent`, `CullingPlaneNormal/Offset`, volume/corona params, `Direction`, `Tangent`, `ConeInnerAngle`, `ConeOuterAngle`, `Extent`, `ProjectedTextureHash`.

### Attaching lights to the renderable (Renderable.Init L323-331; same logic re-checked each frame in Renderer.RenderRenderable L3315-3325)

```csharp
var lights = dd?.LightAttributes?.data_items;           // dd = drawable as Drawable (YDR case)
if ((lights == null) && (fd != null) && (fd?.OwnerFragment?.Drawable == fd))
{
    lights = fd.OwnerFragment.LightAttributes?.data_items;   // YFT case
}
if (lights != null) InitLights(lights);
```

Critical detail: `fd.OwnerFragment.Drawable == fd` — frag lights are attached **only to the renderable of the MAIN drawable**, so they render once, never per physics child. Renderer also re-inits when `lights.Length != rndbl.Lights.Length` (live add/remove in editor) and per-light when `light.UpdateRenderable` is set.

### BoneId → bone lookup (RenderableLight.Init, Renderable.cs L1390-1415)

```csharp
var bones = Owner?.Skeleton?.BonesMap;       // Owner = the Renderable of the MAIN drawable
bones?.TryGetValue(l.BoneId, out Bone);      // => the MAIN drawable's skeleton is used
Colour = new Vector3(l.ColorR, l.ColorG, l.ColorB) * (2.0f * l.Intensity / 255.0f);
ConeInnerAngle = Math.Min(l.ConeInnerAngle, l.ConeOuterAngle) * 0.01745329f; // deg->rad
ConeOuterAngle = Math.Max(l.ConeInnerAngle, l.ConeOuterAngle) * 0.01745329f;
TangentY = Vector3.Normalize(Vector3.Cross(l.Direction, TangentX));
```

`BonesMap` is `Dictionary<ushort, Bone>` keyed by **`Bone.Tag`** (Skeleton.BuildBonesMap, Drawable.cs L1596-1611); building it also computes each bone's `AnimTransform` (absolute bind transform via `Matrix.AffineTransformation(1, AnimRotation, AnimTranslation)` multiplied by parent chain) and `BindTransformInv`. So a light's `BoneId` is a bone TAG, not an index, resolved against `FragType.Drawable.Skeleton`.

### Applying the bone transform at render (DeferredScene.RenderLights L548-570)

```csharp
var pos = rl.Position; var dir = rl.Direction; var tx = rl.TangentX; var ty = rl.TangentY;
if (rl.Bone != null)
{
    var xform = rl.Bone.AnimTransform;     // absolute, object space (bind pose unless animated)
    pos = xform.Multiply(pos);             // full transform for position
    dir = xform.MultiplyRot(dir);          // rotation only for direction/tangents
    tx  = xform.MultiplyRot(tx);
    ty  = xform.MultiplyRot(ty);
}
InstPosition = EntityPosition + EntityRotation.Multiply(pos) - camera.Position;
InstDirection = EntityRotation.Multiply(dir);   // etc.
InstCullingPlaneEnable = ((rl.Flags & 0x40000) != 0) ? 1u : 0u;
```

Note: for the light editor's rest pose, `Bone.AnimTransform` equals the bind pose as computed by `BuildBonesMap` (it does NOT use FragType.BoneTransforms — vehicle skeletons' bind pose matches; the FragBoneTransforms pose is used only for mesh placement).

---

## 5. Pitfalls when saving YFT

1. **Version must match generation**: 162 legacy / 171 gen9; call `Fragment.EnsureGen9()` before building gen9 output (converts vertex declarations, glass window G9 blocks etc.). Writing gen9 blocks with version 162 (or vice versa) produces a corrupt file. `ResourceBuilder.Build(Fragment, version, compress:true, gen9)` throws if any block's written size differs from `BlockLength`/`BlockLength_Gen9` — good early corruption check.
2. **For a lights-only editor, keep the loaded object graph intact and just rebuild.** Every unknown field (`Unknown_B0h/B8h/BCh/C0h/C4h/CCh`, `GravityFactor`, `BuoyancyFactor`, NaN-flag uints `0x7f800001`/`0x7fc00001`, `Unknown_C8h = -1`) is round-tripped by the Read/Write methods; do NOT reconstruct FragType from scratch.
3. **Pointers/counts recomputed automatically on Write** — `DrawablePointer`, `DrawableArrayCount`, `DrawableArrayFlag` (0 vs -1), `GlassWindowsCount`, etc. are refreshed from reference properties in each block's `Write()`; you only need the reference objects populated.
4. **Blocks created lazily in GetReferences()**: `Name` → `NameBlock (string_r)`, `FragMatricesInds/FragMatrices` → struct blocks, `ChildrenUnkFloats/InertiaTensors/UnkVecs/UnknownData1/2` → struct blocks on FragPhysicsLOD. If you null a reference property the pointer becomes 0.
5. **`FragDrawable.FragMatricesCount` is NOT auto-updated on Write** (only `FragMatricesCapacity` comes from the block; Count keeps its loaded value — capacity may exceed count with NaN-padded entries). Preserve it.
6. **Shared blocks must stay shared**: physics children reference the SAME Skeleton block as the main drawable in vanilla files; ResourceBuilder dedups by object identity, so don't clone the skeleton per child or the file grows and game behavior may change. Same for `PhysicsLOD.Bound === Archetype1.Bound`.
7. **Editing lights**: mutate `Fragment.LightAttributes.data_items` (assign a new array to add/remove — `ResourceSimpleList64.Write` recreates its data block and updates EntriesCount/Capacity). Keep `data_items` non-null (CW uses empty `LightAttributes[0]` when absent from XML). Set `LightAttributes` list object itself non-null — it is an inline part of FragType (offset 0x110) and is always written.
8. **GroupNames fix-up**: on load CW rewrites `Groups.data_items[i].Name = GroupNames.data_items[i]` (fixes zmodeler-broken files); group names are embedded 40-byte strings inside each group, and GroupNames block points at them — handled by `FragPhysGroupNamesBlock` on save.
9. **Do not save while `Drawable.OwnerDrawable`-based inheritance is misunderstood**: children whose ShaderGroup was assigned from the parent (`AssignGeometryShaders`) still serialize their own (possibly empty) ShaderGroup — the assignment only sets runtime geometry shader references, it does not copy blocks into the child. Nothing to undo before save when only lights were edited.
10. **RpfManager.IsGen9** is a global that decides both load interpretation ambiguity (version 162 within gen9 install → read legacy) and save output; a standalone port should carry an explicit `gen9` flag per file instead (read it from the RSC7 header version: 162 = legacy, 171 = gen9).

---

## 6. Minimal port recipe (viewer + light editor)

```
YftFile yft = new YftFile(); yft.Load(File.ReadAllBytes(path));   // handles RSC7+deflate
FragType f = yft.Fragment;

// drawables to render (rest pose):
//  1. f.Drawable                      transform: per-model via f.BoneTransforms (absolute; transpose Matrix3_s, translation in .W)
//  2. f.DrawableCloth                 same as main
//  3. foreach child in f.PhysicsLODGroup?.PhysicsLOD1?.Children?.data_items:
//        child.Drawable1 (skip empties; wheel children share the one wheel mesh by BoneTag)
//        transform: lod1.FragTransforms.Matrices[i] ; Row4.xyz += lod1.PositionOffset
//        (optionally child.Drawable2 = damaged version)
// shaders for children: f.Drawable.ShaderGroup (AssignChildrenShaders already ran on load)

// lights: f.LightAttributes.data_items  (may be empty; BoneId -> f.Drawable.Skeleton.BonesMap[tag].AnimTransform)

// save: File.WriteAllBytes(path, yft.Save());   // version 162 (legacy) automatically
```
