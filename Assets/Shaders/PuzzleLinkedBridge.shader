Shader "PuzzleGlass/LinkedBoxBridge"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ColorA ("Half A Body Color", Color) = (0.24, 0.77, 0.16, 1.0)
        _DarkColorA ("Half A 3D Skirt Color", Color) = (0.11, 0.48, 0.09, 1.0)
        _HighlightColorA ("Half A Rim Highlight", Color) = (0.50, 0.93, 0.38, 1.0)
        _ColorB ("Half B Body Color", Color) = (1.00, 0.82, 0.12, 1.0)
        _DarkColorB ("Half B 3D Skirt Color", Color) = (0.80, 0.45, 0.05, 1.0)
        _HighlightColorB ("Half B Rim Highlight", Color) = (1.00, 0.93, 0.42, 1.0)
        _QuadSize ("Quad World Size (w, h)", Vector) = (1.0, 0.5, 0, 0)
        _BarHalfSize ("Bar Half Size (hx, hy, cornerRad, lipWorldY)", Vector) = (0.38, 0.09, 0.04, 0.05)
        _DownDirLocal ("World Down in Bar Local Space (xy)", Vector) = (0.0, -1.0, 0.0, 0.0)
        _ExitHighlight ("Playable State (0=dim, 1=bright)", Float) = 1.0
        _IceA ("Half A Frozen (0..1)", Float) = 0.0
        _IceB ("Half B Frozen (0..1)", Float) = 0.0
        _FlashAmount ("Drag / Completion Glow", Float) = 0.0
        _WaitingSide ("Waiting Side (-1=A done, 0=none, 1=B done)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-3"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "LinkedBoxBridgeForward"
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
                float4 _ColorA;
                float4 _DarkColorA;
                float4 _HighlightColorA;
                float4 _ColorB;
                float4 _DarkColorB;
                float4 _HighlightColorB;
                float4 _QuadSize;
                float4 _BarHalfSize;
                float4 _DownDirLocal;
                float _ExitHighlight;
                float _IceA;
                float _IceB;
                float _FlashAmount;
                float _WaitingSide;
            CBUFFER_END

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
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
                OUT.positionCS = TransformObjectToHClip(IN.vertex.xyz);
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
                float2 q = (IN.uv - 0.5) * max(_QuadSize.xy, float2(0.05, 0.05));
                float2 b = max(_BarHalfSize.xy, float2(0.02, 0.02));
                float r = min(_BarHalfSize.z, min(b.x, b.y));
                float lipOffset = max(0.0, _BarHalfSize.w);
                float2 downDir = _DownDirLocal.xy;
                float downLen = length(downDir);
                downDir = downLen > 0.001 ? (downDir / downLen) : float2(0.0, -1.0);

                // Anti-aliasing width scaled cleanly with bar thickness
                float aa = max(0.0025, b.y * 0.065);

                // Front rounded bar + 3D world-down skirt extrusion (matches PuzzleCarrierBox 2.5D depth!)
                float dFront = sdRoundBox(q, b, r);
                float dSkirt = sdRoundBox(q - downDir * lipOffset, b, r);
                float dTotal = min(dFront, dSkirt);

                // Soft ambient contact drop shadow right below the bridge
                float dShadow = sdRoundBox(q - downDir * (lipOffset * 1.45), b + float2(0.004, 0.004), r + 0.004);
                float shadowAlpha = (1.0 - smoothstep(-aa * 2.0, b.y * 0.72, dShadow)) * 0.30;

                float bodyAlpha = 1.0 - smoothstep(-aa, aa, dTotal);
                float totalAlpha = max(bodyAlpha, shadowAlpha);
                if (totalAlpha < 0.003) discard;

                // Smooth crisp 50/50 split along bar local X (negative = A, positive = B)
                float sideT = smoothstep(-aa * 1.2, aa * 1.2, q.x);
                float3 bodyCol = lerp(_ColorA.rgb, _ColorB.rgb, sideT);
                float3 darkCol = lerp(_DarkColorA.rgb, _DarkColorB.rgb, sideT);
                float3 hiCol   = lerp(_HighlightColorA.rgb, _HighlightColorB.rgb, sideT);
                float  iceAmt  = lerp(saturate(_IceA), saturate(_IceB), sideT);

                // 3D lower skirt shading (exact visual language of PuzzleCarrierBox.shader sockCount == 0)
                float skirtDepth = saturate((-dFront) / max(0.01, lipOffset));
                float3 skirtTop = lerp(darkCol, bodyCol, 0.16);
                float3 skirtBot = darkCol * 0.62;
                float3 skirtCol = lerp(skirtTop, skirtBot, saturate(1.0 - skirtDepth));
                float baseEdge = 1.0 - smoothstep(-aa * 2.2, 0.0, dTotal);
                skirtCol = lerp(skirtCol, darkCol * 0.48, baseEdge * 0.42);

                // Top rounded face with subtle cylindrical bevel gradient along world-up
                float frontMask = 1.0 - smoothstep(-aa, aa, dFront);
                float upCoord = dot(q, -downDir) / max(0.01, b.y);
                float topGrad = saturate(upCoord * 0.42 + 0.54);
                float3 faceCol = lerp(bodyCol * 0.94, bodyCol * 1.06, topGrad);

                // Soft glossy top ridge band (cylindrical rounded bar feel matching Image 1's glossy bar!)
                float perpPos = q.y / max(0.01, b.y);
                float ridge = exp(-pow((perpPos - 0.34) * 2.6, 2.0));
                float lowerShade = smoothstep(-0.15, -0.95, perpPos);
                faceCol = lerp(faceCol, hiCol, ridge * 0.32);
                faceCol = lerp(faceCol, darkCol * 0.90, lowerShade * 0.28);

                // Crisp subtle outer rim highlight on the front face
                float rimBand = frontMask * (1.0 - (1.0 - smoothstep(-b.y * 0.24 - aa, -b.y * 0.24 + aa, dFront)));
                faceCol = lerp(faceCol, hiCol, rimBand * (0.28 + saturate(_FlashAmount) * 0.35));

                // Clean subtle central join seam where color A meets color B
                float seamDist = abs(q.x);
                float seamDark = (1.0 - smoothstep(0.0, max(aa * 2.2, b.y * 0.12), seamDist)) * frontMask;
                faceCol = lerp(faceCol, darkCol * 0.58, seamDark * 0.22);

                // Subtle rounded end-cap anchor highlight where bar grips each box rim
                float endDist = abs(q.x) - (b.x - b.y * 0.65);
                float capBevel = smoothstep(-b.y * 0.15, b.y * 0.12, endDist) * frontMask;
                faceCol = lerp(faceCol, lerp(faceCol, hiCol, 0.16), capBevel * 0.35);

                float3 col = lerp(skirtCol, faceCol, frontMask);

                // Frozen ice tinting if that partner is currently frozen
                if (iceAmt > 0.001)
                {
                    float3 iceBody = float3(0.32, 0.78, 0.98);
                    float3 iceFrost = float3(0.84, 0.97, 1.00);
                    float3 iceCol = lerp(iceBody, iceFrost, ridge * 0.45 + 0.15);
                    col = lerp(col, iceCol, iceAmt * 0.88);
                }

                // Waiting-for-partner subtle energy pulse flowing from finished half toward unfinished partner
                if (abs(_WaitingSide) > 0.5)
                {
                    float isWaitingHalf = (_WaitingSide < 0.0) ? (1.0 - sideT) : sideT;
                    float dir = (_WaitingSide < 0.0) ? 1.0 : -1.0;
                    float wave = 0.5 + 0.5 * sin(q.x * dir * 16.0 - _Time.y * 6.5);
                    float pulse = smoothstep(0.45, 1.0, wave) * frontMask;
                    col = lerp(col, lerp(hiCol, float3(1.0, 1.0, 1.0), 0.55), pulse * 0.30 + isWaitingHalf * frontMask * 0.14);
                }

                // Unplayable dimming matching PuzzleCarrierBox.shader exactly
                float unplayable = (1.0 - saturate(_ExitHighlight)) * (1.0 - iceAmt);
                if (unplayable > 0.001)
                {
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    float3 grayCol = lerp(float3(lum, lum, lum) * 0.82 + float3(0.05, 0.05, 0.07), col, 0.58);
                    col = lerp(col, grayCol, unplayable);
                }

                // Completion / drag flash glow
                float flash = saturate((_FlashAmount - 0.15) * 0.65);
                if (flash > 0.001)
                {
                    float3 warmFlash = lerp(hiCol, float3(1.0, 0.99, 0.94), 0.72);
                    col = lerp(col, warmFlash * 1.14, flash * frontMask);
                }

                // Blend soft shadow underneath body
                float3 shadowRgb = float3(0.10, 0.12, 0.25);
                float3 finalCol = lerp(shadowRgb, col, saturate(bodyAlpha / max(0.003, totalAlpha)));
                return half4(finalCol * IN.color.rgb, totalAlpha * IN.color.a);
            }
            ENDHLSL
        }
    }
}
