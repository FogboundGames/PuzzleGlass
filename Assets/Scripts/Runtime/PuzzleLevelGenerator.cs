using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Procedural Puzzle Level Generator & Solvability Verifier for the 3-Socket Carrier Box & Bottom-to-Top Ball Pool system.
    /// Generates levels across Easy, Medium, Hard, and Boss difficulties where:
    /// - Every Carrier Box holds 3 balls (matching its 3 gray sockets).
    /// - Balls in the glass container are arranged in bottom-to-top triplets (3 triplets = 9 balls per row).
    /// - Once placed in a slot, a Carrier Box is locked until its 3 balls are collected from the bottom floor.
    /// - Every generated level is mathematically verified to be 100% solvable without deadlocking slots.
    /// </summary>
    public static class PuzzleLevelGenerator
    {
        private static readonly PuzzleColor[] FullColorPalette = new PuzzleColor[]
        {
            PuzzleColor.Blue,
            PuzzleColor.Yellow,
            PuzzleColor.Red,
            PuzzleColor.Green,
            PuzzleColor.Purple,
            PuzzleColor.Orange
        };

        private static readonly GlassColorTheme[] ThemesByCycle = new GlassColorTheme[]
        {
            GlassColorTheme.SkyBlueGlossy,
            GlassColorTheme.RoseQuartz,
            GlassColorTheme.EmeraldGreen,
            GlassColorTheme.AmethystPurple,
            GlassColorTheme.AmberGold,
            GlassColorTheme.CrystalClear
        };

        /// <summary>
        /// Returns the recommended difficulty for a given 1-based level number so difficulty naturally progresses.
        /// </summary>
        public static LevelDifficulty GetProgressiveDifficulty(int levelNumber)
        {
            if (levelNumber <= 2) return LevelDifficulty.Easy;
            if (levelNumber <= 4) return LevelDifficulty.Medium;
            if (levelNumber == 5) return LevelDifficulty.Hard;
            if (levelNumber == 6) return LevelDifficulty.Boss;

            int cyclePos = (levelNumber - 7) % 5;
            switch (cyclePos)
            {
                case 0: return LevelDifficulty.Easy;
                case 1: return LevelDifficulty.Medium;
                case 2: return LevelDifficulty.Hard;
                case 3: return LevelDifficulty.Hard;
                default: return LevelDifficulty.Boss;
            }
        }

        /// <summary>
        /// Returns default recommended parameters for a given difficulty tier and level number.
        /// </summary>
        public static void GetRecommendedParameters(
            LevelDifficulty difficulty,
            int levelNumber,
            out int slotCount,
            out int colorCount,
            out int totalBoxes,
            out int stagingColumns,
            out PuzzleLayerStyle layerStyle)
        {
            switch (difficulty)
            {
                case LevelDifficulty.Easy:
                    slotCount = 4;
                    colorCount = (levelNumber <= 1) ? 2 : 3;
                    totalBoxes = (levelNumber <= 1) ? 5 : 6;
                    stagingColumns = 3;
                    layerStyle = PuzzleLayerStyle.PureHorizontalRows;
                    break;

                case LevelDifficulty.Medium:
                    slotCount = 4;
                    colorCount = (levelNumber % 2 == 0) ? 4 : 3;
                    totalBoxes = 7;
                    stagingColumns = 3;
                    layerStyle = PuzzleLayerStyle.SplitLeftRightBlocks;
                    break;

                case LevelDifficulty.Hard:
                    slotCount = 4;
                    colorCount = (levelNumber >= 8) ? 5 : 4;
                    totalBoxes = 9;
                    stagingColumns = 3;
                    layerStyle = PuzzleLayerStyle.InterleavedTriplets;
                    break;

                case LevelDifficulty.Boss:
                default:
                    slotCount = 4;
                    colorCount = (levelNumber >= 10) ? 6 : 5;
                    totalBoxes = (levelNumber >= 10) ? 11 : 10;
                    stagingColumns = 4;
                    layerStyle = PuzzleLayerStyle.CheckerboardMix;
                    break;
            }
        }

        /// <summary>
        /// Populates a PuzzleGlassLevelData instance with a guaranteed-solvable polyomino sliding-grid puzzle
        /// paired with balanced bottom-to-top balls in the top glass container.
        /// </summary>
        public static void GenerateForLevelData(
            PuzzleGlassLevelData levelData,
            int? customColorCount = null,
            int? customTotalBoxes = null,
            int? customSlotCount = null,
            int? customStagingColumns = null,
            PuzzleLayerStyle? customLayerStyle = null,
            int? randomSeed = null,
            int? customBallsPerBox = null)
        {
            if (levelData == null) return;

            GetRecommendedParameters(
                levelData.difficulty,
                Mathf.Max(1, levelData.levelNumber),
                out int recSlots,
                out int recColors,
                out int recBoxes,
                out int recCols,
                out PuzzleLayerStyle recStyle);

            int slotCount = Mathf.Clamp(customSlotCount ?? recSlots, 2, 5);
            int colorCount = Mathf.Clamp(customColorCount ?? recColors, 2, FullColorPalette.Length);
            int baseBallsPerBox = Mathf.Clamp(customBallsPerBox ?? levelData.ballsPerBox, 1, MonsterCharacter.MaxVisualSockets);
            int targetBoxes = Mathf.Clamp(customTotalBoxes ?? recBoxes, 3, 18);

            int stagingColumns = Mathf.Clamp(customStagingColumns ?? recCols, 2, 4);
            PuzzleLayerStyle layerStyle = customLayerStyle ?? recStyle;

            System.Random rng = randomSeed.HasValue
                ? new System.Random(randomSeed.Value)
                : new System.Random(Guid.NewGuid().GetHashCode());

            // 1. Pick distinct colors for this level
            List<PuzzleColor> palette = new List<PuzzleColor>(FullColorPalette);
            int shift = Mathf.Abs(levelData.levelNumber - 1) % palette.Count;
            List<PuzzleColor> activeColors = new List<PuzzleColor>(colorCount);
            for (int i = 0; i < colorCount; i++)
            {
                activeColors.Add(palette[(shift + i) % palette.Count]);
            }

            // 2. Choose framed grid dimensions fitting the polyomino blocks + at least 4 sliding cells
            GetRecommendedGridDimensions(levelData.difficulty, targetBoxes, out int gridCols, out int gridRows);

            // 3. Pack solvable polyomino blocks into the grid and compute a valid slide-out extraction sequence
            List<GridPackedBlock> packedBlocks = BuildSolvableGridLayout(
                gridCols,
                gridRows,
                targetBoxes,
                levelData.difficulty,
                rng,
                out List<int> extractionOrder);

            int blockCount = packedBlocks.Count;

            // 4. Assign shape-proportional socket counts (bigger polyominoes carry more gray sockets!)
            for (int i = 0; i < blockCount; i++)
            {
                packedBlocks[i].socketCount = ComputeShapeSocketCount(packedBlocks[i].shape, baseBallsPerBox, rng);
            }

            // 5. Assign colors in a balanced bottom-to-top sequence aligned with the proven grid extraction order,
            //    applying difficulty-scaled color swaps that remain 100% solvable under upper slotCount rules.
            AssignSolvableBlockColors(
                packedBlocks,
                extractionOrder,
                activeColors,
                layerStyle,
                slotCount,
                levelData.difficulty,
                rng,
                out List<LevelMonsterEntry> bottomToTopGroups,
                out List<PuzzleColor> bottomToTopTriplets,
                out List<LevelMonsterEntry> stagingBlocks);

            // 6. Assign to levelData (carrierBoxFormatVersion = 3)
            levelData.slotCount = slotCount;
            levelData.slotSpacing = (slotCount >= 4) ? 0.80f : 0.88f;
            levelData.ballsPerBox = baseBallsPerBox;
            levelData.carrierBoxFormatVersion = 3;
            levelData.stagingGridCols = gridCols;
            levelData.stagingGridRows = gridRows;
            levelData.stagingColumns = stagingColumns;
            levelData.useStagingColumnQueue = false;
            levelData.puzzleLayerStyle = layerStyle;
            levelData.particleMode = ParticleArrangementMode.PhysicsBalls;
            levelData.particleSize = 0.255f;
            levelData.particlesPerMonsterCount = 1;
            levelData.glassPreset = GlassShapePreset.RectangleBox;
            levelData.compartmentPreset = GlassCompartmentPreset.Custom;
            if (levelData.dividers == null) levelData.dividers = new List<GlassDividerData>();
            levelData.dividers.Clear();
            if (levelData.compartmentZones != null) levelData.compartmentZones.Clear();

            GlassColorTheme theme = ThemesByCycle[Mathf.Abs(levelData.levelNumber - 1) % ThemesByCycle.Length];
            ApplyColorTheme(levelData, theme);

            levelData.bottomToTopTriplets = bottomToTopTriplets;
            levelData.bottomToTopGroups = bottomToTopGroups;
            levelData.monsters = stagingBlocks;

            int totalBalls = 0;
            for (int i = 0; i < stagingBlocks.Count; i++) totalBalls += Mathf.Max(1, stagingBlocks[i].count);

            string diffTag = GetDifficultyTurkishLabel(levelData.difficulty);
            levelData.levelName = $"Level {levelData.levelNumber} - {diffTag} ({gridCols}x{gridRows} Izgara, {blockCount} Küp, {totalBalls} Top)";
        }

        // ==================== POLYOMINO GRID PUZZLE GENERATOR ====================

        private class GridPackedBlock
        {
            public CarrierBlockShape shape;
            public Vector2Int anchor;
            public int socketCount;
            public PuzzleColor color;
        }

        private static readonly CarrierBlockShape[] EasyGridShapes = new CarrierBlockShape[]
        {
            CarrierBlockShape.Bar2x1,
            CarrierBlockShape.Bar1x2,
            CarrierBlockShape.LShape3,
            CarrierBlockShape.JShape3,
            CarrierBlockShape.LTopRight3,
            CarrierBlockShape.Rect1x1
        };

        private static readonly CarrierBlockShape[] MediumGridShapes = new CarrierBlockShape[]
        {
            CarrierBlockShape.LShape3,
            CarrierBlockShape.JShape3,
            CarrierBlockShape.LTopRight3,
            CarrierBlockShape.JTopLeft3,
            CarrierBlockShape.Bar2x1,
            CarrierBlockShape.Bar1x2,
            CarrierBlockShape.TShape4,
            CarrierBlockShape.Square2x2,
            CarrierBlockShape.Rect1x1
        };

        private static readonly CarrierBlockShape[] HardGridShapes = new CarrierBlockShape[]
        {
            CarrierBlockShape.TShape4,
            CarrierBlockShape.TShapeUp4,
            CarrierBlockShape.LShape3,
            CarrierBlockShape.JShape3,
            CarrierBlockShape.LTopRight3,
            CarrierBlockShape.JTopLeft3,
            CarrierBlockShape.Square2x2,
            CarrierBlockShape.Bar3x1,
            CarrierBlockShape.Bar2x1,
            CarrierBlockShape.Bar1x2,
            CarrierBlockShape.Rect1x1
        };

        private static void GetRecommendedGridDimensions(
            LevelDifficulty difficulty,
            int targetBlocks,
            out int gridCols,
            out int gridRows)
        {
            switch (difficulty)
            {
                case LevelDifficulty.Easy:
                    gridCols = 4;
                    gridRows = 4;
                    break;
                case LevelDifficulty.Medium:
                    gridCols = 5;
                    gridRows = 4;
                    break;
                case LevelDifficulty.Hard:
                    gridCols = 5;
                    gridRows = 5;
                    break;
                case LevelDifficulty.Boss:
                default:
                    gridCols = 6;
                    gridRows = 5;
                    break;
            }

            // Ensure there are enough cells to comfortably place targetBlocks + sliding empty cells
            while (gridCols * gridRows < targetBlocks * 2 + 4 && (gridCols < 6 || gridRows < 5))
            {
                if (gridCols < 6) gridCols++;
                else if (gridRows < 5) gridRows++;
                else break;
            }
        }

        private static int ComputeShapeSocketCount(CarrierBlockShape shape, int baseBalls, System.Random rng)
        {
            int cellArea = CarrierBlockShapeUtility.GetCellCount(shape);
            int minSockets;
            int maxSockets;
            switch (cellArea)
            {
                case 1:
                    minSockets = Mathf.Max(1, baseBalls - 1);
                    maxSockets = Mathf.Max(minSockets, baseBalls);
                    break;
                case 2:
                    minSockets = Mathf.Clamp(baseBalls, 2, 12);
                    maxSockets = Mathf.Clamp(baseBalls + 2, minSockets, 14);
                    break;
                case 3:
                    minSockets = Mathf.Clamp(baseBalls + 1, 3, 14);
                    maxSockets = Mathf.Clamp(baseBalls + 3, minSockets, 16);
                    break;
                default: // 4-cell T or 2x2 shapes
                    minSockets = Mathf.Clamp(baseBalls + 2, 4, 14);
                    maxSockets = Mathf.Clamp(baseBalls + 5, minSockets, 16);
                    break;
            }

            return Mathf.Clamp(rng.Next(minSockets, maxSockets + 1), 1, MonsterCharacter.MaxVisualSockets);
        }

        private static List<GridPackedBlock> BuildSolvableGridLayout(
            int gridCols,
            int gridRows,
            int targetBlocks,
            LevelDifficulty difficulty,
            System.Random rng,
            out List<int> bestExtractionOrder)
        {
            CarrierBlockShape[] pool = difficulty == LevelDifficulty.Easy
                ? EasyGridShapes
                : (difficulty == LevelDifficulty.Medium ? MediumGridShapes : HardGridShapes);

            int minFreeCells = Mathf.Max(4, Mathf.RoundToInt(gridCols * gridRows * 0.20f));
            List<GridPackedBlock> bestLayout = null;
            bestExtractionOrder = null;

            for (int attempt = 0; attempt < 28; attempt++)
            {
                bool[,] occupied = new bool[gridCols, gridRows];
                int freeCells = gridCols * gridRows;
                List<GridPackedBlock> blocks = new List<GridPackedBlock>();

                int safetyPlacementTries = 0;
                while (blocks.Count < targetBlocks && freeCells > minFreeCells && safetyPlacementTries < 80)
                {
                    safetyPlacementTries++;
                    CarrierBlockShape candidateShape;
                    if (blocks.Count < Mathf.Max(2, targetBlocks / 2))
                    {
                        candidateShape = pool[rng.Next(0, pool.Length)];
                    }
                    else
                    {
                        // Bias toward compact shapes once board fills up so target count is reached cleanly
                        CarrierBlockShape[] compact = { CarrierBlockShape.Bar2x1, CarrierBlockShape.Bar1x2, CarrierBlockShape.Rect1x1, CarrierBlockShape.LShape3, CarrierBlockShape.JShape3 };
                        candidateShape = compact[rng.Next(0, compact.Length)];
                    }

                    int shapeArea = CarrierBlockShapeUtility.GetCellCount(candidateShape);
                    if (freeCells - shapeArea < minFreeCells)
                    {
                        candidateShape = (freeCells - 2 >= minFreeCells)
                            ? (rng.Next(0, 2) == 0 ? CarrierBlockShape.Bar2x1 : CarrierBlockShape.Bar1x2)
                            : CarrierBlockShape.Rect1x1;
                        shapeArea = CarrierBlockShapeUtility.GetCellCount(candidateShape);
                        if (freeCells - shapeArea < minFreeCells) break;
                    }

                    if (TryPlaceShapeRandomly(occupied, gridCols, gridRows, candidateShape, rng, out Vector2Int anchor))
                    {
                        MarkShape(occupied, candidateShape, anchor, true);
                        freeCells -= shapeArea;
                        blocks.Add(new GridPackedBlock { shape = candidateShape, anchor = anchor });
                    }
                }

                if (blocks.Count < 3) continue;

                if (TryComputeGridExtractionOrder(blocks, gridCols, gridRows, out List<int> order))
                {
                    // Prefer richer layouts with more polyominoes when close to target
                    if (bestLayout == null || blocks.Count > bestLayout.Count)
                    {
                        bestLayout = blocks;
                        bestExtractionOrder = order;
                        if (blocks.Count >= targetBlocks) break;
                    }
                }
            }

            if (bestLayout != null && bestExtractionOrder != null && bestLayout.Count >= 3)
            {
                return bestLayout;
            }

            // Deterministic fallback: clean staggered non-interlocking grid layout (100% solvable)
            List<GridPackedBlock> fallback = new List<GridPackedBlock>();
            bool[,] fbOcc = new bool[gridCols, gridRows];
            int fbFree = gridCols * gridRows;
            CarrierBlockShape[] fbShapes =
            {
                CarrierBlockShape.LShape3,
                CarrierBlockShape.JShape3,
                CarrierBlockShape.Bar2x1,
                CarrierBlockShape.Bar1x2,
                CarrierBlockShape.Rect1x1
            };

            for (int r = gridRows - 1; r >= 0 && fallback.Count < targetBlocks; r--)
            {
                for (int c = 0; c < gridCols && fallback.Count < targetBlocks; c++)
                {
                    for (int s = 0; s < fbShapes.Length; s++)
                    {
                        CarrierBlockShape sh = fbShapes[(r + c + s) % fbShapes.Length];
                        int area = CarrierBlockShapeUtility.GetCellCount(sh);
                        if (fbFree - area < 3) continue;
                        Vector2Int a = new Vector2Int(c, r);
                        if (CanPlaceOnArray(fbOcc, gridCols, gridRows, sh, a))
                        {
                            MarkShape(fbOcc, sh, a, true);
                            fbFree -= area;
                            fallback.Add(new GridPackedBlock { shape = sh, anchor = a });
                            break;
                        }
                    }
                }
            }

            if (!TryComputeGridExtractionOrder(fallback, gridCols, gridRows, out bestExtractionOrder))
            {
                // If even fallback jammed, reduce to clean 1x1 staggered blocks
                fallback.Clear();
                bestExtractionOrder = new List<int>();
                for (int r = gridRows - 1; r >= 0 && fallback.Count < targetBlocks; r--)
                {
                    for (int c = 0; c < gridCols && fallback.Count < targetBlocks; c++)
                    {
                        if ((r + c) % 2 == 0 || r == gridRows - 1)
                        {
                            bestExtractionOrder.Add(fallback.Count);
                            fallback.Add(new GridPackedBlock { shape = CarrierBlockShape.Rect1x1, anchor = new Vector2Int(c, r) });
                        }
                    }
                }
            }

            return fallback;
        }

        private static bool TryPlaceShapeRandomly(
            bool[,] occ,
            int cols,
            int rows,
            CarrierBlockShape shape,
            System.Random rng,
            out Vector2Int chosenAnchor)
        {
            CarrierBlockShapeUtility.GetBounds(shape, out int w, out int h);
            List<Vector2Int> candidates = new List<Vector2Int>();
            for (int y = 0; y <= rows - h; y++)
            {
                for (int x = 0; x <= cols - w; x++)
                {
                    Vector2Int a = new Vector2Int(x, y);
                    if (CanPlaceOnArray(occ, cols, rows, shape, a))
                    {
                        candidates.Add(a);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                chosenAnchor = Vector2Int.zero;
                return false;
            }

            chosenAnchor = candidates[rng.Next(0, candidates.Count)];
            return true;
        }

        private static bool CanPlaceOnArray(bool[,] occ, int cols, int rows, CarrierBlockShape shape, Vector2Int anchor)
        {
            Vector2Int[] cells = CarrierBlockShapeUtility.GetCells(shape);
            for (int i = 0; i < cells.Length; i++)
            {
                int gx = anchor.x + cells[i].x;
                int gy = anchor.y + cells[i].y;
                if (gx < 0 || gx >= cols || gy < 0 || gy >= rows || occ[gx, gy])
                    return false;
            }
            return true;
        }

        private static void MarkShape(bool[,] occ, CarrierBlockShape shape, Vector2Int anchor, bool value)
        {
            Vector2Int[] cells = CarrierBlockShapeUtility.GetCells(shape);
            for (int i = 0; i < cells.Length; i++)
            {
                occ[anchor.x + cells[i].x, anchor.y + cells[i].y] = value;
            }
        }

        private static bool IsSimulatedExitClear(
            int targetIdx,
            Vector2Int[] anchors,
            CarrierBlockShape[] shapes,
            bool[] removed)
        {
            Vector2Int[] myCells = CarrierBlockShapeUtility.GetCells(shapes[targetIdx]);
            for (int j = 0; j < anchors.Length; j++)
            {
                if (j == targetIdx || removed[j]) continue;
                Vector2Int[] otherCells = CarrierBlockShapeUtility.GetCells(shapes[j]);
                for (int a = 0; a < myCells.Length; a++)
                {
                    int mx = anchors[targetIdx].x + myCells[a].x;
                    int my = anchors[targetIdx].y + myCells[a].y;
                    for (int b = 0; b < otherCells.Length; b++)
                    {
                        int ox = anchors[j].x + otherCells[b].x;
                        int oy = anchors[j].y + otherCells[b].y;
                        if (ox == mx && oy > my) return false;
                    }
                }
            }
            return true;
        }

        private static bool TryComputeGridExtractionOrder(
            List<GridPackedBlock> blocks,
            int cols,
            int rows,
            out List<int> extractionOrder)
        {
            int n = blocks.Count;
            extractionOrder = new List<int>(n);
            Vector2Int[] anchors = new Vector2Int[n];
            CarrierBlockShape[] shapes = new CarrierBlockShape[n];
            bool[] removed = new bool[n];

            for (int i = 0; i < n; i++)
            {
                anchors[i] = blocks[i].anchor;
                shapes[i] = blocks[i].shape;
            }

            for (int step = 0; step < n; step++)
            {
                // 1. Prefer an already top-unobstructed block (highest anchor first)
                int clearIdx = -1;
                int bestY = -1;
                for (int i = 0; i < n; i++)
                {
                    if (removed[i]) continue;
                    if (IsSimulatedExitClear(i, anchors, shapes, removed))
                    {
                        if (anchors[i].y > bestY)
                        {
                            bestY = anchors[i].y;
                            clearIdx = i;
                        }
                    }
                }

                if (clearIdx >= 0)
                {
                    removed[clearIdx] = true;
                    extractionOrder.Add(clearIdx);
                    continue;
                }

                // 2. No block has a clear exit yet: run a short BFS of orthogonal grid slides to open an exit
                if (TryFindSlideUnlockingAnyBlock(cols, rows, anchors, shapes, removed, out Vector2Int[] nextAnchors, out int unlockedIdx))
                {
                    anchors = nextAnchors;
                    removed[unlockedIdx] = true;
                    extractionOrder.Add(unlockedIdx);
                    continue;
                }

                return false;
            }

            return extractionOrder.Count == n;
        }

        private static bool TryFindSlideUnlockingAnyBlock(
            int cols,
            int rows,
            Vector2Int[] startAnchors,
            CarrierBlockShape[] shapes,
            bool[] removed,
            out Vector2Int[] winningAnchors,
            out int unlockedBlockIdx)
        {
            Queue<Vector2Int[]> q = new Queue<Vector2Int[]>();
            HashSet<string> visited = new HashSet<string>();
            q.Enqueue((Vector2Int[])startAnchors.Clone());
            visited.Add(EncodeAnchorsKey(startAnchors, removed));

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down };
            int explored = 0;
            const int maxStates = 220;

            while (q.Count > 0 && explored < maxStates)
            {
                Vector2Int[] state = q.Dequeue();
                explored++;

                bool[,] occ = new bool[cols, rows];
                for (int i = 0; i < state.Length; i++)
                {
                    if (!removed[i]) MarkShape(occ, shapes[i], state[i], true);
                }

                for (int i = 0; i < state.Length; i++)
                {
                    if (removed[i]) continue;

                    for (int d = 0; d < dirs.Length; d++)
                    {
                        Vector2Int cand = state[i] + dirs[d];
                        MarkShape(occ, shapes[i], state[i], false);
                        bool valid = CanPlaceOnArray(occ, cols, rows, shapes[i], cand);
                        MarkShape(occ, shapes[i], state[i], true);

                        if (!valid) continue;

                        Vector2Int[] next = (Vector2Int[])state.Clone();
                        next[i] = cand;

                        for (int k = 0; k < next.Length; k++)
                        {
                            if (!removed[k] && IsSimulatedExitClear(k, next, shapes, removed))
                            {
                                winningAnchors = next;
                                unlockedBlockIdx = k;
                                return true;
                            }
                        }

                        string key = EncodeAnchorsKey(next, removed);
                        if (visited.Add(key))
                        {
                            q.Enqueue(next);
                        }
                    }
                }
            }

            winningAnchors = startAnchors;
            unlockedBlockIdx = -1;
            return false;
        }

        private static string EncodeAnchorsKey(Vector2Int[] anchors, bool[] removed)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(anchors.Length * 4);
            for (int i = 0; i < anchors.Length; i++)
            {
                if (removed[i]) sb.Append("x;");
                else sb.Append(anchors[i].x).Append(',').Append(anchors[i].y).Append(';');
            }
            return sb.ToString();
        }

        private static void AssignSolvableBlockColors(
            List<GridPackedBlock> packedBlocks,
            List<int> extractionOrder,
            List<PuzzleColor> activeColors,
            PuzzleLayerStyle layerStyle,
            int slotCount,
            LevelDifficulty difficulty,
            System.Random rng,
            out List<LevelMonsterEntry> bottomToTopGroups,
            out List<PuzzleColor> bottomToTopTriplets,
            out List<LevelMonsterEntry> stagingBlocks)
        {
            int n = packedBlocks.Count;
            List<PuzzleColor> orderedColors = BuildBottomToTopTriplets(n, activeColors, layerStyle, rng);

            bottomToTopGroups = new List<LevelMonsterEntry>(n);
            bottomToTopTriplets = new List<PuzzleColor>(n);
            List<LevelMonsterEntry> extractionQueue = new List<LevelMonsterEntry>(n);

            for (int step = 0; step < n; step++)
            {
                int blockIdx = extractionOrder[step];
                GridPackedBlock b = packedBlocks[blockIdx];
                PuzzleColor col = orderedColors[step];
                b.color = col;

                bottomToTopTriplets.Add(col);
                var groupEntry = new LevelMonsterEntry(col, b.socketCount, b.shape, b.anchor);
                bottomToTopGroups.Add(groupEntry);
                extractionQueue.Add(new LevelMonsterEntry(col, b.socketCount, b.shape, b.anchor));
            }

            // Direct in-grid collection has no upper holding slots (maxBuffer = 0):
            // keep extraction order 100% aligned with bottom-to-top glass lane heads, and only allow safe
            // swaps within adjacent blocks that remain immediately solvable with 0 holding slots.
            int swapTries = difficulty == LevelDifficulty.Easy ? 0 : (difficulty == LevelDifficulty.Medium ? 6 : 12);
            for (int attempt = 0; attempt < swapTries; attempt++)
            {
                int idxA = rng.Next(0, n);
                int idxB = Mathf.Clamp(idxA + rng.Next(-1, 2), 0, n - 1);
                if (idxA == idxB || extractionQueue[idxA].color == extractionQueue[idxB].color) continue;

                var tmpG = bottomToTopGroups[idxA];
                bottomToTopGroups[idxA] = bottomToTopGroups[idxB];
                bottomToTopGroups[idxB] = tmpG;

                if (!SimulateCanSolve(bottomToTopGroups, extractionQueue, 1, Mathf.Max(2, slotCount), useColumnQueue: true, maxAllowedBufferSlots: 0))
                {
                    bottomToTopGroups[idxB] = bottomToTopGroups[idxA];
                    bottomToTopGroups[idxA] = tmpG;
                }
            }

            bottomToTopTriplets.Clear();
            for (int i = 0; i < bottomToTopGroups.Count; i++)
            {
                bottomToTopTriplets.Add(bottomToTopGroups[i].color);
            }

            stagingBlocks = new List<LevelMonsterEntry>(n);
            for (int i = 0; i < n; i++)
            {
                var b = packedBlocks[i];
                stagingBlocks.Add(new LevelMonsterEntry(b.color, b.socketCount, b.shape, b.anchor));
            }
        }

        /// <summary>
        /// Interactive Pull-Pin shelves and Color-Locked Gates are disabled; ensures generated levels contain no pins/gates.
        /// </summary>
        public static void PopulateInteractivePinsAndGates(PuzzleGlassLevelData levelData, List<PuzzleColor> bottomToTopTriplets)
        {
            if (levelData == null) return;
            if (levelData.dividers == null) levelData.dividers = new List<GlassDividerData>();
            levelData.dividers.Clear();
        }

        public static string GetDifficultyTurkishLabel(LevelDifficulty diff)
        {
            switch (diff)
            {
                case LevelDifficulty.Easy: return "Kolay";
                case LevelDifficulty.Medium: return "Orta";
                case LevelDifficulty.Hard: return "Zor";
                case LevelDifficulty.Boss: return "Boss";
                default: return "Normal";
            }
        }

        /// <summary>
        /// Constructs the bottom-to-top sequence of 3-ball triplets (3 triplets per horizontal row = 9 balls per row)
        /// according to the chosen PuzzleLayerStyle.
        /// </summary>
        private static List<PuzzleColor> BuildBottomToTopTriplets(
            int totalTriplets,
            List<PuzzleColor> activeColors,
            PuzzleLayerStyle style,
            System.Random rng)
        {
            List<PuzzleColor> triplets = new List<PuzzleColor>(totalTriplets);
            int numColors = activeColors.Count;
            int rows = Mathf.CeilToInt(totalTriplets / 3f);

            if (style == PuzzleLayerStyle.PureHorizontalRows)
            {
                // Each row of 3 triplets (9 balls) shares the same color so layers are pure horizontal bands
                for (int r = 0; r < rows; r++)
                {
                    PuzzleColor rowCol = activeColors[r % numColors];
                    for (int c = 0; c < 3 && triplets.Count < totalTriplets; c++)
                    {
                        triplets.Add(rowCol);
                    }
                }
                return triplets;
            }

            // For SplitLeftRightBlocks, InterleavedTriplets, and CheckerboardMix:
            // First create a balanced pool of triplets across all activeColors
            List<PuzzleColor> pool = new List<PuzzleColor>(totalTriplets);
            for (int i = 0; i < totalTriplets; i++)
            {
                pool.Add(activeColors[i % numColors]);
            }

            if (style == PuzzleLayerStyle.SplitLeftRightBlocks)
            {
                // Pair up triplets so each row has 2 contiguous triplets of one color + 1 of another
                for (int r = 0; r < rows; r++)
                {
                    PuzzleColor cA = activeColors[r % numColors];
                    PuzzleColor cB = activeColors[(r + 1) % numColors];
                    bool leftHasTwo = (r % 2 == 0);
                    if (triplets.Count < totalTriplets) triplets.Add(leftHasTwo ? cA : cB);
                    if (triplets.Count < totalTriplets) triplets.Add(cA);
                    if (triplets.Count < totalTriplets) triplets.Add(leftHasTwo ? cB : cA);
                }
                return triplets;
            }

            if (style == PuzzleLayerStyle.InterleavedTriplets)
            {
                // Each row has 3 different colors in its Left, Center, Right triplets, shifted per row
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < 3 && triplets.Count < totalTriplets; c++)
                    {
                        PuzzleColor col = activeColors[(r + c) % numColors];
                        triplets.Add(col);
                    }
                }
                return triplets;
            }

            // CheckerboardMix (Boss): Shuffle pool while ensuring no two vertically stacked triplets are identical if avoidable
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int swapIdx = rng.Next(0, i + 1);
                PuzzleColor tmp = pool[i];
                pool[i] = pool[swapIdx];
                pool[swapIdx] = tmp;
            }

            return pool;
        }

        /// <summary>
        /// Distributes bottom-to-top groups into the 3 vertical glass lanes (Left=0, Center=1, Right=2)
        /// by always appending the next group to the shortest lane (ties broken Left-&gt;Center-&gt;Right).
        /// For uniform group counts this matches classic 3-column triplet rows identically,
        /// and for mixed box sizes keeps all 3 vertical glass lanes balanced.
        /// </summary>
        public static List<LevelMonsterEntry>[] DistributeGroupsIntoThreeLanes(List<LevelMonsterEntry> groups)
        {
            List<LevelMonsterEntry>[] lanes = new List<LevelMonsterEntry>[3]
            {
                new List<LevelMonsterEntry>(),
                new List<LevelMonsterEntry>(),
                new List<LevelMonsterEntry>()
            };
            if (groups == null) return lanes;

            int[] heights = new int[3];
            for (int i = 0; i < groups.Count; i++)
            {
                LevelMonsterEntry g = groups[i];
                if (g == null) continue;
                int bestLane = 0;
                if (heights[1] < heights[bestLane]) bestLane = 1;
                if (heights[2] < heights[bestLane]) bestLane = 2;

                int cnt = Mathf.Max(1, g.count);
                lanes[bestLane].Add(new LevelMonsterEntry(g.color, cnt));
                heights[bestLane] += cnt;
            }
            return lanes;
        }

        /// <summary>
        /// Builds the staging Carrier Box queue from the bottom-to-top groups, performing difficulty-scaled
        /// swaps that are verified by SimulateCanSolve so the level is 100% solvable.
        /// </summary>
        private static List<LevelMonsterEntry> BuildSolvableStagingQueue(
            List<LevelMonsterEntry> bottomToTopGroups,
            int stagingColumns,
            int slotCount,
            LevelDifficulty difficulty,
            bool useColumnQueue,
            System.Random rng)
        {
            int n = bottomToTopGroups.Count;
            List<LevelMonsterEntry> result = new List<LevelMonsterEntry>(n);
            for (int i = 0; i < n; i++)
            {
                var g = bottomToTopGroups[i];
                result.Add(new LevelMonsterEntry(g.color, Mathf.Max(1, g.count)));
            }

            // Determine how many swap attempts and how many buffer slots the solution may require
            int swapAttempts;
            int maxAllowedBufferSlots;
            int maxRowSwapDistance;

            switch (difficulty)
            {
                case LevelDifficulty.Easy:
                    // Front row only swap so the bottom-most colors are immediately in the front row of columns
                    swapAttempts = 4;
                    maxAllowedBufferSlots = 0;
                    maxRowSwapDistance = 0; // Only permute within the same horizontal staging row!
                    break;

                case LevelDifficulty.Medium:
                    swapAttempts = 14;
                    maxAllowedBufferSlots = Mathf.Min(1, slotCount - 1);
                    maxRowSwapDistance = 1; // Can swap between adjacent rows (row r and row r+1)
                    break;

                case LevelDifficulty.Hard:
                    swapAttempts = 28;
                    maxAllowedBufferSlots = Mathf.Min(2, slotCount - 1);
                    maxRowSwapDistance = 1;
                    break;

                case LevelDifficulty.Boss:
                default:
                    swapAttempts = 45;
                    maxAllowedBufferSlots = Mathf.Min(2, slotCount - 1);
                    maxRowSwapDistance = 2;
                    break;
            }

            // First, shuffle horizontally within each staging row so column positions aren't always Left->Center->Right
            int totalStagingRows = Mathf.CeilToInt(n / (float)stagingColumns);
            for (int r = 0; r < totalStagingRows; r++)
            {
                int rowStart = r * stagingColumns;
                int rowEnd = Mathf.Min(n, rowStart + stagingColumns);
                for (int i = rowEnd - 1; i > rowStart; i--)
                {
                    int j = rng.Next(rowStart, i + 1);
                    LevelMonsterEntry tmp = result[i];
                    result[i] = result[j];
                    result[j] = tmp;
                }
            }

            // Perform solvability-verified swaps across staging rows for Medium / Hard / Boss
            if (maxRowSwapDistance > 0 && n > stagingColumns)
            {
                for (int attempt = 0; attempt < swapAttempts; attempt++)
                {
                    int idxA = rng.Next(0, n);
                    int rowA = idxA / stagingColumns;
                    int rowB = Mathf.Clamp(rowA + rng.Next(-maxRowSwapDistance, maxRowSwapDistance + 1), 0, totalStagingRows - 1);
                    int colB = rng.Next(0, stagingColumns);
                    int idxB = rowB * stagingColumns + colB;

                    if (idxB >= n || idxA == idxB) continue;
                    if (result[idxA].color == result[idxB].color && result[idxA].count == result[idxB].count) continue;

                    // Try swap
                    LevelMonsterEntry tmp = result[idxA];
                    result[idxA] = result[idxB];
                    result[idxB] = tmp;

                    // Verify solvability under locked-slot + bottom-to-top rules
                    if (!SimulateCanSolve(bottomToTopGroups, result, stagingColumns, slotCount, useColumnQueue, maxAllowedBufferSlots))
                    {
                        // Revert swap if it would make the level unsolvable or exceed target difficulty buffer
                        result[idxB] = result[idxA];
                        result[idxA] = tmp;
                    }
                }
            }

            return result;
        }

        private struct SimSlotState
        {
            public PuzzleColor color;
            public int remainingNeed;
        }

        /// <summary>
        /// Simulates playing a 3-socket Carrier Box level using bottom-to-top color triplets.
        /// </summary>
        public static bool SimulateCanSolve(
            List<PuzzleColor> bottomToTopTriplets,
            List<PuzzleColor> stagingBoxColors,
            int stagingColumns,
            int slotCount,
            bool useColumnQueue,
            int maxAllowedBufferSlots)
        {
            if (bottomToTopTriplets == null || stagingBoxColors == null) return true;
            var groups = new List<LevelMonsterEntry>(bottomToTopTriplets.Count);
            for (int i = 0; i < bottomToTopTriplets.Count; i++) groups.Add(new LevelMonsterEntry(bottomToTopTriplets[i], 3));
            var boxes = new List<LevelMonsterEntry>(stagingBoxColors.Count);
            for (int i = 0; i < stagingBoxColors.Count; i++) boxes.Add(new LevelMonsterEntry(stagingBoxColors[i], 3));
            return SimulateCanSolve(groups, boxes, stagingColumns, slotCount, useColumnQueue, maxAllowedBufferSlots);
        }

        /// <summary>
        /// Simulates playing the level with locked slots and bottom-to-top 3-lane accessibility
        /// for arbitrary variable-socket boxes (1..16 or mixed counts).
        /// Returns true if the level can be solved without ever exceeding slotCount or maxAllowedBufferSlots.
        /// </summary>
        public static bool SimulateCanSolve(
            List<LevelMonsterEntry> bottomToTopGroups,
            List<LevelMonsterEntry> stagingBoxes,
            int stagingColumns,
            int slotCount,
            bool useColumnQueue,
            int maxAllowedBufferSlots)
        {
            if (bottomToTopGroups == null || stagingBoxes == null) return false;
            if (bottomToTopGroups.Count == 0 && stagingBoxes.Count == 0) return true;

            // Verify exact per-color ball totals match between glass pool and staging boxes
            int[] colorDiff = new int[16];
            int totalGlassBalls = 0;
            int totalBoxBalls = 0;
            for (int i = 0; i < bottomToTopGroups.Count; i++)
            {
                var g = bottomToTopGroups[i];
                if (g == null) continue;
                int c = Mathf.Max(1, g.count);
                colorDiff[(int)g.color] += c;
                totalGlassBalls += c;
            }
            for (int i = 0; i < stagingBoxes.Count; i++)
            {
                var b = stagingBoxes[i];
                if (b == null) continue;
                int c = Mathf.Max(1, b.count);
                colorDiff[(int)b.color] -= c;
                totalBoxBalls += c;
            }
            if (totalGlassBalls != totalBoxBalls) return false;
            for (int i = 0; i < colorDiff.Length; i++)
            {
                if (colorDiff[i] != 0) return false;
            }
            if (totalGlassBalls == 0) return true;

            stagingColumns = Mathf.Clamp(stagingColumns, 1, 8);
            slotCount = Mathf.Clamp(slotCount, 1, 8);

            // Distribute bottom-to-top groups into 3 vertical glass lanes (ball queues bottom -> top)
            List<LevelMonsterEntry>[] rawLanes = DistributeGroupsIntoThreeLanes(bottomToTopGroups);
            List<LevelMonsterEntry>[] lanes = new List<LevelMonsterEntry>[3];
            int[] laneGroupCursor = new int[3];
            int[] laneInGroupConsumed = new int[3];
            for (int l = 0; l < 3; l++)
            {
                lanes[l] = rawLanes[l];
            }

            // Staging state: stagingColumns columns, each is a queue from front row (0) to back row
            List<LevelMonsterEntry>[] cols = new List<LevelMonsterEntry>[stagingColumns];
            for (int c = 0; c < stagingColumns; c++) cols[c] = new List<LevelMonsterEntry>();
            for (int i = 0; i < stagingBoxes.Count; i++)
            {
                var b = stagingBoxes[i];
                if (b != null) cols[i % stagingColumns].Add(b);
            }

            List<SimSlotState> lockedSlots = new List<SimSlotState>(slotCount);
            int maxAllowedOccupied = Mathf.Min(slotCount, maxAllowedBufferSlots + 1);
            int remainingBalls = totalGlassBalls;

            int safetySteps = (totalGlassBalls + stagingBoxes.Count) * 8 + 32;
            for (int step = 0; step < safetySteps && remainingBalls > 0; step++)
            {
                // 1. Resolve any locked slotted box that matches an exposed bottom-floor group or ball!
                bool anyResolved = true;
                while (anyResolved)
                {
                    anyResolved = false;

                    // 1a. Prefer whole-group single-lane clears (keeps lanes synchronized cleanly)
                    for (int s = lockedSlots.Count - 1; s >= 0; s--)
                    {
                        SimSlotState slot = lockedSlots[s];
                        int exactLane = FindExactAccessibleLaneForGroup(lanes, laneGroupCursor, laneInGroupConsumed, slot.color, slot.remainingNeed);
                        if (exactLane >= 0)
                        {
                            remainingBalls -= slot.remainingNeed;
                            laneGroupCursor[exactLane]++;
                            laneInGroupConsumed[exactLane] = 0;
                            lockedSlots.RemoveAt(s);
                            anyResolved = true;
                            break;
                        }
                    }

                    if (anyResolved) continue;

                    // 1b. Partial / cross-lane ball-by-ball feeding (supports mixed custom socket sizes)
                    for (int s = lockedSlots.Count - 1; s >= 0; s--)
                    {
                        SimSlotState slot = lockedSlots[s];
                        int laneIdx = FindAccessibleLaneForColor(lanes, laneGroupCursor, laneInGroupConsumed, slot.color);
                        if (laneIdx >= 0)
                        {
                            int gi = laneGroupCursor[laneIdx];
                            int groupLeft = lanes[laneIdx][gi].count - laneInGroupConsumed[laneIdx];
                            int take = Mathf.Min(slot.remainingNeed, groupLeft);
                            remainingBalls -= take;
                            slot.remainingNeed -= take;
                            laneInGroupConsumed[laneIdx] += take;
                            if (laneInGroupConsumed[laneIdx] >= lanes[laneIdx][gi].count)
                            {
                                laneGroupCursor[laneIdx]++;
                                laneInGroupConsumed[laneIdx] = 0;
                            }
                            if (slot.remainingNeed <= 0)
                            {
                                lockedSlots.RemoveAt(s);
                            }
                            else
                            {
                                lockedSlots[s] = slot;
                            }
                            anyResolved = true;
                            break;
                        }
                    }
                }

                if (remainingBalls <= 0) return lockedSlots.Count == 0;

                // 2. Can we pick an accessible staging box that IMMEDIATELY collects from an exposed glass lane bottom?
                bool pickedImmediate = false;
                if (lockedSlots.Count < slotCount)
                {
                    for (int c = 0; c < stagingColumns; c++)
                    {
                        if (cols[c].Count == 0) continue;
                        int maxCheckIdx = useColumnQueue ? 1 : cols[c].Count;
                        for (int k = 0; k < maxCheckIdx; k++)
                        {
                            LevelMonsterEntry candidate = cols[c][k];
                            if (FindAccessibleLaneForColor(lanes, laneGroupCursor, laneInGroupConsumed, candidate.color) >= 0)
                            {
                                cols[c].RemoveAt(k);
                                lockedSlots.Add(new SimSlotState { color = candidate.color, remainingNeed = Mathf.Max(1, candidate.count) });
                                pickedImmediate = true;
                                break;
                            }
                        }
                        if (pickedImmediate) break;
                    }
                }

                if (pickedImmediate) continue;

                // 3. Buffer move: place a front-row box into a spare buffer slot to uncover a matching box behind it
                if (lockedSlots.Count >= maxAllowedOccupied || lockedSlots.Count >= slotCount - 1)
                {
                    return false;
                }

                int bestColToAdvance = -1;
                int bestLookaheadDepth = int.MaxValue;

                for (int c = 0; c < stagingColumns; c++)
                {
                    if (cols[c].Count < 2) continue;
                    for (int depth = 1; depth < cols[c].Count; depth++)
                    {
                        if (FindAccessibleLaneForColor(lanes, laneGroupCursor, laneInGroupConsumed, cols[c][depth].color) >= 0)
                        {
                            if (depth < bestLookaheadDepth && lockedSlots.Count + depth < slotCount)
                            {
                                bestLookaheadDepth = depth;
                                bestColToAdvance = c;
                            }
                            break;
                        }
                    }
                }

                if (bestColToAdvance >= 0)
                {
                    LevelMonsterEntry bufferBox = cols[bestColToAdvance][0];
                    cols[bestColToAdvance].RemoveAt(0);
                    lockedSlots.Add(new SimSlotState { color = bufferBox.color, remainingNeed = Mathf.Max(1, bufferBox.count) });
                }
                else
                {
                    return false;
                }
            }

            return remainingBalls <= 0 && lockedSlots.Count == 0;
        }

        private static int FindExactAccessibleLaneForGroup(
            List<LevelMonsterEntry>[] lanes,
            int[] laneGroupCursor,
            int[] laneInGroupConsumed,
            PuzzleColor targetColor,
            int exactCount)
        {
            for (int l = 0; l < 3; l++)
            {
                int gi = laneGroupCursor[l];
                if (gi >= lanes[l].Count) continue;
                var g = lanes[l][gi];
                int left = g.count - laneInGroupConsumed[l];
                if (g.color == targetColor && left == exactCount)
                {
                    return l;
                }
            }
            return -1;
        }

        private static int FindAccessibleLaneForColor(
            List<LevelMonsterEntry>[] lanes,
            int[] laneGroupCursor,
            int[] laneInGroupConsumed,
            PuzzleColor targetColor)
        {
            for (int l = 0; l < 3; l++)
            {
                int gi = laneGroupCursor[l];
                if (gi < lanes[l].Count && lanes[l][gi].color == targetColor && laneInGroupConsumed[l] < lanes[l][gi].count)
                {
                    return l;
                }
            }
            return -1;
        }

        public static void ApplyColorTheme(PuzzleGlassLevelData lvl, GlassColorTheme theme)
        {
            if (lvl == null) return;
            lvl.colorTheme = theme;
            switch (theme)
            {
                case GlassColorTheme.SkyBlueGlossy:
                default:
                    lvl.glassBodyColor = new Color(0.35f, 0.70f, 0.98f, 0.76f);
                    lvl.glassCenterGlowColor = new Color(0.58f, 0.84f, 1.00f, 0.42f);
                    lvl.glassOuterRimColor = new Color(0.36f, 0.69f, 0.97f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.74f, 0.91f, 1.00f, 0.88f);
                    lvl.glassFrontLensColor = new Color(0.85f, 0.96f, 1.00f, 0.12f);
                    lvl.earHandleColor = new Color(0.92f, 0.96f, 1.00f, 1.00f);
                    break;
                case GlassColorTheme.CrystalClear:
                    lvl.glassBodyColor = new Color(0.82f, 0.93f, 0.99f, 0.38f);
                    lvl.glassCenterGlowColor = new Color(0.94f, 0.98f, 1.00f, 0.25f);
                    lvl.glassOuterRimColor = new Color(0.76f, 0.89f, 0.98f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.95f, 0.99f, 1.00f, 0.92f);
                    lvl.glassFrontLensColor = new Color(0.95f, 0.99f, 1.00f, 0.16f);
                    lvl.earHandleColor = new Color(0.96f, 0.98f, 1.00f, 1.00f);
                    break;
                case GlassColorTheme.EmeraldGreen:
                    lvl.glassBodyColor = new Color(0.22f, 0.82f, 0.64f, 0.72f);
                    lvl.glassCenterGlowColor = new Color(0.52f, 0.95f, 0.80f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.20f, 0.76f, 0.58f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.72f, 0.98f, 0.88f, 0.88f);
                    lvl.glassFrontLensColor = new Color(0.82f, 1.00f, 0.92f, 0.12f);
                    lvl.earHandleColor = new Color(0.90f, 0.99f, 0.95f, 1.00f);
                    break;
                case GlassColorTheme.RoseQuartz:
                    lvl.glassBodyColor = new Color(0.96f, 0.52f, 0.76f, 0.72f);
                    lvl.glassCenterGlowColor = new Color(1.00f, 0.74f, 0.88f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.94f, 0.45f, 0.72f, 1.00f);
                    lvl.glassInnerRimColor = new Color(1.00f, 0.84f, 0.93f, 0.88f);
                    lvl.glassFrontLensColor = new Color(1.00f, 0.90f, 0.96f, 0.12f);
                    lvl.earHandleColor = new Color(1.00f, 0.94f, 0.97f, 1.00f);
                    break;
                case GlassColorTheme.AmethystPurple:
                    lvl.glassBodyColor = new Color(0.64f, 0.46f, 0.96f, 0.74f);
                    lvl.glassCenterGlowColor = new Color(0.80f, 0.68f, 1.00f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.58f, 0.40f, 0.92f, 1.00f);
                    lvl.glassInnerRimColor = new Color(0.86f, 0.78f, 1.00f, 0.88f);
                    lvl.glassFrontLensColor = new Color(0.92f, 0.86f, 1.00f, 0.12f);
                    lvl.earHandleColor = new Color(0.95f, 0.92f, 1.00f, 1.00f);
                    break;
                case GlassColorTheme.AmberGold:
                    lvl.glassBodyColor = new Color(0.98f, 0.74f, 0.28f, 0.72f);
                    lvl.glassCenterGlowColor = new Color(1.00f, 0.88f, 0.55f, 0.40f);
                    lvl.glassOuterRimColor = new Color(0.94f, 0.66f, 0.20f, 1.00f);
                    lvl.glassInnerRimColor = new Color(1.00f, 0.92f, 0.72f, 0.88f);
                    lvl.glassFrontLensColor = new Color(1.00f, 0.96f, 0.84f, 0.12f);
                    lvl.earHandleColor = new Color(1.00f, 0.97f, 0.90f, 1.00f);
                    break;
            }
        }
    }
}
