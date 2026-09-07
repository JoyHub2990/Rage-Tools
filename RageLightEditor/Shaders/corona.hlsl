cbuffer CoronaVars : register(b0)
{
    float4x4 ViewProj;
    float4 CameraPos;
    float4 Params;
}

Texture2D CoronaTex : register(t0);
SamplerState TextureSS : register(s0);

struct VS_Input
{
    float3 Centre : POSITION;
    float2 Corner : TEXCOORD0;
    float4 Colour : COLOR0;
    float Size : TEXCOORD1;
};

struct PS_Input
{
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
    float4 Colour : COLOR0;
};

PS_Input VSMain(VS_Input input)
{
    PS_Input o;
    float3 toCam = normalize(CameraPos.xyz - input.Centre);
    float3 upref = abs(toCam.z) > 0.99 ? float3(0, 1, 0) : float3(0, 0, 1);
    float3 right = normalize(cross(upref, toCam));
    float3 up = cross(toCam, right);
    float3 wpos = input.Centre + (right * input.Corner.x + up * input.Corner.y) * input.Size;
    o.Pos = mul(float4(wpos, 1.0), ViewProj);
    o.UV = input.Corner;
    o.Colour = input.Colour;
    return o;
}

float4 PSMain(PS_Input input) : SV_TARGET
{
    if (Params.x > 0.5)
    {
        float4 t = CoronaTex.Sample(TextureSS, input.UV * 0.5 + 0.5);
        float a = t.a * saturate(input.Colour.a);
        return float4(t.rgb * input.Colour.rgb * a, a);
    }
    float r = length(input.UV);
    if (r > 1.0) discard;

    float core = saturate(1.0 - r * 3.0);
    float glow = pow(saturate(1.0 - r), 2.5) * 0.35;
    float a2 = saturate(core + glow);
    return float4(input.Colour.rgb * a2, a2);
}
