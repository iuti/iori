// Opaque camp surfaces: ground, rocks, props, tree trunks, grass. Flat per-face normals come from
// the mesh. Base color is a palette slot; the brush can switch a fragment to the neighbouring slot,
// so the visible colors always stay inside the palette while the near field gets angular detail.
// Vertex data: COLOR rgb = emission color, a = sway weight
//              TEXCOORD1 = (slot, alt slot, brightness, glow channel)   slot < 0 means emission only
//              TEXCOORD2.x = low-ground flag (darkens more when wet)
Shader "IORI/Camp/Painted"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _BrushAmount ("Brush amount", Range(0, 2)) = 1
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
                float _BrushAmount;
                float _EmissionBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 data : TEXCOORD1; float4 data2 : TEXCOORD2; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : TEXCOORD2; float4 data : TEXCOORD3; float wetFlag : TEXCOORD4; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float t = IoriTime();
                float sway = v.color.a;
                wp.x += sin(t * 1.3 + wp.x * 0.7 + wp.z * 0.5) * 0.025 * sway;
                wp.z += cos(t * 1.1 + wp.x * 0.4) * 0.015 * sway;
                o.positionWS = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.data = v.data;
                o.wetFlag = v.data2.x;
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 emission = i.color.rgb * IoriGlowLevel(i.data.w) * _EmissionBoost;
                if (i.data.x < -0.5) return half4(emission, 1);

                float near = IoriNearFactor(i.positionWS) * _BrushAmount;
                float brush = IoriBrush(i.positionWS, n) * _IoriBrushParams.x * near;
                // switch to the neighbouring palette slot where the brush is strong
                float slot = (brush * _IoriPaintParams.z > 0.13) ? i.data.y : i.data.x;
                float3 baseCol = IoriPalette(slot) * i.data.z;

                float3 col = baseCol * IoriLight(i.positionWS, n, brush * 2.2);
                float wet = _IoriWeather.y;
                col *= lerp(1, 0.8, wet) * lerp(1, 0.72, wet * i.wetFlag);
                float fog = IoriFogFactor(i.positionWS);
                col = lerp(col, _IoriFogColor.rgb, fog) + emission * (1 - fog * 0.5);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
