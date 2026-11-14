// The shade on the ground under Iori and her chair. She is a flat picture and cannot shade the ground
// under herself, so a soft patch does it: it darkens what is already painted there toward the shade
// color (multiplied in, so the ground keeps its strokes and a shadow stays a color, never black).
Shader "IORI/Camp/Contact"
{
    Properties
    {
        _Tint ("Shade color (multiplied onto the ground)", Color) = (0.30, 0.38, 0.62, 1)
        _Strength ("Strength", Range(0, 1)) = 0.75
        _Core ("Size of the dark middle", Range(0, 0.9)) = 0.25
    }
    SubShader
    {
        // after everything solid (the ground must be there to be darkened), before the transparent things
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest+40" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Blend DstColor Zero

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Strength, _Core;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                float a = 1.0 - smoothstep(_Core, 1.0, r);
                a = a * a * _Strength;
                return half4(lerp(float3(1, 1, 1), _Tint.rgb, a), 1);
            }
            ENDHLSL
        }
    }
}
