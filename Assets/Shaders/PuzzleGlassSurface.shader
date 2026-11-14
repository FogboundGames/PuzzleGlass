Shader "PuzzleGlass/GlassChamberLit"
{
    Properties
    {
        _CenterColor ("Center Icy Sky-Blue", Color) = (0.76, 0.91, 1.00, 0.20)
        _EdgeColor ("Thick Wall Sky-Blue", Color) = (0.36, 0.71, 0.98, 0.78)
        _OuterRimColor ("Outer Cobalt Silhouette Rim", Color) = (0.17, 0.49, 0.85, 0.92)
        _InnerGrooveColor ("Inner Cobalt Meniscus Groove", Color) = (0.19, 0.52, 0.87, 0.88)
        _RimGlowColor ("Inner Caustic Cyan-White", Color) = (0.96, 0.99, 1.00, 0.95)
        _WallThickness ("Physical Wall Thickness (World Units)", Range(0.03, 0.16)) = 0.086
        _SheenIntensity ("Glass Specular Intensity", Range(0.0, 1.5)) = 0.85
        _IsFrontLens ("Front Glass Specular Lens Mode", Float) = 0.0
        _IsRoundedBoxPool ("Image 2 Rounded Box Pool Mode", Float) = 1.0
        _BoxAspect ("Box Width/Height Aspect", Float) = 1.88
        _BoxCornerRadius ("Rounded Box Corner Radius", Range(0.04, 0.65)) = 0.22
        _FrameThickness ("Rounded Box Frame Thickness", Range(0.04, 0.24)) = 0.115
        _FrameColor ("Outer Frame Periwinkle", Color) = (0.50, 0.50, 0.90, 1.0)
        _FrameHighlight ("Frame Top Highlight", Color) = (0.84, 0.84, 0.98, 1.0)
        _FrameEdgeShade ("Frame Outer Lip Shade", Color) = (0.38, 0.36, 0.80, 1.0)
        _FakeShadowColor ("Drop Shadow Color", Color) = (0.72, 0.60, 0.48, 0.30)
        _PolyCount ("Custom Pool Polygon Vertex Count (0 = rounded box)", Float) = 0
        _PolyRadius ("Custom Pool Inner Corner Radius", Float) = 0.1
        _PolyBounds ("Custom Pool Inner Bounds (minX, minY, maxX, maxY)", Vector) = (-1, -1, 1, 1)
        _JarNeckParams ("Optional Jar Neck (halfW, height, lipOverhang, lipHalfH)", Vector) = (0, 0, 0, 0)
        _CenterGlassAlpha ("Back Chamber Center Alpha", Range(0.05, 1.0)) = 0.20
        _IsCollar ("Periwinkle 3D Collar Mode", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-5"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GlassChamberForward"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CenterColor;
                float4 _EdgeColor;
                float4 _OuterRimColor;
                float4 _InnerGrooveColor;
                float4 _RimGlowColor;
                float _WallThickness;
                float _SheenIntensity;
                float _IsFrontLens;
                float _IsRoundedBoxPool;
                float _BoxAspect;
                float _BoxCornerRadius;
                float _FrameThickness;
                float4 _FrameColor;
                float4 _FrameHighlight;
                float4 _FrameEdgeShade;
                float4 _FakeShadowColor;
                float _PolyCount;
                float _PolyRadius;
                float4 _PolyBounds;
                float4 _JarNeckParams;
                float _CenterGlassAlpha;
                float _IsCollar;
            CBUFFER_END

            // Özel şekilli havuz: iç hattın köşeleri (yarıçap kadar içeri alınmış), iki köşe bir float4'te.
            // Kutu modunda kullanılmaz (_PolyCount = 0).
            float4 _PolyPoints[48];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.uv2 = IN.uv2;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            float sdRoundBox2D(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float2 PolyVertex(int i)
            {
                float4 v = _PolyPoints[i >> 1];
                return (i & 1) == 0 ? v.xy : v.zw;
            }

            // İşaretli çokgen mesafesi (içeride negatif)
            float sdPolygon(float2 p)
            {
                int n = (int)_PolyCount;
                float2 v0 = PolyVertex(0);
                float d = dot(p - v0, p - v0);
                float s = 1.0;
                [loop]
                for (int i = 0; i < 96; i++)
                {
                    if (i >= n) break;
                    int j = (i == 0) ? (n - 1) : (i - 1);
                    float2 vi = PolyVertex(i);
                    float2 vj = PolyVertex(j);
                    float2 e = vj - vi;
                    float2 w = p - vi;
                    float2 b = w - e * clamp(dot(w, e) / max(dot(e, e), 1e-6), 0.0, 1.0);
                    d = min(d, dot(b, b));
                    bool3 c = bool3(p.y >= vi.y, p.y < vj.y, e.x * w.y > e.y * w.x);
                    if (all(c) || all(!c)) s *= -1.0;
                }
                return s * sqrt(d);
            }

            float opSmoothUnion(float d1, float d2, float k)
            {
                float h = clamp(0.5 + 0.5 * (d2 - d1) / max(k, 1e-4), 0.0, 1.0);
                return lerp(d2, d1, h) - k * h * (1.0 - h);
            }

            // Havuzun iç (cam) ve dış (çerçeve) hatları: kutu ya da özel şekil (+ isteğe bağlı kavanoz boynu)
            float ShapeInnerBase(float2 p, float2 innerHalf, float innerRad, float2 bodyOffset)
            {
                if (_PolyCount > 2.5) return sdPolygon(p) - _PolyRadius;
                return sdRoundBox2D(p - bodyOffset, innerHalf, innerRad);
            }

            float sdEllipseApprox(float2 p, float2 ab)
            {
                ab = max(ab, float2(0.01, 0.01));
                float2 q = p / ab;
                float lq = length(q);
                float g = max(length(q / ab), 1e-4);
                return (lq - 1.0) / g;
            }

            float ShapeInner(float2 p, float2 innerHalf, float innerRad, float2 bodyOffset)
            {
                float dBase = ShapeInnerBase(p, innerHalf, innerRad, bodyOffset);
                if (_JarNeckParams.y <= 0.005) return dBase;

                float neckHalfW = _JarNeckParams.x;
                float neckH = _JarNeckParams.y;
                float lipOver = _JarNeckParams.z;
                float bodyTopY = bodyOffset.y + innerHalf.y;

                // Seamless S-curve shoulder into cylindrical jar neck (no flat seam dividing shoulder & neck)
                float neckCenterY = bodyTopY + neckH * 0.30;
                float dNeck = sdRoundBox2D(p - float2(0.0, neckCenterY), float2(neckHalfW, neckH * 0.56), min(neckHalfW * 0.28, 0.05));
                float dCombined = opSmoothUnion(dBase, dNeck, 0.135);

                // Smooth oval jar top lip profile
                float lipCenterY = bodyTopY + neckH - 0.015;
                float dLip = sdEllipseApprox(p - float2(0.0, lipCenterY), float2(neckHalfW + lipOver, 0.036));
                return opSmoothUnion(dCombined, dLip, 0.035);
            }

            float ShapeOuter(float2 p, float2 innerHalf, float innerRad, float frameThick, float2 bodyOffset)
            {
                return ShapeInner(p, innerHalf, innerRad, bodyOffset) - frameThick;
            }

            half4 EvaluateCollar3D(Varyings IN)
            {
                float aspect = max(_BoxAspect, 0.5);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;

                float2 halfSize = float2(aspect * 0.78, 0.56);
                float2 capCenter = float2(0.0, 0.10);
                float dCap = sdRoundBox2D(p - capCenter, halfSize, 0.24);
                float dShad = sdRoundBox2D(p - float2(0.0, -0.06), halfSize * float2(1.03, 1.02), 0.26);

                float edgeFade = smoothstep(0.0, 0.08, IN.uv.x) * smoothstep(1.0, 0.92, IN.uv.x)
                               * smoothstep(0.0, 0.08, IN.uv.y) * smoothstep(1.0, 0.92, IN.uv.y);

                float capMask = 1.0 - smoothstep(-0.010, 0.010, dCap);
                float shadAlpha = (1.0 - smoothstep(-0.04, 0.22, dShad)) * 0.30 * edgeFade;

                float nx = clamp(p.x / max(halfSize.x, 0.05), -1.0, 1.0);
                float ny = clamp((p.y - capCenter.y) / max(halfSize.y, 0.05), -1.0, 1.0);

                // Cylindrical 3D periwinkle shading matching Image 2 upper exit collar
                float3 baseCol = float3(0.49, 0.43, 0.90);
                float3 hiCol   = float3(0.84, 0.82, 1.00);
                float3 shadeCol = float3(0.28, 0.22, 0.64);

                float leftSpec = exp(-pow((nx + 0.34) / 0.26, 2.0));
                float rightRim = exp(-pow((nx - 0.78) / 0.20, 2.0)) * 0.45;
                float cylLit = saturate(0.55 + leftSpec * 0.70 + rightRim - smoothstep(0.18, 0.95, nx) * 0.45);
                float vertLit = saturate(0.52 + ny * 0.32);

                float3 collarCol = lerp(shadeCol, baseCol, saturate(cylLit * vertLit * 1.15));
                collarCol = lerp(collarCol, hiCol, saturate(leftSpec * 0.62 * smoothstep(-0.6, 0.5, ny)));

                // Top glass-seat rim lip highlight
                float topLip = smoothstep(0.54, 0.88, ny);
                collarCol = lerp(collarCol, hiCol, topLip * 0.68);

                // Subtle bottom collar groove & darker curved underside
                float botGroove = exp(-pow((ny + 0.56) / 0.14, 2.0));
                float botShade  = smoothstep(-0.48, -0.95, ny);
                collarCol = lerp(collarCol, shadeCol * 0.72, saturate(botGroove * 0.55 + botShade * 0.65));

                // Soft outer anti-aliased edge highlight
                float rimAA = exp(-pow((-dCap - 0.014) / 0.014, 2.0));
                collarCol = lerp(collarCol, hiCol, rimAA * 0.35 * smoothstep(-0.3, 0.6, ny));

                float3 shadRgb = float3(0.50, 0.44, 0.78);
                float outAlpha = saturate(capMask + shadAlpha * (1.0 - capMask));
                if (outAlpha < 0.002) discard;

                float3 outRgb = lerp(shadRgb, collarCol, capMask);
                return half4(saturate(outRgb), outAlpha);
            }

            half4 EvaluateRoundedBoxPool(Varyings IN)
            {
                bool isPoly = _PolyCount > 2.5;
                bool hasNeck = _JarNeckParams.y > 0.005;
                float aspect = max(_BoxAspect, 0.25);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;

                // Smooth edge fade prevents any shadow or glow from ever clipping at quad bounds
                float quadEdgeFade = smoothstep(0.0, 0.05, IN.uv.x) * smoothstep(1.0, 0.95, IN.uv.x)
                                   * smoothstep(0.0, 0.05, IN.uv.y) * smoothstep(1.0, 0.95, IN.uv.y);

                float neckShift = hasNeck ? _JarNeckParams.y * 0.5 : 0.0;
                float2 bodyOffset = float2(0.0, -neckShift);

                // Reserve generous 0.20 / 0.24 quad margins so drop shadow fades completely inside the quad!
                float2 outerHalf = float2(aspect * 0.80, 0.76 - neckShift);
                float maxSafeRad = min(outerHalf.x, outerHalf.y) * 0.95;
                float outerRad   = clamp(_BoxCornerRadius, 0.04, maxSafeRad);

                float frameThick = clamp(_FrameThickness, 0.035, min(outerHalf.x, outerHalf.y) * 0.38);
                float2 innerHalf = outerHalf - float2(frameThick, frameThick);
                float innerRad   = clamp(outerRad - frameThick * 0.65, 0.03, min(innerHalf.x, innerHalf.y) * 0.92);

                if (isPoly)
                {
                    innerHalf = float2(max(abs(_PolyBounds.x), abs(_PolyBounds.z)), (_PolyBounds.w - _PolyBounds.y) * 0.5);
                    outerHalf = innerHalf + frameThick;
                    bodyOffset = float2(0.0, 0.0);
                }

                float dOuter = ShapeOuter(p, innerHalf, innerRad, frameThick, bodyOffset);
                float dInner = ShapeInner(p, innerHalf, innerRad, bodyOffset);

                // 1. Soft Unclipped Floor Shadow (Back layer only)
                float shadowAlpha = 0.0;
                float3 shadowRGB = _FakeShadowColor.rgb;
                if (_IsFrontLens < 0.5)
                {
                    float2 pShadFar  = p - float2(0.0, -0.058);
                    float2 pShadNear = p - float2(0.0, -0.026);
                    float dShadFar   = ShapeOuter(pShadFar, innerHalf, innerRad, frameThick + 0.015, bodyOffset);
                    float dShadNear  = ShapeOuter(pShadNear, innerHalf, innerRad, frameThick, bodyOffset);
                    float aFar  = (1.0 - smoothstep(-0.04, 0.145, dShadFar)) * _FakeShadowColor.a;
                    float aNear = (1.0 - smoothstep(-0.02, 0.065, dShadNear)) * (_FakeShadowColor.a * 0.60);
                    shadowAlpha = saturate(aFar + aNear) * quadEdgeFade;
                }

                float outerMask = 1.0 - smoothstep(-0.008, 0.008, dOuter);
                float dFromOuter = max(0.0, -dOuter);

                // Sleek unified periwinkle-crystal blown glass palette (matching Image 2 directly!)
                float3 rimPeriwinkle = lerp(_OuterRimColor.rgb, float3(0.46, 0.45, 0.92), 0.55);
                float3 wallIceBlue   = lerp(_EdgeColor.rgb, float3(0.70, 0.74, 0.99), 0.55);
                float3 pureWhite     = float3(1.00, 1.00, 1.00);

                // Thin crisp outer glass silhouette + glossy white rim hairline (NO fat striped wireframe!)
                float outerEdgeRim   = exp(-pow((dFromOuter - 0.006) / 0.007, 2.0));
                float crispWhiteRim  = exp(-pow((dFromOuter - 0.020) / 0.012, 2.0));
                float wallThicknessFade = exp(-dFromOuter / 0.088);

                float2 gradOut = normalize(float2(
                    ShapeOuter(p + float2(0.005, 0.0), innerHalf, innerRad, frameThick, bodyOffset) - ShapeOuter(p - float2(0.005, 0.0), innerHalf, innerRad, frameThick, bodyOffset),
                    ShapeOuter(p + float2(0.0, 0.005), innerHalf, innerRad, frameThick, bodyOffset) - ShapeOuter(p - float2(0.0, 0.005), innerHalf, innerRad, frameThick, bodyOffset)
                ) + 1e-5);

                // Asymmetric studio lighting: bright white specular along left/right contours & top shoulders
                float sideFacing = saturate(abs(gradOut.x) * 0.85 + max(0.0, gradOut.y) * 0.35);
                float3 rimGlassCol = lerp(wallIceBlue, rimPeriwinkle, outerEdgeRim * 0.85);
                rimGlassCol = lerp(rimGlassCol, pureWhite, saturate(crispWhiteRim * (0.70 + 0.30 * sideFacing)));

                // 2. Signature 3D Cylindrical Glossy Pillars & Thick Bottom Base (dFromOuter > 0)
                float2 pLocal = p - bodyOffset;
                float2 normIn = pLocal / max(innerHalf, float2(0.01, 0.01));
                float dSideWall = (isPoly || hasNeck) ? max(0.0, -dOuter) : (outerHalf.x - abs(pLocal.x));

                float vertSpan = smoothstep(-innerHalf.y + 0.02, -innerHalf.y + 0.16, pLocal.y)
                               * (1.0 - smoothstep(innerHalf.y - 0.08, innerHalf.y + 0.04, pLocal.y));

                // Left vertical bright crystal glass reflection strip
                float leftSideMask = smoothstep(0.02, -0.06, pLocal.x);
                float leftHighlight = exp(-pow((dSideWall - 0.042) / 0.020, 2.0)) * vertSpan * leftSideMask * 0.90;
                float leftSoftSheen = exp(-pow((dSideWall - 0.085) / 0.046, 2.0)) * vertSpan * leftSideMask * 0.35;

                // Right vertical broad glossy white bottle pill (brightest at upper-right shoulder/wall like Image 2!)
                float rightSideMask = smoothstep(-0.02, 0.06, pLocal.x);
                float upperRightFocus = smoothstep(-innerHalf.y * 0.35, innerHalf.y * 0.45, pLocal.y);
                float rightPill = exp(-pow((dSideWall - 0.050) / 0.022, 2.0)) * vertSpan * rightSideMask * (0.68 + 0.52 * upperRightFocus);

                // Curved Bottom Solid Glass Floor Base (thick crystal arc at bottom like Image 2!)
                float dBotFromOuter = pLocal.y - (-outerHalf.y);
                float hMaskFloor = isPoly
                    ? smoothstep(0.02, 0.16, -ShapeInnerBase(p + float2(0.0, 0.14), innerHalf, innerRad, bodyOffset))
                    : 1.0 - smoothstep(outerHalf.x - 0.14, outerHalf.x - 0.01, abs(pLocal.x));
                float botBaseGlass = exp(-pow((dBotFromOuter - 0.016) / 0.016, 2.0)) * hMaskFloor;
                float botWhiteArc  = exp(-pow((dBotFromOuter - 0.044) / 0.016, 2.0)) * hMaskFloor;

                // 3. True 3D Open Elliptical Glass Jar Mouth & Neck Ring (when hasNeck is active)
                float neckFrontLip = 0.0;
                float neckBackLip  = 0.0;
                float neckCollarRing = 0.0;
                float neckCavity   = 0.0;
                if (hasNeck)
                {
                    float bodyTopOuterY = bodyOffset.y + outerHalf.y;
                    float lipCenterY = bodyTopOuterY + _JarNeckParams.y - 0.036;
                    float rx = _JarNeckParams.x + _JarNeckParams.z + frameThick * 0.65;
                    float ry = 0.042;
                    float dLipOuter = sdEllipseApprox(p - float2(0.0, lipCenterY), float2(rx, ry));
                    float dLipInner = sdEllipseApprox(p - float2(0.0, lipCenterY), float2(rx * 0.80, ry * 0.56));

                    float inOuterEllipse = 1.0 - smoothstep(-0.008, 0.008, dLipOuter);
                    float inCavity = 1.0 - smoothstep(-0.008, 0.008, dLipInner);
                    float onLipRing = saturate(inOuterEllipse - inCavity);

                    // Front lower ellipse arc sits in front of the opening; back upper arc closes the 3D ellipse behind!
                    float frontHalfMask = smoothstep(0.012, -0.010, p.y - lipCenterY);
                    neckFrontLip = onLipRing * frontHalfMask;
                    neckBackLip  = onLipRing * (1.0 - frontHalfMask);
                    neckCavity   = inCavity;

                    // Secondary horizontal curved glass neck collar groove right below the oval lip
                    float inNeckX = 1.0 - smoothstep(rx * 0.88, rx * 1.06, abs(p.x));
                    float xN = clamp(p.x / max(rx, 0.05), -1.0, 1.0);
                    float grooveCurveY = (lipCenterY - 0.056) - 0.014 * (1.0 - xN * xN);
                    float grooveLine = exp(-pow((p.y - grooveCurveY) / 0.010, 2.0)) * inNeckX;
                    float ridgeGlow  = exp(-pow((p.y - (grooveCurveY + 0.016)) / 0.012, 2.0)) * inNeckX;
                    neckCollarRing = saturate(grooveLine * 0.65 + ridgeGlow * 0.80);

                    // Erase harsh flat glass wall inside top oval cavity so the jar actually looks open at top!
                    outerMask *= saturate(1.0 - neckCavity * smoothstep(lipCenterY - 0.012, lipCenterY + 0.008, p.y));
                }

                float specularTotal = saturate((leftHighlight + leftSoftSheen + rightPill + botWhiteArc * 0.90) * _SheenIntensity);

                if (_IsFrontLens > 0.5)
                {
                    // FRONT LAYER (in front of 3D balls & tray):
                    // Keeps central interior 100% transparent so balls/blocks pop with crystal clarity,
                    // while rendering sleek outer wall rim, bright cylindrical white highlights, and front open jar lip!
                    float wallRimAlpha = saturate(outerEdgeRim * 0.72 + crispWhiteRim * 0.82 + wallThicknessFade * 0.24) * outerMask;
                    float specAlpha    = specularTotal * 0.84 * outerMask;
                    float baseAlpha    = botBaseGlass * 0.45 * outerMask;

                    float3 frontRgb = lerp(rimGlassCol, pureWhite, saturate(specularTotal * 0.95));
                    frontRgb = lerp(frontRgb, rimPeriwinkle, saturate(botBaseGlass * 0.45 * (1.0 - botWhiteArc)));

                    float frontAlpha = saturate(max(wallRimAlpha, max(specAlpha, baseAlpha)));

                    if (hasNeck)
                    {
                        float3 neckCol = lerp(rimPeriwinkle, pureWhite, 0.78);
                        frontRgb = lerp(frontRgb, neckCol, saturate(neckFrontLip + neckCollarRing * 0.75));
                        frontAlpha = max(frontAlpha, saturate(neckFrontLip * 0.92 + neckCollarRing * 0.62));
                    }

                    frontAlpha *= quadEdgeFade;
                    if (frontAlpha < 0.002) discard;
                    return half4(saturate(frontRgb), frontAlpha);
                }
                else
                {
                    // BACK GLASS CHAMBER (behind 3D balls & tray):
                    // Airy semi-translucent lavender crystal chamber + back jar lip + unclipped soft floor shadow
                    float vGrad = saturate(normIn.y * 0.5 + 0.5);
                    float3 topCol = lerp(float3(0.92, 0.93, 1.00), wallIceBlue, 0.34);
                    float3 midCol = float3(0.96, 0.96, 1.00);
                    float3 botCol = lerp(float3(0.92, 0.92, 1.00), rimPeriwinkle, 0.24);

                    float3 chamberRgb = lerp(midCol, topCol, smoothstep(0.45, 0.98, vGrad));
                    chamberRgb = lerp(botCol, chamberRgb, smoothstep(0.0, 0.32, vGrad));
                    chamberRgb = lerp(chamberRgb, wallIceBlue, wallThicknessFade * 0.50);
                    chamberRgb = lerp(chamberRgb, rimGlassCol, saturate(outerEdgeRim * 0.75 + crispWhiteRim * 0.85));
                    chamberRgb = lerp(chamberRgb, pureWhite, specularTotal * 0.72);
                    chamberRgb = lerp(chamberRgb, rimPeriwinkle, saturate(botBaseGlass * 0.45));

                    float centerA = clamp(_CenterGlassAlpha, 0.08, 0.75);
                    float chamberAlpha = saturate(max(centerA + wallThicknessFade * 0.40, max(outerEdgeRim * 0.72, specularTotal * 0.58))) * outerMask;

                    if (hasNeck)
                    {
                        float3 backRimCol = lerp(rimPeriwinkle, pureWhite, 0.58);
                        float3 throatCol  = lerp(rimPeriwinkle, float3(0.66, 0.64, 0.96), 0.50);
                        chamberRgb = lerp(chamberRgb, throatCol, neckCavity * 0.55);
                        chamberRgb = lerp(chamberRgb, backRimCol, neckBackLip * 0.88);
                        chamberAlpha = max(chamberAlpha, saturate(neckBackLip * 0.82 + neckCavity * 0.28));
                    }

                    chamberAlpha *= quadEdgeFade;
                    float finalAlpha = saturate(chamberAlpha + shadowAlpha * (1.0 - chamberAlpha * 0.92));
                    if (finalAlpha < 0.002) discard;

                    float3 finalRgb = lerp(shadowRGB * shadowAlpha, chamberRgb, chamberAlpha) / max(finalAlpha, 0.004);
                    if (chamberAlpha < 0.02) finalRgb = shadowRGB;
                    return half4(saturate(finalRgb), finalAlpha);
                }
            }

            // Evaluates the 1-to-1 optical blown-glass wall profile and interior reflections
            // matching the reference image (outer cobalt rim -> bright icy white core ->
            // sky-blue refractive wall -> inner cobalt meniscus groove -> inner white rim ->
            // broad glossy lobe crescent highlight & cylindrical neck/spout streaks).
            void EvaluateBlownGlassWall(
                float2 uv,
                float dPerp,
                float wallThick,
                float xNorm,
                out float3 wallRgb,
                out float wallAlpha,
                out float wallZoneMask)
            {
                // Normalized distance across the physical glass wall:
                // tWall = 0.0 at outer silhouette edge, tWall = 1.0 at inner meniscus edge
                float tWall = dPerp / max(wallThick, 0.02);

                // Anti-aliased outer silhouette edge feather
                float outerAA = smoothstep(0.0, 0.035, tWall);

                // Band 1: Crisp Outer Cobalt-Sapphire Silhouette Edge (tWall ~ 0.02..0.16)
                float outerCobaltBand = exp(-pow((tWall - 0.07) / 0.075, 2.0));

                // Band 2: Bright Icy-White Outer Rim Highlight Core (tWall ~ 0.14..0.44)
                float outerWhiteCore = exp(-pow((tWall - 0.27) / 0.125, 2.0));

                // Band 3: Translucent Sky-Blue Refractive Wall Mid-Body (tWall ~ 0.36..0.74)
                float midSkyBody = smoothstep(0.18, 0.42, tWall) * (1.0 - smoothstep(0.68, 0.92, tWall));

                // Band 4: Rich Cobalt-Azure Inner Meniscus Groove (tWall ~ 0.68..0.89)
                float innerCobaltGroove = exp(-pow((tWall - 0.78) / 0.095, 2.0));

                // Band 5: Crisp Inner White-Cyan Meniscus Highlight (tWall ~ 0.86..1.06)
                float innerWhiteRim = exp(-pow((tWall - 0.96) / 0.085, 2.0));

                // Combine colors across the physical wall (0 <= tWall <= 1.08)
                float3 col = _EdgeColor.rgb;
                col = lerp(col, _OuterRimColor.rgb, saturate(outerCobaltBand * 0.95));
                col = lerp(col, _RimGlowColor.rgb, saturate(outerWhiteCore * 0.96));
                col = lerp(col, _EdgeColor.rgb, saturate(midSkyBody * 0.55));
                col = lerp(col, _InnerGrooveColor.rgb, saturate(innerCobaltGroove * 0.92));
                col = lerp(col, _RimGlowColor.rgb, saturate(innerWhiteRim * 0.95));

                // Wall opacity profile across tWall in [0, 1.08]
                float baseWallMask = 1.0 - smoothstep(0.96, 1.10, tWall);
                float bandAlpha = saturate(
                    baseWallMask * _EdgeColor.a +
                    outerCobaltBand * 0.35 +
                    outerWhiteCore * 0.38 +
                    innerCobaltGroove * 0.28 +
                    innerWhiteRim * 0.36);

                wallRgb = saturate(col);
                wallAlpha = saturate(bandAlpha * outerAA);
                wallZoneMask = baseWallMask * outerAA;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_IsCollar > 0.5)
                {
                    return EvaluateCollar3D(IN);
                }
                if (_IsRoundedBoxPool > 0.5)
                {
                    return EvaluateRoundedBoxPool(IN);
                }

                // xNorm in [-1, +1] across the horizontal width of the glass at this height
                float xNorm = IN.uv.x * 2.0 - 1.0;
                float absX = abs(xNorm);

                // True perpendicular world-space distance (in meters) from the left and right outer glass contour!
                // uv2.x = halfSpan * invHypL, uv2.y = halfSpan * invHypR
                float scaleL = max(IN.uv2.x, 0.08);
                float scaleR = max(IN.uv2.y, 0.08);
                float dLeft  = IN.uv.x * 2.0 * scaleL;
                float dRight = (1.0 - IN.uv.x) * 2.0 * scaleR;
                float dPerp  = min(dLeft, dRight);

                float wallThick = max(_WallThickness, 0.03);
                float tWall = dPerp / wallThick;

                // Evaluate the continuous multi-band blown-glass wall
                float3 wallRgb;
                float wallAlpha;
                float wallZoneMask;
                EvaluateBlownGlassWall(IN.uv, dPerp, wallThick, xNorm, wallRgb, wallAlpha, wallZoneMask);

                // -------------------------------------------------------------------------
                // 1. Broad Glossy White Crescent Highlights Hugging Left & Right Lobes/Shoulders
                //    (Directly inside the inner wall meniscus: tWall ~ 1.08 .. 2.35)
                // -------------------------------------------------------------------------
                float innerMargin = smoothstep(0.94, 1.16, tWall);
                float lobeRadial  = exp(-pow((tWall - 1.46) / 0.56, 2.0)) * innerMargin;

                // Vertical mask covering the rounded left/right lobes and upper sloped shoulders (uv.y ~ 0.21..0.57)
                float lobeYMask = smoothstep(0.19, 0.29, IN.uv.y) * (1.0 - smoothstep(0.46, 0.58, IN.uv.y));
                float lobeCrescent = lobeYMask * lobeRadial * 0.78 * _SheenIntensity;

                // Subtle glossy white cushion reflection along the lower sloped funnel floor (uv.y ~ 0.13..0.24)
                float floorYMask = smoothstep(0.12, 0.17, IN.uv.y) * (1.0 - smoothstep(0.21, 0.27, IN.uv.y));
                float floorRadial = exp(-pow((tWall - 1.32) / 0.40, 2.0)) * innerMargin;
                float floorCrescent = floorYMask * floorRadial * 0.46 * _SheenIntensity;

                // -------------------------------------------------------------------------
                // 2. Sleek Vertical Cylindrical Specular Streaks on the Upper Neck & Bottom Spout
                // -------------------------------------------------------------------------
                float interiorMask = smoothstep(0.88, 1.18, tWall);

                // Upper neck & upper shoulder transition (uv.y > 0.46)
                float neckYMask = smoothstep(0.44, 0.58, IN.uv.y);
                float neckLeftStreak  = exp(-pow((xNorm + 0.48) * 6.0, 2.0)) * 0.54;
                float neckRightStreak = exp(-pow((xNorm - 0.52) * 6.8, 2.0)) * 0.42;
                float neckCenterGlow  = exp(-pow((xNorm + 0.12) * 3.2, 2.0)) * 0.22;
                float neckStreaks = (neckLeftStreak + neckRightStreak + neckCenterGlow) * neckYMask * interiorMask * _SheenIntensity;

                // Bottom vertical spout cylindrical reflection streaks (uv.y < 0.20)
                float spoutYMask = (1.0 - smoothstep(0.14, 0.21, IN.uv.y)) * smoothstep(0.015, 0.035, IN.uv.y);
                float spoutLeftStreak  = exp(-pow((xNorm + 0.38) * 6.5, 2.0)) * 0.50;
                float spoutRightStreak = exp(-pow((xNorm - 0.42) * 7.2, 2.0)) * 0.38;
                float spoutStreaks = (spoutLeftStreak + spoutRightStreak) * spoutYMask * interiorMask * _SheenIntensity;

                // -------------------------------------------------------------------------
                // 3. 3D Oval / Elliptical Glass Spout Mouth Ring at the Bottom Tip (uv.y < 0.038)
                // -------------------------------------------------------------------------
                float ellipseProfile = sqrt(saturate(1.0 - xNorm * xNorm));
                // Lower front lip arc and upper back lip arc of the 3D circular spout opening
                float yFrontLip = 0.013 - 0.007 * ellipseProfile;
                float yBackLip  = 0.016 + 0.009 * ellipseProfile;

                float frontLipWhite  = exp(-pow((IN.uv.y - yFrontLip) / 0.0048, 2.0));
                float frontLipCobalt = exp(-pow((IN.uv.y - (yFrontLip - 0.0035)) / 0.0042, 2.0));
                float backLipWhite   = exp(-pow((IN.uv.y - yBackLip) / 0.0042, 2.0)) * 0.65;
                float nozzleRingMask = saturate(frontLipWhite + frontLipCobalt * 0.85 + backLipWhite) * smoothstep(0.0, 0.004, IN.uv.y);
                float3 nozzleRingRgb = lerp(_OuterRimColor.rgb, _RimGlowColor.rgb, saturate(frontLipWhite * 1.15 + backLipWhite));

                // -------------------------------------------------------------------------
                // FRONT GLASS LENS MODE (_IsFrontLens > 0.5):
                // Sits in front of the 3D cubes at z = -0.24f.
                // Renders the crisp optical blown-glass wall border, the 3D bottom spout mouth ring,
                // the broad white lobe crescents, and the cylindrical neck/spout specular streaks,
                // while keeping the interior center transparent so the candy cubes remain 100% vibrant!
                // -------------------------------------------------------------------------
                if (_IsFrontLens > 0.5)
                {
                    // Subtle diagonal glass window sheen across upper-left interior
                    float diagCoord = -xNorm * 0.52 + (IN.uv.y - 0.36) * 0.88;
                    float windowBand = exp(-pow((diagCoord - 0.20) * 6.0, 2.0)) * interiorMask * (1.0 - smoothstep(0.65, 0.92, absX)) * 0.14 * _SheenIntensity;

                    float specHighlights = saturate(lobeCrescent + floorCrescent + neckStreaks * 0.85 + spoutStreaks + windowBand);
                    float3 specRgb = lerp(_EdgeColor.rgb, _RimGlowColor.rgb, 0.92);

                    // Blend interior specular reflections with the physical outer/inner glass wall
                    float3 frontRgb = lerp(specRgb, wallRgb, wallZoneMask);
                    float frontAlpha = saturate(max(wallAlpha, specHighlights * 0.82));

                    // Composite the 3D bottom elliptical spout nozzle ring on top
                    frontRgb = lerp(frontRgb, nozzleRingRgb, saturate(nozzleRingMask * 0.92));
                    frontAlpha = max(frontAlpha, nozzleRingMask * 0.90);

                    return half4(saturate(frontRgb), saturate(frontAlpha));
                }

                // -------------------------------------------------------------------------
                // BACK CHAMBER MODE (_IsFrontLens <= 0.5):
                // Sits behind the 3D cubes at z = +0.08f.
                // Renders the airy crystal sky-blue interior with smooth Fresnel radial shading,
                // subtle upper-dome sheen, and the back layer of the blown-glass wall.
                // -------------------------------------------------------------------------
                float fresnel = pow(smoothstep(0.05, 0.96, absX), 1.85);
                // Vertical gradient: slightly deeper sky-blue in the neck & upper shoulders, super airy in the main belly
                float neckTintBoost = smoothstep(0.45, 0.78, IN.uv.y) * 0.16;
                float4 interiorCol = lerp(_CenterColor, _EdgeColor, saturate(fresnel * 0.78 + neckTintBoost));

                // Soft broad white sheen in the upper chamber & neck
                float diagSheen = saturate(-xNorm * 0.45 + (IN.uv.y - 0.30) * 0.85);
                float domeGlow = smoothstep(0.20, 0.80, diagSheen) * interiorMask * 0.26 * _SheenIntensity;
                float backSpec = saturate(domeGlow + lobeCrescent * 0.55 + neckStreaks * 0.65 + spoutStreaks * 0.50);

                interiorCol.rgb = lerp(interiorCol.rgb, _RimGlowColor.rgb, backSpec);
                interiorCol.a   = saturate(interiorCol.a + backSpec * 0.22);

                // Seamlessly transition into the blown-glass wall at the edges
                float3 finalRgb = lerp(interiorCol.rgb, wallRgb, wallZoneMask);
                float finalAlpha = lerp(interiorCol.a, wallAlpha, wallZoneMask);

                return half4(saturate(finalRgb), saturate(finalAlpha));
            }
            ENDHLSL
        }
    }
}
