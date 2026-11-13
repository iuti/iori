// Sky: smooth three-color gradient (horizon pink, mid violet, zenith blue), a soft sun/moon glow,
// hard-edged cloud bands, stars and two thin streaks. Used as the skybox material.
// Directions are evaluated in the mock's coordinate frame (z towards the camera), so the
// values in CampPalette (sunDir) are the same as in the three.js mock.
Shader "IORI/Camp/Sky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "IoriCamp.hlsl"

            float4 _IoriSkyHorizon, _IoriSkyMid, _IoriSkyZenith, _IoriSunDir, _IoriSunColor, _IoriSkyParams, _IoriCloud, _IoriCloudDark, _IoriCloudParams;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), u.x), lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p) { float s = 0, a = 0.5; [unroll] for (int i = 0; i < 4; i++) { s += a * noise2(p); p *= 2.02; a *= 0.5; } return s; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                d.z = -d.z; // mock frame
                float h = abs(d.y);
                float3 col = lerp(_IoriSkyHorizon.rgb, _IoriSkyMid.rgb, smoothstep(0.0, 0.2, h));
                col = lerp(col, _IoriSkyZenith.rgb, smoothstep(0.1, 0.5, h));
                float3 sunDir = normalize(_IoriSunDir.xyz);
                float sd = max(dot(d, sunDir), 0.0);
                float glow = _IoriSkyParams.x, disc = _IoriSkyParams.y, stars = _IoriSkyParams.z, streak = _IoriSkyParams.w;
                col += _IoriSunColor.rgb * (pow(sd, 6.0) * 0.5 + pow(sd, 40.0) * 0.5) * glow;

                float cover = _IoriCloudParams.y;
                float2 cp = d.xz / (h + 0.22) * float2(1.2, 4.2);
                float c = fbm(cp + float2(3.1, 0.7) + float2(IoriTime() * 0.004, 0));
                float th = lerp(0.6, 0.4, cover);
                float cm = smoothstep(th, th + 0.025, c) * smoothstep(0.02, 0.08, h) * (1 - smoothstep(lerp(0.22, 0.7, cover), lerp(0.42, 0.95, cover), h));
                float3 cc = lerp(_IoriCloudDark.rgb, _IoriCloud.rgb, smoothstep(0.0, 0.3, 1 - h * 2.2) * (0.45 + 0.55 * pow(sd, 2.0)));
                col = lerp(col, cc, cm * _IoriCloudParams.x);

                float3 sp = floor(d * 300.0);
                float twinkle = 0.75 + 0.25 * sin(IoriTime() * (1.5 + hash3(sp + 3.0) * 3.0) + hash3(sp) * 40.0);
                float3 fp = frac(d * 300.0) - 0.5;
                float dotMask = smoothstep(0.3, 0.12, length(fp));
                float st = dotMask * step(0.9968, hash3(sp)) * (0.4 + 0.6 * hash3(sp + 7.0)) * stars * smoothstep(0.05, 0.28, h) * (1 - cm) * twinkle;
                float3 n1 = normalize(float3(0.34, 0.9, 0.26)), n2 = normalize(float3(-0.22, 0.93, 0.3));
                float s1 = smoothstep(0.0035, 0.0, abs(dot(d, n1))) * smoothstep(-0.7, 0.1, d.x) * smoothstep(0.95, 0.3, d.x);
                float s2 = smoothstep(0.0028, 0.0, abs(dot(d, n2))) * smoothstep(-0.3, 0.5, d.x);
                float sk = (s1 * 0.4 + s2 * 0.25) * streak * step(d.z, 0.0) * smoothstep(0.04, 0.16, h);
                col += float3(0.95, 0.95, 1.0) * st * 1.5 + float3(1.0, 0.92, 1.0) * sk;
                float mo = disc * smoothstep(0.99962, 0.99972, dot(d, sunDir));
                col = lerp(col, _IoriSunColor.rgb * 1.7, mo);
                // below the horizon (seen only in reflections at grazing angles): keep the horizon color
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
