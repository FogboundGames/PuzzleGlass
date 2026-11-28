Shader "PuzzleGlass/CandyCubeLit"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.18, 0.68, 0.96, 1.0)
        _Color ("Color", Color) = (0.18, 0.68, 0.96, 1.0)
        _ShadowColor ("Warm Subsurface Shadow Color", Color) = (0.07, 0.40, 0.72, 1.0)
        _HighlightColor ("Creamy Bevel Highlight Color", Color) = (0.62, 0.92, 1.00, 1.0)
        _Smoothness ("Satin Smoothness", Range(0.0, 1.0)) = 0.56
        _BevelHighlight ("3D Bevel Edge Catchlight", Range(0.0, 1.0)) = 0.24
        _RimSeparation ("3D Cube Border Separation", Range(0.0, 1.0)) = 0.42
        _WobblePhase ("Wobble Phase", Float) = 0.0
        _WobbleAmount ("Wobble Amount", Range(0.0, 1.0)) = 0.0
        _SquashAmount ("Squash Amount", Range(-0.5, 0.5)) = 0.0
        _SquashAxis ("Squash Axis (Local)", Vector) = (0.0, 1.0, 0.0, 0.0)
        _ReadyGlow ("Collectable Ready Glow", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+15"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _PuzzleBallMarble; // genel (global) değişken; ayarlanmazsa 0 = desen kapalı

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Color;
                float4 _ShadowColor;
                float4 _HighlightColor;
                float _Smoothness;
                float _BevelHighlight;
                float _RimSeparation;
                float _WobblePhase;
                float _WobbleAmount;
                float _SquashAmount;
                float4 _SquashAxis;
                float _ReadyGlow;
            CBUFFER_END

            #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
                UNITY_INSTANCING_BUFFER_START(Props)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _ShadowColor)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _HighlightColor)
                    UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
                    UNITY_DEFINE_INSTANCED_PROP(float, _BevelHighlight)
                    UNITY_DEFINE_INSTANCED_PROP(float, _RimSeparation)
                    UNITY_DEFINE_INSTANCED_PROP(float, _WobblePhase)
                    UNITY_DEFINE_INSTANCED_PROP(float, _WobbleAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float, _SquashAmount)
                    UNITY_DEFINE_INSTANCED_PROP(float4, _SquashAxis)
                    UNITY_DEFINE_INSTANCED_PROP(float, _ReadyGlow)
                UNITY_INSTANCING_BUFFER_END(Props)
                #define GET_CANDY_PROP(prop) UNITY_ACCESS_INSTANCED_PROP(Props, prop)
            #else
                #define GET_CANDY_PROP(prop) prop
            #endif

            inline float3 ApplyProceduralCandyVertex(float3 positionOS, float wobblePhase, float wobbleAmount, float squashAmount, float3 squashAxis)
            {
                if (wobbleAmount <= 0.0001 && abs(squashAmount) <= 0.0001)
                    return positionOS;

                float3 outPos = positionOS;

                // 1. Directional volume-preserving squash & stretch (for collision impacts & arrival pop)
                if (abs(squashAmount) > 0.0001)
                {
                    float axisLenSq = dot(squashAxis, squashAxis);
                    float3 axis = axisLenSq > 0.0001 ? squashAxis * rsqrt(axisLenSq) : float3(0.0, 1.0, 0.0);
                    float axial = dot(outPos, axis);
                    float3 radial = outPos - axis * axial;
                    float scaleAxial = max(0.55, 1.0 + squashAmount);
                    float scaleRadial = rsqrt(scaleAxial);
                    outPos = axis * (axial * scaleAxial) + radial * scaleRadial;
                }

                // 2. Subtle organic GPU wobble / breathing (0 CPU cost)
                if (wobbleAmount > 0.0001)
                {
                    float t = _Time.y * 5.2 + wobblePhase;
                    float sway = sin(t) * wobbleAmount * 0.032;
                    float breath = 1.0 + sin(t * 1.3 + 0.7) * wobbleAmount * 0.025;
                    outPos.y *= breath;
                    float invBreath = rsqrt(max(0.5, breath));
                    outPos.x = outPos.x * invBreath + sway * (outPos.y + 0.5);
                    outPos.z = outPos.z * invBreath;
                }

                return outPos;
            }

            struct Attributes
            {
                float4 vertex       : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS    : TEXCOORD0;
                half3 normalOS    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float wobblePhase = GET_CANDY_PROP(_WobblePhase);
                float wobbleAmount = GET_CANDY_PROP(_WobbleAmount);
                float squashAmount = GET_CANDY_PROP(_SquashAmount);
                float3 squashAxis = GET_CANDY_PROP(_SquashAxis).xyz;

                float3 animatedOS = ApplyProceduralCandyVertex(IN.vertex.xyz, wobblePhase, wobbleAmount, squashAmount, squashAxis);

                OUT.positionCS = TransformObjectToHClip(animatedOS);
                OUT.normalWS   = (half3)TransformObjectToWorldNormal(IN.normalOS);
                OUT.normalOS   = (half3)IN.normalOS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half3 N = normalize(IN.normalWS);

                // Önceden normalize edilmiş sabit stüdyo ışık ve yarı-açı vektörleri
                const half3 L_key   = half3(-0.3399h,  0.6798h, -0.6498h);
                const half3 L_fill  = half3( 0.4214h, -0.3813h, -0.8228h);
                const half3 V       = half3( 0.0000h,  0.0599h, -0.9982h);
                const half3 H       = half3(-0.1849h,  0.4024h, -0.8966h);
                const half3 H_catch = half3(-0.1694h,  0.3387h, -0.9255h);

                // 1. Subsurface-scattered half-Lambert wrap diffuse (never turns gray/black!)
                half ndl = dot(N, L_key);
                half wrapDiff = saturate(ndl * 0.54h + 0.46h);
                half smoothDiff = smoothstep(0.08h, 0.92h, wrapDiff);

                half3 baseAlbedo = (half3)GET_CANDY_PROP(_BaseColor).rgb;
                half3 shadowTone = (half3)GET_CANDY_PROP(_ShadowColor).rgb;
                half3 highTone   = (half3)GET_CANDY_PROP(_HighlightColor).rgb;
                half smoothness  = (half)GET_CANDY_PROP(_Smoothness);
                half bevelHigh   = (half)GET_CANDY_PROP(_BevelHighlight);
                half rimSep      = (half)GET_CANDY_PROP(_RimSeparation);
                half readyGlow   = saturate((half)GET_CANDY_PROP(_ReadyGlow));
                half rg          = readyGlow > 0.001h ? (readyGlow * (0.86h + 0.14h * (half)sin(_Time.y * 4.4))) : 0.0h;

                half3 col = lerp(shadowTone, baseAlbedo, smoothDiff);

                // 2. Soft top-down hemisphere studio gradient (preserves rich saturation)
                half hemi = saturate(N.y * 0.5h + 0.5h);
                col = lerp(col * 0.94h, lerp(col, highTone, 0.14h), hemi);

                // Subtle warm fill bounce on lower-right faces
                half fillTerm = saturate(dot(N, L_fill)) * 0.12h;
                col = lerp(col, baseAlbedo * 1.05h, fillTerm);

                // 3. Object-space marble & cube bevel (kürelerde her ikisi de varsayılan kapalı: normalOS hesabı atlanır)
                half bevelMask = 0.0h;
                bool needNOS = (_PuzzleBallMarble > 0.001) || (bevelHigh > 0.001h);
                if (needNOS)
                {
                    half3 nOS = normalize(IN.normalOS);
                    if (_PuzzleBallMarble > 0.001)
                    {
                        const half3 kPearlDir = half3(-0.3812h, 0.7824h, 0.4915h);
                        half isSphere = 1.0h - smoothstep(0.01h, 0.08h, bevelHigh);
                        half swirlCoord = dot(nOS, half3(0.68h, 0.56h, 0.47h)) + 0.18h * sin(nOS.y * 5.2h + nOS.x * 3.1h);
                        half swirlRatio = swirlCoord * 4.3478h;
                        half swirlBand = exp(-(swirlRatio * swirlRatio));
                        half pearlDot = smoothstep(0.84h, 0.95h, abs(dot(nOS, kPearlDir)));
                        half marblePattern = saturate(swirlBand * 0.17h + pearlDot * 0.21h) * isSphere * (0.38h + 0.62h * wrapDiff) * (half)_PuzzleBallMarble;
                        col = lerp(col, lerp(highTone, half3(1.0h, 0.99h, 0.97h), 0.38h), marblePattern);
                    }
                    if (bevelHigh > 0.001h)
                    {
                        half maxAxis = max(abs(nOS.x), max(abs(nOS.y), abs(nOS.z)));
                        bevelMask = saturate((0.975h - maxAxis) * 2.4h) * 0.55h;
                        half upperBevelLight = bevelMask * saturate(ndl * 0.60h + 0.35h) * saturate(N.y + 0.38h);
                        col = lerp(col, highTone, upperBevelLight * bevelHigh);
                    }
                }

                // 4. Lively Satin-Candy Sheen + Delicate Tinted Catchlight (pow yerine hızlı çarpım zinciri)
                half ndh = saturate(dot(N, H));
                half ndh2 = ndh * ndh;
                half ndh4 = ndh2 * ndh2;
                half specBroad = (ndh4 * ndh4 * ndh2) * 0.045h; // ndh^10
                half specPower = lerp(18.0h, 52.0h, smoothness);
                half specGloss = (ndh > 0.001h ? (half)pow((float)ndh, (float)specPower) : 0.0h) * (0.09h + 0.06h * bevelMask);

                half catchDot = saturate(dot(N, H_catch));
                half c2 = catchDot * catchDot;
                half c4 = c2 * c2;
                half c8 = c4 * c4;
                half c28 = (c8 * c8) * c8 * c4; // catchDot^28
                half catchSpot = smoothstep(0.962h, 0.994h, catchDot) * 0.22h + c28 * 0.05h;

                half3 specColor = lerp(highTone, half3(1.0h, 0.99h, 0.97h), 0.28h);
                col += specColor * (specBroad + specGloss + catchSpot);

                // 5. 3D Silhouette & Lower Bevel Cushion AO (keeps adjacent same-color cubes/balls distinctly separated!)
                half ndv = saturate(dot(N, V));
                half grazing = 1.0h - ndv;
                half rimBorder = smoothstep(0.32h, 0.92h, grazing);
                half lowerBias = saturate(-N.y * 0.55h + 0.45h);
                half borderShade = rimBorder * (0.42h + 0.58h * lowerBias) * rimSep * (1.0h - rg * 0.65h);
                col = lerp(col, shadowTone * 0.66h, borderShade);

                // 6. Subtle brightness & rim glow for balls currently open to the exit
                if (rg > 0.001h)
                {
                    col = lerp(col, max(col * 1.13h, lerp(col, highTone, 0.26h)), rg * 0.58h);
                    half readyRim = smoothstep(0.38h, 0.90h, grazing) * rg;
                    half3 readyRimCol = lerp(highTone, half3(1.0h, 0.99h, 0.94h), 0.38h);
                    col += readyRimCol * (readyRim * 0.26h + rg * 0.045h);
                }

                return half4(saturate(col), 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}

