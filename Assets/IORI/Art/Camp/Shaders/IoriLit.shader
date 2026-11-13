// Solid things in the camp: props, rocks, bark, the tent, the logs in the fire.
// Colors come from the mesh (vertex color = surface color, alpha = baked shade; TEXCOORD1 = glow
// channel, glow strength, gloss). Light and shadow are taken one brush stroke at a time (IoriCommon.hlsl).
Shader "IORI/Camp/Lit"
{
    Properties
    {
        _StrokeScale ("Stroke fineness (bigger = smaller strokes)", Float) = 2
        _Variation ("Lighter and darker strokes", Range(0, 1)) = 0.16
        [HDR] _TintA ("Some strokes lean toward", Color) = (1.08, 1.0, 0.9, 1)
        [HDR] _TintB ("Others lean toward", Color) = (0.86, 0.94, 1.12, 1)
        _TintAmount ("How much they lean", Range(0, 1)) = 0.6
        _Bristle ("Bristle streaks", Range(0, 1)) = 0.25
        _Wrap ("Light wraps around", Range(0, 1)) = 0.25
        _Through ("Light shines through (cloth)", Range(0, 1)) = 0
        _WetDarken ("Darker when wet", Range(0, 1)) = 0.3
        _WetGloss ("Shinier when wet", Range(0, 1)) = 0.35
        [Toggle(_EMBER)] _Ember ("Glowing cracks (burning wood)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "IoriCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _StrokeScale, _Variation, _TintAmount, _Bristle, _Wrap, _Through, _WetDarken, _WetGloss, _Ember;
            float4 _TintA, _TintB;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _EMBER
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 data : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 albedo : TEXCOORD2;      // rgb: surface color (linear), a: baked shade
                float4 glow : TEXCOORD3;        // rgb: light given off, a: gloss
                float3 positionOS : TEXCOORD4;
                float heat : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.albedo = float4(IoriVertexAlbedo(v.color.rgb), v.color.a);
                o.positionOS = v.positionOS.xyz;
                #ifdef _EMBER
                    o.glow = float4(0, 0, 0, v.data.z);
                    o.heat = v.data.y * step(6.5, v.data.x);
                #else
                    o.glow = float4(IoriGlowOf(v.data), v.data.z);
                    o.heat = 0;
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                IoriStroke st = IoriStrokeAt(i.positionWS, n, _StrokeScale);
                float r2 = frac(st.rnd * 7.31 + 0.13);

                float3 albedo = i.albedo.rgb;
                float lean = (r2 - 0.5) * 2.0;
                albedo *= 1.0 + (st.rnd - 0.5) * 2.0 * _Variation + st.bristle * _Bristle;
                albedo *= lerp(float3(1, 1, 1), lean > 0.0 ? _TintA.rgb : _TintB.rgb, abs(lean) * _TintAmount);

                float wet = _IoriWeather.y * saturate(n.y * 2.0 + 0.6);
                albedo *= 1.0 - wet * _WetDarken;
                float gloss = saturate(i.glow.a + wet * _WetGloss);

                IoriLit L = IoriLighting(st.pos, n, viewDir, st.rnd, i.albedo.a, _Wrap, _Through, gloss);
                float3 col = albedo * L.diffuse + L.spec;

                float3 glow = i.glow.rgb;
                #ifdef _EMBER
                    // burning wood: thin glowing cracks where the wood is charred, and a slow breathing
                    float t = IoriTime();
                    float heat = i.heat;
                    float3 q = i.positionOS * 34.0;
                    float nz = IoriNoise3(q) * 0.65 + IoriNoise3(q * 2.3 + 7.0) * 0.35;
                    float breathe = 0.75 + 0.25 * sin(t * 1.7 + IoriNoise3(i.positionOS * 6.0) * 12.0);
                    float crack = smoothstep(0.07, 0.015, abs(nz - 0.5)) * smoothstep(0.25, 0.7, heat);
                    float core = smoothstep(0.62, 0.95, heat * (0.75 + 0.5 * IoriNoise3(i.positionOS * 11.0 + t * 0.15)));
                    float e = saturate(crack * 0.9 + core) * breathe;
                    float3 hot = _IoriGlow[7].rgb;
                    // cooler cracks are deep red, the hottest spots go yellow
                    glow = hot * e * lerp(float3(0.8, 0.12, 0.03), float3(1.4, 0.75, 0.25), saturate(core * 1.2 + crack * heat * 0.4));
                    col *= 1.0 - saturate(heat * 1.4) * 0.75;       // charred wood is nearly black
                #else
                    glow *= 1.0 + (st.rnd - 0.5) * 0.35;
                #endif
                col += glow;
                col = IoriAir(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
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
