// Everything too far away to ever move against anything else, painted into the skybox:
// the sky itself (three colors, laid down as large brush strokes), sun glow, moon, stars, clouds,
// the mountain ranges across the lake with the lights of a town at their feet, the far shore's
// tree line, and the haze over the water. Being the skybox, all of it is mirrored in the lake
// and the puddles by the reflection camera at no extra cost.
// The camp looks toward +z; azimuth 0 is straight across the lake.
Shader "IORI/Camp/Sky"
{
    Properties { }
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

            // straight-edged noise: mountain outlines with corners instead of curves
            float Lin1(float x, float seed)
            {
                float i = floor(x), f = frac(x);
                return lerp(IoriHash1(float2(i, seed)), IoriHash1(float2(i + 1.0, seed)), f);
            }
            float RidgeLine(float az, float seed, float freq)
            {
                return Lin1(az * freq, seed) * 0.6 + Lin1(az * freq * 2.7 + 11.0, seed + 1.0) * 0.28 + Lin1(az * freq * 6.3 + 5.0, seed + 2.0) * 0.12;
            }
            float Fbm2(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { s += a * IoriNoise2(p); p = p * 2.03 + 7.1; a *= 0.5; }
                return s;
            }

            #define DEG 0.0174533

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

                // ---- mountains across the lake: far to near, each cut out stroke by stroke
                float az = atan2(dc.x, dc.z);
                float el = asin(clamp(dc.y, -1.0, 1.0));
                float valley = 0.55 + 0.45 * smoothstep(0.1, 0.9, abs(az - 0.25));   // lower straight ahead, so the horizon glow shows
                float3 air = _IoriFogColor.rgb;
                float mistBand = 0.0;
                float ridge = 0.0;
                for (int k = 3; k >= 1; k--)
                {
                    float fk = k;
                    float top = (0.5 + fk * 0.75 + RidgeLine(az, 20.0 + fk * 7.0, 3.5 + (3.0 - fk) * 2.6) * (1.6 + fk * 1.9) * valley) * DEG;
                    float inside = step(el, top);
                    float depth = fk / 3.0;
                    float3 mc = lerp(_IoriFarNear.rgb, _IoriFarFar.rgb, depth);
                    // strokes near the crest catch a little of the sky; the rest vary a touch
                    float crest = saturate(1.0 - (top - el) / (1.6 * DEG));
                    mc = lerp(mc, lerp(mc, col, 0.35), crest * step(0.45, rnd));
                    mc *= 1.0 + (rnd - 0.5) * 0.16;
                    // the glow of the light behind the mountains lifts the far ones
                    mc += glowCol * pow(sdFlat, 4.0) * depth * 0.10;
                    col = lerp(col, mc, inside);
                    ridge = max(ridge, inside);
                }

                // ---- lights of the town on the far shore (per pixel: they are tiny)
                float azp = atan2(d.x, d.z), elp = asin(clamp(d.y, -1.0, 1.0));
                float2 lg = float2(azp, elp) / (0.2 * DEG);
                float2 lid = floor(lg), lf = frac(lg) - 0.5;
                float lh = IoriHash1(lid + 17.0);
                float cluster = smoothstep(0.42, 0.75, Lin1(azp * 7.0, 91.0) * 0.7 + Lin1(azp * 23.0, 92.0) * 0.3);
                float band = smoothstep(-0.25 * DEG, 0.1 * DEG, elp) * (1.0 - smoothstep(0.5 * DEG, (0.7 + 1.6 * cluster) * DEG, elp));
                float on = step(1.0 - 0.34 * cluster * band, lh);
                float2 lp = (float2(IoriHash1(lid + 2.2), IoriHash1(lid + 6.6)) - 0.5) * 0.6;
                float ldot = smoothstep(0.34, 0.1, length(lf - lp));
                float lt = 0.8 + 0.2 * sin(t * (0.7 + lh * 2.0) + lh * 30.0);
                float3 lcol = _IoriTown.rgb * lerp(float3(1.0, 0.72, 0.42), float3(1.0, 0.9, 0.7), IoriHash1(lid + 4.4));

                // ---- the far shore: a low, toothed line of firs right on the water
                float shore = (0.28 + Lin1(azp * 9.0, 40.0) * 0.5 + Lin1(azp * 31.0, 41.0) * 0.22) * DEG;
                float tooth = abs(frac(azp * 150.0 + Lin1(azp * 40.0, 44.0) * 3.0) - 0.5) * 2.0;
                shore += (1.0 - tooth) * (0.10 + 0.22 * Lin1(azp * 55.0, 43.0)) * DEG;
                float inShore = step(elp, shore);
                float3 shoreCol = _IoriFarNear.rgb * 0.62;
                col = lerp(col, shoreCol, inShore);
                col += lcol * (ldot * on * lt) * (1.0 - inShore * 0.5);

                // ---- haze low over the water, and plain air below the horizon
                float haze = (1.0 - smoothstep(0.0, 3.2 * DEG, elp)) * saturate(_IoriFog.y * 14.0);
                col = lerp(col, _IoriMistColor.rgb, haze * 0.55 * (1.0 - 0.5 * inShore));
                col = lerp(col, air, smoothstep(-0.2 * DEG, -3.0 * DEG, elp));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
