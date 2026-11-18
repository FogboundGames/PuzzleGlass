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

        private enum Handle { None, Width, Height, Corner, Point, TrackPoint }

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
        private int m_DragBranch = -1;
        private Handle m_Hover = Handle.None;
        private int m_HoverPoint = -1;
        private int m_HoverBranch = -1;
        private bool m_TrackSymmetricEdit = true;

        private ListView m_List;
        private Label m_Title, m_State, m_Readout;
        private Button m_SaveButton;
        private VisualElement m_Canvas, m_Inspector;
        private ScrollView m_Issues;

        private static readonly (string name, float w, float h, float corner, float frame)[] BoxPresets =
        {
            ("Varsayılan", 2.64f, 1.18f, 0.28f, 0.095f),
            ("Kompakt", 2.70f, 0.62f, 0.22f, 0.085f),
            ("Kavanoz", 2.40f, 1.38f, 0.36f, 0.095f),
            ("Geniş", 2.92f, 0.96f, 0.24f, 0.090f),
            ("Kare", 1.60f, 1.60f, 0.28f, 0.095f),
            ("Yüksek", 1.50f, 1.95f, 0.28f, 0.095f),
            ("Dar uzun", 1.20f, 2.30f, 0.32f, 0.095f),
            ("Kapsül", 2.40f, 1.24f, 0.60f, 0.095f)
        };

        private static readonly (string name, Color main, Color high, Color edge)[] FramePalettes =
        {
            ("Lavanta", new Color(0.49f, 0.44f, 0.90f), new Color(0.94f, 0.94f, 1.00f), new Color(0.35f, 0.32f, 0.78f)),
            ("Gök mavi", new Color(0.28f, 0.62f, 0.92f), new Color(0.86f, 0.94f, 1.00f), new Color(0.18f, 0.44f, 0.76f)),
            ("Nane", new Color(0.30f, 0.76f, 0.62f), new Color(0.86f, 0.98f, 0.92f), new Color(0.18f, 0.56f, 0.46f)),
            ("Mercan", new Color(0.90f, 0.48f, 0.52f), new Color(0.99f, 0.88f, 0.90f), new Color(0.70f, 0.32f, 0.38f)),
            ("Arduvaz", new Color(0.32f, 0.36f, 0.48f), new Color(0.82f, 0.86f, 0.94f), new Color(0.20f, 0.22f, 0.32f))
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
            if (!lvl.useCustomGlassShape) kind = "Varsayılan pist";
            else if (GlassShapeData.IsTrack(lvl)) kind = $"Pist ({TrackPresetLabel(lvl.trackPreset)})";
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
            // Varsayılan cam şişe tabanlıdır: ilk düzenlemede aynı şişeden başla
            m_Level.useCustomGlassShape = true;
            m_Level.glassIsBottle = true;
            m_Level.glassPreset = GlassShapePreset.CustomFreeform;
            m_Level.symmetricMode = true;
            m_Level.widthScale = 1f;
            m_Level.heightScale = 1f;
            m_Level.showShoulderHandles = false;
            m_Level.contourPoints = ShapedGlassContainer.BuildAutoBottleContour(m_Level.bottomToTopGroups);
        }

        /// <summary>Şişe: taban sabit şişe omuzları; genişlik (yanlar) ve yükseklik (üst) ayarlı.</summary>
        private void SetBottle(float width, float height, string undo = "Şişe ölçüsü", bool syncScene = true)
        {
            Record(undo);
            m_Level.useCustomGlassShape = true;
            m_Level.glassIsBottle = true;
            m_Level.glassPreset = GlassShapePreset.CustomFreeform;
            m_Level.symmetricMode = true;
            m_Level.widthScale = 1f;
            m_Level.heightScale = 1f;
            m_Level.showShoulderHandles = false;
            m_Level.contourPoints = ShapedGlassContainer.BuildBottleContour(width, height);
            OnEdited(syncScene);
        }

        private const float BottleMinW = 1.20f, BottleMaxW = 3.40f, BottleMinH = 0.55f, BottleMaxH = 2.80f;

        private void SetBox(float width, float height, float? corner = null, float? frame = null, string undo = "Cam ölçüsü", bool syncScene = true)
        {
            Record(undo);
            EnsureCustom();
            GlassShapeData.GetBox(m_Level, out _, out _, out float bottom);
            if (!GlassShapeData.IsBoxPreset(m_Level.glassPreset)) bottom = GlassShapeData.DefaultBottomY;
            width = Mathf.Clamp(width, GlassShapeData.MinWidth, GlassShapeData.MaxWidth);
            height = Mathf.Clamp(height, GlassShapeData.MinHeight, GlassShapeData.MaxHeight);
            m_Level.glassPreset = Mathf.Abs(width - height) < 0.12f ? GlassShapePreset.SquareBox : GlassShapePreset.RectangleBox;
            m_Level.glassIsBottle = false;
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
            m_Level.glassIsBottle = false;
            m_Level.glassPreset = GlassShapePreset.CustomFreeform;
            m_Level.symmetricMode = true;
            m_Level.widthScale = 1f;
            m_Level.heightScale = 1f;
            m_Level.showShoulderHandles = false;
            m_Level.contourPoints = pts;
            OnEdited(syncScene);
        }

        private static string TrackPresetLabel(GlassTrackPreset p)
        {
            switch (p)
            {
                case GlassTrackPreset.ArchLoop: return "U";
                case GlassTrackPreset.HeartLoop: return "Kalp";
                case GlassTrackPreset.UChannel: return "Stadyum";
                case GlassTrackPreset.ORing: return "Oval Halka";
                case GlassTrackPreset.SCurve: return "Fasulye";
                case GlassTrackPreset.DoubleArch: return "Kare";
                case GlassTrackPreset.OvalRing: return "Oval Halka";
                default: return "Özel Pist";
            }
        }

        private void SetTrackPreset(GlassTrackPreset preset, string undo = "Pist şekli")
        {
            Record(undo);
            m_Level.useCustomGlassShape = true;
            m_Level.glassIsBottle = false;
            m_Level.glassPreset = GlassShapePreset.TrackChannel;
            m_Level.trackPreset = preset;
            if (preset != GlassTrackPreset.Custom)
            {
                m_Level.trackBranches = GlassTrackLayout.BuildPresetBranches(preset);
            }
            else if (m_Level.trackBranches == null || m_Level.trackBranches.Count == 0)
            {
                m_Level.trackBranches = GlassTrackLayout.BuildPresetBranches(GlassTrackPreset.ArchLoop);
            }
            if (m_Level.trackWallThickness < 0.04f) m_Level.trackWallThickness = 0.082f;
            if (m_Level.trackScale < 0.5f) m_Level.trackScale = 1.0f;
            OnEdited();
        }

        private void EnsureEditableTrackBranches()
        {
            if (m_Level.useCustomGlassShape && m_Level.glassPreset == GlassShapePreset.TrackChannel &&
                m_Level.trackBranches != null && m_Level.trackBranches.Count > 0)
                return;
            m_Level.useCustomGlassShape = true;
            m_Level.glassIsBottle = false;
            m_Level.glassPreset = GlassShapePreset.TrackChannel;
            GlassTrackPreset srcPreset = m_Level.trackPreset == GlassTrackPreset.Custom ? GlassTrackPreset.ArchLoop : m_Level.trackPreset;
            m_Level.trackBranches = GlassTrackLayout.BuildPresetBranches(srcPreset);
        }

        private void ResetToDefault()
        {
            Record("Varsayılan cam");
            m_Level.useCustomGlassShape = false;
            m_Level.glassIsBottle = false;
            m_Level.glassPreset = GlassShapePreset.TrackChannel;
            m_Level.trackPreset = GlassTrackPreset.ArchLoop;
            m_Level.trackLanesAcross = 0;
            m_Level.trackAutoFitToBalls = true;
            m_Level.trackScale = 1.0f;
            m_Level.trackWallThickness = 0.082f;
            m_Level.trackBranches = new List<GlassTrackBranch>();
            m_Level.contourPoints = new List<GlassContourPoint>();
            m_Level.boxCornerRadius = 0.28f;
            m_Level.boxFrameThickness = 0.055f;
            OnEdited();
            ShowNotification(new GUIContent("Level varsayılan cam pistine (Kemer) döndü"));
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
            if (GlassShapeData.IsTrack(m_Level))
            {
                GlassShapeData.BuildTrack(m_Level, out var compiled, out _);
                int lanes = compiled != null ? compiled.lanesAcross : 4;
                m_Readout.text = $"Pist ({TrackPresetLabel(m_Level.useCustomGlassShape ? m_Level.trackPreset : GlassTrackPreset.ArchLoop)})   ·   {lanes} şerit   ·   {layout.balls.Count} top, çap {layout.diameter:0.000}" + (m_Level.useCustomGlassShape ? "" : "   ·   varsayılan");
            }
            else if (GlassShapeData.IsBottle(m_Level))
            {
                GlassShapeData.GetBottle(m_Level, out float bw, out float bh);
                m_Readout.text = $"Şişe  {bw:0.00} × {bh:0.00}   ·   köşe {m_Level.boxCornerRadius:0.00}   ·   {layout.balls.Count} top, çap {layout.diameter:0.000}" + (m_Level.useCustomGlassShape ? "" : "   ·   varsayılan");
            }
            else if (GlassShapeData.IsBox(m_Level))
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
            bool track = GlassShapeData.IsTrack(m_Level);
            bool bottle = !track && GlassShapeData.IsBottle(m_Level);
            bool box = !track && !bottle && GlassShapeData.IsBox(m_Level);

            Section("Cam türü");
            var kinds = new VisualElement();
            kinds.AddToClassList("segmented");

            var trackBtn = new Button(() =>
            {
                if (GlassShapeData.IsTrack(m_Level)) return;
                SetTrackPreset(m_Level.trackPreset == GlassTrackPreset.Custom ? GlassTrackPreset.ArchLoop : m_Level.trackPreset, "Cam pistine geç");
            }) { text = "Pist (önerilen)", tooltip = "Görsel 1 & 3 tarzı eğrisel cam kanal/pist; toplar sıkı şeritler halinde akar." };
            trackBtn.AddToClassList("segmented__item");
            trackBtn.EnableInClassList("segmented__item--active", track);
            kinds.Add(trackBtn);

            var bottleBtn = new Button(() =>
            {
                if (GlassShapeData.IsBottle(m_Level)) return;
                float w = ShapedGlassContainer.DefaultBottleWidth, h = ShapedGlassContainer.ComputeAutoBottleHeight(m_Level.bottomToTopGroups);
                if (GlassShapeData.IsBox(m_Level)) GlassShapeData.GetBox(m_Level, out w, out h, out _);
                SetBottle(Mathf.Clamp(w, BottleMinW, BottleMaxW), Mathf.Clamp(h, BottleMinH, BottleMaxH), "Şişeye geç");
            }) { text = "Şişe", tooltip = "Taban şişe gibi yuvarlak omuzlarla yakaya iner; yanları ve üstü ayarlanır." };
            bottleBtn.AddToClassList("segmented__item");
            bottleBtn.EnableInClassList("segmented__item--active", bottle);
            kinds.Add(bottleBtn);
            var boxBtn = new Button(() => { if (!GlassShapeData.IsBox(m_Level) || bottle || track) { SetBox(2.64f, 1.18f, undo: "Havuza geç"); } }) { text = "Havuz" };
            boxBtn.AddToClassList("segmented__item");
            boxBtn.EnableInClassList("segmented__item--active", box);
            kinds.Add(boxBtn);
            var freeBtn = new Button(() =>
            {
                if (GlassShapeData.IsBox(m_Level) && !bottle) ConvertBoxToContour();
                else if (bottle || track) SetContour(new List<GlassContourPoint>(GlassShapeData.GetContour(m_Level)), "Özel şekle geç");
            }) { text = "Özel şekil" };
            freeBtn.tooltip = "Yamuk, altıgen, kapsül gibi şekiller. Taban düz ve geniş olduğu sürece oyunda havuz gibi oynanır.";
            freeBtn.AddToClassList("segmented__item");
            freeBtn.EnableInClassList("segmented__item--active", !track && !box && !bottle);
            kinds.Add(freeBtn);
            m_Inspector.Add(kinds);

            if (track) BuildTrackInspector();
            else if (bottle) BuildBottleInspector();
            else if (box) BuildBoxInspector();
            else BuildContourInspector();

            BuildFeedTubesInspector();
            BuildTrayInspector();

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

            Section("Cam vurgu / Yaka rengi");
            var frames = new VisualElement();
            frames.AddToClassList("chip-grid");
            foreach (var (name, main, high, edge) in FramePalettes)
            {
                Color mc = main, hc = high, ec = edge;
                var chip = new Button(() =>
                {
                    Record("Cam vurgu ve yaka rengi");
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

            var frameColor = new ColorField("Yaka / Vurgu") { value = m_Level.boxFrameColor, showAlpha = false };
            frameColor.RegisterValueChangedCallback(e =>
            {
                Record("Cam vurgu rengi");
                m_Level.boxFrameColor = e.newValue;
                m_Level.boxFrameHighlightColor = Color.Lerp(e.newValue, Color.white, 0.78f);
                m_Level.boxFrameEdgeShadeColor = Color.Lerp(e.newValue, Color.black, 0.24f);
                OnEdited();
            });
            m_Inspector.Add(frameColor);
            var bodyColor = new ColorField("Cam tonu") { value = m_Level.glassBodyColor, showAlpha = true };
            bodyColor.RegisterValueChangedCallback(e =>
            {
                Record("Cam rengi");
                m_Level.colorTheme = GlassColorTheme.CustomTheme;
                m_Level.glassBodyColor = e.newValue;
                OnEdited();
            });
            m_Inspector.Add(bodyColor);

            Section("");
            var reset = new Button(ResetToDefault) { text = "Varsayılan cama dön", tooltip = "Level özel cam kullanmaz; oyunun varsayılan kristal havuzu (2.64 × 1.18) yüklenir." };
            reset.AddToClassList("inspector__action");
            m_Inspector.Add(reset);
        }

        private void BuildFeedTubesInspector()
        {
            Section("Üst Besleme Cam Tüpleri (Bu Levele Özel)");

            var modes = new VisualElement();
            modes.AddToClassList("segmented");

            var offBtn = new Button(() =>
            {
                if (!m_Level.useBallFeedTubes) return;
                Record("Üst besleme tüplerini kapat");
                m_Level.useBallFeedTubes = false;
                OnEdited();
            }) { text = "Kapalı", tooltip = "Bu levelde üst besleme tüpü olmaz; tüm toplar cam haznede başlar." };
            offBtn.AddToClassList("segmented__item");
            offBtn.EnableInClassList("segmented__item--active", !m_Level.useBallFeedTubes);
            modes.Add(offBtn);

            AddTubeModeBtn(modes, "İki Yan", BallFeedTubeMode.BothLeftAndRight);
            AddTubeModeBtn(modes, "Sol", BallFeedTubeMode.LeftOnly);
            AddTubeModeBtn(modes, "Sağ", BallFeedTubeMode.RightOnly);
            m_Inspector.Add(modes);

            if (!m_Level.useBallFeedTubes)
            {
                var offHint = new Label("İki Yan, Sol veya Sağ seçtiğinde yalnızca bu levele özel kıvrımlı üst cam borular açılır; toplar sırayla hazneye akar.");
                offHint.AddToClassList("inspector__hint");
                m_Inspector.Add(offHint);
                return;
            }

            var layout = GlassShapeData.BuildLayout(m_Level);
            int total = layout.balls != null ? layout.balls.Count : 0;
            int effLimit = BallFeedTubeUtility.GetEffectiveContainerLimit(m_Level.feedTubeContainerLimit, total);
            int queued = Mathf.Max(0, total - effLimit);

            var limitRow = new VisualElement();
            limitRow.AddToClassList("inspector__row");
            var limitLbl = new Label("Haznede başlayan top");
            limitLbl.AddToClassList("inspector__row-label");
            limitRow.Add(limitLbl);
            limitRow.Add(new Button(() =>
            {
                Record("Tüp hazne limiti");
                m_Level.feedTubeContainerLimit = Mathf.Clamp(m_Level.feedTubeContainerLimit - 1, 0, 60);
                OnEdited();
            }) { text = "−" });
            var limitField = new IntegerField { value = m_Level.feedTubeContainerLimit, isDelayed = true };
            limitField.style.width = 42;
            limitField.RegisterValueChangedCallback(e =>
            {
                Record("Tüp hazne limiti");
                m_Level.feedTubeContainerLimit = Mathf.Clamp(e.newValue, 0, 60);
                OnEdited();
            });
            limitRow.Add(limitField);
            limitRow.Add(new Button(() =>
            {
                Record("Tüp hazne limiti");
                m_Level.feedTubeContainerLimit = Mathf.Clamp(m_Level.feedTubeContainerLimit + 1, 0, 60);
                OnEdited();
            }) { text = "+" });
            limitRow.Add(new Button(() =>
            {
                Record("Otomatik hazne limiti");
                m_Level.feedTubeContainerLimit = 0;
                OnEdited();
            }) { text = "Oto", tooltip = "0 = toplam topların yaklaşık yarısı haznede, kalanı tüplerde başlar." });
            m_Inspector.Add(limitRow);

            var splitInfo = new Label($"Başlangıç: {effLimit} top haznede  ·  {queued} top üst tüplerde");
            splitInfo.AddToClassList("inspector__hint");
            m_Inspector.Add(splitInfo);

            var visRow = new VisualElement();
            visRow.AddToClassList("inspector__row");
            var visLbl = new Label("Tüpte görünen top");
            visLbl.AddToClassList("inspector__row-label");
            visRow.Add(visLbl);
            visRow.Add(new Button(() =>
            {
                Record("Tüpte görünen top");
                m_Level.feedTubeQueueVisibleCount = Mathf.Clamp(m_Level.feedTubeQueueVisibleCount - 1, 2, 14);
                OnEdited();
            }) { text = "−" });
            var visVal = new Label(m_Level.feedTubeQueueVisibleCount.ToString());
            visVal.style.minWidth = 22;
            visVal.style.unityTextAlign = TextAnchor.MiddleCenter;
            visRow.Add(visVal);
            visRow.Add(new Button(() =>
            {
                Record("Tüpte görünen top");
                m_Level.feedTubeQueueVisibleCount = Mathf.Clamp(m_Level.feedTubeQueueVisibleCount + 1, 2, 14);
                OnEdited();
            }) { text = "+" });
            m_Inspector.Add(visRow);

            AddSlider("Tüp genişliği", m_Level.feedTubeWidthMultiplier, 1.05f, 1.65f, v =>
            {
                Record("Tüp genişliği");
                m_Level.feedTubeWidthMultiplier = v;
                OnEdited();
            });
            AddSlider("Tüp açıklığı", m_Level.feedTubeAnchorSpread, 0.15f, 1.0f, v =>
            {
                Record("Tüp açıklığı");
                m_Level.feedTubeAnchorSpread = v;
                OnEdited();
            });
            AddSlider("Akış hızı (sn)", m_Level.feedTubeDropInterval, 0.05f, 0.50f, v =>
            {
                Record("Tüp bırakma aralığı");
                m_Level.feedTubeDropInterval = v;
                OnEdited();
            });
        }

        private void BuildTrayInspector()
        {
            Section("Alt Grid Tepsisi (Bu Levele Özel)");

            var presets = new VisualElement();
            presets.AddToClassList("chip-grid");
            foreach (var (c, r) in new[] { (5, 4), (6, 5), (6, 7), (7, 8) })
            {
                int cc = c, rr = r;
                var chip = new Button(() =>
                {
                    Record("Tepsi boyutu");
                    m_Level.stagingGridCols = cc;
                    m_Level.stagingGridRows = rr;
                    m_Level.EnsureCarrierBoxPuzzleFormat();
                    OnEdited();
                }) { text = $"{cc}×{rr}" };
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--active", m_Level.stagingGridCols == cc && m_Level.stagingGridRows == rr);
                presets.Add(chip);
            }
            m_Inspector.Add(presets);

            var colsSlider = new SliderInt("Tepsi sütun", 2, 9) { value = m_Level.stagingGridCols, showInputField = true };
            colsSlider.RegisterValueChangedCallback(e =>
            {
                Record("Tepsi sütun");
                m_Level.stagingGridCols = e.newValue;
                m_Level.EnsureCarrierBoxPuzzleFormat();
                OnEdited();
            });
            m_Inspector.Add(colsSlider);

            var rowsSlider = new SliderInt("Tepsi satır", 2, 10) { value = m_Level.stagingGridRows, showInputField = true };
            rowsSlider.RegisterValueChangedCallback(e =>
            {
                Record("Tepsi satır");
                m_Level.stagingGridRows = e.newValue;
                m_Level.EnsureCarrierBoxPuzzleFormat();
                OnEdited();
            });
            m_Inspector.Add(rowsSlider);

            var customToggle = new Toggle("Özel tepsi ölçüleri")
            {
                value = m_Level.useCustomTraySettings,
                tooltip = "Açıkken bu level kendi tepsi genişliği, yüksekliği, hücre boyutu ve çerçeve ayarlarını kullanır."
            };
            customToggle.RegisterValueChangedCallback(e =>
            {
                Record("Özel tepsi ölçüleri");
                m_Level.useCustomTraySettings = e.newValue;
                OnEdited();
            });
            m_Inspector.Add(customToggle);

            if (!m_Level.useCustomTraySettings) return;

            var matchGlass = new Toggle("Cama göre genişlik") { value = m_Level.trayMatchGlassWidth };
            matchGlass.RegisterValueChangedCallback(e => { Record("Tepsi genişlik hizası"); m_Level.trayMatchGlassWidth = e.newValue; OnEdited(); });
            m_Inspector.Add(matchGlass);

            var showJar = new Toggle("Dış cam kavanoz") { value = m_Level.trayShowOuterGlassJar };
            showJar.RegisterValueChangedCallback(e => { Record("Tepsi dış cam kavanoz"); m_Level.trayShowOuterGlassJar = e.newValue; OnEdited(); });
            m_Inspector.Add(showJar);

            AddSlider("Maks. genişlik", m_Level.trayMaxBoardWidth, 1.20f, 4.00f, v => { Record("Tepsi maks. genişlik"); m_Level.trayMaxBoardWidth = v; OnEdited(); });
            AddSlider("Maks. yükseklik", m_Level.trayMaxBoardHeight, 1.20f, 5.20f, v => { Record("Tepsi maks. yükseklik"); m_Level.trayMaxBoardHeight = v; OnEdited(); });
            AddSlider("Camdan boşluk", m_Level.trayGapBelowGlass, 0.02f, 0.80f, v => { Record("Tepsi boşluk"); m_Level.trayGapBelowGlass = v; OnEdited(); });

            var manualCell = new Toggle("Elle hücre boyutu") { value = m_Level.trayUseManualCellSize };
            manualCell.RegisterValueChangedCallback(e => { Record("Elle hücre boyutu"); m_Level.trayUseManualCellSize = e.newValue; OnEdited(); });
            m_Inspector.Add(manualCell);

            if (m_Level.trayUseManualCellSize)
            {
                AddSlider("Hücre boyutu", m_Level.trayManualCellSize, 0.22f, 0.82f, v => { Record("Tepsi hücre boyutu"); m_Level.trayManualCellSize = v; OnEdited(); });
            }

            AddSlider("Çerçeve kalınlığı", m_Level.trayFramePadding, 0.03f, 0.30f, v => { Record("Tepsi çerçeve"); m_Level.trayFramePadding = v; OnEdited(); });
            AddSlider("Köşe yuvarlaklığı", m_Level.trayFrameCornerRadius, 0.04f, 0.40f, v => { Record("Tepsi köşe"); m_Level.trayFrameCornerRadius = v; OnEdited(); });
            AddSlider("İç boşluk", m_Level.trayInnerMargin, 0.01f, 0.25f, v => { Record("Tepsi iç boşluk"); m_Level.trayInnerMargin = v; OnEdited(); });

            var resetTray = new Button(() =>
            {
                Record("Tepsi ayarlarını sıfırla");
                m_Level.useCustomTraySettings = false;
                m_Level.trayMatchGlassWidth = true;
                m_Level.trayMaxBoardWidth = 2.822f;
                m_Level.trayMaxBoardHeight = 3.20f;
                m_Level.trayFramePadding = 0.068f;
                m_Level.trayInnerMargin = 0.048f;
                m_Level.trayFrameCornerRadius = 0.135f;
                m_Level.trayGapBelowGlass = 0.11f;
                m_Level.trayUseManualCellSize = false;
                m_Level.trayManualCellSize = 0.506f;
                m_Level.trayShowOuterGlassJar = true;
                OnEdited();
            }) { text = "Tepsiyi varsayılana sıfırla" };
            resetTray.AddToClassList("inspector__action");
            m_Inspector.Add(resetTray);
        }

        private void AddTubeModeBtn(VisualElement parent, string label, BallFeedTubeMode mode)
        {
            var btn = new Button(() =>
            {
                if (m_Level.useBallFeedTubes && m_Level.feedTubeMode == mode) return;
                Record("Besleme tüpü yönü");
                m_Level.useBallFeedTubes = true;
                m_Level.feedTubeMode = mode;
                OnEdited();
            }) { text = label };
            btn.AddToClassList("segmented__item");
            btn.EnableInClassList("segmented__item--active", m_Level.useBallFeedTubes && m_Level.feedTubeMode == mode);
            parent.Add(btn);
        }

        private void BuildTrackInspector()
        {
            GlassTrackPreset activePreset = m_Level.useCustomGlassShape ? m_Level.trackPreset : GlassTrackPreset.ArchLoop;

            Section("Pist Şekilleri (dışı ve ortadaki boşluğu aynı biçim)");
            var presets = new VisualElement();
            presets.AddToClassList("chip-grid");
            var trackList = new (string name, GlassTrackPreset p, string tip)[]
            {
                ("U", GlassTrackPreset.ArchLoop, "Dışı ve ortadaki boşluğu U biçimli yol; kol boyu top sayısına göre ayarlanır"),
                ("Kalp", GlassTrackPreset.HeartLoop, "Kalp biçimli halka"),
                ("Stadyum", GlassTrackPreset.UChannel, "Hap biçimli halka"),
                ("Oval Halka", GlassTrackPreset.ORing, "Oval halka"),
                ("Fasulye", GlassTrackPreset.SCurve, "Fasulye biçimli halka"),
                ("Kare", GlassTrackPreset.DoubleArch, "Yuvarlatılmış kare halka")
            };
            foreach (var item in trackList)
            {
                var pp = item.p;
                var chip = new Button(() => SetTrackPreset(pp, $"Pist: {item.name}")) { text = item.name, tooltip = item.tip };
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--active", m_Level.useCustomGlassShape && activePreset == pp);
                presets.Add(chip);
            }
            m_Inspector.Add(presets);

            Section("Top Sıklığı & Ölçü");
            var laneRow = new VisualElement();
            laneRow.AddToClassList("segmented");
            foreach (var (lbl, lv) in new[] { ("Oto", 0), ("3 Şerit", 3), ("4 Şerit", 4), ("5 Şerit", 5) })
            {
                int val = lv;
                var btn = new Button(() =>
                {
                    Record("Pist şerit sayısı");
                    EnsureEditableTrackBranches();
                    m_Level.trackLanesAcross = val;
                    OnEdited();
                }) { text = lbl };
                btn.AddToClassList("segmented__item");
                btn.EnableInClassList("segmented__item--active", (m_Level.useCustomGlassShape ? m_Level.trackLanesAcross : 0) == val);
                laneRow.Add(btn);
            }
            m_Inspector.Add(laneRow);

            var autoFit = new Toggle("Topların miktarına göre otomatik sığdır")
            {
                value = !m_Level.useCustomGlassShape || m_Level.trackAutoFitToBalls,
                tooltip = "Açıkken pist ölçeği ve top çapı, toplar pisti sıkı sıkıya dolduracak şekilde ayarlanır."
            };
            autoFit.RegisterValueChangedCallback(e =>
            {
                Record("Pist otomatik sığdır");
                EnsureEditableTrackBranches();
                m_Level.trackAutoFitToBalls = e.newValue;
                OnEdited();
            });
            m_Inspector.Add(autoFit);

            if (m_Level.useCustomGlassShape && !m_Level.trackAutoFitToBalls)
            {
                AddSlider("Pist ölçeği", m_Level.trackScale > 0.05f ? m_Level.trackScale : 1.0f, 0.55f, 1.45f, v =>
                {
                    Record("Pist ölçeği");
                    EnsureEditableTrackBranches();
                    m_Level.trackScale = v;
                    OnEdited();
                });
            }

            AddSlider("Duvar kalınlığı", m_Level.trackWallThickness > 0.01f ? m_Level.trackWallThickness : 0.082f, 0.045f, 0.14f, v =>
            {
                Record("Pist duvar kalınlığı");
                EnsureEditableTrackBranches();
                m_Level.trackWallThickness = v;
                OnEdited();
            });

            var symToggle = new Toggle("Simetrik düzenle (Sol ↔ Sağ)")
            {
                value = m_TrackSymmetricEdit,
                tooltip = "Açıkken bir kolun noktasını sürüklediğinde karşı kolun simetrik noktası da birlikte hareket eder."
            };
            symToggle.RegisterValueChangedCallback(e => m_TrackSymmetricEdit = e.newValue);
            m_Inspector.Add(symToggle);

            Section("Pist Kolları / Şekillendirme");
            var branchActions = new VisualElement();
            branchActions.AddToClassList("create-zone");
            branchActions.Add(new Button(() =>
            {
                Record("Kemer köprü ekle");
                EnsureEditableTrackBranches();
                m_Level.trackPreset = GlassTrackPreset.Custom;
                m_Level.trackBranches.Add(new GlassTrackBranch($"Kemer {m_Level.trackBranches.Count + 1}", true,
                    new Vector2(-0.85f, 0.25f), new Vector2(0f, 0.58f), new Vector2(0.85f, 0.25f)));
                OnEdited();
            }) { text = "+ Kemer / Köprü" });
            branchActions.Add(new Button(() =>
            {
                Record("Yeni kol ekle");
                EnsureEditableTrackBranches();
                m_Level.trackPreset = GlassTrackPreset.Custom;
                m_Level.trackBranches.Add(new GlassTrackBranch($"Kol {m_Level.trackBranches.Count + 1}", false,
                    new Vector2(0f, -0.54f), new Vector2(0.45f, 0.0f), new Vector2(0.55f, 0.65f)));
                OnEdited();
            }) { text = "+ Kol Ekle" });
            m_Inspector.Add(branchActions);

            var branches = (m_Level.useCustomGlassShape && m_Level.trackBranches != null && m_Level.trackBranches.Count > 0)
                ? m_Level.trackBranches
                : GlassTrackLayout.BuildPresetBranches(activePreset);
            for (int b = 0; b < branches.Count; b++)
            {
                int bIdx = b;
                var br = branches[b];
                if (br == null) continue;
                var row = new VisualElement();
                row.AddToClassList("inspector__row");
                var lbl = new Label($"{br.name} ({br.points?.Count ?? 0} nokta)");
                lbl.AddToClassList("inspector__row-label");
                row.Add(lbl);
                var bridgeTog = new Toggle("Köprü") { value = br.isBridge };
                bridgeTog.RegisterValueChangedCallback(e =>
                {
                    Record("Kol türü");
                    EnsureEditableTrackBranches();
                    if (bIdx < m_Level.trackBranches.Count) m_Level.trackBranches[bIdx].isBridge = e.newValue;
                    m_Level.trackPreset = GlassTrackPreset.Custom;
                    OnEdited();
                });
                row.Add(bridgeTog);
                if (branches.Count > 1)
                {
                    var del = new Button(() =>
                    {
                        Record("Kolu sil");
                        EnsureEditableTrackBranches();
                        if (bIdx < m_Level.trackBranches.Count && m_Level.trackBranches.Count > 1)
                            m_Level.trackBranches.RemoveAt(bIdx);
                        m_Level.trackPreset = GlassTrackPreset.Custom;
                        OnEdited();
                    }) { text = "×", tooltip = "Bu kolu sil" };
                    row.Add(del);
                }
                m_Inspector.Add(row);
            }

            var hint = new Label("Önizlemedeki beyaz noktaları sürükleyerek pisti istediğin gibi eğip bükebilirsin. Kol çizgisine çift tıkla = nokta ekle, noktaya sağ tıkla = sil.");
            hint.AddToClassList("inspector__hint");
            m_Inspector.Add(hint);
        }

        private void BuildBottleInspector()
        {
            GlassShapeData.GetBottle(m_Level, out float w, out float h);

            Section("Hazır boyutlar");
            var presets = new VisualElement();
            presets.AddToClassList("chip-grid");
            var list = new (string name, float w, float h)[]
            {
                ("Varsayılan", ShapedGlassContainer.DefaultBottleWidth, ShapedGlassContainer.DefaultBottleHeight),
                ("Kompakt", 2.70f, 0.62f),
                ("Geniş", 3.00f, 1.00f), ("Yüksek", 2.20f, 1.80f), ("Dar", 1.80f, 1.50f), ("Büyük", 3.00f, 1.60f)
            };
            var autoChip = new Button(ResetToDefault);
            autoChip.AddToClassList("chip");
            autoChip.EnableInClassList("chip--active", !m_Level.useCustomGlassShape);
            autoChip.Add(new Label("Otomatik"));
            autoChip.tooltip = "Yükseklik top sayısına göre otomatik ayarlanır (az toplu levellerde cam kısalır).";
            presets.Add(autoChip);
            foreach (var pr in list)
            {
                var pp = pr;
                var chip = new Button(() => SetBottle(pp.w, pp.h, "Hazır şişe"));
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--active", m_Level.useCustomGlassShape && Mathf.Abs(w - pr.w) < 0.01f && Mathf.Abs(h - pr.h) < 0.01f);
                chip.Add(new Label(pr.name));
                chip.tooltip = $"{pr.w:0.00} × {pr.h:0.00}";
                presets.Add(chip);
            }
            m_Inspector.Add(presets);

            Section("Ölçüler");
            AddSlider("Genişlik", w, BottleMinW, BottleMaxW, v => { GlassShapeData.GetBottle(m_Level, out _, out float hh); SetBottle(v, hh); });
            AddSlider("Yükseklik", h, BottleMinH, BottleMaxH, v => { GlassShapeData.GetBottle(m_Level, out float ww, out _); SetBottle(ww, v); });
            AddSlider("Köşe", m_Level.boxCornerRadius, 0.04f, 0.40f, v => { Record("Köşe"); EnsureCustom(); m_Level.boxCornerRadius = v; OnEdited(); });
            AddSlider("Cam duvarı", m_Level.boxFrameThickness, 0.04f, 0.24f, v => { Record("Cam duvarı"); EnsureCustom(); m_Level.boxFrameThickness = v; OnEdited(); });

            var hint = new Label("Taban şekli sabit: yuvarlak omuzlarla alttaki çıkış yakasına iner. Tutamaçlar: sağ kenar = genişlik, üst kenar = yükseklik.");
            hint.AddToClassList("inspector__hint");
            m_Inspector.Add(hint);
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
            AddSlider("Cam duvarı", m_Level.boxFrameThickness, 0.04f, 0.24f, v => { GlassShapeData.GetBox(m_Level, out float ww, out float hh, out _); SetBox(ww, hh, frame: v, undo: "Cam duvar kalınlığı"); });

            var hint = new Label("Tepsi alttaki cam kavanozun içinde, üst camın genişliğine göre hizalanır.");
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
                var ok = new Label("Taban düz ve geniş: yeni parlak kristal cam temasıyla ve alt çıkış yakasıyla havuz gibi oynanır.");
                ok.AddToClassList("inspector__hint");
                m_Inspector.Add(ok);
            }

            Section("Cam Şekilleri");
            var presets = new VisualElement();
            presets.AddToClassList("chip-grid");
            presets.Add(ContourChip("Şişe / Kavanoz", BottleJar()));
            presets.Add(ContourChip("Huni Şişe", FunnelJar()));
            presets.Add(ContourChip("Yuvarlak", OvalFlask()));
            presets.Add(ContourChip("Kum Saati", HourglassJar()));
            presets.Add(ContourChip("Geniş Kase", WideBowl()));
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

            if (GlassShapeData.IsTrack(m_Level))
            {
                if (!m_Level.useCustomGlassShape)
                    findings.Add((FindingSeverity.Info, "Bu level varsayılan Kemer (Görsel 1) cam pistini kullanıyor. Bir şekil seçtiğinde veya nokta sürüklediğinde levele özel kaydedilir."));
                if (m_Level.monsters == null || m_Level.monsters.Count == 0)
                    findings.Add((FindingSeverity.Info, "Levelde blok yok; önizlemede top görünmez. Blokları Level Stüdyosu'nda ekle."));
                if (findings.Count == 0) { m_Issues.Add(new Label("Bu cam pisti için bulgu yok.")); return; }
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
                return;
            }

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

        /// <summary>Görünümü üst kristal cam + alt kavanoz tepsinin tamamı sığacak şekilde ayarlar.</summary>
        private void FitView(Rect rect, PuzzleGlassLevelData lvl)
        {
            if (m_ViewFrozen) return;
            float maxHw, top, bottom;
            float frame = lvl.boxFrameThickness;
            if (GlassShapeData.IsTrack(lvl))
            {
                GlassShapeData.BuildTrack(lvl, out var compiled, out _);
                Rect ob = compiled != null ? compiled.outerBounds : Rect.MinMaxRect(-1.35f, -0.82f, 1.35f, 1.08f);
                maxHw = Mathf.Max(Mathf.Abs(ob.xMin), Mathf.Abs(ob.xMax));
                top = ob.yMax;
                bottom = ob.yMin - 0.14f;
                frame = compiled != null ? compiled.wallThickness : 0.082f;
            }
            else
            {
                var contour = GlassShapeData.GetContour(lvl);
                maxHw = 0f;
                foreach (var p in contour) maxHw = Mathf.Max(maxHw, p.halfWidth);
                top = contour[contour.Count - 1].y + frame;
                bottom = contour[0].y - frame - 0.14f;
            }
            ComputeTrayPreviewMetrics(lvl, maxHw, out _, out float trayHalfW, out float trayH, out float trayGap, out _, out _);
            float slotReserve = 0.76f;
            float viewBottom = bottom - slotReserve - trayGap - trayH - 0.22f;
            float viewTop = top + (lvl.useBallFeedTubes ? 1.48f : 0.25f);
            float viewHalfW = Mathf.Max(Mathf.Max(maxHw, trayHalfW) + frame + (lvl.useBallFeedTubes ? 0.58f : 0.42f), 1.30f);
            float sx = rect.width / (viewHalfW * 2f);
            float sy = rect.height / (viewTop - viewBottom);
            m_Scale = Mathf.Max(10f, Mathf.Min(sx, sy) * 0.90f);
            m_Origin = new Vector2(rect.width * 0.5f, rect.height * 0.5f + (viewTop + viewBottom) * 0.5f * m_Scale);
        }

        private static void ComputeTrayPreviewMetrics(
            PuzzleGlassLevelData lvl, float glassHalfW,
            out float cell, out float trayHalfW, out float trayH, out float gap, out float jarPad, out float cornerR)
        {
            int cols = Mathf.Max(1, lvl.stagingGridCols);
            int rows = Mathf.Max(1, lvl.stagingGridRows);
            if (lvl.useCustomTraySettings)
            {
                float maxW = Mathf.Max(1.0f, lvl.trayMaxBoardWidth);
                if (lvl.trayMatchGlassWidth && glassHalfW > 0.5f) maxW = Mathf.Min(glassHalfW * 2.12f, maxW);
                float frameOverhead = lvl.trayInnerMargin + lvl.trayFramePadding * 2f;
                float maxH = Mathf.Max(1.0f, lvl.trayMaxBoardHeight);
                cell = lvl.trayUseManualCellSize
                    ? Mathf.Clamp(lvl.trayManualCellSize, 0.22f, 0.82f)
                    : Mathf.Clamp(Mathf.Min((maxW - frameOverhead) / cols, (maxH - frameOverhead) / rows), 0.22f, 0.78f);
                gap = Mathf.Clamp(lvl.trayGapBelowGlass, 0.02f, 0.80f) + 0.22f;
                jarPad = Mathf.Clamp(lvl.trayFramePadding, 0.03f, 0.30f);
                cornerR = Mathf.Clamp(lvl.trayFrameCornerRadius, 0.04f, 0.40f);
            }
            else
            {
                float maxW = Mathf.Min(glassHalfW * 2.12f, 2.822f);
                float maxH = rows >= 5 ? 4.4f : 3.2f;
                float frameOverhead = 0.048f + 0.068f * 2f;
                cell = Mathf.Clamp(Mathf.Min((maxW - frameOverhead) / cols, (maxH - frameOverhead) / rows), 0.22f, 0.78f);
                gap = 0.34f;
                jarPad = 0.11f;
                cornerR = 0.20f;
            }
            trayHalfW = cols * cell * 0.5f;
            trayH = rows * cell;
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
            float frame = Mathf.Clamp(lvl.boxFrameThickness, 0.035f, 0.24f);
            Color collarColor = new Color(0.49f, 0.43f, 0.90f, 1f);
            Color glassOuterRim = new Color(0.46f, 0.45f, 0.92f, 0.92f);
            Color glassWallFill = new Color(0.74f, 0.79f, 1.00f, 0.46f);
            Color crystalFill = new Color(0.95f, 0.96f, 1.00f, 0.42f);

            // Soft pastel lavender -> cream -> peach stüdyo zemini + dış kenarda yüzen pastel küpler
            p.fillColor = new Color(0.91f, 0.90f, 0.98f, 1f);
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(rect.width, 0)); p.LineTo(new Vector2(rect.width, rect.height)); p.LineTo(new Vector2(0, rect.height)); p.ClosePath(); p.Fill();
            p.fillColor = new Color(0.76f, 0.79f, 0.99f, 0.52f);
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(rect.width, 0)); p.LineTo(new Vector2(rect.width, rect.height * 0.42f)); p.LineTo(new Vector2(0, rect.height * 0.42f)); p.ClosePath(); p.Fill();
            p.fillColor = new Color(0.995f, 0.90f, 0.83f, 0.58f);
            p.BeginPath(); p.MoveTo(new Vector2(0, rect.height * 0.58f)); p.LineTo(new Vector2(rect.width, rect.height * 0.58f)); p.LineTo(new Vector2(rect.width, rect.height)); p.LineTo(new Vector2(0, rect.height)); p.ClosePath(); p.Fill();

            // Dış kenarlarda odak dışı yüzen pastel küpler (Referans teması)
            void DrawBokehSquare(float wx, float wy, float sizeW, Color col)
            {
                RoundedRect(p, wx - sizeW * 0.5f, wy - sizeW * 0.5f, wx + sizeW * 0.5f, wy + sizeW * 0.5f, sizeW * 0.28f, col);
            }
            DrawBokehSquare(-1.85f,  0.45f, 0.46f, new Color(0.70f, 0.68f, 0.98f, 0.36f));
            DrawBokehSquare( 1.90f,  0.38f, 0.34f, new Color(0.99f, 0.76f, 0.28f, 0.48f));
            DrawBokehSquare(-1.95f, -0.80f, 0.26f, new Color(0.99f, 0.82f, 0.42f, 0.44f));
            DrawBokehSquare( 1.85f, -1.05f, 0.44f, new Color(0.74f, 0.72f, 0.98f, 0.38f));
            DrawBokehSquare(-1.85f, -2.55f, 0.44f, new Color(0.56f, 0.52f, 0.96f, 0.44f));
            DrawBokehSquare( 1.92f, -2.25f, 0.26f, new Color(0.99f, 0.78f, 0.32f, 0.42f));

            // Hafif ölçü ızgarası
            p.strokeColor = new Color(0.45f, 0.42f, 0.78f, 0.06f);
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

            bool isTrack = GlassShapeData.IsTrack(lvl);
            GlassTrackLayout.CompiledTrack compiledTrack = null;
            OrderedPoolLayout.Result layout;
            if (isTrack)
            {
                GlassShapeData.BuildTrack(lvl, out compiledTrack, out layout);
                if (compiledTrack != null)
                {
                    maxHw = Mathf.Max(Mathf.Abs(compiledTrack.outerBounds.xMin), Mathf.Abs(compiledTrack.outerBounds.xMax));
                    DrawTrackPreview2D(p, compiledTrack);
                }
            }
            else
            {
                layout = GlassShapeData.BuildLayout(lvl);
            }

            // Üst kristal cam çizimi (İnce parlak 3D şişe camı duvar + yumuşak saydamlık)
            if (!isTrack && box)
            {
                float hw = contour[0].halfWidth;
                float bottom = contour[0].y, top = contour[contour.Count - 1].y;
                float r = Mathf.Min(lvl.boxCornerRadius, Mathf.Min(hw, (top - bottom) * 0.5f));
                RoundedRect(p, -hw - frame, bottom - frame - 0.08f, hw + frame, top + frame - 0.08f, r + frame, new Color(0.58f, 0.50f, 0.82f, 0.22f));
                RoundedRect(p, -hw - frame, bottom - frame, hw + frame, top + frame, r + frame, glassWallFill);
                RoundedRect(p, -hw, bottom, hw, top, r, crystalFill);
                RoundedRectStroke(p, -hw - frame, bottom - frame, hw + frame, top + frame, r + frame, 1.8f, glassOuterRim);
                RoundedRectStroke(p, -hw - frame * 0.45f, bottom - frame * 0.45f, hw + frame * 0.45f, top + frame * 0.45f, r + frame * 0.45f, 2.6f, new Color(1f, 1f, 1f, 0.90f));
                // Sol/sağ dikey cam parlamaları
                RoundedRect(p, -hw + 0.04f, bottom + r * 0.45f, -hw + 0.11f, top - r * 0.45f, 0.03f, new Color(1f, 1f, 1f, 0.48f));
                RoundedRect(p, hw - 0.12f, bottom + r * 0.65f, hw - 0.04f, top - r * 0.35f, 0.035f, new Color(1f, 1f, 1f, 0.68f));
            }
            if (!isTrack && !box)
            {
                float strokePx = Mathf.Max(2.5f, frame * m_Scale);
                TraceContourPath(p, contour);
                p.fillColor = crystalFill;
                p.strokeColor = glassWallFill;
                p.lineWidth = strokePx;
                p.Fill();
                p.Stroke();

                TraceContourPath(p, contour);
                p.strokeColor = glassOuterRim;
                p.lineWidth = Mathf.Max(1.8f, strokePx * 0.32f);
                p.Stroke();

                TraceContourPath(p, contour);
                p.strokeColor = new Color(1f, 1f, 1f, 0.88f);
                p.lineWidth = Mathf.Max(1.6f, strokePx * 0.42f);
                p.Stroke();

                TraceContourPath(p, contour);
                p.fillColor = crystalFill;
                p.Fill();
            }

            // Toplar + opsiyonel üst cam besleme tüpleri (bu levele özel)
            var split = GlassShapeData.SplitForFeedTubes(lvl, layout);
            float ballRadiusWorld = Mathf.Max(0.05f, layout.diameter * 0.5f);
            float rad = ballRadiusWorld * m_Scale;

            FeedTubeCurveData? leftTube = null;
            FeedTubeCurveData? rightTube = null;
            if (lvl.useBallFeedTubes)
            {
                BallFeedTubeUtility.GetTopAnchorFromContour(contour, 1f, 1f, out float ceilY, out float topHalfW, out float topCenterX);
                if (lvl.feedTubeMode == BallFeedTubeMode.BothLeftAndRight || lvl.feedTubeMode == BallFeedTubeMode.LeftOnly)
                {
                    leftTube = BallFeedTubeUtility.BuildTubeCurve(
                        true, ceilY, topHalfW, topCenterX, ballRadiusWorld, lvl.feedTubeWidthMultiplier, lvl.feedTubeAnchorSpread, 1.42f);
                    DrawFeedTube2D(p, leftTube.Value, glassWallFill, glassOuterRim);
                }
                if (lvl.feedTubeMode == BallFeedTubeMode.BothLeftAndRight || lvl.feedTubeMode == BallFeedTubeMode.RightOnly)
                {
                    rightTube = BallFeedTubeUtility.BuildTubeCurve(
                        false, ceilY, topHalfW, topCenterX, ballRadiusWorld, lvl.feedTubeWidthMultiplier, lvl.feedTubeAnchorSpread, 1.42f);
                    DrawFeedTube2D(p, rightTube.Value, glassWallFill, glassOuterRim);
                }
            }

            // Hazne içindeki toplar
            var chamberBalls = split.chamberBalls ?? layout.balls;
            for (int i = 0; i < chamberBalls.Count; i++)
            {
                var b = chamberBalls[i];
                DrawPreviewBall2D(p, ToPx(b.localPos.x, b.localPos.y), rad, b.color);
            }

            // Tüp içindeki sıralı toplar
            if (lvl.useBallFeedTubes)
            {
                int maxPerTube = Mathf.Clamp(lvl.feedTubeQueueVisibleCount, 2, 14);
                float spacing = ballRadiusWorld * 2.15f;

                if (leftTube.HasValue && split.leftTubeColors != null)
                {
                    int vis = Mathf.Min(maxPerTube, split.leftTubeColors.Count);
                    float firstDist = leftTube.Value.restDist;
                    for (int i = 0; i < vis; i++)
                    {
                        float d = Mathf.Min(leftTube.Value.totalLength - ballRadiusWorld, firstDist + i * spacing);
                        leftTube.Value.EvaluateAtDistance(d, out Vector2 pt, out _);
                        DrawPreviewBall2D(p, ToPx(pt.x, pt.y), rad, split.leftTubeColors[i]);
                    }
                }
                if (rightTube.HasValue && split.rightTubeColors != null)
                {
                    int vis = Mathf.Min(maxPerTube, split.rightTubeColors.Count);
                    float firstDist = rightTube.Value.restDist;
                    for (int i = 0; i < vis; i++)
                    {
                        float d = Mathf.Min(rightTube.Value.totalLength - ballRadiusWorld, firstDist + i * spacing);
                        rightTube.Value.EvaluateAtDistance(d, out Vector2 pt, out _);
                        DrawPreviewBall2D(p, ToPx(pt.x, pt.y), rad, split.rightTubeColors[i]);
                    }
                }
            }

            // Üst camın altındaki 3D mor çıkış yakası + alt açık ağızlı cam kavanoz + mor tepsi
            {
                float glassBottomY = isTrack && compiledTrack != null ? compiledTrack.outerBounds.yMin : (contour[0].y - frame);
                float collarBaseW = isTrack && compiledTrack != null ? (compiledTrack.innerHalfWidth * 3.2f) : (contour[0].halfWidth * 2f);
                float collarW = Mathf.Clamp(collarBaseW * 0.48f, 0.72f, 1.14f);
                float collarH = 0.23f;
                float collarTop = glassBottomY + 0.03f;
                float collarBottom = collarTop - collarH;
                RoundedRect(p, -collarW * 0.5f, collarBottom - 0.04f, collarW * 0.5f, collarTop - 0.02f, 0.09f, new Color(0.46f, 0.40f, 0.78f, 0.26f));
                RoundedRect(p, -collarW * 0.5f, collarBottom, collarW * 0.5f, collarTop, 0.09f, new Color(0.47f, 0.41f, 0.88f, 1f));
                RoundedRect(p, -collarW * 0.40f, collarBottom + 0.02f, -collarW * 0.12f, collarTop - 0.02f, 0.04f, new Color(0.76f, 0.74f, 0.99f, 0.42f));
                RoundedRect(p, -collarW * 0.44f, collarTop - 0.055f, collarW * 0.44f, collarTop - 0.01f, 0.025f, new Color(0.86f, 0.84f, 1.00f, 0.82f));
                RoundedRect(p, -collarW * 0.48f, collarBottom, collarW * 0.48f, collarBottom + 0.05f, 0.03f, new Color(0.28f, 0.22f, 0.64f, 0.72f));

                // 5'li buz mavisi slot sırası (cam çıkışı ile alt tepsi arasında)
                int previewSlotCount = lvl.useCustomSlotSettings ? Mathf.Clamp(lvl.slotCount, 1, 6) : 5;
                float slotSpacing = lvl.useCustomSlotSettings
                    ? Mathf.Clamp(lvl.slotSpacing, 0.50f, 1.05f)
                    : (previewSlotCount >= 6 ? 0.50f : (previewSlotCount == 5 ? 0.59f : 0.72f));
                float slotScaleRatio = previewSlotCount >= 6 ? 0.64f / 0.74f : (previewSlotCount == 5 ? 1f : 0.82f / 0.74f);
                float slotHalf = 0.21f * slotScaleRatio;
                float slotCenterY = collarBottom - 0.20f - slotHalf;
                float slotStartX = -(previewSlotCount - 1) * slotSpacing * 0.5f;
                for (int si = 0; si < previewSlotCount; si++)
                {
                    float sx = slotStartX + si * slotSpacing;
                    // Soft cool shadow beneath each tile
                    RoundedRect(p, sx - slotHalf, slotCenterY - slotHalf - 0.055f, sx + slotHalf, slotCenterY + slotHalf - 0.02f, 0.08f * slotScaleRatio, new Color(0.18f, 0.23f, 0.35f, 0.28f));
                    // 3D sky-blue lower lip + perimeter outline
                    RoundedRect(p, sx - slotHalf - 0.01f, slotCenterY - slotHalf - 0.032f, sx + slotHalf + 0.01f, slotCenterY + slotHalf + 0.01f, 0.075f * slotScaleRatio, new Color(0.25f, 0.66f, 1.00f, 1f));
                    // Frosted icy pale-blue top face + crisp white rim
                    RoundedRect(p, sx - slotHalf, slotCenterY - slotHalf + 0.012f, sx + slotHalf, slotCenterY + slotHalf + 0.01f, 0.07f * slotScaleRatio, new Color(0.87f, 0.94f, 1.00f, 1f));
                    RoundedRectStroke(p, sx - slotHalf + 0.008f, slotCenterY - slotHalf + 0.02f, sx + slotHalf - 0.008f, slotCenterY + slotHalf + 0.004f, 0.066f * slotScaleRatio, 1.2f, new Color(0.99f, 1.00f, 1.00f, 0.92f));
                }

                ComputeTrayPreviewMetrics(lvl, maxHw, out float cell, out float trayHalfW, out float trayH, out float trayGap, out float jarPad, out float trayCornerR);
                float trayTop = slotCenterY - slotHalf - Mathf.Max(0.24f, trayGap * 0.85f);
                float trayBottom = trayTop - trayH;
                float neckW = Mathf.Clamp(trayHalfW * 0.43f, 0.38f, 0.64f);
                float neckTop = Mathf.Min(slotCenterY - slotHalf - 0.08f, trayTop + jarPad + 0.14f);
                bool showJar = lvl.useCustomTraySettings && lvl.trayShowOuterGlassJar;

                if (showJar)
                {
                    // Alt kavanoz gövde + açık ağızlı 3D boyun + alt yumuşak mor zemin gölgesi
                    RoundedRect(p, -trayHalfW - jarPad, trayBottom - jarPad - 0.08f, trayHalfW + jarPad, trayTop + jarPad - 0.06f, 0.24f, new Color(0.58f, 0.50f, 0.82f, 0.24f));
                    RoundedRect(p, -neckW, trayTop + jarPad - 0.02f, neckW, neckTop, 0.08f, glassWallFill);
                    RoundedRectStroke(p, -neckW, trayTop + jarPad - 0.02f, neckW, neckTop, 0.08f, 1.6f, glassOuterRim);
                    RoundedRect(p, -trayHalfW - jarPad, trayBottom - jarPad, trayHalfW + jarPad, trayTop + jarPad, 0.24f, glassWallFill);
                    RoundedRectStroke(p, -trayHalfW - jarPad, trayBottom - jarPad, trayHalfW + jarPad, trayTop + jarPad, 0.24f, 1.8f, glassOuterRim);
                    RoundedRectStroke(p, -trayHalfW - jarPad * 0.55f, trayBottom - jarPad * 0.55f, trayHalfW + jarPad * 0.55f, trayTop + jarPad * 0.55f, 0.21f, 2.2f, new Color(1f, 1f, 1f, 0.86f));
                    // Kavanoz gövde içi parlama şeritleri & üst oval ağız dudağı
                    RoundedRect(p, -trayHalfW - jarPad * 0.3f, trayBottom + 0.16f, -trayHalfW - jarPad * 0.02f, trayTop - 0.12f, 0.025f, new Color(1f, 1f, 1f, 0.45f));
                    RoundedRect(p, trayHalfW + jarPad * 0.02f, trayBottom + 0.22f, trayHalfW + jarPad * 0.34f, trayTop - 0.04f, 0.03f, new Color(1f, 1f, 1f, 0.70f));
                    RoundedRect(p, -neckW - 0.04f, neckTop - 0.055f, neckW + 0.04f, neckTop + 0.025f, 0.045f, new Color(0.90f, 0.92f, 1.00f, 0.85f));
                    RoundedRectStroke(p, -neckW - 0.04f, neckTop - 0.055f, neckW + 0.04f, neckTop + 0.025f, 0.045f, 1.6f, glassOuterRim);
                    RoundedRect(p, -neckW * 0.82f, neckTop - 0.032f, neckW * 0.82f, neckTop + 0.006f, 0.02f, new Color(0.66f, 0.64f, 0.96f, 0.52f));
                }

                // Düzenli kutu konum önizlemeleri (mor tepsi kaldırıldı)
                var wallSet = lvl.gridWalls != null ? new HashSet<Vector2Int>(lvl.gridWalls) : null;
                for (int x = 0; x < lvl.stagingGridCols; x++)
                    for (int y = 0; y < lvl.stagingGridRows; y++)
                    {
                        float x0 = -trayHalfW + x * cell + 0.04f, y0 = trayBottom + y * cell + 0.04f;
                        bool isWall = wallSet != null && wallSet.Contains(new Vector2Int(x, y));
                        RoundedRect(p, x0, y0, x0 + cell - 0.08f, y0 + cell - 0.08f, 0.07f,
                            isWall ? new Color(0.68f, 0.66f, 0.94f, 0.55f) : new Color(0.84f, 0.87f, 0.96f, 0.55f));
                        RoundedRectStroke(p, x0, y0, x0 + cell - 0.08f, y0 + cell - 0.08f, 0.07f, 1.0f, new Color(1f, 1f, 1f, 0.72f));
                    }
            }

            // Tutamaçlar
            if (isTrack)
            {
                var branches = GetEditableTrackBranches(lvl);
                // İskelet çizgileri
                p.strokeColor = new Color(0.28f, 0.34f, 0.72f, 0.35f);
                p.lineWidth = 1.5f;
                for (int b = 0; b < branches.Count; b++)
                {
                    var pts = branches[b]?.points;
                    if (pts == null || pts.Count < 2) continue;
                    p.BeginPath();
                    p.MoveTo(ToPx(pts[0].x, pts[0].y));
                    for (int i = 1; i < pts.Count; i++) p.LineTo(ToPx(pts[i].x, pts[i].y));
                    p.Stroke();
                }
                for (int b = 0; b < branches.Count; b++)
                {
                    var pts = branches[b]?.points;
                    if (pts == null) continue;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        DrawTrackHandle(p, ToPx(pts[i].x, pts[i].y), b, i);
                    }
                }
            }
            else if (GlassShapeData.IsBottle(lvl))
            {
                DrawHandle(p, HandlePos(Handle.Width, -1), Handle.Width, -1);
                DrawHandle(p, HandlePos(Handle.Height, -1), Handle.Height, -1);
            }
            else if (box)
            {
                float hw = contour[0].halfWidth, bottom = contour[0].y;
                DrawHandle(p, HandlePos(Handle.Width, -1), Handle.Width, -1);
                DrawHandle(p, HandlePos(Handle.Height, -1), Handle.Height, -1);
                DrawHandle(p, HandlePos(Handle.Corner, -1), Handle.Corner, -1);
                // Ölçü çizgisi
                p.strokeColor = new Color(0.32f, 0.30f, 0.68f, 0.42f);
                p.lineWidth = 1f;
                var a = ToPx(-hw, bottom - frame - 0.20f); var bb = ToPx(hw, bottom - frame - 0.20f);
                p.BeginPath(); p.MoveTo(a); p.LineTo(bb); p.Stroke();
            }
            else
            {
                for (int i = 0; i < contour.Count; i++) DrawHandle(p, HandlePos(Handle.Point, i), Handle.Point, i);
            }
        }

        private void DrawTrackPreview2D(Painter2D p, GlassTrackLayout.CompiledTrack track)
        {
            if (track == null || track.polylines.Count == 0) return;
            float outerStroke = (track.innerHalfWidth + track.wallThickness) * 2f * m_Scale;
            float rimStroke = (track.innerHalfWidth + track.wallThickness * 0.85f) * 2f * m_Scale;
            float innerStroke = track.innerHalfWidth * 2f * m_Scale;

            void StrokeAll(float widthPx, Color col, Vector2 offsetWorld)
            {
                p.strokeColor = col;
                p.lineWidth = Mathf.Max(2f, widthPx);
                p.lineCap = LineCap.Round;
                p.lineJoin = LineJoin.Round;
                for (int b = 0; b < track.polylines.Count; b++)
                {
                    var poly = track.polylines[b];
                    if (poly == null || poly.Count < 2) continue;
                    p.BeginPath();
                    p.MoveTo(ToPx(poly[0].x + offsetWorld.x, poly[0].y + offsetWorld.y));
                    for (int i = 1; i < poly.Count; i++)
                        p.LineTo(ToPx(poly[i].x + offsetWorld.x, poly[i].y + offsetWorld.y));
                    p.Stroke();
                }
            }

            // 1. Yumuşak gölge
            StrokeAll(outerStroke + 6f, new Color(0.54f, 0.50f, 0.82f, 0.22f), new Vector2(0.02f, -0.06f));
            // 2. Dış kontur kenarı (koyu periwinkle / gümüş kenarlık)
            StrokeAll(outerStroke, new Color(0.52f, 0.54f, 0.84f, 0.92f), Vector2.zero);
            // 3. Parlak beyaz yükseltilmiş cam/seramik duvar rayları
            StrokeAll(rimStroke, new Color(0.98f, 0.99f, 1.00f, 0.96f), Vector2.zero);
            // 4. İç oluk gölgesi
            StrokeAll(innerStroke + 3f, new Color(0.76f, 0.79f, 0.93f, 0.90f), Vector2.zero);
            // 5. Temiz buzlu kanal zemini
            StrokeAll(Mathf.Max(2f, innerStroke - 2f), new Color(0.94f, 0.95f, 0.99f, 0.94f), Vector2.zero);
        }

        private List<GlassTrackBranch> GetEditableTrackBranches(PuzzleGlassLevelData lvl)
        {
            if (lvl != null && lvl.useCustomGlassShape && lvl.trackBranches != null && lvl.trackBranches.Count > 0)
                return lvl.trackBranches;
            GlassTrackPreset p = (lvl != null && lvl.useCustomGlassShape) ? lvl.trackPreset : GlassTrackPreset.ArchLoop;
            return GlassTrackLayout.BuildPresetBranches(p);
        }

        private void DrawTrackHandle(Painter2D p, Vector2 pos, int branchIdx, int ptIdx)
        {
            bool active = m_Drag == Handle.TrackPoint && m_DragBranch == branchIdx && m_DragPoint == ptIdx;
            bool hover = m_Hover == Handle.TrackPoint && m_HoverBranch == branchIdx && m_HoverPoint == ptIdx;
            p.fillColor = active ? new Color(0.35f, 0.66f, 0.94f) : (hover ? Color.white : new Color(0.96f, 0.97f, 1.00f));
            p.strokeColor = new Color(0.24f, 0.28f, 0.62f);
            p.lineWidth = 2f;
            p.BeginPath();
            p.Arc(pos, active || hover ? HandleRadius + 1.5f : HandleRadius - 0.5f, 0f, 360f);
            p.Fill();
            p.Stroke();
        }

        private void DrawPreviewBall2D(Painter2D p, Vector2 c, float rad, PuzzleColor color)
        {
            Color bc = PuzzleColorPalette.GetBodyColor(color);
            p.fillColor = bc;
            p.strokeColor = Color.Lerp(bc, Color.black, 0.36f);
            p.lineWidth = Mathf.Max(1.1f, rad * 0.15f);
            p.BeginPath();
            p.Arc(c, rad * 0.94f, 0f, 360f);
            p.Fill();
            p.Stroke();
            p.fillColor = new Color(1f, 1f, 1f, 0.60f);
            p.BeginPath();
            p.Arc(c + new Vector2(-rad * 0.30f, -rad * 0.30f), rad * 0.28f, 0f, 360f);
            p.Fill();
        }

        private void DrawFeedTube2D(Painter2D p, FeedTubeCurveData curve, Color wallFill, Color outerRim)
        {
            if (!curve.IsValid) return;

            // Crystal fill + outer perimeter wall
            p.BeginPath();
            p.MoveTo(ToPx(curve.leftEdgePath[0].x, curve.leftEdgePath[0].y));
            for (int i = 1; i < curve.leftEdgePath.Length; i++)
                p.LineTo(ToPx(curve.leftEdgePath[i].x, curve.leftEdgePath[i].y));
            for (int i = curve.rightEdgePath.Length - 1; i >= 0; i--)
                p.LineTo(ToPx(curve.rightEdgePath[i].x, curve.rightEdgePath[i].y));
            p.ClosePath();
            p.fillColor = new Color(wallFill.r, wallFill.g, wallFill.b, 0.34f);
            p.Fill();

            // Left & right rim curves
            for (int side = 0; side < 2; side++)
            {
                var path = side == 0 ? curve.leftEdgePath : curve.rightEdgePath;
                p.BeginPath();
                p.MoveTo(ToPx(path[0].x, path[0].y));
                for (int i = 1; i < path.Length; i++)
                    p.LineTo(ToPx(path[i].x, path[i].y));
                p.strokeColor = outerRim;
                p.lineWidth = 2.6f;
                p.Stroke();

                p.BeginPath();
                p.MoveTo(ToPx(path[0].x, path[0].y));
                for (int i = 1; i < path.Length; i++)
                    p.LineTo(ToPx(path[i].x, path[i].y));
                p.strokeColor = new Color(1f, 1f, 1f, 0.85f);
                p.lineWidth = 1.3f;
                p.Stroke();
            }

            // Engel çizgisi önizlemesi (haznedeki toplar toplanana kadar topları tutan kapak)
            float radAngle = curve.gateAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(radAngle), Mathf.Sin(radAngle)) * (curve.gateWidth * 0.5f);
            Vector2 aPx = ToPx(curve.gatePoint.x - dir.x, curve.gatePoint.y - dir.y);
            Vector2 bPx = ToPx(curve.gatePoint.x + dir.x, curve.gatePoint.y + dir.y);
            p.BeginPath();
            p.MoveTo(aPx);
            p.LineTo(bPx);
            p.strokeColor = new Color(0.42f, 0.40f, 0.88f, 0.96f);
            p.lineWidth = Mathf.Max(4.5f, 0.058f * m_Scale);
            p.Stroke();

            p.BeginPath();
            p.MoveTo(aPx);
            p.LineTo(bPx);
            p.strokeColor = new Color(0.84f, 0.92f, 1f, 0.95f);
            p.lineWidth = Mathf.Max(2f, 0.026f * m_Scale);
            p.Stroke();
        }

        private void TraceContourPath(Painter2D p, List<GlassContourPoint> contour)
        {
            p.BeginPath();
            for (int i = 0; i < contour.Count; i++)
            {
                var pt = ToPx(contour[i].centerX + contour[i].halfWidth, contour[i].y);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            for (int i = contour.Count - 1; i >= 0; i--)
                p.LineTo(ToPx(contour[i].centerX - contour[i].halfWidth, contour[i].y));
            p.ClosePath();
        }

        private void RoundedRectStroke(Painter2D p, float x0, float y0, float x1, float y1, float r, float lineWidth, Color stroke)
        {
            var tl = ToPx(x0, y1); var br = ToPx(x1, y0);
            float rp = Mathf.Max(0f, Mathf.Min(r * m_Scale, Mathf.Min(br.x - tl.x, br.y - tl.y) * 0.5f));
            p.strokeColor = stroke;
            p.lineWidth = lineWidth;
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
            p.Stroke();
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
            if (GlassShapeData.IsBottle(m_Level))
            {
                // Şişe: genişlik tutamacı düz yan duvarın ortasında (taban daha dar)
                float topHw = contour[contour.Count - 1].halfWidth;
                float straightBottom = contour[contour.Count - 2].y;
                if (h == Handle.Width) return ToPx(topHw, (straightBottom + top) * 0.5f);
                if (h == Handle.Height) return ToPx(0f, top);
            }
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

        private (Handle, int, int) HitTest(Vector2 local)
        {
            if (m_Level == null) return (Handle.None, -1, -1);
            float best = HandleRadius + 6f;
            var result = (Handle.None, -1, -1);
            if (GlassShapeData.IsTrack(m_Level))
            {
                var branches = GetEditableTrackBranches(m_Level);
                for (int b = 0; b < branches.Count; b++)
                {
                    var pts = branches[b]?.points;
                    if (pts == null) continue;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        float d = Vector2.Distance(local, ToPx(pts[i].x, pts[i].y));
                        if (d < best) { best = d; result = (Handle.TrackPoint, b, i); }
                    }
                }
                return result;
            }
            if (GlassShapeData.IsBottle(m_Level))
            {
                foreach (var h in new[] { Handle.Height, Handle.Width })
                {
                    float d = Vector2.Distance(local, HandlePos(h, -1));
                    if (d < best) { best = d; result = (h, -1, -1); }
                }
            }
            else if (GlassShapeData.IsBox(m_Level))
            {
                foreach (var h in new[] { Handle.Corner, Handle.Height, Handle.Width })
                {
                    float d = Vector2.Distance(local, HandlePos(h, -1));
                    if (d < best) { best = d; result = (h, -1, -1); }
                }
            }
            else
            {
                var contour = GlassShapeData.GetContour(m_Level);
                for (int i = 0; i < contour.Count; i++)
                {
                    float d = Vector2.Distance(local, HandlePos(Handle.Point, i));
                    if (d < best) { best = d; result = (Handle.Point, -1, i); }
                }
            }
            return result;
        }

        // ==================== Fare ====================

        private void OnPointerDown(PointerDownEvent e)
        {
            if (m_Level == null) return;
            Vector2 local = e.localPosition;
            var (h, br, pt) = HitTest(local);

            if (e.button == 1 && h == Handle.TrackPoint)
            {
                RemoveTrackPoint(br, pt);
                e.StopPropagation();
                return;
            }
            if (e.button == 1 && h == Handle.Point)
            {
                RemovePoint(pt);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;

            if (h == Handle.None)
            {
                if (e.clickCount == 2 && GlassShapeData.IsTrack(m_Level)) AddTrackPointAt(ToWorld(local));
                else if (e.clickCount == 2 && !GlassShapeData.IsBox(m_Level) && !GlassShapeData.IsBottle(m_Level)) AddPointAt(ToWorld(local));
                return;
            }
            m_Drag = h;
            m_DragBranch = br;
            m_DragPoint = pt;
            m_ViewFrozen = true;
            Record("Cam tutamacı");
            if (h == Handle.TrackPoint) EnsureEditableTrackBranches();
            else EnsureCustom();
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
                var (h, br, pt) = HitTest(local);
                if (h != m_Hover || br != m_HoverBranch || pt != m_HoverPoint)
                {
                    m_Hover = h;
                    m_HoverBranch = br;
                    m_HoverPoint = pt;
                    m_Canvas.MarkDirtyRepaint();
                }
                return;
            }

            var w = ToWorld(local);
            if (m_Drag == Handle.TrackPoint)
            {
                EnsureEditableTrackBranches();
                if (m_DragBranch >= 0 && m_DragBranch < m_Level.trackBranches.Count)
                {
                    var pts = m_Level.trackBranches[m_DragBranch].points;
                    if (m_DragPoint >= 0 && m_DragPoint < pts.Count)
                    {
                        Vector2 prevPt = pts[m_DragPoint];
                        Vector2 nextPt = new Vector2(
                            Mathf.Clamp(Snap(w.x, e.shiftKey), -1.85f, 1.85f),
                            Mathf.Clamp(Snap(w.y, e.shiftKey), -1.20f, 1.60f));
                        pts[m_DragPoint] = nextPt;
                        if (m_TrackSymmetricEdit)
                        {
                            MirrorTrackPointEdit(m_DragBranch, m_DragPoint, prevPt, nextPt);
                        }
                        m_Level.trackPreset = GlassTrackPreset.Custom;
                        m_Level.trackAutoFitToBalls = false;
                    }
                }
            }
            else if (m_Drag == Handle.Point)
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
            else if (GlassShapeData.IsBottle(m_Level))
            {
                GlassShapeData.GetBottle(m_Level, out float bw, out float bh);
                float bottomY = GlassShapeData.GetContour(m_Level)[0].y;
                if (m_Drag == Handle.Width) bw = Snap(Mathf.Abs(w.x) * 2f, e.shiftKey);
                if (m_Drag == Handle.Height) bh = Snap(w.y - bottomY, e.shiftKey);
                bw = Mathf.Clamp(bw, BottleMinW, BottleMaxW);
                bh = Mathf.Clamp(bh, BottleMinH, BottleMaxH);
                m_Level.glassIsBottle = true;
                m_Level.glassPreset = GlassShapePreset.CustomFreeform;
                m_Level.contourPoints = ShapedGlassContainer.BuildBottleContour(bw, bh, bottomY);
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
            m_DragBranch = -1;
            m_DragPoint = -1;
            m_ViewFrozen = false;
            if (m_Canvas.HasPointerCapture(e.pointerId)) m_Canvas.ReleasePointer(e.pointerId);
            OnEdited();
        }

        private void MirrorTrackPointEdit(int branchIdx, int ptIdx, Vector2 prevPt, Vector2 nextPt)
        {
            Vector2 mirroredPrev = new Vector2(-prevPt.x, prevPt.y);
            Vector2 mirroredNext = new Vector2(-nextPt.x, nextPt.y);
            for (int b = 0; b < m_Level.trackBranches.Count; b++)
            {
                var pts = m_Level.trackBranches[b]?.points;
                if (pts == null) continue;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (b == branchIdx && i == ptIdx) continue;
                    if (Vector2.Distance(pts[i], mirroredPrev) < 0.08f)
                    {
                        pts[i] = mirroredNext;
                        return;
                    }
                }
            }
        }

        private void AddTrackPointAt(Vector2 world)
        {
            Record("Pist noktası ekle");
            EnsureEditableTrackBranches();
            int bestB = -1, bestInsert = -1;
            float bestDistSq = 0.08f;
            for (int b = 0; b < m_Level.trackBranches.Count; b++)
            {
                var pts = m_Level.trackBranches[b]?.points;
                if (pts == null || pts.Count < 2) continue;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector2 ab = pts[i + 1] - pts[i];
                    float abSq = ab.sqrMagnitude;
                    float t = abSq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(world - pts[i], ab) / abSq) : 0f;
                    Vector2 proj = pts[i] + ab * t;
                    float dSq = (world - proj).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestB = b;
                        bestInsert = i + 1;
                    }
                }
            }
            if (bestB >= 0)
            {
                m_Level.trackBranches[bestB].points.Insert(bestInsert, world);
                m_Level.trackPreset = GlassTrackPreset.Custom;
                OnEdited();
            }
        }

        private void RemoveTrackPoint(int branchIdx, int ptIdx)
        {
            Record("Pist noktası sil");
            EnsureEditableTrackBranches();
            if (branchIdx < 0 || branchIdx >= m_Level.trackBranches.Count) return;
            var pts = m_Level.trackBranches[branchIdx].points;
            if (pts == null || pts.Count <= 2 || ptIdx < 0 || ptIdx >= pts.Count) return;
            pts.RemoveAt(ptIdx);
            m_Level.trackPreset = GlassTrackPreset.Custom;
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

        private static List<GlassContourPoint> BottleJar() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.96f, 0f, 0.35f),
            new GlassContourPoint(-0.34f, 1.24f, 0f, 0.10f),
            new GlassContourPoint( 0.28f, 1.24f, 0f, 0.15f),
            new GlassContourPoint( 0.54f, 0.72f, 0f, 0.20f),
            new GlassContourPoint( 0.86f, 0.72f, 0f, 0.35f)
        };

        private static List<GlassContourPoint> FunnelJar() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.72f, 0f, 0.30f),
            new GlassContourPoint(-0.18f, 0.92f, 0f, 0.10f),
            new GlassContourPoint( 0.30f, 1.30f, 0f, 0.15f),
            new GlassContourPoint( 0.78f, 1.32f, 0f, 0.30f)
        };

        private static List<GlassContourPoint> OvalFlask() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.88f, 0f, 0.35f),
            new GlassContourPoint(-0.22f, 1.30f, 0f, 0.15f),
            new GlassContourPoint( 0.16f, 1.34f, 0f, 0.15f),
            new GlassContourPoint( 0.50f, 1.22f, 0f, 0.20f),
            new GlassContourPoint( 0.76f, 0.84f, 0f, 0.35f)
        };

        private static List<GlassContourPoint> HourglassJar() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 1.24f, 0f, 0.30f),
            new GlassContourPoint(-0.22f, 1.18f, 0f, 0.15f),
            new GlassContourPoint( 0.12f, 0.82f, 0f, 0.10f),
            new GlassContourPoint( 0.46f, 1.22f, 0f, 0.15f),
            new GlassContourPoint( 0.80f, 1.26f, 0f, 0.30f)
        };

        private static List<GlassContourPoint> WideBowl() => new List<GlassContourPoint>
        {
            new GlassContourPoint(-0.56f, 0.86f, 0f, 0.35f),
            new GlassContourPoint(-0.14f, 1.28f, 0f, 0.15f),
            new GlassContourPoint( 0.56f, 1.46f, 0f, 0.30f)
        };

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
