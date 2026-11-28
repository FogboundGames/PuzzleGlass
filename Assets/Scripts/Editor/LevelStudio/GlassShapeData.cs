using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Bir levelin cam ölçüleri: levelin kendi (özel) camı varsa ondan, yoksa oyunun varsayılan havuz kutusundan.
    /// Level Stüdyosu (önizleme + çözücü) ve Cam Stüdyosu aynı hesabı kullanır.
    /// </summary>
    public static class GlassShapeData
    {
        public const float DefaultWidth = 2.64f;
        public const float DefaultHeight = 1.18f;
        public const float DefaultBottomY = -0.56f;
        public const float MinWidth = 0.70f, MaxWidth = 3.40f;
        public const float MinHeight = 0.50f, MaxHeight = 2.80f;

        public static bool IsBoxPreset(GlassShapePreset p) => p == GlassShapePreset.SquareBox || p == GlassShapePreset.RectangleBox;

        /// <summary>Eğrisel cam pist / kanal mı? (Varsayılan üst cam artık Görsel 1 tarzı pisttir.)</summary>
        public static bool IsTrack(PuzzleGlassLevelData lvl) =>
            lvl == null || (lvl.useCustomGlassShape ? lvl.glassPreset == GlassShapePreset.TrackChannel : !lvl.useManualBallLayout);

        /// <summary>Elle top dizimli ama kendi cam şekli olmayan level: oyunda ve editörde düz dikdörtgen kutu cam.</summary>
        public static bool IsManualDefaultBox(PuzzleGlassLevelData lvl) =>
            lvl != null && !lvl.useCustomGlassShape && lvl.useManualBallLayout;

        /// <summary>Level oyunda havuz kutusu olarak mı yüklenecek?</summary>
        public static bool IsBox(PuzzleGlassLevelData lvl) =>
            lvl != null && ((lvl.useCustomGlassShape && IsBoxPreset(lvl.glassPreset)) || IsManualDefaultBox(lvl));

        /// <summary>Şişe tabanlı cam mı?</summary>
        public static bool IsBottle(PuzzleGlassLevelData lvl) =>
            lvl != null && lvl.useCustomGlassShape && lvl.glassIsBottle && lvl.glassPreset != GlassShapePreset.TrackChannel;

        /// <summary>Şişenin genişliği ve yüksekliği (konturdan).</summary>
        public static void GetBottle(PuzzleGlassLevelData lvl, out float width, out float height)
        {
            var c = GetContour(lvl);
            width = c[c.Count - 1].halfWidth * 2f;
            height = c[c.Count - 1].y - c[0].y;
        }

        /// <summary>
        /// Düz tabanlı özel şekil mi? (ShapedGlassContainer.IsCustomPool ile aynı kural: serbest kontur, taban yarı genişliği ≥ 0.30)
        /// Oyunda havuz gibi oynanır; top dizilimi her satırda camın genişliğine göre yapılır.
        /// </summary>
        public static bool IsShapedPool(PuzzleGlassLevelData lvl)
        {
            if (lvl == null || !lvl.useCustomGlassShape || lvl.glassPreset == GlassShapePreset.TrackChannel) return false;
            return lvl.glassPreset == GlassShapePreset.CustomFreeform
                && lvl.contourPoints != null && lvl.contourPoints.Count >= 2 && lvl.contourPoints[0].halfWidth >= 0.30f;
        }

        /// <summary>Oyunda oynanabilir cam mı? (pist, kutu ya da düz tabanlı özel şekil)</summary>
        public static bool IsPlayablePool(PuzzleGlassLevelData lvl) => IsTrack(lvl) || IsBox(lvl) || IsShapedPool(lvl);

        public static void BuildTrack(PuzzleGlassLevelData lvl, out GlassTrackLayout.CompiledTrack compiled, out OrderedPoolLayout.Result layout)
        {
            GlassTrackPreset preset = (lvl != null && lvl.useCustomGlassShape) ? lvl.trackPreset : GlassTrackPreset.ArchLoop;
            var branches = (lvl != null && lvl.useCustomGlassShape) ? lvl.trackBranches : null;
            int lanes = (lvl != null && lvl.useCustomGlassShape) ? lvl.trackLanesAcross : 0;
            bool autoFit = lvl == null || !lvl.useCustomGlassShape || lvl.trackAutoFitToBalls;
            float scale = (lvl != null && lvl.useCustomGlassShape && lvl.trackScale > 0.05f) ? lvl.trackScale : 1.0f;
            float wallThick = (lvl != null && lvl.useCustomGlassShape && lvl.trackWallThickness > 0.01f) ? lvl.trackWallThickness : 0.082f;

            List<LevelMonsterEntry> groups = lvl?.bottomToTopGroups;
            if ((groups == null || groups.Count == 0) && lvl != null && !lvl.HasAnyFilledBox() && lvl.monsters != null && lvl.monsters.Count > 0)
                groups = lvl.monsters;

            // Önbellek: pist derlemesi (duvarlar, 112x112 mesafe alanı, şeritler) ~300 ms sürüyor ve stüdyo her tıklamada
            // birkaç kez çağırıyordu (dizilim, ölçüler, çözücü kontrolü). Girdilerin içeriği aynıysa hazır sonuç döner.
            string key = TrackCacheKey(preset, branches, groups, lvl, lanes, autoFit, scale, wallThick);
            if (s_TrackCache.TryGetValue(key, out var cached))
            {
                compiled = cached.compiled;
                layout = cached.layout;
                return;
            }

            GlassTrackLayout.BuildForLevel(
                preset,
                branches,
                groups,
                lvl?.manualBalls,
                lvl != null && lvl.useManualBallLayout,
                lanes,
                autoFit,
                scale,
                wallThick,
                0f,
                float.NaN,
                out compiled,
                out layout,
                lvl != null ? lvl.manualTrackCapacity : 0,
                throatEnabled: lvl != null && lvl.trackThroatEnabled,
                throatNormalizedCenter: lvl != null ? lvl.trackThroatNormalizedCenter : Vector2.zero,
                throatRadius: lvl != null ? lvl.trackThroatRadius : 0.22f,
                throatWidthRatio: lvl != null ? lvl.trackThroatWidthRatio : 1f);

            if (s_TrackCache.Count >= TrackCacheMax) s_TrackCache.Clear();
            s_TrackCache[key] = (compiled, layout);
        }

        private const int TrackCacheMax = 16;
        private static readonly Dictionary<string, (GlassTrackLayout.CompiledTrack compiled, OrderedPoolLayout.Result layout)> s_TrackCache =
            new Dictionary<string, (GlassTrackLayout.CompiledTrack, OrderedPoolLayout.Result)>();
        private static readonly System.Text.StringBuilder s_KeySb = new System.Text.StringBuilder(4096);

        /// <summary>Pist derlemesini etkileyen tüm girdilerin içeriği (serileştirilmiş alanlar dahil).</summary>
        private static string TrackCacheKey(GlassTrackPreset preset, List<GlassTrackBranch> branches, List<LevelMonsterEntry> groups,
            PuzzleGlassLevelData lvl, int lanes, bool autoFit, float scale, float wallThick)
        {
            var sb = s_KeySb;
            sb.Clear();
            sb.Append((int)preset).Append('|').Append(lanes).Append('|').Append(autoFit).Append('|')
              .Append(scale.ToString("R")).Append('|').Append(wallThick.ToString("R")).Append('|');
            if (lvl != null)
            {
                sb.Append(lvl.useManualBallLayout).Append('|').Append(lvl.manualTrackCapacity).Append('|');
                // Boğaz pistin kesitini (ve dolayısıyla slot sayısını) değiştirir: önbellek anahtarına girmeli
                sb.Append(lvl.trackThroatEnabled).Append('|')
                  .Append(lvl.trackThroatNormalizedCenter.x.ToString("R")).Append(',')
                  .Append(lvl.trackThroatNormalizedCenter.y.ToString("R")).Append('|')
                  .Append(lvl.trackThroatRadius.ToString("R")).Append('|')
                  .Append(lvl.trackThroatWidthRatio.ToString("R")).Append('|');
            }
            if (branches != null) foreach (var b in branches) sb.Append(JsonUtility.ToJson(b)).Append(';');
            sb.Append('#');
            if (groups != null) foreach (var g in groups) if (g != null) sb.Append(JsonUtility.ToJson(g)).Append(';');
            sb.Append('#');
            if (lvl != null && lvl.useManualBallLayout && lvl.manualBalls != null)
                foreach (var m in lvl.manualBalls) if (m != null) sb.Append(JsonUtility.ToJson(m)).Append(';');
            return sb.ToString();
        }

        /// <summary>
        /// Kanalda elle dizim: her topun adresini (sütun/sıra) oturduğu slota göre günceller ve konumunu (pist sınırlarına
        /// göre 0..1) kaydeder. Böylece slot üretimi ileride değişse de toplar kayıtlı konumlarına en yakın slota oturur.
        /// Bir şey değiştiyse true döner.
        /// </summary>
        public static bool CanonicalizeManualTrackBalls(PuzzleGlassLevelData lvl)
        {
            if (lvl == null || !lvl.useManualBallLayout || lvl.manualBalls == null || lvl.manualBalls.Count == 0 || !IsTrack(lvl)) return false;
            BuildTrack(lvl, out var track, out _);
            if (track == null || track.manualSlots == null || track.manualSlots.Count == 0) return false;
            var slots = track.manualSlots;
            int[] assign = GlassTrackLayout.ResolveManualAssignments(track, slots, lvl.manualBalls);
            bool changed = false;
            for (int i = 0; i < lvl.manualBalls.Count; i++)
            {
                var mb = lvl.manualBalls[i];
                if (mb == null || assign[i] < 0) continue;
                var s = slots[assign[i]];
                Vector2 norm = GlassTrackLayout.NormalizeTrackPos(track, s.localPos);
                if (mb.column != s.column || mb.row != s.row || !mb.hasTrackPos || (mb.trackPos - norm).sqrMagnitude > 1e-10f)
                {
                    mb.column = s.column;
                    mb.row = s.row;
                    mb.hasTrackPos = true;
                    mb.trackPos = norm;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Oyundaki top dizilimi (pist, kutu ya da düz tabanlı özel şekil için oyunla aynı hesap).</summary>
        public static OrderedPoolLayout.Result BuildLayout(PuzzleGlassLevelData lvl)
        {
            if (IsTrack(lvl))
            {
                BuildTrack(lvl, out _, out var trackLayout);
                return trackLayout;
            }
            if (lvl != null && lvl.useManualBallLayout)
            {
                if (IsShapedPool(lvl))
                {
                    bool custom = lvl.useCustomGlassShape;
                    return OrderedPoolLayout.BuildManualShaped(lvl.manualBalls, lvl.manualLaneWidth, GetContour(lvl),
                        custom ? Mathf.Max(0.5f, lvl.widthScale) : 1f, custom ? Mathf.Max(0.5f, lvl.heightScale) : 1f);
                }
                var md = GetDims(lvl);
                return OrderedPoolLayout.BuildManual(lvl.manualBalls, lvl.manualLaneWidth, md.innerHalfW, md.floorY, md.ceilY);
            }
            if (IsShapedPool(lvl))
            {
                bool custom = lvl != null && lvl.useCustomGlassShape;
                return OrderedPoolLayout.BuildShaped(lvl?.bottomToTopGroups, GetContour(lvl),
                    custom ? Mathf.Max(0.5f, lvl.widthScale) : 1f, custom ? Mathf.Max(0.5f, lvl.heightScale) : 1f);
            }
            var d = GetDims(lvl);
            return OrderedPoolLayout.Build(lvl?.bottomToTopGroups, d.innerHalfW, d.floorY, d.ceilY);
        }

        /// <summary>Elle top yerleşimi için camdaki tüm geçerli petek slotlarını döndürür.</summary>
        public static List<OrderedPoolLayout.Slot> GetManualSlots(
            PuzzleGlassLevelData lvl, out float diameter, out int laneWidth, out int totalColumns)
        {
            int lw = Mathf.Clamp(lvl != null && lvl.manualLaneWidth > 0 ? lvl.manualLaneWidth : 4, 3, 8);
            laneWidth = lw;
            totalColumns = lw * 3;
            if (IsTrack(lvl))
            {
                // Kanal: oyunla aynı pist slotları (sıra 0 = çıkışa en yakın)
                BuildTrack(lvl, out var compiled, out var trackLayout);
                diameter = compiled != null ? compiled.ballDiameter : trackLayout.diameter;
                var trackSlots = compiled != null ? compiled.manualSlots : new List<OrderedPoolLayout.Slot>();
                int maxCol = 0;
                foreach (var s in trackSlots) maxCol = Mathf.Max(maxCol, s.column);
                totalColumns = maxCol + 1;
                return trackSlots;
            }
            if (IsShapedPool(lvl))
            {
                bool custom = lvl != null && lvl.useCustomGlassShape;
                return OrderedPoolLayout.GetShapedSlots(
                    lw, GetContour(lvl),
                    custom ? Mathf.Max(0.5f, lvl.widthScale) : 1f,
                    custom ? Mathf.Max(0.5f, lvl.heightScale) : 1f,
                    0.07f, out diameter);
            }
            var d = GetDims(lvl);
            int maxRow = 0;
            if (lvl != null && lvl.manualBalls != null)
            {
                foreach (var b in lvl.manualBalls)
                    if (b != null && b.row > maxRow) maxRow = b.row;
            }
            // Elle dizimde en az 12 satır (ve dolu en üst satırın 6 satır üstü) açılır; böylece istenildiği kadar top dizilebilir.
            int targetRows = Mathf.Max(maxRow + 6, 12);
            float safeW = Mathf.Max(0.60f, d.innerHalfW * 2f - 0.14f);
            float maxDia = Mathf.Clamp(safeW / (lw * 3), OrderedPoolLayout.MinDiameter, OrderedPoolLayout.MaxDiameter);
            float neededCeilY = d.floorY + maxDia + (targetRows - 1) * (maxDia * OrderedPoolLayout.RowFactor) + 0.02f;
            float effCeilY = Mathf.Max(d.ceilY, neededCeilY);
            return OrderedPoolLayout.GetBoxSlots(lw, targetRows, d.innerHalfW, d.floorY, effCeilY, 0f, out diameter);
        }

        /// <summary>Verilen düzeni üst besleme tüpleri açıkken Wave 1 (hazne) + Wave 2 (tüpler) olarak ayırır.</summary>
        public static OrderedPoolLayout.FeedTubeWaveSplit SplitForFeedTubes(PuzzleGlassLevelData lvl, OrderedPoolLayout.Result layout)
        {
            int totalCols = Mathf.Max(1, layout.totalColumns);
            if (lvl == null || !lvl.useBallFeedTubes)
                return OrderedPoolLayout.SplitFeedTubeWaves(layout.balls, false, BallFeedTubeMode.BothLeftAndRight, 0, null, totalCols);
            int lw = Mathf.Clamp(layout.laneWidth > 0 ? layout.laneWidth : 4, 3, 8);
            List<OrderedPoolLayout.Slot> slots;
            if (IsShapedPool(lvl))
            {
                bool custom = lvl.useCustomGlassShape;
                slots = OrderedPoolLayout.GetShapedSlots(
                    lw, GetContour(lvl),
                    custom ? Mathf.Max(0.5f, lvl.widthScale) : 1f,
                    custom ? Mathf.Max(0.5f, lvl.heightScale) : 1f,
                    0.07f, out _);
            }
            else
            {
                var d = GetDims(lvl);
                int maxRow = 0;
                if (layout.balls != null)
                    foreach (var b in layout.balls) if (b.row > maxRow) maxRow = b.row;
                slots = OrderedPoolLayout.GetBoxSlots(lw, maxRow + 1, d.innerHalfW, d.floorY, d.ceilY, 0f, out _);
            }
            return OrderedPoolLayout.SplitFeedTubeWaves(
                layout.balls,
                lvl.useBallFeedTubes,
                lvl.feedTubeMode,
                lvl.feedTubeContainerLimit,
                slots,
                Mathf.Max(1, layout.totalColumns));
        }

        /// <summary>Levelin oyunda kullanacağı kontur (alttan üste).</summary>
        public static List<GlassContourPoint> GetContour(PuzzleGlassLevelData lvl)
        {
            if (lvl != null && lvl.useCustomGlassShape && lvl.contourPoints != null && lvl.contourPoints.Count >= 3)
                return lvl.contourPoints;
            if (lvl != null && lvl.useManualBallLayout)
                return ShapedGlassContainer.BuildManualBoxContour(lvl.manualBalls, lvl.manualLaneWidth);
            return ShapedGlassContainer.BuildAutoBottleContour(lvl?.bottomToTopGroups);
        }

        /// <summary>ShapedGlassContainer.ConfigureRoundedBoxPool ile aynı kutu konturu.</summary>
        public static List<GlassContourPoint> BuildBoxContour(float width, float height, float bottomY)
        {
            float hw = Mathf.Clamp(width, MinWidth, MaxWidth) * 0.5f;
            float top = bottomY + Mathf.Clamp(height, MinHeight, MaxHeight);
            return new List<GlassContourPoint>
            {
                new GlassContourPoint(bottomY, hw, 0f, 1f),
                new GlassContourPoint(bottomY + 0.04f, hw, 0f, 1f),
                new GlassContourPoint(top - 0.04f, hw, 0f, 1f),
                new GlassContourPoint(top, hw, 0f, 1f)
            };
        }

        public static void GetBox(PuzzleGlassLevelData lvl, out float width, out float height, out float bottomY)
        {
            var c = GetContour(lvl);
            bottomY = c[0].y;
            width = c[0].halfWidth * 2f;
            height = c[c.Count - 1].y - bottomY;
        }

        /// <summary>Top dizilimi ve çözücü için camın iç ölçüleri (kutu ise tam; özel şekilde en geniş yerden tahmini).</summary>
        public static PuzzleLevelSolver.GlassDims GetDims(PuzzleGlassLevelData lvl)
        {
            if (IsTrack(lvl))
            {
                BuildTrack(lvl, out var compiled, out _);
                if (compiled != null)
                {
                    return new PuzzleLevelSolver.GlassDims
                    {
                        innerHalfW = Mathf.Max(0.6f, compiled.outerBounds.width * 0.5f),
                        floorY = compiled.outerBounds.yMin,
                        ceilY = compiled.outerBounds.yMax
                    };
                }
            }
            var c = GetContour(lvl);
            float w = lvl != null && lvl.useCustomGlassShape ? Mathf.Max(0.5f, lvl.widthScale) : 1f;
            float h = lvl != null && lvl.useCustomGlassShape ? Mathf.Max(0.5f, lvl.heightScale) : 1f;
            if (IsBox(lvl))
            {
                return new PuzzleLevelSolver.GlassDims
                {
                    innerHalfW = c[0].halfWidth * w,
                    floorY = c[0].y * h,
                    ceilY = c[c.Count - 1].y * h
                };
            }
            float maxHw = 0f;
            foreach (var p in c) maxHw = Mathf.Max(maxHw, p.halfWidth);
            return new PuzzleLevelSolver.GlassDims
            {
                innerHalfW = maxHw * w,
                floorY = c[0].y * h,
                ceilY = c[c.Count - 1].y * h
            };
        }
    }
}
