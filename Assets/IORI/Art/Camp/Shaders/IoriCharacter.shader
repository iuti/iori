// The 2D character board: a finished drawing (her own cel shading, drawn in flat light) standing in
// the scene. She takes the same light as the scene, pixel by pixel, through a normal map made from
// the drawing (tools/character/make_sprite.py): the sides of her that face the fire take its warm
// light, the sides that face away keep only the sky's blue. No brush strokes on her; the border
// between lit and unlit is a soft cel edge. A warm rim on the side the fire is on and a thin cool
// rim on the other stay as a finishing touch. Without a normal map (or with its strength at 0) the
// whole board is lit as one flat thing facing the camera.
// Drawn as a solid thing so that depth of field and shadows treat her properly.
Shader "IORI/Camp/Character"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Breath ("Breath sway", Float) = 1
        _RimWarm ("Fire rim", Range(0, 2)) = 0.7
        _RimCool ("Sky rim", Range(0, 2)) = 0.35
        [NoScaleOffset] _NormalTex ("Normals (r right, g up, b to viewer; sRGB off)", 2D) = "bump" {}
        _NormalStrength ("Normal strength (0 = flat board)", Range(0, 2)) = 1
        _Cel ("Cel edge amount (0 = smooth falloff)", Range(0, 1)) = 0.7
        _CelEdge ("Cel edge position", Range(-0.5, 0.5)) = 0.08
        _CelSoft ("Cel edge softness", Range(0.01, 0.5)) = 0.07
        _Wrap ("Light wrap of the smooth part", Range(0, 1)) = 0.35
        _SkyBack ("Sky light stepping back where a lamp lights her", Range(0, 1)) = 0.3
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
            float _NormalStrength, _Cel, _CelEdge, _CelSoft, _Wrap, _SkyBack;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

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
                float4 fire : TEXCOORD2;      // rgb: fire light for the rim, a: which side of the picture the fire is on (-1 left, 1 right)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = IoriBreath(v.positionOS.xyz, v.uv);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                float3 mid = TransformObjectToWorld(float3(0.0, v.positionOS.y, 0.0));
                float3 right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                float3 toFire = _IoriFirePos.xyz - mid;
                float fd = dot(toFire, toFire);
                o.fire = float4(_IoriGlow[1].rgb / (1.0 + fd * 0.35), dot(right, toFire) > 0.0 ? 1.0 : -1.0);
                return o;
            }

            // How much of a light a side takes: a cel edge (lit or not, with a soft border) mixed with a
            // smooth falloff, so the drawing keeps its flat look and still turns gently.
            float IoriCel(float nl)
            {
                float gentle = saturate((nl + _Wrap) / (1.0 + _Wrap));
                float hard = smoothstep(_CelEdge - _CelSoft, _CelEdge + _CelSoft, nl);
                return lerp(gentle, hard, _Cel);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(t.a - _Cutoff);

                // the board's own directions: the picture's right, up, and out of the picture toward the viewer
                float3 right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                float3 up = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 front = normalize(TransformObjectToWorldDir(float3(0, 0, -1)));
                float3 nt = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv).xyz * 2.0 - 1.0;
                nt.xy *= _NormalStrength;
                float3 n = normalize(right * nt.x + up * nt.y + front * max(nt.z, 0.05));

                // shadows are looked up a little in front of the board, so the board does not shade itself
                float3 sp = i.positionWS + front * 0.08;
                float3 sky = IoriAmbient(n);
                Light m = GetMainLight(TransformWorldToShadowCoord(sp), sp, half4(1, 1, 1, 1));
                sky += m.color * (IoriCel(dot(n, m.direction)) * m.shadowAttenuation);
                float3 lamp = 0;
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; ++k)
                {
                    Light l = GetAdditionalLight(k, sp, half4(1, 1, 1, 1));
                    lamp += l.color * (IoriCel(dot(n, l.direction)) * IoriLampFalloff(l.distanceAttenuation) * l.shadowAttenuation);
                }
                // where a lamp lights her the sky's blue steps back a little, so the lit side is clearly warm
                float lw = max(lamp.r, max(lamp.g, lamp.b)), sw = max(sky.r, max(sky.g, sky.b));
                float warm = smoothstep(0.35, 0.65, lw / (lw + sw + 1e-4));
                float3 L = sky * (1.0 - _SkyBack * warm) + lamp;
                // she is a finished drawing with her own shading: the scene's light tints her and sets how
                // bright she is, but strong light is held back so that she never burns out next to the fire
                float lm = max(L.r, max(L.g, L.b));
                L *= 1.2 / (1.0 + 0.6 * lm);

                float2 px = float2(_MainTex_TexelSize.x * 14.0 * i.fire.a, 0.0);
                float aToFire = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + px).a;
                float aAway = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - px).a;
                float rimFire = saturate(t.a - aToFire);
                float rimCool = saturate(t.a - aAway);
                float3 col = t.rgb * L;
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
