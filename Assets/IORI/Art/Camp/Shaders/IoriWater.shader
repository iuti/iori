// The lake and the puddles. Both show the mirror image that CampReflection renders (a camera
// mirrored under the water, so its picture is flipped left to right).
//
// The lake is a calm mirror, painted in rows of ripples: thin rows at the far shore, taller ones
// toward the viewer. Every row is cut into lengths, and each length shows the mirror image pushed
// a little to the side, slowly swaying. So everything in the mirror keeps its shape and its colors,
// and only its edges break up into steps, the way a painter lays a reflection down. Nothing blinks.
// On top of that come three kinds of light, all made of short flat dabs lying in those rows:
//   - every light of the town on the far shore has its own mirror image, and under it a ladder of
//     dabs running toward the viewer, fewer the farther down (worked out from the same rule the sky
//     uses to place the lights, IoriFar.hlsl; the mirror camera leaves the lights themselves out);
//   - under the moon or the low sun lies a path of dabs that catch its light, each fading in and out;
//   - here and there a long thin lane of ruffled water shows the sky.
// The puddles are small and close: they are painted a brush stroke at a time like the ground, and a
// few strokes reach toward the horizon for what they show, which draws the fire out into a column.
// Where the water is shallow the bottom shows through (needs the depth texture; see _IoriDepthFx).
Shader "IORI/Camp/Water"
{
    Properties
    {
        [Toggle(_PUDDLE)] _Puddle ("Puddle (off: lake)", Float) = 0
        _StrokeTiles ("Puddles: stroke fineness", Float) = 9
        _Wobble ("Swaying of the mirror image", Float) = 1
        _Streak ("Puddles: how far lights are drawn out (part of the picture's height)", Float) = 0
        _Columns ("Lake: lights of the far shore on the water", Range(0, 2)) = 1
        _ColumnLength ("  How far they run toward the viewer (degrees)", Float) = 1.1
        _Glitter ("Lake: path of light under the moon or the low sun", Range(0, 2)) = 1
        _Lanes ("Lake: lanes of ruffled water that show the sky", Range(0, 1)) = 0.45
        _Air ("Most that haze may hide of the water", Range(0, 1)) = 0.5
        _Tint ("Tint (lake: set by the scene)", Color) = (0.2, 0.25, 0.5, 1)
        _Mirror ("Mirror strength looking straight down", Range(0, 1)) = 0.45
        _Shallow ("Depth at which the bottom is hidden (m)", Float) = 0.35
    }
    SubShader
    {
        // drawn after the opaque things so the depth texture already holds what lies under the water
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _PUDDLE
            #include "IoriCommon.hlsl"
            #include "IoriFar.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Puddle, _StrokeTiles, _Wobble, _Mirror, _Shallow, _Streak, _Air, _Columns, _ColumnLength, _Glitter, _Lanes;
                float4 _Tint;
            CBUFFER_END
            float4 _IoriSkyMid;         // the sky's color a little above the horizon
            float4 _IoriSunDir, _IoriMoonDir, _IoriSunColor, _IoriMoonColor;
            float4 _IoriSkyParams;      // y: moon, w: sun disc
            float4 _IoriWaterTint;      // lake tint for the time of day
            float4 _IoriPuddleTint;
            float _IoriDepthFx;         // 1 when the camera depth texture is there
            TEXTURE2D(_IoriReflectionTex);          // mirror image for the lake
            TEXTURE2D(_IoriReflectionTexPuddle);    // mirror image for the puddles (they lie higher than the lake)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float Rings(float2 p, float t)
            {
                float acc = 0.0;
                for (int k = 0; k < 2; k++)
                {
                    float fk = k;
                    float2 g = p * (2.3 + fk * 1.4) + fk * 7.3;
                    float2 id = floor(g), f = frac(g) - 0.5;
                    float h = IoriHash1(id + fk * 3.1);
                    float tt = frac(t * (0.8 + 0.4 * h) + h * 7.0);
                    float2 c = (float2(IoriHash1(id + 1.7), IoriHash1(id + 5.3)) - 0.5) * 0.5;
                    float dd = length(f - c) - tt * 0.42;
                    acc += smoothstep(0.035, 0.0, abs(dd)) * (1.0 - tt);
                }
                return acc;
            }

            // Rows of ripples, counted from the far shore: dep is how far below the horizon the water is seen
            // (degrees). A row covers little of the picture far away and more close by.
            float Rows(float dep, float k) { return k * sqrt(max(dep, 0.0) + 0.02); }
            float RowHeight(float dep, float k) { return 2.0 * sqrt(max(dep, 0.0) + 0.02) / k; }

        #ifndef _PUDDLE
            // ---------------------------------------------------------------- the lake
            // edge: a number per length of ripple, for the broken edge of the water at the shore
            float3 Lake(float3 wp, float2 suv, float t, float rain, out float fres, out float edge)
            {
                // Where this bit of water lies as seen from the camera's home (the ripples are laid out from there,
                // so they stay on the water when the camera moves), and as seen from the camera itself (what it mirrors).
                float3 hv = wp - _IoriStrokeOrigin.xyz;
                float azH = atan2(hv.x, hv.z) / IORI_DEG;
                float depH = atan2(-hv.y, length(hv.xz)) / IORI_DEG;
                float3 ray = normalize(wp - _WorldSpaceCameraPos);
                float azW = atan2(ray.x, ray.z) / IORI_DEG;
                float depW = asin(saturate(-ray.y)) / IORI_DEG;
                float dist = distance(wp, _WorldSpaceCameraPos);

                // ---- the mirror image, pushed aside a length of ripple at a time
                float rf = Rows(depH, 9.0);
                float row = floor(rf);
                float clen = 1.4 * (0.6 + 0.8 * IoriHash1(float2(row, 3.0))) * (0.5 + 0.5 * sqrt(max(depH, 0.05)));
                float cf = azH / clen + IoriHash1(float2(row, 9.0)) * 7.0;
                float cell = floor(cf);
                float h1 = IoriHash1(float2(cell + row * 13.0, row + 5.0)), h2 = IoriHash1(float2(cell + 3.0, row * 7.0 + 1.0));
                edge = h1;
                // (right at the far shore the water is seen so flat that it mirrors without a break)
                float amp = 0.05 * _Wobble * smoothstep(0.1, 1.2, depH) * (0.5 + 0.5 * sqrt(depH / 3.0)) * (1.0 + rain * 2.0);
                float ph = h1 * 6.283 + t * (0.5 + 0.7 * h2);
                float2 push = float2(sin(ph) * amp + (h2 - 0.5) * amp * 1.2, (h1 - 0.5) * 0.6 * amp);
                float ringsV = rain > 0.01 ? Rings(wp.xz * 0.6, t) * rain * saturate(14.0 / dist) : 0.0;
                // degrees to parts of the picture
                float2 uv = suv + push * (IORI_DEG * 0.5) * float2(unity_CameraProjection._m00, unity_CameraProjection._m11) + ringsV * 0.01;
                uv.x = 1.0 - uv.x;
                float3 refl = SAMPLE_TEXTURE2D_LOD(_IoriReflectionTex, sampler_LinearClamp, saturate(uv), 0).rgb;

                // ---- how much mirror, how much water
                float3 viewDir = normalize(_WorldSpaceCameraPos - wp);
                fres = pow(1.0 - saturate(viewDir.y), 3.0);
                float mirror = lerp(_Mirror, 1.0, fres) * (1.0 - rain * 0.25);
                float3 col = lerp(_IoriWaterTint.rgb * _Tint.rgb, refl, saturate(mirror));
                col += _IoriAmbientSky.rgb * ringsV * 0.25;

                // ---- lanes of ruffled water: long, thin, pointed at both ends, showing the sky instead of the far shore
                float lr = Rows(depH, 7.2) + 0.37;
                float lrow = floor(lr), lfr = frac(lr);
                float llen = 4.5 * (0.6 + 0.9 * IoriHash1(float2(lrow, 21.0)));
                float lcf = azH / llen + IoriHash1(float2(lrow, 23.0)) * 5.0 + t * 0.004 * (IoriHash1(float2(lrow, 29.0)) - 0.5);
                float lcell = floor(lcf), lu = (frac(lcf) - 0.5) * 2.0;
                float lhalf = 0.45 + 0.5 * IoriHash1(float2(lcell + 1.0, lrow + 37.0));
                float lx = lu / lhalf;
                float lane = smoothstep(-0.03, 0.05, (1.0 - lx * lx) * 0.16 - abs(lfr - 0.5)) * step(abs(lx), 1.0);
                lane *= step(0.7, IoriHash1(float2(lcell, lrow + 31.0))) * smoothstep(0.25, 1.0, depH) * (1.0 - rain * 0.5);
                col = lerp(col, _IoriSkyMid.rgb * 0.9, lane * _Lanes);

                // ---- the lights of the far shore. Each has its mirror image as far below the horizon as it stands
                // above it, and from there a ladder of dabs toward the viewer: one to a row of ripples, each a little
                // to the side, fewer and fainter the farther down. The rows drift slowly toward the viewer.
                // (A bit of water only looks at the lights whose image lies up to a degree and a half above it:
                // nine cells of the town's grid. Looking at every light in its column cost several times as much.)
                float3 top, slope;
                float2 town;
                IoriRidges(azW * IORI_DEG, top, slope, town);
                float landTop = max(top.x, top.y);
                float jhead = floor(depW / 0.2);
                float jtop = min(floor(IoriTownTop(town, landTop) / 0.2), 19.0);
                if (town.x > 0.01 && _Columns > 0.001 && jhead - 7.0 <= jtop)
                {
                    float lgx = azW / 0.2;
                    float lidx = floor(lgx), lfx = frac(lgx) - 0.5;
                    float sr = Rows(depH, 10.0) - t * 0.10 + IoriHash1(float2(lidx, 91.0));
                    float srow = floor(sr), sfr = frac(sr);
                    float rowh = RowHeight(depH, 10.0);
                    float3 lights = 0;
                    for (int k = -1; k < 8; k++)
                    {
                        float fj = jhead - k;
                        if (fj < 0.0) break;
                        if (fj > jtop) continue;
                        float2 lid = float2(lidx, fj);
                        float lh;
                        if (IoriTownOn(lid, town, landTop, lh) < 0.5) continue;
                        float2 lp;
                        float lsize;
                        IoriTownPlace(lid, lp, lsize);
                        float d = depW - (fj + 0.5 + lp.y) * 0.2;          // degrees below the light's mirror image
                        float hl = IoriHash1(lid + float2(5.5, 12.1));
                        float len = max(_ColumnLength, 0.05) * (0.25 + 1.4 * hl * hl * hl);
                        // the image itself: a dab right under the light, a little wider than the light
                        float room = max(0.5 - abs(lp.x) - 0.02, 0.05);
                        float w0 = min(lsize * 1.5, room);
                        float head = smoothstep(1.0, 0.5, length(float2((lfx - lp.x) / w0, d / (lsize * 0.2))));
                        // the ladder below it
                        float a = IoriHash1(float2(srow + lidx * 3.0, fj + 41.0)), b = IoriHash1(float2(srow + lidx * 7.0, fj + 59.0));
                        float wv = min(w0 * (1.0 + 0.5 * clamp(d, 0.0, 4.0)) * (0.7 + 0.6 * b), room);
                        float zig = (a - 0.5) * 2.0 * min(room - wv, wv * 0.8);
                        float u = min(abs(lfx - lp.x - zig) / wv, 1.0);
                        // (a dab is wider than it is high: its height goes by its width, not by the row)
                        float thick = min(0.38, wv * 0.11 / rowh) * (1.0 - u * u * u);
                        float dab = smoothstep(-0.04, 0.06, thick - abs(sfr - 0.5)) * step(u, 0.999);
                        float fall = exp(-max(d, 0.0) / len);
                        float keep = smoothstep(0.0, 0.1, fall * 1.15 - b) * smoothstep(1.7, 1.2, d);
                        float below = smoothstep(lsize * 0.1, lsize * 0.25, d);
                        float k1 = max(head, dab * keep * below * (0.55 + 0.45 * fall * fall)) * (0.85 + 0.15 * sin(t * (0.7 + lh * 2.0) + lh * 30.0));
                        lights += IoriTownColor(step(0.7, IoriHash1(lid + 4.4))) * k1;
                    }
                    col += lights * (_Columns * 0.75 * (1.0 - rain * 0.5));
                }

                // ---- the path of light under the moon (or the low sun): flat dabs that catch it, many right
                // under it and fewer to the sides, each one fading in and out over a few seconds
                float moonUp = _IoriSkyParams.y, sunUp = _IoriSkyParams.w;
                bool byMoon = moonUp > sunUp;
                float3 gdir = normalize(byMoon ? _IoriMoonDir.xyz : _IoriSunDir.xyz);
                float3 gcol = byMoon ? _IoriMoonColor.rgb * moonUp : _IoriSunColor.rgb * (sunUp * 2.0);
                float gate = _Glitter * (1.0 - rain) * smoothstep(0.0, 6.0, asin(gdir.y) / IORI_DEG);
                if (gate * max(gcol.r, max(gcol.g, gcol.b)) > 0.001)
                {
                    float gr = Rows(depH, 15.0) + 0.61;
                    float grow = floor(gr), gfr = frac(gr);
                    float glen = 0.8 * (0.7 + 0.6 * IoriHash1(float2(grow, 71.0))) * (0.35 + 0.65 * sqrt(max(depH, 0.02) / 2.0));
                    float slide = IoriHash1(float2(grow, 73.0)) * 3.0 + t * 0.03 * (IoriHash1(float2(grow, 79.0)) - 0.5);
                    float gcf = azH / glen + slide;
                    float gcell = floor(gcf), gu = (frac(gcf) - 0.5) * 2.0;
                    float g1 = IoriHash1(float2(gcell + grow * 17.0, grow + 83.0)), g2 = IoriHash1(float2(gcell + 9.0, grow * 3.0 + 89.0));
                    float gx = min(abs(gu) / (0.55 + 0.4 * g2), 1.0);
                    float gdab = smoothstep(-0.02, 0.06, (0.14 + 0.2 * g1) * (1.0 - gx * gx * gx) - abs(gfr - 0.5)) * step(gx, 0.999);
                    // the middle of the dab decides how likely it is to catch the light
                    float dAz = azW - gu * 0.5 * glen - atan2(gdir.x, gdir.z) / IORI_DEG;
                    dAz -= 360.0 * round(dAz / 360.0);
                    float widthG = 0.07 + 0.22 * depW;
                    float catches = exp(-(dAz * dAz) / (widthG * widthG)) * smoothstep(0.03, 0.4, depW);
                    float tau = t * 0.35 + g1 * 11.0;
                    float ft = frac(tau);
                    float n = lerp(IoriHash1(float2(gcell + floor(tau) * 0.37, grow + g2 * 5.0)), IoriHash1(float2(gcell + (floor(tau) + 1.0) * 0.37, grow + g2 * 5.0)), ft * ft * (3.0 - 2.0 * ft));
                    float thr = 1.0 - catches * 0.95;
                    float lit = smoothstep(thr - 0.06, thr + 0.06, n);
                    col += gcol * (gdab * lit * (0.55 + 0.45 * smoothstep(0.2, 0.9, catches)) * gate * 0.85);
                }
                return col;
            }

        #else
            // ---------------------------------------------------------------- the puddles
            float3 Puddle(float3 wp, float t, float rain, out float fres, out float edge)
            {
                // which stroke: the surface as seen from the camera's home position
                float3 o = _IoriStrokeOrigin.xyz;
                float hgt = max(o.y - wp.y, 0.2);
                float dz = max(wp.z - o.z, 0.05);
                float2 pv = float2((wp.x - o.x) / dz, hgt / dz);
                float tiles = _StrokeTiles;
                float4 sm = SAMPLE_TEXTURE2D(_IoriStrokeTex, sampler_trilinear_repeat, pv * tiles);
                float2 off = float2(sm.r - 0.5, 0.5 - sm.g) * (2.0 * IORI_STROKE_MAX_OFF) / tiles * _IoriStroke.z;
                float2 pc = pv + off;
                pc.y = max(pc.y, 0.0006);
                float cz = hgt / pc.y;
                float3 center = float3(o.x + pc.x * cz, wp.y, o.z + cz);
                float rnd = sm.b;
                edge = rnd;

                // the mirror image at the stroke's center, trembling a little
                float4 ccs = TransformWorldToHClip(center);
                float4 csp = ComputeScreenPos(ccs);
                float2 uv = csp.xy / max(csp.w, 1e-4);
                float dist = distance(center, _WorldSpaceCameraPos);
                float ph = rnd * 6.283 + t * (0.6 + rnd * 1.1);
                float2 wob = float2(sin(ph * 1.3 + 1.0) * 0.003, sin(ph) * 0.005 + (rnd - 0.5) * 0.009) * _Wobble * 0.25;
                wob *= (1.0 + rain * 2.0) * saturate(8.0 / dist + 0.25);
                float ringsV = rain > 0.01 ? Rings(wp.xz, t) * rain * saturate(14.0 / dist) : 0.0;
                uv += wob + ringsV * 0.01;
                // most strokes stay close; a few show what lies well toward the horizon (always that way: a
                // light's column runs from its image toward the viewer). Which ones changes slowly.
                float r3 = frac(rnd * 13.7 + 0.31);
                float tail = r3 * r3 * r3 * (0.7 + 0.3 * sin(ph * 0.9 + 2.0));
                // never past the horizon: above it the mirror image holds nothing
                float toHorizon = 0.5 * unity_CameraProjection._m11 * max(_WorldSpaceCameraPos.y - wp.y, 0.0) / max(ccs.w, 0.05);
                uv.y += min(tail * _Streak * (1.0 + rain * 0.6), toHorizon * 0.9);
                uv.x = 1.0 - uv.x;
                float3 refl = SAMPLE_TEXTURE2D_LOD(_IoriReflectionTexPuddle, sampler_LinearClamp, saturate(uv), 0).rgb;

                float3 viewDir = normalize(_WorldSpaceCameraPos - center);
                fres = pow(1.0 - saturate(viewDir.y), 3.0);
                float mirror = lerp(_Mirror, 1.0, fres) * (1.0 - rain * 0.25);
                float3 col = lerp(_IoriPuddleTint.rgb, refl, saturate(mirror));
                col *= 1.0 + sm.a * 0.08 - 0.04;
                col += _IoriAmbientSky.rgb * ringsV * 0.25;
                return col;
            }

        #endif

            half4 frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                float t = IoriTime();
                float rain = _IoriWeather.x;
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float fres, edge;
            #ifdef _PUDDLE
                float3 col = Puddle(wp, t, rain, fres, edge);
            #else
                float3 col = Lake(wp, suv, t, rain, fres, edge);
            #endif

                // ---- shallow water lets the bottom through
                float alpha = 1.0;
                if (_IoriDepthFx > 0.5)
                {
                    float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float mine = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                    float viewY = normalize(_WorldSpaceCameraPos - wp).y;
                    float through = max(scene - mine, 0.0) * saturate(viewY * 3.0 + 0.15);
                    // the edge of the water is broken too
                    alpha = saturate(through / max(_Shallow, 0.01) + (edge - 0.5) * 0.5);
                    alpha = lerp(alpha, 1.0, fres * 0.85);
                }
                // the mirror image already carries the air of what it shows; only part of the haze is laid over it
                float air = min(IoriAirAmount(wp) * 0.6, _Air);
                col = lerp(col, lerp(_IoriFogColor.rgb, _IoriMistColor.rgb, 0.5), air);
                col += IoriLampAir(wp);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
