cbuffer ParticleVars : register(b0)
{
    float4x4 ViewProj;
    float4 CameraPos;
    float4 Flags;
}

Texture2D SpriteTex : register(t0);
SamplerState LinearSampler : register(s0);

struct VS_Input
{
    float3 Centre : POSITION;
    float2 Corner : TEXCOORD0;
    float4 UvRect : TEXCOORD3;
    float4 UvRect2 : TEXCOORD4;
    float2 Anim : TEXCOORD5;
    float4 Colour : COLOR0;
    float2 Size : TEXCOORD1;
    float Rotation : TEXCOORD2;
};

struct PS_Input
{
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
    float2 UV2 : TEXCOORD1;
    float Blend : TEXCOORD2;
    float4 Colour : COLOR0;
};

PS_Input VSMain(VS_Input input)
{
    PS_Input o;
    float3 toCam = normalize(CameraPos.xyz - input.Centre);
    float3 upref = abs(toCam.z) > 0.99 ? float3(0, 1, 0) : float3(0, 0, 1);
    float3 right = normalize(cross(upref, toCam));
    float3 up = cross(toCam, right);

    float s = sin(input.Rotation);
    float c = cos(input.Rotation);
    float2 rc = float2(input.Corner.x * c - input.Corner.y * s,
                       input.Corner.x * s + input.Corner.y * c);

    float3 wpos = input.Centre + (right * rc.x * input.Size.x * 0.5 + up * rc.y * input.Size.y * 0.5);
    o.Pos = mul(float4(wpos, 1.0), ViewProj);
    float2 baseUV = input.Corner * 0.5 + 0.5;
    baseUV.y = 1.0 - baseUV.y;

    o.UV = input.UvRect.xy + baseUV * input.UvRect.zw;
    o.UV2 = input.UvRect2.xy + baseUV * input.UvRect2.zw;
    o.Blend = input.Anim.x;

    float3 lin = Flags.x > 0.5 ? input.Colour.rgb : input.Colour.rgb * input.Colour.rgb;
    o.Colour = float4(lin + lin * input.Anim.y, input.Colour.a);
    return o;
}

float4 PSMain(PS_Input input) : SV_TARGET
{

    float4 t1 = SpriteTex.Sample(LinearSampler, input.UV);
    float4 t2 = SpriteTex.Sample(LinearSampler, input.UV2);
    float4 tex = lerp(t1, t2, input.Blend);

    if (Flags.x < 0.5) tex.rgb *= tex.rgb;

    float4 col = tex * input.Colour;
    if (col.a < 0.003) discard;
    return col;
}
