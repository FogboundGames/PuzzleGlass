using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Dönen (halkalı) pist modeli. Oyunda (TrackCrowdQueue) halkadaki toplar yürüyen bant gibi sürekli döner:
    /// bantta olan her top eninde sonunda çıkışa gelir, yani slottaki kutu bantta kendi renginden ne varsa alabilir
    /// (bant sırası önemsiz bir renk havuzudur). Halkanın dışındaki toplar (kollar, giriş yolu, gizli kuyruk) sıralıdır
    /// ve ancak banttan top ayrılıp yer açıldıkça sırayla banda katılır.
    ///
    /// Katman kilidi (trackLayerLock): halkadaki her top doğduğu şeride göre dış (0), orta (1) ya da iç (2) katmandadır.
    /// Topu kalan en dış katman açıktır; ondan içteki katmanların topları çıkıştan ayrılamaz (TrackCrowdQueue.LayerLock).
    /// Kilit kapalıysa ya da top kuyruktan geldiyse top "serbest" kovadadır (her zaman alınabilir).
    ///
    /// Durum kodlaması (State.taken, uzunluk Buckets * ColorSlots + 1):
    ///   taken[k * ColorSlots + c] = k kovasından (0..2 katman, 3 serbest) alınan c rengi top sayısı
    ///   taken[QueueIndex]         = kuyruktan banda katılmış top sayısı (serbest kovaya girer)
    /// </summary>
    public static partial class PuzzleLevelSolver
    {
        private const int ColorSlots = 16;
        private const int Layers = 3;
        private const int FreeBucket = Layers;
        private const int Buckets = Layers + 1;
        private const int QueueIndex = Buckets * ColorSlots;

        private sealed class BeltModel
        {
            public int[][] initial = NewBuckets();          // başlangıçta bantta olan toplar: [kova][renk]
            public bool layerLock;
            public List<PuzzleColor> queue = new List<PuzzleColor>(); // halka dışı, katılma sırasıyla
            public int[][] prefix;                        // prefix[k][c]: kuyruğun ilk k topunda c rengi sayısı
            public int capacity;                          // bandın taşıyabileceği en fazla top
        }

        // Çözücünün dalga dizisine (wave1Stacks) iliştirilir: böylece mevcut tüm çağrılar (çözüm, botlar, ölçüler,
        // oynatma analizi) imza değişmeden dönen pist modelini kullanır.
        private static readonly ConditionalWeakTable<List<PuzzleColor>[], BeltModel> s_Belts =
            new ConditionalWeakTable<List<PuzzleColor>[], BeltModel>();

        private static BeltModel GetBelt(List<PuzzleColor>[] wave1Stacks) =>
            wave1Stacks != null && s_Belts.TryGetValue(wave1Stacks, out var b) ? b : null;

        /// <summary>Level halkalı pistse dönen bant modelini kurar; değilse null (eski sabit sütun modeli kullanılır).</summary>
        private static BeltModel TryBuildBelt(PuzzleGlassLevelData lvl, List<OrderedPoolLayout.PoolBall> chamberBalls)
        {
            if (!GlassShapeData.IsTrack(lvl)) return null;
            GlassShapeData.BuildTrack(lvl, out var compiled, out var layout);
            if (compiled == null || compiled.closedLoopBranch < 0) return null;

            var belt = new BeltModel { layerLock = lvl.trackLayerLock };
            float half = compiled.innerHalfWidth;
            var off = new List<OrderedPoolLayout.PoolBall>();
            int onRing = 0;
            if (chamberBalls != null)
            {
                foreach (var b in chamberBalls)
                {
                    if (compiled.SampleDistToRingCenter(b.localPos) < half)
                    {
                        // Oyunla aynı: katman doğduğu konumdan (BallPatternGenerator → ComputeRingLayer)
                        int layer = belt.layerLock ? GlassTrackLayout.ComputeRingLayer(compiled, b.localPos) : -1;
                        int k = layer >= 0 && layer < Layers ? layer : FreeBucket;
                        belt.initial[k][(int)b.color]++;
                        onRing++;
                    }
                    else off.Add(b);
                }
            }
            // Halka dışındaki toplar çıkışa (halkaya) yakın olandan uzağa doğru katılır; sonra gizli kuyruk
            off.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.ballColumn.CompareTo(b.ballColumn));
            foreach (var b in off) belt.queue.Add(b.color);
            if (layout.overflowColors != null) belt.queue.AddRange(layout.overflowColors);

            // Bant kapasitesi: oyundaki bant sınırı (beltCount) varsa o; yoksa halkadaki fiziksel slot sayısı
            int ringSlots = 0;
            if (compiled.manualSlots != null)
                foreach (var s in compiled.manualSlots)
                    if (compiled.SampleDistToRingCenter(s.localPos) < half) ringSlots++;
            belt.capacity = layout.beltCount > 0 ? layout.beltCount : Mathf.Max(onRing, ringSlots);
            if (belt.capacity <= 0) belt.capacity = int.MaxValue;

            belt.prefix = new int[belt.queue.Count + 1][];
            belt.prefix[0] = new int[ColorSlots];
            for (int k = 0; k < belt.queue.Count; k++)
            {
                belt.prefix[k + 1] = (int[])belt.prefix[k].Clone();
                belt.prefix[k + 1][(int)belt.queue[k]]++;
            }
            return belt;
        }

        /// <summary>
        /// "Katmanları koru" (trackKeepLayers) levelleri: toplar dönerken şeritlerini korur ve hep dışa (çıkış tarafına)
        /// yaslanır; çıkışta bir top, dış tarafında başka top varken çıkamaz, o gidince üstteki o boşluğa düşer.
        /// Model: halka top çapı kadar enine dilimlere bölünür; her dilim dıştan içe bir yığındır, sadece en dıştaki alınır.
        /// Halka dışı top ya da kuyruk varsa null (renk havuzu modeli kullanılır).
        /// </summary>
        private static List<PuzzleColor>[] TryBuildRingStacks(PuzzleGlassLevelData lvl, List<OrderedPoolLayout.PoolBall> chamberBalls)
        {
            if (lvl == null || !lvl.trackKeepLayers || lvl.trackLayerLock || !GlassShapeData.IsTrack(lvl) || chamberBalls == null || chamberBalls.Count == 0) return null;
            var stacks = BuildRingSlices(lvl, chamberBalls, out _);
            if (stacks == null) return null;
            var result = new List<PuzzleColor>[stacks.Count];
            for (int k = 0; k < stacks.Count; k++)
            {
                result[k] = new List<PuzzleColor>(stacks[k].Count);
                foreach (var e in stacks[k]) result[k].Add(e.color);
            }
            return result;
        }

        /// <summary>
        /// Halka dilimleri: her dilimdeki toplar dıştan içe sıralı (index = topun chamberBalls içindeki sırası).
        /// Level tasarımında renkleri dilim sırasına göre dağıtmak için de kullanılır (0 = en dış).
        /// </summary>
        public static List<List<(int index, PuzzleColor color)>> BuildRingSlices(PuzzleGlassLevelData lvl, List<OrderedPoolLayout.PoolBall> balls, out int sliceCount)
        {
            sliceCount = 0;
            GlassShapeData.BuildTrack(lvl, out var compiled, out var layout);
            if (compiled == null || compiled.closedLoopBranch < 0 || balls == null) return null;
            if (layout.overflowColors != null && layout.overflowColors.Count > 0) return null;
            var ring = compiled.polylines[compiled.closedLoopBranch];
            if (ring == null || ring.Count < 3) return null;

            float ringLen = 0f, area = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % ring.Count];
                ringLen += (b - a).magnitude;
                area += a.x * b.y - b.x * a.y;
            }
            float d = Mathf.Max(0.02f, compiled.ballDiameter);
            int bins = Mathf.Max(8, Mathf.RoundToInt(ringLen / d));
            sliceCount = bins;
            var tmp = new List<(float lat, int index, PuzzleColor color)>[bins];
            for (int k = 0; k < bins; k++) tmp[k] = new List<(float, int, PuzzleColor)>();
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                if (compiled.SampleDistToRingCenter(b.localPos) >= compiled.innerHalfWidth) return null;
                float u = GlassTrackLayout.RingParam(compiled, b.localPos);
                if (u < 0f) return null;
                int bin = Mathf.Clamp(Mathf.FloorToInt(u * bins), 0, bins - 1);
                tmp[bin].Add((RingLateral(ring, area, b.localPos), i, b.color));
            }
            var result = new List<List<(int, PuzzleColor)>>();
            foreach (var st in tmp)
            {
                if (st.Count == 0) continue;
                st.Sort((x, y) => y.lat.CompareTo(x.lat)); // dış önce
                var col = new List<(int, PuzzleColor)>(st.Count);
                foreach (var e in st) col.Add((e.index, e.color));
                result.Add(col);
            }
            return result;
        }

        private static float RingLateral(List<Vector2> ring, float area, Vector2 p)
        {
            float best = float.MaxValue;
            Vector2 bc = p, bt = Vector2.right;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % n];
                Vector2 ab = b - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-9f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                Vector2 c = a + ab * t;
                float dsq = (c - p).sqrMagnitude;
                if (dsq < best) { best = dsq; bc = c; bt = ab / Mathf.Sqrt(l2); }
            }
            Vector2 outward = area > 0f ? new Vector2(bt.y, -bt.x) : new Vector2(-bt.y, bt.x);
            return Vector2.Dot(p - bc, outward);
        }

        private static int[][] NewBuckets()
        {
            var a = new int[Buckets][];
            for (int k = 0; k < Buckets; k++) a[k] = new int[ColorSlots];
            return a;
        }

        /// <summary>k kovasında (katman ya da serbest) şu an bantta duran c rengi top sayısı.</summary>
        private static int BucketCount(BeltModel belt, State s, int k, int c)
        {
            int n = belt.initial[k][c] - s.taken[k * ColorSlots + c];
            if (k == FreeBucket) n += belt.prefix[s.taken[QueueIndex]][c];
            return n;
        }

        /// <summary>Açık katman: topu kalan en dış katman (kalmadıysa Layers). Kilit kapalıysa hepsi açık.</summary>
        private static int OpenLayer(BeltModel belt, State s)
        {
            if (!belt.layerLock) return Layers;
            for (int k = 0; k < Layers; k++)
                for (int c = 0; c < ColorSlots; c++)
                    if (BucketCount(belt, s, k, c) > 0) return k;
            return Layers;
        }

        /// <summary>Çıkıştan şu an alınabilecek c rengi top: açık katmana kadar olan katmanlar + serbest kova.</summary>
        private static int BeltAvail(BeltModel belt, State s, int c)
        {
            int open = OpenLayer(belt, s);
            int n = BucketCount(belt, s, FreeBucket, c);
            for (int k = 0; k < Layers && k <= open; k++) n += BucketCount(belt, s, k, c);
            return n;
        }

        /// <summary>Banttaki toplam top (kilitli katmanlar dahil): kuyruğun girişi buna bağlı.</summary>
        private static int BeltTotal(BeltModel belt, State s)
        {
            int t = 0;
            for (int k = 0; k < Buckets; k++)
                for (int c = 0; c < ColorSlots; c++) t += BucketCount(belt, s, k, c);
            return t;
        }

        /// <summary>Şu an çıkıştan alınabilecek renkler (akıllı bot "gelecek renkler" olarak bunları görür).</summary>
        private static void BeltColors(BeltModel belt, State s, HashSet<PuzzleColor> into)
        {
            for (int c = 0; c < ColorSlots; c++)
                if (BeltAvail(belt, s, c) > 0) into.Add((PuzzleColor)c);
        }

        /// <summary>c renginden n topu alınabilir kovalardan düşer (önce açık katman, sonra serbest).</summary>
        private static void BeltTake(BeltModel belt, State s, int c, int n)
        {
            int open = OpenLayer(belt, s);
            for (int k = 0; k < Layers && k <= open && n > 0; k++)
            {
                int t = Mathf.Min(n, BucketCount(belt, s, k, c));
                s.taken[k * ColorSlots + c] += t;
                n -= t;
            }
            if (n > 0) s.taken[FreeBucket * ColorSlots + c] += n;
        }

        /// <summary>
        /// Dönen pistte kendiliğinden toplama: slottaki kutular bantta kendi renginden olanı alır, dolan kutu çıkar,
        /// banttan ayrılan topların yerine kuyruktan sırayla top katılır. Değişiklik kalmayana kadar tekrarlanır.
        /// </summary>
        private static void BeltClosure(State s, Block[] blocks, BeltModel belt, System.Action<int, int> onStepCompleted)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (s.removed[i] || s.inSlot == null || !s.inSlot[i]) continue;

                    int ballsBefore = 0, boxesBefore = 0;
                    if (onStepCompleted != null)
                    {
                        for (int j = 0; j < blocks.Length; j++)
                        {
                            ballsBefore += s.removed[j] ? blocks[j].count : s.fill[j];
                            if (s.removed[j]) boxesBefore++;
                        }
                    }

                    if (blocks[i].isFilledBox && (s.emptied == null || !s.emptied[i]))
                    {
                        if (s.supplied == null) s.supplied = new int[ColorSlots];
                        if (s.emptied == null) s.emptied = new bool[blocks.Length];
                        s.supplied[(int)blocks[i].filledBallColor] += blocks[i].count;
                        s.emptied[i] = true;
                        changed = true;
                        continue;
                    }

                    int c = (int)blocks[i].color;
                    int need = blocks[i].count - s.fill[i];
                    int take = Mathf.Min(need, BeltAvail(belt, s, c));
                    if (take > 0)
                    {
                        BeltTake(belt, s, c, take);
                        s.fill[i] += take;
                        need -= take;
                        changed = true;
                    }
                    if (need > 0 && s.supplied != null && s.supplied[c] > 0)
                    {
                        int sup = Mathf.Min(need, s.supplied[c]);
                        s.supplied[c] -= sup;
                        s.fill[i] += sup;
                        changed = true;
                    }
                    if (s.fill[i] >= blocks[i].count)
                    {
                        s.removed[i] = true;
                        changed = true;
                        onStepCompleted?.Invoke(ballsBefore, boxesBefore);
                    }
                }

                // Bantta yer açıldıkça kuyruktaki toplar sırayla katılır
                int total = BeltTotal(belt, s);
                while (total < belt.capacity && s.taken[QueueIndex] < belt.queue.Count)
                {
                    s.taken[QueueIndex]++;
                    total++;
                    changed = true;
                }
            }
        }
    }
}
