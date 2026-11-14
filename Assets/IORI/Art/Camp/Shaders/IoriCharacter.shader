// The 2D character board: a finished drawing (her own cel shading, drawn in flat light) standing in
// the scene. She takes the same light as the scene, pixel by pixel, through a normal map made from
// the drawing (tools/character/make_sprite.py): the sides of her that face the fire take its warm
// light, the sides that face away keep only the sky's blue. No brush strokes on her; the border
// between lit and unlit is a soft cel edge. A warm rim on the side the fire is on and a thin cool
// rim on the other stay as a finishing touch. Without a normal map (or with its strength at 0) the
// whole board is lit as one flat thing facing the camera.
// The fire is what makes her sit in the scene, so its light is painted with some care:
// - It stands low and nearly in front of her. Lit from where it really is, every side that faces
//   the viewer is lit and nothing turns. So its light is turned toward the picture plane
//   (_LampFromSide): it comes from below and from the fire's side, the undersides glow, and the
//   tops of her shoulders, sleeves and thighs fall into the sky's blue.
//   A part of it still arrives from where the fire really is (_LampFill), so those tops are not cut
//   off from the fire altogether, and the border of its light is softer than a cel edge (_LampSoft):
//   a hard border between orange and blue on white cloth reads as satin or metal.
// - Next to a fire the eye is set to its brightness: the sides its light does not reach take less of
//   the sky's light too (_ShadeDeep). Without this the moonlit tops shine as bright as the lit sides.
// - It thins toward her head (_LampUpFade).
// - Just inside the border of the firelight her own color glows saturated and warm, as light does
//   that passes through skin (_EdgeGlow). It lies on the lit side only (laid over the blue side it
//   turns lavender), and white cloth takes little of it (_EdgeGlowOnWhite).
//   Gold fittings catch the fire (_Glint).
// - It flickers: her firelight swells and sinks with the fire, and the border between light and
//   shade moves with it and leans with the flame (_Flicker, _Dance; CampAtmosphere's _IoriFireFlicker).
// Drawn as a solid thing so that depth of field and shadows treat her properly.
Shader "IORI/Camp/Character"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Breath ("Breath sway", Float) = 1
        _RimWarm ("Fire rim", Range(0, 2)) = 0.7
        _RimCool ("Sky rim", Range(0, 2)) = 0.35
        [NoScaleOffset] _NormalTex ("Normals (r right, g up, b to viewer; sRGB off)", 2D) = "bump" {}
        _NormalStrength ("Normal strength (0 = flat board)", Range(0, 2)) = 1
        _Cel ("Cel edge amount (0 = smooth falloff)", Range(0, 1)) = 0.7
        _CelEdge ("Cel edge position", Range(-0.5, 0.5)) = 0.08
        _CelSoft ("Cel edge softness", Range(0.01, 0.5)) = 0.07
        _Wrap ("Light wrap of the smooth part", Range(0, 1)) = 0.35
        _SkyBack ("Sky light stepping back where a lamp lights her", Range(0, 1)) = 0.3
        _LampInDaylight ("Lamps fading on her as the sun or moon gets brighter", Range(0, 4)) = 1.2
        _LampFromSide ("Firelight turned into the picture plane (0 = from where the fire is)", Range(0, 1)) = 0.6
        _LampFill ("Firelight that still comes from where the fire is", Range(0, 1)) = 0.4
        _LampSoft ("Softness of the firelight's border", Range(0.01, 0.5)) = 0.14
        _LampUpFade ("Firelight thinning toward her head", Range(0, 1)) = 0.3
        _ShadeDeep ("Sky light held back on the sides the fire does not reach", Range(0, 1)) = 0.4
        _EdgeGlow ("Warm glow just inside the border of the firelight", Range(0, 3)) = 0.9
        _EdgeWidth ("Width of that glow", Range(0.02, 0.5)) = 0.12
        _EdgeGlowOnWhite ("How much of that glow white cloth takes", Range(0, 1)) = 0.25
        _Glint ("Fire glinting on gold fittings", Range(0, 8)) = 1
        _Flicker ("Her firelight swelling and sinking with the fire", Range(0, 3)) = 1
        _Dance ("The border of light moving with the flicker", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "IoriCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _Cutoff, _Breath, _RimWarm, _RimCool;
            float _NormalStrength, _Cel, _CelEdge, _CelSoft, _Wrap, _SkyBack, _LampInDaylight;
            float _LampFromSide, _LampUpFade, _EdgeGlow, _EdgeWidth, _Glint, _Flicker, _Dance;
            float _LampFill, _LampSoft, _ShadeDeep, _EdgeGlowOnWhite;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

        float3 IoriBreath(float3 p, float2 uv)
        {
            float t = IoriTime();
            p.y += sin(t * 1.5) * 0.006 * smoothstep(0.45, 0.8, uv.y) * _Breath;
            p.x += sin(t * 0.6) * 0.004 * smoothstep(0.2, 1.0, uv.y) * _Breath;
            return p;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 fire : TEXCOORD2;      // rgb: fire light for the rim, a: which side of the picture the fire is on (-1 left, 1 right)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = IoriBreath(v.positionOS.xyz, v.uv);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                float3 mid = TransformObjectToWorld(float3(0.0, v.positionOS.y, 0.0));
                float3 right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                float3 toFire = _IoriFirePos.xyz - mid;
                float fd = dot(toFire, toFire);
                o.fire = float4(_IoriGlow[1].rgb / (1.0 + fd * 0.35), dot(right, toFire) > 0.0 ? 1.0 : -1.0);
                return o;
            }

            // How much of a light a side takes: a cel edge (lit or not, with a soft border) mixed with a
            // smooth falloff, so the drawing keeps its flat look and still turns gently.
            // edge: where on the turn from facing the light to facing away the border lies.
            // soft: half the width of that border.
            float IoriCelAt(float nl, float edge, float soft)
            {
                float gentle = saturate((nl + _Wrap) / (1.0 + _Wrap));
                float hard = smoothstep(edge - soft, edge + soft, nl);
                return lerp(gentle, hard, _Cel);
            }
            float IoriCel(float nl) { return IoriCelAt(nl, _CelEdge, _CelSoft); }

            half4 frag(Varyings i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(t.a - _Cutoff);

                // the board's own directions: the picture's right, up, and out of the picture toward the viewer
                float3 right = normalize(TransformObjectToWorldDir(float3(1, 0, 0)));
                float3 up = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 front = normalize(TransformObjectToWorldDir(float3(0, 0, -1)));
                float3 nt = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv).xyz * 2.0 - 1.0;
                nt.xy *= _NormalStrength;
                float3 n = normalize(right * nt.x + up * nt.y + front * max(nt.z, 0.05));

                // shadows are looked up a little in front of the board, so the board does not shade itself
                float3 sp = i.positionWS + front * 0.08;
                float3 sky = IoriAmbient(n);
                Light m = GetMainLight(TransformWorldToShadowCoord(sp), sp, half4(1, 1, 1, 1));
                sky += m.color * (IoriCel(dot(n, m.direction)) * m.shadowAttenuation);
                float3 lamp = 0;
                float3 toFire = normalize(_IoriFirePos.xyz - sp);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                float flick = _IoriFireFlicker.x;
                float border = 0.0;     // how close this spot is to the border of the firelight (on its lit side)
                float glint = 0.0;
                float unlit = 0.0;      // near the fire, but on a side its light does not reach
                uint count = GetAdditionalLightsCount();
                for (uint k = 0u; k < count; ++k)
                {
                    Light l = GetAdditionalLight(k, sp, half4(1, 1, 1, 1));
                    float isFire = step(0.99, dot(l.direction, toFire));
                    // the fire's light, turned toward the picture plane and leaning with the flame
                    float3 turned = l.direction - front * (dot(l.direction, front) * _LampFromSide);
                    turned += (right * _IoriFireFlicker.z + up * _IoriFireFlicker.w) * (0.08 * _Dance);
                    float3 ld = normalize(lerp(l.direction, turned, isFire));
                    float nl = dot(n, ld);
                    // when the fire flares, its light reaches further around her
                    float edge = _CelEdge - isFire * _Dance * 0.12 * flick;
                    float take = IoriCelAt(nl, edge, lerp(_CelSoft, _LampSoft, isFire));
                    float reach = IoriLampFalloff(l.distanceAttenuation) * l.shadowAttenuation;
                    float close = saturate(max(l.color.r, max(l.color.g, l.color.b)) * reach * 0.25) * isFire;
                    float away = (nl - edge) / _EdgeWidth - 0.8;
                    border = max(border, exp(-away * away) * take * close);
                    unlit = max(unlit, (1.0 - take) * close);
                    // what still arrives from where the fire really is: a soft fill, no border of its own
                    float direct = saturate((dot(n, l.direction) + 0.5) / 1.5) * _LampFill * isFire;
                    take = max(take, direct);
                    lamp += l.color * (take * reach);
                    float3 h = normalize(ld + viewDir);
                    glint = max(glint, pow(saturate(dot(n, h)), 48.0) * take * close * 1.5);
                }
                // in daylight a fire hardly shows on a person next to it: the brighter the sun or the moon stands
                // (whether or not it reaches her), the less of the lamps she takes. Without this she turns orange at noon.
                float night = 1.0 / (1.0 + _LampInDaylight * max(m.color.r, max(m.color.g, m.color.b)));
                // the fire stands low: its light thins toward her head
                lamp *= night * (1.0 - _LampUpFade * smoothstep(0.15, 1.0, i.uv.y));
                // where a lamp lights her the sky's blue steps back a little, so the lit side is clearly warm
                float lw = max(lamp.r, max(lamp.g, lamp.b)), sw = max(sky.r, max(sky.g, sky.b));
                float warm = smoothstep(0.35, 0.65, lw / (lw + sw + 1e-4));
                sky *= 1.0 - _SkyBack * warm;
                sky *= 1.0 - _ShadeDeep * unlit * night;
                // she is a finished drawing with her own shading: the scene's light tints her and sets how
                // bright she is, but strong light is held back so that she never burns out next to the fire.
                // The flicker is laid on after that, or holding back would even it out again.
                float3 L = sky + lamp;
                float lm = max(L.r, max(L.g, L.b));
                float hold = 1.2 / (1.0 + 0.6 * lm);
                L = (sky + lamp * (1.0 + _Flicker * 0.2 * flick)) * hold;

                float3 col = t.rgb * L;
                // the fire's own color and how strongly it burns
                float3 fireColor = _IoriGlow[1].rgb;
                float fireLevel = max(fireColor.r, max(fireColor.g, fireColor.b));
                fireColor /= max(fireLevel, 1e-3);
                float fireShow = saturate(fireLevel) * night;
                // the border of the firelight: her own color at full strength, pushed toward red
                float v = max(t.r, max(t.g, t.b));
                float3 pure = t.rgb / max(v, 1e-3);
                float colored = saturate((1.0 - min(pure.r, min(pure.g, pure.b))) * 2.5);
                border *= lerp(_EdgeGlowOnWhite, 1.0, colored);
                col += pure * sqrt(pure) * (v * border * _EdgeGlow * fireShow) * fireColor * float3(1.0, 0.55, 0.35);
                // gold fittings (yellow paint) catch the fire
                float gold = saturate((t.r - t.b) * 2.5 - 0.35) * saturate((t.g - t.b) * 3.0);
                col += fireColor * (glint * gold * _Glint * fireShow);

                float2 px = float2(_MainTex_TexelSize.x * 14.0 * i.fire.a, 0.0);
                float aToFire = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + px).a;
                float aAway = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - px).a;
                float rimFire = saturate(t.a - aToFire);
                float rimCool = saturate(t.a - aAway);
                col += i.fire.rgb * (rimFire * _RimWarm) + _IoriAmbientSky.rgb * (rimCool * _RimCool);
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(IoriBreath(v.positionOS.xyz, v.uv));
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, lightDirectionWS, lightDirectionWS)));
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return 0;
            }
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IoriBreath(v.positionOS.xyz, v.uv));
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
}
