using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum PatternCategory
    {
        ReferenceLayouts,
        AnimalSilhouette,
        HeartsAndStars,
        Fruits,
        GeometricShapes,
        MultiLayerBands,
        RandomMosaic
    }

    public enum ShapePatternType
    {
        // Reference 1: 4 Quadrants (Image 1 Ref: Blue top-left, Red top-right, Yellow bottom-left, Green bottom-right)
        FourQuadrants = 0,

        // Reference 2: Gumball Physics Mix (Image 2 Ref: Colorful candy balls settling under gravity)
        GumballMix = 1,

        // Animals & Silhouettes
        Frog = 2,
        Heart = 3,
        Mushroom = 4,
        Bee = 5,
        Octopus = 6,
        Penguin = 7,
        Butterfly = 8,
        Tulip = 9,
        Saturn = 10,
        Rainbow = 11,
        Cat = 12,
        Star = 13,

        // Fruits
        Strawberry = 14,
        Cherry = 15,
        OrangeFruit = 16,

        // Geometric
        Diamond = 17,
        Pyramid = 18,
        SquareGrid = 19,
        HexagonRing = 20,

        // Layers
        HorizontalBands = 21,
        VFunnelBands = 22,
        DiagonalBands = 23,

        // Random
        RandomMosaic = 24
    }

    [ExecuteAlways]
    public class BallPatternGenerator : MonoBehaviour
    {
        public static BallPatternGenerator Instance { get; private set; }

        public ShapedGlassContainer glassContainer;

        [Header("Spawn Settings")]
        [Range(0.08f, 0.35f)]
        public float ballDiameter = 0.24f; // Glossy 3D pool spheres matching Reference Image 2 (10 / 11 / 10 honeycomb rows)
        public float patternVerticalOffset = 0.0f;
        [Tooltip("İlk desen şeklinin serbest düşmeden önce kısaca hayranlıkla izlenmesi için kinematic bekleme süresi (0 = hemen düşer)")]
        public float initialShapeHoldTime = 0.0f; // 0 = Immediately dynamic, obeying physics from frame 0!

        private Coroutine _releaseHoldCoroutine;

        private void Awake()
        {
            Instance = this;
            EnsureReferences();
        }

        private void OnEnable()
        {
            Instance = this;
            EnsureReferences();
        }

        private void EnsureReferences()
        {
            if (glassContainer == null)
            {
                glassContainer = GetComponent<ShapedGlassContainer>().OrNull() ?? FindFirstObjectByType<ShapedGlassContainer>();
            }
        }

        /// <summary>
        /// Generates and spawns 3D physical balls in the container matching the requested pattern!
        /// Balls obey real 3D Newtonian physics with Rigidbody, SphereCollider, gravity, and collisions!
        /// </summary>
        public List<BallPhysicsController> GeneratePattern(
            ShapePatternType patternType,
            List<LevelMonsterEntry> monsterGoals = null,
            float customSpacing = -1f)
        {
            EnsureReferences();

            if (BallObjectPool.Instance == null)
            {
                var poolObj = GameObject.Find("BallObjectPool");
                if (poolObj == null) poolObj = new GameObject("BallObjectPool");
                poolObj.AddComponent<BallObjectPool>();
            }

            BallObjectPool.Instance.ClearAllActiveBalls();

            float spacing = (customSpacing > 0.06f) ? customSpacing : ballDiameter;
            float ballRadius = ballDiameter * 0.5f;

            List<(Vector2 pos, PuzzleColor col)> ballSpawns = new List<(Vector2, PuzzleColor)>();

            switch (patternType)
            {
                case ShapePatternType.FourQuadrants:
                    GenerateFourQuadrants(spacing, monsterGoals, ballSpawns);
                    break;
                case ShapePatternType.GumballMix:
                case ShapePatternType.HorizontalBands:
                case ShapePatternType.VFunnelBands:
                    GenerateOrderedBallPool(ballDiameter, monsterGoals, ballSpawns);
                    break;

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

                case ShapePatternType.DiagonalBands:
                case ShapePatternType.RandomMosaic:
                    GenerateLayeredOrRandomBands(patternType, spacing, monsterGoals, ballSpawns);
                    break;
                default:
                    GenerateOrderedBallPool(ballDiameter, monsterGoals, ballSpawns);
                    break;
            }

            if (ballSpawns.Count == 0)
            {
                GenerateOrderedBallPool(ballDiameter, monsterGoals, ballSpawns);
            }

            // Ensure no spawned ball overlaps any active internal glass divider
            bool hasSlopedDivider = false;
            if (glassContainer != null && glassContainer.dividers != null && glassContainer.dividers.Count > 0)
            {
                for (int d = 0; d < glassContainer.dividers.Count; d++)
                {
                    var div = glassContainer.dividers[d];
                    if (div != null && div.enabled && Mathf.Abs(div.angleDegrees) < 78f)
                    {
                        hasSlopedDivider = true;
                    }
                }

                for (int i = ballSpawns.Count - 1; i >= 0; i--)
                {
                    Vector2 pt = ballSpawns[i].pos;
                    bool overlapsDivider = false;
                    for (int d = 0; d < glassContainer.dividers.Count; d++)
                    {
                        var div = glassContainer.dividers[d];
                        if (div != null && div.enabled && div.OverlapsCircle(pt, ballRadius, 0.006f))
                        {
                            overlapsDivider = true;
                            break;
                        }
                    }
                    if (overlapsDivider)
                    {
                        ballSpawns.RemoveAt(i);
                    }
                }
            }

            // Compute ordered layer indices (0 = bottom-most row, 1 = second row from bottom, etc.)
            List<float> rowYCenters = new List<float>();
            float rowClusterThreshold = ballDiameter * 0.42f;
            for (int i = 0; i < ballSpawns.Count; i++)
            {
                float y = ballSpawns[i].pos.y;
                bool foundCluster = false;
                for (int r = 0; r < rowYCenters.Count; r++)
                {
                    if (Mathf.Abs(y - rowYCenters[r]) <= rowClusterThreshold)
                    {
                        foundCluster = true;
                        break;
                    }
                }
                if (!foundCluster)
                {
                    rowYCenters.Add(y);
                }
            }
            rowYCenters.Sort();

            float localFloorY = (glassContainer != null) ? glassContainer.GetChamberFloorY() : -0.56f;
            float worldFloorY = (glassContainer != null)
                ? glassContainer.transform.TransformPoint(new Vector3(0f, localFloorY, 0f)).y
                : localFloorY;
            float worldRowStepY = ballDiameter * 0.855f * ((glassContainer != null) ? glassContainer.transform.lossyScale.y : 1f);

            List<BallPhysicsController> spawnedBalls = new List<BallPhysicsController>(ballSpawns.Count);

            for (int i = 0; i < ballSpawns.Count; i++)
            {
                var entry = ballSpawns[i];
                Vector3 localPos = new Vector3(entry.pos.x, entry.pos.y, -0.10f);
                Vector3 worldPos = (glassContainer != null)
                    ? glassContainer.transform.TransformPoint(localPos)
                    : localPos;

                int layerIdx = 0;
                float bestDiff = float.MaxValue;
                for (int r = 0; r < rowYCenters.Count; r++)
                {
                    float diff = Mathf.Abs(entry.pos.y - rowYCenters[r]);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        layerIdx = r;
                    }
                }

                var ball = BallObjectPool.Instance.SpawnBall(entry.col, worldPos, ballRadius);
                if (ball != null)
                {
                    ball.ConfigurePoolLayer(layerIdx, worldPos.x, worldFloorY, worldRowStepY);
                    if (hasSlopedDivider)
                    {
                        // Allow balls to roll sideways along sloped shelves/ramps
                        ball.hasHomeX = false;
                    }

                    if (initialShapeHoldTime > 0.05f || !Application.isPlaying)
                    {
                        ball.MakeKinematic();
                    }
                    else
                    {
                        ball.ReleaseToPhysics();
                    }
                    spawnedBalls.Add(ball);
                }
            }

            if (initialShapeHoldTime > 0.05f && Application.isPlaying)
            {
                if (_releaseHoldCoroutine != null) StopCoroutine(_releaseHoldCoroutine);
                _releaseHoldCoroutine = StartCoroutine(ReleaseBallsHoldRoutine(initialShapeHoldTime, spawnedBalls));
            }

            return spawnedBalls;
        }

        private IEnumerator ReleaseBallsHoldRoutine(float delay, List<BallPhysicsController> balls)
        {
            yield return new WaitForSeconds(delay);

            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i] != null)
                {
                    balls[i].ReleaseToPhysics();
                }
            }
            _releaseHoldCoroutine = null;
        }

        /// <summary>
        /// Generates the 4-Quadrant layout:
        /// Top-Left: Blue, Top-Right: Red, Bottom-Left: Yellow, Bottom-Right: Green.
        /// Packed in a dense, continuous, bottom-resting honeycomb pile with zero center gap and zero floating!
        /// </summary>
        private void GenerateFourQuadrants(float spacing, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            PuzzleColor cTopLeft = PuzzleColor.Blue;
            PuzzleColor cTopRight = PuzzleColor.Red;
            PuzzleColor cBottomLeft = PuzzleColor.Yellow;
            PuzzleColor cBottomRight = PuzzleColor.Green;

            if (goals != null && goals.Count >= 4)
            {
                cBottomLeft = goals[0].color;
                cBottomRight = goals[1].color;
                cTopLeft = goals[2].color;
                cTopRight = goals[3].color;
            }

            float colSpacing = spacing;
            float rowSpacing = colSpacing * 0.866025f;
            float ballR = colSpacing * 0.5f;

            float throatY = (glassContainer != null) ? glassContainer.GetSpoutThroatY() : -0.70f;
            float startY = throatY + ballR * 1.08f + patternVerticalOffset;
            int totalRows = 8;
            int splitRow = totalRows / 2; // Rows 0-3 bottom, 4-7 top

            for (int r = 0; r < totalRows; r++)
            {
                float y = startY + r * rowSpacing;

                float minX, maxX;
                if (glassContainer != null)
                {
                    glassContainer.GetSafeCubeBoundsAtY(y, out minX, out maxX);
                }
                else
                {
                    minX = -0.98f;
                    maxX = 0.98f;
                }

                if (maxX - minX < colSpacing * 0.6f) continue;

                float centerX = (minX + maxX) * 0.5f;
                float stagger = (r % 2 == 1) ? (colSpacing * 0.5f) : 0f;

                List<float> rowX = new List<float>();
                for (int c = -14; c <= 14; c++)
                {
                    float x = centerX + c * colSpacing + stagger;
                    if (x >= minX && x <= maxX)
                    {
                        rowX.Add(x);
                    }
                }

                for (int i = 0; i < rowX.Count; i++)
                {
                    float x = rowX[i];
                    PuzzleColor col;
                    if (r < splitRow)
                    {
                        col = (x < centerX) ? cBottomLeft : cBottomRight;
                    }
                    else
                    {
                        col = (x < centerX) ? cTopLeft : cTopRight;
                    }
                    list.Add((new Vector2(x, y), col));
                }
            }
        }

        /// <summary>
        /// Generates the Ordered Ball Pool layout from Reference Image 2:
        /// Staggered horizontal color rows (e.g., Row 0 Bottom = 10 Blue, Row 1 Middle = 11 Yellow, Row 2 Top = 10 Red)
        /// resting in hexagonal close-packing inside the rounded-rectangle glass pool.
        /// Balls obey real 3D physics and are collected strictly from the bottom-most row to the top-most row.
        /// </summary>
        private void GenerateOrderedBallPool(float ballDiam, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            float colSpacing = Mathf.Max(0.12f, ballDiam);
            float rowSpacing = colSpacing * 0.855f; // Honeycomb vertical support step matching 0.495f sphere collider radius
            float ballR = colSpacing * 0.5f;

            float floorY = (glassContainer != null) ? glassContainer.GetChamberFloorY() : -0.56f;
            float startY = floorY + ballR + patternVerticalOffset;

            List<PuzzleColor> rowColors = new List<PuzzleColor>();
            if (goals != null && goals.Count > 0)
            {
                for (int i = 0; i < goals.Count && rowColors.Count < 4; i++)
                {
                    if (goals[i] != null && !rowColors.Contains(goals[i].color))
                    {
                        rowColors.Add(goals[i].color);
                    }
                }
            }

            // Default to Reference Image 2: Bottom = Blue (10), Middle = Yellow (11), Top = Red (10)
            if (rowColors.Count == 0)
            {
                rowColors.Add(PuzzleColor.Blue);
                rowColors.Add(PuzzleColor.Yellow);
                rowColors.Add(PuzzleColor.Red);
            }

            int totalRows = Mathf.Max(3, rowColors.Count);
            float innerHalfW = (glassContainer != null && glassContainer.contourPoints != null && glassContainer.contourPoints.Count > 0)
                ? glassContainer.contourPoints[0].halfWidth * glassContainer.widthScale
                : 1.32f;

            bool hasZones = glassContainer != null && glassContainer.compartmentZones != null && glassContainer.compartmentZones.Count > 0;
            bool hasDividers = glassContainer != null && glassContainer.dividers != null && glassContainer.dividers.Count > 0;

            for (int r = 0; r < totalRows; r++)
            {
                PuzzleColor defaultRowCol = rowColors[r % rowColors.Count];
                float y = startY + r * rowSpacing;

                // Even rows (0, 2): 10 balls centered at (-4.5 .. +4.5) * colSpacing
                // Odd rows  (1, 3): 11 balls centered at (-5.0 .. +5.0) * colSpacing
                int countInRow = (r % 2 == 0) ? 10 : 11;
                float firstX = -(countInRow - 1) * 0.5f * colSpacing;

                for (int c = 0; c < countInRow; c++)
                {
                    float x = firstX + c * colSpacing;
                    Vector2 candidatePt = new Vector2(x, y);

                    if (hasDividers)
                    {
                        bool blocked = false;
                        for (int d = 0; d < glassContainer.dividers.Count; d++)
                        {
                            var div = glassContainer.dividers[d];
                            if (div != null && div.enabled && div.OverlapsCircle(candidatePt, ballR, 0.008f))
                            {
                                blocked = true;
                                break;
                            }
                        }
                        if (blocked) continue;
                    }

                    PuzzleColor ballCol = defaultRowCol;
                    if (hasZones)
                    {
                        float normX = Mathf.Clamp(x / Mathf.Max(0.1f, innerHalfW), -1f, 1f);
                        bool matchedExactRow = false;

                        // 1. Prefer zone matching exact rowLayer == r
                        for (int z = 0; z < glassContainer.compartmentZones.Count; z++)
                        {
                            var zone = glassContainer.compartmentZones[z];
                            if (zone != null && zone.rowLayer == r && zone.Matches(normX, r))
                            {
                                ballCol = zone.color;
                                matchedExactRow = true;
                                break;
                            }
                        }

                        // 2. Fallback to wildcard zone (rowLayer == -1)
                        if (!matchedExactRow)
                        {
                            for (int z = 0; z < glassContainer.compartmentZones.Count; z++)
                            {
                                var zone = glassContainer.compartmentZones[z];
                                if (zone != null && zone.rowLayer < 0 && zone.Matches(normX, r))
                                {
                                    ballCol = zone.color;
                                    break;
                                }
                            }
                        }
                    }

                    list.Add((candidatePt, ballCol));
                }
            }
        }

        /// <summary>
        /// Generates the Gumball Physics Mix from Reference Image 2:
        /// ~80-90 vibrant candy balls densely resting in hexagonal close packing at the bottom of the flask!
        /// Resting naturally on the floor and walls under physics, with zero floating in mid-air and zero central gap.
        /// </summary>
        private void GenerateGumballMix(float spacing, List<LevelMonsterEntry> goals, List<(Vector2, PuzzleColor)> list)
        {
            float colSpacing = spacing;
            float rowSpacing = colSpacing * 0.866025f; // sqrt(3)/2 for honeycomb packing
            float ballR = colSpacing * 0.5f;

            // Start row 0 right above the bottom stopper gate at the throat
            float throatY = (glassContainer != null) ? glassContainer.GetSpoutThroatY() : -0.70f;
            float startY = throatY + ballR * 1.08f + patternVerticalOffset;

            int targetBallCount = 84;
            List<PuzzleColor> colorPool = new List<PuzzleColor>();

            if (goals != null && goals.Count > 0)
            {
                int totalGoalCount = 0;
                for (int i = 0; i < goals.Count; i++) totalGoalCount += goals[i].count;

                for (int i = 0; i < goals.Count; i++)
                {
                    int countForThis = Mathf.Max(4, Mathf.RoundToInt((float)goals[i].count / Mathf.Max(1, totalGoalCount) * targetBallCount));
                    for (int k = 0; k < countForThis; k++)
                    {
                        colorPool.Add(goals[i].color);
                    }
                }
            }

            if (colorPool.Count == 0)
            {
                // 4 balanced candy colors matching the 4 slots and staging monsters
                PuzzleColor[] palette = new PuzzleColor[]
                {
                    PuzzleColor.Blue,
                    PuzzleColor.Yellow,
                    PuzzleColor.Red,
                    PuzzleColor.Green
                };

                int perColor = targetBallCount / palette.Length;
                for (int p = 0; p < palette.Length; p++)
                {
                    for (int k = 0; k < perColor; k++)
                    {
                        colorPool.Add(palette[p]);
                    }
                }
            }

            // Pseudo-random shuffle of color pool for authentic mixed gumball appearance
            for (int i = 0; i < colorPool.Count; i++)
            {
                int swapIdx = UnityEngine.Random.Range(i, colorPool.Count);
                PuzzleColor tmp = colorPool[i];
                colorPool[i] = colorPool[swapIdx];
                colorPool[swapIdx] = tmp;
            }

            int colorIdx = 0;
            int maxRows = 9;

            for (int r = 0; r < maxRows; r++)
            {
                float y = startY + r * rowSpacing;

                float minX, maxX;
                if (glassContainer != null)
                {
                    glassContainer.GetSafeCubeBoundsAtY(y, out minX, out maxX);
                }
                else
                {
                    minX = -0.98f;
                    maxX = 0.98f;
                }

                float safeSpan = maxX - minX;
                if (safeSpan < colSpacing * 0.6f) continue;

                float centerX = (minX + maxX) * 0.5f;
                float stagger = (r % 2 == 1) ? (colSpacing * 0.5f) : 0f;

                List<float> rowX = new List<float>();
                for (int c = -14; c <= 14; c++)
                {
                    float x = centerX + c * colSpacing + stagger;
                    if (x >= minX && x <= maxX)
                    {
                        rowX.Add(x);
                    }
                }

                if (rowX.Count == 0) continue;

                // Top row (r >= 7): slight natural settled crest profile
                if (r >= 7)
                {
                    float crestRadius = 0.68f;
                    rowX.RemoveAll(x => Mathf.Abs(x - centerX) > crestRadius);
                }

                for (int i = 0; i < rowX.Count; i++)
                {
                    PuzzleColor col = colorPool[colorIdx % colorPool.Count];
                    colorIdx++;
                    list.Add((new Vector2(rowX[i], y), col));
                }

                if (list.Count >= 86) break;
            }
        }

        public void GenerateFromAscii(PixelArtPattern pattern, float spacing, List<(Vector2, PuzzleColor)> list)
        {
            if (pattern == null) return;

            var grid = pattern.GetColorGrid();
            int cols = pattern.width;
            int rows = pattern.height;

            float w = (cols - 1) * spacing;
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
            if (totalBalls <= 0) totalBalls = 40;

            PuzzleColor[] colors = GetGoalColors(goals);

            int cols = 8;
            int rows = Mathf.CeilToInt((float)totalBalls / cols);
            float chamberFloorY = (glassContainer != null) ? glassContainer.GetChamberFloorY() : -0.46f;
            float startY = chamberFloorY + spacing * 0.55f + patternVerticalOffset;
            float startX = -(cols - 1) * spacing * 0.5f;

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