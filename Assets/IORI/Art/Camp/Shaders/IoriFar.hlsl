// The far side of the lake, shared by the sky (which paints it) and the water (which draws every
// light of the town out toward the viewer, under its own mirror image): the mountains' outlines and
// the lights of the town.
// The outlines are a drawn composition: tools/camp/make_ridges.py -> iori_ridges.png, handed to
// the shaders by CampAtmosphere as _IoriRidgeTex.
#ifndef IORI_FAR_INCLUDED
#define IORI_FAR_INCLUDED

#define IORI_DEG 0.0174533
#define IORI_RIDGE_W 2048
#define IORI_RIDGE_MAX_DEG 16.0
#define IORI_RIDGE_SLOPE 2.0

TEXTURE2D(_IoriRidgeTex);
float4 _IoriTown;            // rgb: town lights (color times level)

// Outlines at the direction az (radians, 0 = straight across the lake): top = heights in degrees of the
// near headlands, the main range and the far range; slope = how steeply each climbs to the right;
// town = how many lights, and how high they climb (degrees).
// Blended by hand between two texels: straight lines, sharp corners.
void IoriRidges(float az, out float3 top, out float3 slope, out float2 town)
{
    float x = (az / 6.2831853 + 0.5) * IORI_RIDGE_W - 0.5;
    float fx = floor(x), f = x - fx;
    int i0 = ((int)fx + IORI_RIDGE_W) % IORI_RIDGE_W, i1 = (i0 + 1) % IORI_RIDGE_W;
    float4 a0 = LOAD_TEXTURE2D(_IoriRidgeTex, int2(i0, 0)), a1 = LOAD_TEXTURE2D(_IoriRidgeTex, int2(i1, 0));
    float4 b0 = LOAD_TEXTURE2D(_IoriRidgeTex, int2(i0, 1)), b1 = LOAD_TEXTURE2D(_IoriRidgeTex, int2(i1, 1));
    const float2 K = float2(65280.0, 255.0) / 65535.0 * IORI_RIDGE_MAX_DEG;
    float3 h0 = float3(dot(a0.rg, K), dot(a0.ba, K), dot(b0.rg, K));
    float3 h1 = float3(dot(a1.rg, K), dot(a1.ba, K), dot(b1.rg, K));
    top = lerp(h0, h1, f);
    slope = (h1 - h0) * (IORI_RIDGE_W / 360.0);
    town = lerp(float2(b0.b, b0.a * (IORI_RIDGE_MAX_DEG * 0.25)), float2(b1.b, b1.a * (IORI_RIDGE_MAX_DEG * 0.25)), f);
}

// Which way each range climbs at az, with the small notches smoothed away (degrees up per degree to the
// right): x near headlands, y main range, z far range. The mountains' faces are lit by this.
float3 IoriRidgeLean(float az)
{
    float x = (az / 6.2831853 + 0.5) * IORI_RIDGE_W - 0.5;
    float fx = floor(x), f = x - fx;
    int i0 = ((int)fx + IORI_RIDGE_W) % IORI_RIDGE_W, i1 = (i0 + 1) % IORI_RIDGE_W;
    float3 c = lerp(LOAD_TEXTURE2D(_IoriRidgeTex, int2(i0, 2)).rgb, LOAD_TEXTURE2D(_IoriRidgeTex, int2(i1, 2)).rgb, f);
    return (c.brg * 255.0 - 127.5) * (IORI_RIDGE_SLOPE / 127.5);
}

// The lights of the town sit in a grid of 0.2 degree cells (x: azimuth, y: height), thick near the shore
// and thinning as they climb the slopes. This says whether the cell lid holds a light (1 or 0), and
// where in the cell it sits (lp, -0.3..0.3), how large it is (lsize, in cells) and its own number (lh).
// town: from IoriRidges. landTop: height of the land here (no lights in the sky).
float IoriTownCell(float2 lid, float2 town, float landTop, out float2 lp, out float lsize, out float lh)
{
    float el = (lid.y + 0.5) * 0.2;
    lh = IoriHash1(lid + 17.0);
    float climb = saturate(1.0 - el / max(town.y, 0.05));
    float chance = town.x * (0.45 * pow(climb, 0.8) + 0.35 * step(el, 0.45));
    lp = (float2(IoriHash1(lid + 2.2), IoriHash1(lid + 6.6)) - 0.5) * 0.6;
    lsize = lerp(0.16, 0.34, IoriHash1(lid + 8.8));
    return step(1.0 - chance, lh) * step(el, max(landTop, 0.5)) * step(0.0, el);
}

// A light keeps breathing a little.
float IoriTownBreath(float lh, float t) { return 0.8 + 0.2 * sin(t * (0.7 + lh * 2.0) + lh * 30.0); }

// The light of the town at a direction (azimuth and height in degrees), if one burns there.
// wide: > 1 widens a light. Returns x: how bright (0..1), y: 1 for the paler ones.
float2 IoriTownLight(float2 azel, float2 town, float landTop, float wide, float t)
{
    float2 lg = azel / 0.2;
    float2 lid = floor(lg), lf = frac(lg) - 0.5;
    float2 lp;
    float lsize, lh;
    float on = IoriTownCell(lid, town, landTop, lp, lsize, lh);
    float2 q = lf - lp;
    q.x /= wide;
    float ldot = smoothstep(lsize, lsize * 0.35, length(q));
    return float2(ldot * on * IoriTownBreath(lh, t), step(0.7, IoriHash1(lid + 4.4)));
}

float3 IoriTownColor(float pale)
{
    return _IoriTown.rgb * lerp(float3(1.0, 0.66, 0.36), float3(1.0, 0.92, 0.74), pale);
}

#endif
