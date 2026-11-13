// Makes the soft, out-of-focus copy of the scene that the glass panels show behind them.
// The picture is halved several times and then doubled back (each step spreads it a little),
// which costs far less than one wide blur. The first step also turns the light amounts into
// screen values and cuts off anything brighter than white, so the result is what a browser's
// blur of the finished picture would give.
Shader "Hidden/IORI/UiBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _IoriBlurSpread;

        half3 Tap(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
        }

        // uv per pixel of the picture being written
        float2 PixelStep(float2 uv)
        {
            return float2(abs(ddx(uv.x)) + abs(ddy(uv.x)), abs(ddx(uv.y)) + abs(ddy(uv.y)));
        }

        half3 Down(float2 uv)
        {
            float2 h = PixelStep(uv) * 0.5 * _IoriBlurSpread;
            half3 sum = Tap(uv) * 4.0;
            sum += Tap(uv - h);
            sum += Tap(uv + h);
            sum += Tap(uv + float2(h.x, -h.y));
            sum += Tap(uv - float2(h.x, -h.y));
            return sum / 8.0;
        }

        half3 Up(float2 uv)
        {
            float2 h = PixelStep(uv) * _IoriBlurSpread;
            half3 sum = Tap(uv + float2(-h.x * 2.0, 0.0));
            sum += Tap(uv + float2(-h.x, h.y)) * 2.0;
            sum += Tap(uv + float2(0.0, h.y * 2.0));
            sum += Tap(uv + float2(h.x, h.y)) * 2.0;
            sum += Tap(uv + float2(h.x * 2.0, 0.0));
            sum += Tap(uv + float2(h.x, -h.y)) * 2.0;
            sum += Tap(uv + float2(0.0, -h.y * 2.0));
            sum += Tap(uv + float2(-h.x, -h.y)) * 2.0;
            return sum / 12.0;
        }
        ENDHLSL

        Pass
        {
            Name "First"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 h = PixelStep(uv) * 0.5;
                half3 a = saturate(Tap(uv - h));
                half3 b = saturate(Tap(uv + h));
                half3 c = saturate(Tap(uv + float2(h.x, -h.y)));
                half3 d = saturate(Tap(uv - float2(h.x, -h.y)));
                #ifndef UNITY_COLORSPACE_GAMMA
                a = LinearToSRGB(a); b = LinearToSRGB(b); c = LinearToSRGB(c); d = LinearToSRGB(d);
                #endif
                return half4((a + b + c + d) * 0.25, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Down"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(Down(input.texcoord), 1.0); }
            ENDHLSL
        }

        Pass
        {
            Name "Up"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(Up(input.texcoord), 1.0); }
            ENDHLSL
        }
    }
}
