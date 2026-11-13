// Everything too far away to ever move against anything else, painted into the skybox:
// the sky itself (three colors, laid down as large brush strokes), sun glow, moon, stars, clouds,
// the mountain ranges across the lake with the lights of a town at their feet, the far shore's
// tree line, and the haze over the water. Being the skybox, all of it is mirrored in the lake
// and the puddles by the reflection camera at no extra cost (all but the stars and the town's lights:
// see "direct" below).
// The camp looks toward +z; azimuth 0 is straight across the lake.
// The mountains' outlines are a drawn composition (tools/camp/make_ridges.py -> iori_ridges.png, see IoriFar.hlsl).
Shader "IORI/Camp/Sky"
{
    Properties
    {
        _MountTiles ("Fineness of the strokes on the mountains", Float) = 5
        _FacetLean ("How far the mountains' faces lean away from their peaks", Float) = 0.7
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
            #include "IoriFar.hlsl"

            float4 _IoriSkyHorizon, _IoriSkyMid, _IoriSkyZenith;
            float4 _IoriSunDir;        // xyz: toward the sun (it may be below the horizon)
            float4 _IoriSunColor;      // rgb: color of its glow
            float4 _IoriMoonDir;       // xyz: toward the moon
            float4 _IoriMoonColor;
            float4 _IoriSkyParams;     // x: glow, y: moon disc, z: stars, w: sun disc
            float4 _IoriCloud, _IoriCloudDark;
            float4 _IoriCloudParams;   // x: how much cloud, y: overcast (rain), z: drift speed
            float4 _IoriFarNear, _IoriFarFar;   // colors of the nearest and the farthest range
            float4 _IoriSkyStroke;     // x: stroke tiles per unit of the dome, y: stroke amount
            CBUFFER_START(UnityPerMaterial)
                float _MountTiles, _FacetLean;
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

            #define DEG IORI_DEG

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

                // The mirror cameras (the only ones that ever sit below the ground) leave out the stars and the
                // lights of the town: such small points only flicker in a mirror image, and the lake draws the
                // town's lights on the water itself (IoriWater).
                float direct = step(0.0, _WorldSpaceCameraPos.y);

                float3 sunDir = normalize(_IoriSunDir.xyz);
                float glow = _IoriSkyParams.x, moon = _IoriSkyParams.y, stars = _IoriSkyParams.z, sunDisc = _IoriSkyParams.w;
                float overcast = _IoriCloudParams.y;

                // ---- sky: horizon to zenith, one flat color per stroke
                float h = saturate(dc.y);
                float hj = h + (rnd - 0.5) * 0.05;
                // (the change of color sits low: a phone held upright sees only the lowest 15 degrees of sky)
                float3 col = lerp(_IoriSkyHorizon.rgb, _IoriSkyMid.rgb, smoothstep(0.0, 0.17, hj));
                col = lerp(col, _IoriSkyZenith.rgb, smoothstep(0.10, 0.46, hj));
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
                col += float3(0.92, 0.95, 1.0) * (star * stars * twinkle * clear * direct * smoothstep(0.03, 0.2, d.y) * (0.5 + 0.9 * IoriHash1(sid + 1.3)) * 1.6);

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
                IoriRidges(azp, top, slope, town);
                float4 ms = SAMPLE_TEXTURE2D(_IoriStrokeTex, sampler_trilinear_repeat, float2(azp, elp * DEG) * _MountTiles);
                float mrnd = ms.b;
                float elc = elp + (0.5 - ms.g) * (2.0 * IORI_STROKE_MAX_OFF) / _MountTiles / DEG;   // height of the stroke's center
                float3 lightDir = lerp(sunDir, normalize(_IoriMoonDir.xyz), saturate(moon));
                float side = sign(azp - atan2(lightDir.x, lightDir.z));
                float3 air = _IoriFogColor.rgb;
                float3 behind = col;
                float aa = max(fwidth(elp), 1e-4);
                // A mountain's faces run down from its peaks at a slant, not straight down: a point below the
                // crest belongs to the piece of crest that lies uphill from it, toward the peak. So the slope is
                // read there, and the borders between lit and shaded faces fan out from the peaks.
                float3 climbs = IoriRidgeLean(azp);
                float3 uphill = azp + max(top - elc, 0.0) * clamp(climbs * 1.5, -1.0, 1.0) * (_FacetLean * DEG);
                float climbMain = IoriRidgeLean(uphill.y).y, climbFar = IoriRidgeLean(uphill.z).z;
                for (int k = 2; k >= 0; k--)
                {
                    float tk = k == 2 ? top.z : (k == 1 ? top.y : top.x);
                    float sk = k == 2 ? climbFar : (k == 1 ? climbMain : climbs.x);
                    float depth = k == 2 ? 1.0 : (k == 1 ? 0.5 : 0.0);
                    float inside = saturate((tk - elp) / aa + 0.5) * step(0.02, tk);
                    float3 mc = lerp(_IoriFarNear.rgb, _IoriFarFar.rgb, depth);
                    mc += glowCol * pow(sdFlat, 4.0) * depth * 0.10;                 // the glow behind lifts the far ones
                    // faces: under the crest, the strokes on the side of the light are lighter; a few are darker
                    float under = saturate(1.0 - (tk - elc) / (2.6 + depth));
                    float facing = saturate(sk * side * 1.5 + 0.15);
                    float faceLit = saturate(floor(under * facing * 3.0 + mrnd) / 3.0);
                    mc = lerp(mc, lerp(mc, behind, 0.35) * 1.12, faceLit * (0.75 - 0.25 * depth));
                    mc *= 1.0 + (mrnd - 0.5) * 0.10 - step(mrnd, 0.14) * 0.07 * (1.0 - depth);
                    // paler toward the water: mist lies on the lake
                    float low = (1.0 - smoothstep(0.0, 2.8, elc)) * (0.12 + 0.30 * depth);
                    mc = lerp(mc, _IoriMistColor.rgb, low);
                    col = lerp(col, mc, inside);
                }

                // ---- lights of the town (per pixel: they are tiny). Thick near the shore, thinning as they climb.
                float2 tl = IoriTownLight(float2(azp / DEG, elp), town, max(top.x, top.y), 1.0, t);

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
                col += IoriTownColor(tl.y) * (tl.x * direct * (1.0 - inShore * 0.4));

                // ---- haze low over the water, and plain air below the horizon
                float haze = (1.0 - smoothstep(0.0, 3.2, elp)) * saturate(_IoriFog.y * 40.0);
                col = lerp(col, _IoriMistColor.rgb, haze * 0.6 * (1.0 - 0.5 * inShore));
                col = lerp(col, air, smoothstep(-0.2, -3.0, elp));
                // the air around the lamps glows in front of the far view as well
                col += IoriLampAir(_WorldSpaceCameraPos + d * 3000.0);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
