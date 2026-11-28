Shader "PuzzleGlass/FakeDropShadow"
{
    Properties
    {
        _ShadowColor ("Warm Fake Shadow Color", Color) = (0.76, 0.60, 0.45, 0.36)
        _FeatherWidth ("Edge Feather Softness", Range(0.05, 0.95)) = 0.48
        _IsRadial ("Mode (0=Ribbon, 1=Oval, 2=PolyominoBox)", Float) = 0.0
        _LocalSize ("Quad Local Size", Vector) = (1.72, 1.84, 0, 0)
        _RectCount ("Shape Rect Count", Float) = 1.0
        _Rect0 ("Shape Rect 0 (cx,cy,hx,hy)", Vector) = (0.0, 0.024, 0.445, 0.425)
        _Rect1 ("Shape Rect 1 (cx,cy,hx,hy)", Vector) = (0.0, 0.0, 0.0, 0.0)
        _CornerRad ("Outer Corner Radius", Float) = 0.148
        _LipOffset ("3D Lower Lip Offset", Float) = 0.102
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-25"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "FakeDropShadowForward"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
                float _FeatherWidth;
                float _IsRadial;
                float4 _LocalSize;
                float _RectCount;
                float4 _Rect0;
                float4 _Rect1;
                float _CornerRad;
                float _LipOffset;
            CBUFFER_END

            #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
                UNITY_INSTANCING_BUFFER_START(Props)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _ShadowColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _LocalSize)
                    UNITY_DEFINE_INSTANCED_PROP(float, _RectCount)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _Rect0)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _Rect1)
                    UNITY_DEFINE_INSTANCED_PROP(float, _CornerRad)
                    UNITY_DEFINE_INSTANCED_PROP(float, _LipOffset)
                UNITY_INSTANCING_BUFFER_END(Props)
                #define GET_SHADOW_PROP(prop) UNITY_ACCESS_INSTANCED_PROP(Props, prop)
            #else
                #define GET_SHADOW_PROP(prop) prop
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half sdRoundBoxShadow(half2 p, half2 b, half r)
            {
                half2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0h) + length(max(q, 0.0h)) - r;
            }

            half sdPolyominoShadow(half2 q, half inset, half radius, half rectCount, half4 rect0, half4 rect1)
            {
                half rClamped = max(0.008h, radius);
                half2 b0 = max(half2(0.02h, 0.02h), rect0.zw - inset);
                half d = sdRoundBoxShadow(q - rect0.xy, b0, min(rClamped, min(b0.x, b0.y)));
                if (rectCount > 1.5h)
                {
                    half2 b1 = max(half2(0.02h, 0.02h), rect1.zw - inset);
                    half d1 = sdRoundBoxShadow(q - rect1.xy, b1, min(rClamped, min(b1.x, b1.y)));
                    d = min(d, d1);
                }
                return d;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 shadowCol = (half4)GET_SHADOW_PROP(_ShadowColor);
                half alpha;
                if (_IsRadial > 1.5)
                {
                    // Polyomino carrier block 2.5D fake drop shadow (contact AO + soft bottom falloff)
                    half4 localSize = (half4)GET_SHADOW_PROP(_LocalSize);
                    half  rectCount = (half)GET_SHADOW_PROP(_RectCount);
                    half4 rect0     = (half4)GET_SHADOW_PROP(_Rect0);
                    half4 rect1     = (half4)GET_SHADOW_PROP(_Rect1);
                    half  cornerRad = (half)GET_SHADOW_PROP(_CornerRad);
                    half  lipOffset = (half)GET_SHADOW_PROP(_LipOffset);

                    half2 q = ((half2)IN.uv - 0.5h) * max(localSize.xy, half2(0.25h, 0.25h));

                    half dContact = sdPolyominoShadow(q + half2(0.0h, lipOffset + 0.036h), -0.008h, cornerRad + 0.012h, rectCount, rect0, rect1);
                    half cBase    = 1.0h - smoothstep(-0.020h, 0.095h, dContact);
                    half contact  = cBase > 0.001h ? (half)pow((float)cBase, 1.55) : 0.0h;

                    half dDrop    = sdPolyominoShadow(q + half2(0.0h, lipOffset + 0.105h), -0.024h, cornerRad + 0.042h, rectCount, rect0, rect1);
                    half dBase    = 1.0h - smoothstep(-0.035h, 0.215h, dDrop);
                    half drop     = dBase > 0.001h ? (half)pow((float)dBase, 1.75) : 0.0h;

                    alpha = saturate(contact * 0.72h + drop * 0.44h) * shadowCol.a;
                }
                else if (_IsRadial > 0.5)
                {
                    // Soft oval contact shadow (for Monsters & Shoulder Caps)
                    half2 p = (half2)IN.uv * 2.0h - 1.0h;
                    half dist = length(p);
                    half falloff = 1.0h - smoothstep(0.10h, 1.0h, dist);
                    alpha = (falloff > 0.001h ? (half)pow((float)falloff, 1.45) : 0.0h) * shadowCol.a;
                }
                else
                {
                    // Contour-hugging ribbon drop shadow (for any ShapedGlassContainer contour!)
                    half edgeDist = 1.0h - abs((half)IN.uv.x * 2.0h - 1.0h);
                    half horizFade = smoothstep(0.0h, (half)_FeatherWidth, edgeDist);
                    half bottomTipFade = smoothstep(0.0h, 0.04h, (half)IN.uv.y);
                    alpha = (horizFade > 0.001h ? (half)pow((float)horizFade, 1.25) : 0.0h) * bottomTipFade * shadowCol.a;
                }

                if (alpha < 0.002h) discard;
                return half4(shadowCol.rgb, alpha);
            }
            ENDHLSL
        }
    }
}

