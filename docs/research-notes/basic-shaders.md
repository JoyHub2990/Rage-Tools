# CodeWalker Basic surface shaders — implementation notes for the port

Sources (all under `C:/Users/GS/Desktop/MAX_Light_Editor/CodeWalker-master/`):
- `CodeWalker.Shaders/BasicVS.hlsli` — shared VS cbuffers, VS_OUTPUT, transform helpers
- `CodeWalker.Shaders/BasicVS_PNCT.hlsl`, `BasicVS_PNCTX.hlsl`, `BasicVS_PBBNCTX.hlsl` — per-vertex-type VS entry points
- `CodeWalker.Shaders/BasicPS.hlsli` — PS cbuffers, texture slots, VS_OUTPUT (duplicated), PS_OUTPUT (deferred)
- `CodeWalker.Shaders/BasicPS.hlsl` — **forward** pixel shader (this is the one to reproduce)
- `CodeWalker.Shaders/BasicPS_Deferred.hlsl` — deferred variant (GBuffer outputs)
- `CodeWalker.Shaders/Common.hlsli` — `ShaderGlobalLightParams`, `NormalMap`, `GlobalLighting`, `AmbientLight`, `BasicLighting`, `DepthFunc`, `GeomWindMotion`
- `CodeWalker.Shaders/Shadowmap.hlsli` — shadowmap cbuffer (b1), `ShadowmapSceneDepth`, `ShadowAmount`, **`FullLighting`**
- `CodeWalker.Shaders/Quaternion.hlsli` — `mulvq`
- `CodeWalker/Rendering/Shaders/BasicShader.cs` — C#-side cbuffer structs + how everything is fed
- `CodeWalker/Rendering/Renderable.cs` (lines ~830–1086) — where bumpiness/spec params come from drawable shader params
- `CodeWalker/Rendering/ShaderManager.cs` (~1426–1447) — `ShaderGlobalLights` / `ShaderGlobalLightParams` C# structs
- `CodeWalker/Rendering/Renderer.cs` (~500–609) — how global light params are computed from weather/timecycle

Key architectural fact: **positions are camera-relative throughout**. The VS never produces a world
position; `ModelTransform` returns `CamRel.xyz + rotatedScaledLocalPos`, and `ViewProj` is a
view-projection built for a camera at the origin. `CamRelPos` is what the PS receives, used for the
view/incident vector (`normalize(input.CamRelPos)` = direction camera→pixel).

---

## 1. Vertex shader

### 1.1 VS cbuffers (`BasicVS.hlsli`, exact HLSL)

```hlsl
cbuffer VSSceneVars : register(b0)
{
    float4x4 ViewProj;      // Matrix.Transpose(camera.ViewProjMatrix) on CPU; mul(pos, ViewProj) in HLSL
    float4 WindVector;
}
cbuffer VSEntityVars : register(b2)
{
    float4 CamRel;          // entity position minus camera position
    float4 Orientation;     // entity rotation quaternion (xyzw)
    uint HasSkeleton;
    uint HasTransforms;
    uint TintPaletteIndex;
    uint Pad1;
    float3 Scale;
    uint IsInstanced;
}
cbuffer VSModelVars : register(b3)
{
    float4x4 Transform;     // Matrix.Transpose(model.Transform); only applied when HasTransforms==1
}
cbuffer VSGeomVars : register(b4)
{
    uint EnableTint;        // 0=off, 1=use colour0.b as palette U, 2=use colour1 (trees)
    float TintYVal;         // (TintPaletteIndex + 0.5) / paletteTexHeight
    uint IsDecal;
    uint EnableWind;
    float4 WindOverrideParams;
    float4 globalAnimUV0;   // default (1,0,0,0)
    float4 globalAnimUV1;   // default (0,1,0,0)
}
cbuffer VSInstGlobals : register(b5) { float4 gInstanceVars[24]; }  // 8x 3-row grass rotation matrices
cbuffer VSInstLocals  : register(b6) { ... }                        // grass batch params (see BasicShaderInstLocals below)
cbuffer BoneMatrices  : register(b7) { row_major float3x4 gBoneMtx[255]; } // rage_bonemtx
cbuffer ClothVertices : register(b8) { float4 clothVertices[254]; }
```

Note b1 is reserved for the shadowmap cbuffer (`ShadowmapVars`, see section 4) in both VS and PS stages.

### 1.2 VS_OUTPUT (identical struct in BasicVS.hlsli and BasicPS.hlsli — keep in sync!)

```hlsl
struct VS_OUTPUT
{
    float4 Position  : SV_POSITION;
    float3 Normal    : NORMAL;       // world(-oriented) normal, normalized in VS
    float2 Texcoord0 : TEXCOORD0;
    float2 Texcoord1 : TEXCOORD1;    // 0.5 if vertex type has no 2nd texcoord
    float2 Texcoord2 : TEXCOORD2;    // 0.5 if none
    float4 Shadows   : TEXCOORD3;    // x = scene eye-space depth for cascade selection
    float4 LightShadow : TEXCOORD4;  // position in light (shadowmap) space
    float4 Colour0   : COLOR0;       // vertex colour 0 (raw)
    float4 Colour1   : COLOR1;       // (0.5,0.5,0.5,1) if none
    float4 Tint      : COLOR2;       // sampled tint palette colour (or 1)
    float4 Tangent   : TEXCOORD5;    // xyz = world tangent, w = input tangent.w (handedness)
    float4 Bitangent : TEXCOORD6;    // xyz = cross(tang, norm) * tangent.w
    float3 CamRelPos : TEXCOORD7;    // camera-relative world position
};
```

VS also binds: `Texture2D<float4> TintPalette : register(t0);` + `SamplerState TextureSS : register(s0);`
(VS sampler s0 = point-filter tint sampler set from C#; grass instance buffer is `StructuredBuffer` at VS t2).

### 1.3 VS input semantics per vertex type

Letters: P=Position, N=Normal, C=Colour, T=Texcoord, X=Tangent, BB=BlendWeights+BlendIndices.

`BasicVS_PNCT.hlsl`:
```hlsl
struct VS_INPUT
{
    float4 Position  : POSITION;
    float3 Normal    : NORMAL;
    float2 Texcoord0 : TEXCOORD0;
    float4 Colour0   : COLOR0;
};
```

`BasicVS_PNCTX.hlsl` adds `float4 Tangent : TANGENT;`.
`PNCCT*` variants add `float4 Colour1 : COLOR1;`, `PNCTT*` add `float2 Texcoord1 : TEXCOORD1;` (and TEXCOORD2), etc.
Skinned `PBBNCTX` (`BasicVS_PBBNCTX.hlsl`):
```hlsl
struct VS_INPUT
{
    float4 Position : POSITION;
    float4 BlendWeights : BLENDWEIGHTS;
    float4 BlendIndices : BLENDINDICES;
    float3 Normal : NORMAL;
    float4 Colour0 : COLOR0;
    float2 Texcoord0 : TEXCOORD0;
    float4 Tangent : TANGENT;
};
```

### 1.4 VS main body — PNCTX (verbatim; PNCT identical except tangent)

```hlsl
VS_OUTPUT main(VS_INPUT input, uint iid : SV_InstanceID)
{
    VS_OUTPUT output;
    float3 opos = ModelTransform(input.Position.xyz, input.Colour0.xyz, input.Colour0.xyz, iid);
    float4 cpos = ScreenTransform(opos);
    float3 bnorm = NormalTransform(input.Normal);
    float3 btang = NormalTransform(input.Tangent.xyz);

    float4 tnt = ColourTint(input.Colour0.b, 0, iid); //colour tinting if enabled

    float4 lightspacepos;
    float shadowdepth = ShadowmapSceneDepth(opos, lightspacepos);
    output.LightShadow = lightspacepos;
    output.Shadows = float4(shadowdepth, 0,0,0);

    output.Position = cpos;
    output.CamRelPos = opos;
    output.Normal = bnorm;
    output.Texcoord0 = GlobalUVAnim(input.Texcoord0);
    output.Texcoord1 = 0.5;
    output.Texcoord2 = 0.5;
    output.Colour0 = input.Colour0;
    output.Colour1 = float4(0.5,0.5,0.5,1);
    output.Tint = tnt;
    output.Tangent = float4(btang, input.Tangent.w);
    output.Bitangent = float4(cross(btang, bnorm) * input.Tangent.w, 0);
    return output;
}
```

**Tangent-space construction:** tangent is transformed like a normal (`NormalTransform`), bitangent is
`cross(tangent, normal) * tangent.w` computed in the VS (tangent.w = handedness sign from the vertex data).
For PNCT (no tangent in vertex): `float3 btang = 0.5;` and `output.Tangent = float4(btang, 1); output.Bitangent = float4(cross(btang, bnorm), 0);` — junk, but EnableNormalMap will be 0 for those geoms anyway.
Skinned variant: `BoneTransform(...)` first produces bone-space pos/norm/tang, then the same
`ModelTransform`/`NormalTransform` pipeline, and `Bitangent = float4(cross(otang, onorm) * input.Tangent.w, 0)`.

### 1.5 VS helper functions (verbatim, from BasicVS.hlsli / Quaternion.hlsli / Common.hlsli)

```hlsl
// Quaternion.hlsli — rotate vector by quaternion
float3 mulvq(float3 v, float4 q)
{
    float3 u = q.xyz;
    float s = q.w;
    return (dot(u, v)*u*2.0f) + (s*s - dot(u, u)) * v + (cross(u, v)*s*2.0f);
}

float3 ModelTransform(float3 ipos, float3 vc0, float3 vc1, uint iid)
{
    if (IsInstanced) { return GetGrassInstancePosition(ipos, vc0, vc1, iid); }
    else
    {
        float3 tpos = (HasTransforms == 1) ? mul(float4(ipos, 1), Transform).xyz : ipos;
        float3 spos = tpos * Scale;
        float3 bpos = mulvq(spos, Orientation);
        if (EnableWind) { bpos = GeomWindMotion(bpos, vc0, WindVector, WindOverrideParams); }
        return CamRel.xyz + bpos;   // <-- camera-relative output
    }
}

float4 ScreenTransform(float3 opos)
{
    float4 pos = float4(opos, 1);
    float4 cpos = mul(pos, ViewProj);
    cpos.z = DepthFunc(cpos.zw);    // DepthFunc(zw) currently just returns zw.x (standard depth)
    return cpos;
}

float3 NormalTransform(float3 inorm)
{
    float3 tnorm = (HasTransforms == 1) ? mul(inorm, (float3x3)Transform) : inorm;
    float3 bnorm = normalize(mulvq(tnorm, Orientation));
    return bnorm;
}

float4 ColourTint(float tx, float tx2, uint iid)
{
    float4 tnt = 1;
    if (IsInstanced) { /* grass: unpack RGB from instance data * 0.003922 */ }
    else if (EnableTint > 0)
    {
        float tu = (EnableTint == 1) ? tx : tx2;   // tx = Colour0.b, tx2 = Colour1.b (trees)
        tnt = TintPalette.SampleLevel(TextureSS, float2(tu, TintYVal), 0);
    }
    return tnt;
}

float2 GlobalUVAnim(float2 uv)   // identity when globalAnimUV0=(1,0,0,0), UV1=(0,1,0,0)
{
    float2 r;
    float3 uvw = float3(uv, 1);
    r.x = dot(globalAnimUV0.xyz, uvw);
    r.y = dot(globalAnimUV1.xyz, uvw);
    return r;
}
```

Wind (Common.hlsli, used only when EnableWind — vegetation shaders):
```hlsl
float3 GeomWindMotion(float3 ipos, float3 vc0, float4 windvec, float4 overrideparams)
{
    float3 f1 = vc0.xxz * windvec.xxy * overrideparams.xxy;
    float phase = vc0.y + 0.0;
    float phrad = abs(phase)*6.283185;
    float3 f2 = windvec.zzw * overrideparams.zzw + phrad;
    f2 = sin(f2);
    f1 = f2*f1 + ipos;
    return f1;
}
```

Skinning (BasicVS.hlsli `BoneTransform`): `uint4 binds = (uint4)(indices * 255.001953);` then blends 4 rows
of `row_major float3x4 gBoneMtx[255]`; `binds.z > 254` signals the cloth-vertices path. Not needed for a
static-prop light editor unless you render skinned YFTs.

---

## 2. Pixel shader (forward: `BasicPS.hlsl`)

### 2.1 PS resources and cbuffers (`BasicPS.hlsli`, exact)

```hlsl
Texture2D<float4> Colourmap   : register(t0);   // DiffuseSampler
// t1 = shadowmap Depthmap (Shadowmap.hlsli)
Texture2D<float4> Bumpmap     : register(t2);   // BumpSampler
Texture2D<float4> Specmap     : register(t3);   // SpecSampler
Texture2D<float4> Detailmap   : register(t4);   // DetailSampler
Texture2D<float4> Colourmap2  : register(t5);   // DiffuseSampler2/DiffuseExtraSampler
Texture2D<float4> TintPalette : register(t6);   // weapon-tint palette (PS side)
SamplerState TextureSS : register(s0);          // linear-wrap (or 8x aniso)
// s1 = SamplerComparisonState DepthmapSS (shadowmap)

cbuffer PSSceneVars : register(b0)
{
    ShaderGlobalLightParams GlobalLights;   // 7 float4s, see section 3
    uint EnableShadows;
    uint RenderMode;   // 0=default, 1=normals, 2=tangents, 3=colours, 4=texcoords,
                       // 5=diffuse, 6=normalmap, 7=spec, 8=direct(single texture)
    uint RenderModeIndex;
    uint RenderSamplerCoord;
}
cbuffer PSGeomVars : register(b2)
{
    uint EnableTexture;   // 0=none, 1=diffuse1, +2=diffuse2 present
    uint EnableTint;      // 0/1 = vertex-driven palette tint, 2 = weapon tint (diffuse.a lookup)
    uint EnableNormalMap;
    uint EnableSpecMap;
    uint EnableDetailMap;
    uint IsDecal;         // 0=opaque(alpha test .33), 1=decal, 2=dirt-decal mask, 3=normal-only decal, 4=spec-only decal
    uint IsEmissive;
    uint IsDistMap;       // distance-map (signed distance field signage)
    float bumpiness;
    float AlphaScale;
    float HardAlphaBlend;
    float useTessellation;
    float4 detailSettings;
    float3 specMapIntMask;
    float specularIntensityMult;
    float specularFalloffMult;
    float specularFresnel;
    float wetnessMultiplier;
    uint SpecOnly;
    float4 TextureAlphaMask;
}
```

### 2.2 Forward PS flow (BasicPS.hlsl, condensed but with all live code)

```hlsl
float4 main(VS_OUTPUT input) : SV_TARGET
{
    float4 c = float4(0.5, 0.5, 0.5, 1);
    if (RenderMode == 0) c = float4(1, 1, 1, 1);      // white base when untextured
    if (EnableTexture > 0)
    {
        float2 texc = input.Texcoord0;                 // (debug modes may pick Texcoord1/2)
        c = Colourmap.Sample(TextureSS, texc);
        if (EnableTexture > 1)                         // second diffuse, alpha-blended over first
        {
            float4 c2 = Colourmap2.Sample(TextureSS, input.Texcoord1);
            c = c2.a * c2 + (1 - c2.a) * c;
        }
        if (EnableTint == 2)                           // weapon tint via diffuse alpha
        {
            float tx = (round(c.a * 255.009995) - 32.0) * 0.007813;
            float ty = 0.03125 * 0.5;
            float4 c3 = TintPalette.Sample(TextureSS, float2(tx, ty));
            c.rgb *= c3.rgb;  c.a = 1;
        }
        if (IsDistMap) c = float4(c.rgb*2, (c.r+c.g+c.b) - 1);
        if ((IsDecal == 0) && (c.a <= 0.33)) discard;  // ALPHA TEST for opaque
        if ((IsDecal == 1) && (c.a <= 0.0)) discard;   // decals: discard fully transparent
        if (IsDecal == 0) c.a = 1;                     // opaque forces alpha 1
        if (IsDecal == 2)                              // dirt decal channel mask
        {
            float4 mask = TextureAlphaMask * c;
            c.a = saturate(mask.r + mask.g + mask.b + mask.a);
            c.rgb = 0;
        }
        c.a = saturate(c.a * AlphaScale);
    }
    if (EnableTint == 1) c.rgb *= input.Tint.rgb;      // palette tint from VS
    if (IsDecal == 1)    c.a *= input.Colour0.a;       // decal fade by vertex alpha

    float3 norm = normalize(input.Normal);
    // RenderMode 1..4 debug overrides omitted (normals/tangents/colours/texcoords as rgb)

    float3 spec = 0;
    if (RenderMode == 0)
    {
        float4 nv = Bumpmap.Sample(TextureSS, input.Texcoord0);
        float4 sv = Specmap.Sample(TextureSS, input.Texcoord0);

        float2 nmv = nv.xy;
        float4 r0 = 0, r1, r2, r3;

        if (EnableNormalMap)
        {
            if (EnableDetailMap)
            {
                r0.xy = input.Texcoord0 * detailSettings.zw;
                r0.zw = r0.xy * 3.17;
                r0.xy = Detailmap.Sample(TextureSS, r0.xy).xy - 0.5;
                r0.zw = Detailmap.Sample(TextureSS, r0.zw).xy - 0.5;
                r0.xy = r0.xy + r0.zw;
                r0.yz = r0.xy * detailSettings.y;
                nmv = r0.yz*sv.w + nv.xy;   // detail added pre-decode, scaled by specmap alpha(!)
            }
            norm = NormalMap(nmv, bumpiness, input.Normal.xyz, input.Tangent.xyz, input.Bitangent.xyz);
        }

        if (EnableSpecMap == 0) sv = float4(0.1,0.1,0.1,0.1);   // fallback constant spec

        // wetness block is effectively neutralized by constants:
        float r1y = norm.z - 0.35;
        float3 globalScalars = float3(0.5, 0.5, 0.5);
        float globalScalars2z = 1;    // => r0.z = 0
        float wetness = 0;            // => wet contribution = 0
        r0.x = 0;
        r0.z = 1 - globalScalars2z;
        r0.y = saturate(r1y*1.538462) * wetness * r0.z;         // = 0
        r1.yz = input.Colour0.xy * globalScalars.zy;
        r0.y = r0.y * r1.y;                                     // = 0
        r0.x = r0.x * sv.w + 1.0;                               // = 1
        sv.xy = sv.xy*sv.xy;                                    // spec map RG squared
        r0.z = sv.w * specularFalloffMult;
        r3.y = r0.z * 0.001953125;                              // (1/512) — falloff, unused in forward
        r0.z = dot(sv.xyz, specMapIntMask);                     // <-- spec intensity: dp3(specRGB^2ish, mask)
        r0.z = r0.z*specularIntensityMult;
        r3.x = r0.x * r0.z;                                     // = spec intensity (r0.x==1)
        r0.z = saturate(r0.z*r0.x + 0.4);
        r0.z = 1 - r3.x*0.5;
        r0.z = r0.z * r0.y;                                     // = 0
        r0.y = r0.y * wetnessMultiplier;                        // = 0
        r0.z = 1 - r0.z*0.5;                                    // = 1

        float3 tc = c.rgb * r0.x;    // = c.rgb
        c.rgb = tc * r0.z;           // = c.rgb  (net effect of whole block on diffuse: none)

        // CodeWalker's own ad-hoc sun specular highlight:
        float3 incident = normalize(input.CamRelPos);           // camera->pixel dir (cam at origin)
        float3 refl = normalize(reflect(incident, norm));
        float specb = saturate(dot(refl, GlobalLights.LightDir));
        float specp = max(exp(specb * 10) - 1, 0);
        spec += GlobalLights.LightDirColour.rgb * 0.00006 * specp * r0.z * sv.x * specularIntensityMult;

        if (SpecOnly == 1)
            c.a *= (EnableSpecMap == 0) ? nv.a : saturate(specp);
    }

    float4 fc = c;   // save pre-lighting colour for emissive

    c.rgb = FullLighting(c.rgb, spec, norm, input.Colour0, GlobalLights,
                         EnableShadows, input.Shadows.x, input.LightShadow);

    if (IsEmissive == 1)
    {
        c.rgb += fc.rgb;     // emissive: ADD the unlit albedo on top of the lit result
    }

    c.a = saturate(c.a);
    return c;
}
```

**Practical takeaways for a simpler forward shader:**
- Net diffuse modification from the wetness/spec block is identity (all wet factors are zeroed by
  constants), so you can skip it entirely and keep only: `specIntensity = dot(specSample.xyz^2-ish, specMapIntMask) * specularIntensityMult`.
  Note only `sv.xy` get squared before the dot; `sv.z` is not squared (faithful to CW).
- Sun specular = `LightDirColour.rgb * 0.00006 * (exp(10*saturate(dot(reflect(view,n),L)))-1) * sv.x * specularIntensityMult`.
- Alpha test: opaque `c.a <= 0.33 → discard`, then force `c.a = 1`. Decal: `c.a <= 0 → discard`, multiply by `Colour0.a`, blend on.
- Emissive is handled by adding the unlit albedo after lighting (`c.rgb += fc.rgb`).

### 2.3 Normal-mapping decode (`Common.hlsli::NormalMap`, verbatim)

```hlsl
float3 NormalMap(float2 nmv, float bumpinezz, float3 norm, float3 tang, float3 bita)
{
    float2 nxy = nmv.xy * 2 - 1;                    // decode RG -> [-1,1]
    float2 bxy = nxy * max(bumpinezz, 0.001);       // scale XY by bumpiness (min 0.001)
    float bxyz = sqrt(abs(1 - dot(nxy, nxy)));      // reconstruct Z from UNSCALED xy
    float3 t1 = tang * bxy.x;
    float3 t2 = bita * bxy.y + t1;
    float3 t3 = norm * bxyz + t2;
    return normalize(t3);
}
```
i.e. `worldN = normalize(T*nx*bump + B*ny*bump + N*sqrt(|1-dot(nxy,nxy)|))` — GTA normal maps store
XY in RG (BC-style two-channel); Z is derived, and **bumpiness scales only the tangent-plane part**.

### 2.4 Deferred PS differences (BasicPS_Deferred.hlsl)

Same front half; ends with 4 MRT outputs instead of lighting:
```hlsl
spec.xy = sqrt(r3.xy);  spec.z = r0.z;
float emiss = (IsEmissive == 1) ? 1.0 : 0.0;
float4 a = c.aaaa;
if (IsDecal==3) a.xzw = 0; //normal_only decal writes only normal target
if (IsDecal==4) a.xyw = 0; //spec_only decal writes only spec target
output.Diffuse    = float4(c.rgb, a.x);
output.Normal     = float4(saturate(norm * 0.5 + 0.5), a.y);
output.Specular   = float4(spec, a.z);
output.Irradiance = float4(input.Colour0.rg, emiss, a.w);
```
Also: deferred adds `if (IsDecal == 4) c.a = c.r;` before alpha test and treats `IsDecal >= 3` like decal for
discard/vertex-alpha. Not needed for a forward one-pass port.

---

## 3. Lighting — FullLighting and friends

### 3.1 Global light params struct (Common.hlsli / ShaderManager.cs — identical layout, 7 float4s = 112 bytes)

```hlsl
struct ShaderGlobalLightParams
{
    float3 LightDir;              // direction TOWARD the light (sun/moon), normalized, Z clamped >= 0
    float LightHdr;               // global intensity (unused in Basic shaders)
    float4 LightDirColour;        // sun colour
    float4 LightDirAmbColour;     // directional ambient
    float4 LightNaturalAmbUp;     // hemisphere ambient sky
    float4 LightNaturalAmbDown;   // hemisphere ambient ground
    float4 LightArtificialAmbUp;
    float4 LightArtificialAmbDown;
};
```

### 3.2 The lighting functions (verbatim)

`Shadowmap.hlsli`:
```hlsl
float3 FullLighting(float3 diff, float3 spec, float3 norm, float4 vc0,
                    uniform ShaderGlobalLightParams globalLights,
                    uint enableShadows, float shadowdepth, float4 shadowcoord)
{
    float lf = saturate(dot(norm, globalLights.LightDir.xyz));

    float shadowlit = 1.0;
    if (enableShadows == 1)
    {
        if (abs(shadowdepth) < ShadowMaxDistance)  //2km
        {
            shadowlit = ShadowAmount(shadowcoord, shadowdepth);
        }
    }

    lf *= shadowlit;
    float3 speclit = spec*shadowlit;
    return GlobalLighting(diff, norm, vc0, lf, globalLights) + speclit;
}
```

`Common.hlsli`:
```hlsl
float3 BasicLighting(float4 lightcolour, float4 ambcolour, float pclit)
{
    return (ambcolour.rgb + lightcolour.rgb*pclit);
}

float3 AmbientLight(float3 diff, float normz, float4 upcolour, float4 downcolour, float amount)
{
    float bf = normz*0.5 + 0.5;
    float3 upval = upcolour.rgb*saturate(1.0-bf);
    float3 downval = downcolour.rgb*saturate(bf);
    return diff*(upval + downval)*amount;
}

float3 GlobalLighting(float3 diff, float3 norm, float4 vc0, float lf,
                      uniform ShaderGlobalLightParams globalLights)
{
    float3 c = saturate(diff);
    float3 fc = c;
    float naturalDiffuseFactor = vc0.r;                 // vertex colour R = natural-ambient bake
    float artificialDiffuseFactor = saturate(vc0.g);    // vertex colour G = artificial-ambient bake
    c *= BasicLighting(globalLights.LightDirColour, globalLights.LightDirAmbColour, lf);
    c += AmbientLight(fc, norm.z, globalLights.LightNaturalAmbUp, globalLights.LightNaturalAmbDown, naturalDiffuseFactor);
    c += AmbientLight(fc, norm.z, globalLights.LightArtificialAmbUp, globalLights.LightArtificialAmbDown, artificialDiffuseFactor);
    return c;
}
```

So the final forward combine is exactly:
```
lit = albedo * (LightDirAmbColour + LightDirColour * NdotL * shadow)
    + albedo * hemi(NaturalAmbUp/Down, n.z) * vertColour0.r
    + albedo * hemi(ArtificialAmbUp/Down, n.z) * saturate(vertColour0.g)
    + specular * shadow
[+ albedo again if IsEmissive]
```
Hemisphere blend: `bf = n.z*0.5+0.5`; up colour weighted `1-bf`, down colour weighted `bf`
(note this looks inverted — up colour applies when normal points DOWN — but it's what CW ships and
must be copied verbatim to match its look).

**Important quirk:** `AmbientLight` weights are up*(1-bf), down*bf, where bf=1 when normal points up.
Copy as-is to match CodeWalker output exactly.

### 3.3 Shadow sampling (only if you implement CW-style cascaded shadows)

`ShadowmapVars : register(b1)`, both stages:
```hlsl
cbuffer ShadowmapVars : register(b1)
{
    float4 CamScenePos;          // camera position in shadow-scene coords
    float4x4 CamSceneView;
    float4x4 LightView;
    float4 LightDir;
    float4 CascadeOffsets[16];
    float4 CascadeScales[16];
    float4 CascadeDepths[16];
    int CascadeCount;  int CascadeVisual;  int PCFLoopStart;  int PCFLoopEnd;
    float BorderPaddingMin;  float BorderPaddingMax;  float Bias;  float BlurBetweenCascades;
    float CascadeCountInv;  float TexelSize;  float TexelSizeX;  float ShadowMaxDistance; //~2000
};
```
VS: `float ShadowmapSceneDepth(float3 camRelPos, out float4 lspos)` = `scenePos = camRelPos + CamScenePos.xyz`,
`lspos = mul(scenePos, LightView)`, returns `mul(scenePos, CamSceneView).z`.
PS: `ShadowAmount(shadowcoord, shadowdepth)` selects a cascade by testing `coord*CascadeScales[i]+CascadeOffsets[i]`
against border padding, does a PCF loop with `Depthmap.SampleCmpLevelZero(DepthmapSS, uv, depth - Bias)`,
X coord remapped by `x = x*CascadeCountInv + CascadeCountInv*cascadeIndex` (cascades side by side in one texture),
with lerp between cascades inside the blur band. For a first pass you can stub `shadowlit = 1.0`.

### 3.4 How the CPU fills the global lights (Renderer.cs ~543–601)

From weather/timecycle values (`weather.CurrentValues.*`):
```csharp
lightdircolour        = lightDirCol;         lightdircolour  *= Math.Max(lightdircolour.Alpha, hdr?0f:0.5f);
lightdirambcolour     = lightDirAmbCol;      lightdirambcolour *= lightdirambcolour.Alpha * lightDirAmbIntensityMult;
lightnaturalupcolour  = lightNaturalAmbUp;   *= .Alpha * lightNaturalAmbUpIntensityMult;
lightnaturaldowncolour= lightNaturalAmbDown; *= .Alpha;
lightartificialup/down= lightArtificialExtUp/Down; *= .Alpha;
if (!hdr) all clamped: dir <= (1,1,1,1), ambients <= (0.5,...);
lightdir = sun (or moon when timeofday <5 or >21); if (lightdir.Z < 0) lightdir.Z = 0;
```
For a standalone editor a fixed daylight setup works, e.g. LightDir=normalize(0.3,-0.2,0.9),
LightDirColour≈(1,1,1), LightDirAmbColour≈(0.1..0.2), NaturalAmbUp/Down≈(0.3..0.5), Artificial=0.

---

## 4. C#-side cbuffer structs (BasicShader.cs — copy layouts exactly; all 16-byte aligned)

```csharp
public struct BasicShaderVSSceneVars   // b0 (VS)
{ public Matrix ViewProj; public Vector4 WindVector; }               // 80 bytes

public struct BasicShaderVSEntityVars  // b2 (VS)
{ public Vector4 CamRel; public Quaternion Orientation;
  public uint HasSkeleton; public uint HasTransforms; public uint TintPaletteIndex; public uint Pad1;
  public Vector3 Scale; public uint IsInstanced; }                   // 64 bytes

public struct BasicShaderVSModelVars   // b3 (VS)
{ public Matrix Transform; }

public struct BasicShaderVSGeomVars    // b4 (VS)
{ public uint EnableTint; public float TintYVal; public uint IsDecal; public uint EnableWind;
  public Vector4 WindOverrideParams; public Vector4 globalAnimUV0; public Vector4 globalAnimUV1; }  // 64 bytes

public struct BasicShaderPSSceneVars   // b0 (PS)
{ public ShaderGlobalLightParams GlobalLights;   // 112 bytes (Vector3+float + 6x Color4)
  public uint EnableShadows; public uint RenderMode; public uint RenderModeIndex; public uint RenderSamplerCoord; } // 128 total

public struct BasicShaderPSGeomVars    // b2 (PS)
{ public uint EnableTexture; public uint EnableTint; public uint EnableNormalMap; public uint EnableSpecMap;
  public uint EnableDetailMap; public uint IsDecal; public uint IsEmissive; public uint IsDistMap;
  public float bumpiness; public float AlphaScale; public float HardAlphaBlend; public float useTessellation;
  public Vector4 detailSettings;
  public Vector3 specMapIntMask; public float specularIntensityMult;
  public float specularFalloffMult; public float specularFresnel; public float wetnessMultiplier;   // note: 3 floats
  public uint SpecOnly;
  public Vector4 TextureAlphaMask; }   // 112 bytes total; matches HLSL packing exactly
```
Matrices are stored transposed on CPU (`Matrix.Transpose(camera.ViewProjMatrix)`) because HLSL uses
`mul(vector, matrix)` with default column-major packing.

Register map recap:
- VS: b0 scene, b1 shadowmap, b2 entity, b3 model, b4 geom, b5 inst-globals, b6 inst-locals, b7 bones, b8 cloth; t0 tint palette, t2 grass instances; s0 tint sampler (point).
- PS: b0 scene, b1 shadowmap, b2 geom; t0 diffuse, t1 shadow depth, t2 bump, t3 spec, t4 detail, t5 diffuse2, t6 tint palette; s0 tex sampler, s1 shadow comparison sampler.

### 4.1 Sampler states (BasicShader ctor)

- `texsampler` (PS s0 default): MinMagMipLinear, Wrap UVW, ComparisonFunction=Always, MaxAniso 1.
- `texsampleranis` (PS s0 if AnisotropicFilter): Filter.Anisotropic, MaxAniso 8, Wrap.
- `texsamplertnt` (VS s0, non-fragment): MinMagMipPoint, AddressU=**Clamp**, V/W=Wrap, border white.
- `texsamplertntyft` (VS s0 for fragments): MinMagMipPoint, all Wrap.

### 4.2 How the material params are fed (SetGeomVars)

Texture slot selection walks `geom.RenderableTextures` matched with `geom.TextureParamHashes`
(from drawable shader param hashes): `DiffuseSampler`→t0, `BumpSampler`→t2, `SpecSampler`→t3,
`DetailSampler`→t4, `DiffuseSampler2`/`DiffuseExtraSampler`→t5, `TintPaletteSampler`/`TextureSamplerDiffPal`→VS t0
(and PS t6 for weapon tint), `distanceMapSampler`→t0 + isdistmap flag. Textures named hash 1678728908
("blank") are skipped. Unknown samplers fall back to diffuse if diffuse is empty.

`tntpalind = (TintPaletteIndex + 0.5f) / tintpal.Key.Height;` → `TintYVal`.

Flags:
```csharp
PSGeomVars.Vars.EnableTexture = (usediff ? 1u : 0u) + (usediff2 ? 2u : 0u);
PSGeomVars.Vars.EnableNormalMap = usebump ? 1u : 0u;
PSGeomVars.Vars.EnableSpecMap = usespec ? 1u : 0u;
PSGeomVars.Vars.EnableDetailMap = usedetl ? 1u : 0u;
PSGeomVars.Vars.bumpiness = geom.bumpiness;                          // default 1.0
PSGeomVars.Vars.detailSettings = geom.detailSettings;                // default 0
PSGeomVars.Vars.specMapIntMask = geom.specMapIntMask;                // default (0,0,0)
PSGeomVars.Vars.specularIntensityMult = SpecularEnable ? geom.specularIntensityMult : 0.0f; // default 0
PSGeomVars.Vars.specularFalloffMult = geom.specularFalloffMult;      // default 0
PSGeomVars.Vars.specularFresnel = geom.specularFresnel;              // default 0
PSGeomVars.Vars.AlphaScale = isdistmap ? 1.0f : AlphaScale;          // AlphaScale=1 normally
```
These `geom.*` values come from the drawable's shader parameter list in `Renderable.cs Init()`:
`ShaderParamNames.bumpiness` → `((Vector4)param.Data).X`, `.specMapIntMask` → `.XYZ()`,
`.specularIntensityMult/.specularFalloffMult/.specularFresnel/.wetnessMultiplier` → `.X`,
`.detailSettings` → full Vector4. Defaults (when param absent): bumpiness=1, specMapIntMask=(0,0,0)
— note dot(spec, 0)=0, so spec dies unless the drawable provides the mask (commonly (1,0,0)).

Shader-file-hash special cases (SetGeomVars): trees tint shaders (2245870123, 3334613197, 1229591973)
set `tintflag=2` (use Colour1 for tint U); weapon palette shaders (231364109, 3294641629, 731050667)
set VS tintflag=0 / PS EnableTint=2; decal_normal_only/mirror_decal/reflect_decal → IsDecal=3;
decal_spec_only/spec_decal → IsDecal=4; decal_dirt (2655725442) → IsDecal=2 + TextureAlphaMask=geom.DirtDecalMask.
`IsEmissive` is set in Renderable.Init() for the emissive shader-name-hash list (2635608835 emissive.sps,
443538781 emissive_clip, 140448747 emissivestrong, 1193295596 emissive_tnt, glass_emissive 3924045432, etc.).
`EnableWind` likewise for trees/um shaders.

`DecalMode` (renderer-wide bool for the alpha pass) drives IsDecal=1 default; VSEntityVars set per
instance: CamRel = entityPos - camPos, Orientation = entity quat, Scale, HasTransforms (drawable model
transforms), TintPaletteIndex.

---

## 5. Minimal forward one-pass port checklist

1. VS: input P/N/C/T(+X); cbuffers b0 (ViewProj transposed) + b2-style entity vars (CamRel, quat
   Orientation, Scale). Output: SV_Position, camera-relative pos, world normal, tangent
   (`float4(worldTang, tang.w)`), bitangent `cross(T,N)*tang.w`, Texcoord0, Colour0, Tint.
   Use `mulvq` for orientation; position = `CamRel.xyz + mulvq(pos*Scale, Orientation)`.
2. PS: sample diffuse; alpha test `a <= 0.33 discard; a = 1` (opaque) / decal path; tint multiply;
   NormalMap() exactly as section 2.3 when bump bound; spec intensity = `dot(float3(s.x*s.x, s.y*s.y, s.z), specMapIntMask) * specularIntensityMult`;
   sun spec = `LightDirColour.rgb * 0.00006 * (exp(10*saturate(dot(normalize(reflect(normalize(camRelPos), n)), LightDir)))-1) * s.x*s.x_note * specularIntensityMult`
   (in CW code `sv.x` used there is the already-squared value);
   combine with GlobalLighting (section 3.2) using vertex Colour0.r/.g as ambient bake factors; add
   albedo once more if emissive; shadows optional (stub shadowlit=1).
3. Blend state: opaque geoms alpha-tested; decal pass uses standard alpha blending with `DecalMode=true`.
4. Keep constants: 0.33 alpha ref, bumpiness min 0.001, spec exp(x*10)-1 with 0.00006 scale, hemisphere
   `bf = n.z*0.5+0.5` with up*(1-bf)+down*bf.
