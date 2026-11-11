using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    public class GlassCompartmentEditorWindow : EditorWindow
    {
        private ShapedGlassContainer _container;
        private PuzzleGlassGameManager _gameManager;
        private PuzzleGlassLevelData _selectedLevel;
        private List<PuzzleGlassLevelData> _allLevels = new List<PuzzleGlassLevelData>();

        private Vector2 _scrollPos;
        private int _selectedDividerIndex = -1;
        private bool _isDraggingDivider = false;
        private bool _autoRebuildScene = true;

        // Canvas coordinate mapping (matches RectangleBox inner bounds)
        private const float POOL_HALF_W = 1.32f;
        private const float POOL_MIN_Y = -0.56f;
        private const float POOL_MAX_Y = 0.62f;

        [MenuItem("Tools/PuzzleGlass/🧩 Cam Bölme & Level Tasarımcısı (Compartment Tool)", false, 2)]
        public static void ShowWindow()
        {
            var win = GetWindow<GlassCompartmentEditorWindow>("🧩 Cam Bölme Aracı");
            win.minSize = new Vector2(460f, 680f);
            win.RefreshReferences();
            win.Show();
        }

        private void OnEnable()
        {
            RefreshReferences();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void RefreshReferences()
        {
            if (_container == null)
                _container = FindFirstObjectByType<ShapedGlassContainer>();
            if (_gameManager == null)
                _gameManager = FindFirstObjectByType<PuzzleGlassGameManager>();

            _allLevels.Clear();
            string[] guids = AssetDatabase.FindAssets("t:PuzzleGlassLevelData");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var lvl = AssetDatabase.LoadAssetAtPath<PuzzleGlassLevelData>(path);
                if (lvl != null) _allLevels.Add(lvl);
            }
            _allLevels.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));

            if (_selectedLevel == null)
            {
                if (_gameManager != null && _gameManager.currentLevel != null)
                    _selectedLevel = _gameManager.currentLevel;
                else if (_allLevels.Count > 0)
                    _selectedLevel = _allLevels[0];
            }
        }

        private void OnGUI()
        {
            if (_container == null || _gameManager == null)
            {
                RefreshReferences();
            }

            DrawHeaderBanner();

            if (_container == null)
            {
                EditorGUILayout.HelpBox("Sahnede ShapedGlassContainer bulunamadı! Lütfen SampleScene sahnesini açın.", MessageType.Warning);
                if (GUILayout.Button("Sahneyi Otomatik Kur (Setup Scene)", GUILayout.Height(30)))
                {
                    PuzzleGlassSceneSetup.BuildCompleteScene();
                    RefreshReferences();
                }
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawLevelSelectorSection();
            GUILayout.Space(6);

            DrawPresetTemplatesSection();
            GUILayout.Space(6);

            DrawInteractive2DCanvas();
            GUILayout.Space(6);

            DrawDividersListSection();
            GUILayout.Space(6);

            DrawCompartmentColorZonesSection();
            GUILayout.Space(12);

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeaderBanner()
        {
            Rect bannerRect = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bannerRect, new Color(0.24f, 0.28f, 0.58f, 1f));

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.82f, 0.90f, 1f, 1f) }
            };

            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 4f, bannerRect.width, 20f), "🧩 CAM BÖLME & LEVEL TASARIMCISI", titleStyle);
            GUI.Label(new Rect(bannerRect.x, bannerRect.y + 23f, bannerRect.width, 16f), "Her Level İçin Farklı Cam Bölmeler, Raflar ve Renk Havuzları Tasarlayın", subStyle);
        }

        private void DrawLevelSelectorSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("1. Level Seçimi & Kayıt (Her Level Farklı Bölme)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var newLevel = (PuzzleGlassLevelData)EditorGUILayout.ObjectField("Aktif Level Asset:", _selectedLevel, typeof(PuzzleGlassLevelData), false);
            if (newLevel != _selectedLevel)
            {
                _selectedLevel = newLevel;
                if (_selectedLevel != null)
                {
                    LoadSelectedLevelIntoScene();
                }
            }

            if (GUILayout.Button("➕ Yeni Level", GUILayout.Width(95f)))
            {
                CreateNewLevelAsset();
            }
            EditorGUILayout.EndHorizontal();

            if (_allLevels.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < _allLevels.Count; i++)
                {
                    var lvl = _allLevels[i];
                    if (lvl == null) continue;
                    bool isSelected = (lvl == _selectedLevel);
                    Color prevBg = GUI.backgroundColor;
                    if (isSelected) GUI.backgroundColor = new Color(0.45f, 0.85f, 1f, 1f);
                    if (GUILayout.Button($"L{lvl.levelNumber}: {lvl.name.Replace("Level_", "")}", GUILayout.Height(22f)))
                    {
                        _selectedLevel = lvl;
                        LoadSelectedLevelIntoScene();
                    }
                    GUI.backgroundColor = prevBg;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            _autoRebuildScene = EditorGUILayout.ToggleLeft("Canlı Sahne & Fizik Önizleme (Anında Güncelle)", _autoRebuildScene);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.28f, 0.85f, 0.48f, 1f);
            if (GUILayout.Button("💾 Bu Bölmeleri Seçili Level'a Kaydet", GUILayout.Height(28f)))
            {
                SaveCurrentToSelectedLevel();
            }

            GUI.backgroundColor = new Color(0.35f, 0.72f, 0.98f, 1f);
            if (GUILayout.Button("🔄 Sahneyi Yeniden Oluştur", GUILayout.Height(28f), GUILayout.Width(175f)))
            {
                RebuildSceneContainer();
            }
            GUI.backgroundColor = oldColor;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawPresetTemplatesSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("2. Hazır Bölme Şablonları (Tek Tıkla Uygula)", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("1. Bölmesiz (3 Sıra)", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.None_OrderedPool);
            if (GUILayout.Button("2. Sol / Sağ (2 Bölme)", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.TwoVertical_LeftRight);
            if (GUILayout.Button("3. 3 Sütun Bölme", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.ThreeVertical_Columns);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("4. Ters-T (Alt Tam + Üst 2)", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.InvertedT_BottomRowPlusSplitTop);
            if (GUILayout.Button("5. Üst-T (Alt 2 + Üst Tam)", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.TopT_SplitBottomPlusTopRow);
            if (GUILayout.Button("6. V-Huni Kanallar", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.VFunnel_SlopedChannels);
            if (GUILayout.Button("7. Zikzak Raflar", GUILayout.Height(26f)))
                ApplyPresetToContainer(GlassCompartmentPreset.ZigZag_Shelves);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawInteractive2DCanvas()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("3. İnteraktif Cam Havuz Önizleme (Bölmeyi Tıkla & Sürükle | Shift+Tık: Yeni Bölme)", EditorStyles.boldLabel);

            Rect canvasRect = GUILayoutUtility.GetRect(420f, 210f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(canvasRect, new Color(0.96f, 0.93f, 0.88f, 1f)); // Warm cream background

            float padX = 24f;
            float padY = 18f;
            Rect poolRect = new Rect(
                canvasRect.x + padX,
                canvasRect.y + padY,
                canvasRect.width - padX * 2f,
                canvasRect.height - padY * 2f);

            // Outer periwinkle glass frame
            Rect frameRect = new Rect(poolRect.x - 8f, poolRect.y - 8f, poolRect.width + 16f, poolRect.height + 16f);
            EditorGUI.DrawRect(frameRect, new Color(0.49f, 0.47f, 0.96f, 1f));
            // Inner white-cyan rim
            Rect rimRect = new Rect(poolRect.x - 2f, poolRect.y - 2f, poolRect.width + 4f, poolRect.height + 4f);
            EditorGUI.DrawRect(rimRect, new Color(0.92f, 0.98f, 1.00f, 1f));
            // Sky-blue interior
            EditorGUI.DrawRect(poolRect, new Color(0.68f, 0.85f, 1.00f, 1f));

            // Draw subtle grid lines
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            float midX = poolRect.center.x;
            Handles.DrawLine(new Vector3(midX, poolRect.yMin), new Vector3(midX, poolRect.yMax));
            for (int r = 0; r < 3; r++)
            {
                float rowLocalY = -0.44f + r * (0.24f * 0.855f);
                Vector2 guiPt = LocalToCanvas(new Vector2(0f, rowLocalY), poolRect);
                Handles.DrawLine(new Vector3(poolRect.xMin, guiPt.y), new Vector3(poolRect.xMax, guiPt.y));
            }

            // Draw active balls inside the pool preview
            if (BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls != null)
            {
                var balls = BallObjectPool.Instance.ActiveBalls;
                float radiusPx = (0.12f / (POOL_HALF_W * 2f)) * poolRect.width;
                for (int i = 0; i < balls.Count; i++)
                {
                    var b = balls[i];
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    Vector3 localPos = _container.transform.InverseTransformPoint(b.transform.position);
                    Vector2 p = LocalToCanvas(new Vector2(localPos.x, localPos.y), poolRect);
                    Handles.color = PuzzleColorPalette.GetBodyColor(b.colorType);
                    Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, radiusPx * 0.92f);
                    Handles.color = new Color(1f, 1f, 1f, 0.55f);
                    Handles.DrawWireDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, radiusPx * 0.92f);
                }
            }

            // Draw and interact with dividers
            if (_container.dividers == null) _container.dividers = new List<GlassDividerData>();

            Event e = Event.current;
            for (int i = 0; i < _container.dividers.Count; i++)
            {
                var div = _container.dividers[i];
                if (div == null) continue;

                Vector2 cGui = LocalToCanvas(div.center, poolRect);
                float rad = div.angleDegrees * Mathf.Deg2Rad;
                Vector2 dirLocal = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                Vector2 endA = LocalToCanvas(div.center - dirLocal * (div.length * 0.5f), poolRect);
                Vector2 endB = LocalToCanvas(div.center + dirLocal * (div.length * 0.5f), poolRect);

                bool isSel = (i == _selectedDividerIndex);
                float lineThick = Mathf.Max(6f, (div.thickness / (POOL_HALF_W * 2f)) * poolRect.width);

                Handles.color = !div.enabled
                    ? new Color(0.5f, 0.5f, 0.5f, 0.5f)
                    : (isSel ? new Color(1.0f, 0.88f, 0.20f, 1f) : new Color(0.42f, 0.39f, 0.94f, 1f));
                Handles.DrawAAPolyLine(lineThick + 4f, new Vector3(endA.x, endA.y, 0f), new Vector3(endB.x, endB.y, 0f));

                Handles.color = !div.enabled
                    ? new Color(0.75f, 0.75f, 0.75f, 0.5f)
                    : new Color(0.88f, 0.96f, 1.00f, 1f);
                Handles.DrawAAPolyLine(lineThick, new Vector3(endA.x, endA.y, 0f), new Vector3(endB.x, endB.y, 0f));

                Handles.color = isSel ? new Color(1f, 0.45f, 0.1f, 1f) : new Color(0.28f, 0.25f, 0.80f, 1f);
                Handles.DrawSolidDisc(new Vector3(cGui.x, cGui.y, 0f), Vector3.forward, isSel ? 6.5f : 5f);
            }
            Handles.EndGUI();

            // Mouse interaction inside poolRect
            if (poolRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (e.shift)
                    {
                        Undo.RecordObject(_container, "Add Glass Divider");
                        Vector2 localClick = CanvasToLocal(e.mousePosition, poolRect);
                        _container.compartmentPreset = GlassCompartmentPreset.Custom;
                        _container.dividers.Add(new GlassDividerData($"Bölme {_container.dividers.Count + 1}", localClick, 0.76f, 0.085f, 90f, true));
                        _selectedDividerIndex = _container.dividers.Count - 1;
                        _isDraggingDivider = true;
                        if (_autoRebuildScene) RebuildSceneContainer();
                        e.Use();
                    }
                    else
                    {
                        int hitIdx = FindDividerAtGuiPos(e.mousePosition, poolRect);
                        _selectedDividerIndex = hitIdx;
                        _isDraggingDivider = (hitIdx >= 0);
                        Repaint();
                        e.Use();
                    }
                }
                else if (e.type == EventType.MouseDown && e.button == 1)
                {
                    int hitIdx = FindDividerAtGuiPos(e.mousePosition, poolRect);
                    if (hitIdx >= 0 && hitIdx < _container.dividers.Count)
                    {
                        Undo.RecordObject(_container, "Remove Glass Divider");
                        _container.compartmentPreset = GlassCompartmentPreset.Custom;
                        _container.dividers.RemoveAt(hitIdx);
                        _selectedDividerIndex = -1;
                        if (_autoRebuildScene) RebuildSceneContainer();
                        Repaint();
                        e.Use();
                    }
                }
                else if (e.type == EventType.MouseDrag && _isDraggingDivider && _selectedDividerIndex >= 0 && _selectedDividerIndex < _container.dividers.Count)
                {
                    Undo.RecordObject(_container, "Move Glass Divider");
                    Vector2 newLocal = CanvasToLocal(e.mousePosition, poolRect);
                    newLocal.x = Mathf.Clamp(newLocal.x, -1.18f, 1.18f);
                    newLocal.y = Mathf.Clamp(newLocal.y, -0.48f, 0.50f);
                    _container.compartmentPreset = GlassCompartmentPreset.Custom;
                    _container.dividers[_selectedDividerIndex].center = newLocal;
                    if (_autoRebuildScene) RebuildSceneContainer();
                    Repaint();
                    e.Use();
                }
            }

            if (e.type == EventType.MouseUp)
            {
                _isDraggingDivider = false;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDividersListSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"4. Cam Bölmeler Listesi ({(_container.dividers != null ? _container.dividers.Count : 0)} Adet)", EditorStyles.boldLabel);

            if (GUILayout.Button("➕ Dikey Bölme", GUILayout.Width(105f)))
            {
                AddNewDivider("Dikey Bölme", new Vector2(0f, -0.02f), 0.96f, 0.085f, 90f);
            }
            if (GUILayout.Button("➕ Yatay Raf", GUILayout.Width(95f)))
            {
                AddNewDivider("Yatay Raf", new Vector2(-0.35f, -0.12f), 0.85f, 0.080f, 0f);
            }
            if (GUILayout.Button("➕ Eğimli Kanal", GUILayout.Width(105f)))
            {
                AddNewDivider("Eğimli Kanal", new Vector2(-0.52f, -0.02f), 0.75f, 0.080f, -26f);
            }
            EditorGUILayout.EndHorizontal();

            if (_container.dividers == null || _container.dividers.Count == 0)
            {
                EditorGUILayout.HelpBox("Şu an iç bölme yok (Standart 3 katmanlı sıralı top havuzu). Yukarıdaki şablonlardan seçebilir veya '+ Dikey Bölme' butonuna tıklayabilirsiniz.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < _container.dividers.Count; i++)
                {
                    var div = _container.dividers[i];
                    if (div == null) continue;

                    bool isSel = (i == _selectedDividerIndex);
                    Color prevBg = GUI.backgroundColor;
                    if (isSel) GUI.backgroundColor = new Color(0.85f, 0.94f, 1.0f, 1f);

                    EditorGUILayout.BeginVertical("helpBox");
                    GUI.backgroundColor = prevBg;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    div.enabled = EditorGUILayout.Toggle(div.enabled, GUILayout.Width(18f));
                    div.name = EditorGUILayout.TextField(div.name);

                    if (GUILayout.Button("Seç", GUILayout.Width(42f)))
                    {
                        _selectedDividerIndex = i;
                    }
                    if (GUILayout.Button("⇄ Simetri İkizi", GUILayout.Width(95f)))
                    {
                        Undo.RecordObject(_container, "Mirror Divider");
                        _container.dividers.Add(new GlassDividerData(
                            div.name + " (Simetri)",
                            new Vector2(-div.center.x, div.center.y),
                            div.length,
                            div.thickness,
                            div.angleDegrees == 90f ? 90f : -div.angleDegrees,
                            div.enabled));
                        if (_autoRebuildScene) RebuildSceneContainer();
                        break;
                    }
                    if (GUILayout.Button("✖", GUILayout.Width(26f)))
                    {
                        Undo.RecordObject(_container, "Delete Divider");
                        _container.dividers.RemoveAt(i);
                        if (_selectedDividerIndex == i) _selectedDividerIndex = -1;
                        if (_autoRebuildScene) RebuildSceneContainer();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    float cx = EditorGUILayout.Slider("Yatay Konum (X)", div.center.x, -1.20f, 1.20f);
                    float cy = EditorGUILayout.Slider("Dikey Konum (Y)", div.center.y, -0.50f, 0.52f);
                    div.center = new Vector2(cx, cy);
                    div.length = EditorGUILayout.Slider("Uzunluk (Length)", div.length, 0.15f, 2.40f);
                    div.thickness = EditorGUILayout.Slider("Kalınlık (Thickness)", div.thickness, 0.04f, 0.18f);
                    div.angleDegrees = EditorGUILayout.Slider("Açı (Angle °)", div.angleDegrees, -90f, 90f);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Hızlı Açı:", GUILayout.Width(65f));
                    if (GUILayout.Button("Dikey (90°)")) div.angleDegrees = 90f;
                    if (GUILayout.Button("Yatay (0°)")) div.angleDegrees = 0f;
                    if (GUILayout.Button("Sol Eğim (-28°)")) div.angleDegrees = -28f;
                    if (GUILayout.Button("Sağ Eğim (+28°)")) div.angleDegrees = 28f;
                    EditorGUILayout.EndHorizontal();

                    if (EditorGUI.EndChangeCheck())
                    {
                        _selectedDividerIndex = i;
                        _container.compartmentPreset = GlassCompartmentPreset.Custom;
                        EditorUtility.SetDirty(_container);
                        if (_autoRebuildScene) RebuildSceneContainer();
                    }

                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCompartmentColorZonesSection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("5. Bölme Renk Atamaları (Hangi Bölmede Hangi Renk Top Olsun?)", EditorStyles.boldLabel);
            if (GUILayout.Button("➕ Renk Bölgesi Ekle", GUILayout.Width(135f)))
            {
                Undo.RecordObject(_container, "Add Compartment Color Zone");
                if (_container.compartmentZones == null) _container.compartmentZones = new List<CompartmentColorZone>();
                _container.compartmentZones.Add(new CompartmentColorZone("Yeni Bölge", -1f, 1f, 0, PuzzleColor.Blue));
                if (_autoRebuildScene) RebuildSceneContainer();
            }
            if (_container.compartmentZones != null && _container.compartmentZones.Count > 0)
            {
                if (GUILayout.Button("Temizle", GUILayout.Width(65f)))
                {
                    Undo.RecordObject(_container, "Clear Compartment Zones");
                    _container.compartmentZones.Clear();
                    if (_autoRebuildScene) RebuildSceneContainer();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_container.compartmentZones == null || _container.compartmentZones.Count == 0)
            {
                EditorGUILayout.HelpBox("Özel bölme renk kuralı yok: Satırlar otomatik olarak Level canavar sırasına göre (En Alt -> Orta -> En Üst) dizilir.", MessageType.None);
            }
            else
            {
                for (int i = 0; i < _container.compartmentZones.Count; i++)
                {
                    var z = _container.compartmentZones[i];
                    if (z == null) continue;

                    EditorGUILayout.BeginVertical("helpBox");
                    EditorGUI.BeginChangeCheck();

                    EditorGUILayout.BeginHorizontal();
                    z.zoneName = EditorGUILayout.TextField(z.zoneName, GUILayout.Width(110f));
                    z.color = (PuzzleColor)EditorGUILayout.EnumPopup(z.color, GUILayout.Width(90f));

                    string[] rowLabels = new string[] { "Tüm Satırlar (-1)", "0: En Alt Sıra", "1: Orta Sıra", "2: Üst Sıra", "3: 4. Sıra" };
                    int[] rowValues = new int[] { -1, 0, 1, 2, 3 };
                    z.rowLayer = EditorGUILayout.IntPopup(z.rowLayer, rowLabels, rowValues);

                    if (GUILayout.Button("✖", GUILayout.Width(26f)))
                    {
                        Undo.RecordObject(_container, "Remove Zone");
                        _container.compartmentZones.RemoveAt(i);
                        if (_autoRebuildScene) RebuildSceneContainer();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    float minX = z.normalizedXRange.x;
                    float maxX = z.normalizedXRange.y;
                    EditorGUILayout.MinMaxSlider($"Yatay Bölge [{minX:F2} .. {maxX:F2}]", ref minX, ref maxX, -1.0f, 1.0f);
                    z.normalizedXRange = new Vector2(minX, maxX);

                    if (GUILayout.Button("Sol Yarı", GUILayout.Width(62f))) z.normalizedXRange = new Vector2(-1f, 0f);
                    if (GUILayout.Button("Sağ Yarı", GUILayout.Width(62f))) z.normalizedXRange = new Vector2(0f, 1f);
                    if (GUILayout.Button("Tam Boy", GUILayout.Width(62f))) z.normalizedXRange = new Vector2(-1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_container);
                        if (_autoRebuildScene) RebuildSceneContainer();
                    }
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void AddNewDivider(string name, Vector2 center, float len, float thick, float angle)
        {
            Undo.RecordObject(_container, "Add Glass Divider");
            if (_container.dividers == null) _container.dividers = new List<GlassDividerData>();
            _container.compartmentPreset = GlassCompartmentPreset.Custom;
            _container.dividers.Add(new GlassDividerData(name, center, len, thick, angle, true));
            _selectedDividerIndex = _container.dividers.Count - 1;
            EditorUtility.SetDirty(_container);
            if (_autoRebuildScene) RebuildSceneContainer();
        }

        private void ApplyPresetToContainer(GlassCompartmentPreset preset)
        {
            if (_container == null) return;
            Undo.RecordObject(_container, "Apply Compartment Preset");
            _container.ApplyCompartmentPreset(preset, true);
            EditorUtility.SetDirty(_container);
            SceneView.RepaintAll();
            Repaint();
        }

        private void RebuildSceneContainer()
        {
            if (_container == null) return;
            if (!_container.enabled) _container.enabled = true;
            _container.CleanOrphanedChildren();
            _container.RebuildContainer();
            EditorUtility.SetDirty(_container);
            SceneView.RepaintAll();
        }

        private void LoadSelectedLevelIntoScene()
        {
            if (_selectedLevel == null) return;
            if (_gameManager != null)
            {
                _gameManager.LoadLevel(_selectedLevel);
            }
            else if (_container != null)
            {
                _container.compartmentPreset = _selectedLevel.compartmentPreset;
                _container.dividers = new List<GlassDividerData>();
                if (_selectedLevel.dividers != null)
                {
                    for (int i = 0; i < _selectedLevel.dividers.Count; i++)
                        _container.dividers.Add(_selectedLevel.dividers[i].Clone());
                }
                _container.compartmentZones = new List<CompartmentColorZone>();
                if (_selectedLevel.compartmentZones != null)
                {
                    for (int i = 0; i < _selectedLevel.compartmentZones.Count; i++)
                        _container.compartmentZones.Add(_selectedLevel.compartmentZones[i].Clone());
                }
                _container.RebuildContainer();
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private void SaveCurrentToSelectedLevel()
        {
            if (_selectedLevel == null || _container == null) return;

            Undo.RecordObject(_selectedLevel, "Save Compartments to Level Data");
            _selectedLevel.glassPreset = GlassShapePreset.RectangleBox;
            _selectedLevel.compartmentPreset = _container.compartmentPreset;

            _selectedLevel.dividers = new List<GlassDividerData>();
            if (_container.dividers != null)
            {
                for (int i = 0; i < _container.dividers.Count; i++)
                {
                    if (_container.dividers[i] != null)
                        _selectedLevel.dividers.Add(_container.dividers[i].Clone());
                }
            }

            _selectedLevel.compartmentZones = new List<CompartmentColorZone>();
            if (_container.compartmentZones != null)
            {
                for (int i = 0; i < _container.compartmentZones.Count; i++)
                {
                    if (_container.compartmentZones[i] != null)
                        _selectedLevel.compartmentZones.Add(_container.compartmentZones[i].Clone());
                }
            }

            // Also update level monster quotas to match the exact balls currently spawned in the container
            if (BallObjectPool.Instance != null && BallObjectPool.Instance.ActiveBalls != null && BallObjectPool.Instance.ActiveBalls.Count > 0)
            {
                List<PuzzleColor> order = new List<PuzzleColor>();
                Dictionary<PuzzleColor, int> counts = new Dictionary<PuzzleColor, int>();
                var balls = BallObjectPool.Instance.ActiveBalls;
                for (int i = 0; i < balls.Count; i++)
                {
                    var b = balls[i];
                    if (b == null) continue;
                    if (!counts.ContainsKey(b.colorType))
                    {
                        counts[b.colorType] = 0;
                        order.Add(b.colorType);
                    }
                    counts[b.colorType]++;
                }

                _selectedLevel.monsters.Clear();
                for (int i = 0; i < order.Count; i++)
                {
                    PuzzleColor c = order[i];
                    _selectedLevel.monsters.Add(new LevelMonsterEntry(c, counts[c]));
                }
            }

            EditorUtility.SetDirty(_selectedLevel);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=#2ED170><b>[Cam Bölme Aracı]</b></color> '{_selectedLevel.name}' level verisine {_selectedLevel.dividers.Count} bölme ve {_selectedLevel.compartmentZones.Count} renk bölgesi kaydedildi!");
        }

        private void CreateNewLevelAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Levels"))
            {
                AssetDatabase.CreateFolder("Assets", "Levels");
            }

            int nextNum = _allLevels.Count + 1;
            string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/Levels/Level_{nextNum:00}.asset");
            var newAsset = CreateInstance<PuzzleGlassLevelData>();
            newAsset.levelNumber = nextNum;
            newAsset.levelName = $"Level {nextNum} - Bölmeli Cam Havuz";
            newAsset.glassPreset = GlassShapePreset.RectangleBox;

            AssetDatabase.CreateAsset(newAsset, path);
            AssetDatabase.SaveAssets();

            if (_gameManager != null && _gameManager.levels != null && !_gameManager.levels.Contains(newAsset))
            {
                Undo.RecordObject(_gameManager, "Add New Level to GameManager");
                _gameManager.levels.Add(newAsset);
                EditorUtility.SetDirty(_gameManager);
            }

            RefreshReferences();
            _selectedLevel = newAsset;
            SaveCurrentToSelectedLevel();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_container == null || _container.dividers == null || _container.dividers.Count == 0) return;

            for (int i = 0; i < _container.dividers.Count; i++)
            {
                var div = _container.dividers[i];
                if (div == null || !div.enabled) continue;

                Vector3 worldPos = _container.transform.TransformPoint(new Vector3(div.center.x, div.center.y, -0.10f));
                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, Quaternion.Euler(0f, 0f, div.angleDegrees));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_container, "Move Glass Divider in Scene");
                    Vector3 newLocal = _container.transform.InverseTransformPoint(newWorldPos);
                    div.center = new Vector2(
                        Mathf.Clamp(newLocal.x, -1.20f, 1.20f),
                        Mathf.Clamp(newLocal.y, -0.50f, 0.52f));
                    _selectedDividerIndex = i;
                    _container.compartmentPreset = GlassCompartmentPreset.Custom;
                    if (_autoRebuildScene) RebuildSceneContainer();
                    Repaint();
                }
            }
        }

        private static Vector2 LocalToCanvas(Vector2 localPt, Rect poolRect)
        {
            float nx = Mathf.InverseLerp(-POOL_HALF_W, POOL_HALF_W, localPt.x);
            float ny = Mathf.InverseLerp(POOL_MIN_Y, POOL_MAX_Y, localPt.y);
            return new Vector2(
                Mathf.Lerp(poolRect.xMin, poolRect.xMax, nx),
                Mathf.Lerp(poolRect.yMax, poolRect.yMin, ny));
        }

        private static Vector2 CanvasToLocal(Vector2 guiPt, Rect poolRect)
        {
            float nx = Mathf.InverseLerp(poolRect.xMin, poolRect.xMax, guiPt.x);
            float ny = Mathf.InverseLerp(poolRect.yMax, poolRect.yMin, guiPt.y);
            return new Vector2(
                Mathf.Lerp(-POOL_HALF_W, POOL_HALF_W, nx),
                Mathf.Lerp(POOL_MIN_Y, POOL_MAX_Y, ny));
        }

        private int FindDividerAtGuiPos(Vector2 mouseGui, Rect poolRect)
        {
            if (_container == null || _container.dividers == null) return -1;
            Vector2 localMouse = CanvasToLocal(mouseGui, poolRect);
            for (int i = _container.dividers.Count - 1; i >= 0; i--)
            {
                var div = _container.dividers[i];
                if (div != null && div.OverlapsCircle(localMouse, 0.08f, 0.02f))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
