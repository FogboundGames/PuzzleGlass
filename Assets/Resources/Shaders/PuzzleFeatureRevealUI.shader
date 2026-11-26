Shader "PuzzleGlass/FeatureRevealUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        _RevealProgress ("Reveal Progress (0..1)", Range(0, 1)) = 0.6
        _ContentBounds ("Content V Bounds (minV, maxV, 0, 0)", Vector) = (0.12, 0.88, 0, 0)
        _SilhouetteColor ("Locked Silhouette Color", Color) = (0.04, 0.04, 0.07, 1.0)
        _EdgeGlowColor ("Reveal Edge Glow Color", Color) = (0.72, 0.92, 1.0, 0.9)
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.05)) = 0.009

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Pass
        {
            Name "FeatureRevealUIForward"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _RevealProgress;
                float4 _ContentBounds;
                float4 _SilhouetteColor;
                float4 _EdgeGlowColor;
                float _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half alpha = tex.a * IN.color.a;
                if (alpha <= 0.004h) discard;

                float progress = saturate(_RevealProgress);
                float minV = _ContentBounds.x;
                float maxV = max(minV + 0.001, _ContentBounds.y);
                float vNorm = saturate((IN.uv.y - minV) / (maxV - minV));

                half3 unlockedRgb = tex.rgb * IN.color.rgb;
                half3 lockedRgb = _SilhouetteColor.rgb * IN.color.rgb;

                if (progress >= 0.999)
                {
                    return half4(unlockedRgb, alpha);
                }
                if (progress <= 0.001)
                {
                    return half4(lockedRgb, alpha);
                }

                float soft = max(0.001, _EdgeSoftness);
                float revealMask = 1.0 - smoothstep(progress - soft, progress + soft, vNorm);
                half3 baseRgb = lerp(lockedRgb, unlockedRgb, (half)revealMask);

                // Crisp subtle glow right along the rising fill line
                float distToLine = abs(vNorm - progress);
                float edgeGlow = exp(-pow(distToLine / 0.009, 2.0)) * _EdgeGlowColor.a;
                half3 finalRgb = lerp(baseRgb, _EdgeGlowColor.rgb, (half)saturate(edgeGlow * 0.42));

                return half4(finalRgb, alpha);
            }
            ENDHLSL
        }
    }
}
