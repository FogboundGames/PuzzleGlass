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
            lvl == null || !lvl.useCustomGlassShape || lvl.glassPreset == GlassShapePreset.TrackChannel;

        /// <summary>Level oyunda havuz kutusu olarak mı yüklenecek?</summary>
        public static bool IsBox(PuzzleGlassLevelData lvl) => lvl != null && lvl.useCustomGlassShape && IsBoxPreset(lvl.glassPreset);

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
                out compiled,
                out layout);
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
            return OrderedPoolLayout.GetBoxSlots(lw, maxRow + 1, d.innerHalfW, d.floorY, d.ceilY, 0f, out diameter);
        }

        /// <summary>Verilen düzeni üst besleme tüpleri açıkken Wave 1 (hazne) + Wave 2 (tüpler) olarak ayırır.</summary>
        public static OrderedPoolLayout.FeedTubeWaveSplit SplitForFeedTubes(PuzzleGlassLevelData lvl, OrderedPoolLayout.Result layout)
        {
            if (lvl == null) return OrderedPoolLayout.SplitFeedTubeWaves(layout.balls, false, BallFeedTubeMode.BothLeftAndRight, 0, null, 12);
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
                return ShapedGlassContainer.BuildManualBottleContour(lvl.manualBalls, lvl.manualLaneWidth);
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
