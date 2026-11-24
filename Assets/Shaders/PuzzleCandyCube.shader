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
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 normalOS   : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
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

                VertexPositionInputs posInputs = GetVertexPositionInputs(animatedOS);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.normalOS   = IN.normalOS;
                OUT.positionOS = animatedOS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 N = normalize(IN.normalWS);
                float3 nOS = normalize(IN.normalOS);

                // Studio key light from upper-left-front (matching Image 2 reference lighting)
                float3 L_key  = normalize(float3(-0.34, 0.68, -0.65));
                // Warm bounce fill light from lower-right-front
                float3 L_fill = normalize(float3(0.42, -0.38, -0.82));
                // Orthographic camera view direction (camera at -Z looking toward +Z, so toward-camera vector is -Z)
                float3 V = normalize(float3(0.0, 0.06, -1.0));

                // 1. Subsurface-scattered half-Lambert wrap diffuse (never turns gray/black!)
                float ndl = dot(N, L_key);
                float wrapDiff = saturate(ndl * 0.54 + 0.46);
                float smoothDiff = smoothstep(0.08, 0.92, wrapDiff);

                float3 baseAlbedo = GET_CANDY_PROP(_BaseColor).rgb;
                float3 shadowTone = GET_CANDY_PROP(_ShadowColor).rgb;
                float3 highTone   = GET_CANDY_PROP(_HighlightColor).rgb;
                float smoothness  = GET_CANDY_PROP(_Smoothness);
                float bevelHigh   = GET_CANDY_PROP(_BevelHighlight);
                float rimSep      = GET_CANDY_PROP(_RimSeparation);

                float3 col = lerp(shadowTone, baseAlbedo, smoothDiff);

                // 2. Soft top-down hemisphere studio gradient (preserves rich saturation)
                float hemi = saturate(N.y * 0.5 + 0.5);
                col = lerp(col * 0.94, lerp(col, highTone, 0.14), hemi);

                // Subtle warm fill bounce on lower-right faces
                float fillTerm = saturate(dot(N, L_fill)) * 0.12;
                col = lerp(col, baseAlbedo * 1.05, fillTerm);

                // Subtle object-space marble swirl & pearl spot on spheres so 3D rolling motion reads crisply
                float isSphere = 1.0 - smoothstep(0.01, 0.08, bevelHigh);
                float swirlCoord = dot(nOS, float3(0.68, 0.56, 0.47)) + 0.18 * sin(nOS.y * 5.2 + nOS.x * 3.1);
                float swirlBand = exp(-pow(swirlCoord / 0.23, 2.0));
                float pearlDot = smoothstep(0.84, 0.95, abs(dot(nOS, normalize(float3(-0.38, 0.78, 0.49)))));
                float marblePattern = saturate(swirlBand * 0.17 + pearlDot * 0.21) * isSphere * (0.38 + 0.62 * wrapDiff);
                col = lerp(col, lerp(highTone, float3(1.0, 0.99, 0.97), 0.38), marblePattern);

                // 3. Soft Tactile Bevel Edge Catchlight (0 for spheres via _BevelHighlight = 0)
                float maxAxis = max(abs(nOS.x), max(abs(nOS.y), abs(nOS.z)));
                float bevelMask = saturate((0.975 - maxAxis) * 2.4) * 0.55;
                float upperBevelLight = bevelMask * saturate(ndl * 0.60 + 0.35) * saturate(N.y + 0.38);
                col = lerp(col, highTone, upperBevelLight * bevelHigh);

                // 4. Lively Satin-Candy Sheen + Delicate Tinted Catchlight (no chalk-white blowout)
                float3 H = normalize(L_key + V);
                float ndh = saturate(dot(N, H));
                float specPower = lerp(18.0, 52.0, smoothness);
                float specBroad = pow(ndh, 10.0) * 0.045;
                float specGloss = pow(ndh, specPower) * (0.09 + 0.06 * bevelMask);

                // Small soft upper-left catchlight tinted with ball highlight hue
                float3 L_catch = normalize(float3(-0.32, 0.58, -0.75));
                float3 H_catch = normalize(L_catch + V);
                float catchDot = saturate(dot(N, H_catch));
                float catchSpot = smoothstep(0.962, 0.994, catchDot) * 0.22 + pow(catchDot, 28.0) * 0.05;

                float3 specColor = lerp(highTone, float3(1.0, 0.99, 0.97), 0.28);
                col += specColor * (specBroad + specGloss + catchSpot);

                // 5. 3D Silhouette & Lower Bevel Cushion AO (keeps adjacent same-color cubes/balls distinctly separated!)
                float ndv = saturate(dot(N, V));
                float grazing = 1.0 - ndv;
                float rimBorder = smoothstep(0.32, 0.92, grazing);
                float lowerBias = saturate(-N.y * 0.55 + 0.45);
                float borderShade = rimBorder * (0.42 + 0.58 * lowerBias) * rimSep;
                col = lerp(col, shadowTone * 0.66, borderShade);

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}

