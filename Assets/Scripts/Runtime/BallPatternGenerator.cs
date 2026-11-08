using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum PatternCategory
    {
        AnimalSilhouette,
        HeartsAndStars,
        Fruits,
        GeometricShapes,
        MultiLayerBands,
        RandomMosaic
    }

    public enum ShapePatternType
    {
        // Animals (from notebook Image 2)
        Frog,
        Heart,
        Mushroom,
        Bee,
        Octopus,
        Penguin,
        Butterfly,
        Tulip,
        Saturn,
        Rainbow,
        Cat,
        Star,

        // Fruits
        Strawberry,
        Cherry,
        OrangeFruit,

        // Geometric
        Diamond,
        Pyramid,
        SquareGrid,
        HexagonRing,

        // Layers
        HorizontalBands,
        VFunnelBands,
        DiagonalBands,

        // Random
        RandomMosaic
    }

    public class BallPatternGenerator : MonoBehaviour
    {
        public static BallPatternGenerator Instance { get; private set; }

        public ShapedGlassContainer glassContainer;

        [Header("Spawn Settings")]
        [Range(0.08f, 0.22f)]
        public float ballDiameter = 0.165f;
        public float patternVerticalOffset = 0.0f;
        public float initialShapeHoldTime = 0.45f; // Brief moment to admire shape before fully releasing

        private void Awake()
        {
            Instance = this;
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void OnEnable()
        {
            Instance = this;
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        /// <summary>
        /// Builds any shape pattern as a pixel grid for the sand visual.
        /// </summary>
        public PixelArtPattern BuildSandPattern(ShapePatternType patternType, List<LevelMonsterEntry> monsterGoals = null)
        {
            float spacing = ballDiameter * 1.05f;
            List<(Vector2 pos, PuzzleColor col)> ballSpawns = new List<(Vector2, PuzzleColor)>();

            switch (patternType)
            {
                case ShapePatternType.Frog:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Frog), spacing, ballSpawns);
                    break;
                case ShapePatternType.Heart:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Heart), spacing, ballSpawns);
                    break;
                case ShapePatternType.Mushroom:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Mushroom), spacing, ballSpawns);
                    break;
                case ShapePatternType.Bee:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Bee), spacing, ballSpawns);
                    break;
                case ShapePatternType.Octopus:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.OctopusSkull), spacing, ballSpawns);
                    break;
                case ShapePatternType.Penguin:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Penguin), spacing, ballSpawns);
                    break;
                case ShapePatternType.Butterfly:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Butterfly), spacing, ballSpawns);
                    break;
                case ShapePatternType.Tulip:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Tulip), spacing, ballSpawns);
                    break;
                case ShapePatternType.Saturn:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Saturn), spacing, ballSpawns);
                    break;
                case ShapePatternType.Rainbow:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Rainbow), spacing, ballSpawns);
                    break;
                case ShapePatternType.Cat:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Cat), spacing, ballSpawns);
                    break;
                case ShapePatternType.Star:
                    GenerateFromAscii(PixelArtLibrary.GetPreset(PixelArtPreset.Star), spacing, ballSpawns);
                    break;

                case ShapePatternType.Strawberry:
                    GenerateStrawberry(spacing, ballSpawns);
                    break;
                case ShapePatternType.Cherry:
                    GenerateCherry(spacing, ballSpawns);
                    break;
                case ShapePatternType.OrangeFruit:
                    GenerateOrange(spacing, ballSpawns);
                    break;

                case ShapePatternType.Diamond:
                    GenerateDiamond(spacing, monsterGoals, ballSpawns);
                    break;
                case ShapePatternType.Pyramid:
                    GeneratePyramid(spacing, monsterGoals, ballSpawns);
                    break;
                case ShapePatternType.SquareGrid:
                    GenerateSquare(spacing, monsterGoals, ballSpawns);
                    break;
                case ShapePatternType.HexagonRing:
                    GenerateHexagon(spacing, monsterGoals, ballSpawns);
                    break;

                case ShapePatternType.HorizontalBands:
                case ShapePatternType.VFunnelBands:
                case ShapePatternType.DiagonalBands:
                case ShapePatternType.RandomMosaic:
                default:
                    GenerateLayeredOrRandomBands(patternType, spacing, monsterGoals, ballSpawns);
                    break;
            }

            if (ballSpawns.Count == 0)
            {
                return PixelArtLibrary.GetPreset(PixelArtPreset.Frog);
            }

            // Snap the generated layout onto a pixel grid
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < ballSpawns.Count; i++)
            {
                minX = Mathf.Min(minX, ballSpawns[i].pos.x);
                minY = Mathf.Min(minY, ballSpawns[i].pos.y);
                maxX = Mathf.Max(maxX, ballSpawns[i].pos.x);
                maxY = Mathf.Max(maxY, ballSpawns[i].pos.y);
            }

            int cols = Mathf.RoundToInt((maxX - minX) / spacing) + 1;
            int rows = Mathf.RoundToInt((maxY - minY) / spacing) + 1;
            PuzzleColor?[,] grid = new PuzzleColor?[cols, rows];

            for (int i = 0; i < ballSpawns.Count; i++)
            {
                int x = Mathf.RoundToInt((ballSpawns[i].pos.x - minX) / spacing);
                int y = Mathf.RoundToInt((ballSpawns[i].pos.y - minY) / spacing);
                grid[x, y] = ballSpawns[i].col;
            }

            return PixelArtPattern.FromColorGrid(patternType.ToString(), grid);
        }

        private void GenerateFromAscii(PixelArtPattern pattern, float spacing, List<(Vector2, PuzzleColor)> list)
        {
            var grid = pattern.GetColorGrid();
            int cols = pattern.width;
            int rows = pattern.height;

            float w = (cols - 1) * spacing;
            float h = (rows - 1) * spacing;

            float centerOffset = (glassContainer != null && glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                ? glassContainer.contourPoints[0].centerX * glassContainer.widthScale : 0f;
            float startX = centerOffset - w * 0.5f;

            float floorY = glassContainer != null ? glassContainer.GetChamberFloorY() : -0.46f;
            float startY = floorY + spacing * 0.5f + patternVerticalOffset;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    var col = grid[x, y];
                    if (col.HasValue)
                    {
                        list.Add((new Vector2(startX + x * spacing, startY + y * spacing), col.Value));
                    }
                }
            }
        }

        private void GenerateStrawberry(float spacing, List<(Vector2, PuzzleColor)> list)
        {
            // Cute strawberry with green leafy top and red seeded body
            string[] ascii = new string[]
            {
                "...GG.GG...",
                "..GGGGGGG..",
                ".RRRRWRRRR.",
                ".RWRRRRRWR.",
                "..RRWRRRR..",
                "..RRRRWRR..",
                "...RRRRR...",
                "....RRR...."
            };
            var pat = new PixelArtPattern("Strawberry", 11, 8, ascii);
            GenerateFromAscii(pat, spacing, list);
        }

        private void GenerateCherry(float spacing, List<(Vector2, PuzzleColor)> list)
        {
            // Two ripe cherries connected by green stem
            string[] ascii = new string[]
            {
                "....GG....",
                "...G..G...",
                "..G....G..",
                ".RRW..RRW.",
                "RRRR..RRRR",
                ".RR....RR."
            };
            var pat = new PixelArtPattern("Cherry", 10, 6, ascii);
            GenerateFromAscii(pat, spacing, list);
        }

        private void GenerateOrange(float spacing, List<(Vector2, PuzzleColor)> list)
        {
            // Juicy orange fruit with green leaf
            string[] ascii = new string[]
            {
                "....G.....",
                "...GG.....",
                "..OOOOO...",
                ".OOOOOOO..",
                ".OOWOOOO..",
                "..OOOOO..."
            };
            var pat = new PixelArtPattern("Orange", 10, 6, ascii);
            GenerateFromAscii(pat, spacing, list);
        }

        private void GenerateDiamond(float spacing, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            int size = 9;
            float chamberCenterY = GetChamberCenterY();
            PuzzleColor[] colors = GetGoalColors(goals);

            for (int r = 0; r < size; r++)
            {
                int countInRow = (r <= size / 2) ? (r * 2 + 1) : ((size - 1 - r) * 2 + 1);
                float startX = -(countInRow - 1) * spacing * 0.5f;
                float y = chamberCenterY - (size * 0.5f - r) * spacing;
                PuzzleColor c = colors[r % colors.Length];

                for (int cIdx = 0; cIdx < countInRow; cIdx++)
                {
                    list.Add((new Vector2(startX + cIdx * spacing, y), c));
                }
            }
        }

        private void GeneratePyramid(float spacing, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            int rows = 8;
            float chamberCenterY = GetChamberCenterY();
            PuzzleColor[] colors = GetGoalColors(goals);

            for (int r = 0; r < rows; r++)
            {
                int countInRow = r + 1;
                float startX = -(countInRow - 1) * spacing * 0.5f;
                float y = chamberCenterY + (rows * 0.5f - r) * spacing;
                PuzzleColor c = colors[r % colors.Length];

                for (int cIdx = 0; cIdx < countInRow; cIdx++)
                {
                    list.Add((new Vector2(startX + cIdx * spacing, y), c));
                }
            }
        }

        private void GenerateSquare(float spacing, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            int side = 8;
            float chamberCenterY = GetChamberCenterY();
            float startX = -(side - 1) * spacing * 0.5f;
            float startY = chamberCenterY - (side - 1) * spacing * 0.5f;
            PuzzleColor[] colors = GetGoalColors(goals);

            for (int r = 0; r < side; r++)
            {
                PuzzleColor c = colors[r % colors.Length];
                for (int col = 0; col < side; col++)
                {
                    list.Add((new Vector2(startX + col * spacing, startY + r * spacing), c));
                }
            }
        }

        private void GenerateHexagon(float spacing, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            int rings = 4;
            float chamberCenterY = GetChamberCenterY();
            PuzzleColor[] colors = GetGoalColors(goals);

            list.Add((new Vector2(0f, chamberCenterY), colors[0]));
            for (int r = 1; r <= rings; r++)
            {
                int ballsInRing = r * 6;
                PuzzleColor c = colors[r % colors.Length];
                float radius = r * spacing;

                for (int i = 0; i < ballsInRing; i++)
                {
                    float angle = (i / (float)ballsInRing) * Mathf.PI * 2f;
                    list.Add((new Vector2(Mathf.Cos(angle) * radius, chamberCenterY + Mathf.Sin(angle) * radius), c));
                }
            }
        }

        private void GenerateLayeredOrRandomBands(
            ShapePatternType type,
            float spacing,
            List<LevelMonsterEntry> goals,
            List<(Vector2, PuzzleColor)> list)
        {
            int totalBalls = 0;
            if (goals != null && goals.Count > 0)
            {
                for (int i = 0; i < goals.Count; i++) totalBalls += goals[i].count;
            }
            if (totalBalls <= 0) totalBalls = 35;

            PuzzleColor[] colors = GetGoalColors(goals);

            int cols = 8;
            int rows = Mathf.CeilToInt((float)totalBalls / cols);
            float chamberCenterY = GetChamberCenterY();
            float startX = -(cols - 1) * spacing * 0.5f;
            float startY = chamberCenterY - (rows - 1) * spacing * 0.5f;

            int spawned = 0;
            for (int r = 0; r < rows && spawned < totalBalls; r++)
            {
                for (int c = 0; c < cols && spawned < totalBalls; c++)
                {
                    PuzzleColor col;
                    if (type == ShapePatternType.RandomMosaic)
                    {
                        col = colors[UnityEngine.Random.Range(0, colors.Length)];
                    }
                    else
                    {
                        int colorBandIdx = Mathf.Clamp(r * colors.Length / rows, 0, colors.Length - 1);
                        col = colors[colorBandIdx];
                    }

                    float stagger = (r % 2 == 1) ? spacing * 0.5f : 0f;
                    list.Add((new Vector2(startX + c * spacing + stagger, startY + r * spacing), col));
                    spawned++;
                }
            }
        }

        private float GetChamberCenterY()
        {
            if (glassContainer != null)
            {
                return glassContainer.CalculateChamberCenterY() + patternVerticalOffset;
            }
            return 0.30f + patternVerticalOffset;
        }

        private PuzzleColor[] GetGoalColors(List<LevelMonsterEntry> goals)
        {
            if (goals != null && goals.Count > 0)
            {
                List<PuzzleColor> cols = new List<PuzzleColor>();
                for (int i = 0; i < goals.Count; i++)
                {
                    if (goals[i] != null && !cols.Contains(goals[i].color))
                        cols.Add(goals[i].color);
                }
                if (cols.Count > 0) return cols.ToArray();
            }
            return new PuzzleColor[] { PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red, PuzzleColor.Green };
        }
    }
}

