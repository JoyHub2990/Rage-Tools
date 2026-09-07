cbuffer GrassBatchVars : register(b2)
{
    float3 AabbMin;      float LodDist;
    float3 AabbDelta;    float LodFadeStart;
    float3 ScaleRange;   float LodInstFadeRange;
    float3 GrassCamPos;  float OrientToTerrain;
    float GrassAlphaScale;
    float GrassLegacy;
    float GrassFadeRange;
    float GrassFadePower;
    float4 InstRot[24];
}

StructuredBuffer<uint4> GrassInstances : register(t2);

struct GrassPS_Input
{
    float4 Pos : SV_POSITION;
    float3 WorldPos : TEXCOORD0;
    float3 Normal : NORMAL;
    float4 Colour : COLOR0;
    float2 UV0 : TEXCOORD1;
    float2 Ao : TEXCOORD2;
};

GrassPS_Input VSGrass(VS_Input input, uint iid : SV_InstanceID)
{
    GrassPS_Input o;
    uint4 u1 = GrassInstances[iid];

    float3 ipos = float3(u1.x & 0xFFFF, u1.x >> 16, u1.y & 0xFFFF) * (AabbDelta / 65535.0) + AabbMin;

    float2 nxy = float2((u1.y >> 16) & 255, u1.y >> 24) * 0.0078431373 - 1.0;
    float3 tn = float3(nxy, sqrt(saturate(1.0 - dot(nxy, nxy))));

    float d = distance(ipos, GrassCamPos);
    float fade = GrassLodFade_U5(ipos, iid, d, LodFadeStart, LodDist, GrassFadeRange, GrassFadePower);

    float s = ((u1.z >> 24) * 0.0039215686) * (ScaleRange.y - ScaleRange.x) + ScaleRange.x;
    uint ri = (iid & 7) * 3;
    s += (InstRot[ri].w + InstRot[ri].w) * (ScaleRange.z * ScaleRange.y) - ScaleRange.y * ScaleRange.z;
    s = max(s, 0.0);

    if (GrassLegacy < 0.5) s *= fade;

    float3 p = input.Position * s;
    float3 n = input.Normal;

    p = p.x * InstRot[ri].xyz + p.y * InstRot[ri + 1].xyz + p.z * InstRot[ri + 2].xyz;
    n = n.x * InstRot[ri].xyz + n.y * InstRot[ri + 1].xyz + n.z * InstRot[ri + 2].xyz;

    float3 axis = cross(float3(0, 0, 1), tn);
    float sa = length(axis);
    if (sa > 1e-4 && OrientToTerrain > 0.0)
    {
        axis /= sa;
        float ang = atan2(sa, tn.z) * saturate(OrientToTerrain);
        float cs, sn;
        sincos(ang, sn, cs);
        p = p * cs + cross(axis, p) * sn + axis * dot(axis, p) * (1.0 - cs);
        n = n * cs + cross(axis, n) * sn + axis * dot(axis, n) * (1.0 - cs);
    }
    float3 wpos = ipos + p;

    float3 col = float3(u1.z & 0xFF, (u1.z >> 8) & 0xFF, (u1.z >> 16) & 0xFF) * 0.0039215686;
    float ao = (u1.w & 0xFF) * 0.0039215686;

    o.Pos = mul(float4(wpos, 1.0), ViewProj);
    o.WorldPos = wpos;

    o.Normal = GrassLegacy > 0.5 ? normalize(lerp(float3(0, 0, 1), tn, 0.7)) : normalize(n);

    o.Colour = float4(col, GrassLegacy > 0.5 ? fade * input.Colour.a : input.Colour1.r);
    o.UV0 = input.UV0;

    o.Ao = float2(ao, GrassLegacy > 0.5 ? fade * input.Colour.a : input.Colour.a);
    return o;
}

float4 PSGrass(GrassPS_Input input) : SV_TARGET
{

    float4 c = DiffuseTex.Sample(LinearSampler, input.UV0);

    float alpha;
    float3 tinted;
    if (GrassLegacy > 0.5)
    {

        alpha = saturate(c.a * GrassAlphaScale) * input.Colour.a;
        tinted = c.rgb * input.Colour.rgb;
    }
    else
    {

        float aRef = 0.33;
        alpha = saturate((c.a - aRef) / max(fwidth(c.a), 1e-5) + 0.5) * input.Ao.y;
        tinted = lerp(c.rgb, c.rgb * input.Colour.rgb, saturate(input.Colour.a));
    }
    clip(alpha - 0.004);
    c.rgb = tinted;

    if (RenderMode == 1) return float4(c.rgb, 1.0);
    if (RenderMode == 2) return float4(input.Normal * 0.5 + 0.5, 1.0);
    if (RenderMode == 7) return float4(alpha, alpha, alpha, 1.0);

    float3 norm = normalize(input.Normal);
    float3 lit;
    if (UseTimecycle)
    {

        if (GrassLegacy < 0.5 && dot(norm, CameraPos.xyz - input.WorldPos) < 0.0) norm = -norm;
        float lf = saturate(dot(norm, GlobalLightDir)) * SunShadowFactor(input.WorldPos, norm);

        lit = GlobalLighting(c.rgb, norm, float4(input.Ao.x, 0.0, 0.0, 1.0), lf, input.WorldPos - CameraPos.xyz);
    }
    else lit = c.rgb * AmbientColour.rgb;

    if (FogDensity > 0.0)
    {
        float dist = max(length(input.WorldPos - CameraPos.xyz) - FogStart, 0.0);
        float f = saturate(1.0 - exp(-dist * FogDensity));
        lit = lerp(lit, FogColour, f);
    }
    return float4(lit, alpha);
}
