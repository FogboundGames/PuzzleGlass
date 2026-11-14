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
        public const float MinWidth = 0.80f, MaxWidth = 3.40f;
        public const float MinHeight = 0.60f, MaxHeight = 2.80f;

        public static bool IsBoxPreset(GlassShapePreset p) => p == GlassShapePreset.SquareBox || p == GlassShapePreset.RectangleBox;

        /// <summary>Level oyunda havuz kutusu olarak mı yüklenecek? (özel cam yoksa varsayılan kutu)</summary>
        public static bool IsBox(PuzzleGlassLevelData lvl) => lvl == null || !lvl.useCustomGlassShape || IsBoxPreset(lvl.glassPreset);

        /// <summary>
        /// Düz tabanlı özel şekil mi? (ShapedGlassContainer.IsCustomPool ile aynı kural: serbest kontur, taban yarı genişliği ≥ 0.30)
        /// Oyunda havuz gibi oynanır; top dizilimi her satırda camın genişliğine göre yapılır.
        /// </summary>
        public static bool IsShapedPool(PuzzleGlassLevelData lvl) =>
            lvl != null && lvl.useCustomGlassShape && lvl.glassPreset == GlassShapePreset.CustomFreeform
            && lvl.contourPoints != null && lvl.contourPoints.Count >= 2 && lvl.contourPoints[0].halfWidth >= 0.30f;

        /// <summary>Oyunda havuz mantığıyla oynanır mı? (kutu ya da düz tabanlı özel şekil)</summary>
        public static bool IsPlayablePool(PuzzleGlassLevelData lvl) => IsBox(lvl) || IsShapedPool(lvl);

        /// <summary>Oyundaki top dizilimi (kutu ya da düz tabanlı özel şekil için oyunla aynı hesap).</summary>
        public static OrderedPoolLayout.Result BuildLayout(PuzzleGlassLevelData lvl)
        {
            if (IsShapedPool(lvl))
                return OrderedPoolLayout.BuildShaped(lvl.bottomToTopGroups, lvl.contourPoints, Mathf.Max(0.5f, lvl.widthScale), Mathf.Max(0.5f, lvl.heightScale));
            var d = GetDims(lvl);
            return OrderedPoolLayout.Build(lvl?.bottomToTopGroups, d.innerHalfW, d.floorY, d.ceilY);
        }

        /// <summary>Levelin oyunda kullanacağı kontur (alttan üste).</summary>
        public static List<GlassContourPoint> GetContour(PuzzleGlassLevelData lvl)
        {
            if (lvl != null && lvl.useCustomGlassShape && lvl.contourPoints != null && lvl.contourPoints.Count >= 3)
                return lvl.contourPoints;
            return BuildBoxContour(DefaultWidth, DefaultHeight, DefaultBottomY);
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
