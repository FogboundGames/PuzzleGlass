// Pistteki hızlı topların arkasındaki kısa hız izi (BallSpeedStreaks). Renk ve alfa köşe renginden gelir;
// u = 0 topun arkası (parlak), u = 1 kuyruk ucu (sönük); v enine yumuşak kenar.
Shader "PuzzleGlass/BallSpeedStreak"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+5"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BallSpeedStreak"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half along = 1.0 - saturate(IN.uv.x);
                along = along * along;
                half across = 1.0 - abs(IN.uv.y * 2.0 - 1.0);
                across = smoothstep(0.0, 0.7, across);
                return half4(IN.color.rgb, IN.color.a * along * across);
            }
            ENDHLSL
        }
    }
}
