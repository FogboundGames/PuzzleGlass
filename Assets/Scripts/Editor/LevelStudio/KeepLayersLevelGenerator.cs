using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// "Katmanları koru" (trackKeepLayers) levelleri üretir: halkalı bir kaynak levelin top konumlarını kullanır,
    /// renkleri halka dilimlerindeki sıraya göre (dış 2 sıra / orta / iç) dağıtır, kutuları ve mekanikleri yerleştirir,
    /// çözücüyle hedef zorluğa en yakın ve adil (ilk hamle tuzağı yok) sonucu seçer.
    /// </summary>
    public static class KeepLayersLevelGenerator
    {
        public class Spec
        {
            public LevelDifficulty band = LevelDifficulty.Medium;
            public int colors = 4;          // 3..5
            public int boxes = 10;          // 8 (4x2) ya da 10 (5x2)
            public bool ice, lockKey, filled;
            public int seed = 1;
            public double budgetMs = 40000;
        }

        public class Result
        {
            public PuzzleGlassLevelData level; // kaydedilmemiş kopya (DontSave)
            public float smart, random;
            public LevelMetrics metrics;
            public int tries, candidates;
            public string layers;
        }

        private static readonly PuzzleColor[] Palette =
        {
            PuzzleColor.Yellow, PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Pink,
            PuzzleColor.Red, PuzzleColor.Purple, PuzzleColor.Orange
        };

        /// <summary>Bandın akıllı bot kilitlenme aralığı ve hedef değeri.</summary>
        private static void BandRange(LevelDifficulty d, out float lo, out float hi, out float target)
        {
            switch (d)
            {
                case LevelDifficulty.Easy: lo = 0f; hi = PuzzleLevelSolver.EasyMaxSmart; target = 0.01f; break;
                // Orta: akıllı ≤%12; akıllı ≤%5 ise rastgele >%60 olmalı (DifficultyFromRates)
                case LevelDifficulty.Medium: lo = 0f; hi = PuzzleLevelSolver.MediumMaxSmart; target = 0.07f; break;
                case LevelDifficulty.Hard: lo = 0.15f; hi = 0.45f; target = 0.28f; break;
                default: lo = 0.5f; hi = 0.8f; target = 0.6f; break;
            }
        }

        /// <summary>
        /// Üretir ve "Assets/Levels/Level_{num+45}.asset" olarak kaydeder (kaynak levelin pist/cam ayarları kopyalanır).
        /// Özet metni döner; bulunamazsa ya da dosya varsa kaydetmez.
        /// </summary>
        public static string CreateLevelAsset(int num, string sourcePath, Spec spec)
        {
            var src = UnityEditor.AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(sourcePath);
            if (src == null) return $"Level {num}: kaynak yok ({sourcePath})";
            string path = $"Assets/Levels/Level_{num + 45}.asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(path) != null) return $"Level {num}: dosya zaten var ({path})";
            var r = Generate(src, spec);
            if (r.level == null) return $"Level {num}: uygun dizilim bulunamadı ({r.tries} deneme)";

            UnityEditor.AssetDatabase.CopyAsset(sourcePath, path);
            var lvl = UnityEditor.AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
            lvl.levelNumber = num;
            lvl.levelName = $"Level {num}";
            lvl.difficulty = spec.band;
            lvl.tutorial = LevelTutorial.None;
            lvl.tutorialHint = false;
            lvl.tutorialBlockIndex = -1;
            lvl.trackLayerLock = false;
            lvl.trackKeepLayers = true;
            lvl.stagingGridCols = r.level.stagingGridCols;
            lvl.stagingGridRows = r.level.stagingGridRows;
            lvl.monsters = new List<LevelMonsterEntry>();
            foreach (var m in r.level.monsters)
                lvl.monsters.Add(new LevelMonsterEntry(m.color, m.count)
                {
                    gridAnchor = m.gridAnchor, hasGridAnchor = true, iceCount = m.iceCount, hasKey = m.hasKey, lockKeyCount = m.lockKeyCount,
                    isFilledBox = m.isFilledBox, filledBallColor = m.filledBallColor
                });
            lvl.manualBalls = new List<ManualPoolBallEntry>();
            foreach (var b in r.level.manualBalls)
                lvl.manualBalls.Add(new ManualPoolBallEntry(b.color, b.column, b.row) { hasTrackPos = b.hasTrackPos, trackPos = b.trackPos });
            UnityEditor.EditorUtility.SetDirty(lvl);
            UnityEditor.AssetDatabase.SaveAssets();
            Object.DestroyImmediate(r.level);

            var front = new List<string>();
            foreach (var m in lvl.monsters)
                if (m.gridAnchor.y == lvl.stagingGridRows - 1)
                    front.Add($"{PuzzleLevelSolver.ColorName(m.color)} {m.count}{(m.iceCount > 0 ? " (buz " + m.iceCount + ")" : "")}{(m.lockKeyCount > 0 ? " (kilit)" : "")}{(m.hasKey ? " (anahtar)" : "")}{(m.isFilledBox ? " (dolu: " + PuzzleLevelSolver.ColorName(m.filledBallColor) + ")" : "")}");
            return $"Level {num} → {path}: akıllı %{Mathf.RoundToInt(r.smart * 100)}, rastgele %{Mathf.RoundToInt(r.random * 100)}, " +
                   $"puan {PuzzleLevelSolver.DifficultyScore(r.random, r.smart)}, en az {r.metrics.minSlots} slot, ilk tuzak {r.metrics.firstMoveTraps}, kritik {r.metrics.criticalSteps} | " +
                   $"{r.layers} | ön: {string.Join(", ", front)} | aday {r.candidates}/{r.tries}";
        }

        public static Result Generate(PuzzleGlassLevelData source, Spec spec)
        {
            var res = new Result();
            if (source == null || source.manualBalls == null || source.manualBalls.Count == 0) return res;
            var rng = new System.Random(spec.seed);

            var probe = Object.Instantiate(source);
            probe.trackKeepLayers = true;
            probe.trackLayerLock = false;
            GlassShapeData.BuildTrack(probe, out _, out var lay);
            var slices = PuzzleLevelSolver.BuildRingSlices(probe, lay.balls, out _);
            Object.DestroyImmediate(probe);
            int n = source.manualBalls.Count;
            if (slices == null || lay.balls == null || lay.balls.Count != n) return res;

            var rankOf = new int[n];
            var sliceOf = new int[n];
            for (int k = 0; k < slices.Count; k++)
                for (int r = 0; r < slices[k].Count; r++) { rankOf[slices[k][r].index] = r; sliceOf[slices[k][r].index] = k; }
            int sCount = slices.Count;

            BandRange(spec.band, out float lo, out float hi, out float target);
            bool gentle = spec.band == LevelDifficulty.Easy || spec.band == LevelDifficulty.Medium;
            int cols = spec.boxes <= 8 ? 4 : 5, rows = 2, boxCount = cols * rows;
            float bestFit = float.MinValue;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.Elapsed.TotalMilliseconds < spec.budgetMs)
            {
                res.tries++;
                var cs = new List<PuzzleColor>(Palette);
                for (int i = cs.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cs[i], cs[j]) = (cs[j], cs[i]); }
                cs = cs.GetRange(0, Mathf.Clamp(spec.colors, 3, 5));

                // Katman planı: dış bant (dilimin en dış 2 topu) 1-2 renk, orta (3. top) 1 renk, iç (4.+) 1-2 renk
                PuzzleColor oA, oB, mid, inA, inB;
                if (cs.Count == 3) { oA = cs[0]; oB = rng.Next(2) == 0 ? cs[0] : cs[1]; mid = cs[1]; inA = cs[2]; inB = rng.Next(2) == 0 ? cs[2] : cs[1]; }
                else if (cs.Count == 4) { oA = cs[0]; oB = cs[1]; mid = cs[2]; inA = cs[3]; inB = rng.Next(2) == 0 ? cs[3] : cs[2]; }
                else { oA = cs[0]; oB = cs[1]; mid = rng.Next(2) == 0 ? cs[2] : cs[4]; inA = cs[3]; inB = mid == cs[2] ? cs[4] : cs[2]; }

                double cutO = 0.35 + rng.NextDouble() * 0.3, cutI = 0.35 + rng.NextDouble() * 0.3;
                int offO = rng.Next(sCount), offI = rng.Next(sCount);
                var colors = new PuzzleColor[n];
                for (int i = 0; i < n; i++)
                {
                    int r = rankOf[i];
                    if (r <= 1) colors[i] = ((sliceOf[i] + offO) % sCount) < cutO * sCount ? oA : oB;
                    else if (r == 2) colors[i] = mid;
                    else colors[i] = ((sliceOf[i] + offI) % sCount) < cutI * sCount ? inA : inB;
                }

                // Dolu kutu: c topluk (fillCol) stok döker, sonra kendi rengini (ownCol) c tane toplar.
                // Denge için pistte c tane fillCol topu ownCol olur (en içtekilerden; katman bantları bozulmasın).
                LevelMonsterEntry filledBox = null;
                if (spec.filled)
                {
                    var fillCol = cs[rng.Next(cs.Count)];
                    var ownCol = cs[rng.Next(cs.Count)];
                    if (fillCol == ownCol) continue;
                    int fc = 28 + rng.Next(18);
                    var cand = new List<int>();
                    for (int i = 0; i < n; i++) if (colors[i] == fillCol) cand.Add(i);
                    if (cand.Count < fc + 26) continue;
                    cand.Sort((a, b) => rankOf[b].CompareTo(rankOf[a]));
                    for (int k = 0; k < fc; k++) colors[cand[k]] = ownCol;
                    filledBox = new LevelMonsterEntry(ownCol, fc) { isFilledBox = true, filledBallColor = fillCol };
                }

                var total = new Dictionary<PuzzleColor, int>();
                foreach (var c in colors) total[c] = (total.TryGetValue(c, out int v) ? v : 0) + 1;
                if (filledBox != null)
                {
                    total[filledBox.color] -= filledBox.count;           // bu kadarını dolu kutunun kendisi toplar
                    total[filledBox.filledBallColor] += filledBox.count; // dökülen stok da normal kutularca toplanır
                }
                if (total.Count != cs.Count) continue;
                var keys = new List<PuzzleColor>(total.Keys);
                int regular = boxCount - (filledBox != null ? 1 : 0);
                if (keys.Count > regular) continue;
                bool zeroColor = false;
                foreach (var c in keys) if (total[c] < 26) zeroColor = true;
                if (zeroColor) continue;
                var parts = new Dictionary<PuzzleColor, int>();
                foreach (var c in keys) parts[c] = 1;
                for (int assigned = keys.Count; assigned < regular; assigned++)
                {
                    var big = keys[0];
                    foreach (var c in keys) if ((double)total[c] / parts[c] > (double)total[big] / parts[big]) big = c;
                    parts[big]++;
                }
                var boxes = new List<LevelMonsterEntry>();
                bool bad = false;
                foreach (var c in keys)
                {
                    int rem = total[c], p = parts[c];
                    for (int q = 0; q < p; q++)
                    {
                        int v = q == p - 1 ? rem : rem / (p - q) + rng.Next(-5, 6);
                        if (v < 26 || v > 72) bad = true;
                        boxes.Add(new LevelMonsterEntry(c, v));
                        rem -= v;
                    }
                }
                if (bad) continue;
                if (filledBox != null) boxes.Add(filledBox);

                if (spec.ice) { int ii = rng.Next(boxes.Count); if (boxes[ii].isFilledBox) continue; boxes[ii].iceCount = 40 + rng.Next(80); }
                if (spec.lockKey)
                {
                    int li = rng.Next(boxes.Count), ki = rng.Next(boxes.Count);
                    if (li == ki || boxes[li].iceCount > 0 || boxes[ki].iceCount > 0 || boxes[li].isFilledBox || boxes[ki].isFilledBox) continue;
                    boxes[li].lockKeyCount = 1;
                    boxes[ki].hasKey = true;
                }

                // Dizilim: içteki renkler öne gelme eğilimli (yem); kolay levelde eğilim zayıf
                var innerSet = new HashSet<PuzzleColor> { inA, inB };
                var outerSet = new HashSet<PuzzleColor> { oA, oB };
                double bias = (gentle ? 0.0 : 0.2) + rng.NextDouble() * (gentle ? 0.5 : 0.7);
                var keyed = new List<(double w, LevelMonsterEntry m)>();
                foreach (var m in boxes)
                {
                    double w = rng.NextDouble();
                    if (innerSet.Contains(m.color) && !outerSet.Contains(m.color)) w -= bias;
                    if (outerSet.Contains(m.color) && !innerSet.Contains(m.color)) w += bias * 0.4;
                    keyed.Add((w, m));
                }
                keyed.Sort((a, b) => a.w.CompareTo(b.w));
                var mons = new List<LevelMonsterEntry>();
                int z = 0;
                for (int y = rows - 1; y >= 0; y--)
                    for (int x = 0; x < cols; x++)
                    {
                        var m = keyed[z++].m;
                        m.gridAnchor = new Vector2Int(x, y);
                        m.hasGridAnchor = true;
                        mons.Add(m);
                    }
                // Anahtar en önde olmasın (hemen alınıp mekanik kendiliğinden çözülmesin)
                bool keyFront = false;
                foreach (var m in mons) if (m.hasKey && m.gridAnchor.y == rows - 1) keyFront = true;
                if (keyFront && spec.lockKey) continue;

                var lvl = Object.Instantiate(source);
                lvl.hideFlags = HideFlags.DontSave;
                lvl.trackLayerLock = false;
                lvl.trackKeepLayers = true;
                lvl.stagingGridCols = cols;
                lvl.stagingGridRows = rows;
                lvl.monsters = mons;
                lvl.manualBalls = new List<ManualPoolBallEntry>();
                for (int i = 0; i < n; i++)
                {
                    var b = source.manualBalls[i];
                    lvl.manualBalls.Add(new ManualPoolBallEntry(colors[i], b.column, b.row) { hasTrackPos = b.hasTrackPos, trackPos = b.trackPos });
                }

                var dims = GlassShapeData.GetDims(lvl);
                float sm = PuzzleLevelSolver.EstimateSmartFailRate(lvl, dims, 150);
                if (sm < lo || sm > hi) { Object.DestroyImmediate(lvl); continue; }
                float rnd = PuzzleLevelSolver.EstimateRandomFailRate(lvl, dims, 150);
                if (PuzzleLevelSolver.DifficultyFromRates(rnd, sm) != spec.band) { Object.DestroyImmediate(lvl); continue; }
                var mm = PuzzleLevelSolver.MeasureMetrics(lvl, dims);
                int maxTraps = gentle ? 0 : (spec.band == LevelDifficulty.Hard ? 0 : 1);
                if (mm.minSlots < 0 || !mm.complete || mm.firstMoveTraps > maxTraps) { Object.DestroyImmediate(lvl); continue; }
                res.candidates++;

                // Kolay/orta: rastgele oyuncu da biraz zorlansın (sıkıcı olmasın); zor: kritik adım az olsun
                float fit = -Mathf.Abs(sm - target) * 4f + Mathf.Min(rnd, 0.8f) * (gentle ? 0.3f : 0.15f) - mm.criticalSteps * 0.02f;
                if (fit > bestFit)
                {
                    if (res.level != null) Object.DestroyImmediate(res.level);
                    bestFit = fit;
                    res.level = lvl;
                    res.smart = sm;
                    res.random = rnd;
                    res.metrics = mm;
                    res.layers = $"dış {PuzzleLevelSolver.ColorName(oA)}{(oB != oA ? "+" + PuzzleLevelSolver.ColorName(oB) : "")}, " +
                                 $"orta {PuzzleLevelSolver.ColorName(mid)}, iç {PuzzleLevelSolver.ColorName(inA)}{(inB != inA ? "+" + PuzzleLevelSolver.ColorName(inB) : "")}";
                }
                else Object.DestroyImmediate(lvl);
            }
            return res;
        }
    }
}
