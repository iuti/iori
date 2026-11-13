// Solid things in the camp: props, rocks, bark, the tent, the logs in the fire.
// Colors come from the mesh (vertex color = surface color, alpha = baked shade; TEXCOORD1 = glow
// channel, glow strength, gloss). Light and shadow are taken one brush stroke at a time (IoriCommon.hlsl).
// Two special cases: wood that burns (glowing cracks), and cloth with a lamp behind it (the tent).
Shader "IORI/Camp/Lit"
{
    Properties
    {
        _StrokeScale ("Stroke fineness (bigger = smaller strokes)", Float) = 2
        _Variation ("Lighter and darker strokes", Range(0, 1)) = 0.16
        [HDR] _TintA ("Some strokes lean toward", Color) = (1.08, 1.0, 0.9, 1)
        [HDR] _TintB ("Others lean toward", Color) = (0.86, 0.94, 1.12, 1)
        _TintAmount ("How much they lean", Range(0, 1)) = 0.6
        _Bristle ("Bristle streaks", Range(0, 1)) = 0.25
        _Wrap ("Light wraps around", Range(0, 1)) = 0.25
        _Through ("Light shines through (cloth)", Range(0, 1)) = 0
        _WetDarken ("Darker when wet", Range(0, 1)) = 0.3
        _WetGloss ("Shinier when wet", Range(0, 1)) = 0.35
        _LampAmount ("Lamp light taken (logs in the fire: little)", Range(0, 1)) = 1
        [Toggle(_EMBER)] _Ember ("Glowing cracks (burning wood)", Float) = 0
        [Toggle(_LAMP_INSIDE)] _LampInside ("Cloth lit from inside (the tent)", Float) = 0
        _InsideSpot ("  Size of the bright patch in front of the lamp (m)", Float) = 0.6
        _InsidePanel ("  One tone per panel (0 = smooth across the cloth)", Range(0, 1)) = 0.75
        _InsideOutside ("  Light from outside while the lamp is on", Range(0, 1)) = 0.3
        [HDR] _InsideCore ("  Right in front of the lamp", Color) = (1.75, 1.35, 0.60, 1)
        [HDR] _InsideHot ("  Near it", Color) = (1.30, 0.66, 0.10, 1)
        [HDR] _InsideMid ("  Farther", Color) = (0.86, 0.27, 0.035, 1)
        [HDR] _InsideDeep ("  At the rim", Color) = (0.40, 0.095, 0.02, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "IoriCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _StrokeScale, _Variation, _TintAmount, _Bristle, _Wrap, _Through, _WetDarken, _WetGloss, _Ember;
            float _LampAmount, _LampInside, _InsideSpot, _InsidePanel, _InsideOutside;
            float4 _TintA, _TintB, _InsideCore, _InsideHot, _InsideMid, _InsideDeep;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _EMBER
            #pragma shader_feature_local _LAMP_INSIDE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 albedo : TEXCOORD2;      // rgb: surface color (linear), a: baked shade
                float4 glow : TEXCOORD3;        // rgb: light given off, a: gloss
                float3 positionOS : TEXCOORD4;
                float heat : TEXCOORD5;
                float3 panelWS : TEXCOORD6;     // the tent: the middle of the panel this pixel is on
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.albedo = float4(IoriVertexAlbedo(v.color.rgb), v.color.a);
                o.positionOS = v.positionOS.xyz;
                o.panelWS = o.positionWS;
                #if defined(_EMBER)
                    o.glow = float4(0, 0, 0, v.data.z);
                    o.heat = v.data.y * step(6.5, v.data.x);
                #elif defined(_LAMP_INSIDE)
                    // how strongly the lamp's light arrives at this piece of cloth (glow channel 2); the rest of the tent has none
                    // the model also gives the middle of each panel (uv0: x and height, data.w: depth)
                    o.glow = float4(0, 0, 0, v.data.z);
                    o.heat = v.data.y * step(1.5, v.data.x) * step(v.data.x, 2.5);
                    o.panelWS = TransformObjectToWorld(float3(v.uv.x, v.uv.y, v.data.w));
                #else
                    o.glow = float4(IoriGlowOf(v.data, o.albedo.rgb), v.data.z);
                    o.heat = 0;
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                IoriStroke st = IoriStrokeAt(i.positionWS, n, _StrokeScale);
                float r2 = frac(st.rnd * 7.31 + 0.13);

                float3 albedo = i.albedo.rgb;
                float lean = (r2 - 0.5) * 2.0;
                albedo *= 1.0 + (st.rnd - 0.5) * 2.0 * _Variation + st.bristle * _Bristle;
                albedo *= lerp(float3(1, 1, 1), lean > 0.0 ? _TintA.rgb : _TintB.rgb, abs(lean) * _TintAmount);

                float wet = _IoriWeather.y * saturate(n.y * 2.0 + 0.6);
                albedo *= 1.0 - wet * _WetDarken;
                float gloss = saturate(i.glow.a + wet * _WetGloss);

                IoriLit L = IoriLighting(st.pos, n, viewDir, st.rnd, i.albedo.a, _Wrap, _Through, gloss);
                float3 col = IoriShade(albedo, L, _LampAmount);

                float3 glow = i.glow.rgb;
                #if defined(_LAMP_INSIDE)
                    // The tent: its cloth shows the lamp that hangs inside. A panel that lies between us and the
                    // lamp is pale yellow; panels away from that line deepen to orange and to a burnt orange at
                    // the rim. Each panel of the dome is mostly one tone (taken at its middle), like the facets
                    // of a paper lantern, and the bright panels move over the dome as the camera moves.
                    float level = _IoriTentLamp.w;
                    float spot = max(_InsideSpot, 0.1);
                    float3 toLamp = _IoriTentLamp.xyz - st.pos;
                    float3 ray = normalize(st.pos - _WorldSpaceCameraPos);
                    float frontHere = 1.0 - smoothstep(0.08, spot, length(toLamp - ray * dot(toLamp, ray)));
                    float3 toLampP = _IoriTentLamp.xyz - i.panelWS;
                    float3 rayP = normalize(i.panelWS - _WorldSpaceCameraPos);
                    float frontPanel = 1.0 - smoothstep(0.08, spot, length(toLampP - rayP * dot(toLampP, rayP)));
                    float front = lerp(frontHere, frontPanel, _InsidePanel);
                    float cloth = step(0.3, i.heat);
                    float arrive = saturate((i.heat - 0.3) / 0.95);
                    // cloth seen edge-on at the tent's outline is darker
                    float edgeOn = 1.0 - saturate(dot(n, viewDir));
                    float h = saturate(arrive * 0.3 + front * front * 0.8 - edgeOn * edgeOn * 0.3);
                    h = saturate((floor(h * 12.0 + st.rnd * 0.35) + 0.5) / 12.0);
                    float3 inside = lerp(_InsideDeep.rgb, _InsideMid.rgb, saturate(h / 0.35));
                    inside = lerp(inside, _InsideHot.rgb, saturate((h - 0.35) / 0.35));
                    inside = lerp(inside, _InsideCore.rgb, saturate((h - 0.70) / 0.30));
                    inside *= 1.0 + st.bristle * 0.12;
                    // the poles along the seams stay dark lines against the light
                    inside = lerp(_InsideDeep.rgb * 0.3, inside, cloth);
                    float lampOn = step(0.05, i.heat) * level;
                    col *= 1.0 - saturate(lampOn) * (1.0 - _InsideOutside);
                    glow = inside * lampOn;
                #elif defined(_EMBER)
                    // burning wood: thin glowing cracks where the wood is charred, and a slow breathing
                    float t = IoriTime();
                    float heat = i.heat;
                    float3 q = i.positionOS * 34.0;
                    float nz = IoriNoise3(q) * 0.65 + IoriNoise3(q * 2.3 + 7.0) * 0.35;
                    float breathe = 0.75 + 0.25 * sin(t * 1.7 + IoriNoise3(i.positionOS * 6.0) * 12.0);
                    float crack = smoothstep(0.07, 0.015, abs(nz - 0.5)) * smoothstep(0.25, 0.7, heat);
                    // whole glowing patches only where it is hottest (the bed of embers; on the logs just a spot here and there)
                    float core = smoothstep(0.85, 1.25, heat * (0.75 + 0.5 * IoriNoise3(i.positionOS * 11.0 + t * 0.15)));
                    float e = saturate(crack * 0.9 + core) * breathe;
                    float3 hot = _IoriGlow[7].rgb;
                    // cooler cracks are deep red, the hottest spots a bright orange (never white: white is the flame's)
                    glow = hot * e * lerp(float3(0.8, 0.12, 0.03), float3(1.0, 0.34, 0.05), saturate(core * 1.2 + crack * heat * 0.4));
                    col *= 1.0 - saturate(heat * 1.4) * 0.75;       // charred wood is nearly black
                #else
                    glow *= 1.0 + (st.rnd - 0.5) * 0.35;
                #endif
                col += glow;
                col = IoriAir(col, i.positionWS);
                return half4(col, 1);
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
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
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
