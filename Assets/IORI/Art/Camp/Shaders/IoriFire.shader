// The campfire's flame: a real volume, not a picture on a card. The pixel shader walks a ray
// through the box around the fire and looks for the hottest point along it in a flame-shaped
// field of rising noise (several tongues leaning out from the middle). The hottest value picks one
// of four flat colors, so the flame reads as painted shapes, yet it has depth: it wraps around
// the logs, the logs hide what burns behind them, and it changes as the camera moves.
// The box is any cube; its size and place come from its transform. Flame size follows _IoriFire.x.
Shader "IORI/Camp/Fire"
{
    Properties
    {
        _Scale ("Height of the tallest tongue at full size (m)", Float) = 0.85
        _Steps ("Steps along the ray", Range(6, 32)) = 24
        _Rise ("Rise speed", Float) = 1.5
        _Stretch ("Tall shapes (smaller = taller)", Float) = 0.42
        _Sway ("Sway", Float) = 0.5
        _Core ("Core color", Color) = (1.0, 0.93, 0.62, 1)
        _Mid ("Middle color", Color) = (1.0, 0.70, 0.18, 1)
        _Outer ("Outer color", Color) = (1.0, 0.36, 0.06, 1)
        _Rim ("Rim color", Color) = (0.70, 0.12, 0.10, 1)
        _Bright ("Brightness (core, middle, outer, rim)", Vector) = (2.2, 1.5, 1.1, 0.7)
        _Bands ("Heat where each color starts (core, middle, outer, rim)", Vector) = (0.52, 0.30, 0.14, 0.05)
        _FrameRate ("Drawn frames per second (0 = smooth)", Float) = 0
        [Toggle] _UseDepth ("Stop at solid things (needs the depth texture)", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Front with depth, Back without)", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest (Always with depth, LessEqual without)", Float) = 8
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
            // With the depth texture: far faces, never rejected, and the walk stops at the scene's depth.
            // Without it (the mirror image, the low quality level): near faces with the normal depth test.
            Cull [_Cull]
            ZTest [_ZTest]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Scale, _Steps, _Rise, _Stretch, _Sway, _FrameRate, _UseDepth, _Cull, _ZTest;
                float4 _Core, _Mid, _Outer, _Rim, _Bright, _Bands;
            CBUFFER_END
            TEXTURE3D(_IoriNoise3D);
            float4 _IoriFire;          // x: flame size (1 = full), y: brightness
            float _IoriDepthFx;        // 1 when the camera depth texture is there

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

            // x, z of each tongue's foot, its height and its radius (metres, at full size)
            #define TONGUE_COUNT 6
            static const float4 TONGUES[TONGUE_COUNT] =
            {
                float4(0.00, 0.00, 1.00, 0.30),
                float4(0.15, 0.04, 0.66, 0.20),
                float4(-0.14, -0.07, 0.58, 0.19),
                float4(0.02, 0.13, 0.50, 0.16),
                float4(-0.05, -0.15, 0.44, 0.15),
                float4(0.20, -0.10, 0.36, 0.13)
            };

            float FlameHeat(float3 p, float t)
            {
                float y = p.y;
                float3 q = float3(p.x, (y - t * _Rise) * _Stretch, p.z);
                float3 q2 = float3(p.x, (y - t * _Rise * 1.7) * _Stretch, p.z);
                float n1 = SAMPLE_TEXTURE3D_LOD(_IoriNoise3D, sampler_LinearRepeat, q * 2.0, 0).r;
                float n2 = SAMPLE_TEXTURE3D_LOD(_IoriNoise3D, sampler_LinearRepeat, q2 * 4.2 + 0.3, 0).g;
                float2 sw = SAMPLE_TEXTURE3D_LOD(_IoriNoise3D, sampler_LinearRepeat, q * 0.8 + 0.37, 0).gr - 0.5;
                float n = (n1 + n2 * 0.35) / 1.35;
                float best = -1.0;
                for (int k = 0; k < TONGUE_COUNT; k++)
                {
                    float4 c = TONGUES[k];
                    float h = clamp(y / c.z, 0.0, 1.3);
                    float2 xz = p.xz - c.xy * (1.0 + 0.6 * h) + sw * (_Sway * h);
                    float r = c.w * pow(saturate(1.0 - h / 1.15), 0.8) * (0.55 + 0.45 * smoothstep(0.0, 0.14, h)) + 1e-4;
                    float d = 1.0 - length(xz) / r;
                    // noise bites more out of the flame the higher it gets, and it cools on the way up
                    float e = d - (n - 0.30) * (0.9 + 2.4 * h) - 0.25 * h;
                    best = max(best, e);
                }
                return saturate(best * 1.15) * smoothstep(-0.03, 0.05, y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // the flame's shapes are laid out for a tongue one unit tall; this is how many metres that unit is now
                float size = max(_IoriFire.x, 0.0) * _Scale;
                if (size < 0.02) discard;

                // the box in world space (no rotation expected)
                float3 boxC = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 boxH = 0.5 * float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                float3 foot = boxC - float3(0, boxH.y, 0);

                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.positionWS - ro);
                float3 inv = 1.0 / (abs(rd) > 1e-5 ? rd : float3(1e-5, 1e-5, 1e-5));
                float3 ta = (boxC - boxH - ro) * inv, tb = (boxC + boxH - ro) * inv;
                float3 tmin3 = min(ta, tb), tmax3 = max(ta, tb);
                float t0 = max(max(tmin3.x, tmin3.y), max(tmin3.z, 0.0));
                float t1 = min(tmax3.x, min(tmax3.y, tmax3.z));

                float2 suv = i.screenPos.xy / i.screenPos.w;
                if (_IoriDepthFx * _UseDepth > 0.5)
                {
                    // stop at whatever solid thing is in the way (logs, stones, the chair leg in front)
                    float eye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float3 fwd = -UNITY_MATRIX_V[2].xyz;
                    t1 = min(t1, eye / max(dot(rd, fwd), 1e-4));
                }
                if (t1 <= t0) discard;

                float time = IoriTime();
                if (_FrameRate > 0.5) time = floor(time * _FrameRate) / _FrameRate;

                int steps = (int)clamp(_Steps, 6.0, 32.0);
                float dt = (t1 - t0) / steps;
                float inv_size = 1.0 / size;
                float hottest = 0.0, at = t0;
                for (int s = 0; s < 32; s++)
                {
                    if (s >= steps) break;
                    float ts = t0 + (s + 0.5) * dt;
                    float hs = FlameHeat((ro + rd * ts - foot) * inv_size, time);
                    if (hs > hottest) { hottest = hs; at = ts; }
                }
                // look closer on both sides of the hottest step, so the colors meet in clean edges, not in stripes
                float reach = dt * 0.5;
                for (int r = 0; r < 3; r++)
                {
                    float ta2 = max(at - reach, t0), tb2 = min(at + reach, t1);
                    float ha = FlameHeat((ro + rd * ta2 - foot) * inv_size, time);
                    float hb = FlameHeat((ro + rd * tb2 - foot) * inv_size, time);
                    if (ha > hottest && ha >= hb) { hottest = ha; at = ta2; }
                    else if (hb > hottest) { hottest = hb; at = tb2; }
                    reach *= 0.5;
                }
                hottest *= saturate(_IoriFire.x * 1.5);

                float w = max(fwidth(hottest) * 0.75, 0.004);
                float3 col = 0;
                float alpha = 0;
                float m;
                m = smoothstep(_Bands.w - w, _Bands.w + w, hottest); col = lerp(col, _Rim.rgb * _Bright.w, m * 0.75); alpha = max(alpha, m * 0.75);
                m = smoothstep(_Bands.z - w, _Bands.z + w, hottest); col = lerp(col, _Outer.rgb * _Bright.z, m); alpha = max(alpha, m);
                m = smoothstep(_Bands.y - w, _Bands.y + w, hottest); col = lerp(col, _Mid.rgb * _Bright.y, m);
                m = smoothstep(_Bands.x - w, _Bands.x + w, hottest); col = lerp(col, _Core.rgb * _Bright.x, m);
                col *= _IoriFire.y;
                // seen through air like everything else, measured at the fire
                float air = IoriAirAmount(boxC);
                return half4(col * (1.0 - air), alpha * (1.0 - air * 0.5));
            }
            ENDHLSL
        }
    }
}
