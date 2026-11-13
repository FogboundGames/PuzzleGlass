Shader "PuzzleGlass/SoftBackdrop"
{
    Properties
    {
        _TopColor    ("Top Gradient Color", Color)    = (0.945, 0.941, 0.988, 1.0)
        _MidColor    ("Middle Gradient Color", Color) = (0.980, 0.953, 0.914, 1.0)
        _BotColor    ("Bottom Gradient Color", Color) = (0.953, 0.894, 0.824, 1.0)
        _GlowColor   ("Center Soft Glow", Color)      = (1.000, 0.988, 0.968, 1.0)
        _DotColor    ("Subtle Pattern Tint", Color)   = (0.875, 0.840, 0.815, 1.0)
        _DotDensity  ("Pattern Density", Float)       = 14.0
        _DotStrength ("Pattern Strength", Range(0, 0.25)) = 0.055
        _Aspect      ("Viewport Aspect", Float)       = 0.5625
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "IgnoreProjector" = "True"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SoftBackdropForward"
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _MidColor;
                float4 _BotColor;
                float4 _GlowColor;
                float4 _DotColor;
                float  _DotDensity;
                float  _DotStrength;
                float  _Aspect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Smooth 3-stop vertical gradient (soft lavender mist -> warm cream -> pastel peach)
                float topBlend = smoothstep(0.45, 1.0, uv.y);
                float botBlend = 1.0 - smoothstep(0.0, 0.55, uv.y);
                float3 col = lerp(_MidColor.rgb, _TopColor.rgb, topBlend);
                col = lerp(col, _BotColor.rgb, botBlend);

                // Soft central spotlight halo behind the puzzle trays
                float2 centered = uv - float2(0.5, 0.52);
                centered.x *= max(0.35, _Aspect);
                float radialDist = length(centered);
                float centerGlow = 1.0 - smoothstep(0.05, 0.58, radialDist);
                col = lerp(col, _GlowColor.rgb, centerGlow * 0.36);

                // Subtle soft staggered dot pattern (casual mobile polish without distracting from balls)
                float2 aspectUv = float2((uv.x - 0.5) * max(0.35, _Aspect), uv.y - 0.5) * _DotDensity;
                // Rotate slightly (22 degrees) for diagonal honeycomb feel
                float cs = 0.927;
                float sn = 0.375;
                float2 rotUv = float2(aspectUv.x * cs - aspectUv.y * sn, aspectUv.x * sn + aspectUv.y * cs);
                float rowId = floor(rotUv.y);
                rotUv.x += fmod(abs(rowId), 2.0) * 0.5;
                float2 cell = frac(rotUv) - 0.5;
                float dotDist = length(cell);
                float dotMask = 1.0 - smoothstep(0.11, 0.22, dotDist);
                col = lerp(col, _DotColor.rgb, dotMask * _DotStrength);

                // Very soft warm edge vignette
                float2 edge = abs(uv - 0.5) * 2.0;
                float vig = saturate(dot(edge, edge) * 0.5);
                col *= lerp(1.0, 0.945, vig);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
