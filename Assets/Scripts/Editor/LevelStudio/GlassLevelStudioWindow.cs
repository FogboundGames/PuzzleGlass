using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuzzleGlass.Editor.Studio
{
    /// <summary>
    /// Level Stüdyosu: solda level listesi; ortada fırçalar, cam önizlemesi, tepsi ve cam sırası;
    /// sağda çözüm durumu ve seçili bloğun / levelin ayarları; altta bulgular.
    ///
    /// Tepsi: fırçayla boş hücreye tıkla = blok ekle, bloğa tıkla = rengini/şeklini değiştir,
    /// fırçasızken sürükle = taşı. Cam sırası: bir çipe sonra diğerine tıkla = yer değiştir.
    /// Her değişiklikten sonra level, şu anki oyun kurallarıyla çözülmeye çalışılır.
    /// </summary>
    public sealed class GlassLevelStudioWindow : EditorWindow
    {
        private const string LevelsFolder = "Assets/Levels";
        private const string StylePath = "Assets/Scripts/Editor/LevelStudio/GlassLevelStudio.uss";
        private const string SelectedKey = "PuzzleGlass_Studio_SelectedLevel";
        private const string LiveKey = "PuzzleGlass_Studio_LiveSync";
        private const float CellPx = 64f;

        private static readonly PuzzleColor[] BrushColors =
        {
            PuzzleColor.Blue, PuzzleColor.Yellow, PuzzleColor.Red, PuzzleColor.Green, PuzzleColor.Purple,
            PuzzleColor.Orange, PuzzleColor.Pink, PuzzleColor.Cyan, PuzzleColor.Brown, PuzzleColor.White, PuzzleColor.Black
        };

        private static readonly CarrierBlockShape[] BrushShapes =
        {
            CarrierBlockShape.Rect1x1, CarrierBlockShape.Bar2x1, CarrierBlockShape.Bar1x2, CarrierBlockShape.Bar3x1,
            CarrierBlockShape.LShape3, CarrierBlockShape.JShape3, CarrierBlockShape.LTopRight3, CarrierBlockShape.JTopLeft3,
            CarrierBlockShape.Square2x2, CarrierBlockShape.TShape4, CarrierBlockShape.TShapeUp4
        };

        // Veri
        private readonly List<PuzzleGlassLevelData> m_All = new List<PuzzleGlassLevelData>();
        private readonly List<PuzzleGlassLevelData> m_Visible = new List<PuzzleGlassLevelData>();
        private PuzzleGlassLevelData m_Level;
        private PuzzleGlassLevelData m_Pending;      // otomatik üretim önizlemesi (asset'e dokunmaz)
        private LevelDifficulty m_PendingDifficulty;
        private string m_Search = "";

        // Etkileşim durumu
        private int m_SelectedBlock = -1;
        private int m_SelectedGroup = -1;
        private PuzzleColor? m_BrushColor;
        private CarrierBlockShape? m_BrushShape;
        private bool m_Eraser;
        private int m_DragBlock = -1;
        private Vector2Int m_DragOffset;
        private Vector2Int m_DragAnchor;
        private bool m_DragMoved;

        // Çözüm
        private SolveResult m_Solve;
        private List<LevelFinding> m_Findings = new List<LevelFinding>();
        private PuzzleLevelSolver.Run m_SolveRun;   // arka planda (her karede birkaç ms) ilerleyen çözüm
        private bool m_LiveSync;
        private double m_SceneSyncDue = -1;        // sahne güncellemesi: son değişiklikten kısa süre sonra tek sefer
        private const double SceneSyncDelay = 0.6;
        private Vector2Int? m_HoverCell;            // fırçayla gezinirken hayalet önizleme

        // UI
        private ListView m_List;
        private Label m_Title, m_State, m_Summary;
        private Button m_SaveButton;
        private VisualElement m_Palette, m_PreviewBar, m_BoardContent, m_GlassHolder, m_Tray, m_OrderHolder;
        private Label m_GlassCaption, m_OrderCaption, m_EmptyLabel;
        private Label m_PreviewTitle, m_PreviewDetail;
        private VisualElement m_SolveCard, m_Stats, m_Inspector;
        private ScrollView m_Issues;

        [MenuItem("Tools/PuzzleGlass/🎮 Level Stüdyosu", false, 0)]
        public static void Open()
        {
            var w = GetWindow<GlassLevelStudioWindow>("Level Stüdyosu");
            w.minSize = new Vector2(1040, 640);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            m_LiveSync = EditorPrefs.GetBool(LiveKey, true);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
            m_SolveRun = null;
            DiscardPending();
        }

        private void OnFocus()
        {
            if (m_List == null) return;
            LoadLevels(keepSelection: true);
        }

        private void OnUndoRedo()
        {
            if (m_Level == null) return;
            ClampSelection();
            OnEdited(syncScene: true);
        }

        // ==================== Kurulum ====================

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("studio");
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);

            root.Add(BuildToolbar());

            var body = new VisualElement();
            body.AddToClassList("studio__body");
            root.Add(body);
            body.Add(BuildLevelList());

            var center = new VisualElement();
            center.AddToClassList("studio__center");
            m_Palette = new VisualElement();
            m_Palette.AddToClassList("palette");
            center.Add(m_Palette);
            center.Add(BuildAutomation());
            center.Add(BuildPreviewBar());
            var board = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            board.AddToClassList("board");
            m_BoardContent = new VisualElement();
            m_BoardContent.AddToClassList("board__content");
            board.Add(m_BoardContent);
            BuildBoardSkeleton();
            center.Add(board);
            body.Add(center);

            var side = new ScrollView();
            side.AddToClassList("studio__side");
            m_SolveCard = new VisualElement();
            m_SolveCard.AddToClassList("solve");
            side.Add(m_SolveCard);
            m_Stats = new VisualElement();
            m_Stats.AddToClassList("stats");
            side.Add(m_Stats);
            m_Inspector = new VisualElement();
            m_Inspector.AddToClassList("inspector");
            side.Add(m_Inspector);
            body.Add(side);

            m_Issues = new ScrollView();
            m_Issues.AddToClassList("issues");
            root.Add(m_Issues);

            LoadLevels(keepSelection: true);
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("toolbar");
            m_Title = new Label();
            m_Title.AddToClassList("toolbar__title");
            bar.Add(m_Title);
            m_State = new Label();
            m_State.AddToClassList("toolbar__state");
            bar.Add(m_State);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);
            m_Summary = new Label();
            m_Summary.AddToClassList("toolbar__summary");
            bar.Add(m_Summary);

            var live = new Toggle("Sahnede canlı") { value = m_LiveSync, tooltip = "Açıkken her değişiklik sahnedeki levele de uygulanır." };
            live.RegisterValueChangedCallback(e =>
            {
                m_LiveSync = e.newValue;
                EditorPrefs.SetBool(LiveKey, m_LiveSync);
                if (m_LiveSync) ScheduleSceneSync();
            });
            bar.Add(live);
            bar.Add(new Button(() => ApplyToScene(force: true)) { text = "▶ Sahnede aç", tooltip = "Leveli sahneye yükler (Play modunda da çalışır)." });
            m_SaveButton = new Button(Save) { text = "Kaydet" };
            m_SaveButton.AddToClassList("toolbar__primary");
            bar.Add(m_SaveButton);
            return bar;
        }

        private VisualElement BuildLevelList()
        {
            var panel = new VisualElement();
            panel.AddToClassList("levels");
            var search = new ToolbarSearchField();
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(e => { m_Search = e.newValue ?? ""; ApplyFilter(); });
            panel.Add(search);

            var zone = new VisualElement();
            zone.AddToClassList("create-zone");
            zone.Add(new Button(CreateEmptyLevel) { text = "Boş level", tooltip = "Sona boş bir level ekler." });
            zone.Add(new Button(DuplicateLevel) { text = "Kopyala", tooltip = "Seçili leveli kopyalayıp sona ekler." });
            var del = new Button(DeleteLevel) { text = "Sil", tooltip = "Seçili leveli çöp kutusuna taşır." };
            del.AddToClassList("inspector__danger");
            zone.Add(del);
            panel.Add(zone);

            m_List = new ListView(m_Visible, 48, MakeLevelRow, BindLevelRow) { selectionType = SelectionType.Single };
            m_List.style.flexGrow = 1;
            m_List.selectionChanged += items =>
            {
                foreach (var item in items) { SelectLevel((PuzzleGlassLevelData)item); break; }
            };
            panel.Add(m_List);
            return panel;
        }

        private VisualElement BuildAutomation()
        {
            var row = new VisualElement();
            row.AddToClassList("automation");
            var label = new Label("Otomatik üret:");
            label.AddToClassList("automation__label");
            row.Add(label);
            row.Add(new Button(() => ProposeGenerated(LevelDifficulty.Easy)) { text = "Kolay" });
            row.Add(new Button(() => ProposeGenerated(LevelDifficulty.Medium)) { text = "Orta" });
            row.Add(new Button(() => ProposeGenerated(LevelDifficulty.Hard)) { text = "Zor" });
            row.Add(new Button(() => ProposeGenerated(LevelDifficulty.Boss)) { text = "Boss" });
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);
            row.Add(new Button(RebuildOrderFromBlocks)
            {
                text = "Cam sırasını kutulardan kur",
                tooltip = "Camdaki top sırasını kutuların renk ve yuvalarından yeniden kurar (elle yaptığın sıra değişir)."
            });
            return row;
        }

        private VisualElement BuildPreviewBar()
        {
            m_PreviewBar = new VisualElement();
            m_PreviewBar.AddToClassList("preview");
            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            m_PreviewTitle = new Label();
            m_PreviewTitle.AddToClassList("preview__title");
            text.Add(m_PreviewTitle);
            m_PreviewDetail = new Label();
            m_PreviewDetail.AddToClassList("preview__detail");
            text.Add(m_PreviewDetail);
            m_PreviewBar.Add(text);
            var apply = new Button(ApplyPending) { text = "Uygula" };
            apply.AddToClassList("preview__apply");
            m_PreviewBar.Add(apply);
            m_PreviewBar.Add(new Button(() => ProposeGenerated(m_PendingDifficulty)) { text = "Yeniden üret" });
            m_PreviewBar.Add(new Button(() => { DiscardPending(); RefreshAll(); }) { text = "Vazgeç" });
            m_PreviewBar.style.display = DisplayStyle.None;
            return m_PreviewBar;
        }

        // ==================== Level listesi ====================

        private static VisualElement MakeLevelRow()
        {
            var row = new VisualElement();
            row.AddToClassList("level-row");
            var number = new Label { name = "number" };
            number.AddToClassList("level-row__number");
            row.Add(number);
            var text = new VisualElement();
            text.AddToClassList("level-row__text");
            var title = new Label { name = "title" };
            title.AddToClassList("level-row__title");
            text.Add(title);
            var sub = new Label { name = "sub" };
            sub.AddToClassList("level-row__sub");
            text.Add(sub);
            var dots = new VisualElement { name = "dots" };
            dots.AddToClassList("level-row__dots");
            text.Add(dots);
            row.Add(text);
            return row;
        }

        private void BindLevelRow(VisualElement row, int i)
        {
            var lvl = m_Visible[i];
            row.Q<Label>("number").text = lvl.levelNumber.ToString();
            row.Q<Label>("title").text = ShortName(lvl);
            int blocks = lvl.monsters?.Count ?? 0;
            row.Q<Label>("sub").text = $"{DifficultyName(lvl.difficulty)} · {blocks} blok · {lvl.GetTotalBallCount()} top · {lvl.stagingGridCols}x{lvl.stagingGridRows}";
            var dots = row.Q("dots");
            dots.Clear();
            var seen = new HashSet<PuzzleColor>();
            if (lvl.monsters != null)
                foreach (var m in lvl.monsters)
                    if (m != null && seen.Add(m.color))
                    {
                        var dot = new VisualElement();
                        dot.AddToClassList("level-row__dot");
                        dot.style.backgroundColor = PuzzleColorPalette.GetBodyColor(m.color);
                        dots.Add(dot);
                    }
        }

        private void LoadLevels(bool keepSelection)
        {
            m_All.Clear();
            if (!Directory.Exists(LevelsFolder))
            {
                Directory.CreateDirectory(LevelsFolder);
                AssetDatabase.Refresh();
            }
            foreach (string guid in AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) m_All.Add(asset);
            }
            m_All.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));
            ApplyFilter();

            PuzzleGlassLevelData start = null;
            if (keepSelection && m_Level != null && m_All.Contains(m_Level)) start = m_Level;
            if (start == null)
            {
                string saved = SessionState.GetString(SelectedKey, "");
                var prev = string.IsNullOrEmpty(saved) ? null : AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(saved));
                start = prev != null && m_All.Contains(prev) ? prev : (m_All.Count > 0 ? m_All[0] : null);
            }
            if (start == null) { m_Level = null; RefreshAll(); return; }
            if (start != m_Level) SelectLevel(start);
            else RefreshAll();
            int idx = m_Visible.IndexOf(start);
            if (idx >= 0) { m_List.SetSelectionWithoutNotify(new[] { idx }); m_List.ScrollToItem(idx); }
        }

        private void ApplyFilter()
        {
            m_Visible.Clear();
            string term = m_Search.Trim().ToLowerInvariant();
            foreach (var lvl in m_All)
            {
                string name = (lvl.levelName ?? "").ToLowerInvariant();
                if (term.Length == 0 || name.Contains(term) || lvl.levelNumber.ToString() == term) m_Visible.Add(lvl);
            }
            m_List?.RefreshItems();
        }

        private void SelectLevel(PuzzleGlassLevelData lvl)
        {
            if (lvl == m_Level) return;
            DiscardPending();
            m_Level = lvl;
            m_SelectedBlock = -1;
            m_SelectedGroup = -1;
            m_DragBlock = -1;
            if (lvl != null) SessionState.SetString(SelectedKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(lvl)));
            m_Solve = null;
            ScheduleSolve();
            RefreshAll();
            if (m_LiveSync) ScheduleSceneSync();
        }

        private void SyncGameManagerLevels()
        {
            var gm = FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm == null) return;
            gm.levels = new List<PuzzleGlassLevelData>(m_All);
            EditorUtility.SetDirty(gm);
        }

        private void CreateEmptyLevel()
        {
            int next = NextLevelNumber();
            var lvl = CreateInstance<PuzzleGlassLevelData>();
            lvl.levelNumber = next;
            lvl.levelName = $"Level {next}";
            lvl.difficulty = PuzzleLevelGenerator.GetProgressiveDifficulty(next);
            lvl.carrierBoxFormatVersion = 3;
            lvl.monsters = new List<LevelMonsterEntry>();
            lvl.bottomToTopGroups = new List<LevelMonsterEntry>();
            lvl.bottomToTopTriplets = new List<PuzzleColor>();
            lvl.stagingGridCols = 5;
            lvl.stagingGridRows = 4;
            CreateAsset(lvl, next);
        }

        private void DuplicateLevel()
        {
            if (m_Level == null) return;
            int next = NextLevelNumber();
            var copy = Instantiate(m_Level);
            copy.levelNumber = next;
            copy.levelName = $"{ShortName(m_Level)} (Kopya)";
            CreateAsset(copy, next);
        }

        private void CreateAsset(PuzzleGlassLevelData lvl, int number)
        {
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/Level_{number:00}.asset");
            AssetDatabase.CreateAsset(lvl, path);
            AssetDatabase.SaveAssets();
            LoadLevels(keepSelection: false);
            SyncGameManagerLevels();
            SelectAndShow(lvl);
            ShowNotification(new GUIContent($"Level {number} eklendi"));
        }

        private void DeleteLevel()
        {
            if (m_Level == null) return;
            if (!EditorUtility.DisplayDialog("Leveli sil", $"'{ShortName(m_Level)}' çöp kutusuna taşınacak. Devam edilsin mi?", "Sil", "Vazgeç")) return;
            string path = AssetDatabase.GetAssetPath(m_Level);
            m_Level = null;
            if (!string.IsNullOrEmpty(path)) AssetDatabase.MoveAssetToTrash(path);
            AssetDatabase.SaveAssets();
            LoadLevels(keepSelection: false);
            SyncGameManagerLevels();
        }

        private void SelectAndShow(PuzzleGlassLevelData lvl)
        {
            SelectLevel(lvl);
            int i = m_Visible.IndexOf(lvl);
            if (i >= 0) { m_List.SetSelectionWithoutNotify(new[] { i }); m_List.ScrollToItem(i); }
        }

        private int NextLevelNumber()
        {
            int max = 0;
            foreach (var l in m_All) max = Mathf.Max(max, l.levelNumber);
            return max + 1;
        }

        // ==================== Otomatik üretim (önizlemeli) ====================

        private void ProposeGenerated(LevelDifficulty diff)
        {
            if (m_Level == null) return;
            DiscardPending();
            m_Pending = CreateInstance<PuzzleGlassLevelData>();
            m_Pending.hideFlags = HideFlags.DontSave;
            EditorUtility.CopySerialized(m_Level, m_Pending);
            m_Pending.difficulty = diff;
            m_PendingDifficulty = diff;
            PuzzleLevelGenerator.GenerateForLevelData(m_Pending, randomSeed: Random.Range(1, int.MaxValue));
            m_SelectedBlock = -1;
            m_SelectedGroup = -1;
            m_Solve = null;
            ScheduleSolve();
            RefreshAll();
        }

        private void ApplyPending()
        {
            if (m_Pending == null || m_Level == null) return;
            Undo.RecordObject(m_Level, "Otomatik level üret");
            int number = m_Level.levelNumber;
            EditorUtility.CopySerialized(m_Pending, m_Level);
            m_Level.levelNumber = number;
            m_Level.hideFlags = HideFlags.None;
            DiscardPending();
            OnEdited(syncScene: true);
            ShowNotification(new GUIContent("Üretilen level uygulandı (Ctrl+Z geri alır)"));
        }

        private void DiscardPending()
        {
            if (m_Pending != null) DestroyImmediate(m_Pending);
            m_Pending = null;
        }

        /// <summary>Panoda gösterilen level: önizleme varsa o, yoksa seçili level.</summary>
        private PuzzleGlassLevelData Shown => m_Pending != null ? m_Pending : m_Level;

        // ==================== Düzenleme işlemleri ====================

        private void Record(string what)
        {
            Undo.RecordObject(m_Level, what);
        }

        private void OnEdited(bool syncScene)
        {
            if (m_Level == null) return;
            EditorUtility.SetDirty(m_Level);
            m_Solve = null;
            ScheduleSolve();
            RefreshAll();
            m_List?.RefreshItems();
            if (syncScene && m_LiveSync) ScheduleSceneSync();
        }

        private void AddBlock(PuzzleColor color, CarrierBlockShape shape, Vector2Int anchor)
        {
            Record("Blok ekle");
            int count = Mathf.Clamp(CarrierBlockShapeUtility.GetCellCount(shape) * 2, 1, MonsterCharacter.MaxVisualSockets);
            m_Level.monsters.Add(new LevelMonsterEntry(color, count, shape, anchor) { hasGridAnchor = true });
            if (m_Level.bottomToTopGroups == null) m_Level.bottomToTopGroups = new List<LevelMonsterEntry>();
            m_Level.bottomToTopGroups.Add(new LevelMonsterEntry(color, count));
            m_Level.carrierBoxFormatVersion = 3;
            m_Level.SyncTripletsFromGroups();
            m_SelectedBlock = m_Level.monsters.Count - 1;
            OnEdited(syncScene: true);
        }

        private void RemoveBlock(int i)
        {
            if (!ValidBlock(i)) return;
            Record("Blok sil");
            var m = m_Level.monsters[i];
            int g = FindGroup(m.color, m.count, fromTop: true);
            if (g >= 0) m_Level.bottomToTopGroups.RemoveAt(g);
            m_Level.monsters.RemoveAt(i);
            m_Level.SyncTripletsFromGroups();
            m_SelectedBlock = -1;
            m_SelectedGroup = -1;
            OnEdited(syncScene: true);
        }

        private void SetBlockColor(int i, PuzzleColor c)
        {
            if (!ValidBlock(i) || m_Level.monsters[i].color == c) return;
            Record("Blok rengi");
            var m = m_Level.monsters[i];
            int g = FindGroup(m.color, m.count, fromTop: false);
            if (g >= 0) m_Level.bottomToTopGroups[g].color = c;
            m.color = c;
            m_Level.SyncTripletsFromGroups();
            OnEdited(syncScene: true);
        }

        private void SetBlockCount(int i, int count)
        {
            if (!ValidBlock(i)) return;
            count = Mathf.Clamp(count, 1, MonsterCharacter.MaxVisualSockets);
            var m = m_Level.monsters[i];
            if (m.count == count) return;
            Record("Yuva sayısı");
            int g = FindGroup(m.color, m.count, fromTop: false);
            if (g >= 0) m_Level.bottomToTopGroups[g].count = count;
            m.count = count;
            m_Level.SyncTripletsFromGroups();
            OnEdited(syncScene: true);
        }

        private bool SetBlockShape(int i, CarrierBlockShape shape, Vector2Int? clickedCell = null)
        {
            if (!ValidBlock(i)) return false;
            var m = m_Level.monsters[i];
            if (m.blockShape == shape) return true;
            // Önce aynı konum, sonra tıklanan hücreyi (yoksa bloğun eski hücrelerini) içeren konumlar
            Vector2Int? anchor = null;
            if (clickedCell.HasValue) anchor = FindPlacement(m_Level, shape, clickedCell.Value, i, m.gridAnchor);
            if (anchor == null)
                foreach (var c in CarrierBlockShapeUtility.GetCells(m.blockShape))
                {
                    anchor = FindPlacement(m_Level, shape, m.gridAnchor + c, i, m.gridAnchor);
                    if (anchor != null) break;
                }
            if (anchor == null) return false;
            Record("Blok şekli");
            m.blockShape = shape;
            m.gridAnchor = anchor.Value;
            m.hasGridAnchor = true;
            OnEdited(syncScene: true);
            return true;
        }

        private void MoveBlock(int i, Vector2Int anchor)
        {
            if (!ValidBlock(i) || m_Level.monsters[i].gridAnchor == anchor) return;
            Record("Blok taşı");
            m_Level.monsters[i].gridAnchor = anchor;
            m_Level.monsters[i].hasGridAnchor = true;
            OnEdited(syncScene: true);
        }

        private void SwapGroups(int a, int b)
        {
            var groups = m_Level.bottomToTopGroups;
            if (groups == null || a < 0 || b < 0 || a >= groups.Count || b >= groups.Count || a == b) return;
            Record("Cam sırası");
            (groups[a], groups[b]) = (groups[b], groups[a]);
            m_Level.SyncTripletsFromGroups();
            OnEdited(syncScene: true);
        }

        private void RebuildOrderFromBlocks()
        {
            if (m_Level == null || m_Pending != null) return;
            Record("Cam sırasını kur");
            m_Level.RebuildTripletsFromBoxes();
            m_SelectedGroup = -1;
            OnEdited(syncScene: true);
            ShowNotification(new GUIContent("Cam sırası kutulardan yeniden kuruldu"));
        }

        /// <summary>Cam sırasında verilen renk/yuva sayısındaki grubu bulur (yoksa aynı renkteki).</summary>
        private int FindGroup(PuzzleColor color, int count, bool fromTop)
        {
            var groups = m_Level.bottomToTopGroups;
            if (groups == null) return -1;
            int sameColor = -1;
            for (int k = 0; k < groups.Count; k++)
            {
                int idx = fromTop ? groups.Count - 1 - k : k;
                var g = groups[idx];
                if (g == null || g.color != color) continue;
                if (g.count == count) return idx;
                if (sameColor < 0) sameColor = idx;
            }
            return sameColor;
        }

        private bool ValidBlock(int i) => m_Level != null && m_Pending == null && m_Level.monsters != null && i >= 0 && i < m_Level.monsters.Count && m_Level.monsters[i] != null;

        private void ClampSelection()
        {
            if (m_Level == null || m_Level.monsters == null || m_SelectedBlock >= m_Level.monsters.Count) m_SelectedBlock = -1;
            if (m_Level == null || m_Level.bottomToTopGroups == null || m_SelectedGroup >= m_Level.bottomToTopGroups.Count) m_SelectedGroup = -1;
        }

        /// <summary>Şekil verilen konumda tepsiye sığıyor ve başka blokla çakışmıyor mu? (ignore = taşınan blok)</summary>
        private static bool Fits(PuzzleGlassLevelData lvl, CarrierBlockShape shape, Vector2Int anchor, int ignore)
        {
            var own = CarrierBlockShapeUtility.GetCells(shape);
            foreach (var c in own)
            {
                int x = anchor.x + c.x, y = anchor.y + c.y;
                if (x < 0 || y < 0 || x >= lvl.stagingGridCols || y >= lvl.stagingGridRows) return false;
                if (BlockAt(lvl, new Vector2Int(x, y), ignore) >= 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Tıklanan hücreyi içeren ve sığan bir konum arar. Önce şeklin üst hücreleri denenir: dikey 2'liyi
        /// üstteki boşluğa tıklayınca aşağı doğru yerleşir. Bulamazsa null.
        /// </summary>
        private static Vector2Int? FindPlacement(PuzzleGlassLevelData lvl, CarrierBlockShape shape, Vector2Int cell, int ignore, Vector2Int? preferAnchor = null)
        {
            if (preferAnchor.HasValue && Fits(lvl, shape, preferAnchor.Value, ignore)) return preferAnchor;
            var cells = new List<Vector2Int>(CarrierBlockShapeUtility.GetCells(shape));
            cells.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
            foreach (var c in cells)
            {
                var anchor = cell - c;
                if (Fits(lvl, shape, anchor, ignore)) return anchor;
            }
            return null;
        }

        private static int BlockAt(PuzzleGlassLevelData lvl, Vector2Int cell, int ignore = -1)
        {
            if (lvl.monsters == null) return -1;
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                if (i == ignore || lvl.monsters[i] == null) continue;
                var m = lvl.monsters[i];
                foreach (var c in CarrierBlockShapeUtility.GetCells(m.blockShape))
                    if (m.gridAnchor.x + c.x == cell.x && m.gridAnchor.y + c.y == cell.y) return i;
            }
            return -1;
        }

        // ==================== Kaydet / sahne ====================

        private void Save()
        {
            if (m_Level == null) return;
            AssetDatabase.SaveAssetIfDirty(m_Level);
            RefreshState();
            ShowNotification(new GUIContent("Kaydedildi"));
        }

        /// <summary>Sahneyi her tıklamada değil, düzenleme durduktan kısa süre sonra bir kez günceller (ağır iş).</summary>
        private void ScheduleSceneSync()
        {
            m_SceneSyncDue = EditorApplication.timeSinceStartup + SceneSyncDelay;
        }

        /// <summary>Her editör karesi: çözümü birkaç ms ilerlet, zamanı gelen sahne güncellemesini yap.</summary>
        private void OnEditorUpdate()
        {
            if (m_SolveRun != null)
            {
                if (m_SolveRun.Step(6.0))
                {
                    m_Solve = m_SolveRun.Result;
                    m_SolveRun = null;
                    RefreshAll();
                }
            }
            if (m_SceneSyncDue > 0 && EditorApplication.timeSinceStartup >= m_SceneSyncDue && m_DragBlock < 0)
            {
                m_SceneSyncDue = -1;
                ApplyToScene(force: false);
            }
        }

        private void ApplyToScene(bool force)
        {
            if (m_Level == null || m_Level.monsters == null || m_Level.monsters.Count == 0)
            {
                if (force) ShowNotification(new GUIContent("Boş level sahneye yüklenmez: önce blok ekle"));
                return;
            }
            // Çakışan bloklar varken sahneye uygulamak oyunu bozar
            foreach (var f in m_Findings)
            {
                if (f.severity == FindingSeverity.Critical && f.blockIndex >= 0)
                {
                    if (force) ShowNotification(new GUIContent("Önce çakışan/taşan blokları düzelt"));
                    return;
                }
            }
            var gm = FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm == null)
            {
                if (force) ShowNotification(new GUIContent("Sahnede PuzzleGlassGameManager yok"));
                return;
            }
            if (!gm.levels.Contains(m_Level)) SyncGameManagerLevels();
            gm.LoadLevel(m_Level);
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(gm);
                EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            }
            SceneView.RepaintAll();
        }

        // ==================== Çözüm ====================

        /// <summary>Yeni bir çözüm başlatır (öncekini bırakır); OnEditorUpdate karelere yayarak yürütür.</summary>
        private void ScheduleSolve()
        {
            var lvl = Shown;
            m_Solve = null;
            m_SolveRun = lvl != null ? new PuzzleLevelSolver.Run(lvl, GetGlassDims(lvl)) : null;
        }

        /// <summary>Camın iç ölçüleri: levelin kendi camından (yoksa oyunun varsayılan havuz kutusu).</summary>
        private static PuzzleLevelSolver.GlassDims GetGlassDims(PuzzleGlassLevelData lvl) => GlassShapeData.GetDims(lvl);

        // ==================== Yenileme ====================

        private void RefreshAll()
        {
            if (m_BoardContent == null) return;
            var lvl = Shown;
            m_Findings = lvl != null ? PuzzleLevelSolver.Check(lvl, m_Solve) : new List<LevelFinding>();
            RefreshPalette();
            RefreshPreviewBar();
            RefreshBoard();
            RefreshSide();
            RefreshIssues();
            RefreshState();
        }

        private void RefreshState()
        {
            if (m_Title == null) return;
            if (m_Level == null)
            {
                m_Title.text = "Level yok";
                m_State.text = "";
                m_Summary.text = "Soldan 'Boş level' ile başla";
                m_SaveButton.SetEnabled(false);
                return;
            }
            m_Title.text = $"Level {m_Level.levelNumber}  {ShortName(m_Level)}";
            bool dirty = EditorUtility.IsDirty(m_Level);
            m_State.text = dirty ? "● Kaydedilmedi" : "Kaydedildi";
            m_State.EnableInClassList("toolbar__state--dirty", dirty);
            m_SaveButton.SetEnabled(dirty);

            int critical = 0, warning = 0;
            foreach (var f in m_Findings)
            {
                if (f.severity == FindingSeverity.Critical) critical++;
                else if (f.severity == FindingSeverity.Warning) warning++;
            }
            bool ready = critical == 0 && m_Solve != null && m_Solve.status == SolveStatus.Solved;
            m_Summary.text = (ready ? "Yayına hazır" : "Yayına hazır değil") + $" · {critical} kritik · {warning} uyarı";
            m_Summary.EnableInClassList("toolbar__summary--bad", critical > 0);
            m_Summary.EnableInClassList("toolbar__summary--good", ready);
        }

        private void RefreshPreviewBar()
        {
            bool show = m_Pending != null;
            m_PreviewBar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            m_PreviewTitle.text = $"Önizleme: otomatik {DifficultyName(m_PendingDifficulty)} level";
            string solve = m_Solve == null ? "çözüm aranıyor…" : SolveText(m_Solve);
            m_PreviewDetail.text = $"{m_Pending.monsters.Count} blok · {m_Pending.GetTotalBallCount()} top · {m_Pending.stagingGridCols}x{m_Pending.stagingGridRows} tepsi · {solve}. Uygula derseniz şimdiki levelin yerine geçer.";
        }

        // ---------- Palet ----------

        private void RefreshPalette()
        {
            m_Palette.Clear();
            bool editable = m_Level != null && m_Pending == null;
            m_Palette.SetEnabled(editable);

            var colorLabel = new Label("Renk");
            colorLabel.AddToClassList("palette__label");
            m_Palette.Add(colorLabel);
            foreach (var c in BrushColors)
            {
                var color = c;
                var chip = Chip(() =>
                {
                    m_Eraser = false;
                    m_BrushColor = m_BrushColor == color ? (PuzzleColor?)null : color;
                    RefreshAll();
                }, m_BrushColor == color && !m_Eraser);
                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.backgroundColor = PuzzleColorPalette.GetBodyColor(color);
                chip.Add(sw);
                chip.Add(new Label(PuzzleLevelSolver.ColorName(color)));
                chip.tooltip = "Renk fırçası: boş hücreye tıkla = bu renkte blok ekle, bloğa tıkla = rengini değiştir.";
                m_Palette.Add(chip);
            }

            AddSep();
            var shapeLabel = new Label("Şekil");
            shapeLabel.AddToClassList("palette__label");
            m_Palette.Add(shapeLabel);
            foreach (var s in BrushShapes)
            {
                var shape = s;
                var chip = Chip(() =>
                {
                    m_Eraser = false;
                    m_BrushShape = m_BrushShape == shape ? (CarrierBlockShape?)null : shape;
                    RefreshAll();
                }, m_BrushShape == shape && !m_Eraser);
                chip.Add(ShapeIcon(shape));
                chip.tooltip = $"{CarrierBlockShapeUtility.GetTurkishLabel(shape)}: boş hücreye tıkla = ekle, bloğa tıkla = şeklini değiştir.";
                m_Palette.Add(chip);
            }

            AddSep();
            var eraser = Chip(() =>
            {
                m_Eraser = !m_Eraser;
                RefreshAll();
            }, m_Eraser);
            eraser.Add(new Label("Silgi"));
            eraser.tooltip = "Tıklanan blok silinir (Ctrl+Z geri alır).";
            m_Palette.Add(eraser);

            if (m_BrushColor != null || m_BrushShape != null || m_Eraser)
            {
                var clear = Chip(() => { ClearBrush(); RefreshAll(); }, false);
                clear.Add(new Label("Fırçayı bırak (Esc)"));
                m_Palette.Add(clear);
            }
        }

        private void AddSep()
        {
            var sep = new VisualElement();
            sep.AddToClassList("palette__sep");
            m_Palette.Add(sep);
        }

        private static Button Chip(System.Action onClick, bool active)
        {
            var chip = new Button(onClick);
            chip.AddToClassList("chip");
            chip.EnableInClassList("chip--active", active);
            return chip;
        }

        private static VisualElement ShapeIcon(CarrierBlockShape shape)
        {
            var icon = new VisualElement();
            icon.AddToClassList("shape-icon");
            CarrierBlockShapeUtility.GetBounds(shape, out int w, out int h);
            foreach (var c in CarrierBlockShapeUtility.GetCells(shape))
            {
                var cell = new VisualElement();
                cell.AddToClassList("shape-icon__cell");
                cell.style.left = c.x * 7;
                cell.style.top = (h - 1 - c.y) * 7 + (h == 1 ? 5 : 1);
                icon.Add(cell);
            }
            return icon;
        }

        private void ClearBrush()
        {
            m_BrushColor = null;
            m_BrushShape = null;
            m_Eraser = false;
        }

        // ---------- Pano: cam + tepsi + cam sırası ----------

        /// <summary>
        /// Pano iskeleti bir kez kurulur. Tepsi elemanı kalıcıdır (sürüklerken fare yakalaması kaybolmasın);
        /// yenilemede sadece içerikleri yeniden çizilir.
        /// </summary>
        private void BuildBoardSkeleton()
        {
            m_EmptyLabel = new Label("Level seçilmedi. Soldan bir level seç ya da 'Boş level' ile başla.");
            m_EmptyLabel.AddToClassList("board__caption");
            m_BoardContent.Add(m_EmptyLabel);

            m_GlassCaption = new Label("Cam (toplar alttan üste; seçili bloğun deliğinin üstü ve rengi vurgulanır)");
            m_GlassCaption.AddToClassList("board__caption");
            m_BoardContent.Add(m_GlassCaption);
            m_GlassHolder = new VisualElement();
            m_BoardContent.Add(m_GlassHolder);

            m_Tray = new VisualElement();
            m_Tray.AddToClassList("tray");
            m_Tray.RegisterCallback<PointerDownEvent>(OnTrayPointerDown);
            m_Tray.RegisterCallback<PointerMoveEvent>(OnTrayPointerMove);
            m_Tray.RegisterCallback<PointerUpEvent>(OnTrayPointerUp);
            m_Tray.RegisterCallback<PointerLeaveEvent>(_ => { if (m_HoverCell != null) { m_HoverCell = null; RedrawTray(); } });
            m_BoardContent.Add(m_Tray);

            m_OrderCaption = new Label("Cam sırası (alttan üste) · bir çipe, sonra diğerine tıkla = yer değiştir");
            m_OrderCaption.AddToClassList("board__caption");
            m_BoardContent.Add(m_OrderCaption);
            m_OrderHolder = new VisualElement();
            m_BoardContent.Add(m_OrderHolder);
        }

        private void RefreshBoard()
        {
            var lvl = Shown;
            bool has = lvl != null;
            m_EmptyLabel.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var el in new VisualElement[] { m_GlassCaption, m_GlassHolder, m_Tray, m_OrderCaption, m_OrderHolder })
                el.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;

            int cols = lvl.stagingGridCols;
            m_GlassHolder.Clear();
            m_GlassHolder.Add(BuildGlass(lvl, GetGlassDims(lvl), cols * CellPx, cols));
            RedrawTray();
            m_OrderHolder.Clear();
            m_OrderHolder.Add(BuildOrder(lvl));
        }

        private int m_CellCols = -1, m_CellRows = -1;

        private void RedrawTray()
        {
            var lvl = Shown;
            if (lvl == null) { m_Tray.Clear(); m_CellElements.Clear(); m_CellCols = m_CellRows = -1; return; }
            int cols = lvl.stagingGridCols, rows = lvl.stagingGridRows;
            m_Tray.style.width = cols * CellPx + 6f;   // + kenarlık
            m_Tray.style.height = rows * CellPx + 6f;

            var critical = new HashSet<int>();
            foreach (var f in m_Findings) if (f.severity == FindingSeverity.Critical && f.blockIndex >= 0) critical.Add(f.blockIndex);

            // Hücre kutuları kalıcı: sadece tepsi boyutu değişince yeniden kurulur (fare eşlemesi ekrandaki konumlarını kullanır)
            if (cols != m_CellCols || rows != m_CellRows || m_CellElements.Count == 0)
            {
                m_Tray.Clear();
                m_CellElements.Clear();
                for (int x = 0; x < cols; x++)
                {
                    for (int y = 0; y < rows; y++)
                    {
                        var cell = new VisualElement();
                        cell.AddToClassList("tray__cell");
                        if (y == rows - 1) cell.AddToClassList("tray__cell--top");
                        PlaceCell(cell, x, y, rows, 3f);
                        cell.pickingMode = PickingMode.Ignore;
                        m_Tray.Add(cell);
                        m_CellElements.Add((cell, new Vector2Int(x, y)));
                    }
                }
                m_CellCols = cols;
                m_CellRows = rows;
            }
            else
            {
                // Hücreler kalsın, blokları temizle
                for (int i = m_Tray.childCount - 1; i >= 0; i--)
                    if (!m_Tray[i].ClassListContains("tray__cell")) m_Tray.RemoveAt(i);
            }

            if (lvl.monsters == null) return;
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var m = lvl.monsters[i];
                if (m == null) continue;
                bool dragging = i == m_DragBlock && m_Pending == null;
                Vector2Int anchor = dragging ? m_DragAnchor : m.gridAnchor;
                bool invalid = dragging && !Fits(lvl, m.blockShape, anchor, i);
                DrawBlock(m_Tray, m, i, anchor, rows, i == m_SelectedBlock, critical.Contains(i), dragging, invalid);
            }

            // Fırça hayaleti: boş hücrede yeni blok, blok üstünde yeni şekil
            if (m_HoverCell.HasValue && m_Pending == null && (m_BrushColor != null || m_BrushShape != null) && !m_Eraser)
            {
                var cell = m_HoverCell.Value;
                int hit = BlockAt(lvl, cell);
                var shape = m_BrushShape ?? (hit >= 0 ? lvl.monsters[hit].blockShape : CarrierBlockShape.Rect1x1);
                var color = m_BrushColor ?? (hit >= 0 ? lvl.monsters[hit].color : PuzzleColor.Blue);
                Vector2Int? at = hit >= 0
                    ? FindPlacement(lvl, shape, cell, hit, lvl.monsters[hit].gridAnchor)
                    : FindPlacement(lvl, shape, cell, -1);
                var ghost = new LevelMonsterEntry(color, Mathf.Clamp(CarrierBlockShapeUtility.GetCellCount(shape) * 2, 1, 16), shape, at ?? cell);
                if (hit >= 0) ghost.count = lvl.monsters[hit].count;
                DrawBlock(m_Tray, ghost, -1, at ?? cell - CarrierBlockShapeUtility.GetCells(shape)[0], rows, false, false, true, !at.HasValue);
            }
        }

        private VisualElement BuildGlass(PuzzleGlassLevelData lvl, PuzzleLevelSolver.GlassDims dims, float width, int cols)
        {
            var layout = GlassShapeData.BuildLayout(lvl);
            float scale = width / Mathf.Max(0.01f, dims.innerHalfW * 2f);
            float height = Mathf.Max(80f, (dims.ceilY - dims.floorY) * scale);

            var glass = new VisualElement();
            glass.AddToClassList("glass");
            glass.style.width = width + 6f;   // + kenarlık (UI Toolkit genişliği kenarlığı içerir)
            glass.style.height = height + 6f;

            // Seçili blok: deliğinin üstündeki sütunları ve rengini vurgula
            PuzzleColor? focusColor = null;
            HashSet<int> focusCols = null;
            if (m_Pending == null && m_SelectedBlock >= 0 && lvl.monsters != null && m_SelectedBlock < lvl.monsters.Count)
            {
                var sel = lvl.monsters[m_SelectedBlock];
                focusColor = sel.color;
                focusCols = new HashSet<int>();
                foreach (var c in CarrierBlockShapeUtility.GetCells(sel.blockShape)) focusCols.Add(sel.gridAnchor.x + c.x);
            }
            else if (m_SelectedGroup >= 0 && lvl.bottomToTopGroups != null && m_SelectedGroup < lvl.bottomToTopGroups.Count)
            {
                focusColor = lvl.bottomToTopGroups[m_SelectedGroup].color;
            }

            for (int c = 0; c < cols; c++)
            {
                if (focusCols != null && focusCols.Contains(c))
                {
                    var col = new VisualElement();
                    col.AddToClassList("glass__column");
                    col.style.left = c * CellPx;
                    col.style.width = CellPx;
                    glass.Add(col);
                }
                if (c > 0)
                {
                    var guide = new VisualElement();
                    guide.AddToClassList("glass__guide");
                    guide.style.left = c * CellPx;
                    glass.Add(guide);
                }
            }

            float d = layout.diameter * scale;
            foreach (var b in layout.balls)
            {
                var ball = new VisualElement();
                ball.AddToClassList("ball");
                float cx = (b.localPos.x + dims.innerHalfW) * scale;
                float cy = height - (b.localPos.y - dims.floorY) * scale;
                ball.style.left = cx - d * 0.5f;
                ball.style.top = cy - d * 0.5f;
                ball.style.width = d;
                ball.style.height = d;
                ball.style.backgroundColor = PuzzleColorPalette.GetBodyColor(b.color);
                if (focusColor.HasValue && b.color != focusColor.Value) ball.AddToClassList("ball--dim");
                glass.Add(ball);
            }
            if (layout.balls.Count == 0)
            {
                var empty = new Label("Camda top yok");
                empty.style.position = Position.Absolute;
                empty.style.left = 0;
                empty.style.right = 0;
                empty.style.top = height * 0.4f;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.color = new Color(1f, 1f, 1f, 0.5f);
                glass.Add(empty);
            }
            return glass;
        }

        private static void PlaceCell(VisualElement el, int x, int y, int rows, float inset)
        {
            el.style.left = x * CellPx + inset;
            el.style.top = (rows - 1 - y) * CellPx + inset;
            el.style.width = CellPx - inset * 2f;
            el.style.height = CellPx - inset * 2f;
        }

        /// <summary>Hücrenin verilen köşesine (dx: +1 sağ, dy: +1 üst) kare yama.</summary>
        private static VisualElement CornerPatch(int dx, int dy, float size, Color color)
        {
            var p = new VisualElement();
            p.pickingMode = PickingMode.Ignore;
            p.style.position = Position.Absolute;
            p.style.width = size;
            p.style.height = size;
            p.style.backgroundColor = color;
            if (dx > 0) p.style.right = 0; else p.style.left = 0;
            if (dy > 0) p.style.top = 0; else p.style.bottom = 0;
            return p;
        }

        private void DrawBlock(VisualElement tray, LevelMonsterEntry m, int index, Vector2Int anchor, int rows, bool selected, bool critical, bool ghost, bool invalid)
        {
            var cells = CarrierBlockShapeUtility.GetCells(m.blockShape);
            Color body = PuzzleColorPalette.GetBodyColor(m.color);
            var own = new HashSet<Vector2Int>(cells);

            // Blok tek parça çizilir: kendi hücreleri arasında boşluk/çizgi yok; sadece dış kenarlarda
            // boşluk, çerçeve ve yuvarlak köşe var. Böylece yan yana aynı renk bloklar ayırt edilir.
            const float gap = 3f, border = 3f, radius = 9f, plate = 6f, plateRadius = 5f;
            // Çerçeve rengi: USS'teki yarı saydam siyahın gövde üstündeki etkisi; seçili = beyaz, hatalı = kırmızı
            Color frame = selected ? Color.white
                : (critical ? new Color(0.898f, 0.325f, 0.294f)
                : Color.Lerp(body, Color.black, 0.25f));
            if (invalid) frame = new Color(0.898f, 0.325f, 0.294f);
            Color gapColor = new Color(0.361f, 0.361f, 0.682f); // tepsi zemini (#5c5cae)
            for (int k = 0; k < cells.Length; k++)
            {
                var c = cells[k];
                bool left = !own.Contains(c + Vector2Int.left);
                bool right = !own.Contains(c + Vector2Int.right);
                bool up = !own.Contains(c + Vector2Int.up);      // y yukarı = ekranda üst
                bool down = !own.Contains(c + Vector2Int.down);

                var el = new VisualElement();
                el.AddToClassList("block-cell");
                el.EnableInClassList("block-cell--selected", selected);
                el.EnableInClassList("block-cell--critical", critical && !selected);
                el.EnableInClassList("block-cell--ghost", ghost);
                el.EnableInClassList("block-cell--invalid", invalid);
                el.style.backgroundColor = body;
                el.pickingMode = PickingMode.Ignore;

                int gx = anchor.x + c.x, gy = anchor.y + c.y;
                float l = gx * CellPx + (left ? gap : 0f);
                float t = (rows - 1 - gy) * CellPx + (up ? gap : 0f);
                el.style.left = l;
                el.style.top = t;
                el.style.width = CellPx - (left ? gap : 0f) - (right ? gap : 0f);
                el.style.height = CellPx - (up ? gap : 0f) - (down ? gap : 0f);

                el.style.borderLeftWidth = left ? border : 0f;
                el.style.borderRightWidth = right ? border : 0f;
                el.style.borderTopWidth = up ? border : 0f;
                el.style.borderBottomWidth = down ? border : 0f;
                el.style.borderTopLeftRadius = (up && left) ? radius : 0f;
                el.style.borderTopRightRadius = (up && right) ? radius : 0f;
                el.style.borderBottomLeftRadius = (down && left) ? radius : 0f;
                el.style.borderBottomRightRadius = (down && right) ? radius : 0f;

                // Beyaz iç plaka da birleşik: iç kenarlarda hücre sınırına kadar uzanır
                var inner = new VisualElement();
                inner.AddToClassList("block-cell__inner");
                inner.pickingMode = PickingMode.Ignore;
                inner.style.left = left ? plate : 0f;
                inner.style.right = right ? plate : 0f;
                inner.style.top = up ? plate : 0f;
                inner.style.bottom = down ? plate : 0f;
                inner.style.borderTopLeftRadius = (up && left) ? plateRadius : 0f;
                inner.style.borderTopRightRadius = (up && right) ? plateRadius : 0f;
                inner.style.borderBottomLeftRadius = (down && left) ? plateRadius : 0f;
                inner.style.borderBottomRightRadius = (down && right) ? plateRadius : 0f;
                el.Add(inner);

                // İç (içe dönük) köşeler: iki yanda kendi hücresi var ama çaprazda yok (L/T'nin iç köşesi).
                // Komşu hücrelerin kenar boşluğu ve çerçevesi orada kesiliyordu; köşeyi tamamla:
                // önce gövde (plaka payı), üstüne çerçeve, en üste boşluk karesi.
                for (int dx = -1; dx <= 1; dx += 2)
                {
                    for (int dy = -1; dy <= 1; dy += 2)
                    {
                        if (!own.Contains(c + new Vector2Int(dx, 0)) || !own.Contains(c + new Vector2Int(0, dy))) continue;
                        if (own.Contains(c + new Vector2Int(dx, dy))) continue;
                        // Komşuların beyaz plakası dış hattan gap + border + plate içeride başlıyor
                        el.Add(CornerPatch(dx, dy, gap + border + plate, body));
                        el.Add(CornerPatch(dx, dy, gap + border, frame));
                        el.Add(CornerPatch(dx, dy, gap, gapColor));
                    }
                }
                tray.Add(el);
            }

            // Yuva sayısı ve blok numarası: şeklin ilk hücresine
            var first = cells[0];
            var label = new Label(m.count.ToString());
            label.AddToClassList("block-label");
            label.pickingMode = PickingMode.Ignore;
            PlaceCell(label, anchor.x + first.x, anchor.y + first.y, rows, 2f);
            tray.Add(label);
            if (index < 0) return; // hayalet: numara yok
            var number = new Label($"#{index + 1}");
            number.AddToClassList("block-number");
            number.pickingMode = PickingMode.Ignore;
            number.style.left = (anchor.x + first.x) * CellPx + 10f;
            number.style.top = (rows - 1 - (anchor.y + first.y)) * CellPx + 8f;
            tray.Add(number);
        }

        /// <summary>Tepsi yerel konumundan hücre (kenarlık payı düşülür; y=0 en alt sıra).</summary>
        private readonly List<(VisualElement el, Vector2Int cell)> m_CellElements = new List<(VisualElement, Vector2Int)>();

        /// <summary>
        /// Fare konumundaki tepsi hücresi: ekranda çizili hücre kutularının gerçek konumlarıyla eşlenir
        /// (koordinat hesabına güvenmez). Hücreler arası boşlukta en yakın hücre; tepsinin çok dışındaysa (-1,-1).
        /// </summary>
        private Vector2Int CellAtPanel(Vector2 panelPos, bool allowOutside = false)
        {
            float best = float.MaxValue;
            Vector2Int result = new Vector2Int(-1, -1);
            foreach (var (el, cell) in m_CellElements)
            {
                Rect r = el.worldBound;
                if (r.Contains(panelPos)) return cell;
                float d = (r.center - panelPos).sqrMagnitude;
                if (d < best) { best = d; result = cell; }
            }
            if (!allowOutside && best > CellPx * CellPx) return new Vector2Int(-1, -1);
            return result;
        }

        /// <summary>Sürüklerken tepsi dışına taşan konum için hücre (taşma yönünde devam eder).</summary>
        private Vector2Int CellFromPanelUnclamped(Vector2 panelPos)
        {
            var nearest = CellAtPanel(panelPos, allowOutside: true);
            if (nearest.x < 0) return nearest;
            foreach (var (el, cell) in m_CellElements)
            {
                if (cell != nearest) continue;
                Rect r = el.worldBound;
                float step = Mathf.Max(1f, r.width + 6f);
                int dx = Mathf.RoundToInt((panelPos.x - r.center.x) / step);
                int dy = Mathf.RoundToInt((r.center.y - panelPos.y) / step);
                return nearest + new Vector2Int(dx, dy);
            }
            return nearest;
        }

        /// <summary>Şeklin neden sığmadığını anlatır (ilk denenen konum için).</summary>
        private string WhyNotFits(PuzzleGlassLevelData lvl, CarrierBlockShape shape, Vector2Int cell, int ignore)
        {
            var cells = new List<Vector2Int>(CarrierBlockShapeUtility.GetCells(shape));
            cells.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
            var anchor = cell - cells[0];
            foreach (var c in CarrierBlockShapeUtility.GetCells(shape))
            {
                var p = anchor + c;
                if (p.x < 0 || p.y < 0 || p.x >= lvl.stagingGridCols || p.y >= lvl.stagingGridRows) return $"tepsinin dışına taşıyor (hücre {cell.x + 1},{cell.y + 1})";
                int b = BlockAt(lvl, p, ignore);
                if (b >= 0) return $"#{b + 1} ile çakışıyor (hücre {cell.x + 1},{cell.y + 1})";
            }
            return $"hücre {cell.x + 1},{cell.y + 1}";
        }

        private void OnTrayPointerDown(PointerDownEvent e)
        {
            if (m_Level == null || m_Pending != null || e.button != 0) return;
            var lvl = m_Level;
            var cell = CellAtPanel(e.position);
            if (cell.x < 0 || cell.y < 0 || cell.x >= lvl.stagingGridCols || cell.y >= lvl.stagingGridRows) return;
            int hit = BlockAt(lvl, cell);

            if (m_Eraser)
            {
                if (hit >= 0) RemoveBlock(hit);
                e.StopPropagation();
                return;
            }

            if (m_BrushColor != null || m_BrushShape != null)
            {
                if (hit >= 0)
                {
                    if (m_BrushShape != null && !SetBlockShape(hit, m_BrushShape.Value, cell))
                        ShowNotification(new GUIContent($"#{hit + 1} bu şekle dönüşemiyor: {WhyNotFits(lvl, m_BrushShape.Value, cell, hit)}"));
                    if (m_BrushColor != null) SetBlockColor(hit, m_BrushColor.Value);
                    m_SelectedBlock = hit;
                    RefreshAll();
                }
                else
                {
                    var shape = m_BrushShape ?? CarrierBlockShape.Rect1x1;
                    var color = m_BrushColor ?? PuzzleColor.Blue;
                    // Tıklanan hücreyi içeren, sığan bir konum ara
                    var anchor = FindPlacement(lvl, shape, cell, -1);
                    if (anchor.HasValue) AddBlock(color, shape, anchor.Value);
                    else ShowNotification(new GUIContent($"Sığmıyor: {WhyNotFits(lvl, shape, cell, -1)}"));
                    m_HoverCell = null;
                }
                e.StopPropagation();
                return;
            }

            // Fırça yok: seç ve sürüklemeye başla
            m_SelectedBlock = hit;
            m_SelectedGroup = -1;
            if (hit >= 0)
            {
                m_DragBlock = hit;
                m_DragOffset = cell - lvl.monsters[hit].gridAnchor;
                m_DragAnchor = lvl.monsters[hit].gridAnchor;
                m_DragMoved = false;
                m_Tray.CapturePointer(e.pointerId);
            }
            RefreshAll();
            e.StopPropagation();
        }

        private void OnTrayPointerMove(PointerMoveEvent e)
        {
            if (m_Level == null) return;
            if (m_DragBlock < 0)
            {
                // Fırça seçiliyken hayalet önizleme: blok tıklamadan önce nereye geleceği görünür
                bool brush = (m_BrushColor != null || m_BrushShape != null) && !m_Eraser && m_Pending == null;
                Vector2Int? hover = null;
                if (brush)
                {
                    var c = CellAtPanel(e.position);
                    if (c.x >= 0 && c.y >= 0 && c.x < m_Level.stagingGridCols && c.y < m_Level.stagingGridRows) hover = c;
                }
                if (hover != m_HoverCell)
                {
                    m_HoverCell = hover;
                    RedrawTray();
                }
                return;
            }
            var cell = CellFromPanelUnclamped(e.position);
            var anchor = cell - m_DragOffset;
            if (anchor == m_DragAnchor) return;
            m_DragAnchor = anchor;
            m_DragMoved = true;
            RedrawTray();
        }

        private void OnTrayPointerUp(PointerUpEvent e)
        {
            if (m_DragBlock < 0) return;
            int i = m_DragBlock;
            m_DragBlock = -1;
            if (m_Tray.HasPointerCapture(e.pointerId)) m_Tray.ReleasePointer(e.pointerId);
            if (m_DragMoved && ValidBlock(i))
            {
                var m = m_Level.monsters[i];
                if (Fits(m_Level, m.blockShape, m_DragAnchor, i)) MoveBlock(i, m_DragAnchor);
                else ShowNotification(new GUIContent("Oraya sığmıyor: blok yerinde kaldı"));
            }
            RefreshAll();
        }

        private VisualElement BuildOrder(PuzzleGlassLevelData lvl)
        {
            var strip = new VisualElement();
            strip.AddToClassList("order");
            var groups = lvl.bottomToTopGroups;
            if (groups == null || groups.Count == 0)
            {
                strip.Add(new Label("Cam boş") { style = { color = new Color(0.6f, 0.6f, 0.64f) } });
                return strip;
            }
            PuzzleColor? linked = null;
            if (m_SelectedBlock >= 0 && lvl.monsters != null && m_SelectedBlock < lvl.monsters.Count) linked = lvl.monsters[m_SelectedBlock].color;

            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g == null) continue;
                int index = i;
                var chip = new Button(() => OnOrderChipClicked(index));
                chip.AddToClassList("order__chip");
                chip.EnableInClassList("order__chip--selected", index == m_SelectedGroup);
                chip.EnableInClassList("order__chip--linked", linked.HasValue && linked.Value == g.color && index != m_SelectedGroup);
                var idx = new Label((i + 1).ToString());
                idx.AddToClassList("order__index");
                chip.Add(idx);
                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.backgroundColor = PuzzleColorPalette.GetBodyColor(g.color);
                chip.Add(sw);
                chip.Add(new Label(g.count.ToString()));
                chip.tooltip = $"{i + 1}. sıra (alttan): {PuzzleLevelSolver.ColorName(g.color)}, {g.count} top";
                chip.SetEnabled(m_Pending == null);
                strip.Add(chip);
            }
            return strip;
        }

        private void OnOrderChipClicked(int index)
        {
            if (m_Level == null || m_Pending != null) return;
            if (m_SelectedGroup < 0)
            {
                m_SelectedGroup = index;
                m_SelectedBlock = -1;
                RefreshAll();
                return;
            }
            if (m_SelectedGroup == index)
            {
                m_SelectedGroup = -1;
                RefreshAll();
                return;
            }
            int from = m_SelectedGroup;
            m_SelectedGroup = -1;
            SwapGroups(from, index);
        }

        // ---------- Sağ panel ----------

        private void RefreshSide()
        {
            m_SolveCard.Clear();
            m_Stats.Clear();
            m_Inspector.Clear();
            var lvl = Shown;
            if (lvl == null) return;

            // Çözüm kartı
            m_SolveCard.RemoveFromClassList("solve--good");
            m_SolveCard.RemoveFromClassList("solve--bad");
            m_SolveCard.RemoveFromClassList("solve--unknown");
            string title, detail;
            if (m_Solve == null) { title = "Çözüm aranıyor…"; detail = ""; }
            else
            {
                switch (m_Solve.status)
                {
                    case SolveStatus.Solved:
                        title = "✓ Çözülebilir";
                        detail = m_Solve.minSlides == 0
                            ? "Hiç kaydırmadan çözülüyor."
                            : $"En az {m_Solve.minSlides} kaydırmayla çözülüyor.";
                        detail += $" Başta {m_Solve.openingCollectors} blok toplayabiliyor.";
                        m_SolveCard.AddToClassList("solve--good");
                        break;
                    case SolveStatus.Unsolvable:
                        title = "✗ Çözülemiyor";
                        detail = $"{m_Solve.exploredStates} durum denendi, hiçbirinde tüm bloklar dolmadı.";
                        m_SolveCard.AddToClassList("solve--bad");
                        break;
                    case SolveStatus.LimitReached:
                        title = "? Belirsiz";
                        detail = $"Arama sınırına ulaşıldı ({m_Solve.exploredStates} durum). Oynayarak doğrula.";
                        m_SolveCard.AddToClassList("solve--unknown");
                        break;
                    default:
                        title = "— Kontrol edilemedi";
                        detail = "Önce boş/çakışan blokları düzelt.";
                        m_SolveCard.AddToClassList("solve--bad");
                        break;
                }
            }
            var t = new Label(title);
            t.AddToClassList("solve__title");
            m_SolveCard.Add(t);
            var dl = new Label(detail);
            dl.AddToClassList("solve__detail");
            m_SolveCard.Add(dl);

            // İstatistik
            var colors = new HashSet<PuzzleColor>();
            if (lvl.monsters != null) foreach (var m in lvl.monsters) if (m != null) colors.Add(m.color);
            m_Stats.Add(Stat((lvl.monsters?.Count ?? 0).ToString(), "blok"));
            m_Stats.Add(Stat(lvl.GetTotalBallCount().ToString(), "top"));
            m_Stats.Add(Stat(colors.Count.ToString(), "renk"));

            if (m_Pending != null)
            {
                var hint = new Label("Önizleme gösteriliyor. Düzenlemek için önce Uygula ya da Vazgeç.");
                hint.AddToClassList("inspector__hint");
                m_Inspector.Add(hint);
                return;
            }

            if (m_SelectedBlock >= 0 && m_SelectedBlock < (m_Level.monsters?.Count ?? 0)) BuildBlockInspector(m_SelectedBlock);
            else BuildLevelInspector();
        }

        private static VisualElement Stat(string value, string label)
        {
            var box = new VisualElement();
            box.AddToClassList("stat");
            var v = new Label(value);
            v.AddToClassList("stat__value");
            box.Add(v);
            var l = new Label(label);
            l.AddToClassList("stat__label");
            box.Add(l);
            return box;
        }

        private void BuildBlockInspector(int i)
        {
            var m = m_Level.monsters[i];
            var title = new Label($"Blok #{i + 1}");
            title.AddToClassList("inspector__title");
            m_Inspector.Add(title);
            var sub = new Label($"{CarrierBlockShapeUtility.GetTurkishLabel(m.blockShape)} · konum {m.gridAnchor.x},{m.gridAnchor.y}");
            sub.AddToClassList("inspector__sub");
            m_Inspector.Add(sub);

            var colorField = new EnumField("Renk", m.color);
            colorField.RegisterValueChangedCallback(e => SetBlockColor(i, (PuzzleColor)e.newValue));
            m_Inspector.Add(colorField);

            var shapeField = new EnumField("Şekil", m.blockShape);
            shapeField.RegisterValueChangedCallback(e =>
            {
                if (!SetBlockShape(i, (CarrierBlockShape)e.newValue))
                {
                    ShowNotification(new GUIContent("Bu şekil burada sığmıyor"));
                    RefreshAll();
                }
            });
            m_Inspector.Add(shapeField);

            var row = new VisualElement();
            row.AddToClassList("inspector__row");
            var rl = new Label("Yuva (top)");
            rl.AddToClassList("inspector__row-label");
            row.Add(rl);
            row.Add(new Button(() => SetBlockCount(i, m.count - 1)) { text = "−" });
            var count = new Label(m.count.ToString());
            count.AddToClassList("inspector__count");
            row.Add(count);
            row.Add(new Button(() => SetBlockCount(i, m.count + 1)) { text = "+" });
            row.Add(new Button(() => SetBlockCount(i, CarrierBlockShapeUtility.GetCellCount(m.blockShape) * 2))
            {
                text = "↺",
                tooltip = "Şekle göre varsayılan: hücre başına 2 yuva (büyük bloğa çok, küçüğe az)."
            });
            m_Inspector.Add(row);

            int g = FindGroup(m.color, m.count, fromTop: false);
            var order = new Label(g >= 0
                ? $"Bu bloğun topları cam sırasında {g + 1}. sırada (alttan). Sırayı aşağıdaki şeritten değiştir."
                : "Bu bloğun topları cam sırasında yok!");
            order.AddToClassList("inspector__hint");
            m_Inspector.Add(order);

            var del = new Button(() => RemoveBlock(i)) { text = "Bloğu sil (Delete)" };
            del.AddToClassList("inspector__action");
            del.AddToClassList("inspector__danger");
            m_Inspector.Add(del);

            var hint = new Label("Sürükle: taşı. Vurgulanan cam sütunları bu bloğun deliğinin üstü; sadece oradaki en alt top iner.");
            hint.AddToClassList("inspector__hint");
            m_Inspector.Add(hint);
        }

        private void BuildLevelInspector()
        {
            var lvl = m_Level;
            var title = new Label("Level ayarları");
            title.AddToClassList("inspector__title");
            m_Inspector.Add(title);
            var sub = new Label("Bir bloğa tıklayınca onun ayarları görünür.");
            sub.AddToClassList("inspector__sub");
            m_Inspector.Add(sub);

            var name = new TextField("Ad") { value = lvl.levelName, isDelayed = true };
            name.RegisterValueChangedCallback(e => { Record("Level adı"); lvl.levelName = e.newValue; OnEdited(syncScene: false); });
            m_Inspector.Add(name);

            var number = new IntegerField("Numara") { value = lvl.levelNumber, isDelayed = true };
            number.RegisterValueChangedCallback(e =>
            {
                Record("Level numarası");
                lvl.levelNumber = Mathf.Max(1, e.newValue);
                OnEdited(syncScene: false);
                LoadLevels(keepSelection: true);
                SyncGameManagerLevels();
            });
            m_Inspector.Add(number);

            var diff = new EnumField("Zorluk", lvl.difficulty);
            diff.RegisterValueChangedCallback(e => { Record("Zorluk"); lvl.difficulty = (LevelDifficulty)e.newValue; OnEdited(syncScene: false); });
            m_Inspector.Add(diff);

            var cols = new SliderInt("Tepsi sütun", 2, 7) { value = lvl.stagingGridCols, showInputField = true };
            cols.RegisterValueChangedCallback(e => { Record("Tepsi sütun"); lvl.stagingGridCols = e.newValue; OnEdited(syncScene: true); });
            m_Inspector.Add(cols);
            var rows = new SliderInt("Tepsi satır", 2, 6) { value = lvl.stagingGridRows, showInputField = true };
            rows.RegisterValueChangedCallback(e => { Record("Tepsi satır"); lvl.stagingGridRows = e.newValue; OnEdited(syncScene: true); });
            m_Inspector.Add(rows);

            var glassBtn = new Button(() => GlassStudioWindow.OpenFor(m_Level)) { text = "✨ Bu levelin camını düzenle" };
            glassBtn.AddToClassList("inspector__action");
            m_Inspector.Add(glassBtn);

            var hint = new Label(
                "Nasıl kullanılır:\n" +
                "• Üstten renk ve/veya şekil fırçası seç, tepside boş hücreye tıkla: blok eklenir.\n" +
                "• Fırça seçiliyken bloğa tıkla: rengi/şekli değişir. Silgi: blok silinir.\n" +
                "• Fırça yokken bloğu sürükle: taşınır.\n" +
                "• Cam sırasında iki çipe sırayla tıkla: yerleri değişir.\n" +
                "• Esc fırçayı bırakır, Delete seçili bloğu siler, Ctrl+Z geri alır.");
            hint.AddToClassList("inspector__hint");
            m_Inspector.Add(hint);
        }

        // ---------- Bulgular ----------

        private void RefreshIssues()
        {
            m_Issues.Clear();
            if (Shown == null) return;
            if (m_Findings.Count == 0)
            {
                m_Issues.Add(new Label(m_Solve == null ? "Kontrol ediliyor…" : "Bu level için bulgu yok."));
                return;
            }
            foreach (var f in m_Findings)
            {
                var row = new VisualElement();
                row.AddToClassList("issue");
                row.AddToClassList(f.severity == FindingSeverity.Critical ? "issue--critical" : f.severity == FindingSeverity.Warning ? "issue--warning" : "issue--info");
                var tag = new Label(f.severity == FindingSeverity.Critical ? "KRİTİK" : f.severity == FindingSeverity.Warning ? "UYARI" : "BİLGİ");
                tag.AddToClassList("issue__tag");
                row.Add(tag);
                var text = new Label($"{f.text} {f.why}");
                text.AddToClassList("issue__text");
                row.Add(text);
                if (f.blockIndex >= 0)
                {
                    int b = f.blockIndex;
                    row.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (m_Pending != null) return;
                        m_SelectedBlock = b;
                        m_SelectedGroup = -1;
                        RefreshAll();
                    });
                }
                m_Issues.Add(row);
            }
        }

        // ==================== Klavye ====================

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                ClearBrush();
                m_SelectedGroup = -1;
                m_SelectedBlock = -1;
                RefreshAll();
                e.StopPropagation();
            }
            else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && m_SelectedBlock >= 0 && m_Pending == null)
            {
                var ve = e.target as VisualElement;
                if (ve != null && (ve.GetFirstOfType<TextField>() != null || ve.GetFirstOfType<IntegerField>() != null)) return;
                RemoveBlock(m_SelectedBlock);
                e.StopPropagation();
            }
        }

        // ==================== Yardımcılar ====================

        private static string SolveText(SolveResult s)
        {
            switch (s.status)
            {
                case SolveStatus.Solved: return $"çözülebilir, en az {s.minSlides} kaydırma";
                case SolveStatus.Unsolvable: return "ÇÖZÜLEMİYOR";
                case SolveStatus.LimitReached: return "çözüm belirsiz";
                default: return "kontrol edilemedi";
            }
        }

        private static string DifficultyName(LevelDifficulty d)
        {
            switch (d)
            {
                case LevelDifficulty.Easy: return "Kolay";
                case LevelDifficulty.Medium: return "Orta";
                case LevelDifficulty.Hard: return "Zor";
                default: return "Boss";
            }
        }

        /// <summary>Otomatik üretilen uzun adlardan ("Level 3 - Kolay (5x4 Izgara, ...)") kısa ad.</summary>
        private static string ShortName(PuzzleGlassLevelData lvl)
        {
            string n = string.IsNullOrEmpty(lvl.levelName) ? $"Level {lvl.levelNumber}" : lvl.levelName;
            int paren = n.IndexOf(" (");
            return paren > 0 && n.Contains("Izgara") ? n.Substring(0, paren) : n;
        }
    }
}
