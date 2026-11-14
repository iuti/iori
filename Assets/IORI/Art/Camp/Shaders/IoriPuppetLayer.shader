// One layer of the moving puppet, drawn into the two textures the character board then shows
// (PuppetRenderer): her colors and her normals, layer over layer, as a painting tool lays them.
// Both come out multiplied by alpha; the board's shader (IoriCharacter, _Premultiplied) divides again.
// The layers' colors are read as the numbers in the file (their textures are imported with sRGB off)
// and laid over each other as such, which is how the tool that drew them does it.
// No includes on purpose: it is drawn outside of any camera, with its own matrix.
Shader "IORI/Puppet/Layer"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Layer", 2D) = "white" {}
        [NoScaleOffset] _NormalTex ("Layer normals", 2D) = "white" {}
        _Alpha ("Alpha", Float) = 1
        _StencilRef ("Stencil value", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        float4x4 _PuppetVP;
        sampler2D _MainTex;
        sampler2D _NormalTex;
        float _Alpha;

        struct Attributes { float4 position : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        struct Targets { float4 color : SV_Target0; float4 normal : SV_Target1; };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.position = mul(_PuppetVP, float4(v.position.xy, 0.0, 1.0));
            o.uv = v.uv;
            return o;
        }

        // the mesh reaches a little past the layer's picture: nothing is drawn there
        float Inside(float2 uv)
        {
            float2 s = step(0.0, uv) * step(uv, 1.0);
            return s.x * s.y;
        }

        Targets fragLayer(Varyings i)
        {
            float4 t = tex2D(_MainTex, i.uv);
            float a = t.a * _Alpha * Inside(i.uv);
            Targets o;
            o.color = float4(t.rgb * a, a);
            o.normal = float4(tex2D(_NormalTex, i.uv).rgb * a, a);
            return o;
        }

        Targets fragMask(Varyings i)
        {
            clip(tex2D(_MainTex, i.uv).a * Inside(i.uv) - 0.5);
            Targets o;
            o.color = 0; o.normal = 0;
            return o;
        }
        ENDHLSL

        // 0: a layer
        Pass
        {
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragLayer
            ENDHLSL
        }
        // 1: the whites of an eye, into the stencil only
        Pass
        {
            ColorMask 0
            Stencil { Ref [_StencilRef] Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragMask
            ENDHLSL
        }
        // 2: a layer cut by what pass 1 wrote (the iris inside the whites)
        Pass
        {
            Blend One OneMinusSrcAlpha
            Stencil { Ref [_StencilRef] Comp Equal }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragLayer
            ENDHLSL
        }
    }
}
