// Every shape of the IORI panels is drawn by this one shader, from numbers alone (no pictures):
// a rounded box with a top-to-bottom colour, a soft glow or shadow around it, a thin light along
// its top edge, and, for the glass panels, the blurred scene behind it mixed with the panel's colour.
//
// The numbers arrive in the vertices (see UiShape.cs):
//   uv0 = where this pixel is from the middle of the box (xy) and half the box's size (zw), in canvas units
//   uv1 = corner radius, glow sigma, top light strength, kind (1 glass, 2 round gradient, 4 picture)
//   uv2 = top colour packed, top alpha, bottom colour packed, bottom alpha
//   uv3 = glow colour packed, glow alpha, glow shift downwards, inner ring strength
//
// All colour sums are done the way a browser does them (on the screen values, not on light amounts),
// so the result matches the look proposal, and are turned into light amounts only at the end.
Shader "IORI/UI/Shape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float4 geo : TEXCOORD0;
                nointerpolation float4 p1 : TEXCOORD1;
                nointerpolation float4 p2 : TEXCOORD2;
                nointerpolation float4 p3 : TEXCOORD3;
                float4 world : TEXCOORD4;
                float4 screen : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _ClipRect;

            sampler2D _IoriUiBackdrop;      // the blurred scene, already as screen values
            float _IoriUiBackdropOn;        // 1 while the blurred scene is being made
            float _IoriUiBackdropFlip;
            float _IoriUiBackdropSaturation;
            float _IoriUiGround;            // how bright the things under the UI usually are (screen value)

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.geo = v.uv0;
                o.p1 = v.uv1;
                o.p2 = v.uv2;
                o.p3 = v.uv3;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float3 unpack(float v)
            {
                v = floor(v + 0.5);
                float r = floor(v / 65536.0);
                float g = floor((v - r * 65536.0) / 256.0);
                float b = v - r * 65536.0 - g * 256.0;
                return float3(r, g, b) / 255.0;
            }

            // distance to the edge of a rounded box: negative inside
            float sdRound(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // how much of a blurred edge is left at x (in units of sigma * sqrt 2) outside it: half of erfc
            float falloff(float x)
            {
                float ax = abs(x);
                float f = 0.5 * exp(-1.02 * ax - 0.83 * ax * ax);
                return x >= 0.0 ? f : 1.0 - f;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.geo.xy;
                float2 hs = i.geo.zw;
                float radius = i.p1.x;
                float sigma = i.p1.y;
                float kind = floor(i.p1.w + 0.5);
                bool glass = fmod(kind, 2.0) >= 1.0;
                bool radial = fmod(floor(kind / 2.0), 2.0) >= 1.0;
                bool picture = kind >= 4.0;

                float d = sdRound(p, hs, radius);
                float aa = max(fwidth(d), 1e-4);
                float cover = saturate(0.5 - d / aa);

                // the fill
                float t = saturate(0.5 - p.y / max(2.0 * hs.y, 1e-4));
                if (radial) t = saturate(length(p - float2(0.0, hs.y * 0.3)) / max(length(float2(hs.x, hs.y * 1.3)), 1e-4));
                float4 top = float4(unpack(i.p2.x), i.p2.y);
                float4 bottom = float4(unpack(i.p2.z), i.p2.w);
                float4 fill = lerp(top, bottom, t);

                // (the two pictures are read outside the branches: a branch may not hide a picture read)
                float4 tex = tex2D(_MainTex, p / max(2.0 * hs, 1e-4) + 0.5);
                float2 suv = i.screen.xy / i.screen.w;
                suv.y = lerp(suv.y, 1.0 - suv.y, step(0.5, _IoriUiBackdropFlip));
                float3 bd = tex2D(_IoriUiBackdrop, suv).rgb;

                if (picture)
                {
                    #ifndef UNITY_COLORSPACE_GAMMA
                    tex.rgb = LinearToGammaSpace(saturate(tex.rgb));
                    #endif
                    fill *= tex;
                }

                if (glass)
                {
                    float l = dot(bd, float3(0.299, 0.587, 0.114));
                    bd = saturate(lerp(float3(l, l, l), bd, _IoriUiBackdropSaturation));
                    float4 withScene = float4(lerp(bd, fill.rgb, fill.a), 1.0);
                    // without the blurred scene (it could not be made): a denser pane, so the text stays readable
                    float4 without = float4(fill.rgb, saturate(fill.a + 0.34));
                    fill = lerp(without, withScene, saturate(_IoriUiBackdropOn));
                }

                // the thin light along the top edge, and the faint ring just inside the edge
                float dUp = sdRound(p + float2(0.0, 1.0), hs, radius);
                float light = saturate(0.5 + dUp / aa) * i.p1.z;
                float ring = saturate(0.5 + (d + 1.0) / aa) * i.p3.w;
                float w = saturate(light + ring);
                float fa = w + fill.a * (1.0 - w);
                fill = float4((float3(w, w, w) + fill.rgb * fill.a * (1.0 - w)) / max(fa, 1e-4), fa);

                // the glow or shadow around it (only outside the box, as in a browser)
                float ga = 0.0;
                float3 gc = float3(0.0, 0.0, 0.0);
                if (i.p3.y > 0.0)
                {
                    gc = unpack(i.p3.x);
                    float dg = sdRound(p + float2(0.0, i.p3.z), hs, radius);
                    ga = i.p3.y * falloff(dg / max(sigma * 1.41421, 1e-3));
                }

                float a = fill.a * cover + ga * (1.0 - cover);
                float3 c = (fill.rgb * fill.a * cover + gc * ga * (1.0 - cover)) / max(a, 1e-5);

                // a breath of noise, so the smooth dark gradients do not show steps
                float n = frac(sin(dot(floor(i.pos.xy), float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                c = saturate(c + n / 255.0);

                #ifndef UNITY_COLORSPACE_GAMMA
                // The screen mixes light amounts, a browser mixes screen values. Choose the alpha that
                // gives the browser's result over a ground of the usual brightness.
                float lc = dot(c, float3(0.299, 0.587, 0.114));
                float g = _IoriUiGround;
                float gl = pow(g, 2.2);
                float ll = pow(max(lc, 0.0), 2.2);
                float tl = pow(lerp(g, lc, a), 2.2);
                float den = ll - gl;
                if (abs(den) > 0.004) a = saturate((tl - gl) / den);
                c = GammaToLinearSpace(c);
                #endif

                a *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return float4(c * a, a);
            }
            ENDCG
        }
    }
}
