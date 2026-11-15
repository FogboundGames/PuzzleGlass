Shader "PuzzleGlass/CarrierBox"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BodyColor ("Box Front Frame Color", Color) = (0.06, 0.56, 0.94, 1.0)
        _BodyDarkColor ("Box 3D Base Color", Color) = (0.04, 0.43, 0.78, 1.0)
        _OutlineColor ("Outer Outline Color", Color) = (0.03, 0.31, 0.58, 1.0)
        _HighlightColor ("Inner Rim Highlight", Color) = (0.25, 0.72, 1.00, 1.0)
        _PlateColor ("Inner White Plate Color", Color) = (0.96, 0.96, 0.96, 1.0)
        _PlateShadowColor ("Inner Plate Top Shadow", Color) = (0.67, 0.68, 0.70, 1.0)
        _SocketColor ("Gray Socket Base Color", Color) = (0.56, 0.56, 0.57, 1.0)
        _SocketShadowColor ("Gray Socket Crescent Shadow", Color) = (0.42, 0.42, 0.43, 1.0)
        _LocalSize ("Quad Local Size", Vector) = (1.16, 1.16, 0, 0)
        _RectCount ("Shape Rect Count", Float) = 1.0
        _Rect0 ("Shape Rect 0 (cx,cy,hx,hy)", Vector) = (0.0, 0.024, 0.445, 0.425)
        _Rect1 ("Shape Rect 1 (cx,cy,hx,hy)", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WallInset ("Inner Plate Wall Inset", Float) = 0.088
        _CornerRad ("Outer Corner Radius", Float) = 0.195
        _InnerRad ("Inner Plate Corner Radius", Float) = 0.125
        _OutlineThickness ("Outer Outline Thickness", Float) = 0.032
        _LipOffset ("3D Lower Lip Offset", Float) = 0.088
        _ExitHighlight ("Exit-Ready Rim Highlight", Float) = 0.0
        _DragHighlight ("Active Drag / Focus Highlight", Float) = 0.0
        _IceAmount ("Ice Freeze Overlay", Float) = 0.0
        _LockAmount ("Key-Locked Lid Overlay", Float) = 0.0
        _LockOpen ("Lock Shackle Open (0..1)", Float) = 0.0
        _LockWiggle ("Lock Glyph Wiggle (radians)", Float) = 0.0
        _LockCenter ("Lock Glyph Center (xy) + Scale (z)", Vector) = (0, 0, 1, 0)
        _SocketCount ("Socket Count", Float) = 3.0
        _SocketRadius ("Socket Radius", Float) = 0.108
        _SockPair0 ("Socket Pair 0-1", Vector) = (-0.158, -0.081, 0.158, -0.081)
        _SockPair1 ("Socket Pair 2-3", Vector) = (0.00, 0.134, 0.0, 0.0)
        _SockPair2 ("Socket Pair 4-5", Vector) = (0, 0, 0, 0)
        _SockPair3 ("Socket Pair 6-7", Vector) = (0, 0, 0, 0)
        _SockPair4 ("Socket Pair 8-9", Vector) = (0, 0, 0, 0)
        _SockPair5 ("Socket Pair 10-11", Vector) = (0, 0, 0, 0)
        _SockPair6 ("Socket Pair 12-13", Vector) = (0, 0, 0, 0)
        _SockPair7 ("Socket Pair 14-15", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-5"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CarrierBoxForward"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BodyColor;
                float4 _BodyDarkColor;
                float4 _OutlineColor;
                float4 _HighlightColor;
                float4 _PlateColor;
                float4 _PlateShadowColor;
                float4 _SocketColor;
                float4 _SocketShadowColor;
                float4 _LocalSize;
                float _RectCount;
                float4 _Rect0;
                float4 _Rect1;
                float _WallInset;
                float _CornerRad;
                float _InnerRad;
                float _OutlineThickness;
                float _LipOffset;
                float _ExitHighlight;
                float _DragHighlight;
                float _IceAmount;
                float _LockAmount;
                float _LockOpen;
                float _LockWiggle;
                float4 _LockCenter;
                float _SocketCount;
                float _SocketRadius;
                float4 _SockPair0;
                float4 _SockPair1;
                float4 _SockPair2;
                float4 _SockPair3;
                float4 _SockPair4;
                float4 _SockPair5;
                float4 _SockPair6;
                float4 _SockPair7;
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

            float sdPolyomino(float2 q, float inset, float radius)
            {
                float rClamped = max(0.008, radius);
                float2 b0 = max(float2(0.02, 0.02), _Rect0.zw - inset);
                float d = sdRoundBox(q - _Rect0.xy, b0, min(rClamped, min(b0.x, b0.y)));
                if (_RectCount > 1.5)
                {
                    float2 b1 = max(float2(0.02, 0.02), _Rect1.zw - inset);
                    float d1 = sdRoundBox(q - _Rect1.xy, b1, min(rClamped, min(b1.x, b1.y)));
                    d = min(d, d1);
                }
                return d;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Isotropic block-local coordinates so circles and borders never stretch across non-square shapes
                float2 q = (IN.uv - 0.5) * max(_LocalSize.xy, float2(0.25, 0.25));
                float aa = 0.008;

                // 1. Main Box Geometry (Front Face + 3D Lower Base Extrusion)
                float dFrontBox = sdPolyomino(q, 0.0, _CornerRad);
                float dBaseBox  = sdPolyomino(q + float2(0.0, _LipOffset), 0.0, _CornerRad);
                float dTotal    = min(dFrontBox, dBaseBox);

                // Complete Silhouette
                float totalAlpha = 1.0 - smoothstep(-aa, aa, dTotal);
                if (totalAlpha < 0.002) discard;

                // Sharper outer edge silhouette (brightens cleanly when exit-ready or actively dragged)
                float activeGlow = saturate(max(_ExitHighlight, _DragHighlight));
                float3 activeOutline = lerp(_OutlineColor.rgb, float3(0.99, 0.995, 1.0), activeGlow * 0.96);
                float outlineWidth = _OutlineThickness + activeGlow * 0.018;
                float innerSilhouetteMask = 1.0 - smoothstep(-outlineWidth - aa, -outlineWidth + aa, dTotal);
                float skirtDepth = saturate((-dFrontBox) / max(0.02, _LipOffset));
                float3 skirtCol = lerp(_BodyDarkColor.rgb * 0.98, _BodyDarkColor.rgb * 0.76, saturate(1.0 - skirtDepth));
                float3 col = lerp(activeOutline, skirtCol, innerSilhouetteMask);

                // Front Box Frame (chunky bright colored rim above the 3D lower skirt + crisp specular top bevel)
                float frontOuterMask = 1.0 - smoothstep(-outlineWidth - aa, -outlineWidth + aa, dFrontBox);
                float frontRimMask = frontOuterMask - (1.0 - smoothstep(-outlineWidth - 0.024 - aa, -outlineWidth - 0.024 + aa, dFrontBox));
                float3 topFrameRGB = lerp(_BodyColor.rgb * 0.96, _BodyColor.rgb * 1.04, saturate(q.y * 0.35 + 0.55));
                col = lerp(col, topFrameRGB, frontOuterMask);
                col = lerp(col, _HighlightColor.rgb, saturate(frontRimMask) * (0.56 + activeGlow * 0.34));

                // 3. Deep 3D Recessed Tray Cavity with Sunken White Plate
                float  dPlate    = sdPolyomino(q, _WallInset, _InnerRad);
                float  plateMask = 1.0 - smoothstep(-aa, aa, dPlate);

                // Crisp inner wall rim catchlight just outside the cavity opening
                float plateOuterRim = (1.0 - smoothstep(0.0, 0.020, dPlate)) * (1.0 - plateMask);
                col = lerp(col, _HighlightColor.rgb, plateOuterRim * (0.44 + activeGlow * 0.24));

                // Visible colored 3D back (top) and side inner walls descending down to the sunken white floor
                float  dCavityFloor = sdPolyomino(q + float2(0.0, 0.044), _WallInset + 0.014, max(0.016, _InnerRad - 0.012));
                float  floorMask    = (1.0 - smoothstep(-aa, aa, dCavityFloor)) * plateMask;

                float  wallDropGrad = saturate(-dPlate / 0.052);
                float  sideSlope    = saturate((dCavityFloor - dPlate) * 10.0);
                float3 innerWallCol = lerp(_BodyDarkColor.rgb * 0.88, _BodyDarkColor.rgb * 0.62, wallDropGrad);
                innerWallCol = lerp(innerWallCol, _BodyColor.rgb * 0.80, sideSlope * 0.24);

                // Deep cast shadow across the upper & edge parts of the white floor so it feels deeply sunken inside the box
                float  dDeepShadow  = sdPolyomino(q - float2(0.0, -0.056), _WallInset + 0.014, max(0.016, _InnerRad - 0.012));
                float  castBandMask = smoothstep(-0.024, 0.018, dDeepShadow);
                float  edgeAO       = 1.0 - smoothstep(0.0, 0.075, -dCavityFloor);
                float  floorShadowMix = saturate(max(castBandMask * 0.85, edgeAO * 0.72));
                float3 sunkenWhiteRGB = lerp(_PlateColor.rgb, _PlateShadowColor.rgb, floorShadowMix);

                // Subtle contact crease right where the colored inner walls meet the sunken white plate
                float  wallFloorCrease = (1.0 - smoothstep(-0.012, 0.006, dCavityFloor)) * floorMask;
                sunkenWhiteRGB = lerp(sunkenWhiteRGB, _PlateShadowColor.rgb * 0.78, wallFloorCrease * 0.45);

                float3 cavityRGB = lerp(innerWallCol, sunkenWhiteRGB, floorMask);
                col = lerp(col, cavityRGB, plateMask);

                // Keep plateShadowMask alias for lock/ice overlays below
                float plateShadowMask = floorShadowMix;

                // 4. Dynamic Gray Circular Sockets (1 to 16 sockets packed in _SockPair0.._SockPair7)
                float2 socks[16];
                socks[0]  = _SockPair0.xy; socks[1]  = _SockPair0.zw;
                socks[2]  = _SockPair1.xy; socks[3]  = _SockPair1.zw;
                socks[4]  = _SockPair2.xy; socks[5]  = _SockPair2.zw;
                socks[6]  = _SockPair3.xy; socks[7]  = _SockPair3.zw;
                socks[8]  = _SockPair4.xy; socks[9]  = _SockPair4.zw;
                socks[10] = _SockPair5.xy; socks[11] = _SockPair5.zw;
                socks[12] = _SockPair6.xy; socks[13] = _SockPair6.zw;
                socks[14] = _SockPair7.xy; socks[15] = _SockPair7.zw;

                int   sockCount   = clamp((int)round(_SocketCount), 1, 16);
                float sockRad     = max(0.018, _SocketRadius);
                float dSock       = 1e5;
                float2 nearestSockPos = float2(0.0, 0.0);

                for (int i = 0; i < 16; i++)
                {
                    if (i < sockCount)
                    {
                        float2 sPos = socks[i];
                        float d = length(q - sPos) - sockRad;
                        if (d < dSock)
                        {
                            dSock = d;
                            nearestSockPos = sPos;
                        }
                    }
                }

                // True recessed 3D socket well shading (outer plate lip bevel + inner overhanging cast shadow + deep pit AO)
                float2 sockDelta = (q - nearestSockPos) / sockRad;
                float rNorm = length(sockDelta);

                // Outer chamfer ring right around the hole on the white plate: bottom lip catches light, top lip dips into hole
                float lipDist = saturate(dSock / max(0.008, sockRad * 0.24));
                float outerLipMask = (1.0 - lipDist) * step(0.0, dSock) * floorMask;
                float bottomCatch = saturate(-sockDelta.y * 0.85 + 0.15);
                float topDip      = saturate(sockDelta.y * 0.90 + 0.10);
                col = lerp(col, _PlateShadowColor.rgb * 0.92, outerLipMask * topDip * 0.55);
                col = lerp(col, float3(1.0, 1.0, 1.0), outerLipMask * bottomCatch * 0.58);

                // Inside the recessed cylindrical socket pit
                float sockMask = 1.0 - smoothstep(-aa, aa, dSock);
                float wallDepth = smoothstep(1.0, 0.68, rNorm); // 0 at outer hole edge -> 1 inside pit bottom

                // Soft crescent cast shadow under the top overhang (simulating recessed hole depth lit from above)
                float dInnerRim = length(sockDelta - float2(0.0, -0.34));
                float overhangShadow = smoothstep(0.68, 1.12, dInnerRim);
                float topWallMask = smoothstep(0.10, 0.85, sockDelta.y);

                // Smooth shaded well colors: darker deep ceiling shadow, soft radial ambient occlusion, gentle lower bowl bounce light
                float3 pitFloorDark  = _SocketShadowColor.rgb * 0.58;
                float3 pitWallShadow = _SocketShadowColor.rgb * 0.72;
                float3 pitWallBounce = lerp(_SocketColor.rgb, _PlateColor.rgb, 0.18);
                float3 pitWellMid    = lerp(_SocketShadowColor.rgb * 0.86, _SocketColor.rgb * 0.94, saturate(-sockDelta.y * 0.55 + 0.45));

                float3 sockRGB = lerp(pitWallShadow, pitWellMid, wallDepth);
                sockRGB = lerp(sockRGB, pitFloorDark, overhangShadow * 0.82);
                // Crisp subtle dark inner rim contact occlusion + gentle bottom inner wall catchlight
                float innerEdgeAO = smoothstep(0.76, 1.0, rNorm);
                sockRGB = lerp(sockRGB, pitFloorDark * 0.82, innerEdgeAO * topWallMask * 0.65);
                sockRGB = lerp(sockRGB, pitWallBounce, innerEdgeAO * saturate(-sockDelta.y) * 0.42);

                col = lerp(col, sockRGB, sockMask * plateMask);

                // 4b. Kilitli blok: beyaz plaka bloğun kendi renginde bir kapakla kapanır, ortasında düz beyaz asma kilit
                if (_LockAmount > 0.001)
                {
                    float la = saturate(_LockAmount);
                    float3 lidCol   = lerp(_BodyColor.rgb, float3(1.0, 1.0, 1.0), 0.30);
                    float3 lidShade = lerp(_BodyColor.rgb, float3(1.0, 1.0, 1.0), 0.08);
                    float3 lid = lerp(lidCol, lidShade, plateShadowMask);

                    // Kilit simgesi (dönme + ölçek)
                    float gs = max(0.2, _LockCenter.z);
                    float2 p = (q - _LockCenter.xy) / gs;
                    float ca = cos(_LockWiggle), sa = sin(_LockWiggle);
                    p = float2(ca * p.x + sa * p.y, -sa * p.x + ca * p.y);

                    float2 bp = p - float2(0.0, -0.050);
                    float dBody = sdRoundBox(bp, float2(0.135, 0.105), 0.050);

                    float2 sp = p - float2(0.0, 0.050 + saturate(_LockOpen) * 0.075);
                    float dRing = abs(length(sp) - 0.078) - 0.026;
                    float dLeg = max(abs(abs(sp.x) - 0.078) - 0.026, abs(sp.y + 0.05) - 0.05);
                    float dShackle = sp.y >= 0.0 ? dRing : dLeg;

                    float dGlyph = min(dBody, dShackle);
                    float aaG = aa / gs;
                    float glyphMask = 1.0 - smoothstep(-aaG, aaG, dGlyph);

                    // Yumuşak alt gölge (plakadaki gölge diliyle aynı)
                    float2 shp = p + float2(0.0, 0.020);
                    float2 sbp = shp - float2(0.0, -0.050);
                    float dShadow = min(sdRoundBox(sbp, float2(0.135, 0.105), 0.050),
                                        (shp - float2(0.0, 0.050 + saturate(_LockOpen) * 0.075)).y >= 0.0
                                            ? abs(length(shp - float2(0.0, 0.050 + saturate(_LockOpen) * 0.075)) - 0.078) - 0.026
                                            : 1.0);
                    float shadowMask = 1.0 - smoothstep(-aaG * 2.0, aaG * 2.0, dShadow);

                    // Anahtar deliği (bloğun koyu renginde)
                    float dHole = min(length(bp - float2(0.0, 0.016)) - 0.030,
                                      sdRoundBox(bp - float2(0.0, -0.028), float2(0.013, 0.036), 0.012));
                    float holeMask = 1.0 - smoothstep(-aaG, aaG, dHole);

                    float3 glyphCol = _PlateColor.rgb;
                    float3 lockLayer = lid;
                    lockLayer = lerp(lockLayer, _BodyDarkColor.rgb, shadowMask * 0.45);
                    lockLayer = lerp(lockLayer, glyphCol, glyphMask);
                    lockLayer = lerp(lockLayer, _BodyDarkColor.rgb, holeMask);

                    col = lerp(col, lockLayer, plateMask * la);
                }

                // 5. Optional Stylized Frozen Ice Cap Overlay (_IceAmount = 0..1)
                if (_IceAmount > 0.001)
                {
                    float iceAmt = saturate(_IceAmount);
                    float3 iceOutline = float3(0.14, 0.46, 0.78);
                    float3 iceBaseDark = float3(0.18, 0.56, 0.88);
                    float3 iceBody = float3(0.32, 0.78, 0.98);
                    float3 iceFrost = float3(0.84, 0.97, 1.00);
                    float3 icePlate = float3(0.45, 0.85, 0.99);
                    float3 icePlateShade = float3(0.30, 0.72, 0.94);

                    // Subtle diagonal ice crystal facets & snowy top frost highlight
                    float sheen = smoothstep(0.65, 0.95, sin((q.x * 2.4 + q.y * 2.4) * 3.14159));
                    float snowBand = smoothstep(0.08, 0.36, q.y + 0.06 * sin(q.x * 9.0));

                    float3 iceCol = lerp(iceOutline, iceBaseDark, innerSilhouetteMask);
                    iceCol = lerp(iceCol, iceBody, frontOuterMask);
                    iceCol = lerp(iceCol, iceFrost, saturate(frontRimMask) * (0.45 + 0.45 * snowBand));

                    float3 iceInner = lerp(icePlate, icePlateShade, plateShadowMask * 0.65);
                    iceInner = lerp(iceInner, iceFrost, sheen * 0.24 + snowBand * 0.14);
                    iceCol = lerp(iceCol, iceInner, plateMask);

                    col = lerp(col, iceCol, iceAmt);
                }

                return half4(col * IN.color.rgb, totalAlpha * IN.color.a);
            }
            ENDHLSL
        }
    }
}
