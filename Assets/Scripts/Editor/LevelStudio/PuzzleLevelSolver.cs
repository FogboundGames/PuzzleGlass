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
                        if (lvl.difficulty == LevelDifficulty.Easy && solve.minSlides > 6)
                            list.Add(new LevelFinding { severity = FindingSeverity.Info, text = $"Kolay level için uzun: en az {solve.minSlides} kaydırma gerekiyor.", why = "Zorluğu Orta yapmayı ya da blokları sadeleştirmeyi düşün." });
                        if (lvl.difficulty >= LevelDifficulty.Hard && solve.minSlides <= 1)
                            list.Add(new LevelFinding { severity = FindingSeverity.Info, text = $"{(lvl.difficulty == LevelDifficulty.Boss ? "Boss" : "Zor")} level için çok kısa: {solve.minSlides} kaydırmada çözülüyor.", why = "Blokları kilitlemek için daha fazla blok ya da daha az boşluk ekle." });
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
            private readonly Block[] m_Blocks;
            private readonly List<PuzzleColor>[] m_Stacks;
            private readonly int[] m_StackGridCol;
            private readonly HashSet<string> m_Visited = new HashSet<string>();
            private readonly Queue<(State s, int depth)> m_Queue = new Queue<(State s, int depth)>();
            private static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            public Run(PuzzleGlassLevelData lvl, GlassDims dims, int stateLimit = DefaultStateLimit)
            {
                Result = new SolveResult { status = SolveStatus.Invalid };
                m_Limit = stateLimit;
                if (lvl == null || lvl.monsters == null || lvl.monsters.Count == 0) { Done = true; return; }

                m_Cols = lvl.stagingGridCols;
                m_Rows = lvl.stagingGridRows;
                m_N = lvl.monsters.Count;
                m_Blocks = new Block[m_N];
                var anchors = new Vector2Int[m_N];
                for (int i = 0; i < m_N; i++)
                {
                    var m = lvl.monsters[i];
                    if (m == null) { Done = true; return; }
                    m_Blocks[i] = new Block { color = m.color, count = Mathf.Max(1, m.count), cells = CarrierBlockShapeUtility.GetCells(m.blockShape) };
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
                CollectClosure(start, m_Blocks, m_Stacks, m_StackGridCol, m_Rows);
                Result.openingCollectors = CountFilledAny(start);
                Result.status = SolveStatus.LimitReached; // bitene kadar
                m_Visited.Add(Key(start));
                m_Queue.Enqueue((start, 0));
            }

            /// <summary>En fazla budgetMs milisaniye arar. Bittiyse true döner (Result hazır).</summary>
            public bool Step(double budgetMs)
            {
                if (Done) return true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (m_Queue.Count > 0)
                {
                    var (s, depth) = m_Queue.Dequeue();
                    Result.exploredStates++;
                    if (AllRemoved(s))
                    {
                        Result.status = SolveStatus.Solved;
                        Result.minSlides = depth;
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
                        if (s.removed[i]) continue;
                        foreach (var d in Dirs)
                        {
                            var next = s.Clone();
                            next.anchors[i] = s.anchors[i] + d;
                            if (!IsValidPlacement(m_Blocks, next.anchors, next.removed, m_Cols, m_Rows)) continue;
                            CollectClosure(next, m_Blocks, m_Stacks, m_StackGridCol, m_Rows);
                            if (m_Visited.Add(Key(next))) m_Queue.Enqueue((next, depth + 1));
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

        /// <summary>
        /// Oyundaki kendiliğinden toplama: en üst sıradaki bloklar kendi açık sütunlarının hizasındaki
        /// en alt topları alır; dolan blok çıkar. Değişiklik kalmayana kadar tekrarlar.
        /// </summary>
        private static void CollectClosure(State s, Block[] blocks, List<PuzzleColor>[] stacks, int[] stackGridCol, int rows)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (s.removed[i]) continue;
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
