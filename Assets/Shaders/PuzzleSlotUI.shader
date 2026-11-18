Shader "PuzzleGlass/SlotSquircleUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _FrameColor ("Top Face Lower Gradient", Color) = (0.83, 0.92, 1.00, 1.0)
        _FrameHighlight ("Rim & Top Specular White", Color) = (0.98, 0.99, 1.00, 1.0)
        _FrameEdgeShade ("3D Sky-Blue Lip & Border", Color) = (0.26, 0.66, 1.00, 1.0)
        _CavityColor ("Top Face Upper Gradient", Color) = (0.91, 0.96, 1.00, 1.0)
        _CavityShadow ("3D Lip Deep Side Shade", Color) = (0.17, 0.54, 0.94, 1.0)
        _FakeShadowColor ("Fake Drop Shadow Color", Color) = (0.18, 0.23, 0.35, 0.34)

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
                // Map UV [0,1] to [-1, +1] centered coordinates (with room around the outside for the soft shadow)
                float2 p = (IN.uv - 0.5) * 2.0;

                float2 faceHalf = float2(0.64, 0.59);
                float cornerRad = 0.175;

                // 1. Soft Cool Drop Shadow below the 3D tile
                float2 pShadowFar  = p - float2(0.015, -0.090);
                float2 pShadowNear = p - float2(0.008, -0.055);
                float dShadowFar   = sdRoundBox(pShadowFar, faceHalf + float2(0.015, 0.015), cornerRad + 0.025);
                float dShadowNear  = sdRoundBox(pShadowNear, faceHalf + float2(0.008, 0.008), cornerRad + 0.010);

                float alphaShadowFar  = (1.0 - smoothstep(-0.08, 0.24, dShadowFar)) * (_FakeShadowColor.a * 0.75);
                float alphaShadowNear = (1.0 - smoothstep(-0.04, 0.12, dShadowNear)) * (_FakeShadowColor.a * 0.65);
                float shadowAlpha = saturate(alphaShadowFar + alphaShadowNear);
                float3 shadowRGB = _FakeShadowColor.rgb;

                // 2. Extruded 3D Sky-Blue Lower Lip + Crisp Thin Blue Perimeter Outline
                float2 pBase = p - float2(0.0, -0.038);
                float dBase = sdRoundBox(pBase, faceHalf + float2(0.016, 0.016), cornerRad + 0.008);
                float baseMask = 1.0 - smoothstep(-0.010, 0.010, dBase);

                // Side/corner shading on the extruded blue bottom lip
                float sideCurve = saturate(abs(p.x) / faceHalf.x);
                float3 lipRGB = lerp(_FrameEdgeShade.rgb, _CavityShadow.rgb, smoothstep(0.55, 1.0, sideCurve) * 0.65);
                // Slight bright highlight near the top-center transition of the lip
                float bottomCenterGlow = (1.0 - smoothstep(0.0, 0.55, sideCurve)) * 0.22;
                lipRGB = lerp(lipRGB, float3(0.45, 0.78, 1.0), bottomCenterGlow);

                // 3. Elevated Frosted Icy-Blue / White Rounded-Square Top Pad
                float2 pFace = p - float2(0.0, 0.042);
                float dFace = sdRoundBox(pFace, faceHalf, cornerRad);
                float faceMask = 1.0 - smoothstep(-0.010, 0.010, dFace);

                // Subtle top-to-bottom icy pale-blue face gradient
                float faceY = saturate((pFace.y / faceHalf.y) * 0.5 + 0.5);
                float3 faceRGB = lerp(_FrameColor.rgb, _CavityColor.rgb, smoothstep(0.0, 1.0, faceY));

                // Crisp bright white inner bevel along the top & upper sides
                float rimMask = 1.0 - smoothstep(0.0, 0.055, -dFace);
                float topSideWeight = saturate((pFace.y / faceHalf.y) * 0.65 + 0.55);
                faceRGB = lerp(faceRGB, _FrameHighlight.rgb, rimMask * topSideWeight * 0.88);

                // Combine extruded blue lip & frosted top face, tinted by RawImage color (hover highlight)
                float3 slotRGB = lerp(lipRGB, faceRGB, faceMask) * IN.color.rgb;

                float bodyMask = saturate(max(baseMask, faceMask));
                float finalAlpha = saturate(bodyMask + shadowAlpha * (1.0 - bodyMask)) * IN.color.a;
                if (finalAlpha < 0.002) discard;

                float3 finalRGB = lerp(shadowRGB, slotRGB, bodyMask);
                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}
