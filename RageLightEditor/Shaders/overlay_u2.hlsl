cbuffer LineVars : register(b0)
{
    float4x4 ViewProj;

    float4 CamPull;

    float4 PullClamp;
}

cbuffer InkVars : register(b1)
{

    float4 Screen;

    float4 Ink;
}

Texture2D<float> SceneDepth : register(t0);
Texture2D Backdrop : register(t1);

static const float3 Luma3 = float3(0.2126, 0.7152, 0.0722);

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

float3 Ink_U2(float3 want, float bgl, float gap)
{

    float target = (bgl < 0.5) ? min(1.0, bgl + gap) : max(0.0, bgl - gap);

    float wl = max(dot(want, Luma3), 1e-3);
    float3 ink = want * (target / wl);

    ink /= max(max(max(ink.r, ink.g), ink.b), 1.0);
    float il = dot(ink, Luma3);
    float3 pole = (bgl < 0.5) ? float3(1.0, 1.0, 1.0) : float3(0.0, 0.0, 0.0);
    float miss = saturate(((bgl < 0.5) ? (target - il) : (il - target)) / max(gap, 1e-3));
    return lerp(ink, pole, 0.85 * miss);
}

float4 PSMain(PS_Input input) : SV_TARGET
{
    int3 px = int3((int)input.Pos.x, (int)input.Pos.y, 0);

    if (Screen.x > 0.5 && input.Pos.z < SceneDepth.Load(px)) discard;

    float3 want = input.Colour.rgb;
    float3 bg = Backdrop.Load(px).rgb;
    float3 ink = lerp(want, Ink_U2(want, dot(bg, Luma3), Ink.x), Ink.y);
    return float4(ink, input.Colour.a);
}
