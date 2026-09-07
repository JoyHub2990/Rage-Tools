cbuffer SkyVars : register(b0)
{

    float4x4 InvViewProjDir;
    float4 CameraPos;

    float3 AzimuthEastColour;       float AzimuthTransitionPosition;
    float3 AzimuthWestColour;       float ZenithTransitionPosition;
    float3 AzimuthTransitionColour; float ZenithBlendStart;
    float3 ZenithColour;            float ZenithTransitionEastBlend;
    float3 ZenithTransitionColour;  float ZenithTransitionWestBlend;

    float3 SunDirection;    float SunDiscSize;
    float3 SunColour;       float SunHdr;
    float3 SunDiscColour;   float SunInfluenceRadius;
    float3 SunMie;          float SunScatterIntensity;

    float3 MoonDirection;   float MoonDiscSize;
    float3 MoonColour;      float MoonIntensity;
    float3 LunarCycle;      float MoonInfluenceRadius;

    float3 CloudBaseColour;   float CloudBaseStrength;
    float3 CloudMidColour;    float CloudDensityMultiplier;
    float3 CloudShadowColour; float CloudDensityBias;
    float CloudFadeOut;       float CloudShadowStrength;
    float CloudCoverage;      float CloudTime;

    float3 FogColour;   float FogDensity;
    float HdrIntensity; float StarfieldIntensity; float SkyPad0; float SkyPad1;
    float4 SkyExtra0;

float4 GFogParams0;
float4 GFogParams1;
float4 GFogParams2;
float4 GFogSunDir;
float4 GFogMoonDir;
float4 GFogColSun;
float4 GFogColAtmo;
float4 GFogColGround;
float4 GFogColHaze;
float4 GFogColMoon;
}

struct VS_Input  { float3 Pos : POSITION; };
struct PS_Input  { float4 Pos : SV_POSITION; float3 Ray : TEXCOORD0; };

PS_Input VSMain(uint id : SV_VertexID)
{
    PS_Input o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.Pos = float4(uv * float2(2, -2) + float2(-1, 1), 1, 1);
    float4 dir = mul(float4(o.Pos.xy, 1, 1), InvViewProjDir);
    o.Ray = dir.xyz / dir.w;
    return o;
}

float ComputeGlobalVolumetricFogValue_Crytek(float3 cameraToWorldPos, out float dist)
{
    const float threshold = 0.01;
    float fullDist = length(cameraToWorldPos);
    dist = max(0, fullDist - GFogParams0.x);
    float deltaZ = cameraToWorldPos.z * (dist / max(fullDist, 1e-4));
    float t = (GFogParams2.z * deltaZ);
    float fogInt = (abs(deltaZ) > threshold) ? (1.0 - exp(-t)) / t : 1.0;

    float val = min(1.0f, GFogParams1.w * dist * fogInt);
    float res = 1.0 - saturate(exp(val));
    return res;
}

float4 CalcFogData(float3 eyeRayToPoint, float hazeScale)
{
    if (GFogParams0.w < 0.5) return float4(0, 0, 0, 0);
    float dist = 0.0f;
    float groundFogAmount = ComputeGlobalVolumetricFogValue_Crytek(eyeRayToPoint, dist) * GFogParams2.y;
    float3 nray = normalize(eyeRayToPoint);

    float moonAmount = pow(saturate(dot(nray, GFogMoonDir.xyz)), GFogMoonDir.w);
    float sunAmount = pow(saturate(dot(nray, GFogSunDir.xyz)), GFogSunDir.w);

    float horizonHazeBlend = hazeScale * GFogParams1.y * (1 - groundFogAmount);

    float horizonHazeAmount = horizonHazeBlend * (1 - exp(GFogParams1.x * max(0, dist - GFogParams2.x)));
    float finalFogBlend = saturate(horizonHazeAmount + groundFogAmount);

    float atmosphereBlend = 1.0 - exp(-GFogParams1.z * dist);
    float3 atmosphereAndMoonColor = lerp(GFogColAtmo.rgb, GFogColMoon.rgb, moonAmount);
    float3 atmosphereColor = lerp(atmosphereAndMoonColor, GFogColSun.rgb, sunAmount);
    float3 groundFogAtmoColor = lerp(GFogColGround.rgb, atmosphereColor, atmosphereBlend);
    float3 groundFogHazeAtmoColor = lerp(groundFogAtmoColor, GFogColHaze.rgb, horizonHazeBlend);
    return float4(groundFogHazeAtmoColor, finalFogBlend);
}

float3 CalcAtmosphereColour(float3 viewDir)
{
    float azimuthBlend = sqrt(-viewDir.x * 0.5 + 0.5);
    float zenithBlend = abs(viewDir.z);

    float atp = max(AzimuthTransitionPosition, 1e-4);
    float3 azimuthColour = (azimuthBlend < atp)
        ? lerp(AzimuthEastColour, AzimuthTransitionColour, azimuthBlend / atp)
        : lerp(AzimuthTransitionColour, AzimuthWestColour, (azimuthBlend - atp) / max(1.0 - atp, 1e-4));

    float zenithTransitionBlend = lerp(ZenithTransitionEastBlend, ZenithTransitionWestBlend, azimuthBlend);
    float3 newZenithTransitionColour = lerp(azimuthColour, ZenithTransitionColour, zenithTransitionBlend);

    float ztp = max(ZenithTransitionPosition, 1e-4);
    float zenithTransitionToTop = saturate((zenithBlend - ztp) / max(1.0 - ztp, 1e-4));
    float bottomToZenithTransition = zenithBlend / ztp;
    zenithTransitionToTop = saturate(zenithTransitionToTop / max(1.0 - ZenithBlendStart, 1e-4));

    float3 skyCol = (zenithBlend < ztp)
        ? lerp(azimuthColour, newZenithTransitionColour, bottomToZenithTransition)
        : lerp(newZenithTransitionColour, ZenithColour, zenithTransitionToTop);

    float sat = SkyExtra0.x;
    if (abs(sat - 1.0) > 0.001)
    {
        float lum = dot(skyCol, float3(0.2126, 0.7152, 0.0722));
        float up = saturate(zenithBlend * 1.35);
        skyCol = max(lerp(lum.xxx, skyCol, lerp(1.0, sat, up)), 0.0);
    }
    return skyCol;
}

float3 CalcSunScatter(float3 viewDir, out float cosTheta)
{
    float miePhase = SunMie.x;
    float mieScatter = SunMie.y;
    float mieIntensityMult = SunMie.z;

    cosTheta = dot(viewDir, SunDirection);
    float cosThetaSq = cosTheta * cosTheta;

    float miePhaseSqrPlusOne = miePhase * miePhase + 1.0;
    float miePhaseTimesTwo = miePhase * 2.0;

    const float mieConstant = 0.02;
    float mieConstantTimesScatter = mieScatter * mieConstant;

    float denom = pow(max(miePhaseSqrPlusOne - miePhaseTimesTwo * cosTheta, 1e-4), 1.5);
    float phase = (1.0 + cosThetaSq) / denom * mieConstantTimesScatter;

    return SunColour * saturate(phase) * mieIntensityMult;
}

float Hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash(i), b = Hash(i + float2(1, 0));
    float c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float Fbm(float2 p)
{
    float v = 0.0, amp = 0.5;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        v += amp * ValueNoise(p);
        p *= 2.03;
        amp *= 0.5;
    }
    return v;
}

float4 CalcClouds(float3 viewDir)
{
    if (viewDir.z <= 0.001) return 0;

    float2 uv = viewDir.xy / max(viewDir.z, 0.06) * 0.09;
    uv += float2(CloudTime * 0.010, CloudTime * 0.004);

    float perlin = Fbm(uv * 1.6);
    float detail = Fbm(uv * 5.2 + 13.7);

    float cloudBase = saturate((perlin + detail * 0.35) * max(CloudBaseStrength, 0.25));

    float cloud = perlin + detail * 0.30;
    float density = saturate(saturate(CloudDensityMultiplier) * cloud - saturate(CloudDensityBias)
                             - (1.0 - CloudCoverage));
    float amount = density * density;

    amount *= saturate(viewDir.z * 5.0 - saturate(CloudFadeOut));

    float shadowAmount = saturate((detail + perlin * 0.5) * saturate(CloudShadowStrength));
    float3 colour = lerp(CloudMidColour, CloudBaseColour, cloudBase);
    colour = lerp(colour, CloudShadowColour, shadowAmount * 0.6);

    return float4(colour, saturate(amount));
}

float4 PSMain(PS_Input input) : SV_TARGET
{
    float3 viewDir = normalize(input.Ray);
    if (SkyPad0 > 0.5 && SkyPad0 < 1.5) return float4(viewDir * 0.5 + 0.5, 1);
    if (SkyPad1 > 0.5) return float4(CalcAtmosphereColour(viewDir), 1);

    float3 sky = CalcAtmosphereColour(viewDir) * HdrIntensity;

    float cosTheta = dot(viewDir, SunDirection);
    float3 scatter;
    {
        float sx = SunMie.x * SunMie.y, sy = SunMie.y, sz = 0.00003 * SunInfluenceRadius, sw = SunMie.z;
        float ph = pow(max(abs(-sx * cosTheta + sy), 1e-5), 1.5);
        ph = (cosTheta * cosTheta + 1.0) / ph;
        float halo = saturate(ph * sz);
        scatter = halo * SunColour * sw;
        if (SkyPad0 > 3.5 && SkyPad0 < 4.5) return float4(scatter, 1);
        sky = sky * saturate(1.0 - halo) + scatter;
    }

    float2 starP = viewDir.xy * rsqrt(1.0 + max(viewDir.z, 0.0)) * 90.0;
    float starPx = max(max(fwidth(starP.x), fwidth(starP.y)), 1e-4);
    if (StarfieldIntensity > 0.001 && viewDir.z > 0.0)
    {

        float2 sp = starP;
        float2 cell = floor(sp);
        float2 f = sp - cell;
        float h = Hash(cell);
        float2 centre = float2(Hash(cell + 7.31), Hash(cell + 19.17)) * 0.8 + 0.1;
        float d = length(f - centre) / starPx;
        float lit = h > 0.94 ? 1.0 : 0.0;
        float mag = 0.35 + 0.65 * saturate((h - 0.94) / 0.06);
        float star = lit * mag * saturate(1.5 - d);

        float dark = saturate(1.5 - dot(sky, float3(0.299, 0.587, 0.114)) * 2.0);
        sky += star * StarfieldIntensity * saturate(viewDir.z + 0.2) * 0.5 * dark;
    }

    float sunAngle = acos(clamp(cosTheta, -1.0, 1.0));
    float discSize = clamp(SunDiscSize * 0.02, 0.004, 0.02);
    if (sunAngle < discSize * 2.5)
    {
        float d = 1.0 - smoothstep(discSize * 0.8, discSize * 1.7, sunAngle);

        sky += SunDiscColour * d * SunHdr;
    }

    float moonCos = dot(viewDir, MoonDirection);
    float moonAngle = acos(clamp(moonCos, -1.0, 1.0));
    float moonSize = clamp(MoonDiscSize * 0.06, 0.006, 0.06);
    if (moonAngle < moonSize)
    {

        float3 up = abs(MoonDirection.z) < 0.9 ? float3(0, 0, 1) : float3(1, 0, 0);
        float3 mx = normalize(cross(up, MoonDirection));
        float3 my = cross(MoonDirection, mx);
        float2 nc = float2(dot(viewDir, mx), dot(viewDir, my)) / moonSize;
        float r2 = dot(nc, nc);
        if (r2 <= 1.0)
        {
            float3 n = normalize(float3(nc.x, sqrt(max(1.0 - r2, 0.0)), nc.y));
            float lit = max(0.0, dot(n, normalize(LunarCycle + 1e-6)));
            float edge = 1.0 - smoothstep(0.85, 1.0, sqrt(r2));
            sky += MoonColour * lit * MoonIntensity * edge;
        }
    }

    float4 clouds = 0;
    if (CloudCoverage > 0.001)
    {
        clouds = CalcClouds(viewDir);
        sky = lerp(sky, clouds.rgb * HdrIntensity, clouds.a);
    }

    {
        float4 fd = CalcFogData(viewDir * 25000.0, 0.0);
        sky = lerp(sky, fd.rgb, fd.a);
    }

    if (SkyPad0 > 2.5 && SkyPad0 < 3.5) return float4(sky * 0.25 / max(HdrIntensity, 1e-3), 1);
    if (SkyPad0 > 4.5) return float4(clouds.rgb * clouds.a, 1);

    return float4(max(sky, 0.0), 1);
}
