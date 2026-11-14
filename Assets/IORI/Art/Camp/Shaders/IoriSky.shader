// Everything too far away to ever move against anything else, painted into the skybox:
// the sky itself (three colors, laid down as large brush strokes), sun glow, moon, stars, clouds,
// the mountain ranges across the lake with the lights of a town at their feet, the far shore's
// tree line, and the haze over the water. Being the skybox, all of it is mirrored in the lake
// and the puddles by the reflection camera at no extra cost (all but the stars and the town's lights:
// see "direct" below).
// The camp looks toward +z; azimuth 0 is straight across the lake.
// The mountains' outlines are a drawn composition (tools/camp/make_ridges.py -> iori_ridges.png, see IoriFar.hlsl).
Shader "IORI/Camp/Sky"
{
    Properties
    {
        _MountTiles ("Fineness of the strokes on the mountains", Float) = 5
        _FacetLean ("How far the mountains' faces lean away from their peaks", Float) = 0.7
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            // phones: the far view is read from a picture that is painted a strip at a time (CampSkyCache)
            #pragma multi_compile_fragment _ IORI_SKY_CACHED
            #include "IoriCommon.hlsl"
            #include "IoriFar.hlsl"
            #include "IoriSkyPaint.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                // The mirror cameras (the only ones that ever sit below the ground) leave out the stars and the
                // lights of the town: such small points only flicker in a mirror image, and the lake draws the
                // town's lights on the water itself (IoriWater).
                // (The earlier water shader, IoriWaterClassic, has no lights of its own: for it they are drawn after all.)
                float direct = max(step(0.0, _WorldSpaceCameraPos.y), _IoriMirrorLights);
            #if defined(IORI_SKY_CACHED)
                float3 col = IoriSkyFromCache(d, direct);
            #else
                float3 col = IoriSkyPaint(d, direct);
            #endif
                // the air around the lamps glows in front of the far view as well
                col += IoriLampAir(_WorldSpaceCameraPos + d * 3000.0);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
