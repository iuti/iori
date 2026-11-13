// The lake and the puddles. Both show the shared mirror image that CampReflection renders
// (a camera mirrored under the water, so its picture is flipped left to right).
//
// Water is painted in strokes too, but laid out the way the camera's home position sees the
// surface, so the dashes stay the same size in the picture from the shore to the far side.
// Every dash shows the mirror image at its own center, nudged a little over time: a calm mirror
// with a painted, slightly broken edge to everything in it.
// On the lake, lights are drawn out the way ruffled water does it: the town on the far shore
// becomes columns of short dashes that tremble and drift toward the viewer (worked out from the
// same rule the sky uses to place the lights, IoriFar.hlsl), and under the moon or the low sun
// lies a path of dashes that catch its light. In the puddles a few dashes reach toward the horizon
// for what they show, which draws the fire and the lantern out into columns.
// Where the water is shallow the bottom shows through (needs the depth texture; see _IoriDepthFx).
Shader "IORI/Camp/Water"
{
    Properties
    {
        _Puddle ("Puddle (0 lake, 1 puddle)", Float) = 0
        _StrokeTiles ("Dash fineness", Float) = 9
        _Wobble ("Trembling of the mirror image", Float) = 1
        _Streak ("Puddles: how far lights are drawn out (part of the picture's height)", Float) = 0
        _Columns ("Lake: lights of the far shore drawn out on the water", Range(0, 2)) = 1
        _ColumnStretch ("  How long the columns are (times the light's height above the shore)", Float) = 6
        _Glitter ("Lake: path of light under the moon or the low sun", Range(0, 2)) = 1
        _Lanes ("Lake: lanes of ruffled water that show the sky", Range(0, 1)) = 0.4
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
            #include "IoriCommon.hlsl"
            #include "IoriFar.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Puddle, _StrokeTiles, _Wobble, _Mirror, _Shallow, _Streak, _Air, _Columns, _ColumnStretch, _Glitter, _Lanes;
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

            half4 frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                float t = IoriTime();
                float rain = _IoriWeather.x;
                bool puddle = _Puddle > 0.5;

                // ---- which dash: the surface as seen from the camera's home position
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

                // ---- the mirror image at the dash's center, trembling
                float4 ccs = TransformWorldToHClip(center);
                float4 csp = ComputeScreenPos(ccs);
                float2 uv = csp.xy / max(csp.w, 1e-4);
                float dist = distance(center, _WorldSpaceCameraPos);
                float calm = puddle ? 0.25 : 1.0;
                float ph = rnd * 6.283 + t * (0.6 + rnd * 1.1);
                float2 wob = float2(sin(ph * 1.3 + 1.0) * 0.003, sin(ph) * 0.005 + (rnd - 0.5) * 0.009) * _Wobble * calm;
                wob *= 1.0 + rain * 2.0;
                // far water is seen so flat that a small tremble moves the image a lot less
                wob *= saturate(8.0 / dist + 0.25);
                float ringsV = rain > 0.01 ? Rings(wp.xz * (puddle ? 1.0 : 0.6), t) * rain * saturate(14.0 / dist) : 0.0;
                uv += wob + ringsV * 0.01;
                // puddles: most dashes stay close; a few show what lies well toward the horizon (always that
                // way: a light's column runs from its image toward the viewer). Which ones changes slowly.
                float r3 = frac(rnd * 13.7 + 0.31);
                float tail = r3 * r3 * r3 * (0.7 + 0.3 * sin(ph * 0.9 + 2.0));
                // never past the horizon: above it the mirror image holds nothing
                float toHorizon = 0.5 * unity_CameraProjection._m11 * max(_WorldSpaceCameraPos.y - wp.y, 0.0) / max(ccs.w, 0.05);
                uv.y += min(tail * _Streak * (1.0 + rain * 0.6), toHorizon * 0.9);
                uv.x = 1.0 - uv.x;
                float3 refl = puddle
                    ? SAMPLE_TEXTURE2D_LOD(_IoriReflectionTexPuddle, sampler_LinearClamp, saturate(uv), 0).rgb
                    : SAMPLE_TEXTURE2D_LOD(_IoriReflectionTex, sampler_LinearClamp, saturate(uv), 0).rgb;

                // ---- how much mirror, how much water
                float3 viewDir = normalize(_WorldSpaceCameraPos - center);
                float fres = pow(1.0 - saturate(viewDir.y), 3.0);
                float mirror = lerp(_Mirror, 1.0, fres);
                float3 tint = puddle ? _IoriPuddleTint.rgb : _IoriWaterTint.rgb * _Tint.rgb;
                mirror *= 1.0 - rain * 0.25;
                float3 col = lerp(tint, refl, saturate(mirror));
                col *= 1.0 + sm.a * 0.08 - 0.04;
                col += _IoriAmbientSky.rgb * ringsV * 0.25;

                if (!puddle)
                {
                    // ---- lanes of ruffled water: long, thin streaks that show the sky instead of the far shore.
                    // Laid out the way the picture sees the lake (thinner toward the horizon), a dash at a time.
                    float laneN = IoriNoise2(float2(pc.x * 2.5 + t * 0.006, log2(pc.y) * 5.0 + 11.0));
                    float lane = step(0.64, laneN) * smoothstep(9.0, 30.0, dist) * (1.0 - rain * 0.5);
                    col = lerp(col, _IoriSkyMid.rgb * 0.9, lane * _Lanes);

                    // ---- lights of the far shore, drawn out into columns (per pixel: they are thin).
                    // The water mirrors the town stretched toward the viewer. Every ripple is a row: it shifts
                    // what it mirrors a little to the side, and some rows miss the light altogether. The rows
                    // drift toward the viewer.
                    float3 ray = normalize(wp - _WorldSpaceCameraPos);
                    float azW = atan2(ray.x, ray.z);
                    float depW = asin(saturate(-ray.y)) / IORI_DEG;
                    float3 top, slope;
                    float2 town;
                    IoriRidges(azW, top, slope, town);
                    float row = floor(pow(max(depW, 1e-4), 0.75) * 14.0 + t * 0.5);
                    float shift = (IoriHash1(float2(row, 7.0)) - 0.5) * 0.12 * (0.5 + 0.5 * depW);
                    float rowOn = step(min(0.25 + 0.12 * depW, 0.8), IoriHash1(float2(row, 19.0)));
                    float2 tl = IoriTownLight(float2(azW / IORI_DEG + shift, depW / max(_ColumnStretch, 1.0)), town, max(top.x, top.y), 1.5, t);
                    float fadeW = exp(-depW / 2.5) * smoothstep(0.0, 0.1, depW) * (1.0 - rain * 0.5);
                    col += IoriTownColor(tl.y) * (tl.x * rowOn * fadeW * _Columns * 0.75);

                    // ---- the path of light under the moon (or the low sun): dashes that catch it, more of
                    // them right under it, each one for a moment
                    float moonUp = _IoriSkyParams.y, sunUp = _IoriSkyParams.w;
                    bool byMoon = moonUp > sunUp;
                    float3 gdir = normalize(byMoon ? _IoriMoonDir.xyz : _IoriSunDir.xyz);
                    float3 gcol = byMoon ? _IoriMoonColor.rgb * moonUp : _IoriSunColor.rgb * (sunUp * 2.0);
                    float3 cray = normalize(center - _WorldSpaceCameraPos);
                    float depC = asin(saturate(-cray.y)) / IORI_DEG;
                    float dAz = (atan2(cray.x, cray.z) - atan2(gdir.x, gdir.z)) / IORI_DEG;
                    dAz -= 360.0 * round(dAz / 360.0);
                    float widthG = 0.5 + 0.22 * depC;
                    float catches = exp(-(dAz * dAz) / (widthG * widthG)) * smoothstep(0.05, 0.6, depC) * smoothstep(0.0, 6.0, asin(gdir.y) / IORI_DEG);
                    float blink = frac(rnd * 31.7 + floor(t * 1.6 + rnd * 9.0) * 0.618);
                    col += gcol * (step(1.0 - catches * 0.75, blink) * _Glitter * 0.9 * (1.0 - rain));
                }

                // ---- shallow water lets the bottom through
                float alpha = 1.0;
                if (_IoriDepthFx > 0.5)
                {
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float mine = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                    float through = max(scene - mine, 0.0) * saturate(viewDir.y * 3.0 + 0.15);
                    // the edge of the water is cut by strokes as well
                    alpha = saturate(through / max(_Shallow, 0.01) + (rnd - 0.5) * 0.5);
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
