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
        private const float BaseCellPx = 64f;
        private float CellPx => Shown != null && (Shown.stagingGridCols > 5 || Shown.stagingGridRows > 5)
            ? Mathf.Clamp(340f / Mathf.Max(Shown.stagingGridCols, Shown.stagingGridRows), 40f, 58f)
            : BaseCellPx;

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
        private bool m_PendingFullMechanics;
        private string m_Search = "";

        private bool m_ShowSmartGenTab;
        private FullMechanicsLevelGenerator.Options m_GenOptions = FullMechanicsLevelGenerator.Options.Balanced;
        private bool m_GenProgressive = true;
        private LevelDifficulty m_GenDifficulty = LevelDifficulty.Medium;
        private int m_GenBatchCount = 3;

        private enum MechanicBrush { None, Key, Lock, Ice, Filled, Wall }

        // Etkileşim durumu
        private int m_SelectedBlock = -1;
        private int m_SelectedGroup = -1;
        private PuzzleColor? m_BrushColor;
        private CarrierBlockShape? m_BrushShape;
        private MechanicBrush m_BrushMechanic;
        private int m_BrushLockCount = 1;
        private int m_BrushIceCount = 10;
        private PuzzleColor? m_BrushFilledColor;
        private bool m_Eraser;
        private int m_DragBlock = -1;
        private Vector2Int m_DragOffset;
        private Vector2Int m_DragAnchor;
        private bool m_DragMoved;
        private bool m_TrayPainting;
        private bool m_TrayWallPaintAdd;
        private readonly HashSet<int> m_TrayPaintedBlocks = new HashSet<int>();
        private readonly HashSet<Vector2Int> m_TrayPaintedCells = new HashSet<Vector2Int>();

        // Elle top yerleşimi etkileşimi
        private PuzzleColor? m_BallBrushColor;
        private bool m_BallEraser;
        private (int col, int row)? m_SelectedBallSlot;
        private (int col, int row)? m_DragBallSlot;
        private (int col, int row)? m_HoverBallSlot;
        private bool m_BallDragMoved;
        private bool m_BallPainting;
        private int m_BallPaintButton = -1;
        private bool m_BallStrokeRecorded;
        private bool m_AutoShapeBoxesFromBalls = true;
        private readonly List<(VisualElement el, int col, int row)> m_BallSlotElements = new List<(VisualElement, int, int)>();
        private readonly Dictionary<(int col, int row), VisualElement> m_BallSlotByKey = new Dictionary<(int, int), VisualElement>();
        private readonly List<(Vector2 localCenter, float radius, int col, int row)> m_BallSlotHitCache = new List<(Vector2, float, int, int)>();
        private readonly Dictionary<PuzzleColor, (VisualElement chip, Label label)> m_BallToolbarChips = new Dictionary<PuzzleColor, (VisualElement, Label)>();
        private PuzzleColor? m_GlassFocusColor;
        private bool m_GlassHasFeedSplit;
        private readonly HashSet<(int col, int row)> m_GlassChamberSet = new HashSet<(int, int)>();

        // Çözüm
        private SolveResult m_Solve;
        private List<LevelFinding> m_Findings = new List<LevelFinding>();
        private PuzzleLevelSolver.Run m_SolveRun;   // arka planda (her karede birkaç ms) ilerleyen çözüm
        private bool m_LiveSync;
        private double m_SceneSyncDue = -1;        // sahne güncellemesi: son değişiklikten kısa süre sonra tek sefer
        private const double SceneSyncDelay = 1.0;
        private Vector2Int? m_HoverCell;            // fırçayla gezinirken hayalet önizleme

        // UI
        private ListView m_List;
        private Label m_Title, m_State, m_Summary;
        private Button m_SaveButton, m_ManualBallsBtn, m_GenToggleBtn;
        private Button m_TabLevelsBtn, m_TabSmartGenBtn;
        private VisualElement m_LevelsSubPanel, m_SmartGenSubPanel, m_SmartGenForm, m_GenStatusBox, m_GenLinksRow;
        private Label m_GenStatusText;
        private VisualElement m_Palette, m_PreviewBar, m_BoardContent, m_GlassHeader, m_BallToolbar, m_TubeCanvas, m_Glass, m_Tray, m_OrderHolder;
        private float m_TubeHeight, m_TubeScale, m_TubeHalfW, m_TubeBallDiam;
        private List<PuzzleColor> m_TubeLeftColors, m_TubeRightColors;
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
            if (m_SelectedBallSlot.HasValue && FindManualBallIndex(m_SelectedBallSlot.Value.col, m_SelectedBallSlot.Value.row) < 0)
                m_SelectedBallSlot = null;
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

            var tabs = new VisualElement();
            tabs.AddToClassList("left-tabs");
            m_TabLevelsBtn = new Button(() => SetLeftTab(false)) { text = "📋 Leveller" };
            m_TabLevelsBtn.AddToClassList("left-tabs__btn");
            m_TabSmartGenBtn = new Button(() => SetLeftTab(true)) { text = "🤖 Akıllı Üretici" };
            m_TabSmartGenBtn.AddToClassList("left-tabs__btn");
            tabs.Add(m_TabLevelsBtn);
            tabs.Add(m_TabSmartGenBtn);
            panel.Add(tabs);

            m_LevelsSubPanel = new VisualElement();
            m_LevelsSubPanel.AddToClassList("left-subpanel");
            var search = new ToolbarSearchField();
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(e => { m_Search = e.newValue ?? ""; ApplyFilter(); });
            m_LevelsSubPanel.Add(search);

            var zone = new VisualElement();
            zone.AddToClassList("create-zone");
            zone.Add(new Button(CreateEmptyLevel) { text = "Boş level", tooltip = "Sona boş bir level ekler." });
            zone.Add(new Button(DuplicateLevel) { text = "Kopyala", tooltip = "Seçili leveli kopyalayıp sona ekler." });
            var del = new Button(DeleteLevel) { text = "Sil", tooltip = "Seçili leveli çöp kutusuna taşır." };
            del.AddToClassList("inspector__danger");
            zone.Add(del);
            m_LevelsSubPanel.Add(zone);

            m_List = new ListView(m_Visible, 48, MakeLevelRow, BindLevelRow) { selectionType = SelectionType.Single };
            m_List.style.flexGrow = 1;
            m_List.selectionChanged += items =>
            {
                foreach (var item in items) { SelectLevel((PuzzleGlassLevelData)item); break; }
            };
            m_LevelsSubPanel.Add(m_List);
            panel.Add(m_LevelsSubPanel);

            m_SmartGenSubPanel = BuildSmartGenPanel();
            panel.Add(m_SmartGenSubPanel);

            SetLeftTab(m_ShowSmartGenTab);
            return panel;
        }

        private void SetLeftTab(bool smartGen)
        {
            m_ShowSmartGenTab = smartGen;
            if (m_LevelsSubPanel != null)
                m_LevelsSubPanel.style.display = smartGen ? DisplayStyle.None : DisplayStyle.Flex;
            if (m_SmartGenSubPanel != null)
                m_SmartGenSubPanel.style.display = smartGen ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_TabLevelsBtn != null)
                m_TabLevelsBtn.EnableInClassList("left-tabs__btn--active", !smartGen);
            if (m_TabSmartGenBtn != null)
                m_TabSmartGenBtn.EnableInClassList("left-tabs__btn--active", smartGen);
            if (m_GenToggleBtn != null)
                m_GenToggleBtn.EnableInClassList("chip--active", smartGen);
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
            var fullBtn = new Button(() =>
            {
                SetLeftTab(true);
                ProposeFullMechanics(Shown?.difficulty ?? m_GenDifficulty);
            })
            {
                text = "✨ Tüm Mekanikler",
                tooltip = "Sol paneldeki oranlarla çözülebilir önizleme üretir ve sol panelden ince ayar yapmanı sağlar."
            };
            row.Add(fullBtn);
            m_GenToggleBtn = Chip(() => SetLeftTab(!m_ShowSmartGenTab), m_ShowSmartGenTab);
            m_GenToggleBtn.Add(new Label("🤖 Akıllı Üretici"));
            m_GenToggleBtn.tooltip = "Sol panelde mekanik oranlarını sorarak kontrollü üretim ekranını açar.";
            row.Add(m_GenToggleBtn);
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

        private VisualElement BuildSmartGenPanel()
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.minHeight = 0;

            var scroll = new ScrollView();
            scroll.AddToClassList("smart-gen");
            m_SmartGenForm = new VisualElement();
            scroll.Add(m_SmartGenForm);
            container.Add(scroll);

            var actions = new VisualElement();
            actions.AddToClassList("smart-gen__actions");

            var previewBtn = new Button(() => ProposeFullMechanics(m_GenProgressive && m_Level != null ? m_Level.difficulty : m_GenDifficulty))
            {
                text = "👁 Seçili Levelde Önizle",
                tooltip = "Mevcut levele henüz kaydetmeden seçili oranlarla önizleme kurar (Uygula / Vazgeç ile kontrollü)."
            };
            previewBtn.AddToClassList("smart-gen__btn");
            actions.Add(previewBtn);

            var batchCountRow = new VisualElement();
            batchCountRow.AddToClassList("smart-gen__row");
            var countLabel = new Label("Yeni level adedi") { tooltip = "Tek seferde oluşturulacak çözülebilir level sayısı." };
            countLabel.AddToClassList("smart-gen__label");
            batchCountRow.Add(countLabel);
            var countReadout = new Label($"{m_GenBatchCount}");
            countReadout.AddToClassList("smart-gen__pct");
            var minusBtn = new Button(() =>
            {
                m_GenBatchCount = Mathf.Clamp(m_GenBatchCount - 1, 1, 15);
                countReadout.text = $"{m_GenBatchCount}";
            }) { text = "-" };
            minusBtn.style.width = 24; minusBtn.style.height = 20;
            var plusBtn = new Button(() =>
            {
                m_GenBatchCount = Mathf.Clamp(m_GenBatchCount + 1, 1, 15);
                countReadout.text = $"{m_GenBatchCount}";
            }) { text = "+" };
            plusBtn.style.width = 24; plusBtn.style.height = 20;
            batchCountRow.Add(minusBtn);
            batchCountRow.Add(countReadout);
            batchCountRow.Add(plusBtn);
            actions.Add(batchCountRow);

            var createBatchBtn = new Button(() => GenerateBatchLevels(m_GenBatchCount))
            {
                text = "✨ Oranlara Göre Yeni Level(ler) Üret",
                tooltip = "Belirlediğin % oranlarına göre çözülebilir yeni leveller üretir ve sonuç dökümünü gösterir."
            };
            createBatchBtn.AddToClassList("smart-gen__btn-primary");
            actions.Add(createBatchBtn);

            var quickRow = new VisualElement();
            quickRow.AddToClassList("smart-gen__batch-row");
            quickRow.Add(new Button(() => GenerateBatchLevels(1)) { text = "+1" });
            quickRow.Add(new Button(() => GenerateBatchLevels(3)) { text = "+3" });
            quickRow.Add(new Button(() => GenerateBatchLevels(5)) { text = "+5" });
            quickRow.Add(new Button(() => SetLeftTab(false)) { text = "📋 Liste" });
            actions.Add(quickRow);

            m_GenStatusBox = new VisualElement();
            m_GenStatusBox.AddToClassList("smart-gen__status");
            m_GenStatusBox.style.display = DisplayStyle.None;
            m_GenStatusText = new Label();
            m_GenStatusText.AddToClassList("smart-gen__status-text");
            m_GenStatusBox.Add(m_GenStatusText);
            m_GenLinksRow = new VisualElement();
            m_GenLinksRow.AddToClassList("smart-gen__links");
            m_GenStatusBox.Add(m_GenLinksRow);
            actions.Add(m_GenStatusBox);

            container.Add(actions);
            RebuildSmartGenForm();
            return container;
        }

        private void RebuildSmartGenForm()
        {
            if (m_SmartGenForm == null) return;
            m_SmartGenForm.Clear();

            var intro = new Label("Önce oran ve sınırları belirle, ardından önizle veya toplu üret. Tüm leveller çözücüyle doğrulanır.");
            intro.AddToClassList("smart-gen__intro");
            m_SmartGenForm.Add(intro);

            // Hazır profiller
            var presetRow = new VisualElement();
            presetRow.AddToClassList("smart-gen__presets");
            presetRow.Add(new Button(() => { m_GenOptions = FullMechanicsLevelGenerator.Options.Balanced; RebuildSmartGenForm(); }) { text = "Dengeli" });
            presetRow.Add(new Button(() => { m_GenOptions = FullMechanicsLevelGenerator.Options.HeavyMechanics; RebuildSmartGenForm(); }) { text = "Bol Mekanik" });
            presetRow.Add(new Button(() => { m_GenOptions = FullMechanicsLevelGenerator.Options.ClassicLight; RebuildSmartGenForm(); }) { text = "Sade" });
            m_SmartGenForm.Add(presetRow);

            // 1. Zorluk & Tepsi
            var secLayout = new Label("ZORLUK VE TEPSİ YERLEŞİMİ");
            secLayout.AddToClassList("smart-gen__section");
            m_SmartGenForm.Add(secLayout);

            var layoutCard = new VisualElement();
            layoutCard.AddToClassList("smart-gen__card");

            var progToggle = new Toggle("İlerleyen zorluk (toplu)")
            {
                value = m_GenProgressive,
                tooltip = "Açıkken yeni leveller numaraya göre Kolay → Orta → Zor → Boss ilerler."
            };
            progToggle.RegisterValueChangedCallback(e => m_GenProgressive = e.newValue);
            layoutCard.Add(progToggle);

            var diffSeg = new VisualElement();
            diffSeg.AddToClassList("segmented");
            diffSeg.style.marginTop = 3;
            diffSeg.style.marginBottom = 4;
            void AddDiffBtn(LevelDifficulty d, string title)
            {
                var b = new Button(() => { m_GenDifficulty = d; RebuildSmartGenForm(); }) { text = title };
                b.AddToClassList("segmented__item");
                if (m_GenDifficulty == d) b.AddToClassList("segmented__item--active");
                diffSeg.Add(b);
            }
            AddDiffBtn(LevelDifficulty.Easy, "Kolay");
            AddDiffBtn(LevelDifficulty.Medium, "Orta");
            AddDiffBtn(LevelDifficulty.Hard, "Zor");
            AddDiffBtn(LevelDifficulty.Boss, "Boss");
            layoutCard.Add(diffSeg);

            void AddAutoStepper(string title, int val, int minV, int maxV, int defOnEnable, System.Action<int> setter)
            {
                var r = new VisualElement();
                r.AddToClassList("smart-gen__row");
                var lbl = new Label(title);
                lbl.AddToClassList("smart-gen__label");
                r.Add(lbl);

                bool isAuto = val <= 0;
                var valLbl = new Label(isAuto ? "Oto" : $"{val}");
                valLbl.AddToClassList("smart-gen__pct");
                if (isAuto) valLbl.style.color = new StyleColor(new Color(0.60f, 0.62f, 0.66f));

                var autoBtn = new Button(() =>
                {
                    setter(isAuto ? defOnEnable : 0);
                    RebuildSmartGenForm();
                }) { text = isAuto ? "Oto" : "Özel" };
                autoBtn.style.height = 19; autoBtn.style.fontSize = 10; autoBtn.style.paddingLeft = 5; autoBtn.style.paddingRight = 5;

                r.Add(autoBtn);
                if (!isAuto)
                {
                    var m = new Button(() => { setter(Mathf.Clamp(val - 1, minV, maxV)); RebuildSmartGenForm(); }) { text = "-" };
                    m.style.width = 20; m.style.height = 19;
                    var p = new Button(() => { setter(Mathf.Clamp(val + 1, minV, maxV)); RebuildSmartGenForm(); }) { text = "+" };
                    p.style.width = 20; p.style.height = 19;
                    r.Add(m);
                    r.Add(valLbl);
                    r.Add(p);
                }
                else
                {
                    r.Add(valLbl);
                }
                layoutCard.Add(r);
            }

            AddAutoStepper("Tepsi Sütun", m_GenOptions.customGridCols, 4, 7, 5, v => m_GenOptions.customGridCols = v);
            AddAutoStepper("Tepsi Satır", m_GenOptions.customGridRows, 4, 7, 5, v => m_GenOptions.customGridRows = v);
            AddAutoStepper("Küp (Blok)", m_GenOptions.customBoxCount, 3, 16, 8, v => m_GenOptions.customBoxCount = v);
            AddAutoStepper("Renk Çeşidi", m_GenOptions.customColorCount, 2, 8, 4, v => m_GenOptions.customColorCount = v);
            AddAutoStepper("Üst Yuva", m_GenOptions.customSlotCount, 2, 5, 3, v => m_GenOptions.customSlotCount = v);
            m_SmartGenForm.Add(layoutCard);

            // 2. Mekanik Oranları
            var secMech = new Label("MEKANİK ORANLARI (%0 KAPALI · %100 KESİN)");
            secMech.AddToClassList("smart-gen__section");
            m_SmartGenForm.Add(secMech);

            VisualElement AddPctCard(string title, int pct, System.Action<int> setPct, System.Action<VisualElement> buildSub = null)
            {
                var card = new VisualElement();
                card.AddToClassList("smart-gen__card");

                var hdr = new VisualElement();
                hdr.AddToClassList("smart-gen__row");
                var lbl = new Label(title);
                lbl.AddToClassList("smart-gen__label");
                var pctLbl = new Label(pct <= 0 ? "Kapalı" : $"%{pct}");
                pctLbl.AddToClassList("smart-gen__pct");
                if (pct <= 0) pctLbl.style.color = new StyleColor(new Color(0.58f, 0.60f, 0.65f));
                hdr.Add(lbl);
                hdr.Add(pctLbl);
                card.Add(hdr);

                var slider = new SliderInt(0, 100) { value = pct };
                slider.RegisterValueChangedCallback(e =>
                {
                    bool wasOff = pct <= 0;
                    bool isOff = e.newValue <= 0;
                    setPct(e.newValue);
                    pctLbl.text = isOff ? "Kapalı" : $"%{e.newValue}";
                    pctLbl.style.color = isOff
                        ? new StyleColor(new Color(0.58f, 0.60f, 0.65f))
                        : new StyleColor(new Color(0.35f, 0.66f, 0.94f));
                    if (buildSub != null && wasOff != isOff) RebuildSmartGenForm();
                });
                card.Add(slider);

                if (buildSub != null && pct > 0)
                {
                    var sub = new VisualElement();
                    sub.AddToClassList("smart-gen__sub");
                    buildSub(sub);
                    card.Add(sub);
                }

                m_SmartGenForm.Add(card);
                return card;
            }

            void AddSubSlider(VisualElement parent, string labelText, int min, int max, int val, string suffix, System.Action<int> setVal)
            {
                var s = new SliderInt(labelText, min, max) { value = val, showInputField = true };
                s.tooltip = $"{labelText}: {val}{suffix}";
                s.RegisterValueChangedCallback(e => setVal(e.newValue));
                parent.Add(s);
            }

            // Anahtar & Kilit
            AddPctCard("🔑 Anahtar & Kilit", m_GenOptions.keyLockChancePct,
                v => m_GenOptions.keyLockChancePct = v,
                sub => AddSubSlider(sub, "Maks. Anahtar", 1, 3, m_GenOptions.maxKeyPairs, "", v => m_GenOptions.maxKeyPairs = v));

            // Buz
            AddPctCard("❄ Buzlu Blok", m_GenOptions.iceChancePct,
                v => m_GenOptions.iceChancePct = v,
                sub =>
                {
                    AddSubSlider(sub, "Maks. Blok", 1, 3, m_GenOptions.maxIceBlocks, "", v => m_GenOptions.maxIceBlocks = v);
                    AddSubSlider(sub, "Yoğunluk %", 25, 90, m_GenOptions.iceRatioPct, "%", v => m_GenOptions.iceRatioPct = v);

                    var meltSeg = new VisualElement();
                    meltSeg.AddToClassList("segmented");
                    meltSeg.style.marginTop = 2;
                    void AddMeltBtn(FullMechanicsLevelGenerator.IceMeltPreference pref, string t)
                    {
                        var b = new Button(() => { m_GenOptions.iceMeltPreference = pref; RebuildSmartGenForm(); }) { text = t };
                        b.AddToClassList("segmented__item");
                        b.style.height = 20; b.style.fontSize = 10;
                        if (m_GenOptions.iceMeltPreference == pref) b.AddToClassList("segmented__item--active");
                        meltSeg.Add(b);
                    }
                    AddMeltBtn(FullMechanicsLevelGenerator.IceMeltPreference.Mixed, "Karışık");
                    AddMeltBtn(FullMechanicsLevelGenerator.IceMeltPreference.PerBallCollected, "Topta");
                    AddMeltBtn(FullMechanicsLevelGenerator.IceMeltPreference.PerBoxCompleted, "Kutuda");
                    sub.Add(meltSeg);
                });

            // Üst Besleme Tüpü
            AddPctCard("🧪 Üst Besleme Tüpü", m_GenOptions.feedTubeChancePct,
                v => m_GenOptions.feedTubeChancePct = v,
                sub =>
                {
                    AddSubSlider(sub, "Tüpteki Top %", 20, 70, m_GenOptions.feedTubeHoldRatioPct, "%", v => m_GenOptions.feedTubeHoldRatioPct = v);
                    AddSubSlider(sub, "İki Yan %", 0, 100, m_GenOptions.feedTubeBothSidesPct, "%", v => m_GenOptions.feedTubeBothSidesPct = v);
                });

            // Tepsi Bölmesi
            AddPctCard("🧱 Tepsi Bölmesi (Duvar)", m_GenOptions.trayWallChancePct,
                v => m_GenOptions.trayWallChancePct = v,
                sub => AddSubSlider(sub, "Maks. Hücre", 1, 4, m_GenOptions.maxTrayWalls, "", v => m_GenOptions.maxTrayWalls = v));

            // Cam Şekli
            AddPctCard("🏺 Farklı Cam Şekli", m_GenOptions.variedGlassChancePct,
                v => m_GenOptions.variedGlassChancePct = v,
                sub =>
                {
                    var shapeSeg = new VisualElement();
                    shapeSeg.AddToClassList("segmented");
                    void AddShapeBtn(FullMechanicsLevelGenerator.GlassShapeFilter flt, string t)
                    {
                        var b = new Button(() => { m_GenOptions.glassShapeFilter = flt; RebuildSmartGenForm(); }) { text = t };
                        b.AddToClassList("segmented__item");
                        b.style.height = 20; b.style.fontSize = 10;
                        if (m_GenOptions.glassShapeFilter == flt) b.AddToClassList("segmented__item--active");
                        shapeSeg.Add(b);
                    }
                    AddShapeBtn(FullMechanicsLevelGenerator.GlassShapeFilter.All, "Tümü");
                    AddShapeBtn(FullMechanicsLevelGenerator.GlassShapeFilter.BottlesOnly, "Şişe");
                    AddShapeBtn(FullMechanicsLevelGenerator.GlassShapeFilter.BoxesOnly, "Kutu");
                    AddShapeBtn(FullMechanicsLevelGenerator.GlassShapeFilter.CustomPoolsOnly, "Özel");
                    sub.Add(shapeSeg);
                });

            // El İpucu
            var hintCard = new VisualElement();
            hintCard.AddToClassList("smart-gen__card");
            var hintToggle = new Toggle("👆 İlk Hamle El İpucu")
            {
                value = m_GenOptions.addTutorialHint,
                tooltip = "Çözücünün bulduğu ilk hamle için öğretici el ipucu ekler."
            };
            hintToggle.RegisterValueChangedCallback(e => m_GenOptions.addTutorialHint = e.newValue);
            hintCard.Add(hintToggle);
            m_SmartGenForm.Add(hintCard);
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
            m_PreviewBar.Add(new Button(() =>
            {
                if (m_PendingFullMechanics) ProposeFullMechanics(m_PendingDifficulty);
                else ProposeGenerated(m_PendingDifficulty);
            }) { text = "Yeniden üret" });
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
            int filledBalls = lvl.GetFilledBoxBallCount();
            int glassBalls = lvl.useManualBallLayout ? (lvl.manualBalls?.Count ?? 0) : lvl.GetGlassStartBallCount();
            string ballInfo = filledBalls > 0 ? $"{glassBalls}+{filledBalls} top" : $"{lvl.GetTotalBallCount()} top";
            row.Q<Label>("sub").text = $"{DifficultyName(lvl.difficulty)} · {blocks} blok · {ballInfo} · {lvl.stagingGridCols}x{lvl.stagingGridRows}";
            var dots = row.Q("dots");
            dots.Clear();
            var seen = new HashSet<PuzzleColor>();
            if (lvl.monsters != null)
                foreach (var m in lvl.monsters)
                    if (m != null)
                    {
                        if (seen.Add(m.color))
                        {
                            var dot = new VisualElement();
                            dot.AddToClassList("level-row__dot");
                            dot.style.backgroundColor = PuzzleColorPalette.GetBodyColor(m.color);
                            dots.Add(dot);
                        }
                        if (m.isFilledBox && seen.Add(m.filledBallColor))
                        {
                            var fdot = new VisualElement();
                            fdot.AddToClassList("level-row__dot");
                            fdot.style.backgroundColor = PuzzleColorPalette.GetBodyColor(m.filledBallColor);
                            dots.Add(fdot);
                        }
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
            m_SelectedBallSlot = null;
            m_DragBallSlot = null;
            m_HoverBallSlot = null;
            m_BallPainting = false;
            m_BallStrokeRecorded = false;
            if (lvl != null) SessionState.SetString(SelectedKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(lvl)));
            // Eski formatta kaydedilmiş level (kutularda şekil/konum yok): oyun bunu yüklerken bellekte çeviriyor
            // ama dosya eski kalıyor ve stüdyoda boş görünüyordu. Oyunla aynı dönüşümü yapıp kaydet.
            if (lvl != null && lvl.monsters != null && lvl.monsters.Count > 0 && !lvl.HasValidGridLayout())
            {
                Undo.RecordObject(lvl, "Eski level formatını çevir");
                lvl.EnsureCarrierBoxPuzzleFormat();
                EditorUtility.SetDirty(lvl);
                AssetDatabase.SaveAssetIfDirty(lvl);
                ShowNotification(new GUIContent("Eski formattaki level yeni formata çevrildi"));
            }
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
            lvl.useManualBallLayout = false;
            lvl.manualLaneWidth = 0;
            lvl.manualBalls = new List<ManualPoolBallEntry>();
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
            m_PendingFullMechanics = false;
            m_Pending = CreateInstance<PuzzleGlassLevelData>();
            m_Pending.hideFlags = HideFlags.DontSave;
            EditorUtility.CopySerialized(m_Level, m_Pending);
            m_Pending.difficulty = diff;
            m_PendingDifficulty = diff;
            // Zorluk hedefine (kaydırma aralığına) uyan bir level arar; camı korur
            PuzzleLevelSolver.GenerateForTarget(m_Pending, diff, tries: 10, seed: Random.Range(1, int.MaxValue));
            m_SelectedBlock = -1;
            m_SelectedGroup = -1;
            m_Solve = null;
            ScheduleSolve();
            RefreshAll();
        }

        private void ProposeFullMechanics(LevelDifficulty diff)
        {
            if (m_Level == null) return;
            DiscardPending();
            m_PendingFullMechanics = true;
            m_Pending = CreateInstance<PuzzleGlassLevelData>();
            m_Pending.hideFlags = HideFlags.DontSave;
            EditorUtility.CopySerialized(m_Level, m_Pending);
            m_Pending.difficulty = diff;
            m_PendingDifficulty = diff;
            FullMechanicsLevelGenerator.Generate(m_Pending, diff, m_GenOptions, seed: Random.Range(1, int.MaxValue));
            m_SelectedBlock = -1;
            m_SelectedGroup = -1;
            m_Solve = null;
            ScheduleSolve();
            RefreshAll();
        }

        private void GenerateBatchLevels(int count)
        {
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);
            count = Mathf.Clamp(count, 1, 20);
            int next = NextLevelNumber();
            var createdList = new List<PuzzleGlassLevelData>(count);
            var batchPlan = FullMechanicsLevelGenerator.PlanBatch(m_GenOptions, count, Random.Range(1, int.MaxValue));
            int solvedCount = 0;
            int keysCount = 0, iceCount = 0, tubeCount = 0, wallCount = 0;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Akıllı Level Üretici",
                        $"Level {next} üretiliyor ve doğrulanıyor ({i + 1}/{count})...",
                        (float)i / count))
                        break;

                    var lvl = CreateInstance<PuzzleGlassLevelData>();
                    lvl.levelNumber = next;
                    var diff = m_GenProgressive ? PuzzleLevelGenerator.GetProgressiveDifficulty(next) : m_GenDifficulty;
                    var solve = FullMechanicsLevelGenerator.Generate(lvl, diff, batchPlan[i], seed: next * 7919 + Random.Range(1, 9999));
                    if (solve != null && solve.status == SolveStatus.Solved) solvedCount++;

                    bool hasK = false, hasI = false;
                    if (lvl.monsters != null)
                    {
                        foreach (var m in lvl.monsters)
                        {
                            if (m == null) continue;
                            if (m.hasKey || m.lockKeyCount > 0) hasK = true;
                            if (m.iceCount > 0) hasI = true;
                        }
                    }
                    if (hasK) keysCount++;
                    if (hasI) iceCount++;
                    if (lvl.useBallFeedTubes) tubeCount++;
                    if (lvl.gridWalls != null && lvl.gridWalls.Count > 0) wallCount++;

                    string path = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/Level_{next:00}.asset");
                    AssetDatabase.CreateAsset(lvl, path);
                    createdList.Add(lvl);
                    next++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            LoadLevels(keepSelection: false);
            SyncGameManagerLevels();
            if (createdList.Count > 0) SelectAndShow(createdList[0]);

            if (m_GenStatusBox != null && m_GenStatusText != null && m_GenLinksRow != null)
            {
                int produced = Mathf.Max(1, createdList.Count);
                m_GenStatusText.text =
                    $"Üretildi: {solvedCount}/{createdList.Count} çözülebilir\n" +
                    $"🔑 {keysCount}/{produced} · ❄ {iceCount}/{produced} · 🧪 {tubeCount}/{produced} · 🧱 {wallCount}/{produced}";
                m_GenLinksRow.Clear();
                foreach (var created in createdList)
                {
                    var captured = created;
                    var linkBtn = new Button(() => SelectAndShow(captured)) { text = $"L{captured.levelNumber}" };
                    linkBtn.AddToClassList("smart-gen__link");
                    m_GenLinksRow.Add(linkBtn);
                }
                m_GenStatusBox.style.display = createdList.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            ShowNotification(new GUIContent($"{solvedCount}/{createdList.Count} çözülebilir level üretildi (kontrol edebilirsin)"));
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
            // Kanalda elle dizim kapasitesi henüz sabitlenmemişse ilk düzenlemeden önce mevcut top sayısına sabitle
            if (m_Level != null && m_Level.useManualBallLayout && m_Level.manualTrackCapacity <= 0 && GlassShapeData.IsTrack(m_Level))
                m_Level.manualTrackCapacity = Mathf.Max(1, m_Level.manualBalls != null ? m_Level.manualBalls.Count : 0);
        }

        private void OnEdited(bool syncScene)
        {
            if (m_Level == null) return;
            // Kanalda elle dizim: adresleri oturulan slotlara göre güncelle, konumları kaydet
            GlassShapeData.CanonicalizeManualTrackBalls(m_Level);
            EditorUtility.SetDirty(m_Level);
            m_Solve = null;
            ScheduleSolve();
            RefreshAll();
            m_List?.RefreshItems();
            if (syncScene && m_LiveSync) ScheduleSceneSync();
        }

        private static void ResolveFilledTarget(
            PuzzleGlassLevelData lvl,
            int excludeIndex,
            PuzzleColor preferredColor,
            int currentCount,
            bool allowFallbackColor,
            out PuzzleColor resolvedColor,
            out int resolvedCount)
        {
            resolvedColor = preferredColor;
            int clampedCurrent = Mathf.Clamp(currentCount, 1, LevelMonsterEntry.MaxBallsPerEntry);
            resolvedCount = clampedCurrent;
            if (lvl == null) return;

            var remaining = lvl.GetRemainingCollectorCapacityByColor(excludeIndex);
            if (!remaining.TryGetValue(resolvedColor, out int cap) || cap <= 0)
            {
                if (allowFallbackColor)
                {
                    PuzzleColor? bestCol = null;
                    int bestCap = 0;
                    foreach (var kv in remaining)
                    {
                        if (kv.Value <= 0) continue;
                        if (kv.Value >= clampedCurrent)
                        {
                            if (!bestCol.HasValue || bestCap < clampedCurrent || kv.Value > bestCap)
                            {
                                bestCol = kv.Key;
                                bestCap = kv.Value;
                            }
                        }
                        else if (!bestCol.HasValue || (bestCap < clampedCurrent && kv.Value > bestCap))
                        {
                            bestCol = kv.Key;
                            bestCap = kv.Value;
                        }
                    }
                    if (bestCol.HasValue)
                    {
                        resolvedColor = bestCol.Value;
                        cap = bestCap;
                    }
                }
            }

            if (cap > 0)
            {
                resolvedCount = Mathf.Clamp(clampedCurrent, 1, cap);
            }
        }

        private static void SyncManualBallsAfterBoxEdit(PuzzleGlassLevelData lvl)
        {
            if (lvl == null || !lvl.useManualBallLayout || lvl.manualBalls == null || lvl.manualBalls.Count == 0) return;

            var remaining = lvl.GetRemainingCollectorCapacityByColor(-1);
            var placed = new Dictionary<PuzzleColor, int>();
            for (int i = 0; i < lvl.manualBalls.Count; i++)
            {
                var b = lvl.manualBalls[i];
                if (b != null) placed[b.color] = (placed.TryGetValue(b.color, out int v) ? v : 0) + 1;
            }

            foreach (var kv in placed)
            {
                remaining.TryGetValue(kv.Key, out int exp);
                int expected = Mathf.Max(0, exp);
                int excess = kv.Value - expected;
                if (excess <= 0) continue;

                var candidates = new List<int>();
                for (int i = 0; i < lvl.manualBalls.Count; i++)
                {
                    var b = lvl.manualBalls[i];
                    if (b != null && b.color == kv.Key) candidates.Add(i);
                }
                candidates.Sort((a, b) =>
                {
                    var ba = lvl.manualBalls[a];
                    var bb = lvl.manualBalls[b];
                    int cmp = bb.row.CompareTo(ba.row);
                    return cmp != 0 ? cmp : bb.column.CompareTo(ba.column);
                });

                var removeSet = new HashSet<int>();
                for (int k = 0; k < excess && k < candidates.Count; k++) removeSet.Add(candidates[k]);
                for (int i = lvl.manualBalls.Count - 1; i >= 0; i--)
                {
                    if (removeSet.Contains(i)) lvl.manualBalls.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Elle dizilen toplara göre tepsideki kutuları otomatik şekillendirir:
        /// mevcut kutuların sayılarını koyulan top adedine göre böler, topları silinen rengin kutularını kaldırır,
        /// yeni eklenen renkler için boş tepsi hücresine kutu açar (yer kalmazsa tepsi satırını büyütür).
        /// </summary>
        private void SyncBoxesFromManualBalls(PuzzleGlassLevelData lvl)
        {
            if (!m_AutoShapeBoxesFromBalls || lvl == null || !lvl.useManualBallLayout) return;
            if (lvl.manualBalls == null || lvl.manualBalls.Count == 0) return;
            if (lvl.monsters == null) lvl.monsters = new List<LevelMonsterEntry>();

            var glassCount = new Dictionary<PuzzleColor, int>();
            for (int i = 0; i < lvl.manualBalls.Count; i++)
            {
                var b = lvl.manualBalls[i];
                if (b != null) glassCount[b.color] = (glassCount.TryGetValue(b.color, out int v) ? v : 0) + 1;
            }

            var toRemove = new HashSet<int>();
            for (int pass = 0; pass < 2; pass++)
            {
                toRemove.Clear();
                var supplyCount = new Dictionary<PuzzleColor, int>();
                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    if (m != null && m.isFilledBox)
                        supplyCount[m.filledBallColor] = (supplyCount.TryGetValue(m.filledBallColor, out int sv) ? sv : 0) + Mathf.Max(1, m.count);
                }

                var needed = new Dictionary<PuzzleColor, int>();
                foreach (var kv in glassCount) needed[kv.Key] = kv.Value;
                foreach (var kv in supplyCount) needed[kv.Key] = (needed.TryGetValue(kv.Key, out int v) ? v : 0) + kv.Value;

                var byColor = new Dictionary<PuzzleColor, List<int>>();
                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    if (m == null) { toRemove.Add(i); continue; }
                    if (!byColor.TryGetValue(m.color, out var list))
                        byColor[m.color] = list = new List<int>();
                    list.Add(i);
                }

                foreach (var kv in byColor)
                {
                    needed.TryGetValue(kv.Key, out int targetTotal);
                    var idxs = kv.Value;
                    if (targetTotal <= 0)
                    {
                        foreach (int idx in idxs) toRemove.Add(idx);
                        continue;
                    }

                    int activeBoxes = Mathf.Min(idxs.Count, targetTotal);
                    for (int k = activeBoxes; k < idxs.Count; k++) toRemove.Add(idxs[k]);

                    int baseCnt = targetTotal / activeBoxes;
                    int rem = targetTotal % activeBoxes;
                    for (int k = 0; k < activeBoxes; k++)
                    {
                        var box = lvl.monsters[idxs[k]];
                        box.count = Mathf.Clamp(baseCnt + (k < rem ? 1 : 0), 1, LevelMonsterEntry.MaxBallsPerEntry);
                    }
                }
            }

            if (toRemove.Count > 0)
            {
                for (int i = lvl.monsters.Count - 1; i >= 0; i--)
                    if (toRemove.Contains(i)) lvl.monsters.RemoveAt(i);
            }

            var finalSupply = new Dictionary<PuzzleColor, int>();
            var collectorCap = new Dictionary<PuzzleColor, int>();
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var m = lvl.monsters[i];
                if (m == null) continue;
                int c = Mathf.Max(1, m.count);
                collectorCap[m.color] = (collectorCap.TryGetValue(m.color, out int cv) ? cv : 0) + c;
                if (m.isFilledBox)
                    finalSupply[m.filledBallColor] = (finalSupply.TryGetValue(m.filledBallColor, out int sv) ? sv : 0) + c;
            }

            var finalNeeded = new Dictionary<PuzzleColor, int>();
            foreach (var kv in glassCount) finalNeeded[kv.Key] = kv.Value;
            foreach (var kv in finalSupply) finalNeeded[kv.Key] = (finalNeeded.TryGetValue(kv.Key, out int v) ? v : 0) + kv.Value;

            var orderedSpawnColors = new List<PuzzleColor>();
            foreach (var c in BrushColors)
                if (finalNeeded.ContainsKey(c)) orderedSpawnColors.Add(c);

            int gridCols = Mathf.Max(2, lvl.stagingGridCols);
            int gridRows = Mathf.Max(2, lvl.stagingGridRows);
            var occupiedCells = new HashSet<Vector2Int>();
            if (lvl.gridWalls != null)
                foreach (var w in lvl.gridWalls) occupiedCells.Add(w);
            for (int i = 0; i < lvl.monsters.Count; i++)
            {
                var m = lvl.monsters[i];
                if (m == null) continue;
                foreach (var c in CarrierBlockShapeUtility.GetCells(m.blockShape))
                    occupiedCells.Add(m.gridAnchor + c);
            }

            Vector2Int NextFreeTrayCell()
            {
                for (int y = gridRows - 1; y >= 0; y--)
                {
                    for (int x = 0; x < gridCols; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (!occupiedCells.Contains(cell)) return cell;
                    }
                }
                gridRows = Mathf.Min(12, gridRows + 1);
                lvl.stagingGridRows = gridRows;
                return new Vector2Int(0, gridRows - 1);
            }

            foreach (var c in orderedSpawnColors)
            {
                finalNeeded.TryGetValue(c, out int need);
                collectorCap.TryGetValue(c, out int have);
                int deficit = need - have;
                while (deficit > 0)
                {
                    int chunk = Mathf.Min(deficit, LevelMonsterEntry.MaxBallsPerEntry);
                    var cell = NextFreeTrayCell();
                    occupiedCells.Add(cell);
                    lvl.monsters.Add(new LevelMonsterEntry(c, chunk, CarrierBlockShape.Rect1x1, cell) { hasGridAnchor = true });
                    deficit -= chunk;
                }
            }

            ClampSelection();
        }

        private void SyncGlassAfterBoxChange(bool forceRebuild)
        {
            if (m_Level == null) return;
            if (m_Level.useManualBallLayout)
            {
                SyncManualBallsAfterBoxEdit(m_Level);
            }
            else if (forceRebuild || m_Level.HasAnyFilledBox())
            {
                m_Level.RebuildTripletsFromBoxes();
            }
            else
            {
                m_Level.SyncTripletsFromGroups();
            }
        }

        private void AddBlock(PuzzleColor color, CarrierBlockShape shape, Vector2Int anchor)
        {
            Record("Blok ekle");
            int count = Mathf.Clamp(CarrierBlockShapeUtility.GetCellCount(shape) * 2, 1, LevelMonsterEntry.MaxBallsPerEntry);
            var entry = new LevelMonsterEntry(color, count, shape, anchor) { hasGridAnchor = true };
            if (m_BrushMechanic == MechanicBrush.Key) entry.hasKey = true;
            else if (m_BrushMechanic == MechanicBrush.Lock) entry.lockKeyCount = Mathf.Max(1, m_BrushLockCount);
            else if (m_BrushMechanic == MechanicBrush.Ice) entry.iceCount = Mathf.Max(1, m_BrushIceCount);
            else if (m_BrushMechanic == MechanicBrush.Filled)
            {
                PuzzleColor targetFill = m_BrushFilledColor ?? color;
                int resolvedCount = count;
                if (targetFill != color)
                {
                    ResolveFilledTarget(
                        m_Level, -1, targetFill, count,
                        allowFallbackColor: false,
                        out targetFill, out resolvedCount);
                }
                entry.isFilledBox = true;
                entry.filledBallColor = targetFill;
                entry.count = resolvedCount;
            }
            m_Level.monsters.Add(entry);
            if (!m_Level.useManualBallLayout && (entry.isFilledBox || m_Level.HasAnyFilledBox()))
            {
                m_Level.RebuildTripletsFromBoxes();
            }
            else if (!m_Level.useManualBallLayout)
            {
                if (m_Level.bottomToTopGroups == null) m_Level.bottomToTopGroups = new List<LevelMonsterEntry>();
                m_Level.bottomToTopGroups.Add(new LevelMonsterEntry(color, entry.count));
                m_Level.carrierBoxFormatVersion = 3;
                m_Level.SyncTripletsFromGroups();
            }
            else
            {
                SyncManualBallsAfterBoxEdit(m_Level);
            }
            m_SelectedBlock = m_Level.monsters.Count - 1;
            OnEdited(syncScene: true);
        }

        private void SetBlockHasKey(int i, bool hasKey)
        {
            if (!ValidBlock(i)) return;
            var m = m_Level.monsters[i];
            if (m.hasKey == hasKey) return;
            Record("Anahtarlı blok");
            m.hasKey = hasKey;
            OnEdited(syncScene: true);
        }

        private void SetBlockLockKeys(int i, int lockKeys)
        {
            if (!ValidBlock(i)) return;
            lockKeys = Mathf.Max(0, lockKeys);
            var m = m_Level.monsters[i];
            if (m.lockKeyCount == lockKeys) return;
            Record("Kilit anahtar sayısı");
            m.lockKeyCount = lockKeys;
            OnEdited(syncScene: true);
        }

        private void SetBlockIceCount(int i, int iceCount)
        {
            if (!ValidBlock(i)) return;
            iceCount = Mathf.Max(0, iceCount);
            var m = m_Level.monsters[i];
            if (m.iceCount == iceCount) return;
            Record("Buz sayacı");
            m.iceCount = iceCount;
            OnEdited(syncScene: true);
        }

        private void SetBlockIsFilled(int i, bool isFilled)
        {
            if (!ValidBlock(i)) return;
            var m = m_Level.monsters[i];
            if (m.isFilledBox == isFilled) return;
            Record("İçi dolu kutu");
            m.isFilledBox = isFilled;
            if (isFilled)
            {
                ResolveFilledTarget(m_Level, i, m.color, m.count, allowFallbackColor: true, out var ballColor, out var newCount);
                m.filledBallColor = ballColor;
                m.count = newCount;
            }
            SyncGlassAfterBoxChange(forceRebuild: true);
            OnEdited(syncScene: true);
        }

        private void SetBlockFilledColor(int i, PuzzleColor ballColor)
        {
            if (!ValidBlock(i)) return;
            var m = m_Level.monsters[i];
            if (m.filledBallColor == ballColor) return;
            Record("Dolu kutu top rengi");
            m.filledBallColor = ballColor;
            if (m.isFilledBox)
            {
                ResolveFilledTarget(m_Level, i, ballColor, m.count, allowFallbackColor: false, out _, out int newCount);
                m.count = newCount;
            }
            SyncGlassAfterBoxChange(forceRebuild: m.isFilledBox);
            OnEdited(syncScene: true);
        }

        private void ToggleMechanicBrushOnBlock(int i, MechanicBrush brush)
        {
            if (!ValidBlock(i)) return;
            var m = m_Level.monsters[i];
            switch (brush)
            {
                case MechanicBrush.Key:
                    SetBlockHasKey(i, !m.hasKey);
                    break;
                case MechanicBrush.Lock:
                    int targetLock = Mathf.Max(1, m_BrushLockCount);
                    SetBlockLockKeys(i, m.lockKeyCount == targetLock ? 0 : targetLock);
                    break;
                case MechanicBrush.Ice:
                    int targetIce = Mathf.Max(1, m_BrushIceCount);
                    SetBlockIceCount(i, m.iceCount == targetIce ? 0 : targetIce);
                    break;
                case MechanicBrush.Filled:
                    if (!m.isFilledBox)
                    {
                        Record("İçi dolu kutu");
                        bool allowFallback = !m_BrushFilledColor.HasValue;
                        PuzzleColor preferred = m_BrushFilledColor ?? m.color;
                        ResolveFilledTarget(m_Level, i, preferred, m.count, allowFallback, out var ballColor, out var newCount);
                        m.isFilledBox = true;
                        m.filledBallColor = ballColor;
                        m.count = newCount;
                        SyncGlassAfterBoxChange(forceRebuild: true);
                        OnEdited(syncScene: true);
                    }
                    else if (m_BrushFilledColor.HasValue && m.filledBallColor != m_BrushFilledColor.Value)
                    {
                        SetBlockFilledColor(i, m_BrushFilledColor.Value);
                    }
                    else
                    {
                        SetBlockIsFilled(i, false);
                    }
                    break;
            }
        }

        private void RemoveBlock(int i)
        {
            if (!ValidBlock(i)) return;
            Record("Blok sil");
            var m = m_Level.monsters[i];
            bool wasFilled = m.isFilledBox;
            bool hadFilled = wasFilled || m_Level.HasAnyFilledBox();
            if (!wasFilled && !hadFilled && !m_Level.useManualBallLayout)
            {
                int g = FindGroup(m.color, m.count, fromTop: true);
                if (g >= 0) m_Level.bottomToTopGroups.RemoveAt(g);
            }
            m_Level.monsters.RemoveAt(i);
            SyncGlassAfterBoxChange(forceRebuild: hadFilled);
            m_SelectedBlock = -1;
            m_SelectedGroup = -1;
            OnEdited(syncScene: true);
        }

        private void SetBlockColor(int i, PuzzleColor c)
        {
            if (!ValidBlock(i) || m_Level.monsters[i].color == c) return;
            Record("Blok rengi");
            var m = m_Level.monsters[i];
            bool hasFilled = m_Level.HasAnyFilledBox();
            if (!m.isFilledBox && !hasFilled && !m_Level.useManualBallLayout)
            {
                int g = FindGroup(m.color, m.count, fromTop: false);
                if (g >= 0) m_Level.bottomToTopGroups[g].color = c;
            }
            m.color = c;
            SyncGlassAfterBoxChange(forceRebuild: hasFilled);
            OnEdited(syncScene: true);
        }

        private void SetBlockCount(int i, int count)
        {
            if (!ValidBlock(i)) return;
            count = Mathf.Clamp(count, 1, LevelMonsterEntry.MaxBallsPerEntry);
            var m = m_Level.monsters[i];
            if (m.count == count) return;
            Record(m.isFilledBox ? "Dolu kutu top sayısı" : "Yuva sayısı");
            bool hasFilled = m.isFilledBox || m_Level.HasAnyFilledBox();
            if (!m.isFilledBox && !hasFilled && !m_Level.useManualBallLayout)
            {
                int g = FindGroup(m.color, m.count, fromTop: false);
                if (g >= 0) m_Level.bottomToTopGroups[g].count = count;
            }
            m.count = count;
            SyncGlassAfterBoxChange(forceRebuild: hasFilled);
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

        private static bool IsWall(PuzzleGlassLevelData lvl, int x, int y)
        {
            if (lvl?.gridWalls == null) return false;
            for (int i = 0; i < lvl.gridWalls.Count; i++)
                if (lvl.gridWalls[i].x == x && lvl.gridWalls[i].y == y) return true;
            return false;
        }

        private static void PruneOutOfBoundsWalls(PuzzleGlassLevelData lvl)
        {
            if (lvl?.gridWalls == null) return;
            for (int i = lvl.gridWalls.Count - 1; i >= 0; i--)
            {
                var w = lvl.gridWalls[i];
                if (w.x < 0 || w.y < 0 || w.x >= lvl.stagingGridCols || w.y >= lvl.stagingGridRows)
                    lvl.gridWalls.RemoveAt(i);
            }
        }

        private void ToggleWallCell(Vector2Int cell, bool? forceAdd = null)
        {
            if (m_Level == null || m_Pending != null) return;
            if (m_Level.gridWalls == null) m_Level.gridWalls = new List<Vector2Int>();
            int idx = m_Level.gridWalls.IndexOf(cell);
            bool shouldAdd = forceAdd ?? (idx < 0);
            if (shouldAdd && idx >= 0) return;
            if (!shouldAdd && idx < 0) return;
            Record(shouldAdd ? "Tepsi bölmesi ekle" : "Tepsi bölmesi sil");
            if (shouldAdd) m_Level.gridWalls.Add(cell);
            else m_Level.gridWalls.RemoveAt(idx);
            OnEdited(syncScene: true);
        }

        private void SetQuickGlassSize(bool compact)
        {
            if (m_Level == null || m_Pending != null) return;
            Record(compact ? "Kompakt cam" : "Normal cam");
            if (compact)
            {
                m_Level.useCustomGlassShape = true;
                m_Level.glassIsBottle = true;
                m_Level.glassPreset = GlassShapePreset.CustomFreeform;
                m_Level.symmetricMode = true;
                m_Level.widthScale = 1f;
                m_Level.heightScale = 1f;
                m_Level.showShoulderHandles = false;
                m_Level.contourPoints = ShapedGlassContainer.BuildBottleContour(2.70f, 0.62f);
                m_Level.boxCornerRadius = 0.22f;
                m_Level.boxFrameThickness = 0.055f;
            }
            else
            {
                m_Level.useCustomGlassShape = false;
                m_Level.glassIsBottle = false;
            }
            OnEdited(syncScene: true);
        }

        /// <summary>Şekil verilen konumda tepsiye sığıyor, bölmeye çarpmıyor ve başka blokla çakışmıyor mu? (ignore = taşınan blok)</summary>
        private static bool Fits(PuzzleGlassLevelData lvl, CarrierBlockShape shape, Vector2Int anchor, int ignore)
        {
            var own = CarrierBlockShapeUtility.GetCells(shape);
            foreach (var c in own)
            {
                int x = anchor.x + c.x, y = anchor.y + c.y;
                if (x < 0 || y < 0 || x >= lvl.stagingGridCols || y >= lvl.stagingGridRows) return false;
                if (IsWall(lvl, x, y)) return false;
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
            // Fırça boyama veya sürükleme sırasında arka plan çözücü/sahne senkronizasyonu arayüzü dondurmasın
            if (m_BallPainting || m_DragBallSlot.HasValue || m_DragBlock >= 0 || m_TrayPainting) return;

            if (m_SolveRun != null)
            {
                if (m_SolveRun.Step(3.0))
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
            // Eski (konumsuz) kanal dizimleri ilk açılışta konumlu hale getirilir; görünen dizilim değişmez
            if (lvl != null && lvl == m_Level && GlassShapeData.CanonicalizeManualTrackBalls(lvl))
                EditorUtility.SetDirty(lvl);
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
            m_PreviewTitle.text = m_PendingFullMechanics
                ? $"Önizleme: tüm mekanikli {DifficultyName(m_PendingDifficulty)} level"
                : $"Önizleme: otomatik {DifficultyName(m_PendingDifficulty)} level";
            string solve = m_Solve == null ? "çözüm aranıyor…" : SolveText(m_Solve);
            string tube = m_Pending.useBallFeedTubes ? " · 🧪 tüp" : "";
            int filledBalls = m_Pending.GetFilledBoxBallCount();
            int glassBalls = m_Pending.GetGlassStartBallCount();
            string ballTxt = filledBalls > 0 ? $"{glassBalls} cam + {filledBalls} dolu kutu" : $"{m_Pending.GetTotalBallCount()} top";
            m_PreviewDetail.text = $"{m_Pending.monsters.Count} blok · {ballTxt} · {m_Pending.stagingGridCols}x{m_Pending.stagingGridRows} tepsi{tube} · {solve}. Uygula derseniz şimdiki levelin yerine geçer.";
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
            var mechLabel = new Label("Mekanik");
            mechLabel.AddToClassList("palette__label");
            m_Palette.Add(mechLabel);
            AddMechanicChip("🔑 Anahtar", MechanicBrush.Key, "Bloğa tıkla/sürükle = anahtar ekle/kaldır (tamamlanınca 1 kilit açar).");
            AddMechanicChip("🔒 Kilit", MechanicBrush.Lock, "Bloğa tıkla/sürükle = kilit ekle/kaldır.");
            AddMechanicChip("❄ Buz", MechanicBrush.Ice, "Bloğa tıkla/sürükle = buz ekle/kaldır.");
            AddMechanicChip("●↑ Dolu", MechanicBrush.Filled, "Bloğa tıkla/sürükle = içi dolu kutu yap/kaldır; boş hücreye tıkla = içi dolu kutu ekle.");
            AddMechanicChip("🧱 Bölme", MechanicBrush.Wall, "Boş hücreye tıkla/sürükle = tepsi duvarı/bölmesi ekle veya kaldır.");

            if (m_BrushMechanic == MechanicBrush.Lock)
            {
                foreach (int lk in new[] { 1, 2, 3 })
                {
                    int v = lk;
                    var sub = Chip(() => { m_BrushLockCount = v; RefreshPalette(); }, m_BrushLockCount == v);
                    sub.Add(new Label($"{v}🔑"));
                    sub.tooltip = $"Kilit fırçası: {v} anahtar";
                    m_Palette.Add(sub);
                }
            }
            else if (m_BrushMechanic == MechanicBrush.Ice)
            {
                foreach (int ic in new[] { 5, 10, 15 })
                {
                    int v = ic;
                    var sub = Chip(() => { m_BrushIceCount = v; RefreshPalette(); }, m_BrushIceCount == v);
                    sub.Add(new Label($"{v}❄"));
                    sub.tooltip = $"Buz fırçası: {v} sayaç";
                    m_Palette.Add(sub);
                }
            }
            else if (m_BrushMechanic == MechanicBrush.Filled)
            {
                var sameChip = Chip(() => { m_BrushFilledColor = null; RefreshPalette(); }, !m_BrushFilledColor.HasValue);
                sameChip.Add(new Label("Aynı"));
                sameChip.tooltip = "Dolu kutunun içindeki top rengi kutu çerçevesiyle aynı olsun.";
                m_Palette.Add(sameChip);

                foreach (var fc in BrushColors)
                {
                    var fcol = fc;
                    var fchip = Chip(() => { m_BrushFilledColor = fcol; RefreshPalette(); }, m_BrushFilledColor == fcol);
                    var sw = new VisualElement();
                    sw.AddToClassList("swatch");
                    sw.style.backgroundColor = PuzzleColorPalette.GetBodyColor(fcol);
                    fchip.Add(sw);
                    fchip.tooltip = $"Dolu kutu iç top rengi: {PuzzleLevelSolver.ColorName(fcol)}";
                    m_Palette.Add(fchip);
                }
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

            if (m_BrushColor != null || m_BrushShape != null || m_BrushMechanic != MechanicBrush.None || m_Eraser)
            {
                var clear = Chip(() => { ClearBrush(); RefreshAll(); }, false);
                clear.Add(new Label("Fırçayı bırak (Esc)"));
                m_Palette.Add(clear);
            }
        }

        private void AddMechanicChip(string text, MechanicBrush brush, string tooltip)
        {
            var chip = Chip(() =>
            {
                m_BrushMechanic = m_BrushMechanic == brush ? MechanicBrush.None : brush;
                m_Eraser = false;
                RefreshAll();
            }, m_BrushMechanic == brush);
            chip.Add(new Label(text));
            chip.tooltip = tooltip;
            m_Palette.Add(chip);
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
            m_BrushMechanic = MechanicBrush.None;
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

            m_GlassHeader = new VisualElement();
            m_GlassHeader.AddToClassList("glass-header");
            m_GlassCaption = new Label("Cam (toplar alttan üste; seçili bloğun deliğinin üstü ve rengi vurgulanır)");
            m_GlassCaption.AddToClassList("board__caption");
            m_GlassHeader.Add(m_GlassCaption);
            m_ManualBallsBtn = Chip(ToggleManualBallLayout, false);
            m_ManualBallsBtn.Add(new Label("✋ Elle top diz"));
            m_ManualBallsBtn.tooltip = "Açıkken camdaki slotlara tıklayıp sürükleyerek topları kendin yerleştirebilirsin.";
            m_GlassHeader.Add(m_ManualBallsBtn);
            m_BoardContent.Add(m_GlassHeader);

            m_BallToolbar = new VisualElement();
            m_BallToolbar.AddToClassList("ball-bar");
            m_BoardContent.Add(m_BallToolbar);

            m_TubeCanvas = new VisualElement();
            m_TubeCanvas.pickingMode = PickingMode.Ignore;
            m_TubeCanvas.style.marginBottom = -4f;
            m_TubeCanvas.style.overflow = Overflow.Visible;
            m_TubeCanvas.generateVisualContent += DrawStudioFeedTubes;
            m_BoardContent.Add(m_TubeCanvas);

            m_Glass = new VisualElement();
            m_Glass.AddToClassList("glass");
            m_Glass.RegisterCallback<PointerDownEvent>(OnGlassPointerDown);
            m_Glass.RegisterCallback<PointerMoveEvent>(OnGlassPointerMove);
            m_Glass.RegisterCallback<PointerUpEvent>(OnGlassPointerUp);
            m_Glass.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (m_HoverBallSlot.HasValue && !m_BallPainting && !m_DragBallSlot.HasValue)
                {
                    SetHoverBallSlot(null);
                }
            });
            m_BoardContent.Add(m_Glass);

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
            foreach (var el in new VisualElement[] { m_GlassHeader, m_Glass, m_Tray, m_OrderCaption, m_OrderHolder })
                el.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has)
            {
                m_BallToolbar.style.display = DisplayStyle.None;
                m_TubeCanvas.style.display = DisplayStyle.None;
                return;
            }

            bool manual = lvl.useManualBallLayout;
            m_ManualBallsBtn.EnableInClassList("chip--active", manual);
            m_ManualBallsBtn.SetEnabled(m_Pending == null);
            m_GlassCaption.text = manual
                ? "Cam (elle dizim: slota tıkla/sürükle = koy veya taşı, sağ tık = sil)"
                : "Cam (toplar alttan üste; seçili bloğun deliğinin üstü ve rengi vurgulanır)";

            m_BallToolbar.style.display = manual && m_Pending == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (manual && m_Pending == null) RefreshBallToolbar(lvl);

            RedrawGlass();
            RedrawTray();

            m_OrderCaption.text = manual
                ? "Cam sırası devre dışı (elle top yerleşimi açık)"
                : "Cam sırası (alttan üste) · bir çipe, sonra diğerine tıkla = yer değiştir";
            m_OrderHolder.style.display = manual ? DisplayStyle.None : DisplayStyle.Flex;
            m_OrderHolder.Clear();
            if (!manual) m_OrderHolder.Add(BuildOrder(lvl));
        }

        private int m_CellCols = -1, m_CellRows = -1;
        private float m_LastTrayCellPx = -1f;

        private void RedrawTray()
        {
            var lvl = Shown;
            if (lvl == null) { m_Tray.Clear(); m_CellElements.Clear(); m_CellCols = m_CellRows = -1; return; }
            int cols = lvl.stagingGridCols, rows = lvl.stagingGridRows;
            float cellPx = CellPx;
            m_Tray.style.width = cols * cellPx + 6f;   // + kenarlık
            m_Tray.style.height = rows * cellPx + 6f;

            var critical = new HashSet<int>();
            foreach (var f in m_Findings) if (f.severity == FindingSeverity.Critical && f.blockIndex >= 0) critical.Add(f.blockIndex);

            // Hücre kutuları kalıcı: sadece tepsi boyutu veya hücre boyutu değişince yeniden kurulur
            if (cols != m_CellCols || rows != m_CellRows || !Mathf.Approximately(cellPx, m_LastTrayCellPx) || m_CellElements.Count == 0)
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
                m_LastTrayCellPx = cellPx;
            }
            else
            {
                // Hücreler kalsın, blokları ve duvarları temizle
                for (int i = m_Tray.childCount - 1; i >= 0; i--)
                    if (!m_Tray[i].ClassListContains("tray__cell")) m_Tray.RemoveAt(i);
            }

            var wallSet = new HashSet<Vector2Int>();
            if (lvl.gridWalls != null)
            {
                foreach (var w in lvl.gridWalls)
                    if (w.x >= 0 && w.y >= 0 && w.x < cols && w.y < rows) wallSet.Add(w);
                foreach (var w in wallSet) DrawTrayWallCell(w, rows, wallSet, ghost: false);
            }

            if (lvl.monsters != null)
            {
                for (int i = 0; i < lvl.monsters.Count; i++)
                {
                    var m = lvl.monsters[i];
                    if (m == null) continue;
                    bool dragging = i == m_DragBlock && m_Pending == null;
                    Vector2Int anchor = dragging ? m_DragAnchor : m.gridAnchor;
                    bool invalid = dragging && !Fits(lvl, m.blockShape, anchor, i);
                    DrawBlock(m_Tray, m, i, anchor, rows, i == m_SelectedBlock, critical.Contains(i), dragging, invalid);
                }
            }

            // Fırça hayaleti
            if (m_HoverCell.HasValue && m_Pending == null && (m_BrushColor != null || m_BrushShape != null || m_BrushMechanic != MechanicBrush.None) && !m_Eraser)
            {
                var cell = m_HoverCell.Value;
                if (m_BrushMechanic == MechanicBrush.Wall)
                {
                    if (BlockAt(lvl, cell) < 0)
                    {
                        var ghostWalls = new HashSet<Vector2Int>(wallSet) { cell };
                        DrawTrayWallCell(cell, rows, ghostWalls, ghost: true);
                    }
                    return;
                }
                int hit = BlockAt(lvl, cell);
                var shape = m_BrushShape ?? (hit >= 0 ? lvl.monsters[hit].blockShape : CarrierBlockShape.Rect1x1);
                var color = m_BrushColor ?? (hit >= 0 ? lvl.monsters[hit].color : PuzzleColor.Blue);
                Vector2Int? at = hit >= 0
                    ? FindPlacement(lvl, shape, cell, hit, lvl.monsters[hit].gridAnchor)
                    : FindPlacement(lvl, shape, cell, -1);
                var ghost = new LevelMonsterEntry(color, Mathf.Clamp(CarrierBlockShapeUtility.GetCellCount(shape) * 2, 1, 16), shape, at ?? cell);
                if (hit >= 0)
                {
                    var hm = lvl.monsters[hit];
                    ghost.count = hm.count;
                    ghost.hasKey = hm.hasKey;
                    ghost.lockKeyCount = hm.lockKeyCount;
                    ghost.iceCount = hm.iceCount;
                    ghost.isFilledBox = hm.isFilledBox;
                    ghost.filledBallColor = hm.filledBallColor;
                }
                if (m_BrushMechanic == MechanicBrush.Key) ghost.hasKey = hit < 0 || !ghost.hasKey;
                else if (m_BrushMechanic == MechanicBrush.Lock) ghost.lockKeyCount = (hit >= 0 && ghost.lockKeyCount == m_BrushLockCount) ? 0 : Mathf.Max(1, m_BrushLockCount);
                else if (m_BrushMechanic == MechanicBrush.Ice) ghost.iceCount = (hit >= 0 && ghost.iceCount == m_BrushIceCount) ? 0 : Mathf.Max(1, m_BrushIceCount);
                else if (m_BrushMechanic == MechanicBrush.Filled)
                {
                    bool makeFilled = hit < 0 || !ghost.isFilledBox || (m_BrushFilledColor.HasValue && ghost.filledBallColor != m_BrushFilledColor.Value);
                    ghost.isFilledBox = makeFilled;
                    if (makeFilled)
                    {
                        if (hit < 0 && !m_BrushFilledColor.HasValue)
                        {
                            ghost.filledBallColor = ghost.color;
                        }
                        else
                        {
                            ResolveFilledTarget(
                                lvl, hit, m_BrushFilledColor ?? ghost.color, ghost.count,
                                allowFallbackColor: !m_BrushFilledColor.HasValue,
                                out var gCol, out var gCnt);
                            ghost.filledBallColor = gCol;
                            ghost.count = gCnt;
                        }
                    }
                }
                DrawBlock(m_Tray, ghost, -1, at ?? cell - CarrierBlockShapeUtility.GetCells(shape)[0], rows, false, false, true, !at.HasValue);
            }
        }

        private void DrawTrayWallCell(Vector2Int c, int rows, HashSet<Vector2Int> walls, bool ghost)
        {
            const float gap = 2f, border = 2.5f, radius = 8f, innerPad = 5f, innerRadius = 4f;
            bool left = !walls.Contains(c + Vector2Int.left);
            bool right = !walls.Contains(c + Vector2Int.right);
            bool up = !walls.Contains(c + Vector2Int.up);
            bool down = !walls.Contains(c + Vector2Int.down);

            var el = new VisualElement();
            el.AddToClassList("tray-wall");
            if (ghost) el.AddToClassList("tray-wall--ghost");
            el.pickingMode = PickingMode.Ignore;

            float cpx = CellPx;
            el.style.left = c.x * cpx + (left ? gap : 0f);
            el.style.top = (rows - 1 - c.y) * cpx + (up ? gap : 0f);
            el.style.width = cpx - (left ? gap : 0f) - (right ? gap : 0f);
            el.style.height = cpx - (up ? gap : 0f) - (down ? gap : 0f);

            el.style.borderLeftWidth = left ? border : 0f;
            el.style.borderRightWidth = right ? border : 0f;
            el.style.borderTopWidth = up ? border : 0f;
            el.style.borderBottomWidth = down ? border : 0f;
            el.style.borderTopLeftRadius = (up && left) ? radius : 0f;
            el.style.borderTopRightRadius = (up && right) ? radius : 0f;
            el.style.borderBottomLeftRadius = (down && left) ? radius : 0f;
            el.style.borderBottomRightRadius = (down && right) ? radius : 0f;

            var inner = new VisualElement();
            inner.AddToClassList("tray-wall__inner");
            inner.pickingMode = PickingMode.Ignore;
            inner.style.left = left ? innerPad : 0f;
            inner.style.right = right ? innerPad : 0f;
            inner.style.top = up ? innerPad : 0f;
            inner.style.bottom = down ? innerPad : 0f;
            inner.style.borderTopLeftRadius = (up && left) ? innerRadius : 0f;
            inner.style.borderTopRightRadius = (up && right) ? innerRadius : 0f;
            inner.style.borderBottomLeftRadius = (down && left) ? innerRadius : 0f;
            inner.style.borderBottomRightRadius = (down && right) ? innerRadius : 0f;
            el.Add(inner);

            m_Tray.Add(el);
        }

        private void ToggleManualBallLayout()
        {
            if (m_Level == null || m_Pending != null) return;
            if (!m_Level.useManualBallLayout)
            {
                var currentLayout = GlassShapeData.BuildLayout(m_Level);
                Record("Elle top yerleşimi");
                if (m_Level.manualBalls == null) m_Level.manualBalls = new List<ManualPoolBallEntry>();
                if (m_Level.manualBalls.Count == 0 && currentLayout.balls != null && currentLayout.balls.Count > 0)
                {
                    CopyLayoutToManualBalls(m_Level, currentLayout);
                }
                else if (m_Level.manualLaneWidth <= 0)
                {
                    m_Level.manualLaneWidth = Mathf.Clamp(currentLayout.laneWidth > 0 ? currentLayout.laneWidth : 4, 3, 8);
                }
                m_Level.useManualBallLayout = true;
                m_SelectedGroup = -1;
            }
            else
            {
                Record("Otomatik top yerleşimi");
                m_Level.useManualBallLayout = false;
                m_SelectedBallSlot = null;
                m_DragBallSlot = null;
                m_HoverBallSlot = null;
                m_BallPainting = false;
            }
            OnEdited(syncScene: true);
        }

        private static void CopyLayoutToManualBalls(PuzzleGlassLevelData lvl, OrderedPoolLayout.Result layout)
        {
            if (lvl.manualBalls == null) lvl.manualBalls = new List<ManualPoolBallEntry>();
            lvl.manualBalls.Clear();
            lvl.manualLaneWidth = Mathf.Clamp(layout.laneWidth > 0 ? layout.laneWidth : 4, 3, 8);
            if (layout.balls == null) return;
            if (lvl.useCustomGlassShape && lvl.glassPreset == GlassShapePreset.TrackChannel)
            {
                // Kanal: pist boyunu bu top sayısına sabitle (top ekleyip silmek pisti yeniden boyutlandırmasın).
                // Elle dizim pisti otomatik pistten biraz farklı ölçekte olabilir; her top otomatik dizilimdeki konumunun
                // (pist sınırlarına göre 0..1) karşılığı olan en yakın slota oturur. Sıraya göre eşleme halkada sol ve sağ
                // kolu karıştırıyordu (çıkışa iki yönden eşit uzaklık), konuma göre eşleme renk bloklarını yerinde tutar.
                bool prevManual = lvl.useManualBallLayout;
                lvl.useManualBallLayout = false;
                GlassShapeData.BuildTrack(lvl, out var autoTrack, out _);
                lvl.manualTrackCapacity = layout.balls.Count;
                lvl.useManualBallLayout = true;
                GlassShapeData.BuildTrack(lvl, out var manualTrack, out _);
                lvl.useManualBallLayout = prevManual;

                // Halka: toplar halka boyunca tek yönde sıralanıp aynı yönde sıralı slotlara dizilir (zincir kopmaz,
                // sıkışık bölgedeki toplar uzak boş köşelere savrulmaz)
                if (autoTrack != null && manualTrack != null && autoTrack.closedLoopBranch >= 0 && manualTrack.closedLoopBranch >= 0
                    && manualTrack.manualSlots != null && manualTrack.manualSlots.Count >= layout.balls.Count)
                {
                    var balls = new List<(float u, OrderedPoolLayout.PoolBall b)>();
                    foreach (var b in layout.balls) balls.Add((GlassTrackLayout.RingParam(autoTrack, b.localPos), b));
                    var mslots = new List<(float u, OrderedPoolLayout.Slot s)>();
                    foreach (var s in manualTrack.manualSlots) mslots.Add((GlassTrackLayout.RingParam(manualTrack, s.localPos), s));
                    if (balls.Count > 0 && balls.TrueForAll(x => x.u >= 0f) && mslots.TrueForAll(x => x.u >= 0f))
                    {
                        // Sıralamayı topların arasındaki en büyük boşluktan (dizilimin boş kalan kısmı) başlat
                        balls.Sort((x, y) => x.u.CompareTo(y.u));
                        float startU = balls[0].u, maxGap = -1f;
                        for (int i = 0; i < balls.Count; i++)
                        {
                            float next = i + 1 < balls.Count ? balls[i + 1].u : balls[0].u + 1f;
                            if (next - balls[i].u > maxGap) { maxGap = next - balls[i].u; startU = next % 1f; }
                        }
                        float Shift(float u) => (u - startU + 2f) % 1f;
                        balls.Sort((x, y) => Shift(x.u).CompareTo(Shift(y.u)));
                        mslots.Sort((x, y) => Shift(x.u).CompareTo(Shift(y.u)));

                        // Sıra korunarak her topa hedef konumuna en yakın slot (kalan toplara yetecek slot bırakılır)
                        int B = balls.Count, S = mslots.Count, ptr = 0;
                        for (int i = 0; i < B; i++)
                        {
                            float target = Shift(balls[i].u);
                            int limit = S - (B - i);
                            int j = ptr;
                            while (j < limit && Shift(mslots[j + 1].u) <= target) j++;
                            if (j + 1 <= limit && Mathf.Abs(Shift(mslots[j + 1].u) - target) < Mathf.Abs(Shift(mslots[j].u) - target)) j++;
                            var s = mslots[j].s;
                            var entry = new ManualPoolBallEntry(balls[i].b.color, s.column, s.row)
                            {
                                hasTrackPos = true,
                                trackPos = GlassTrackLayout.NormalizeTrackPos(manualTrack, s.localPos)
                            };
                            lvl.manualBalls.Add(entry);
                            ptr = j + 1;
                        }
                        return;
                    }
                }

                foreach (var b in layout.balls)
                {
                    var entry = new ManualPoolBallEntry(b.color, -1, -1);
                    if (autoTrack != null)
                    {
                        entry.hasTrackPos = true;
                        entry.trackPos = GlassTrackLayout.NormalizeTrackPos(autoTrack, b.localPos);
                    }
                    lvl.manualBalls.Add(entry);
                }
                // Adresler (sütun/sıra) konumlardan hesaplanır
                lvl.useManualBallLayout = true;
                GlassShapeData.CanonicalizeManualTrackBalls(lvl);
                lvl.useManualBallLayout = prevManual;
                return;
            }
            foreach (var b in layout.balls)
                lvl.manualBalls.Add(new ManualPoolBallEntry(b.color, b.ballColumn, b.row));
        }

        private void RefreshBallToolbar(PuzzleGlassLevelData lvl)
        {
            m_BallToolbar.Clear();
            m_BallToolbarChips.Clear();
            if (lvl == null) return;

            var moveChip = BallChip(() =>
            {
                m_BallBrushColor = null;
                m_BallEraser = false;
                RefreshBallToolbar(lvl);
            }, !m_BallBrushColor.HasValue && !m_BallEraser);
            moveChip.Add(new Label("✋ Taşı"));
            moveChip.tooltip = "Taşı / Değiştir: iki topa sırayla tıkla ya da topu başka slota sürükle.";
            m_BallToolbar.Add(moveChip);

            var placedByColor = new Dictionary<PuzzleColor, int>();
            if (lvl.manualBalls != null)
            {
                foreach (var b in lvl.manualBalls)
                    if (b != null) placedByColor[b.color] = (placedByColor.TryGetValue(b.color, out int v) ? v : 0) + 1;
            }
            var socketsByColor = new Dictionary<PuzzleColor, int>();
            var supplyByColor = new Dictionary<PuzzleColor, int>();
            if (lvl.monsters != null)
            {
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    int cnt = Mathf.Max(1, m.count);
                    socketsByColor[m.color] = (socketsByColor.TryGetValue(m.color, out int v) ? v : 0) + cnt;
                    if (m.isFilledBox)
                        supplyByColor[m.filledBallColor] = (supplyByColor.TryGetValue(m.filledBallColor, out int sv) ? sv : 0) + cnt;
                }
            }
            var neededByColor = new Dictionary<PuzzleColor, int>();
            foreach (var kv in socketsByColor)
            {
                supplyByColor.TryGetValue(kv.Key, out int sup);
                int net = Mathf.Max(0, kv.Value - sup);
                if (net > 0) neededByColor[kv.Key] = net;
            }

            // Önce leveldeki blokların/camın renkleri, sonra diğer palet renkleri
            var orderedColors = new List<PuzzleColor>();
            foreach (var c in BrushColors)
                if (neededByColor.ContainsKey(c) || placedByColor.ContainsKey(c) || supplyByColor.ContainsKey(c) || socketsByColor.ContainsKey(c))
                    orderedColors.Add(c);
            foreach (var c in BrushColors) if (!orderedColors.Contains(c)) orderedColors.Add(c);

            foreach (var c in orderedColors)
            {
                var color = c;
                neededByColor.TryGetValue(color, out int need);
                placedByColor.TryGetValue(color, out int have);
                supplyByColor.TryGetValue(color, out int sup);
                socketsByColor.TryGetValue(color, out int sc);
                bool overSupplied = sup > sc;
                bool active = m_BallBrushColor == color && !m_BallEraser;
                var chip = BallChip(() =>
                {
                    m_BallEraser = false;
                    m_BallBrushColor = m_BallBrushColor == color ? (PuzzleColor?)null : color;
                    m_SelectedBallSlot = null;
                    RefreshBallToolbar(lvl);
                    RedrawGlass();
                }, active);
                if (need != have || overSupplied) chip.AddToClassList("ball-chip--warn");

                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.backgroundColor = PuzzleColorPalette.GetBodyColor(color);
                if (need == 0 && have == 0 && sup == 0) sw.style.marginRight = 0;
                chip.Add(sw);
                if (need > 0 || have > 0 || sup > 0)
                {
                    var countLbl = new Label($"{have}/{need}");
                    chip.Add(countLbl);
                    m_BallToolbarChips[color] = (chip, countLbl);
                }
                else
                {
                    m_BallToolbarChips[color] = (chip, null);
                }
                chip.tooltip = overSupplied
                    ? $"{PuzzleLevelSolver.ColorName(color)}: dolu kutularda {sup} top var ama toplam toplayıcı yuva {sc}!"
                    : (need > 0 || have > 0 || sup > 0)
                        ? $"{PuzzleLevelSolver.ColorName(color)}: camda {have}/{need} top (kutu yuvası {sc}, dolu kutu {sup}). Tıkla = top fırçası."
                        : $"{PuzzleLevelSolver.ColorName(color)} top fırçası";
                m_BallToolbar.Add(chip);
            }

            var eraser = BallChip(() =>
            {
                m_BallEraser = !m_BallEraser;
                m_SelectedBallSlot = null;
                RefreshBallToolbar(lvl);
                RedrawGlass();
            }, m_BallEraser);
            eraser.Add(new Label("Silgi"));
            eraser.tooltip = "Tıklanan/sürüklenen slottaki topu siler (sağ tık da her zaman siler).";
            m_BallToolbar.Add(eraser);

            var sep = new VisualElement();
            sep.AddToClassList("ball-bar__sep");
            m_BallToolbar.Add(sep);

            int lw = Mathf.Clamp(lvl.manualLaneWidth > 0 ? lvl.manualLaneWidth : 4, 3, 8);
            var colMinus = BallChip(() => SetManualLaneWidth(lw - 1), false);
            colMinus.Add(new Label("−"));
            colMinus.tooltip = "Sütun sayısını azalt (toplar büyür).";
            colMinus.SetEnabled(lw > 3);
            m_BallToolbar.Add(colMinus);

            var colLabel = new Label($"{lw * 3} sütun");
            colLabel.AddToClassList("ball-bar__count");
            colLabel.tooltip = "Camdaki petek sütun yoğunluğu";
            m_BallToolbar.Add(colLabel);

            var colPlus = BallChip(() => SetManualLaneWidth(lw + 1), false);
            colPlus.Add(new Label("+"));
            colPlus.tooltip = "Sütun sayısını artır (toplar küçülür).";
            colPlus.SetEnabled(lw < 8);
            m_BallToolbar.Add(colPlus);

            var dropBtn = BallChip(ApplyManualBallGravity, false);
            dropBtn.Add(new Label("↓ Oturt"));
            dropBtn.tooltip = "Altta boşluk kalan topları kendi sütununda en alta düşürür.";
            m_BallToolbar.Add(dropBtn);

            var resetBtn = BallChip(ResetManualBallsFromAuto, false);
            resetBtn.Add(new Label("↺ Sıradan al"));
            resetBtn.tooltip = "Camı şu anki otomatik grup sırasından yeniden doldurur.";
            m_BallToolbar.Add(resetBtn);

            var clearBtn = BallChip(ClearManualBalls, false);
            clearBtn.Add(new Label("✕ Boşalt"));
            clearBtn.tooltip = "Camdaki tüm topları temizler (sıfırdan kendin koy).";
            m_BallToolbar.Add(clearBtn);

            var autoBoxBtn = BallChip(() =>
            {
                m_AutoShapeBoxesFromBalls = !m_AutoShapeBoxesFromBalls;
                if (m_AutoShapeBoxesFromBalls && m_Level != null)
                {
                    Record("Kutuları toplara uydur");
                    SyncBoxesFromManualBalls(m_Level);
                    OnEdited(syncScene: true);
                }
                else
                {
                    RefreshBallToolbar(lvl);
                }
            }, m_AutoShapeBoxesFromBalls);
            autoBoxBtn.Add(new Label("🔄 Kutuları uydur"));
            autoBoxBtn.tooltip = "Açıkken koyduğun veya sildiğin toplara göre tepsideki kutuların sayısı/rengi otomatik ayarlanır.";
            m_BallToolbar.Add(autoBoxBtn);
        }

        private static Button BallChip(System.Action onClick, bool active)
        {
            var chip = new Button(onClick);
            chip.AddToClassList("ball-chip");
            chip.EnableInClassList("ball-chip--active", active);
            return chip;
        }

        private void SetManualLaneWidth(int newLw)
        {
            if (m_Level == null || m_Pending != null) return;
            newLw = Mathf.Clamp(newLw, 3, 8);
            if (m_Level.manualLaneWidth == newLw) return;
            Record("Cam sütun yoğunluğu");
            m_Level.manualLaneWidth = newLw;
            m_SelectedBallSlot = null;
            OnEdited(syncScene: true);
        }

        private void ApplyManualBallGravity()
        {
            if (m_Level == null || m_Pending != null || m_Level.manualBalls == null) return;
            var slots = GlassShapeData.GetManualSlots(m_Level, out _, out _, out _);
            Record("Topları alta oturt");
            OrderedPoolLayout.ApplyGravityToManualBalls(m_Level.manualBalls, slots);
            m_SelectedBallSlot = null;
            OnEdited(syncScene: true);
        }

        private void ResetManualBallsFromAuto()
        {
            if (m_Level == null || m_Pending != null) return;
            bool prev = m_Level.useManualBallLayout;
            m_Level.useManualBallLayout = false;
            var autoLayout = GlassShapeData.BuildLayout(m_Level);
            m_Level.useManualBallLayout = prev;
            Record("Camı otomatik sıradan doldur");
            CopyLayoutToManualBalls(m_Level, autoLayout);
            m_SelectedBallSlot = null;
            OnEdited(syncScene: true);
        }

        private void ClearManualBalls()
        {
            if (m_Level == null || m_Pending != null) return;
            Record("Camdaki topları temizle");
            if (m_Level.manualBalls == null) m_Level.manualBalls = new List<ManualPoolBallEntry>();
            m_Level.manualBalls.Clear();
            m_SelectedBallSlot = null;
            OnEdited(syncScene: true);
        }

        private void UpdateBallToolbarCounts(PuzzleGlassLevelData lvl)
        {
            if (lvl == null || m_BallToolbarChips.Count == 0) return;

            var placedByColor = new Dictionary<PuzzleColor, int>();
            if (lvl.manualBalls != null)
            {
                foreach (var b in lvl.manualBalls)
                    if (b != null) placedByColor[b.color] = (placedByColor.TryGetValue(b.color, out int v) ? v : 0) + 1;
            }
            var socketsByColor = new Dictionary<PuzzleColor, int>();
            var supplyByColor = new Dictionary<PuzzleColor, int>();
            if (lvl.monsters != null)
            {
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    int cnt = Mathf.Max(1, m.count);
                    socketsByColor[m.color] = (socketsByColor.TryGetValue(m.color, out int v) ? v : 0) + cnt;
                    if (m.isFilledBox)
                        supplyByColor[m.filledBallColor] = (supplyByColor.TryGetValue(m.filledBallColor, out int sv) ? sv : 0) + cnt;
                }
            }

            foreach (var kv in m_BallToolbarChips)
            {
                PuzzleColor color = kv.Key;
                var (chip, lbl) = kv.Value;
                if (lbl == null)
                {
                    // Yeni bir renk ilk kez boyandıysa etiket eklemek için tek seferlik yenile
                    if (placedByColor.ContainsKey(color)) { RefreshBallToolbar(lvl); return; }
                    continue;
                }
                socketsByColor.TryGetValue(color, out int sc);
                supplyByColor.TryGetValue(color, out int sup);
                placedByColor.TryGetValue(color, out int have);
                int need = Mathf.Max(0, sc - sup);
                bool overSupplied = sup > sc;
                lbl.text = $"{have}/{need}";
                chip.EnableInClassList("ball-chip--warn", need != have || overSupplied);
            }
        }

        private void RedrawGlass()
        {
            m_Glass.Clear();
            m_BallSlotElements.Clear();
            m_BallSlotByKey.Clear();
            m_BallSlotHitCache.Clear();
            m_GlassChamberSet.Clear();
            var lvl = Shown;
            if (lvl == null) return;

            var dims = GetGlassDims(lvl);
            int cols = lvl.stagingGridCols;
            bool manual = lvl.useManualBallLayout && m_Pending == null;
            float width = manual ? Mathf.Max(cols * CellPx, 460f) : (cols * CellPx);

            var layout = GlassShapeData.BuildLayout(lvl);
            var split = GlassShapeData.SplitForFeedTubes(lvl, layout);
            bool hasFeedSplit = lvl.useBallFeedTubes && layout.balls.Count > 1 &&
                                (split.leftTubeColors.Count > 0 || split.rightTubeColors.Count > 0);
            var chamberBalls = hasFeedSplit ? split.chamberBalls : layout.balls;

            List<OrderedPoolLayout.Slot> slots = null;
            float drawDiam = layout.diameter;
            if (manual)
            {
                slots = GlassShapeData.GetManualSlots(lvl, out drawDiam, out _, out _);
            }

            float viewHalfW = dims.innerHalfW;
            float slotsTop = dims.floorY + 0.6f;
            if (manual && slots != null && slots.Count > 0)
            {
                float maxAbsX = 0f;
                foreach (var s in slots)
                {
                    maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(s.localPos.x));
                    slotsTop = Mathf.Max(slotsTop, s.localPos.y + drawDiam * 0.85f);
                }
                if (!GlassShapeData.IsShapedPool(lvl) && !GlassShapeData.IsBox(lvl))
                {
                    viewHalfW = Mathf.Max(maxAbsX + drawDiam * 0.90f, drawDiam * 2.5f);
                }
            }

            float scale = width / Mathf.Max(0.01f, viewHalfW * 2f);
            float ballsTop = dims.floorY + 0.6f;
            foreach (var bb in (manual ? layout.balls : chamberBalls)) ballsTop = Mathf.Max(ballsTop, bb.localPos.y + drawDiam);
            float extraTop = hasFeedSplit ? 0.38f : 0.35f;
            float shownTop = manual
                ? Mathf.Max(ballsTop + drawDiam * 1.6f, slotsTop)
                : Mathf.Min(dims.ceilY, ballsTop + extraTop);
            float height = Mathf.Max(manual ? 260f : 80f, (shownTop - dims.floorY) * scale);

            m_Glass.style.width = width + 6f;   // + kenarlık (UI Toolkit genişliği kenarlığı içerir)
            m_Glass.style.height = height + 6f;

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

            float colPx = width / Mathf.Max(1, cols);
            for (int c = 0; c < cols; c++)
            {
                if (focusCols != null && focusCols.Contains(c))
                {
                    var col = new VisualElement();
                    col.AddToClassList("glass__column");
                    col.pickingMode = PickingMode.Ignore;
                    col.style.left = c * colPx;
                    col.style.width = colPx;
                    m_Glass.Add(col);
                }
                if (c > 0)
                {
                    var guide = new VisualElement();
                    guide.AddToClassList("glass__guide");
                    guide.pickingMode = PickingMode.Ignore;
                    guide.style.left = c * colPx;
                    m_Glass.Add(guide);
                }
            }

            m_GlassFocusColor = focusColor;
            m_GlassHasFeedSplit = hasFeedSplit;
            float d = drawDiam * scale;
            float hitRadius = Mathf.Max(12f, d * 0.55f);
            if (manual && slots != null)
            {
                if (hasFeedSplit)
                {
                    foreach (var cb in chamberBalls) m_GlassChamberSet.Add((cb.ballColumn, cb.row));
                }

                var occupied = new Dictionary<(int col, int row), PuzzleColor>();
                if (lvl.manualBalls != null)
                {
                    foreach (var mb in lvl.manualBalls)
                        if (mb != null && !occupied.ContainsKey((mb.column, mb.row)))
                            occupied[(mb.column, mb.row)] = mb.color;
                }

                var drawnKeys = new HashSet<(int col, int row)>();
                foreach (var s in slots)
                {
                    float cx = (s.localPos.x + viewHalfW) * scale;
                    float cy = height - (s.localPos.y - dims.floorY) * scale;
                    if (cy < -d * 0.25f || cy > height + d * 0.25f) continue;

                    var key = (s.column, s.row);
                    drawnKeys.Add(key);
                    bool hasBall = occupied.TryGetValue(key, out PuzzleColor ballColor);
                    bool hovered = m_HoverBallSlot.HasValue && m_HoverBallSlot.Value == key;
                    bool selected = m_SelectedBallSlot.HasValue && m_SelectedBallSlot.Value == key;
                    bool draggingSrc = m_DragBallSlot.HasValue && m_DragBallSlot.Value == key && m_BallDragMoved;

                    var el = new VisualElement();
                    el.pickingMode = PickingMode.Ignore;
                    el.style.left = cx - d * 0.5f;
                    el.style.top = cy - d * 0.5f;
                    el.style.width = d;
                    el.style.height = d;
                    if (hasBall)
                    {
                        el.AddToClassList("ball");
                        el.style.backgroundColor = PuzzleColorPalette.GetBodyColor(ballColor);
                        if (hasFeedSplit && !m_GlassChamberSet.Contains(key)) el.AddToClassList("ball--tube");
                        if (focusColor.HasValue && ballColor != focusColor.Value && !m_BallBrushColor.HasValue && !m_BallEraser)
                            el.AddToClassList("ball--dim");
                        if (selected) el.AddToClassList("ball--selected");
                        if (hovered && m_DragBallSlot.HasValue && m_BallDragMoved) el.AddToClassList("ball--hover");
                        if (draggingSrc) el.style.opacity = 0.45f;
                    }
                    else
                    {
                        el.AddToClassList("ball-slot");
                        if (hovered) el.AddToClassList("ball-slot--hover");
                    }
                    m_Glass.Add(el);
                    m_BallSlotElements.Add((el, s.column, s.row));
                    m_BallSlotByKey[key] = el;
                    m_BallSlotHitCache.Add((new Vector2(cx, cy), hitRadius, s.column, s.row));
                }

                // Taşma varsa (kontur küçüldüyse) görünmeyen topları da çiz
                foreach (var b in layout.balls)
                {
                    if (drawnKeys.Contains((b.ballColumn, b.row))) continue;
                    var ball = new VisualElement();
                    ball.AddToClassList("ball");
                    if (hasFeedSplit && !m_GlassChamberSet.Contains((b.ballColumn, b.row))) ball.AddToClassList("ball--tube");
                    ball.pickingMode = PickingMode.Ignore;
                    float cx = (b.localPos.x + viewHalfW) * scale;
                    float cy = height - (b.localPos.y - dims.floorY) * scale;
                    ball.style.left = cx - d * 0.5f;
                    ball.style.top = cy - d * 0.5f;
                    ball.style.width = d;
                    ball.style.height = d;
                    ball.style.backgroundColor = PuzzleColorPalette.GetBodyColor(b.color);
                    m_Glass.Add(ball);
                    var key = (b.ballColumn, b.row);
                    m_BallSlotElements.Add((ball, b.ballColumn, b.row));
                    m_BallSlotByKey[key] = ball;
                    m_BallSlotHitCache.Add((new Vector2(cx, cy), hitRadius, b.ballColumn, b.row));
                }
            }
            else
            {
                foreach (var b in chamberBalls)
                {
                    var ball = new VisualElement();
                    ball.AddToClassList("ball");
                    ball.pickingMode = PickingMode.Ignore;
                    float cx = (b.localPos.x + viewHalfW) * scale;
                    float cy = height - (b.localPos.y - dims.floorY) * scale;
                    ball.style.left = cx - d * 0.5f;
                    ball.style.top = cy - d * 0.5f;
                    ball.style.width = d;
                    ball.style.height = d;
                    ball.style.backgroundColor = PuzzleColorPalette.GetBodyColor(b.color);
                    if (focusColor.HasValue && b.color != focusColor.Value) ball.AddToClassList("ball--dim");
                    m_Glass.Add(ball);
                }
                if (chamberBalls.Count == 0)
                {
                    var empty = new Label("Camda top yok");
                    empty.pickingMode = PickingMode.Ignore;
                    empty.style.position = Position.Absolute;
                    empty.style.left = 0;
                    empty.style.right = 0;
                    empty.style.top = height * 0.4f;
                    empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                    empty.style.color = new Color(1f, 1f, 1f, 0.5f);
                    m_Glass.Add(empty);
                }
            }

            if (lvl.useBallFeedTubes)
            {
                float bendH = Mathf.Clamp(dims.innerHalfW * 0.72f, 0.62f, 1.15f);
                float tubeH = Mathf.Clamp((bendH + 0.42f) * scale, 96f, 150f);
                m_TubeCanvas.style.display = DisplayStyle.Flex;
                m_TubeCanvas.style.width = width + 6f;
                m_TubeCanvas.style.height = tubeH;
                m_TubeHeight = tubeH;
                m_TubeScale = scale;
                m_TubeHalfW = viewHalfW;
                m_TubeBallDiam = drawDiam;
                m_TubeLeftColors = split.leftTubeColors;
                m_TubeRightColors = split.rightTubeColors;
                m_TubeCanvas.MarkDirtyRepaint();

                int eff = split.effectiveContainerLimit;
                int queued = Mathf.Max(0, layout.balls.Count - eff);
                string dirLabel = lvl.feedTubeMode == BallFeedTubeMode.LeftOnly ? "Sol Tüp"
                    : lvl.feedTubeMode == BallFeedTubeMode.RightOnly ? "Sağ Tüp"
                    : "Çift Tüp";
                var badge = new Label($"🧪 {dirLabel}: {eff} hazne + {queued} tüpte");
                badge.AddToClassList("tube-pill");
                badge.AddToClassList("tube-pill__label");
                badge.pickingMode = PickingMode.Ignore;
                badge.style.position = Position.Absolute;
                badge.style.top = 4;
                if (lvl.feedTubeMode == BallFeedTubeMode.LeftOnly) badge.style.right = 6;
                else badge.style.left = 6;
                m_Glass.Add(badge);
            }
            else
            {
                m_TubeCanvas.style.display = DisplayStyle.None;
            }
        }

        private void DrawStudioFeedTubes(MeshGenerationContext mgc)
        {
            var lvl = Shown;
            if (lvl == null || !lvl.useBallFeedTubes || m_TubeScale <= 0.01f) return;

            var p = mgc.painter2D;
            float ballR = Mathf.Clamp(m_TubeBallDiam * 0.5f, 0.07f, 0.17f);
            float aboveWorld = Mathf.Max(0.55f, (m_TubeHeight - 8f) / m_TubeScale);
            bool useLeft = lvl.feedTubeMode == BallFeedTubeMode.BothLeftAndRight || lvl.feedTubeMode == BallFeedTubeMode.LeftOnly;
            bool useRight = lvl.feedTubeMode == BallFeedTubeMode.BothLeftAndRight || lvl.feedTubeMode == BallFeedTubeMode.RightOnly;

            Vector2 ToTubePx(float x, float y) => new Vector2(3f + (x + m_TubeHalfW) * m_TubeScale, m_TubeHeight - y * m_TubeScale);

            void DrawOneTube(bool isLeft, List<PuzzleColor> colors)
            {
                var curve = BallFeedTubeUtility.BuildTubeCurve(
                    isLeft, 0f, m_TubeHalfW, 0f, ballR,
                    lvl.feedTubeWidthMultiplier, lvl.feedTubeAnchorSpread, aboveWorld);
                if (!curve.IsValid) return;

                p.BeginPath();
                p.MoveTo(ToTubePx(curve.leftEdgePath[0].x, curve.leftEdgePath[0].y));
                for (int i = 1; i < curve.leftEdgePath.Length; i++)
                    p.LineTo(ToTubePx(curve.leftEdgePath[i].x, curve.leftEdgePath[i].y));
                for (int i = curve.rightEdgePath.Length - 1; i >= 0; i--)
                    p.LineTo(ToTubePx(curve.rightEdgePath[i].x, curve.rightEdgePath[i].y));
                p.ClosePath();
                p.fillColor = new Color(0.875f, 0.894f, 0.984f, 0.92f);
                p.Fill();

                for (int side = 0; side < 2; side++)
                {
                    var path = side == 0 ? curve.leftEdgePath : curve.rightEdgePath;
                    p.BeginPath();
                    p.MoveTo(ToTubePx(path[0].x, path[0].y));
                    for (int i = 1; i < path.Length; i++)
                        p.LineTo(ToTubePx(path[i].x, path[i].y));
                    p.strokeColor = new Color(0.620f, 0.588f, 0.933f, 1f);
                    p.lineWidth = 3.2f;
                    p.Stroke();
                }

                // Kapak çizgisi
                float radAngle = curve.gateAngleDeg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(radAngle), Mathf.Sin(radAngle)) * (curve.gateWidth * 0.5f);
                Vector2 aPx = ToTubePx(curve.gatePoint.x - dir.x, curve.gatePoint.y - dir.y);
                Vector2 bPx = ToTubePx(curve.gatePoint.x + dir.x, curve.gatePoint.y + dir.y);
                p.BeginPath();
                p.MoveTo(aPx);
                p.LineTo(bPx);
                p.strokeColor = new Color(0.36f, 0.34f, 0.82f, 0.96f);
                p.lineWidth = 4f;
                p.Stroke();

                if (colors == null || colors.Count == 0) return;
                int maxVis = Mathf.Clamp(lvl.feedTubeQueueVisibleCount, 2, 14);
                int vis = Mathf.Min(maxVis, colors.Count);
                float radPx = ballR * m_TubeScale * 0.94f;
                float spacing = ballR * 2.15f;
                float firstDist = curve.restDist;
                for (int i = 0; i < vis; i++)
                {
                    float dist = Mathf.Min(curve.totalLength - ballR, firstDist + i * spacing);
                    curve.EvaluateAtDistance(dist, out Vector2 pt, out _);
                    Vector2 c = ToTubePx(pt.x, pt.y);
                    Color bc = PuzzleColorPalette.GetBodyColor(colors[i]);
                    p.fillColor = bc;
                    p.strokeColor = Color.Lerp(bc, Color.black, 0.40f);
                    p.lineWidth = 1.5f;
                    p.BeginPath();
                    p.Arc(c, radPx, 0f, 360f);
                    p.Fill();
                    p.Stroke();
                }
            }

            if (useLeft) DrawOneTube(true, m_TubeLeftColors);
            if (useRight) DrawOneTube(false, m_TubeRightColors);
        }

        private (int col, int row)? BallSlotAtPanel(Vector2 panelPos)
        {
            Vector2 localPos = m_Glass.WorldToLocal(panelPos) - m_Glass.contentRect.position;
            float best = float.MaxValue;
            (int col, int row)? hit = null;
            for (int i = 0; i < m_BallSlotHitCache.Count; i++)
            {
                var entry = m_BallSlotHitCache[i];
                float d2 = (entry.localCenter - localPos).sqrMagnitude;
                if (d2 <= entry.radius * entry.radius && d2 < best)
                {
                    best = d2;
                    hit = (entry.col, entry.row);
                }
            }
            return hit;
        }

        private void ApplyBallHoverClass((int col, int row) key, bool hovered)
        {
            if (!m_BallSlotByKey.TryGetValue(key, out var el)) return;
            if (el.ClassListContains("ball"))
            {
                el.EnableInClassList("ball--hover", hovered && m_DragBallSlot.HasValue && m_BallDragMoved);
                el.RemoveFromClassList("ball-slot--hover");
            }
            else
            {
                el.EnableInClassList("ball-slot--hover", hovered);
                el.RemoveFromClassList("ball--hover");
            }
        }

        private void SetHoverBallSlot((int col, int row)? nextSlot)
        {
            if (nextSlot == m_HoverBallSlot) return;
            var prev = m_HoverBallSlot;
            m_HoverBallSlot = nextSlot;
            if (prev.HasValue) ApplyBallHoverClass(prev.Value, false);
            if (nextSlot.HasValue) ApplyBallHoverClass(nextSlot.Value, true);
        }

        private void SetSelectedBallSlot((int col, int row)? nextSlot)
        {
            if (nextSlot == m_SelectedBallSlot) return;
            var prev = m_SelectedBallSlot;
            m_SelectedBallSlot = nextSlot;
            if (prev.HasValue && m_BallSlotByKey.TryGetValue(prev.Value, out var prevEl))
                prevEl.RemoveFromClassList("ball--selected");
            if (nextSlot.HasValue && m_BallSlotByKey.TryGetValue(nextSlot.Value, out var nextEl) && nextEl.ClassListContains("ball"))
                nextEl.AddToClassList("ball--selected");
        }

        private bool UpdateManualBallSlotVisual((int col, int row) key)
        {
            if (!m_BallSlotByKey.TryGetValue(key, out var el)) return false;
            int idx = FindManualBallIndex(key.col, key.row);
            bool hasBall = idx >= 0;
            bool hovered = m_HoverBallSlot.HasValue && m_HoverBallSlot.Value == key;
            bool selected = m_SelectedBallSlot.HasValue && m_SelectedBallSlot.Value == key;

            el.ClearClassList();
            el.style.opacity = 1f;
            if (hasBall)
            {
                PuzzleColor ballColor = m_Level.manualBalls[idx].color;
                el.AddToClassList("ball");
                el.style.backgroundColor = PuzzleColorPalette.GetBodyColor(ballColor);
                if (m_GlassHasFeedSplit && !m_GlassChamberSet.Contains(key)) el.AddToClassList("ball--tube");
                if (m_GlassFocusColor.HasValue && ballColor != m_GlassFocusColor.Value && !m_BallBrushColor.HasValue && !m_BallEraser)
                    el.AddToClassList("ball--dim");
                if (selected) el.AddToClassList("ball--selected");
                if (hovered && m_DragBallSlot.HasValue && m_BallDragMoved) el.AddToClassList("ball--hover");
            }
            else
            {
                el.AddToClassList("ball-slot");
                el.style.backgroundColor = StyleKeyword.Null;
                if (hovered) el.AddToClassList("ball-slot--hover");
            }
            return true;
        }

        private int FindManualBallIndex(int col, int row)
        {
            if (m_Level?.manualBalls == null) return -1;
            for (int i = 0; i < m_Level.manualBalls.Count; i++)
            {
                var b = m_Level.manualBalls[i];
                if (b != null && b.column == col && b.row == row) return i;
            }
            return -1;
        }

        private void EnsureBallStrokeRecorded(string label)
        {
            if (m_BallStrokeRecorded) return;
            Record(label);
            m_BallStrokeRecorded = true;
        }

        private bool ApplyPaintOrEraseAt((int col, int row) slot, bool erase)
        {
            if (m_Level == null) return false;
            if (m_Level.manualBalls == null) m_Level.manualBalls = new List<ManualPoolBallEntry>();
            int idx = FindManualBallIndex(slot.col, slot.row);
            if (erase)
            {
                if (idx < 0) return false;
                EnsureBallStrokeRecorded("Camdan top sil");
                m_Level.manualBalls.RemoveAt(idx);
                return true;
            }
            if (!m_BallBrushColor.HasValue) return false;
            var color = m_BallBrushColor.Value;
            if (idx >= 0)
            {
                if (m_Level.manualBalls[idx].color == color) return false;
                EnsureBallStrokeRecorded("Top rengini boya");
                m_Level.manualBalls[idx].color = color;
                return true;
            }
            EnsureBallStrokeRecorded("Cama top koy");
            m_Level.manualBalls.Add(new ManualPoolBallEntry(color, slot.col, slot.row));
            return true;
        }

        private void MoveOrSwapManualBall((int col, int row) from, (int col, int row) to)
        {
            if (m_Level?.manualBalls == null || from == to) return;
            int fromIdx = FindManualBallIndex(from.col, from.row);
            if (fromIdx < 0) return;
            int toIdx = FindManualBallIndex(to.col, to.row);
            Record(toIdx >= 0 ? "Topların yerini değiştir" : "Topu taşı");
            if (toIdx >= 0)
            {
                (m_Level.manualBalls[fromIdx].color, m_Level.manualBalls[toIdx].color) =
                    (m_Level.manualBalls[toIdx].color, m_Level.manualBalls[fromIdx].color);
            }
            else
            {
                m_Level.manualBalls[fromIdx].column = to.col;
                m_Level.manualBalls[fromIdx].row = to.row;
                // Eski kanal konumu bırakılmazsa top eski yerine geri oturur; yeni konum OnEdited'da kaydedilir
                m_Level.manualBalls[fromIdx].hasTrackPos = false;
            }
            OnEdited(syncScene: true);
        }

        private void OnGlassPointerDown(PointerDownEvent e)
        {
            if (m_Level == null || m_Pending != null || !m_Level.useManualBallLayout) return;
            var slot = BallSlotAtPanel(e.position);

            if (e.button == 1)
            {
                m_BallPainting = true;
                m_BallPaintButton = 1;
                m_BallStrokeRecorded = false;
                SetSelectedBallSlot(null);
                m_Glass.CapturePointer(e.pointerId);
                if (slot.HasValue && ApplyPaintOrEraseAt(slot.Value, erase: true))
                {
                    UpdateBallToolbarCounts(m_Level);
                    if (!UpdateManualBallSlotVisual(slot.Value)) RedrawGlass();
                }
                e.StopPropagation();
                return;
            }

            if (e.button != 0) return;

            if (m_BallEraser || m_BallBrushColor.HasValue)
            {
                m_BallPainting = true;
                m_BallPaintButton = 0;
                m_BallStrokeRecorded = false;
                m_Glass.CapturePointer(e.pointerId);
                if (slot.HasValue && ApplyPaintOrEraseAt(slot.Value, erase: m_BallEraser))
                {
                    UpdateBallToolbarCounts(m_Level);
                    if (!UpdateManualBallSlotVisual(slot.Value)) RedrawGlass();
                }
                e.StopPropagation();
                return;
            }

            if (!slot.HasValue)
            {
                if (m_SelectedBallSlot.HasValue) SetSelectedBallSlot(null);
                return;
            }

            int hitIdx = FindManualBallIndex(slot.Value.col, slot.Value.row);
            if (hitIdx >= 0)
            {
                m_DragBallSlot = slot.Value;
                m_BallDragMoved = false;
                SetHoverBallSlot(slot.Value);
                m_Glass.CapturePointer(e.pointerId);
            }
            else if (m_SelectedBallSlot.HasValue)
            {
                var from = m_SelectedBallSlot.Value;
                SetSelectedBallSlot(null);
                MoveOrSwapManualBall(from, slot.Value);
            }
            e.StopPropagation();
        }

        private void OnGlassPointerMove(PointerMoveEvent e)
        {
            if (m_Level == null || m_Pending != null || !m_Level.useManualBallLayout) return;
            var slot = BallSlotAtPanel(e.position);

            if (m_BallPainting)
            {
                bool changed = slot.HasValue && ApplyPaintOrEraseAt(slot.Value, erase: m_BallPaintButton == 1 || m_BallEraser);
                if (slot != m_HoverBallSlot) SetHoverBallSlot(slot);
                if (changed)
                {
                    UpdateBallToolbarCounts(m_Level);
                    if (!UpdateManualBallSlotVisual(slot.Value)) RedrawGlass();
                }
                return;
            }

            if (m_DragBallSlot.HasValue)
            {
                if (!m_BallDragMoved && slot.HasValue && slot.Value != m_DragBallSlot.Value)
                {
                    m_BallDragMoved = true;
                    if (m_BallSlotByKey.TryGetValue(m_DragBallSlot.Value, out var srcEl))
                        srcEl.style.opacity = 0.45f;
                }
                if (slot != m_HoverBallSlot) SetHoverBallSlot(slot);
                return;
            }

            if (slot != m_HoverBallSlot) SetHoverBallSlot(slot);
        }

        private void OnGlassPointerUp(PointerUpEvent e)
        {
            if (m_Glass.HasPointerCapture(e.pointerId)) m_Glass.ReleasePointer(e.pointerId);

            if (m_BallPainting)
            {
                m_BallPainting = false;
                m_BallPaintButton = -1;
                if (m_BallStrokeRecorded)
                {
                    m_BallStrokeRecorded = false;
                    SyncBoxesFromManualBalls(m_Level);
                    OnEdited(syncScene: true);
                }
                return;
            }

            if (!m_DragBallSlot.HasValue) return;
            var from = m_DragBallSlot.Value;
            bool moved = m_BallDragMoved;
            m_DragBallSlot = null;
            m_BallDragMoved = false;
            if (m_BallSlotByKey.TryGetValue(from, out var srcEl)) srcEl.style.opacity = 1f;
            var drop = BallSlotAtPanel(e.position) ?? m_HoverBallSlot;

            if (moved && drop.HasValue && drop.Value != from)
            {
                SetSelectedBallSlot(null);
                MoveOrSwapManualBall(from, drop.Value);
                return;
            }

            if (!moved)
            {
                if (!m_SelectedBallSlot.HasValue) SetSelectedBallSlot(from);
                else if (m_SelectedBallSlot.Value == from) SetSelectedBallSlot(null);
                else
                {
                    var prev = m_SelectedBallSlot.Value;
                    SetSelectedBallSlot(null);
                    MoveOrSwapManualBall(prev, from);
                    return;
                }
            }
            if (m_HoverBallSlot.HasValue) ApplyBallHoverClass(m_HoverBallSlot.Value, true);
        }

        private void PlaceCell(VisualElement el, int x, int y, int rows, float inset)
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
            bool frozen = m.iceCount > 0;
            bool locked = m.lockKeyCount > 0;
            Color cellColor = frozen ? new Color(0.337f, 0.761f, 0.965f) : body;
            var own = new HashSet<Vector2Int>(cells);

            // Blok tek parça çizilir: kendi hücreleri arasında boşluk/çizgi yok; sadece dış kenarlarda
            // boşluk, çerçeve ve yuvarlak köşe var. Böylece yan yana aynı renk bloklar ayırt edilir.
            const float gap = 3f, border = 3f, radius = 9f, plate = 6f, plateRadius = 5f;
            // Çerçeve rengi: USS'teki yarı saydam siyahın gövde üstündeki etkisi; seçili = beyaz, hatalı = kırmızı
            Color frame = selected ? Color.white
                : (critical ? new Color(0.898f, 0.325f, 0.294f)
                : (frozen ? new Color(0.76f, 0.94f, 1f)
                : (locked ? new Color(1f, 0.82f, 0.28f)
                : Color.Lerp(body, Color.black, 0.25f))));
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
                el.EnableInClassList("block-cell--ice", frozen);
                el.EnableInClassList("block-cell--locked", locked && !frozen && !selected);
                el.EnableInClassList("block-cell--selected", selected);
                el.EnableInClassList("block-cell--critical", critical && !selected);
                el.EnableInClassList("block-cell--ghost", ghost);
                el.EnableInClassList("block-cell--invalid", invalid);
                el.style.backgroundColor = cellColor;
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

                // Beyaz/buz iç plaka da birleşik: iç kenarlarda hücre sınırına kadar uzanır
                var inner = new VisualElement();
                inner.AddToClassList("block-cell__inner");
                inner.EnableInClassList("block-cell__inner--ice", frozen);
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
                        el.Add(CornerPatch(dx, dy, gap + border + plate, cellColor));
                        el.Add(CornerPatch(dx, dy, gap + border, frame));
                        el.Add(CornerPatch(dx, dy, gap, gapColor));
                    }
                }
                tray.Add(el);
            }

            // Yuva/buz sayısı ve blok numarası: şeklin ilk hücresine
            var first = cells[0];
            int firstGx = anchor.x + first.x;
            int firstGy = anchor.y + first.y;
            float firstLeft = firstGx * CellPx;
            float firstTop = (rows - 1 - firstGy) * CellPx;

            var label = new Label(frozen ? m.iceCount.ToString() : m.count.ToString());
            label.AddToClassList("block-label");
            label.EnableInClassList("block-label--ice", frozen);
            label.pickingMode = PickingMode.Ignore;
            PlaceCell(label, firstGx, firstGy, rows, frozen ? 0f : 2f);
            tray.Add(label);

            if (frozen)
            {
                var dot = new VisualElement();
                dot.AddToClassList("block-colordot");
                dot.pickingMode = PickingMode.Ignore;
                dot.style.backgroundColor = body;
                dot.style.left = firstLeft + CellPx - 19f;
                dot.style.top = firstTop + 9f;
                tray.Add(dot);
            }

            if (m.hasKey || locked || m.isFilledBox)
            {
                var badgeRow = new VisualElement();
                badgeRow.AddToClassList("block-badge-row");
                badgeRow.pickingMode = PickingMode.Ignore;
                badgeRow.style.left = firstLeft + 8f;
                badgeRow.style.top = firstTop + CellPx - 22f;
                if (m.isFilledBox)
                {
                    var fillBadge = new Label("●↑");
                    fillBadge.AddToClassList("block-badge");
                    fillBadge.style.color = PuzzleColorPalette.GetBodyColor(m.filledBallColor);
                    fillBadge.style.backgroundColor = new Color(0.10f, 0.12f, 0.22f, 0.88f);
                    fillBadge.pickingMode = PickingMode.Ignore;
                    badgeRow.Add(fillBadge);
                }
                if (m.hasKey)
                {
                    var keyBadge = new Label("🔑");
                    keyBadge.AddToClassList("block-badge");
                    keyBadge.AddToClassList("block-badge--key");
                    keyBadge.pickingMode = PickingMode.Ignore;
                    badgeRow.Add(keyBadge);
                }
                if (locked)
                {
                    var lockBadge = new Label($"🔒{m.lockKeyCount}");
                    lockBadge.AddToClassList("block-badge");
                    lockBadge.AddToClassList("block-badge--lock");
                    lockBadge.pickingMode = PickingMode.Ignore;
                    badgeRow.Add(lockBadge);
                }
                tray.Add(badgeRow);
            }

            if (index < 0) return; // hayalet: numara yok
            var number = new Label(frozen ? $"#{index + 1}·{m.count}t" : $"#{index + 1}");
            number.AddToClassList("block-number");
            number.pickingMode = PickingMode.Ignore;
            if (frozen) number.style.color = new Color(0.04f, 0.21f, 0.37f);
            number.style.left = firstLeft + 10f;
            number.style.top = firstTop + 8f;
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
                if (IsWall(lvl, p.x, p.y)) return $"tepsi bölmesiyle çakışıyor (hücre {cell.x + 1},{cell.y + 1})";
                int b = BlockAt(lvl, p, ignore);
                if (b >= 0) return $"#{b + 1} ile çakışıyor (hücre {cell.x + 1},{cell.y + 1})";
            }
            return $"hücre {cell.x + 1},{cell.y + 1}";
        }

        private void OnTrayPointerDown(PointerDownEvent e)
        {
            if (m_Level == null || m_Pending != null) return;
            var lvl = m_Level;
            var cell = CellAtPanel(e.position);
            if (cell.x < 0 || cell.y < 0 || cell.x >= lvl.stagingGridCols || cell.y >= lvl.stagingGridRows) return;
            int hit = BlockAt(lvl, cell);

            if (e.button == 1)
            {
                ShowTrayContextMenu(cell, hit);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;

            if (m_Eraser)
            {
                m_TrayPainting = true;
                m_TrayPaintedCells.Clear();
                m_TrayPaintedCells.Add(cell);
                m_Tray.CapturePointer(e.pointerId);
                if (hit >= 0) RemoveBlock(hit);
                else if (IsWall(lvl, cell.x, cell.y)) ToggleWallCell(cell, forceAdd: false);
                e.StopPropagation();
                return;
            }

            if (m_BrushMechanic == MechanicBrush.Wall)
            {
                if (hit >= 0) ShowNotification(new GUIContent($"#{hit + 1} bloğunun üstüne bölme konulamaz"));
                else
                {
                    m_TrayWallPaintAdd = !IsWall(lvl, cell.x, cell.y);
                    m_TrayPainting = true;
                    m_TrayPaintedCells.Clear();
                    m_TrayPaintedCells.Add(cell);
                    m_Tray.CapturePointer(e.pointerId);
                    ToggleWallCell(cell, forceAdd: m_TrayWallPaintAdd);
                }
                e.StopPropagation();
                return;
            }

            if (m_BrushColor != null || m_BrushShape != null || m_BrushMechanic != MechanicBrush.None)
            {
                if (hit >= 0)
                {
                    if (m_BrushShape != null && !SetBlockShape(hit, m_BrushShape.Value, cell))
                        ShowNotification(new GUIContent($"#{hit + 1} bu şekle dönüşemiyor: {WhyNotFits(lvl, m_BrushShape.Value, cell, hit)}"));
                    if (m_BrushColor != null) SetBlockColor(hit, m_BrushColor.Value);
                    if (m_BrushMechanic != MechanicBrush.None)
                    {
                        ToggleMechanicBrushOnBlock(hit, m_BrushMechanic);
                        m_TrayPainting = true;
                        m_TrayPaintedBlocks.Clear();
                        m_TrayPaintedBlocks.Add(hit);
                        m_Tray.CapturePointer(e.pointerId);
                    }
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

        private void ShowTrayContextMenu(Vector2Int cell, int hit)
        {
            var menu = new GenericMenu();
            if (hit >= 0 && ValidBlock(hit))
            {
                int idx = hit;
                var m = m_Level.monsters[idx];
                m_SelectedBlock = idx;
                RefreshAll();

                menu.AddItem(new GUIContent("●↑ İçi Dolu Kutu (Aç/Kapat)"), m.isFilledBox, () => SetBlockIsFilled(idx, !m.isFilledBox));
                foreach (var fc in BrushColors)
                {
                    var col = fc;
                    menu.AddItem(
                        new GUIContent($"İçindeki Top Rengi/{PuzzleLevelSolver.ColorName(col)}"),
                        m.isFilledBox && m.filledBallColor == col,
                        () =>
                        {
                            if (!ValidBlock(idx)) return;
                            var blk = m_Level.monsters[idx];
                            if (!blk.isFilledBox)
                            {
                                Record("İçi dolu kutu");
                                ResolveFilledTarget(m_Level, idx, col, blk.count, allowFallbackColor: false, out _, out int newCnt);
                                blk.isFilledBox = true;
                                blk.filledBallColor = col;
                                blk.count = newCnt;
                                SyncGlassAfterBoxChange(forceRebuild: true);
                                OnEdited(syncScene: true);
                            }
                            else
                            {
                                SetBlockFilledColor(idx, col);
                            }
                        });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("🔑 Anahtarlı Kutu"), m.hasKey, () => SetBlockHasKey(idx, !m.hasKey));
                menu.AddItem(new GUIContent("🔒 Kilit/Kilitsiz (0)"), m.lockKeyCount == 0, () => SetBlockLockKeys(idx, 0));
                for (int k = 1; k <= 3; k++)
                {
                    int kv = k;
                    menu.AddItem(new GUIContent($"🔒 Kilit/{kv} Anahtar"), m.lockKeyCount == kv, () => SetBlockLockKeys(idx, kv));
                }
                menu.AddItem(new GUIContent("❄ Buz/Buzsuz (0)"), m.iceCount == 0, () => SetBlockIceCount(idx, 0));
                foreach (int iv in new[] { 5, 10, 15 })
                {
                    int v = iv;
                    menu.AddItem(new GUIContent($"❄ Buz/{v} Sayaç"), m.iceCount == v, () => SetBlockIceCount(idx, v));
                }
                menu.AddSeparator("");
                foreach (var c in BrushColors)
                {
                    var col = c;
                    menu.AddItem(new GUIContent($"Kutu Rengi/{PuzzleLevelSolver.ColorName(col)}"), m.color == col, () => SetBlockColor(idx, col));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Bloğu Sil"), false, () => RemoveBlock(idx));
            }
            else
            {
                bool hasWall = IsWall(m_Level, cell.x, cell.y);
                menu.AddItem(new GUIContent(hasWall ? "🧱 Bölmeyi Kaldır" : "🧱 Bölme (Duvar) Ekle"), hasWall, () => ToggleWallCell(cell));
                if (!hasWall)
                {
                    var shape = m_BrushShape ?? CarrierBlockShape.Rect1x1;
                    var anchor = FindPlacement(m_Level, shape, cell, -1);
                    if (anchor.HasValue)
                    {
                        foreach (var c in BrushColors)
                        {
                            var col = c;
                            menu.AddItem(new GUIContent($"Blok Ekle/{PuzzleLevelSolver.ColorName(col)}"), false, () =>
                            {
                                var prev = m_BrushMechanic;
                                m_BrushMechanic = MechanicBrush.None;
                                AddBlock(col, shape, anchor.Value);
                                m_BrushMechanic = prev;
                            });
                            menu.AddItem(new GUIContent($"●↑ İçi Dolu Kutu Ekle/{PuzzleLevelSolver.ColorName(col)}"), false, () =>
                            {
                                var prev = m_BrushMechanic;
                                var prevCol = m_BrushFilledColor;
                                m_BrushMechanic = MechanicBrush.Filled;
                                m_BrushFilledColor = col;
                                AddBlock(col, shape, anchor.Value);
                                m_BrushMechanic = prev;
                                m_BrushFilledColor = prevCol;
                            });
                        }
                    }
                }
            }
            menu.ShowAsContext();
        }

        private void OnTrayPointerMove(PointerMoveEvent e)
        {
            if (m_Level == null) return;
            if (m_TrayPainting)
            {
                var c = CellAtPanel(e.position);
                if (c.x < 0 || c.y < 0 || c.x >= m_Level.stagingGridCols || c.y >= m_Level.stagingGridRows) return;
                int hit = BlockAt(m_Level, c);
                if (m_Eraser)
                {
                    if (m_TrayPaintedCells.Add(c))
                    {
                        if (hit >= 0) RemoveBlock(hit);
                        else if (IsWall(m_Level, c.x, c.y)) ToggleWallCell(c, forceAdd: false);
                    }
                }
                else if (m_BrushMechanic == MechanicBrush.Wall)
                {
                    if (hit < 0 && m_TrayPaintedCells.Add(c))
                    {
                        ToggleWallCell(c, forceAdd: m_TrayWallPaintAdd);
                    }
                }
                else if (m_BrushMechanic != MechanicBrush.None && hit >= 0 && m_TrayPaintedBlocks.Add(hit))
                {
                    ToggleMechanicBrushOnBlock(hit, m_BrushMechanic);
                }
                return;
            }
            if (m_DragBlock < 0)
            {
                // Fırça seçiliyken hayalet önizleme: blok tıklamadan önce nereye geleceği görünür
                bool brush = (m_BrushColor != null || m_BrushShape != null || m_BrushMechanic != MechanicBrush.None) && !m_Eraser && m_Pending == null;
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
            if (m_TrayPainting)
            {
                m_TrayPainting = false;
                m_TrayPaintedBlocks.Clear();
                m_TrayPaintedCells.Clear();
                if (m_Tray.HasPointerCapture(e.pointerId)) m_Tray.ReleasePointer(e.pointerId);
                return;
            }
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
            if (m_SelectedBlock >= 0 && lvl.monsters != null && m_SelectedBlock < lvl.monsters.Count)
                linked = lvl.monsters[m_SelectedBlock].color;

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
                            : $"En az {m_Solve.Drags} sürüklemeyle çözülüyor ({m_Solve.minSlides} hücre).";
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
            if (lvl.monsters != null)
            {
                foreach (var m in lvl.monsters)
                {
                    if (m == null) continue;
                    colors.Add(m.color);
                    if (m.isFilledBox) colors.Add(m.filledBallColor);
                }
            }
            m_Stats.Add(Stat((lvl.monsters?.Count ?? 0).ToString(), "blok"));
            int expectedGlass = lvl.GetExpectedGlassBallCount();
            int filledBalls = lvl.GetFilledBoxBallCount();
            int camBalls = lvl.useManualBallLayout ? (lvl.manualBalls?.Count ?? 0) : lvl.GetGlassStartBallCount();
            string ballStat = lvl.useManualBallLayout
                ? (camBalls != expectedGlass ? $"{camBalls}/{expectedGlass}" : camBalls.ToString())
                : (filledBalls > 0 ? $"{camBalls}+{filledBalls}" : camBalls.ToString());
            string ballLabel = lvl.useManualBallLayout
                ? "top (elle)"
                : (filledBalls > 0 ? "cam+kutu" : "top");
            var ballStatBox = Stat(ballStat, ballLabel);
            if (filledBalls > 0)
            {
                ballStatBox.tooltip = $"Camda başlayan: {camBalls} (beklenen {expectedGlass}) · Dolu kutularda: {filledBalls} · Toplam toplayıcı yuva: {lvl.GetTotalBallCount()}";
            }
            m_Stats.Add(ballStatBox);
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
            var rl = new Label(m.isFilledBox ? "Top / Yuva" : "Yuva (top)");
            rl.AddToClassList("inspector__row-label");
            row.Add(rl);
            row.Add(new Button(() => SetBlockCount(i, m.count - 1)) { text = "−" });
            var count = new Label(m.count.ToString());
            count.AddToClassList("inspector__count");
            row.Add(count);
            row.Add(new Button(() => SetBlockCount(i, m.count + 1)) { text = "+" });
            int defaultCount = CarrierBlockShapeUtility.GetCellCount(m.blockShape) * 2;
            int filledCap = 0;
            if (m.isFilledBox)
            {
                var remCap = m_Level.GetRemainingCollectorCapacityByColor(i);
                remCap.TryGetValue(m.filledBallColor, out filledCap);
            }
            row.Add(new Button(() =>
            {
                int target = m.isFilledBox && filledCap > 0 ? Mathf.Min(defaultCount, filledCap) : defaultCount;
                SetBlockCount(i, target);
            })
            {
                text = "↺",
                tooltip = "Şekle göre varsayılan: hücre başına 2 yuva (büyük bloğa çok, küçüğe az)."
            });
            if (m.isFilledBox && filledCap > 0 && m.count != filledCap)
            {
                row.Add(new Button(() => SetBlockCount(i, filledCap))
                {
                    text = $"{filledCap}",
                    tooltip = $"Aynı renkteki boş kutuların kalan kapasitesine ({filledCap}) eşitle."
                });
            }
            m_Inspector.Add(row);

            var mechTitle = new Label("Özel Mekanikler");
            mechTitle.AddToClassList("inspector__sub");
            mechTitle.style.marginTop = 8;
            mechTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Inspector.Add(mechTitle);

            var keyToggle = new Toggle("🔑 Anahtarlı kutu") { value = m.hasKey };
            keyToggle.tooltip = "Tamamlandığında kilitli bloklara 1 altın anahtar gönderir.";
            keyToggle.RegisterValueChangedCallback(e => SetBlockHasKey(i, e.newValue));
            m_Inspector.Add(keyToggle);

            var lockRow = new VisualElement();
            lockRow.AddToClassList("inspector__row");
            var lockLbl = new Label("🔒 Kilit (anahtar)");
            lockLbl.AddToClassList("inspector__row-label");
            lockRow.Add(lockLbl);
            lockRow.Add(new Button(() => SetBlockLockKeys(i, m.lockKeyCount - 1)) { text = "−" });
            var lockCount = new Label(m.lockKeyCount > 0 ? m.lockKeyCount.ToString() : "Yok");
            lockCount.AddToClassList("inspector__count");
            lockRow.Add(lockCount);
            lockRow.Add(new Button(() => SetBlockLockKeys(i, m.lockKeyCount + 1)) { text = "+" });
            if (m.lockKeyCount > 0)
                lockRow.Add(new Button(() => SetBlockLockKeys(i, 0)) { text = "Aç", tooltip = "Kilidi kaldır (0)." });
            m_Inspector.Add(lockRow);

            var iceRow = new VisualElement();
            iceRow.AddToClassList("inspector__row");
            var iceLbl = new Label("❄ Buz (sayaç)");
            iceLbl.AddToClassList("inspector__row-label");
            iceRow.Add(iceLbl);
            iceRow.Add(new Button(() => SetBlockIceCount(i, m.iceCount - 1)) { text = "−" });
            var iceField = new IntegerField { value = m.iceCount, isDelayed = true };
            iceField.style.width = 44;
            iceField.RegisterValueChangedCallback(e => SetBlockIceCount(i, e.newValue));
            iceRow.Add(iceField);
            iceRow.Add(new Button(() => SetBlockIceCount(i, m.iceCount + 1)) { text = "+" });
            m_Inspector.Add(iceRow);

            var icePresets = new VisualElement();
            icePresets.AddToClassList("inspector__row");
            var icePresetLbl = new Label("Buz hızlı");
            icePresetLbl.AddToClassList("inspector__row-label");
            icePresets.Add(icePresetLbl);
            icePresets.Add(new Button(() => SetBlockIceCount(i, 0)) { text = "0" });
            icePresets.Add(new Button(() => SetBlockIceCount(i, 5)) { text = "5" });
            icePresets.Add(new Button(() => SetBlockIceCount(i, 10)) { text = "10" });
            icePresets.Add(new Button(() => SetBlockIceCount(i, 15)) { text = "15" });
            m_Inspector.Add(icePresets);

            var filledToggle = new Toggle("●↑ İçi dolu kutu (cama atar + toplar)") { value = m.isFilledBox };
            filledToggle.tooltip = "Açıkken kutu başlangıçta toplarla doludur; slota konulunca içindeki topları camın içine gönderir, ardından yok olmadan kendi rengindeki topları toplayıp tamamlanır.";
            filledToggle.RegisterValueChangedCallback(e => SetBlockIsFilled(i, e.newValue));
            m_Inspector.Add(filledToggle);

            if (m.isFilledBox)
            {
                var ballColorField = new EnumField("İçindeki Top Rengi", m.filledBallColor);
                ballColorField.RegisterValueChangedCallback(e => SetBlockFilledColor(i, (PuzzleColor)e.newValue));
                m_Inspector.Add(ballColorField);

                if (filledCap <= 0)
                {
                    var matchBtn = new Button(() =>
                    {
                        ResolveFilledTarget(m_Level, i, m.filledBallColor, m.count, allowFallbackColor: true, out var rCol, out var rCnt);
                        if (rCol == m.filledBallColor && rCnt == m.count)
                        {
                            ShowNotification(new GUIContent("Levelde uygun toplayıcı kutu yok"));
                            return;
                        }
                        Record("Dolu kutuyu mevcut toplayıcıya uydur");
                        m.filledBallColor = rCol;
                        m.count = rCnt;
                        SyncGlassAfterBoxChange(forceRebuild: true);
                        OnEdited(syncScene: true);
                    }) { text = "Mevcut toplayıcıya uydur" };
                    matchBtn.AddToClassList("inspector__action");
                    m_Inspector.Add(matchBtn);
                }
            }

            if (m.hasKey || m.lockKeyCount > 0 || m.iceCount > 0 || m.isFilledBox)
            {
                string unit = m_Level.iceMeltMode == IceMeltMode.PerBallCollected ? "top toplandığında" : "kutu tamamlandığında";
                string mechSummary = "";
                if (m.isFilledBox) mechSummary += $"• İçi dolu: slota konulunca camın içine {m.count} adet {PuzzleLevelSolver.ColorName(m.filledBallColor)} top gönderir, ardından {m.count} adet {PuzzleLevelSolver.ColorName(m.color)} top toplayıp tamamlanır.\n";
                if (m.hasKey) mechSummary += "• Anahtarlı: tamamlanınca tüm kilitleri 1 azaltır.\n";
                if (m.lockKeyCount > 0) mechSummary += $"• Kilitli ({m.lockKeyCount}): {m.lockKeyCount} anahtar gelene kadar kaymaz ve top toplamaz.\n";
                if (m.iceCount > 0) mechSummary += $"• Buzlu ({m.iceCount}): {m.iceCount} {unit} eriyene kadar donuk kalır.";
                var mechHint = new Label(mechSummary.TrimEnd());
                mechHint.AddToClassList("inspector__hint");
                m_Inspector.Add(mechHint);
            }

            if (m.isFilledBox)
            {
                int collectorSockets = 0;
                int totalSupply = 0;
                if (m_Level.monsters != null)
                {
                    foreach (var box in m_Level.monsters)
                    {
                        if (box == null) continue;
                        if (box.color == m.filledBallColor) collectorSockets += Mathf.Max(1, box.count);
                        if (box.isFilledBox && box.filledBallColor == m.filledBallColor) totalSupply += Mathf.Max(1, box.count);
                    }
                }
                int fromGlass = Mathf.Max(0, collectorSockets - totalSupply);
                string balanceText = collectorSockets >= totalSupply
                    ? $"{PuzzleLevelSolver.ColorName(m.filledBallColor)} dengesi: {collectorSockets} toplam yuva = {fromGlass} camdan + {totalSupply} dolu kutudan."
                    : $"{PuzzleLevelSolver.ColorName(m.filledBallColor)} eksik yuva: dolu kutularda {totalSupply} top, kutularda {collectorSockets} yuva var!";
                var order = new Label(balanceText);
                order.AddToClassList("inspector__hint");
                m_Inspector.Add(order);
            }
            else if (m_Level.useManualBallLayout)
            {
                int inGlass = 0;
                if (m_Level.manualBalls != null)
                    foreach (var mb in m_Level.manualBalls) if (mb != null && mb.color == m.color) inGlass++;
                var order = new Label($"Elle top yerleşimi açık: camda {inGlass} adet {PuzzleLevelSolver.ColorName(m.color)} top var.");
                order.AddToClassList("inspector__hint");
                m_Inspector.Add(order);
            }
            else
            {
                int g = FindGroup(m.color, m.count, fromTop: false);
                bool suppliedByFilled = g < 0 && m_Level.HasAnyFilledBox();
                var order = new Label(g >= 0
                    ? $"Bu bloğun topları cam sırasında {g + 1}. sırada (alttan). Sırayı aşağıdaki şeritten değiştir."
                    : (suppliedByFilled
                        ? "Bu bloğun topları dolu kutudan gelecek (camda başlamıyor)."
                        : "Bu bloğun topları cam sırasında yok!"));
                order.AddToClassList("inspector__hint");
                m_Inspector.Add(order);
            }

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

            var trayPresets = new VisualElement();
            trayPresets.AddToClassList("chip-grid");
            foreach (var (tc, tr) in new[] { (5, 4), (6, 5), (6, 7), (7, 8) })
            {
                int cc = tc, rr = tr;
                var chip = Chip(() =>
                {
                    Record("Tepsi boyutu");
                    lvl.stagingGridCols = cc;
                    lvl.stagingGridRows = rr;
                    PruneOutOfBoundsWalls(lvl);
                    OnEdited(syncScene: true);
                }, lvl.stagingGridCols == cc && lvl.stagingGridRows == rr);
                chip.Add(new Label($"{cc}×{rr}"));
                trayPresets.Add(chip);
            }
            m_Inspector.Add(trayPresets);

            var cols = new SliderInt("Tepsi sütun", 2, 9) { value = lvl.stagingGridCols, showInputField = true };
            cols.RegisterValueChangedCallback(e =>
            {
                Record("Tepsi sütun");
                lvl.stagingGridCols = e.newValue;
                PruneOutOfBoundsWalls(lvl);
                OnEdited(syncScene: true);
            });
            m_Inspector.Add(cols);
            var rows = new SliderInt("Tepsi satır", 2, 10) { value = lvl.stagingGridRows, showInputField = true };
            rows.RegisterValueChangedCallback(e =>
            {
                Record("Tepsi satır");
                lvl.stagingGridRows = e.newValue;
                PruneOutOfBoundsWalls(lvl);
                OnEdited(syncScene: true);
            });
            m_Inspector.Add(rows);

            var customTrayToggle = new Toggle("Özel tepsi ölçüleri")
            {
                value = lvl.useCustomTraySettings,
                tooltip = "Açıkken sahnedeki tepsinin fiziksel genişliğini, yüksekliğini, boşluğunu, hücre boyutunu ve çerçevesini özelleştirir."
            };
            customTrayToggle.RegisterValueChangedCallback(e =>
            {
                Record("Özel tepsi ölçüleri");
                lvl.useCustomTraySettings = e.newValue;
                OnEdited(syncScene: true);
            });
            m_Inspector.Add(customTrayToggle);

            if (lvl.useCustomTraySettings)
            {
                void SyncTrayVisualOnly()
                {
                    EditorUtility.SetDirty(lvl);
                    RefreshState();
                    if (m_LiveSync) ScheduleSceneSync();
                }

                void AddTraySlider(string label, float value, float min, float max, System.Action<float> setter)
                {
                    var s = new Slider(label, min, max) { value = value, showInputField = true };
                    s.RegisterValueChangedCallback(e =>
                    {
                        Record(label);
                        setter(e.newValue);
                        SyncTrayVisualOnly();
                    });
                    m_Inspector.Add(s);
                }

                var matchGlass = new Toggle("Cama göre genişlik") { value = lvl.trayMatchGlassWidth };
                matchGlass.RegisterValueChangedCallback(e => { Record("Tepsi genişlik hizası"); lvl.trayMatchGlassWidth = e.newValue; SyncTrayVisualOnly(); });
                m_Inspector.Add(matchGlass);

                var showJar = new Toggle("Dış cam kavanoz") { value = lvl.trayShowOuterGlassJar };
                showJar.RegisterValueChangedCallback(e => { Record("Tepsi dış cam kavanoz"); lvl.trayShowOuterGlassJar = e.newValue; SyncTrayVisualOnly(); });
                m_Inspector.Add(showJar);

                AddTraySlider("Maks. genişlik", lvl.trayMaxBoardWidth, 1.20f, 4.00f, v => lvl.trayMaxBoardWidth = v);
                AddTraySlider("Maks. yükseklik", lvl.trayMaxBoardHeight, 1.20f, 5.20f, v => lvl.trayMaxBoardHeight = v);
                AddTraySlider("Camdan boşluk", lvl.trayGapBelowGlass, 0.02f, 0.80f, v => lvl.trayGapBelowGlass = v);

                var manualCell = new Toggle("Elle hücre boyutu") { value = lvl.trayUseManualCellSize };
                manualCell.RegisterValueChangedCallback(e =>
                {
                    Record("Elle hücre boyutu");
                    lvl.trayUseManualCellSize = e.newValue;
                    OnEdited(syncScene: true);
                });
                m_Inspector.Add(manualCell);

                if (lvl.trayUseManualCellSize)
                {
                    AddTraySlider("Hücre boyutu", lvl.trayManualCellSize, 0.22f, 0.82f, v => lvl.trayManualCellSize = v);
                }

                AddTraySlider("Çerçeve kalınlığı", lvl.trayFramePadding, 0.03f, 0.30f, v => lvl.trayFramePadding = v);
                AddTraySlider("Köşe yuvarlaklığı", lvl.trayFrameCornerRadius, 0.04f, 0.40f, v => lvl.trayFrameCornerRadius = v);
                AddTraySlider("İç boşluk", lvl.trayInnerMargin, 0.01f, 0.25f, v => lvl.trayInnerMargin = v);

                var resetTray = new Button(() =>
                {
                    Record("Tepsi ayarlarını sıfırla");
                    lvl.useCustomTraySettings = false;
                    lvl.trayMatchGlassWidth = true;
                    lvl.trayMaxBoardWidth = 2.822f;
                    lvl.trayMaxBoardHeight = 3.20f;
                    lvl.trayFramePadding = 0.068f;
                    lvl.trayInnerMargin = 0.048f;
                    lvl.trayFrameCornerRadius = 0.135f;
                    lvl.trayGapBelowGlass = 0.11f;
                    lvl.trayUseManualCellSize = false;
                    lvl.trayManualCellSize = 0.506f;
                    lvl.trayShowOuterGlassJar = true;
                    OnEdited(syncScene: true);
                }) { text = "Tepsiyi varsayılana sıfırla" };
                resetTray.AddToClassList("inspector__action");
                m_Inspector.Add(resetTray);
            }

            var iceMelt = new EnumField("Buz erimesi", lvl.iceMeltMode);
            iceMelt.tooltip = "PerBallCollected: herhangi bir kutuya her top indiğinde buz sayacı 1 düşer.\nPerBoxCompleted: her kutu tamamlandığında buz sayacı 1 düşer.";
            iceMelt.RegisterValueChangedCallback(e => { Record("Buz erime modu"); lvl.iceMeltMode = (IceMeltMode)e.newValue; OnEdited(syncScene: true); });
            m_Inspector.Add(iceMelt);

            // ---- Öğretici: oyunda bir el, hangi bloğun nereye sürükleneceğini gösterir ----
            var tutTitle = new Label("Öğretici");
            tutTitle.AddToClassList("inspector__title");
            m_Inspector.Add(tutTitle);
            var tutToggle = new Toggle("El ipucu") { value = lvl.tutorialHint, tooltip = "Level başında çözümün ilk hamlesini bir el ile gösterir (ilk leveller için)." };
            tutToggle.RegisterValueChangedCallback(e =>
            {
                Record("Öğretici ipucu");
                lvl.tutorialHint = e.newValue;
                if (e.newValue && lvl.tutorialBlockIndex < 0) TakeTutorialFromSolution(lvl, silent: true);
                OnEdited(syncScene: false);
            });
            m_Inspector.Add(tutToggle);
            if (lvl.tutorialHint)
            {
                string desc = lvl.tutorialBlockIndex >= 0
                    ? $"Blok #{lvl.tutorialBlockIndex + 1}: ({lvl.tutorialFromAnchor.x + 1},{lvl.tutorialFromAnchor.y + 1}) → ({lvl.tutorialTargetAnchor.x + 1},{lvl.tutorialTargetAnchor.y + 1})"
                    : "Henüz hamle seçilmedi.";
                var tutDesc = new Label(desc);
                tutDesc.AddToClassList("inspector__sub");
                m_Inspector.Add(tutDesc);
                var take = new Button(() => TakeTutorialFromSolution(lvl, silent: false)) { text = "Çözümden ilk hamleyi al", tooltip = "Çözücünün bulduğu en kısa çözümün ilk sürüklemesini ipucu yapar." };
                take.AddToClassList("inspector__action");
                m_Inspector.Add(take);
            }

            // ---- Üst Cam Besleme Tüpleri (bu levele özel) ----
            var feedTitle = new Label("Üst cam besleme tüpleri (bu levele özel)");
            feedTitle.AddToClassList("inspector__title");
            m_Inspector.Add(feedTitle);

            var feedToggle = new Toggle("Üstten tüp besleme")
            {
                value = lvl.useBallFeedTubes,
                tooltip = "Sadece bu levelde üst omuzlardan topları döken kıvrımlı cam borular kullanır."
            };
            feedToggle.RegisterValueChangedCallback(e =>
            {
                Record("Üst cam besleme tüpleri");
                lvl.useBallFeedTubes = e.newValue;
                OnEdited(syncScene: true);
            });
            m_Inspector.Add(feedToggle);

            if (lvl.useBallFeedTubes)
            {
                var feedModeField = new EnumField("Tüp yönü", lvl.feedTubeMode);
                feedModeField.RegisterValueChangedCallback(e =>
                {
                    Record("Besleme tüpü yönü");
                    lvl.feedTubeMode = (BallFeedTubeMode)e.newValue;
                    OnEdited(syncScene: true);
                });
                m_Inspector.Add(feedModeField);

                var feedRow = new VisualElement();
                feedRow.AddToClassList("inspector__row");
                var feedLbl = new Label("Hazne limiti (0=oto)");
                feedLbl.AddToClassList("inspector__row-label");
                feedRow.Add(feedLbl);
                feedRow.Add(new Button(() =>
                {
                    Record("Tüp hazne limiti");
                    lvl.feedTubeContainerLimit = Mathf.Clamp(lvl.feedTubeContainerLimit - 1, 0, 60);
                    OnEdited(syncScene: true);
                }) { text = "−" });
                var limitField = new IntegerField { value = lvl.feedTubeContainerLimit, isDelayed = true };
                limitField.style.width = 44;
                limitField.RegisterValueChangedCallback(e =>
                {
                    Record("Tüp hazne limiti");
                    lvl.feedTubeContainerLimit = Mathf.Clamp(e.newValue, 0, 60);
                    OnEdited(syncScene: true);
                });
                feedRow.Add(limitField);
                feedRow.Add(new Button(() =>
                {
                    Record("Tüp hazne limiti");
                    lvl.feedTubeContainerLimit = Mathf.Clamp(lvl.feedTubeContainerLimit + 1, 0, 60);
                    OnEdited(syncScene: true);
                }) { text = "+" });
                m_Inspector.Add(feedRow);

                var feedLayout = GlassShapeData.BuildLayout(lvl);
                int total = feedLayout.balls != null ? feedLayout.balls.Count : 0;
                int eff = BallFeedTubeUtility.GetEffectiveContainerLimit(lvl.feedTubeContainerLimit, total);
                int q = Mathf.Max(0, total - eff);
                var feedInfo = new Label($"{eff} top haznede · {q} top tüplerde. Genişlik ve açı için Cam Stüdyosu'nu aç.");
                feedInfo.AddToClassList("inspector__sub");
                m_Inspector.Add(feedInfo);
            }

            var glassSizeRow = new VisualElement();
            glassSizeRow.AddToClassList("inspector__row");
            var glassSizeLbl = new Label("Cam boyutu");
            glassSizeLbl.AddToClassList("inspector__row-label");
            glassSizeRow.Add(glassSizeLbl);
            var compactBtn = new Button(() => SetQuickGlassSize(compact: true))
            {
                text = "Kompakt",
                tooltip = "Camı alçaltır (2.70 × 0.62); büyük grid tepsilere yer açar."
            };
            compactBtn.style.width = StyleKeyword.Auto;
            compactBtn.style.flexGrow = 1;
            glassSizeRow.Add(compactBtn);
            var normalGlassBtn = new Button(() => SetQuickGlassSize(compact: false))
            {
                text = "Normal",
                tooltip = "Varsayılan otomatik cam yüksekliğine döner."
            };
            normalGlassBtn.style.width = StyleKeyword.Auto;
            normalGlassBtn.style.flexGrow = 1;
            glassSizeRow.Add(normalGlassBtn);
            m_Inspector.Add(glassSizeRow);

            var glassBtn = new Button(() => GlassStudioWindow.OpenFor(m_Level)) { text = "✨ Bu levelin camını düzenle" };
            glassBtn.AddToClassList("inspector__action");
            m_Inspector.Add(glassBtn);

            var hint = new Label(
                "Nasıl kullanılır:\n" +
                "• Üstten renk, şekil ve/veya mekanik fırçası seç, tepside boş hücreye tıkla: blok eklenir.\n" +
                "• Fırça seçiliyken bloğa tıkla: rengi/şekli/mekaniği değişir. Silgi: blok silinir.\n" +
                "• Mekanik sayaçlarını (kilit anahtar sayısı / buz sayacı) bloğu seçip buradan ayarla.\n" +
                "• Fırça yokken bloğu sürükle: taşınır.\n" +
                "• Cam sırasında iki çipe sırayla tıkla: yerleri değişir.\n" +
                "• '✋ Elle top diz' açıkken camdaki slotlara tıklayıp/sürükleyerek topları kendin koyabilir veya yerini değiştirebilirsin.\n" +
                "• Esc fırçayı bırakır, Delete seçili bloğu/topu siler, Ctrl+Z geri alır.");
            hint.AddToClassList("inspector__hint");
            m_Inspector.Add(hint);
        }

        /// <summary>Çözümün ilk sürüklemesini öğretici ipucu olarak kaydeder.</summary>
        private void TakeTutorialFromSolution(PuzzleGlassLevelData lvl, bool silent)
        {
            if (m_Solve == null || m_Solve.status != SolveStatus.Solved)
            {
                if (!silent) ShowNotification(new GUIContent("Önce çözüm bulunmalı (çözüm aranıyor ya da çözülemiyor)"));
                return;
            }
            if (!m_Solve.TryGetFirstDrag(out int block, out Vector2Int offset) || block < 0 || block >= lvl.monsters.Count)
            {
                if (!silent) ShowNotification(new GUIContent("Bu level kaydırmadan çözülüyor: gösterilecek hamle yok"));
                return;
            }
            Record("Öğretici ipucu");
            lvl.tutorialHint = true;
            lvl.tutorialBlockIndex = block;
            lvl.tutorialFromAnchor = lvl.monsters[block].gridAnchor;
            lvl.tutorialTargetAnchor = lvl.monsters[block].gridAnchor + offset;
            OnEdited(syncScene: false);
            if (!silent) ShowNotification(new GUIContent($"İpucu: blok #{block + 1}"));
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
                m_BallBrushColor = null;
                m_BallEraser = false;
                m_SelectedBallSlot = null;
                m_SelectedGroup = -1;
                m_SelectedBlock = -1;
                RefreshAll();
                e.StopPropagation();
            }
            else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && m_Pending == null)
            {
                var ve = e.target as VisualElement;
                if (ve != null && (ve.GetFirstOfType<TextField>() != null || ve.GetFirstOfType<IntegerField>() != null)) return;
                if (m_SelectedBlock >= 0)
                {
                    RemoveBlock(m_SelectedBlock);
                    e.StopPropagation();
                }
                else if (m_Level != null && m_Level.useManualBallLayout && m_SelectedBallSlot.HasValue)
                {
                    int idx = FindManualBallIndex(m_SelectedBallSlot.Value.col, m_SelectedBallSlot.Value.row);
                    if (idx >= 0)
                    {
                        Record("Camdan top sil");
                        m_Level.manualBalls.RemoveAt(idx);
                        m_SelectedBallSlot = null;
                        SyncBoxesFromManualBalls(m_Level);
                        OnEdited(syncScene: true);
                        e.StopPropagation();
                    }
                }
            }
        }

        // ==================== Yardımcılar ====================

        private static string SolveText(SolveResult s)
        {
            switch (s.status)
            {
                case SolveStatus.Solved: return $"çözülebilir, en az {s.Drags} sürükleme";
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
