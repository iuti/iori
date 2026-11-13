// Sparks rising from the fire. The mesh is a handful of quads (CampFire builds it) whose
// POSITION is three random numbers, not a place: the vertex shader works out where each spark is
// right now, so nothing has to be simulated and a photo at a given time always looks the same.
Shader "IORI/Camp/Sparks"
{
    Properties
    {
        _Color ("Hot color", Color) = (1.0, 0.78, 0.35, 1)
        _ColorEnd ("Cooling color", Color) = (1.0, 0.25, 0.06, 1)
        _Bright ("Brightness", Float) = 5
        _Size ("Size", Float) = 0.007
        _Height ("How high they climb (m)", Float) = 1.9
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+30" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _ColorEnd;
                float _Bright, _Size, _Height;
            CBUFFER_END
            float4 _IoriFire;          // x: flame size, y: brightness, z: how many sparks (0..1)

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 color : TEXCOORD0; float2 corner : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = IoriTime();
                float3 seed = v.positionOS.xyz;
                float life = 1.5 + seed.y * 2.4;
                float clock = t / life + seed.x * 7.13;
                float ph = frac(clock);
                float cycle = floor(clock);
                float r1 = IoriHash1(float2(seed.z * 91.7, cycle));
                float r2 = IoriHash1(float2(cycle + 3.0, seed.x * 53.1));
                float size = max(_IoriFire.x, 0.0);

                float3 foot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float ang = r1 * 6.2832;
                float rad = 0.04 + 0.13 * r2;
                float3 p = foot + float3(cos(ang) * rad, 0.2 * size, sin(ang) * rad);
                float up = ph * (0.45 + 0.55 * r1) * _Height * (0.5 + 0.5 * size);
                p.y += up;
                // they wander more the higher they are, and lean with the wind
                p.xz += float2(sin(t * 2.1 + seed.x * 40.0), cos(t * 1.7 + seed.z * 40.0)) * (0.11 * ph);
                p.xz += float2(cos(ang), sin(ang)) * (0.18 * ph * r2);
                p.xz += _IoriWind.xy * (ph * ph * 0.5 * _IoriWind.z);

                float sz = _Size * (0.6 + 0.9 * seed.z) * (1.0 - 0.55 * ph);
                float3 toCam = normalize(_WorldSpaceCameraPos - p);
                float3 along = normalize(float3(_IoriWind.x * 0.25, 1.0, _IoriWind.y * 0.25));
                float3 side = normalize(cross(along, toCam));
                p += side * (v.corner.x * sz) + along * (v.corner.y * sz * 2.4);

                float fade = smoothstep(0.0, 0.06, ph) * (1.0 - smoothstep(0.5, 1.0, ph));
                float flick = 0.7 + 0.3 * sin(t * 23.0 + seed.x * 60.0);
                float shown = step(seed.y, _IoriFire.z) * (1.0 - _IoriWeather.x * 0.7);
                o.color = lerp(_Color.rgb, _ColorEnd.rgb, ph) * (_Bright * fade * flick * shown * _IoriFire.y);
                o.corner = v.corner;
                o.positionCS = shown > 0.0 ? TransformWorldToHClip(p) : float4(0, 0, 2, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = max(abs(i.corner.x), abs(i.corner.y) * 0.9);
                float a = 1.0 - smoothstep(0.55, 1.0, d);
                return half4(i.color * a, 0);
            }
            ENDHLSL
        }
    }
}
