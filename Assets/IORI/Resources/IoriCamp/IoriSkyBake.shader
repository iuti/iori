// Paints the far view (IoriSkyPaint.hlsl) into the wide picture that the skybox reads on phones.
// Drawn by CampSkyCache, a strip at a time, as one triangle over the whole picture.
Shader "Hidden/IORI/SkyBake"
{
    Properties
    {
        _MountTiles ("Fineness of the strokes on the mountains", Float) = 5
        _FacetLean ("How far the mountains' faces lean away from their peaks", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/IORI/Art/Camp/Shaders/IoriCommon.hlsl"
            #include "Assets/IORI/Art/Camp/Shaders/IoriFar.hlsl"
            #include "Assets/IORI/Art/Camp/Shaders/IoriSkyPaint.hlsl"

            float _IoriSkyBakeDirect;      // 1: with the stars and the town's lights

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                // (the place this pixel is later read from, whichever way up the device keeps its pictures)
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(IoriSkyPaint(IoriSkyCacheDir(i.uv), _IoriSkyBakeDirect), 1);
            }
            ENDHLSL
        }
    }
}
