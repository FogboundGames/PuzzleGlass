Shader "PuzzleGlass/SlotSquircleUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _FrameColor ("Outer Frame Periwinkle", Color) = (0.62, 0.67, 0.98, 1.0)
        _FrameHighlight ("Frame Top-Left Highlight", Color) = (0.86, 0.89, 1.00, 1.0)
        _FrameEdgeShade ("Frame Outer Lip Shade", Color) = (0.49, 0.55, 0.89, 1.0)
        _CavityColor ("Inner Well Periwinkle", Color) = (0.42, 0.49, 0.88, 1.0)
        _CavityShadow ("Inner Well Cast Shadow", Color) = (0.28, 0.35, 0.75, 1.0)
        _FakeShadowColor ("Fake Drop Shadow Color", Color) = (0.74, 0.58, 0.44, 0.44)

        // Required for Unity UI Canvas (Image / RawImage) compatibility
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
            "Queue" = "Transparent-10"
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
            Name "SlotSquircleUIForward"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _FrameColor;
                float4 _FrameHighlight;
                float4 _FrameEdgeShade;
                float4 _CavityColor;
                float4 _CavityShadow;
                float4 _FakeShadowColor;
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
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Map UV [0,1] to [-1, +1] centered coordinates (with room around the outside for the soft fake shadow)
                float2 p = (IN.uv - 0.5) * 2.0;

                float2 outerHalf = float2(0.67, 0.67);
                float outerRad   = 0.245;

                float2 innerHalf = float2(0.455, 0.455);
                float innerRad   = 0.140;

                // 1. Soft Warm Fake Drop Shadow (offset down and slightly left, matching Image 2)
                float2 pShadowFar  = p - float2(-0.048, -0.095);
                float2 pShadowNear = p - float2(-0.022, -0.042);
                float dShadowFar   = sdRoundBox(pShadowFar, outerHalf, outerRad + 0.02);
                float dShadowNear  = sdRoundBox(pShadowNear, outerHalf, outerRad);

                float alphaShadowFar  = (1.0 - smoothstep(-0.10, 0.26, dShadowFar)) * _FakeShadowColor.a;
                float alphaShadowNear = (1.0 - smoothstep(-0.04, 0.11, dShadowNear)) * (_FakeShadowColor.a * 0.58);
                float shadowAlpha = saturate(alphaShadowFar + alphaShadowNear);
                float3 shadowRGB = _FakeShadowColor.rgb;

                // 2. Outer Pillowy Rounded-Square Frame SDF
                float dOuter = sdRoundBox(p, outerHalf, outerRad);
                float dInner = sdRoundBox(p, innerHalf, innerRad);

                float outerMask = 1.0 - smoothstep(-0.008, 0.008, dOuter);

                // Compute smooth 2D gradient normal of the outer rounded box for 3D pillowy frame shading
                float eps = 0.005;
                float2 gradOuter = normalize(float2(
                    sdRoundBox(p + float2(eps, 0.0), outerHalf, outerRad) - sdRoundBox(p - float2(eps, 0.0), outerHalf, outerRad),
                    sdRoundBox(p + float2(0.0, eps), outerHalf, outerRad) - sdRoundBox(p - float2(0.0, eps), outerHalf, outerRad)
                ) + 1e-5);

                // Normalized position across the frame thickness (0 = outer border, 1 = inner border)
                float ringT = saturate((-dOuter) / 0.215);

                // Arch profile across the frame rim: rises smoothly in the middle, curves down at outer & inner lips
                float radialSlope = cos(ringT * 3.14159265);
                float3 frameNormal = normalize(float3(gradOuter * radialSlope * 0.65, -0.76));

                float3 lightDir = normalize(float3(-0.38, 0.68, -0.62));
                float frameLit = saturate(dot(frameNormal, lightDir) * 0.55 + 0.45);

                // Base periwinkle frame shading
                float3 frameCol = lerp(_FrameEdgeShade.rgb, _FrameColor.rgb, smoothstep(0.15, 0.68, frameLit));

                // Creamy-icy top & top-left crest highlight (signature Image 2 glossy satin ridge)
                float crestMask = sin(ringT * 3.14159265);
                float topLeftCatch = saturate((-p.x * 0.35 + p.y * 0.75) + 0.38);
                float specRidge = pow(crestMask, 2.1) * topLeftCatch * 0.74;
                frameCol = lerp(frameCol, _FrameHighlight.rgb, saturate(specRidge));

                // Subtle 3D outer lip shading on bottom & left outer rim
                float outerLipMask = 1.0 - smoothstep(0.0, 0.048, -dOuter);
                float bottomLeftBias = saturate((-p.x * 0.35 - p.y * 0.65) + 0.45);
                frameCol = lerp(frameCol, _FrameEdgeShade.rgb * 0.88, outerLipMask * bottomLeftBias * 0.68);

                // Inner chamfer highlight along bottom-right inner edge of the frame
                float innerLipMask = 1.0 - smoothstep(0.0, 0.038, abs(dInner));
                float bottomRightCatch = saturate((p.x * 0.45 - p.y * 0.65) + 0.28);
                frameCol = lerp(frameCol, _FrameHighlight.rgb, innerLipMask * bottomRightCatch * 0.55);

                // 3. Recessed Inner Cavity Well (Image 2 rich medium periwinkle with top-left cast shadow)
                float innerMask = 1.0 - smoothstep(-0.008, 0.008, dInner);

                // Subtle vertical gradient inside the well
                float3 cavityCol = lerp(_CavityColor.rgb * 1.05, _CavityColor.rgb * 0.94, saturate(p.y * 0.5 + 0.5));

                // Top-left inner wall cast shadow inside the recessed well
                float dWellShadow = sdRoundBox(p - float2(0.040, -0.055), innerHalf, innerRad);
                float wellShadowMask = smoothstep(-0.058, 0.015, dWellShadow);
                float topLeftWellBias = saturate((-p.x * 0.42 + p.y * 0.72) + 0.38);
                cavityCol = lerp(cavityCol, _CavityShadow.rgb, wellShadowMask * topLeftWellBias * 0.72);

                // Soft ambient occlusion in the inner perimeter of the well
                float wellRimAO = 1.0 - smoothstep(-0.05, 0.0, dInner);
                cavityCol = lerp(cavityCol, _CavityShadow.rgb * 0.90, (1.0 - wellRimAO) * 0.34);

                // Combine Cavity inside Frame
                float3 slotRGB = lerp(frameCol, cavityCol, innerMask);

                // Re-apply subtle inner bevel rim highlight over the seam
                slotRGB = lerp(slotRGB, _FrameHighlight.rgb, innerLipMask * bottomRightCatch * 0.36);

                // Composite Slot over Fake Drop Shadow
                float finalAlpha = saturate(outerMask + shadowAlpha * (1.0 - outerMask));
                if (finalAlpha < 0.002) discard;

                float3 finalRGB = lerp(shadowRGB, slotRGB, outerMask);
                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}
