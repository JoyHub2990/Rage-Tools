cbuffer CineVars : register(b0)
{
    uint CinePass;
    float AoRadius;
    float AoStrength;
    float BloomThreshold;
    float4 TexelSize;
    float4 CamPos;
    float4x4 InvViewProj;
    float4x4 ViewProj;
    float4x4 PrevViewProj;

    float AoQuality;
    float SsrThickness;
    float SsrMaxDistance;
    float BloomSpread;
    float BloomAnamorphic;
    float DofFocus;
    float DofAperture;
    float CinePad2;

    float SsrSky;
    float SsrFresnel;
    float SsrBlur;
    float DofRange;

    float DofMaxRadius;
    float DofBokehBoost;
    float DofBlades;
    float CinePad3;

    float4 SsrSkyColour;

    float DofStretch;
    float DofRadial;
    float MotionBlur;
    float CinePad4;

    float4 MotionDelta;
}

Texture2D SrcTex : register(t0);
Texture2D DepthTex : register(t1);
Texture2DMS<float> DepthMsTex : register(t2);
SamplerState LinearClampSampler : register(s0);
SamplerState PointClampSampler : register(s1);

struct PS_Input
{
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
};

PS_Input VSMain(uint id : SV_VertexID)
{
    PS_Input o;
    o.UV = float2((id << 1) & 2, id & 2);
    o.Pos = float4(o.UV * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

float RawDepth(float2 uv) { return DepthTex.SampleLevel(PointClampSampler, uv, 0).r; }

bool IsSky(float2 uv) { return RawDepth(uv) <= 1e-7; }

float3 WorldFromDepth(float2 uv)
{
    float4 clip = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, RawDepth(uv), 1.0);
    float4 w = mul(clip, InvViewProj);
    return w.xyz / max(w.w, 1e-6);
}

void SurfaceAt(float2 uv, out float3 P, out float3 N)
{
    P = WorldFromDepth(uv);
    N = normalize(cross(ddx(P), ddy(P)));
    if (dot(N, CamPos.xyz - P) < 0.0) N = -N;
}

float Hash(float2 uv)
{
    float2 px = uv / max(TexelSize.xy, 1e-6);
    return frac(sin(dot(px, float2(12.9898, 78.233))) * 43758.5453);
}

float Occlusion(float2 uv)
{
    if (IsSky(uv)) return 1.0;

    int RAYS = (int)(6.0 + AoQuality * 18.0);
    int STEPS = (int)(4.0 + AoQuality * 8.0);

    float3 P, N;
    SurfaceAt(uv, P, N);

    float3 up = abs(N.z) < 0.9 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 t = normalize(cross(up, N));
    float3 b = cross(N, t);

    float ang0 = Hash(uv) * 6.2831853;
    float occl = 0.0;

    [loop]
    for (int r = 0; r < RAYS; r++)
    {
        float a = ang0 + (r * 6.2831853 / RAYS);
        float3 dir = normalize(t * (cos(a) * 0.75) + b * (sin(a) * 0.75) + N * 0.66);

        [loop]
        for (int st = 1; st <= STEPS; st++)
        {
            float march = AoRadius * (float)(st * st) / (float)(STEPS * STEPS);
            float3 sp = P + dir * march;

            float4 cp = mul(float4(sp, 1.0), ViewProj);
            if (cp.w <= 1e-4) break;
            float2 suv = (cp.xy / cp.w) * float2(0.5, -0.5) + 0.5;
            if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0) break;
            if (IsSky(suv)) continue;

            float3 sceneP = WorldFromDepth(suv);
            float dSample = distance(CamPos.xyz, sp);
            float dScene = distance(CamPos.xyz, sceneP);
            float bias = 0.02 + dSample * 0.004;

            if (dScene < dSample - bias)
            {

                float gap = dSample - dScene;
                occl += saturate(1.0 - gap / (AoRadius * 2.0));
                break;
            }
        }
    }

    float o = saturate((occl / RAYS) * AoStrength);
    return 1.0 - smoothstep(0.0, 1.0, o);
}

float BlurAO(float2 uv, float2 dir)
{
    const int R = 6;
    float centreDepth = RawDepth(uv);
    float sum = SrcTex.SampleLevel(PointClampSampler, uv, 0).r;
    float wsum = 1.0;

    [unroll]
    for (int i = 1; i <= R; i++)
    {
        [unroll]
        for (int side = -1; side <= 1; side += 2)
        {
            float2 o = dir * TexelSize.xy * (float)(i * side);
            float2 suv = saturate(uv + o);

            float wg = exp(-(float)(i * i) / (2.0 * 3.0 * 3.0));

            float dd = abs(RawDepth(suv) - centreDepth);
            float wd = exp(-dd * dd * 40000.0);
            float w = wg * wd;
            sum += SrcTex.SampleLevel(PointClampSampler, suv, 0).r * w;
            wsum += w;
        }
    }
    return sum / max(wsum, 1e-5);
}

float3 BrightPass(float2 uv)
{
    float3 c = 0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float2 o = float2((i & 1) ? 0.5 : -0.5, (i & 2) ? 0.5 : -0.5) * TexelSize.zw * 2.0;
        c += SrcTex.SampleLevel(LinearClampSampler, saturate(uv + o), 0).rgb;
    }
    c *= 0.25;

    float lum = max(max(c.r, c.g), c.b);
    float knee = BloomThreshold * 0.6;
    float soft = clamp(lum - BloomThreshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee + 1e-5);
    float contrib = max(soft, lum - BloomThreshold) / max(lum, 1e-5);
    return c * contrib;
}

float3 BlurBloom(float2 uv, float2 dir)
{
    const int R = 13;
    float axis = dir.x > 0.5 ? (1.0 + BloomAnamorphic * 2.5) : (1.0 - BloomAnamorphic * 0.5);

    float spread = max(BloomSpread, 0.05) * axis;
    float3 sum = 0;
    float wsum = 0;
    [unroll]
    for (int i = -R; i <= R; i++)
    {
        float x = (float)i;
        float w = exp(-(x * x) / (2.0 * 6.5 * 6.5));
        float2 o = dir * TexelSize.xy * (x * spread);
        sum += SrcTex.SampleLevel(LinearClampSampler, saturate(uv + o), 0).rgb * w;
        wsum += w;
    }
    return sum / max(wsum, 1e-5);
}

float4 Reflection(float2 uv)
{
    if (IsSky(uv)) return 0;

    float3 P, N;
    SurfaceAt(uv, P, N);

    float3 V = normalize(P - CamPos.xyz);
    float3 R = reflect(V, N);

    if (dot(R, -V) > 0.92) return 0;

    float ndv = saturate(dot(N, -V));

    float fres = lerp(1.0, 0.06 + 0.94 * pow(1.0 - ndv, 2.2), saturate(SsrFresnel));

    float fresReal = 0.04 + 0.96 * pow(1.0 - ndv, 5.0);

    const int STEPS = 28;
    float jitter = Hash(uv);
    float travelled = 0.0;
    float step = SsrMaxDistance / STEPS;

    float4 miss = float4(SsrSkyColour.rgb, fresReal * saturate(SsrSky));

    [loop]
    for (int i = 1; i <= STEPS; i++)
    {

        travelled += step * (1.0 + i * 0.06);
        float3 sp = P + R * (travelled + jitter * step);

        float4 cp = mul(float4(sp, 1.0), ViewProj);
        if (cp.w <= 1e-4) return miss;
        float2 suv = (cp.xy / cp.w) * float2(0.5, -0.5) + 0.5;
        if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0) return miss;
        if (IsSky(suv)) return miss;

        float dRay = distance(CamPos.xyz, sp);
        float dScene = distance(CamPos.xyz, WorldFromDepth(suv));
        float behind = dRay - dScene;

        if (behind <= 0.0) continue;
        if (behind > SsrThickness + travelled * 0.05) continue;

        float2 e = abs(suv - 0.5) * 2.0;
        float edge = saturate((1.0 - max(e.x, e.y)) * 6.0);
        float range = saturate(1.0 - travelled / SsrMaxDistance);

        float3 hit = SrcTex.SampleLevel(LinearClampSampler, suv, 0).rgb;

        float conf = edge * range * range;
        return float4(lerp(miss.rgb, hit, conf), lerp(miss.a, fres, conf));
    }
    return miss;
}

float4 BlurSsr(float2 uv, float2 dir)
{
    const int R = 5;

    float spread = 0.6 + SsrBlur * 3.4;
    float4 sum = 0;
    float wsum = 0;
    [unroll]
    for (int i = -R; i <= R; i++)
    {
        float x = (float)i;
        float w = exp(-(x * x) / (2.0 * 2.4 * 2.4));
        sum += SrcTex.SampleLevel(LinearClampSampler,
            saturate(uv + dir * TexelSize.xy * (x * spread)), 0) * w;
        wsum += w;
    }
    return sum / max(wsum, 1e-5);
}

float CircleOfConfusion(float2 uv)
{
    if (IsSky(uv)) return 1.0;
    float d = distance(CamPos.xyz, WorldFromDepth(uv));

    float off = max(abs(d - DofFocus) - DofRange, 0.0);
    return saturate(off / max(d, 0.25) * DofAperture);
}

float4 DofPrepare(float2 uv)
{
    float3 c = 0;
    float coc = 0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float2 o = float2((i & 1) ? 0.5 : -0.5, (i & 2) ? 0.5 : -0.5) * TexelSize.zw;
        float2 suv = saturate(uv + o);
        c += SrcTex.SampleLevel(LinearClampSampler, suv, 0).rgb;
        coc = max(coc, CircleOfConfusion(suv));
    }
    return float4(c * 0.25, coc);
}

float4 BokehGather(float2 uv)
{
    float4 centre = SrcTex.SampleLevel(LinearClampSampler, uv, 0);
    float radius = centre.a * max(DofMaxRadius, 1.0);
    if (radius < 0.4) return centre;

    const int TAPS = 40;
    const float GOLDEN = 2.39996323;

    float3 sum = centre.rgb * (1.0 + DofBokehBoost * dot(centre.rgb, float3(0.299, 0.587, 0.114)));
    float wsum = 1.0 + DofBokehBoost * dot(centre.rgb, float3(0.299, 0.587, 0.114));
    float cocSum = centre.a;
    float jitter = Hash(uv) * 6.2831853;

    [loop]
    for (int i = 1; i <= TAPS; i++)
    {
        float t = (float)i / TAPS;
        float ang = i * GOLDEN + jitter;

        float r = sqrt(t) * radius;

        if (DofBlades >= 3.0)
        {
            float n = DofBlades;
            float seg = 6.2831853 / n;
            float half = seg * 0.5;
            r *= cos(half) / cos(fmod(abs(ang), seg) - half);
        }

        float2 dir2 = float2(cos(ang), sin(ang));

        dir2.x *= max(DofStretch, 0.05);

        if (DofRadial > 0.001)
        {
            float2 fromC = uv - 0.5;
            float rad = length(fromC);
            if (rad > 1e-4)
            {
                float2 tangent = float2(-fromC.y, fromC.x) / rad;
                float2 outward = fromC / rad;

                float along = dot(dir2, outward);
                dir2 = normalize(lerp(dir2, tangent * sign(along) + outward * 0.35,
                                      saturate(DofRadial) * saturate(rad * 2.0)));
            }
        }

        float2 o = dir2 * r * TexelSize.xy;
        float4 s = SrcTex.SampleLevel(LinearClampSampler, saturate(uv + o), 0);

        float w = saturate(s.a / max(centre.a, 1e-3));
        w *= 1.0 + DofBokehBoost * dot(s.rgb, float3(0.299, 0.587, 0.114));

        sum += s.rgb * w;
        wsum += w;
        cocSum += s.a;
    }
    return float4(sum / max(wsum, 1e-5), cocSum / (TAPS + 1));
}

float4 CleanDof(float2 uv)
{
    float4 sum = 0;
    [unroll]
    for (int y = -1; y <= 1; y++)
        [unroll]
        for (int x = -1; x <= 1; x++)
            sum += SrcTex.SampleLevel(LinearClampSampler,
                saturate(uv + float2(x, y) * TexelSize.xy), 0);
    return sum / 9.0;
}

float4 MotionBlurPass(float2 uv)
{
    float4 here = SrcTex.SampleLevel(LinearClampSampler, uv, 0);
    if (MotionBlur <= 0.001 || IsSky(uv)) return here;

    float3 P = WorldFromDepth(uv);
    float4 prev = mul(float4(P, 1.0), PrevViewProj);
    if (prev.w <= 1e-4) return here;
    float2 prevUv = (prev.xy / prev.w) * float2(0.5, -0.5) + 0.5;

    float2 vel = (uv - prevUv) * MotionBlur;

    float speed = length(vel);
    if (speed < TexelSize.x) return here;
    vel *= min(1.0, 0.25 / speed);

    const int TAPS = 12;
    float3 sum = here.rgb;
    float wsum = 1.0;
    float jitter = Hash(uv);
    [unroll]
    for (int i = 1; i <= TAPS; i++)
    {
        float t = (i - 0.5 + jitter) / TAPS;
        float2 suv = uv - vel * t;

        float3 sp = WorldFromDepth(saturate(suv));
        float dHere = distance(CamPos.xyz, P);
        float dThere = distance(CamPos.xyz, sp);
        float w = (dThere > dHere - 0.5) ? 1.0 : 0.15;
        sum += SrcTex.SampleLevel(LinearClampSampler, saturate(suv), 0).rgb * w;
        wsum += w;
    }
    return float4(sum / wsum, here.a);
}

float4 PSMain(PS_Input input) : SV_TARGET
{
    if (CinePass == 0) return float4(Occlusion(input.UV).xxx, 1.0);
    if (CinePass == 1) return float4(BlurAO(input.UV, float2(1, 0)).xxx, 1.0);
    if (CinePass == 2) return float4(BlurAO(input.UV, float2(0, 1)).xxx, 1.0);
    if (CinePass == 3) return float4(BrightPass(input.UV), 1.0);
    if (CinePass == 4) return float4(BlurBloom(input.UV, float2(1, 0)), 1.0);
    if (CinePass == 5) return float4(BlurBloom(input.UV, float2(0, 1)), 1.0);
    if (CinePass == 6) return Reflection(input.UV);
    if (CinePass == 7) return BlurSsr(input.UV, float2(1, 0));
    if (CinePass == 8) return BlurSsr(input.UV, float2(0, 1));
    if (CinePass == 9) return DofPrepare(input.UV);
    if (CinePass == 10) return BokehGather(input.UV);
    if (CinePass == 11) return CleanDof(input.UV);
    if (CinePass == 13) return MotionBlurPass(input.UV);

    return DepthMsTex.Load(int2(input.Pos.xy), 0).rrrr;
}
