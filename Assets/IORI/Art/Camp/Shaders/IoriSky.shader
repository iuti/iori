// Everything too far away to ever move against anything else, painted into the skybox:
// the sky itself (three colors, laid down as large brush strokes), sun glow, moon, stars, clouds,
// the mountain ranges across the lake with the lights of a town at their feet, the far shore's
// tree line, and the haze over the water. Being the skybox, all of it is mirrored in the lake
// and the puddles by the reflection camera at no extra cost.
// The camp looks toward +z; azimuth 0 is straight across the lake.
// The mountains' outlines are a drawn composition (tools/camp/make_ridges.py -> iori_ridges.png).
Shader "IORI/Camp/Sky"
{
    Properties
    {
        [NoScaleOffset] _RidgeTex ("Mountain outlines (iori_ridges.png)", 2D) = "black" {}
        _MountTiles ("Fineness of the strokes on the mountains", Float) = 5
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCommon.hlsl"

            float4 _IoriSkyHorizon, _IoriSkyMid, _IoriSkyZenith;
            float4 _IoriSunDir;        // xyz: toward the sun (it may be below the horizon)
            float4 _IoriSunColor;      // rgb: color of its glow
            float4 _IoriMoonDir;       // xyz: toward the moon
            float4 _IoriMoonColor;
            float4 _IoriSkyParams;     // x: glow, y: moon disc, z: stars, w: sun disc
            float4 _IoriCloud, _IoriCloudDark;
            float4 _IoriCloudParams;   // x: how much cloud, y: overcast (rain), z: drift speed
            float4 _IoriFarNear, _IoriFarFar;   // colors of the nearest and the farthest range
            float4 _IoriTown;          // rgb: town lights (color times level)
            float4 _IoriSkyStroke;     // x: stroke tiles per unit of the dome, y: stroke amount
            TEXTURE2D(_RidgeTex);
            CBUFFER_START(UnityPerMaterial)
                float _MountTiles;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            #define DOME_C 0.35
            float2 DomeUV(float3 d) { return d.xz / (max(d.y, -0.2) + DOME_C); }
            float3 DomeDir(float2 uv)
            {
                float r2 = dot(uv, uv);
                float s = (-r2 * DOME_C + sqrt(max(1.0 + r2 - r2 * DOME_C * DOME_C, 0.0))) / (1.0 + r2);
                float c = sqrt(saturate(1.0 - s * s));
                float2 h = r2 > 1e-8 ? uv * rsqrt(r2) : float2(0, 1);
                return float3(h.x * c, s, h.y * c);
            }

            float Lin1(float x, float seed)
            {
                float i = floor(x), f = frac(x);
                return lerp(IoriHash1(float2(i, seed)), IoriHash1(float2(i + 1.0, seed)), f);
            }

            #define DEG 0.0174533
            #define RIDGE_W 2048
            #define RIDGE_MAX_DEG 16.0

            // Outlines at the direction az (radians): top = heights in degrees of the near headlands, the main
            // range and the far range; slope = how steeply each climbs to the right; town = how many lights, and
            // how high they climb (degrees). Blended by hand between two texels: straight lines, sharp corners.
            void Ridges(float az, out float3 top, out float3 slope, out float2 town)
            {
                float x = (az / 6.2831853 + 0.5) * RIDGE_W - 0.5;
                float fx = floor(x), f = x - fx;
                int i0 = ((int)fx + RIDGE_W) % RIDGE_W, i1 = (i0 + 1) % RIDGE_W;
                float4 a0 = LOAD_TEXTURE2D(_RidgeTex, int2(i0, 0)), a1 = LOAD_TEXTURE2D(_RidgeTex, int2(i1, 0));
                float4 b0 = LOAD_TEXTURE2D(_RidgeTex, int2(i0, 1)), b1 = LOAD_TEXTURE2D(_RidgeTex, int2(i1, 1));
                const float2 K = float2(65280.0, 255.0) / 65535.0 * RIDGE_MAX_DEG;
                float3 h0 = float3(dot(a0.rg, K), dot(a0.ba, K), dot(b0.rg, K));
                float3 h1 = float3(dot(a1.rg, K), dot(a1.ba, K), dot(b1.rg, K));
                top = lerp(h0, h1, f);
                slope = (h1 - h0) * (RIDGE_W / 360.0);
                town = lerp(float2(b0.b, b0.a * (RIDGE_MAX_DEG * 0.25)), float2(b1.b, b1.a * (RIDGE_MAX_DEG * 0.25)), f);
            }
            float Fbm2(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { s += a * IoriNoise2(p); p = p * 2.03 + 7.1; a *= 0.5; }
                return s;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float t = IoriTime();

                // which stroke of the sky this pixel belongs to, and the direction of that stroke's center
                float2 uv = DomeUV(d);
                float4 sm = SAMPLE_TEXTURE2D(_IoriStrokeTex, sampler_trilinear_repeat, uv * _IoriSkyStroke.x);
                float2 off = float2(sm.r - 0.5, 0.5 - sm.g) * (2.0 * IORI_STROKE_MAX_OFF) / _IoriSkyStroke.x;
                float3 dc = DomeDir(uv + off * _IoriSkyStroke.y);
                float rnd = sm.b;

                float3 sunDir = normalize(_IoriSunDir.xyz);
                float glow = _IoriSkyParams.x, moon = _IoriSkyParams.y, stars = _IoriSkyParams.z, sunDisc = _IoriSkyParams.w;
                float overcast = _IoriCloudParams.y;

                // ---- sky: horizon to zenith, one flat color per stroke
                float h = saturate(dc.y);
                float hj = h + (rnd - 0.5) * 0.05;
                float3 col = lerp(_IoriSkyHorizon.rgb, _IoriSkyMid.rgb, smoothstep(0.0, 0.22, hj));
                col = lerp(col, _IoriSkyZenith.rgb, smoothstep(0.14, 0.62, hj));
                float sd = saturate(dot(dc, sunDir));
                float sdFlat = saturate(dot(normalize(float3(dc.x, 0, dc.z)), normalize(float3(sunDir.x, 0, sunDir.z) + 1e-5)));
                // the glow hugs the horizon under the light, and closes in around the light itself
                float3 glowCol = _IoriSunColor.rgb * glow;
                col += glowCol * (pow(sd, 5.0) * 0.45 + pow(sd, 48.0) * 0.6);
                col += glowCol * pow(sdFlat, 3.0) * pow(1.0 - h, 6.0) * 0.5;
                col *= 1.0 + sm.a * 0.06 - 0.03;

                // ---- clouds: long bars, each one a few strokes
                float2 cp = DomeUV(dc) * float2(0.55, 1.9);
                cp.x += t * _IoriCloudParams.z;
                float c = Fbm2(cp + float2(3.1, 0.7));
                float amount = lerp(_IoriCloudParams.x, 1.0, overcast);
                float th = lerp(0.68, 0.36, amount);
                float low = smoothstep(0.015, 0.07, h);
                float cm = step(th, c) * low * (1.0 - smoothstep(lerp(0.5, 0.9, amount), 1.0, h) * (1.0 - overcast));
                float inner = step(th + 0.07, c);
                float cloudLit = saturate(0.35 + 0.65 * pow(sd, 1.5)) * (1.0 - 0.6 * inner * (1.0 - sd));
                float3 cc = lerp(_IoriCloudDark.rgb, _IoriCloud.rgb, saturate(cloudLit + (rnd - 0.5) * 0.25));
                col = lerp(col, cc, cm * saturate(amount * 1.6 + 0.25));

                // ---- stars and moon (hidden by cloud)
                float clear = 1.0 - cm;
                float2 sg = uv * 46.0;
                float2 sid = floor(sg), sf = frac(sg) - 0.5;
                float sh = IoriHash1(sid);
                float2 sp = (float2(IoriHash1(sid + 3.7), IoriHash1(sid + 9.1)) - 0.5) * 0.7;
                float big = step(0.985, sh);
                float sr = lerp(0.035, 0.085, big) * (0.6 + 0.4 * IoriHash1(sid + 5.3));
                float sdist = length(sf - sp);
                float twinkle = 0.7 + 0.3 * sin(t * (1.0 + sh * 3.0) + sh * 40.0);
                float star = smoothstep(sr, sr * 0.45, sdist) * step(0.80, sh);
                // the brightest ones get a small cross
                float2 q = abs(sf - sp);
                star += big * smoothstep(0.02, 0.0, min(q.x, q.y)) * smoothstep(0.32, 0.0, max(q.x, q.y)) * 0.6;
                col += float3(0.92, 0.95, 1.0) * (star * stars * twinkle * clear * smoothstep(0.03, 0.2, d.y) * (0.5 + 0.9 * IoriHash1(sid + 1.3)) * 1.6);

                if (moon > 0.001)
                {
                    // a waxing moon: a disc with another disc bitten out of it, and a faint ring of light
                    float3 moonDir = normalize(_IoriMoonDir.xyz);
                    float md = dot(d, moonDir);
                    float3 side = normalize(cross(moonDir, float3(0, 1, 0)));
                    float3 up = cross(side, moonDir);
                    float2 m = float2(dot(d, side), dot(d, up)) / 0.017;
                    float disc = smoothstep(1.0, 0.94, length(m)) * step(0.0, md);
                    float bite = smoothstep(1.0, 0.94, length(m - float2(0.42, 0.18)) / 0.98);
                    float shape = disc * (1.0 - bite * 0.93);
                    col += _IoriMoonColor.rgb * (pow(saturate(dot(dc, moonDir)), 60.0) * 0.16 * moon * clear);
                    col = lerp(col, _IoriMoonColor.rgb * 2.6, shape * moon * clear);
                }
                if (sunDisc > 0.001)
                {
                    float sdsk = smoothstep(0.99955, 0.9997, dot(d, sunDir));
                    col = lerp(col, _IoriSunColor.rgb * 6.0, sdsk * sunDisc * clear);
                }

                // ---- mountains across the lake. The outlines are cut pixel by pixel (crisp, angular);
                // their faces are filled with a finer set of strokes: a few tones, lighter where a slope turns
                // toward the light, paler toward the water.
                float azp = atan2(d.x, d.z), elp = asin(clamp(d.y, -1.0, 1.0)) / DEG;
                float3 top, slope;
                float2 town;
                Ridges(azp, top, slope, town);
                float4 ms = SAMPLE_TEXTURE2D(_IoriStrokeTex, sampler_trilinear_repeat, float2(azp, elp * DEG) * _MountTiles);
                float mrnd = ms.b;
                float elc = elp + (0.5 - ms.g) * (2.0 * IORI_STROKE_MAX_OFF) / _MountTiles / DEG;   // height of the stroke's center
                float3 lightDir = lerp(sunDir, normalize(_IoriMoonDir.xyz), saturate(moon));
                float side = sign(azp - atan2(lightDir.x, lightDir.z));
                float3 air = _IoriFogColor.rgb;
                float3 behind = col;
                float aa = max(fwidth(elp), 1e-4);
                for (int k = 2; k >= 0; k--)
                {
                    float tk = k == 2 ? top.z : (k == 1 ? top.y : top.x);
                    float sk = k == 2 ? slope.z : (k == 1 ? slope.y : slope.x);
                    float depth = k == 2 ? 1.0 : (k == 1 ? 0.5 : 0.0);
                    float inside = saturate((tk - elp) / aa + 0.5) * step(0.02, tk);
                    float3 mc = lerp(_IoriFarNear.rgb, _IoriFarFar.rgb, depth);
                    mc += glowCol * pow(sdFlat, 4.0) * depth * 0.10;                 // the glow behind lifts the far ones
                    // faces: under the crest, the strokes on the side of the light are lighter; a few are darker
                    float under = saturate(1.0 - (tk - elc) / (2.2 + depth));
                    float facing = saturate(sk * side * 1.5 + 0.15);
                    float faceLit = step(0.42, mrnd) * under * facing;
                    mc = lerp(mc, lerp(mc, behind, 0.5) * 1.06, faceLit * (0.55 - 0.2 * depth));
                    mc *= 1.0 + (mrnd - 0.5) * 0.10 - step(mrnd, 0.14) * 0.07 * (1.0 - depth);
                    // paler toward the water: mist lies on the lake
                    float low = (1.0 - smoothstep(0.0, 2.8, elc)) * (0.12 + 0.30 * depth);
                    mc = lerp(mc, _IoriMistColor.rgb, low);
                    col = lerp(col, mc, inside);
                }

                // ---- lights of the town (per pixel: they are tiny). Thick near the shore, thinning as they climb.
                float2 lg = float2(azp / DEG, elp) / 0.2;
                float2 lid = floor(lg), lf = frac(lg) - 0.5;
                float lh = IoriHash1(lid + 17.0);
                float climb = saturate(1.0 - elp / max(town.y, 0.05));
                float chance = town.x * (0.45 * pow(climb, 0.8) + 0.35 * step(elp, 0.45));
                float land = step(elp, max(max(top.x, top.y), 0.5)) * step(-0.05, elp);
                float on = step(1.0 - chance, lh) * land;
                float2 lp = (float2(IoriHash1(lid + 2.2), IoriHash1(lid + 6.6)) - 0.5) * 0.6;
                float lsize = lerp(0.16, 0.34, IoriHash1(lid + 8.8));
                float ldot = smoothstep(lsize, lsize * 0.35, length(lf - lp));
                float lt = 0.8 + 0.2 * sin(t * (0.7 + lh * 2.0) + lh * 30.0);
                float3 lcol = _IoriTown.rgb * lerp(float3(1.0, 0.66, 0.36), float3(1.0, 0.92, 0.74), step(0.7, IoriHash1(lid + 4.4)));

                // ---- the far shore: a low, dark line of trees right on the water, every tree its own height
                float shore = 0.2 + Lin1(azp * 9.0, 40.0) * 0.34 + Lin1(azp * 31.0, 41.0) * 0.16;
                float tx = azp * 170.0 + Lin1(azp * 40.0, 44.0) * 2.0;
                float th1 = IoriHash1(float2(floor(tx), 43.0));
                float tw = 0.22 + 0.25 * IoriHash1(float2(floor(tx), 47.0));
                shore += saturate(1.0 - abs(frac(tx) - 0.5) / tw) * step(0.3, th1) * th1 * 0.34;
                float tx2 = azp * 61.0 + 3.7;
                float th2 = IoriHash1(float2(floor(tx2), 53.0));
                shore += saturate(1.0 - abs(frac(tx2) - 0.5) / 0.5) * th2 * 0.22;
                float inShore = saturate((shore - elp) / aa + 0.5);
                float3 shoreCol = lerp(_IoriFarNear.rgb * 0.7, air, 0.22);
                col = lerp(col, shoreCol, inShore);
                col += lcol * (ldot * on * lt) * (1.0 - inShore * 0.4);

                // ---- haze low over the water, and plain air below the horizon
                float haze = (1.0 - smoothstep(0.0, 3.2, elp)) * saturate(_IoriFog.y * 40.0);
                col = lerp(col, _IoriMistColor.rgb, haze * 0.6 * (1.0 - 0.5 * inShore));
                col = lerp(col, air, smoothstep(-0.2, -3.0, elp));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
