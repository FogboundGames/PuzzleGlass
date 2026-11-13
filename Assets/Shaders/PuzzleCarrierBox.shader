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
        _OutlineThickness ("Outer Outline Thickness", Float) = 0.022
        _LipOffset ("3D Lower Lip Offset", Float) = 0.088
        _ExitHighlight ("Exit-Ready Rim Highlight", Float) = 0.0
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

                // Start with Outer Outline Color (turns crisp white when unobstructed exit is ready, matching Image 1)
                float3 activeOutline = lerp(_OutlineColor.rgb, float3(0.96, 0.98, 1.0), saturate(_ExitHighlight) * 0.92);
                float outlineWidth = _OutlineThickness + saturate(_ExitHighlight) * 0.008;
                float innerSilhouetteMask = 1.0 - smoothstep(-outlineWidth - aa, -outlineWidth + aa, dTotal);
                float3 col = lerp(activeOutline, _BodyDarkColor.rgb, innerSilhouetteMask);

                // Front Box Frame (bright body color sitting above the 3D lower base lip)
                float frontOuterMask = 1.0 - smoothstep(-outlineWidth - aa, -outlineWidth + aa, dFrontBox);
                float frontRimMask = frontOuterMask - (1.0 - smoothstep(-outlineWidth - 0.016 - aa, -outlineWidth - 0.016 + aa, dFrontBox));
                col = lerp(col, _BodyColor.rgb, frontOuterMask);
                col = lerp(col, _HighlightColor.rgb, saturate(frontRimMask) * 0.32);

                // 3. Inner White Recessed Plate
                float  dPlate    = sdPolyomino(q, _WallInset, _InnerRad);
                float  plateMask = 1.0 - smoothstep(-aa, aa, dPlate);

                // Subtle highlight rim just outside the white plate
                float plateOuterRim = (1.0 - smoothstep(0.0, 0.018, dPlate)) * (1.0 - plateMask);
                col = lerp(col, _HighlightColor.rgb, plateOuterRim * 0.38);

                // Top Recessed Inner Shadow Crescent on the White Plate
                float dPlateShifted   = sdPolyomino(q - float2(0.0, -0.028), _WallInset, _InnerRad);
                float plateShadowMask = smoothstep(-aa, aa, dPlateShifted);
                float3 plateRGB       = lerp(_PlateColor.rgb, _PlateShadowColor.rgb, plateShadowMask);

                col = lerp(col, plateRGB, plateMask);

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
                float2 sockShift  = float2(0.0, -sockRad * 0.293);
                float dSock       = 1e5;
                float dActiveShad = 1e5;

                for (int i = 0; i < 16; i++)
                {
                    if (i < sockCount)
                    {
                        float2 sPos = socks[i];
                        float d = length(q - sPos) - sockRad;
                        if (d < dSock)
                        {
                            dSock = d;
                            dActiveShad = length(q - (sPos + sockShift)) - sockRad;
                        }
                    }
                }

                float sockMask = 1.0 - smoothstep(-aa, aa, dSock);
                float sockShadowMask = smoothstep(-aa, aa, dActiveShad);

                float3 sockRGB = lerp(_SocketColor.rgb, _SocketShadowColor.rgb, sockShadowMask);
                col = lerp(col, sockRGB, sockMask * plateMask);

                return half4(col * IN.color.rgb, totalAlpha * IN.color.a);
            }
            ENDHLSL
        }
    }
}
