// Unlit flat color: distant silhouettes, ridges, leaves, the glowing parts of props.
// Same vertex layout as IORI/Camp/Painted. _FogAmount 0 = no fog (cut-out layers keep their exact color).
Shader "IORI/Camp/Flat"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _FogAmount ("Fog amount", Range(0, 1)) = 1
        _EmissionBoost ("Emission boost", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Cull;
                float _FogAmount;
                float _EmissionBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : TEXCOORD1; float4 data : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float t = IoriTime();
                float sway = v.color.a;
                wp.x += sin(t * 0.9 + wp.x * 0.5 + wp.y * 0.3) * 0.04 * sway;
                wp.y += sin(t * 1.7 + wp.z * 0.8) * 0.015 * sway;
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.color = v.color;
                o.data = v.data;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 emission = i.color.rgb * IoriGlowLevel(i.data.w) * _EmissionBoost;
                float3 col = i.data.x < -0.5 ? 0 : IoriPalette(i.data.x) * i.data.z;
                float fog = IoriFogFactor(i.positionWS) * _FogAmount;
                col = lerp(col, _IoriFogColor.rgb, fog) + emission;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
