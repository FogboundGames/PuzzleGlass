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
            CBUFFER_END

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
