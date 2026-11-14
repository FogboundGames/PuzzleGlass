using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    [Serializable]
    public class SavedGlassShapeData
    {
        public string shapeName = "CustomGlass";
        public GlassShapePreset shapePreset;
        public bool symmetricMode = true;
        public int curveSubdivisions = 14;
        public float widthScale = 1f;
        public float heightScale = 1f;
        public GlassColorTheme colorTheme;
        public Color glassBodyColor;
        public Color glassCenterGlowColor;
        public Color glassOuterRimColor;
        public Color glassInnerRimColor;
        public Color glassFrontLensColor;
        public Color earHandleColor;
        public float boxCornerRadius = 0.22f;
        public float boxFrameThickness = 0.115f;
        public Color boxFrameColor = new Color(0.50f, 0.50f, 0.90f, 1f);
        public Color boxFrameHighlightColor = new Color(0.84f, 0.84f, 0.98f, 1f);
        public Color boxFrameEdgeShadeColor = new Color(0.38f, 0.36f, 0.80f, 1f);
        public Color dropShadowColor = new Color(0.72f, 0.60f, 0.48f, 0.30f);
        public float glassRimRadius = 0.042f;
        public float innerRimInset = 0.064f;
        public float glassShellDepth = 0.08f;
        public bool showShoulderHandles = false;
        public Vector2 shoulderHandleOffset;
        public float shoulderHandleScale = 1f;
        public bool showSpecularReflections = true;
        public float specularIntensity = 0.68f;
        public float particleSize = 0.24f;
        public float particleFillRatio = 0.48f;
        public int floatingBeadCount = 0;
        public List<GlassContourPoint> contourPoints = new List<GlassContourPoint>();
    }

    public class GlassShapeDesignerWindow : EditorWindow
    {
        private ShapedGlassContainer _targetContainer;
        private PuzzleGlassGameManager _gameManager;
        private PuzzleGlassLevelData _targetLevel;
        private StagingGridBoard _stagingBoard;

        private Vector2 _scrollPos;
        private int _activeTab = 0;
        private bool _enableSceneHandles = true;
        private bool _showContourPointTable = false;

        private int _draggingPointIndex = -1;
        private bool _draggingRightSide = true;
        private bool _autoRebuildParticlesOnRelease = false;

        private float _boxWidth = 2.64f;
        private float _boxHeight = 1.18f;
        private float _boxBottomY = -0.56f;
        private float _funnelBoxWidth = 2.2f;
        private float _funnelBoxHeight = 2.1f;
        private float _funnelBoxTension = 0.90f;
        private bool _funnelBoxHasSpout = true;
        private bool _funnelBoxHasNeck = true;

        private static readonly string[] TabTitles =
        {
            "📐 Şekil & Profil",
            "🎨 Renk & Çerçeve",
            "🟦 Tepsi (Grid Tray)",
            "🧩 Bölmeler & Test",
            "💾 JSON Kütüphane"
        };

        private static readonly string[] PresetLabels =
        {
            "🔵 Geniş Bombeli Flask",
            "🧪 Klasik İksir Şişesi",
            "⏳ Kum Saati Cam",
            "💙 Kalp Cam Şişe",
            "💎 Elmas Kristal Karaf",
            "📐 Konik Laboratuvar",
            "⛄ Çift Boğumlu Şişe",
            "🏺 Dalgalı Amfora",
            "🐍 S-Kıvrımlı Tüp",
            "🔲 Serbest Özel Şekil",
            "⬛ Hunili Kare Şişe",
            "📱 Hunili Dikey Kutu",
            "🖥️ Hunili Geniş Kutu",
            "📦 Düz Kare Havuz",
            "🖼️ Düz Dikdörtgen Havuz"
        };

        private static readonly string[] ThemeLabels =
        {
            "💧 Gök Mavisi",
            "💎 Şeffaf Kristal",
            "🟢 Zümrüt Yeşili",
            "🌸 Pembe Kuvars",
            "🟣 Mor Ametist",
            "🟠 Kehribar Sarısı"
        };

        [MenuItem("Tools/PuzzleGlass/✨ Cam Şekil & Tasarım Aracı (Glass Customizer)", false, 1)]
        [MenuItem("Window/PuzzleGlass/✨ Glass Shape Editor", false, 1)]
        public static void OpenWindow()
        {
            var win = GetWindow<GlassShapeDesignerWindow>("✨ Cam Özelleştirici");
            win.minSize = new Vector2(520f, 700f);
            win.Show();
        }

        public static void OpenForContainer(ShapedGlassContainer container)
        {
            var win = GetWindow<GlassShapeDesignerWindow>("✨ Cam Özelleştirici");
            win._targetContainer = container;
            win.minSize = new Vector2(520f, 700f);
            win.SyncLocalBoxCacheFromContainer();
            win.Show();
        }

        private void OnEnable()
        {
            FindTargets();
            SyncLocalBoxCacheFromContainer();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void FindTargets()
        {
            if (_targetContainer == null)
            {
                if (Selection.activeGameObject != null)
                    _targetContainer = Selection.activeGameObject.GetComponentInParent<ShapedGlassContainer>();

                if (_targetContainer == null)
                    _targetContainer = FindFirstObjectByType<ShapedGlassContainer>();
            }

            if (_gameManager == null)
                _gameManager = FindFirstObjectByType<PuzzleGlassGameManager>();

            if (_stagingBoard == null)
                _stagingBoard = FindFirstObjectByType<StagingGridBoard>();

            if (_targetLevel == null && _gameManager != null && _gameManager.currentLevel != null)
                _targetLevel = _gameManager.currentLevel;
        }

        private void SyncLocalBoxCacheFromContainer()
        {
            if (_targetContainer == null) return;
            _targetContainer.GetRoundedBoxPoolParameters(out _boxWidth, out _boxHeight, out _boxBottomY);
        }

        private void OnGUI()
        {
            FindTargets();
            DrawTopBanner();

            if (_targetContainer == null)
            {
                EditorGUILayout.HelpBox(
                    "Sahnede aktif bir ShapedGlassContainer bulunamadı. Sahneyi oluşturun veya bir ShapedGlassContainer seçin.",
                    MessageType.Warning);
                if (GUILayout.Button("Sahneyi Otomatik Oluştur", GUILayout.Height(34)))
                {
                    PuzzleGlassSceneSetup.BuildCompleteScene();
                    FindTargets();
                    SyncLocalBoxCacheFromContainer();
                }
                return;
            }

            _targetContainer.EnsureContourInitialized();

            DrawLevelSyncBar();

            EditorGUILayout.Space(4);
            _activeTab = GUILayout.Toolbar(_activeTab, TabTitles, GUILayout.Height(28));
            EditorGUILayout.Space(6);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            switch (_activeTab)
            {
                case 0:
                    DrawShapeAndProfileTab();
                    break;
                case 1:
                    DrawColorAndFrameTab();
                    break;
                case 2:
                    DrawTraySizeAndStyleTab();
                    break;
                case 3:
                    DrawCompartmentsAndTestTab();
                    break;
                case 4:
                    DrawJsonLibraryTab();
                    break;
            }
            EditorGUILayout.Space(16);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTopBanner()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("✨ PuzzleGlass – Cam Şekil & Özelleştirme Aracı ✨", titleStyle, GUILayout.Height(22));

            if (_targetContainer != null)
            {
                EditorGUILayout.BeginHorizontal();
                _targetContainer = (ShapedGlassContainer)EditorGUILayout.ObjectField("Hedef Cam:", _targetContainer, typeof(ShapedGlassContainer), true);
                _enableSceneHandles = GUILayout.Toggle(_enableSceneHandles, "✋ Sahne Tutamaçları", "Button", GUILayout.Width(145));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.28f, 0.72f, 0.98f);
                if (GUILayout.Button("🔄 Camı Yenile", GUILayout.Height(26)))
                {
                    RebuildContainerAndColliders();
                }

                GUI.backgroundColor = new Color(0.32f, 0.86f, 0.56f);
                if (GUILayout.Button("🛡️ Collider Senkronize Et", GUILayout.Height(26)))
                {
                    SyncPhysicsColliders();
                }

                GUI.backgroundColor = new Color(0.55f, 0.62f, 0.96f);
                if (GUILayout.Button("📐 Alt Grid'i Hizala", GUILayout.Height(26)))
                {
                    AlignStagingGridBelowGlass();
                }

                GUI.backgroundColor = new Color(0.95f, 0.80f, 0.35f);
                if (GUILayout.Button("🧪 Toplar / Kum", GUILayout.Height(26)))
                {
                    _targetContainer.SpawnTestSandParticles(72);
                    EditorUtility.SetDirty(_targetContainer);
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLevelSyncBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            _targetLevel = (PuzzleGlassLevelData)EditorGUILayout.ObjectField("Aktif Level:", _targetLevel, typeof(PuzzleGlassLevelData), false);

            GUI.enabled = _targetLevel != null;
            GUI.backgroundColor = new Color(0.30f, 0.85f, 0.50f);
            if (GUILayout.Button("💾 Level'e Kaydet", GUILayout.Width(118), GUILayout.Height(20)))
            {
                SaveContainerShapeToLevel(_targetLevel);
            }
            GUI.backgroundColor = new Color(0.45f, 0.75f, 1.0f);
            if (GUILayout.Button("📥 Yükle", GUILayout.Width(65), GUILayout.Height(20)))
            {
                LoadLevelShapeIntoContainer(_targetLevel);
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_gameManager != null)
            {
                EditorGUI.BeginChangeCheck();
                bool keepInPlay = EditorGUILayout.ToggleLeft(
                    "🔒 Play moduna geçince Level asset'i sahnedeki bu özel camı ezmesin (Preserve Scene Glass)",
                    _gameManager.preserveSceneGlassInPlayMode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_gameManager, "Toggle Preserve Scene Glass");
                    _gameManager.preserveSceneGlassInPlayMode = keepInPlay;
                    EditorUtility.SetDirty(_gameManager);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawShapeAndProfileTab()
        {
            // Mode switcher: Rounded Box Pool vs Custom Contour / Flask
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("1. Cam Gövde Modu", EditorStyles.boldLabel);
            bool isBoxMode = _targetContainer.IsFlatBottomBox;

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isBoxMode ? new Color(0.30f, 0.84f, 0.54f) : new Color(0.85f, 0.88f, 0.95f);
            if (GUILayout.Button("🟦 Çerçeveli Havuz Cam (Rounded Box Pool)", GUILayout.Height(30)))
            {
                Undo.RecordObject(_targetContainer, "Switch to Rounded Box Pool");
                _targetContainer.ConfigureRoundedBoxPool(_boxWidth, _boxHeight, _targetContainer.boxCornerRadius, _targetContainer.boxFrameThickness, _boxBottomY, true);
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = !isBoxMode ? new Color(0.30f, 0.84f, 0.54f) : new Color(0.85f, 0.88f, 0.95f);
            if (GUILayout.Button("🧪 Özel Kontur / Şişe / Çokgen (Custom Contour)", GUILayout.Height(30)))
            {
                Undo.RecordObject(_targetContainer, "Switch to Custom Contour Glass");
                if (_targetContainer.IsFlatBottomBox)
                {
                    _targetContainer.ApplyShapePreset(GlassShapePreset.CustomFreeform, true);
                }
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            if (isBoxMode)
            {
                DrawRoundedBoxCustomizerSection();
            }
            else
            {
                DrawInteractive2DCanvas();
                EditorGUILayout.Space(6);
                DrawQuickGeometricPresetsSection();
                EditorGUILayout.Space(6);
                DrawParametricDimensionsSection();
                EditorGUILayout.Space(6);
                DrawSquareRectangleContourBuilderSection();
                EditorGUILayout.Space(6);
                DrawContourPointTableSection();
            }
        }

        private void DrawRoundedBoxCustomizerSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("2. Çerçeveli Havuz Camı Ölçüleri (Rounded Box Pool)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Scene ekranındaki yeşil/camgöbeği okları sürükleyerek veya aşağıdaki kaydırıcılarla kutunun iç boyutunu, köşe yuvarlaklığını ve çerçeve kalınlığını anlık ayarlayabilirsiniz.",
                MessageType.Info);

            EditorGUILayout.LabelField("⚡ Hızlı Boyut Şablonları", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Geniş Havuz (2.64 x 1.18)", GUILayout.Height(26))) ApplyBoxSizePreset(2.64f, 1.18f, 0.22f, 0.115f, -0.56f);
            if (GUILayout.Button("Kare Havuz (1.60 x 1.60)", GUILayout.Height(26))) ApplyBoxSizePreset(1.60f, 1.60f, 0.26f, 0.115f, -0.56f);
            if (GUILayout.Button("Yüksek Havuz (1.50 x 1.95)", GUILayout.Height(26))) ApplyBoxSizePreset(1.50f, 1.95f, 0.24f, 0.115f, -0.60f);
            if (GUILayout.Button("Panoramik (2.92 x 0.96)", GUILayout.Height(26))) ApplyBoxSizePreset(2.92f, 0.96f, 0.20f, 0.105f, -0.50f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            float newW = EditorGUILayout.Slider("İç Genişlik (Width)", _boxWidth, 0.80f, 3.40f);
            float newH = EditorGUILayout.Slider("İç Yükseklik (Height)", _boxHeight, 0.60f, 2.80f);
            float newBottom = EditorGUILayout.Slider("Alt Taban Y (Bottom Y)", _boxBottomY, -1.00f, 0.40f);
            float newCorner = EditorGUILayout.Slider("Köşe Yuvarlaklığı (Corner Radius)", _targetContainer.boxCornerRadius, 0.04f, 0.65f);
            float newFrameThick = EditorGUILayout.Slider("Dış Çerçeve Kalınlığı (Frame Thick)", _targetContainer.boxFrameThickness, 0.04f, 0.24f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Modify Rounded Box Glass");
                _boxWidth = newW;
                _boxHeight = newH;
                _boxBottomY = newBottom;
                _targetContainer.ConfigureRoundedBoxPool(_boxWidth, _boxHeight, newCorner, newFrameThick, _boxBottomY, true);
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndVertical();
        }

        private void ApplyBoxSizePreset(float w, float h, float corner, float frameThick, float bottomY)
        {
            Undo.RecordObject(_targetContainer, "Apply Rounded Box Size Preset");
            _boxWidth = w;
            _boxHeight = h;
            _boxBottomY = bottomY;
            _targetContainer.ConfigureRoundedBoxPool(w, h, corner, frameThick, bottomY, true);
            SyncPhysicsColliders();
            EditorUtility.SetDirty(_targetContainer);
            SceneView.RepaintAll();
        }

        private void DrawQuickGeometricPresetsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🍾 Hazır Cam Şekli Şablonları", EditorStyles.boldLabel);

            int cols = 3;
            for (int i = 0; i < PresetLabels.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && (i + j) < PresetLabels.Length; j++)
                {
                    int presetIdx = i + j;
                    GlassShapePreset preset = (GlassShapePreset)presetIdx;
                    bool isActive = (_targetContainer.shapePreset == preset);

                    GUI.backgroundColor = isActive ? new Color(0.28f, 0.85f, 0.55f) : new Color(0.45f, 0.72f, 0.98f);
                    if (GUILayout.Button(PresetLabels[presetIdx], GUILayout.Height(26)))
                    {
                        Undo.RecordObject(_targetContainer, "Apply Glass Shape Preset");
                        _targetContainer.ApplyShapePreset(preset, true);
                        SyncLocalBoxCacheFromContainer();
                        SyncPhysicsColliders();
                        EditorUtility.SetDirty(_targetContainer);
                        SceneView.RepaintAll();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("🔷 Ekstra Özel Siluetler (Hızlı Başlangıç):", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.75f, 0.65f, 0.96f);
            if (GUILayout.Button("🔻 Yamuk / V-Huni", GUILayout.Height(26))) ApplyCustomTrapezoidShape();
            if (GUILayout.Button("⬡ Altıgen / Fıçı", GUILayout.Height(26))) ApplyCustomBarrelHexShape();
            if (GUILayout.Button("🧪 Dar Boğaz Tüp", GUILayout.Height(26))) ApplyCustomSlimTubeShape();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void ApplyCustomTrapezoidShape()
        {
            Undo.RecordObject(_targetContainer, "Apply Trapezoid Glass");
            _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
            _targetContainer.symmetricMode = true;
            _targetContainer.showShoulderHandles = false;
            _targetContainer.contourPoints = new List<GlassContourPoint>
            {
                new GlassContourPoint(-1.15f, 0.19f, 0f, 0.35f),
                new GlassContourPoint(-0.74f, 0.19f, 0f, 0.25f),
                new GlassContourPoint(-0.48f, 0.48f, 0f, 0.85f),
                new GlassContourPoint( 0.88f, 1.38f, 0f, 0.90f),
                new GlassContourPoint( 1.25f, 1.38f, 0f, 0.85f),
                new GlassContourPoint( 1.55f, 0.34f, 0f, 0.25f),
                new GlassContourPoint( 2.35f, 0.34f, 0f, 0.35f)
            };
            _targetContainer.SyncBoundsFromContour();
            _targetContainer.RebuildContainer();
            SyncPhysicsColliders();
            EditorUtility.SetDirty(_targetContainer);
        }

        private void ApplyCustomBarrelHexShape()
        {
            Undo.RecordObject(_targetContainer, "Apply Barrel Hex Glass");
            _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
            _targetContainer.symmetricMode = true;
            _targetContainer.showShoulderHandles = false;
            _targetContainer.contourPoints = new List<GlassContourPoint>
            {
                new GlassContourPoint(-1.15f, 0.19f, 0f, 0.35f),
                new GlassContourPoint(-0.74f, 0.19f, 0f, 0.25f),
                new GlassContourPoint(-0.45f, 0.74f, 0f, 0.95f),
                new GlassContourPoint( 0.25f, 1.32f, 0f, 0.95f),
                new GlassContourPoint( 0.95f, 1.32f, 0f, 0.95f),
                new GlassContourPoint( 1.48f, 0.72f, 0f, 0.90f),
                new GlassContourPoint( 1.85f, 0.32f, 0f, 0.25f),
                new GlassContourPoint( 2.40f, 0.32f, 0f, 0.35f)
            };
            _targetContainer.SyncBoundsFromContour();
            _targetContainer.RebuildContainer();
            SyncPhysicsColliders();
            EditorUtility.SetDirty(_targetContainer);
        }

        private void ApplyCustomSlimTubeShape()
        {
            Undo.RecordObject(_targetContainer, "Apply Slim Tube Glass");
            _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
            _targetContainer.symmetricMode = true;
            _targetContainer.showShoulderHandles = false;
            _targetContainer.contourPoints = new List<GlassContourPoint>
            {
                new GlassContourPoint(-1.15f, 0.20f, 0f, 0.35f),
                new GlassContourPoint(-0.68f, 0.20f, 0f, 0.25f),
                new GlassContourPoint(-0.45f, 0.62f, 0f, 0.85f),
                new GlassContourPoint( 1.45f, 0.62f, 0f, 0.85f),
                new GlassContourPoint( 1.85f, 0.30f, 0f, 0.25f),
                new GlassContourPoint( 2.45f, 0.30f, 0f, 0.35f)
            };
            _targetContainer.SyncBoundsFromContour();
            _targetContainer.RebuildContainer();
            SyncPhysicsColliders();
            EditorUtility.SetDirty(_targetContainer);
        }

        private void DrawInteractive2DCanvas()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🎨 Canlı 2B Cam Profili Tuvali", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool newSym = GUILayout.Toggle(_targetContainer.symmetricMode, "Simetrik Mod", "Button", GUILayout.Width(105));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Toggle Symmetric Mode");
                _targetContainer.symmetricMode = newSym;
                if (newSym)
                {
                    for (int i = 0; i < _targetContainer.contourPoints.Count; i++)
                    {
                        var cp = _targetContainer.contourPoints[i];
                        cp.centerX = 0f;
                        _targetContainer.contourPoints[i] = cp;
                    }
                }
                _targetContainer.RebuildGlassMeshAndSettleParticles();
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
            }

            if (GUILayout.Button("Sahne Objesini Seç", GUILayout.Width(128)))
            {
                Selection.activeGameObject = _targetContainer.gameObject;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "• Sol Tık + Sürükle: Noktayı taşı   • Shift + Sol Tık: Yeni nokta ekle   • Sağ Tık: Noktayı sil",
                EditorStyles.miniLabel);

            Rect canvasRect = GUILayoutUtility.GetRect(440f, 290f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(canvasRect, new Color(0.11f, 0.14f, 0.20f, 1f));

            const float minX = -2.0f;
            const float maxX = 2.0f;
            const float minY = -1.45f;
            const float maxY = 2.85f;

            Vector2 ToCanvas(Vector2 localPt)
            {
                float nx = Mathf.InverseLerp(minX, maxX, localPt.x);
                float ny = Mathf.InverseLerp(minY, maxY, localPt.y);
                return new Vector2(
                    Mathf.Lerp(canvasRect.xMin + 16f, canvasRect.xMax - 16f, nx),
                    Mathf.Lerp(canvasRect.yMax - 12f, canvasRect.yMin + 12f, ny));
            }

            Vector2 FromCanvas(Vector2 guiPt)
            {
                float nx = Mathf.InverseLerp(canvasRect.xMin + 16f, canvasRect.xMax - 16f, guiPt.x);
                float ny = Mathf.InverseLerp(canvasRect.yMax - 12f, canvasRect.yMin + 12f, guiPt.y);
                return new Vector2(
                    Mathf.Lerp(minX, maxX, nx),
                    Mathf.Lerp(minY, maxY, ny));
            }

            Handles.BeginGUI();

            Handles.color = new Color(1f, 1f, 1f, 0.06f);
            for (float gx = -1.5f; gx <= 1.5f; gx += 0.5f)
                Handles.DrawLine(ToCanvas(new Vector2(gx, minY)), ToCanvas(new Vector2(gx, maxY)));
            for (float gy = -1.0f; gy <= 2.5f; gy += 0.5f)
                Handles.DrawLine(ToCanvas(new Vector2(minX, gy)), ToCanvas(new Vector2(maxX, gy)));

            Handles.color = new Color(0.4f, 0.8f, 1f, 0.25f);
            Handles.DrawDottedLine(ToCanvas(new Vector2(0f, minY)), ToCanvas(new Vector2(0f, maxY)), 3f);

            _targetContainer.GenerateFlaskPaths(
                out Vector3[] leftPath,
                out Vector3[] rightPath,
                out Vector3[] leftInner,
                out Vector3[] rightInner);

            if (leftPath != null && rightPath != null && leftPath.Length > 1)
            {
                Color fillCol = _targetContainer.glassBodyColor;
                fillCol.a = 0.28f;
                Handles.color = fillCol;
                for (int i = 0; i < leftPath.Length - 1; i++)
                {
                    Vector3[] quad =
                    {
                        ToCanvas(leftPath[i]),
                        ToCanvas(rightPath[i]),
                        ToCanvas(rightPath[i + 1]),
                        ToCanvas(leftPath[i + 1])
                    };
                    Handles.DrawAAConvexPolygon(quad);
                }

                Vector3[] leftCanvas = new Vector3[leftPath.Length];
                Vector3[] rightCanvas = new Vector3[rightPath.Length];
                for (int i = 0; i < leftPath.Length; i++)
                {
                    leftCanvas[i] = ToCanvas(leftPath[i]);
                    rightCanvas[i] = ToCanvas(rightPath[i]);
                }
                Handles.color = new Color(0.25f, 0.82f, 1.0f, 0.95f);
                Handles.DrawAAPolyLine(4f, leftCanvas);
                Handles.DrawAAPolyLine(4f, rightCanvas);

                if (leftInner != null && rightInner != null && leftInner.Length == leftPath.Length)
                {
                    Vector3[] lIn = new Vector3[leftInner.Length];
                    Vector3[] rIn = new Vector3[rightInner.Length];
                    for (int i = 0; i < leftInner.Length; i++)
                    {
                        lIn[i] = ToCanvas(leftInner[i]);
                        rIn[i] = ToCanvas(rightInner[i]);
                    }
                    Handles.color = new Color(0.85f, 0.96f, 1.0f, 0.5f);
                    Handles.DrawAAPolyLine(1.5f, lIn);
                    Handles.DrawAAPolyLine(1.5f, rIn);
                }
            }

            var pts = _targetContainer.contourPoints;
            float wScale = Mathf.Max(0.1f, _targetContainer.widthScale);
            float hScale = Mathf.Max(0.1f, _targetContainer.heightScale);

            for (int i = 0; i < pts.Count; i++)
            {
                var cp = pts[i];
                float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                float hw = cp.halfWidth * wScale;
                float y = cp.y * hScale;

                Vector2 leftGui = ToCanvas(new Vector2(cx - hw, y));
                Vector2 rightGui = ToCanvas(new Vector2(cx + hw, y));

                Handles.color = new Color(1f, 1f, 1f, 0.14f);
                Handles.DrawDottedLine(leftGui, rightGui, 2f);

                bool isSelectedLeft = (_draggingPointIndex == i && !_draggingRightSide);
                bool isSelectedRight = (_draggingPointIndex == i && _draggingRightSide);

                Handles.color = isSelectedLeft ? Color.yellow : new Color(0.30f, 0.92f, 0.62f, 1f);
                Handles.DrawSolidDisc(leftGui, Vector3.forward, isSelectedLeft ? 6.5f : 5.0f);

                Handles.color = isSelectedRight ? Color.yellow : new Color(0.30f, 0.92f, 0.62f, 1f);
                Handles.DrawSolidDisc(rightGui, Vector3.forward, isSelectedRight ? 6.5f : 5.0f);
            }

            Event e = Event.current;
            if (canvasRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (e.shift)
                    {
                        InsertControlPointAtLocalPosition(FromCanvas(e.mousePosition));
                        e.Use();
                    }
                    else
                    {
                        float bestDist = 14f;
                        int bestIdx = -1;
                        bool bestRight = true;

                        for (int i = 0; i < pts.Count; i++)
                        {
                            var cp = pts[i];
                            float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                            float hw = cp.halfWidth * wScale;
                            float y = cp.y * hScale;

                            float dL = Vector2.Distance(e.mousePosition, ToCanvas(new Vector2(cx - hw, y)));
                            if (dL < bestDist) { bestDist = dL; bestIdx = i; bestRight = false; }

                            float dR = Vector2.Distance(e.mousePosition, ToCanvas(new Vector2(cx + hw, y)));
                            if (dR < bestDist) { bestDist = dR; bestIdx = i; bestRight = true; }
                        }

                        if (bestIdx >= 0)
                        {
                            if (e.control && pts.Count > 4 && bestIdx > 0 && bestIdx < pts.Count - 1)
                            {
                                Undo.RecordObject(_targetContainer, "Delete Glass Contour Point");
                                pts.RemoveAt(bestIdx);
                                _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                                _targetContainer.RebuildGlassMeshAndSettleParticles();
                                SyncPhysicsColliders();
                                EditorUtility.SetDirty(_targetContainer);
                                e.Use();
                            }
                            else
                            {
                                _draggingPointIndex = bestIdx;
                                _draggingRightSide = bestRight;
                                _autoRebuildParticlesOnRelease = true;
                                e.Use();
                            }
                        }
                    }
                }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    for (int i = 1; i < pts.Count - 1; i++)
                    {
                        var cp = pts[i];
                        float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                        float hw = cp.halfWidth * wScale;
                        float y = cp.y * hScale;

                        if (Vector2.Distance(e.mousePosition, ToCanvas(new Vector2(cx - hw, y))) < 12f ||
                            Vector2.Distance(e.mousePosition, ToCanvas(new Vector2(cx + hw, y))) < 12f)
                        {
                            if (pts.Count > 4)
                            {
                                Undo.RecordObject(_targetContainer, "Delete Glass Contour Point");
                                pts.RemoveAt(i);
                                _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                                _targetContainer.RebuildGlassMeshAndSettleParticles();
                                SyncPhysicsColliders();
                                EditorUtility.SetDirty(_targetContainer);
                                e.Use();
                            }
                            break;
                        }
                    }
                }
            }

            if (_draggingPointIndex >= 0 && _draggingPointIndex < pts.Count)
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    Undo.RecordObject(_targetContainer, "Move Glass Contour Point");
                    UpdateControlPointFromDrag(_draggingPointIndex, _draggingRightSide, FromCanvas(e.mousePosition));
                    _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                    _targetContainer.RebuildGlassMeshAndSettleParticles();
                    EditorUtility.SetDirty(_targetContainer);
                    SceneView.RepaintAll();
                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp || e.type == EventType.Ignore)
                {
                    _draggingPointIndex = -1;
                    if (_autoRebuildParticlesOnRelease)
                    {
                        _autoRebuildParticlesOnRelease = false;
                        RebuildContainerAndColliders();
                    }
                    Repaint();
                }
            }

            Handles.EndGUI();
            EditorGUILayout.EndVertical();
        }

        private void DrawParametricDimensionsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📐 Hazne Boyutları & Ölçek Kontrolleri", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float spoutW = EditorGUILayout.Slider("Alt Çıkış Genişliği (Spout Width)", _targetContainer.SpoutWidth, 0.15f, 1.20f);
            float spoutL = EditorGUILayout.Slider("Alt Çıkış Uzunluğu (Spout Length)", _targetContainer.SpoutLength, 0.15f, 1.20f);
            float neckW = EditorGUILayout.Slider("Üst Giriş Genişliği (Neck Width)", _targetContainer.NeckWidth, 0.20f, 1.80f);
            float neckL = EditorGUILayout.Slider("Üst Giriş Uzunluğu (Neck Length)", _targetContainer.NeckLength, 0.20f, 1.80f);
            float waistW = EditorGUILayout.Slider("Orta Bel Genişliği (Waist)", _targetContainer.ChamberWaistWidth, 0.40f, 3.20f);
            float wScale = EditorGUILayout.Slider("Genişlik Ölçeği (Width Scale)", _targetContainer.widthScale, 0.5f, 1.8f);
            float hScale = EditorGUILayout.Slider("Yükseklik Ölçeği (Height Scale)", _targetContainer.heightScale, 0.6f, 1.6f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Modify Parametric Glass Dimensions");
                _targetContainer.SpoutWidth = spoutW;
                _targetContainer.SpoutLength = spoutL;
                _targetContainer.NeckWidth = neckW;
                _targetContainer.NeckLength = neckL;
                _targetContainer.ChamberWaistWidth = waistW;
                _targetContainer.widthScale = wScale;
                _targetContainer.heightScale = hScale;
                _targetContainer.RebuildGlassMeshAndSettleParticles();
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSquareRectangleContourBuilderSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📏 Parametrik Kare / Dikdörtgen Kontur Oluşturucu", EditorStyles.boldLabel);

            _funnelBoxWidth = EditorGUILayout.Slider("Genişlik (Width)", _funnelBoxWidth, 0.8f, 3.2f);
            _funnelBoxHeight = EditorGUILayout.Slider("Yükseklik (Height)", _funnelBoxHeight, 0.8f, 3.2f);
            _funnelBoxTension = EditorGUILayout.Slider("Köşe Keskinliği (Corner Tension)", _funnelBoxTension, 0.0f, 1.0f);
            EditorGUILayout.BeginHorizontal();
            _funnelBoxHasSpout = EditorGUILayout.Toggle("Alt Çıkış Hunisi (Spout)", _funnelBoxHasSpout);
            _funnelBoxHasNeck = EditorGUILayout.Toggle("Üst Boğaz (Neck)", _funnelBoxHasNeck);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("✨ Özel Dikdörtgen Konturu Oluştur", GUILayout.Height(28)))
            {
                Undo.RecordObject(_targetContainer, "Configure Rectangular Contour");
                _targetContainer.ConfigureRectangularContainer(_funnelBoxWidth, _funnelBoxHeight, _funnelBoxTension, _funnelBoxHasSpout, _funnelBoxHasNeck, true);
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawContourPointTableSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            _showContourPointTable = EditorGUILayout.Foldout(_showContourPointTable, $"📍 Kontrol Noktaları Tablosu ({_targetContainer.contourPoints.Count} Nokta)", true);
            if (GUILayout.Button("+ Nokta Ekle", GUILayout.Width(95)))
            {
                Undo.RecordObject(_targetContainer, "Add Glass Point");
                var pts = _targetContainer.contourPoints;
                int mid = pts.Count / 2;
                float y = (pts[mid - 1].y + pts[mid].y) * 0.5f;
                float hw = (pts[mid - 1].halfWidth + pts[mid].halfWidth) * 0.5f;
                pts.Insert(mid, new GlassContourPoint(y, hw, 0f, 0f));
                _targetContainer.RebuildGlassMeshAndSettleParticles();
                SyncPhysicsColliders();
                EditorUtility.SetDirty(_targetContainer);
            }
            EditorGUILayout.EndHorizontal();

            if (_showContourPointTable)
            {
                var list = _targetContainer.contourPoints;
                for (int i = 0; i < list.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(26));

                    EditorGUI.BeginChangeCheck();
                    var cp = list[i];
                    cp.y = EditorGUILayout.FloatField(cp.y, GUILayout.Width(58));
                    cp.halfWidth = Mathf.Max(0.08f, EditorGUILayout.FloatField(cp.halfWidth, GUILayout.Width(58)));
                    if (!_targetContainer.symmetricMode)
                        cp.centerX = EditorGUILayout.FloatField(cp.centerX, GUILayout.Width(58));
                    cp.cornerTension = EditorGUILayout.Slider(cp.cornerTension, 0f, 1f);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_targetContainer, "Edit Contour Point");
                        list[i] = cp;
                        _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                        _targetContainer.RebuildGlassMeshAndSettleParticles();
                        SyncPhysicsColliders();
                        EditorUtility.SetDirty(_targetContainer);
                        SceneView.RepaintAll();
                    }

                    GUI.enabled = list.Count > 4 && i > 0 && i < list.Count - 1;
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    {
                        Undo.RecordObject(_targetContainer, "Remove Contour Point");
                        list.RemoveAt(i);
                        _targetContainer.RebuildGlassMeshAndSettleParticles();
                        SyncPhysicsColliders();
                        EditorUtility.SetDirty(_targetContainer);
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawColorAndFrameTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("💎 Hazır Cam Temaları", EditorStyles.boldLabel);
            int cols = 3;
            for (int i = 0; i < ThemeLabels.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && (i + j) < ThemeLabels.Length; j++)
                {
                    int themeIdx = i + j;
                    GlassColorTheme theme = (GlassColorTheme)themeIdx;
                    bool isActive = (_targetContainer.colorTheme == theme);
                    GUI.backgroundColor = isActive ? new Color(0.35f, 0.90f, 0.60f) : Color.white;
                    if (GUILayout.Button(ThemeLabels[themeIdx], GUILayout.Height(26)))
                    {
                        Undo.RecordObject(_targetContainer, "Apply Glass Color Theme");
                        _targetContainer.ApplyColorTheme(theme, true);
                        EditorUtility.SetDirty(_targetContainer);
                        SceneView.RepaintAll();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("🖼️ Hypercasual Çerçeve Paletleri (Rounded Box Frame)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Periwinkle", GUILayout.Height(24))) ApplyFramePalette(new Color(0.50f, 0.50f, 0.90f, 1f), new Color(0.84f, 0.84f, 0.98f, 1f), new Color(0.38f, 0.36f, 0.80f, 1f));
            if (GUILayout.Button("Gök Mavi", GUILayout.Height(24))) ApplyFramePalette(new Color(0.28f, 0.62f, 0.92f, 1f), new Color(0.76f, 0.90f, 0.99f, 1f), new Color(0.18f, 0.44f, 0.76f, 1f));
            if (GUILayout.Button("Nane Yeşil", GUILayout.Height(24))) ApplyFramePalette(new Color(0.30f, 0.76f, 0.62f, 1f), new Color(0.76f, 0.96f, 0.88f, 1f), new Color(0.18f, 0.56f, 0.46f, 1f));
            if (GUILayout.Button("Mercan", GUILayout.Height(24))) ApplyFramePalette(new Color(0.90f, 0.48f, 0.52f, 1f), new Color(0.98f, 0.82f, 0.84f, 1f), new Color(0.70f, 0.32f, 0.38f, 1f));
            if (GUILayout.Button("Arduvaz", GUILayout.Height(24))) ApplyFramePalette(new Color(0.32f, 0.36f, 0.48f, 1f), new Color(0.68f, 0.72f, 0.84f, 1f), new Color(0.20f, 0.22f, 0.32f, 1f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("🎨 Detaylı Renk & Kalınlık Ayarları", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            Color fCol = EditorGUILayout.ColorField("Kutu Dış Çerçeve Rengi", _targetContainer.boxFrameColor);
            Color fhCol = EditorGUILayout.ColorField("Çerçeve Üst Parlama", _targetContainer.boxFrameHighlightColor);
            Color fsCol = EditorGUILayout.ColorField("Çerçeve Kenar Gölgesi", _targetContainer.boxFrameEdgeShadeColor);
            Color shCol = EditorGUILayout.ColorField("Zemin Düşen Gölge", _targetContainer.dropShadowColor);

            EditorGUILayout.Space(4);
            Color bodyCol = EditorGUILayout.ColorField("Cam Gövde Rengi", _targetContainer.glassBodyColor);
            Color glowCol = EditorGUILayout.ColorField("Cam Merkez Rengi", _targetContainer.glassCenterGlowColor);
            Color outerRimCol = EditorGUILayout.ColorField("Dış Kırılma Çerçevesi", _targetContainer.glassOuterRimColor);
            Color innerRimCol = EditorGUILayout.ColorField("İç Kırılma Parıltısı", _targetContainer.glassInnerRimColor);
            Color frontLensCol = EditorGUILayout.ColorField("Ön Lens Rengi", _targetContainer.glassFrontLensColor);

            EditorGUILayout.Space(4);
            float rimRad = EditorGUILayout.Slider("3B Şişe Kenar Kalınlığı", _targetContainer.glassRimRadius, 0.015f, 0.090f);
            float innerInset = EditorGUILayout.Slider("İç Duvar Mesafesi", _targetContainer.innerRimInset, 0.025f, 0.120f);
            float shellDepth = EditorGUILayout.Slider("3B Cam Derinliği", _targetContainer.glassShellDepth, 0.02f, 0.25f);
            int subdiv = EditorGUILayout.IntSlider("Eğri Yumuşaklığı", _targetContainer.curveSubdivisions, 4, 24);

            bool showSpec = EditorGUILayout.Toggle("Cam Parlak Yansımaları", _targetContainer.showSpecularReflections);
            float specInt = _targetContainer.specularIntensity;
            if (showSpec) specInt = EditorGUILayout.Slider("Yansıma Şiddeti", _targetContainer.specularIntensity, 0.0f, 1.0f);

            bool showEars = EditorGUILayout.Toggle("Yan Kulplar (Ears - 3B Şişede)", _targetContainer.showShoulderHandles);
            float earScale = _targetContainer.shoulderHandleScale;
            Vector2 earOffset = _targetContainer.shoulderHandleOffset;
            if (showEars)
            {
                earScale = EditorGUILayout.Slider("Kulp Boyutu", _targetContainer.shoulderHandleScale, 0.4f, 1.8f);
                earOffset = EditorGUILayout.Vector2Field("Kulp Pozisyon Ofseti", _targetContainer.shoulderHandleOffset);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Modify Glass Colors & Styling");
                _targetContainer.boxFrameColor = fCol;
                _targetContainer.boxFrameHighlightColor = fhCol;
                _targetContainer.boxFrameEdgeShadeColor = fsCol;
                _targetContainer.dropShadowColor = shCol;
                _targetContainer.glassBodyColor = bodyCol;
                _targetContainer.glassCenterGlowColor = glowCol;
                _targetContainer.glassOuterRimColor = outerRimCol;
                _targetContainer.glassInnerRimColor = innerRimCol;
                _targetContainer.glassFrontLensColor = frontLensCol;
                _targetContainer.glassRimRadius = rimRad;
                _targetContainer.innerRimInset = innerInset;
                _targetContainer.glassShellDepth = shellDepth;
                _targetContainer.curveSubdivisions = subdiv;
                _targetContainer.showSpecularReflections = showSpec;
                _targetContainer.specularIntensity = specInt;
                _targetContainer.showShoulderHandles = showEars;
                _targetContainer.shoulderHandleScale = earScale;
                _targetContainer.shoulderHandleOffset = earOffset;

                _targetContainer.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndVertical();
        }

        private void ApplyFramePalette(Color main, Color highlight, Color edgeShade)
        {
            Undo.RecordObject(_targetContainer, "Apply Frame Palette");
            _targetContainer.boxFrameColor = main;
            _targetContainer.boxFrameHighlightColor = highlight;
            _targetContainer.boxFrameEdgeShadeColor = edgeShade;
            _targetContainer.RebuildGlassMeshAndSettleParticles();
            EditorUtility.SetDirty(_targetContainer);
            SceneView.RepaintAll();
        }

        private void DrawTraySizeAndStyleTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🟦 Tepsi Hedef & Kayıt Kontrolü (StagingGridBoard)", EditorStyles.boldLabel);

            _stagingBoard = (StagingGridBoard)EditorGUILayout.ObjectField(
                "Hedef Tepsi (Tray)", _stagingBoard, typeof(StagingGridBoard), true);

            if (_stagingBoard == null)
            {
                EditorGUILayout.HelpBox(
                    "Sahnede aktif bir StagingGridBoard bulunamadı.",
                    MessageType.Warning);
                if (GUILayout.Button("Sahnede Ara / Yenile", GUILayout.Height(26)))
                    _stagingBoard = FindFirstObjectByType<StagingGridBoard>();
                EditorGUILayout.EndVertical();
                return;
            }

            if (_gameManager != null)
            {
                bool lockTray = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "🔒 Play Mode'da Sahnedeki Tepsi Ayarlarını Koru",
                        "Açıkken LoadLevel() sahnedeki tepsinin cols/rows, boyut ve renk ayarlarını level verisiyle ezmez."),
                    _gameManager.preserveSceneTrayInPlayMode);
                if (lockTray != _gameManager.preserveSceneTrayInPlayMode)
                {
                    Undo.RecordObject(_gameManager, "Toggle Preserve Scene Tray");
                    _gameManager.preserveSceneTrayInPlayMode = lockTray;
                    EditorUtility.SetDirty(_gameManager);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _targetLevel != null;
            GUI.backgroundColor = new Color(0.32f, 0.84f, 0.56f);
            if (GUILayout.Button("💾 Tepsiyi Level'e Kaydet", GUILayout.Height(28)))
                SaveTraySettingsToLevel(_targetLevel, _stagingBoard);
            GUI.backgroundColor = new Color(0.38f, 0.72f, 0.96f);
            if (GUILayout.Button("📥 Level'dan Tepsi Yükle", GUILayout.Height(28)))
                LoadTraySettingsFromLevel(_targetLevel, _stagingBoard);
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            _stagingBoard.GetOuterWorldDimensions(out float outerW, out float outerH);
            EditorGUILayout.LabelField(
                $"Aktif Ölçü: {_stagingBoard.cols}×{_stagingBoard.rows}  |  Hücre (cellSize): {_stagingBoard.cellSize:0.000}  |  Dış Çerçeve: {outerW:0.00} × {outerH:0.00}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // Grid dimensions & presets
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🧱 Izgara Ölçüleri (Grid Hücre Sayısı)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            DrawTrayGridPresetButton("4×4", 4, 4);
            DrawTrayGridPresetButton("5×3", 5, 3);
            DrawTrayGridPresetButton("5×4", 5, 4);
            DrawTrayGridPresetButton("6×4", 6, 4);
            DrawTrayGridPresetButton("6×5", 6, 5);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            int newCols = EditorGUILayout.IntSlider("Sütun Sayısı (Cols)", _stagingBoard.cols, 2, 7);
            int newRows = EditorGUILayout.IntSlider("Satır Sayısı (Rows)", _stagingBoard.rows, 2, 6);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_stagingBoard, "Change Tray Grid Dimensions");
                _stagingBoard.cols = newCols;
                _stagingBoard.rows = newRows;
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // Physical size, margins, alignment
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📏 Fiziksel Boyut & Konum (Size & Alignment)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool useManualCell = EditorGUILayout.Toggle("Manuel Hücre Boyutu Kullan", _stagingBoard.useManualCellSize);
            float manualCell = _stagingBoard.manualCellSize;
            bool matchGlassW = _stagingBoard.matchGlassWidth;
            float maxW = _stagingBoard.maxBoardWidth;
            float maxH = _stagingBoard.maxBoardHeight;

            if (useManualCell)
            {
                manualCell = EditorGUILayout.Slider("Hücre Boyutu (cellSize)", _stagingBoard.manualCellSize, 0.30f, 0.82f);
            }
            else
            {
                matchGlassW = EditorGUILayout.Toggle("Cam Genişliğiyle Eşle (Match Glass)", _stagingBoard.matchGlassWidth);
                GUI.enabled = !matchGlassW;
                maxW = EditorGUILayout.Slider("Maks. Tepsi Genişliği (Width)", _stagingBoard.maxBoardWidth, 1.60f, 3.80f);
                GUI.enabled = true;
                maxH = EditorGUILayout.Slider("Maks. Tepsi Yüksekliği (Height)", _stagingBoard.maxBoardHeight, 1.40f, 4.20f);
            }

            EditorGUILayout.Space(4);
            float framePad = EditorGUILayout.Slider("Çerçeve Kalınlığı (Frame Padding)", _stagingBoard.framePadding, 0.04f, 0.25f);
            float innerMarg = EditorGUILayout.Slider("İç Boşluk (Inner Margin)", _stagingBoard.innerMargin, 0.01f, 0.20f);
            float cornerRad = EditorGUILayout.Slider("Köşe Yuvarlaklığı (Corner Radius)", _stagingBoard.frameCornerRadius, 0.04f, 0.40f);

            EditorGUILayout.Space(4);
            bool autoAlign = EditorGUILayout.Toggle("Camın Altına Otomatik Hizala", _stagingBoard.autoAlignBelowGlass);
            float gapBelow = _stagingBoard.gapBelowGlass;
            Vector2 center = _stagingBoard.boardCenter;
            if (autoAlign)
            {
                gapBelow = EditorGUILayout.Slider("Cam ile Mesafe (Gap Below Glass)", _stagingBoard.gapBelowGlass, 0.02f, 0.60f);
            }
            else
            {
                center.x = EditorGUILayout.Slider("Tepsi Merkez X", _stagingBoard.boardCenter.x, -1.50f, 1.50f);
                center.y = EditorGUILayout.Slider("Tepsi Merkez Y", _stagingBoard.boardCenter.y, -4.50f, 0.50f);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_stagingBoard, "Resize Tray Settings");
                _stagingBoard.useManualCellSize = useManualCell;
                _stagingBoard.manualCellSize = manualCell;
                _stagingBoard.matchGlassWidth = matchGlassW;
                _stagingBoard.maxBoardWidth = maxW;
                _stagingBoard.maxBoardHeight = maxH;
                _stagingBoard.framePadding = framePad;
                _stagingBoard.innerMargin = innerMarg;
                _stagingBoard.frameCornerRadius = cornerRad;
                _stagingBoard.autoAlignBelowGlass = autoAlign;
                _stagingBoard.gapBelowGlass = gapBelow;
                _stagingBoard.boardCenter = center;
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📐 Camın Altına Hizala & Ölçüleri Uygula", GUILayout.Height(26)))
            {
                Undo.RecordObject(_stagingBoard, "Align Tray Below Glass");
                _stagingBoard.autoAlignBelowGlass = true;
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }
            if (GUILayout.Button("↺ Varsayılan Ölçülere Dön", GUILayout.Height(26), GUILayout.Width(175)))
            {
                Undo.RecordObject(_stagingBoard, "Reset Tray Dimensions");
                _stagingBoard.useManualCellSize = false;
                _stagingBoard.manualCellSize = 0.506f;
                _stagingBoard.matchGlassWidth = true;
                _stagingBoard.maxBoardWidth = 2.822f;
                _stagingBoard.maxBoardHeight = 3.20f;
                _stagingBoard.framePadding = 0.115f;
                _stagingBoard.innerMargin = 0.06f;
                _stagingBoard.frameCornerRadius = 0.18f;
                _stagingBoard.autoAlignBelowGlass = true;
                _stagingBoard.gapBelowGlass = 0.11f;
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // Colors
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🎨 Tepsi Renkleri (Tray Colors)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            Color frameFront = EditorGUILayout.ColorField("Çerçeve Ön Yüz (Frame Front)", _stagingBoard.frameFrontColor);
            Color frameDark = EditorGUILayout.ColorField("Çerçeve Arka / Kenar (Frame Dark)", _stagingBoard.frameDarkColor);
            Color trayFloor = EditorGUILayout.ColorField("Tepsi Zemin (Tray Floor)", _stagingBoard.trayFloorColor);
            Color tileBevel = EditorGUILayout.ColorField("Hücre Kenar (Tile Bevel)", _stagingBoard.tileBevelColor);
            Color tileBody = EditorGUILayout.ColorField("Hücre İç (Tile Body)", _stagingBoard.tileBodyColor);
            Color exitRim = EditorGUILayout.ColorField("Üst Çıkış Kenarı (Exit Rim)", _stagingBoard.exitRimColor);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_stagingBoard, "Change Tray Colors");
                _stagingBoard.frameFrontColor = frameFront;
                _stagingBoard.frameDarkColor = frameDark;
                _stagingBoard.trayFloorColor = trayFloor;
                _stagingBoard.tileBevelColor = tileBevel;
                _stagingBoard.tileBodyColor = tileBody;
                _stagingBoard.exitRimColor = exitRim;
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }

            if (GUILayout.Button("Varsayılan Tepsi Renklerine Sıfırla", GUILayout.Height(24)))
            {
                Undo.RecordObject(_stagingBoard, "Reset Tray Colors");
                _stagingBoard.ResetDefaultColors();
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }
            EditorGUILayout.EndVertical();

            if (_enableSceneHandles)
            {
                EditorGUILayout.HelpBox(
                    "İpucu: Tepsi sekmesi açıkken Scene View'da tepsinin sağ/sol ve alt/üst oklarını sürükleyerek canlı boyutlandırabilirsiniz.",
                    MessageType.Info);
            }
        }

        private void DrawTrayGridPresetButton(string label, int cols, int rows)
        {
            bool active = _stagingBoard != null && _stagingBoard.cols == cols && _stagingBoard.rows == rows;
            GUI.backgroundColor = active ? new Color(0.45f, 0.85f, 1.0f) : Color.white;
            if (GUILayout.Button(label, GUILayout.Height(25)))
            {
                Undo.RecordObject(_stagingBoard, $"Tray Grid Preset {label}");
                _stagingBoard.cols = cols;
                _stagingBoard.rows = rows;
                ApplyTrayChangesAndRefreshMonsters(_stagingBoard);
            }
            GUI.backgroundColor = Color.white;
        }

        private void ApplyTrayChangesAndRefreshMonsters(StagingGridBoard board)
        {
            if (board == null) return;
            board.Configure(board.cols, board.rows, _targetContainer);
            RefreshMonstersInTray(board);
            EditorUtility.SetDirty(board);
            SceneView.RepaintAll();
            Repaint();
        }

        private void RefreshMonstersInTray(StagingGridBoard board)
        {
            if (board == null) return;

            MonsterCharacter[] all = FindObjectsByType<MonsterCharacter>(FindObjectsSortMode.None);
            if (all == null || all.Length == 0) return;

            var activeList = new List<MonsterCharacter>();
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                if (m.assignedSlot != null || m.IsCompleted || m.remainingCount <= 0) continue;
                activeList.Add(m);
            }

            for (int i = 0; i < activeList.Count; i++)
            {
                var m = activeList[i];
                Vector2Int[] cells = CarrierBlockShapeUtility.GetCells(m.blockShape);
                int maxX = 0;
                int maxY = 0;
                for (int c = 0; c < cells.Length; c++)
                {
                    if (cells[c].x > maxX) maxX = cells[c].x;
                    if (cells[c].y > maxY) maxY = cells[c].y;
                }

                Vector2Int anchor = m.gridAnchor;
                anchor.x = Mathf.Clamp(anchor.x, 0, Mathf.Max(0, board.cols - 1 - maxX));
                anchor.y = Mathf.Clamp(anchor.y, 0, Mathf.Max(0, board.rows - 1 - maxY));

                Undo.RecordObject(m.transform, "Update Block Transform For Tray Resize");
                Undo.RecordObject(m, "Update Block Grid State For Tray Resize");
                Vector3 homePos = board.GetWorldPositionForAnchor(anchor, m.blockShape);
                Vector3 gridScale = Vector3.one * board.cellSize;
                Vector3 fitScale = Vector3.one * board.GetSlotFitScaleForShape(m.blockShape);
                bool exitClear = board.IsBlockExitClearToSlots(m, activeList);
                m.SetGridState(anchor, homePos, gridScale, fitScale, exitClear, false);
                EditorUtility.SetDirty(m);
            }
        }

        private void SaveTraySettingsToLevel(PuzzleGlassLevelData lvl, StagingGridBoard board)
        {
            if (lvl == null || board == null) return;
            Undo.RecordObject(lvl, "Save Tray Settings to Level");

            lvl.stagingGridCols = Mathf.Clamp(board.cols, 2, 7);
            lvl.stagingGridRows = Mathf.Clamp(board.rows, 2, 6);
            lvl.useCustomTraySettings = true;
            lvl.trayMatchGlassWidth = board.matchGlassWidth;
            lvl.trayMaxBoardWidth = board.maxBoardWidth;
            lvl.trayMaxBoardHeight = board.maxBoardHeight;
            lvl.trayFramePadding = board.framePadding;
            lvl.trayInnerMargin = board.innerMargin;
            lvl.trayFrameCornerRadius = board.frameCornerRadius;
            lvl.trayGapBelowGlass = board.gapBelowGlass;
            lvl.trayUseManualCellSize = board.useManualCellSize;
            lvl.trayManualCellSize = board.manualCellSize;

            EditorUtility.SetDirty(lvl);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=#32C260><b>[Glass Customizer]</b></color> Tepsi ayarları '{lvl.name}' level asset'ine kaydedildi!");
        }

        private void LoadTraySettingsFromLevel(PuzzleGlassLevelData lvl, StagingGridBoard board)
        {
            if (lvl == null || board == null) return;
            Undo.RecordObject(board, "Load Tray Settings From Level");

            board.cols = Mathf.Clamp(lvl.stagingGridCols, 2, 7);
            board.rows = Mathf.Clamp(lvl.stagingGridRows, 2, 6);
            if (lvl.useCustomTraySettings)
            {
                board.matchGlassWidth = lvl.trayMatchGlassWidth;
                board.maxBoardWidth = Mathf.Clamp(lvl.trayMaxBoardWidth, 1.4f, 4.2f);
                board.maxBoardHeight = Mathf.Clamp(lvl.trayMaxBoardHeight, 1.2f, 4.5f);
                board.framePadding = Mathf.Clamp(lvl.trayFramePadding, 0.03f, 0.30f);
                board.innerMargin = Mathf.Clamp(lvl.trayInnerMargin, 0.01f, 0.25f);
                board.frameCornerRadius = Mathf.Clamp(lvl.trayFrameCornerRadius, 0.02f, 0.45f);
                board.gapBelowGlass = Mathf.Clamp(lvl.trayGapBelowGlass, 0.01f, 0.80f);
                board.useManualCellSize = lvl.trayUseManualCellSize;
                board.manualCellSize = Mathf.Clamp(lvl.trayManualCellSize, 0.25f, 0.90f);
            }

            ApplyTrayChangesAndRefreshMonsters(board);
        }

        private void DrawCompartmentsAndTestTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🧩 Cam İçi Bölmeler & Raflar", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.42f, 0.48f, 0.96f);
            if (GUILayout.Button("🧩 Cam Bölme & Level Tasarımcısını Aç (Compartment Tool)", GUILayout.Height(30)))
            {
                GlassCompartmentEditorWindow.ShowWindow();
            }
            GUI.backgroundColor = Color.white;

            if (_targetContainer.dividers != null && _targetContainer.dividers.Count > 0)
            {
                EditorGUILayout.LabelField($"Aktif Bölme Sayısı: {_targetContainer.dividers.Count}", EditorStyles.miniLabel);
                for (int i = 0; i < _targetContainer.dividers.Count; i++)
                {
                    var d = _targetContainer.dividers[i];
                    if (d == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    bool en = EditorGUILayout.ToggleLeft($"{d.name} ({d.angleDegrees:0}°)", d.enabled);
                    if (en != d.enabled)
                    {
                        Undo.RecordObject(_targetContainer, "Toggle Divider");
                        d.enabled = en;
                        RebuildContainerAndColliders();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("🗑️ Tüm Bölmeleri Temizle", GUILayout.Height(24)))
                {
                    Undo.RecordObject(_targetContainer, "Clear Glass Dividers");
                    _targetContainer.dividers.Clear();
                    _targetContainer.compartmentPreset = GlassCompartmentPreset.None_OrderedPool;
                    RebuildContainerAndColliders();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Bu camda aktif bölme duvarı yok.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            DrawPhysicsCollidersSection();
            EditorGUILayout.Space(6);
            DrawSandTestingSimulationSection();
        }

        private void DrawPhysicsCollidersSection()
        {
            var bcm = _targetContainer.GetComponent<BallContainerManager>().OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🛡️ Fiziksel Duvarlar & Collider Kafesi", EditorStyles.boldLabel);

            if (bcm != null)
            {
                EditorGUI.BeginChangeCheck();
                float wallThick = EditorGUILayout.Slider("İç Duvar Kalınlığı", bcm.wallThickness, 0.04f, 0.25f);
                float wallDepth = EditorGUILayout.Slider("Duvar Z Derinliği", bcm.wallDepth, 0.20f, 0.90f);
                float frontZ = EditorGUILayout.Slider("Ön Cam Düzlemi Z", bcm.frontPlaneZ, -0.40f, -0.15f);
                float backZ = EditorGUILayout.Slider("Arka Cam Düzlemi Z", bcm.backPlaneZ, -0.05f, 0.20f);
                bool showGizmos = EditorGUILayout.Toggle("Collider Gizmos Göster", bcm.showColliderGizmos);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(bcm, "Modify Collider Settings");
                    bcm.wallThickness = wallThick;
                    bcm.wallDepth = wallDepth;
                    bcm.frontPlaneZ = frontZ;
                    bcm.backPlaneZ = backZ;
                    bcm.showColliderGizmos = showGizmos;
                    bcm.RebuildColliders();
                    EditorUtility.SetDirty(bcm);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("🛡️ Collider Kafesini Şimdi Yeniden Oluştur", GUILayout.Height(28)))
                {
                    SyncPhysicsColliders();
                }
            }
            else
            {
                if (GUILayout.Button("BallContainerManager Ekle", GUILayout.Height(26)))
                    _targetContainer.gameObject.AddComponent<BallContainerManager>();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSandTestingSimulationSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🧪 Canlı Top / Kum Testi", EditorStyles.boldLabel);

            var engine = _targetContainer.GetComponent<SandSimulationEngine>();
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.35f, 0.85f, 1.0f);
            if (GUILayout.Button("🧪 Doldur (Generate)", GUILayout.Height(28)))
            {
                if (engine != null)
                {
                    var pat = PixelArtLibrary.Resolve(_targetContainer.pixelArtPreset, _targetContainer.customPattern);
                    engine.InitializeFromPattern(pat);
                    EditorUtility.SetDirty(engine);
                }
                _targetContainer.SpawnTestSandParticles(64);
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.95f, 0.45f, 0.45f);
            if (GUILayout.Button("🧹 Temizle (Clear)", GUILayout.Height(28)))
            {
                if (engine != null) { engine.ClearAll(); EditorUtility.SetDirty(engine); }
                _targetContainer.ClearTestSandParticles();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            bool gateOpen = _targetContainer.IsSpoutGateOpen();
            GUI.backgroundColor = gateOpen ? new Color(0.35f, 0.90f, 0.55f) : new Color(1.0f, 0.65f, 0.25f);
            if (GUILayout.Button(gateOpen ? "🚪 Kapağı Kapat" : "🚪 Kapağı Aç", GUILayout.Height(28)))
            {
                _targetContainer.ToggleSpoutGate();
                if (engine != null) engine.ToggleSpoutGate();
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("🎨 Sand Image Generator'ı Aç", GUILayout.Height(26)))
                SandImageGeneratorWindow.OpenWindow();

            EditorGUILayout.EndVertical();
        }

        private void DrawJsonLibraryTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("💾 Cam Şekli JSON Kaydet / Yükle", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.30f, 0.85f, 0.48f);
            if (GUILayout.Button("💾 Mevcut Camı JSON Olarak Kaydet", GUILayout.Height(30)))
                SaveCurrentShapeToJson();

            GUI.backgroundColor = new Color(0.95f, 0.76f, 0.28f);
            if (GUILayout.Button("📂 Dosyadan JSON Yükle", GUILayout.Height(30)))
                LoadShapeFromJson();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            string dir = "Assets/GlassShapes";
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.json");
                if (files.Length > 0)
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Kayıtlı Cam Şekilleri (Assets/GlassShapes):", EditorStyles.miniBoldLabel);
                    foreach (string file in files)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(file));
                        if (GUILayout.Button("Uygula", GUILayout.Width(80)))
                        {
                            LoadShapeFromJsonPath(file);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_enableSceneHandles) return;

            if (_activeTab == 2 && _stagingBoard != null)
            {
                DrawTraySceneHandles(_stagingBoard);
                return;
            }

            if (_targetContainer == null) return;

            Transform tr = _targetContainer.transform;

            if (_targetContainer.IsFlatBottomBox)
            {
                _targetContainer.GetRoundedBoxPoolParameters(out float w, out float h, out float bottomY);
                float hw = w * 0.5f;
                float topY = bottomY + h;
                float midY = (bottomY + topY) * 0.5f;

                Vector3 worldRight = tr.TransformPoint(new Vector3(hw, midY, -0.25f));
                Vector3 worldLeft = tr.TransformPoint(new Vector3(-hw, midY, -0.25f));
                Vector3 worldTop = tr.TransformPoint(new Vector3(0f, topY, -0.25f));
                Vector3 worldBottom = tr.TransformPoint(new Vector3(0f, bottomY, -0.25f));

                Handles.color = new Color(0.25f, 0.92f, 0.65f, 0.95f);
                float handleSize = HandleUtility.GetHandleSize(worldRight) * 0.09f;

                EditorGUI.BeginChangeCheck();
                Vector3 newRight = Handles.Slider(worldRight, tr.right, handleSize, Handles.CubeHandleCap, 0.02f);
                Vector3 newLeft = Handles.Slider(worldLeft, -tr.right, handleSize, Handles.CubeHandleCap, 0.02f);
                Vector3 newTop = Handles.Slider(worldTop, tr.up, handleSize, Handles.CubeHandleCap, 0.02f);
                Vector3 newBottom = Handles.Slider(worldBottom, -tr.up, handleSize, Handles.CubeHandleCap, 0.02f);
                if (EditorGUI.EndChangeCheck())
                {
                    float localRX = tr.InverseTransformPoint(newRight).x;
                    float localLX = tr.InverseTransformPoint(newLeft).x;
                    float localTY = tr.InverseTransformPoint(newTop).y;
                    float localBY = tr.InverseTransformPoint(newBottom).y;

                    float targetW = Mathf.Abs(localRX - hw) > 0.001f ? localRX * 2f : -localLX * 2f;
                    float targetBottom = Mathf.Abs(localBY - bottomY) > 0.001f ? localBY : bottomY;
                    float targetTop = Mathf.Abs(localTY - topY) > 0.001f ? localTY : topY;

                    Undo.RecordObject(_targetContainer, "Resize Box Glass in Scene");
                    _boxWidth = Mathf.Clamp(targetW, 0.80f, 3.40f);
                    _boxBottomY = Mathf.Clamp(targetBottom, -1.00f, targetTop - 0.50f);
                    _boxHeight = Mathf.Clamp(targetTop - _boxBottomY, 0.60f, 2.80f);
                    _targetContainer.ConfigureRoundedBoxPool(
                        _boxWidth, _boxHeight, _targetContainer.boxCornerRadius, _targetContainer.boxFrameThickness, _boxBottomY, true);
                    SyncPhysicsColliders();
                    EditorUtility.SetDirty(_targetContainer);
                    Repaint();
                }

                Handles.color = new Color(0.25f, 0.92f, 0.65f, 0.55f);
                Vector3 bl = tr.TransformPoint(new Vector3(-hw, bottomY, -0.25f));
                Vector3 br = tr.TransformPoint(new Vector3(hw, bottomY, -0.25f));
                Vector3 tl = tr.TransformPoint(new Vector3(-hw, topY, -0.25f));
                Vector3 trp = tr.TransformPoint(new Vector3(hw, topY, -0.25f));
                Handles.DrawPolyLine(bl, br, trp, tl, bl);
            }
            else
            {
                var pts = _targetContainer.contourPoints;
                if (pts == null || pts.Count == 0) return;

                float wScale = Mathf.Max(0.1f, _targetContainer.widthScale);
                float hScale = Mathf.Max(0.1f, _targetContainer.heightScale);

                for (int i = 0; i < pts.Count; i++)
                {
                    var cp = pts[i];
                    float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                    float hw = cp.halfWidth * wScale;
                    float y = cp.y * hScale;

                    Vector3 rWorld = tr.TransformPoint(new Vector3(cx + hw, y, -0.26f));
                    Vector3 lWorld = tr.TransformPoint(new Vector3(cx - hw, y, -0.26f));

                    float sz = HandleUtility.GetHandleSize(rWorld) * 0.075f;
                    Handles.color = new Color(0.30f, 0.92f, 0.65f, 0.95f);

                    EditorGUI.BeginChangeCheck();
                    Vector3 movedR = Handles.Slider2D(rWorld, tr.forward, tr.right, tr.up, sz, Handles.SphereHandleCap, 0.01f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector3 localR = tr.InverseTransformPoint(movedR);
                        Undo.RecordObject(_targetContainer, "Move Glass Contour Handle");
                        UpdateControlPointFromDrag(i, true, new Vector2(localR.x, localR.y));
                        _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                        _targetContainer.RebuildGlassMeshAndSettleParticles();
                        SyncPhysicsColliders();
                        EditorUtility.SetDirty(_targetContainer);
                        Repaint();
                    }

                    if (!_targetContainer.symmetricMode)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 movedL = Handles.Slider2D(lWorld, tr.forward, tr.right, tr.up, sz, Handles.SphereHandleCap, 0.01f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Vector3 localL = tr.InverseTransformPoint(movedL);
                            Undo.RecordObject(_targetContainer, "Move Left Glass Contour Handle");
                            UpdateControlPointFromDrag(i, false, new Vector2(localL.x, localL.y));
                            _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                            _targetContainer.RebuildGlassMeshAndSettleParticles();
                            SyncPhysicsColliders();
                            EditorUtility.SetDirty(_targetContainer);
                            Repaint();
                        }
                    }
                    else
                    {
                        Handles.color = new Color(0.30f, 0.92f, 0.65f, 0.45f);
                        Handles.SphereHandleCap(0, lWorld, Quaternion.identity, sz * 0.8f, EventType.Repaint);
                    }
                }
            }
        }

        private void DrawTraySceneHandles(StagingGridBoard board)
        {
            if (board == null) return;

            board.GetOuterWorldDimensions(out float outerW, out float outerH);
            float hw = outerW * 0.5f;
            float hh = outerH * 0.5f;
            float cx = board.boardCenter.x;
            float cy = board.boardCenter.y;

            Vector3 worldRight = new Vector3(cx + hw, cy, -0.25f);
            Vector3 worldLeft = new Vector3(cx - hw, cy, -0.25f);
            Vector3 worldTop = new Vector3(cx, cy + hh, -0.25f);
            Vector3 worldBottom = new Vector3(cx, cy - hh, -0.25f);

            Handles.color = new Color(0.35f, 0.78f, 1.0f, 0.95f);
            float handleSize = HandleUtility.GetHandleSize(worldRight) * 0.09f;

            EditorGUI.BeginChangeCheck();
            Vector3 newRight = Handles.Slider(worldRight, Vector3.right, handleSize, Handles.CubeHandleCap, 0.02f);
            Vector3 newLeft = Handles.Slider(worldLeft, Vector3.left, handleSize, Handles.CubeHandleCap, 0.02f);
            Vector3 newTop = Handles.Slider(worldTop, Vector3.up, handleSize, Handles.CubeHandleCap, 0.02f);
            Vector3 newBottom = Handles.Slider(worldBottom, Vector3.down, handleSize, Handles.CubeHandleCap, 0.02f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(board, "Resize Tray in Scene");

                if (Mathf.Abs(newRight.x - worldRight.x) > 0.001f || Mathf.Abs(newLeft.x - worldLeft.x) > 0.001f)
                {
                    float draggedHalfW = Mathf.Abs(newRight.x - worldRight.x) > 0.001f
                        ? Mathf.Abs(newRight.x - cx)
                        : Mathf.Abs(cx - newLeft.x);
                    float targetOuterW = Mathf.Clamp(draggedHalfW * 2f, 1.40f, 3.80f);

                    if (board.useManualCellSize)
                    {
                        float gridW = Mathf.Max(0.4f, targetOuterW - (board.framePadding * 2f + board.innerMargin * 2f));
                        board.manualCellSize = Mathf.Clamp(gridW / Mathf.Max(1, board.cols), 0.30f, 0.82f);
                    }
                    else
                    {
                        board.matchGlassWidth = false;
                        board.maxBoardWidth = targetOuterW;
                    }
                }

                if (Mathf.Abs(newBottom.y - worldBottom.y) > 0.001f)
                {
                    float topEdgeY = cy + hh;
                    float targetOuterH = Mathf.Clamp(topEdgeY - newBottom.y, 1.20f, 4.20f);
                    if (board.useManualCellSize)
                    {
                        float gridH = Mathf.Max(0.4f, targetOuterH - (board.framePadding * 2f + board.innerMargin * 2f));
                        board.manualCellSize = Mathf.Clamp(gridH / Mathf.Max(1, board.rows), 0.30f, 0.82f);
                    }
                    else
                    {
                        board.maxBoardHeight = targetOuterH;
                    }
                }

                if (Mathf.Abs(newTop.y - worldTop.y) > 0.001f)
                {
                    float deltaY = newTop.y - worldTop.y;
                    if (board.autoAlignBelowGlass)
                    {
                        board.gapBelowGlass = Mathf.Clamp(board.gapBelowGlass - deltaY, 0.02f, 0.60f);
                    }
                    else
                    {
                        board.boardCenter = new Vector2(cx, Mathf.Clamp(cy + deltaY, -4.50f, 0.50f));
                    }
                }

                ApplyTrayChangesAndRefreshMonsters(board);
            }

            Handles.color = new Color(0.35f, 0.78f, 1.0f, 0.55f);
            Vector3 bl = new Vector3(cx - hw, cy - hh, -0.25f);
            Vector3 br = new Vector3(cx + hw, cy - hh, -0.25f);
            Vector3 tl = new Vector3(cx - hw, cy + hh, -0.25f);
            Vector3 trp = new Vector3(cx + hw, cy + hh, -0.25f);
            Handles.DrawPolyLine(bl, br, trp, tl, bl);
        }

        private void RebuildContainerAndColliders()
        {
            if (_targetContainer == null) return;
            _targetContainer.RebuildContainer();
            SyncPhysicsColliders();
            EditorUtility.SetDirty(_targetContainer);
            SceneView.RepaintAll();
        }

        private void SyncPhysicsColliders()
        {
            if (_targetContainer == null) return;
            var bcm = _targetContainer.GetComponent<BallContainerManager>().OrNull() ?? FindFirstObjectByType<BallContainerManager>();
            if (bcm != null)
            {
                bcm.RebuildColliders();
                EditorUtility.SetDirty(bcm);
            }
            SceneView.RepaintAll();
        }

        private void AlignStagingGridBelowGlass()
        {
            if (_targetContainer == null) return;
            var board = FindFirstObjectByType<StagingGridBoard>();
            if (board == null) return;

            Undo.RecordObject(board, "Align Staging Grid Below Glass");
            board.Configure(board.cols, board.rows, _targetContainer);
            EditorUtility.SetDirty(board);
            SceneView.RepaintAll();
        }

        private void SaveContainerShapeToLevel(PuzzleGlassLevelData lvl)
        {
            if (lvl == null || _targetContainer == null) return;
            Undo.RecordObject(lvl, "Save Custom Glass Shape to Level");

            lvl.useCustomGlassShape = true;
            lvl.glassPreset = _targetContainer.shapePreset;
            lvl.symmetricMode = _targetContainer.symmetricMode;
            lvl.curveSubdivisions = _targetContainer.curveSubdivisions;
            lvl.widthScale = _targetContainer.widthScale;
            lvl.heightScale = _targetContainer.heightScale;

            lvl.colorTheme = _targetContainer.colorTheme;
            lvl.glassBodyColor = _targetContainer.glassBodyColor;
            lvl.glassCenterGlowColor = _targetContainer.glassCenterGlowColor;
            lvl.glassOuterRimColor = _targetContainer.glassOuterRimColor;
            lvl.glassInnerRimColor = _targetContainer.glassInnerRimColor;
            lvl.glassFrontLensColor = _targetContainer.glassFrontLensColor;
            lvl.earHandleColor = _targetContainer.earHandleColor;
            lvl.glassRimRadius = _targetContainer.glassRimRadius;
            lvl.innerRimInset = _targetContainer.innerRimInset;
            lvl.glassShellDepth = _targetContainer.glassShellDepth;
            lvl.showShoulderHandles = _targetContainer.showShoulderHandles;
            lvl.showSpecularReflections = _targetContainer.showSpecularReflections;

            lvl.boxCornerRadius = _targetContainer.boxCornerRadius;
            lvl.boxFrameThickness = _targetContainer.boxFrameThickness;
            lvl.boxFrameColor = _targetContainer.boxFrameColor;
            lvl.boxFrameHighlightColor = _targetContainer.boxFrameHighlightColor;
            lvl.boxFrameEdgeShadeColor = _targetContainer.boxFrameEdgeShadeColor;
            lvl.dropShadowColor = _targetContainer.dropShadowColor;

            lvl.contourPoints = new List<GlassContourPoint>(_targetContainer.contourPoints);

            EditorUtility.SetDirty(lvl);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=#32C260><b>[Glass Customizer]</b></color> Cam şekli '{lvl.name}' level asset'ine kaydedildi!");
        }

        private void LoadLevelShapeIntoContainer(PuzzleGlassLevelData lvl)
        {
            if (lvl == null || _targetContainer == null) return;
            Undo.RecordObject(_targetContainer, "Load Glass Shape From Level");

            _targetContainer.colorTheme = lvl.colorTheme;
            _targetContainer.glassBodyColor = lvl.glassBodyColor;
            _targetContainer.glassCenterGlowColor = lvl.glassCenterGlowColor;
            _targetContainer.glassOuterRimColor = lvl.glassOuterRimColor;
            _targetContainer.glassInnerRimColor = lvl.glassInnerRimColor;
            _targetContainer.glassFrontLensColor = lvl.glassFrontLensColor;
            _targetContainer.earHandleColor = lvl.earHandleColor;
            _targetContainer.glassRimRadius = lvl.glassRimRadius;
            _targetContainer.innerRimInset = lvl.innerRimInset;
            _targetContainer.glassShellDepth = lvl.glassShellDepth;
            _targetContainer.showShoulderHandles = lvl.showShoulderHandles;
            _targetContainer.showSpecularReflections = lvl.showSpecularReflections;

            if (lvl.boxCornerRadius > 0.01f) _targetContainer.boxCornerRadius = lvl.boxCornerRadius;
            if (lvl.boxFrameThickness > 0.01f) _targetContainer.boxFrameThickness = lvl.boxFrameThickness;
            if (lvl.boxFrameColor.a > 0.01f) _targetContainer.boxFrameColor = lvl.boxFrameColor;
            if (lvl.boxFrameHighlightColor.a > 0.01f) _targetContainer.boxFrameHighlightColor = lvl.boxFrameHighlightColor;
            if (lvl.boxFrameEdgeShadeColor.a > 0.01f) _targetContainer.boxFrameEdgeShadeColor = lvl.boxFrameEdgeShadeColor;
            if (lvl.dropShadowColor.a > 0.01f) _targetContainer.dropShadowColor = lvl.dropShadowColor;

            _targetContainer.shapePreset = lvl.useCustomGlassShape ? lvl.glassPreset : GlassShapePreset.RectangleBox;
            _targetContainer.symmetricMode = lvl.symmetricMode;
            _targetContainer.curveSubdivisions = Mathf.Clamp(lvl.curveSubdivisions, 4, 24);
            _targetContainer.widthScale = Mathf.Clamp(lvl.widthScale, 0.5f, 1.8f);
            _targetContainer.heightScale = Mathf.Clamp(lvl.heightScale, 0.6f, 1.6f);

            if (lvl.useCustomGlassShape && lvl.contourPoints != null && lvl.contourPoints.Count >= 3)
            {
                _targetContainer.contourPoints = new List<GlassContourPoint>(lvl.contourPoints);
                _targetContainer.SyncBoundsFromContour();
            }
            else
            {
                _targetContainer.ApplyShapePreset(_targetContainer.shapePreset, false);
            }

            SyncLocalBoxCacheFromContainer();
            RebuildContainerAndColliders();

            if (_stagingBoard != null && lvl.useCustomTraySettings)
                LoadTraySettingsFromLevel(lvl, _stagingBoard);

            Repaint();
        }

        private void InsertControlPointAtLocalPosition(Vector2 localClick)
        {
            Undo.RecordObject(_targetContainer, "Insert Glass Contour Point");
            var pts = _targetContainer.contourPoints;
            float wScale = Mathf.Max(0.1f, _targetContainer.widthScale);
            float hScale = Mathf.Max(0.1f, _targetContainer.heightScale);

            float newY = localClick.y / hScale;
            float newHW = Mathf.Clamp(Mathf.Abs(localClick.x) / wScale, 0.12f, 1.85f);

            int insertIdx = pts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].y > newY)
                {
                    insertIdx = i;
                    break;
                }
            }

            insertIdx = Mathf.Clamp(insertIdx, 1, pts.Count - 1);
            pts.Insert(insertIdx, new GlassContourPoint(newY, newHW, 0f, 0f));
            _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
            _targetContainer.RebuildGlassMeshAndSettleParticles();
            SyncPhysicsColliders();
            EditorUtility.SetDirty(_targetContainer);
        }

        private void UpdateControlPointFromDrag(int index, bool isRightSide, Vector2 localPos)
        {
            var pts = _targetContainer.contourPoints;
            var cp = pts[index];
            float wScale = Mathf.Max(0.1f, _targetContainer.widthScale);
            float hScale = Mathf.Max(0.1f, _targetContainer.heightScale);

            float targetY = localPos.y / hScale;
            if (index > 0) targetY = Mathf.Max(targetY, pts[index - 1].y + 0.04f);
            if (index < pts.Count - 1) targetY = Mathf.Min(targetY, pts[index + 1].y - 0.04f);
            cp.y = targetY;

            if (_targetContainer.symmetricMode)
            {
                cp.centerX = 0f;
                cp.halfWidth = Mathf.Clamp(Mathf.Abs(localPos.x) / wScale, 0.10f, 1.85f);
            }
            else
            {
                float oldLeft = cp.centerX - cp.halfWidth;
                float oldRight = cp.centerX + cp.halfWidth;
                float draggedX = localPos.x / wScale;

                if (isRightSide)
                    oldRight = Mathf.Max(oldLeft + 0.20f, draggedX);
                else
                    oldLeft = Mathf.Min(oldRight - 0.20f, draggedX);

                cp.centerX = (oldLeft + oldRight) * 0.5f;
                cp.halfWidth = Mathf.Clamp((oldRight - oldLeft) * 0.5f, 0.10f, 1.85f);
            }

            pts[index] = cp;
        }

        private void SaveCurrentShapeToJson()
        {
            string dir = "Assets/GlassShapes";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = EditorUtility.SaveFilePanel("Cam Şeklini Kaydet", dir, $"GlassShape_{_targetContainer.shapePreset}", "json");
            if (string.IsNullOrEmpty(path)) return;

            SavedGlassShapeData data = new SavedGlassShapeData
            {
                shapeName = Path.GetFileNameWithoutExtension(path),
                shapePreset = _targetContainer.shapePreset,
                symmetricMode = _targetContainer.symmetricMode,
                curveSubdivisions = _targetContainer.curveSubdivisions,
                widthScale = _targetContainer.widthScale,
                heightScale = _targetContainer.heightScale,
                colorTheme = _targetContainer.colorTheme,
                glassBodyColor = _targetContainer.glassBodyColor,
                glassCenterGlowColor = _targetContainer.glassCenterGlowColor,
                glassOuterRimColor = _targetContainer.glassOuterRimColor,
                glassInnerRimColor = _targetContainer.glassInnerRimColor,
                glassFrontLensColor = _targetContainer.glassFrontLensColor,
                earHandleColor = _targetContainer.earHandleColor,
                boxCornerRadius = _targetContainer.boxCornerRadius,
                boxFrameThickness = _targetContainer.boxFrameThickness,
                boxFrameColor = _targetContainer.boxFrameColor,
                boxFrameHighlightColor = _targetContainer.boxFrameHighlightColor,
                boxFrameEdgeShadeColor = _targetContainer.boxFrameEdgeShadeColor,
                dropShadowColor = _targetContainer.dropShadowColor,
                glassRimRadius = _targetContainer.glassRimRadius,
                innerRimInset = _targetContainer.innerRimInset,
                glassShellDepth = _targetContainer.glassShellDepth,
                showShoulderHandles = _targetContainer.showShoulderHandles,
                shoulderHandleOffset = _targetContainer.shoulderHandleOffset,
                shoulderHandleScale = _targetContainer.shoulderHandleScale,
                showSpecularReflections = _targetContainer.showSpecularReflections,
                specularIntensity = _targetContainer.specularIntensity,
                particleSize = _targetContainer.particleSize,
                particleFillRatio = _targetContainer.particleFillRatio,
                floatingBeadCount = _targetContainer.floatingBeadCount,
                contourPoints = new List<GlassContourPoint>(_targetContainer.contourPoints)
            };

            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            AssetDatabase.Refresh();
            Debug.Log($"<color=#2ED170><b>[Glass Customizer]</b></color> Cam şekli kaydedildi: {path}");
        }

        private void LoadShapeFromJson()
        {
            string dir = "Assets/GlassShapes";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = EditorUtility.OpenFilePanel("Cam Şekli Yükle", dir, "json");
            if (!string.IsNullOrEmpty(path)) LoadShapeFromJsonPath(path);
        }

        private void LoadShapeFromJsonPath(string path)
        {
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            SavedGlassShapeData data = JsonUtility.FromJson<SavedGlassShapeData>(json);
            if (data == null || data.contourPoints == null || data.contourPoints.Count < 3) return;

            Undo.RecordObject(_targetContainer, "Load Glass Shape JSON");
            _targetContainer.shapePreset = data.shapePreset;
            _targetContainer.symmetricMode = data.symmetricMode;
            _targetContainer.curveSubdivisions = data.curveSubdivisions;
            _targetContainer.widthScale = data.widthScale;
            _targetContainer.heightScale = data.heightScale;
            _targetContainer.colorTheme = data.colorTheme;
            _targetContainer.glassBodyColor = data.glassBodyColor;
            _targetContainer.glassCenterGlowColor = data.glassCenterGlowColor;
            _targetContainer.glassOuterRimColor = data.glassOuterRimColor;
            _targetContainer.glassInnerRimColor = data.glassInnerRimColor;
            _targetContainer.glassFrontLensColor = data.glassFrontLensColor;
            _targetContainer.earHandleColor = data.earHandleColor;
            if (data.boxCornerRadius > 0.01f) _targetContainer.boxCornerRadius = data.boxCornerRadius;
            if (data.boxFrameThickness > 0.01f) _targetContainer.boxFrameThickness = data.boxFrameThickness;
            if (data.boxFrameColor.a > 0.01f) _targetContainer.boxFrameColor = data.boxFrameColor;
            if (data.boxFrameHighlightColor.a > 0.01f) _targetContainer.boxFrameHighlightColor = data.boxFrameHighlightColor;
            if (data.boxFrameEdgeShadeColor.a > 0.01f) _targetContainer.boxFrameEdgeShadeColor = data.boxFrameEdgeShadeColor;
            if (data.dropShadowColor.a > 0.01f) _targetContainer.dropShadowColor = data.dropShadowColor;
            _targetContainer.glassRimRadius = data.glassRimRadius;
            _targetContainer.innerRimInset = data.innerRimInset;
            _targetContainer.glassShellDepth = data.glassShellDepth;
            _targetContainer.showShoulderHandles = data.showShoulderHandles;
            _targetContainer.shoulderHandleOffset = data.shoulderHandleOffset;
            _targetContainer.shoulderHandleScale = data.shoulderHandleScale;
            _targetContainer.showSpecularReflections = data.showSpecularReflections;
            _targetContainer.specularIntensity = data.specularIntensity;
            _targetContainer.particleSize = data.particleSize;
            _targetContainer.particleFillRatio = data.particleFillRatio;
            _targetContainer.floatingBeadCount = data.floatingBeadCount;
            _targetContainer.contourPoints = new List<GlassContourPoint>(data.contourPoints);

            SyncLocalBoxCacheFromContainer();
            RebuildContainerAndColliders();
            Repaint();
        }
    }
}
