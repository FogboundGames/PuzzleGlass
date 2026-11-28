using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Tüm leveller oyun sırasıyla yan yana: renk sayısı, mekanikler, ölçülen zorluk puanı (0-100), hata payı,
    /// adil başlangıç ve kritik anlar. Üstte eğri, altta tablo ve eğri kuralı uyarıları (tekdüze seri, yeni mekanik + yüksek zorluk...).
    /// Ölçümler editör karelerine bölünerek arka planda yapılır.
    /// </summary>
    public sealed class DifficultyCurveWindow : EditorWindow
    {
        private const string LevelsFolder = "Assets/Levels";
        private static readonly string[] MechNames = { "Dolu kutu", "Buz", "Kilit", "2'li bağ" };
        private const int MechCount = 4;

        private sealed class Row
        {
            public PuzzleGlassLevelData level;
            public int colors, blocks;
            public bool[] mech = new bool[MechCount];     // 0 dolu kutu, 1 buz, 2 kilit, 3 2'li bağ
            public bool[] newMech = new bool[MechCount];  // bu levelde ilk kez görülüyor
            public SolveResult solve;
            public LevelMetrics metrics;
            public bool done;
            public int Score => PuzzleLevelSolver.DifficultyScore(solve);
            public bool HasBand => solve != null && solve.HasDifficultyRates;
            public LevelDifficulty Band => PuzzleLevelSolver.DifficultyFromRates(solve.randomFailRate, solve.smartFailRate);
        }

        private readonly List<Row> m_Rows = new List<Row>();
        private readonly List<(MessageType type, string text)> m_Warnings = new List<(MessageType, string)>();
        private int m_Current;
        private PuzzleLevelSolver.Run m_SolveRun;
        private PuzzleLevelSolver.DifficultyRun m_DiffRun;
        private PuzzleLevelSolver.MetricsRun m_MetricsRun;
        private Vector2 m_Scroll;
        private string m_OrderSource = "";

        [MenuItem("Tools/PuzzleGlass/📈 Zorluk Eğrisi", false, 2)]
        public static void Open()
        {
            var w = GetWindow<DifficultyCurveWindow>();
            w.titleContent = new GUIContent("Zorluk Eğrisi");
            w.minSize = new Vector2(760, 480);
            w.Rebuild();
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            if (m_Rows.Count == 0) Rebuild();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            m_SolveRun = null; m_DiffRun = null; m_MetricsRun = null;
        }

        // ==================== Veri ====================

        private void Rebuild()
        {
            m_Rows.Clear();
            m_Warnings.Clear();
            m_SolveRun = null; m_DiffRun = null; m_MetricsRun = null;
            m_Current = 0;

            var gm = FindFirstObjectByType<PuzzleGlassGameManager>();
            List<PuzzleGlassLevelData> levels;
            if (gm != null && gm.levels != null && gm.levels.Any(l => l != null))
            {
                levels = gm.levels.Where(l => l != null).ToList();
                m_OrderSource = "Sıra: sahnedeki GameManager level listesi";
            }
            else
            {
                levels = AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder })
                    .Select(g => AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(l => l != null).OrderBy(l => l.levelNumber).ToList();
                m_OrderSource = "Sıra: level numarası (sahnede GameManager yok)";
            }

            var seen = new bool[MechCount];
            foreach (var lvl in levels)
            {
                var row = new Row { level = lvl };
                var cols = new HashSet<PuzzleColor>();
                if (lvl.monsters != null)
                {
                    foreach (var m in lvl.monsters)
                    {
                        if (m == null) continue;
                        row.blocks++;
                        cols.Add(m.color);
                        if (m.isFilledBox) { cols.Add(m.filledBallColor); row.mech[0] = true; }
                        if (m.iceCount > 0) row.mech[1] = true;
                        if (m.lockKeyCount > 0 || m.hasKey) row.mech[2] = true;
                    }
                    if (lvl.HasAnyLinkedPair()) row.mech[3] = true;
                }
                row.colors = cols.Count;
                for (int k = 0; k < MechCount; k++)
                {
                    if (row.mech[k] && !seen[k]) { row.newMech[k] = true; seen[k] = true; }
                }
                m_Rows.Add(row);
            }
            StartCurrent();
            Repaint();
        }

        private void StartCurrent()
        {
            if (m_Current >= m_Rows.Count) { BuildWarnings(); return; }
            var lvl = m_Rows[m_Current].level;
            m_SolveRun = new PuzzleLevelSolver.Run(lvl, GlassShapeData.GetDims(lvl));
        }

        private void Tick()
        {
            if (m_Current >= m_Rows.Count) return;
            var row = m_Rows[m_Current];
            var lvl = row.level;
            const double budget = 6.0;

            if (m_SolveRun != null)
            {
                if (!m_SolveRun.Step(budget)) return;
                row.solve = m_SolveRun.Result;
                m_SolveRun = null;
                if ((row.solve.status == SolveStatus.Solved || row.solve.status == SolveStatus.LimitReached) && PuzzleLevelSolver.UseSlotModel)
                    m_DiffRun = new PuzzleLevelSolver.DifficultyRun(lvl, GlassShapeData.GetDims(lvl), 150, 12345);
                else
                    FinishRow(row);
            }
            else if (m_DiffRun != null)
            {
                if (!m_DiffRun.Step(budget)) return;
                row.solve.randomFailRate = m_DiffRun.RandomFailRate;
                row.solve.smartFailRate = m_DiffRun.SmartFailRate;
                m_DiffRun = null;
                m_MetricsRun = new PuzzleLevelSolver.MetricsRun(lvl, GlassShapeData.GetDims(lvl));
            }
            else if (m_MetricsRun != null)
            {
                if (!m_MetricsRun.Step(budget)) return;
                row.metrics = m_MetricsRun.Result;
                row.solve.metrics = row.metrics;
                m_MetricsRun = null;
                FinishRow(row);
            }
            Repaint();
        }

        private void FinishRow(Row row)
        {
            row.done = true;
            m_Current++;
            StartCurrent();
        }

        // ==================== Eğri kuralları ====================

        private void BuildWarnings()
        {
            m_Warnings.Clear();
            int n = m_Rows.Count;
            string Name(int i) => $"Level {LevelNo(i)}";

            for (int i = 0; i < n; i++)
            {
                var r = m_Rows[i];
                if (r.solve == null) continue;
                if (r.solve.status == SolveStatus.Unsolvable)
                    m_Warnings.Add((MessageType.Error, $"{Name(i)} çözülemiyor."));
                else if (r.solve.status == SolveStatus.LimitReached)
                {
                    var m = r.metrics;
                    if (m != null && m.minSlots > 0)
                        m_Warnings.Add((MessageType.Info, $"{Name(i)}: ana çözücü sınıra ulaştı ama slota gönderme sırası bulundu, çözülebilir."));
                    else if (m != null && m.complete)
                        m_Warnings.Add((MessageType.Error, $"{Name(i)}: sadece slota göndererek hiçbir sırayla çözülemiyor (tüm sıralar denendi). Oyunda dene; gerçekten kilitleniyorsa level bozuk."));
                    else
                        m_Warnings.Add((MessageType.Warning, $"{Name(i)}: çözücü sınıra ulaştı, çözülebilirlik kesin değil."));
                }
            }

            // 1) Tekdüze seri: aynı bantta üst üste çok level (Kolay/Orta 4+, Zor/Boss 3+)
            for (int i = 0; i < n;)
            {
                if (!m_Rows[i].HasBand) { i++; continue; }
                var band = m_Rows[i].Band;
                int j = i;
                while (j + 1 < n && m_Rows[j + 1].HasBand && m_Rows[j + 1].Band == band) j++;
                int len = j - i + 1;
                int limit = band == LevelDifficulty.Easy || band == LevelDifficulty.Medium ? 4 : 3;
                if (len >= limit)
                    m_Warnings.Add((MessageType.Warning,
                        $"Level {LevelNo(i)}–{LevelNo(j)}: {len} level üst üste {PuzzleLevelSolver.DifficultyName(band)}. " +
                        (band >= LevelDifficulty.Hard ? "Araya rahatlatıcı (kolay) bir level koy." : "Tekdüzelik riski: araya bir zirve ya da rahatlama koy.")));
                i = j + 1;
            }

            // 2) Boss'tan sonra nefes yok
            for (int i = 0; i + 1 < n; i++)
            {
                if (!m_Rows[i].HasBand || !m_Rows[i + 1].HasBand) continue;
                if (m_Rows[i].Band == LevelDifficulty.Boss && m_Rows[i + 1].Score >= m_Rows[i].Score - 15)
                    m_Warnings.Add((MessageType.Warning, $"{Name(i)} Boss, ardından gelen {Name(i + 1)} rahatlatmıyor (puan {m_Rows[i + 1].Score}). Boss'tan sonra kolay bir level iyi olur."));
            }

            // 3) Yeni mekanik + yüksek zorluk
            for (int i = 0; i < n; i++)
            {
                var r = m_Rows[i];
                if (!r.HasBand || r.Band < LevelDifficulty.Hard) continue;
                for (int k = 0; k < MechCount; k++)
                    if (r.newMech[k])
                        m_Warnings.Add((MessageType.Warning, $"{Name(i)}: {MechNames[k]} ilk kez burada görülüyor ama level {PuzzleLevelSolver.DifficultyName(r.Band)}. Mekaniği önce kolay bir levelde öğret."));
            }

            // 4) Erken kombinasyon: yeni öğrenilen mekanik (bu ya da önceki levelde ilk kez) başka bir mekanikle birlikte
            int[] intro = new int[MechCount];
            for (int k = 0; k < MechCount; k++) intro[k] = -1;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < MechCount; k++)
                    if (m_Rows[i].newMech[k]) intro[k] = i;
            for (int i = 0; i < n; i++)
            {
                var r = m_Rows[i];
                int count = r.mech.Count(b => b);
                if (count < 2) continue;
                for (int k = 0; k < MechCount; k++)
                {
                    if (!r.mech[k] || intro[k] < 0 || i - intro[k] > 1) continue;
                    var others = Enumerable.Range(0, MechCount).Where(o => o != k && r.mech[o]).Select(o => MechNames[o]);
                    m_Warnings.Add((MessageType.Warning,
                        $"{Name(i)}: {MechNames[k]} {(i == intro[k] ? "burada" : "bir önceki levelde")} öğretildi ama hemen {string.Join(" + ", others)} ile birleşiyor. Önce 1-2 level tek başına oynat."));
                    break;
                }
            }

            // 5) Renk sayısı: ani sıçrama ve erken yüksek renk
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && m_Rows[i].colors - m_Rows[i - 1].colors >= 2)
                    m_Warnings.Add((MessageType.Info, $"{Name(i)}: renk sayısı {m_Rows[i - 1].colors} → {m_Rows[i].colors}, bir anda 2+ renk artıyor."));
                if (i < 3 && m_Rows[i].colors > 3)
                    m_Warnings.Add((MessageType.Info, $"{Name(i)}: ilk levellerden biri ama {m_Rows[i].colors} renk var (öğrenme için 2-3 renk önerilir)."));
            }

            // 6) Level bazlı affedicilik (kolay/orta levellerde)
            for (int i = 0; i < n; i++)
            {
                var r = m_Rows[i];
                if (r.metrics == null || r.metrics.minSlots < 0 || !r.HasBand) continue;
                bool gentle = r.Band <= LevelDifficulty.Medium;
                if (gentle && r.metrics.firstMoveTraps > 0)
                    m_Warnings.Add((MessageType.Warning, $"{Name(i)}: ilk hamledeki {r.metrics.firstMoves} seçenekten {r.metrics.firstMoveTraps} tanesi kaybettiriyor ({PuzzleLevelSolver.DifficultyName(r.Band)} level)."));
                if (gentle && r.metrics.Slack == 0)
                    m_Warnings.Add((MessageType.Warning, $"{Name(i)}: hata payı yok, 5 slotun hepsi gerekiyor ({PuzzleLevelSolver.DifficultyName(r.Band)} level)."));
            }

            if (m_Warnings.Count == 0) m_Warnings.Add((MessageType.Info, "Eğride kural ihlali bulunmadı."));
        }

        private int LevelNo(int i) => m_Rows[i].level.levelNumber;

        // ==================== Çizim ====================

        private static Color BandColor(LevelDifficulty d)
        {
            switch (d)
            {
                case LevelDifficulty.Easy: return new Color(0.30f, 0.78f, 0.45f);
                case LevelDifficulty.Medium: return new Color(0.95f, 0.75f, 0.20f);
                case LevelDifficulty.Hard: return new Color(0.95f, 0.45f, 0.25f);
                default: return new Color(0.80f, 0.30f, 0.85f);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Yeniden ölç", EditorStyles.toolbarButton, GUILayout.Width(90))) Rebuild();
                GUILayout.Label(m_OrderSource, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                string prog = m_Current < m_Rows.Count ? $"Ölçülüyor: {m_Current + 1}/{m_Rows.Count} (Level {LevelNo(m_Current)})" : $"{m_Rows.Count} level ölçüldü";
                GUILayout.Label(prog, EditorStyles.miniLabel);
            }

            if (m_Rows.Count == 0)
            {
                EditorGUILayout.HelpBox("Level bulunamadı.", MessageType.Info);
                return;
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawChart();
            GUILayout.Space(8);
            DrawTable();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Eğri uyarıları", EditorStyles.boldLabel);
            if (m_Current < m_Rows.Count) EditorGUILayout.HelpBox("Tüm leveller ölçülünce uyarılar çıkar.", MessageType.None);
            else foreach (var w in m_Warnings) EditorGUILayout.HelpBox(w.text, w.type);
            EditorGUILayout.EndScrollView();
        }

        private void DrawChart()
        {
            var rect = GUILayoutUtility.GetRect(10, 200, GUILayout.ExpandWidth(true));
            rect.xMin += 34; rect.xMax -= 12; rect.yMin += 18; rect.yMax -= 18;
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.18f));

            // Bant çizgileri
            for (int b = 0; b <= 4; b++)
            {
                float y = Mathf.Lerp(rect.yMax, rect.yMin, b / 4f);
                EditorGUI.DrawRect(new Rect(rect.xMin, y, rect.width, 1), new Color(1, 1, 1, 0.08f));
                GUI.Label(new Rect(rect.xMin - 32, y - 8, 30, 16), (b * 25).ToString(), EditorStyles.miniLabel);
            }
            string[] bandNames = { "Kolay", "Orta", "Zor", "Boss" };
            for (int b = 0; b < 4; b++)
            {
                float y = Mathf.Lerp(rect.yMax, rect.yMin, (b + 0.5f) / 4f);
                var c = BandColor((LevelDifficulty)b);
                var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                st.normal.textColor = new Color(c.r, c.g, c.b, 0.6f);
                GUI.Label(new Rect(rect.xMax - 60, y - 8, 56, 16), bandNames[b], st);
            }

            int n = m_Rows.Count;
            float step = n > 1 ? rect.width / (n - 1) : 0f;
            var pts = new List<Vector3>();
            Handles.BeginGUI();
            for (int i = 0; i < n; i++)
            {
                float x = n > 1 ? rect.xMin + step * i : rect.center.x;
                var r = m_Rows[i];
                GUI.Label(new Rect(x - 14, rect.yMax + 2, 28, 16), LevelNo(i).ToString(), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter });
                string tag = string.Join("", Enumerable.Range(0, MechCount).Where(k => r.newMech[k]).Select(k => "★"));
                if (tag.Length > 0) GUI.Label(new Rect(x - 20, rect.yMin - 17, 40, 16), tag, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter });
                int score = r.Score;
                if (score < 0) continue;
                float y = Mathf.Lerp(rect.yMax, rect.yMin, score / 100f);
                pts.Add(new Vector3(x, y));
            }
            if (pts.Count > 1)
            {
                Handles.color = new Color(1, 1, 1, 0.7f);
                Handles.DrawAAPolyLine(2.5f, pts.ToArray());
            }
            int pi = 0;
            for (int i = 0; i < n; i++)
            {
                var r = m_Rows[i];
                if (r.Score < 0) continue;
                var p = pts[pi++];
                Handles.color = BandColor(r.Band);
                Handles.DrawSolidDisc(p, Vector3.forward, 5f);
                var hit = new Rect(p.x - 7, p.y - 7, 14, 14);
                GUI.Label(hit, new GUIContent("", $"Level {LevelNo(i)} · puan {r.Score}"));
            }
            Handles.EndGUI();
            GUI.Label(new Rect(rect.xMin, rect.yMin - 17, 300, 16), "★ = yeni mekanik ilk kez", EditorStyles.miniLabel);
        }

        private void DrawTable()
        {
            string[] heads = { "Level", "Renk", "Blok", "Mekanik", "Seçili", "Ölçülen", "Puan", "Min slot", "İlk hamle tuzak", "Kritik adım" };
            float[] widths = { 50, 44, 44, 190, 56, 64, 44, 64, 104, 80 };
            using (new EditorGUILayout.HorizontalScope())
                for (int c = 0; c < heads.Length; c++) GUILayout.Label(heads[c], EditorStyles.miniBoldLabel, GUILayout.Width(widths[c]));

            for (int i = 0; i < m_Rows.Count; i++)
            {
                var r = m_Rows[i];
                var line = EditorGUILayout.BeginHorizontal();
                if (i % 2 == 0) EditorGUI.DrawRect(line, new Color(1, 1, 1, 0.03f));
                if (GUILayout.Button(LevelNo(i).ToString(), EditorStyles.linkLabel, GUILayout.Width(widths[0])))
                {
                    Selection.activeObject = r.level;
                    EditorGUIUtility.PingObject(r.level);
                }
                GUILayout.Label(r.colors.ToString(), GUILayout.Width(widths[1]));
                GUILayout.Label(r.blocks.ToString(), GUILayout.Width(widths[2]));
                var mech = Enumerable.Range(0, MechCount).Where(k => r.mech[k]).Select(k => r.newMech[k] ? MechNames[k] + "★" : MechNames[k]);
                GUILayout.Label(r.mech.Any(b => b) ? string.Join(", ", mech) : "–", GUILayout.Width(widths[3]));
                GUILayout.Label(PuzzleLevelSolver.DifficultyName(r.level.difficulty), GUILayout.Width(widths[4]));

                if (!r.done && i != m_Current) { GUILayout.Label("…", GUILayout.Width(widths[5])); EditorGUILayout.EndHorizontal(); continue; }
                string measured = r.solve == null ? "ölçülüyor"
                    : r.solve.status == SolveStatus.Unsolvable ? "ÇÖZÜLMEZ"
                    : r.solve.status == SolveStatus.LimitReached && r.metrics != null && r.metrics.minSlots < 0 && r.metrics.complete ? "KİLİTLENİR"
                    : r.solve.status == SolveStatus.LimitReached && !r.HasBand ? "belirsiz"
                    : r.HasBand ? PuzzleLevelSolver.DifficultyName(r.Band) : "ölçülüyor";
                var ms = new GUIStyle(EditorStyles.label);
                if (r.HasBand) ms.normal.textColor = BandColor(r.Band);
                if (r.HasBand && r.Band != r.level.difficulty) measured += " ≠";
                GUILayout.Label(measured, ms, GUILayout.Width(widths[5]));
                GUILayout.Label(r.Score >= 0 ? r.Score.ToString() : "", GUILayout.Width(widths[6]));
                var m = r.metrics;
                string approx = m != null && !m.complete ? "~" : "";
                GUILayout.Label(m != null && m.minSlots > 0 ? $"{approx}{m.minSlots}/{m.slotCapacity}" : (r.done ? "–" : ""), GUILayout.Width(widths[7]));
                GUILayout.Label(m != null && m.minSlots > 0 ? $"{m.firstMoveTraps}/{m.firstMoves}" : "", GUILayout.Width(widths[8]));
                GUILayout.Label(m != null && m.minSlots > 0 ? m.criticalSteps.ToString() : "", GUILayout.Width(widths[9]));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField("Min slot: level'ı çözmek için gereken en az slot (düşük = hata payı yüksek). İlk hamle tuzak: kaybettiren/toplam ilk hamle. " +
                                       "Kritik adım: çözüm yolunda tek doğru hamlenin olduğu adımlar. ≠: seçili zorluk ölçülenden farklı.", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
