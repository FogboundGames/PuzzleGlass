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

    public enum PuzzleLayerStyle
    {
        PureHorizontalRows = 0,     // Tam Yatay Katmanlar (Kolay - Her satır tek renk)
        SplitLeftRightBlocks = 1,   // Sol / Sağ Bölünmüş Bloklar (Orta - Satır içi 2 renk bloğu)
        InterleavedTriplets = 2,    // Parçalı 3'lü Gruplar (Zor - Satır içi 3 ayrı 3'lü grup)
        CheckerboardMix = 3         // Karışık Satranç Dizilimi (Boss - Çok katmanlı şaşırtmalı)
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

    public enum DividerMechanicType
    {
        StaticGlass = 0,        // Sabit Cam Bölme Duvarı
        PullPin = 1,            // Çekilebilir Cam Sürgü (Tıklanınca Çekilir)
        ColorLockedGate = 2     // Renk Kilitli Cam Kapak (Renk Kutusu Dolunca Otomatik Açılır)
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

        [Header("Interactive Mechanic (Pull-Pin / Color-Locked Gate)")]
        public DividerMechanicType mechanicType = DividerMechanicType.StaticGlass;
        [Tooltip("Renk Kilitli Kapak için: Hangi renkteki kutu tamamlanınca bu kapak otomatik açılsın?")]
        public PuzzleColor unlockColor = PuzzleColor.Blue;
        [Tooltip("Sürgü veya kapak açılırken sağa mı (true) yoksa sola mı (false) kayarak çıksın?")]
        public bool pullToRight = true;

        [NonSerialized] public bool runtimeIsOpened = false;

        public GlassDividerData() { }

        public GlassDividerData(
            string name,
            Vector2 center,
            float length,
            float thickness = 0.085f,
            float angleDegrees = 90f,
            bool enabled = true,
            DividerMechanicType mechanicType = DividerMechanicType.StaticGlass,
            PuzzleColor unlockColor = PuzzleColor.Blue,
            bool pullToRight = true)
        {
            this.name = name;
            this.center = center;
            this.length = Mathf.Clamp(length, 0.10f, 2.60f);
            this.thickness = Mathf.Clamp(thickness, 0.03f, 0.22f);
            this.angleDegrees = Mathf.Clamp(angleDegrees, -90f, 90f);
            this.enabled = enabled;
            this.mechanicType = mechanicType;
            this.unlockColor = unlockColor;
            this.pullToRight = pullToRight;
            this.runtimeIsOpened = false;
        }

        public GlassDividerData Clone()
        {
            return new GlassDividerData(name, center, length, thickness, angleDegrees, enabled, mechanicType, unlockColor, pullToRight);
        }

        /// <summary>
        /// Checks whether a circle (ball) at container-local position pt with given radius overlaps this oriented divider bar.
        /// </summary>
        public bool OverlapsCircle(Vector2 pt, float radius, float extraMargin = 0.012f)
        {
            if (!enabled || runtimeIsOpened) return false;

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
            if (!enabled || runtimeIsOpened) return false;

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
        [Range(1, 16)]
        public int count = 3;
        public CarrierBlockShape blockShape = CarrierBlockShape.Rect1x1;
        public Vector2Int gridAnchor = Vector2Int.zero;
        public bool hasGridAnchor = false;

        public LevelMonsterEntry() { }

        public LevelMonsterEntry(PuzzleColor color, int count)
        {
            this.color = color;
            this.count = Mathf.Clamp(count, 1, 16);
        }

        public LevelMonsterEntry(PuzzleColor color, int count, CarrierBlockShape shape, Vector2Int gridAnchor)
        {
            this.color = color;
            this.count = Mathf.Clamp(count, 1, 16);
            this.blockShape = shape;
            this.gridAnchor = gridAnchor;
            this.hasGridAnchor = true;
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

        [Header("Bottom Grid Tray (Sliding Block Puzzle Area)")]
        [Range(3, 6)]
        public int stagingGridCols = 5;
        [Range(3, 5)]
        public int stagingGridRows = 4;

        [Header("Carrier Box Puzzle Settings (Variable-Socket Boxes & Bottom-to-Top Layers)")]
        [Tooltip("Her kutunun içindeki varsayılan gri yuva/boşluk sayısı (1 - 16 arası)")]
        [Range(1, 16)]
        public int ballsPerBox = 3;

        [HideInInspector]
        public int carrierBoxFormatVersion = 3;

        [Tooltip("Cam haznedeki topların alttan üste katman dizilim zorluk stili")]
        public PuzzleLayerStyle puzzleLayerStyle = PuzzleLayerStyle.PureHorizontalRows;

        [Tooltip("Alt bekleme alanındaki kutuların kaç sütun halinde dizileceği (2 - 4 sütun)")]
        [Range(2, 4)]
        public int stagingColumns = 3;

        [Tooltip("Aktif olduğunda her sütunun sadece en üstteki (öndeki) kutusu seçilebilir; o slota gidince arkadaki kutu öne kayar")]
        public bool useStagingColumnQueue = true;

        [Tooltip("Cam haznedeki top gruplarının En Alttan (0) En Üste doğru renk sırası (Eski/uyumluluk listesi)")]
        public List<PuzzleColor> bottomToTopTriplets = new List<PuzzleColor>()
        {
            PuzzleColor.Blue, PuzzleColor.Blue, PuzzleColor.Blue,
            PuzzleColor.Yellow, PuzzleColor.Yellow, PuzzleColor.Yellow,
            PuzzleColor.Red, PuzzleColor.Red, PuzzleColor.Red
        };

        [Tooltip("Cam haznedeki top gruplarının En Alttan (0) En Üste doğru tam (Renk + Adet) listesi")]
        public List<LevelMonsterEntry> bottomToTopGroups = new List<LevelMonsterEntry>()
        {
            new LevelMonsterEntry(PuzzleColor.Blue, 3),
            new LevelMonsterEntry(PuzzleColor.Blue, 3),
            new LevelMonsterEntry(PuzzleColor.Blue, 3),
            new LevelMonsterEntry(PuzzleColor.Yellow, 3),
            new LevelMonsterEntry(PuzzleColor.Yellow, 3),
            new LevelMonsterEntry(PuzzleColor.Yellow, 3),
            new LevelMonsterEntry(PuzzleColor.Red, 3),
            new LevelMonsterEntry(PuzzleColor.Red, 3),
            new LevelMonsterEntry(PuzzleColor.Red, 3)
        };

        [Header("Staging Carrier Boxes (Top-to-Bottom, Left-to-Right Queue)")]
        public List<LevelMonsterEntry> monsters = new List<LevelMonsterEntry>()
        {
            new LevelMonsterEntry(PuzzleColor.Blue, 3),
            new LevelMonsterEntry(PuzzleColor.Yellow, 3),
            new LevelMonsterEntry(PuzzleColor.Red, 3),
            new LevelMonsterEntry(PuzzleColor.Blue, 3),
            new LevelMonsterEntry(PuzzleColor.Red, 3),
            new LevelMonsterEntry(PuzzleColor.Yellow, 3),
            new LevelMonsterEntry(PuzzleColor.Yellow, 3),
            new LevelMonsterEntry(PuzzleColor.Blue, 3),
            new LevelMonsterEntry(PuzzleColor.Red, 3)
        };

        /// <summary>
        /// Sets the socket capacity (1..16) across ballsPerBox and every staging box, then rebuilds container groups.
        /// </summary>
        public void SetAllBoxesSocketCount(int newCount)
        {
            ballsPerBox = Mathf.Clamp(newCount, 1, 16);
            carrierBoxFormatVersion = 2;
            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                {
                    if (monsters[i] != null) monsters[i].count = ballsPerBox;
                }
            }
            RebuildTripletsFromBoxes();
        }

        public int GetTotalBallCount()
        {
            if (monsters == null) return 0;
            int sum = 0;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null) sum += Mathf.Max(1, monsters[i].count);
            }
            return sum;
        }

        public bool HasValidGridLayout()
        {
            if (monsters == null || monsters.Count == 0) return false;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] == null || !monsters[i].hasGridAnchor) return false;
            }
            return true;
        }

        /// <summary>
        /// Ensures Carrier Box entries, grid placements, and bottom-to-top glass groups remain valid and consistent.
        /// Migrates legacy assets to the shaped grid-puzzle format automatically.
        /// </summary>
        public void EnsureCarrierBoxPuzzleFormat()
        {
            ballsPerBox = Mathf.Clamp(ballsPerBox, 1, 16);
            if (stagingColumns < 2 || stagingColumns > 4) stagingColumns = 3;
            stagingGridCols = Mathf.Clamp(stagingGridCols <= 0 ? 5 : stagingGridCols, 3, 6);
            stagingGridRows = Mathf.Clamp(stagingGridRows <= 0 ? 4 : stagingGridRows, 3, 5);

            if (monsters == null || monsters.Count == 0)
            {
                PuzzleLevelGenerator.GenerateForLevelData(this, customBallsPerBox: ballsPerBox, randomSeed: Mathf.Max(1, levelNumber) * 7919);
                return;
            }

            if (bottomToTopTriplets == null) bottomToTopTriplets = new List<PuzzleColor>();
            if (bottomToTopGroups == null) bottomToTopGroups = new List<LevelMonsterEntry>();

            // Legacy migration for old assets saved before shaped grid format (v3)
            if (carrierBoxFormatVersion < 3 || !HasValidGridLayout())
            {
                bool hasLegacyMegaCounts = false;
                for (int i = 0; i < monsters.Count; i++)
                {
                    if (monsters[i] != null && monsters[i].count > 16)
                    {
                        hasLegacyMegaCounts = true;
                        break;
                    }
                }

                if (hasLegacyMegaCounts || !HasValidGridLayout())
                {
                    PuzzleLevelGenerator.GenerateForLevelData(this, customBallsPerBox: ballsPerBox, randomSeed: Mathf.Max(1, levelNumber) * 7919);
                    return;
                }

                carrierBoxFormatVersion = 3;
            }

            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                {
                    monsters[i].count = Mathf.Clamp(monsters[i].count, 1, 16);
                }
            }

            if (!DoGroupsMatchBoxes(bottomToTopGroups, monsters))
            {
                RebuildTripletsFromBoxes();
            }
            else if (bottomToTopTriplets.Count != bottomToTopGroups.Count)
            {
                SyncTripletsFromGroups();
            }
        }

        private bool DoTripletsMatchBoxes(List<PuzzleColor> triplets, List<LevelMonsterEntry> boxes)
        {
            if (triplets == null || boxes == null || triplets.Count != boxes.Count) return false;

            int[] diff = new int[16];
            for (int i = 0; i < triplets.Count; i++)
            {
                diff[(int)triplets[i]]++;
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i] == null || boxes[i].count != 3) return false;
                diff[(int)boxes[i].color]--;
            }
            for (int i = 0; i < diff.Length; i++)
            {
                if (diff[i] != 0) return false;
            }
            return true;
        }

        private bool DoGroupsMatchBoxes(List<LevelMonsterEntry> groups, List<LevelMonsterEntry> boxes)
        {
            if (groups == null || boxes == null || groups.Count != boxes.Count) return false;

            Dictionary<(PuzzleColor, int), int> diff = new Dictionary<(PuzzleColor, int), int>();
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == null) return false;
                var key = (groups[i].color, Mathf.Max(1, groups[i].count));
                diff[key] = diff.TryGetValue(key, out int cur) ? cur + 1 : 1;
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i] == null) return false;
                var key = (boxes[i].color, Mathf.Max(1, boxes[i].count));
                diff[key] = diff.TryGetValue(key, out int cur) ? cur - 1 : -1;
            }
            foreach (var kv in diff)
            {
                if (kv.Value != 0) return false;
            }
            return true;
        }

        public void SyncTripletsFromGroups()
        {
            if (bottomToTopTriplets == null) bottomToTopTriplets = new List<PuzzleColor>();
            bottomToTopTriplets.Clear();
            if (bottomToTopGroups == null) return;
            for (int i = 0; i < bottomToTopGroups.Count; i++)
            {
                if (bottomToTopGroups[i] != null) bottomToTopTriplets.Add(bottomToTopGroups[i].color);
            }
        }

        public void RebuildTripletsFromBoxes()
        {
            if (bottomToTopGroups == null) bottomToTopGroups = new List<LevelMonsterEntry>();
            bottomToTopGroups.Clear();
            if (bottomToTopTriplets == null) bottomToTopTriplets = new List<PuzzleColor>();
            bottomToTopTriplets.Clear();

            if (monsters == null) return;

            // Group boxes by color in order of first appearance while preserving each box's socket count
            Dictionary<PuzzleColor, Queue<LevelMonsterEntry>> buckets = new Dictionary<PuzzleColor, Queue<LevelMonsterEntry>>();
            List<PuzzleColor> order = new List<PuzzleColor>();
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] == null) continue;
                int cnt = Mathf.Clamp(monsters[i].count, 1, 50);
                monsters[i].count = cnt;
                PuzzleColor c = monsters[i].color;
                if (!buckets.ContainsKey(c))
                {
                    buckets[c] = new Queue<LevelMonsterEntry>();
                    order.Add(c);
                }
                buckets[c].Enqueue(new LevelMonsterEntry(c, cnt));
            }

            if (puzzleLayerStyle == PuzzleLayerStyle.PureHorizontalRows)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    var q = buckets[order[i]];
                    while (q.Count > 0) bottomToTopGroups.Add(q.Dequeue());
                }
            }
            else
            {
                // Interleave across layers according to puzzleLayerStyle
                int step = (puzzleLayerStyle == PuzzleLayerStyle.SplitLeftRightBlocks) ? 2 : 1;
                bool added = true;
                while (added)
                {
                    added = false;
                    for (int i = 0; i < order.Count; i++)
                    {
                        var q = buckets[order[i]];
                        for (int s = 0; s < step && q.Count > 0; s++)
                        {
                            bottomToTopGroups.Add(q.Dequeue());
                            added = true;
                        }
                    }
                }
            }

            SyncTripletsFromGroups();
        }
    }
}
