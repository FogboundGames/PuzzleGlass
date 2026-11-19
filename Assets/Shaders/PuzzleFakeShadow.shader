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

            float sdRoundBoxShadow(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float sdPolyominoShadow(float2 q, float inset, float radius, float rectCount, float4 rect0, float4 rect1)
            {
                float rClamped = max(0.008, radius);
                float2 b0 = max(float2(0.02, 0.02), rect0.zw - inset);
                float d = sdRoundBoxShadow(q - rect0.xy, b0, min(rClamped, min(b0.x, b0.y)));
                if (rectCount > 1.5)
                {
                    float2 b1 = max(float2(0.02, 0.02), rect1.zw - inset);
                    float d1 = sdRoundBoxShadow(q - rect1.xy, b1, min(rClamped, min(b1.x, b1.y)));
                    d = min(d, d1);
                }
                return d;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 shadowCol = GET_SHADOW_PROP(_ShadowColor);
                float alpha;
                if (_IsRadial > 1.5)
                {
                    // Polyomino carrier block 2.5D fake drop shadow (contact AO + soft bottom falloff)
                    float4 localSize = GET_SHADOW_PROP(_LocalSize);
                    float  rectCount = GET_SHADOW_PROP(_RectCount);
                    float4 rect0     = GET_SHADOW_PROP(_Rect0);
                    float4 rect1     = GET_SHADOW_PROP(_Rect1);
                    float  cornerRad = GET_SHADOW_PROP(_CornerRad);
                    float  lipOffset = GET_SHADOW_PROP(_LipOffset);

                    float2 q = (IN.uv - 0.5) * max(localSize.xy, float2(0.25, 0.25));

                    float dContact = sdPolyominoShadow(q + float2(0.0, lipOffset + 0.036), -0.008, cornerRad + 0.012, rectCount, rect0, rect1);
                    float contact  = pow(1.0 - smoothstep(-0.020, 0.095, dContact), 1.55);

                    float dDrop    = sdPolyominoShadow(q + float2(0.0, lipOffset + 0.105), -0.024, cornerRad + 0.042, rectCount, rect0, rect1);
                    float drop     = pow(1.0 - smoothstep(-0.035, 0.215, dDrop), 1.75);

                    alpha = saturate(contact * 0.72 + drop * 0.44) * shadowCol.a;
                }
                else if (_IsRadial > 0.5)
                {
                    // Soft oval contact shadow (for Monsters & Shoulder Caps)
                    float2 p = IN.uv * 2.0 - 1.0;
                    float dist = length(p);
                    float falloff = 1.0 - smoothstep(0.10, 1.0, dist);
                    alpha = pow(falloff, 1.45) * shadowCol.a;
                }
                else
                {
                    // Contour-hugging ribbon drop shadow (for any ShapedGlassContainer contour!)
                    // uv.x = 0 at left outer shadow border, 0.5 at bottle center, 1 at right outer shadow border
                    float edgeDist = 1.0 - abs(IN.uv.x * 2.0 - 1.0);
                    float horizFade = smoothstep(0.0, _FeatherWidth, edgeDist);

                    // Soft fade at very bottom spout tip so it doesn't hard-clip
                    float bottomTipFade = smoothstep(0.0, 0.04, IN.uv.y);
                    alpha = pow(horizFade, 1.25) * bottomTipFade * shadowCol.a;
                }

                if (alpha < 0.002) discard;
                return half4(shadowCol.rgb, alpha);
            }
            ENDHLSL
        }
    }
}

