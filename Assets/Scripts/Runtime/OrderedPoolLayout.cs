using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Sıralı top havuzunun (normal leveller) dizilim hesabı. Oyun (BallPatternGenerator) ve
    /// Level Stüdyosu (önizleme + çözücü) aynı hesabı kullanır; stüdyoda görülen dizilim oyundakiyle aynıdır.
    ///
    /// Gruplar (alttan üste) 3 şeride dağıtılır; her şerit kendi sütunlarını satır satır soldan sağa doldurur.
    /// Toplar petek dizilimde durur: satır aralığı = çap * RowFactor, tek/çift satırlar ±çeyrek çap kaydırılır.
    /// </summary>
    public static class OrderedPoolLayout
    {
        public const float RowFactor = 0.866f;
        public const float MaxDiameter = 0.265f;
        public const float MinDiameter = 0.095f;

        public struct PoolBall
        {
            public PuzzleColor color;
            /// <summary>Camdaki top sütunu (0 = en sol).</summary>
            public int ballColumn;
            /// <summary>Satır (0 = en alt).</summary>
            public int row;
            /// <summary>Cam yerel uzayında merkez.</summary>
            public Vector2 localPos;
        }

        public struct Result
        {
            public List<PoolBall> balls;
            public float diameter;
            public int laneWidth;
            public int totalColumns;
        }

        /// <summary>
        /// Şerit başına sütun sayısını (3..8) ve top çapını seçer: kabın içine sığan en büyük çap.
        /// </summary>
        public static void ChooseLaneWidth(int maxBallsInLane, float safeWidth, float availHeight, out int laneWidth, out float diameter)
        {
            laneWidth = 3;
            float best = -1f;
            float bestDiam = MinDiameter;
            for (int lw = 3; lw <= 8; lw++)
            {
                int cols = lw * 3;
                int laneRows = Mathf.CeilToInt(maxBallsInLane / (float)lw);
                float maxDiamX = safeWidth / cols;
                float maxDiamY = availHeight / (1f + Mathf.Max(0, laneRows - 1) * RowFactor);
                float cand = Mathf.Min(MaxDiameter, Mathf.Min(maxDiamX, maxDiamY));
                if (cand > best + 0.0005f)
                {
                    best = cand;
                    bestDiam = cand;
                    laneWidth = lw;
                }
            }
            diameter = Mathf.Clamp(bestDiam, MinDiameter, MaxDiameter);
        }

        /// <summary>Kabın iç ölçülerinden güvenli genişlik ve kullanılabilir yüksekliği hesaplar.</summary>
        public static void GetUsableArea(float innerHalfW, float floorY, float ceilY, float verticalOffset, out float safeWidth, out float availHeight)
        {
            safeWidth = Mathf.Max(0.8f, innerHalfW * 2f - 0.22f);
            availHeight = Mathf.Max(0.45f, ceilY - floorY - 0.008f - Mathf.Max(0f, verticalOffset));
        }

        /// <summary>Grupları (alttan üste) cam içindeki top konumlarına çevirir.</summary>
        public static Result Build(List<LevelMonsterEntry> bottomToTopGroups, float innerHalfW, float floorY, float ceilY, float verticalOffset = 0f)
        {
            var result = new Result { balls = new List<PoolBall>() };

            List<LevelMonsterEntry>[] lanes = PuzzleLevelGenerator.DistributeGroupsIntoThreeLanes(bottomToTopGroups);
            var laneBalls = new List<PuzzleColor>[3];
            int maxInLane = 0;
            for (int l = 0; l < 3; l++)
            {
                laneBalls[l] = new List<PuzzleColor>();
                for (int g = 0; g < lanes[l].Count; g++)
                {
                    var e = lanes[l][g];
                    for (int k = 0; k < e.count; k++) laneBalls[l].Add(e.color);
                }
                maxInLane = Mathf.Max(maxInLane, laneBalls[l].Count);
            }

            GetUsableArea(innerHalfW, floorY, ceilY, verticalOffset, out float safeWidth, out float availHeight);
            ChooseLaneWidth(maxInLane, safeWidth, availHeight, out int lw, out float d);

            result.diameter = d;
            result.laneWidth = lw;
            result.totalColumns = lw * 3;

            float rowSpacing = d * RowFactor;
            float startY = floorY + d * 0.5f + verticalOffset;
            int colsPerRow = lw * 3;
            int totalRows = Mathf.CeilToInt(maxInLane / (float)lw);

            for (int r = 0; r < totalRows; r++)
            {
                float y = startY + r * rowSpacing;
                float stagger = ((r % 2 == 1) ? 0.25f : -0.25f) * d;
                float firstX = -(colsPerRow - 1) * 0.5f * d + stagger;
                for (int lane = 0; lane < 3; lane++)
                {
                    for (int lc = 0; lc < lw; lc++)
                    {
                        int idx = r * lw + lc;
                        if (idx >= laneBalls[lane].Count) continue;
                        int col = lane * lw + lc;
                        result.balls.Add(new PoolBall
                        {
                            color = laneBalls[lane][idx],
                            ballColumn = col,
                            row = r,
                            localPos = new Vector2(firstX + col * d, y)
                        });
                    }
                }
            }
            return result;
        }

        // ==================== Düz tabanlı özel şekiller (yamuk, altıgen, kapsül...) ====================

        /// <summary>Kontur noktaları arasında doğrusal ara değerle, verilen yükseklikteki iç yarı genişlik.</summary>
        public static float HalfWidthAt(List<GlassContourPoint> contour, float y, float widthScale, float heightScale)
        {
            if (contour == null || contour.Count == 0) return 0f;
            float y0 = contour[0].y * heightScale;
            if (y <= y0) return contour[0].halfWidth * widthScale;
            for (int i = 1; i < contour.Count; i++)
            {
                float ya = contour[i - 1].y * heightScale, yb = contour[i].y * heightScale;
                if (y <= yb)
                {
                    float t = (yb - ya) > 0.0001f ? (y - ya) / (yb - ya) : 0f;
                    return Mathf.Lerp(contour[i - 1].halfWidth, contour[i].halfWidth, t) * widthScale;
                }
            }
            return contour[contour.Count - 1].halfWidth * widthScale;
        }

        /// <summary>
        /// Düz tabanlı özel şekil için dizilim: aynı 3 şerit mantığı, ama her satırda sadece camın o
        /// yükseklikteki genişliğine sığan yerlere top konur (dar tabanda az, geniş gövdede çok top).
        /// Tüm topları sığdıran en büyük çap seçilir.
        /// </summary>
        public static Result BuildShaped(List<LevelMonsterEntry> bottomToTopGroups, List<GlassContourPoint> contour,
                                         float widthScale = 1f, float heightScale = 1f, float wallMargin = 0.07f)
        {
            List<LevelMonsterEntry>[] lanes = PuzzleLevelGenerator.DistributeGroupsIntoThreeLanes(bottomToTopGroups);
            var laneBalls = new List<PuzzleColor>[3];
            int total = 0;
            for (int l = 0; l < 3; l++)
            {
                laneBalls[l] = new List<PuzzleColor>();
                foreach (var e in lanes[l]) for (int k = 0; k < e.count; k++) laneBalls[l].Add(e.color);
                total += laneBalls[l].Count;
            }

            float floorY = contour[0].y * heightScale;
            float ceilY = contour[contour.Count - 1].y * heightScale;
            float maxHw = 0f;
            foreach (var p in contour) maxHw = Mathf.Max(maxHw, p.halfWidth * widthScale);
            float safeWidth = Mathf.Max(0.6f, maxHw * 2f - 0.22f);

            Result best = default;
            bool found = false;
            for (int lw = 3; lw <= 8 && !found; lw++)
            {
                float d = Mathf.Clamp(safeWidth / (lw * 3), MinDiameter, MaxDiameter);
                var r = PlaceShaped(laneBalls, lw, d, contour, widthScale, heightScale, floorY, ceilY, wallMargin, allowOverflow: false);
                if (r.balls.Count == total) { best = r; found = true; }
            }
            if (!found)
            {
                // Hiçbiri sığmadı: en küçük çapla yerleştir, sığmayanlar tavanın üstüne taşar (Cam Stüdyosu uyarır)
                float d = Mathf.Clamp(safeWidth / 24f, MinDiameter, MaxDiameter);
                best = PlaceShaped(laneBalls, 8, d, contour, widthScale, heightScale, floorY, ceilY, wallMargin, allowOverflow: true);
            }
            return best;
        }

        private static Result PlaceShaped(List<PuzzleColor>[] laneBalls, int lw, float d, List<GlassContourPoint> contour,
                                          float ws, float hs, float floorY, float ceilY, float margin, bool allowOverflow)
        {
            var res = new Result { balls = new List<PoolBall>(), diameter = d, laneWidth = lw, totalColumns = lw * 3 };
            int colsPerRow = lw * 3;
            float rowSpacing = d * RowFactor;
            int[] next = new int[3];
            int remaining = laneBalls[0].Count + laneBalls[1].Count + laneBalls[2].Count;

            for (int r = 0; r < 400 && remaining > 0; r++)
            {
                float y = floorY + d * 0.5f + r * rowSpacing;
                if (!allowOverflow && y + d * 0.5f > ceilY + 0.001f) break;
                // Topun alt ve üst kenarındaki genişliğin küçüğü (eğik duvara taşmasın)
                float hw = Mathf.Min(HalfWidthAt(contour, y - d * 0.45f, ws, hs), HalfWidthAt(contour, y + d * 0.45f, ws, hs));
                if (y > ceilY) hw = HalfWidthAt(contour, ceilY, ws, hs); // taşma: tavan genişliğiyle devam
                float limit = hw - margin - d * 0.5f;
                float stagger = ((r % 2 == 1) ? 0.25f : -0.25f) * d;
                float firstX = -(colsPerRow - 1) * 0.5f * d + stagger;

                for (int lane = 0; lane < 3; lane++)
                {
                    for (int lc = 0; lc < lw; lc++)
                    {
                        if (next[lane] >= laneBalls[lane].Count) continue;
                        int col = lane * lw + lc;
                        float x = firstX + col * d;
                        if (Mathf.Abs(x) > limit) continue;
                        res.balls.Add(new PoolBall { color = laneBalls[lane][next[lane]], ballColumn = col, row = r, localPos = new Vector2(x, y) });
                        next[lane]++;
                        remaining--;
                    }
                }
            }

            // Bir şeridin sütunları hiç sığmıyorsa (çok dar cam): kalan toplarını boş yerlere dağıt
            if (remaining > 0 && allowOverflow)
            {
                for (int lane = 0; lane < 3; lane++)
                {
                    while (next[lane] < laneBalls[lane].Count)
                    {
                        int r = 400 + next[lane];
                        res.balls.Add(new PoolBall { color = laneBalls[lane][next[lane]], ballColumn = lw + lane, row = r, localPos = new Vector2(0f, floorY + d * 0.5f + r * rowSpacing) });
                        next[lane]++;
                    }
                }
            }
            return res;
        }

        /// <summary>Cam genişliği boyunca eşit bölünmüş tepsi sütununa göre topun hangi sütunun üstünde olduğu.</summary>
        public static int GridColumnForX(float localX, float innerHalfW, int gridCols)
        {
            float t = (localX + innerHalfW) / Mathf.Max(0.001f, innerHalfW * 2f);
            return Mathf.Clamp(Mathf.FloorToInt(t * gridCols), 0, Mathf.Max(0, gridCols - 1));
        }
    }
}
