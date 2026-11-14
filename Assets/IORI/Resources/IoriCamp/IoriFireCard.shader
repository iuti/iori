// The campfire's flame for phones: the flame of IoriFire.shader, worked out beforehand for every
// frame of a loop (tools/camp/bake_fire.py) and shown on a card that turns toward the camera. One
// pixel costs two looks at a picture instead of a walk through a volume.
// The picture holds the flame's heat, not its colours (R: square root of the hottest tongue's heat,
// G: how much a cooler tongue in front dims what burns behind it). The colours are painted here
// with the same bands and the same crisp outline as the volume, at any size, and two frames are
// mixed by their heat, so the outline moves from one to the next instead of fading.
// The card is a square from (0,0) to (1,1) at the fire's foot; its size and place come from here.
// Keep the colours the same as IoriFire.shader's, and _Grid and _Rect the same as bake_fire.py's.
Shader "IORI/Camp/FireCard"
{
    Properties
    {
        [NoScaleOffset] _Book ("Flip book (R: root of the heat, G: dimming)", 2D) = "black" {}
        _Grid ("Columns, rows, frames, seconds of the loop", Vector) = (12, 10, 120, 6)
        _Rect ("What a frame shows, in tongue heights: half its width, its bottom, its top", Vector) = (0.58, -0.06, 1.60, 0)
        _Scale ("Height of the tallest tongue at full size (m)", Float) = 0.82
        _Core ("Core color", Color) = (1.0, 0.94, 0.67, 1)
        _Mid ("Body color", Color) = (1.0, 0.77, 0.27, 1)
        _Outer ("Edge color", Color) = (0.98, 0.56, 0.15, 1)
        _Rim ("Outline color", Color) = (0.84, 0.36, 0.12, 1)
        _Bright ("Brightness (core, body, edge, outline)", Vector) = (1.3, 1.2, 1.1, 1.0)
        _Bands ("Heat where each color starts (core, body, edge, outline)", Vector) = (0.68, 0.38, 0.16, 0.08)
        _Soft ("Softness of the borders between the colors", Range(0.005, 0.2)) = 0.05
        _Inner ("Line where a near tongue crosses a far one", Range(0, 0.5)) = 0.28
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/IORI/Art/Camp/Shaders/IoriCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Grid, _Rect, _Book_TexelSize;
                float _Scale, _Soft, _Inner;
                float4 _Core, _Mid, _Outer, _Rim, _Bright, _Bands;
            CBUFFER_END
            TEXTURE2D(_Book);
            float4 _IoriFire;          // x: flame size (1 = full), y: brightness

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 foot : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float size = max(_IoriFire.x, 0.0) * _Scale;
                float3 foot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                // upright, turned toward the camera (the mirror cameras stand straight below the real one, so they see the same card)
                float3 toCam = _WorldSpaceCameraPos - foot;
                float2 side = float2(-toCam.z, toCam.x);
                side = dot(side, side) > 1e-8 ? normalize(side) : float2(1.0, 0.0);
                float x = lerp(-_Rect.x, _Rect.x, v.uv.x), y = lerp(_Rect.y, _Rect.z, v.uv.y);
                float3 positionWS = foot + float3(side.x * x, y, side.y * x) * size;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.foot = foot;
                return o;
            }

            // where a frame's pixel lies in the whole picture (frames run left to right, top row first)
            float2 InBook(float frame, float2 uv)
            {
                float row = floor(frame / _Grid.x), column = frame - row * _Grid.x;
                // (half a texel away from the frame's border, so the neighbouring frames do not show)
                float2 edge = 0.5 * _Book_TexelSize.xy * _Grid.xy;
                uv = clamp(uv, edge, 1.0 - edge);
                return float2((column + uv.x) / _Grid.x, (_Grid.y - 1.0 - row + uv.y) / _Grid.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_IoriFire.x * _Scale < 0.02) discard;

                float at = frac(IoriTime() / _Grid.w) * _Grid.z;
                float first = min(floor(at), _Grid.z - 1.0), mix = at - first;
                float second = first + 1.0 < _Grid.z ? first + 1.0 : 0.0;
                float2 a = SAMPLE_TEXTURE2D_LOD(_Book, sampler_LinearClamp, InBook(first, i.uv), 0).rg;
                float2 b = SAMPLE_TEXTURE2D_LOD(_Book, sampler_LinearClamp, InBook(second, i.uv), 0).rg;
                float2 s = lerp(a, b, mix);

                // a low fire burns cooler (as in the volume)
                float heat = s.r * s.r * saturate(_IoriFire.x * 1.5);
                float3 c = _Rim.rgb * _Bright.w;
                c = lerp(c, _Outer.rgb * _Bright.z, smoothstep(_Bands.z - _Soft, _Bands.z + _Soft, heat));
                c = lerp(c, _Mid.rgb * _Bright.y, smoothstep(_Bands.y - _Soft, _Bands.y + _Soft, heat));
                c = lerp(c, _Core.rgb * _Bright.x, smoothstep(_Bands.x - _Soft, _Bands.x + _Soft, heat));
                // a cooler tongue in front: what burns behind it shows through, a little redder
                float3 through = float3(1.0, 1.0 - 0.6 * _Inner, 1.0 - 1.6 * _Inner);
                c *= pow(max(through, 1e-3), s.g * 2.0);
                // the outline is crisp close up, and no finer than a pixel from afar
                float w = max(fwidth(heat) * 0.7, 0.012);
                float alpha = smoothstep(_Bands.w - w, _Bands.w + w, heat);

                float3 col = c * alpha * _IoriFire.y;
                float air = IoriAirAmount(i.foot);
                return half4(col * (1.0 - air), alpha * (1.0 - air * 0.5));
            }
            ENDHLSL
        }
    }
}
