// Lake and puddles. Both sample the shared planar reflection (CampReflection renders it from a
// camera mirrored under the puddle plane, so the texture is flipped horizontally).
// _Puddle = 1: clipped to an ellipse around the camp, calm, rain rings when it rains.
Shader "IORI/Camp/Water"
{
    Properties
    {
        _Puddle ("Puddle", Float) = 0
        _Ripple ("Ripple", Float) = 1
        _Center ("Puddle center (xz) and radius (zw)", Vector) = (0, -0.5, 6.6, 4.6)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Puddle;
                float _Ripple;
                float4 _Center;
            CBUFFER_END
            float4 _IoriWaterTint, _IoriPuddleTint;
            TEXTURE2D(_IoriReflectionTex); SAMPLER(sampler_IoriReflectionTex);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), u.x), lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), u.x), u.y);
            }
            float rings(float2 p, float t)
            {
                float acc = 0;
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float fk = k;
                    float2 g = p * (2.1 + fk * 1.3) + fk * 7.3;
                    float2 id = floor(g), f = frac(g) - 0.5;
                    float h = hash2(id + fk * 3.1);
                    float tt = frac(t * (0.7 + 0.35 * h) + h * 7.0);
                    float2 c = (float2(hash2(id + 1.7), hash2(id + 5.3)) - 0.5) * 0.5;
                    float d = length(f - c) - tt * 0.42;
                    acc += sin(d * 70.0) * smoothstep(0.07, 0.0, abs(d)) * (1 - tt);
                }
                return acc;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 wp = i.positionWS;
                float t = IoriTime();
                float d = distance(_WorldSpaceCameraPos, wp);
                if (_Puddle > 0.5)
                {
                    float2 e = (wp.xz - _Center.xy) / _Center.zw;
                    clip(1 - dot(e, e));
                }
                float2 p = wp.xz * float2(0.9, 3.2) + float2(t * 0.10, -t * 0.42);
                float n1 = noise2(p) + 0.5 * noise2(p * 2.3 + 4.0);
                float n2 = noise2(p + float2(31, 7)) + 0.5 * noise2(p * 2.1 + 9.0);
                float2 off = (float2(n1, n2) - 0.75) * float2(0.012, 0.05) * _Ripple * smoothstep(260.0, 8.0, d);
                float rain = _IoriWeather.x;
                float rg = rain * rings(wp.xz, t) * smoothstep(30.0, 4.0, d);
                off += rg * 0.012;

                float2 uv = i.screenPos.xy / i.screenPos.w;
                uv.x = 1 - uv.x;
                uv += off;
                float3 refl = SAMPLE_TEXTURE2D(_IoriReflectionTex, sampler_IoriReflectionTex, uv).rgb;
                float3 V = normalize(_WorldSpaceCameraPos - wp);
                float fres = pow(1 - max(V.y, 0), 4.0);
                float3 tint = _Puddle > 0.5 ? _IoriPuddleTint.rgb : _IoriWaterTint.rgb;
                float3 col = lerp(tint, refl, lerp(_Puddle > 0.5 ? 0.74 : 0.55, 0.97, fres));
                col = lerp(col, tint, _Puddle > 0.5 ? 0.04 : 0.12);
                col += abs(rg) * 0.05;
                float fog = IoriFogFactor(wp);
                col = lerp(col, _IoriFogColor.rgb, fog * 0.55);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
