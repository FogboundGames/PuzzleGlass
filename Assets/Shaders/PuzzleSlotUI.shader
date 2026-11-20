Shader "PuzzleGlass/SlotSquircleUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SlotMode ("0=Regular, 1=VIP, 2=LockedPlus, 3=TrapezoidDock, 4=GlassBackdrop", Float) = 0.0
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

            // Rounded isosceles trapezoid with slightly larger top shoulder radius and tighter bottom corner radius
            float sdRoundTrapezoid(float2 p, float rBotWidth, float rTopWidth, float halfH, float cornerRad)
            {
                float y01 = saturate((p.y / max(0.05, halfH)) * 0.5 + 0.5);
                float rBot = cornerRad * 0.72;
                float rTop = cornerRad * 1.18;
                float rAvg = cornerRad * 0.95;
                float rCur = lerp(rBot, rTop, y01);
                return sdTrapezoid(p, max(0.05, rBotWidth - rBot), max(0.05, rTopWidth - rTop), max(0.05, halfH - rAvg)) - rCur;
            }

            float sdSeg2D(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / max(1e-6, dot(ba, ba)), 0.0, 1.0);
                return length(pa - ba * h);
            }

            // Exact Euclidean SDF for the 180-rotated broad sculpted purple dock platform:
            // Wide full-width top edge curving smoothly down into a generous rounded bottom plateau,
            // while keeping 3D lighting, lower base lip, and drop shadow oriented naturally downward.
            // İkizkenar yamuk (iq): r1 = alt yarı genişlik, r2 = üst yarı genişlik, he = yarı yükseklik
            float sdTrapezoidIQ(float2 p, float r1, float r2, float he)
            {
                float2 k1 = float2(r2, he);
                float2 k2 = float2(r2 - r1, 2.0 * he);
                p.x = abs(p.x);
                float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
                float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / dot(k2, k2), 0.0, 1.0);
                float sgn = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
                return sgn * sqrt(min(dot(ca, ca), dot(cb, cb)));
            }

            float sdChamferDock(float2 p, float r1, float r2, float he, float rad)
            {
                return sdTrapezoidIQ(p, max(0.01, r1 - rad), max(0.01, r2 - rad), max(0.01, he - rad)) - rad;
            }

            float sdSculptedApronDock(float2 p, float rBotWidth, float rTopWidth, float halfH, float cornerRad)
            {
                // Flip Y by 180 degrees for silhouette orientation while preserving world-down lighting & shadows
                float2 q = float2(abs(p.x), -p.y);
                float topR = clamp(cornerRad * 1.35, 0.08, halfH * 0.54);
                float botR = clamp(cornerRad * 0.40, 0.03, halfH * 0.20);
                float shoulderY = halfH - topR;
                float shoulderCenterX = max(0.08, rTopWidth - topR);
                float apronStartX = shoulderCenterX + topR;

                float2 pts[18];
                pts[0] = float2(0.0, halfH);
                // 6-step smooth rounded corner quarter-arc (zero polygonal facets)
                [unroll]
                for (int j = 0; j <= 6; j++)
                {
                    float theta = (1.0 - j / 6.0) * 1.5707963;
                    pts[1 + j] = float2(shoulderCenterX + topR * cos(theta), shoulderY + topR * sin(theta));
                }

                // Plush sweeping S-curve widening smoothly toward the full-width top edges
                float sideSpanY = max(0.08, shoulderY - (-halfH + botR));
                [unroll]
                for (int k = 1; k <= 8; k++)
                {
                    float t = k / 8.0;
                    float smoothS = t * t * (3.0 - 2.0 * t);
                    float fullArc = sin(t * 1.5707963);
                    float flare = lerp(smoothS, fullArc, 0.28);
                    pts[7 + k] = float2(lerp(apronStartX, rBotWidth, flare), shoulderY - t * sideSpanY);
                }

                pts[16] = float2(max(0.1, rBotWidth - botR * 0.25), -halfH);
                pts[17] = float2(0.0, -halfH);

                float minD = 1e6;
                float boundX = 0.0;
                [unroll]
                for (int i = 0; i < 17; i++)
                {
                    float2 a = pts[i];
                    float2 b = pts[i + 1];
                    minD = min(minD, sdSeg2D(q, a, b));

                    if (q.y <= a.y && q.y >= b.y && abs(a.y - b.y) > 1e-5)
                    {
                        float f = (a.y - q.y) / (a.y - b.y);
                        boundX = lerp(a.x, b.x, f);
                    }
                }

                bool inside = (q.y <= halfH && q.y >= -halfH && q.x <= boundX);
                return inside ? -minD : minD;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // =========================================================================
                // MODE 4: SCULPTED FROSTED SHOWCASE BACKDROP BEHIND THE GLASS CONTAINER
                // =========================================================================
                if (_SlotMode > 3.5)
                {
                    float2 quadSize = max(_QuadSize.xy, float2(0.5, 0.5));
                    float2 p = (IN.uv - 0.5) * quadSize;

                    float halfW     = max(0.25, _DockParams.x);
                    float halfH     = max(0.25, _DockParams.y);
                    float cornerRad = max(0.08, _DockParams.z);
                    float rimWidth  = max(0.03, _DockParams.w);
                    float lipOffset = 0.048;

                    float2 q = p - float2(0.0, 0.022);
                    float dFront = sdRoundBox(q, float2(halfW, halfH), cornerRad);
                    float dBase  = sdRoundBox(q + float2(0.0, lipOffset), float2(halfW, halfH), cornerRad);
                    float dTotal = min(dFront, dBase);

                    float aa = max(fwidth(dTotal) * 1.15, 0.003);

                    // Soft ambient studio shadow behind the showcase plaque
                    float dShadow = sdRoundBox(q + float2(0.0, lipOffset + 0.038), float2(halfW + 0.015, halfH + 0.015), cornerRad + 0.02);
                    float shadowAlpha = (1.0 - smoothstep(-0.04, 0.14, dShadow)) * 0.22;
                    float3 shadowRGB = float3(0.16, 0.15, 0.36);

                    // 3D Base Skirt
                    float bodyMask = 1.0 - smoothstep(-aa, aa, dTotal);
                    float skirtT = saturate((-dFront) / max(0.01, lipOffset));
                    float3 cardRGB = lerp(float3(0.27, 0.28, 0.56), float3(0.39, 0.41, 0.72), skirtT);

                    // Raised Bevelled Frame Rim (harmonized periwinkle-lavender)
                    float frontMask = 1.0 - smoothstep(-aa, aa, dFront);
                    float yGrad = saturate((q.y / halfH) * 0.5 + 0.5);
                    float3 rimCol = lerp(float3(0.60, 0.63, 0.90), float3(0.73, 0.76, 0.96), yGrad);

                    float topEdgeBand = frontMask - (1.0 - smoothstep(-0.020 - aa, -0.020 + aa, dFront));
                    rimCol = lerp(rimCol, float3(0.88, 0.91, 0.99), saturate(topEdgeBand) * saturate(yGrad * 1.25) * 0.85);

                    float dMidStep = dFront + rimWidth * 0.70;
                    float midStepMask = (1.0 - smoothstep(-aa, aa, dMidStep)) * frontMask;
                    rimCol = lerp(rimCol, float3(0.46, 0.48, 0.78), midStepMask * 0.85);
                    cardRGB = lerp(cardRGB, rimCol, frontMask);

                    // Recessed Showcase Interior Pad (twilight sky-lavender so bright glass highlights pop!)
                    float dInner = dFront + rimWidth;
                    float innerMask = (1.0 - smoothstep(-aa, aa, dInner)) * frontMask;
                    float dFloor = sdRoundBox(q + float2(0.0, 0.024), float2(halfW, halfH) - (rimWidth + 0.008), max(0.04, cornerRad - rimWidth));
                    float floorMask = (1.0 - smoothstep(-aa, aa, dFloor)) * innerMask;

                    float3 innerWallCol = lerp(float3(0.28, 0.30, 0.58), float3(0.36, 0.38, 0.68), saturate(dFloor / 0.024));
                    float3 padCol = lerp(float3(0.44, 0.48, 0.76), float3(0.38, 0.42, 0.70), yGrad);

                    // Soft radial studio spotlight in the center behind the glass curves
                    float2 normP = q / max(float2(halfW, halfH), float2(0.2, 0.2));
                    float radialHalo = 1.0 - smoothstep(0.08, 1.05, length(normP * float2(0.95, 1.05)));
                    padCol = lerp(padCol, float3(0.56, 0.61, 0.88), radialHalo * 0.46);

                    // Subtle diagonal satin sheen across the showcase panel
                    float diag = -normP.x * 0.55 + normP.y * 0.85;
                    float satinBand = exp(-pow((diag - 0.14) / 0.38, 2.0));
                    padCol = lerp(padCol, float3(0.68, 0.74, 0.96), satinBand * 0.16);

                    // Inner top recess overhang ambient shadow
                    float innerAO = 1.0 - smoothstep(-0.14, 0.0, dFloor);
                    padCol = lerp(padCol, float3(0.26, 0.28, 0.56), innerAO * 0.42);

                    float3 cavityCombined = lerp(innerWallCol, padCol, floorMask);
                    cardRGB = lerp(cardRGB, cavityCombined, innerMask);

                    float finalAlpha = saturate(bodyMask + shadowAlpha * (1.0 - bodyMask)) * IN.color.a;
                    if (finalAlpha < 0.002) discard;

                    float3 outRGB = lerp(shadowRGB, cardRGB * IN.color.rgb, bodyMask);
                    return half4(outRGB, finalAlpha);
                }

                // =========================================================================
                // MODE 3: LAYERED TRAPEZOID BACKING DOCK PLATFORM (Image 2 Shape & Layers)
                // =========================================================================
                if (_SlotMode > 2.5)
                {
                    // =====================================================================
                    // MODE 3: PARK ALANI TABLASI (Bus Traffic Fever renk ve oranlarıyla, ekrana dönük düz plaka)
                    // Yuvarlak köşeli plaka (üst = alt genişlik), düz asfalt zemin, üst ve yanlarda ince kabarık bordür,
                    // alt kenarda parlak bordür çizgisi, altında tablanın kalınlığı ve yumuşak gölge.
                    // =====================================================================
                    float2 quadSize = max(_QuadSize.xy, float2(0.5, 0.5));
                    float2 p = (IN.uv - 0.5) * quadSize;

                    float he   = _DockParams.x; // yüzeyin yarı yüksekliği
                    float rBot = _DockParams.y; // alt yarı genişlik (ekrandan taşar)
                    float rTop = _DockParams.z; // üst yarı genişlik (pahlı köşeler)
                    float rad  = _DockParams.w; // köşe yuvarlaklığı
                    const float lip   = 0.055;  // tablanın görünen kalınlığı (alt kenarın altında)
                    const float rimW  = 0.030;  // üst ve pah kenarlarındaki kabarık bordür
                    const float curbW = 0.026;  // alt kenardaki parlak bordür çizgisi

                    float2 q = p - float2(0.0, lip * 0.5 + 0.02);
                    float dTop  = sdChamferDock(q, rBot, rTop, he, rad);
                    float dLip  = sdChamferDock(q + float2(0.0, lip), rBot, rTop, he, rad);
                    float dBody = min(dTop, dLip);
                    float aa = max(fwidth(dBody) * 1.15, 0.0025);

                    // Yumuşak gölge
                    float dSh = sdChamferDock(q + float2(0.0, lip + 0.055), rBot + 0.02, rTop + 0.02, he, rad + 0.03);
                    float shadowAlpha = (1.0 - smoothstep(-0.04, 0.17, dSh)) * 0.30;
                    float3 shadowRGB = float3(0.16, 0.15, 0.32);

                    float bodyMask = 1.0 - smoothstep(-aa, aa, dBody);
                    float topMask  = 1.0 - smoothstep(-aa, aa, dTop);
                    float yN = saturate(q.y / max(0.1, he) * 0.5 + 0.5); // 0 alt kenar, 1 üst kenar

                    // Tablanın kalınlığı (yan yüz): koyu lavanta, aşağı doğru koyulaşır
                    float lipT = saturate(dTop / lip);
                    float3 dockRGB = lerp(float3(0.15, 0.15, 0.30), float3(0.07, 0.07, 0.18), lipT);

                    // Düz asfalt yüzey (alt kısım biraz daha açık)
                    float3 asphalt = lerp(float3(0.29, 0.29, 0.42), float3(0.25, 0.25, 0.38), yN);
                    // Kenarların hemen içinde hafif ortam gölgesi
                    float edgeAO = 1.0 - smoothstep(0.0, 0.10, -dTop);
                    asphalt = lerp(asphalt, asphalt * 0.90, edgeAO * 0.6);

                    // Üst ve yan kenarlarda ince kabarık bordür (alt kenarda parlak bordür çizgisine geçer)
                    float rimZone = smoothstep(0.10, 0.30, yN);
                    float rimBand = (1.0 - smoothstep(rimW - aa, rimW + aa, -dTop)) * rimZone;
                    float rimInnerLine = exp(-pow((-dTop - rimW) / 0.006, 2.0)) * rimZone;
                    float3 rimCol = lerp(float3(0.46, 0.46, 0.62), float3(0.67, 0.67, 0.82), saturate(1.0 - (-dTop) / rimW));
                    asphalt = lerp(asphalt, rimCol, rimBand);
                    asphalt = lerp(asphalt, float3(0.16, 0.16, 0.27), rimInnerLine * 0.45);

                    // Alt kenarda parlak bordür çizgisi (referanstaki beyaz kaldırım çizgisi)
                    float curbBand = (1.0 - smoothstep(curbW - aa, curbW + aa, -dTop)) * (1.0 - smoothstep(0.10, 0.30, yN));
                    asphalt = lerp(asphalt, float3(0.84, 0.84, 0.92), curbBand);

                    dockRGB = lerp(dockRGB, asphalt, topMask);

                    float finalAlpha = saturate(bodyMask + shadowAlpha * (1.0 - bodyMask)) * IN.color.a;
                    if (finalAlpha < 0.002) discard;

                    float3 finalRGB = lerp(shadowRGB, dockRGB * IN.color.rgb, bodyMask);
                    return half4(finalRGB, finalAlpha);
                }

                // =========================================================================
                // MODES 0, 1, 2: INDIVIDUAL DARK INDIGO SLOT (Regular, VIP, Locked '+')
                // =========================================================================
                float2 p = (IN.uv - 0.5) * 2.0;

                // Düz "park yeri" slotu: zeminden biraz koyu dolgu + ince açık kenar + yumuşak dış/iç fake shadow
                float2 spotHalf = float2(0.84, 0.84);
                float  spotRad  = 0.30;
                float  dSpot    = sdRoundBox(p, spotHalf, spotRad);
                float  aa       = max(fwidth(dSpot) * 1.15, 0.004);
                float  wellMask = 1.0 - smoothstep(-aa, aa, dSpot);       // slotun tamamı

                // Dış fake drop + temas gölgesi (quad sınırına taşmadan pürüzsüz sönümlenir)
                float  dDrop           = sdRoundBox(p + float2(0.0, 0.042), spotHalf + float2(0.012, 0.012), spotRad + 0.012);
                float  dropAlpha       = (1.0 - smoothstep(-0.01, 0.115, dDrop)) * 0.36;
                float  contactAlpha    = exp(-max(0.0, dSpot) / 0.032) * 0.24;
                float  edgeFade        = 1.0 - smoothstep(0.91, 1.0, max(abs(p.x), abs(p.y)));
                float  softOuterShadow = saturate(max(dropAlpha, contactAlpha)) * edgeFade;
                const float borderW = 0.075;
                float  dInner    = dSpot + borderW;
                float  innerMask = (1.0 - smoothstep(-aa, aa, dInner)) * wellMask; // kenarın içi
                float  yN = saturate(p.y * 0.5 + 0.5);

                float3 fillCol;
                float3 borderCol;
                if (_SlotMode > 0.5 && _SlotMode < 1.5)
                {
                    // VIP: altın kenar + koyu kehribar dolgu
                    fillCol   = lerp(float3(0.23, 0.12, 0.012), float3(0.19, 0.09, 0.010), yN);
                    borderCol = lerp(float3(0.83, 0.42, 0.026), float3(1.00, 0.71, 0.11), yN);
                }
                else if (_SlotMode > 1.5)
                {
                    // Kilitli "+": kesik çizgili koyu kenar, dolgu biraz daha koyu
                    fillCol = lerp(float3(0.14, 0.14, 0.23), float3(0.12, 0.12, 0.21), yN);
                    float perim = atan2(p.y, p.x);
                    float dash = smoothstep(-0.25, 0.25, cos(perim * 14.0));
                    borderCol = lerp(fillCol, float3(0.06, 0.06, 0.11), dash);
                }
                else
                {
                    // Normal: asfalttan biraz koyu dolgu + açık lavanta kenar
                    fillCol   = lerp(float3(0.16, 0.16, 0.25), float3(0.14, 0.14, 0.22), yN);
                    borderCol = lerp(float3(0.40, 0.41, 0.55), float3(0.51, 0.51, 0.67), yN);
                }

                // Blok üzerine gelince vurgu
                float hi = saturate(_Highlight);
                fillCol   = lerp(fillCol, float3(0.27, 0.27, 0.48), hi * 0.7);
                borderCol = lerp(borderCol, float3(0.83, 0.83, 1.00), hi * 0.6);

                // Kenar ve köşelerin hemen içinde üstten aşağı yumuşak gömülme gölgesi
                float dInnerSh  = sdRoundBox(p + float2(0.0, 0.055), spotHalf - borderW, max(0.05, spotRad - borderW));
                float innerDrop = smoothstep(-0.16, 0.02, dInnerSh);
                float recessAO  = exp(-max(0.0, -dInner) / 0.13) * (0.45 + 0.55 * yN);
                fillCol = lerp(fillCol, fillCol * 0.66, saturate(recessAO * 0.62 + innerDrop * 0.35));

                float3 slotRGB = lerp(borderCol, fillCol, innerMask);

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

                float3 finalRGB = lerp(float3(0.11, 0.11, 0.24), slotRGB * IN.color.rgb, wellMask);
                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}
