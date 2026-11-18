Shader "PuzzleGlass/SlotSquircleUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SlotMode ("0=Regular, 1=VIP, 2=LockedPlus, 3=TrapezoidDock", Float) = 0.0
        _Highlight ("Hover Highlight (0..1)", Float) = 0.0
        _QuadSize ("Quad World Size (w, h)", Vector) = (1, 1, 0, 0)
        _DockParams ("Dock Trapezoid (he, rBot, rTop, flare)", Vector) = (0.38, 1.55, 1.25, 0.16)

        // Required for Unity UI / Sprite compatibility
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
            ZTest LEqual
            ColorMask [_ColorMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _SlotMode;
                float  _Highlight;
                float4 _QuadSize;
                float4 _DockParams;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
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
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float sdPlus(float2 p, float armLen, float armThick, float r)
            {
                float dH = sdRoundBox(p, float2(armLen, armThick), r);
                float dV = sdRoundBox(p, float2(armThick, armLen), r);
                return min(dH, dV);
            }

            // Exact Euclidean SDF for an isosceles trapezoid:
            // r1 = bottom half-width (at y = -he), r2 = top half-width (at y = +he), he = half-height
            float sdTrapezoid(float2 p, float r1, float r2, float he)
            {
                float2 k1 = float2(r2, he);
                float2 k2 = float2(r2 - r1, 2.0 * he);
                p.x = abs(p.x);
                float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
                float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / dot(k2, k2), 0.0, 1.0);
                float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
                return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
            }

            // Rounded isosceles trapezoid with slightly larger top shoulder radius and tighter bottom corner radius (Image 2 style)
            float sdRoundTrapezoid(float2 p, float rBotWidth, float rTopWidth, float halfH, float cornerRad)
            {
                float y01 = saturate((p.y / max(0.05, halfH)) * 0.5 + 0.5);
                float rBot = cornerRad * 0.72;
                float rTop = cornerRad * 1.18;
                float rAvg = cornerRad * 0.95;
                float rCur = lerp(rBot, rTop, y01);
                return sdTrapezoid(p, max(0.05, rBotWidth - rBot), max(0.05, rTopWidth - rTop), max(0.05, halfH - rAvg)) - rCur;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // =========================================================================
                // MODE 3: LAYERED TRAPEZOID BACKING DOCK PLATFORM (Image 2 Shape & Layers)
                // =========================================================================
                if (_SlotMode > 2.5)
                {
                    float2 quadSize = max(_QuadSize.xy, float2(0.5, 0.5));
                    float2 p = (IN.uv - 0.5) * quadSize;

                    float he        = _DockParams.x; // front face half-height
                    float rBotWidth = _DockParams.y; // bottom half-width (wider)
                    float rTopWidth = _DockParams.z; // top half-width (narrower)
                    float cornerRad = _DockParams.w; // base corner radius
                    float lipOffset = 0.044;         // 3D lower base lip thickness
                    float rimWidth  = 0.050;         // raised perimeter border rim width

                    // Slight vertical centering offset so the 3D lip and bottom shadow fit inside the quad
                    float2 q = p - float2(0.0, 0.024);

                    float dFront = sdRoundTrapezoid(q, rBotWidth, rTopWidth, he, cornerRad);
                    float dBase  = sdRoundTrapezoid(q + float2(0.0, lipOffset), rBotWidth, rTopWidth, he, cornerRad);
                    float dTotal = min(dFront, dBase);

                    // Hardware derivative screen-space anti-aliasing (eliminates all pixelation!)
                    float aa = max(fwidth(dTotal) * 1.15, 0.0025);

                    // 0. Soft Ambient Drop Shadow beneath the trapezoid dock
                    float dShadow = sdRoundTrapezoid(q + float2(0.0, lipOffset + 0.032), rBotWidth + 0.015, rTopWidth + 0.015, he, cornerRad + 0.02);
                    float shadowAlpha = (1.0 - smoothstep(-0.035, 0.115, dShadow)) * 0.28;
                    float3 shadowRGB = float3(0.14, 0.12, 0.34);

                    // 1. 3D Lower Base Skirt (deep royal indigo)
                    float bodyMask = 1.0 - smoothstep(-aa, aa, dTotal);
                    float skirtT = saturate((-dFront) / max(0.01, lipOffset));
                    float3 skirtTop = float3(0.26, 0.24, 0.53);
                    float3 skirtBot = float3(0.17, 0.15, 0.39);
                    float3 dockRGB = lerp(skirtTop, skirtBot, saturate(1.0 - skirtT));
                    float outerDarkSeam = (1.0 - smoothstep(-aa * 1.6, 0.0, dTotal)) * bodyMask;
                    dockRGB = lerp(dockRGB, float3(0.13, 0.11, 0.32), outerDarkSeam * 0.45);

                    // 2. Raised Outer Trapezoid Border Rim (periwinkle-lavender with top highlight)
                    float frontMask = 1.0 - smoothstep(-aa, aa, dFront);
                    float yGrad = saturate((q.y / max(0.1, he)) * 0.5 + 0.5);
                    float3 rimBaseCol = lerp(float3(0.53, 0.55, 0.85), float3(0.64, 0.66, 0.93), yGrad);

                    // Bright top catchlight along the upper edge of the raised border rim
                    float topEdgeBand = frontMask - (1.0 - smoothstep(-0.016 - aa, -0.016 + aa, dFront));
                    float topWeight = saturate(yGrad * 1.2 - 0.15);
                    rimBaseCol = lerp(rimBaseCol, float3(0.79, 0.81, 0.98), saturate(topEdgeBand) * topWeight * 0.85);

                    // Intermediate stepped bevel ring just inside the outer border rim
                    float dMidStep = dFront + rimWidth * 0.72;
                    float midStepMask = (1.0 - smoothstep(-aa, aa, dMidStep)) * frontMask;
                    float3 midStepCol = float3(0.39, 0.39, 0.69);
                    rimBaseCol = lerp(rimBaseCol, midStepCol, midStepMask);

                    dockRGB = lerp(dockRGB, rimBaseCol, frontMask);

                    // 3. Recessed Inner Trapezoid Opening & 3D Inner Wall Step
                    float dInner = dFront + rimWidth;
                    float innerMask = (1.0 - smoothstep(-aa, aa, dInner)) * frontMask;

                    // Shifted deck floor exposes the 3D inner top & slanted side wall step under the raised rim
                    float innerWallDrop = 0.026;
                    float dDeckFloor = sdRoundTrapezoid(q + float2(0.0, innerWallDrop), rBotWidth, rTopWidth, he, cornerRad) + (rimWidth + 0.008);
                    float deckFloorMask = (1.0 - smoothstep(-aa, aa, dDeckFloor)) * innerMask;

                    float3 innerWallCol = lerp(float3(0.18, 0.16, 0.40), float3(0.23, 0.21, 0.47), saturate((dDeckFloor) / max(0.008, innerWallDrop)));

                    // 4. Main Sunken Trapezoid Deck Floor (twilight slate-indigo)
                    float3 deckTopCol = float3(0.28, 0.26, 0.54);
                    float3 deckBotCol = float3(0.34, 0.32, 0.61);
                    float3 deckRGB = lerp(deckBotCol, deckTopCol, yGrad);

                    // Soft ambient overhang shadow cast by the inner top/side wall onto the deck floor
                    float wallAO = 1.0 - smoothstep(-0.036, 0.0, dDeckFloor);
                    deckRGB = lerp(deckRGB, float3(0.22, 0.20, 0.45), wallAO * 0.55);

                    // Subtle bottom inner rim catchlight where the deck floor meets the bottom raised border
                    float botInnerCatch = (1.0 - smoothstep(-0.014, 0.0, dInner)) * saturate(1.0 - yGrad * 2.2);
                    deckRGB = lerp(deckRGB, float3(0.45, 0.46, 0.76), botInnerCatch * 0.55);

                    float3 innerCombined = lerp(innerWallCol, deckRGB, deckFloorMask);
                    dockRGB = lerp(dockRGB, innerCombined, innerMask);

                    float finalAlpha = saturate(bodyMask + shadowAlpha * (1.0 - bodyMask)) * IN.color.a;
                    if (finalAlpha < 0.002) discard;

                    float3 finalRGB = lerp(shadowRGB, dockRGB * IN.color.rgb, bodyMask);
                    return half4(finalRGB, finalAlpha);
                }

                // =========================================================================
                // MODES 0, 1, 2: INDIVIDUAL DARK INDIGO SLOT (Regular, VIP, Locked '+')
                // =========================================================================
                float2 p = (IN.uv - 0.5) * 2.0;

                // 1. Outer Recessed Dark Well & Soft Contact Shadow
                float2 wellHalf = float2(0.86, 0.86);
                float  wellRad  = 0.31;
                float  dWell    = sdRoundBox(p - float2(0.0, -0.015), wellHalf, wellRad);
                float  aa       = max(fwidth(dWell) * 1.15, 0.004);

                float softOuterShadow = (1.0 - smoothstep(-0.02, 0.09, dWell)) * 0.32;
                float wellMask = 1.0 - smoothstep(-aa, aa, dWell);
                float3 wellCol = float3(0.09, 0.08, 0.25); // Deep midnight navy-indigo well (Image 1 border)

                // 2. Slot Perimeter Bevel Rim
                float2 bevelHalf = float2(0.81, 0.81);
                float  bevelRad  = 0.28;
                float  dBevel    = sdRoundBox(p, bevelHalf, bevelRad);
                float  bevelMask = (1.0 - smoothstep(-aa, aa, dBevel)) * wellMask;

                float bevelY = saturate((p.y / bevelHalf.y) * 0.5 + 0.5);
                float3 bevelCol;
                float3 innerShadowCol;
                float3 bodyBaseCol;
                float3 bodyTopCol;

                if (_SlotMode > 0.5 && _SlotMode < 1.5)
                {
                    // VIP Slot: Warm Golden Rim + Deep Amber-Gold Recessed Pad
                    bevelCol       = lerp(float3(0.88, 0.64, 0.14), float3(1.00, 0.86, 0.36), bevelY);
                    innerShadowCol = float3(0.32, 0.20, 0.04);
                    bodyBaseCol    = float3(0.52, 0.35, 0.08);
                    bodyTopCol     = float3(0.45, 0.30, 0.06);
                }
                else if (_SlotMode > 1.5)
                {
                    // Locked '+' Slot: Dashed Dark Border Rim + Deep Indigo Pad (Image 2 style)
                    float angle = atan2(p.y, p.x);
                    float dashMask = smoothstep(-0.18, 0.18, cos(angle * 12.0));
                    float3 dashLight = lerp(float3(0.22, 0.20, 0.46), float3(0.28, 0.26, 0.55), bevelY);
                    float3 dashDark  = float3(0.08, 0.07, 0.22);
                    bevelCol       = lerp(dashDark, dashLight, dashMask);
                    innerShadowCol = float3(0.08, 0.07, 0.22);
                    bodyBaseCol    = float3(0.14, 0.12, 0.34);
                    bodyTopCol     = float3(0.12, 0.10, 0.30);
                }
                else
                {
                    // Regular Slot: Crisp Indigo-Violet Bevel Rim + Deep Dark Indigo Body (rgb 44,37,104)
                    bevelCol       = lerp(float3(0.26, 0.24, 0.55), float3(0.38, 0.36, 0.70), bevelY);
                    innerShadowCol = float3(0.11, 0.10, 0.28);
                    bodyBaseCol    = float3(0.175, 0.150, 0.415);
                    bodyTopCol     = float3(0.155, 0.135, 0.380);
                }

                // Hover highlight when a block is dragged over the slot
                float hi = saturate(_Highlight);
                bevelCol    = lerp(bevelCol, float3(0.56, 0.55, 0.88), hi * 0.55);
                bodyBaseCol = lerp(bodyBaseCol, float3(0.30, 0.28, 0.64), hi * 0.85);
                bodyTopCol  = lerp(bodyTopCol, float3(0.26, 0.24, 0.58), hi * 0.85);

                float3 slotRGB = lerp(wellCol, bevelCol, bevelMask);

                // 3. Inner Recessed Cavity Step & Dark Slot Body
                float2 innerHalf = float2(0.71, 0.71);
                float  innerRad  = 0.21;
                float  dInner    = sdRoundBox(p, innerHalf, innerRad);
                float  innerMask = (1.0 - smoothstep(-aa, aa, dInner)) * bevelMask;

                // Shifted floor exposes the 3D top inner recess shadow inside the slot
                float  dFloor    = sdRoundBox(p + float2(0.0, 0.072), innerHalf - float2(0.008, 0.008), innerRad * 0.94);
                float  floorMask = (1.0 - smoothstep(-aa, aa, dFloor)) * innerMask;

                float3 bodyCol = lerp(bodyBaseCol, bodyTopCol, bevelY);
                float  topFloorAO = 1.0 - smoothstep(-0.08, 0.0, dFloor);
                bodyCol = lerp(bodyCol, innerShadowCol, topFloorAO * 0.45);

                float3 cavityCombined = lerp(innerShadowCol, bodyCol, floorMask);
                slotRGB = lerp(slotRGB, cavityCombined, innerMask);

                // 4. Smooth Anti-Aliased 3D Green '+' Icon on Locked Slot (_SlotMode == 2)
                if (_SlotMode > 1.5 && _SlotMode < 2.5)
                {
                    float2 pPlus = p - float2(0.0, -0.015);
                    float armLen = 0.30;
                    float armThk = 0.096;
                    float armRad = 0.046;

                    // Dark outer outline & lower drop shadow
                    float dPlusOutline = min(
                        sdPlus(pPlus, armLen + 0.036, armThk + 0.036, armRad + 0.016),
                        sdPlus(pPlus - float2(0.0, -0.042), armLen + 0.032, armThk + 0.032, armRad + 0.014));
                    float outlineMask = (1.0 - smoothstep(-aa, aa, dPlusOutline)) * innerMask;
                    slotRGB = lerp(slotRGB, float3(0.04, 0.12, 0.13), outlineMask * 0.92);

                    // 3D darker green lower lip
                    float dPlusBase = sdPlus(pPlus - float2(0.0, -0.028), armLen, armThk, armRad);
                    float baseMask  = (1.0 - smoothstep(-aa, aa, dPlusBase)) * innerMask;
                    slotRGB = lerp(slotRGB, float3(0.11, 0.58, 0.20), baseMask);

                    // Vibrant emerald-lime front '+' face with top highlight
                    float dPlusFront = sdPlus(pPlus - float2(0.0, 0.006), armLen, armThk, armRad);
                    float frontPlusMask = (1.0 - smoothstep(-aa, aa, dPlusFront)) * innerMask;
                    float plusGrad = saturate(pPlus.y * 1.8 + 0.5);
                    float3 plusCol = lerp(float3(0.18, 0.82, 0.30), float3(0.34, 0.95, 0.44), plusGrad);
                    float plusRim = frontPlusMask - (1.0 - smoothstep(-0.018 - aa, -0.018 + aa, dPlusFront));
                    plusCol = lerp(plusCol, float3(0.65, 1.00, 0.70), saturate(plusRim) * plusGrad * 0.55);
                    slotRGB = lerp(slotRGB, plusCol, frontPlusMask);
                }

                float finalAlpha = saturate(wellMask + softOuterShadow * (1.0 - wellMask)) * IN.color.a;
                if (finalAlpha < 0.002) discard;

                float3 finalRGB = lerp(float3(0.07, 0.06, 0.20), slotRGB * IN.color.rgb, wellMask);
                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}
