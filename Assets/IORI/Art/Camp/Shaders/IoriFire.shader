// The campfire's flame: a real volume, not a picture on a card. The pixel shader walks a ray
// through the box around the fire, through a flame made of several tongues of rising, bitten
// noise. For every tongue it keeps the hottest point it passed, then paints the tongues one over
// the other, the far ones first: each has a pale core, a yellow body and an orange edge, and the
// edge of a near tongue shows against the body of the one behind it. So the flame reads as
// painted, overlapping shapes, yet it has depth: it wraps around the logs, the logs hide what
// burns behind them, and it changes as the camera moves.
// The box is any cube; its size and place come from its transform. Flame size follows _IoriFire.x.
// (Tuned with a stand-alone copy of this arithmetic before it was written here.)
Shader "IORI/Camp/Fire"
{
    Properties
    {
        _Scale ("Height of the tallest tongue at full size (m)", Float) = 0.74
        _Steps ("Steps along the ray", Range(6, 32)) = 22
        _Rise ("Rise speed", Float) = 1.5
        _Stretch ("Tall shapes (smaller = taller)", Float) = 0.42
        _Sway ("Wavering", Float) = 0.5
        _Lean ("Tongues lean outward", Float) = 0.9
        _Curl ("Tips swing", Float) = 0.16
        _Breath ("Tongues grow and shrink", Range(0, 0.5)) = 0.18
        _Bite ("Noise bites the upper flame", Float) = 2.0
        _Cool ("Cooling toward the tips", Float) = 0.5
        _Core ("Core color", Color) = (1.0, 0.965, 0.80, 1)
        _Mid ("Body color", Color) = (1.0, 0.86, 0.45, 1)
        _Outer ("Edge color", Color) = (0.96, 0.58, 0.16, 1)
        _Rim ("Outermost rim, against the dark only", Color) = (0.84, 0.36, 0.12, 1)
        _Bright ("Brightness (core, body, edge, rim)", Vector) = (1.5, 1.25, 1.12, 0.9)
        _Bands ("Heat where each color starts (core, body, edge, rim)", Vector) = (0.66, 0.36, 0.12, 0.06)
        _Inner ("How strongly edges show inside the flame", Range(0, 1)) = 0.8
        _RimAmount ("How strongly the outermost rim shows", Range(0, 1)) = 0.6
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
                float _Scale, _Steps, _Rise, _Stretch, _Sway, _Lean, _Curl, _Breath, _Bite, _Cool, _Inner, _RimAmount, _FrameRate, _UseDepth, _Cull, _ZTest;
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

            // x, z of each tongue's foot, its height and its radius (in units of the tallest tongue), and a number
            // that sets each one's own rhythm
            #define TONGUE_COUNT 6
            static const float4 TONGUES[TONGUE_COUNT] =
            {
                float4(0.00, 0.02, 1.00, 0.27),
                float4(0.16, 0.05, 0.74, 0.21),
                float4(-0.16, -0.04, 0.66, 0.20),
                float4(0.03, 0.15, 0.56, 0.18),
                float4(-0.07, -0.16, 0.50, 0.17),
                float4(0.22, -0.10, 0.42, 0.15)
            };
            static const float PHASES[TONGUE_COUNT] = { 0.0, 1.7, 3.1, 4.4, 5.3, 2.4 };

            // what all tongues share at a point: x = how much is bitten away, yz = sideways wavering
            float3 FlameNoise(float3 p, float t)
            {
                float3 q = float3(p.x, (p.y - t * _Rise) * _Stretch, p.z);
                float3 q2 = float3(p.x, (p.y - t * _Rise * 1.7) * _Stretch, p.z);
                float n1 = SAMPLE_TEXTURE3D_LOD(_IoriNoise3D, sampler_LinearRepeat, q * 2.0, 0).r;
                float n2 = SAMPLE_TEXTURE3D_LOD(_IoriNoise3D, sampler_LinearRepeat, q2 * 4.2 + 0.3, 0).g;
                float2 sw = SAMPLE_TEXTURE3D_LOD(_IoriNoise3D, sampler_LinearRepeat, q * 0.8 + 0.37, 0).gr - 0.5;
                return float3((n1 + n2 * 0.35) / 1.35, sw);
            }

            // heat of one tongue at p. c: the tongue; now: xy = where its tip has swung to, z = its height right now
            float TongueHeat(float4 c, float3 now, float3 p, float3 nz)
            {
                float h = clamp(p.y / now.z, 0.0, 1.3);
                float2 xz = p.xz - c.xy * (1.0 + _Lean * h) + nz.yz * (_Sway * h) + now.xy * (h * h);
                float r = c.w * pow(saturate(1.0 - h / 1.15), 0.8) * (0.55 + 0.45 * smoothstep(0.0, 0.14, h)) + 1e-4;
                // noise bites more out of the flame the higher it gets, and it cools on the way up
                float e = 1.0 - length(xz) / r - (nz.x - 0.30) * (0.9 + _Bite * h) - _Cool * h;
                return saturate(e * 1.15) * smoothstep(-0.03, 0.05, p.y);
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

                // every tongue's own rhythm: its tip swings, and it grows and shrinks
                float3 now[TONGUE_COUNT];
                float best[TONGUE_COUNT], bestAt[TONGUE_COUNT];
                float3 bestNz[TONGUE_COUNT];
                int k;
                for (k = 0; k < TONGUE_COUNT; k++)
                {
                    float ph = PHASES[k];
                    now[k] = float3(sin(time * (1.6 + 0.21 * ph) + ph) * _Curl, cos(time * (1.3 + 0.17 * ph) + ph * 1.7) * _Curl,
                                    TONGUES[k].z * (1.0 + _Breath * sin(time * (2.1 + 0.37 * ph) + ph * 2.0)));
                    best[k] = 0.0; bestAt[k] = t0; bestNz[k] = float3(0.5, 0.0, 0.0);
                }

                // walk the ray: one look at the noise per step, shared by all tongues
                int steps = (int)clamp(_Steps, 6.0, 32.0);
                float dt = (t1 - t0) / steps;
                float inv_size = 1.0 / size;
                for (int s = 0; s < 32; s++)
                {
                    if (s >= steps) break;
                    float ts = t0 + (s + 0.5) * dt;
                    float3 p = (ro + rd * ts - foot) * inv_size;
                    float3 nz = FlameNoise(p, time);
                    for (k = 0; k < TONGUE_COUNT; k++)
                    {
                        float hk = TongueHeat(TONGUES[k], now[k], p, nz);
                        if (hk > best[k]) { best[k] = hk; bestAt[k] = ts; bestNz[k] = nz; }
                    }
                }
                // look closer on both sides of each tongue's hottest step (the noise held still: arithmetic only),
                // so the colors meet in clean edges, not in stripes
                for (k = 0; k < TONGUE_COUNT; k++)
                {
                    float reach = dt * 0.5;
                    for (int r = 0; r < 3; r++)
                    {
                        float ta2 = max(bestAt[k] - reach, t0), tb2 = min(bestAt[k] + reach, t1);
                        float ha = TongueHeat(TONGUES[k], now[k], (ro + rd * ta2 - foot) * inv_size, bestNz[k]);
                        float hb = TongueHeat(TONGUES[k], now[k], (ro + rd * tb2 - foot) * inv_size, bestNz[k]);
                        if (ha > best[k] && ha >= hb) { best[k] = ha; bestAt[k] = ta2; }
                        else if (hb > best[k]) { best[k] = hb; bestAt[k] = tb2; }
                        reach *= 0.5;
                    }
                }

                // far tongues first
                float small = saturate(_IoriFire.x * 1.5);
                float2 order[TONGUE_COUNT];          // x: where along the ray, y: heat
                for (k = 0; k < TONGUE_COUNT; k++) order[k] = float2(bestAt[k], best[k] * small);
                for (int a = 0; a < TONGUE_COUNT - 1; a++)
                    for (int b = 0; b < TONGUE_COUNT - 1 - a; b++)
                        if (order[b].x < order[b + 1].x) { float2 tmp = order[b]; order[b] = order[b + 1]; order[b + 1] = tmp; }

                // paint them one over the other
                float3 col = 0;
                float alpha = 0;
                for (k = 0; k < TONGUE_COUNT; k++)
                {
                    float heat = order[k].y;
                    float w = 0.012;
                    float mRim = smoothstep(_Bands.w - w, _Bands.w + w, heat);
                    float mOut = smoothstep(_Bands.z - w, _Bands.z + w, heat);
                    float mMid = smoothstep(_Bands.y - w, _Bands.y + w, heat);
                    float mCore = smoothstep(_Bands.x - w, _Bands.x + w, heat);
                    float3 c = _Rim.rgb * _Bright.w;
                    c = lerp(c, _Outer.rgb * _Bright.z, mOut);
                    c = lerp(c, _Mid.rgb * _Bright.y, mMid);
                    c = lerp(c, _Core.rgb * _Bright.x, mCore);
                    // an edge in front of another tongue is fainter than one against the dark; the rim only shows against the dark
                    float a2 = max(mMid, mOut * (1.0 - alpha * (1.0 - _Inner)));
                    a2 = max(a2, mRim * _RimAmount * (1.0 - alpha));
                    col = lerp(col, c, a2);
                    alpha = alpha + (1.0 - alpha) * a2;
                }
                col *= _IoriFire.y;
                // seen through air like everything else, measured at the fire
                float air = IoriAirAmount(boxC);
                return half4(col * (1.0 - air), alpha * (1.0 - air * 0.5));
            }
            ENDHLSL
        }
    }
}
