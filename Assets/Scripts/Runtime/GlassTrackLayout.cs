using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum GlassTrackPreset
    {
        ArchLoop = 0,    // Görsel 1: Dış U + İç Kemer Köprü + Orta Ada
        HeartLoop = 1,   // Görsel 3: Kalp / M-Kıvrımlı Pist + Orta Ada
        UChannel = 2,    // Düzgün U-Kanal
        ORing = 3,       // Kapalı Halka (O-Pist)
        SCurve = 4,      // Kıvrımlı S-Kanal
        DoubleArch = 5,  // Çift Kemerli Pist
        Custom = 6,      // Cam Stüdyosu'nda Serbest Düzenlenen Pist
        OvalRing = 7     // Sabit genişlikli kapalı oval halka (bant) + sağ üstten giriş kolu
    }

    [Serializable]
    public class GlassTrackBranch
    {
        public string name = "Kol";
        [Tooltip("True: İki kol arasına köprü kurar (ortadan iki uca akar). False: Çıkıştan yukarı uzanan ana kol.")]
        public bool isBridge = false;
        public List<Vector2> points = new List<Vector2>();

        public GlassTrackBranch() { }

        public GlassTrackBranch(string name, bool isBridge, params Vector2[] pts)
        {
            this.name = name;
            this.isBridge = isBridge;
            if (pts != null) this.points = new List<Vector2>(pts);
        }

        public GlassTrackBranch Clone()
        {
            return new GlassTrackBranch(name, isBridge, points != null ? points.ToArray() : null);
        }
    }

    /// <summary>
    /// Görsel 1 ve Görsel 3 tarzı çoklu-kollu, iç adalı eğrisel cam pist/kanal geometrisi,
    /// 2D SDF + akış alanı (TrackField2D), Marching Squares duvar çıkarımı ve sıkı top dizilimi.
    /// </summary>
    public static class GlassTrackLayout
    {
        public const int MaxShaderSegments = 64;
        // Duvar collider'ları bu ızgaradan çıkarılır; gerçek fizikte toplar duvara değdiği için daha ince ızgara
        private const int FieldRes = 112;

        public struct TrackSegment
        {
            public Vector2 a;
            public Vector2 b;
            public int branchId;
            public float distA;
            public float distB;
        }

        public struct TrackSlot
        {
            public Vector2 localPos;
            public int branchId;
            public int rowIndex;
            public int laneIndex;
            public float distToExit;
        }

        public sealed class CompiledTrack
        {
            public GlassTrackPreset preset;
            public List<List<Vector2>> polylines = new List<List<Vector2>>();
            public List<bool> polylineIsBridge = new List<bool>();
            public List<TrackSegment> segments = new List<TrackSegment>();
            public float innerHalfWidth;
            public float wallThickness;
            public float ballDiameter;
            public int lanesAcross;
            public Vector2 exitPoint;
            /// <summary>Elle top dizimi için adreslenmiş slotlar (column = kol*şerit+şerit, row = çıkıştan uzaklık sırası).
            /// Level Design ve oyun aynı listeyi kullanır.</summary>
            public List<OrderedPoolLayout.Slot> manualSlots = new List<OrderedPoolLayout.Slot>();
            /// <summary>Kapalı halka (yürüyen bant) olarak tanımlanmış kolun indeksi; yoksa -1.</summary>
            public int closedLoopBranch = -1;
            public Rect innerBounds;
            public Rect outerBounds;
            /// <summary>
            /// Camın "gövdesi": halkalı pistte ekranın üstüne uzanan giriş yolu hariç dış sınır. Camı ekrana
            /// yerleştirmek ve slotları hizalamak için kullanılır (yol hesaba katılırsa halka aşağı itiliyordu).
            /// </summary>
            public Rect bodyOuterBounds;

            // 2D grid field over fieldRect for O(1) physics flow, wall containment, and collider extraction
            public Rect fieldRect;
            public float[] sdf;
            public Vector2[] flowDir;
            public float[] distToExit;
            public Vector2[] wallNormal;

            public void SampleField(Vector2 p, out float signedDist, out Vector2 flow, out float exitDist, out Vector2 inward)
            {
                if (sdf == null || sdf.Length != FieldRes * FieldRes)
                {
                    signedDist = 0f;
                    flow = Vector2.down;
                    exitDist = 0f;
                    inward = Vector2.up;
                    return;
                }

                float u = Mathf.Clamp01((p.x - fieldRect.xMin) / Mathf.Max(0.001f, fieldRect.width)) * (FieldRes - 1);
                float v = Mathf.Clamp01((p.y - fieldRect.yMin) / Mathf.Max(0.001f, fieldRect.height)) * (FieldRes - 1);
                int ix0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, FieldRes - 2);
                int iy0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, FieldRes - 2);
                float tx = u - ix0;
                float ty = v - iy0;

                int i00 = iy0 * FieldRes + ix0;
                int i10 = i00 + 1;
                int i01 = i00 + FieldRes;
                int i11 = i01 + 1;

                signedDist = Mathf.Lerp(Mathf.Lerp(sdf[i00], sdf[i10], tx), Mathf.Lerp(sdf[i01], sdf[i11], tx), ty);
                exitDist = Mathf.Lerp(Mathf.Lerp(distToExit[i00], distToExit[i10], tx), Mathf.Lerp(distToExit[i01], distToExit[i11], tx), ty);

                Vector2 f0 = Vector2.Lerp(flowDir[i00], flowDir[i10], tx);
                Vector2 f1 = Vector2.Lerp(flowDir[i01], flowDir[i11], tx);
                flow = Vector2.Lerp(f0, f1, ty);
                if (flow.sqrMagnitude > 0.0001f) flow.Normalize();
                else flow = Vector2.down;

                Vector2 n0 = Vector2.Lerp(wallNormal[i00], wallNormal[i10], tx);
                Vector2 n1 = Vector2.Lerp(wallNormal[i01], wallNormal[i11], tx);
                inward = Vector2.Lerp(n0, n1, ty);
                if (inward.sqrMagnitude > 0.0001f) inward.Normalize();
                else inward = Vector2.up;
            }

            /// <summary>
            /// Marching Squares ile pistin dış duvarlarını ve tüm iç ada boşluklarını
            /// kapalı/açık duvar segmentleri olarak çıkarır. Her segmentin normali duvar dışına (sdf > 0) bakar.
            /// </summary>
            public List<(Vector2 a, Vector2 b)> ExtractWallSegments()
            {
                var segs = new List<(Vector2 a, Vector2 b)>(256);
                if (sdf == null || sdf.Length != FieldRes * FieldRes) return segs;

                float dx = fieldRect.width / (FieldRes - 1);
                float dy = fieldRect.height / (FieldRes - 1);

                for (int iy = 0; iy < FieldRes - 1; iy++)
                {
                    float y0 = fieldRect.yMin + iy * dy;
                    float y1 = y0 + dy;
                    for (int ix = 0; ix < FieldRes - 1; ix++)
                    {
                        float x0 = fieldRect.xMin + ix * dx;
                        float x1 = x0 + dx;

                        float d00 = sdf[iy * FieldRes + ix];
                        float d10 = sdf[iy * FieldRes + ix + 1];
                        float d11 = sdf[(iy + 1) * FieldRes + ix + 1];
                        float d01 = sdf[(iy + 1) * FieldRes + ix];

                        int mask = (d00 < 0f ? 1 : 0) | (d10 < 0f ? 2 : 0) | (d11 < 0f ? 4 : 0) | (d01 < 0f ? 8 : 0);
                        if (mask == 0 || mask == 15) continue;

                        Vector2 Interp(Vector2 pa, Vector2 pb, float da, float db)
                        {
                            float denom = da - db;
                            float t = Mathf.Abs(denom) > 1e-5f ? Mathf.Clamp01(da / denom) : 0.5f;
                            return Vector2.Lerp(pa, pb, t);
                        }

                        Vector2 p00 = new Vector2(x0, y0);
                        Vector2 p10 = new Vector2(x1, y0);
                        Vector2 p11 = new Vector2(x1, y1);
                        Vector2 p01 = new Vector2(x0, y1);

                        Vector2 eB = Interp(p00, p10, d00, d10);
                        Vector2 eR = Interp(p10, p11, d10, d11);
                        Vector2 eT = Interp(p01, p11, d01, d11);
                        Vector2 eL = Interp(p00, p01, d00, d01);

                        // Orient each segment so left-hand side (or normal) is consistent; we verify normal via SDF gradient
                        void AddOriented(Vector2 u, Vector2 v)
                        {
                            if ((u - v).sqrMagnitude < 1e-6f) return;
                            Vector2 mid = (u + v) * 0.5f;
                            Vector2 grad = EstimateSdfGradient(mid);
                            Vector2 edge = v - u;
                            // Ensure (edge.y, -edge.x) points outward (same direction as grad)
                            Vector2 rightNorm = new Vector2(edge.y, -edge.x);
                            if (Vector2.Dot(rightNorm, grad) < 0f) segs.Add((v, u));
                            else segs.Add((u, v));
                        }

                        switch (mask)
                        {
                            case 1: AddOriented(eL, eB); break;
                            case 2: AddOriented(eB, eR); break;
                            case 3: AddOriented(eL, eR); break;
                            case 4: AddOriented(eR, eT); break;
                            case 5: AddOriented(eL, eT); AddOriented(eR, eB); break;
                            case 6: AddOriented(eB, eT); break;
                            case 7: AddOriented(eL, eT); break;
                            case 8: AddOriented(eT, eL); break;
                            case 9: AddOriented(eT, eB); break;
                            case 10: AddOriented(eB, eL); AddOriented(eT, eR); break;
                            case 11: AddOriented(eT, eR); break;
                            case 12: AddOriented(eR, eL); break;
                            case 13: AddOriented(eR, eB); break;
                            case 14: AddOriented(eB, eL); break;
                        }
                    }
                }
                return segs;
            }

            /// <summary>Noktanın kapalı halka (bant) orta çizgisine uzaklığı; halka yoksa çok büyük.</summary>
            public float SampleDistToRingCenter(Vector2 p)
            {
                if (closedLoopBranch < 0 || closedLoopBranch >= polylines.Count) return float.MaxValue;
                var poly = polylines[closedLoopBranch];
                float best = float.MaxValue;
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    Vector2 a = poly[i], ab = poly[i + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
                }
                return Mathf.Sqrt(best);
            }

            public Vector2 EstimateSdfGradient(Vector2 p)
            {
                const float eps = 0.015f;
                SampleField(p + new Vector2(eps, 0f), out float rx, out _, out _, out _);
                SampleField(p - new Vector2(eps, 0f), out float lx, out _, out _, out _);
                SampleField(p + new Vector2(0f, eps), out float ty, out _, out _, out _);
                SampleField(p - new Vector2(0f, eps), out float by, out _, out _, out _);
                Vector2 g = new Vector2(rx - lx, ty - by);
                return g.sqrMagnitude > 1e-8f ? g.normalized : Vector2.up;
            }
        }

        // ==================== Hazır Pist Şekilleri ====================

        /// <summary>
        /// Hazır pist şekilleri. Hepsi aynı yapıdadır: dış şekil ile ortadaki boşluk aynı biçimdedir ve arada
        /// sabit genişlikli kapalı bir yol (yürüyen bant) döner. Ayrı giriş kolu / boş kalan çıkıntı yoktur.
        /// Yol alttaki çıkıştan başlar, saat yönünde döner ve çıkışa kapanır. Tek kapalı kol olduğu için cam her
        /// yerde aynı kalınlıkta ve pürüzsüz çizilir; bant şeritleri camın içine birebir oturur.
        /// </summary>
        public static List<GlassTrackBranch> BuildPresetBranches(GlassTrackPreset preset)
        {
            return BuildPresetBranches(preset, 0.5f);
        }

        /// <summary>
        /// stretch (0..1): şeklin uzayabilen kısmının oranı (U'da kolların boyu). Top sayısına göre
        /// BuildForLevel seçer ki yol topla dolu olsun, boş kalmasın.
        /// </summary>
        public static List<GlassTrackBranch> BuildPresetBranches(GlassTrackPreset preset, float stretch)
        {
            var list = new List<GlassTrackBranch>();
            Vector2 c = new Vector2(0f, 0.06f);
            switch (preset)
            {
                case GlassTrackPreset.ArchLoop:
                default:
                    // U: dış şekil U, ortadaki boşluk da aynı U; yol U boşluğun etrafında döner (kollar yolun parçası)
                    list.Add(BuildULoop(Mathf.Lerp(0.0f, 0.50f, Mathf.Clamp01(stretch))));
                    break;

                case GlassTrackPreset.HeartLoop:
                    list.Add(BuildRingBranch("Kalp Halka", th => c + HeartRing(th)));
                    break;

                case GlassTrackPreset.UChannel:
                    list.Add(BuildRingBranch("Stadyum Halka", th => c + SuperEllipse(th, 1.04f, 0.52f, 4.0f)));
                    break;

                case GlassTrackPreset.ORing:
                case GlassTrackPreset.OvalRing:
                    list.Add(BuildRingBranch("Oval Halka", th => c + SuperEllipse(th, 0.98f, 0.62f, 2.6f)));
                    break;

                case GlassTrackPreset.SCurve:
                    list.Add(BuildRingBranch("Fasulye Halka", th => c + BeanRing(th)));
                    break;

                case GlassTrackPreset.DoubleArch:
                    list.Add(BuildRingBranch("Kare Halka", th => c + SuperEllipse(th, 0.92f, 0.66f, 4.5f)));
                    break;
            }
            return list;
        }

        /// <summary>U şeklinin omurgası ile yol ortası arasındaki mesafe (boşluğun yarı kalınlığı + yolun yarı genişliği).</summary>
        private const float ULoopOffset = 0.38f;
        /// <summary>U omurgasının yarı genişliği (alttaki yarım çemberin yarıçapı).</summary>
        private const float ULoopHalfSpan = 0.74f;

        /// <summary>
        /// U yolu: U biçimli bir omurganın iki yanından ULoopOffset kadar uzakta geçen kapalı yol.
        /// Böylece hem dış cam hem ortadaki boşluk aynı U biçimini alır. armH: kolların düz kısmının boyu.
        /// Alt ortadan (çıkış) başlar: dış alt yay sola -> sol kol dışı yukarı -> sol kol tepesi -> sol kol içi aşağı
        /// -> iç alt yay sağa -> sağ kol içi yukarı -> sağ kol tepesi -> sağ kol dışı aşağı -> dış alt yay çıkışa.
        /// </summary>
        private static GlassTrackBranch BuildULoop(float armH)
        {
            const float a = ULoopHalfSpan, w = ULoopOffset;
            float cy = 0f;
            float top = cy + armH;
            float rOut = a + w, rIn = a - w;
            var pts = new List<Vector2>();

            void Arc(Vector2 center, float r, float fromDeg, float toDeg, int steps, bool includeEnd)
            {
                int n = includeEnd ? steps : steps - 1;
                for (int i = 0; i <= n; i++)
                {
                    float ang = Mathf.Lerp(fromDeg, toDeg, (float)i / steps) * Mathf.Deg2Rad;
                    pts.Add(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                }
            }
            void Line(Vector2 from, Vector2 to)
            {
                float len = Vector2.Distance(from, to);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.18f));
                for (int i = 1; i < steps; i++) pts.Add(Vector2.Lerp(from, to, (float)i / steps));
            }

            Vector2 cBottom = new Vector2(0f, cy);
            Arc(cBottom, rOut, -90f, -180f, 6, true);                                   // dış alt yay, sola
            Line(new Vector2(-rOut, cy), new Vector2(-rOut, top));                       // sol kol dışı, yukarı
            Arc(new Vector2(-a, top), w, 180f, 0f, 6, true);                              // sol kol tepesi
            Line(new Vector2(-rIn, top), new Vector2(-rIn, cy));                          // sol kol içi, aşağı
            Arc(cBottom, rIn, 180f, 360f, 8, true);                                       // iç alt yay, sağa
            Line(new Vector2(rIn, cy), new Vector2(rIn, top));                            // sağ kol içi, yukarı
            Arc(new Vector2(a, top), w, 180f, 0f, 6, true);                               // sağ kol tepesi
            Line(new Vector2(rOut, top), new Vector2(rOut, cy));                          // sağ kol dışı, aşağı
            Arc(cBottom, rOut, 0f, -90f, 6, false);                                       // dış alt yay, çıkışa

            // Ardışık çakışan noktaları ayıkla, kapat
            var clean = new List<Vector2>(pts.Count + 1);
            for (int i = 0; i < pts.Count; i++)
                if (clean.Count == 0 || (pts[i] - clean[clean.Count - 1]).sqrMagnitude > 1e-5f) clean.Add(pts[i]);
            clean.Add(clean[0]);
            var br = new GlassTrackBranch("U Yol", false);
            br.points.AddRange(clean);
            return br;
        }

        private const int RingControlPoints = 32;

        /// <summary>th = -90° (alt, çıkış) noktasından başlayıp saat yönünde dönen kapalı halka kolu üretir.</summary>
        private static GlassTrackBranch BuildRingBranch(string name, System.Func<float, Vector2> shape)
        {
            var ring = new GlassTrackBranch(name, false);
            for (int i = 0; i <= RingControlPoints; i++)
            {
                float th = -Mathf.PI * 0.5f - (i % RingControlPoints) * (Mathf.PI * 2f / RingControlPoints);
                ring.points.Add(shape(th));
            }
            return ring;
        }

        /// <summary>Yuvarlatılmış kalp: üstte iki tümsek ve ortada çukur, altta küt uç (kanal ucu sıkıştırmasın).</summary>
        private static Vector2 HeartRing(float th)
        {
            float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            // Elips taban + üstte ortada çukur (iki tümsek) + altta hafif sivrilme
            float rx = 1.02f, ry = 0.60f;
            float x = rx * cs;
            float y = ry * sn;
            float topDip = Mathf.Max(0f, sn) * Mathf.Exp(-(cs * cs) / 0.06f) * 0.20f;
            float lobes = Mathf.Max(0f, sn) * (1f - Mathf.Exp(-(cs * cs) / 0.06f)) * 0.06f;
            float bottomPinch = Mathf.Max(0f, -sn);
            x *= 1f - 0.22f * bottomPinch * bottomPinch;
            y += lobes - topDip - 0.08f * bottomPinch * bottomPinch;
            return new Vector2(x, y);
        }

        /// <summary>Fasulye: sol yarısı dolgun, sağ yarısı daha ince, üst kenarda hafif içe kıvrım.</summary>
        private static Vector2 BeanRing(float th)
        {
            float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            float rx = 1.00f;
            float ry = 0.62f - 0.10f * Mathf.Max(0f, cs);
            float x = rx * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.8f);
            float y = ry * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.8f);
            y -= Mathf.Max(0f, sn) * Mathf.Exp(-((cs - 0.15f) * (cs - 0.15f)) / 0.08f) * 0.12f;
            return new Vector2(x, y);
        }

        private static Vector2 SuperEllipse(float theta, float rx, float ry, float exponent)
        {
            float cs = Mathf.Cos(theta), sn = Mathf.Sin(theta);
            float e = 2f / Mathf.Max(1f, exponent);
            return new Vector2(rx * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), e), ry * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e));
        }

        /// <summary>İlk ve son noktası çakışan (kapalı) polyline mı?</summary>
        public static bool IsClosedPolyline(List<Vector2> pts)
        {
            return pts != null && pts.Count >= 4 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f;
        }

        public static List<GlassTrackBranch> CloneBranches(List<GlassTrackBranch> src)
        {
            var res = new List<GlassTrackBranch>();
            if (src == null) return res;
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i] != null) res.Add(src[i].Clone());
            }
            return res;
        }

        // ==================== Derleme, Otomatik Sığdırma ve Sıkı Top Dizilimi ====================

        public static void BuildForLevel(
            GlassTrackPreset preset,
            List<GlassTrackBranch> customBranches,
            List<LevelMonsterEntry> bottomToTopGroups,
            List<ManualPoolBallEntry> manualBalls,
            bool useManualLayout,
            int lanesAcrossSetting,
            bool autoFitToBalls,
            float manualScale,
            float wallThickness,
            out CompiledTrack compiled,
            out OrderedPoolLayout.Result layout)
        {
            BuildForLevel(preset, customBranches, bottomToTopGroups, manualBalls, useManualLayout, lanesAcrossSetting,
                autoFitToBalls, manualScale, wallThickness, 0f, float.NaN, out compiled, out layout);
        }

        /// <param name="entryRoadTopLocalY">Giriş yolunun uzanacağı cam yerel Y (ekranın üst kenarı); NaN: sabit boy.</param>
        /// <param name="hiddenRatio">Gizli kuyruk oranı: bant, toplamın (1 - oran) kadarına göre boyutlanır ve tam
        /// dolu başlar; sığmayan toplar layout.overflowColors'ta girişte bekler.</param>
        public static void BuildForLevel(
            GlassTrackPreset preset,
            List<GlassTrackBranch> customBranches,
            List<LevelMonsterEntry> bottomToTopGroups,
            List<ManualPoolBallEntry> manualBalls,
            bool useManualLayout,
            int lanesAcrossSetting,
            bool autoFitToBalls,
            float manualScale,
            float wallThickness,
            float hiddenRatio,
            float entryRoadTopLocalY,
            out CompiledTrack compiled,
            out OrderedPoolLayout.Result layout,
            int manualCapacity = 0,
            int manualRetry = 0,
            int manualNeed = 0)
        {
            // Hedef top listesi (alttan üste)
            List<PuzzleColor> orderedColors = ExpandBallColors(bottomToTopGroups, manualBalls, useManualLayout);
            int totalCount = orderedColors.Count;
            // Elle dizimde pist sabit kapasiteye göre boyutlanır: top ekleyip silmek slotları kaydırmaz
            if (useManualLayout && manualCapacity > totalCount) totalCount = manualCapacity;
            // Gizli kuyruk: toplamın (1 - oran) kadarı bantta başlar, kalanı sırasıyla girişte bekler.
            // Pist, banttaki top sayısına göre boyutlanır.
            hiddenRatio = Mathf.Clamp(hiddenRatio, 0f, 0.6f);
            bool useHidden = hiddenRatio > 0.001f && !useManualLayout && totalCount > 8;
            int targetCount = useHidden
                ? Mathf.Max(6, Mathf.RoundToInt(totalCount * (1f - hiddenRatio)))
                : totalCount;

            bool isCustom = preset == GlassTrackPreset.Custom && customBranches != null && customBranches.Count > 0;
            int presetLanes = -1;
            List<GlassTrackBranch> sourceBranches = isCustom
                ? CloneBranches(customBranches)
                : ChoosePresetForCount(preset, targetCount, lanesAcrossSetting, autoFitToBalls, out presetLanes);

            if (sourceBranches.Count == 0)
            {
                sourceBranches = BuildPresetBranches(GlassTrackPreset.ArchLoop);
            }

            bool addEntryRoad = useHidden && !isCustom && sourceBranches.Count > 0 && IsClosedPolyline(sourceBranches[0].points);

            // Halkalı pistlerde yol, ortadaki boşluktan dar kalmalı: şerit sayısı top sayısına göre 2-4
            bool isRing = false;
            for (int i = 0; i < sourceBranches.Count; i++)
                if (sourceBranches[i] != null && !sourceBranches[i].isBridge && IsClosedPolyline(sourceBranches[i].points)) { isRing = true; break; }
            int lanes = lanesAcrossSetting > 0
                ? Mathf.Clamp(lanesAcrossSetting, 2, 6)
                : presetLanes > 0 ? presetLanes
                : (isRing
                    ? (targetCount <= 28 ? 3 : (targetCount <= 84 ? 4 : 5))
                    : ((preset == GlassTrackPreset.ArchLoop || preset == GlassTrackPreset.HeartLoop) ? 4 : (targetCount <= 20 ? 3 : (targetCount <= 72 ? 4 : 5))));
            // Kanal yarı genişliğinin ölçeğe oranı üst sınırı (halkada ada boşluğu net görünsün)
            float maxHalfWidthRatio = isRing ? RingHalfWidthRatio(preset) : 0.375f;

            float chosenScale = Mathf.Clamp(manualScale > 0.05f ? manualScale : 1.08f, 0.65f, 1.40f);
            float chosenDiam = 0.102f;
            float armTrim = 1.0f;

            if (autoFitToBalls && targetCount > 0)
            {
                ChooseBestFitParameters(
                    preset, sourceBranches, targetCount, lanes, maxHalfWidthRatio,
                    out chosenScale, out chosenDiam, out armTrim);
            }
            else
            {
                float maxDiamForHole = (Mathf.Min(0.36f, maxHalfWidthRatio) * chosenScale) / Mathf.Max(1f, lanes * 0.495f);
                chosenDiam = Mathf.Clamp(maxDiamForHole * 0.60f, 0.068f, 0.114f);
            }

            float innerHalfW = lanes * chosenDiam * 0.495f + 0.012f;
            float clampedWall = Mathf.Clamp(wallThickness > 0.01f ? wallThickness : 0.092f, 0.045f, 0.14f);

            // Gizli kuyruk: halkanın en üst noktasından yukarı, ekranın üst kenarına kadar çıkan giriş yolu.
            // Bekleyen toplar bu yolda sıra halinde durur, ekranın dışından gelir ve halkada yer açıldıkça
            // aşağı akıp banda katılır. (Pist ölçeği ve top boyu hesabına katılmaz.)
            if (addEntryRoad) sourceBranches.Add(BuildEntryRoad(sourceBranches[0].points));

            List<GlassTrackBranch> fittedBranches = TransformBranches(sourceBranches, chosenScale, armTrim);
            if (addEntryRoad && !float.IsNaN(entryRoadTopLocalY))
            {
                var road = fittedBranches[fittedBranches.Count - 1].points;
                Vector2 j = road[0];
                float topY = Mathf.Max(j.y + 0.4f, entryRoadTopLocalY);
                road.Clear();
                road.Add(j);
                road.Add(new Vector2(j.x, Mathf.Lerp(j.y, topY, 0.5f)));
                road.Add(new Vector2(j.x, topY));
            }
            compiled = CompileTrack(preset, fittedBranches, innerHalfW, clampedWall, chosenDiam, lanes);

            List<TrackSlot> allSlots = GeneratePackedTrackSlots(compiled);
            if (autoFitToBalls)
            {
                for (int pass = 0; pass < 2 && targetCount > allSlots.Count && allSlots.Count > 0; pass++)
                {
                    float shrink = Mathf.Sqrt(allSlots.Count / (float)(targetCount + 5));
                    chosenDiam = Mathf.Clamp(chosenDiam * Mathf.Min(shrink, 0.94f), 0.068f, 0.125f);
                    innerHalfW = lanes * chosenDiam * 0.495f + 0.012f;
                    compiled = CompileTrack(preset, fittedBranches, innerHalfW, clampedWall, chosenDiam, lanes);
                    allSlots = GeneratePackedTrackSlots(compiled);
                }
            }

            // Elle dizimde gereken slot sayısı (ilk çağrıda belirlenir; tekrar denemelerde büyüyen pist hedefiyle karışmaz).
            // Sabit kapasiteye küçük pay: topları sürükleyip yeniden dizmek için birkaç boş slot kalsın.
            int headroom = manualCapacity > 0 ? Mathf.Max(6, Mathf.CeilToInt(manualCapacity * 0.06f)) : 0;
            int need = !useManualLayout ? 0
                : (manualNeed > 0 ? manualNeed : Mathf.Max(orderedColors.Count, manualCapacity + headroom));

            if (compiled != null)
            {
                if (compiled.closedLoopBranch >= 0)
                {
                    // Halka: önce tam top aralığıyla diz; yer yetmezse pisti değiştirmeden (otomatik dizilimle aynı pist
                    // kalsın) şerit boyunca adımı sıkılaştır (en fazla %18 değme). Pist ancak bu da yetmezse büyütülür.
                    float stepFactor = 1.01f;
                    var ms = BuildManualTrackSlots(GenerateManualRingSlots(compiled, allSlots, stepFactor), chosenDiam, lanes);
                    for (int it = 0; it < 3 && need > 0 && ms.Count < need && stepFactor > 0.821f; it++)
                    {
                        stepFactor = Mathf.Clamp(stepFactor * ms.Count / need * 0.99f, 0.82f, stepFactor - 0.01f);
                        ms = BuildManualTrackSlots(GenerateManualRingSlots(compiled, allSlots, stepFactor), chosenDiam, lanes);
                    }
                    compiled.manualSlots = ms;
                }
                else
                {
                    compiled.manualSlots = BuildManualTrackSlots(allSlots, chosenDiam, lanes);
                }
            }

            // Elle dizim: yine de yer kalmadıysa pisti biraz büyütüp tekrar dene
            if (useManualLayout && compiled != null && manualRetry < 8)
            {
                if (compiled.manualSlots.Count < need)
                {
                    // Küçük adımlarla büyüt: oto-sığdırma topu gereğinden fazla küçültmesin
                    int grown = targetCount + Mathf.Max(2, Mathf.CeilToInt((need - compiled.manualSlots.Count) * 0.6f));
                    BuildForLevel(preset, customBranches, bottomToTopGroups, manualBalls, true, lanesAcrossSetting,
                        autoFitToBalls, manualScale, wallThickness, hiddenRatio, entryRoadTopLocalY,
                        out compiled, out layout, grown, manualRetry + 1, need);
                    return;
                }
            }

            layout = useManualLayout
                ? BuildManualResult(compiled, compiled != null ? compiled.manualSlots : BuildManualTrackSlots(allSlots, chosenDiam, lanes), manualBalls, chosenDiam, lanes)
                : BuildOrderedResultFromSlots(allSlots, orderedColors, bottomToTopGroups, false, chosenDiam, lanes, compiled, targetCount);
        }

        /// <summary>
        /// Pist slotlarına elle dizim adresi verir: çıkışa en yakından uzağa sıralanır; column = kol*şerit+şerit,
        /// row = çıkışa uzaklık bandı (aynı adrese düşen slot bir sonraki boş sıraya kayar). Sıra 0 = çıkışa en yakın.
        /// </summary>
        public static List<OrderedPoolLayout.Slot> BuildManualTrackSlots(List<TrackSlot> allSlots, float ballDiam, int lanes)
        {
            var res = new List<OrderedPoolLayout.Slot>();
            if (allSlots == null || allSlots.Count == 0) return res;
            float bandSize = Mathf.Max(0.05f, ballDiam * 0.98f);
            int laneCount = Mathf.Max(1, lanes);
            var sorted = new List<TrackSlot>(allSlots);
            sorted.Sort((a, b) =>
            {
                int bandA = Mathf.RoundToInt(a.distToExit / bandSize);
                int bandB = Mathf.RoundToInt(b.distToExit / bandSize);
                if (bandA != bandB) return bandA.CompareTo(bandB);
                if (a.branchId != b.branchId) return a.branchId.CompareTo(b.branchId);
                return a.laneIndex.CompareTo(b.laneIndex);
            });
            var used = new HashSet<(int, int)>();
            // Halka şeritleri iç virajlarda ve kol birleşimlerinde iç içe slot üretebiliyor: elle dizimde üst üste binen
            // slotları ele (çıkışa yakın olan kalır) ki iki top aynı yere konamasın
            // Eşik, sıkılaştırılmış şerit adımının (en az 0.82d) altında kalır ki aynı şeritteki komşular elenmesin
            float minSepSq = (ballDiam * 0.78f) * (ballDiam * 0.78f);
            var accepted = new List<Vector2>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                var s = sorted[i];
                bool overlaps = false;
                for (int k = 0; k < accepted.Count; k++)
                {
                    if ((accepted[k] - s.localPos).sqrMagnitude < minSepSq) { overlaps = true; break; }
                }
                if (overlaps) continue;
                accepted.Add(s.localPos);
                int col = Mathf.Max(0, s.branchId) * laneCount + s.laneIndex;
                int row = Mathf.Max(0, Mathf.RoundToInt(s.distToExit / bandSize));
                while (!used.Add((col, row))) row++;
                res.Add(new OrderedPoolLayout.Slot { column = col, row = row, localPos = s.localPos });
            }
            return res;
        }

        /// <summary>
        /// Halka pist (kemer, kalp, O, oval) için elle dizim slotları. Bant slotları orta çizgiye göre eşit açılır:
        /// virajda dış şerit seyrekleşir (aralarda boşluk), iç şerit iç içe geçer. Burada her şerit kendi eğrisi boyunca
        /// top çapı aralıkla, komşu şeritler yarım adım kaydırılarak (petek) dizilir. Halka dışı kollar (giriş yolu) aynen kalır.
        /// Oyunda toplar fizikle hareket ettiği için bu konumlar yalnızca doğma noktasıdır.
        /// </summary>
        private static List<TrackSlot> GenerateManualRingSlots(CompiledTrack track, List<TrackSlot> baseSlots, float stepFactor = 1.01f)
        {
            int ring = track.closedLoopBranch;
            if (ring < 0 || ring >= track.polylines.Count) return baseSlots;
            var poly = new List<Vector2>(track.polylines[ring]);
            if (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude < 1e-6f) poly.RemoveAt(poly.Count - 1);
            if (poly.Count < 4) return baseSlots;

            float d = Mathf.Max(0.02f, track.ballDiameter);
            float r = d * 0.5f;
            var center = ResampleClosedByStep(poly, d * 0.12f);
            int n = center.Count;
            if (n < 8) return baseSlots;

            int lanes = Mathf.Max(1, track.lanesAcross);
            // Bant ile aynı enine şerit aralığı (petek: ~0.87d)
            float maxSpan = Mathf.Max(0f, track.innerHalfWidth * 2f - d * 1.12f);
            float idealSpan = d * 0.87f * Mathf.Max(0, lanes - 1);
            float minSpan = Mathf.Min(maxSpan, d * 0.76f * Mathf.Max(0, lanes - 1));
            float span = lanes > 1 ? Mathf.Clamp(idealSpan, minSpan, Mathf.Max(minSpan, maxSpan)) : 0f;
            float laneSpacing = lanes > 1 ? span / (lanes - 1) : 0f;
            float exitStopperTop = (track.exitPoint.y - track.innerHalfWidth) - 0.045f + 0.04f;

            var res = new List<TrackSlot>();
            foreach (var s in baseSlots) if (s.branchId != ring) res.Add(s);

            for (int l = 0; l < lanes; l++)
            {
                float off = (l - (lanes - 1) * 0.5f) * laneSpacing;
                var lane = new List<Vector2>(n);
                for (int i = 0; i < n; i++)
                {
                    Vector2 tang = (center[(i + 1) % n] - center[(i - 1 + n) % n]).normalized;
                    lane.Add(center[i] + new Vector2(-tang.y, tang.x) * off);
                }
                float len = 0f;
                for (int i = 0; i < n; i++) len += Vector2.Distance(lane[i], lane[(i + 1) % n]);
                int count = Mathf.Max(1, Mathf.FloorToInt(len / (d * Mathf.Clamp(stepFactor, 0.8f, 1.2f))));
                float step = len / count;
                float start = (l % 2 == 1) ? step * 0.5f : 0f;

                int seg = 0;
                float segStart = 0f;
                for (int k = 0; k < count; k++)
                {
                    float target = start + k * step;
                    while (seg < n)
                    {
                        float segLen = Vector2.Distance(lane[seg], lane[(seg + 1) % n]);
                        if (segStart + segLen >= target || seg == n - 1) break;
                        segStart += segLen;
                        seg++;
                    }
                    Vector2 a = lane[seg], b = lane[(seg + 1) % n];
                    float sl = Vector2.Distance(a, b);
                    Vector2 p = sl > 1e-6f ? Vector2.Lerp(a, b, Mathf.Clamp01((target - segStart) / sl)) : a;

                    track.SampleField(p, out float sDist, out _, out float exitDist, out _);
                    if (sDist > -r * 1.02f) continue;
                    if (p.y - r < exitStopperTop + 0.012f) continue;
                    res.Add(new TrackSlot { localPos = p, branchId = ring, rowIndex = k, laneIndex = l, distToExit = exitDist });
                }
            }
            return res;
        }

        private static List<Vector2> ResampleClosedByStep(List<Vector2> pts, float step)
        {
            var res = new List<Vector2>();
            int n = pts.Count;
            float total = 0f;
            for (int i = 0; i < n; i++) total += Vector2.Distance(pts[i], pts[(i + 1) % n]);
            int count = Mathf.Clamp(Mathf.CeilToInt(total / Mathf.Max(0.005f, step)), 8, 4000);
            float dl = total / count;
            int seg = 0;
            float segStart = 0f;
            for (int k = 0; k < count; k++)
            {
                float target = k * dl;
                while (seg < n - 1 && segStart + Vector2.Distance(pts[seg], pts[(seg + 1) % n]) < target)
                {
                    segStart += Vector2.Distance(pts[seg], pts[(seg + 1) % n]);
                    seg++;
                }
                Vector2 a = pts[seg], b = pts[(seg + 1) % n];
                float sl = Vector2.Distance(a, b);
                res.Add(sl > 1e-6f ? Vector2.Lerp(a, b, Mathf.Clamp01((target - segStart) / sl)) : a);
            }
            return res;
        }

        /// <summary>
        /// Halka pistte noktanın halka orta çizgisi boyunca konumu (0..1, tek yön). Halka yoksa -1.
        /// Çıkışa uzaklıktan farklı olarak sol ve sağ kolu karıştırmaz.
        /// </summary>
        public static float RingParam(CompiledTrack track, Vector2 p)
        {
            if (track == null || track.closedLoopBranch < 0 || track.closedLoopBranch >= track.polylines.Count) return -1f;
            var poly = track.polylines[track.closedLoopBranch];
            int n = poly.Count;
            if (n < 3) return -1f;
            bool closed = (poly[0] - poly[n - 1]).sqrMagnitude < 1e-6f;
            int segs = closed ? n - 1 : n;
            float total = 0f, best = float.MaxValue, bestAt = 0f;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector2 ab = b - a;
                float len = ab.magnitude;
                float t = len > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (len * len)) : 0f;
                float dsq = (a + ab * t - p).sqrMagnitude;
                if (dsq < best) { best = dsq; bestAt = total + t * len; }
                total += len;
            }
            return total > 1e-6f ? bestAt / total : -1f;
        }

        /// <summary>Pist yerel konumunu pist dış sınırlarına göre 0..1 aralığına çevirir (pist ölçeklense de değişmez).</summary>
        public static Vector2 NormalizeTrackPos(CompiledTrack track, Vector2 local)
        {
            Rect b = track.outerBounds;
            return new Vector2((local.x - b.xMin) / Mathf.Max(1e-4f, b.width), (local.y - b.yMin) / Mathf.Max(1e-4f, b.height));
        }

        public static Vector2 DenormalizeTrackPos(CompiledTrack track, Vector2 norm)
        {
            Rect b = track.outerBounds;
            return new Vector2(b.xMin + norm.x * b.width, b.yMin + norm.y * b.height);
        }

        /// <summary>
        /// Elle dizilen her topun hangi pist slotuna oturacağını bulur (sonuç: top başına slot indeksi, -1 = yer yok).
        /// 1) Konumu kayıtlı ve adresi hâlâ o konumdaki slotu gösteren toplar doğrudan yerleşir.
        /// 2) Konumu kayıtlı olmayan eski toplar adresleriyle eşleşir.
        /// 3) Konumu kayıtlı ama slot düzeni değişmiş toplar kayıtlı konuma en yakın boş slota oturur.
        /// 4) Kalanlar çıkışa en yakın boş slota geçer.
        /// Oyun ve Level Design aynı fonksiyonu kullanır.
        /// </summary>
        public static int[] ResolveManualAssignments(CompiledTrack track, List<OrderedPoolLayout.Slot> slots, List<ManualPoolBallEntry> manualBalls)
        {
            int n = manualBalls != null ? manualBalls.Count : 0;
            var result = new int[n];
            for (int i = 0; i < n; i++) result[i] = -1;
            if (n == 0 || slots == null || slots.Count == 0) return result;

            var index = new Dictionary<(int, int), int>(slots.Count);
            for (int i = 0; i < slots.Count; i++) index[(slots[i].column, slots[i].row)] = i;
            var taken = new bool[slots.Count];
            float d = track != null ? Mathf.Max(0.02f, track.ballDiameter) : 0.1f;
            float snapTolSq = (d * 0.45f) * (d * 0.45f);
            var wanted = new Vector2[n];

            // 1) ve 2): adres eşleşmesi
            for (int i = 0; i < n; i++)
            {
                var mb = manualBalls[i];
                if (mb == null) continue;
                if (mb.hasTrackPos && track != null) wanted[i] = DenormalizeTrackPos(track, mb.trackPos);
                if (!index.TryGetValue((mb.column, mb.row), out int si) || taken[si]) continue;
                if (mb.hasTrackPos && track != null && (slots[si].localPos - wanted[i]).sqrMagnitude > snapTolSq) continue;
                taken[si] = true;
                result[i] = si;
            }

            // 3) kayıtlı konuma en yakın boş slot (en yakın çiftler önce)
            var pairs = new List<(float dist, int ball, int slot)>();
            for (int i = 0; i < n; i++)
            {
                var mb = manualBalls[i];
                if (result[i] >= 0 || mb == null || !mb.hasTrackPos || track == null) continue;
                for (int s = 0; s < slots.Count; s++)
                    if (!taken[s]) pairs.Add(((slots[s].localPos - wanted[i]).sqrMagnitude, i, s));
            }
            if (pairs.Count > 0)
            {
                pairs.Sort((a, b) => a.dist.CompareTo(b.dist));
                foreach (var p in pairs)
                {
                    if (result[p.ball] >= 0 || taken[p.slot]) continue;
                    taken[p.slot] = true;
                    result[p.ball] = p.slot;
                }
            }

            // 4) kalanlar sıradaki boş slota
            int next = 0;
            for (int i = 0; i < n; i++)
            {
                if (result[i] >= 0 || manualBalls[i] == null) continue;
                while (next < slots.Count && taken[next]) next++;
                if (next >= slots.Count) break;
                taken[next] = true;
                result[i] = next;
            }
            return result;
        }

        /// <summary>Elle dizilen topları ResolveManualAssignments ile pist slotlarına koyar; yer kalmayanlar taşar.</summary>
        private static OrderedPoolLayout.Result BuildManualResult(
            CompiledTrack track, List<OrderedPoolLayout.Slot> slots, List<ManualPoolBallEntry> manualBalls, float ballDiam, int lanes)
        {
            var res = new OrderedPoolLayout.Result
            {
                balls = new List<OrderedPoolLayout.PoolBall>(),
                diameter = ballDiam,
                laneWidth = lanes,
                totalColumns = Mathf.Max(1, lanes * 2),
                overflowColors = new List<PuzzleColor>()
            };
            if (slots == null || slots.Count == 0 || manualBalls == null || manualBalls.Count == 0) return res;

            int[] assign = ResolveManualAssignments(track, slots, manualBalls);
            for (int i = 0; i < manualBalls.Count; i++)
            {
                var mb = manualBalls[i];
                if (mb == null) continue;
                if (assign[i] < 0) { res.overflowColors.Add(mb.color); continue; }
                var s = slots[assign[i]];
                res.balls.Add(new OrderedPoolLayout.PoolBall { color = mb.color, ballColumn = s.column, row = s.row, localPos = s.localPos });
            }
            return res;
        }

        /// <summary>Giriş yolunun boyu (pist ölçeği öncesi birim).</summary>
        private const float EntryRoadLength = 0.78f;

        /// <summary>Halkanın en üst noktasından dümdüz yukarı çıkan giriş yolu (ilk nokta halkada: birleşim).</summary>
        private static GlassTrackBranch BuildEntryRoad(List<Vector2> ringPts)
        {
            int top = 0;
            for (int i = 1; i < ringPts.Count; i++)
                if (ringPts[i].y > ringPts[top].y + 1e-4f || (Mathf.Abs(ringPts[i].y - ringPts[top].y) <= 1e-4f && ringPts[i].x < ringPts[top].x))
                    top = i;
            Vector2 j = ringPts[top];
            return new GlassTrackBranch("Giriş Yolu", false,
                j, j + new Vector2(0f, EntryRoadLength * 0.5f), j + new Vector2(0f, EntryRoadLength));
        }

        private static float RingHalfWidthRatio(GlassTrackPreset preset)
        {
            // U'da omurgadan yol ortasına ULoopOffset var; boşluk görünür kalsın diye yol biraz daha dar
            return preset == GlassTrackPreset.ArchLoop ? 0.18f : 0.24f;
        }

        /// <summary>
        /// Hazır halka şekli için top sayısına en uygun şerit sayısı ve uzama oranını seçer: yol, toplarla
        /// yaklaşık %85 dolu olsun (ne taşsın ne de boş kalsın).
        /// </summary>
        private static List<GlassTrackBranch> ChoosePresetForCount(GlassTrackPreset preset, int targetCount, int lanesSetting, bool autoFit, out int chosenLanes)
        {
            chosenLanes = -1;
            if (!autoFit || targetCount <= 0) return BuildPresetBranches(preset);

            float halfRatio = RingHalfWidthRatio(preset);
            bool stretchable = preset == GlassTrackPreset.ArchLoop;
            int minL = lanesSetting > 0 ? Mathf.Clamp(lanesSetting, 2, 6) : 3;
            int maxL = lanesSetting > 0 ? minL : 5;

            List<GlassTrackBranch> best = null;
            float bestScore = float.MaxValue;
            for (int l = minL; l <= maxL; l++)
            {
                // Çap / ölçek oranı (kanal yarı genişliği sınırına göre en büyük top)
                float dPerScale = halfRatio / Mathf.Max(1f, l * 0.495f);
                for (int k = 0; k <= (stretchable ? 10 : 0); k++)
                {
                    float st = stretchable ? k / 10f : 0.5f;
                    var br = BuildPresetBranches(preset, st);
                    float len = 0f;
                    var pts = br[0].points;
                    for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
                    // En iri topla (kanal sınırında) doluluk. %85'in üstündeyse top biraz küçültülerek %85'e
                    // indirilebilir (yol daralır, boşluk büyür); altındaysa yol boş kalır.
                    float capacity = len / (dPerScale * 1.03f) * l * 0.95f;
                    float fillAtMax = targetCount / Mathf.Max(1f, capacity);
                    float score;
                    if (fillAtMax >= 0.85f)
                    {
                        float shrink = 0.85f / fillAtMax;           // gereken çap oranı (1 = en iri)
                        score = (1f - shrink) * 0.6f;
                        if (shrink < 0.70f) score += (0.70f - shrink) * 4f; // toplar çok küçülmesin
                    }
                    else
                    {
                        score = (0.85f - fillAtMax) * 1.5f;          // boş yol
                    }
                    score += (l - 2) * 0.02f; // eşitlikte daha az şerit (daha iri top)
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = br;
                        chosenLanes = l;
                    }
                }
            }
            if (lanesSetting > 0) chosenLanes = -1;
            return best ?? BuildPresetBranches(preset);
        }

        private static List<PuzzleColor> ExpandBallColors(
            List<LevelMonsterEntry> groups,
            List<ManualPoolBallEntry> manualBalls,
            bool useManual)
        {
            var list = new List<PuzzleColor>();
            if (useManual && manualBalls != null && manualBalls.Count > 0)
            {
                var sorted = new List<ManualPoolBallEntry>(manualBalls);
                sorted.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.column.CompareTo(b.column));
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i] != null) list.Add(sorted[i].color);
                }
                return list;
            }

            if (groups != null && groups.Count > 0)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    if (g == null) continue;
                    int cnt = Mathf.Max(1, g.count);
                    for (int k = 0; k < cnt; k++) list.Add(g.color);
                }
            }
            return list;
        }

        private static void ChooseBestFitParameters(
            GlassTrackPreset preset,
            List<GlassTrackBranch> branches,
            int targetCount,
            int lanes,
            float maxHalfWidthRatio,
            out float bestScale,
            out float bestDiam,
            out float bestTrim)
        {
            float baseTotalLen = EstimateUniqueBranchLength(branches);
            bestTrim = 1.0f;

            // Küçük/orta levellerde açık üst kolları top sayısına göre ayarla ki pist boş kalmasın
            bool anyClosed = false;
            for (int b = 0; b < branches.Count; b++)
                if (branches[b] != null && IsClosedPolyline(branches[b].points)) { anyClosed = true; break; }
            if (!anyClosed && targetCount <= 48 && (preset == GlassTrackPreset.ArchLoop || preset == GlassTrackPreset.HeartLoop || preset == GlassTrackPreset.UChannel || preset == GlassTrackPreset.DoubleArch))
            {
                bestTrim = Mathf.Lerp(0.54f, 0.95f, Mathf.InverseLerp(12f, 48f, targetCount));
            }

            float effectiveLenAtUnit = Mathf.Max(0.8f, baseTotalLen * (0.32f + 0.68f * bestTrim));

            // İstenen yoğunluk: toplar pistin ~%82-96'sını sıkıca doldursun
            float desiredTotalRowSteps = Mathf.Max(3.5f, (targetCount * 1.08f) / lanes);
            // Kanal yarı genişliği iç ada boşluğunu kapatmamalı (<= 0.36 * scale)
            float maxDiamRatioPerScale = maxHalfWidthRatio / Mathf.Max(1f, lanes * 0.495f);

            bestScale = 1.08f;
            bestDiam = 0.102f;
            float bestScore = float.MaxValue;

            for (int si = 0; si <= 12; si++)
            {
                float s = Mathf.Lerp(0.94f, 1.18f, si / 12f);
                float maxDiamSafe = Mathf.Min(0.114f, maxDiamRatioPerScale * s);
                float minDiamSafe = Mathf.Min(0.068f, maxDiamSafe);
                float idealDiam = (effectiveLenAtUnit * s) / (desiredTotalRowSteps * 0.95f);
                float d = Mathf.Clamp(idealDiam, minDiamSafe, maxDiamSafe);

                float capacity = (effectiveLenAtUnit * s) / (d * 0.95f) * lanes;
                float fillRatio = targetCount / Mathf.Max(1f, capacity);

                // Hedef doluluk %80..%96 arası, öne çıkan büyük hero cam pist ölçeği ve küçük zarif toplar
                float penalty = 0f;
                if (fillRatio > 0.97f) penalty += (fillRatio - 0.97f) * 24f;
                if (fillRatio < 0.78f) penalty += (0.78f - fillRatio) * 8f;
                penalty += Mathf.Abs(fillRatio - 0.88f) * 2.2f;
                penalty += Mathf.Abs(s - 1.08f) * 0.45f;
                penalty += Mathf.Abs(d - 0.100f) * 1.35f;

                if (penalty < bestScore)
                {
                    bestScore = penalty;
                    bestScale = s;
                    bestDiam = d;
                }
            }
        }

        private static float EstimateUniqueBranchLength(List<GlassTrackBranch> branches)
        {
            float sum = 0f;
            for (int b = 0; b < branches.Count; b++)
            {
                var pts = branches[b]?.points;
                if (pts == null || pts.Count < 2) continue;
                float len = 0f;
                for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
                sum += len * (b == 0 ? 1.0f : 0.78f);
            }
            return Mathf.Max(0.8f, sum);
        }

        private static List<GlassTrackBranch> TransformBranches(List<GlassTrackBranch> src, float scale, float openArmTrim)
        {
            var res = new List<GlassTrackBranch>(src.Count);
            if (src.Count == 0) return res;

            // Çıkış tabanını y = -0.56 civarında sabit tut ki tepsi üstüyle mesafe değişmesin
            float minY = float.MaxValue;
            for (int b = 0; b < src.Count; b++)
            {
                var pts = src[b]?.points;
                if (pts == null) continue;
                for (int i = 0; i < pts.Count; i++) minY = Mathf.Min(minY, pts[i].y);
            }
            if (minY > 100f) minY = -0.56f;
            float targetMinY = -0.56f;

            for (int b = 0; b < src.Count; b++)
            {
                var br = src[b];
                if (br == null || br.points == null || br.points.Count < 2) continue;
                var nb = new GlassTrackBranch(br.name, br.isBridge);

                List<Vector2> srcPts = br.points;
                if (!br.isBridge && openArmTrim < 0.99f && srcPts.Count >= 3 && !IsClosedPolyline(srcPts))
                {
                    // Kolu son noktasından itibaren hafifçe kısalt (köprü birleşimini koru)
                    srcPts = TrimPolylineEnd(srcPts, openArmTrim);
                }

                for (int i = 0; i < srcPts.Count; i++)
                {
                    float x = srcPts[i].x * scale;
                    float y = targetMinY + (srcPts[i].y - minY) * scale;
                    nb.points.Add(new Vector2(x, y));
                }
                res.Add(nb);
            }
            return res;
        }

        private static List<Vector2> TrimPolylineEnd(List<Vector2> pts, float keepFraction)
        {
            int n = pts.Count;
            float[] cum = new float[n];
            cum[0] = 0f;
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = cum[n - 1];
            float target = Mathf.Clamp(keepFraction, 0.55f, 1.0f) * total;

            var outPts = new List<Vector2> { pts[0] };
            for (int i = 1; i < n; i++)
            {
                if (cum[i] <= target)
                {
                    outPts.Add(pts[i]);
                }
                else
                {
                    float seg = Mathf.Max(0.0001f, cum[i] - cum[i - 1]);
                    float t = Mathf.Clamp01((target - cum[i - 1]) / seg);
                    outPts.Add(Vector2.Lerp(pts[i - 1], pts[i], t));
                    break;
                }
            }
            return outPts;
        }

        public static CompiledTrack CompileTrack(
            GlassTrackPreset preset,
            List<GlassTrackBranch> branches,
            float innerHalfWidth,
            float wallThickness,
            float ballDiameter,
            int lanesAcross)
        {
            var track = new CompiledTrack
            {
                preset = preset,
                innerHalfWidth = Mathf.Max(0.10f, innerHalfWidth),
                wallThickness = Mathf.Max(0.035f, wallThickness),
                ballDiameter = Mathf.Clamp(ballDiameter, 0.060f, 0.125f),
                lanesAcross = Mathf.Clamp(lanesAcross, 2, 6),
                exitPoint = new Vector2(0f, -0.56f)
            };

            if (branches == null || branches.Count == 0)
            {
                branches = BuildPresetBranches(preset);
            }

            // Toplam segment sayısı <= MaxShaderSegments (64) kalacak şekilde her kolu örnekle
            int branchCount = branches.Count;
            int closedCount = 0;
            for (int b = 0; b < branchCount; b++)
                if (branches[b] != null && IsClosedPolyline(branches[b].points)) closedCount++;
            // Kapalı halka daha çok segment alır ki camın iç/dış kenarı köşelenmeden yuvarlak görünsün
            int closedPts = closedCount > 0 && closedCount == branchCount ? (MaxShaderSegments / closedCount + 1) : 41;
            int openCount = Mathf.Max(1, branchCount - closedCount);
            int remainingSegs = Mathf.Max(openCount * 5, MaxShaderSegments - closedCount * (closedPts - 1));
            int maxPtsPerBranch = Mathf.Clamp(remainingSegs / openCount + 1, 6, 18);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int b = 0; b < branchCount; b++)
            {
                var br = branches[b];
                if (br == null || br.points == null || br.points.Count < 2) continue;

                bool closed = IsClosedPolyline(br.points);
                List<Vector2> smooth = closed
                    ? SampleClosedCatmullRom(br.points, closedPts)
                    : SampleSmoothCatmullRom(br.points, maxPtsPerBranch);
                if (closed && track.closedLoopBranch < 0 && !br.isBridge) track.closedLoopBranch = track.polylines.Count;
                track.polylines.Add(smooth);
                track.polylineIsBridge.Add(br.isBridge);

                for (int i = 0; i < smooth.Count; i++)
                {
                    minX = Mathf.Min(minX, smooth[i].x);
                    maxX = Mathf.Max(maxX, smooth[i].x);
                    minY = Mathf.Min(minY, smooth[i].y);
                    maxY = Mathf.Max(maxY, smooth[i].y);
                }

                // Çıkış noktası ilk ana kolun taban noktasıdır
                if (!br.isBridge && b == 0)
                {
                    track.exitPoint = smooth[0];
                }
            }

            if (minX > maxX)
            {
                minX = -1f; maxX = 1f; minY = -0.56f; maxY = 0.8f;
            }

            // Halka varsa: halka üzerindeki her noktanın çıkışa bant yönünde (saat yönü) kalan yolu
            float[] ringArc = null;
            float ringLen = 0f;
            List<Vector2> ringPoly = track.closedLoopBranch >= 0 ? track.polylines[track.closedLoopBranch] : null;
            if (ringPoly != null)
            {
                ringArc = new float[ringPoly.Count];
                for (int i = 1; i < ringPoly.Count; i++) ringArc[i] = ringArc[i - 1] + Vector2.Distance(ringPoly[i - 1], ringPoly[i]);
                ringLen = ringArc[ringPoly.Count - 1];
            }

            // Her kol için çıkışa mesafe (arc-length) ve shader segmentleri
            for (int b = 0; b < track.polylines.Count; b++)
            {
                var poly = track.polylines[b];
                bool isBridge = track.polylineIsBridge[b];
                float[] arc = new float[poly.Count];
                arc[0] = 0f;
                for (int i = 1; i < poly.Count; i++)
                {
                    arc[i] = arc[i - 1] + Vector2.Distance(poly[i - 1], poly[i]);
                }
                float totalLen = arc[poly.Count - 1];

                // Köprü ise uçlarının en yakın ana kola mesafesini ekle ki akış her zaman çıkışa doğru azalsın
                float startBaseDist = 0f;
                float endBaseDist = totalLen;
                if (isBridge)
                {
                    startBaseDist = EstimateDistanceToArmExit(track, poly[0]);
                    endBaseDist = EstimateDistanceToArmExit(track, poly[poly.Count - 1]);
                }

                // Halkaya bağlanan açık kol (giriş kolu): mesafe = bağlantı noktasının halka mesafesi + kol boyu
                float feederBase = 0f;
                if (ringPoly != null && b != track.closedLoopBranch && !isBridge)
                {
                    int nj = 0;
                    float bestSq = float.MaxValue;
                    for (int i = 0; i < ringPoly.Count; i++)
                    {
                        float dSq = (ringPoly[i] - poly[0]).sqrMagnitude;
                        if (dSq < bestSq) { bestSq = dSq; nj = i; }
                    }
                    feederBase = ringLen - ringArc[nj];
                }

                for (int i = 0; i < poly.Count - 1; i++)
                {
                    if (track.segments.Count >= MaxShaderSegments) break;

                    float dA, dB;
                    if (b == track.closedLoopBranch)
                    {
                        dA = totalLen - arc[i];
                        dB = totalLen - arc[i + 1];
                    }
                    else if (!isBridge && ringPoly != null)
                    {
                        dA = feederBase + arc[i];
                        dB = feederBase + arc[i + 1];
                    }
                    else if (!isBridge)
                    {
                        dA = arc[i];
                        dB = arc[i + 1];
                    }
                    else
                    {
                        float midA = arc[i];
                        float midB = arc[i + 1];
                        dA = Mathf.Min(startBaseDist + midA, endBaseDist + (totalLen - midA));
                        dB = Mathf.Min(startBaseDist + midB, endBaseDist + (totalLen - midB));
                    }

                    track.segments.Add(new TrackSegment
                    {
                        a = poly[i],
                        b = poly[i + 1],
                        branchId = b,
                        distA = dA,
                        distB = dB
                    });
                }
            }

            float padIn = track.innerHalfWidth;
            float padOut = track.innerHalfWidth + track.wallThickness;
            track.innerBounds = Rect.MinMaxRect(minX - padIn, minY - padIn, maxX + padIn, maxY + padIn);
            track.outerBounds = Rect.MinMaxRect(minX - padOut, minY - padOut, maxX + padOut, maxY + padOut);

            // Gövde sınırı: halka varsa ona bağlı açık kollar (giriş yolu) hariç
            track.bodyOuterBounds = track.outerBounds;
            if (track.closedLoopBranch >= 0)
            {
                float bx0 = float.MaxValue, bx1 = float.MinValue, by0 = float.MaxValue, by1 = float.MinValue;
                for (int b = 0; b < track.polylines.Count; b++)
                {
                    bool isOpenArm = b != track.closedLoopBranch && !track.polylineIsBridge[b];
                    if (isOpenArm) continue;
                    var poly = track.polylines[b];
                    for (int i = 0; i < poly.Count; i++)
                    {
                        bx0 = Mathf.Min(bx0, poly[i].x); bx1 = Mathf.Max(bx1, poly[i].x);
                        by0 = Mathf.Min(by0, poly[i].y); by1 = Mathf.Max(by1, poly[i].y);
                    }
                }
                if (bx0 <= bx1) track.bodyOuterBounds = Rect.MinMaxRect(bx0 - padOut, by0 - padOut, bx1 + padOut, by1 + padOut);
            }

            float fieldPad = padOut + 0.16f;
            track.fieldRect = Rect.MinMaxRect(minX - fieldPad, minY - fieldPad, maxX + fieldPad, maxY + fieldPad);

            BuildTrackField2D(track);
            return track;
        }

        private static float EstimateDistanceToArmExit(CompiledTrack track, Vector2 pt)
        {
            float bestDistSq = float.MaxValue;
            float bestArmDist = 1.0f;
            for (int b = 0; b < track.polylines.Count; b++)
            {
                if (track.polylineIsBridge[b]) continue;
                var poly = track.polylines[b];
                float acc = 0f;
                for (int i = 0; i < poly.Count; i++)
                {
                    if (i > 0) acc += Vector2.Distance(poly[i - 1], poly[i]);
                    float dSq = (poly[i] - pt).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestArmDist = acc;
                    }
                }
            }
            return bestArmDist;
        }

        private static void BuildTrackField2D(CompiledTrack track)
        {
            int n = FieldRes * FieldRes;
            track.sdf = new float[n];
            track.flowDir = new Vector2[n];
            track.distToExit = new float[n];
            track.wallNormal = new Vector2[n];

            float filletK = Mathf.Min(0.055f, track.innerHalfWidth * 0.20f);
            int branchCount = Mathf.Max(1, track.polylines.Count);
            float[] branchMinDist = new float[branchCount];

            for (int iy = 0; iy < FieldRes; iy++)
            {
                float py = Mathf.Lerp(track.fieldRect.yMin, track.fieldRect.yMax, (float)iy / (FieldRes - 1));
                for (int ix = 0; ix < FieldRes; ix++)
                {
                    float px = Mathf.Lerp(track.fieldRect.xMin, track.fieldRect.xMax, (float)ix / (FieldRes - 1));
                    Vector2 p = new Vector2(px, py);

                    for (int b = 0; b < branchCount; b++) branchMinDist[b] = float.MaxValue;

                    float bestFlowExitDist = float.MaxValue;
                    Vector2 bestFlow = Vector2.down;
                    float bestSegDistSq = float.MaxValue;

                    for (int s = 0; s < track.segments.Count; s++)
                    {
                        var seg = track.segments[s];
                        Vector2 ab = seg.b - seg.a;
                        float abSq = ab.sqrMagnitude;
                        float t = abSq > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - seg.a, ab) / abSq) : 0f;
                        Vector2 proj = seg.a + ab * t;
                        float dSq = (p - proj).sqrMagnitude;
                        float d = Mathf.Sqrt(dSq) - track.innerHalfWidth;

                        if (d < branchMinDist[seg.branchId])
                        {
                            branchMinDist[seg.branchId] = d;
                        }

                        if (dSq < bestSegDistSq + 0.002f)
                        {
                            float dExit = Mathf.Lerp(seg.distA, seg.distB, t);
                            if (dSq < bestSegDistSq - 0.002f || dExit < bestFlowExitDist)
                            {
                                bestSegDistSq = dSq;
                                bestFlowExitDist = dExit;

                                // Akış yönü: distToExit'in azaldığı yön + viraj duvarlarında takılmayı önleyen hafif merkez eğilimi
                                Vector2 segDir = abSq > 1e-8f ? ab.normalized : Vector2.up;
                                Vector2 tangDown = (seg.distB < seg.distA) ? segDir : -segDir;
                                Vector2 toCenter = proj - p;
                                float centerPull = Mathf.Clamp(toCenter.magnitude / Mathf.Max(0.05f, track.innerHalfWidth), 0f, 1f) * 0.16f;
                                bestFlow = (tangDown + (toCenter.sqrMagnitude > 1e-6f ? toCenter.normalized * centerPull : Vector2.zero)).normalized;
                            }
                        }
                    }

                    // Kollar arası yumuşak birleşme (SmoothUnion)
                    float combined = branchMinDist[0];
                    for (int b = 1; b < branchCount; b++)
                    {
                        float d2 = branchMinDist[b];
                        if (d2 > 1e6f) continue;
                        float h = Mathf.Clamp01(0.5f + 0.5f * (d2 - combined) / Mathf.Max(0.001f, filletK));
                        combined = Mathf.Lerp(d2, combined, h) - filletK * h * (1f - h);
                    }

                    int idx = iy * FieldRes + ix;
                    track.sdf[idx] = combined;
                    track.flowDir[idx] = bestFlow.sqrMagnitude > 0.001f ? bestFlow : Vector2.down;
                    track.distToExit[idx] = bestFlowExitDist < 1e6f ? bestFlowExitDist : 0f;
                }
            }

            // Duvar içe normali (-∇sdf)
            float cellDx = track.fieldRect.width / (FieldRes - 1);
            float cellDy = track.fieldRect.height / (FieldRes - 1);
            for (int iy = 0; iy < FieldRes; iy++)
            {
                int iy0 = Mathf.Max(0, iy - 1);
                int iy1 = Mathf.Min(FieldRes - 1, iy + 1);
                for (int ix = 0; ix < FieldRes; ix++)
                {
                    int ix0 = Mathf.Max(0, ix - 1);
                    int ix1 = Mathf.Min(FieldRes - 1, ix + 1);
                    float gx = (track.sdf[iy * FieldRes + ix1] - track.sdf[iy * FieldRes + ix0]) / Mathf.Max(0.001f, (ix1 - ix0) * cellDx);
                    float gy = (track.sdf[iy1 * FieldRes + ix] - track.sdf[iy0 * FieldRes + ix]) / Mathf.Max(0.001f, (iy1 - iy0) * cellDy);
                    Vector2 inward = new Vector2(-gx, -gy);
                    track.wallNormal[iy * FieldRes + ix] = inward.sqrMagnitude > 1e-6f ? inward.normalized : Vector2.up;
                }
            }
        }

        // ==================== Şerit Bazlı Sıkı Top Slot Üretimi ====================

        public static List<TrackSlot> GeneratePackedTrackSlots(CompiledTrack track)
        {
            var slots = new List<TrackSlot>();
            if (track == null || track.polylines.Count == 0) return slots;

            float d = track.ballDiameter;
            float r = d * 0.5f;
            int lanes = track.lanesAcross;
            float step = d * 1.02f;
            float minDistOther = d * 1.01f;
            float minDistOtherSq = minDistOther * minDistOther;

            // Şerit genişliğini kanal içine sıkıca oturt (duvar köşe ve kutu çarpıştırıcılarına çarpmayacak emniyetli yayılım)
            float usableSpan = Mathf.Max(d * (lanes - 1), track.innerHalfWidth * 2f - d * 1.22f);
            float laneSpacing = lanes > 1 ? usableSpan / (lanes - 1) : 0f;

            // Halka: yuvalar doğrudan bant şeritlerinden gelir (toplar bandın tam yuvalarında doğar)
            if (track.closedLoopBranch >= 0) TrackCrowdQueue.AppendRingSlots(track, slots);

            for (int b = 0; b < track.polylines.Count; b++)
            {
                if (b == track.closedLoopBranch && slots.Count > 0) continue;
                var poly = track.polylines[b];
                if (poly == null || poly.Count < 2) continue;
                bool isBridge = track.polylineIsBridge[b];

                // Çıkışa yakın uçtan başlayarak kolu ince adımlarla yürü
                List<Vector2> dense = ResamplePolylineUniform(poly, d * 0.16f);
                if (dense.Count < 2) continue;

                // Halkaya bağlı giriş yolu: toplar halkanın şeridine taşmasın, yol halkanın kenarından başlasın
                if (track.closedLoopBranch >= 0 && !isBridge && dense.Count > 2)
                {
                    Vector2 j0 = dense[0];
                    Vector2 dir0 = (dense[Mathf.Min(2, dense.Count - 1)] - j0).normalized;
                    float gate = track.innerHalfWidth + r * 1.05f;
                    int cut = 0;
                    while (cut < dense.Count - 2 && Vector2.Dot(dense[cut] - j0, dir0) < gate) cut++;
                    if (cut > 0) dense.RemoveRange(0, cut);
                }

                // Köprüler için ortadan dışa doğru iki yarıyı sırala; ana kollar için tabandan yukarı
                if (!isBridge)
                {
                    AddSlotsAlongDirectedPath(dense, b, 0, lanes, laneSpacing, step, r, minDistOtherSq, track, slots);
                }
                else
                {
                    int mid = dense.Count / 2;
                    var leftHalf = new List<Vector2>();
                    for (int i = mid; i >= 0; i--) leftHalf.Add(dense[i]);
                    var rightHalf = new List<Vector2>();
                    for (int i = mid; i < dense.Count; i++) rightHalf.Add(dense[i]);

                    AddSlotsAlongDirectedPath(leftHalf, b, 0, lanes, laneSpacing, step, r, minDistOtherSq, track, slots);
                    AddSlotsAlongDirectedPath(rightHalf, b, 1000, lanes, laneSpacing, step, r, minDistOtherSq, track, slots);
                }
            }

            return slots;
        }

        private static void AddSlotsAlongDirectedPath(
            List<Vector2> path,
            int branchId,
            int rowBaseOffset,
            int lanes,
            float laneSpacing,
            float step,
            float ballR,
            float minDistOtherSq,
            CompiledTrack track,
            List<TrackSlot> slots)
        {
            if (path == null || path.Count < 2) return;

            bool[] hasPrev = new bool[lanes];
            Vector2[] prevLanePos = new Vector2[lanes];
            int[] laneRowCounter = new int[lanes];
            float stepSq = step * step;

            for (int i = 0; i < path.Count; i++)
            {
                Vector2 prev = path[Mathf.Max(0, i - 1)];
                Vector2 next = path[Mathf.Min(path.Count - 1, i + 1)];
                Vector2 tang = (next - prev).normalized;
                if (tang.sqrMagnitude < 1e-6f) tang = Vector2.up;
                Vector2 normal = new Vector2(-tang.y, tang.x);

                for (int l = 0; l < lanes; l++)
                {
                    float latOffset = (l - (lanes - 1) * 0.5f) * laneSpacing;
                    Vector2 cand = path[i] + normal * latOffset;

                    if (hasPrev[l] && (cand - prevLanePos[l]).sqrMagnitude < stepSq)
                        continue;

                    track.SampleField(cand, out float sDist, out _, out float exitDist, out _);
                    // Duvar içine gömülmesin: köşeli BoxCollider geometrisi ve grid hatasını tolere edecek emniyetli hava payı (~8mm)
                    if (sDist > -ballR * 1.10f)
                        continue;

                    // Çıkış durdurucu kapısının içine gömülmesin
                    float exitStopperTop = (track.exitPoint.y - track.innerHalfWidth) - 0.045f + 0.04f;
                    if (cand.y - ballR < exitStopperTop + 0.012f)
                        continue;

                    // Diğer tüm toplarla (özellikle kavşak ve iç virajlarda) çakışmasın
                    bool overlaps = false;
                    for (int k = 0; k < slots.Count; k++)
                    {
                        if ((slots[k].localPos - cand).sqrMagnitude < minDistOtherSq)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) continue;

                    hasPrev[l] = true;
                    prevLanePos[l] = cand;

                    slots.Add(new TrackSlot
                    {
                        localPos = cand,
                        branchId = branchId,
                        rowIndex = rowBaseOffset + laneRowCounter[l],
                        laneIndex = l,
                        distToExit = exitDist
                    });
                    laneRowCounter[l]++;
                }
            }
        }

        private static OrderedPoolLayout.Result BuildOrderedResultFromSlots(
            List<TrackSlot> allSlots,
            List<PuzzleColor> orderedColors,
            List<LevelMonsterEntry> groups,
            bool useManual,
            float ballDiam,
            int lanes,
            CompiledTrack track,
            int maxOnTrack = int.MaxValue)
        {
            var res = new OrderedPoolLayout.Result
            {
                balls = new List<OrderedPoolLayout.PoolBall>(),
                diameter = ballDiam,
                laneWidth = lanes,
                totalColumns = Mathf.Max(1, lanes * 2),
                overflowColors = new List<PuzzleColor>()
            };

            if (allSlots == null || allSlots.Count == 0 || orderedColors == null || orderedColors.Count == 0)
                return res;

            float bandSize = Mathf.Max(0.05f, ballDiam * 0.98f);

            // Manuel serbest dizilim istenmişse, çıkışa olan mesafeyi baz alan sıralamayı kullan
            if (useManual)
            {
                var sorted = new List<TrackSlot>(allSlots);
                sorted.Sort((a, b) =>
                {
                    int bandA = Mathf.RoundToInt(a.distToExit / bandSize);
                    int bandB = Mathf.RoundToInt(b.distToExit / bandSize);
                    if (bandA != bandB) return bandA.CompareTo(bandB);
                    if (a.branchId != b.branchId) return a.branchId.CompareTo(b.branchId);
                    return a.laneIndex.CompareTo(b.laneIndex);
                });

                int manualCount = Mathf.Min(sorted.Count, orderedColors.Count);
                for (int i = 0; i < manualCount; i++)
                {
                    var sl = sorted[i];
                    res.balls.Add(new OrderedPoolLayout.PoolBall
                    {
                        color = orderedColors[i],
                        ballColumn = sl.laneIndex,
                        row = Mathf.Max(0, Mathf.RoundToInt(sl.distToExit / bandSize)),
                        localPos = sl.localPos
                    });
                }
                return res;
            }

            // ==================== Bus Traffic Fever Tarzı Nizami Blok Sıralaması ====================
            // 1. Slotları ait oldukları kollara (branch) göre ayır ve her kolu satır-satır, şerit-şerit sırala
            int branchCount = track != null && track.polylines != null ? track.polylines.Count : 1;
            var branchSlots = new List<TrackSlot>[branchCount];
            for (int b = 0; b < branchCount; b++) branchSlots[b] = new List<TrackSlot>();

            for (int i = 0; i < allSlots.Count; i++)
            {
                int bid = Mathf.Clamp(allSlots[i].branchId, 0, branchCount - 1);
                branchSlots[bid].Add(allSlots[i]);
            }

            for (int b = 0; b < branchCount; b++)
            {
                branchSlots[b].Sort((s1, s2) =>
                {
                    if (s1.rowIndex != s2.rowIndex) return s1.rowIndex.CompareTo(s2.rowIndex);
                    return s1.laneIndex.CompareTo(s2.laneIndex);
                });
            }

            // 2. Çıkışa açılan ana kolları (feeder) ve üst bağ/köprüleri (bridge) tespit et
            var feederIds = new List<int>();
            var bridgeIds = new List<int>();

            for (int b = 0; b < branchCount; b++)
            {
                bool isBridge = track != null && track.polylineIsBridge != null && b < track.polylineIsBridge.Count && track.polylineIsBridge[b];
                if (isBridge) bridgeIds.Add(b);
                else feederIds.Add(b);
            }
            if (feederIds.Count == 0) feederIds.Add(0);

            // Halkalı pist: hepsi tek bant akışı; renk blokları çıkışa bant yönünde kalan yola göre art arda dizilir
            if (track != null && track.closedLoopBranch >= 0)
            {
                bridgeIds.AddRange(feederIds);
                bridgeIds.Remove(track.closedLoopBranch);
                feederIds.Clear();
                feederIds.Add(track.closedLoopBranch);
            }

            // Her ana akış için sıralı slot kuyruğunu oluştur
            var streamSlots = new List<List<TrackSlot>>();
            for (int f = 0; f < feederIds.Count; f++)
            {
                streamSlots.Add(new List<TrackSlot>(branchSlots[feederIds[f]]));
            }

            // ArchLoop gibi çift kollu ve iç kemerli pistlerde köprünün sol yarısını sol kola, sağ yarısını sağ kola bağla
            if (feederIds.Count == 2 && bridgeIds.Count > 0)
            {
                for (int bi = 0; bi < bridgeIds.Count; bi++)
                {
                    int bridgeId = bridgeIds[bi];
                    var bSlots = branchSlots[bridgeId];
                    for (int k = 0; k < bSlots.Count; k++)
                    {
                        if (bSlots[k].localPos.x <= 0f) streamSlots[0].Add(bSlots[k]);
                        else streamSlots[1].Add(bSlots[k]);
                    }
                }
            }
            else if (bridgeIds.Count > 0)
            {
                for (int bi = 0; bi < bridgeIds.Count; bi++)
                {
                    streamSlots[0].AddRange(branchSlots[bridgeIds[bi]]);
                }
            }

            // Her akışın slotlarını çıkışa olan mesafesine göre nizami sırala
            // Böylece renk blokları çıkıştan geriye doğru kesintisiz ve nizami bir şekilde akar
            int ringId = track != null ? track.closedLoopBranch : -1;
            for (int s = 0; s < streamSlots.Count; s++)
            {
                streamSlots[s].Sort((s1, s2) =>
                {
                    // Halkalı pist: önce bant (çıkışa kalan yola göre), sonra giriş kolları (banda sonra katılır)
                    if (ringId >= 0)
                    {
                        bool r1 = s1.branchId == ringId, r2 = s2.branchId == ringId;
                        if (r1 != r2) return r1 ? -1 : 1;
                    }
                    int bandA = Mathf.RoundToInt(s1.distToExit / bandSize);
                    int bandB = Mathf.RoundToInt(s2.distToExit / bandSize);
                    if (bandA != bandB) return bandA.CompareTo(bandB);
                    if (s1.rowIndex != s2.rowIndex) return s1.rowIndex.CompareTo(s2.rowIndex);
                    return s1.laneIndex.CompareTo(s2.laneIndex);
                });
            }

            // Gizli kuyrukta halkaya en fazla maxOnTrack top: fazla halka yuvaları boş kalır, sonraki toplar giriş
            // yoluna, yol da dolarsa tepeden sonradan gelecek kuyruğa (overflowColors) gider.
            if (ringId >= 0 && maxOnTrack < int.MaxValue)
            {
                var sl0 = streamSlots[0];
                int ringSeen = 0;
                for (int k = 0; k < sl0.Count; k++)
                {
                    if (sl0[k].branchId != ringId) continue;
                    if (++ringSeen > maxOnTrack) { sl0.RemoveAt(k); k--; }
                }
            }

            // 3. Renk bloklarını çıkar (LevelMonsterEntry gruplarından veya orderedColors akışından)
            var blocks = new List<(PuzzleColor color, int count)>();
            if (groups != null && groups.Count > 0)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i] != null && groups[i].count > 0)
                        blocks.Add((groups[i].color, groups[i].count));
                }
            }
            else
            {
                for (int i = 0; i < orderedColors.Count; i++)
                {
                    PuzzleColor c = orderedColors[i];
                    if (blocks.Count > 0 && blocks[blocks.Count - 1].color == c)
                    {
                        var last = blocks[blocks.Count - 1];
                        blocks[blocks.Count - 1] = (last.color, last.count + 1);
                    }
                    else
                    {
                        blocks.Add((c, 1));
                    }
                }
            }

            // 4. Renk bloklarını kollara (Sol Kol & Sağ Kol) sırayla dağıt (bölünmeden monolitik bloklar)
            var streamBlocks = new List<List<(PuzzleColor color, int count)>>();
            for (int f = 0; f < streamSlots.Count; f++) streamBlocks.Add(new List<(PuzzleColor, int)>());

            if (streamSlots.Count >= 2)
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    int targetStream = i % streamSlots.Count;
                    streamBlocks[targetStream].Add(blocks[i]);
                }
            }
            else
            {
                streamBlocks[0].AddRange(blocks);
            }

            // 5. Her kola ait blokları o kolun slotlarına kesintisiz tam şeritler halinde yerleştir
            int[] streamSlotIndex = new int[streamSlots.Count];

            for (int f = 0; f < streamSlots.Count; f++)
            {
                var sList = streamSlots[f];
                var bList = streamBlocks[f];

                for (int bi = 0; bi < bList.Count; bi++)
                {
                    var blk = bList[bi];
                    for (int k = 0; k < blk.count; k++)
                    {
                        if (ringId < 0 && res.balls.Count >= maxOnTrack)
                        {
                            // Gizli kuyruk: bu top bantta değil, girişte sırasını bekler
                            res.overflowColors.Add(blk.color);
                        }
                        else if (streamSlotIndex[f] < sList.Count)
                        {
                            var sl = sList[streamSlotIndex[f]++];
                            res.balls.Add(new OrderedPoolLayout.PoolBall
                            {
                                color = blk.color,
                                ballColumn = f * lanes + sl.laneIndex,
                                row = Mathf.Max(0, Mathf.RoundToInt(sl.distToExit / bandSize)),
                                localPos = sl.localPos
                            });
                        }
                        else
                        {
                            // Kol kapasitesi dolarsa diğer kola taşır
                            bool placed = false;
                            for (int otherF = 0; otherF < streamSlots.Count; otherF++)
                            {
                                if (otherF == f) continue;
                                if (streamSlotIndex[otherF] < streamSlots[otherF].Count)
                                {
                                    var sl = streamSlots[otherF][streamSlotIndex[otherF]++];
                                    res.balls.Add(new OrderedPoolLayout.PoolBall
                                    {
                                        color = blk.color,
                                        ballColumn = otherF * lanes + sl.laneIndex,
                                        row = Mathf.Max(0, Mathf.RoundToInt(sl.distToExit / bandSize)),
                                        localPos = sl.localPos
                                    });
                                    placed = true;
                                    break;
                                }
                            }
                            // Banda sığmadı: gizli kuyrukta sırasıyla bekler (bant boşaldıkça girer)
                            if (!placed) res.overflowColors.Add(blk.color);
                        }
                    }
                }
            }

            if (ringId >= 0)
            {
                // Halkadaki top sayısı (bant kapasite sınırı): giriş yolundakiler bunun altına düşünce katılır
                int onRing = 0;
                for (int i = 0; i < res.balls.Count; i++)
                    if (track.SampleDistToRingCenter(res.balls[i].localPos) < track.innerHalfWidth) onRing++;
                res.beltCount = onRing;
            }
            return res;
        }

        // ==================== Spline / Polyline Yardımcıları ====================

        public static List<Vector2> SampleSmoothCatmullRom(List<Vector2> ctrl, int targetCount)
        {
            var outPts = new List<Vector2>();
            if (ctrl == null || ctrl.Count == 0) return outPts;
            if (ctrl.Count == 1) { outPts.Add(ctrl[0]); return outPts; }
            if (ctrl.Count == 2)
            {
                int segs = Mathf.Max(2, targetCount - 1);
                for (int i = 0; i <= segs; i++) outPts.Add(Vector2.Lerp(ctrl[0], ctrl[1], (float)i / segs));
                return outPts;
            }

            // Önce yoğun Catmull-Rom üret, sonra eşit yay uzunluğunda targetCount noktaya örnekle
            var dense = new List<Vector2>();
            int n = ctrl.Count;
            const int sub = 12;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = ctrl[Mathf.Max(0, i - 1)];
                Vector2 p1 = ctrl[i];
                Vector2 p2 = ctrl[i + 1];
                Vector2 p3 = ctrl[Mathf.Min(n - 1, i + 2)];
                for (int s = 0; s < sub; s++)
                {
                    float t = (float)s / sub;
                    dense.Add(EvalCatmullRom(p0, p1, p2, p3, t));
                }
            }
            dense.Add(ctrl[n - 1]);
            return ResamplePolylineByCount(dense, Mathf.Max(2, targetCount));
        }

        /// <summary>Kapalı kontrol poligonu için (son nokta = ilk nokta) sarmalı Catmull-Rom; çıktı da kapalıdır.</summary>
        public static List<Vector2> SampleClosedCatmullRom(List<Vector2> ctrlClosed, int targetCount)
        {
            int n = ctrlClosed.Count - 1; // son nokta tekrar
            var dense = new List<Vector2>();
            const int sub = 12;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = ctrlClosed[(i - 1 + n) % n];
                Vector2 p1 = ctrlClosed[i];
                Vector2 p2 = ctrlClosed[(i + 1) % n];
                Vector2 p3 = ctrlClosed[(i + 2) % n];
                for (int s = 0; s < sub; s++) dense.Add(EvalCatmullRom(p0, p1, p2, p3, (float)s / sub));
            }
            dense.Add(ctrlClosed[0]);
            var res = ResamplePolylineByCount(dense, Mathf.Max(4, targetCount));
            res[res.Count - 1] = res[0];
            return res;
        }

        private static Vector2 EvalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private static List<Vector2> ResamplePolylineByCount(List<Vector2> pts, int count)
        {
            var res = new List<Vector2>(count);
            if (pts.Count < 2 || count < 2) { res.AddRange(pts); return res; }

            float[] cum = new float[pts.Count];
            cum[0] = 0f;
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            float total = cum[pts.Count - 1];
            if (total < 1e-5f) { res.Add(pts[0]); res.Add(pts[pts.Count - 1]); return res; }

            int cursor = 0;
            for (int k = 0; k < count; k++)
            {
                float d = total * k / (count - 1);
                while (cursor < pts.Count - 2 && cum[cursor + 1] < d) cursor++;
                float seg = Mathf.Max(1e-5f, cum[cursor + 1] - cum[cursor]);
                float t = Mathf.Clamp01((d - cum[cursor]) / seg);
                res.Add(Vector2.Lerp(pts[cursor], pts[cursor + 1], t));
            }
            return res;
        }

        private static List<Vector2> ResamplePolylineUniform(List<Vector2> pts, float step)
        {
            var res = new List<Vector2>();
            if (pts == null || pts.Count < 2) return res;
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            int count = Mathf.Clamp(Mathf.CeilToInt(total / Mathf.Max(0.01f, step)) + 1, 2, 240);
            return ResamplePolylineByCount(pts, count);
        }
    }
}
