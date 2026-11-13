// Grass blades, leaves, ferns, reeds. Every blade or leaf is one brush stroke: the mesh stores
// its center (TEXCOORD0 = center x, z; TEXCOORD1.z = center height) and the light and shadow are
// taken there, in the vertex shader, so a blade is one flat color and the pixel shader is trivial.
// TEXCOORD1.y = tone (0 at the base of a blade, 1 at its tip), TEXCOORD1.w = how much it sways.
Shader "IORI/Camp/Foliage"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Leafy ("Season color: 0 follows the grass, 1 the leaves, -1 neither", Float) = 0
        _BaseShade ("Shade at the base of a blade", Range(0, 1)) = 0.55
        _Variation ("Lighter and darker pieces", Range(0, 1)) = 0.3
        _Wrap ("Light wraps around", Range(0, 1)) = 0.5
        _Through ("Light shines through", Range(0, 1)) = 0.45
        _UpNormal ("Lit as if facing up", Range(0, 1)) = 0
        _Sway ("Sway", Float) = 1
        _WetDarken ("Darker when wet", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+1" }
        Cull Off

        HLSLINCLUDE
        #include "IoriCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Leafy, _BaseShade, _Variation, _Wrap, _Through, _UpNormal, _Sway, _WetDarken;
        CBUFFER_END
        float4 _IoriSeasonGrass, _IoriSeasonLeaf;

        // Wind: a slow lean plus a quicker shiver, different for every piece; rain makes plants nod.
        float3 IoriSway(float3 posWS, float3 centerWS, float weight)
        {
            float t = IoriTime();
            float ph = dot(centerWS.xz, float2(0.71, 0.53));
            float gust = 0.6 + 0.4 * sin(t * 0.35 + centerWS.x * 0.21 + centerWS.z * 0.13);
            float a = sin(t * 1.3 + ph * 1.7) * 0.6 + sin(t * 2.9 + ph * 4.3) * 0.25;
            float w = weight * _Sway * _IoriWind.z * gust;
            posWS.xz += _IoriWind.xy * (a * 0.045 + 0.03) * w;
            posWS.xz += float2(sin(t * 3.7 + ph * 9.0), cos(t * 3.1 + ph * 7.0)) * 0.006 * w;
            posWS.y -= abs(sin(t * 7.0 + ph * 23.0)) * 0.012 * weight * _IoriWeather.x;
            return posWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            // light is taken in the vertex shader, so the shadow keywords are needed there too
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 center : TEXCOORD0;
                float4 data : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : TEXCOORD0;      // rgb: finished color
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centerWS = TransformObjectToWorld(float3(v.center.x, v.data.z, v.center.y));
                float3 posWS = IoriSway(TransformObjectToWorld(v.positionOS.xyz), centerWS, v.data.w);
                o.positionCS = TransformWorldToHClip(posWS);

                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - centerWS);
                // leaves are lit on whichever side we see; grass is lit from above
                n = dot(n, viewDir) < 0.0 ? -n : n;
                n = normalize(lerp(n, float3(0, 1, 0), _UpNormal));

                float rnd = IoriHash1(centerWS.xz * 37.0 + centerWS.y * 11.0);
                float tone = v.data.y;
                float3 seasonTint = _Leafy < -0.5 ? float3(1, 1, 1) : lerp(_IoriSeasonGrass.rgb, _IoriSeasonLeaf.rgb, saturate(_Leafy));
                float3 albedo = IoriVertexAlbedo(v.color.rgb) * _Tint.rgb * seasonTint;
                albedo *= 1.0 + (rnd - 0.5) * 2.0 * _Variation;
                albedo *= 1.0 - _IoriWeather.y * _WetDarken;
                float ao = lerp(_BaseShade, 1.0, saturate(tone * 1.4));

                IoriLit L = IoriLighting(centerWS + float3(0, 0.03, 0), n, viewDir, rnd, ao, _Wrap, _Through, 0.0);
                float3 col = IoriShade(albedo, L, 1.0);
                // the air is also taken once per piece
                float3 fogged = IoriAir(col, centerWS);
                o.color = float4(fogged, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 center : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centerWS = TransformObjectToWorld(float3(v.center.x, v.data.z, v.center.y));
                float3 positionWS = IoriSway(TransformObjectToWorld(v.positionOS.xyz), centerWS, v.data.w);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                // thin pieces: push along the light only, a normal bias would open holes
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, lightDirectionWS, lightDirectionWS)));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 center : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centerWS = TransformObjectToWorld(float3(v.center.x, v.data.z, v.center.y));
                o.positionCS = TransformWorldToHClip(IoriSway(TransformObjectToWorld(v.positionOS.xyz), centerWS, v.data.w));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
