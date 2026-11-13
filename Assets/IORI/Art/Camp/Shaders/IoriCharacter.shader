// The 2D character board (a placeholder until the real model is drawn into a texture).
// She takes the same light as the scene, measured at a few heights of the board and kept smooth
// (no brush strokes on her), with a warm rim on the side the fire is on and a thin cool rim on
// the other. Drawn as a solid thing so that depth of field and shadows treat her properly.
Shader "IORI/Camp/Character"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Breath ("Breath sway", Float) = 1
        _RimWarm ("Fire rim", Range(0, 2)) = 0.7
        _RimCool ("Sky rim", Range(0, 2)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "IoriCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _Cutoff, _Breath, _RimWarm, _RimCool;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        float3 IoriBreath(float3 p, float2 uv)
        {
            float t = IoriTime();
            p.y += sin(t * 1.5) * 0.006 * smoothstep(0.45, 0.8, uv.y) * _Breath;
            p.x += sin(t * 0.6) * 0.004 * smoothstep(0.2, 1.0, uv.y) * _Breath;
            return p;
        }
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
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 light : TEXCOORD2;
                float4 fire : TEXCOORD3;      // rgb: fire light for the rim, a: which side of the picture the fire is on (-1 left, 1 right)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = IoriBreath(v.positionOS.xyz, v.uv);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                // light at this height of the board, facing the camera and a little up
                float3 mid = TransformObjectToWorld(float3(0.0, v.positionOS.y, 0.0));
                float3 toCam = normalize(_WorldSpaceCameraPos - mid);
                float3 n = normalize(float3(toCam.x, 0.25, toCam.z));
                o.light = IoriLightingSmooth(mid, n, 0.7);
                float3 right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                float3 toFire = _IoriFirePos.xyz - mid;
                float fd = dot(toFire, toFire);
                o.fire = float4(_IoriGlow[1].rgb / (1.0 + fd * 0.35), dot(right, toFire) > 0.0 ? 1.0 : -1.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(t.a - _Cutoff);
                float2 px = float2(_MainTex_TexelSize.x * 14.0 * i.fire.a, 0.0);
                float aToFire = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + px).a;
                float aAway = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - px).a;
                float rimFire = saturate(t.a - aToFire);
                float rimCool = saturate(t.a - aAway);
                float3 col = t.rgb * i.light;
                col += i.fire.rgb * (rimFire * _RimWarm) + _IoriAmbientSky.rgb * (rimCool * _RimCool);
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(IoriBreath(v.positionOS.xyz, v.uv));
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, lightDirectionWS, lightDirectionWS)));
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return 0;
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IoriBreath(v.positionOS.xyz, v.uv));
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
}
