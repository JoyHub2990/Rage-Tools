cbuffer DistVars : register(b0)
{
    float4x4 ViewProj;
    float4 CameraPos;
    float4 Params;
}

Texture2D LightTex : register(t0);
SamplerState LinearSS : register(s0);

struct VS_Input
{
    float3 Centre : POSITION;
    float2 Corner : TEXCOORD0;
    uint Colour : COLOR0;
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
    float4 rgbi = float4((input.Colour >> 16) & 0xff, (input.Colour >> 8) & 0xff,
                         input.Colour & 0xff, (input.Colour >> 24) & 0xff) / 255.0;

    float3 rel = input.Centre - CameraPos.xyz;
    float dist = max(length(rel), 0.001);
    float size = min(rgbi.a * min(dist, 50.0) * 0.1, 3.0);
    size = max(size, dist * Params.y * 1.4);

    float3 toCam = -rel / dist;
    float3 upref = abs(toCam.z) > 0.99 ? float3(0, 1, 0) : float3(0, 0, 1);
    float3 right = normalize(cross(upref, toCam));
    float3 up = cross(toCam, right);
    float3 wpos = input.Centre + (right * input.Corner.x + up * input.Corner.y) * size;

    o.Pos = mul(float4(wpos, 1.0), ViewProj);
    o.UV = input.Corner * 0.5 + 0.5;
    float nearFade = saturate((dist - 40.0) / 80.0);
    o.Colour = float4(rgbi.rgb, 0.25 * CameraPos.w * nearFade);
    return o;
}

float4 PSMain(PS_Input input) : SV_TARGET
{
    float4 t;
    if (Params.x > 0.5)
    {
        t = LightTex.Sample(LinearSS, input.UV);
    }
    else
    {
        float r = length(input.UV * 2.0 - 1.0);
        float a = pow(saturate(1.0 - r), 2.0);
        t = float4(a, a, a, a);
    }
    float3 c = t.rgb * input.Colour.rgb * t.a * input.Colour.a;
    return float4(c, t.a * input.Colour.a);
}
