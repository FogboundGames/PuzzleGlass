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
        /// <summary>Hamle yönü bu değerse kaydırma değil: blok slota gönderildi (yeni sistem).</summary>
        public static readonly Vector2Int SlotMove = new Vector2Int(0, 100);
        /// <summary>Çözüm slot modeliyle mi arandı? (yeni sistem: blok slota gider, slottaki blok toplar)</summary>
        public bool slotModel;
        /// <summary>Slot modelinde kullanılan slot sayısı.</summary>
        public int slotCapacity;
        /// <summary>Slot modelinde rastgele oynayan oyuncunun kilitlenme oranı (0-1). Zorluk ölçüsü; -1 = hesaplanmadı.</summary>
        public float randomFailRate = -1f;
        /// <summary>Slot modelinde çözümdeki slota gönderme sayısı.</summary>
        public int SlotSends
        {
            get
            {
                int n = 0;
                if (moves != null) foreach (var m in moves) if (m.dir == SlotMove) n++;
                return n;
            }
        }

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
                if (m.dir == SlotMove) break;
                totalOffset += m.dir;
            }
            return true;
        }

        /// <summary>İlk hamle (birleştirilmiş) sonunda blok slota mı gidiyor?</summary>
        public bool FirstDragEndsInSlot
        {
            get
            {
                if (moves == null || moves.Count == 0) return false;
                int block = moves[0].block;
                foreach (var m in moves)
                {
                    if (m.block != block) return false;
                    if (m.dir == SlotMove) return true;
                }
                return false;
            }
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

        /// <summary>
        /// Yeni sistem (slot sırası): bloklar tepside toplamaz; çıkış yolu (üstündeki hücreler) açık blok tıklanınca slota gider,
        /// slottaki bloklar raydaki en öndeki topları alır, dolan blok slotu boşaltır. Slota giden bloğun arkasındaki 1x1 bloklar
        /// sütunda öne kayar. Slotlar dolu ve hiçbiri top alamıyorsa oyun kilitlenir.
        /// Oyundaki SlotSystem sahnede açık olduğu sürece bu model kullanılmalı.
        /// </summary>
        public static bool UseSlotModel = true;

        /// <summary>Levelin oyunda başladığı sabit slot sayısı (5). GameManager.LoadLevel ile aynı kural.</summary>
        public static int GetSlotCapacity(PuzzleGlassLevelData lvl)
        {
            return SlotSystem.FixedSlotCount;
        }

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
            public bool isFilledBox;
            public PuzzleColor filledBallColor;
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

            // Sınır dışı / çakışma / duvar
            var wallGrid = new bool[cols, rows];
            if (lvl.gridWalls != null)
                foreach (var w in lvl.gridWalls)
                    if (w.x >= 0 && w.y >= 0 && w.x < cols && w.y < rows) wallGrid[w.x, w.y] = true;

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
                    if (wallGrid[x, y])
                    {
                        list.Add(new LevelFinding { severity = FindingSeverity.Critical, blockIndex = i, text = $"Blok #{i + 1} tepsi bölmesiyle çakışıyor.", why = "Bloğu boş hücreye taşı ya da bölmeyi sil." });
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

            // Renk dengesi: camda başlayan top + dolu kutu stoğu = tüm kutuların toplam toplayıcı yuvası
            // (Dolu kutular içindekileri cama aktardıktan sonra yok olmaz, kendi rengindeki topları da toplar.)
            var glass = new Dictionary<PuzzleColor, int>();
            var supply = new Dictionary<PuzzleColor, int>();
            var sockets = new Dictionary<PuzzleColor, int>();
            if (lvl.useManualBallLayout)
            {
                if (lvl.manualBalls != null)
                    foreach (var b in lvl.manualBalls) if (b != null) glass[b.color] = (glass.TryGetValue(b.color, out int v) ? v : 0) + 1;
            }
            else if (lvl.bottomToTopGroups != null)
            {
                foreach (var g in lvl.bottomToTopGroups) if (g != null) glass[g.color] = (glass.TryGetValue(g.color, out int v) ? v : 0) + Mathf.Max(1, g.count);
            }
            foreach (var m in monsters)
            {
                if (m == null) continue;
                int cnt = Mathf.Max(1, m.count);
                sockets[m.color] = (sockets.TryGetValue(m.color, out int v) ? v : 0) + cnt;
                if (m.isFilledBox)
                    supply[m.filledBallColor] = (supply.TryGetValue(m.filledBallColor, out int sv) ? sv : 0) + cnt;
            }
            var allColors = new HashSet<PuzzleColor>(glass.Keys);
            allColors.UnionWith(supply.Keys);
            allColors.UnionWith(sockets.Keys);
            foreach (var c in allColors)
            {
                glass.TryGetValue(c, out int gc);
                supply.TryGetValue(c, out int sup);
                sockets.TryGetValue(c, out int sc);
                if (sup > sc)
                {
                    list.Add(new LevelFinding
                    {
                        severity = FindingSeverity.Critical,
                        text = $"{ColorName(c)}: dolu kutularda {sup} top var ama toplam toplayıcı yuva {sc}.",
                        why = "Dolu kutuların içindeki topları toplayabilecek kadar aynı renkte kutu yuvası olmalı."
                    });
                }
                else if (gc + sup != sc)
                {
                    list.Add(new LevelFinding
                    {
                        severity = FindingSeverity.Critical,
                        text = $"{ColorName(c)}: camda {gc} + dolu kutuda {sup} = {gc + sup} top var, kutularda {sc} yuva.",
                        why = lvl.useManualBallLayout
                            ? "Her renk için (camdaki top + dolu kutu stoğu) toplamı kutu yuvalarına eşit olmalı."
                            : "Her renk için (camdaki top + dolu kutu stoğu) toplamı kutu yuvalarına eşit olmalı. Cam sırasını kutulardan yeniden kurabilirsin."
                    });
                }
            }

            if (lvl.useManualBallLayout && lvl.manualBalls != null && lvl.manualBalls.Count > 0)
            {
                var validSlots = GlassShapeData.GetManualSlots(lvl, out _, out _, out _);
                var occupiedSlots = new HashSet<(int col, int row)>();
                foreach (var b in lvl.manualBalls) if (b != null) occupiedSlots.Add((b.column, b.row));
                var validSet = new HashSet<(int col, int row)>();
                foreach (var s in validSlots) validSet.Add((s.column, s.row));

                bool hasFloating = false;
                foreach (var b in lvl.manualBalls)
                {
                    if (b == null) continue;
                    for (int r = 0; r < b.row; r++)
                    {
                        if (validSet.Contains((b.column, r)) && !occupiedSlots.Contains((b.column, r)))
                        {
                            hasFloating = true;
                            break;
                        }
                    }
                    if (hasFloating) break;
                }
                if (hasFloating)
                {
                    list.Add(new LevelFinding
                    {
                        severity = FindingSeverity.Info,
                        text = "Camda altında boşluk olan top var.",
                        why = "Oyunda toplar yerçekimiyle alttaki boş slota düşer; gerçekçi görünüm için '↓ Alta oturt' butonunu kullanabilirsin."
                    });
                }
            }

            // Tepside boş hücre
            int occupied = 0;
            for (int x = 0; x < cols; x++) for (int y = 0; y < rows; y++) if (owner[x, y] >= 0 || wallGrid[x, y]) occupied++;
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

            if (lvl.useBallFeedTubes)
            {
                int totalBalls = lvl.useManualBallLayout ? (lvl.manualBalls?.Count ?? 0) : lvl.GetGlassStartBallCount();
                int eff = BallFeedTubeUtility.GetEffectiveContainerLimit(lvl.feedTubeContainerLimit, totalBalls);
                if (totalBalls > 1 && eff >= totalBalls)
                {
                    list.Add(new LevelFinding
                    {
                        severity = FindingSeverity.Info,
                        text = "Üst cam besleme tüpü açık ama tüm toplar zaten haznede başlıyor.",
                        why = "Tüpte bekleyen top olması için 'Haznede başlayan' limitini toplam top sayısından küçük yap."
                    });
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
        /// Slot modelinde zorluk ölçüsü: kaydırma yapmadan, çıkışı açık bloklardan rastgele birini slota gönderen oyuncunun
        /// kaç denemede kilitlendiği (slotlar dolu, kimse top alamıyor). 0 = her sıra çözer (kolay), 0.9 = neredeyse her rastgele sıra kilitlenir.
        /// </summary>
        public static float EstimateRandomFailRate(PuzzleGlassLevelData lvl, GlassDims dims, int trials = 200, int seed = 12345)
        {
            if (!BuildRuntimeModel(lvl, dims, out var blocks, out var anchors0, out var walls, out var w1, out var w2, out var gcol, out bool hasW2, out int bc))
                return -1f;
            int cols = lvl.stagingGridCols, rows = lvl.stagingGridRows, cap = GetSlotCapacity(lvl), n = blocks.Length;
            var rng = new System.Random(seed);
            bool isTrack = GlassShapeData.IsTrack(lvl);
            int fails = 0;
            var cand = new List<int>();
            for (int t = 0; t < trials; t++)
            {
                var s = new State { anchors = (Vector2Int[])anchors0.Clone(), removed = new bool[n], fill = new int[n], taken = new int[bc], inSlot = new bool[n], wave = 0 };
                CollectClosure(s, blocks, walls, w1, w2, hasW2, gcol, rows, lvl.iceMeltMode, isTrack);
                for (int guard = 0; guard < n * 4 && !AllRemoved(s); guard++)
                {
                    cand.Clear();
                    if (SlotsUsed(s) < cap)
                        for (int i = 0; i < n; i++)
                            if (InGrid(s, i) && !IsBlockBlocked(i, s, blocks, lvl.iceMeltMode) && IsExitClear(i, s, blocks, walls, cols, rows)) cand.Add(i);
                    if (cand.Count == 0) break;
                    ApplyMove(s, cand[rng.Next(cand.Count)], SolveResult.SlotMove, blocks, walls, cols, rows, cap, lvl.iceMeltMode);
                    CollectClosure(s, blocks, walls, w1, w2, hasW2, gcol, rows, lvl.iceMeltMode, isTrack);
                }
                if (!AllRemoved(s)) fails++;
            }
            return trials > 0 ? (float)fails / trials : -1f;
        }

        public struct PlaythroughAnalysis
        {
            public List<int> completionOrder;
            public int[] ballsCollectedBeforeFinish;
            public int[] boxesCompletedBeforeFinish;
            public int[] waveAtFinish;
        }

        /// <summary>Çözülen hamleleri baştan oynatıp blokların tamamlanma sırasını ve öncesindeki ilerlemeyi çıkarır.</summary>
        public static PlaythroughAnalysis AnalyzePlaythrough(PuzzleGlassLevelData lvl, GlassDims dims, SolveResult solve)
        {
            var res = new PlaythroughAnalysis
            {
                completionOrder = new List<int>(),
                ballsCollectedBeforeFinish = new int[lvl?.monsters?.Count ?? 0],
                boxesCompletedBeforeFinish = new int[lvl?.monsters?.Count ?? 0],
                waveAtFinish = new int[lvl?.monsters?.Count ?? 0]
            };
            if (lvl == null || lvl.monsters == null || solve == null || solve.status != SolveStatus.Solved) return res;

            BuildRuntimeModel(lvl, dims, out var blocks, out var anchors, out var walls, out var wave1, out var wave2, out var stackGridCol, out bool hasWave2, out int bc);
            if (blocks == null) return res;

            bool slotMode = solve.slotModel;
            int slotCap = solve.slotCapacity > 0 ? solve.slotCapacity : GetSlotCapacity(lvl);
            var s = new State { anchors = anchors, removed = new bool[blocks.Length], fill = new int[blocks.Length], taken = new int[bc], inSlot = slotMode ? new bool[blocks.Length] : null, wave = 0 };
            var order = new List<int>();
            var prevRemoved = new bool[blocks.Length];

            void RecordNewCompletions(int ballsBeforeStep, int boxesBeforeStep)
            {
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (!prevRemoved[i] && s.removed[i])
                    {
                        prevRemoved[i] = true;
                        order.Add(i);
                        res.ballsCollectedBeforeFinish[i] = ballsBeforeStep;
                        res.boxesCompletedBeforeFinish[i] = boxesBeforeStep;
                        res.waveAtFinish[i] = s.wave;
                    }
                }
            }

            CollectClosureWithTrace(s, blocks, walls, wave1, wave2, hasWave2, stackGridCol, lvl.stagingGridRows, lvl.iceMeltMode, GlassShapeData.IsTrack(lvl), RecordNewCompletions);
            if (solve.moves != null)
            {
                foreach (var (blockIdx, dir) in solve.moves)
                {
                    if (blockIdx < 0 || blockIdx >= blocks.Length) continue;
                    if (slotMode) ApplyMove(s, blockIdx, dir, blocks, walls, lvl.stagingGridCols, lvl.stagingGridRows, slotCap, lvl.iceMeltMode);
                    else s.anchors[blockIdx] += dir;
                    CollectClosureWithTrace(s, blocks, walls, wave1, wave2, hasWave2, stackGridCol, lvl.stagingGridRows, lvl.iceMeltMode, GlassShapeData.IsTrack(lvl), RecordNewCompletions);
                }
            }
            res.completionOrder = order;
            return res;
        }

        private static bool BuildRuntimeModel(
            PuzzleGlassLevelData lvl, GlassDims dims,
            out Block[] blocks, out Vector2Int[] anchors, out bool[,] walls,
            out List<PuzzleColor>[] wave1Stacks, out List<PuzzleColor>[] wave2Stacks,
            out int[] stackGridCol, out bool hasWave2, out int bc)
        {
            blocks = null;
            anchors = null;
            walls = null;
            wave1Stacks = null;
            wave2Stacks = null;
            stackGridCol = null;
            hasWave2 = false;
            bc = 1;
            if (lvl == null || lvl.monsters == null || lvl.monsters.Count == 0) return false;

            int cols = lvl.stagingGridCols, rows = lvl.stagingGridRows;
            walls = new bool[cols, rows];
            if (lvl.gridWalls != null)
            {
                foreach (var w in lvl.gridWalls)
                    if (w.x >= 0 && w.y >= 0 && w.x < cols && w.y < rows) walls[w.x, w.y] = true;
            }

            int n = lvl.monsters.Count;
            blocks = new Block[n];
            anchors = new Vector2Int[n];
            for (int i = 0; i < n; i++)
            {
                var m = lvl.monsters[i];
                if (m == null) return false;
                blocks[i] = new Block
                {
                    color = m.color,
                    count = Mathf.Max(1, m.count),
                    cells = CarrierBlockShapeUtility.GetCells(m.blockShape),
                    hasKey = m.hasKey,
                    lockKeyCount = Mathf.Max(0, m.lockKeyCount),
                    iceCount = Mathf.Max(0, m.iceCount),
                    isFilledBox = m.isFilledBox,
                    filledBallColor = m.isFilledBox ? m.filledBallColor : m.color
                };
                anchors[i] = m.gridAnchor;
            }
            if (!IsValidPlacement(blocks, anchors, new bool[n], cols, rows, walls)) return false;

            var layout = GlassShapeData.BuildLayout(lvl);
            var split = GlassShapeData.SplitForFeedTubes(lvl, layout);
            int localBc = Mathf.Max(1, layout.totalColumns + 3);
            var localWave1 = new List<PuzzleColor>[localBc];
            var localWave2 = new List<PuzzleColor>[localBc];
            var localGridCol = new int[localBc];
            for (int c = 0; c < localBc; c++)
            {
                localWave1[c] = new List<PuzzleColor>();
                localWave2[c] = new List<PuzzleColor>();
                localGridCol[c] = -1;
            }

            FillStacks(split.chamberBalls, localWave1, localGridCol, localBc, dims.innerHalfW, cols);
            if (split.wave2Balls != null && split.wave2Balls.Count > 0)
            {
                hasWave2 = true;
                FillStacks(split.wave2Balls, localWave2, localGridCol, localBc, dims.innerHalfW, cols);
            }

            bc = localBc;
            wave1Stacks = localWave1;
            wave2Stacks = localWave2;
            stackGridCol = localGridCol;
            return true;
        }

        private static void FillStacks(
            List<OrderedPoolLayout.PoolBall> balls,
            List<PuzzleColor>[] target,
            int[] stackGridCol,
            int bc,
            float innerHalfW,
            int gridCols)
        {
            if (balls == null || balls.Count == 0) return;
            var sorted = new List<OrderedPoolLayout.PoolBall>(balls);
            sorted.Sort((a, b) => a.row.CompareTo(b.row));
            foreach (var b in sorted)
            {
                int col = Mathf.Clamp(b.ballColumn, 0, bc - 1);
                target[col].Add(b.color);
                if (stackGridCol[col] < 0)
                    stackGridCol[col] = OrderedPoolLayout.GridColumnForX(b.localPos.x, innerHalfW, gridCols);
            }
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
            private readonly bool[,] m_Walls;
            private readonly List<PuzzleColor>[] m_Wave1Stacks;
            private readonly List<PuzzleColor>[] m_Wave2Stacks;
            private readonly bool m_HasWave2;
            private readonly bool m_IsTrack;
            private readonly int[] m_StackGridCol;
            private readonly bool m_SlotMode;
            private readonly int m_SlotCap;
            // Slot modelinde önce kaydırmasız derinlik öncelikli arama: her çözüm tam N slota göndermedir,
            // yani ilk bulunan çözüm en kısadır. Bulamazsa kaydırmalı genişlik öncelikli aramaya geçilir.
            private bool m_DfsPhase;
            private State m_Start;
            private readonly Stack<(State s, int depth, int node)> m_Stack = new Stack<(State s, int depth, int node)>();
            private static readonly Vector2Int[] SlotOnly = { SolveResult.SlotMove };
            private readonly HashSet<string> m_Visited = new HashSet<string>();
            private readonly Queue<(State s, int depth, int node)> m_Queue = new Queue<(State s, int depth, int node)>();
            // Çözüm yolunu geri kurmak için: her düğümün ebeveyni ve oraya gelen hamle
            private readonly List<(int parent, int block, Vector2Int dir)> m_Nodes = new List<(int, int, Vector2Int)>();
            private static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            // Slot modelinde önce slota gönderme denenir (en kısa çözüm genelde kaydırmasız)
            private static readonly Vector2Int[] SlotDirs = { SolveResult.SlotMove, Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            public Run(PuzzleGlassLevelData lvl, GlassDims dims, int stateLimit = DefaultStateLimit)
            {
                Result = new SolveResult { status = SolveStatus.Invalid };
                m_Limit = stateLimit;
                if (!BuildRuntimeModel(lvl, dims, out m_Blocks, out var anchors, out m_Walls, out m_Wave1Stacks, out m_Wave2Stacks, out m_StackGridCol, out m_HasWave2, out int bc))
                {
                    Done = true;
                    return;
                }

                m_Cols = lvl.stagingGridCols;
                m_Rows = lvl.stagingGridRows;
                m_IceMeltMode = lvl.iceMeltMode;
                m_IsTrack = GlassShapeData.IsTrack(lvl);
                m_N = m_Blocks.Length;
                m_SlotMode = UseSlotModel;
                m_SlotCap = GetSlotCapacity(lvl);
                Result.slotModel = m_SlotMode;
                Result.slotCapacity = m_SlotCap;

                var start = new State { anchors = anchors, removed = new bool[m_N], fill = new int[m_N], taken = new int[bc], inSlot = m_SlotMode ? new bool[m_N] : null, wave = 0 };
                CollectClosure(start, m_Blocks, m_Walls, m_Wave1Stacks, m_Wave2Stacks, m_HasWave2, m_StackGridCol, m_Rows, m_IceMeltMode, m_IsTrack);
                Result.openingCollectors = CountFilledAny(start);
                Result.status = SolveStatus.LimitReached; // bitene kadar
                m_Visited.Add(Key(start));
                m_Nodes.Add((-1, -1, Vector2Int.zero));
                m_Start = start;
                m_DfsPhase = m_SlotMode;
                if (m_DfsPhase) m_Stack.Push((start, 0, 0));
                else m_Queue.Enqueue((start, 0, 0));
            }

            /// <summary>En fazla budgetMs milisaniye arar. Bittiyse true döner (Result hazır).</summary>
            public bool Step(double budgetMs)
            {
                if (Done) return true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    if (m_DfsPhase && m_Stack.Count == 0)
                    {
                        // Kaydırmasız çözüm yok: kaydırmalı aramaya geç
                        m_DfsPhase = false;
                        m_Visited.Clear();
                        m_Nodes.Clear();
                        m_Visited.Add(Key(m_Start));
                        m_Nodes.Add((-1, -1, Vector2Int.zero));
                        m_Queue.Enqueue((m_Start, 0, 0));
                    }
                    if (!m_DfsPhase && m_Queue.Count == 0) break;
                    var (s, depth, node) = m_DfsPhase ? m_Stack.Pop() : m_Queue.Dequeue();
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
                        if (!InGrid(s, i) || IsBlockBlocked(i, s, m_Blocks, m_IceMeltMode)) continue;
                        foreach (var d in m_DfsPhase ? SlotOnly : (m_SlotMode ? SlotDirs : Dirs))
                        {
                            var next = s.Clone();
                            if (!ApplyMove(next, i, d, m_Blocks, m_Walls, m_Cols, m_Rows, m_SlotCap, m_IceMeltMode)) continue;
                            CollectClosure(next, m_Blocks, m_Walls, m_Wave1Stacks, m_Wave2Stacks, m_HasWave2, m_StackGridCol, m_Rows, m_IceMeltMode, m_IsTrack);
                            if (m_Visited.Add(Key(next)))
                            {
                                m_Nodes.Add((node, i, d));
                                if (m_DfsPhase) m_Stack.Push((next, depth + 1, m_Nodes.Count - 1));
                                else m_Queue.Enqueue((next, depth + 1, m_Nodes.Count - 1));
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
            public bool[] emptied;
            public int[] fill;
            public int[] taken;
            public int[] supplied;
            public bool[] inSlot;
            public byte wave;

            public State Clone() => new State
            {
                anchors = (Vector2Int[])anchors.Clone(),
                removed = (bool[])removed.Clone(),
                emptied = emptied != null ? (bool[])emptied.Clone() : null,
                inSlot = inSlot != null ? (bool[])inSlot.Clone() : null,
                fill = (int[])fill.Clone(),
                taken = (int[])taken.Clone(),
                supplied = supplied != null ? (int[])supplied.Clone() : null,
                wave = wave
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
            var sb = new StringBuilder(s.anchors.Length * 6 + s.taken.Length * 2 + 4);
            sb.Append(s.wave).Append('|');
            for (int i = 0; i < s.anchors.Length; i++)
            {
                if (s.removed[i]) sb.Append('x');
                else if (s.inSlot != null && s.inSlot[i]) sb.Append(s.emptied != null && s.emptied[i] ? 'e' : 's').Append(s.fill[i]);
                else sb.Append(s.anchors[i].x).Append(',').Append(s.anchors[i].y).Append(':').Append(s.fill[i]);
                sb.Append(';');
            }
            sb.Append('|');
            for (int c = 0; c < s.taken.Length; c++) sb.Append(s.taken[c]).Append(',');
            if (s.supplied != null)
            {
                sb.Append('|');
                for (int c = 0; c < s.supplied.Length; c++) if (s.supplied[c] != 0) sb.Append(c).Append(':').Append(s.supplied[c]).Append(',');
            }
            return sb.ToString();
        }

        private static bool IsValidPlacement(Block[] blocks, Vector2Int[] anchors, bool[] removed, int cols, int rows, bool[,] walls, bool[] inSlot = null)
        {
            var occ = new bool[cols, rows];
            for (int i = 0; i < blocks.Length; i++)
            {
                if (removed[i] || (inSlot != null && inSlot[i])) continue;
                foreach (var c in blocks[i].cells)
                {
                    int x = anchors[i].x + c.x, y = anchors[i].y + c.y;
                    if (x < 0 || y < 0 || x >= cols || y >= rows || occ[x, y] || (walls != null && walls[x, y])) return false;
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
                    for (int j = 0; j < blocks.Length; j++)
                        progress += s.removed[j] ? blocks[j].count : s.fill[j];
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

        private static bool AllStacksTaken(int[] taken, List<PuzzleColor>[] stacks)
        {
            for (int c = 0; c < stacks.Length; c++)
                if (taken[c] < stacks[c].Count) return false;
            return true;
        }

        /// <summary>
        /// Oyundaki kendiliğinden toplama: en üst sıradaki bloklar kendi açık sütunlarının hizasındaki
        /// en alt topları alır; dolan blok çıkar. Haznedeki toplar bitince (Wave 1) üst tüpler açılır (Wave 2).
        /// </summary>
        private static void CollectClosure(
            State s, Block[] blocks, bool[,] walls, List<PuzzleColor>[] wave1Stacks, List<PuzzleColor>[] wave2Stacks,
            bool hasWave2, int[] stackGridCol, int rows, IceMeltMode iceMeltMode, bool isTrack)
        {
            CollectClosureWithTrace(s, blocks, walls, wave1Stacks, wave2Stacks, hasWave2, stackGridCol, rows, iceMeltMode, isTrack, null);
        }

        private static bool InGrid(State s, int i) => !s.removed[i] && (s.inSlot == null || !s.inSlot[i]);

        private static int SlotsUsed(State s)
        {
            if (s.inSlot == null) return 0;
            int n = 0;
            for (int i = 0; i < s.inSlot.Length; i++) if (s.inSlot[i] && !s.removed[i]) n++;
            return n;
        }

        /// <summary>StagingGridBoard.IsBlockExitClearToSlots: bloğun her hücresinin üstünde (tepsinin sonuna kadar) duvar ya da başka blok yok.</summary>
        private static bool IsExitClear(int i, State s, Block[] blocks, bool[,] walls, int cols, int rows)
        {
            foreach (var c in blocks[i].cells)
            {
                int cx = s.anchors[i].x + c.x, cy = s.anchors[i].y + c.y;
                if (cx < 0 || cx >= cols) continue;
                for (int oy = cy + 1; oy < rows; oy++)
                {
                    if (walls != null && walls[cx, oy]) return false;
                    for (int j = 0; j < blocks.Length; j++)
                    {
                        if (j == i || !InGrid(s, j)) continue;
                        foreach (var o in blocks[j].cells)
                            if (s.anchors[j].x + o.x == cx && s.anchors[j].y + o.y == oy) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>StagingGridBoard.AdvanceColumnQueues: her sütunda boş hücrenin altındaki ilk 1x1 blok yukarı kayar (duvar/çok hücreli blokta durur).</summary>
        private static void AdvanceQueues(State s, Block[] blocks, bool[,] walls, int cols, int rows)
        {
            var occ = new int[cols, rows];
            for (int x = 0; x < cols; x++) for (int y = 0; y < rows; y++) occ[x, y] = -1;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (!InGrid(s, i)) continue;
                foreach (var c in blocks[i].cells)
                {
                    int x = s.anchors[i].x + c.x, y = s.anchors[i].y + c.y;
                    if (x >= 0 && y >= 0 && x < cols && y < rows) occ[x, y] = i;
                }
            }
            for (int c = 0; c < cols; c++)
            {
                for (int r = rows - 1; r >= 0; r--)
                {
                    if (walls != null && walls[c, r]) continue;
                    if (occ[c, r] >= 0) continue;
                    int pull = -1;
                    for (int below = r - 1; below >= 0; below--)
                    {
                        if (walls != null && walls[c, below]) break;
                        int o = occ[c, below];
                        if (o >= 0)
                        {
                            if (blocks[o].cells.Length == 1) pull = o;
                            break;
                        }
                    }
                    if (pull >= 0)
                    {
                        occ[s.anchors[pull].x, s.anchors[pull].y] = -1;
                        occ[c, r] = pull;
                        // 1x1 bloğun tek hücresi (0,0) ofsetli
                        var cell = blocks[pull].cells[0];
                        s.anchors[pull] = new Vector2Int(c - cell.x, r - cell.y);
                    }
                }
            }
        }

        /// <summary>Slot modelinde bir hamleyi uygular (kaydırma ya da slota gönderme). Geçersizse false.</summary>
        private static bool ApplyMove(State s, int i, Vector2Int dir, Block[] blocks, bool[,] walls, int cols, int rows, int slotCap, IceMeltMode ice)
        {
            if (!InGrid(s, i) || IsBlockBlocked(i, s, blocks, ice)) return false;
            if (dir == SolveResult.SlotMove)
            {
                if (s.inSlot == null || SlotsUsed(s) >= slotCap) return false;
                if (!IsExitClear(i, s, blocks, walls, cols, rows)) return false;
                s.inSlot[i] = true;
                AdvanceQueues(s, blocks, walls, cols, rows);
                return true;
            }
            s.anchors[i] += dir;
            return IsValidPlacement(blocks, s.anchors, s.removed, cols, rows, walls, s.inSlot);
        }

        private static void CollectClosureWithTrace(
            State s, Block[] blocks, bool[,] walls, List<PuzzleColor>[] wave1Stacks, List<PuzzleColor>[] wave2Stacks,
            bool hasWave2, int[] stackGridCol, int rows, IceMeltMode iceMeltMode, bool isTrack,
            System.Action<int, int> onStepCompleted)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var stacks = s.wave == 0 ? wave1Stacks : wave2Stacks;
                bool slotMode = s.inSlot != null;
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (s.removed[i]) continue;
                    HashSet<int> open = null;
                    if (slotMode)
                    {
                        // Yeni sistem: sadece slottaki bloklar toplar, raydaki her şeridin en öndeki topundan
                        if (!s.inSlot[i]) continue;
                    }
                    else
                    {
                        if (IsBlockBlocked(i, s, blocks, iceMeltMode)) continue;
                        open = OpenColumns(i, s, blocks, walls, rows);
                        if (open == null) continue;
                    }

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
                        if (slotMode && s.inSlot[i])
                        {
                            if (s.supplied == null) s.supplied = new int[16];
                            if (s.emptied == null) s.emptied = new bool[blocks.Length];
                            s.supplied[(int)blocks[i].filledBallColor] += blocks[i].count;
                            s.emptied[i] = true;
                            changed = true;
                        }
                        continue;
                    }

                    for (int c = 0; c < stacks.Length && s.fill[i] < blocks[i].count; c++)
                    {
                        if (!slotMode && !isTrack && !open.Contains(stackGridCol[c])) continue;
                        while (s.taken[c] < stacks[c].Count && stacks[c][s.taken[c]] == blocks[i].color && s.fill[i] < blocks[i].count)
                        {
                            s.taken[c]++;
                            s.fill[i]++;
                            changed = true;
                        }
                    }
                    if (s.supplied != null && s.supplied[(int)blocks[i].color] > 0 && s.fill[i] < blocks[i].count)
                    {
                        int takeSup = Mathf.Min(s.supplied[(int)blocks[i].color], blocks[i].count - s.fill[i]);
                        s.supplied[(int)blocks[i].color] -= takeSup;
                        s.fill[i] += takeSup;
                        changed = true;
                    }
                    if (s.fill[i] >= blocks[i].count)
                    {
                        s.removed[i] = true;
                        changed = true;
                        onStepCompleted?.Invoke(ballsBefore, boxesBefore);
                    }
                }

                if (s.wave == 0 && hasWave2 && AllStacksTaken(s.taken, wave1Stacks))
                {
                    s.wave = 1;
                    System.Array.Clear(s.taken, 0, s.taken.Length);
                    changed = true;
                }
            }
        }

        /// <summary>
        /// Blok en üst sıraya değiyorsa, üstünde başka blok olmayan sütunları (deliği açık sütunlar); değmiyorsa null.
        /// StagingGridBoard.IsWorldXAlignedWithBlockTopColumns ile aynı kural.
        /// </summary>
        private static HashSet<int> OpenColumns(int i, State s, Block[] blocks, bool[,] walls, int rows)
        {
            bool atTop = false;
            foreach (var c in blocks[i].cells) if (s.anchors[i].y + c.y >= rows - 1) { atTop = true; break; }
            if (!atTop) return null;

            var result = new HashSet<int>();
            foreach (var c in blocks[i].cells)
            {
                int cx = s.anchors[i].x + c.x, cy = s.anchors[i].y + c.y;
                bool blocked = false;
                if (walls != null)
                {
                    for (int oy = cy + 1; oy < rows; oy++)
                        if (walls[cx, oy]) { blocked = true; break; }
                }
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
            lvl.useManualBallLayout = false;
            lvl.manualLaneWidth = 0;
            if (lvl.manualBalls != null) lvl.manualBalls.Clear();
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
