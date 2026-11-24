// Önceden dokuya pişirilmiş cam quad'ını gösterir (GlassChamberBakeCache).
// GlassChamberLit'in çıktısı yalnızca UV'ye ve sabit malzeme değerlerine bağlı olduğu için bir kez dokuya çizilir;
// burada her karede sadece tek doku örneği okunur. Karışım modu orijinal malzemeninkiyle aynı tutulur.
Shader "PuzzleGlass/BakedGlassQuad"
{
    Properties
    {
        _MainTex ("Baked Glass", 2D) = "white" {}
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5.0
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-5"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BakedGlassQuad"
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _SrcBlend;
                float _DstBlend;
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
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
            }
            ENDHLSL
        }
    }
}
