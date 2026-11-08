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
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        private void OnEnable()
        {
            Instance = this;
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        /// <summary>
        /// Generates and spawns a dynamic level pattern using real, individual 3D physics balls!
        /// </summary>
        public List<BallPhysicsController> GeneratePattern(
            ShapePatternType patternType,
            List<LevelMonsterEntry> monsterGoals = null,
            float customSpacing = -1f)
        {
            if (BallObjectPool.Instance == null)
            {
                var poolObj = new GameObject("BallObjectPool");
                poolObj.AddComponent<BallObjectPool>();
            }

            BallObjectPool.Instance.ClearAllActiveBalls();

            float spacing = (customSpacing > 0.06f) ? customSpacing : ballDiameter * 1.05f;
            float ballRadius = ballDiameter * 0.5f;

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

            // Spawn real 3D physical balls from the object pool
            List<BallPhysicsController> spawnedBalls = new List<BallPhysicsController>(ballSpawns.Count);
            for (int i = 0; i < ballSpawns.Count; i++)
            {
                var entry = ballSpawns[i];
                Vector3 worldPos = new Vector3(entry.pos.x, entry.pos.y, -0.10f);
                var ball = BallObjectPool.Instance.SpawnBall(entry.col, worldPos, ballRadius);
                if (ball != null && ball.rb != null)
                {
                    ball.rb.isKinematic = !Application.isPlaying;
                    ball.rb.useGravity = Application.isPlaying;
                }
                spawnedBalls.Add(ball);
            }

            // In Play Mode, release to PhysX physics simulation!
            if (Application.isPlaying)
            {
                StartCoroutine(ReleaseBallsToPhysicsRoutine(spawnedBalls));
            }

            return spawnedBalls;
        }

        public List<BallPhysicsController> GenerateFromCustomPattern(PixelArtPattern pattern, float customSpacing = -1f)
        {
            if (pattern == null) return new List<BallPhysicsController>();

            if (BallObjectPool.Instance == null)
            {
                var poolObj = new GameObject("BallObjectPool");
                poolObj.AddComponent<BallObjectPool>();
            }

            BallObjectPool.Instance.ClearAllActiveBalls();

            float spacing = (customSpacing > 0.06f) ? customSpacing : ballDiameter * 1.05f;
            float ballRadius = ballDiameter * 0.5f;

            List<(Vector2 pos, PuzzleColor col)> ballSpawns = new List<(Vector2, PuzzleColor)>();
            GenerateFromAscii(pattern, spacing, ballSpawns);

            List<BallPhysicsController> spawnedBalls = new List<BallPhysicsController>(ballSpawns.Count);
            for (int i = 0; i < ballSpawns.Count; i++)
            {
                var entry = ballSpawns[i];
                Vector3 worldPos = new Vector3(entry.pos.x, entry.pos.y, -0.10f);
                var ball = BallObjectPool.Instance.SpawnBall(entry.col, worldPos, ballRadius);
                if (ball != null && ball.rb != null)
                {
                    ball.rb.isKinematic = !Application.isPlaying;
                    ball.rb.useGravity = Application.isPlaying;
                }
                spawnedBalls.Add(ball);
            }

            if (Application.isPlaying)
            {
                StartCoroutine(ReleaseBallsToPhysicsRoutine(spawnedBalls));
            }

            return spawnedBalls;
        }

        private IEnumerator ReleaseBallsToPhysicsRoutine(List<BallPhysicsController> balls)
        {
            // Initial high drag holds the clean formation in place
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i]?.rb != null)
                {
                    balls[i].rb.linearDamping = 3.5f;
                    balls[i].rb.angularDamping = 2.0f;
                }
            }

            yield return new WaitForSeconds(initialShapeHoldTime);

            // Natural gravity release: balls tumble, roll, and settle into a realistic organic pile!
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i]?.rb != null && balls[i].state == BallState.InContainer)
                {
                    balls[i].rb.isKinematic = false;
                    balls[i].rb.useGravity = true;
                    balls[i].rb.linearDamping = balls[i].defaultDrag;
                    balls[i].rb.angularDamping = balls[i].defaultAngularDrag;
                    balls[i].rb.WakeUp();
                }
            }
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

