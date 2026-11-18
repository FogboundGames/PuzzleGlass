using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Yeni sistem (pist cam + slot sırası + sütun kuyruğu) için level üretici.
    /// Ezgi'nin 1-14 levelleri gibi: tepsi tamamen 1x1 bloklarla dolu, her sütun bir kuyruk; üstteki blok tıklanınca slota gider,
    /// arkasındaki öne kayar. Top sırası bir "örnek oynanış" üzerinden kurulur, bu yüzden her level çözülebilir;
    /// zorluk, aynı anda kaç bloğun topunun karıştığı (iç içe geçme) ile ayarlanır ve rastgele oynayan oyuncunun
    /// kilitlenme oranıyla (PuzzleLevelSolver.EstimateRandomFailRate) ölçülür.
    /// </summary>
    public static class SlotLevelGenerator
    {
        public class Spec
        {
            public int cols = 4, rows = 3;
            public int colors = 4;
            public int minCount = 6, maxCount = 10;
            public LevelDifficulty difficulty = LevelDifficulty.Medium;
            /// <summary>Hedef rastgele kilitlenme oranı aralığı.</summary>
            public float failLo = 0.1f, failHi = 0.35f;
            /// <summary>Buzlu blok sayısı (0 = yok).</summary>
            public int iceBlocks;
            public IceMeltMode iceMode = IceMeltMode.PerBallCollected;
            /// <summary>Kilitli blok için gereken anahtar sayısı (0 = kilit yok).</summary>
            public int lockKeys;
            public GlassTrackPreset track = GlassTrackPreset.ArchLoop;
            public GlassColorTheme theme = GlassColorTheme.SkyBlueGlossy;
        }

        private static readonly PuzzleColor[] Palette =
        {
            PuzzleColor.Blue, PuzzleColor.Red, PuzzleColor.Yellow, PuzzleColor.Green,
            PuzzleColor.Purple, PuzzleColor.Orange, PuzzleColor.Pink
        };

        public class Outcome
        {
            public bool ok;
            public int seed;
            public float failRate;
            public SolveResult solve;
            public int balls;
            public int tries;
        }

        /// <summary>
        /// Seviyeyi üretir; hedef zorluk bandına giren ilk çözülebilir sonucu yazar. Bulamazsa banda en yakın olanı yazar.
        /// Cam/pist ayarları <paramref name="template"/>'ten (yeni sistemdeki bir level) alınır.
        /// </summary>
        public static Outcome Generate(PuzzleGlassLevelData lvl, PuzzleGlassLevelData template, Spec spec, int baseSeed, int maxTries = 400)
        {
            var best = new Outcome { ok = false, failRate = -1f };
            float bestDist = float.MaxValue;
            int bestSeed = 0;
            for (int t = 0; t < maxTries; t++)
            {
                int seed = baseSeed * 1000 + t;
                if (!Build(lvl, template, spec, seed)) continue;
                var dims = GlassShapeData.GetDims(lvl);
                var r = PuzzleLevelSolver.Solve(lvl, dims, 20000);
                if (r == null || r.status != SolveStatus.Solved) continue;
                float fr = PuzzleLevelSolver.EstimateRandomFailRate(lvl, dims, 300, seed);
                float dist = fr < spec.failLo ? spec.failLo - fr : (fr > spec.failHi ? fr - spec.failHi : 0f);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestSeed = seed;
                    best = new Outcome { ok = dist == 0f, seed = seed, failRate = fr, solve = r, tries = t + 1 };
                }
                if (dist == 0f) break;
            }
            if (bestSeed != 0)
            {
                Build(lvl, template, spec, bestSeed);
                best.balls = 0;
                foreach (var m in lvl.monsters) best.balls += m.count;
            }
            return best;
        }

        /// <summary>Tek bir tohumla leveli kurar (doğrulama yapmaz).</summary>
        public static bool Build(PuzzleGlassLevelData lvl, PuzzleGlassLevelData template, Spec spec, int seed)
        {
            var rng = new System.Random(seed);
            int cols = Mathf.Max(1, spec.cols), rows = Mathf.Max(1, spec.rows);
            int n = cols * rows;
            int colorCount = Mathf.Clamp(spec.colors, 2, Palette.Length);

            // Renk seçimi (karışık) ve bloklara dağıtım: her renk en az bir kez, mümkünse iki kez
            var palette = new List<PuzzleColor>(Palette);
            Shuffle(palette, rng);
            palette.RemoveRange(colorCount, palette.Count - colorCount);
            var blockColors = new List<PuzzleColor>();
            for (int i = 0; i < n; i++) blockColors.Add(palette[i % colorCount]);
            Shuffle(blockColors, rng);

            // Blok listesi: üst sıra önce (Ezgi'nin levellerindeki sıra)
            var blocks = new List<LevelMonsterEntry>();
            var anchorOf = new List<Vector2Int>();
            int k = 0;
            for (int r = rows - 1; r >= 0; r--)
            {
                for (int c = 0; c < cols; c++)
                {
                    int cnt = rng.Next(spec.minCount, spec.maxCount + 1);
                    blocks.Add(new LevelMonsterEntry
                    {
                        color = blockColors[k++],
                        count = cnt,
                        blockShape = CarrierBlockShape.Rect1x1,
                        gridAnchor = new Vector2Int(c, r),
                        hasGridAnchor = true
                    });
                    anchorOf.Add(new Vector2Int(c, r));
                }
            }

            // Örnek oynanış sırası: her adımda sütun başlarından (kuyruğun önü) rastgele biri slota gider
            var colQueues = new List<List<int>>();
            for (int c = 0; c < cols; c++)
            {
                var q = new List<int>();
                for (int r = rows - 1; r >= 0; r--) q.Add((rows - 1 - r) * cols + c);
                colQueues.Add(q);
            }
            var order = new List<int>();
            while (order.Count < n)
            {
                var heads = new List<int>();
                for (int c = 0; c < cols; c++) if (colQueues[c].Count > 0) heads.Add(c);
                int pick = heads[rng.Next(heads.Count)];
                order.Add(colQueues[pick][0]);
                colQueues[pick].RemoveAt(0);
            }

            // Top dizisi: örnek oynanışta aynı anda en fazla "overlap" blok slotta toplar; onların topları parça parça karışır
            int overlap;
            switch (spec.difficulty)
            {
                case LevelDifficulty.Easy: overlap = 1 + (rng.Next(3) == 0 ? 1 : 0); break;
                case LevelDifficulty.Medium: overlap = 2 + (rng.Next(3) == 0 ? 1 : 0); break;
                case LevelDifficulty.Hard: overlap = 3; break;
                default: overlap = 3 + (rng.Next(2) == 0 ? 1 : 0); break;
            }
            overlap = Mathf.Clamp(overlap, 1, 4);

            var remaining = new int[n];
            for (int i = 0; i < n; i++) remaining[i] = blocks[i].count;
            var active = new List<int>();
            int next = 0;
            var groups = new List<LevelMonsterEntry>();
            while (next < n || active.Count > 0)
            {
                while (active.Count < overlap && next < n) active.Add(order[next++]);
                int a = active[rng.Next(active.Count)];
                int chunk = overlap == 1 ? remaining[a] : Mathf.Min(remaining[a], rng.Next(2, 6));
                remaining[a] -= chunk;
                var col = blocks[a].color;
                if (groups.Count > 0 && groups[groups.Count - 1].color == col) groups[groups.Count - 1].count += chunk;
                else groups.Add(new LevelMonsterEntry { color = col, count = chunk });
                if (remaining[a] <= 0) active.Remove(a);
            }

            // Mekanikler: örnek sırada geç gelen bloklara buz / kilit; anahtarlar daha erken bitenlere
            if (spec.iceBlocks > 0)
            {
                var cand = new List<int>();
                for (int i = n / 3; i < n; i++) cand.Add(order[i]);
                Shuffle(cand, rng);
                for (int j = 0; j < spec.iceBlocks && j < cand.Count; j++)
                {
                    int bi = cand[j];
                    int pos = order.IndexOf(bi);
                    if (spec.iceMode == IceMeltMode.PerBoxCompleted)
                        blocks[bi].iceCount = Mathf.Clamp(pos - overlap - rng.Next(0, 2), 1, 9);
                    else
                    {
                        int before = 0;
                        for (int p = 0; p < pos - overlap; p++) before += blocks[order[p]].count;
                        blocks[bi].iceCount = Mathf.Clamp(Mathf.RoundToInt(before * (0.55f + (float)rng.NextDouble() * 0.35f)), 4, 99);
                    }
                }
            }
            if (spec.lockKeys > 0)
            {
                int lockPos = Mathf.Clamp(n / 2 + rng.Next(0, Mathf.Max(1, n / 3)), spec.lockKeys + overlap, n - 1);
                int lockIdx = order[lockPos];
                blocks[lockIdx].lockKeyCount = spec.lockKeys;
                blocks[lockIdx].iceCount = 0;
                var keyCand = new List<int>();
                for (int p = 0; p < lockPos - overlap; p++) if (blocks[order[p]].iceCount == 0) keyCand.Add(order[p]);
                if (keyCand.Count < spec.lockKeys) return false;
                Shuffle(keyCand, rng);
                for (int j = 0; j < spec.lockKeys; j++) blocks[keyCand[j]].hasKey = true;
            }

            // Cam/pist ayarlarını şablondan al, içerik bu üreticiden
            string assetName = lvl.name;
            int levelNumber = lvl.levelNumber;
            string levelName = lvl.levelName;
            UnityEditor.EditorUtility.CopySerialized(template, lvl);
            lvl.name = assetName;
            lvl.levelNumber = levelNumber;
            lvl.levelName = levelName;
            lvl.useCustomGlassShape = true;
            lvl.glassPreset = GlassShapePreset.TrackChannel;
            lvl.trackPreset = spec.track;
            lvl.trackLanesAcross = 0;
            lvl.trackAutoFitToBalls = true;
            lvl.trackScale = 1f;
            if (lvl.trackBranches != null) lvl.trackBranches.Clear();
            lvl.colorTheme = spec.theme;
            lvl.difficulty = spec.difficulty;
            lvl.stagingGridCols = cols;
            lvl.stagingGridRows = rows;
            if (lvl.gridWalls != null) lvl.gridWalls.Clear();
            lvl.useManualBallLayout = false;
            if (lvl.manualBalls != null) lvl.manualBalls.Clear();
            lvl.useCustomSlotSettings = false;
            lvl.iceMeltMode = spec.iceMode;
            lvl.tutorialHint = false;
            lvl.tutorialBlockIndex = -1;
            lvl.monsters = blocks;
            lvl.bottomToTopGroups = groups;
            return true;
        }

        private static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
