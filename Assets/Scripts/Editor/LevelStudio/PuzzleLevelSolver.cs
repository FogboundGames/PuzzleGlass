using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    public enum FindingSeverity { Critical, Warning, Info }

    public class LevelFinding
    {
        public FindingSeverity severity;
        public string text;
        public string why;
        /// <summary>İlgili blok (tıklayınca seçilir); yoksa -1.</summary>
        public int blockIndex = -1;
    }

    public enum SolveStatus { Solved, Unsolvable, LimitReached, Invalid }

    public class SolveResult
    {
        public SolveStatus status;
        /// <summary>Çözüm için gereken en az kaydırma sayısı (Solved ise).</summary>
        public int minSlides;
        public int exploredStates;
        /// <summary>Başlangıçta hiç kaydırmadan toplayabilen blok sayısı.</summary>
        public int openingCollectors;
        /// <summary>Çözüm yolu: sırayla (blok, tek hücrelik kaydırma yönü). Solved ise dolu.</summary>
        public List<(int block, Vector2Int dir)> moves = new List<(int, Vector2Int)>();

        /// <summary>
        /// Oyuncunun yapacağı sürükleme sayısı: aynı bloğun art arda kaydırmaları tek sürükleme sayılır
        /// (alttaki bloğu en üste çıkarmak 3 kaydırma ama 1 sürüklemedir). Zorluk bununla ölçülür.
        /// </summary>
        public int Drags
        {
            get
            {
                if (moves == null || moves.Count == 0) return 0;
                int n = 1;
                for (int i = 1; i < moves.Count; i++) if (moves[i].block != moves[i - 1].block) n++;
                return n;
            }
        }

        /// <summary>
        /// Öğretici için ilk sürükleme: çözümün başındaki aynı bloğa ait ardışık kaydırmalar birleştirilir.
        /// Hamle yoksa false.
        /// </summary>
        public bool TryGetFirstDrag(out int block, out Vector2Int totalOffset)
        {
            block = -1;
            totalOffset = Vector2Int.zero;
            if (moves == null || moves.Count == 0) return false;
            block = moves[0].block;
            foreach (var m in moves)
            {
                if (m.block != block) break;
                totalOffset += m.dir;
            }
            return true;
        }
    }

    /// <summary>
    /// Şu anki oyun kurallarıyla level çözücü (Level Stüdyosu için):
    ///  - Bloklar tepside kayar (çakışmadan, sınır içinde, birer hücre).
    ///  - En üst sıraya değen blok kendiliğinden toplar: sadece kendi sütunlarının (üstünde başka blok
    ///    olmayan) hizasındaki camın EN ALTTAKİ topunu alır. Deliğin üstünde uygun top yoksa bir şey inmez.
    ///  - Alttaki top gidince o sütundaki toplar bir sıra iner.
    ///  - Dolan blok tepsiden çıkar ve yerini boşaltır.
    /// En az kaydırmayla çözümü genişlik öncelikli arar; arama sınırı aşılırsa "belirsiz" der.
    /// Cam dizilimi OrderedPoolLayout ile oyundakiyle aynı hesaplanır.
    /// </summary>
    public static class PuzzleLevelSolver
    {
        public const int DefaultStateLimit = 20000;

        public struct GlassDims
        {
            public float innerHalfW, floorY, ceilY;
            public static GlassDims Default => new GlassDims { innerHalfW = 1.32f, floorY = -0.56f, ceilY = 0.62f };
        }

        private class Block
        {
            public PuzzleColor color;
            public int count;
            public Vector2Int[] cells;
            public bool hasKey;
            public int lockKeyCount;
            public int iceCount;
        }

        // ==================== Kural kontrolleri ====================

        public static List<LevelFinding> Check(PuzzleGlassLevelData lvl, SolveResult solve)
        {
            var list = new List<LevelFinding>();
            if (lvl == null) return list;
            int cols = lvl.stagingGridCols, rows = lvl.stagingGridRows;
            var monsters = lvl.monsters ?? new List<LevelMonsterEntry>();

            if (monsters.Count == 0)
            {
                list.Add(new LevelFinding { severity = FindingSeverity.Critical, text = "Levelde hiç blok yok.", why = "Fırçayla tepsiye blok ekle." });
                return list;
            }

            // Sınır dışı / çakışma
            var owner = new int[cols, rows];
            for (int x = 0; x < cols; x++) for (int y = 0; y < rows; y++) owner[x, y] = -1;
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m == null) continue;
                foreach (var c in CarrierBlockShapeUtility.GetCells(m.blockShape))
                {
                    int x = m.gridAnchor.x + c.x, y = m.gridAnchor.y + c.y;
                    if (x < 0 || y < 0 || x >= cols || y >= rows)
                    {
                        list.Add(new LevelFinding { severity = FindingSeverity.Critical, blockIndex = i, text = $"Blok #{i + 1} tepsinin dışına taşıyor.", why = "Bloğu tepsi içine sürükle ya da tepsiyi büyüt." });
                        break;
                    }
                    if (owner[x, y] >= 0)
                    {
                        list.Add(new LevelFinding { severity = FindingSeverity.Critical, blockIndex = i, text = $"Blok #{i + 1} ile #{owner[x, y] + 1} üst üste biniyor.", why = "Bloklardan birini boş hücreye sürükle." });
                        break;
                    }
                    owner[x, y] = i;
                }
            }

            // Renk dengesi: camdaki top sayısı = o rengin kutularındaki yuva sayısı
            var glass = new Dictionary<PuzzleColor, int>();
            var sockets = new Dictionary<PuzzleColor, int>();
            if (lvl.bottomToTopGroups != null)
                foreach (var g in lvl.bottomToTopGroups) if (g != null) glass[g.color] = (glass.TryGetValue(g.color, out int v) ? v : 0) + Mathf.Max(1, g.count);
            foreach (var m in monsters) if (m != null) sockets[m.color] = (sockets.TryGetValue(m.color, out int v) ? v : 0) + Mathf.Max(1, m.count);
            var allColors = new HashSet<PuzzleColor>(glass.Keys);
            allColors.UnionWith(sockets.Keys);
            foreach (var c in allColors)
            {
                glass.TryGetValue(c, out int gc);
                sockets.TryGetValue(c, out int sc);
                if (gc != sc)
                    list.Add(new LevelFinding { severity = FindingSeverity.Critical, text = $"{ColorName(c)}: camda {gc} top var, kutularda {sc} yuva.", why = "Her renk için camdaki top sayısı kutu yuvalarının toplamına eşit olmalı. Cam sırasını kutulardan yeniden kurabilirsin." });
            }

            // Tepside boş hücre
            int occupied = 0;
            for (int x = 0; x < cols; x++) for (int y = 0; y < rows; y++) if (owner[x, y] >= 0) occupied++;
            if (occupied >= cols * rows)
                list.Add(new LevelFinding { severity = FindingSeverity.Warning, text = "Tepside boş hücre yok: hiçbir blok kaydırılamaz.", why = "Oyuncu sadece en üst sıradaki bloklarla başlayabilir." });

            // Anahtar / kilit / buz tutarlılığı
            int totalKeyBlocks = 0;
            int totalBoxSockets = 0;
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var m = lvl.monsters[i];
                if (m == null) continue;
                if (m.hasKey) totalKeyBlocks++;
                totalBoxSockets += Mathf.Max(1, m.count);
            }
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var m = lvl.monsters[i];
                if (m == null) continue;
                if (m.lockKeyCount > 0)
                {
                    int availableKeys = totalKeyBlocks - (m.hasKey ? 1 : 0);
                    if (m.lockKeyCount > availableKeys)
                        list.Add(new LevelFinding { severity = FindingSeverity.Critical, text = $"{i + 1}. blok ({ColorName(m.color)}) {m.lockKeyCount} anahtar istiyor ama onu açabilecek {availableKeys} anahtarlı blok var.", why = "Kilit sayısı kadar ayrı anahtarlı kutu ekle veya kilit sayısını düşür." });
                }
                if (m.iceCount > 0)
                {
                    int maxProgress = lvl.iceMeltMode == IceMeltMode.PerBallCollected
                        ? Mathf.Max(0, totalBoxSockets - Mathf.Max(1, m.count))
                        : Mathf.Max(0, lvl.monsters.Count - 1);
                    if (m.iceCount > maxProgress)
                    {
                        string unit = lvl.iceMeltMode == IceMeltMode.PerBallCollected ? "top" : "kutu";
                        list.Add(new LevelFinding { severity = FindingSeverity.Critical, text = $"{i + 1}. blok ({ColorName(m.color)}) {m.iceCount} buz istiyor ama kendisi donukken en fazla {maxProgress} {unit} tamamlanabilir.", why = "Buz sayacını düşür veya başka kutu/yuva ekle." });
                    }
                }
            }

            if (solve != null)
            {
                switch (solve.status)
                {
                    case SolveStatus.Unsolvable:
                        list.Add(new LevelFinding { severity = FindingSeverity.Critical, text = "Level çözülemiyor: hangi sırayla oynanırsa oynansın bloklar dolmuyor.", why = "Cam sırasını değiştir (gerekli renkler alta), blokları farklı sütunlara koy ya da tepsiye boşluk aç." });
                        break;
                    case SolveStatus.LimitReached:
                        list.Add(new LevelFinding { severity = FindingSeverity.Warning, text = $"Çözüm aranırken sınıra ulaşıldı ({solve.exploredStates} durum); çözülebilirliği kesin değil.", why = "Çok büyük tepsi/çok blok aramayı uzatır. Oynayarak doğrula." });
                        break;
                    case SolveStatus.Solved:
                        if (solve.openingCollectors == 0)
                            list.Add(new LevelFinding { severity = FindingSeverity.Info, text = "Başta hiçbir blok toplayamıyor; oyuncu önce kaydırmalı.", why = "Zor açılış. Kolay levellerde ilk hamlede toplanabilen bir blok iyi olur." });
                        GetTargetDrags(lvl.difficulty, out int lo, out int hi);
                        int drags = solve.Drags;
                        if (drags > hi)
                            list.Add(new LevelFinding { severity = FindingSeverity.Warning, text = $"{DifficultyName(lvl.difficulty)} level için uzun: en az {drags} sürükleme (hedef {lo}-{hi}).", why = "Zorluğu yükselt ya da 'Otomatik üret' ile bu zorlukta yeniden üret." });
                        else if (drags < lo)
                            list.Add(new LevelFinding { severity = FindingSeverity.Warning, text = $"{DifficultyName(lvl.difficulty)} level için kısa: {drags} sürüklemede çözülüyor (hedef {lo}-{hi}).", why = "Zorluğu düşür ya da daha çok blok/daha az boşluk ile yeniden üret." });
                        break;
                }
            }
            return list;
        }

        // ==================== Çözücü ====================

        /// <summary>Tek seferde çözer (örnek level üretimi gibi toplu işler için).</summary>
        public static SolveResult Solve(PuzzleGlassLevelData lvl, GlassDims dims, int stateLimit = DefaultStateLimit)
        {
            var run = new Run(lvl, dims, stateLimit);
            while (!run.Step(double.MaxValue)) { }
            return run.Result;
        }

        /// <summary>
        /// Parça parça ilerleyen çözüm: her Step çağrısı verilen süre kadar arar ve döner.
        /// Level Stüdyosu bunu her editör karesinde birkaç milisaniye çalıştırır; Unity hiç donmaz.
        /// </summary>
        public sealed class Run
        {
            public SolveResult Result { get; private set; }
            public bool Done { get; private set; }

            private readonly int m_Limit, m_Cols, m_Rows, m_N;
            private readonly IceMeltMode m_IceMeltMode;
            private readonly Block[] m_Blocks;
            private readonly List<PuzzleColor>[] m_Stacks;
            private readonly int[] m_StackGridCol;
            private readonly HashSet<string> m_Visited = new HashSet<string>();
            private readonly Queue<(State s, int depth, int node)> m_Queue = new Queue<(State s, int depth, int node)>();
            // Çözüm yolunu geri kurmak için: her düğümün ebeveyni ve oraya gelen hamle
            private readonly List<(int parent, int block, Vector2Int dir)> m_Nodes = new List<(int, int, Vector2Int)>();
            private static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            public Run(PuzzleGlassLevelData lvl, GlassDims dims, int stateLimit = DefaultStateLimit)
            {
                Result = new SolveResult { status = SolveStatus.Invalid };
                m_Limit = stateLimit;
                if (lvl == null || lvl.monsters == null || lvl.monsters.Count == 0) { Done = true; return; }

                m_Cols = lvl.stagingGridCols;
                m_Rows = lvl.stagingGridRows;
                m_IceMeltMode = lvl.iceMeltMode;
                m_N = lvl.monsters.Count;
                m_Blocks = new Block[m_N];
                var anchors = new Vector2Int[m_N];
                for (int i = 0; i < m_N; i++)
                {
                    var m = lvl.monsters[i];
                    if (m == null) { Done = true; return; }
                    m_Blocks[i] = new Block
                    {
                        color = m.color,
                        count = Mathf.Max(1, m.count),
                        cells = CarrierBlockShapeUtility.GetCells(m.blockShape),
                        hasKey = m.hasKey,
                        lockKeyCount = Mathf.Max(0, m.lockKeyCount),
                        iceCount = Mathf.Max(0, m.iceCount)
                    };
                    anchors[i] = m.gridAnchor;
                }
                if (!IsValidPlacement(m_Blocks, anchors, new bool[m_N], m_Cols, m_Rows)) { Done = true; return; }

                // Cam: top sütunları (alttan üste) ve her sütunun hangi tepsi sütununun üstünde olduğu
                var layout = GlassShapeData.BuildLayout(lvl);
                int bc = Mathf.Max(1, layout.totalColumns + 3);
                m_Stacks = new List<PuzzleColor>[bc];
                m_StackGridCol = new int[bc];
                for (int c = 0; c < bc; c++) { m_Stacks[c] = new List<PuzzleColor>(); m_StackGridCol[c] = -1; }
                var sorted = new List<OrderedPoolLayout.PoolBall>(layout.balls);
                sorted.Sort((a, b) => a.row.CompareTo(b.row));
                foreach (var b in sorted)
                {
                    int col = Mathf.Clamp(b.ballColumn, 0, bc - 1);
                    m_Stacks[col].Add(b.color);
                    if (m_StackGridCol[col] < 0)
                        m_StackGridCol[col] = OrderedPoolLayout.GridColumnForX(b.localPos.x, dims.innerHalfW, m_Cols);
                }

                var start = new State { anchors = anchors, removed = new bool[m_N], fill = new int[m_N], taken = new int[bc] };
                CollectClosure(start, m_Blocks, m_Stacks, m_StackGridCol, m_Rows, m_IceMeltMode);
                Result.openingCollectors = CountFilledAny(start);
                Result.status = SolveStatus.LimitReached; // bitene kadar
                m_Visited.Add(Key(start));
                m_Nodes.Add((-1, -1, Vector2Int.zero));
                m_Queue.Enqueue((start, 0, 0));
            }

            /// <summary>En fazla budgetMs milisaniye arar. Bittiyse true döner (Result hazır).</summary>
            public bool Step(double budgetMs)
            {
                if (Done) return true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (m_Queue.Count > 0)
                {
                    var (s, depth, node) = m_Queue.Dequeue();
                    Result.exploredStates++;
                    if (AllRemoved(s))
                    {
                        Result.status = SolveStatus.Solved;
                        Result.minSlides = depth;
                        // Yolu geri kur
                        var path = new List<(int, Vector2Int)>();
                        for (int k = node; k > 0; k = m_Nodes[k].parent) path.Add((m_Nodes[k].block, m_Nodes[k].dir));
                        path.Reverse();
                        Result.moves = path;
                        Done = true;
                        return true;
                    }
                    if (Result.exploredStates >= m_Limit)
                    {
                        Result.status = SolveStatus.LimitReached;
                        Done = true;
                        return true;
                    }

                    for (int i = 0; i < m_N; i++)
                    {
                        if (s.removed[i] || IsBlockBlocked(i, s, m_Blocks, m_IceMeltMode)) continue;
                        foreach (var d in Dirs)
                        {
                            var next = s.Clone();
                            next.anchors[i] = s.anchors[i] + d;
                            if (!IsValidPlacement(m_Blocks, next.anchors, next.removed, m_Cols, m_Rows)) continue;
                            CollectClosure(next, m_Blocks, m_Stacks, m_StackGridCol, m_Rows, m_IceMeltMode);
                            if (m_Visited.Add(Key(next)))
                            {
                                m_Nodes.Add((node, i, d));
                                m_Queue.Enqueue((next, depth + 1, m_Nodes.Count - 1));
                            }
                        }
                    }

                    if (sw.Elapsed.TotalMilliseconds >= budgetMs) return false;
                }

                Result.status = SolveStatus.Unsolvable;
                Done = true;
                return true;
            }
        }

        private class State
        {
            public Vector2Int[] anchors;
            public bool[] removed;
            public int[] fill;
            public int[] taken;

            public State Clone() => new State
            {
                anchors = (Vector2Int[])anchors.Clone(),
                removed = (bool[])removed.Clone(),
                fill = (int[])fill.Clone(),
                taken = (int[])taken.Clone()
            };
        }

        private static bool AllRemoved(State s)
        {
            for (int i = 0; i < s.removed.Length; i++) if (!s.removed[i]) return false;
            return true;
        }

        private static int CountFilledAny(State s)
        {
            int k = 0;
            for (int i = 0; i < s.fill.Length; i++) if (s.fill[i] > 0 || s.removed[i]) k++;
            return k;
        }

        private static string Key(State s)
        {
            var sb = new StringBuilder(s.anchors.Length * 6 + s.taken.Length * 2);
            for (int i = 0; i < s.anchors.Length; i++)
            {
                if (s.removed[i]) sb.Append('x');
                else sb.Append(s.anchors[i].x).Append(',').Append(s.anchors[i].y).Append(':').Append(s.fill[i]);
                sb.Append(';');
            }
            sb.Append('|');
            for (int c = 0; c < s.taken.Length; c++) sb.Append(s.taken[c]).Append(',');
            return sb.ToString();
        }

        private static bool IsValidPlacement(Block[] blocks, Vector2Int[] anchors, bool[] removed, int cols, int rows)
        {
            var occ = new bool[cols, rows];
            for (int i = 0; i < blocks.Length; i++)
            {
                if (removed[i]) continue;
                foreach (var c in blocks[i].cells)
                {
                    int x = anchors[i].x + c.x, y = anchors[i].y + c.y;
                    if (x < 0 || y < 0 || x >= cols || y >= rows || occ[x, y]) return false;
                    occ[x, y] = true;
                }
            }
            return true;
        }

        private static bool IsBlockBlocked(int i, State s, Block[] blocks, IceMeltMode iceMeltMode)
        {
            var b = blocks[i];
            if (b.lockKeyCount > 0)
            {
                int keysCollected = 0;
                for (int j = 0; j < blocks.Length; j++)
                    if (s.removed[j] && blocks[j].hasKey) keysCollected++;
                if (keysCollected < b.lockKeyCount) return true;
            }
            if (b.iceCount > 0)
            {
                int progress = 0;
                if (iceMeltMode == IceMeltMode.PerBallCollected)
                {
                    for (int c = 0; c < s.taken.Length; c++) progress += s.taken[c];
                }
                else
                {
                    for (int j = 0; j < blocks.Length; j++)
                        if (s.removed[j]) progress++;
                }
                if (progress < b.iceCount) return true;
            }
            return false;
        }

        /// <summary>
        /// Oyundaki kendiliğinden toplama: en üst sıradaki bloklar kendi açık sütunlarının hizasındaki
        /// en alt topları alır; dolan blok çıkar. Değişiklik kalmayana kadar tekrarlar.
        /// </summary>
        private static void CollectClosure(State s, Block[] blocks, List<PuzzleColor>[] stacks, int[] stackGridCol, int rows, IceMeltMode iceMeltMode)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (s.removed[i] || IsBlockBlocked(i, s, blocks, iceMeltMode)) continue;
                    var open = OpenColumns(i, s, blocks, rows);
                    if (open == null) continue;

                    for (int c = 0; c < stacks.Length && s.fill[i] < blocks[i].count; c++)
                    {
                        if (!open.Contains(stackGridCol[c])) continue;
                        while (s.taken[c] < stacks[c].Count && stacks[c][s.taken[c]] == blocks[i].color && s.fill[i] < blocks[i].count)
                        {
                            s.taken[c]++;
                            s.fill[i]++;
                            changed = true;
                        }
                    }
                    if (s.fill[i] >= blocks[i].count)
                    {
                        s.removed[i] = true;
                        changed = true;
                    }
                }
            }
        }

        /// <summary>
        /// Blok en üst sıraya değiyorsa, üstünde başka blok olmayan sütunları (deliği açık sütunlar); değmiyorsa null.
        /// StagingGridBoard.IsWorldXAlignedWithBlockTopColumns ile aynı kural.
        /// </summary>
        private static HashSet<int> OpenColumns(int i, State s, Block[] blocks, int rows)
        {
            bool atTop = false;
            foreach (var c in blocks[i].cells) if (s.anchors[i].y + c.y >= rows - 1) { atTop = true; break; }
            if (!atTop) return null;

            var result = new HashSet<int>();
            foreach (var c in blocks[i].cells)
            {
                int cx = s.anchors[i].x + c.x, cy = s.anchors[i].y + c.y;
                bool blocked = false;
                for (int j = 0; j < blocks.Length && !blocked; j++)
                {
                    if (j == i || s.removed[j]) continue;
                    foreach (var o in blocks[j].cells)
                        if (s.anchors[j].x + o.x == cx && s.anchors[j].y + o.y > cy) { blocked = true; break; }
                }
                if (!blocked) result.Add(cx);
            }
            return result;
        }

        /// <summary>
        /// Zorluk eğrisi hedefi: en kısa çözümdeki sürükleme sayısının bu zorlukta olması gereken aralık.
        /// Kolay 1-3, Orta 3-6, Zor 5-9, Boss 8+.
        /// </summary>
        public static void GetTargetDrags(LevelDifficulty d, out int lo, out int hi)
        {
            switch (d)
            {
                case LevelDifficulty.Easy: lo = 1; hi = 3; break;
                case LevelDifficulty.Medium: lo = 3; hi = 6; break;
                case LevelDifficulty.Hard: lo = 5; hi = 9; break;
                default: lo = 8; hi = 99; break;
            }
        }

        public static string DifficultyName(LevelDifficulty d)
        {
            switch (d)
            {
                case LevelDifficulty.Easy: return "Kolay";
                case LevelDifficulty.Medium: return "Orta";
                case LevelDifficulty.Hard: return "Zor";
                default: return "Boss";
            }
        }

        /// <summary>
        /// Leveli verilen zorlukta, kaydırma sayısı hedef aralığa düşene kadar farklı tohumlarla üretir
        /// (cam ve tepsi dışındaki içerik yenilenir). En iyi sonucu döner; hedefe girilemezse en yakını kalır.
        /// </summary>
        public static SolveResult GenerateForTarget(PuzzleGlassLevelData lvl, LevelDifficulty diff, int tries = 12, int seed = 0, int stateLimit = 6000, int boxes = 0, int colors = 0)
        {
            GetTargetDrags(diff, out int lo, out int hi);
            var rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            // Cam ayarlarını koru: üretici camı sıfırlıyor
            var keep = UnityEngine.ScriptableObject.CreateInstance<PuzzleGlassLevelData>();
            UnityEditor.EditorUtility.CopySerialized(lvl, keep);

            int bestSeed = 0, bestDist = int.MaxValue;
            SolveResult best = null;
            for (int t = 0; t < tries; t++)
            {
                int s = rng.Next(1, int.MaxValue);
                lvl.difficulty = diff;
                Generate(lvl, s, boxes, colors);
                RestoreGlass(lvl, keep);
                var r = Solve(lvl, GlassShapeData.GetDims(lvl), stateLimit);
                if (r.status != SolveStatus.Solved) continue;
                int dg = r.Drags;
                int dist = dg < lo ? lo - dg : (dg > hi ? dg - hi : 0);
                if (dist < bestDist) { bestDist = dist; bestSeed = s; best = r; }
                if (dist == 0) break;
            }
            if (best != null && bestSeed != 0)
            {
                lvl.difficulty = diff;
                Generate(lvl, bestSeed, boxes, colors);
                RestoreGlass(lvl, keep);
            }
            UnityEngine.Object.DestroyImmediate(keep);
            return best;
        }

        private static void Generate(PuzzleGlassLevelData lvl, int seed, int boxes, int colors)
        {
            PuzzleLevelGenerator.GenerateForLevelData(lvl,
                customColorCount: colors > 0 ? colors : (int?)null,
                customTotalBoxes: boxes > 0 ? boxes : (int?)null,
                randomSeed: seed);
        }

        private static void RestoreGlass(PuzzleGlassLevelData lvl, PuzzleGlassLevelData from)
        {
            lvl.useCustomGlassShape = from.useCustomGlassShape;
            lvl.glassIsBottle = from.glassIsBottle;
            lvl.glassPreset = from.glassPreset;
            lvl.contourPoints = from.contourPoints != null ? new List<GlassContourPoint>(from.contourPoints) : new List<GlassContourPoint>();
            lvl.symmetricMode = from.symmetricMode;
            lvl.widthScale = from.widthScale;
            lvl.heightScale = from.heightScale;
            lvl.showShoulderHandles = from.showShoulderHandles;
            lvl.colorTheme = from.colorTheme;
            lvl.glassBodyColor = from.glassBodyColor;
            lvl.glassCenterGlowColor = from.glassCenterGlowColor;
            lvl.glassOuterRimColor = from.glassOuterRimColor;
            lvl.glassInnerRimColor = from.glassInnerRimColor;
            lvl.boxCornerRadius = from.boxCornerRadius;
            lvl.boxFrameThickness = from.boxFrameThickness;
            lvl.boxFrameColor = from.boxFrameColor;
            lvl.boxFrameHighlightColor = from.boxFrameHighlightColor;
            lvl.boxFrameEdgeShadeColor = from.boxFrameEdgeShadeColor;
            lvl.useBallFeedTubes = from.useBallFeedTubes;
            lvl.feedTubeMode = from.feedTubeMode;
            lvl.feedTubeContainerLimit = from.feedTubeContainerLimit;
            lvl.feedTubeQueueVisibleCount = from.feedTubeQueueVisibleCount;
            lvl.feedTubeWidthMultiplier = from.feedTubeWidthMultiplier;
            lvl.feedTubeAnchorSpread = from.feedTubeAnchorSpread;
            lvl.feedTubeDropInterval = from.feedTubeDropInterval;
            lvl.levelName = from.levelName;
            lvl.tutorialHint = from.tutorialHint;
            lvl.tutorialBlockIndex = -1; // içerik değişti, ipucu yeniden alınmalı
        }

        public static string ColorName(PuzzleColor c)
        {
            switch (c)
            {
                case PuzzleColor.Blue: return "Mavi";
                case PuzzleColor.Yellow: return "Sarı";
                case PuzzleColor.Red: return "Kırmızı";
                case PuzzleColor.Green: return "Yeşil";
                case PuzzleColor.Purple: return "Mor";
                case PuzzleColor.Orange: return "Turuncu";
                case PuzzleColor.Pink: return "Pembe";
                case PuzzleColor.Black: return "Siyah";
                case PuzzleColor.White: return "Beyaz";
                case PuzzleColor.Cyan: return "Turkuaz";
                case PuzzleColor.Brown: return "Kahverengi";
                default: return c.ToString();
            }
        }
    }
}
