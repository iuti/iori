// Additive camera-facing glows: halos around the fire, lantern, tent, radio; the far town lights.
// Every vertex of a quad carries the quad center as its position.
// TEXCOORD0.xy = corner (-1..1), TEXCOORD1 = (size, glow channel, hardness 0..1, opacity), COLOR = tint
Shader "IORI/Camp/Glow"
{
    Properties { _Boost ("Boost", Float) = 1 }
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
                float _Boost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 corner : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 corner : TEXCOORD0; float4 color : TEXCOORD1; float hard : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(v.positionOS.xyz));
                centerVS.xy += v.corner * v.data.x * 0.5;
                o.positionCS = TransformWViewToHClip(centerVS);
                o.corner = v.corner;
                float level = v.data.y < -0.5 ? 1 : IoriGlowLevel(v.data.y);
                o.color = float4(v.color.rgb * v.data.w * level * _Boost, 1);
                o.hard = v.data.z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.corner);
                // same falloff as the mock's canvas gradient (1 / .45 / .1 / 0 at 0 / .25 / .6 / 1)
                float soft = r < 0.25 ? lerp(1, 0.45, r / 0.25) : (r < 0.6 ? lerp(0.45, 0.1, (r - 0.25) / 0.35) : lerp(0.1, 0, saturate((r - 0.6) / 0.4)));
                float hardP = smoothstep(1.0, 0.55, r);
                float a = lerp(soft, hardP, i.hard);
                return half4(i.color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
