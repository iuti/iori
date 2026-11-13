// Rain streaks. Each streak is a thin quad stretched along y and turned to face the camera
// around the vertical axis. POSITION = (x, start height, z), TEXCOORD0.xy = corner, TEXCOORD1.x = fall speed.
Shader "IORI/Camp/Rain"
{
    Properties
    {
        _Color ("Color", Color) = (0.87, 0.9, 1, 0.34)
        _Length ("Length", Float) = 0.36
        _Width ("Width", Float) = 0.008
        _Span ("Fall span", Float) = 7
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Length, _Width, _Span;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float a : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = IoriTime();
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float y = v.positionOS.y - t * v.data.x;
                y = y - floor(y / _Span) * _Span;
                p.y = y + (v.corner.y * 0.5 + 0.5) * _Length;
                float3 toCam = _WorldSpaceCameraPos - p;
                float3 side = normalize(float3(toCam.z, 0, -toCam.x));
                p += side * v.corner.x * _Width * 0.5;
                p.x += (v.corner.y * 0.5 + 0.5) * 0.03;
                o.positionCS = TransformWorldToHClip(p);
                o.a = _Color.a * _IoriWeather.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(_Color.rgb, i.a); }
            ENDHLSL
        }
    }
}
