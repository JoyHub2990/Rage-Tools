float GrassLodFade_U5(float3 ipos, uint iid, float camDist, float fadeStart, float lodDist,
                      float fadeRange, float fadePower)
{

    float threshold = (camDist - fadeStart) / max(lodDist - fadeStart, 0.001);

    float range = clamp(fadeRange, 0.01, 1.0);
    float scaledRange = 1.0 - range;
    float2 s = sin(ipos.xy);
    float c = cos(s.x + s.y + (float)iid);
    float r = pow(saturate((c + 1.0) * 0.5), max(fadePower, 0.001)) * scaledRange;

    return saturate(((r + range) - threshold) / range);
}
