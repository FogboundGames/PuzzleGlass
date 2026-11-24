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
        public const float MaxDiameter = GlassTrackLayout.FixedBallDiameter;
        public const float MinDiameter = 0.068f;

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
            /// <summary>Pist bandına sığmayıp gizli kuyrukta bekleyecek toplar (sırasıyla).</summary>
            public List<PuzzleColor> overflowColors;
            /// <summary>Halkalı pistte başlangıçta bantta (halkada) olan top sayısı = bant kapasite sınırı. 0: sınırsız.</summary>
            public int beltCount;
        }

        /// <summary>
        /// Şerit başına sütun sayısını (3..8) ve top çapını seçer: kabın içine sığan en büyük çap.
        /// </summary>
        public static void ChooseLaneWidth(int maxBallsInLane, float safeWidth, float availHeight, out int laneWidth, out float diameter)
        {
            laneWidth = 3;
            float best = -1f;
            float bestDiam = MinDiameter;
            for (int lw = 3; lw <= 10; lw++)
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
            for (int lw = 3; lw <= 10 && !found; lw++)
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

                // Bu satırda sığan tüm yerler, ortadan kenara doğru (son satır yarım kalırsa ortada toplansın)
                var slots = new List<int>();
                for (int col = 0; col < colsPerRow; col++)
                    if (Mathf.Abs(firstX + col * d) <= limit) slots.Add(col);
                slots.Sort((a, b) => Mathf.Abs(firstX + a * d).CompareTo(Mathf.Abs(firstX + b * d)));

                // Her yer önce kendi şeridinden doldurulur; şerit bittiyse en çok topu kalan şeritten alınır.
                // Böylece toplar düz bir seviye oluşturur (dar tabanda yan şeritler yukarı taşıp U şekli olmaz).
                foreach (int col in slots)
                {
                    if (remaining <= 0) break;
                    int lane = Mathf.Clamp(col / lw, 0, 2);
                    if (next[lane] >= laneBalls[lane].Count)
                    {
                        int bestLane = -1, bestLeft = 0;
                        for (int l = 0; l < 3; l++)
                        {
                            int left = laneBalls[l].Count - next[l];
                            if (left > bestLeft) { bestLeft = left; bestLane = l; }
                        }
                        if (bestLane < 0) break;
                        lane = bestLane;
                    }
                    res.balls.Add(new PoolBall { color = laneBalls[lane][next[lane]], ballColumn = col, row = r, localPos = new Vector2(firstX + col * d, y) });
                    next[lane]++;
                    remaining--;
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

        // ==================== Elle Top Dizilimi (Manual Pool) ====================

        public struct Slot
        {
            public int column;
            public int row;
            public Vector2 localPos;
        }

        private static Vector2 SlotPosition(int col, int row, int colsPerRow, float d, float floorY, float verticalOffset)
        {
            float rowSpacing = d * RowFactor;
            float y = floorY + d * 0.5f + verticalOffset + row * rowSpacing;
            float stagger = ((row % 2 == 1) ? 0.25f : -0.25f) * d;
            float firstX = -(colsPerRow - 1) * 0.5f * d + stagger;
            return new Vector2(firstX + col * d, y);
        }

        /// <summary>Düz tabanlı kutu cam içinde verilen şerit genişliği için tüm geçerli petek slotlarını üretir.</summary>
        public static List<Slot> GetBoxSlots(
            int laneWidth, int minRows, float innerHalfW, float floorY, float ceilY,
            float verticalOffset, out float diameter)
        {
            int lw = Mathf.Clamp(laneWidth > 0 ? laneWidth : 4, 3, 8);
            GetUsableArea(innerHalfW, floorY, ceilY, verticalOffset, out float safeWidth, out float availHeight);
            float maxDiamX = safeWidth / (lw * 3);
            int rowsNeeded = Mathf.Max(1, minRows);
            float maxDiamY = availHeight / (1f + Mathf.Max(0, rowsNeeded - 1) * RowFactor);
            diameter = Mathf.Clamp(Mathf.Min(MaxDiameter, Mathf.Min(maxDiamX, maxDiamY)), MinDiameter, MaxDiameter);

            var slots = new List<Slot>();
            int colsPerRow = lw * 3;
            float rowSpacing = diameter * RowFactor;
            float startY = floorY + diameter * 0.5f + verticalOffset;
            for (int r = 0; r < 400; r++)
            {
                float y = startY + r * rowSpacing;
                if (y + diameter * 0.5f > ceilY + 0.001f) break;
                for (int col = 0; col < colsPerRow; col++)
                {
                    slots.Add(new Slot
                    {
                        column = col,
                        row = r,
                        localPos = SlotPosition(col, r, colsPerRow, diameter, floorY, verticalOffset)
                    });
                }
            }
            return slots;
        }

        /// <summary>Şişe/serbest konturlu cam içinde verilen şerit genişliği için tüm geçerli petek slotlarını üretir.</summary>
        public static List<Slot> GetShapedSlots(
            int laneWidth, List<GlassContourPoint> contour,
            float widthScale, float heightScale, float wallMargin, out float diameter)
        {
            var slots = new List<Slot>();
            if (contour == null || contour.Count < 2)
            {
                diameter = MaxDiameter;
                return slots;
            }

            int lw = Mathf.Clamp(laneWidth > 0 ? laneWidth : 4, 3, 8);
            float floorY = contour[0].y * heightScale;
            float ceilY = contour[contour.Count - 1].y * heightScale;
            float maxHw = 0f;
            foreach (var p in contour) maxHw = Mathf.Max(maxHw, p.halfWidth * widthScale);
            float safeWidth = Mathf.Max(0.6f, maxHw * 2f - 0.22f);
            diameter = Mathf.Clamp(safeWidth / (lw * 3), MinDiameter, MaxDiameter);

            int colsPerRow = lw * 3;
            float rowSpacing = diameter * RowFactor;
            for (int r = 0; r < 400; r++)
            {
                float y = floorY + diameter * 0.5f + r * rowSpacing;
                if (y + diameter * 0.5f > ceilY + 0.001f) break;
                float hw = Mathf.Min(HalfWidthAt(contour, y - diameter * 0.45f, widthScale, heightScale),
                                     HalfWidthAt(contour, y + diameter * 0.45f, widthScale, heightScale));
                float limit = hw - wallMargin - diameter * 0.5f;
                float stagger = ((r % 2 == 1) ? 0.25f : -0.25f) * diameter;
                float firstX = -(colsPerRow - 1) * 0.5f * diameter + stagger;
                for (int col = 0; col < colsPerRow; col++)
                {
                    float x = firstX + col * diameter;
                    if (Mathf.Abs(x) <= limit)
                    {
                        slots.Add(new Slot { column = col, row = r, localPos = new Vector2(x, y) });
                    }
                }
            }
            return slots;
        }

        /// <summary>Elle yerleştirilmiş toplardan kutu cam düzeni kurar (alttan üste sıralı).</summary>
        public static Result BuildManual(
            List<ManualPoolBallEntry> manualBalls, int laneWidth,
            float innerHalfW, float floorY, float ceilY, float verticalOffset = 0f)
        {
            int lw = Mathf.Clamp(laneWidth > 0 ? laneWidth : 4, 3, 8);
            int maxRow = 0;
            if (manualBalls != null)
            {
                foreach (var b in manualBalls)
                    if (b != null && b.row > maxRow) maxRow = b.row;
            }
            var slots = GetBoxSlots(lw, maxRow + 1, innerHalfW, floorY, ceilY, verticalOffset, out float d);
            return BuildManualFromSlots(manualBalls, lw, d, floorY, verticalOffset, slots);
        }

        /// <summary>Elle yerleştirilmiş toplardan şişe/özel konturlu cam düzeni kurar (alttan üste sıralı).</summary>
        public static Result BuildManualShaped(
            List<ManualPoolBallEntry> manualBalls, int laneWidth, List<GlassContourPoint> contour,
            float widthScale = 1f, float heightScale = 1f, float wallMargin = 0.07f)
        {
            int lw = Mathf.Clamp(laneWidth > 0 ? laneWidth : 4, 3, 8);
            var slots = GetShapedSlots(lw, contour, widthScale, heightScale, wallMargin, out float d);
            float floorY = (contour != null && contour.Count > 0) ? contour[0].y * heightScale : -0.56f;
            return BuildManualFromSlots(manualBalls, lw, d, floorY, 0f, slots);
        }

        private static Result BuildManualFromSlots(
            List<ManualPoolBallEntry> manualBalls, int lw, float d, float floorY, float verticalOffset, List<Slot> slots)
        {
            int colsPerRow = lw * 3;
            var res = new Result
            {
                balls = new List<PoolBall>(),
                diameter = d,
                laneWidth = lw,
                totalColumns = colsPerRow
            };
            if (manualBalls == null || manualBalls.Count == 0) return res;

            var slotMap = new Dictionary<(int col, int row), Vector2>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
                slotMap[(slots[i].column, slots[i].row)] = slots[i].localPos;

            var seen = new HashSet<(int col, int row)>();
            for (int i = 0; i < manualBalls.Count; i++)
            {
                var entry = manualBalls[i];
                if (entry == null) continue;
                int col = Mathf.Clamp(entry.column, 0, colsPerRow - 1);
                int row = Mathf.Max(0, entry.row);
                if (!seen.Add((col, row))) continue;

                Vector2 pos = slotMap.TryGetValue((col, row), out Vector2 mapped)
                    ? mapped
                    : SlotPosition(col, row, colsPerRow, d, floorY, verticalOffset);
                res.balls.Add(new PoolBall
                {
                    color = entry.color,
                    ballColumn = col,
                    row = row,
                    localPos = pos
                });
            }

            res.balls.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.ballColumn.CompareTo(b.ballColumn));
            return res;
        }

        /// <summary>Her sütundaki topları aynı sütunun en alttaki boş geçerli slotlarına oturtur (yerçekimi).</summary>
        public static void ApplyGravityToManualBalls(List<ManualPoolBallEntry> manualBalls, List<Slot> validSlots)
        {
            if (manualBalls == null || manualBalls.Count == 0 || validSlots == null || validSlots.Count == 0) return;

            var rowsByCol = new Dictionary<int, List<int>>();
            for (int i = 0; i < validSlots.Count; i++)
            {
                var s = validSlots[i];
                if (!rowsByCol.TryGetValue(s.column, out var list))
                {
                    list = new List<int>();
                    rowsByCol[s.column] = list;
                }
                list.Add(s.row);
            }
            foreach (var list in rowsByCol.Values) list.Sort();

            var ballsByCol = new Dictionary<int, List<ManualPoolBallEntry>>();
            for (int i = 0; i < manualBalls.Count; i++)
            {
                var b = manualBalls[i];
                if (b == null) continue;
                if (!ballsByCol.TryGetValue(b.column, out var list))
                {
                    list = new List<ManualPoolBallEntry>();
                    ballsByCol[b.column] = list;
                }
                list.Add(b);
            }

            foreach (var kv in ballsByCol)
            {
                kv.Value.Sort((a, b) => a.row.CompareTo(b.row));
                if (!rowsByCol.TryGetValue(kv.Key, out var validRows) || validRows.Count == 0) continue;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    int targetRow = i < validRows.Count ? validRows[i] : validRows[validRows.Count - 1] + (i - validRows.Count + 1);
                    if (kv.Value[i].row != targetRow) kv.Value[i].hasTrackPos = false; // kanalda yeni yer: eski konum kaydı bırakılır
                    kv.Value[i].row = targetRow;
                }
            }
        }

        // ==================== Üst Besleme Tüpleri Dalga Ayrımı ====================

        public struct FeedTubeWaveSplit
        {
            public List<PoolBall> chamberBalls;
            public List<PuzzleColor> leftTubeColors;
            public List<PuzzleColor> rightTubeColors;
            public List<PoolBall> wave2Balls;
            public int effectiveContainerLimit;
        }

        /// <summary>
        /// Üst cam besleme tüpleri açıkken topları BallPatternGenerator ve BallFeedTubeController ile aynı
        /// sırayla Wave 1 (hazne) ve Wave 2 (tüpler + boşalan hazneye iniş konumları) olarak ayırır.
        /// </summary>
        public static FeedTubeWaveSplit SplitFeedTubeWaves(
            List<PoolBall> allBalls,
            bool useBallFeedTubes,
            BallFeedTubeMode feedTubeMode,
            int feedTubeContainerLimit,
            List<Slot> validSlots,
            int totalColumns)
        {
            var split = new FeedTubeWaveSplit
            {
                chamberBalls = new List<PoolBall>(),
                leftTubeColors = new List<PuzzleColor>(),
                rightTubeColors = new List<PuzzleColor>(),
                wave2Balls = new List<PoolBall>(),
                effectiveContainerLimit = 0
            };
            if (allBalls == null || allBalls.Count == 0) return split;

            var sorted = new List<PoolBall>(allBalls);
            sorted.Sort((a, b) =>
            {
                int cy = a.localPos.y.CompareTo(b.localPos.y);
                if (cy != 0) return cy;
                int cr = a.row.CompareTo(b.row);
                return cr != 0 ? cr : a.ballColumn.CompareTo(b.ballColumn);
            });

            if (!useBallFeedTubes || sorted.Count <= 1)
            {
                split.chamberBalls.AddRange(sorted);
                split.effectiveContainerLimit = sorted.Count;
                return split;
            }

            int eff = BallFeedTubeUtility.GetEffectiveContainerLimit(feedTubeContainerLimit, sorted.Count);
            split.effectiveContainerLimit = eff;
            if (eff >= sorted.Count)
            {
                split.chamberBalls.AddRange(sorted);
                return split;
            }

            for (int i = 0; i < eff; i++) split.chamberBalls.Add(sorted[i]);

            bool useLeft = feedTubeMode == BallFeedTubeMode.BothLeftAndRight || feedTubeMode == BallFeedTubeMode.LeftOnly;
            bool useRight = feedTubeMode == BallFeedTubeMode.BothLeftAndRight || feedTubeMode == BallFeedTubeMode.RightOnly;
            bool flip = true;
            var queued = new List<(PuzzleColor color, bool fromLeft)>(sorted.Count - eff);
            for (int i = eff; i < sorted.Count; i++)
            {
                bool toLeft = useLeft && (!useRight || flip);
                if (useLeft && useRight) flip = !flip;
                if (toLeft) split.leftTubeColors.Add(sorted[i].color);
                else split.rightTubeColors.Add(sorted[i].color);
                queued.Add((sorted[i].color, toLeft));
            }

            if (validSlots == null || validSlots.Count == 0)
            {
                for (int i = 0; i < queued.Count; i++)
                {
                    split.wave2Balls.Add(new PoolBall
                    {
                        color = queued[i].color,
                        ballColumn = i % Mathf.Max(1, totalColumns),
                        row = i / Mathf.Max(1, totalColumns),
                        localPos = sorted[Mathf.Min(i, sorted.Count - 1)].localPos
                    });
                }
                return split;
            }

            // Wave 2: hazne tamamen boşaldıktan sonra tüplerden dökülen topların hazneye yerleşimi
            var used = new bool[validSlots.Count];
            int midCol = Mathf.Max(1, totalColumns / 2);
            int maxSpan = Mathf.Max(2, Mathf.CeilToInt(totalColumns * 0.67f));
            for (int i = 0; i < queued.Count; i++)
            {
                bool fromLeft = queued[i].fromLeft;
                int chosenIdx = -1;
                int bestScore = int.MaxValue;

                for (int sIdx = 0; sIdx < validSlots.Count; sIdx++)
                {
                    if (used[sIdx]) continue;
                    var sl = validSlots[sIdx];
                    int sideDist;
                    if (feedTubeMode == BallFeedTubeMode.LeftOnly)
                    {
                        bool inPrimary = sl.column < maxSpan;
                        sideDist = (inPrimary ? 0 : 100) + sl.column;
                    }
                    else if (feedTubeMode == BallFeedTubeMode.RightOnly)
                    {
                        bool inPrimary = sl.column >= totalColumns - maxSpan;
                        sideDist = (inPrimary ? 0 : 100) + (totalColumns - 1 - sl.column);
                    }
                    else
                    {
                        bool onSide = fromLeft ? (sl.column < midCol) : (sl.column >= midCol);
                        int distFromEdge = fromLeft ? sl.column : (totalColumns - 1 - sl.column);
                        sideDist = (onSide ? 0 : 100) + distFromEdge;
                    }
                    int score = sl.row * 1000 + sideDist;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        chosenIdx = sIdx;
                    }
                }

                if (chosenIdx < 0) chosenIdx = Mathf.Min(i, validSlots.Count - 1);
                used[chosenIdx] = true;
                var slot = validSlots[chosenIdx];
                split.wave2Balls.Add(new PoolBall
                {
                    color = queued[i].color,
                    ballColumn = slot.column,
                    row = slot.row,
                    localPos = slot.localPos
                });
            }

            return split;
        }
    }
}
