cbuffer ShaftVars : register(b0)
{
    float4x4 ViewProj;
    float4 CameraPos;
    float4 CameraFwd;
    float4 ProjParams;
    float4 Params;
}

Texture2D<float> SceneDepthTex : register(t25);
Texture2DMS<float> SceneDepthMS : register(t26);

struct VS_Input
{
    float3 Pos    : POSITION;
    float4 Centre : TEXCOORD0;
    float4 AxisX  : TEXCOORD1;
    float4 AxisY  : TEXCOORD2;
    float4 AxisD  : TEXCOORD3;
    float4 Colour : COLOR0;
};

struct PS_Input
{
    float4 Pos    : SV_POSITION;
    float3 World  : TEXCOORD0;
    float4 Centre : TEXCOORD1;
    float4 AxisX  : TEXCOORD2;
    float4 AxisY  : TEXCOORD3;
    float4 AxisD  : TEXCOORD4;
    float4 Colour : COLOR0;
};

PS_Input VSMain(VS_Input i)
{
    PS_Input o;
    o.Pos = mul(float4(i.Pos, 1.0), ViewProj);
    o.World = i.Pos;
    o.Centre = i.Centre; o.AxisX = i.AxisX; o.AxisY = i.AxisY; o.AxisD = i.AxisD; o.Colour = i.Colour;
    return o;
}

float SceneViewDistance(float2 screenPos)
{
    float z;
    if (CameraFwd.w > 1.5)      z = SceneDepthMS.Load(int2(screenPos), 0);
    else if (CameraFwd.w > 0.5) z = SceneDepthTex.Load(int3(int2(screenPos), 0));
    else return 1e6;
    if (z <= 0.0) return 1e6;
    return ProjParams.y / (z + ProjParams.x);
}

float Hash3(float3 p)
{
    p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float Noise3(float3 x)
{
    float3 i = floor(x); float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(Hash3(i + float3(0,0,0)), Hash3(i + float3(1,0,0)), f.x),
                     lerp(Hash3(i + float3(0,1,0)), Hash3(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(Hash3(i + float3(0,0,1)), Hash3(i + float3(1,0,1)), f.x),
                     lerp(Hash3(i + float3(0,1,1)), Hash3(i + float3(1,1,1)), f.x), f.y), f.z);
}

float4 PSMain(PS_Input i) : SV_TARGET
{
    float3 eye = CameraPos.xyz;
    float3 ray = i.World - eye;
    float rayLen = length(ray);
    float3 rd = ray / max(rayLen, 1e-4);

    float3 X = i.AxisX.xyz, Y = i.AxisY.xyz, D = i.AxisD.xyz;
    float det = dot(X, cross(Y, D));
    if (abs(det) < 1e-9) discard;
    float3 dX = cross(Y, D) / det, dY = cross(D, X) / det, dD = cross(X, Y) / det;
    float3 o = eye - i.Centre.xyz;
    float3 lo = float3(dot(o, dX), dot(o, dY), dot(o, dD));
    float3 ld = float3(dot(rd, dX), dot(rd, dY), dot(rd, dD));

    float3 bmin = float3(-1, -1, 0), bmax = float3(1, 1, 1);
    float3 inv = 1.0 / (abs(ld) > 1e-6 ? ld : (ld >= 0 ? 1e-6 : -1e-6));
    float3 ta = (bmin - lo) * inv, tb = (bmax - lo) * inv;
    float3 tn = min(ta, tb), tf = max(ta, tb);
    float s0 = max(max(tn.x, tn.y), tn.z);
    float s1 = min(min(tf.x, tf.y), tf.z);
    s0 = max(s0, 0.0);

    float2 px = i.Pos.xy;
    float viewDist = SceneViewDistance(px);
    float sceneAlong = viewDist / max(dot(rd, CameraFwd.xyz), 0.05);
    s1 = min(s1, sceneAlong);
    if (s1 <= s0 + 1e-4) discard;

    float soft = saturate(i.Centre.w);
    float spread = max(i.AxisD.w, 1.0);
    float k = max(i.Colour.a, 0.5);
    float amountNoise = Params.x;
    int steps = (int)clamp(Params.y, 4, 32);
    float seg = (s1 - s0);
    float dt = seg / steps;
    float3 nDrift = -normalize(D) * (CameraPos.w * 0.12);
    float3 nDrift2 = float3(0.03, 0.02, -0.05) * CameraPos.w;

    float3 acc = 0;

    float jit = frac(sin(dot(px, float2(12.9898, 78.233))) * 43758.5453);
    float s = s0 + dt * (0.5 + (jit - 0.5) * 0.9);
    [loop]
    for (int n = 0; n < steps; n++)
    {
        float3 p = lo + ld * s;
        float t = saturate(p.z);

        float sw = lerp(1.0, spread, t);
        float2 uv = p.xy / sw;

        float along = pow(saturate(1.0 - t), k);

        float edge = 1.0 - max(abs(uv.x), abs(uv.y));
        float halfW = max(min(length(X), length(Y)), 0.01);
        float fw = clamp(lerp(0.02, 0.08, soft) / halfW, 0.1, 0.5);
        float feather = smoothstep(0.0, fw, edge);
        float core = 1.0 + 0.25 * saturate(1.0 - dot(uv, uv));
        float dens = along * feather * core;
        if (amountNoise > 0.001)
        {
            float3 wp = eye + rd * s;
            float n1 = Noise3(wp * 1.7 + nDrift);
            float n2 = Noise3(wp * 4.3 + nDrift2 + nDrift * 1.7);
            float nz = (n1 * 0.65 + n2 * 0.35);
            dens *= lerp(1.0, nz * 2.0, amountNoise);
        }
        acc += dens * dt;
        s += dt;
    }
    if (Params.w > 0.5) acc = seg;
    float3 rad = i.Colour.rgb * acc * Params.z;
    return float4(rad, 1.0);
}
