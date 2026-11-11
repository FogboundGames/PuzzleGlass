using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum LevelDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
        Boss = 3
    }

    public enum BottleLayerPattern
    {
        VFunnelBands = 0,      // Huni V-Katmanları (Referans Video Tarzı)
        HorizontalWaves = 1,   // Yatay Dalgalı Katmanlar
        LeftRightSplit = 2,    // Sol & Sağ Bölünmüş Bloklar
        DiagonalBands = 3      // Çapraz Kuşaklar
    }

    public enum GlassCompartmentPreset
    {
        None_OrderedPool = 0,               // Bölmesiz Sıralı Havuz (Alt -> Orta -> Üst)
        TwoVertical_LeftRight = 1,          // 2 Dikey Bölme (Sol / Sağ Ayırıcı)
        ThreeVertical_Columns = 2,          // 3 Dikey Bölme (Sol / Orta / Sağ Sütun)
        InvertedT_BottomRowPlusSplitTop = 3,// Ters-T Bölme (Alt Tam Sıra + Üst Sol/Sağ Bölme)
        TopT_SplitBottomPlusTopRow = 4,     // Üst-T Bölme (Alt Sol/Sağ Bölme + Üst Tam Sıra)
        VFunnel_SlopedChannels = 5,         // V-Huni Bölme (Eğimli Kanallar)
        ZigZag_Shelves = 6,                 // Zikzak Raf Bölme (Kaydıraklı Katmanlar)
        Custom = 7                          // Özel Serbest Bölme Tasarımı
    }

    [Serializable]
    public class GlassDividerData
    {
        public string name = "Cam Bölme";
        public bool enabled = true;
        [Tooltip("Cam havuz içindeki yerel (X, Y) merkez konumu (X: -1.30..+1.30, Y: -0.54..+0.58)")]
        public Vector2 center = new Vector2(0f, -0.05f);
        [Range(0.10f, 2.60f)]
        public float length = 0.72f;
        [Range(0.03f, 0.22f)]
        public float thickness = 0.085f;
        [Range(-90f, 90f)]
        [Tooltip("90 = Dikey Bölme Duvarı, 0 = Yatay Raf, -35/+35 = Eğimli Kanal")]
        public float angleDegrees = 90f;

        public GlassDividerData() { }

        public GlassDividerData(string name, Vector2 center, float length, float thickness = 0.085f, float angleDegrees = 90f, bool enabled = true)
        {
            this.name = name;
            this.center = center;
            this.length = Mathf.Clamp(length, 0.10f, 2.60f);
            this.thickness = Mathf.Clamp(thickness, 0.03f, 0.22f);
            this.angleDegrees = Mathf.Clamp(angleDegrees, -90f, 90f);
            this.enabled = enabled;
        }

        public GlassDividerData Clone()
        {
            return new GlassDividerData(name, center, length, thickness, angleDegrees, enabled);
        }

        /// <summary>
        /// Checks whether a circle (ball) at container-local position pt with given radius overlaps this oriented divider bar.
        /// </summary>
        public bool OverlapsCircle(Vector2 pt, float radius, float extraMargin = 0.012f)
        {
            if (!enabled) return false;

            Vector2 d = pt - center;
            float rad = -angleDegrees * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(rad);

            float lx = Mathf.Abs(d.x * cosA - d.y * sinA);
            float ly = Mathf.Abs(d.x * sinA + d.y * cosA);

            float halfL = length * 0.5f;
            float halfT = thickness * 0.5f;

            float qx = Mathf.Max(0f, lx - halfL);
            float qy = Mathf.Max(0f, ly - halfT);
            float r = radius + extraMargin;

            return (qx * qx + qy * qy) <= (r * r);
        }

        /// <summary>
        /// Returns true if this divider acts as a vertical/steep partition that separates xA and xB at height y.
        /// </summary>
        public bool BlocksHorizontalBetween(float xA, float xB, float y, float yPadding = 0.06f)
        {
            if (!enabled) return false;

            float rad = angleDegrees * Mathf.Deg2Rad;
            float sinA = Mathf.Sin(rad);
            if (Mathf.Abs(sinA) < 0.55f) return false; // Nearly horizontal shelf, not a vertical left/right separator

            float halfSpanY = Mathf.Abs(sinA) * (length * 0.5f) + Mathf.Abs(Mathf.Cos(rad)) * (thickness * 0.5f) + yPadding;
            if (y < center.y - halfSpanY || y > center.y + halfSpanY) return false;

            float cosA = Mathf.Cos(rad);
            float dy = y - center.y;
            float dividerXAtY = center.x + (Mathf.Abs(sinA) > 0.01f ? dy * (cosA / sinA) : 0f);

            float minX = Mathf.Min(xA, xB);
            float maxX = Mathf.Max(xA, xB);
            return dividerXAtY > minX + 0.01f && dividerXAtY < maxX - 0.01f;
        }
    }

    [Serializable]
    public class CompartmentColorZone
    {
        public string zoneName = "Bölme Alanı";
        [Tooltip("Normalize X aralığı (-1 = Sol Duvar, 0 = Orta, +1 = Sağ Duvar)")]
        public Vector2 normalizedXRange = new Vector2(-1f, 1f);
        [Tooltip("-1 = Tüm satırlar, 0 = En alt satır, 1 = 2. satır, 2 = 3. satır, 3 = 4. satır")]
        public int rowLayer = -1;
        public PuzzleColor color = PuzzleColor.Blue;

        public CompartmentColorZone() { }

        public CompartmentColorZone(string zoneName, float minNormX, float maxNormX, int rowLayer, PuzzleColor color)
        {
            this.zoneName = zoneName;
            this.normalizedXRange = new Vector2(Mathf.Clamp(minNormX, -1.05f, 1.05f), Mathf.Clamp(maxNormX, -1.05f, 1.05f));
            this.rowLayer = rowLayer;
            this.color = color;
        }

        public CompartmentColorZone Clone()
        {
            return new CompartmentColorZone(zoneName, normalizedXRange.x, normalizedXRange.y, rowLayer, color);
        }

        public bool Matches(float normX, int r)
        {
            if (rowLayer >= 0 && rowLayer != r) return false;
            float minX = Mathf.Min(normalizedXRange.x, normalizedXRange.y);
            float maxX = Mathf.Max(normalizedXRange.x, normalizedXRange.y);
            return normX >= minX - 0.001f && normX <= maxX + 0.001f;
        }
    }

    [Serializable]
    public class LevelMonsterEntry
    {
        public PuzzleColor color = PuzzleColor.Blue;
        [Range(1, 50)]
        public int count = 10;

        public LevelMonsterEntry() { }

        public LevelMonsterEntry(PuzzleColor color, int count)
        {
            this.color = color;
            this.count = Mathf.Max(1, count);
        }
    }

    [CreateAssetMenu(fileName = "Level_01", menuName = "PuzzleGlass/Level Data", order = 1)]
    public class PuzzleGlassLevelData : ScriptableObject
    {
        [Header("Level Identity")]
        public int levelNumber = 1;
        public string levelName = "Level 1 - Gök Mavisi Flask";
        public LevelDifficulty difficulty = LevelDifficulty.Easy;

        [Header("Slot Configuration")]
        [Range(1, 5)]
        public int slotCount = 4;
        [Range(0.60f, 1.10f)]
        public float slotSpacing = 0.84f;

        [Header("Glass Shape & Aesthetics")]
        public GlassShapePreset glassPreset = GlassShapePreset.RectangleBox;
        public GlassColorTheme colorTheme = GlassColorTheme.SkyBlueGlossy;
        public BottleLayerPattern layerPattern = BottleLayerPattern.VFunnelBands;
        public bool symmetricMode = true;
        [Range(4, 24)]
        public int curveSubdivisions = 12;
        [Range(0.5f, 1.8f)]
        public float widthScale = 1.0f;
        [Range(0.6f, 1.6f)]
        public float heightScale = 1.0f;

        public Color glassBodyColor = new Color(0.35f, 0.70f, 0.98f, 0.76f);
        public Color glassCenterGlowColor = new Color(0.58f, 0.84f, 1.00f, 0.42f);
        public Color glassOuterRimColor = new Color(0.36f, 0.69f, 0.97f, 1.00f);
        public Color glassInnerRimColor = new Color(0.74f, 0.91f, 1.00f, 0.88f);
        public Color glassFrontLensColor = new Color(0.85f, 0.96f, 1.00f, 0.12f);
        public Color earHandleColor = new Color(0.92f, 0.96f, 1.00f, 1.00f);

        [Range(0.015f, 0.090f)]
        public float glassRimRadius = 0.042f;
        [Range(0.025f, 0.120f)]
        public float innerRimInset = 0.064f;
        [Range(0.02f, 0.25f)]
        public float glassShellDepth = 0.08f;
        public bool showShoulderHandles = false;
        public bool showSpecularReflections = true;

        [Header("Particle Settings")]
        [Range(0.08f, 0.30f)]
        public float particleSize = 0.24f;
        [Range(1, 8)]
        public int particlesPerMonsterCount = 1;

        [Header("Pixel Art Visual & Dynamic Patterns")]
        public ParticleArrangementMode particleMode = ParticleArrangementMode.PhysicsBalls;
        public ShapePatternType patternShape = ShapePatternType.GumballMix;
        public PixelArtPreset pixelArtPreset = PixelArtPreset.Frog;
        public PixelArtPattern customPattern;
        public SandSimulationMode initialSimulationMode = SandSimulationMode.StaticImage;
        public bool freezePatternInPlace = false;
        [Range(0.10f, 0.30f)]
        public float gridCellSpacing = 0.24f;
        public float patternVerticalOffset = 0.0f;
        public bool fillEmptyWithColor = false;
        public PuzzleColor emptyCellColor = PuzzleColor.Blue;
        [Tooltip("Görselin canavarlar alttaki kumu yiyene kadar kilitli kalan üst oranı (0 = kilit yok)")]
        [Range(0f, 0.6f)]
        public float lockedSectionRatio = 0.3f;

        [Header("Custom Glass Contour Points")]
        public List<GlassContourPoint> contourPoints = new List<GlassContourPoint>();

        [Header("Glass Compartments & Internal Dividers (Per-Level)")]
        public GlassCompartmentPreset compartmentPreset = GlassCompartmentPreset.None_OrderedPool;
        public List<GlassDividerData> dividers = new List<GlassDividerData>();
        public List<CompartmentColorZone> compartmentZones = new List<CompartmentColorZone>();

        [Header("Monsters & Bottle Color Quotas (Bottom Layer -> Top Layer)")]
        public List<LevelMonsterEntry> monsters = new List<LevelMonsterEntry>()
        {
            new LevelMonsterEntry(PuzzleColor.Blue, 10),
            new LevelMonsterEntry(PuzzleColor.Yellow, 11),
            new LevelMonsterEntry(PuzzleColor.Red, 10)
        };
    }
}
