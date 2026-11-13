// The lake and the puddles. Both show the shared mirror image that CampReflection renders
// (a camera mirrored under the water, so its picture is flipped left to right).
//
// Water is painted in strokes too, but laid out the way the camera's home position sees the
// surface, so the dashes stay the same size in the picture from the shore to the far side.
// Every dash shows the mirror image at its own center, nudged up and down over time: lights on
// the far shore, the moon and the fire break into trembling columns of dashes.
// Where the water is shallow the bottom shows through (needs the depth texture; see _IoriDepthFx).
Shader "IORI/Camp/Water"
{
    Properties
    {
        _Puddle ("Puddle (0 lake, 1 puddle)", Float) = 0
        _StrokeTiles ("Dash fineness", Float) = 9
        _Wobble ("Trembling of the mirror image", Float) = 1
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Puddle, _StrokeTiles, _Wobble, _Mirror, _Shallow;
                float4 _Tint;
            CBUFFER_END
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
                float2 wob = float2(sin(ph * 1.3 + 1.0) * 0.004, sin(ph) * 0.014 + (rnd - 0.5) * 0.02) * _Wobble * calm;
                wob *= 1.0 + rain * 2.0;
                // far water is seen so flat that a small tremble moves the image a lot less
                wob *= saturate(8.0 / dist + 0.25);
                float ringsV = rain > 0.01 ? Rings(wp.xz * (puddle ? 1.0 : 0.6), t) * rain * saturate(14.0 / dist) : 0.0;
                uv += wob + ringsV * 0.01;
                uv.x = 1.0 - uv.x;
                float3 refl = puddle
                    ? SAMPLE_TEXTURE2D_LOD(_IoriReflectionTexPuddle, sampler_LinearClamp, saturate(uv), 0).rgb
                    : SAMPLE_TEXTURE2D_LOD(_IoriReflectionTex, sampler_LinearClamp, saturate(uv), 0).rgb;

                // ---- how much mirror, how much water
                float3 viewDir = normalize(_WorldSpaceCameraPos - center);
                float fres = pow(1.0 - saturate(viewDir.y), 3.0);
                float mirror = lerp(_Mirror, 1.0, fres);
                float3 tint = puddle ? _IoriPuddleTint.rgb : _IoriWaterTint.rgb * _Tint.rgb;
                // ruffled lanes on the lake: the wind takes the mirror away in long streaks
                float lane = puddle ? 0.0 : step(0.62, IoriNoise2(center.xz * float2(0.045, 0.5) + float2(t * 0.02, 0.0))) * smoothstep(9.0, 30.0, dist);
                mirror *= 1.0 - lane * 0.45 - rain * 0.25;
                float3 col = lerp(tint, refl, saturate(mirror));
                col *= 1.0 + sm.a * 0.08 - 0.04;
                col += _IoriAmbientSky.rgb * ringsV * 0.25;

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
                col = IoriAir(col, wp);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
