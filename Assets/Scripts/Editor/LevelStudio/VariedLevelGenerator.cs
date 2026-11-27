using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Çeşitli level üretici: kare/dikdörtgen kutu cam (dönmeyen) ya da farklı boyut/şekilde halka pist; değişken top sayısı;
    /// çoklu mekanik (birden çok buz, çok anahtarlı kilit, birden çok dolu kutu). Çözücüyle hedef zorluk bandına göre seçer.
    /// </summary>
    public static class VariedLevelGenerator
    {
        public enum GlassKind { Track, Box }

        public class Spec
        {
            public GlassKind kind = GlassKind.Track;
            public GlassTrackPreset trackPreset = GlassTrackPreset.ORing;
            public GlassShapePreset boxPreset = GlassShapePreset.SquareBox;
            public bool keepLayers = true;     // pistte "Katmanları koru"
            public int balls = 300;            // hedef top sayısı (camda başlayan)
            public LevelDifficulty band = LevelDifficulty.Medium;
            public int colors = 4;
            public int cols = 5, rows = 2;     // tepsi
            public int ice, locks, keysPerLock = 1, filled;
            public int seed = 1;
            public double budgetMs = 40000;
        }

        private const string TrackTemplate = "Assets/Levels/Level_46.asset";
        private const string BoxTemplate = "Assets/Levels/Level_48.asset";
        private const int MinCount = 22, MaxCount = 70;

        private static readonly PuzzleColor[] Palette =
        {
            PuzzleColor.Yellow, PuzzleColor.Blue, PuzzleColor.Green, PuzzleColor.Pink,
            PuzzleColor.Red, PuzzleColor.Purple, PuzzleColor.Orange
        };

        private class Candidate
        {
            public List<LevelMonsterEntry> monsters;
            public List<ManualPoolBallEntry> manualBalls;   // pist
            public List<LevelMonsterEntry> groups;          // kutu cam (alttan üste)
            public float smart, random, fit;
            public LevelMetrics metrics;
        }

        // ==================== Giriş ====================

        /// <summary>Üretir ve Level_{num+45}.asset'e yazar (varsa üstüne; GUID korunur). Özet döner.</summary>
        public static string CreateLevel(int num, Spec spec)
        {
            string templatePath = spec.kind == GlassKind.Box ? BoxTemplate : TrackTemplate;
            var template = UnityEditor.AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(templatePath);
            if (template == null) return $"Level {num}: şablon yok";

            var work = Object.Instantiate(template);
            work.hideFlags = HideFlags.DontSave;
            PrepareBase(work, spec);

            Candidate best = null;
            int tries = 0, cands = 0;
            var rng = new System.Random(spec.seed);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            List<List<(int index, PuzzleColor color)>> slices = null;
            int[] rankOf = null, sliceOf = null;
            if (spec.kind == GlassKind.Track)
            {
                if (!BuildTrackBalls(work, spec.balls)) { Object.DestroyImmediate(work); return $"Level {num}: pistte {spec.balls} topluk yer yok"; }
                GlassShapeData.BuildTrack(work, out _, out var lay);
                slices = PuzzleLevelSolver.BuildRingSlices(work, lay.balls, out _);
                if (slices == null || lay.balls.Count != work.manualBalls.Count) { Object.DestroyImmediate(work); return $"Level {num}: halka dilimleri kurulamadı"; }
                rankOf = new int[lay.balls.Count];
                sliceOf = new int[lay.balls.Count];
                for (int k = 0; k < slices.Count; k++)
                    for (int r = 0; r < slices[k].Count; r++) { rankOf[slices[k][r].index] = r; sliceOf[slices[k][r].index] = k; }
            }

            while (sw.Elapsed.TotalMilliseconds < spec.budgetMs)
            {
                tries++;
                var c = spec.kind == GlassKind.Track
                    ? TryTrack(work, spec, rng, rankOf, sliceOf, slices.Count)
                    : TryBox(work, spec, rng);
                if (c == null) continue;
                cands++;
                if (best == null || c.fit > best.fit) best = c;
            }

            if (best == null) { Object.DestroyImmediate(work); return $"Level {num}: uygun tasarım bulunamadı ({tries} deneme)"; }

            // Kaydet
            string path = $"Assets/Levels/Level_{num + 45}.asset";
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
            if (asset == null)
            {
                UnityEditor.AssetDatabase.CopyAsset(templatePath, path);
                asset = UnityEditor.AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
            }
            UnityEditor.Undo.RecordObject(asset, $"Level {num} tasarım");
            UnityEditor.EditorUtility.CopySerialized(work, asset);
            asset.name = $"Level_{num + 45}";
            asset.hideFlags = HideFlags.None;
            asset.levelNumber = num;
            asset.levelName = $"Level {num}";
            asset.tutorial = LevelTutorial.None;
            asset.tutorialHint = false;
            asset.tutorialBlockIndex = -1;
            asset.monsters = best.monsters;
            if (spec.kind == GlassKind.Track) asset.manualBalls = best.manualBalls;
            else asset.bottomToTopGroups = best.groups;
            asset.difficulty = PuzzleLevelSolver.DifficultyFromRates(best.random, best.smart);
            UnityEditor.EditorUtility.SetDirty(asset);
            UnityEditor.AssetDatabase.SaveAssets();
            Object.DestroyImmediate(work);

            return Summary(asset, spec, best, cands, tries);
        }

        private static void PrepareBase(PuzzleGlassLevelData lvl, Spec spec)
        {
            lvl.trackLayerLock = false;
            lvl.stagingGridCols = spec.cols;
            lvl.stagingGridRows = spec.rows;
            lvl.gridWalls = new List<Vector2Int>();
            lvl.iceMeltMode = IceMeltMode.PerBallCollected;
            if (spec.kind == GlassKind.Track)
            {
                lvl.useCustomGlassShape = true;
                lvl.glassPreset = GlassShapePreset.TrackChannel;
                lvl.trackPreset = spec.trackPreset;
                lvl.trackBranches = new List<GlassTrackBranch>();
                lvl.trackAutoFitToBalls = true;
                lvl.trackHiddenRatio = 0f;
                lvl.useManualBallLayout = true;
                lvl.trackKeepLayers = spec.keepLayers;
                lvl.useBallFeedTubes = false;
            }
            else
            {
                lvl.useCustomGlassShape = true;
                lvl.glassPreset = spec.boxPreset;
                lvl.useManualBallLayout = false;
                lvl.manualBalls = new List<ManualPoolBallEntry>();
                lvl.trackKeepLayers = false;
                lvl.useBallFeedTubes = false;
            }
        }

        // ==================== Pist ====================

        /// <summary>Pisti n topa göre boyutlandırır ve n topu halka slotlarına (en içten fazlalık atılarak) dizer.</summary>
        private static bool BuildTrackBalls(PuzzleGlassLevelData lvl, int n)
        {
            lvl.manualTrackCapacity = n;
            lvl.manualBalls = new List<ManualPoolBallEntry>();
            for (int i = 0; i < n; i++) lvl.manualBalls.Add(new ManualPoolBallEntry(PuzzleColor.Blue, 0, i));
            var slots = GlassShapeData.GetManualSlots(lvl, out _, out _, out _);
            if (slots == null || slots.Count < n) return false;
            GlassShapeData.BuildTrack(lvl, out var compiled, out _);
            if (compiled == null || compiled.closedLoopBranch < 0) return false;

            // Fazla slotları iç şeritten, halka boyunca eşit aralıkla at
            var order = new List<(int i, int layer, float u)>();
            for (int i = 0; i < slots.Count; i++)
                order.Add((i, GlassTrackLayout.ComputeRingLayer(compiled, slots[i].localPos), GlassTrackLayout.RingParam(compiled, slots[i].localPos)));
            var inner = order.FindAll(o => o.layer == 2);
            inner.Sort((a, b) => a.u.CompareTo(b.u));
            int drop = slots.Count - n;
            var dropSet = new HashSet<int>();
            if (drop > 0)
            {
                if (inner.Count >= drop)
                    for (int k = 0; k < drop; k++) dropSet.Add(inner[(int)((long)k * inner.Count / drop)].i);
                else
                    for (int k = slots.Count - 1; k >= 0 && dropSet.Count < drop; k--) dropSet.Add(k);
            }
            lvl.manualBalls = new List<ManualPoolBallEntry>();
            for (int i = 0; i < slots.Count; i++)
                if (!dropSet.Contains(i)) lvl.manualBalls.Add(new ManualPoolBallEntry(PuzzleColor.Blue, slots[i].column, slots[i].row));
            GlassShapeData.CanonicalizeManualTrackBalls(lvl);
            return lvl.manualBalls.Count == n;
        }

        private static Candidate TryTrack(PuzzleGlassLevelData work, Spec spec, System.Random rng, int[] rankOf, int[] sliceOf, int sCount)
        {
            int n = work.manualBalls.Count;
            var cs = PickColors(rng, spec.colors);
            var colors = new PuzzleColor[n];
            if (spec.keepLayers)
            {
                // Dış bant (dilimin en dış 2 topu) / orta (3.) / iç (4.+)
                int k = cs.Count;
                PuzzleColor oA = cs[0], oB = cs[1 % k], mid = cs[2 % k], inA = cs[(k - 1)], inB = cs[Mathf.Max(2, k - 2) % k];
                if (k >= 5) { mid = rng.Next(2) == 0 ? cs[2] : cs[4]; inA = cs[3]; inB = mid == cs[2] ? cs[4] : cs[2]; }
                if (k >= 6) { oB = cs[1]; mid = cs[2]; inA = cs[3]; inB = cs[4]; }
                double cutO = 0.3 + rng.NextDouble() * 0.4, cutI = 0.3 + rng.NextDouble() * 0.4;
                int offO = rng.Next(sCount), offI = rng.Next(sCount);
                for (int i = 0; i < n; i++)
                {
                    int r = rankOf[i];
                    if (r <= 1) colors[i] = ((sliceOf[i] + offO) % sCount) < cutO * sCount ? oA : oB;
                    else if (r == 2) colors[i] = k >= 6 && ((sliceOf[i] + offI) % sCount) < cutI * sCount ? cs[5] : mid;
                    else colors[i] = ((sliceOf[i] + offI) % sCount) < cutI * sCount ? inA : inB;
                }
            }
            else
            {
                // Katmansız pist: halka boyunca renk yayları (görsel düzen; toplar dönüp karıştığı için kolay)
                var cuts = new List<double>();
                for (int k = 0; k < cs.Count; k++) cuts.Add(rng.NextDouble());
                cuts.Sort();
                int off = rng.Next(sCount);
                for (int i = 0; i < n; i++)
                {
                    double pos = ((sliceOf[i] + off) % sCount) / (double)sCount;
                    int seg = 0;
                    while (seg < cuts.Count - 1 && pos > cuts[seg]) seg++;
                    colors[i] = cs[seg];
                }
            }

            var filledBoxes = MakeFilled(rng, spec, cs, colors, rankOf);
            if (filledBoxes == null) return null;
            var glass = CountColors(colors);
            var boxes = BuildBoxesFromGlass(rng, spec, glass, filledBoxes);
            if (boxes == null) return null;
            var mons = Layout(rng, spec, boxes, InnerColors(colors, rankOf, spec.keepLayers));
            if (mons == null || !AssignMechanics(rng, spec, mons)) return null;

            var lvl = Object.Instantiate(work);
            lvl.hideFlags = HideFlags.DontSave;
            lvl.monsters = mons;
            lvl.manualBalls = new List<ManualPoolBallEntry>();
            for (int i = 0; i < n; i++)
            {
                var b = work.manualBalls[i];
                lvl.manualBalls.Add(new ManualPoolBallEntry(colors[i], b.column, b.row) { hasTrackPos = b.hasTrackPos, trackPos = b.trackPos });
            }
            var cand = Evaluate(lvl, spec);
            if (cand != null) cand.manualBalls = lvl.manualBalls;
            Object.DestroyImmediate(lvl);
            return cand;
        }

        // ==================== Kutu cam ====================

        private static Candidate TryBox(PuzzleGlassLevelData work, Spec spec, System.Random rng)
        {
            var cs = PickColors(rng, spec.colors);
            int boxCount = spec.cols * spec.rows;
            int regular = boxCount - spec.filled;
            if (regular < cs.Count) return null;

            // Kutu sayaçları: toplamı hedef top sayısına yakın
            var boxes = new List<LevelMonsterEntry>();
            var colorOf = new List<PuzzleColor>(cs);
            while (colorOf.Count < regular) colorOf.Add(cs[rng.Next(cs.Count)]);
            float avg = spec.balls / (float)boxCount;
            foreach (var c in colorOf)
                boxes.Add(new LevelMonsterEntry(c, Mathf.Clamp(Mathf.RoundToInt(avg * (0.7f + (float)rng.NextDouble() * 0.6f)), MinCount, MaxCount)));
            for (int f = 0; f < spec.filled; f++)
            {
                var own = cs[rng.Next(cs.Count)];
                var fill = cs[rng.Next(cs.Count)];
                if (own == fill) return null;
                boxes.Add(new LevelMonsterEntry(own, MinCount + rng.Next(18)) { isFilledBox = true, filledBallColor = fill });
            }

            // Camda başlayacak toplar = toplayıcı yuvalar − dolu kutu stoğu
            var glass = new Dictionary<PuzzleColor, int>();
            foreach (var m in boxes) glass[m.color] = (glass.TryGetValue(m.color, out int v) ? v : 0) + m.count;
            foreach (var m in boxes) if (m.isFilledBox) glass[m.filledBallColor] = (glass.TryGetValue(m.filledBallColor, out int v2) ? v2 : 0) - m.count;
            foreach (var kv in glass) if (kv.Value < 0) return null;

            var mons = Layout(rng, spec, boxes, null);
            if (mons == null || !AssignMechanics(rng, spec, mons)) return null;

            // Cam sırası: öndeki kutuların renkleri üste (çıkıştan uzağa), arkadakiler alta — oyuncu sırayı düşünmeli
            var frontColors = new HashSet<PuzzleColor>();
            foreach (var m in mons) if (m.gridAnchor.y == spec.rows - 1) frontColors.Add(m.color);
            var chunks = new List<(double key, LevelMonsterEntry g)>();
            double bias = rng.NextDouble() * 0.8;
            foreach (var kv in glass)
            {
                int rem = kv.Value;
                while (rem > 0)
                {
                    int size = Mathf.Min(rem, 8 + rng.Next(18));
                    if (rem - size > 0 && rem - size < 6) size = rem;
                    double key = rng.NextDouble() + (frontColors.Contains(kv.Key) ? bias : 0);
                    chunks.Add((key, new LevelMonsterEntry(kv.Key, size)));
                    rem -= size;
                }
            }
            chunks.Sort((a, b) => a.key.CompareTo(b.key));
            var groups = new List<LevelMonsterEntry>();
            foreach (var ch in chunks)
            {
                if (groups.Count > 0 && groups[groups.Count - 1].color == ch.g.color && groups[groups.Count - 1].count + ch.g.count <= LevelMonsterEntry.MaxBallsPerEntry)
                    groups[groups.Count - 1].count += ch.g.count;
                else groups.Add(ch.g);
            }

            var lvl = Object.Instantiate(work);
            lvl.hideFlags = HideFlags.DontSave;
            lvl.monsters = mons;
            lvl.bottomToTopGroups = groups;
            int expected = 0;
            foreach (var kv in glass) expected += kv.Value;
            var layout = GlassShapeData.BuildLayout(lvl);
            if (layout.balls == null || layout.balls.Count != expected) { Object.DestroyImmediate(lvl); return null; } // cama sığmadı
            var cand = Evaluate(lvl, spec);
            if (cand != null) cand.groups = groups;
            Object.DestroyImmediate(lvl);
            return cand;
        }

        // ==================== Ortak ====================

        private static List<PuzzleColor> PickColors(System.Random rng, int k)
        {
            var cs = new List<PuzzleColor>(Palette);
            for (int i = cs.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cs[i], cs[j]) = (cs[j], cs[i]); }
            return cs.GetRange(0, Mathf.Clamp(k, 2, Palette.Length));
        }

        private static Dictionary<PuzzleColor, int> CountColors(PuzzleColor[] colors)
        {
            var d = new Dictionary<PuzzleColor, int>();
            foreach (var c in colors) d[c] = (d.TryGetValue(c, out int v) ? v : 0) + 1;
            return d;
        }

        private static HashSet<PuzzleColor> InnerColors(PuzzleColor[] colors, int[] rankOf, bool keepLayers)
        {
            var s = new HashSet<PuzzleColor>();
            if (!keepLayers) return s;
            var outer = new HashSet<PuzzleColor>();
            for (int i = 0; i < colors.Length; i++) if (rankOf[i] <= 1) outer.Add(colors[i]);
            for (int i = 0; i < colors.Length; i++) if (rankOf[i] >= 2 && !outer.Contains(colors[i])) s.Add(colors[i]);
            return s;
        }

        /// <summary>Pistte dolu kutular: c tane fillCol topu ownCol'a çevrilir (en içtekilerden) ve dolu kutu üretilir.</summary>
        private static List<LevelMonsterEntry> MakeFilled(System.Random rng, Spec spec, List<PuzzleColor> cs, PuzzleColor[] colors, int[] rankOf)
        {
            var list = new List<LevelMonsterEntry>();
            for (int f = 0; f < spec.filled; f++)
            {
                var fill = cs[rng.Next(cs.Count)];
                var own = cs[rng.Next(cs.Count)];
                if (fill == own) return null;
                int fc = MinCount + rng.Next(18);
                var cand = new List<int>();
                for (int i = 0; i < colors.Length; i++) if (colors[i] == fill) cand.Add(i);
                if (cand.Count < fc + MinCount) return null;
                cand.Sort((a, b) => rankOf[b].CompareTo(rankOf[a]));
                for (int k = 0; k < fc; k++) colors[cand[k]] = own;
                list.Add(new LevelMonsterEntry(own, fc) { isFilledBox = true, filledBallColor = fill });
            }
            return list;
        }

        /// <summary>Camdaki renk sayılarından normal kutuları böler; dolu kutuların stok/toplama farkını hesaba katar.</summary>
        private static List<LevelMonsterEntry> BuildBoxesFromGlass(System.Random rng, Spec spec, Dictionary<PuzzleColor, int> glass, List<LevelMonsterEntry> filled)
        {
            var total = new Dictionary<PuzzleColor, int>(glass);
            foreach (var fb in filled)
            {
                total[fb.color] = (total.TryGetValue(fb.color, out int a) ? a : 0) - fb.count;
                total[fb.filledBallColor] = (total.TryGetValue(fb.filledBallColor, out int b) ? b : 0) + fb.count;
            }
            var keys = new List<PuzzleColor>();
            foreach (var kv in total) { if (kv.Value < 0) return null; if (kv.Value > 0) keys.Add(kv.Key); }
            foreach (var c in keys) if (total[c] < MinCount) return null;
            int regular = spec.cols * spec.rows - filled.Count;
            if (keys.Count > regular) return null;
            var parts = new Dictionary<PuzzleColor, int>();
            foreach (var c in keys) parts[c] = 1;
            for (int assigned = keys.Count; assigned < regular; assigned++)
            {
                var big = keys[0];
                foreach (var c in keys) if ((double)total[c] / parts[c] > (double)total[big] / parts[big]) big = c;
                parts[big]++;
            }
            var boxes = new List<LevelMonsterEntry>();
            foreach (var c in keys)
            {
                int rem = total[c], p = parts[c];
                for (int q = 0; q < p; q++)
                {
                    int v = q == p - 1 ? rem : rem / (p - q) + rng.Next(-4, 5);
                    if (v < MinCount || v > MaxCount) return null;
                    boxes.Add(new LevelMonsterEntry(c, v));
                    rem -= v;
                }
            }
            boxes.AddRange(filled);
            return boxes;
        }

        /// <summary>Kutuları tepsiye dizer; iç (kapalı) renkler öne gelme eğiliminde (yem).</summary>
        private static List<LevelMonsterEntry> Layout(System.Random rng, Spec spec, List<LevelMonsterEntry> boxes, HashSet<PuzzleColor> innerColors)
        {
            if (boxes.Count != spec.cols * spec.rows) return null;
            bool gentle = spec.band == LevelDifficulty.Easy || spec.band == LevelDifficulty.Medium;
            double bias = (gentle ? 0.0 : 0.2) + rng.NextDouble() * (gentle ? 0.5 : 0.7);
            var keyed = new List<(double w, LevelMonsterEntry m)>();
            foreach (var m in boxes)
            {
                double w = rng.NextDouble();
                if (innerColors != null && innerColors.Contains(m.color)) w -= bias;
                keyed.Add((w, m));
            }
            keyed.Sort((a, b) => a.w.CompareTo(b.w));
            var mons = new List<LevelMonsterEntry>();
            int z = 0;
            for (int y = spec.rows - 1; y >= 0; y--)
                for (int x = 0; x < spec.cols; x++)
                {
                    var m = keyed[z++].m;
                    m.gridAnchor = new Vector2Int(x, y);
                    m.hasGridAnchor = true;
                    mons.Add(m);
                }
            return mons;
        }

        /// <summary>Buz, kilit ve anahtarları dağıtır. Anahtar ön sırada olmaz; mekanikler aynı kutuda üst üste binmez.</summary>
        private static bool AssignMechanics(System.Random rng, Spec spec, List<LevelMonsterEntry> mons)
        {
            var free = new List<LevelMonsterEntry>();
            foreach (var m in mons) if (!m.isFilledBox) free.Add(m);
            for (int i = free.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (free[i], free[j]) = (free[j], free[i]); }
            int idx = 0;
            // Kilitler önce (anahtarlar arka sıralardan seçilir)
            for (int l = 0; l < spec.locks; l++)
            {
                if (idx >= free.Count) return false;
                free[idx++].lockKeyCount = Mathf.Max(1, spec.keysPerLock);
            }
            int keysNeeded = spec.locks * Mathf.Max(1, spec.keysPerLock);
            int placed = 0;
            for (int k = idx; k < free.Count && placed < keysNeeded; k++)
            {
                var m = free[k];
                if (m.lockKeyCount > 0 || m.hasKey || m.gridAnchor.y == spec.rows - 1) continue;
                m.hasKey = true;
                placed++;
            }
            if (placed < keysNeeded) return false;
            int iced = 0;
            foreach (var m in free)
            {
                if (iced >= spec.ice) break;
                if (m.lockKeyCount > 0 || m.hasKey || m.iceCount > 0) continue;
                m.iceCount = 30 + rng.Next(90);
                iced++;
            }
            return iced >= spec.ice;
        }

        private static void BandRange(LevelDifficulty d, out float lo, out float hi, out float target)
        {
            switch (d)
            {
                case LevelDifficulty.Easy: lo = 0f; hi = PuzzleLevelSolver.EasyMaxSmart; target = 0.01f; break;
                case LevelDifficulty.Medium: lo = 0f; hi = PuzzleLevelSolver.MediumMaxSmart; target = 0.07f; break;
                case LevelDifficulty.Hard: lo = 0.15f; hi = 0.45f; target = 0.28f; break;
                default: lo = 0.5f; hi = 0.85f; target = 0.6f; break;
            }
        }

        private static Candidate Evaluate(PuzzleGlassLevelData lvl, Spec spec)
        {
            var dims = GlassShapeData.GetDims(lvl);
            BandRange(spec.band, out float lo, out float hi, out float target);
            float sm = PuzzleLevelSolver.EstimateSmartFailRate(lvl, dims, 150);
            if (sm < 0f || sm < lo || sm > hi) return null;
            float rnd = PuzzleLevelSolver.EstimateRandomFailRate(lvl, dims, 150);
            if (PuzzleLevelSolver.DifficultyFromRates(rnd, sm) != spec.band) return null;
            var mm = PuzzleLevelSolver.MeasureMetrics(lvl, dims);
            bool gentle = spec.band == LevelDifficulty.Easy || spec.band == LevelDifficulty.Medium;
            int maxTraps = spec.band == LevelDifficulty.Boss ? 1 : 0;
            if (mm.minSlots < 0 || !mm.complete || mm.firstMoveTraps > maxTraps) return null;
            var copy = new List<LevelMonsterEntry>();
            foreach (var m in lvl.monsters)
                copy.Add(new LevelMonsterEntry(m.color, m.count)
                {
                    gridAnchor = m.gridAnchor, hasGridAnchor = true, iceCount = m.iceCount, hasKey = m.hasKey,
                    lockKeyCount = m.lockKeyCount, isFilledBox = m.isFilledBox, filledBallColor = m.filledBallColor
                });
            return new Candidate
            {
                monsters = copy,
                smart = sm,
                random = rnd,
                metrics = mm,
                fit = -Mathf.Abs(sm - target) * 4f + Mathf.Min(rnd, 0.85f) * (gentle ? 0.3f : 0.15f) - mm.criticalSteps * 0.02f
            };
        }

        private static string Summary(PuzzleGlassLevelData lvl, Spec spec, Candidate c, int cands, int tries)
        {
            string glass = spec.kind == GlassKind.Box
                ? (spec.boxPreset == GlassShapePreset.SquareBox ? "kare kutu" : "dikdörtgen kutu")
                : $"{spec.trackPreset} pist{(spec.keepLayers ? " (katmanlı)" : "")}";
            int balls = 0;
            if (spec.kind == GlassKind.Track) balls = lvl.manualBalls.Count;
            else foreach (var g in lvl.bottomToTopGroups) balls += g.count;
            var colors = new HashSet<PuzzleColor>();
            int ice = 0, locks = 0, keys = 0, filled = 0, minC = 999, maxC = 0;
            foreach (var m in lvl.monsters)
            {
                colors.Add(m.color);
                if (m.isFilledBox) { colors.Add(m.filledBallColor); filled++; }
                if (m.iceCount > 0) ice++;
                if (m.lockKeyCount > 0) locks++;
                if (m.hasKey) keys++;
                minC = Mathf.Min(minC, m.count); maxC = Mathf.Max(maxC, m.count);
            }
            return $"Level {lvl.levelNumber} | {glass} | {balls} top | {colors.Count} renk | {lvl.monsters.Count} kutu ({minC}-{maxC}) | " +
                   $"buz {ice}, kilit {locks} (anahtar {keys}), dolu {filled} | {PuzzleLevelSolver.DifficultyName(lvl.difficulty)}: " +
                   $"akıllı %{Mathf.RoundToInt(c.smart * 100)}, rastgele %{Mathf.RoundToInt(c.random * 100)}, en az {c.metrics.minSlots} slot | aday {cands}/{tries}";
        }
    }
}
