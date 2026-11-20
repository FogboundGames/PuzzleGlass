using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Tüm oyun mekaniklerini (Anahtar+Kilit, Buz, Üst Besleme Cam Tüpleri, Tepsi Bölmesi, Farklı Cam Şekilleri,
    /// Polyomino Bloklar ve Öğretici El İpucu) kullanıcının belirlediği oranlara ve kısıtlara göre birleştirip
    /// PuzzleLevelSolver ile %100 çözülebilirliğini doğrulayarak level üretir.
    /// </summary>
    public static class FullMechanicsLevelGenerator
    {
        public enum GlassShapeFilter
        {
            All = 0,
            BottlesOnly = 1,
            BoxesOnly = 2,
            CustomPoolsOnly = 3
        }

        public enum IceMeltPreference
        {
            Mixed = 0,
            PerBallCollected = 1,
            PerBoxCompleted = 2
        }

        public struct Options
        {
            // Mekanik oranları (%0 = tamamen kapalı, %100 = kesin dahil, %1-%99 = olasılık / toplu üretim kotası)
            public int keyLockChancePct;
            public int maxKeyPairs;

            public int iceChancePct;
            public int maxIceBlocks;
            public int iceRatioPct;
            public IceMeltPreference iceMeltPreference;

            public int feedTubeChancePct;
            public int feedTubeHoldRatioPct;
            public int feedTubeBothSidesPct;

            public int trayWallChancePct;
            public int maxTrayWalls;

            public int variedGlassChancePct;
            public GlassShapeFilter glassShapeFilter;

            public bool addTutorialHint;

            // Tepsi & Blok hedefleri (0 = zorluğa göre otomatik)
            public int customGridCols;
            public int customGridRows;
            public int customBoxCount;
            public int customColorCount;
            public int customSlotCount;

            // Geriye uyumlu kolaylık özellikleri
            public bool useKeysAndLocks => keyLockChancePct > 0;
            public bool useIce => iceChancePct > 0;
            public bool useFeedTubes => feedTubeChancePct > 0;
            public bool useTrayWalls => trayWallChancePct > 0;
            public bool varyGlassShape => variedGlassChancePct > 0;

            public static Options All => Balanced;

            public static Options Balanced => new Options
            {
                keyLockChancePct = 75,
                maxKeyPairs = 2,
                iceChancePct = 70,
                maxIceBlocks = 2,
                iceRatioPct = 60,
                iceMeltPreference = IceMeltPreference.Mixed,
                feedTubeChancePct = 75,
                feedTubeHoldRatioPct = 42,
                feedTubeBothSidesPct = 45,
                trayWallChancePct = 55,
                maxTrayWalls = 2,
                variedGlassChancePct = 70,
                glassShapeFilter = GlassShapeFilter.All,
                addTutorialHint = true,
                customGridCols = 0,
                customGridRows = 0,
                customBoxCount = 0,
                customColorCount = 0,
                customSlotCount = 0
            };

            public static Options HeavyMechanics => new Options
            {
                keyLockChancePct = 100,
                maxKeyPairs = 2,
                iceChancePct = 100,
                maxIceBlocks = 3,
                iceRatioPct = 75,
                iceMeltPreference = IceMeltPreference.Mixed,
                feedTubeChancePct = 100,
                feedTubeHoldRatioPct = 48,
                feedTubeBothSidesPct = 65,
                trayWallChancePct = 85,
                maxTrayWalls = 3,
                variedGlassChancePct = 90,
                glassShapeFilter = GlassShapeFilter.All,
                addTutorialHint = true,
                customGridCols = 0,
                customGridRows = 0,
                customBoxCount = 0,
                customColorCount = 0,
                customSlotCount = 0
            };

            public static Options ClassicLight => new Options
            {
                keyLockChancePct = 40,
                maxKeyPairs = 1,
                iceChancePct = 30,
                maxIceBlocks = 1,
                iceRatioPct = 45,
                iceMeltPreference = IceMeltPreference.PerBallCollected,
                feedTubeChancePct = 40,
                feedTubeHoldRatioPct = 35,
                feedTubeBothSidesPct = 25,
                trayWallChancePct = 0,
                maxTrayWalls = 1,
                variedGlassChancePct = 0,
                glassShapeFilter = GlassShapeFilter.All,
                addTutorialHint = true,
                customGridCols = 0,
                customGridRows = 0,
                customBoxCount = 0,
                customColorCount = 0,
                customSlotCount = 0
            };
        }

        private struct ActiveFlags
        {
            public bool useKeysAndLocks;
            public bool useIce;
            public bool useFeedTubes;
            public bool useTrayWalls;
            public bool varyGlassShape;
        }

        private enum GlassTemplateKind
        {
            AutoBottle,
            CompactBottle,
            WideBox,
            CapsuleBox,
            TallBottle,
            Hexagon,
            Trapezoid,
            OvalFlask
        }

        /// <summary>
        /// Toplu üretim için verilen yüzde oranlarını tam adet kotasına çevirip karıştırılmış bir plan üretir.
        /// Örneğin %60 oran ve 5 level için tam 3 levelde ilgili mekanik hedeflenir.
        /// </summary>
        public static Options[] PlanBatch(Options baseOpt, int count, int seed = 0)
        {
            count = Mathf.Max(1, count);
            var rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            bool[] keysPlan = BuildQuotaArray(count, baseOpt.keyLockChancePct, rng);
            bool[] icePlan = BuildQuotaArray(count, baseOpt.iceChancePct, rng);
            bool[] tubesPlan = BuildQuotaArray(count, baseOpt.feedTubeChancePct, rng);
            bool[] wallsPlan = BuildQuotaArray(count, baseOpt.trayWallChancePct, rng);
            bool[] glassPlan = BuildQuotaArray(count, baseOpt.variedGlassChancePct, rng);

            var list = new Options[count];
            for (int i = 0; i < count; i++)
            {
                var o = baseOpt;
                o.keyLockChancePct = keysPlan[i] ? 100 : 0;
                o.iceChancePct = icePlan[i] ? 100 : 0;
                o.feedTubeChancePct = tubesPlan[i] ? 100 : 0;
                o.trayWallChancePct = wallsPlan[i] ? 100 : 0;
                o.variedGlassChancePct = glassPlan[i] ? 100 : 0;
                list[i] = o;
            }
            return list;
        }

        private static bool[] BuildQuotaArray(int count, int pct, System.Random rng)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            int target = Mathf.RoundToInt(count * (pct / 100f));
            var arr = new bool[count];
            for (int i = 0; i < target && i < count; i++) arr[i] = true;
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        /// <summary>
        /// Verilen leveli seçili oran ve kısıtlarla doldurur; tüm mekanikler eklendikten sonra
        /// Wave 1 + Wave 2 dahil PuzzleLevelSolver ile %100 çözülebilirliği doğrulanır.
        /// </summary>
        public static SolveResult Generate(PuzzleGlassLevelData lvl, LevelDifficulty diff, Options opt, int seed = 0, int tries = 10)
        {
            if (lvl == null) return null;
            PuzzleLevelSolver.GetTargetDrags(diff, out int lo, out int hi);
            var rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);

            var flags = new ActiveFlags
            {
                useKeysAndLocks = RollChance(opt.keyLockChancePct, rng),
                useIce = RollChance(opt.iceChancePct, rng),
                useFeedTubes = RollChance(opt.feedTubeChancePct, rng),
                useTrayWalls = RollChance(opt.trayWallChancePct, rng),
                varyGlassShape = RollChance(opt.variedGlassChancePct, rng)
            };

            var keep = ScriptableObject.CreateInstance<PuzzleGlassLevelData>();
            UnityEditor.EditorUtility.CopySerialized(lvl, keep);

            int bestSeed = 0;
            int bestScore = int.MinValue;
            SolveResult bestSolve = null;
            var bestSnapshot = ScriptableObject.CreateInstance<PuzzleGlassLevelData>();
            int expectedCount = ExpectedMechanicsCount(flags);

            for (int t = 0; t < tries; t++)
            {
                int candidateSeed = rng.Next(1, int.MaxValue);
                if (!TryBuildCandidate(lvl, diff, opt, flags, candidateSeed, keep, out var solve)) continue;

                int drags = solve.Drags;
                int dist = drags < lo ? lo - drags : (drags > hi ? drags - hi : 0);
                int mechCount = CountMatchedMechanics(lvl, flags);
                int score = mechCount * 120 - dist * 20;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestSeed = candidateSeed;
                    bestSolve = solve;
                    UnityEditor.EditorUtility.CopySerialized(lvl, bestSnapshot);
                    if (dist == 0 && mechCount >= expectedCount) break;
                }
            }

            if (bestSolve != null && bestSeed != 0)
            {
                UnityEditor.EditorUtility.CopySerialized(bestSnapshot, lvl);
            }
            else
            {
                // Hiç tam aday tutmazsa seçili tepsi/blok ayarlarıyla temiz bir baz kur
                UnityEditor.EditorUtility.CopySerialized(keep, lvl);
                ApplyBaseLayout(lvl, diff, opt, rng.Next(1, int.MaxValue));
                var dims = GlassShapeData.GetDims(lvl);
                bestSolve = PuzzleLevelSolver.Solve(lvl, dims, 6000);
                if (bestSolve == null || bestSolve.status != SolveStatus.Solved)
                {
                    bestSolve = PuzzleLevelSolver.GenerateForTarget(lvl, diff, tries: 8, seed: rng.Next(1, int.MaxValue));
                }
            }

            Object.DestroyImmediate(keep);
            Object.DestroyImmediate(bestSnapshot);
            return bestSolve;
        }

        private static bool RollChance(int pct, System.Random rng)
        {
            if (pct <= 0) return false;
            if (pct >= 100) return true;
            return rng.Next(0, 100) < pct;
        }

        private static int ExpectedMechanicsCount(ActiveFlags f)
        {
            int k = 0;
            if (f.useKeysAndLocks) k++;
            if (f.useIce) k++;
            if (f.useFeedTubes) k++;
            if (f.useTrayWalls) k++;
            return k;
        }

        private static int CountMatchedMechanics(PuzzleGlassLevelData lvl, ActiveFlags f)
        {
            bool hasKey = false, hasLock = false, hasIce = false;
            if (lvl.monsters != null)
            {
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    if (m.hasKey) hasKey = true;
                    if (m.lockKeyCount > 0) hasLock = true;
                    if (m.iceCount > 0) hasIce = true;
                }
            }
            int c = 0;
            if (f.useKeysAndLocks && hasKey && hasLock) c++;
            if (f.useIce && hasIce) c++;
            if (f.useFeedTubes && lvl.useBallFeedTubes) c++;
            if (f.useTrayWalls && lvl.gridWalls != null && lvl.gridWalls.Count > 0) c++;
            return c;
        }

        private static void ApplyBaseLayout(PuzzleGlassLevelData lvl, LevelDifficulty diff, Options opt, int seed)
        {
            int defaultBoxes = diff == LevelDifficulty.Easy ? 5 : (diff == LevelDifficulty.Medium ? 7 : (diff == LevelDifficulty.Hard ? 9 : 10));
            int? boxArg = opt.customBoxCount > 0 ? Mathf.Clamp(opt.customBoxCount, 3, 18) : defaultBoxes;
            int? colorArg = opt.customColorCount > 0 ? Mathf.Clamp(opt.customColorCount, 2, 10) : (int?)null;
            int? slotArg = opt.customSlotCount > 0 ? Mathf.Clamp(opt.customSlotCount, 2, 5) : (int?)null;
            int? colsArg = opt.customGridCols > 0 ? Mathf.Clamp(opt.customGridCols, 4, 7) : (int?)null;
            int? rowsArg = opt.customGridRows > 0 ? Mathf.Clamp(opt.customGridRows, 4, 7) : (int?)null;

            PuzzleLevelGenerator.GenerateForLevelData(
                lvl,
                customColorCount: colorArg,
                customTotalBoxes: boxArg,
                customSlotCount: slotArg,
                randomSeed: seed,
                customGridCols: colsArg,
                customGridRows: rowsArg);
        }

        private static bool TryBuildCandidate(
            PuzzleGlassLevelData lvl,
            LevelDifficulty diff,
            Options opt,
            ActiveFlags flags,
            int seed,
            PuzzleGlassLevelData fallbackGlassFrom,
            out SolveResult finalSolve)
        {
            finalSolve = null;
            var rng = new System.Random(seed);

            // 1. Baz çözülebilir tepsi + top dizilimi
            lvl.difficulty = diff;
            lvl.useManualBallLayout = false;
            lvl.manualLaneWidth = 0;
            lvl.manualBalls?.Clear();
            lvl.useBallFeedTubes = false;
            lvl.tutorialHint = false;
            lvl.tutorialBlockIndex = -1;
            if (lvl.gridWalls == null) lvl.gridWalls = new List<Vector2Int>();
            lvl.gridWalls.Clear();

            ApplyBaseLayout(lvl, diff, opt, seed);

            // Kullanıcı sabit tepsi ölçüsü seçmediyse ve bölme isteniyorsa gerektiğinde ferahlat
            if (opt.customGridCols == 0 && opt.customGridRows == 0 &&
                flags.useTrayWalls && (diff == LevelDifficulty.Hard || diff == LevelDifficulty.Boss || seed % 2 == 0))
            {
                lvl.stagingGridCols = Mathf.Clamp(lvl.stagingGridCols + (diff == LevelDifficulty.Boss ? 1 : 0), 5, 7);
                lvl.stagingGridRows = Mathf.Clamp(lvl.stagingGridRows + 1, 5, 7);
            }

            // 2. Cam şekli & teması
            if (flags.varyGlassShape)
            {
                ApplyVariedGlass(lvl, opt.glassShapeFilter, rng);
            }
            else if (fallbackGlassFrom != null && (fallbackGlassFrom.monsters != null && fallbackGlassFrom.monsters.Count > 0 || fallbackGlassFrom.useCustomGlassShape))
            {
                CopyGlassOnly(fallbackGlassFrom, lvl);
            }

            // 3. Üst Besleme Cam Tüpleri (Wave 1 -> Wave 2)
            if (flags.useFeedTubes && lvl.bottomToTopGroups != null && lvl.bottomToTopGroups.Count >= 3)
            {
                ConfigureFeedTubes(lvl, opt, rng);
            }

            var dims = GlassShapeData.GetDims(lvl);
            if (flags.useTrayWalls)
            {
                TryPlaceTrayWalls(lvl, dims, Mathf.Clamp(opt.maxTrayWalls, 1, 4), rng);
            }

            var baseSolve = PuzzleLevelSolver.Solve(lvl, dims, 6000);
            if ((baseSolve == null || baseSolve.status != SolveStatus.Solved) && lvl.useBallFeedTubes)
            {
                lvl.feedTubeMode = BallFeedTubeMode.BothLeftAndRight;
                baseSolve = PuzzleLevelSolver.Solve(lvl, dims, 6000);
            }
            if ((baseSolve == null || baseSolve.status != SolveStatus.Solved) && lvl.useBallFeedTubes)
            {
                lvl.useBallFeedTubes = false;
                dims = GlassShapeData.GetDims(lvl);
                baseSolve = PuzzleLevelSolver.Solve(lvl, dims, 6000);
            }
            if (baseSolve == null || baseSolve.status != SolveStatus.Solved) return false;

            // 4. Çözüm sırasını analiz ederek Anahtar+Kilit ve Buz ekle
            var analysis = PuzzleLevelSolver.AnalyzePlaythrough(lvl, dims, baseSolve);
            var order = analysis.completionOrder;
            if (order != null && order.Count >= 3 && lvl.monsters != null)
            {
                var usedSpecial = new HashSet<int>();

                if (flags.useKeysAndLocks)
                {
                    // Kilitli blok: mümkünse ikinci yarıdan veya Wave 1'den seç
                    int lockOrderPos = -1;
                    int lockIdx = -1;
                    for (int k = order.Count - 1; k >= Mathf.Max(2, order.Count / 2); k--)
                    {
                        int cand = order[k];
                        if (!lvl.useBallFeedTubes || analysis.waveAtFinish[cand] == 1)
                        {
                            lockIdx = cand;
                            lockOrderPos = k;
                            break;
                        }
                    }
                    if (lockIdx < 0)
                    {
                        lockOrderPos = order.Count - 1;
                        lockIdx = order[lockOrderPos];
                    }

                    int desiredKeys = Mathf.Clamp(opt.maxKeyPairs, 1, Mathf.Max(1, Mathf.Min(3, lockOrderPos)));
                    int assignedKeys = 0;
                    for (int k = 0; k < lockOrderPos && assignedKeys < desiredKeys; k++)
                    {
                        int keyIdx = order[k];
                        if (keyIdx == lockIdx || usedSpecial.Contains(keyIdx)) continue;
                        lvl.monsters[keyIdx].hasKey = true;
                        usedSpecial.Add(keyIdx);
                        assignedKeys++;
                    }
                    if (assignedKeys > 0)
                    {
                        lvl.monsters[lockIdx].lockKeyCount = assignedKeys;
                        usedSpecial.Add(lockIdx);
                    }
                }

                if (flags.useIce)
                {
                    lvl.iceMeltMode = opt.iceMeltPreference switch
                    {
                        IceMeltPreference.PerBallCollected => IceMeltMode.PerBallCollected,
                        IceMeltPreference.PerBoxCompleted => IceMeltMode.PerBoxCompleted,
                        _ => (seed % 3 == 0) ? IceMeltMode.PerBoxCompleted : IceMeltMode.PerBallCollected
                    };

                    int iceCountTarget = Mathf.Clamp(opt.maxIceBlocks, 1, 3);
                    float iceDensity = Mathf.Clamp(opt.iceRatioPct, 25, 90) / 100f;
                    int addedIce = 0;
                    for (int k = 2; k < order.Count && addedIce < iceCountTarget; k++)
                    {
                        int bIdx = order[k];
                        if (usedSpecial.Contains(bIdx)) continue;
                        if (lvl.iceMeltMode == IceMeltMode.PerBallCollected)
                        {
                            int safeBalls = analysis.ballsCollectedBeforeFinish[bIdx] - 1;
                            if (safeBalls < 2) continue;
                            int cnt = Mathf.Clamp(Mathf.RoundToInt(safeBalls * iceDensity), 2, Mathf.Min(safeBalls, 15));
                            lvl.monsters[bIdx].iceCount = cnt;
                        }
                        else
                        {
                            int safeBoxes = analysis.boxesCompletedBeforeFinish[bIdx];
                            if (safeBoxes < 1) continue;
                            int cnt = Mathf.Clamp(Mathf.RoundToInt(safeBoxes * iceDensity), 1, Mathf.Min(safeBoxes, 5));
                            lvl.monsters[bIdx].iceCount = cnt;
                        }
                        usedSpecial.Add(bIdx);
                        addedIce++;
                    }
                }
            }

            // 5. Tüm mekanikler yerindeyken nihai çözücü doğrulaması
            finalSolve = PuzzleLevelSolver.Solve(lvl, dims, 8000);
            if (finalSolve == null || finalSolve.status != SolveStatus.Solved) return false;

            // 6. İlk hamle ipucu
            if (opt.addTutorialHint && finalSolve.TryGetFirstDrag(out int tutBlock, out Vector2Int offset) &&
                tutBlock >= 0 && tutBlock < lvl.monsters.Count)
            {
                lvl.tutorialHint = true;
                lvl.tutorialBlockIndex = tutBlock;
                lvl.tutorialFromAnchor = lvl.monsters[tutBlock].gridAnchor;
                lvl.tutorialTargetAnchor = lvl.monsters[tutBlock].gridAnchor + offset;
            }

            // 7. Açıklayıcı isim etiketi
            string tags = BuildMechanicTags(lvl);
            string diffTag = PuzzleLevelSolver.DifficultyName(diff);
            lvl.levelName = $"Level {lvl.levelNumber} - {diffTag} ({lvl.stagingGridCols}x{lvl.stagingGridRows}{tags})";
            return true;
        }

        private static string BuildMechanicTags(PuzzleGlassLevelData lvl)
        {
            bool hasKey = false, hasLock = false, hasIce = false;
            if (lvl.monsters != null)
            {
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    if (m.hasKey) hasKey = true;
                    if (m.lockKeyCount > 0) hasLock = true;
                    if (m.iceCount > 0) hasIce = true;
                }
            }
            string s = "";
            if (hasKey || hasLock) s += " 🔑🔒";
            if (hasIce) s += " ❄";
            if (lvl.useBallFeedTubes) s += " 🧪";
            if (lvl.gridWalls != null && lvl.gridWalls.Count > 0) s += " 🧱";
            return string.IsNullOrEmpty(s) ? "" : " ·" + s.TrimStart();
        }

        private static void TryPlaceTrayWalls(PuzzleGlassLevelData lvl, PuzzleLevelSolver.GlassDims dims, int maxWallCells, System.Random rng)
        {
            if (lvl == null || lvl.monsters == null) return;
            int cols = lvl.stagingGridCols, rows = lvl.stagingGridRows;
            var occupied = new bool[cols, rows];
            foreach (var m in lvl.monsters)
            {
                if (m == null) continue;
                foreach (var c in CarrierBlockShapeUtility.GetCells(m.blockShape))
                {
                    int x = m.gridAnchor.x + c.x, y = m.gridAnchor.y + c.y;
                    if (x >= 0 && y >= 0 && x < cols && y < rows) occupied[x, y] = true;
                }
            }

            var free = new List<Vector2Int>();
            for (int y = 0; y < Mathf.Max(1, rows - 1); y++)
                for (int x = 0; x < cols; x++)
                    if (!occupied[x, y]) free.Add(new Vector2Int(x, y));

            if (free.Count < 4) return; // kaydırma için yeterli boşluk kalsın

            int midX = cols / 2;
            free.Sort((a, b) => (Mathf.Abs(a.x - midX) + a.y).CompareTo(Mathf.Abs(b.x - midX) + b.y));
            int targetWalls = Mathf.Min(maxWallCells, free.Count - 3);
            if (targetWalls <= 0) return;

            // İlk bölme hücresini bul, ardından istenen maxWallCells adedine kadar güvenli hücre ekle
            for (int start = 0; start < Mathf.Min(free.Count, 5); start++)
            {
                lvl.gridWalls.Clear();
                lvl.gridWalls.Add(free[start]);
                var testSolve = PuzzleLevelSolver.Solve(lvl, dims, 4000);
                if (testSolve == null || testSolve.status != SolveStatus.Solved) continue;

                // Hedeflenen ek duvarları dene (tercihen bitişik dikey sütun)
                var added = new HashSet<Vector2Int> { free[start] };
                for (int step = 1; step < targetWalls; step++)
                {
                    Vector2Int prev = lvl.gridWalls[lvl.gridWalls.Count - 1];
                    Vector2Int? nextCand = null;
                    Vector2Int up = new Vector2Int(prev.x, prev.y + 1);
                    if (up.y < rows - 1 && !occupied[up.x, up.y] && !added.Contains(up) && rng.Next(0, 3) != 0)
                    {
                        nextCand = up;
                    }
                    else
                    {
                        for (int idx = 0; idx < free.Count; idx++)
                        {
                            if (!added.Contains(free[idx])) { nextCand = free[idx]; break; }
                        }
                    }
                    if (!nextCand.HasValue || free.Count - (added.Count + 1) < 3) break;

                    lvl.gridWalls.Add(nextCand.Value);
                    var multiSolve = PuzzleLevelSolver.Solve(lvl, dims, 3500);
                    if (multiSolve != null && multiSolve.status == SolveStatus.Solved)
                    {
                        added.Add(nextCand.Value);
                    }
                    else
                    {
                        lvl.gridWalls.RemoveAt(lvl.gridWalls.Count - 1);
                    }
                }
                return;
            }
            lvl.gridWalls.Clear();
        }

        private static void ConfigureFeedTubes(PuzzleGlassLevelData lvl, Options opt, System.Random rng)
        {
            int total = lvl.GetGlassStartBallCount();
            if (total < 10) return;

            bool both = RollChance(opt.feedTubeBothSidesPct, rng);
            lvl.feedTubeMode = both
                ? BallFeedTubeMode.BothLeftAndRight
                : (rng.Next(0, 2) == 0 ? BallFeedTubeMode.LeftOnly : BallFeedTubeMode.RightOnly);

            float holdRatio = Mathf.Clamp(opt.feedTubeHoldRatioPct, 20, 70) / 100f;
            int targetInChamber = Mathf.RoundToInt(total * (1f - holdRatio));
            int acc = 0;
            int bestLimit = Mathf.Clamp(targetInChamber, 6, total - 4);
            int bestDiff = int.MaxValue;
            for (int i = 0; i < lvl.bottomToTopGroups.Count - 1; i++)
            {
                var g = lvl.bottomToTopGroups[i];
                if (g == null) continue;
                acc += Mathf.Max(1, g.count);
                if (acc >= 6 && total - acc >= 4)
                {
                    int d = Mathf.Abs(acc - targetInChamber);
                    if (d < bestDiff) { bestDiff = d; bestLimit = acc; }
                }
            }

            lvl.useBallFeedTubes = true;
            lvl.feedTubeContainerLimit = bestLimit;
            lvl.feedTubeQueueVisibleCount = 5;
            lvl.feedTubeWidthMultiplier = 1.24f;
            lvl.feedTubeAnchorSpread = 0.68f;
            lvl.feedTubeDropInterval = 0.14f;
        }

        private static void ApplyVariedGlass(PuzzleGlassLevelData lvl, GlassShapeFilter filter, System.Random rng)
        {
            GlassTemplateKind[] kinds = filter switch
            {
                GlassShapeFilter.BottlesOnly => new[]
                {
                    GlassTemplateKind.AutoBottle,
                    GlassTemplateKind.CompactBottle,
                    GlassTemplateKind.TallBottle
                },
                GlassShapeFilter.BoxesOnly => new[]
                {
                    GlassTemplateKind.WideBox,
                    GlassTemplateKind.CapsuleBox
                },
                GlassShapeFilter.CustomPoolsOnly => new[]
                {
                    GlassTemplateKind.Hexagon,
                    GlassTemplateKind.Trapezoid,
                    GlassTemplateKind.OvalFlask
                },
                _ => new[]
                {
                    GlassTemplateKind.AutoBottle,
                    GlassTemplateKind.CompactBottle,
                    GlassTemplateKind.WideBox,
                    GlassTemplateKind.CapsuleBox,
                    GlassTemplateKind.TallBottle,
                    GlassTemplateKind.Hexagon,
                    GlassTemplateKind.Trapezoid,
                    GlassTemplateKind.OvalFlask
                }
            };
            var kind = kinds[Mathf.Abs(lvl.levelNumber + rng.Next(0, kinds.Length)) % kinds.Length];

            var themes = (GlassColorTheme[])System.Enum.GetValues(typeof(GlassColorTheme));
            var theme = themes[rng.Next(0, Mathf.Max(1, themes.Length - 1))]; // CustomTheme hariç
            PuzzleLevelGenerator.ApplyColorTheme(lvl, theme);

            lvl.symmetricMode = true;
            lvl.widthScale = 1f;
            lvl.heightScale = 1f;
            lvl.showShoulderHandles = false;
            lvl.boxFrameThickness = 0.055f;

            switch (kind)
            {
                case GlassTemplateKind.AutoBottle:
                    lvl.useCustomGlassShape = false;
                    lvl.glassIsBottle = false;
                    lvl.boxCornerRadius = 0.22f;
                    break;
                case GlassTemplateKind.CompactBottle:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = true;
                    lvl.glassPreset = GlassShapePreset.CustomFreeform;
                    lvl.contourPoints = ShapedGlassContainer.BuildBottleContour(2.70f, 0.72f);
                    lvl.boxCornerRadius = 0.22f;
                    break;
                case GlassTemplateKind.WideBox:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = false;
                    lvl.glassPreset = GlassShapePreset.RectangleBox;
                    lvl.contourPoints = GlassShapeData.BuildBoxContour(2.86f, 1.12f, GlassShapeData.DefaultBottomY);
                    lvl.boxCornerRadius = 0.22f;
                    break;
                case GlassTemplateKind.CapsuleBox:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = false;
                    lvl.glassPreset = GlassShapePreset.RectangleBox;
                    lvl.contourPoints = GlassShapeData.BuildBoxContour(2.48f, 1.28f, GlassShapeData.DefaultBottomY);
                    lvl.boxCornerRadius = 0.56f;
                    break;
                case GlassTemplateKind.TallBottle:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = true;
                    lvl.glassPreset = GlassShapePreset.CustomFreeform;
                    lvl.contourPoints = ShapedGlassContainer.BuildBottleContour(2.38f, 1.56f);
                    lvl.boxCornerRadius = 0.26f;
                    break;
                case GlassTemplateKind.Hexagon:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = false;
                    lvl.glassPreset = GlassShapePreset.CustomFreeform;
                    lvl.contourPoints = new List<GlassContourPoint>
                    {
                        new GlassContourPoint(-0.56f, 0.82f, 0f, 0.3f),
                        new GlassContourPoint( 0.06f, 1.32f, 0f, 0f),
                        new GlassContourPoint( 0.68f, 0.82f, 0f, 0.3f)
                    };
                    lvl.boxCornerRadius = 0.20f;
                    break;
                case GlassTemplateKind.Trapezoid:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = false;
                    lvl.glassPreset = GlassShapePreset.CustomFreeform;
                    lvl.contourPoints = new List<GlassContourPoint>
                    {
                        new GlassContourPoint(-0.56f, 0.90f, 0f, 0.3f),
                        new GlassContourPoint( 0.16f, 1.20f, 0f, 0f),
                        new GlassContourPoint( 0.68f, 1.38f, 0f, 0.3f)
                    };
                    lvl.boxCornerRadius = 0.20f;
                    break;
                case GlassTemplateKind.OvalFlask:
                    lvl.useCustomGlassShape = true;
                    lvl.glassIsBottle = false;
                    lvl.glassPreset = GlassShapePreset.CustomFreeform;
                    lvl.contourPoints = new List<GlassContourPoint>
                    {
                        new GlassContourPoint(-0.56f, 0.88f, 0f, 0.35f),
                        new GlassContourPoint(-0.20f, 1.30f, 0f, 0.15f),
                        new GlassContourPoint( 0.18f, 1.34f, 0f, 0.15f),
                        new GlassContourPoint( 0.52f, 1.22f, 0f, 0.20f),
                        new GlassContourPoint( 0.78f, 0.86f, 0f, 0.35f)
                    };
                    lvl.boxCornerRadius = 0.22f;
                    break;
            }
        }

        private static void CopyGlassOnly(PuzzleGlassLevelData from, PuzzleGlassLevelData to)
        {
            if (from == null || to == null) return;
            to.useCustomGlassShape = from.useCustomGlassShape;
            to.glassIsBottle = from.glassIsBottle;
            to.glassPreset = from.glassPreset;
            to.contourPoints = from.contourPoints != null ? new List<GlassContourPoint>(from.contourPoints) : new List<GlassContourPoint>();
            to.symmetricMode = from.symmetricMode;
            to.widthScale = from.widthScale;
            to.heightScale = from.heightScale;
            to.showShoulderHandles = from.showShoulderHandles;
            to.colorTheme = from.colorTheme;
            to.glassBodyColor = from.glassBodyColor;
            to.glassCenterGlowColor = from.glassCenterGlowColor;
            to.glassOuterRimColor = from.glassOuterRimColor;
            to.glassInnerRimColor = from.glassInnerRimColor;
            to.boxCornerRadius = from.boxCornerRadius;
            to.boxFrameThickness = from.boxFrameThickness;
            to.boxFrameColor = from.boxFrameColor;
            to.boxFrameHighlightColor = from.boxFrameHighlightColor;
            to.boxFrameEdgeShadeColor = from.boxFrameEdgeShadeColor;
        }
    }
}
