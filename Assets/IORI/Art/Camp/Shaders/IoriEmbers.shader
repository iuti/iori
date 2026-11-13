// Sparks rising from the fire. Quads whose center is animated in the vertex shader.
// POSITION = base point, TEXCOORD0.xy = corner, TEXCOORD1.x = particle index
Shader "IORI/Camp/Embers"
{
    Properties { _Size ("Size", Float) = 0.03 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Size;
            CBUFFER_END
            float4 _IoriEmberColor;

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 color : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = IoriTime();
                float idx = v.data.x;
                float ph = frac(t * 0.22 + idx * 0.137);
                float3 p = v.positionOS.xyz;
                p.x += sin(t * 1.3 + idx) * 0.06 + ph * 0.2;
                p.y = 0.55 + ph * 2.3;
                p.z -= cos(t * 1.1 + idx * 2.0) * 0.05;
                float3 vs = TransformWorldToView(TransformObjectToWorld(p));
                vs.xy += v.corner * _Size * 0.5;
                o.positionCS = TransformWViewToHClip(vs);
                float fade = (1 - ph) * smoothstep(0.0, 0.08, ph);
                o.color = _IoriEmberColor.rgb * IoriGlowLevel(6) * fade;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(i.color, 1); }
            ENDHLSL
        }
    }
}
