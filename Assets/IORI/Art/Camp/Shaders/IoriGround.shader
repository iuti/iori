// The campsite ground. What covers it (grass floor, bare dirt, moss, sand at the water) comes from
// iori_ground.png, read at the center of each brush stroke, so patches of dirt and grass have
// painted, angular edges. The texture's alpha is the shade baked under the props, stones and trees.
// When it is wet the ground darkens, glints, and turns to mud at the rim of the puddles.
Shader "IORI/Camp/Ground"
{
    Properties
    {
        _GroundMap ("Cover (R dirt, G moss, B sand, A shade)", 2D) = "black" {}
        _Grass ("Grass floor", Color) = (0.20, 0.33, 0.22, 1)
        _GrassB ("Grass floor, other strokes", Color) = (0.26, 0.40, 0.24, 1)
        _Dirt ("Dirt", Color) = (0.36, 0.29, 0.25, 1)
        _DirtB ("Dirt, other strokes", Color) = (0.30, 0.24, 0.24, 1)
        _Moss ("Moss", Color) = (0.24, 0.40, 0.30, 1)
        _Sand ("Sand", Color) = (0.55, 0.52, 0.45, 1)
        _Mud ("Wet mud", Color) = (0.13, 0.11, 0.12, 1)
        _StrokeScale ("Stroke fineness", Float) = 1
        _Variation ("Lighter and darker strokes", Range(0, 1)) = 0.22
        _Bristle ("Bristle streaks", Range(0, 1)) = 0.3
        _Wrap ("Light wraps around", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "IoriCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _GroundMap_ST;
            float4 _Grass, _GrassB, _Dirt, _DirtB, _Moss, _Sand, _Mud;
            float _StrokeScale, _Variation, _Bristle, _Wrap;
        CBUFFER_END
        TEXTURE2D(_GroundMap);
        float4 _IoriSeasonGrass;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 fallback : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fallback = IoriVertexAlbedo(v.color.rgb);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                IoriStroke st = IoriStrokeAt(i.positionWS, n, _StrokeScale);
                float r2 = frac(st.rnd * 7.31 + 0.13);

                // cover at the stroke's center (whole strokes of dirt or grass), shade at the pixel (crisp under things)
                float2 uvS = (st.pos.xz - _IoriGroundMap.xy) * _IoriGroundMap.z;
                float2 uvP = (i.positionWS.xz - _IoriGroundMap.xy) * _IoriGroundMap.z;
                float4 cover = SAMPLE_TEXTURE2D_LOD(_GroundMap, sampler_LinearClamp, uvS, 0);
                float shadeP = SAMPLE_TEXTURE2D_LOD(_GroundMap, sampler_LinearClamp, uvP, 0).a;
                float2 e = abs(uvS - 0.5) * 2.0;
                float inside = 1.0 - smoothstep(0.9, 1.0, max(e.x, e.y));

                float3 grass = lerp(_Grass.rgb, _GrassB.rgb, step(0.5, r2)) * _IoriSeasonGrass.rgb;
                float3 dirt = lerp(_Dirt.rgb, _DirtB.rgb, step(0.55, frac(r2 * 3.7)));
                // each stroke is one thing: the cover values decide which, the stroke's number breaks the border up
                float3 albedo = grass;
                albedo = lerp(albedo, _Moss.rgb * _IoriSeasonGrass.rgb, step(1.0 - cover.g * 0.9, st.rnd));
                albedo = lerp(albedo, dirt, step(1.0 - cover.r, r2 * 0.6 + 0.2));
                albedo = lerp(albedo, _Sand.rgb, step(1.0 - cover.b, st.rnd * 0.5 + 0.25));
                albedo = lerp(i.fallback, albedo, inside);
                float ao = lerp(1.0, lerp(shadeP, cover.a, 0.35), inside);

                albedo *= 1.0 + (st.rnd - 0.5) * 2.0 * _Variation + st.bristle * _Bristle;

                // wet: darker, shiny, and mud just above the water of the puddles
                float wet = _IoriWeather.y;
                float above = i.positionWS.y - _IoriGroundMap.w;
                float rim = wet * (1.0 - smoothstep(0.004, 0.035, above)) * inside;
                albedo = lerp(albedo, _Mud.rgb, saturate(rim * 0.85 + wet * cover.r * 0.25));
                albedo *= 1.0 - wet * 0.38;
                float gloss = wet * (0.35 + 0.45 * cover.r) + rim * 0.3;

                IoriLit L = IoriLighting(st.pos, n, viewDir, st.rnd, ao, _Wrap, 0.0, gloss);
                float3 col = albedo * L.diffuse + L.spec;
                // wet ground mirrors the sky at a low angle
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                col += _IoriAmbientSky.rgb * (fres * wet * 0.35 * ao);
                col = IoriAir(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
