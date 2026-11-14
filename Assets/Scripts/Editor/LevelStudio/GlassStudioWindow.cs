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
    /// Cam Stüdyosu: her levelin camını görerek düzenler.
    /// Solda level listesi; ortada büyük önizleme (cam + toplar + tepsi) ve tutamaçlar;
    /// sağda cam türü, ölçüler, hazır boyutlar ve renkler; altta bulgular.
    ///
    /// Havuz (önerilen): düz tabanlı yuvarlak köşeli kutu. Tutamaçlar: sağ kenar = genişlik,
    /// üst kenar = yükseklik, üst köşe = köşe yuvarlaklığı.
    /// Özel şekil: kontur noktaları sürüklenir, çift tıkla nokta eklenir, sağ tıkla silinir. Taban düz ve yeterince
    /// geniş olduğu sürece oyunda havuz gibi oynanır (toplar deliğin üstünden dümdüz iner).
    /// </summary>
    public sealed class GlassStudioWindow : EditorWindow
    {
        private const string LevelsFolder = "Assets/Levels";
        private const string StylePath = "Assets/Scripts/Editor/LevelStudio/GlassLevelStudio.uss";
        private const string SelectedKey = "PuzzleGlass_GlassStudio_SelectedLevel";
        private const string LiveKey = "PuzzleGlass_Studio_LiveSync";
        private const float HandleRadius = 7f;

        private enum Handle { None, Width, Height, Corner, Point }

        private readonly List<PuzzleGlassLevelData> m_All = new List<PuzzleGlassLevelData>();
        private readonly List<PuzzleGlassLevelData> m_Visible = new List<PuzzleGlassLevelData>();
        private PuzzleGlassLevelData m_Level;
        private string m_Search = "";
        private bool m_LiveSync;
        private double m_SceneSyncDue = -1;

        // Önizleme eşlemesi (dünya -> piksel); sürüklerken sabit tutulur
        private float m_Scale = 100f;
        private Vector2 m_Origin;
        private bool m_ViewFrozen;

        private Handle m_Drag = Handle.None;
        private int m_DragPoint = -1;
        private Handle m_Hover = Handle.None;
        private int m_HoverPoint = -1;

        private ListView m_List;
        private Label m_Title, m_State, m_Readout;
        private Button m_SaveButton;
        private VisualElement m_Canvas, m_Inspector;
        private ScrollView m_Issues;

        private static readonly (string name, float w, float h, float corner, float frame)[] BoxPresets =
        {
            ("Varsayılan", 2.64f, 1.18f, 0.22f, 0.115f),
            ("Geniş", 2.92f, 0.96f, 0.20f, 0.105f),
            ("Kare", 1.60f, 1.60f, 0.26f, 0.115f),
            ("Yüksek", 1.50f, 1.95f, 0.24f, 0.115f),
            ("Dar uzun", 1.20f, 2.30f, 0.30f, 0.115f),
            ("Kapsül", 2.40f, 1.20f, 0.60f, 0.115f)
        };

        private static readonly (string name, Color main, Color high, Color edge)[] FramePalettes =
        {
            ("Lavanta", new Color(0.50f, 0.50f, 0.90f), new Color(0.84f, 0.84f, 0.98f), new Color(0.38f, 0.36f, 0.80f)),
            ("Gök mavi", new Color(0.28f, 0.62f, 0.92f), new Color(0.76f, 0.90f, 0.99f), new Color(0.18f, 0.44f, 0.76f)),
            ("Nane", new Color(0.30f, 0.76f, 0.62f), new Color(0.76f, 0.96f, 0.88f), new Color(0.18f, 0.56f, 0.46f)),
            ("Mercan", new Color(0.90f, 0.48f, 0.52f), new Color(0.98f, 0.82f, 0.84f), new Color(0.70f, 0.32f, 0.38f)),
            ("Arduvaz", new Color(0.32f, 0.36f, 0.48f), new Color(0.68f, 0.72f, 0.84f), new Color(0.20f, 0.22f, 0.32f))
        };

        private static readonly (string name, GlassColorTheme theme)[] Themes =
        {
            ("Gök mavisi", GlassColorTheme.SkyBlueGlossy), ("Kristal", GlassColorTheme.CrystalClear),
            ("Zümrüt", GlassColorTheme.EmeraldGreen), ("Gül kuvars", GlassColorTheme.RoseQuartz),
            ("Ametist", GlassColorTheme.AmethystPurple), ("Kehribar", GlassColorTheme.AmberGold)
        };

        [MenuItem("Tools/PuzzleGlass/✨ Cam Stüdyosu", false, 1)]
        public static void Open()
        {
            var w = GetWindow<GlassStudioWindow>("Cam Stüdyosu");
            w.minSize = new Vector2(980, 600);
        }

        public static void OpenFor(PuzzleGlassLevelData lvl)
        {
            Open();
            var w = GetWindow<GlassStudioWindow>();
            if (lvl != null) w.SelectAndShow(lvl);
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
        }

        private void OnFocus()
        {
            if (m_List != null) LoadLevels();
        }

        private void OnUndoRedo()
        {
            if (m_Level == null) return;
            RefreshAll();
            if (m_LiveSync) ScheduleSceneSync();
        }

        // ==================== Kurulum ====================

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("studio");
            root.focusable = true;

            root.Add(BuildToolbar());

            var body = new VisualElement();
            body.AddToClassList("studio__body");
            root.Add(body);
            body.Add(BuildLevelList());

            var center = new VisualElement();
            center.AddToClassList("studio__center");
            var bar = new VisualElement();
            bar.AddToClassList("automation");
            m_Readout = new Label();
            m_Readout.AddToClassList("glass-readout");
            bar.Add(m_Readout);
            center.Add(bar);

            m_Canvas = new VisualElement();
            m_Canvas.AddToClassList("glass-canvas");
            m_Canvas.generateVisualContent += DrawCanvas;
            m_Canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_Canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_Canvas.RegisterCallback<GeometryChangedEvent>(_ => { if (!m_ViewFrozen) m_Canvas.MarkDirtyRepaint(); });
            center.Add(m_Canvas);

            var legend = new Label("Havuzda tutamaçlar: sağ kenar = genişlik · üst kenar = yükseklik · üst köşe = köşe yuvarlaklığı.   Özel şekilde: noktayı sürükle, çift tıkla ekle, sağ tıkla sil.");
            legend.AddToClassList("board__caption");
            center.Add(legend);
            body.Add(center);

            var side = new ScrollView();
            side.AddToClassList("studio__side");
            m_Inspector = new VisualElement();
            m_Inspector.AddToClassList("inspector");
            side.Add(m_Inspector);
            body.Add(side);

            m_Issues = new ScrollView();
            m_Issues.AddToClassList("issues");
            root.Add(m_Issues);

            LoadLevels();
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
            var live = new Toggle("Sahnede canlı") { value = m_LiveSync, tooltip = "Açıkken her değişiklik sahnedeki levele de uygulanır." };
            live.RegisterValueChangedCallback(e =>
            {
                m_LiveSync = e.newValue;
                EditorPrefs.SetBool(LiveKey, m_LiveSync);
                if (m_LiveSync) ScheduleSceneSync();
            });
            bar.Add(live);
            bar.Add(new Button(() => GlassLevelStudioWindow.Open()) { text = "Level Stüdyosu", tooltip = "Blokları ve cam sırasını düzenle." });
            bar.Add(new Button(() => ApplyToScene(true)) { text = "▶ Sahnede aç" });
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

            m_List = new ListView(m_Visible, 48, MakeRow, BindRow) { selectionType = SelectionType.Single };
            m_List.style.flexGrow = 1;
            m_List.selectionChanged += items =>
            {
                foreach (var item in items) { SelectLevel((PuzzleGlassLevelData)item); break; }
            };
            panel.Add(m_List);
            return panel;
        }

        private static VisualElement MakeRow()
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
            row.Add(text);
            var swatch = new VisualElement { name = "swatch" };
            swatch.AddToClassList("level-row__glass");
            row.Add(swatch);
            return row;
        }

        private void BindRow(VisualElement row, int i)
        {
            var lvl = m_Visible[i];
            row.Q<Label>("number").text = lvl.levelNumber.ToString();
            string name = string.IsNullOrEmpty(lvl.levelName) ? $"Level {lvl.levelNumber}" : lvl.levelName;
            int paren = name.IndexOf(" (");
            if (paren > 0 && name.Contains("Izgara")) name = name.Substring(0, paren);
            row.Q<Label>("title").text = name;
            string kind;
            if (!lvl.useCustomGlassShape) kind = "Varsayılan havuz";
            else if (GlassShapeData.IsBoxPreset(lvl.glassPreset))
            {
                GlassShapeData.GetBox(lvl, out float w, out float h, out _);
                kind = $"Havuz {w:0.00} × {h:0.00}";
            }
            else kind = "Özel şekil";
            row.Q<Label>("sub").text = kind;
            var sw = row.Q("swatch");
            sw.style.backgroundColor = lvl.boxFrameColor.a > 0.01f ? lvl.boxFrameColor : new Color(0.5f, 0.5f, 0.9f);
            sw.style.borderTopColor = sw.style.borderBottomColor = sw.style.borderLeftColor = sw.style.borderRightColor = lvl.glassBodyColor;
        }

        private void LoadLevels()
        {
            m_All.Clear();
            if (!Directory.Exists(LevelsFolder)) Directory.CreateDirectory(LevelsFolder);
            foreach (string guid in AssetDatabase.FindAssets("t:PuzzleGlassLevelData", new[] { LevelsFolder }))
            {
                var a = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) m_All.Add(a);
            }
            m_All.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));
            ApplyFilter();

            var start = m_Level != null && m_All.Contains(m_Level) ? m_Level : null;
            if (start == null)
            {
                string saved = SessionState.GetString(SelectedKey, "");
                var prev = string.IsNullOrEmpty(saved) ? null : AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(AssetDatabase.GUIDToAssetPath(saved));
                start = prev != null && m_All.Contains(prev) ? prev : (m_All.Count > 0 ? m_All[0] : null);
            }
            if (start == null) { m_Level = null; RefreshAll(); return; }
            if (start != m_Level) SelectLevel(start); else RefreshAll();
            int idx = m_Visible.IndexOf(start);
            if (idx >= 0) { m_List.SetSelectionWithoutNotify(new[] { idx }); m_List.ScrollToItem(idx); }
        }

        private void ApplyFilter()
        {
            m_Visible.Clear();
            string term = m_Search.Trim().ToLowerInvariant();
            foreach (var l in m_All)
                if (term.Length == 0 || (l.levelName ?? "").ToLowerInvariant().Contains(term) || l.levelNumber.ToString() == term)
                    m_Visible.Add(l);
            m_List?.RefreshItems();
        }

        private void SelectLevel(PuzzleGlassLevelData lvl)
        {
            if (lvl == m_Level) return;
            m_Level = lvl;
            m_Drag = Handle.None;
            m_ViewFrozen = false;
            if (lvl != null) SessionState.SetString(SelectedKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(lvl)));
            RefreshAll();
            if (m_LiveSync) ScheduleSceneSync();
        }

        public void SelectAndShow(PuzzleGlassLevelData lvl)
        {
            if (m_List == null) { m_Level = null; SessionState.SetString(SelectedKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(lvl))); return; }
            SelectLevel(lvl);
            int i = m_Visible.IndexOf(lvl);
            if (i >= 0) { m_List.SetSelectionWithoutNotify(new[] { i }); m_List.ScrollToItem(i); }
        }

        // ==================== Düzenleme ====================

        private void Record(string what) => Undo.RecordObject(m_Level, what);

        private void OnEdited(bool syncScene = true)
        {
            EditorUtility.SetDirty(m_Level);
            RefreshAll();
            m_List?.RefreshItems();
            if (syncScene && m_LiveSync) ScheduleSceneSync();
        }

        /// <summary>Leveli özel cama geçirir (ilk düzenlemede varsayılan havuzdan kopyalanır).</summary>
        private void EnsureCustom()
        {
            if (m_Level.useCustomGlassShape && m_Level.contourPoints != null && m_Level.contourPoints.Count >= 3) return;
            m_Level.useCustomGlassShape = true;
            m_Level.glassPreset = GlassShapePreset.RectangleBox;
            m_Level.symmetricMode = true;
            m_Level.widthScale = 1f;
            m_Level.heightScale = 1f;
            m_Level.showShoulderHandles = false;
            m_Level.contourPoints = GlassShapeData.BuildBoxContour(GlassShapeData.DefaultWidth, GlassShapeData.DefaultHeight, GlassShapeData.DefaultBottomY);
        }

        private void SetBox(float width, float height, float? corner = null, float? frame = null, string undo = "Cam ölçüsü", bool syncScene = true)
        {
            Record(undo);
            EnsureCustom();
            GlassShapeData.GetBox(m_Level, out _, out _, out float bottom);
            if (!GlassShapeData.IsBoxPreset(m_Level.glassPreset)) bottom = GlassShapeData.DefaultBottomY;
            width = Mathf.Clamp(width, GlassShapeData.MinWidth, GlassShapeData.MaxWidth);
            height = Mathf.Clamp(height, GlassShapeData.MinHeight, GlassShapeData.MaxHeight);
            m_Level.glassPreset = Mathf.Abs(width - height) < 0.12f ? GlassShapePreset.SquareBox : GlassShapePreset.RectangleBox;
            m_Level.contourPoints = GlassShapeData.BuildBoxContour(width, height, bottom);
            m_Level.widthScale = 1f;
            m_Level.heightScale = 1f;
            m_Level.symmetricMode = true;
            m_Level.showShoulderHandles = false;
            if (corner.HasValue) m_Level.boxCornerRadius = Mathf.Clamp(corner.Value, 0.04f, Mathf.Min(0.65f, width * 0.5f, height * 0.5f));
            else m_Level.boxCornerRadius = Mathf.Min(m_Level.boxCornerRadius, Mathf.Min(0.65f, width * 0.5f, height * 0.5f));
            if (frame.HasValue) m_Level.boxFrameThickness = Mathf.Clamp(frame.Value, 0.04f, 0.24f);
            OnEdited(syncScene);
        }

        private void SetContour(List<GlassContourPoint> pts, string undo, bool syncScene = true)
        {
            Record(undo);
            m_Level.useCustomGlassShape = true;
            m_Level.glassPreset = GlassShapePreset.CustomFreeform;
            m_Level.symmetricMode = true;
            m_Level.widthScale = 1f;
            m_Level.heightScale = 1f;
            m_Level.showShoulderHandles = false;
            m_Level.contourPoints = pts;
            OnEdited(syncScene);
        }

        private void ResetToDefault()
        {
            Record("Varsayılan cam");
            m_Level.useCustomGlassShape = false;
            m_Level.glassPreset = GlassShapePreset.RectangleBox;
            m_Level.contourPoints = new List<GlassContourPoint>();
            m_Level.boxCornerRadius = 0.22f;
            m_Level.boxFrameThickness = 0.115f;
            OnEdited();
            ShowNotification(new GUIContent("Level varsayılan havuz camına döndü"));
        }

        private void ConvertBoxToContour()
        {
            GlassShapeData.GetBox(m_Level, out float w, out float h, out float b);
            float hw = w * 0.5f;
            SetContour(new List<GlassContourPoint>
            {
                new GlassContourPoint(b, hw, 0f, 0.3f),
                new GlassContourPoint(b + h * 0.5f, hw, 0f, 0f),
                new GlassContourPoint(b + h, hw, 0f, 0.3f)
            }, "Özel şekle geç");
        }

        // ==================== Kaydet / sahne ====================

        private void Save()
        {
            if (m_Level == null) return;
            AssetDatabase.SaveAssetIfDirty(m_Level);
            RefreshState();
            ShowNotification(new GUIContent("Kaydedildi"));
        }

        /// <summary>Sahneyi her kaydırıcı adımında değil, değişiklik durduktan kısa süre sonra bir kez günceller.</summary>
        private void ScheduleSceneSync()
        {
            m_SceneSyncDue = EditorApplication.timeSinceStartup + 0.6;
        }

        private void OnEditorUpdate()
        {
            if (m_SceneSyncDue > 0 && EditorApplication.timeSinceStartup >= m_SceneSyncDue && m_Drag == Handle.None)
            {
                m_SceneSyncDue = -1;
                ApplyToScene(false);
            }
        }

        private void ApplyToScene(bool force)
        {
            if (m_Level == null) return;
            if (m_Level.monsters == null || m_Level.monsters.Count == 0)
            {
                if (force) ShowNotification(new GUIContent("Boş level sahneye yüklenmez: önce Level Stüdyosu'nda blok ekle"));
                return;
            }
            var gm = FindFirstObjectByType<PuzzleGlassGameManager>();
            if (gm == null)
            {
                if (force) ShowNotification(new GUIContent("Sahnede PuzzleGlassGameManager yok"));
                return;
            }
            if (gm.preserveSceneGlassInPlayMode && force)
                ShowNotification(new GUIContent("Uyarı: GameManager'da 'preserveSceneGlassInPlayMode' açık; Play'de levelin camı uygulanmaz"));
            gm.LoadLevel(m_Level);
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(gm);
                EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            }
            SceneView.RepaintAll();
        }

        // ==================== Yenileme ====================

        private void RefreshAll()
        {
            if (m_Canvas == null) return;
            RefreshState();
            RefreshInspector();
            RefreshIssues();
            m_Canvas.MarkDirtyRepaint();
        }

        private void RefreshState()
        {
            if (m_Title == null) return;
            if (m_Level == null)
            {
                m_Title.text = "Level yok";
                m_State.text = "";
                m_Readout.text = "";
                m_SaveButton.SetEnabled(false);
                return;
            }
            m_Title.text = $"Level {m_Level.levelNumber}  ·  Cam";
            bool dirty = EditorUtility.IsDirty(m_Level);
            m_State.text = dirty ? "● Kaydedilmedi" : "Kaydedildi";
            m_State.EnableInClassList("toolbar__state--dirty", dirty);
            m_SaveButton.SetEnabled(dirty);

            var dims = GlassShapeData.GetDims(m_Level);
            var layout = GlassShapeData.BuildLayout(m_Level);
            if (GlassShapeData.IsBox(m_Level))
            {
                GlassShapeData.GetBox(m_Level, out float w, out float h, out _);
                m_Readout.text = $"Havuz  {w:0.00} × {h:0.00}   ·   köşe {m_Level.boxCornerRadius:0.00}   ·   çerçeve {m_Level.boxFrameThickness:0.000}   ·   {layout.balls.Count} top, çap {layout.diameter:0.000}";
            }
            else
            {
                m_Readout.text = $"Özel şekil   ·   {m_Level.contourPoints.Count} nokta   ·   {layout.balls.Count} top (tahmini)";
            }
        }

        // ---------- Sağ panel ----------

        private void RefreshInspector()
        {
            m_Inspector.Clear();
            if (m_Level == null) return;
            bool box = GlassShapeData.IsBox(m_Level);

            Section("Cam türü");
            var kinds = new VisualElement();
            kinds.AddToClassList("segmented");
            var boxBtn = new Button(() => { if (!GlassShapeData.IsBox(m_Level)) { GlassShapeData.GetBox(null, out float w, out float h, out _); SetBox(w, h, undo: "Havuza geç"); } }) { text = "Havuz (önerilen)" };
            boxBtn.AddToClassList("segmented__item");
            boxBtn.EnableInClassList("segmented__item--active", box);
            kinds.Add(boxBtn);
            var freeBtn = new Button(() => { if (GlassShapeData.IsBox(m_Level)) ConvertBoxToContour(); }) { text = "Özel şekil" };
            freeBtn.tooltip = "Yamuk, altıgen, kapsül gibi şekiller. Taban düz ve geniş olduğu sürece oyunda havuz gibi oynanır.";
            freeBtn.AddToClassList("segmented__item");
            freeBtn.EnableInClassList("segmented__item--active", !box);
            kinds.Add(freeBtn);
            m_Inspector.Add(kinds);

            if (box) BuildBoxInspector();
            else BuildContourInspector();

            Section("Renk teması (cam)");
            var themes = new VisualElement();
            themes.AddToClassList("chip-grid");
            foreach (var (name, theme) in Themes)
            {
                var t = theme;
                var chip = new Button(() =>
                {
                    Record("Cam teması");
                    LevelDesignerWindow.ApplyThemeColorsToLevelData(m_Level, t);
                    OnEdited();
                });
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--active", m_Level.colorTheme == t);
                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.backgroundColor = ThemePreviewColor(t);
                chip.Add(sw);
                chip.Add(new Label(name));
                themes.Add(chip);
            }
            m_Inspector.Add(themes);

            Section("Çerçeve rengi");
            var frames = new VisualElement();
            frames.AddToClassList("chip-grid");
            foreach (var (name, main, high, edge) in FramePalettes)
            {
                Color mc = main, hc = high, ec = edge;
                var chip = new Button(() =>
                {
                    Record("Çerçeve rengi");
                    m_Level.boxFrameColor = mc;
                    m_Level.boxFrameHighlightColor = hc;
                    m_Level.boxFrameEdgeShadeColor = ec;
                    OnEdited();
                });
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--active", Approximately(m_Level.boxFrameColor, mc));
                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.backgroundColor = mc;
                chip.Add(sw);
                chip.Add(new Label(name));
                frames.Add(chip);
            }
            m_Inspector.Add(frames);

            var frameColor = new ColorField("Çerçeve") { value = m_Level.boxFrameColor, showAlpha = false };
            frameColor.RegisterValueChangedCallback(e =>
            {
                Record("Çerçeve rengi");
                m_Level.boxFrameColor = e.newValue;
                m_Level.boxFrameHighlightColor = Color.Lerp(e.newValue, Color.white, 0.68f);
                m_Level.boxFrameEdgeShadeColor = Color.Lerp(e.newValue, Color.black, 0.24f);
                OnEdited();
            });
            m_Inspector.Add(frameColor);
            var bodyColor = new ColorField("Cam") { value = m_Level.glassBodyColor, showAlpha = true };
            bodyColor.RegisterValueChangedCallback(e =>
            {
                Record("Cam rengi");
                m_Level.colorTheme = GlassColorTheme.CustomTheme;
                m_Level.glassBodyColor = e.newValue;
                OnEdited();
            });
            m_Inspector.Add(bodyColor);

            Section("");
            var reset = new Button(ResetToDefault) { text = "Varsayılan cama dön", tooltip = "Level özel cam kullanmaz; oyunun varsayılan havuzu (2.64 × 1.18) yüklenir." };
            reset.AddToClassList("inspector__action");
            m_Inspector.Add(reset);
        }

        private void BuildBoxInspector()
        {
            GlassShapeData.GetBox(m_Level, out float w, out float h, out _);

            Section("Hazır boyutlar");
            var presets = new VisualElement();
            presets.AddToClassList("chip-grid");
            foreach (var p in BoxPresets)
            {
                var pp = p;
                var chip = new Button(() => SetBox(pp.w, pp.h, pp.corner, pp.frame, "Hazır boyut"));
                chip.AddToClassList("chip");
                bool active = Mathf.Abs(w - p.w) < 0.01f && Mathf.Abs(h - p.h) < 0.01f;
                chip.EnableInClassList("chip--active", active);
                chip.Add(BoxIcon(p.w, p.h, p.corner));
                chip.Add(new Label(p.name));
                chip.tooltip = $"{p.w:0.00} × {p.h:0.00}, köşe {p.corner:0.00}";
                presets.Add(chip);
            }
            m_Inspector.Add(presets);

            Section("Ölçüler");
            AddSlider("Genişlik", w, GlassShapeData.MinWidth, GlassShapeData.MaxWidth, v => { GlassShapeData.GetBox(m_Level, out _, out float hh, out _); SetBox(v, hh); });
            AddSlider("Yükseklik", h, GlassShapeData.MinHeight, GlassShapeData.MaxHeight, v => { GlassShapeData.GetBox(m_Level, out float ww, out _, out _); SetBox(ww, v); });
            AddSlider("Köşe", m_Level.boxCornerRadius, 0.04f, 0.65f, v => { GlassShapeData.GetBox(m_Level, out float ww, out float hh, out _); SetBox(ww, hh, corner: v, undo: "Köşe"); });
            AddSlider("Çerçeve", m_Level.boxFrameThickness, 0.04f, 0.24f, v => { GlassShapeData.GetBox(m_Level, out float ww, out float hh, out _); SetBox(ww, hh, frame: v, undo: "Çerçeve kalınlığı"); });

            var hint = new Label("Tepsi camın genişliğine göre hizalanır. Cam daralınca tepsi hücreleri de küçülür.");
            hint.AddToClassList("inspector__hint");
            m_Inspector.Add(hint);
        }

        private void BuildContourInspector()
        {
            if (!GlassShapeData.IsShapedPool(m_Level))
            {
                var warn = new Label("Taban çok dar (genişlik 0.60'tan az): oyun bu camı huni ağızlı şişe gibi işler, toplar deliğin üstünden dümdüz inmez. En alttaki noktayı genişlet.");
                warn.AddToClassList("glass-warning");
                m_Inspector.Add(warn);
            }
            else
            {
                var ok = new Label("Taban düz ve geniş: oyunda havuz gibi oynanır. Toplar her satırda camın genişliğine göre dizilir.");
                ok.AddToClassList("inspector__hint");
                m_Inspector.Add(ok);
            }

            Section("Başlangıç şekilleri");
            var presets = new VisualElement();
            presets.AddToClassList("chip-grid");
            presets.Add(ContourChip("Yamuk", Trapezoid()));
            presets.Add(ContourChip("Altıgen", Hexagon()));
            presets.Add(ContourChip("Dar tüp", SlimTube()));
            m_Inspector.Add(presets);

            Section($"Noktalar ({m_Level.contourPoints.Count})");
            var pts = m_Level.contourPoints;
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                int idx = i;
                var row = new VisualElement();
                row.AddToClassList("inspector__row");
                var label = new Label(i == 0 ? "Alt" : (i == pts.Count - 1 ? "Üst" : $"#{i}"));
                label.AddToClassList("inspector__row-label");
                label.style.width = 36;
                row.Add(label);
                var y = new FloatField { value = (float)System.Math.Round(pts[i].y, 3), isDelayed = true };
                y.style.width = 64;
                y.tooltip = "Yükseklik (y)";
                y.RegisterValueChangedCallback(e => EditPoint(idx, e.newValue, null));
                row.Add(y);
                var hw = new FloatField { value = (float)System.Math.Round(pts[i].halfWidth * 2f, 3), isDelayed = true };
                hw.style.width = 64;
                hw.tooltip = "Genişlik";
                hw.RegisterValueChangedCallback(e => EditPoint(idx, null, e.newValue * 0.5f));
                row.Add(hw);
                if (pts.Count > 3)
                {
                    var del = new Button(() => RemovePoint(idx)) { text = "×", tooltip = "Noktayı sil" };
                    row.Add(del);
                }
                m_Inspector.Add(row);
            }
        }

        private Button ContourChip(string name, List<GlassContourPoint> pts)
        {
            var chip = new Button(() => SetContour(pts, $"Şekil: {name}")) { text = name };
            chip.AddToClassList("chip");
            return chip;
        }

        private void EditPoint(int i, float? y, float? halfWidth)
        {
            var pts = new List<GlassContourPoint>(m_Level.contourPoints);
            if (i < 0 || i >= pts.Count) return;
            var p = pts[i];
            float minY = i > 0 ? pts[i - 1].y + 0.02f : -3f;
            float maxY = i < pts.Count - 1 ? pts[i + 1].y - 0.02f : 3f;
            if (y.HasValue) p.y = Mathf.Clamp(y.Value, minY, maxY);
            if (halfWidth.HasValue) p.halfWidth = Mathf.Clamp(halfWidth.Value, 0.08f, 1.85f);
            pts[i] = p;
            SetContour(pts, "Nokta düzenle");
        }

        private void RemovePoint(int i)
        {
            var pts = new List<GlassContourPoint>(m_Level.contourPoints);
            if (pts.Count <= 3 || i < 0 || i >= pts.Count) return;
            pts.RemoveAt(i);
            SetContour(pts, "Nokta sil");
        }

        private void AddSlider(string label, float value, float min, float max, System.Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = value, showInputField = true };
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            m_Inspector.Add(s);
        }

        private void Section(string title)
        {
            var l = new Label(title);
            l.AddToClassList("inspector__section");
            m_Inspector.Add(l);
        }

        private static VisualElement BoxIcon(float w, float h, float corner)
        {
            float s = 16f / Mathf.Max(w, h);
            var icon = new VisualElement();
            icon.AddToClassList("box-icon");
            icon.style.width = Mathf.Max(5f, w * s);
            icon.style.height = Mathf.Max(5f, h * s);
            float r = Mathf.Min(corner * s, Mathf.Min(w, h) * s * 0.5f);
            icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius = icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = r;
            return icon;
        }

        // ---------- Bulgular ----------

        private void RefreshIssues()
        {
            m_Issues.Clear();
            if (m_Level == null) return;
            var findings = new List<(FindingSeverity sev, string text)>();
            bool box = GlassShapeData.IsBox(m_Level);
            var dims = GlassShapeData.GetDims(m_Level);
            var layout = GlassShapeData.BuildLayout(m_Level);

            if (!GlassShapeData.IsPlayablePool(m_Level))
                findings.Add((FindingSeverity.Critical, "Camın tabanı çok dar: oyun bunu huni ağızlı şişe gibi işler, toplar deliğin üstünden dümdüz inmez. En alttaki noktanın genişliğini 0.60'ın üstüne çıkar."));

            float topOfBalls = float.MinValue;
            foreach (var b in layout.balls) topOfBalls = Mathf.Max(topOfBalls, b.localPos.y + layout.diameter * 0.5f);
            if (layout.balls.Count > 0 && topOfBalls > dims.ceilY + 0.01f)
                findings.Add((FindingSeverity.Critical, $"Toplar cama sığmıyor: en üstteki top camın {topOfBalls - dims.ceilY:0.00} üstüne taşıyor. Camı büyüt ya da Level Stüdyosu'nda top sayısını azalt."));
            else if (layout.balls.Count > 0 && layout.diameter < 0.15f)
                findings.Add((FindingSeverity.Warning, $"Toplar küçük görünecek (çap {layout.diameter:0.000}). Camı büyütmek topları büyütür."));

            if (layout.balls.Count > 0 && topOfBalls < dims.floorY + (dims.ceilY - dims.floorY) * 0.45f)
                findings.Add((FindingSeverity.Info, "Cam yarıdan fazla boş kalıyor; daha alçak bir cam daha dolu görünür."));

            if (box && m_Level.boxCornerRadius > Mathf.Min(dims.innerHalfW, (dims.ceilY - dims.floorY) * 0.5f) - 0.01f)
                findings.Add((FindingSeverity.Info, "Köşe yuvarlaklığı en yüksek değerde: cam kapsül gibi görünür."));

            if (!m_Level.useCustomGlassShape)
                findings.Add((FindingSeverity.Info, "Bu level varsayılan havuz camını kullanıyor. Bir ölçüyü değiştirince levele özel cam oluşur."));

            if (m_Level.monsters == null || m_Level.monsters.Count == 0)
                findings.Add((FindingSeverity.Info, "Levelde blok yok; önizlemede top görünmez. Blokları Level Stüdyosu'nda ekle."));

            if (findings.Count == 0) { m_Issues.Add(new Label("Bu cam için bulgu yok.")); return; }
            foreach (var (sev, text) in findings)
            {
                var row = new VisualElement();
                row.AddToClassList("issue");
                row.AddToClassList(sev == FindingSeverity.Critical ? "issue--critical" : sev == FindingSeverity.Warning ? "issue--warning" : "issue--info");
                var tag = new Label(sev == FindingSeverity.Critical ? "KRİTİK" : sev == FindingSeverity.Warning ? "UYARI" : "BİLGİ");
                tag.AddToClassList("issue__tag");
                row.Add(tag);
                var t = new Label(text);
                t.AddToClassList("issue__text");
                row.Add(t);
                m_Issues.Add(row);
            }
        }

        // ==================== Önizleme çizimi ====================

        private Vector2 ToPx(float x, float y) => new Vector2(m_Origin.x + x * m_Scale, m_Origin.y - y * m_Scale);
        private Vector2 ToWorld(Vector2 px) => new Vector2((px.x - m_Origin.x) / m_Scale, (m_Origin.y - px.y) / m_Scale);

        /// <summary>Görünümü camın ve tepsinin tamamı sığacak şekilde ayarlar (sürüklerken sabit kalır).</summary>
        private void FitView(Rect rect, PuzzleGlassLevelData lvl)
        {
            if (m_ViewFrozen) return;
            var contour = GlassShapeData.GetContour(lvl);
            float maxHw = 0f;
            foreach (var p in contour) maxHw = Mathf.Max(maxHw, p.halfWidth);
            float frame = lvl.boxFrameThickness;
            float top = contour[contour.Count - 1].y + frame;
            float bottom = contour[0].y - frame;
            float trayH = TrayHeight(lvl, maxHw);
            float viewBottom = bottom - 0.11f - trayH - 0.15f;
            float viewTop = top + 0.25f;
            float viewHalfW = Mathf.Max(maxHw + frame + 0.3f, 1.2f);
            float sx = rect.width / (viewHalfW * 2f);
            float sy = rect.height / (viewTop - viewBottom);
            m_Scale = Mathf.Max(10f, Mathf.Min(sx, sy) * 0.92f);
            m_Origin = new Vector2(rect.width * 0.5f, rect.height * 0.5f + (viewTop + viewBottom) * 0.5f * m_Scale);
        }

        private static float TrayHeight(PuzzleGlassLevelData lvl, float glassHalfW)
        {
            float cell = glassHalfW * 2f / Mathf.Max(1, lvl.stagingGridCols);
            return cell * lvl.stagingGridRows;
        }

        private void DrawCanvas(MeshGenerationContext ctx)
        {
            var lvl = m_Level;
            var rect = m_Canvas.contentRect;
            if (lvl == null || rect.width < 10 || rect.height < 10) return;
            FitView(rect, lvl);
            var p = ctx.painter2D;
            bool box = GlassShapeData.IsBox(lvl);
            var contour = GlassShapeData.GetContour(lvl);
            float frame = lvl.boxFrameThickness;
            Color frameColor = lvl.boxFrameColor.a > 0.01f ? lvl.boxFrameColor : new Color(0.5f, 0.5f, 0.9f);
            Color body = lvl.glassBodyColor;
            body.a = Mathf.Max(0.35f, body.a * 0.6f);

            // Hafif ızgara
            p.strokeColor = new Color(1f, 1f, 1f, 0.04f);
            p.lineWidth = 1f;
            for (float gx = -3f; gx <= 3f; gx += 0.5f)
            {
                var a = ToPx(gx, -6f); var b = ToPx(gx, 4f);
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
            }
            for (float gy = -6f; gy <= 4f; gy += 0.5f)
            {
                var a = ToPx(-4f, gy); var b = ToPx(4f, gy);
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
            }

            float maxHw = 0f;
            foreach (var c in contour) maxHw = Mathf.Max(maxHw, c.halfWidth);

            if (box)
            {
                float hw = contour[0].halfWidth;
                float bottom = contour[0].y, top = contour[contour.Count - 1].y;
                float r = Mathf.Min(lvl.boxCornerRadius, Mathf.Min(hw, (top - bottom) * 0.5f));
                // Gölge, çerçeve, cam
                RoundedRect(p, -hw - frame + 0.04f, bottom - frame - 0.06f, hw + frame + 0.04f, top + frame - 0.06f, r + frame, new Color(0f, 0f, 0f, 0.25f));
                RoundedRect(p, -hw - frame, bottom - frame, hw + frame, top + frame, r + frame, frameColor);
                RoundedRect(p, -hw, bottom, hw, top, r, Color.Lerp(new Color(0.12f, 0.14f, 0.18f), body, body.a));
            }
            else
            {
                // Kontur: sağ taraf alttan üste, sol taraf üstten alta
                p.fillColor = Color.Lerp(new Color(0.12f, 0.14f, 0.18f), body, body.a);
                p.strokeColor = frameColor;
                p.lineWidth = Mathf.Max(2f, frame * m_Scale);
                p.BeginPath();
                for (int i = 0; i < contour.Count; i++)
                {
                    var pt = ToPx(contour[i].centerX + contour[i].halfWidth, contour[i].y);
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                for (int i = contour.Count - 1; i >= 0; i--)
                    p.LineTo(ToPx(contour[i].centerX - contour[i].halfWidth, contour[i].y));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            }

            // Toplar (oyundaki dizilim)
            var dims = GlassShapeData.GetDims(lvl);
            var layout = GlassShapeData.BuildLayout(lvl);
            float rad = layout.diameter * 0.5f * m_Scale;
            foreach (var b in layout.balls)
            {
                var c = ToPx(b.localPos.x, b.localPos.y);
                p.fillColor = PuzzleColorPalette.GetBodyColor(b.color);
                p.BeginPath();
                p.Arc(c, rad * 0.94f, 0f, 360f);
                p.Fill();
                p.fillColor = new Color(1f, 1f, 1f, 0.35f);
                p.BeginPath();
                p.Arc(c + new Vector2(-rad * 0.3f, -rad * 0.3f), rad * 0.28f, 0f, 360f);
                p.Fill();
            }

            // Tepsi (hizalama için silik)
            {
                float trayW = maxHw * 2f;
                float cell = trayW / Mathf.Max(1, lvl.stagingGridCols);
                float trayTop = contour[0].y - frame - 0.11f;
                float trayBottom = trayTop - cell * lvl.stagingGridRows;
                RoundedRect(p, -maxHw - 0.06f, trayBottom - 0.06f, maxHw + 0.06f, trayTop + 0.06f, 0.18f, new Color(0.36f, 0.36f, 0.68f, 0.55f));
                for (int x = 0; x < lvl.stagingGridCols; x++)
                    for (int y = 0; y < lvl.stagingGridRows; y++)
                    {
                        float x0 = -maxHw + x * cell + 0.03f, y0 = trayBottom + y * cell + 0.03f;
                        RoundedRect(p, x0, y0, x0 + cell - 0.06f, y0 + cell - 0.06f, 0.05f, new Color(0.23f, 0.23f, 0.40f, 0.8f));
                    }
            }

            // Tutamaçlar
            if (box)
            {
                float hw = contour[0].halfWidth, bottom = contour[0].y, top = contour[contour.Count - 1].y;
                DrawHandle(p, HandlePos(Handle.Width, -1), Handle.Width, -1);
                DrawHandle(p, HandlePos(Handle.Height, -1), Handle.Height, -1);
                DrawHandle(p, HandlePos(Handle.Corner, -1), Handle.Corner, -1);
                // Ölçü çizgileri
                p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
                p.lineWidth = 1f;
                var a = ToPx(-hw, bottom - frame - 0.05f); var bb = ToPx(hw, bottom - frame - 0.05f);
                p.BeginPath(); p.MoveTo(a); p.LineTo(bb); p.Stroke();
            }
            else
            {
                for (int i = 0; i < contour.Count; i++) DrawHandle(p, HandlePos(Handle.Point, i), Handle.Point, i);
            }
        }

        private void RoundedRect(Painter2D p, float x0, float y0, float x1, float y1, float r, Color fill)
        {
            var tl = ToPx(x0, y1); var br = ToPx(x1, y0);
            float rp = Mathf.Max(0f, Mathf.Min(r * m_Scale, Mathf.Min(br.x - tl.x, br.y - tl.y) * 0.5f));
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(tl.x + rp, tl.y));
            p.LineTo(new Vector2(br.x - rp, tl.y));
            p.ArcTo(new Vector2(br.x, tl.y), new Vector2(br.x, tl.y + rp), rp);
            p.LineTo(new Vector2(br.x, br.y - rp));
            p.ArcTo(new Vector2(br.x, br.y), new Vector2(br.x - rp, br.y), rp);
            p.LineTo(new Vector2(tl.x + rp, br.y));
            p.ArcTo(new Vector2(tl.x, br.y), new Vector2(tl.x, br.y - rp), rp);
            p.LineTo(new Vector2(tl.x, tl.y + rp));
            p.ArcTo(new Vector2(tl.x, tl.y), new Vector2(tl.x + rp, tl.y), rp);
            p.ClosePath();
            p.Fill();
        }

        private void DrawHandle(Painter2D p, Vector2 pos, Handle h, int point)
        {
            bool active = m_Drag == h && (h != Handle.Point || m_DragPoint == point);
            bool hover = m_Hover == h && (h != Handle.Point || m_HoverPoint == point);
            p.fillColor = active ? new Color(0.35f, 0.66f, 0.94f) : (hover ? Color.white : new Color(0.92f, 0.93f, 0.96f));
            p.strokeColor = new Color(0.10f, 0.11f, 0.14f);
            p.lineWidth = 2f;
            p.BeginPath();
            p.Arc(pos, active || hover ? HandleRadius + 2f : HandleRadius, 0f, 360f);
            p.Fill();
            p.Stroke();
        }

        private Vector2 HandlePos(Handle h, int point)
        {
            var contour = GlassShapeData.GetContour(m_Level);
            float hw = contour[0].halfWidth, bottom = contour[0].y, top = contour[contour.Count - 1].y;
            switch (h)
            {
                case Handle.Width: return ToPx(hw, (bottom + top) * 0.5f);
                case Handle.Height: return ToPx(0f, top);
                case Handle.Corner:
                    float r = Mathf.Min(m_Level.boxCornerRadius, Mathf.Min(hw, (top - bottom) * 0.5f));
                    return ToPx(-hw + r, top);
                case Handle.Point:
                    var c = contour[point];
                    return ToPx(c.centerX + c.halfWidth, c.y);
            }
            return Vector2.zero;
        }

        private (Handle, int) HitTest(Vector2 local)
        {
            if (m_Level == null) return (Handle.None, -1);
            float best = HandleRadius + 6f;
            var result = (Handle.None, -1);
            if (GlassShapeData.IsBox(m_Level))
            {
                foreach (var h in new[] { Handle.Corner, Handle.Height, Handle.Width })
                {
                    float d = Vector2.Distance(local, HandlePos(h, -1));
                    if (d < best) { best = d; result = (h, -1); }
                }
            }
            else
            {
                var contour = GlassShapeData.GetContour(m_Level);
                for (int i = 0; i < contour.Count; i++)
                {
                    float d = Vector2.Distance(local, HandlePos(Handle.Point, i));
                    if (d < best) { best = d; result = (Handle.Point, i); }
                }
            }
            return result;
        }

        // ==================== Fare ====================

        private void OnPointerDown(PointerDownEvent e)
        {
            if (m_Level == null) return;
            Vector2 local = e.localPosition;
            var (h, pt) = HitTest(local);

            if (e.button == 1 && h == Handle.Point)
            {
                RemovePoint(pt);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;

            if (h == Handle.None)
            {
                // Özel şekilde çift tık: o yüksekliğe nokta ekle
                if (e.clickCount == 2 && !GlassShapeData.IsBox(m_Level)) AddPointAt(ToWorld(local));
                return;
            }
            m_Drag = h;
            m_DragPoint = pt;
            m_ViewFrozen = true;
            Record("Cam tutamacı");
            EnsureCustom();
            m_Canvas.CapturePointer(e.pointerId);
            m_Canvas.MarkDirtyRepaint();
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (m_Level == null) return;
            Vector2 local = e.localPosition;
            if (m_Drag == Handle.None)
            {
                var (h, pt) = HitTest(local);
                if (h != m_Hover || pt != m_HoverPoint)
                {
                    m_Hover = h;
                    m_HoverPoint = pt;
                    m_Canvas.MarkDirtyRepaint();
                }
                return;
            }

            var w = ToWorld(local);
            if (m_Drag == Handle.Point)
            {
                var pts = new List<GlassContourPoint>(m_Level.contourPoints);
                if (m_DragPoint < 0 || m_DragPoint >= pts.Count) return;
                var p = pts[m_DragPoint];
                float minY = m_DragPoint > 0 ? pts[m_DragPoint - 1].y + 0.02f : -3f;
                float maxY = m_DragPoint < pts.Count - 1 ? pts[m_DragPoint + 1].y - 0.02f : 3f;
                p.y = Mathf.Clamp(Snap(w.y, e.shiftKey), minY, maxY);
                p.halfWidth = Mathf.Clamp(Snap(Mathf.Abs(w.x), e.shiftKey), 0.08f, 1.85f);
                pts[m_DragPoint] = p;
                m_Level.contourPoints = pts;
                m_Level.glassPreset = GlassShapePreset.CustomFreeform;
            }
            else
            {
                GlassShapeData.GetBox(m_Level, out float bw, out float bh, out float bottom);
                switch (m_Drag)
                {
                    case Handle.Width: bw = Snap(Mathf.Abs(w.x) * 2f, e.shiftKey); break;
                    case Handle.Height: bh = Snap(w.y - bottom, e.shiftKey); break;
                    case Handle.Corner:
                        m_Level.boxCornerRadius = Mathf.Clamp(Snap(w.x + bw * 0.5f, e.shiftKey), 0.04f, Mathf.Min(0.65f, bw * 0.5f, bh * 0.5f));
                        break;
                }
                bw = Mathf.Clamp(bw, GlassShapeData.MinWidth, GlassShapeData.MaxWidth);
                bh = Mathf.Clamp(bh, GlassShapeData.MinHeight, GlassShapeData.MaxHeight);
                m_Level.contourPoints = GlassShapeData.BuildBoxContour(bw, bh, bottom);
                m_Level.glassPreset = Mathf.Abs(bw - bh) < 0.12f ? GlassShapePreset.SquareBox : GlassShapePreset.RectangleBox;
                m_Level.boxCornerRadius = Mathf.Min(m_Level.boxCornerRadius, Mathf.Min(0.65f, bw * 0.5f, bh * 0.5f));
            }
            EditorUtility.SetDirty(m_Level);
            RefreshState();
            m_Canvas.MarkDirtyRepaint();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (m_Drag == Handle.None) return;
            m_Drag = Handle.None;
            m_DragPoint = -1;
            m_ViewFrozen = false;
            if (m_Canvas.HasPointerCapture(e.pointerId)) m_Canvas.ReleasePointer(e.pointerId);
            OnEdited();
        }

        /// <summary>Shift basılıyken 0.05'lik adımlara oturtur.</summary>
        private static float Snap(float v, bool snap) => snap ? Mathf.Round(v / 0.05f) * 0.05f : v;

        private void AddPointAt(Vector2 world)
        {
            var pts = new List<GlassContourPoint>(m_Level.contourPoints);
            int insert = pts.Count;
            for (int i = 0; i < pts.Count; i++) if (pts[i].y > world.y) { insert = i; break; }
            insert = Mathf.Clamp(insert, 1, pts.Count - 1);
            pts.Insert(insert, new GlassContourPoint(world.y, Mathf.Clamp(Mathf.Abs(world.x), 0.08f, 1.85f), 0f, 0f));
            SetContour(pts, "Nokta ekle");
        }

        // ==================== Hazır konturlar ====================

        private static List<GlassContourPoint> Trapezoid() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.90f, 0f, 0.3f),
            new GlassContourPoint( 0.20f, 1.20f, 0f, 0f),
            new GlassContourPoint( 0.62f, 1.40f, 0f, 0.3f)
        };

        private static List<GlassContourPoint> Hexagon() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.80f, 0f, 0.3f),
            new GlassContourPoint( 0.03f, 1.32f, 0f, 0f),
            new GlassContourPoint( 0.62f, 0.80f, 0f, 0.3f)
        };

        private static List<GlassContourPoint> SlimTube() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.62f, 0f, 0.3f),
            new GlassContourPoint( 0.60f, 0.62f, 0f, 0f),
            new GlassContourPoint( 1.40f, 0.62f, 0f, 0.3f)
        };

        // ==================== Yardımcılar ====================

        private static Color ThemePreviewColor(GlassColorTheme t)
        {
            switch (t)
            {
                case GlassColorTheme.CrystalClear: return new Color(0.85f, 0.92f, 0.98f);
                case GlassColorTheme.EmeraldGreen: return new Color(0.30f, 0.80f, 0.58f);
                case GlassColorTheme.RoseQuartz: return new Color(0.95f, 0.60f, 0.72f);
                case GlassColorTheme.AmethystPurple: return new Color(0.66f, 0.50f, 0.92f);
                case GlassColorTheme.AmberGold: return new Color(0.96f, 0.74f, 0.30f);
                default: return new Color(0.35f, 0.70f, 0.98f);
            }
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
    }
}
