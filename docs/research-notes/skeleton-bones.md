# CodeWalker Skeleton / Bone transforms — attaching lights and rigid meshes

Sources (all paths absolute, line numbers from current CodeWalker-master snapshot):
- `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/CodeWalker.Core/GameFiles/Resources/Drawable.cs`
  - `class Skeleton` @ 1353, `class Bone` @ 2574, `class DrawableModel` (SkeletonBinding) @ ~3325, `class LightAttributes` @ 5676
- `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/CodeWalker/Rendering/Renderable.cs`
  - `Renderable.Init` model-transform logic @ 163–336, `RenderableModel` @ 753, `RenderableLight` @ 1367
- `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/CodeWalker/Rendering/Shaders/DeferredScene.cs` — light world transform @ 548–583
- `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/CodeWalker/Rendering/Renderer.cs` — selection light gizmo @ 1028, skeleton debug render @ 1464
- `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/CodeWalker.Core/Utils/Matrices.cs` — `Multiply` / `MultiplyRot` / `MultiplyW` extensions

## 0. Matrix conventions (critical)

CodeWalker uses **SharpDX row-major matrices with row-vector convention**: `v' = v * M`, translation lives in **Row4 (M41,M42,M43)**. Child-to-world composition is therefore `world = local * parentWorld` (child on the LEFT). `Matrix.AffineTransformation(scale, rotationQuat, translation)` builds `S * R * T` (with S=identity when scale=1). Shaders receive `Matrix.Transpose(model.Transform)` because HLSL cbuffers use column-major packing (e.g. `BasicShader.cs:599`).

Helper extensions (Matrices.cs, exact code):

```csharp
public static Vector3 MultiplyW(this Matrix m, Vector3 v)   // full 4x4 with 1/|w| divide
{
    float x = (((m.M11 * v.X) + (m.M21 * v.Y)) + (m.M31 * v.Z)) + m.M41;
    float y = (((m.M12 * v.X) + (m.M22 * v.Y)) + (m.M32 * v.Z)) + m.M42;
    float z = (((m.M13 * v.X) + (m.M23 * v.Y)) + (m.M33 * v.Z)) + m.M43;
    float w = (((m.M14 * v.X) + (m.M24 * v.Y)) + (m.M34 * v.Z)) + m.M44;
    float iw = 1.0f / Math.Abs(w);
    return new Vector3(x * iw, y * iw, z * iw);
}
public static Vector3 Multiply(this Matrix m, Vector3 v)    // point transform, ignores W
{
    float x = (((m.M11 * v.X) + (m.M21 * v.Y)) + (m.M31 * v.Z)) + m.M41;
    float y = (((m.M12 * v.X) + (m.M22 * v.Y)) + (m.M32 * v.Z)) + m.M42;
    float z = (((m.M13 * v.X) + (m.M23 * v.Y)) + (m.M33 * v.Z)) + m.M43;
    return new Vector3(x, y, z);
}
public static Vector3 MultiplyRot(this Matrix m, Vector3 v) // rotation only, no translation
{
    float x = (((m.M11 * v.X) + (m.M21 * v.Y)) + (m.M31 * v.Z));
    float y = (((m.M12 * v.X) + (m.M22 * v.Y)) + (m.M32 * v.Z));
    float z = (((m.M13 * v.X) + (m.M23 * v.Y)) + (m.M33 * v.Z));
    return new Vector3(x, y, z);
}
```

Note these are `v * M` (row-vector) expansions — i.e. `Multiply(m, v)` == transform point v by m in the row-vector convention.

## 1. Bone struct (Drawable.cs:2574, BlockLength = 80 bytes)

File layout, in read order (offsets from struct start):

| off | type | field |
|-----|------|-------|
| 0x00 | Vector4 -> Quaternion | `Rotation` (x,y,z,w) |
| 0x10 | Vector3 | `Translation` |
| 0x1C | uint | `Unknown_1Ch` (0, RHW?) |
| 0x20 | Vector3 | `Scale` |
| 0x2C | float | `Unknown_2Ch` = 1.0f |
| 0x30 | short | `NextSiblingIndex` |
| 0x32 | short | `ParentIndex` (-1 = root) |
| 0x34 | uint | `Unknown_34h` (0) |
| 0x38 | ulong | `NamePointer` |
| 0x40 | ushort | `Flags` (EBoneFlags) |
| 0x42 | short | `Index` |
| 0x44 | ushort | `Tag` (aka BoneId) |
| 0x46 | short | `Index2` (always == Index) |
| 0x48 | ulong | `Unknown_48h` (0) |

Runtime-only fields (not serialized):

```csharp
public Quaternion AnimRotation;   //relative to parent
public Vector3 AnimTranslation;   //relative to parent
public Vector3 AnimScale;
public Matrix AnimTransform;      //absolute world transform, animated
public Matrix BindTransformInv;   //inverse of bind pose transform
public Matrix SkinTransform;      //transform to use for skin meshes
public Matrix AbsTransform;       //original absolute transform from loaded file
public Vector4 TransformUnk;      //column 4 of skeleton's Transformations[i], IO only
```

In `Bone.Read` the anim state is seeded from the file rest pose:

```csharp
AnimRotation = Rotation;
AnimTranslation = Translation;
AnimScale = Scale;
```

`EBoneFlags` (Drawable.cs:2190): `[Flags] ushort` — `None=0, RotX=0x1, RotY=0x2, RotZ=0x4, LimitRotation=0x8, TransX=0x10, TransY=0x20, TransZ=0x40, LimitTranslation=0x80, ScaleX=0x100, ScaleY=0x200, ScaleZ=0x400, LimitScale=0x800, Unk0=0x1000, Unk1=0x2000, Unk2=0x4000, Unk3=0x8000`.

### Bone tag hash (Drawable.cs:2759)

```csharp
public static uint ElfHash_Uppercased(string str)
{
    uint hash = 0; uint x = 0; uint i = 0;
    for (i = 0; i < str.Length; i++)
    {
        var c = ((byte)str[(int)i]);
        if ((byte)(c - 'a') <= 25u) c -= 32;   // to uppercase
        hash = (hash << 4) + c;
        if ((x = hash & 0xF0000000) != 0) hash ^= (x >> 24);
        hash &= ~x;
    }
    return hash;
}
public static ushort CalculateBoneHash(string boneName)
{
    return (ushort)(ElfHash_Uppercased(boneName) % 0xFE8F + 0x170);
}
```

## 2. Skeleton struct (Drawable.cs:1353, BlockLength = 112)

Key serialized data: `BoneTags` (hash-bucketed `SkeletonBoneTag` list: `{uint BoneTag; uint BoneIndex; Next}`), `Bones` (`SkeletonBonesBlock` with `Bone[] Items`), `Matrix[] TransformationsInverted`, `Matrix[] Transformations`, `short[] ParentIndices`, `short[] ChildIndices`. Also `MetaHash Unknown_50h/54h/58h`, `Unknown_1Ch` (flags), counts.

Runtime helpers:

```csharp
public Dictionary<ushort, Bone> BonesMap { get; set; } //for convienience finding bones by tag
public Bone[] BonesSorted { get; set; } //sometimes bones aren't in parent>child order in the files! (eg player chars)
public Matrix3_s[] BoneTransforms; //for rendering (skin matrices, 3x4)
```

`Skeleton.Read` order matters: read arrays → `AssignBoneParents()` → `BuildBonesMap()`.

`AssignBoneParents` (1579): for each i, `bone.Parent = Bones.Items[ParentIndices[i]]` when `0 <= ParentIndices[i] < len`.

## 3. Rest-pose (bind) world transform — BuildBonesMap (Drawable.cs:1596)

This is where AnimTransform is initialised **at rest** — exact code:

```csharp
public void BuildBonesMap()
{
    BonesMap = new Dictionary<ushort, Bone>();
    if (Bones?.Items != null)
    {
        var bonesSorted = new List<Bone>();
        for (int i = 0; i < Bones.Items.Length; i++)
        {
            var bone = Bones.Items[i];
            BonesMap[bone.Tag] = bone;
            bonesSorted.Add(bone);

            bone.UpdateAnimTransform();
            bone.AbsTransform = bone.AnimTransform;
            bone.BindTransformInv = (i < (TransformationsInverted?.Length ?? 0)) ? TransformationsInverted[i] : Matrix.Invert(bone.AnimTransform);
            bone.BindTransformInv.M44 = 1.0f;
            bone.UpdateSkinTransform();
            bone.TransformUnk = (i < (Transformations?.Length ?? 0)) ? Transformations[i].Column4 : Vector4.Zero;//still dont know what this is
        }
        bonesSorted.Sort((a, b) => a.Index.CompareTo(b.Index));
        BonesSorted = bonesSorted.ToArray();
    }
}
```

So **BonesMap is keyed by `Bone.Tag` (ushort)**, and at rest `AnimTransform == AbsTransform` == bind-pose world (drawable-local model space) transform.

### Bone.UpdateAnimTransform (Drawable.cs:2720) — the core formula

```csharp
public void UpdateAnimTransform()
{
    AnimTransform = Matrix.AffineTransformation(1.0f, AnimRotation, AnimTranslation);
    AnimTransform.ScaleVector *= AnimScale;
    if (Parent != null)
    {
        AnimTransform = AnimTransform * Parent.AnimTransform;
    }
}
public void UpdateSkinTransform()
{
    SkinTransform = BindTransformInv * AnimTransform;
}
public void ResetAnimTransform()
{
    AnimRotation = Rotation;
    AnimTranslation = Translation;
    AnimScale = Scale;
    UpdateAnimTransform();
    UpdateSkinTransform();
}
```

Port recipe for rest-pose world transform of bone i:
1. `local = RotationMatrix(bone.Rotation)` with `local.Row4.xyz = bone.Translation` (that's what `AffineTransformation(1, q, t)` gives in SharpDX row-major).
2. `local.M11 *= Scale.X; local.M22 *= Scale.Y; local.M33 *= Scale.Z;` — note CW multiplies the **diagonal only** (`ScaleVector` = (M11,M22,M33)). This is exact only for identity rotation; it is a CW quirk you must replicate for identical output. In practice bone Scale is (1,1,1) for nearly all props.
3. `world = local * parentWorld` (row-vector convention; child on the left). Root bones (`ParentIndex == -1`): `world = local`.

**Ordering caveat:** `BuildBonesMap` computes transforms in file array order and reads `Parent.AnimTransform` directly, so it silently assumes parents appear before children in `Bones.Items`. The `BonesSorted` comment warns this is not always true (player chars). For a safe port, compute recursively or topologically; for prop/vehicle yft/ydr the file order is parent-first.

`Skeleton.TransformationsInverted[i]` from the file is the inverse bind-pose matrix (used directly as `BindTransformInv`, with `M44` forced to 1.0). `Skeleton.Transformations[i]` is the per-bone **local** transform matrix (rotation+translation), whose Column4 is the mysterious `TransformUnk` — CW rebuilds both arrays from bone R/T/S on save (`BuildTransformations`).

Skin matrices for GPU (Drawable.cs:1946 `UpdateBoneTransforms`): `BoneTransforms[i]` = 3x4 built from `SkinTransform` **columns**: `bt.Row1 = b.Column1; bt.Row2 = b.Column2; bt.Row3 = b.Column3;` (i.e. transposed into 3 rows of 4).

## 4. Lights: BoneId → Bone via BonesMap

`LightAttributes.BoneId` is a **ushort** (Drawable.cs:5694, read at 5780 as `reader.ReadUInt16()`), and it matches `Bone.Tag` — confirmed by the lookup in `RenderableLight.Init` (Renderable.cs:1390):

```csharp
public void Init(LightAttributes l)
{
    OwnerLight = l;
    var pos = l.Position;
    var dir = l.Direction;
    var tan = l.Tangent;
    var bones = Owner?.Skeleton?.BonesMap;
    bones?.TryGetValue(l.BoneId, out Bone);   // BonesMap: Dictionary<ushort Tag, Bone>
    Position = pos;                           // stays LOCAL (bone space); bone applied at draw time
    ...
    TangentY = Vector3.Normalize(Vector3.Cross(l.Direction, TangentX));
    ConeInnerAngle = Math.Min(l.ConeInnerAngle, l.ConeOuterAngle) * 0.01745329f; // deg->rad
    ConeOuterAngle = Math.Max(l.ConeInnerAngle, l.ConeOuterAngle) * 0.01745329f;
}
```

BoneId 0: most drawables have a root bone with Tag 0, so `TryGetValue(0)` finds the root (usually identity at rest). If the drawable has no skeleton, `Bone` stays null and the light position is used as-is in drawable space.

### Light world transform at draw time (DeferredScene.cs:548)

```csharp
var pos = rl.Position;
var dir = rl.Direction;
var tx = rl.TangentX;
var ty = rl.TangentY;
if (rl.Bone != null)
{
    var xform = rl.Bone.AnimTransform;
    pos = xform.Multiply(pos);        // full point transform (rot+trans)
    dir = xform.MultiplyRot(dir);     // rotation only
    tx = xform.MultiplyRot(tx);
    ty = xform.MultiplyRot(ty);
}

LightInstVars.Vars.InstPosition = li.EntityPosition + li.EntityRotation.Multiply(pos) - camera.Position;
LightInstVars.Vars.InstDirection = li.EntityRotation.Multiply(dir);
LightInstVars.Vars.InstTangentX = li.EntityRotation.Multiply(tx);
LightInstVars.Vars.InstTangentY = li.EntityRotation.Multiply(ty);
LightInstVars.Vars.InstCapsuleExtent = li.EntityRotation.Multiply(rl.CapsuleExtent);
LightInstVars.Vars.InstCullingPlaneNormal = li.EntityRotation.Multiply(rl.CullingPlaneNormal);
```

So the full chain is: **lightLocal → bone.AnimTransform (drawable space) → entity rotation+position (world)**. `li.EntityRotation.Multiply(v)` is quaternion-rotate. The selection gizmo path (Renderer.cs:1028 `RenderSelectionDrawableLight`) does the identical `xform.Multiply(pos)` / `MultiplyRot(dir/tx)` with `bone.AnimTransform`.

For a static light editor (no anim playing), `AnimTransform` is exactly the rest-pose world matrix computed in section 3 — nothing else runs unless `UpdateAnims`/`ResetBoneTransforms` is invoked.

## 5. DrawableModel.SkeletonBinding and rigid (non-skinned) model transforms

`DrawableModel` (Drawable.cs:3325):

```csharp
public uint SkeletonBinding { get; set; }//4th byte is bone index, 2nd byte for skin meshes
public byte BoneIndex   { get { return (byte)((SkeletonBinding >> 24) & 0xFF); } ... }
public byte SkeletonBindUnk2 /*>>16*/;  public byte HasSkin /*>>8, 1 if skinned*/;  public byte SkeletonBindUnk1 /*>>0*/;
```

`RenderableModel.Init` (Renderable.cs:770):

```csharp
SkeletonBinding = dmodel.SkeletonBinding;
IsSkinMesh = ((SkeletonBinding >> 8) & 0xFF) > 0;
BoneIndex = (int)((SkeletonBinding >> 24) & 0xFF);
```

### Renderable.Init transform assignment (Renderable.cs:163–336) — the yft props pattern

Setup: `modeltransforms = skeleton.Transformations` (per-bone LOCAL matrices). For FragDrawables, if the owner Fragment has `BoneTransforms` (`FragBoneTransforms`, `Matrix3_s[] Items` — Frag.cs:1079), those are the default **absolute** pose and replace modeltransforms (`usepose = true`), converted with a transpose (Matrix3_s rows become matrix columns, W components become translation Row4):

```csharp
var p = pose.Items[i];
Vector4 r1 = p.Row1; Vector4 r2 = p.Row2; Vector4 r3 = p.Row3;
modeltransforms[i] = new Matrix(r1.X, r2.X, r3.X, 0.0f,  r1.Y, r2.Y, r3.Y, 0.0f,
                                r1.Z, r2.Z, r3.Z, 0.0f,  r1.W, r2.W, r3.W, 1.0f);
```

Per model:

```csharp
int boneidx = model.BoneIndex;                    // SkeletonBinding >> 24
Matrix trans = (boneidx < modeltransforms.Length) ? modeltransforms[boneidx] : Matrix.Identity;
Bone bone = (hasbones && (boneidx < bones.Length)) ? bones[boneidx] : null;

if (mi < HDModels.Length)                          // bone-tag -> model map for anim updates
    if (bone != null) ModelBoneLinks[bone.Tag] = model;

if ((fragtransforms != null))                      // frag phys children: explicit matrix + offset
{
    if (fragtransformid < fragtransforms.Length)
    {
        trans = fragtransforms[fragtransformid];
        trans.Row4 += fragoffset;                  // fragoffset = (PhysicsLOD.PositionOffset, 0)
    }
}
else if (!usepose) //when using the skeleton's matrices, they need to be transformed by parent
{
    trans.Column4 = Vector4.UnitW;                 // clear junk 4th column (TransformUnk), keep Row4 translation
    short[] pinds = skeleton.ParentIndices;
    short parentind = ((pinds != null) && (boneidx < pinds.Length)) ? pinds[boneidx] : (short)-1;
    while ((parentind >= 0) && (parentind < pinds.Length))
    {
        Matrix ptrans = (parentind < modeltransforms.Length) ? modeltransforms[parentind] : Matrix.Identity;
        ptrans.Column4 = Vector4.UnitW;
        trans = Matrix.Multiply(trans, ptrans);    // child * parent, walk up the chain
        parentind = ((pinds != null) && (parentind < pinds.Length)) ? pinds[parentind] : (short)-1;
    }
}

if (model.IsSkinMesh) model.Transform = Matrix.Identity;  // skinned: bones do the work
else                  model.Transform = trans;            // rigid: whole model gets bone world matrix
```

Notes:
- `model.BoneIndex` is a bone **array index** (not a tag). The equivalent bone world matrix equals `bones[boneidx].AbsTransform` (rest pose) — the parent-chain walk over `skeleton.Transformations` is just another way to compute it. In your port you can simply use the section-3 result: `model.Transform = bone.AbsTransform` for rigid models, identity for skinned models.
- `trans.Column4 = Vector4.UnitW` zeroes M14/M24/M34 and sets M44=1 (kills the TransformUnk junk stored in Column4 of the file's Transformations matrices) — required, otherwise MultiplyW/projection goes wrong.
- When animating (Renderable.cs:540–553), CW updates rigid models via the bone-tag map: `bmodel.Transform = bone.AnimTransform;` (skipped for `IsSkinMesh`).
- Shaders consume it as `VSModelVars.Vars.Transform = Matrix.Transpose(model.Transform);` and skip the cbuffer entirely when `!model.UseTransform` (BasicShader.cs:598).
- Vehicle special case (Renderable.cs:217): right-side wheel frag transforms get their basis X/Z negated (mirror) for BoneTags `26418 (wheel_rf), 5857/5858/5859 (wheel_rm1-3), 26398 (wheel_rr)`.

## 6. What a light editor must implement (summary recipe)

1. After loading skeleton: assign `Parent` from `ParentIndices`; per bone in parent-first order compute
   `Local = R(q) with Row4=T, diag *= S` then `World = Local * ParentWorld`. Store as `AbsTransform`/rest `AnimTransform`. Build `Dictionary<ushort,Bone>` on `Tag`.
2. For each light: `bone = BonesMap.TryGetValue(light.BoneId)`. World placement:
   `posW = boneWorld.Multiply(light.Position)`, `dirW = boneWorld.MultiplyRot(light.Direction)`, `txW = boneWorld.MultiplyRot(light.Tangent)`, `tyW = normalize(cross(dirW, txW))` (CW computes ty from local dir/tan then rotates; same result for pure rotations). Then entity: `world = entityPos + entityRot * v` (rotate-only for dir/tx/ty).
3. For rigid meshes on bones (yft props, vehicle parts): `modelWorld = bones[SkeletonBinding>>24].AbsTransform`; skinned models ((SkeletonBinding>>8)&0xFF > 0) render with identity model transform + skin matrices `Column1..3(BindTransformInv * AnimTransform)`.
4. When the user edits a light on a bone, store the **bone-local** position back into `LightAttributes.Position` (i.e. multiply by inverse bone world matrix); the file always stores bone-space values.
