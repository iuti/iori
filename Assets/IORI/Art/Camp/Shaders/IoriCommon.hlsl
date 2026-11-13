// Shared by every camp shader: the values CampAtmosphere pushes, the brush-stroke lookup,
// light arriving at a stroke, and the air between the camera and things.
//
// How the painted look works: a surface is not lit pixel by pixel. Each pixel looks up which
// brush stroke it belongs to (iori_strokes.png, made by tools/camp/make_strokes.py) and takes the
// light, shadow and color at that stroke's center. Every stroke comes out as one flat color with
// a crisp, angular outline, and real light and real shadows decide which color.
#ifndef IORI_COMMON_INCLUDED
#define IORI_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// the stroke map stores offsets of up to 128 texels of its 1024 (MAX_OFF in make_strokes.py)
#define IORI_STROKE_MAX_OFF 0.125

TEXTURE2D(_IoriStrokeTex);
SAMPLER(sampler_trilinear_repeat);

float4 _IoriStroke;          // x: tiles per metre close to the camp, y: distance up to which strokes keep that size, z: stroke amount 0..1, w: light bands per doubling of brightness (0 = smooth)
float4 _IoriStrokeOrigin;    // xyz: where stroke sizes are measured from (the camera's home), so strokes stay put when the camera moves
float4 _IoriAmbientSky;      // light from above
float4 _IoriAmbientEquator;  // light from the sides
float4 _IoriAmbientGround;   // light bounced up from the ground
float4 _IoriFogColor;        // rgb: color of distant air
float4 _IoriMistColor;       // rgb: color of the low mist
float4 _IoriFog;             // x: haze per metre, y: mist per metre at the water, z: height where the mist has thinned to 1/e, w: most that air may hide
float4 _IoriWeather;         // x: rain 0..1, y: wet 0..1, z: sleep 0..1, w: time in seconds
float4 _IoriWind;            // xy: direction on the ground, z: strength, w: gustiness
float4 _IoriGlow[8];         // rgb: light given off by each glow channel (see GLOW in campkit.py), already times its level; a: 1 = tinted by the surface's own color
float4 _IoriGroundMap;       // xy: world x,z of the ground texture's corner, z: 1 / its size in metres, w: puddle water height right now
float4 _IoriFirePos;         // xyz: where the fire's light sits, w: how far it reaches
float4 _IoriLight;           // x: size of the lamps squared (softens their light close up), y: brightness where colors stop getting lighter and start to hold, z: most a lit color may reach, w: how much of a lamp's color lands on a surface whatever its own color
float4 _IoriLamp;            // x: how far the sky's light steps back on a stroke that belongs to a lamp (0..1), y: brightness where lamp-lit colors start to hold, z: most a lamp-lit color may reach
float4 _IoriShadeMix;        // x: how much of the sky's color lands on a surface whatever its own color (blue shade at dusk), y: how far a lamp's light steps back on a stroke that belongs to the sky, z: how far a lamp's light creeps over the ground, w: how wide the border is where warm and cool strokes mix
float4 _IoriTentLamp;        // xyz: the lamp inside the tent, w: how strongly it burns
float4 _IoriAirFire;         // rgb: light that a metre of air catches right at the fire (0 = none)
float4 _IoriAirTent;         // rgb: the same for the lamp in the tent

float IoriTime() { return _IoriWeather.w; }

// ---------------------------------------------------------------- small noises
float IoriHash1(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float IoriHash13(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
float IoriNoise2(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(IoriHash1(i), IoriHash1(i + float2(1, 0)), u.x), lerp(IoriHash1(i + float2(0, 1)), IoriHash1(i + float2(1, 1)), u.x), u.y);
}
float IoriNoise3(float3 p)
{
    float3 i = floor(p), f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float a = lerp(IoriHash13(i), IoriHash13(i + float3(1, 0, 0)), u.x);
    float b = lerp(IoriHash13(i + float3(0, 1, 0)), IoriHash13(i + float3(1, 1, 0)), u.x);
    float c = lerp(IoriHash13(i + float3(0, 0, 1)), IoriHash13(i + float3(1, 0, 1)), u.x);
    float d = lerp(IoriHash13(i + float3(0, 1, 1)), IoriHash13(i + float3(1, 1, 1)), u.x);
    return lerp(lerp(a, b, u.y), lerp(c, d, u.y), u.z);
}

// ---------------------------------------------------------------- brush strokes
struct IoriStroke
{
    float3 pos;      // world position of the center of the stroke this pixel belongs to
    float rnd;       // one random number per stroke
    float bristle;   // about -0.5..0.5: streaks along the stroke, a little darker at its rim
};

// The stroke map is laid on the plane the surface faces most (strokes run up and down on walls).
void IoriStrokePlane(float3 n, out float3 ax, out float3 ay)
{
    float3 a = abs(n);
    if (a.y >= a.x && a.y >= a.z) { ax = float3(1, 0, 0); ay = float3(0, 0, 1); }
    else if (a.x >= a.z)           { ax = float3(0, 1, 0); ay = float3(0, 0, 1); }
    else                           { ax = float3(0, 1, 0); ay = float3(1, 0, 0); }
}

float2 IoriLevelShift(float level) { return frac(float2(0.37, 0.61) * level); }

IoriStroke IoriStrokeAt(float3 posWS, float3 nWS, float scaleMul)
{
    float3 ax, ay;
    IoriStrokePlane(nWS, ax, ay);
    float2 p = float2(dot(posWS, ax), dot(posWS, ay));
    float3 ddxP = ddx(posWS), ddyP = ddy(posWS);
    float2 dpx = float2(dot(ddxP, ax), dot(ddxP, ay));
    float2 dpy = float2(dot(ddyP, ax), dot(ddyP, ay));

    // Strokes double in size every time the distance doubles, so they cover about the same part of
    // the picture near and far. The larger size takes over one stroke at a time, never as a blur.
    float dist = distance(posWS, _IoriStrokeOrigin.xyz);
    float lv = max(0.0, log2(max(dist, 1e-3) / max(_IoriStroke.y, 1e-3)));
    float l0 = floor(lv);
    float f = lv - l0;
    float s0 = _IoriStroke.x * scaleMul * exp2(-l0);
    float s1 = s0 * 0.5;
    float4 t0 = SAMPLE_TEXTURE2D_GRAD(_IoriStrokeTex, sampler_trilinear_repeat, p * s0 + IoriLevelShift(l0), dpx * s0, dpy * s0);
    float4 t1 = SAMPLE_TEXTURE2D_GRAD(_IoriStrokeTex, sampler_trilinear_repeat, p * s1 + IoriLevelShift(l0 + 1.0), dpx * s1, dpy * s1);
    bool coarse = t1.b < smoothstep(0.15, 0.85, f);
    float4 t = coarse ? t1 : t0;
    float sc = coarse ? s1 : s0;

    float2 off = float2(t.r - 0.5, 0.5 - t.g) * (2.0 * IORI_STROKE_MAX_OFF) / sc;
    float3 o3 = ax * off.x + ay * off.y;
    o3 -= nWS * dot(o3, nWS);          // keep the center on the surface

    IoriStroke s;
    s.pos = posWS + o3 * _IoriStroke.z;
    s.rnd = t.b;
    s.bristle = t.a - 0.5;
    return s;
}

// ---------------------------------------------------------------- light
float3 IoriAmbient(float3 n)
{
    float3 a = lerp(_IoriAmbientEquator.rgb, _IoriAmbientSky.rgb, saturate(n.y));
    return lerp(a, _IoriAmbientGround.rgb, saturate(-n.y));
}

// A lamp is not a point: right next to it the light levels off instead of shooting up without end.
// URP hands us 1/d^2 (times its fade at the edge of the range); this turns it into 1/(d^2 + size^2).
float IoriLampFalloff(float att) { return att / (1.0 + att * _IoriLight.x); }

// Keeps a brightly lit color a color: past a knee the whole color is held back together, so ground
// next to the fire turns deep orange, not white.
float3 IoriSoftClip(float3 c)
{
    float knee = max(_IoriLight.y, 0.05), top = max(_IoriLight.z, knee + 0.01);
    float m = max(c.r, max(c.g, c.b));
    if (m <= knee) return c;
    float over = m - knee, room = top - knee;
    return c * ((knee + room * over / (over + room)) / m);
}

// Cuts a brightness into steps that are equal ratios apart (rings of light around the fire).
// rnd shifts the cut per stroke, so the edge of a ring is made of whole strokes.
float IoriBand(float x, float rnd)
{
    float bands = _IoriStroke.w;
    if (bands <= 0.0) return x;
    float l = log2(max(x, 1e-5)) * bands;
    return exp2((floor(l + rnd) + 0.5) / bands) * step(1e-4, x);
}

// Cuts a 0..1 value (how much a surface faces a light) into n flat steps.
float IoriStep(float x, float rnd, float n)
{
    if (_IoriStroke.w <= 0.0) return x;
    return saturate(floor(x * n + rnd * 0.9) / n);
}

struct IoriLit
{
    float3 diffuse;   // light from the sky and the sun or moon
    float3 lamp;      // light from the fire, the lantern and the tent
    float3 spec;      // highlights (already a color)
};

// posWS: the stroke center. wrap: how far light creeps around to the shaded side (0 hard, 0.5 soft).
// lampWrap: the same for the lamps. On the ground it is kept small: a fire stands low, its light only
// grazes the ground, and so its pool of light ends a few steps away and the ground beyond stays dark.
// through: how much light shines through from behind (leaves, cloth). gloss: 0 none .. 1 mirror-like.
IoriLit IoriLightingWrapped(float3 posWS, float3 n, float3 viewDir, float rnd, float ao, float wrap, float lampWrap, float through, float gloss)
{
    IoriLit o;
    o.diffuse = IoriAmbient(n) * ao;
    o.lamp = 0;
    o.spec = 0;
    float direct = lerp(1.0, ao, 0.5);
    float shine = exp2(4.0 + gloss * 6.0);

    float3 sp = posWS + n * 0.03;
    Light m = GetMainLight(TransformWorldToShadowCoord(sp), sp, half4(1, 1, 1, 1));
    float nl = dot(n, m.direction);
    float k = saturate((nl + wrap) / (1.0 + wrap)) + saturate(-nl) * through;
    k = IoriStep(k, rnd, 4.0) * m.shadowAttenuation;
    o.diffuse += m.color * (k * direct);
    if (gloss > 0.0)
    {
        float3 h = normalize(m.direction + viewDir);
        o.spec += m.color * (step(0.5, pow(saturate(dot(n, h)), shine)) * gloss * m.shadowAttenuation * saturate(nl * 4.0));
    }

    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
        Light l = GetAdditionalLight(i, sp, half4(1, 1, 1, 1));
        float nl2 = dot(n, l.direction);
        float k2 = saturate((nl2 + lampWrap) / (1.0 + lampWrap)) + saturate(-nl2) * through;
        float reach = IoriLampFalloff(l.distanceAttenuation);
        k2 *= reach * l.shadowAttenuation;
        k2 = IoriBand(k2, rnd);
        o.lamp += l.color * (k2 * direct);
        if (gloss > 0.0)
        {
            float3 h2 = normalize(l.direction + viewDir);
            o.spec += l.color * (step(0.5, pow(saturate(dot(n, h2)), shine)) * gloss * saturate(reach * 2.0) * l.shadowAttenuation * saturate(nl2 * 4.0));
        }
    }
    return o;
}

IoriLit IoriLighting(float3 posWS, float3 n, float3 viewDir, float rnd, float ao, float wrap, float through, float gloss)
{
    return IoriLightingWrapped(posWS, n, viewDir, rnd, ao, wrap, wrap, through, gloss);
}

// Puts the light on a surface color. Light from the sky and the sun is multiplied in, the way light
// works. A lamp's light is partly laid over the color like a glaze of paint (as much as the color is
// light, whatever its hue): grass by the fire turns a warm color, where plain multiplying gives mud.
// Three more things a painter does:
// - Lamp-lit colors are held at a deep value (ground at the fire is a deep orange, never a pale one).
// - At dusk and at night the sky's light is glazed on as well: whatever a thing's own color, its shade
//   takes the blue of the sky (_IoriShadeMix.x), so the shadows are a clear blue and not a grey.
// - Warm light and cool light are not stirred together, which would make grey. Every stroke belongs
//   to one of them: to the lamp where the lamp is the stronger, to the sky where the sky is. On a
//   lamp's stroke the sky's light steps back, on the sky's stroke the lamp's does. Where the two are
//   about even, the stroke's own number decides, so the pool of light around the fire ends in single
//   warm strokes scattered over the blue ground.
// lampAmount: how much of the lamps this surface takes (logs inside the fire take little: they stay
// dark against the flame). rnd: the stroke's number.
float3 IoriHold(float3 c, float knee, float top)
{
    float m = max(c.r, max(c.g, c.b));
    if (m <= knee) return c;
    float over = m - knee, room = max(top - knee, 0.01);
    return c * ((knee + room * over / (over + room)) / m);
}

float3 IoriShade(float3 albedo, IoriLit L, float lampAmount, float rnd)
{
    float v = max(albedo.r, max(albedo.g, albedo.b));
    float3 grey = float3(v, v, v);
    float3 lamp = IoriHold(lerp(albedo, grey, _IoriLight.w) * (L.lamp * lampAmount), _IoriLamp.y, _IoriLamp.z);
    float3 sky = lerp(albedo, grey, _IoriShadeMix.x) * L.diffuse;
    float lm = max(lamp.r, max(lamp.g, lamp.b)), sm = max(sky.r, max(sky.g, sky.b));
    float warm = smoothstep(0.44, 0.56, lm / (lm + sm + 1e-4) + (rnd - 0.5) * _IoriShadeMix.w);
    float3 col = sky * (1.0 - _IoriLamp.x * warm) + lamp * (1.0 - _IoriShadeMix.y * (1.0 - warm));
    return IoriSoftClip(col) + L.spec;
}

// The same light without strokes or steps, for things that must stay smooth (the character).
float3 IoriLightingSmooth(float3 posWS, float3 n, float wrap)
{
    float3 sum = IoriAmbient(n);
    Light m = GetMainLight(TransformWorldToShadowCoord(posWS), posWS, half4(1, 1, 1, 1));
    sum += m.color * (saturate((dot(n, m.direction) + wrap) / (1.0 + wrap)) * m.shadowAttenuation);
    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
        Light l = GetAdditionalLight(i, posWS, half4(1, 1, 1, 1));
        sum += l.color * (saturate((dot(n, l.direction) + wrap) / (1.0 + wrap)) * IoriLampFalloff(l.distanceAttenuation) * l.shadowAttenuation);
    }
    return sum;
}

// ---------------------------------------------------------------- air
// Haze that thickens with distance plus a low mist that hangs over the water and the hollows.
// Returns how much of the thing is hidden (0..1).
float IoriAirAmount(float3 posWS)
{
    float3 v = posWS - _WorldSpaceCameraPos;
    float d = length(v);
    float b = 1.0 / max(_IoriFog.z, 0.05);
    float t = b * v.y;
    float k = abs(t) > 1e-3 ? (1.0 - exp(-t)) / t : 1.0;
    float mist = _IoriFog.y * exp(-b * max(_WorldSpaceCameraPos.y, 0.0)) * d * k;
    return min(1.0 - exp(-(d * _IoriFog.x + mist)), _IoriFog.w);
}

// The air itself lights up around a lamp: the glow that hangs around a fire on a misty evening.
// This is all the light the air catches along the line of sight from the camera to posWS, worked out
// exactly for a lamp whose light thins with the square of the distance. Unlike a glow added to the
// finished picture it knows about depth: a thing in front of the fire cuts it off, and in the mirror
// images it is right too.
// q: camera minus lamp, rd: the line of sight, tmax: its length, soft2: size of the lamp squared.
float IoriGlowAlong(float3 q, float3 rd, float tmax, float soft2)
{
    float b = dot(rd, q);
    float h = sqrt(max(dot(q, q) - b * b, 0.0) + soft2);
    return (atan((tmax + b) / h) - atan(b / h)) / h;
}

float3 IoriLampAir(float3 posWS)
{
    float3 v = posWS - _WorldSpaceCameraPos;
    float tmax = length(v);
    float3 rd = v / max(tmax, 1e-4);
    return _IoriAirFire.rgb * IoriGlowAlong(_WorldSpaceCameraPos - _IoriFirePos.xyz, rd, tmax, 0.03)
         + _IoriAirTent.rgb * IoriGlowAlong(_WorldSpaceCameraPos - _IoriTentLamp.xyz, rd, tmax, 0.25);
}

float3 IoriAir(float3 col, float3 posWS)
{
    float3 v = posWS - _WorldSpaceCameraPos;
    float d = length(v);
    float b = 1.0 / max(_IoriFog.z, 0.05);
    float t = b * v.y;
    float k = abs(t) > 1e-3 ? (1.0 - exp(-t)) / t : 1.0;
    float haze = d * _IoriFog.x;
    float mist = _IoriFog.y * exp(-b * max(_WorldSpaceCameraPos.y, 0.0)) * d * k;
    float a = min(1.0 - exp(-(haze + mist)), _IoriFog.w);
    float3 c = lerp(_IoriFogColor.rgb, _IoriMistColor.rgb, saturate(mist / max(haze + mist, 1e-4)));
    return lerp(col, c, a) + IoriLampAir(posWS);
}

// ---------------------------------------------------------------- helpers for vertex data
// TEXCOORD1 of the camp meshes: x = glow channel, y = glow strength, z = gloss, w = free
// albedo: the surface's own color (linear). Channels whose w is 1 glow in that color (the tent's cloth).
float3 IoriGlowOf(float4 data, float3 albedo)
{
    int ch = (int)(data.x + 0.5);
    float4 g = _IoriGlow[clamp(ch, 0, 7)];
    float3 own = albedo / max(max(albedo.r, albedo.g), max(albedo.b, 1e-3));
    return g.rgb * lerp(float3(1, 1, 1), own, g.a) * (data.y * step(0.5, data.x));
}

float3 IoriVertexAlbedo(float3 srgb) { return SRGBToLinear(srgb); }

#endif
