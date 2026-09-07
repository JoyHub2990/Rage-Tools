cbuffer LineVars : register(b0)
{
    float4x4 ViewProj;

    float4 CamPull;

    float4 PullClamp;
}

struct VS_Input
{
    float3 Position : POSITION;
    float4 Colour : COLOR0;
};

struct PS_Input
{
    float4 Pos : SV_POSITION;
    float4 Colour : COLOR0;
};

PS_Input VSMain(VS_Input input)
{
    PS_Input o;
    float3 p = input.Position;
    if (CamPull.w > 0.0)
    {
        float3 toCam = CamPull.xyz - p;
        float d = length(toCam);
        float pull = d * CamPull.w;
        if (PullClamp.x > 0.0) pull = min(pull, PullClamp.x);
        if (d > 1e-5) p += toCam * (pull / d);
    }
    o.Pos = mul(float4(p, 1.0), ViewProj);
    o.Colour = input.Colour;
    return o;
}

float4 PSMain(PS_Input input) : SV_TARGET
{
    return input.Colour;
}
