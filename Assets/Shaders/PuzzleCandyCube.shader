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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
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
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.normalOS   = IN.normalOS;
                OUT.positionOS = IN.positionOS.xyz;
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

                float3 baseAlbedo = _BaseColor.rgb;
                float3 shadowTone = _ShadowColor.rgb;
                float3 highTone   = _HighlightColor.rgb;

                float3 col = lerp(shadowTone, baseAlbedo, smoothDiff);

                // 2. Soft top-down hemisphere studio gradient
                float hemi = saturate(N.y * 0.5 + 0.5);
                col = lerp(col * 0.94, lerp(col, highTone, 0.20), hemi);

                // Subtle warm fill bounce on lower-right faces
                float fillTerm = saturate(dot(N, L_fill)) * 0.14;
                col = lerp(col, baseAlbedo * 1.06, fillTerm);

                // 3. Soft Tactile Bevel Edge Catchlight (reduced on curved spheres so colors stay rich)
                float maxAxis = max(abs(nOS.x), max(abs(nOS.y), abs(nOS.z)));
                float bevelMask = saturate((0.975 - maxAxis) * 2.4) * 0.55;
                float upperBevelLight = bevelMask * saturate(ndl * 0.60 + 0.35) * saturate(N.y + 0.38);
                col = lerp(col, highTone, upperBevelLight * _BevelHighlight);

                // 4. Controlled Satin Toy Specular + Delicate Pin-Point Catchlight (no white blowout)
                float3 H = normalize(L_key + V);
                float ndh = saturate(dot(N, H));
                float specPower = lerp(16.0, 64.0, _Smoothness);
                float specBroad = pow(ndh, 9.0) * 0.07;
                float specGloss = pow(ndh, specPower) * (0.09 + 0.09 * bevelMask);

                // Crisp small catchlight on upper-right without washing out the color
                float3 L_catch = normalize(float3(0.36, 0.50, -0.79));
                float3 H_catch = normalize(L_catch + V);
                float catchDot = saturate(dot(N, H_catch));
                float catchSpot = smoothstep(0.955, 0.992, catchDot) * 0.24 + pow(catchDot, 28.0) * 0.07;

                float3 specColor = lerp(highTone, float3(1.0, 0.99, 0.98), 0.42);
                col += specColor * (specBroad + specGloss + catchSpot);

                // 5. 3D Silhouette & Lower Bevel Cushion AO (keeps adjacent same-color cubes distinctly separated!)
                float ndv = saturate(dot(N, V));
                float grazing = 1.0 - ndv;
                float rimBorder = smoothstep(0.36, 0.95, grazing);
                float lowerBias = saturate(-N.y * 0.55 + 0.45);
                float borderShade = rimBorder * (0.38 + 0.62 * lowerBias) * _RimSeparation;
                col = lerp(col, shadowTone * 0.80, borderShade);

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}

