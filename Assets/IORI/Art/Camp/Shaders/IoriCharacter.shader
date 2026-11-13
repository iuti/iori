// The 2D character board (placeholder for the livliv model rendered to a texture later).
// Tinted by the scene's ambient color, with a warm rim and fill from the fire side (screen left)
// and a thin cool rim on the other side. Not touched by the brush, so the face stays clean.
Shader "IORI/Camp/Character"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FireSide ("Fire side (1 = left, -1 = right)", Float) = 1
        _Breath ("Breath sway", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float _FireSide;
                float _Breath;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _IoriSpriteAmbient, _IoriSpriteFire, _IoriSpriteRim;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                float t = IoriTime();
                p.y += sin(t * 1.5) * 0.006 * smoothstep(0.45, 0.8, v.uv.y) * _Breath;
                p.x += sin(t * 0.6) * 0.004 * smoothstep(0.2, 1.0, v.uv.y) * _Breath;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(t.a - 0.08);
                float2 px = _MainTex_TexelSize.xy;
                float aR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(px.x * 14 * _FireSide, 0)).a;
                float aL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(px.x * 14 * _FireSide, 0)).a;
                float rimCool = saturate(t.a - aR);
                float rimFire = saturate(t.a - aL);
                float u = _FireSide > 0 ? i.uv.x : 1 - i.uv.x;
                float side = smoothstep(0.85, 0.0, u);
                float low = 1 - smoothstep(0.0, 0.8, abs(i.uv.y - 0.25));
                float flick = 0.92 + 0.08 * sin(IoriTime() * 9.1) * sin(IoriTime() * 5.3 + 1.0);
                float3 fireC = _IoriSpriteFire.rgb * flick;
                float fire = (0.2 + 0.7 * side) * (0.5 + 0.5 * low);
                float3 col = t.rgb * _IoriSpriteAmbient.rgb + t.rgb * fireC * fire + fireC * rimFire * 0.6 * (0.4 + 0.6 * low) + _IoriSpriteRim.rgb * rimCool * 0.3;
                float fog = IoriFogFactor(i.positionWS);
                col = lerp(col, _IoriFogColor.rgb, fog);
                return half4(col, t.a);
            }
            ENDHLSL
        }
    }
}
