cbuffer ShadowPassVars : register(b0)
{
    float4x4 LightViewProj;
    float4 LightPos;
}

cbuffer ShadowObjVars : register(b1)
{
    float4x4 World;
}

struct VS_Input
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float4 Colour : COLOR0;
    float2 UV0 : TEXCOORD0;
    float2 UV1 : TEXCOORD1;
};

struct PS_Input
{
    float4 Pos : SV_POSITION;
    float3 WorldPos : TEXCOORD0;
};

PS_Input VSMain(VS_Input input)
{
    PS_Input o;
    float4 wpos = mul(float4(input.Position, 1.0), World);
    o.WorldPos = wpos.xyz;
    o.Pos = mul(wpos, LightViewProj);
    return o;
}

float PSMain(PS_Input input) : SV_TARGET
{
    return length(input.WorldPos - LightPos.xyz);
}
