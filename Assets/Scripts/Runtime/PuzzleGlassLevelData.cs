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
        public GlassShapePreset glassPreset = GlassShapePreset.BulbousFlask;
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
        public bool showShoulderHandles = true;
        public bool showSpecularReflections = true;

        [Header("Particle Settings")]
        [Range(0.08f, 0.22f)]
        public float particleSize = 0.155f;
        [Range(1, 8)]
        public int particlesPerMonsterCount = 1;

        [Header("Custom Glass Contour Points")]
        public List<GlassContourPoint> contourPoints = new List<GlassContourPoint>();

        [Header("Monsters & Bottle Color Quotas (Bottom Layer -> Top Layer)")]
        public List<LevelMonsterEntry> monsters = new List<LevelMonsterEntry>()
        {
            new LevelMonsterEntry(PuzzleColor.Blue, 10),
            new LevelMonsterEntry(PuzzleColor.Yellow, 20)
        };
    }
}

