// Sparks rising from the fire. The mesh is a handful of quads (CampFire builds it) whose
// POSITION is three random numbers, not a place: the vertex shader works out where each spark is
// right now, so nothing has to be simulated and a photo at a given time always looks the same.
Shader "IORI/Camp/Sparks"
{
    Properties
    {
        _Color ("Hot color", Color) = (1.0, 0.60, 0.20, 1)
        _ColorEnd ("Cooling color", Color) = (1.0, 0.20, 0.04, 1)
        _Bright ("Brightness", Float) = 2.6
        _Size ("Size (m)", Float) = 0.0045
        _Height ("How high they climb (m)", Float) = 1.3
        _Often ("How much of the time a spark is in the air", Range(0, 1)) = 0.45
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
                float _Bright, _Size, _Height, _Often;
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

                // a glowing grain, a little longer than wide along its way up; never smaller than a pixel or so
                float sz = _Size * (0.6 + 0.8 * seed.z) * (1.0 - 0.5 * ph);
                float3 toCam = _WorldSpaceCameraPos - p;
                float camDist = length(toCam);
                sz = max(sz, camDist * 0.00085);
                toCam /= max(camDist, 1e-3);
                float3 along = normalize(float3(_IoriWind.x * 0.25, 1.0, _IoriWind.y * 0.25));
                float3 side = normalize(cross(along, toCam));
                p += side * (v.corner.x * sz) + along * (v.corner.y * sz * 1.5);

                float fade = smoothstep(0.0, 0.08, ph) * (1.0 - smoothstep(0.35, 1.0, ph));
                float flick = 0.75 + 0.25 * sin(t * 23.0 + seed.x * 60.0);
                // most of the time a spark is not there at all: they come now and then, a few at a time
                float there = step(IoriHash1(float2(cycle * 1.37, seed.z * 17.3)), _Often);
                float shown = step(seed.y, _IoriFire.z) * there * (1.0 - _IoriWeather.x * 0.7);
                o.color = lerp(_Color.rgb, _ColorEnd.rgb, saturate(ph * 1.4)) * (_Bright * fade * flick * shown * _IoriFire.y);
                o.corner = v.corner;
                o.positionCS = shown > 0.0 ? TransformWorldToHClip(p) : float4(0, 0, 2, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // round, with a soft rim
                float a = 1.0 - smoothstep(0.35, 1.0, length(i.corner));
                return half4(i.color * a, 0);
            }
            ENDHLSL
        }
    }
}
