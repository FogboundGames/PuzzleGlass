Shader "PuzzleGlass/SoftBackdrop"
{
    Properties
    {
        _TopColor    ("Top Gradient Color", Color)    = (0.745, 0.775, 0.996, 1.0)
        _MidColor    ("Middle Gradient Color", Color) = (0.935, 0.922, 0.988, 1.0)
        _BotColor    ("Bottom Gradient Color", Color) = (0.996, 0.902, 0.825, 1.0)
        _GlowColor   ("Center Soft Glow", Color)      = (0.985, 0.980, 1.000, 1.0)
        _DotColor    ("Subtle Pattern Tint", Color)   = (0.820, 0.805, 0.910, 1.0)
        _DotDensity  ("Pattern Density", Float)       = 14.0
        _DotStrength ("Pattern Strength", Range(0, 0.25)) = 0.0
        _FloatingStrength ("Floating Cubes Strength", Range(0, 1.0)) = 0.72
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
                float  _FloatingStrength;
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

            float SoftBokehCube(float2 p, float2 center, float halfSize, float angle, float blur, out float shadeT)
            {
                float2 d = p - center;
                float c = cos(angle);
                float s = sin(angle);
                float2 r = float2(c * d.x + s * d.y, -s * d.x + c * d.y);
                float rad = halfSize * 0.34;
                float2 q = abs(r) - float2(halfSize, halfSize) + rad;
                float sdf = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - rad;
                shadeT = saturate(0.5 + (r.y - r.x * 0.45) / max(0.01, halfSize * 2.0));
                return 1.0 - smoothstep(-0.008, blur, sdf);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Smooth 3-stop vertical gradient (soft lavender mist -> luminous center -> pastel peach floor)
                float topBlend = smoothstep(0.38, 0.96, uv.y);
                float botBlend = 1.0 - smoothstep(0.04, 0.52, uv.y);
                float3 col = lerp(_MidColor.rgb, _TopColor.rgb, topBlend);
                col = lerp(col, _BotColor.rgb, botBlend);

                // Soft central spotlight halo behind the glass containers
                float2 centered = uv - float2(0.5, 0.50);
                centered.x *= max(0.35, _Aspect);
                float radialDist = length(centered);
                float centerGlow = 1.0 - smoothstep(0.06, 0.54, radialDist);
                col = lerp(col, _GlowColor.rgb, centerGlow * 0.52);

                // Dreamy out-of-focus 3D pastel cubes clearly placed inside visible left/right outer margins
                if (_FloatingStrength > 0.005)
                {
                    float2 p = float2((uv.x - 0.5) * max(0.35, _Aspect), uv.y - 0.5);
                    float3 lilacHi  = float3(0.84, 0.82, 1.00);
                    float3 lilacLo  = float3(0.60, 0.56, 0.95);
                    float3 amberHi  = float3(1.00, 0.86, 0.48);
                    float3 amberLo  = float3(0.97, 0.68, 0.18);
                    float3 deepVioletHi = float3(0.74, 0.70, 1.00);
                    float3 deepVioletLo = float3(0.49, 0.44, 0.92);

                    float st;
                    float c1 = SoftBokehCube(p, float2(-0.215,  0.225), 0.056,  0.34, 0.026, st);
                    col = lerp(col, lerp(lilacLo, lilacHi, st), c1 * _FloatingStrength * 0.68);

                    float c2 = SoftBokehCube(p, float2( 0.232,  0.215), 0.038, -0.32, 0.014, st);
                    col = lerp(col, lerp(amberLo, amberHi, st), c2 * _FloatingStrength * 0.96);

                    float c3 = SoftBokehCube(p, float2(-0.232,  0.020), 0.024,  0.46, 0.013, st);
                    col = lerp(col, lerp(amberLo, amberHi, st), c3 * _FloatingStrength * 0.72);

                    float c4 = SoftBokehCube(p, float2( 0.220, -0.008), 0.048, -0.34, 0.022, st);
                    col = lerp(col, lerp(lilacLo, lilacHi, st), c4 * _FloatingStrength * 0.72);

                    float c5 = SoftBokehCube(p, float2(-0.215, -0.350), 0.054,  0.42, 0.024, st);
                    col = lerp(col, lerp(deepVioletLo, deepVioletHi, st), c5 * _FloatingStrength * 0.76);

                    float c6 = SoftBokehCube(p, float2( 0.238, -0.285), 0.025,  0.34, 0.014, st);
                    col = lerp(col, lerp(amberLo, amberHi, st), c6 * _FloatingStrength * 0.76);
                }

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
