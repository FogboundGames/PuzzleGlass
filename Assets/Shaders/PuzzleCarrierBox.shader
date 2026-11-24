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
        _CrateMode ("Collector Crate Mode", Float) = 1.0
        _StickerOutlineColor ("Sticker White Border Color", Color) = (0.985, 0.99, 1.0, 1.0)
        _ExitHighlight ("Exit-Ready Rim Highlight", Float) = 1.0
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
        _WobblePhase ("GPU Idle Breath Phase", Float) = 0.0
        _WobbleAmount ("GPU Idle Breath Amount", Range(0, 1)) = 0.0
        _SquashAmount ("GPU Squash & Stretch", Range(-0.5, 0.5)) = 0.0
        _FillAmount ("Collection Fill Progress (0..1)", Range(0, 1)) = 0.0
        _FillWave ("Collection Splash Wave (0..1)", Range(0, 1)) = 0.0
        _FrontRimOnly ("Front Rim Occlusion Pass", Float) = 0.0
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
            #pragma multi_compile_instancing

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
                float _CrateMode;
                float4 _StickerOutlineColor;
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
                float _WobblePhase;
                float _WobbleAmount;
                float _SquashAmount;
                float _FillAmount;
                float _FillWave;
                float _FrontRimOnly;
            CBUFFER_END

            #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
                UNITY_INSTANCING_BUFFER_START(Props)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _BodyColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _BodyDarkColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _OutlineColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _HighlightColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _LocalSize)
                    UNITY_DEFINE_INSTANCED_PROP(float, _RectCount)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _Rect0)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _Rect1)
                    UNITY_DEFINE_INSTANCED_PROP(float, _ExitHighlight)
                    UNITY_DEFINE_INSTANCED_PROP(float, _DragHighlight)
                    UNITY_DEFINE_INSTANCED_PROP(float, _IceAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _LockAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _LockOpen)
                    UNITY_DEFINE_INSTANCED_PROP(float, _LockWiggle)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _LockCenter)
                    UNITY_DEFINE_INSTANCED_PROP(float, _SocketCount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _SocketRadius)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair0)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair1)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair2)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair3)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair4)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair5)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair6)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SockPair7)
                    UNITY_DEFINE_INSTANCED_PROP(float, _WobblePhase)
                    UNITY_DEFINE_INSTANCED_PROP(float, _WobbleAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _SquashAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _FillAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _FillWave)
                UNITY_INSTANCING_BUFFER_END(Props)
                #define GET_BOX_PROP(prop) UNITY_ACCESS_INSTANCED_PROP(Props, prop)
            #else
                #define GET_BOX_PROP(prop) prop
            #endif

            inline float3 ApplyProceduralBoxVertex(float3 positionOS, float wobblePhase, float wobbleAmount, float squashAmount)
            {
                if (wobbleAmount <= 0.0001 && abs(squashAmount) <= 0.0001)
                    return positionOS;

                float breath = sin(_Time.y * 3.7 + wobblePhase) * 0.02 * wobbleAmount;
                float scaleY = max(0.55, 1.0 + breath + squashAmount);
                float scaleX = rsqrt(scaleY);

                float3 outPos = positionOS;
                outPos.x *= scaleX;
                outPos.y *= scaleY;
                return outPos;
            }

            struct Attributes
            {
                float4 vertex     : POSITION;
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

                float wobblePhase = GET_BOX_PROP(_WobblePhase);
                float wobbleAmount = GET_BOX_PROP(_WobbleAmount);
                float squashAmount = GET_BOX_PROP(_SquashAmount);
                float3 animatedOS = ApplyProceduralBoxVertex(IN.vertex.xyz, wobblePhase, wobbleAmount, squashAmount);

                OUT.positionCS = TransformObjectToHClip(animatedOS);
                OUT.uv = IN.uv;
                OUT.color = IN.color * GET_BOX_PROP(_Color);
                return OUT;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float sdPolyomino(float2 q, float inset, float radius, float rectCount, float4 rect0, float4 rect1)
            {
                float rClamped = max(0.008, radius);
                float2 b0 = max(float2(0.02, 0.02), rect0.zw - inset);
                float d = sdRoundBox(q - rect0.xy, b0, min(rClamped, min(b0.x, b0.y)));
                if (rectCount > 1.5)
                {
                    float2 b1 = max(float2(0.02, 0.02), rect1.zw - inset);
                    float d1 = sdRoundBox(q - rect1.xy, b1, min(rClamped, min(b1.x, b1.y)));
                    d = min(d, d1);
                }
                return d;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 localSize      = GET_BOX_PROP(_LocalSize);
                float  rectCount      = GET_BOX_PROP(_RectCount);
                float4 rect0          = GET_BOX_PROP(_Rect0);
                float4 rect1          = GET_BOX_PROP(_Rect1);
                float3 bodyColor      = GET_BOX_PROP(_BodyColor).rgb;
                float3 bodyDarkColor  = GET_BOX_PROP(_BodyDarkColor).rgb;
                float3 outlineColor   = GET_BOX_PROP(_OutlineColor).rgb;
                float3 highlightColor = GET_BOX_PROP(_HighlightColor).rgb;
                float  exitHighlight  = GET_BOX_PROP(_ExitHighlight);
                float  dragHighlight  = GET_BOX_PROP(_DragHighlight);
                float  iceAmount      = GET_BOX_PROP(_IceAmount);
                float  lockAmount     = GET_BOX_PROP(_LockAmount);
                float  lockOpen       = GET_BOX_PROP(_LockOpen);
                float  lockWiggle     = GET_BOX_PROP(_LockWiggle);
                float4 lockCenter     = GET_BOX_PROP(_LockCenter);
                float  socketCountF   = GET_BOX_PROP(_SocketCount);
                float  socketRadF     = GET_BOX_PROP(_SocketRadius);
                float  fillAmount     = saturate(GET_BOX_PROP(_FillAmount));
                float  fillWave       = saturate(GET_BOX_PROP(_FillWave));

                // Isotropic block-local coordinates so circles and borders never stretch across non-square shapes
                float2 q = (IN.uv - 0.5) * max(localSize.xy, float2(0.25, 0.25));
                float aa = 0.008;

                // 1. Main Box Geometry (Front Face + 3D Lower Base Extrusion)
                float dFrontBox = sdPolyomino(q, 0.0, _CornerRad, rectCount, rect0, rect1);
                float dBaseBox  = sdPolyomino(q + float2(0.0, _LipOffset), 0.0, _CornerRad, rectCount, rect0, rect1);
                float dTotal    = min(dFrontBox, dBaseBox);

                if (_CrateMode > 0.5)
                {
                    // Twin sky-blue front latch clasps anchored at the bottom-center rim of the lowest rect
                    float4 claspRect = (rectCount > 1.5 && (rect1.y - rect1.w) < (rect0.y - rect0.w)) ? rect1 : rect0;
                    float2 claspAnchor = float2(claspRect.x, claspRect.y - claspRect.w - 0.012);
                    float2 claspHalf   = float2(0.044, 0.068);
                    float  claspRad    = 0.018;
                    float2 qClaspL     = q - (claspAnchor + float2(-0.076, 0.0));
                    float2 qClaspR     = q - (claspAnchor + float2( 0.076, 0.0));
                    float  dClaspL     = sdRoundBox(qClaspL, claspHalf, claspRad);
                    float  dClaspR     = sdRoundBox(qClaspR, claspHalf, claspRad);
                    float  dTwinClasp  = min(dClaspL, dClaspR);
                    float2 qLocalClasp = (dClaspL < dClaspR) ? qClaspL : qClaspR;

                    // Crisp white outer sticker outline wrapping both case & front clasps
                    float stickerPad = 0.038;
                    float dSticker   = min(dTotal, dTwinClasp) - stickerPad;
                    float totalAlpha = 1.0 - smoothstep(-aa, aa, dSticker);
                    if (totalAlpha < 0.002) discard;

                    float activeGlow = saturate(dragHighlight); // çıkış (tıklanabilir) vurgusu kaldırıldı; sadece sürüklerken parlar
                    float3 col = _StickerOutlineColor.rgb;

                    // Case silhouette inside the white sticker border
                    float caseMask = 1.0 - smoothstep(-aa, aa, dTotal);
                    float skirtDepth = saturate((-dFrontBox) / max(0.02, _LipOffset));
                    float3 skirtCol = lerp(bodyDarkColor * 0.92, bodyDarkColor * 0.72, saturate(1.0 - skirtDepth));

                    // Thin dark definition seam inside sticker border
                    float caseEdgeDark = (1.0 - smoothstep(-0.014, 0.0, dTotal)) * caseMask;
                    float3 caseCol = lerp(skirtCol, bodyDarkColor * 0.55, caseEdgeDark * 0.45);

                    // Top crate lid face + subtle satin gradient + edge highlight
                    float frontMask = 1.0 - smoothstep(-aa, aa, dFrontBox);
                    float frontRim  = frontMask - (1.0 - smoothstep(-0.026 - aa, -0.026 + aa, dFrontBox));
                    float lidGrad   = saturate((q.y - rect0.y) / max(0.15, rect0.w) * 0.5 + 0.55);
                    float3 lidCol   = lerp(bodyColor * 0.93, lerp(bodyColor, highlightColor, 0.16), lidGrad);
                    lidCol = lerp(lidCol, highlightColor, saturate(frontRim) * (0.42 + activeGlow * 0.30));
                    caseCol = lerp(caseCol, lidCol, frontMask);

                    // Stamped recessed inner square lid groove (matching uploaded collector crates)
                    float dInnerPanel = sdPolyomino(q - float2(0.0, 0.012), _WallInset * 1.28, max(0.018, _InnerRad * 0.84), rectCount, rect0, rect1);
                    float panelMask   = (1.0 - smoothstep(-aa, aa, dInnerPanel)) * frontMask;
                    float grooveBand  = 1.0 - smoothstep(0.0, 0.020, abs(dInnerPanel));
                    float upperGroove = saturate((q.y - rect0.y) * 1.4 + 0.35);
                    float3 panelCol   = lerp(bodyColor * 0.98, lerp(bodyColor, highlightColor, 0.18), 0.55);
                    caseCol = lerp(caseCol, panelCol, panelMask);
                    caseCol = lerp(caseCol, bodyDarkColor * 0.62, grooveBand * upperGroove * frontMask * 0.75);
                    caseCol = lerp(caseCol, highlightColor, grooveBand * (1.0 - upperGroove) * frontMask * 0.48);

                    col = lerp(col, caseCol, caseMask);

                    // Draw Twin Sky-Blue Front Latch Clasps with crisp border & glossy top band
                    float claspSeamMask = 1.0 - smoothstep(-aa, aa, dTwinClasp - 0.014);
                    col = lerp(col, _StickerOutlineColor.rgb, claspSeamMask * 0.85);

                    float claspMask = 1.0 - smoothstep(-aa, aa, dTwinClasp);
                    float claspYNorm = saturate((qLocalClasp.y / claspHalf.y) * 0.5 + 0.5);
                    float3 claspBlue = lerp(float3(0.12, 0.48, 0.88), float3(0.32, 0.72, 1.00), smoothstep(0.0, 0.85, claspYNorm));
                    float claspBand  = smoothstep(0.45, 0.85, claspYNorm) * (1.0 - smoothstep(0.85, 1.0, claspYNorm));
                    claspBlue = lerp(claspBlue, float3(0.74, 0.92, 1.00), claspBand * 0.65);
                    float claspDarkEdge = (1.0 - smoothstep(-0.010, 0.0, dTwinClasp)) * claspMask;
                    claspBlue = lerp(claspBlue, float3(0.08, 0.32, 0.68), claspDarkEdge * 0.55);
                    col = lerp(col, claspBlue, claspMask);

                    // Subtle white highlight pulse when ready to tap into slot
                    col = lerp(col, lerp(col, float3(1.0, 1.0, 1.0), 0.10), activeGlow * frontMask * 0.55);

                    // Reuse plateShadowMask & plateMask for lock/ice overlays below
                    float plateMask = panelMask;
                    float plateShadowMask = saturate(0.5 - lidGrad * 0.5);

                    if (lockAmount > 0.001)
                    {
                        // Kilitli kutu kapağı (Silver Lock sprite'ı sahnede üstüne çizilir)
                        float la = saturate(lockAmount);
                        float3 lockLid = lerp(bodyColor, float3(1.0, 1.0, 1.0), 0.24);
                        col = lerp(col, lockLid, plateMask * la);
                    }

                    if (iceAmount > 0.001)
                    {
                        float iceAmt = saturate(iceAmount);
                        float3 iceBody = float3(0.32, 0.78, 0.98);
                        float3 iceFrost = float3(0.86, 0.97, 1.00);
                        float snowBand = smoothstep(0.08, 0.36, q.y + 0.06 * sin(q.x * 9.0));
                        float3 iceCol = lerp(iceBody, iceFrost, 0.25 + 0.35 * snowBand);
                        col = lerp(col, iceCol, caseMask * iceAmt * 0.85);
                    }

                    // Oynanamaz (2. sıra ve arkası / yolu kapalı) kutuları hafifçe grileştir (renk belli kalsın)
                    float unplayableCrate = (1.0 - saturate(exitHighlight)) * (1.0 - saturate(iceAmount));
                    if (unplayableCrate > 0.001)
                    {
                        float lumC = dot(col, float3(0.299, 0.587, 0.114));
                        float3 grayC = lerp(float3(lumC, lumC, lumC) * 0.82 + float3(0.05, 0.05, 0.07), col, 0.58);
                        col = lerp(col, grayC, unplayableCrate);
                    }

                    return half4(col * IN.color.rgb, totalAlpha * IN.color.a);
                }

                // Complete Silhouette (Hollow Square mode when sockCount == 0, or Legacy socket plate mode when sockCount > 0)
                float totalAlpha = 1.0 - smoothstep(-aa, aa, dTotal);
                if (totalAlpha < 0.002) discard;

                int sockCount = clamp((int)round(socketCountF), 0, 16);
                float activeGlow = saturate(dragHighlight); // çıkış (tıklanabilir) vurgusu kaldırıldı; sadece sürüklerken parlar
                float outlineWidth = _OutlineThickness;
                float innerSilhouetteMask = 1.0;
                float frontOuterMask = 1.0;
                float frontRimMask = 0.0;
                float plateMask = 0.0;
                float plateShadowMask = 0.0;
                float3 col = float3(0.0, 0.0, 0.0);

                if (sockCount == 0)
                {
                    // Image-2 Style: Clean vibrant square frame (no dark outer border) + 3D lower skirt + deep hollow 3D cavity in the block's own color
                    float skirtDepth = saturate((-dFrontBox) / max(0.02, _LipOffset));
                    float3 skirtTopCol = lerp(bodyDarkColor, bodyColor, 0.16);
                    float3 skirtBotCol = bodyDarkColor * 0.64;
                    float3 skirtCol = lerp(skirtTopCol, skirtBotCol, saturate(1.0 - skirtDepth));
                    float baseEdgeMask = 1.0 - smoothstep(-0.012, 0.0, dTotal);
                    skirtCol = lerp(skirtCol, bodyDarkColor * 0.50, baseEdgeMask * 0.40);

                    frontOuterMask = 1.0 - smoothstep(-aa, aa, dFrontBox);
                    frontRimMask = frontOuterMask - (1.0 - smoothstep(-0.022 - aa, -0.022 + aa, dFrontBox));
                    float3 topFrameRGB = lerp(bodyColor * 0.98, bodyColor * 1.04, saturate(q.y * 0.35 + 0.55));
                    topFrameRGB = lerp(topFrameRGB, highlightColor, saturate(frontRimMask) * (0.28 + activeGlow * 0.42));
                    col = lerp(skirtCol, topFrameRGB, frontOuterMask);

                    // Symmetric rounded-square inner hollow opening
                    float hollowInset = 0.118;
                    float hollowInnerRad = 0.056;
                    float dPlate = sdPolyomino(q, hollowInset, hollowInnerRad, rectCount, rect0, rect1);
                    plateMask = (1.0 - smoothstep(-aa, aa, dPlate)) * frontOuterMask;

                    // Subtle inner rim catchlight on the front frame just outside the hollow opening
                    float plateOuterRim = (1.0 - smoothstep(0.0, 0.016, dPlate)) * (1.0 - plateMask) * frontOuterMask;
                    col = lerp(col, highlightColor, plateOuterRim * (0.26 + activeGlow * 0.24));

                    // Ön çerçeve + iç kenar gölge katmanı (_FrontRimOnly = 1): çukurdaki topların önünde çizilerek derinlik verir
                    if (_FrontRimOnly > 0.5)
                    {
                        if (lockAmount > 0.001 || iceAmount > 0.001) discard;

                        float frameAlpha = frontOuterMask * (1.0 - plateMask);
                        float topExtentY = (rectCount > 1.5) ? max(rect0.y + rect0.w, rect1.y + rect1.w) : (rect0.y + rect0.w);
                        float topBand = saturate(1.0 - (topExtentY - hollowInset - q.y) * 2.4);
                        float topCast = topBand * (1.0 - smoothstep(-0.155, -0.004, dPlate));
                        float rimVignette = 1.0 - smoothstep(-0.072, 0.0, dPlate);
                        float pitShadowAlpha = saturate(max(topCast * 0.65, rimVignette * 0.36)) * plateMask;
                        float outAlpha = saturate(max(frameAlpha, pitShadowAlpha));
                        if (outAlpha < 0.003) discard;

                        float3 frameCol = col;
                        float unplayableRim = (1.0 - saturate(exitHighlight)) * (1.0 - saturate(iceAmount));
                        if (unplayableRim > 0.001)
                        {
                            float lumR = dot(frameCol, float3(0.299, 0.587, 0.114));
                            float3 grayR = lerp(float3(lumR, lumR, lumR) * 0.82 + float3(0.05, 0.05, 0.07), frameCol, 0.58);
                            frameCol = lerp(frameCol, grayR, unplayableRim);
                        }
                        float completeRimFlash = saturate((dragHighlight - 1.0) * 0.88);
                        if (completeRimFlash > 0.001)
                        {
                            float3 warmRimFlash = lerp(highlightColor, float3(1.0, 0.99, 0.94), 0.74);
                            frameCol = lerp(frameCol, warmRimFlash * 1.16, completeRimFlash);
                        }

                        float3 innerCastCol = lerp(bodyDarkColor * 0.12, float3(0.03, 0.04, 0.11), 0.52);
                        float3 finalRimCol = lerp(frameCol, innerCastCol, saturate(pitShadowAlpha / max(0.003, outAlpha)));
                        return half4(finalRimCol * IN.color.rgb, outAlpha * IN.color.a);
                    }

                    // 3D inner top & side walls descending deeply into the hollow cavity
                    float hollowWallDepth = _LipOffset * 1.72;
                    float dDeepVoid = sdPolyomino(q + float2(0.0, hollowWallDepth), hollowInset + 0.016, max(0.016, hollowInnerRad - 0.008), rectCount, rect0, rect1);
                    float voidMask = (1.0 - smoothstep(-aa, aa, dDeepVoid)) * plateMask;

                    float innerWallT = saturate(dDeepVoid / max(0.02, hollowWallDepth));
                    float3 innerWallTop = lerp(bodyDarkColor * 0.92, bodyColor, 0.12);
                    float3 innerWallBot = bodyDarkColor * 0.28;
                    float3 innerWallCol = lerp(innerWallBot, innerWallTop, innerWallT);
                    float topInnerCrease = (1.0 - smoothstep(-0.022, 0.0, dPlate)) * saturate((q.y - rect0.y) * 3.6);
                    innerWallCol = lerp(innerWallCol, bodyDarkColor * 0.20, topInnerCrease * 0.72);

                    // Deep recessed hollow void floor in a rich dark shade of the block's color
                    float3 deepVoidBase = lerp(bodyDarkColor * 0.24, bodyColor * 0.16, 0.32);
                    float3 deepVoidShadow = lerp(bodyDarkColor * 0.11, bodyColor * 0.08, 0.32);
                    float overhangShadow = 1.0 - smoothstep(-0.085, 0.0, dDeepVoid);
                    float voidEdgeAO = 1.0 - smoothstep(0.0, 0.110, -dDeepVoid);
                    plateShadowMask = saturate(max(overhangShadow * 0.86, voidEdgeAO * 0.58));
                    float3 hollowVoidRGB = lerp(deepVoidBase, deepVoidShadow, plateShadowMask);

                    // Crisp bottom inner lip bevel where the bottom front wall meets the hollow pit
                    float botInnerLip = (1.0 - smoothstep(-0.020, 0.0, dPlate)) * saturate(-(q.y - rect0.y) * 3.0);
                    hollowVoidRGB = lerp(hollowVoidRGB, lerp(bodyDarkColor, bodyColor, 0.12), botInnerLip * 0.88);

                    float3 cavityRGB = lerp(innerWallCol, hollowVoidRGB, voidMask);

                    // Subtle recessed floor bounce highlight as 3D balls fill the cavity
                    if (fillAmount > 0.001 || fillWave > 0.001)
                    {
                        float pitGlow = saturate(fillAmount * 0.28 + fillWave * 0.20) * voidMask;
                        cavityRGB = lerp(cavityRGB, lerp(bodyDarkColor, bodyColor, 0.36), pitGlow);
                    }

                    col = lerp(col, cavityRGB, plateMask);
                }
                else
                {
                    // Sharper outer edge silhouette (brightens cleanly when exit-ready or actively dragged)
                    float3 activeOutline = lerp(outlineColor, float3(0.99, 0.995, 1.0), activeGlow * 0.96);
                    // Vurgu sadece kenar rengini açar; kalınlaşırsa renkli gövde içeri doğru yenir ve öndeki bloklar küçük görünür
                    innerSilhouetteMask = 1.0 - smoothstep(-outlineWidth - aa, -outlineWidth + aa, dTotal);
                    float skirtDepth = saturate((-dFrontBox) / max(0.02, _LipOffset));
                    float3 skirtCol = lerp(bodyDarkColor * 0.98, bodyDarkColor * 0.76, saturate(1.0 - skirtDepth));
                    col = lerp(activeOutline, skirtCol, innerSilhouetteMask);

                    // Front Box Frame (chunky bright colored rim above the 3D lower skirt + crisp specular top bevel)
                    frontOuterMask = 1.0 - smoothstep(-outlineWidth - aa, -outlineWidth + aa, dFrontBox);
                    frontRimMask = frontOuterMask - (1.0 - smoothstep(-outlineWidth - 0.024 - aa, -outlineWidth - 0.024 + aa, dFrontBox));
                    float3 topFrameRGB = lerp(bodyColor * 0.96, bodyColor * 1.04, saturate(q.y * 0.35 + 0.55));
                    col = lerp(col, topFrameRGB, frontOuterMask);
                    col = lerp(col, highlightColor, saturate(frontRimMask) * (0.56 + activeGlow * 0.34));

                    // 3. Deep 3D Recessed Tray Cavity with Sunken White Plate
                    float dPlate = sdPolyomino(q, _WallInset, _InnerRad, rectCount, rect0, rect1);
                    plateMask = 1.0 - smoothstep(-aa, aa, dPlate);

                    // Crisp inner wall rim catchlight just outside the cavity opening
                    float plateOuterRim = (1.0 - smoothstep(0.0, 0.020, dPlate)) * (1.0 - plateMask);
                    col = lerp(col, highlightColor, plateOuterRim * (0.44 + activeGlow * 0.24));

                    // Visible colored 3D back (top) and side inner walls descending down to the sunken white floor
                    float dCavityFloor = sdPolyomino(q + float2(0.0, 0.044), _WallInset + 0.014, max(0.016, _InnerRad - 0.012), rectCount, rect0, rect1);
                    float floorMask = (1.0 - smoothstep(-aa, aa, dCavityFloor)) * plateMask;

                    float wallDropGrad = saturate(-dPlate / 0.052);
                    float sideSlope = saturate((dCavityFloor - dPlate) * 10.0);
                    float3 innerWallCol = lerp(bodyDarkColor * 0.88, bodyDarkColor * 0.62, wallDropGrad);
                    innerWallCol = lerp(innerWallCol, bodyColor * 0.80, sideSlope * 0.24);

                    // Deep cast shadow across the upper & edge parts of the white floor so it feels deeply sunken inside the box
                    float dDeepShadow = sdPolyomino(q - float2(0.0, -0.056), _WallInset + 0.014, max(0.016, _InnerRad - 0.012), rectCount, rect0, rect1);
                    float castBandMask = smoothstep(-0.024, 0.018, dDeepShadow);
                    float edgeAO = 1.0 - smoothstep(0.0, 0.075, -dCavityFloor);
                    float floorShadowMix = saturate(max(castBandMask * 0.85, edgeAO * 0.72));
                    float3 sunkenWhiteRGB = lerp(_PlateColor.rgb, _PlateShadowColor.rgb, floorShadowMix);

                    // Subtle contact crease right where the colored inner walls meet the sunken white plate
                    float wallFloorCrease = (1.0 - smoothstep(-0.012, 0.006, dCavityFloor)) * floorMask;
                    sunkenWhiteRGB = lerp(sunkenWhiteRGB, _PlateShadowColor.rgb * 0.78, wallFloorCrease * 0.45);

                    float3 cavityRGB = lerp(innerWallCol, sunkenWhiteRGB, floorMask);
                    col = lerp(col, cavityRGB, plateMask);

                    // Keep plateShadowMask alias for lock/ice overlays below
                    plateShadowMask = floorShadowMix;

                    // 4. Dynamic Gray Circular Sockets (1 to 16 sockets packed in _SockPair0.._SockPair7)
                    float4 sp0 = GET_BOX_PROP(_SockPair0);
                    float4 sp1 = GET_BOX_PROP(_SockPair1);
                    float4 sp2 = GET_BOX_PROP(_SockPair2);
                    float4 sp3 = GET_BOX_PROP(_SockPair3);
                    float4 sp4 = GET_BOX_PROP(_SockPair4);
                    float4 sp5 = GET_BOX_PROP(_SockPair5);
                    float4 sp6 = GET_BOX_PROP(_SockPair6);
                    float4 sp7 = GET_BOX_PROP(_SockPair7);

                    float2 socks[16];
                    socks[0]  = sp0.xy; socks[1]  = sp0.zw;
                    socks[2]  = sp1.xy; socks[3]  = sp1.zw;
                    socks[4]  = sp2.xy; socks[5]  = sp2.zw;
                    socks[6]  = sp3.xy; socks[7]  = sp3.zw;
                    socks[8]  = sp4.xy; socks[9]  = sp4.zw;
                    socks[10] = sp5.xy; socks[11] = sp5.zw;
                    socks[12] = sp6.xy; socks[13] = sp6.zw;
                    socks[14] = sp7.xy; socks[15] = sp7.zw;

                    float sockRad = max(0.018, socketRadF);
                    float dSock = 1e5;
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
                    float topDip = saturate(sockDelta.y * 0.90 + 0.10);
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
                }

                // 4b. Kilitli blok: plaka bloğun kendi renginde bir kapakla kapanır (Silver Lock sprite'ı sahnede üstüne çizilir)
                if (lockAmount > 0.001)
                {
                    float la = saturate(lockAmount);
                    float3 lidCol   = lerp(bodyColor, float3(1.0, 1.0, 1.0), 0.30);
                    float3 lidShade = lerp(bodyColor, float3(1.0, 1.0, 1.0), 0.08);
                    float3 lid = lerp(lidCol, lidShade, plateShadowMask);
                    col = lerp(col, lid, plateMask * la);
                }

                // 5. Optional Stylized Frozen Ice Cap Overlay (_IceAmount = 0..1)
                if (iceAmount > 0.001)
                {
                    float iceAmt = saturate(iceAmount);
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

                // Oynanamaz (2. sıra ve arkası / yolu kapalı) kutuları hafifçe grileştir (renk belli kalsın)
                float unplayable = (1.0 - saturate(exitHighlight)) * (1.0 - saturate(iceAmount));
                if (unplayable > 0.001)
                {
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    float3 grayCol = lerp(float3(lum, lum, lum) * 0.82 + float3(0.05, 0.05, 0.07), col, 0.58);
                    col = lerp(col, grayCol, unplayable);
                }

                // Tamamlanma sarji sirasinda (_DragHighlight > 1.0) kutuyu yumusak beyaz-altin isiltiyla parlat
                float completeFlash = saturate((dragHighlight - 1.0) * 0.88);
                if (completeFlash > 0.001)
                {
                    float3 warmFlash = lerp(highlightColor, float3(1.0, 0.99, 0.94), 0.74);
                    col = lerp(col, warmFlash * 1.16, completeFlash);
                }

                return half4(col * IN.color.rgb, totalAlpha * IN.color.a);
            }
            ENDHLSL
        }
    }
}
