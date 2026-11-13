// Shared globals for the camp scene. All values are pushed every frame by CampAtmosphere (C#).
// Colors arrive already converted to linear space.
#ifndef IORI_CAMP_INCLUDED
#define IORI_CAMP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float4 _IoriPal[32];          // palette slots (see CampPalette.Slot)
float4 _IoriHemiSky;          // rgb
float4 _IoriHemiGround;       // rgb, a = hemisphere intensity
float4 _IoriDirColor;         // rgb * intensity
float4 _IoriDirVector;        // xyz = direction towards the light (world)
float4 _IoriLightPos[4];      // xyz = world position, w = range
float4 _IoriLightColor[4];    // rgb * intensity
float4 _IoriFogColor;
float4 _IoriFogParams;        // near, far, max amount
float4 _IoriGlowA;            // fire, tent, lantern, town
float4 _IoriGlowB;            // radio dial, player lamp, embers, (unused)
float4 _IoriWeather;          // rain, wet, sleep, time (seconds)
float4 _IoriBrushParams;      // strength, near distance, far distance, world scale
float4 _IoriPaintParams;      // light band count (0 = smooth), band softness, slot-switch amount, unused
TEXTURE2D(_IoriBrushTex); SAMPLER(sampler_IoriBrushTex);

float IoriTime() { return _IoriWeather.w; }

float IoriGlowLevel(float ch)
{
    if (ch < -0.5) return 0;
    float4 a = step(abs(ch - float4(0, 1, 2, 3)), 0.5);
    float4 b = step(abs(ch - float4(4, 5, 6, 7)), 0.5);
    return dot(a, _IoriGlowA) + dot(b, _IoriGlowB);
}

float IoriFogFactor(float3 posWS)
{
    float d = distance(posWS, _WorldSpaceCameraPos);
    return smoothstep(_IoriFogParams.x, _IoriFogParams.y, d) * _IoriFogParams.z;
}

float3 IoriPalette(float slot)
{
    int i = clamp((int)(slot + 0.5), 0, 31);
    return _IoriPal[i].rgb;
}

// Angular brush value in [-0.5, 0.5], projected on the dominant axis plane. Two scales: broad
// strokes plus finer ones near the camera (the near field is where detail is wanted).
float IoriBrush(float3 posWS, float3 n)
{
    float3 a = abs(n);
    float2 p = a.y > max(a.x, a.z) ? posWS.xz : (a.x > a.z ? posWS.zy : posWS.xy);
    float s = _IoriBrushParams.w;
    float b1 = SAMPLE_TEXTURE2D(_IoriBrushTex, sampler_IoriBrushTex, p * s).r;
    float b2 = SAMPLE_TEXTURE2D(_IoriBrushTex, sampler_IoriBrushTex, p * s * 3.1 + 0.37).r;
    return (b1 * 0.6 + b2 * 0.4) - 0.5;
}

float IoriNearFactor(float3 posWS)
{
    float d = distance(posWS, _WorldSpaceCameraPos);
    return 1 - smoothstep(_IoriBrushParams.y, _IoriBrushParams.z, d);
}

// Sum of all light reaching a surface (hemisphere + sun/moon + up to four point lights),
// quantized once into flat bands. The brush nudges the band threshold so band edges look
// like brush marks instead of clean rings.
float3 IoriLight(float3 posWS, float3 n, float brush)
{
    float3 amb = lerp(_IoriHemiGround.rgb, _IoriHemiSky.rgb, n.y * 0.5 + 0.5) * _IoriHemiGround.a;
    float3 sun = _IoriDirColor.rgb * saturate(dot(n, _IoriDirVector.xyz));
    float3 pts = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float3 L = _IoriLightPos[i].xyz - posWS;
        float d2 = max(dot(L, L), 1e-4);
        float d = sqrt(d2);
        float r = max(_IoriLightPos[i].w, 1e-3);
        float win = saturate(1 - pow(d / r, 4));
        float wrap = saturate(dot(n, L / d) * 0.7 + 0.3);
        pts += _IoriLightColor[i].rgb * (win * win / max(d2, 0.35)) * wrap;
    }
    float3 total = amb + sun + pts;
    float bands = _IoriPaintParams.x;
    if (bands > 0.5)
    {
        // quantize the overall level, keep the hue of the summed light
        float lum = max(dot(total, float3(0.299, 0.587, 0.114)), 1e-4);
        float q = lum * bands + brush * _IoriPaintParams.y;
        float stepped = (floor(q) + smoothstep(0.42, 0.58, frac(q))) / bands;
        total *= max(stepped, 0.02) / lum;
    }
    return total;
}

#endif
