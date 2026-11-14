// Rain. The mesh is a cloud of thin quads (CampRain builds it): POSITION = where a streak starts,
// TEXCOORD0 = corner, TEXCOORD1.x = fall speed, .y = a random number. The vertex shader drops
// them, wraps them around, leans them with the wind, and turns them to face the camera.
// Streaks near the fire catch its light.
Shader "IORI/Camp/Rain"
{
    Properties
    {
        _Alpha ("Opacity", Range(0, 1)) = 0.3
        _Length ("Length (m)", Float) = 0.42
        _Width ("Width (m)", Float) = 0.007
        _Span ("Fall height (m)", Float) = 7
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+40" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Alpha, _Length, _Width, _Span;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : TEXCOORD0; float2 corner : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = IoriTime();
                float rain = _IoriWeather.x;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float y = v.positionOS.y - t * v.data.x;
                y = y - floor(y / _Span) * _Span;
                float along01 = v.corner.y * 0.5 + 0.5;
                float3 lean = float3(_IoriWind.x, 0.0, _IoriWind.y) * (0.05 + 0.06 * _IoriWind.z);
                p.y = y - 0.3 + along01 * _Length;
                p += lean * (p.y + along01 * _Length);
                float3 toCam = _WorldSpaceCameraPos - p;
                float3 side = normalize(float3(toCam.z, 0.0, -toCam.x));
                p += side * (v.corner.x * _Width * 0.5);

                float3 col = _IoriAmbientSky.rgb * 1.35 + _IoriAmbientEquator.rgb * 0.4;
                float3 toFire = _IoriFirePos.xyz - p;
                float fd = dot(toFire, toFire);
                col += _IoriGlow[1].rgb * (0.5 / (1.0 + fd * 1.6));
                // only some of the streaks show in light rain
                float shown = step(v.data.y, rain);
                float air = IoriAirAmount(p);
                // streaks right in front of the lens would cross half the picture: they fade out up close
                float nearFade = smoothstep(0.8, 2.2, length(toCam));
                o.color = float4(lerp(col, _IoriFogColor.rgb, air), _Alpha * shown * saturate(rain * 3.0) * (0.6 + 0.4 * along01) * nearFade);
                o.corner = v.corner;
                o.positionCS = shown > 0.0 ? TransformWorldToHClip(p) : float4(0, 0, 2, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a = 1.0 - abs(i.corner.x);
                return half4(i.color.rgb, i.color.a * saturate(a * 2.0));
            }
            ENDHLSL
        }
    }
}
