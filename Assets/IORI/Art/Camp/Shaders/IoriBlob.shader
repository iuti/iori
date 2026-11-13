// Soft dark patch laid on the ground (contact shadow under Iori, the fire, props).
// TEXCOORD0.xy = corner (-1..1), TEXCOORD1.x = opacity
Shader "IORI/Camp/Blob"
{
    Properties { _Slot ("Palette slot", Float) = 10 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+20" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Slot;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 corner : TEXCOORD0; float a : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.corner = v.corner;
                o.a = v.data.x;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.corner);
                float soft = r < 0.25 ? lerp(1, 0.45, r / 0.25) : (r < 0.6 ? lerp(0.45, 0.1, (r - 0.25) / 0.35) : lerp(0.1, 0, saturate((r - 0.6) / 0.4)));
                return half4(IoriPalette(_Slot), soft * i.a);
            }
            ENDHLSL
        }
    }
}
