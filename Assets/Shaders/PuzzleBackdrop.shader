Shader "PuzzleGlass/SoftBackdrop"
{
    Properties
    {
        _MainTex     ("Backdrop Texture", 2D)           = "white" {}
        _TintColor   ("Texture Tint", Color)            = (1.0, 1.0, 1.0, 1.0)
        _UseTexture  ("Use Texture Backdrop", Float)    = 0.0
        _TexAspect   ("Texture Aspect", Float)          = 1.3333
        _TopColor    ("Top Studio Wall", Color)         = (0.875, 0.878, 0.945, 1.0)
        _MidColor    ("Mid Studio Wall", Color)         = (0.928, 0.930, 0.970, 1.0)
        _BotColor    ("Tabletop Acrylic Stone", Color)  = (0.832, 0.838, 0.912, 1.0)
        _GlowColor   ("Center Studio Glow", Color)      = (0.980, 0.982, 0.998, 1.0)
        _DotColor    ("Subtle Pattern Tint", Color)     = (0.840, 0.845, 0.925, 1.0)
        _DotDensity  ("Pattern Density", Float)         = 14.0
        _DotStrength ("Pattern Strength", Range(0, 0.25)) = 0.0
        _FloatingStrength ("Floating Cubes Strength", Range(0, 1.0)) = 0.0
        _Aspect      ("Viewport Aspect", Float)         = 0.5625
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float  _UseTexture;
                float  _TexAspect;
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

                if (_UseTexture > 0.5)
                {
                    float viewAspect = max(0.25, _Aspect);
                    float texAspect  = max(0.25, _TexAspect);
                    float2 texUv = uv;
                    if (viewAspect < texAspect)
                    {
                        texUv.x = (uv.x - 0.5) * (viewAspect / texAspect) + 0.5;
                    }
                    else
                    {
                        texUv.y = (uv.y - 0.5) * (texAspect / viewAspect) + 0.5;
                    }
                    half3 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(texUv, 0.002, 0.998)).rgb * _TintColor.rgb;
                    return half4(texCol, 1.0);
                }

                // Clean, low-saturation pastel lavender studio wall (no murky patches or abrupt horizon bands)
                float wallT = smoothstep(0.24, 0.94, uv.y);
                float3 wallCol = lerp(_MidColor.rgb, _TopColor.rgb, wallT);

                // Soft studio spotlight halo behind the glass container & grid tray
                float aspect = max(0.35, _Aspect);
                float2 centered = float2((uv.x - 0.5) * aspect, uv.y - 0.45);
                float radialDist = length(centered);
                float centerGlow = 1.0 - smoothstep(0.05, 0.56, radialDist);
                wallCol = lerp(wallCol, _GlowColor.rgb, centerGlow * 0.55);

                // Cool acrylic/stone studio tabletop behind the lower half of the grid tray
                float horizonY = 0.225;
                float floorMask = 1.0 - smoothstep(horizonY - 0.025, horizonY + 0.028, uv.y);

                // Subtle studio cove crease right above the horizon for grounded 3D depth
                float crease = exp(-pow((uv.y - (horizonY + 0.014)) / 0.045, 2.0));
                wallCol *= lerp(1.0, 0.962, crease * (1.0 - floorMask));

                // Cool tabletop with gentle foreground brightening and wide soft tray contact shade
                float tableDepthT = saturate((horizonY - uv.y) / max(0.05, horizonY));
                float3 tableHighlight = lerp(_BotColor.rgb, _GlowColor.rgb, 0.34);
                float3 tableCol = lerp(_BotColor.rgb, tableHighlight, smoothstep(0.10, 0.88, tableDepthT) * 0.45);

                float2 tableShadowOffset = float2((uv.x - 0.5) * aspect * 0.72, (uv.y - 0.145) * 2.2);
                float contactShadow = exp(-dot(tableShadowOffset, tableShadowOffset) * 14.0);
                tableCol *= lerp(1.0, 0.925, contactShadow * floorMask);

                float3 col = lerp(wallCol, tableCol, floorMask);

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

                // Very soft framing vignette (barely perceptible, keeps focus centered cleanly)
                float2 edge = abs(uv - 0.5) * 2.0;
                float vig = saturate(dot(edge, edge) * 0.5);
                col *= lerp(1.0, 0.972, vig);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
