Shader "PuzzleGlass/FakeDropShadow"
{
    Properties
    {
        _ShadowColor ("Warm Fake Shadow Color", Color) = (0.76, 0.60, 0.45, 0.36)
        _FeatherWidth ("Edge Feather Softness", Range(0.05, 0.95)) = 0.48
        _IsRadial ("Radial Oval Mode (0=Ribbon, 1=Oval)", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-25"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "FakeDropShadowForward"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
                float _FeatherWidth;
                float _IsRadial;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float alpha;
                if (_IsRadial > 0.5)
                {
                    // Soft oval contact shadow (for Monsters & Shoulder Caps)
                    float2 p = IN.uv * 2.0 - 1.0;
                    float dist = length(p);
                    float falloff = 1.0 - smoothstep(0.10, 1.0, dist);
                    alpha = pow(falloff, 1.45) * _ShadowColor.a;
                }
                else
                {
                    // Contour-hugging ribbon drop shadow (for any ShapedGlassContainer contour!)
                    // uv.x = 0 at left outer shadow border, 0.5 at bottle center, 1 at right outer shadow border
                    float edgeDist = 1.0 - abs(IN.uv.x * 2.0 - 1.0);
                    float horizFade = smoothstep(0.0, _FeatherWidth, edgeDist);

                    // Soft fade at very bottom spout tip so it doesn't hard-clip
                    float bottomTipFade = smoothstep(0.0, 0.04, IN.uv.y);
                    alpha = pow(horizFade, 1.25) * bottomTipFade * _ShadowColor.a;
                }

                if (alpha < 0.002) discard;
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}

