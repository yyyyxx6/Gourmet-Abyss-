#ifndef GA_WIND_WAVE_INCLUDED
#define GA_WIND_WAVE_INCLUDED

float GA_WindWave(float3 anchorWS, float time, float speed, float spatialScale, float gustStrength)
{
    float spatialPhase = dot(anchorWS, float3(0.73, 0.57, 0.41)) * spatialScale;
    float phase = time * speed + spatialPhase;
    float sway = sin(phase) * 0.8 + sin(phase * 1.73 + 1.2) * 0.2;
    float gust = 0.35 + 0.65 * (0.5 + 0.5 * sin(time * speed * 0.29 + spatialPhase * 0.37));
    return sway * lerp(1.0, gust, saturate(gustStrength));
}

float GA_RootWeight(float imageY, float fixedRange, float flexibility)
{
    float weight = saturate((imageY - fixedRange) / max(1.0 - fixedRange, 0.001));
    return pow(weight, max(flexibility, 0.1));
}
#endif
