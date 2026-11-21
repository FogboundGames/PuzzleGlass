Shader "PuzzleGlass/GlassChamberLit"
{
    Properties
    {
        _CenterColor ("Center Icy Sky-Blue", Color) = (0.76, 0.91, 1.00, 0.20)
        _EdgeColor ("Thick Wall Sky-Blue", Color) = (0.36, 0.71, 0.98, 0.78)
        _OuterRimColor ("Outer Cobalt Silhouette Rim", Color) = (0.17, 0.49, 0.85, 0.92)
        _InnerGrooveColor ("Inner Cobalt Meniscus Groove", Color) = (0.19, 0.52, 0.87, 0.88)
        _RimGlowColor ("Inner Caustic Cyan-White", Color) = (0.96, 0.99, 1.00, 0.95)
        _WallThickness ("Physical Wall Thickness (World Units)", Range(0.03, 0.16)) = 0.086
        _SheenIntensity ("Glass Specular Intensity", Range(0.0, 1.5)) = 0.85
        _IsFrontLens ("Front Glass Specular Lens Mode", Float) = 0.0
        _IsRoundedBoxPool ("Image 2 Rounded Box Pool Mode", Float) = 1.0
        _BoxAspect ("Box Width/Height Aspect", Float) = 1.88
        _BoxCornerRadius ("Rounded Box Corner Radius", Range(0.04, 0.65)) = 0.22
        _FrameThickness ("Rounded Box Frame Thickness", Range(0.04, 0.24)) = 0.115
        _FrameColor ("Outer Frame Periwinkle", Color) = (0.50, 0.50, 0.90, 1.0)
        _FrameHighlight ("Frame Top Highlight", Color) = (0.84, 0.84, 0.98, 1.0)
        _FrameEdgeShade ("Frame Outer Lip Shade", Color) = (0.38, 0.36, 0.80, 1.0)
        _FakeShadowColor ("Drop Shadow Color", Color) = (0.72, 0.60, 0.48, 0.30)
        _PolyCount ("Custom Pool Polygon Vertex Count (0 = rounded box)", Float) = 0
        _PolyRadius ("Custom Pool Inner Corner Radius", Float) = 0.1
        _PolyBounds ("Custom Pool Inner Bounds (minX, minY, maxX, maxY)", Vector) = (-1, -1, 1, 1)
        _JarNeckParams ("Optional Jar Neck (halfW, height, lipOverhang, lipHalfH)", Vector) = (0, 0, 0, 0)
        _CenterGlassAlpha ("Back Chamber Center Alpha", Range(0.0, 1.0)) = 0.20
        _IsCollar ("Periwinkle 3D Collar Mode", Float) = 0.0
        _IsFeedTube ("Curved Crystal Feed Tube Mode", Float) = 0.0
        _FeedTubeRatio ("Feed Tube Wall Width Ratio", Range(0.25, 1.0)) = 0.64
        _FeedTubeOpeningL ("Left Feed Tube Opening (on, pX, pHalfW, pTopY)", Vector) = (0, 0, 0, 0)
        _FeedTubeOpeningR ("Right Feed Tube Opening (on, pX, pHalfW, pTopY)", Vector) = (0, 0, 0, 0)
        _IsTrackChannel ("Curved Glass Track Mode", Float) = 0.0
        _TrackSegCount ("Track Segment Count", Float) = 0.0
        _TrackHalfWidth ("Track Inner Half Width (P-Space)", Float) = 0.25
        _TrackWallThick ("Track Wall Thickness (P-Space)", Float) = 0.06
        _TrackFillet ("Track Junction Fillet", Float) = 0.04
        _TrackExitPos ("Track Exit (pX, pY, mouthHalfW, on)", Vector) = (0, -0.5, 0.16, 0)
        _TrackBake ("Bake Track SDF Into Render Texture", Float) = 0.0
        _TrackBoxMode ("Tube-Style Square Glass (box SDF instead of track)", Float) = 0.0
        _TrackBox ("Tube Box Inner (cx, cy, halfW, halfH) P-Space", Vector) = (0, 0, 1, 1)
        _TrackBoxRad ("Tube Box Inner Corner Radius", Float) = 0.15
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5.0
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10.0
        _TrackNeck ("Track Exit Neck (mouthY, innerHalfW, lipHalfH, on)", Vector) = (0, 0, 0, 0)
        _SleeveParams ("Exit Sleeve (halfW, halfH, neckOuterHalfW, unused)", Vector) = (0.5, 0.2, 0.3, 0)
        _ExitChannelCount ("Exit Channel Count", Float) = 0.0
        _ExitChannelHalfW ("Exit Channel Inner Half Width (P-Space)", Float) = 0.04
        _ExitChannelWallThick ("Exit Channel Wall Thickness (P-Space)", Float) = 0.03
        _IsExitChannels ("Thin Per-Slot Exit Channels Pass", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-5"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GlassChamberForward"
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CenterColor;
                float4 _EdgeColor;
                float4 _OuterRimColor;
                float4 _InnerGrooveColor;
                float4 _RimGlowColor;
                float _WallThickness;
                float _SheenIntensity;
                float _IsFrontLens;
                float _IsRoundedBoxPool;
                float _BoxAspect;
                float _BoxCornerRadius;
                float _FrameThickness;
                float4 _FrameColor;
                float4 _FrameHighlight;
                float4 _FrameEdgeShade;
                float4 _FakeShadowColor;
                float _PolyCount;
                float _PolyRadius;
                float4 _PolyBounds;
                float4 _JarNeckParams;
                float _CenterGlassAlpha;
                float _IsCollar;
                float _IsFeedTube;
                float _FeedTubeRatio;
                float4 _FeedTubeOpeningL;
                float4 _FeedTubeOpeningR;
                float _IsTrackChannel;
                float _TrackSegCount;
                float _TrackHalfWidth;
                float _TrackWallThick;
                float _TrackFillet;
                float4 _TrackExitPos;
                float _TrackBake;
                float _TrackBoxMode;
                float4 _TrackBox;
                float _TrackBoxRad;
                float4 _TrackNeck;
                float4 _SleeveParams;
                float _ExitChannelCount;
                float _ExitChannelHalfW;
                float _ExitChannelWallThick;
                float _IsExitChannels;
            CBUFFER_END

            // Özel şekilli havuz: iç hattın köşeleri (yarıçap kadar içeri alınmış), iki köşe bir float4'te.
            // Kutu modunda kullanılmaz (_PolyCount = 0).
            float4 _PolyPoints[48];
            // Eğrisel cam pist/kanal segmentleri: (ax, ay, bx, by) P-uzayında, max 64 segment.
            float4 _TrackSegs[64];
            // Her slota uzanan ince cam çıkış kanalları: en fazla 6 kanal * 16 segment = 96 segment, P-uzayında.
            float4 _ExitChanSegs[96];
            // Her çıkış kanalının alt ağzı: (mouthX, mouthY, lipHalfH, active) P-uzayında.
            float4 _ExitMouths[6];
            // Çıkış kanalları pişirilmiş mesafe dokusu:
            TEXTURE2D(_ExitChanSDFTex);
            SAMPLER(sampler_ExitChanSDFTex);
            float _UseExitChanSDF;
            // Pist mesafe alanı, level kurulurken bir kez bu dokuya pişirilir (ShapedGlassContainer.BakeTrackSdf):
            // r = kanal iç mesafesi, gb = yumuşak dış normal. Her pikselde 64 segmentlik döngü yerine tek örnek okunur
            // (mobil GPU'da asıl darboğaz buydu). Global: malzemelere serileşmez; 0 ise eski döngüye düşülür.
            TEXTURE2D(_TrackSDFTex);
            SAMPLER(sampler_TrackSDFTex);
            float _UseTrackSDF;
            // Her segmentin kol kimliği (4 segment bir float4 içinde): aynı kol exact min, farklı kollar SmoothUnion.
            float4 _TrackBranchPacked[16];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.uv2 = IN.uv2;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS = IN.positionOS.xyz;
                return OUT;
            }

            float sdRoundBox2D(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float2 PolyVertex(int i)
            {
                float4 v = _PolyPoints[i >> 1];
                return (i & 1) == 0 ? v.xy : v.zw;
            }

            // İşaretli çokgen mesafesi (içeride negatif)
            float sdPolygon(float2 p)
            {
                int n = (int)_PolyCount;
                float2 v0 = PolyVertex(0);
                float d = dot(p - v0, p - v0);
                float s = 1.0;
                [loop]
                for (int i = 0; i < 96; i++)
                {
                    if (i >= n) break;
                    int j = (i == 0) ? (n - 1) : (i - 1);
                    float2 vi = PolyVertex(i);
                    float2 vj = PolyVertex(j);
                    float2 e = vj - vi;
                    float2 w = p - vi;
                    float2 b = w - e * clamp(dot(w, e) / max(dot(e, e), 1e-6), 0.0, 1.0);
                    d = min(d, dot(b, b));
                    bool3 c = bool3(p.y >= vi.y, p.y < vj.y, e.x * w.y > e.y * w.x);
                    if (all(c) || all(!c)) s *= -1.0;
                }
                return s * sqrt(d);
            }

            float opSmoothUnion(float d1, float d2, float k)
            {
                float h = clamp(0.5 + 0.5 * (d2 - d1) / max(k, 1e-4), 0.0, 1.0);
                return lerp(d2, d1, h) - k * h * (1.0 - h);
            }

            // Havuzun iç (cam) ve dış (çerçeve) hatları: kutu ya da özel şekil (+ isteğe bağlı kavanoz boynu)
            float ShapeInnerBase(float2 p, float2 innerHalf, float innerRad, float2 bodyOffset)
            {
                if (_PolyCount > 2.5) return sdPolygon(p) - _PolyRadius;
                return sdRoundBox2D(p - bodyOffset, innerHalf, innerRad);
            }

            float sdEllipseApprox(float2 p, float2 ab)
            {
                ab = max(ab, float2(0.01, 0.01));
                float2 q = p / ab;
                float lq = length(q);
                float g = max(length(q / ab), 1e-4);
                // iq yaklaşımı: k0 * (k0 - 1) / k1. (k0 çarpanı eksikti: mesafe uzakta sabite takılıyor,
                // ekranın her yeri kavanozun "içi" sayılıp beyaza boyanıyordu.)
                return lq * (lq - 1.0) / g;
            }

            float ShapeInner(float2 p, float2 innerHalf, float innerRad, float2 bodyOffset)
            {
                float dBase = ShapeInnerBase(p, innerHalf, innerRad, bodyOffset);
                if (_JarNeckParams.y <= 0.005) return dBase;

                float neckHalfW = _JarNeckParams.x;
                float neckH = _JarNeckParams.y;
                float lipOver = _JarNeckParams.z;
                float bodyTopY = bodyOffset.y + innerHalf.y;

                // Seamless S-curve shoulder into cylindrical jar neck (no flat seam dividing shoulder & neck)
                float neckCenterY = bodyTopY + neckH * 0.30;
                float dNeck = sdRoundBox2D(p - float2(0.0, neckCenterY), float2(neckHalfW, neckH * 0.56), min(neckHalfW * 0.28, 0.05));
                float dCombined = opSmoothUnion(dBase, dNeck, 0.135);

                // Smooth oval jar top lip profile
                float lipCenterY = bodyTopY + neckH - 0.015;
                // Ağız: yuvarlak uçlu çubuk (tam mesafe). İnce elips yaklaşımı mesafeyi kısa hesaplayıp
                // gölgede omuzlardan yanlara uzanan bulanık çizgiler bırakıyordu.
                float dLip = sdRoundBox2D(p - float2(0.0, lipCenterY), float2(neckHalfW + lipOver, 0.036), 0.036);
                return opSmoothUnion(dCombined, dLip, 0.035);
            }

            float ShapeOuter(float2 p, float2 innerHalf, float innerRad, float frameThick, float2 bodyOffset)
            {
                return ShapeInner(p, innerHalf, innerRad, bodyOffset) - frameThick;
            }

            half4 EvaluateCollar3D(Varyings IN)
            {
                float aspect = max(_BoxAspect, 0.5);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;

                float edgeFade = smoothstep(0.0, 0.08, IN.uv.x) * smoothstep(1.0, 0.92, IN.uv.x)
                               * smoothstep(0.0, 0.08, IN.uv.y) * smoothstep(1.0, 0.92, IN.uv.y);

                // Şekillendirilmiş 2 kademeli 3B cam çıkış yakası:
                // 1) Üstte cam tabanı kucaklayan genişçe kavisli kelepçe halkası (flange)
                // 2) Altta hafifçe daralan yuvarlatılmış çıkış ağzı gövdesi
                float ringHalfW = aspect * 0.76;
                float nxNorm = clamp(p.x / max(ringHalfW, 0.05), -1.0, 1.0);
                float topCurve = 0.048 * (nxNorm * nxNorm); // camın alt eğrisine uyumlu yumuşak yukarı kavis

                float2 ringCenter   = float2(0.0, 0.36 + topCurve * 0.55);
                float2 ringHalf     = float2(ringHalfW, 0.21);
                float2 barrelCenter = float2(0.0, 0.02);
                float2 barrelHalf   = float2(aspect * 0.68, 0.38);

                float dRing   = sdRoundBox2D(p - ringCenter, ringHalf, 0.145);
                float dBarrel = sdRoundBox2D(p - barrelCenter, barrelHalf, 0.215);
                float dCap    = opSmoothUnion(dRing, dBarrel, 0.055);
                float dShad   = opSmoothUnion(
                    sdRoundBox2D(p - (ringCenter + float2(0.0, -0.075)), ringHalf * float2(1.03, 1.04), 0.16),
                    sdRoundBox2D(p - (barrelCenter + float2(0.0, -0.095)), barrelHalf * float2(1.05, 1.05), 0.23),
                    0.06
                );

                float aa = 0.012;
                float capMask = 1.0 - smoothstep(-aa, aa, dCap);
                float shadAlpha = pow(1.0 - smoothstep(-0.02, 0.24, dShad), 1.65) * 0.36 * edgeFade;

                float nx = clamp(p.x / max(barrelHalf.x, 0.05), -1.0, 1.0);
                float ny = clamp((p.y + 0.36) / 0.94, 0.0, 1.0); // 0=alt dudak, 1=üst halka tepesi

                // Soğuk lavanta-safir kristal gövde + temiz parlak beyaz yansıma paleti
                float3 pureHi    = float3(0.97, 0.98, 1.00);
                float3 lightLav  = float3(0.76, 0.77, 0.99);
                float3 baseCol   = float3(0.57, 0.56, 0.94);
                float3 shadeCol  = float3(0.35, 0.33, 0.77);
                float3 grooveCol = float3(0.24, 0.22, 0.60);

                // 3B silindirik yanal ışık & speküler parlama şeridi
                float leftSpec = exp(-pow((nx + 0.38) / 0.24, 2.0));
                float rightRim = exp(-pow((nx - 0.80) / 0.17, 2.0)) * 0.52;
                float cylLit   = saturate(0.52 + leftSpec * 0.62 + rightRim - smoothstep(0.14, 0.95, nx) * 0.42);
                float vertLit  = saturate(0.46 + ny * 0.48);

                float3 collarCol = lerp(shadeCol, baseCol, saturate(cylLit * vertLit * 1.18));
                collarCol = lerp(collarCol, lightLav, saturate(leftSpec * 0.48));
                collarCol = lerp(collarCol, pureHi, saturate(leftSpec * 0.62 * smoothstep(0.15, 0.85, ny)));

                // Üst halka ile alt gövde arasındaki temiz 3B yaka oluğu (seam) ve halka üstü pahı
                float grooveY = 0.58 + topCurve * 0.35;
                float cuffGroove   = exp(-pow((ny - grooveY) / 0.042, 2.0));
                float cuffOverhang = exp(-pow((ny - (grooveY + 0.065)) / 0.048, 2.0));
                collarCol = lerp(collarCol, grooveCol, cuffGroove * 0.68);
                collarCol = lerp(collarCol, lightLav, cuffOverhang * (0.45 + 0.35 * cylLit));

                // Alt çıkış ağzında derinlik hissi veren zarif içbükey oluk & alt parlak kenar dudağı
                float chuteX = 1.0 - smoothstep(0.30, 0.68, abs(nx));
                float chuteY = (1.0 - smoothstep(0.06, 0.42, ny)) * smoothstep(0.02, 0.12, ny);
                collarCol = lerp(collarCol, grooveCol * 0.92, chuteX * chuteY * 0.32);

                float topLipRim = smoothstep(0.84, 0.98, ny);
                collarCol = lerp(collarCol, pureHi, topLipRim * 0.72);
                float botBevel  = exp(-pow((ny - 0.06) / 0.045, 2.0)) * 0.42 + smoothstep(0.04, 0.0, ny) * 0.35;
                collarCol = lerp(collarCol, shadeCol * 0.72, saturate(botBevel));

                // İnce buz-beyaz dış kontur parlaması (cam hissiyatını yakayla bütünleştirir)
                float rimAA = exp(-pow((-dCap - 0.012) / 0.013, 2.0));
                collarCol = lerp(collarCol, pureHi, rimAA * saturate(0.32 + ny * 0.52));

                float3 shadRgb = float3(0.12, 0.14, 0.30);
                float capSolid = capMask * 0.985;
                float outAlpha = saturate(capSolid + shadAlpha * (1.0 - capSolid));
                if (outAlpha < 0.002) discard;

                float3 premul = collarCol * capSolid + shadRgb * shadAlpha * (1.0 - capSolid);
                float3 outRgb = premul / max(outAlpha, 0.004);
                return half4(saturate(outRgb), outAlpha);
            }

            // Pist çıkış boynunu saran yarı saydam lavanta bilezik (önden bakış, dikey eksenli kısa silindir).
            // Arka katman (_IsFrontLens = 0): bileziğin boynun arkasında kalan yarısı + zemine düşen yumuşak gölge.
            // Ön katman (_IsFrontLens = 1): topların ve boynun önündeki yarı saydam yüz, kenar parlamaları, koyu alt kenar.
            half4 EvaluateExitSleeve(Varyings IN)
            {
                float aspect = max(_BoxAspect, 0.25);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;
                float edgeFade = smoothstep(0.0, 0.06, IN.uv.x) * smoothstep(1.0, 0.94, IN.uv.x)
                               * smoothstep(0.0, 0.06, IN.uv.y) * smoothstep(1.0, 0.94, IN.uv.y);

                float sx = max(_SleeveParams.x, 0.05);
                float sy = max(_SleeveParams.y, 0.03);
                float neckOut = clamp(_SleeveParams.z, 0.02, sx - 0.01);
                float rad = min(sy * 0.85, sx * 0.5);
                float aa = max(fwidth(p.x) * 1.2, 0.002);

                float dS = sdRoundBox2D(p, float2(sx, sy), rad);
                float mask = 1.0 - smoothstep(-aa, aa, dS);

                // Silindirik kesit: nx = -1 sol kenar, +1 sağ kenar; kenarlarda daha çok malzeme (Fresnel)
                float nx = clamp(p.x / sx, -1.0, 1.0);
                float nz = sqrt(saturate(1.0 - nx * nx));
                float fres = pow(1.0 - nz, 1.6);
                float vy = saturate((p.y + sy) / (2.0 * sy));   // 0: alt kenar, 1: üst kenar
                float overNeck = 1.0 - smoothstep(neckOut - aa, neckOut + aa, abs(p.x));

                // Cama yakın, hafif lavanta palet (doğrusal uzay)
                float3 hiCol    = float3(1.00, 1.00, 1.00);
                float3 lavLight = float3(0.70, 0.68, 0.99);
                float3 lavMid   = float3(0.46, 0.42, 0.90);
                float3 lavDeep  = float3(0.22, 0.22, 0.58);

                if (_IsFrontLens > 0.5)
                {
                    // Net (keskin kenarlı) bantlar: bulanık parlama yok
                    float px = aa * 1.5;
                    float topLine = smoothstep(0.78 - px, 0.78 + px, vy) * (1.0 - smoothstep(0.90 - px, 0.90 + px, vy));
                    float botEdge = 1.0 - smoothstep(0.16 - px, 0.16 + px, vy);
                    float outline = smoothstep(-aa * 3.0, -aa * 1.2, dS);         // ince koyu dış kontur
                    float shadeR  = smoothstep(0.35, 0.95, nx);                    // sağ (gölge) yan

                    float3 col = lerp(lavLight, lavMid, saturate(0.25 + shadeR * 0.6));
                    col = lerp(col, lerp(lavLight, hiCol, 0.65), topLine);
                    col = lerp(col, lavDeep, botEdge * 0.75);
                    col = lerp(col, lavDeep * 0.8, outline * 0.85);

                    // Dolgun, net lavanta bant; boynun önünde biraz daha şeffaf ki geçen toplar seçilsin
                    float a = lerp(0.88, 0.62, overNeck) + fres * 0.12 + topLine * 0.10 + botEdge * 0.10 + outline * 0.2;
                    a = saturate(a) * mask * edgeFade;
                    if (a < 0.003) discard;
                    return half4(saturate(col), a);
                }
                else
                {
                    // Arka yarı: koyu lavanta iç yüz (yalnızca boynun yanlarından görünür) + yumuşak temas gölgesi
                    float3 col = lerp(lavDeep, lavMid, saturate(0.30 + vy * 0.45));
                    float a = mask * 0.80;
                    float dSh = sdRoundBox2D(p - float2(0.0, -sy * 0.35), float2(sx * 1.02, sy * 1.05), rad);
                    float shA = pow(1.0 - smoothstep(-0.01, sy * 1.2, dSh), 1.6) * 0.30;
                    float3 shCol = float3(0.12, 0.14, 0.30);
                    float outA = saturate(a + shA * (1.0 - a)) * edgeFade;
                    if (outA < 0.003) discard;
                    float3 rgb = (col * a + shCol * shA * (1.0 - a)) / max(a + shA * (1.0 - a), 0.004);
                    return half4(saturate(rgb), outA);
                }
            }

            half4 EvaluateRoundedBoxPool(Varyings IN)
            {
                bool isPoly = _PolyCount > 2.5;
                bool hasNeck = _JarNeckParams.y > 0.005;
                float aspect = max(_BoxAspect, 0.25);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;

                // Smooth edge fade prevents any shadow or glow from ever clipping at quad bounds
                float quadEdgeFade = smoothstep(0.0, 0.05, IN.uv.x) * smoothstep(1.0, 0.95, IN.uv.x)
                                   * smoothstep(0.0, 0.05, IN.uv.y) * smoothstep(1.0, 0.95, IN.uv.y);

                float neckShift = hasNeck ? _JarNeckParams.y * 0.5 : 0.0;
                float2 bodyOffset = float2(0.0, -neckShift);

                // Reserve generous 0.20 / 0.24 quad margins so drop shadow fades completely inside the quad!
                float2 outerHalf = float2(aspect * 0.80, 0.76 - neckShift);
                float maxSafeRad = min(outerHalf.x, outerHalf.y) * 0.95;
                float outerRad   = clamp(_BoxCornerRadius, 0.04, maxSafeRad);

                float frameThick = clamp(_FrameThickness, 0.035, min(outerHalf.x, outerHalf.y) * 0.38);
                float2 innerHalf = outerHalf - float2(frameThick, frameThick);
                float innerRad   = clamp(outerRad - frameThick * 0.65, 0.03, min(innerHalf.x, innerHalf.y) * 0.92);

                if (isPoly)
                {
                    innerHalf = float2(max(abs(_PolyBounds.x), abs(_PolyBounds.z)), (_PolyBounds.w - _PolyBounds.y) * 0.5);
                    outerHalf = innerHalf + frameThick;
                    bodyOffset = float2(0.0, 0.0);
                }

                float dOuter = ShapeOuter(p, innerHalf, innerRad, frameThick, bodyOffset);
                float dInner = ShapeInner(p, innerHalf, innerRad, bodyOffset);

                // 1. Soft Unclipped Ambient Floor Shadow (down-right studio angle matching reference)
                float shadowAlpha = 0.0;
                float3 shadowRGB = _FakeShadowColor.rgb;
                if (_IsFrontLens < 0.5)
                {
                    float2 pShadFar  = p - float2(0.022, -0.062);
                    float2 pShadNear = p - float2(0.010, -0.028);
                    float dShadFar   = ShapeOuter(pShadFar, innerHalf, innerRad, frameThick + 0.018, bodyOffset);
                    float dShadNear  = ShapeOuter(pShadNear, innerHalf, innerRad, frameThick, bodyOffset);
                    float aFar  = (1.0 - smoothstep(-0.04, 0.165, dShadFar)) * _FakeShadowColor.a;
                    float aNear = (1.0 - smoothstep(-0.02, 0.075, dShadNear)) * (_FakeShadowColor.a * 0.65);
                    shadowAlpha = saturate(aFar + aNear) * quadEdgeFade;
                }

                float outerMask = 1.0 - smoothstep(-0.008, 0.008, dOuter);
                float dFromOuter = max(0.0, -dOuter);

                // Cleanly open top border right where curved feed tubes attach: fully open between the tube's inner
                // walls, the short fade sits under the tube's own wall so no blurry band shows
                float topClipMask = 1.0;
                if (_FeedTubeOpeningL.x > 0.5)
                {
                    float dxL = abs(p.x - _FeedTubeOpeningL.y);
                    float yZoneL = smoothstep(_FeedTubeOpeningL.w - frameThick * 0.85, _FeedTubeOpeningL.w + 0.015, p.y);
                    float gapL = 1.0 - smoothstep(_FeedTubeOpeningL.z * 0.97, _FeedTubeOpeningL.z * 1.03, dxL);
                    topClipMask *= (1.0 - gapL * yZoneL);
                }
                if (_FeedTubeOpeningR.x > 0.5)
                {
                    float dxR = abs(p.x - _FeedTubeOpeningR.y);
                    float yZoneR = smoothstep(_FeedTubeOpeningR.w - frameThick * 0.85, _FeedTubeOpeningR.w + 0.015, p.y);
                    float gapR = 1.0 - smoothstep(_FeedTubeOpeningR.z * 0.97, _FeedTubeOpeningR.z * 1.03, dxR);
                    topClipMask *= (1.0 - gapR * yZoneR);
                }
                outerMask *= topClipMask;

                // Single smooth crystal-clear glass rim (no double-walled / striped outlines!)
                float3 rimPeriwinkle = lerp(_OuterRimColor.rgb, float3(0.46, 0.48, 0.88), 0.62);
                float3 wallIceBlue   = lerp(_EdgeColor.rgb, float3(0.78, 0.82, 0.99), 0.65);
                float3 pureWhite     = float3(1.00, 1.00, 1.00);

                float2 gradOut = normalize(float2(
                    ShapeOuter(p + float2(0.005, 0.0), innerHalf, innerRad, frameThick, bodyOffset) - ShapeOuter(p - float2(0.005, 0.0), innerHalf, innerRad, frameThick, bodyOffset),
                    ShapeOuter(p + float2(0.0, 0.005), innerHalf, innerRad, frameThick, bodyOffset) - ShapeOuter(p - float2(0.0, 0.005), innerHalf, innerRad, frameThick, bodyOffset)
                ) + 1e-5);

                float rimSpan = clamp(frameThick * 0.95, 0.045, 0.072);
                float tWall = dFromOuter / rimSpan;

                float outerEdgeRim      = exp(-pow((tWall - 0.10) / 0.12, 2.0));
                float crispWhiteRim     = exp(-pow((tWall - 0.42) / 0.28, 2.0));
                float wallThicknessFade = exp(-dFromOuter / 0.064);

                // Studio directional lighting along the single smooth glass bevel:
                // Bright glossy white at top-left corner, left/right side walls, and bottom curve
                float topLeftFocus  = saturate(-gradOut.x * 0.68 + gradOut.y * 0.72);
                float topRightFocus = saturate(gradOut.x * 0.55 + gradOut.y * 0.65);
                float sideFocus     = saturate(abs(gradOut.x) * 0.90);
                float bottomFocus   = saturate(-gradOut.y * 0.92);
                float rimSpecBoost  = saturate(0.42 + topLeftFocus * 0.68 + topRightFocus * 0.28 + sideFocus * 0.34 + bottomFocus * 0.38);

                float3 rimGlassCol = lerp(wallIceBlue, rimPeriwinkle, outerEdgeRim * 0.82);
                rimGlassCol = lerp(rimGlassCol, pureWhite, saturate(crispWhiteRim * rimSpecBoost));

                // 2. Soft Back-Only Studio Sheen & Very Subtle Outer Bottom Lip
                float2 pLocal = p - bodyOffset;
                float2 normIn = pLocal / max(innerHalf, float2(0.01, 0.01));

                float dBotFromOuter = pLocal.y - (-outerHalf.y);
                float hMaskFloor = isPoly
                    ? smoothstep(0.02, 0.16, -ShapeInnerBase(p + float2(0.0, 0.14), innerHalf, innerRad, bodyOffset))
                    : 1.0 - smoothstep(outerHalf.x - 0.14, outerHalf.x - 0.01, abs(pLocal.x));
                // Strictly confined to the very outer bottom wall thickness so bottom balls stay 100% crisp
                float botBaseGlass = exp(-pow((dBotFromOuter - 0.018) / 0.014, 2.0)) * hMaskFloor;

                // 3. True 3D Open Elliptical Glass Jar Mouth & Neck Ring (when hasNeck is active)
                float neckFrontLip = 0.0;
                float neckBackLip  = 0.0;
                float neckCollarRing = 0.0;
                float neckCavity   = 0.0;
                if (hasNeck)
                {
                    float bodyTopOuterY = bodyOffset.y + outerHalf.y;
                    float lipCenterY = bodyTopOuterY + _JarNeckParams.y - 0.036;
                    float rx = _JarNeckParams.x + _JarNeckParams.z + frameThick * 0.65;
                    float ry = 0.042;
                    float dLipOuter = sdEllipseApprox(p - float2(0.0, lipCenterY), float2(rx, ry));
                    float dLipInner = sdEllipseApprox(p - float2(0.0, lipCenterY), float2(rx * 0.80, ry * 0.56));

                    float inOuterEllipse = 1.0 - smoothstep(-0.008, 0.008, dLipOuter);
                    float inCavity = 1.0 - smoothstep(-0.008, 0.008, dLipInner);
                    float onLipRing = saturate(inOuterEllipse - inCavity);

                    // Front lower ellipse arc sits in front of the opening; back upper arc closes the 3D ellipse behind!
                    float frontHalfMask = smoothstep(0.012, -0.010, p.y - lipCenterY);
                    neckFrontLip = onLipRing * frontHalfMask;
                    neckBackLip  = onLipRing * (1.0 - frontHalfMask);
                    neckCavity   = inCavity;

                    // Secondary horizontal curved glass neck collar groove right below the oval lip
                    float inNeckX = 1.0 - smoothstep(rx * 0.88, rx * 1.06, abs(p.x));
                    float xN = clamp(p.x / max(rx, 0.05), -1.0, 1.0);
                    float grooveCurveY = (lipCenterY - 0.056) - 0.014 * (1.0 - xN * xN);
                    float grooveLine = exp(-pow((p.y - grooveCurveY) / 0.010, 2.0)) * inNeckX;
                    float ridgeGlow  = exp(-pow((p.y - (grooveCurveY + 0.016)) / 0.012, 2.0)) * inNeckX;
                    neckCollarRing = saturate(grooveLine * 0.55 + ridgeGlow * 0.78);

                    // Erase harsh flat glass wall inside top oval cavity so the jar actually looks open at top!
                    outerMask *= saturate(1.0 - neckCavity * smoothstep(lipCenterY - 0.012, lipCenterY + 0.008, p.y));
                }

                if (_IsFrontLens > 0.5)
                {
                    // FRONT LAYER (in front of 3D balls & tray):
                    // Keeps the interior 100% crystal-clear over all balls and blocks;
                    // renders only the single smooth outer glass rim bevel and front jar lip!
                    float wallRimAlpha = saturate(outerEdgeRim * 0.68 + crispWhiteRim * 0.84 * rimSpecBoost + wallThicknessFade * 0.16) * outerMask;
                    float baseAlpha    = botBaseGlass * 0.28 * outerMask;

                    float3 frontRgb = lerp(rimGlassCol, pureWhite, saturate(crispWhiteRim * rimSpecBoost * 0.85));
                    frontRgb = lerp(frontRgb, rimPeriwinkle, saturate(botBaseGlass * 0.30));

                    float frontAlpha = saturate(max(wallRimAlpha, baseAlpha));

                    if (hasNeck)
                    {
                        float3 neckCol = lerp(rimPeriwinkle, pureWhite, 0.80);
                        frontRgb = lerp(frontRgb, neckCol, saturate(neckFrontLip + neckCollarRing * 0.75));
                        frontAlpha = max(frontAlpha, saturate(neckFrontLip * 0.90 + neckCollarRing * 0.62));
                    }

                    frontAlpha *= quadEdgeFade;
                    if (frontAlpha < 0.002) discard;
                    return half4(saturate(frontRgb), frontAlpha);
                }
                else
                {
                    // BACK GLASS CHAMBER (behind 3D balls & tray):
                    // Clean translucent interior + soft studio sheen + smooth drop shadow
                    float vGrad = saturate(normIn.y * 0.5 + 0.5);
                    float3 topCol = lerp(float3(0.95, 0.96, 1.00), wallIceBlue, 0.25);
                    float3 midCol = float3(0.98, 0.98, 1.00);
                    float3 botCol = lerp(float3(0.93, 0.94, 1.00), rimPeriwinkle, 0.18);

                    float diagSheen = saturate(-normIn.x * 0.45 + normIn.y * 0.55);
                    float backCornerGlow = smoothstep(0.15, 0.95, diagSheen) * 0.32 * _SheenIntensity;

                    float3 chamberRgb = lerp(midCol, topCol, smoothstep(0.45, 0.98, vGrad));
                    chamberRgb = lerp(botCol, chamberRgb, smoothstep(0.0, 0.32, vGrad));
                    chamberRgb = lerp(chamberRgb, wallIceBlue, wallThicknessFade * 0.42);
                    chamberRgb = lerp(chamberRgb, pureWhite, backCornerGlow);
                    chamberRgb = lerp(chamberRgb, rimGlassCol, saturate(outerEdgeRim * 0.72 + crispWhiteRim * 0.86 * rimSpecBoost));

                    float centerA = clamp(_CenterGlassAlpha, 0.0, 0.35);
                    float chamberAlpha = saturate(max(centerA + wallThicknessFade * 0.34, outerEdgeRim * 0.70 + crispWhiteRim * 0.52)) * outerMask;

                    if (hasNeck)
                    {
                        float3 backRimCol = lerp(rimPeriwinkle, pureWhite, 0.68);
                        float3 throatCol  = lerp(rimPeriwinkle, float3(0.70, 0.72, 0.98), 0.50);
                        chamberRgb = lerp(chamberRgb, throatCol, neckCavity * 0.40);
                        chamberRgb = lerp(chamberRgb, backRimCol, neckBackLip * 0.88);
                        chamberAlpha = max(chamberAlpha, saturate(neckBackLip * 0.84 + neckCavity * 0.22));
                    }

                    chamberAlpha *= quadEdgeFade;
                    float finalAlpha = saturate(chamberAlpha + shadowAlpha * (1.0 - chamberAlpha * 0.92));
                    if (finalAlpha < 0.002) discard;

                    float3 finalRgb = lerp(shadowRGB * shadowAlpha, chamberRgb, chamberAlpha) / max(finalAlpha, 0.004);
                    if (chamberAlpha < 0.02) finalRgb = shadowRGB;
                    return half4(saturate(finalRgb), finalAlpha);
                }
            }

            // Evaluates the 1-to-1 optical blown-glass wall profile and interior reflections
            // matching the reference image (outer cobalt rim -> bright icy white core ->
            // sky-blue refractive wall -> inner cobalt meniscus groove -> inner white rim ->
            // broad glossy lobe crescent highlight & cylindrical neck/spout streaks).
            void EvaluateBlownGlassWall(
                float2 uv,
                float dPerp,
                float wallThick,
                float xNorm,
                out float3 wallRgb,
                out float wallAlpha,
                out float wallZoneMask)
            {
                // Normalized distance across the physical glass wall:
                // tWall = 0.0 at outer silhouette edge, tWall = 1.0 at inner meniscus edge
                float tWall = dPerp / max(wallThick, 0.02);

                // Anti-aliased outer silhouette edge feather
                float outerAA = smoothstep(0.0, 0.035, tWall);

                // Band 1: Crisp Outer Cobalt-Sapphire Silhouette Edge (tWall ~ 0.02..0.16)
                float outerCobaltBand = exp(-pow((tWall - 0.07) / 0.075, 2.0));

                // Band 2: Bright Icy-White Outer Rim Highlight Core (tWall ~ 0.14..0.44)
                float outerWhiteCore = exp(-pow((tWall - 0.27) / 0.125, 2.0));

                // Band 3: Translucent Sky-Blue Refractive Wall Mid-Body (tWall ~ 0.36..0.74)
                float midSkyBody = smoothstep(0.18, 0.42, tWall) * (1.0 - smoothstep(0.68, 0.92, tWall));

                // Band 4: Rich Cobalt-Azure Inner Meniscus Groove (tWall ~ 0.68..0.89)
                float innerCobaltGroove = exp(-pow((tWall - 0.78) / 0.095, 2.0));

                // Band 5: Crisp Inner White-Cyan Meniscus Highlight (tWall ~ 0.86..1.06)
                float innerWhiteRim = exp(-pow((tWall - 0.96) / 0.085, 2.0));

                // Combine colors across the physical wall (0 <= tWall <= 1.08)
                float3 col = _EdgeColor.rgb;
                col = lerp(col, _OuterRimColor.rgb, saturate(outerCobaltBand * 0.95));
                col = lerp(col, _RimGlowColor.rgb, saturate(outerWhiteCore * 0.96));
                col = lerp(col, _EdgeColor.rgb, saturate(midSkyBody * 0.55));
                col = lerp(col, _InnerGrooveColor.rgb, saturate(innerCobaltGroove * 0.92));
                col = lerp(col, _RimGlowColor.rgb, saturate(innerWhiteRim * 0.95));

                // Wall opacity profile across tWall in [0, 1.08]
                float baseWallMask = 1.0 - smoothstep(0.96, 1.10, tWall);
                float bandAlpha = saturate(
                    baseWallMask * _EdgeColor.a +
                    outerCobaltBand * 0.35 +
                    outerWhiteCore * 0.38 +
                    innerCobaltGroove * 0.28 +
                    innerWhiteRim * 0.36);

                wallRgb = saturate(col);
                wallAlpha = saturate(bandAlpha * outerAA);
                wallZoneMask = baseWallMask * outerAA;
            }

            half4 EvaluateFeedTube(Varyings IN)
            {
                float ratio = clamp(_FeedTubeRatio, 0.25, 1.0);
                // xNorm = -1..+1 spans the actual crystal tube diameter; |xNorm| > 1 is outer shadow padding
                float xNorm = (IN.uv.x * 2.0 - 1.0) / ratio;
                float absX = abs(xNorm);
                float dEdge = max(0.0, 1.0 - absX);

                // Smooth feather at bottom mouth + soft translucent glass lip at junction (no harsh dark seam)
                float mouthFade = smoothstep(0.025, 0.11, IN.uv.y);
                float collarY = 0.085 - 0.014 * (1.0 - saturate(xNorm * xNorm));
                float collarRing = exp(-pow((IN.uv.y - collarY) / 0.016, 2.0)) * (1.0 - smoothstep(0.92, 1.02, absX));

                float outerMask = 1.0 - smoothstep(0.955, 1.005, absX);

                float3 rimPeriwinkle = lerp(_OuterRimColor.rgb, float3(0.46, 0.48, 0.88), 0.62);
                float3 wallIceBlue   = lerp(_EdgeColor.rgb, float3(0.78, 0.82, 0.99), 0.65);
                float3 pureWhite     = float3(1.00, 1.00, 1.00);

                // Single smooth cylindrical tube wall (no double stripes!)
                float outerEdgeRim      = exp(-pow((dEdge - 0.024) / 0.024, 2.0));
                float crispWhiteRim     = exp(-pow((dEdge - 0.095) / 0.055, 2.0));
                float wallThicknessFade = exp(-dEdge / 0.16);

                float3 rimGlassCol = lerp(wallIceBlue, rimPeriwinkle, outerEdgeRim * 0.82);
                rimGlassCol = lerp(rimGlassCol, pureWhite, saturate(crispWhiteRim * 0.92));

                // Glossy vertical cylindrical specular highlights on tube glass
                float leftSpec    = exp(-pow((xNorm + 0.72) / 0.11, 2.0)) * 0.64;
                float rightSpec   = exp(-pow((xNorm - 0.74) / 0.11, 2.0)) * 0.48;
                float specularTotal = saturate((leftSpec + rightSpec) * _SheenIntensity) * outerMask;

                if (_IsFrontLens > 0.5)
                {
                    float wallRimAlpha = saturate(outerEdgeRim * 0.68 + crispWhiteRim * 0.84 + wallThicknessFade * 0.18) * outerMask;
                    float specAlpha    = specularTotal * 0.82 * outerMask;

                    float3 frontRgb = lerp(rimGlassCol, pureWhite, saturate(specularTotal * 0.95 + collarRing * 0.65));
                    float frontAlpha = saturate(max(max(wallRimAlpha, specAlpha), collarRing * 0.48)) * mouthFade;
                    if (frontAlpha < 0.002) discard;
                    return half4(saturate(frontRgb), frontAlpha);
                }
                else
                {
                    // Soft pastel lavender outer drop shadow outside the curved glass tube
                    float maxNorm = 1.0 / ratio;
                    float padSpan = max(0.05, maxNorm - 1.0);
                    float dOut = clamp((absX - 0.98) / padSpan, 0.0, 1.0);
                    float shadowAlpha = (1.0 - smoothstep(0.0, 0.92, dOut)) * step(0.98, absX) * _FakeShadowColor.a * mouthFade;
                    float3 shadowRGB = _FakeShadowColor.rgb;

                    float centerA = clamp(_CenterGlassAlpha, 0.0, 0.22);
                    float3 chamberRgb = float3(0.97, 0.97, 1.00);
                    chamberRgb = lerp(chamberRgb, wallIceBlue, wallThicknessFade * 0.48);
                    chamberRgb = lerp(chamberRgb, rimGlassCol, saturate(outerEdgeRim * 0.76 + crispWhiteRim * 0.88));
                    chamberRgb = lerp(chamberRgb, pureWhite, saturate(specularTotal * 0.75 + collarRing * 0.60));

                    float chamberAlpha = saturate(max(centerA + wallThicknessFade * 0.34, max(outerEdgeRim * 0.74, max(specularTotal * 0.56, collarRing * 0.55)))) * outerMask * mouthFade;

                    float finalAlpha = saturate(chamberAlpha + shadowAlpha * (1.0 - chamberAlpha * 0.92));
                    if (finalAlpha < 0.002) discard;

                    float3 finalRgb = lerp(shadowRGB * shadowAlpha, chamberRgb, chamberAlpha) / max(finalAlpha, 0.004);
                    if (chamberAlpha < 0.02) finalRgb = shadowRGB;
                    return half4(saturate(finalRgb), finalAlpha);
                }
            }

            float TrackBranchId(int i)
            {
                float4 v = _TrackBranchPacked[i >> 2];
                int lane = i & 3;
                if (lane == 0) return v.x;
                if (lane == 1) return v.y;
                if (lane == 2) return v.z;
                return v.w;
            }

            float sdSegment2D(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
                return length(pa - ba * h);
            }

            // Çoklu-kollu pist için iç kanal işaretli mesafesi (içeride negatif, duvar sınırı = 0).
            // Tek geçişte hem mesafeyi hem de en yakın segmenti döndürür; gradyan ve gölge örnekleri
            // bu segment üzerinden analitik hesaplanır (eskiden 4-6 ek tam SDF geçişi vardı).
            // Bir noktadan segmente dışa doğru birim normal (segment üzerindeki en yakın noktadan p'ye)
            float2 SegOutwardNormal(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float h = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-6), 0.0, 1.0);
                float2 v = p - (a + ab * h);
                float len = length(v);
                return len > 1e-5 ? v / len : float2(0.0, 1.0);
            }

            // smoothGrad: en yakın iki segmentin normallerinin yumuşak karışımı. Tek segmentten alınan normal,
            // segmentlerin birleştiği yerde aniden döndüğü için ışık/gölgede "kırılma" görünüyordu.
            float sdTrackChannelNearest(float2 p, out float2 nearestA, out float2 nearestB, out float2 smoothGrad)
            {
                int n = clamp((int)_TrackSegCount, 0, 64);
                nearestA = p;
                nearestB = p;
                smoothGrad = float2(0.0, 1.0);
                if (n <= 0) return 10.0;

                // Aynı koldaki ardışık düz parçalar sert min yerine yumuşak birleşir: kıvrımların iç tarafındaki
                // keskin köşeler yuvarlanır, cam kenarı eğri gibi akar.
                float kSeg = max(_TrackFillet * 1.6, 0.02);
                float branchMin[6] = { 10.0, 10.0, 10.0, 10.0, 10.0, 10.0 };
                float bestDist = 1e9, secondDist = 1e9;
                float2 secondA = p, secondB = p;
                [loop]
                for (int i = 0; i < 64; i++)
                {
                    if (i >= n) break;
                    float4 seg = _TrackSegs[i];
                    float d = sdSegment2D(p, seg.xy, seg.zw) - _TrackHalfWidth;
                    if (d < bestDist)
                    {
                        secondDist = bestDist; secondA = nearestA; secondB = nearestB;
                        bestDist = d;
                        nearestA = seg.xy;
                        nearestB = seg.zw;
                    }
                    else if (d < secondDist)
                    {
                        secondDist = d; secondA = seg.xy; secondB = seg.zw;
                    }
                    int bId = clamp((int)TrackBranchId(i), 0, 5);
                    branchMin[bId] = opSmoothUnion(branchMin[bId], d, kSeg);
                }

                float2 g1 = SegOutwardNormal(p, nearestA, nearestB);
                if (secondDist < 1e8)
                {
                    float2 g2 = SegOutwardNormal(p, secondA, secondB);
                    // İki segment eşit uzaklıktayken yarı yarıya, uzaklaştıkça en yakının normali
                    float w = 0.5 * (1.0 - saturate((secondDist - bestDist) / max(kSeg * 1.5, 1e-4)));
                    float2 g = lerp(g1, g2, w);
                    smoothGrad = dot(g, g) > 1e-8 ? normalize(g) : g1;
                }
                else smoothGrad = g1;

                // Kollar arası (kulak ↔ köprü gibi) birleşim köşesi de belirgin şekilde yuvarlansın
                float k = max(_TrackFillet * 2.5, 0.04);
                float combined = branchMin[0];
                for (int b = 1; b < 6; b++)
                {
                    float d2 = branchMin[b];
                    if (d2 < 9.0)
                    {
                        combined = opSmoothUnion(combined, d2, k);
                    }
                }
                return combined;
            }

            float sdTrackChannel(float2 p)
            {
                float2 nearestA, nearestB, g;
                return sdTrackChannelNearest(p, nearestA, nearestB, g);
            }

            // Pist mesafesi: pişirilmiş dokudan (hızlı) ya da segment döngüsünden (pişirme sırasında / doku yoksa)
            // Kare cam (kutu modu): iç boşluk yuvarlak köşeli kutu; cidar/zemin/boyun gölgelendirmesi pistle aynı
            float TubeBoxSDF(float2 p)
            {
                float2 h = max(_TrackBox.zw, float2(0.05, 0.05));
                float r = clamp(_TrackBoxRad, 0.01, min(h.x, h.y));
                return sdRoundBox2D(p - _TrackBox.xy, h, r);
            }

            float2 TubeBoxGrad(float2 p)
            {
                const float e = 0.002;
                float2 gg = float2(TubeBoxSDF(p + float2(e, 0.0)) - TubeBoxSDF(p - float2(e, 0.0)),
                                   TubeBoxSDF(p + float2(0.0, e)) - TubeBoxSDF(p - float2(0.0, e)));
                return dot(gg, gg) > 1e-12 ? normalize(gg) : float2(0.0, 1.0);
            }

            // Her slota uzanan ince egimli cikis kanallarinin SDF'i (en fazla 6 kanal * 16 segment = 96)
            float sdExitChannels(float2 p, out float2 chanGrad)
            {
                int count = clamp((int)_ExitChannelCount, 0, 6);
                chanGrad = float2(0.0, 1.0);
                if (count <= 0) return 10.0;

                float halfW = max(_ExitChannelHalfW, 0.012);
                float bestDist = 1e9, secondDist = 1e9;
                float2 bestA = p, bestB = p, secondA = p, secondB = p;

                [loop]
                for (int c = 0; c < 6; c++)
                {
                    if (c >= count) break;
                    int baseIdx = c * 16;
                    for (int s = 0; s < 16; s++)
                    {
                        float4 seg = _ExitChanSegs[baseIdx + s];
                        float d = sdSegment2D(p, seg.xy, seg.zw) - halfW;
                        if (d < bestDist)
                        {
                            secondDist = bestDist; secondA = bestA; secondB = bestB;
                            bestDist = d; bestA = seg.xy; bestB = seg.zw;
                        }
                        else if (d < secondDist)
                        {
                            secondDist = d; secondA = seg.xy; secondB = seg.zw;
                        }
                    }

                    // Agiz ucunda dudak altina kadar dikey kalmasi icin kisa uzanti
                    float4 m = _ExitMouths[c];
                    if (m.w > 0.5)
                    {
                        float2 stubA = float2(m.x, m.y + 0.015);
                        float2 stubB = float2(m.x, m.y - 0.045);
                        float dStub = sdSegment2D(p, stubA, stubB) - halfW;
                        if (dStub < bestDist)
                        {
                            secondDist = bestDist; secondA = bestA; secondB = bestB;
                            bestDist = dStub; bestA = stubA; bestB = stubB;
                        }
                        else if (dStub < secondDist)
                        {
                            secondDist = dStub; secondA = stubA; secondB = stubB;
                        }
                    }
                }

                float2 g1 = SegOutwardNormal(p, bestA, bestB);
                if (secondDist < 1e8)
                {
                    float2 g2 = SegOutwardNormal(p, secondA, secondB);
                    float w = 0.5 * (1.0 - saturate((secondDist - bestDist) / 0.025));
                    float2 g = lerp(g1, g2, w);
                    chanGrad = dot(g, g) > 1e-8 ? normalize(g) : g1;
                }
                else
                {
                    chanGrad = g1;
                }
                return bestDist;
            }

            float ExitChanSDF(float2 p, out float2 g)
            {
                if (_UseExitChanSDF > 0.5 && _TrackBake < 0.5)
                {
                    float aspect = max(_BoxAspect, 0.25);
                    float2 uv = float2(p.x / (2.0 * aspect) + 0.5, p.y * 0.5 + 0.5);
                    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) { g = float2(0.0, 1.0); return 10.0; }
                    float4 t = SAMPLE_TEXTURE2D_LOD(_ExitChanSDFTex, sampler_ExitChanSDFTex, uv, 0);
                    g = dot(t.gb, t.gb) > 1e-6 ? normalize(t.gb) : float2(0.0, 1.0);
                    return t.r;
                }
                return sdExitChannels(p, g);
            }

            float ExitChanSDF(float2 p)
            {
                float2 g;
                return ExitChanSDF(p, g);
            }

            float TrackSDF(float2 p, out float2 g)
            {
                if (_UseTrackSDF > 0.5 && _TrackBake < 0.5)
                {
                    float aspect = max(_BoxAspect, 0.25);
                    float2 uv = float2(p.x / (2.0 * aspect) + 0.5, p.y * 0.5 + 0.5);
                    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) { g = float2(0.0, 1.0); return 10.0; }
                    float4 t = SAMPLE_TEXTURE2D_LOD(_TrackSDFTex, sampler_TrackSDFTex, uv, 0);
                    g = dot(t.gb, t.gb) > 1e-6 ? normalize(t.gb) : float2(0.0, 1.0);
                    return t.r;
                }
                if (_TrackBoxMode > 0.5)
                {
                    const float e = 0.002;
                    float d = TubeBoxSDF(p);
                    float2 gg = float2(TubeBoxSDF(p + float2(e, 0.0)) - TubeBoxSDF(p - float2(e, 0.0)),
                                       TubeBoxSDF(p + float2(0.0, e)) - TubeBoxSDF(p - float2(0.0, e)));
                    g = dot(gg, gg) > 1e-12 ? normalize(gg) : float2(0.0, 1.0);
                    return d;
                }
                float2 a, b;
                return sdTrackChannelNearest(p, a, b, g);
            }

            float TrackSDF(float2 p)
            {
                float2 g;
                return TrackSDF(p, g);
            }

            half4 EvaluateTrackChannel(Varyings IN)
            {
                float aspect = max(_BoxAspect, 0.25);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;

                float quadEdgeFade = smoothstep(0.0, 0.04, IN.uv.x) * smoothstep(1.0, 0.96, IN.uv.x)
                                   * smoothstep(0.0, 0.04, IN.uv.y) * smoothstep(1.0, 0.96, IN.uv.y);

                float wallThick = max(_TrackWallThick, 0.025);
                if (_TrackBake > 0.5)
                {
                    // Pişirme: ham kanal mesafesi + yumuşak normal (karışım yok: _SrcBlend One, _DstBlend Zero)
                    float2 bG;
                    float bD = TrackSDF(p, bG);
                    return half4(bD, bG.x, bG.y, 1.0);
                }

                float2 smoothGrad;
                float dInner = TrackSDF(p, smoothGrad);

                // Çıkış boynu: kanal, çıkış noktasından aşağı doğru aynı cidar kalınlığıyla kısa bir boru olarak devam eder.
                // İç boşluk (dInner) yumuşak birleşimle uzatılır; böylece cidar, gölgelendirme ve koyu zemin boyna kesintisiz akar.
                // Sadece görsel: fizik/collider pist alanından (GlassTrackLayout) gelir, buna dokunulmaz.
                bool hasNeck = _TrackNeck.w > 0.5;
                float neckX = _TrackExitPos.x;
                float neckTopY = _TrackExitPos.y + max(_TrackHalfWidth, 0.04);
                float mouthY = _TrackNeck.x;
                float neckHW = max(_TrackNeck.y, 0.02);
                float lipHH = max(_TrackNeck.z, 0.008);
                if (hasNeck)
                {
                    float neckBotY = mouthY - 0.6; // ağzın altına taşar; ağız aşağıda mouthClip ile kesilir
                    float dNeck = sdRoundBox2D(p - float2(neckX, (neckTopY + neckBotY) * 0.5),
                                               float2(neckHW, (neckTopY - neckBotY) * 0.5), 0.0);
                    dInner = opSmoothUnion(dInner, dNeck, wallThick * 1.1);
                }

                // Işık yönü (duvarın dışa normali) doğrudan yumuşatılmış mesafenin piksel türevlerinden: mesafe alanı
                // sürekli olduğu için normal de sürekli, segment birleşimlerinde sıçrama olmaz. p'nin türevleriyle
                // çözüldüğü için ekranın y yönünün platforma göre ters olması sonucu etkilemez.
                // (Türevler discard'dan ÖNCE alınmalı.)
                float2 dpx = ddx(p), dpy = ddy(p);
                float ddxD = ddx(dInner), ddyD = ddy(dInner);
                float aa = max((abs(ddxD) + abs(ddyD)) * 1.15, 0.0014);
                float det = dpx.x * dpy.y - dpx.y * dpy.x;
                float2 derivGrad = abs(det) > 1e-10
                    ? float2(ddxD * dpy.y - ddyD * dpx.y, dpx.x * ddyD - dpy.x * ddxD) / det
                    : smoothGrad;
                float derivLenSq = dot(derivGrad, derivGrad);
                if (derivLenSq > 1e-5)
                {
                    float2 normDeriv = derivGrad * rsqrt(derivLenSq);
                    // Orta eksen / eyer noktalarında (derivLenSq küçükken) piksel türev gürültüsünü yumuşat
                    float gradConfidence = smoothstep(0.08, 0.55, derivLenSq);
                    float2 blendedGrad = lerp(smoothGrad, normDeriv, gradConfidence);
                    smoothGrad = dot(blendedGrad, blendedGrad) > 1e-6 ? normalize(blendedGrad) : smoothGrad;
                }

                // Kanal duvarı + yumuşak dış gölge bandının tamamen dışındaysa erken çık.
                if (dInner > wallThick + 0.34) discard;

                float dOuter = dInner - wallThick;

                // Duvar eğimi (dışa doğru normal): en yakın iki segmentin yumuşak karışımı (birleşimlerde kırılma yok)
                float2 grad = smoothGrad;

                // Alt çıkış ağzı açıklığı (topla temas eden alt çıkış yakasına temiz geçiş)
                float exitCut = 0.0;
                if (_TrackExitPos.w > 0.5 && !hasNeck)
                {
                    float dxE = abs(p.x - _TrackExitPos.x);
                    float yZoneE = smoothstep(_TrackExitPos.y - wallThick * 0.4, _TrackExitPos.y - wallThick * 1.4, p.y);
                    float gateX = 1.0 - smoothstep(_TrackExitPos.z * 0.65, _TrackExitPos.z * 1.15, dxE);
                    exitCut = gateX * yZoneE;
                }

                // =====================================================================
                // KALIN CİDARLI CAM TÜP (silindirik kesit)
                // Kanal, eksenden dış kenara kadar yatay yatan bir cam boru gibi aydınlatılır:
                //   u = 0 borunun ekseni, u = 1 dış kenar; uIn = iç cidar (topların gezdiği boşluğun sınırı).
                //   Yüzey normali eksende kameraya bakar, kenarda yana yatar -> yol boyunca kesintisiz kavisli
                //   parlama bantları, kenarlarda daha çok cam (Fresnel), ışığa bakan kenar parlak / karşı kenar koyu.
                // Arka katman: topların arkasındaki cam (iç boşluğun arka yüzü, kalın cidar, alt kontur, gölge).
                // Ön katman: topların önündeki cam (ince speküler bantlar, iç ve dış kenar parıltıları).
                // =====================================================================
                float halfW  = max(_TrackHalfWidth, 0.04);
                float tubeR  = halfW + wallThick;
                float u      = saturate((dInner + halfW) / tubeR);
                float uIn    = halfW / tubeR;
                float nz     = sqrt(saturate(1.0 - u * u));
                float3 N     = normalize(float3(grad * u, max(nz, 0.02)));
                float3 V     = float3(0.0, 0.0, 1.0);
                float3 L1    = normalize(float3(-0.42, 0.78, 0.95));   // ana ışık: sol üst
                float3 L2    = normalize(float3(0.60, -0.62, 0.50));   // karşı (dolgu) ışık: sağ alt, zayıf
                float3 H1    = normalize(L1 + V);
                float3 H2    = normalize(L2 + V);
                float  diff  = saturate(dot(N, L1));
                float  spec1 = pow(saturate(dot(N, H1)), 90.0);
                float  spec1Soft = pow(saturate(dot(N, H1)), 18.0);
                float  spec2 = pow(saturate(dot(N, H2)), 45.0);
                float  fres  = pow(1.0 - nz, 2.0);                     // kenara doğru artan cam kalınlığı
                float  facing = dot(grad, normalize(L1.xy));            // +1: ışığa bakan kenar, -1: gölgedeki kenar
                float  sheenMul = max(0.6, _SheenIntensity);

                // Cidar profili çizgileri
                float innerRefract = exp(-pow((u - uIn) / 0.016, 2.0));          // iç cidar kırılma çizgisi (koyu)
                float innerBevel   = exp(-pow((u - (uIn - 0.035)) / 0.022, 2.0)); // iç cidarın parlak kenarı
                float outerRim     = exp(-pow((u - 0.955) / 0.024, 2.0));        // dış kenar parıltısı
                float wallZone     = smoothstep(uIn - 0.012, uIn + 0.012, u);     // 1: kalın cam cidar

                // Cam renkleri (doğrusal uzay): soğuk mavi-beyaz ton
                float3 pureWhite  = float3(1.00, 1.00, 1.00);
                float3 glassLight = lerp(float3(0.64, 0.74, 0.97), _EdgeColor.rgb, 0.15);
                float3 glassMid   = float3(0.38, 0.47, 0.82);
                float3 glassDeep  = lerp(float3(0.13, 0.17, 0.42), _InnerGrooveColor.rgb, 0.25);

                // Yan yüz ve gölge için (eski palet adları)
                float3 icyCrystal      = glassLight;
                float3 glassRefractRim = glassMid;
                float3 innerDeepRim    = glassDeep;
                float3 bedShadowCol    = float3(0.015, 0.020, 0.055);

                // Boyun ağzı: ağız çizgisinin altında boyun bandındaki cam kesilir; ağızda kalın, yuvarlak bir cam dudak durur
                float lipOver = wallThick * 0.35;
                float neckOuterHW = neckHW + wallThick;
                float neckDx = abs(p.x - neckX);
                float neckBand = hasNeck ? 1.0 - smoothstep(neckOuterHW + lipOver + 0.02, neckOuterHW + lipOver + 0.05, neckDx) : 0.0;
                float mouthClip = 1.0 - neckBand * (1.0 - smoothstep(mouthY - aa, mouthY + aa, p.y));
                float dLip = sdRoundBox2D(p - float2(neckX, mouthY + lipHH), float2(neckOuterHW + lipOver, lipHH), lipHH);
                float lipMask = hasNeck ? 1.0 - smoothstep(-aa, aa, dLip) : 0.0;
                float lipV = saturate((p.y - mouthY) / (2.0 * lipHH));                         // 0: dudak altı, 1: dudak üstü
                float lipOverHole = 1.0 - smoothstep(neckHW - aa * 2.0, neckHW + aa * 2.0, neckDx); // 1: açıklığın önü
                // Boyun içi (kanal tabanının altı): ağza yaklaştıkça derinleşen koyu iç yüzey
                float inNeck = neckBand * (1.0 - smoothstep(_TrackExitPos.y - 0.01, _TrackExitPos.y + 0.04, p.y));
                float neckDepth = saturate((p.y - mouthY) / max(_TrackExitPos.y - mouthY, 0.01)); // 0: ağız, 1: kanal tabanı

                float outerMask = (1.0 - smoothstep(-aa, aa, dOuter)) * (1.0 - exitCut * 0.65) * mouthClip;
                float innerMask = (1.0 - smoothstep(-aa, aa, dInner)) * mouthClip;
                float wallBandMask = saturate(outerMask - innerMask);

                if (_IsFrontLens > 0.5)
                {
                    // ÖN CAM (topların önünde): sadece ince ışık olayları; merkez neredeyse tamamen şeffaf
                    float bandMain = spec1 * 0.95 * sheenMul;
                    float bandSoft = spec1Soft * 0.10;
                    float bandFill = spec2 * 0.38;
                    float edgeGlass = fres * wallZone * 0.22;                       // kalın cidarın ön yüzü
                    float rimGlint  = outerRim * (0.30 + 0.45 * saturate(facing));   // ışığa bakan dış kenar daha parlak
                    float bevelGlint = innerBevel * (0.25 + 0.35 * saturate(facing));
                    // Referans tüplerdeki gibi: ışığa bakan kenarın hemen içinde belirgin parlak bant,
                    // karşı kenarda daha sönük ikinci bant (silindirik yüzeyin yansımaları)
                    float litBand    = exp(-pow((u - 0.84) / 0.050, 2.0)) * saturate(facing) * 0.70 * sheenMul;
                    float shadowBand = exp(-pow((u - 0.88) / 0.035, 2.0)) * saturate(-facing) * 0.22;
                    float frontA = saturate(0.03 + bandMain + bandSoft + bandFill + edgeGlass + rimGlint + bevelGlint + litBand + shadowBand);
                    frontA *= outerMask * quadEdgeFade;
                    frontA *= 1.0 - inNeck * 0.75; // boyun içinde bulanık parlama olmasın (net görünüm)

                    float whiteness = saturate(bandMain * 1.3 + bandFill + rimGlint * 1.2 + bevelGlint * 1.2 + litBand * 1.4 + shadowBand);
                    float3 frontRgb = lerp(glassLight, pureWhite, whiteness);

                    if (hasNeck)
                    {
                        // Dudağın ön yarısı: açıklığın önünde yarı saydam cam halka (toplar arkasından geçer),
                        // üstte parlak ışık yakalama çizgisi, altta koyu kesik kenar; yanlarda (cidar üstünde) daha yoğun cam
                        float lpx = aa / max(2.0 * lipHH, 0.001) * 1.5;
                        float lipTopHi  = smoothstep(0.55 - lpx, 0.55 + lpx, lipV) * (1.0 - smoothstep(0.85 - lpx, 0.85 + lpx, lipV));
                        float lipBotLn  = 1.0 - smoothstep(0.22 - lpx, 0.22 + lpx, lipV);
                        float lipLitSide = saturate(0.55 - (p.x - neckX) / max(neckOuterHW + lipOver, 0.01) * 0.45);
                        float lipA = lipMask * saturate(0.16 + (1.0 - lipOverHole) * 0.40 + lipTopHi * (0.40 + 0.30 * lipLitSide) + lipBotLn * 0.28) * quadEdgeFade;
                        float3 lipRgb = lerp(glassLight, pureWhite, saturate(lipTopHi * (0.65 + 0.35 * lipLitSide)));
                        lipRgb = lerp(lipRgb, glassDeep * 0.7, lipBotLn * 0.55);
                        float outA = lipA + frontA * (1.0 - lipA);
                        frontRgb = (lipRgb * lipA + frontRgb * frontA * (1.0 - lipA)) / max(outA, 0.004);
                        frontA = outA;
                    }
                    if (frontA < 0.003) discard;
                    return half4(saturate(frontRgb), frontA);
                }
                else
                {
                    // ARKA CAM (topların arkasında)
                    // Gölge: tüp zeminden hafif yüksekte
                    float dNearRaw  = TrackSDF(p - float2(0.0, -0.040));
                    float dShadNear = dNearRaw - wallThick;
                    float dShadFar  = TrackSDF(p - float2(0.0, -0.085)) - wallThick;
                    if (hasNeck)
                    {
                        // Boyun + dudak da zemine gölge düşürür
                        float2 neckC = float2(neckX, (neckTopY + mouthY) * 0.5);
                        float2 neckH = float2(neckOuterHW + lipOver, (neckTopY - mouthY) * 0.5);
                        dShadNear = min(dShadNear, sdRoundBox2D(p - float2(0.0, -0.040) - neckC, neckH, lipHH));
                        dShadFar  = min(dShadFar,  sdRoundBox2D(p - float2(0.0, -0.085) - neckC, neckH, lipHH));
                    }
                    float contactShad = pow(1.0 - smoothstep(-0.015, 0.080, dShadNear), 1.55);
                    float dropShad    = pow(1.0 - smoothstep(-0.025, 0.175, dShadFar), 1.80);
                    float shadowAlpha = saturate(contactShad * 0.60 + dropShad * 0.42) * 0.34 * (1.0 - exitCut * 0.70) * quadEdgeFade;
                    float3 shadowRGB = float3(0.12, 0.14, 0.30);

                    // İç boşluk: borunun arka yarısı. Şeffaf, soğuk tonlu; gölgedeki yana doğru koyulaşır,
                    // cidara yaklaştıkça (daha çok cam) yoğunlaşır.
                    // Koyu lacivert kanal zemini (topların kontrastı için opak), cidara yakın AO + ince kostik
                    float3 bedTopCol = float3(0.050, 0.062, 0.140);
                    float3 bedBotCol = float3(0.026, 0.034, 0.090);
                    float depthFromWall = max(0.0, -dInner);
                    float innerWallAO = exp(-depthFromWall / max(_TrackHalfWidth * 0.22, 0.022));
                    float innerCaustic = exp(-pow((depthFromWall - max(_TrackHalfWidth * 0.14, 0.018)) / 0.016, 2.0));
                    float shadowSide = saturate(-facing) * u;
                    float3 interiorRgb = lerp(bedBotCol, bedTopCol, saturate(IN.uv.y));
                    interiorRgb = lerp(interiorRgb, bedShadowCol, saturate(innerWallAO * 0.72 + shadowSide * 0.25));
                    interiorRgb = lerp(interiorRgb, float3(0.44, 0.62, 0.94), innerCaustic * 0.22 * (0.5 + 0.5 * saturate(facing)));
                    // Boyun içi ağza doğru derinleşir (açıklık hissi)
                    interiorRgb = lerp(interiorRgb, bedShadowCol * 0.8, inNeck * (1.0 - neckDepth) * 0.75);
                    float interiorA = 0.94;

                    // Kalın cidar: daha yoğun cam; ışığa bakan kenar açık, gölgedeki kenar koyu (silindir hissi)
                    float lit = saturate(0.5 + 0.5 * facing);
                    float3 wallRgb = lerp(glassDeep, glassLight, saturate(0.25 + 0.75 * lit * (0.55 + 0.45 * diff)));
                    wallRgb = lerp(wallRgb, glassDeep * 0.65, saturate(-facing) * fres * 0.55); // koyu alt kontur
                    wallRgb = lerp(wallRgb, pureWhite, outerRim * lit * 0.35);
                    float wallA = 0.62 + 0.30 * fres;
                    // En dışta ince koyu cam konturu (camın kesilmiş kenarı)
                    float edgeLine = smoothstep(0.93, 1.0, u);
                    wallRgb = lerp(wallRgb, glassDeep * 0.5, edgeLine * 0.65);
                    wallA = saturate(wallA + edgeLine * 0.2);

                    float3 backRgb = lerp(interiorRgb, wallRgb, wallZone);
                    float backA = lerp(interiorA, wallA, wallZone);
                    // İç cidar kırılma çizgisi (camın kalınlığını belli eder)
                    backRgb = lerp(backRgb, glassDeep * 0.55, innerRefract * 0.55);
                    backA = saturate(backA + innerRefract * 0.25);

                    float glassAlpha = backA * outerMask * quadEdgeFade;
                    float3 glassRgb = backRgb;

                    // 3D yan yüz: borunun zemine oturduğu alt kalınlığı (güneye bakan kenarlarda)
                    const float sideDepth = 0.040;
                    float wallAbove = (1.0 - smoothstep(-aa, aa, dNearRaw - wallThick)) * smoothstep(-aa, aa, dNearRaw);
                    float sideMask = wallAbove * (1.0 - wallBandMask) * (1.0 - innerMask) * (1.0 - exitCut) * mouthClip * (1.0 - lipMask);
                    float distBelowWall = max(dOuter, 0.0);
                    float sideT = saturate(distBelowWall / sideDepth);
                    float3 sideRgb = lerp(glassMid, glassDeep * 0.6, pow(sideT, 0.8));
                    sideRgb = lerp(sideRgb, pureWhite, exp(-pow(sideT / 0.12, 2.0)) * 0.35);
                    float sideAlpha = sideMask * 0.75 * quadEdgeFade;
                    glassRgb = lerp(glassRgb, sideRgb, sideMask);
                    glassAlpha = max(glassAlpha, sideAlpha);

                    if (hasNeck)
                    {
                        // Dudağın arka gövdesi: cidar üstünde kalın, ışığa bakan yanı açık cam boncuk;
                        // açıklığın önünde koyu iç zemin görünür kalır, üst kenarda ince koyu iç kenar çizgisi
                        float lipLit = saturate(0.5 - (p.x - neckX) / max(neckOuterHW + lipOver, 0.01) * 0.5);
                        float3 lipBackRgb = lerp(glassMid, glassLight, saturate(0.35 + lipV * 0.45 + lipLit * 0.25));
                        lipBackRgb = lerp(lipBackRgb, glassDeep * 0.6, smoothstep(0.30, 0.0, lipV) * 0.55);
                        float lipBackA = lipMask * (1.0 - lipOverHole) * 0.88 * quadEdgeFade;
                        float innerEdge = lipOverHole * lipMask * exp(-pow((lipV - 0.97) / 0.10, 2.0));
                        glassRgb = lerp(glassRgb, lipBackRgb, lipBackA);
                        glassRgb = lerp(glassRgb, bedShadowCol * 0.5, innerEdge * 0.6);
                        glassAlpha = max(glassAlpha, lipBackA);
                    }

                    float finalAlpha = saturate(glassAlpha + shadowAlpha * (1.0 - glassAlpha));
                    if (finalAlpha < 0.002) discard;

                    float3 premul = glassRgb * glassAlpha + shadowRGB * shadowAlpha * (1.0 - glassAlpha);
                    float3 finalRgb = premul / max(finalAlpha, 0.004);
                    return half4(saturate(finalRgb), finalAlpha);
                }
            }

            // Her slota uzanan bagimsiz ince cam tup kanallari (2. gorseldeki ref gibi puruzsuz ve yari saydam)
            half4 EvaluateThinExitChannels(Varyings IN)
            {
                float aspect = max(_BoxAspect, 0.25);
                float2 p = (IN.uv - 0.5) * 2.0;
                p.x *= aspect;

                float quadEdgeFade = smoothstep(0.0, 0.04, IN.uv.x) * smoothstep(1.0, 0.96, IN.uv.x)
                                   * smoothstep(0.0, 0.04, IN.uv.y) * smoothstep(1.0, 0.96, IN.uv.y);

                if (_TrackBake > 1.5)
                {
                    float2 bG;
                    float bD = sdExitChannels(p, bG);
                    return half4(bD, bG.x, bG.y, 1.0);
                }

                float2 smoothGrad;
                float dInner = ExitChanSDF(p, smoothGrad);

                float2 dpx = ddx(p), dpy = ddy(p);
                float ddxD = ddx(dInner), ddyD = ddy(dInner);
                float aa = max((abs(ddxD) + abs(ddyD)) * 1.15, 0.0012);
                float det = dpx.x * dpy.y - dpx.y * dpy.x;
                float2 derivGrad = abs(det) > 1e-10
                    ? float2(ddxD * dpy.y - ddyD * dpx.y, dpx.x * ddyD - dpy.x * ddxD) / det
                    : smoothGrad;
                float derivLenSq = dot(derivGrad, derivGrad);
                if (derivLenSq > 1e-5)
                {
                    float2 normDeriv = derivGrad * rsqrt(derivLenSq);
                    float gradConfidence = smoothstep(0.08, 0.55, derivLenSq);
                    float2 blendedGrad = lerp(smoothGrad, normDeriv, gradConfidence);
                    smoothGrad = dot(blendedGrad, blendedGrad) > 1e-6 ? normalize(blendedGrad) : smoothGrad;
                }

                float halfW = max(_ExitChannelHalfW, 0.012);
                float wallThick = max(_ExitChannelWallThick, 0.005);
                if (dInner > wallThick + 0.16) discard;

                float dOuter = dInner - wallThick;
                float2 grad = smoothGrad;
                float tubeR = halfW + wallThick;
                float u = saturate((dInner + halfW) / tubeR);
                float uIn = halfW / tubeR;
                float nz = sqrt(saturate(1.0 - u * u));
                float3 N = normalize(float3(grad * u, max(nz, 0.02)));
                float3 V = float3(0.0, 0.0, 1.0);
                float3 L1 = normalize(float3(-0.42, 0.78, 0.95));
                float3 L2 = normalize(float3(0.60, -0.62, 0.50));
                float3 H1 = normalize(L1 + V);
                float3 H2 = normalize(L2 + V);
                float diff = saturate(dot(N, L1));
                float spec1 = pow(saturate(dot(N, H1)), 85.0);
                float spec2 = pow(saturate(dot(N, H2)), 40.0);
                float fres = pow(1.0 - nz, 2.0);
                float facing = dot(grad, normalize(L1.xy));
                float sheenMul = max(0.65, _SheenIntensity);

                float wRefr = max(0.018, 0.0024 / max(tubeR, 0.03));
                float wBev  = max(0.022, 0.0032 / max(tubeR, 0.03));
                float oBev  = max(0.032, 0.0050 / max(tubeR, 0.03));
                float wRim  = max(0.024, 0.0035 / max(tubeR, 0.03));
                float wZone = max(0.014, 0.0018 / max(tubeR, 0.03));
                float innerRefract = exp(-pow((u - uIn) / wRefr, 2.0));
                float innerBevel   = exp(-pow((u - (uIn - oBev)) / wBev, 2.0));
                float outerRim     = exp(-pow((u - 0.955) / wRim, 2.0));
                float wallZone     = smoothstep(uIn - wZone, uIn + wZone, u);

                float3 pureWhite  = float3(1.00, 1.00, 1.00);
                float3 glassLight = lerp(float3(0.70, 0.78, 0.98), _EdgeColor.rgb, 0.12);
                float3 glassMid   = float3(0.45, 0.52, 0.84);
                float3 glassDeep  = lerp(float3(0.18, 0.21, 0.48), _InnerGrooveColor.rgb, 0.25);

                // Ustte ana cam yakasinin icine temizce girer, altta her slot agzinda duzgun acik dudak ile biter
                float topY = _TrackExitPos.y;
                float topClip = 1.0 - smoothstep(topY - aa * 2.0, topY + aa * 2.0, p.y);

                int mCount = clamp((int)_ExitChannelCount, 0, 6);
                float cLipOver = wallThick * 0.22;
                float cBandHW = tubeR + cLipOver + 0.012;
                float mouthClip = 1.0;
                float lipMask = 0.0;
                float lipV = 0.0;
                float lipOverHole = 0.0;
                float lipLitDx = 0.0;
                float activeLipHH = 0.006;
                [loop]
                for (int c = 0; c < 6; c++)
                {
                    if (c >= mCount) break;
                    float4 m = _ExitMouths[c];
                    if (m.w < 0.5) continue;
                    float mX = m.x;
                    float mY = m.y;
                    float mLipHH = max(m.z, 0.004);
                    float dxM = abs(p.x - mX);
                    float bandM = (1.0 - smoothstep(cBandHW, cBandHW + 0.018, dxM))
                                * (1.0 - smoothstep(mY + mLipHH * 2.5, mY + mLipHH * 4.5, p.y));
                    mouthClip *= 1.0 - bandM * (1.0 - smoothstep(mY - aa, mY + aa, p.y));

                    float dLipM = sdRoundBox2D(p - float2(mX, mY + mLipHH), float2(tubeR + cLipOver, mLipHH), mLipHH);
                    float lMaskM = 1.0 - smoothstep(-aa, aa, dLipM);
                    if (lMaskM > lipMask)
                    {
                        lipMask = lMaskM;
                        lipV = saturate((p.y - mY) / (2.0 * mLipHH));
                        lipOverHole = 1.0 - smoothstep(halfW - aa * 2.0, halfW + aa * 2.0, dxM);
                        lipLitDx = p.x - mX;
                        activeLipHH = mLipHH;
                    }
                }

                float clipMask = topClip * mouthClip;
                float outerMask = (1.0 - smoothstep(-aa, aa, dOuter)) * clipMask;

                if (_IsFrontLens > 0.5)
                {
                    float bandMain = spec1 * 0.88 * sheenMul;
                    float bandFill = spec2 * 0.34;
                    float edgeGlass = fres * wallZone * 0.24;
                    float rimGlint  = outerRim * (0.35 + 0.45 * saturate(facing));
                    float bevelGlint = innerBevel * (0.28 + 0.36 * saturate(facing));
                    float litBand    = exp(-pow((u - 0.88) / 0.045, 2.0)) * saturate(facing) * 0.65 * sheenMul;
                    float tubeGloss  = exp(-pow((u - 0.36) / 0.19, 2.0)) * saturate(facing + 0.45) * (1.0 - wallZone) * 0.46 * sheenMul;
                    float frontA = saturate(0.02 + bandMain + bandFill + edgeGlass + rimGlint + bevelGlint + litBand + tubeGloss);
                    frontA *= outerMask * quadEdgeFade;

                    float whiteness = saturate(bandMain * 1.3 + bandFill + rimGlint * 1.2 + bevelGlint * 1.2 + litBand * 1.4 + tubeGloss * 1.4);
                    float3 frontRgb = lerp(glassLight, pureWhite, whiteness);

                    if (lipMask > 0.001)
                    {
                        float lpx = aa / max(2.0 * activeLipHH, 0.001) * 1.5;
                        float lipTopHi  = smoothstep(0.55 - lpx, 0.55 + lpx, lipV) * (1.0 - smoothstep(0.85 - lpx, 0.85 + lpx, lipV));
                        float lipBotLn  = 1.0 - smoothstep(0.22 - lpx, 0.22 + lpx, lipV);
                        float lipLitSide = saturate(0.55 - lipLitDx / max(tubeR + cLipOver, 0.01) * 0.45);
                        float lipA = lipMask * saturate(0.14 + (1.0 - lipOverHole) * 0.38 + lipTopHi * (0.42 + 0.28 * lipLitSide) + lipBotLn * 0.25) * quadEdgeFade;
                        float3 lipRgb = lerp(glassLight, pureWhite, saturate(lipTopHi * (0.65 + 0.35 * lipLitSide)));
                        lipRgb = lerp(lipRgb, glassDeep * 0.75, lipBotLn * 0.50);
                        float outA = lipA + frontA * (1.0 - lipA);
                        frontRgb = (lipRgb * lipA + frontRgb * frontA * (1.0 - lipA)) / max(outA, 0.004);
                        frontA = outA;
                    }
                    if (frontA < 0.003) discard;
                    return half4(saturate(frontRgb), frontA);
                }
                else
                {
                    float dNearRaw  = ExitChanSDF(p - float2(0.0, -0.025));
                    float dShadNear = dNearRaw - wallThick;
                    float dShadFar  = ExitChanSDF(p - float2(0.0, -0.055)) - wallThick;
                    float contactShad = pow(1.0 - smoothstep(-0.010, 0.050, dShadNear), 1.6);
                    float dropShad    = pow(1.0 - smoothstep(-0.015, 0.110, dShadFar), 1.8);
                    float shadowAlpha = saturate(contactShad * 0.55 + dropShad * 0.38) * 0.16 * clipMask * quadEdgeFade;
                    float3 shadowRGB = float3(0.16, 0.18, 0.38);

                    float3 bedTopCol = float3(0.30, 0.33, 0.52);
                    float3 bedBotCol = float3(0.23, 0.26, 0.44);
                    float depthFromWall = max(0.0, -dInner);
                    float innerWallAO = exp(-depthFromWall / max(halfW * 0.26, 0.010));
                    float shadowSide = saturate(-facing) * u;
                    float3 interiorRgb = lerp(bedBotCol, bedTopCol, saturate(IN.uv.y));
                    interiorRgb = lerp(interiorRgb, glassDeep * 0.72, saturate(innerWallAO * 0.45 + shadowSide * 0.22));
                    float interiorA = 0.36;

                    float lit = saturate(0.5 + 0.5 * facing);
                    float3 wallRgb = lerp(glassDeep * 1.15, glassLight, saturate(0.32 + 0.68 * lit * (0.55 + 0.45 * diff)));
                    wallRgb = lerp(wallRgb, glassDeep * 0.75, saturate(-facing) * fres * 0.45);
                    wallRgb = lerp(wallRgb, pureWhite, outerRim * lit * 0.38);
                    float wallA = 0.46 + 0.34 * fres;
                    float edgeLine = smoothstep(0.91, 1.0, u);
                    wallRgb = lerp(wallRgb, glassDeep * 0.58, edgeLine * 0.68);
                    wallA = saturate(wallA + edgeLine * 0.24);

                    float3 backRgb = lerp(interiorRgb, wallRgb, wallZone);
                    float backA = lerp(interiorA, wallA, wallZone);
                    backRgb = lerp(backRgb, glassDeep * 0.62, innerRefract * 0.36);
                    backA = saturate(backA + innerRefract * 0.18);

                    float glassAlpha = backA * outerMask * quadEdgeFade;
                    float3 glassRgb = backRgb;

                    if (lipMask > 0.001)
                    {
                        float lipLit = saturate(0.5 - lipLitDx / max(tubeR + cLipOver, 0.01) * 0.5);
                        float3 lipBackRgb = lerp(glassMid, glassLight, saturate(0.35 + lipV * 0.45 + lipLit * 0.25));
                        lipBackRgb = lerp(lipBackRgb, glassDeep * 0.65, smoothstep(0.30, 0.0, lipV) * 0.45);
                        float lipBackA = lipMask * (1.0 - lipOverHole) * 0.78 * quadEdgeFade;
                        glassRgb = lerp(glassRgb, lipBackRgb, lipBackA);
                        glassAlpha = max(glassAlpha, lipBackA);
                    }

                    float finalAlpha = saturate(glassAlpha + shadowAlpha * (1.0 - glassAlpha));
                    if (finalAlpha < 0.002) discard;

                    float3 premul = glassRgb * glassAlpha + shadowRGB * shadowAlpha * (1.0 - glassAlpha);
                    float3 finalRgb = premul / max(finalAlpha, 0.004);
                    return half4(saturate(finalRgb), finalAlpha);
                }
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_IsExitChannels > 0.5)
                {
                    return EvaluateThinExitChannels(IN);
                }
                if (_IsCollar > 1.5)
                {
                    return EvaluateExitSleeve(IN);
                }
                if (_IsCollar > 0.5)
                {
                    return EvaluateCollar3D(IN);
                }
                if (_IsFeedTube > 0.5)
                {
                    return EvaluateFeedTube(IN);
                }
                if (_IsTrackChannel > 0.5)
                {
                    return EvaluateTrackChannel(IN);
                }
                if (_IsRoundedBoxPool > 0.5)
                {
                    return EvaluateRoundedBoxPool(IN);
                }

                // xNorm in [-1, +1] across the horizontal width of the glass at this height
                float xNorm = IN.uv.x * 2.0 - 1.0;
                float absX = abs(xNorm);

                // True perpendicular world-space distance (in meters) from the left and right outer glass contour!
                // uv2.x = halfSpan * invHypL, uv2.y = halfSpan * invHypR
                float scaleL = max(IN.uv2.x, 0.08);
                float scaleR = max(IN.uv2.y, 0.08);
                float dLeft  = IN.uv.x * 2.0 * scaleL;
                float dRight = (1.0 - IN.uv.x) * 2.0 * scaleR;
                float dPerp  = min(dLeft, dRight);

                float wallThick = max(_WallThickness, 0.03);
                float tWall = dPerp / wallThick;

                // Evaluate the continuous multi-band blown-glass wall
                float3 wallRgb;
                float wallAlpha;
                float wallZoneMask;
                EvaluateBlownGlassWall(IN.uv, dPerp, wallThick, xNorm, wallRgb, wallAlpha, wallZoneMask);

                // -------------------------------------------------------------------------
                // 1. Broad Glossy White Crescent Highlights Hugging Left & Right Lobes/Shoulders
                //    (Directly inside the inner wall meniscus: tWall ~ 1.08 .. 2.35)
                // -------------------------------------------------------------------------
                float innerMargin = smoothstep(0.94, 1.16, tWall);
                float lobeRadial  = exp(-pow((tWall - 1.46) / 0.56, 2.0)) * innerMargin;

                // Vertical mask covering the rounded left/right lobes and upper sloped shoulders (uv.y ~ 0.21..0.57)
                float lobeYMask = smoothstep(0.19, 0.29, IN.uv.y) * (1.0 - smoothstep(0.46, 0.58, IN.uv.y));
                float lobeCrescent = lobeYMask * lobeRadial * 0.78 * _SheenIntensity;

                // Subtle glossy white cushion reflection along the lower sloped funnel floor (uv.y ~ 0.13..0.24)
                float floorYMask = smoothstep(0.12, 0.17, IN.uv.y) * (1.0 - smoothstep(0.21, 0.27, IN.uv.y));
                float floorRadial = exp(-pow((tWall - 1.32) / 0.40, 2.0)) * innerMargin;
                float floorCrescent = floorYMask * floorRadial * 0.46 * _SheenIntensity;

                // -------------------------------------------------------------------------
                // 2. Sleek Vertical Cylindrical Specular Streaks on the Upper Neck & Bottom Spout
                // -------------------------------------------------------------------------
                float interiorMask = smoothstep(0.88, 1.18, tWall);

                // Upper neck & upper shoulder transition (uv.y > 0.46)
                float neckYMask = smoothstep(0.44, 0.58, IN.uv.y);
                float neckLeftStreak  = exp(-pow((xNorm + 0.48) * 6.0, 2.0)) * 0.54;
                float neckRightStreak = exp(-pow((xNorm - 0.52) * 6.8, 2.0)) * 0.42;
                float neckCenterGlow  = exp(-pow((xNorm + 0.12) * 3.2, 2.0)) * 0.22;
                float neckStreaks = (neckLeftStreak + neckRightStreak + neckCenterGlow) * neckYMask * interiorMask * _SheenIntensity;

                // Bottom vertical spout cylindrical reflection streaks (uv.y < 0.20)
                float spoutYMask = (1.0 - smoothstep(0.14, 0.21, IN.uv.y)) * smoothstep(0.015, 0.035, IN.uv.y);
                float spoutLeftStreak  = exp(-pow((xNorm + 0.38) * 6.5, 2.0)) * 0.50;
                float spoutRightStreak = exp(-pow((xNorm - 0.42) * 7.2, 2.0)) * 0.38;
                float spoutStreaks = (spoutLeftStreak + spoutRightStreak) * spoutYMask * interiorMask * _SheenIntensity;

                // -------------------------------------------------------------------------
                // 3. 3D Oval / Elliptical Glass Spout Mouth Ring at the Bottom Tip (uv.y < 0.038)
                // -------------------------------------------------------------------------
                float ellipseProfile = sqrt(saturate(1.0 - xNorm * xNorm));
                // Lower front lip arc and upper back lip arc of the 3D circular spout opening
                float yFrontLip = 0.013 - 0.007 * ellipseProfile;
                float yBackLip  = 0.016 + 0.009 * ellipseProfile;

                float frontLipWhite  = exp(-pow((IN.uv.y - yFrontLip) / 0.0048, 2.0));
                float frontLipCobalt = exp(-pow((IN.uv.y - (yFrontLip - 0.0035)) / 0.0042, 2.0));
                float backLipWhite   = exp(-pow((IN.uv.y - yBackLip) / 0.0042, 2.0)) * 0.65;
                float nozzleRingMask = saturate(frontLipWhite + frontLipCobalt * 0.85 + backLipWhite) * smoothstep(0.0, 0.004, IN.uv.y);
                float3 nozzleRingRgb = lerp(_OuterRimColor.rgb, _RimGlowColor.rgb, saturate(frontLipWhite * 1.15 + backLipWhite));

                // -------------------------------------------------------------------------
                // FRONT GLASS LENS MODE (_IsFrontLens > 0.5):
                // Sits in front of the 3D cubes at z = -0.24f.
                // Renders the crisp optical blown-glass wall border, the 3D bottom spout mouth ring,
                // the broad white lobe crescents, and the cylindrical neck/spout specular streaks,
                // while keeping the interior center transparent so the candy cubes remain 100% vibrant!
                // -------------------------------------------------------------------------
                if (_IsFrontLens > 0.5)
                {
                    // Subtle diagonal glass window sheen across upper-left interior
                    float diagCoord = -xNorm * 0.52 + (IN.uv.y - 0.36) * 0.88;
                    float windowBand = exp(-pow((diagCoord - 0.20) * 6.0, 2.0)) * interiorMask * (1.0 - smoothstep(0.65, 0.92, absX)) * 0.14 * _SheenIntensity;

                    float specHighlights = saturate(lobeCrescent + floorCrescent + neckStreaks * 0.85 + spoutStreaks + windowBand);
                    float3 specRgb = lerp(_EdgeColor.rgb, _RimGlowColor.rgb, 0.92);

                    // Blend interior specular reflections with the physical outer/inner glass wall
                    float3 frontRgb = lerp(specRgb, wallRgb, wallZoneMask);
                    float frontAlpha = saturate(max(wallAlpha, specHighlights * 0.82));

                    // Composite the 3D bottom elliptical spout nozzle ring on top
                    frontRgb = lerp(frontRgb, nozzleRingRgb, saturate(nozzleRingMask * 0.92));
                    frontAlpha = max(frontAlpha, nozzleRingMask * 0.90);

                    return half4(saturate(frontRgb), saturate(frontAlpha));
                }

                // -------------------------------------------------------------------------
                // BACK CHAMBER MODE (_IsFrontLens <= 0.5):
                // Sits behind the 3D cubes at z = +0.08f.
                // Renders the airy crystal sky-blue interior with smooth Fresnel radial shading,
                // subtle upper-dome sheen, and the back layer of the blown-glass wall.
                // -------------------------------------------------------------------------
                float fresnel = pow(smoothstep(0.05, 0.96, absX), 1.85);
                // Vertical gradient: slightly deeper sky-blue in the neck & upper shoulders, super airy in the main belly
                float neckTintBoost = smoothstep(0.45, 0.78, IN.uv.y) * 0.16;
                float4 interiorCol = lerp(_CenterColor, _EdgeColor, saturate(fresnel * 0.78 + neckTintBoost));

                // Soft broad white sheen in the upper chamber & neck
                float diagSheen = saturate(-xNorm * 0.45 + (IN.uv.y - 0.30) * 0.85);
                float domeGlow = smoothstep(0.20, 0.80, diagSheen) * interiorMask * 0.26 * _SheenIntensity;
                float backSpec = saturate(domeGlow + lobeCrescent * 0.55 + neckStreaks * 0.65 + spoutStreaks * 0.50);

                interiorCol.rgb = lerp(interiorCol.rgb, _RimGlowColor.rgb, backSpec);
                interiorCol.a   = saturate(interiorCol.a + backSpec * 0.22);

                // Seamlessly transition into the blown-glass wall at the edges
                float3 finalRgb = lerp(interiorCol.rgb, wallRgb, wallZoneMask);
                float finalAlpha = lerp(interiorCol.a, wallAlpha, wallZoneMask);

                return half4(saturate(finalRgb), saturate(finalAlpha));
            }
            ENDHLSL
        }
    }
}
