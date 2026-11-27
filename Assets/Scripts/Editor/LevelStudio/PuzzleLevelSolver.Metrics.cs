using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Bir levelin "affedicilik" ölçüleri (slot modeli, sadece slota gönderme — oyuncunun gerçek hamlesi):
    /// hata payı (en az kaç slotla çözülüyor), adil başlangıç (ilk hamlelerden kaçı kaçınılmaz kayba götürüyor),
    /// kritik anlar (çözüm yolunda tek doğru hamlenin olduğu adımlar).
    /// </summary>
    public class LevelMetrics
    {
        /// <summary>Tüm ölçümler arama sınırına takılmadan bitti mi? false ise değerler alt/üst sınırdır.</summary>
        public bool complete = true;
        /// <summary>Levelin oyundaki slot sayısı.</summary>
        public int slotCapacity;
        /// <summary>Levelin çözülebildiği en az slot sayısı; -1 = ölçülemedi.</summary>
        public int minSlots = -1;
        /// <summary>Başlangıçta yapılabilecek hamle sayısı ve bunlardan kaçının level'ı çözülemez hale getirdiği.</summary>
        public int firstMoves, firstMoveTraps;
        /// <summary>Çözüm yolundaki adım sayısı; birden çok seçenek olan adımlar; tek güvenli hamlenin olduğu adımlar.</summary>
        public int pathSteps, decisionSteps, criticalSteps;
        /// <summary>Çözüm yolu boyunca görülen tüm seçenekler ve bunlardan kaybettirenler.</summary>
        public int totalChoices, trapChoices;

        /// <summary>Kalan slot payı (oyundaki slot − gereken en az slot).</summary>
        public int Slack => minSlots > 0 ? slotCapacity - minSlots : -1;
        /// <summary>Çözüm yolu boyunca rastgele seçilen bir hamlenin level'ı kaybettirme oranı (0-1).</summary>
        public float TrapRate => totalChoices > 0 ? (float)trapChoices / totalChoices : 0f;
    }

    public static partial class PuzzleLevelSolver
    {
        public const int DefaultMetricsNodeLimit = 60000;

        /// <summary>
        /// Ölçülen kilitlenme oranlarından 0-100 sürekli zorluk puanı. Bant sınırları DifficultyFromRates ile aynıdır:
        /// Kolay 0-24, Orta 25-49, Zor 50-74, Boss 75-100. Eğri çizmek ve level'ları karşılaştırmak için.
        /// </summary>
        public static int DifficultyScore(float randomFailRate, float smartFailRate)
        {
            if (randomFailRate < 0f || smartFailRate < 0f) return -1;
            float s = smartFailRate, r = randomFailRate, v;
            switch (DifficultyFromRates(r, s))
            {
                case LevelDifficulty.Easy:
                    v = 25f * Mathf.Clamp01(0.5f * s / EasyMaxSmart + 0.5f * r / EasyMaxRandom);
                    return Mathf.Min(24, Mathf.RoundToInt(v));
                case LevelDifficulty.Medium:
                    v = 25f + 25f * Mathf.Clamp01(s / MediumMaxSmart);
                    return Mathf.Clamp(Mathf.RoundToInt(v), 25, 49);
                case LevelDifficulty.Hard:
                    v = 50f + 25f * Mathf.Clamp01((s - MediumMaxSmart) / (HardMaxSmart - MediumMaxSmart));
                    return Mathf.Clamp(Mathf.RoundToInt(v), 50, 74);
                default:
                    v = 75f + 25f * Mathf.Clamp01((s - HardMaxSmart) / (1f - HardMaxSmart));
                    return Mathf.Clamp(Mathf.RoundToInt(v), 75, 100);
            }
        }

        public static int DifficultyScore(SolveResult s) => s != null && s.HasDifficultyRates ? DifficultyScore(s.randomFailRate, s.smartFailRate) : -1;

        /// <summary>Affedicilik ölçülerinden tasarım uyarıları (Check içinden çağrılır).</summary>
        private static void AddMetricFindings(PuzzleGlassLevelData lvl, SolveResult solve, List<LevelFinding> list)
        {
            var m = solve.metrics;
            if (m == null) return;
            var diff = solve.HasDifficultyRates ? DifficultyFromRates(solve.randomFailRate, solve.smartFailRate) : lvl.difficulty;
            bool gentle = diff == LevelDifficulty.Easy || diff == LevelDifficulty.Medium;

            if (m.minSlots < 0)
            {
                if (!m.complete)
                    list.Add(new LevelFinding { severity = FindingSeverity.Info, text = "Hata payı ölçülemedi: arama sınırına ulaşıldı.", why = "Çok blok/çok renk aramayı uzatır. Oynayarak doğrula." });
                else
                    list.Add(new LevelFinding { severity = FindingSeverity.Warning, text = "Sadece slota göndererek çözülemiyor; çözüm kaydırma gerektiriyor.", why = "Oyuncu blokları kaydıramıyorsa level oyunda çözülemez. Blok sırasını değiştir." });
                return;
            }

            if (m.firstMoveTraps > 0)
            {
                list.Add(new LevelFinding
                {
                    severity = gentle ? FindingSeverity.Warning : FindingSeverity.Info,
                    text = $"Adil başlangıç: ilk hamledeki {m.firstMoves} seçenekten {m.firstMoveTraps} tanesi level'ı kaçınılmaz kayba götürüyor.",
                    why = gentle
                        ? "Oyuncu daha ilk dokunuşta, bilmeden level'ı kaybedebilir. Kolay/orta levellerde açılışta tuzak olmasın."
                        : "Zor levellerde kabul edilebilir; oyuncu bunu ancak tekrar oynayınca anlar."
                });
            }

            if (m.minSlots >= m.slotCapacity && gentle)
            {
                list.Add(new LevelFinding
                {
                    severity = FindingSeverity.Warning,
                    text = $"Hata payı yok: çözüm {m.slotCapacity} slotun hepsini gerektiriyor.",
                    why = "Tek bir gereksiz gönderme level'ı kilitler. Kolay/orta levellerde en az 1 slot boşta kalabilmeli."
                });
            }
            else if (diff == LevelDifficulty.Boss && m.Slack >= 3)
            {
                list.Add(new LevelFinding
                {
                    severity = FindingSeverity.Info,
                    text = $"Boss level ama {m.minSlots} slotla çözülüyor ({m.Slack} slot boşta).",
                    why = "Slot baskısı düşük; zorluk yalnızca dağılımdan geliyor. İstenen buysa sorun yok."
                });
            }

            if (gentle && m.criticalSteps >= 3)
            {
                list.Add(new LevelFinding
                {
                    severity = FindingSeverity.Info,
                    text = $"Çözüm yolunda {m.criticalSteps} adımda tek doğru hamle var.",
                    why = "Bu adımlarda başka her seçim level'ı kaybettiriyor. Bu zorluk için fazla dar olabilir."
                });
            }
        }

        /// <summary>Tek seferde ölçer (toplu işler için).</summary>
        public static LevelMetrics MeasureMetrics(PuzzleGlassLevelData lvl, GlassDims dims, int nodeLimit = DefaultMetricsNodeLimit)
        {
            var run = new MetricsRun(lvl, dims, nodeLimit);
            while (!run.Step(double.MaxValue)) { }
            return run.Result;
        }

        /// <summary>
        /// Slot modelinde "bu durumdan çözüme ulaşılabilir mi?" sorusunu yanıtlar. Hamleler geri alınamaz
        /// (her hamle bir bloğu slota gönderir), yani durum grafiği döngüsüzdür: tamamen aranıp çözüm bulunmayan durum kesin ölüdür.
        /// Ölü/canlı durumlar sorgular arasında paylaşılır; aynı alt ağaç bir daha aranmaz.
        /// </summary>
        private sealed class SlotSolvability
        {
            public enum Answer { Running, Yes, No, Unknown }

            private sealed class Frame
            {
                public State s;
                public string key;
                public List<State> children;
                public int next;
            }

            private readonly Block[] m_Blocks;
            private readonly bool[,] m_Walls;
            private readonly List<PuzzleColor>[] m_W1, m_W2;
            private readonly int[] m_Gcol;
            private readonly bool m_HasW2, m_IsTrack;
            private readonly int m_Cols, m_Rows, m_Cap;
            private readonly IceMeltMode m_Ice;
            private readonly HashSet<string> m_Dead = new HashSet<string>();
            private readonly HashSet<string> m_Alive = new HashSet<string>();
            private readonly Stack<Frame> m_Stack = new Stack<Frame>();

            public List<State> LastPath;

            public SlotSolvability(Block[] blocks, bool[,] walls, List<PuzzleColor>[] w1, List<PuzzleColor>[] w2, int[] gcol,
                bool hasW2, bool isTrack, int cols, int rows, int cap, IceMeltMode ice)
            {
                m_Blocks = blocks; m_Walls = walls; m_W1 = w1; m_W2 = w2; m_Gcol = gcol;
                m_HasW2 = hasW2; m_IsTrack = isTrack; m_Cols = cols; m_Rows = rows; m_Cap = cap; m_Ice = ice;
            }

            /// <summary>Bu durumdan slota gönderilebilecek her bloğun sonucu (toplama kapanışı uygulanmış).</summary>
            public List<State> Children(State s)
            {
                var list = new List<State>();
                for (int i = 0; i < m_Blocks.Length; i++)
                {
                    if (!InGrid(s, i) || IsBlockBlocked(i, s, m_Blocks, m_Ice)) continue;
                    var next = s.Clone();
                    if (!ApplyMove(next, i, SolveResult.SlotMove, m_Blocks, m_Walls, m_Cols, m_Rows, m_Cap, m_Ice)) continue;
                    CollectClosure(next, m_Blocks, m_Walls, m_W1, m_W2, m_HasW2, m_Gcol, m_Rows, m_Ice, m_IsTrack);
                    list.Add(next);
                }
                return list;
            }

            /// <summary>Yeni sorgu başlatır. Cevap hemen belliyse Running dışında bir değer döner.</summary>
            public Answer Begin(State s)
            {
                m_Stack.Clear();
                LastPath = null;
                if (AllRemoved(s)) { LastPath = new List<State> { s }; return Answer.Yes; }
                string k = Key(s);
                if (m_Dead.Contains(k)) return Answer.No;
                if (m_Alive.Contains(k)) return Answer.Yes;
                m_Stack.Push(new Frame { s = s, key = k, children = Children(s) });
                return Answer.Running;
            }

            /// <summary>Sorguyu ilerletir. nodes: kalan düğüm bütçesi (tükenince Unknown).</summary>
            public Answer Step(ref int nodes, System.Diagnostics.Stopwatch sw, double budgetMs)
            {
                while (m_Stack.Count > 0)
                {
                    var f = m_Stack.Peek();
                    if (f.next >= f.children.Count)
                    {
                        m_Dead.Add(f.key);
                        m_Stack.Pop();
                        continue;
                    }
                    var c = f.children[f.next++];
                    bool won = AllRemoved(c);
                    string ck = won ? null : Key(c);
                    if (won || m_Alive.Contains(ck))
                    {
                        // Yığındaki her durum çözüme bağlı: canlı olarak işaretle, yolu kaydet
                        var path = new List<State>();
                        foreach (var fr in m_Stack) { m_Alive.Add(fr.key); path.Add(fr.s); }
                        path.Reverse();
                        path.Add(c);
                        LastPath = path;
                        m_Stack.Clear();
                        return Answer.Yes;
                    }
                    if (m_Dead.Contains(ck)) continue;
                    if (--nodes <= 0) { m_Stack.Clear(); return Answer.Unknown; }
                    m_Stack.Push(new Frame { s = c, key = ck, children = Children(c) });
                    if (sw.Elapsed.TotalMilliseconds >= budgetMs) return Answer.Running;
                }
                return Answer.No;
            }
        }

        /// <summary>
        /// Affedicilik ölçülerini editör karelerine bölerek hesaplar (UI donmaz). Sıra:
        /// 1) tam slotla çözüm yolu, 2) en az slot sayısı, 3) çözüm yolu boyunca her adımdaki tuzak hamleler.
        /// </summary>
        public sealed class MetricsRun
        {
            public bool Done { get; private set; }
            public LevelMetrics Result { get; } = new LevelMetrics();

            private enum Phase { FindPath, MinSlots, Traps, Finished }

            private readonly bool m_Valid;
            private readonly Block[] m_Blocks;
            private readonly bool[,] m_Walls;
            private readonly List<PuzzleColor>[] m_W1, m_W2;
            private readonly int[] m_Gcol;
            private readonly bool m_HasW2, m_IsTrack;
            private readonly int m_Cols, m_Rows, m_Cap;
            private readonly IceMeltMode m_Ice;
            private readonly State m_Start;
            private int m_Nodes;

            private Phase m_Phase = Phase.FindPath;
            private SlotSolvability m_Full;      // oyundaki slot sayısıyla (paylaşılan ölü/canlı kaydı)
            private SlotSolvability m_Reduced;   // en az slot aramasında o anki slot sayısı
            private int m_TryCap;
            private bool m_Querying;
            private List<State> m_Path;
            private int m_PathIdx;
            private List<State> m_StepChildren;
            private int m_ChildIdx, m_StepSafe;

            public MetricsRun(PuzzleGlassLevelData lvl, GlassDims dims, int nodeLimit = DefaultMetricsNodeLimit)
            {
                m_Nodes = Mathf.Max(1000, nodeLimit);
                m_Valid = BuildRuntimeModel(lvl, dims, out m_Blocks, out var anchors, out m_Walls, out m_W1, out m_W2, out m_Gcol, out m_HasW2, out int bc);
                if (!m_Valid) { Result.complete = false; Done = true; return; }
                m_Cols = lvl.stagingGridCols;
                m_Rows = lvl.stagingGridRows;
                m_Cap = GetSlotCapacity(lvl);
                m_Ice = lvl.iceMeltMode;
                m_IsTrack = GlassShapeData.IsTrack(lvl);
                int n = m_Blocks.Length;
                m_Start = new State { anchors = anchors, removed = new bool[n], fill = new int[n], taken = new int[bc], inSlot = new bool[n], wave = 0 };
                CollectClosure(m_Start, m_Blocks, m_Walls, m_W1, m_W2, m_HasW2, m_Gcol, m_Rows, m_Ice, m_IsTrack);
                Result.slotCapacity = m_Cap;
                m_Full = NewSolver(m_Cap);
            }

            private SlotSolvability NewSolver(int cap) =>
                new SlotSolvability(m_Blocks, m_Walls, m_W1, m_W2, m_Gcol, m_HasW2, m_IsTrack, m_Cols, m_Rows, cap, m_Ice);

            /// <summary>En fazla budgetMs milisaniye çalışır. Bittiyse true döner.</summary>
            public bool Step(double budgetMs)
            {
                if (Done) return true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (m_Phase != Phase.Finished)
                {
                    if (sw.Elapsed.TotalMilliseconds >= budgetMs) return false;
                    switch (m_Phase)
                    {
                        case Phase.FindPath: StepFindPath(sw, budgetMs); break;
                        case Phase.MinSlots: StepMinSlots(sw, budgetMs); break;
                        case Phase.Traps: StepTraps(sw, budgetMs); break;
                    }
                }
                Done = true;
                return true;
            }

            private SlotSolvability.Answer Ask(SlotSolvability solver, State s, System.Diagnostics.Stopwatch sw, double budgetMs)
            {
                if (!m_Querying)
                {
                    var a = solver.Begin(s);
                    if (a != SlotSolvability.Answer.Running) return a;
                    m_Querying = true;
                }
                var r = solver.Step(ref m_Nodes, sw, budgetMs);
                if (r != SlotSolvability.Answer.Running) m_Querying = false;
                return r;
            }

            private void StepFindPath(System.Diagnostics.Stopwatch sw, double budgetMs)
            {
                var a = Ask(m_Full, m_Start, sw, budgetMs);
                if (a == SlotSolvability.Answer.Running) return;
                if (a != SlotSolvability.Answer.Yes)
                {
                    // Sadece slota göndererek çözüm yok (ya da bulunamadı): ölçülecek yol yok
                    Result.complete = a == SlotSolvability.Answer.No;
                    m_Phase = Phase.Finished;
                    return;
                }
                m_Path = m_Full.LastPath;
                Result.minSlots = m_Cap;
                Result.pathSteps = m_Path.Count - 1;
                m_TryCap = m_Cap - 1;
                m_Phase = m_TryCap >= 1 ? Phase.MinSlots : Phase.Traps;
                if (m_Phase == Phase.MinSlots) m_Reduced = NewSolver(m_TryCap);
            }

            private void StepMinSlots(System.Diagnostics.Stopwatch sw, double budgetMs)
            {
                var a = Ask(m_Reduced, m_Start, sw, budgetMs);
                if (a == SlotSolvability.Answer.Running) return;
                if (a == SlotSolvability.Answer.Yes)
                {
                    Result.minSlots = m_TryCap;
                    m_TryCap--;
                    if (m_TryCap >= 1) { m_Reduced = NewSolver(m_TryCap); return; }
                }
                else if (a == SlotSolvability.Answer.Unknown)
                {
                    Result.complete = false;
                }
                m_Reduced = null;
                m_PathIdx = 0;
                m_StepChildren = null;
                m_Phase = Phase.Traps;
            }

            private void StepTraps(System.Diagnostics.Stopwatch sw, double budgetMs)
            {
                if (m_Nodes <= 0 && !m_Querying) { Result.complete = false; m_Phase = Phase.Finished; return; }
                // Son durum (kazanılmış) hariç yol üzerindeki her durum bir karar anıdır
                if (m_PathIdx >= m_Path.Count - 1) { m_Phase = Phase.Finished; return; }
                if (m_StepChildren == null)
                {
                    m_StepChildren = m_Full.Children(m_Path[m_PathIdx]);
                    m_ChildIdx = 0;
                    m_StepSafe = 0;
                }
                while (m_ChildIdx < m_StepChildren.Count)
                {
                    var a = Ask(m_Full, m_StepChildren[m_ChildIdx], sw, budgetMs);
                    if (a == SlotSolvability.Answer.Running) return;
                    if (a == SlotSolvability.Answer.Unknown) { Result.complete = false; m_Phase = Phase.Finished; return; }
                    if (a == SlotSolvability.Answer.Yes) m_StepSafe++;
                    m_ChildIdx++;
                    if (sw.Elapsed.TotalMilliseconds >= budgetMs && m_ChildIdx < m_StepChildren.Count) return;
                }

                int legal = m_StepChildren.Count, traps = legal - m_StepSafe;
                if (m_PathIdx == 0) { Result.firstMoves = legal; Result.firstMoveTraps = traps; }
                if (legal > 1)
                {
                    Result.decisionSteps++;
                    Result.totalChoices += legal;
                    Result.trapChoices += traps;
                    if (m_StepSafe == 1) Result.criticalSteps++;
                }
                m_PathIdx++;
                m_StepChildren = null;
            }
        }
    }
}
