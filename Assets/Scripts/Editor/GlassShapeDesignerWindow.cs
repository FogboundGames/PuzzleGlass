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
        public int curveSubdivisions = 12;
        public float widthScale = 1f;
        public float heightScale = 1f;
        public GlassColorTheme colorTheme;
        public Color glassBodyColor;
        public Color glassCenterGlowColor;
        public Color glassOuterRimColor;
        public Color glassInnerRimColor;
        public Color glassFrontLensColor;
        public Color earHandleColor;
        public float glassRimRadius = 0.042f;
        public float innerRimInset = 0.064f;
        public float glassShellDepth = 0.08f;
        public bool showShoulderHandles = true;
        public Vector2 shoulderHandleOffset;
        public float shoulderHandleScale = 1f;
        public bool showSpecularReflections = true;
        public float specularIntensity = 0.68f;
        public float particleSize = 0.155f;
        public float particleFillRatio = 0.48f;
        public int floatingBeadCount = 0;
        public List<GlassContourPoint> contourPoints = new List<GlassContourPoint>();
    }

    public class GlassShapeDesignerWindow : EditorWindow
    {
        private ShapedGlassContainer _targetContainer;
        private Vector2 _scrollPos;
        private int _draggingPointIndex = -1;
        private bool _draggingRightSide = true;
        private bool _autoRebuildParticlesOnRelease = false;

        private static readonly string[] PresetLabels = new string[]
        {
            "🔵 Geniş Bombeli Flask (Referans)",
            "🧪 Klasik İksir Şişesi",
            "⏳ Kum Saati Cam",
            "💙 Kalp Cam Şişe",
            "💎 Elmas Kristal Karaf",
            "📐 Konik Laboratuvar Şişesi",
            "⛄ Çift Boğumlu Şişe",
            "🏺 Dalgalı Amfora Vazo",
            "🐍 S-Kıvrımlı Kaydırak Tüp"
        };

        private static readonly string[] ThemeLabels = new string[]
        {
            "💧 Gök Mavisi (Referans)",
            "💎 Şeffaf Kristal",
            "🟢 Zümrüt Yeşili",
            "🌸 Pembe Kuvars",
            "🟣 Mor Ametist",
            "🟠 Kehribar Sarısı"
        };

        [MenuItem("Tools/PuzzleGlass/✨ Glass Shape Studio (Cam Şekli Tasarım Aracı)", false, 1)]
        public static void OpenWindow()
        {
            var win = GetWindow<GlassShapeDesignerWindow>("Glass Shape Studio");
            win.minSize = new Vector2(520f, 680f);
            win.Show();
        }

        public static void OpenForContainer(ShapedGlassContainer container)
        {
            var win = GetWindow<GlassShapeDesignerWindow>("Glass Shape Studio");
            win._targetContainer = container;
            win.minSize = new Vector2(520f, 680f);
            win.Show();
        }

        private void OnEnable()
        {
            FindTargetContainer();
        }

        private void FindTargetContainer()
        {
            if (_targetContainer == null)
            {
                if (Selection.activeGameObject != null)
                {
                    _targetContainer = Selection.activeGameObject.GetComponentInParent<ShapedGlassContainer>();
                }
                if (_targetContainer == null)
                {
                    _targetContainer = FindFirstObjectByType<ShapedGlassContainer>();
                }
            }
        }

        private void OnGUI()
        {
            FindTargetContainer();

            DrawTopBanner();

            if (_targetContainer == null)
            {
                EditorGUILayout.HelpBox(
                    "Sahnede aktif bir ShapedGlassContainer bulunamadı. Lütfen sahneyi oluşturun veya bir ShapedGlassContainer objesi seçin.",
                    MessageType.Warning);
                if (GUILayout.Button("Sahneyi Otomatik Oluştur", GUILayout.Height(34)))
                {
                    PuzzleGlassSceneSetup.BuildCompleteScene();
                    FindTargetContainer();
                }
                return;
            }

            _targetContainer.EnsureContourInitialized();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // 1. Interactive 2D Blueprint Canvas
            DrawInteractive2DCanvas();

            EditorGUILayout.Space(8);

            // 2. Ready-to-Use Glass Shape Presets
            DrawPresetsSection();

            EditorGUILayout.Space(8);

            // 3. Glass Color Themes & Material Aesthetics
            DrawColorThemeAndAestheticsSection();

            EditorGUILayout.Space(8);

            // 4. Parametric Dimensions & Particle Settings
            DrawDimensionsAndParticlesSection();

            EditorGUILayout.Space(8);

            // 5. Point-by-Point Contour Table & Save/Load JSON
            DrawContourPointListAndSaveLoadSection();

            EditorGUILayout.Space(16);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTopBanner()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("✨ PuzzleGlass - Cam Şekli Tasarım Stüdyosu ✨", titleStyle, GUILayout.Height(24));

            GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            EditorGUILayout.LabelField(
                "Aşağıdaki 2B tuvalden veya Scene ekranından noktaları sürükleyerek camı istediğiniz herhangi bir şekle sokabilirsiniz.",
                subStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawInteractive2DCanvas()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🎨 Canlı 2B Cam Profili Tuvali", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool newSym = GUILayout.Toggle(_targetContainer.symmetricMode, "Simetrik Mod", "Button", GUILayout.Width(110));
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
                EditorUtility.SetDirty(_targetContainer);
            }

            if (GUILayout.Button("Sahne Objesini Seç", GUILayout.Width(130)))
            {
                Selection.activeGameObject = _targetContainer.gameObject;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "• Sol Tık + Sürükle: Noktayı taşı   • Shift + Sol Tık: Yeni nokta ekle   • Sağ Tık: Noktayı sil",
                EditorStyles.miniLabel);

            Rect canvasRect = GUILayoutUtility.GetRect(460f, 300f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(canvasRect, new Color(0.11f, 0.14f, 0.20f, 1f));

            // Coordinate mapping between local flask space (x: -2.0..2.0, y: -1.45..2.85) and canvasRect
            float minX = -2.0f;
            float maxX = 2.0f;
            float minY = -1.45f;
            float maxY = 2.85f;

            Func<Vector2, Vector2> ToCanvas = (Vector2 localPt) =>
            {
                float nx = Mathf.InverseLerp(minX, maxX, localPt.x);
                float ny = Mathf.InverseLerp(minY, maxY, localPt.y);
                return new Vector2(
                    Mathf.Lerp(canvasRect.xMin + 16f, canvasRect.xMax - 16f, nx),
                    Mathf.Lerp(canvasRect.yMax - 12f, canvasRect.yMin + 12f, ny));
            };

            Func<Vector2, Vector2> FromCanvas = (Vector2 guiPt) =>
            {
                float nx = Mathf.InverseLerp(canvasRect.xMin + 16f, canvasRect.xMax - 16f, guiPt.x);
                float ny = Mathf.InverseLerp(canvasRect.yMax - 12f, canvasRect.yMin + 12f, guiPt.y);
                return new Vector2(
                    Mathf.Lerp(minX, maxX, nx),
                    Mathf.Lerp(minY, maxY, ny));
            };

            Handles.BeginGUI();

            // Draw subtle grid lines
            Handles.color = new Color(1f, 1f, 1f, 0.06f);
            for (float gx = -1.5f; gx <= 1.5f; gx += 0.5f)
            {
                Vector2 p0 = ToCanvas(new Vector2(gx, minY));
                Vector2 p1 = ToCanvas(new Vector2(gx, maxY));
                Handles.DrawLine(p0, p1);
            }
            for (float gy = -1.0f; gy <= 2.5f; gy += 0.5f)
            {
                Vector2 p0 = ToCanvas(new Vector2(minX, gy));
                Vector2 p1 = ToCanvas(new Vector2(maxX, gy));
                Handles.DrawLine(p0, p1);
            }

            // Center vertical axis
            Handles.color = new Color(0.4f, 0.8f, 1f, 0.25f);
            Handles.DrawDottedLine(ToCanvas(new Vector2(0f, minY)), ToCanvas(new Vector2(0f, maxY)), 3f);

            // Generate current smooth paths
            _targetContainer.GenerateFlaskPaths(
                out Vector3[] leftPath,
                out Vector3[] rightPath,
                out Vector3[] leftInner,
                out Vector3[] rightInner);

            // Fill translucent interior strips
            if (leftPath != null && rightPath != null && leftPath.Length > 1)
            {
                Color fillCol = _targetContainer.glassBodyColor;
                fillCol.a = 0.28f;
                Handles.color = fillCol;
                for (int i = 0; i < leftPath.Length - 1; i++)
                {
                    Vector3[] quad = new Vector3[4]
                    {
                        ToCanvas(leftPath[i]),
                        ToCanvas(rightPath[i]),
                        ToCanvas(rightPath[i + 1]),
                        ToCanvas(leftPath[i + 1])
                    };
                    Handles.DrawAAConvexPolygon(quad);
                }

                // Draw inner cyan rim curves
                Handles.color = _targetContainer.glassInnerRimColor;
                for (int i = 0; i < leftInner.Length - 1; i++)
                {
                    Handles.DrawAAPolyLine(2.0f, ToCanvas(leftInner[i]), ToCanvas(leftInner[i + 1]));
                    Handles.DrawAAPolyLine(2.0f, ToCanvas(rightInner[i]), ToCanvas(rightInner[i + 1]));
                }

                // Draw outer thick rim curves
                Handles.color = _targetContainer.glassOuterRimColor;
                for (int i = 0; i < leftPath.Length - 1; i++)
                {
                    Handles.DrawAAPolyLine(4.0f, ToCanvas(leftPath[i]), ToCanvas(leftPath[i + 1]));
                    Handles.DrawAAPolyLine(4.0f, ToCanvas(rightPath[i]), ToCanvas(rightPath[i + 1]));
                }
            }

            // Draw and handle control points
            Event e = Event.current;
            var pts = _targetContainer.contourPoints;
            float wScale = _targetContainer.widthScale;
            float hScale = _targetContainer.heightScale;

            for (int i = 0; i < pts.Count; i++)
            {
                var cp = pts[i];
                float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                float hw = cp.halfWidth * wScale;
                float y = cp.y * hScale;

                Vector2 leftGui = ToCanvas(new Vector2(cx - hw, y));
                Vector2 rightGui = ToCanvas(new Vector2(cx + hw, y));

                // Horizontal strut line between left & right control points
                Handles.color = new Color(1f, 1f, 1f, 0.14f);
                Handles.DrawLine(leftGui, rightGui);

                bool isSelected = (_draggingPointIndex == i);
                Color nodeColor = isSelected
                    ? new Color(1f, 0.9f, 0.2f, 1f)
                    : (cp.cornerTension > 0.5f ? new Color(1f, 0.55f, 0.25f, 1f) : new Color(0.25f, 0.95f, 1f, 1f));

                Handles.color = nodeColor;
                Handles.DrawSolidDisc(leftGui, Vector3.forward, isSelected ? 6.5f : 5.0f);
                Handles.DrawSolidDisc(rightGui, Vector3.forward, isSelected ? 6.5f : 5.0f);

                Handles.color = Color.black;
                Handles.DrawWireDisc(leftGui, Vector3.forward, isSelected ? 6.5f : 5.0f);
                Handles.DrawWireDisc(rightGui, Vector3.forward, isSelected ? 6.5f : 5.0f);
            }

            // Handle mouse events inside canvasRect
            if (canvasRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (e.shift)
                    {
                        // Insert new control point at clicked Y
                        Vector2 localClick = FromCanvas(e.mousePosition);
                        InsertControlPointAtLocalPosition(localClick);
                        e.Use();
                    }
                    else
                    {
                        // Find closest control point handle
                        float bestDist = 14f;
                        int bestIdx = -1;
                        bool bestRight = true;

                        for (int i = 0; i < pts.Count; i++)
                        {
                            var cp = pts[i];
                            float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                            float hw = cp.halfWidth * wScale;
                            float y = cp.y * hScale;

                            Vector2 leftGui = ToCanvas(new Vector2(cx - hw, y));
                            Vector2 rightGui = ToCanvas(new Vector2(cx + hw, y));

                            float dL = Vector2.Distance(e.mousePosition, leftGui);
                            if (dL < bestDist)
                            {
                                bestDist = dL;
                                bestIdx = i;
                                bestRight = false;
                            }

                            float dR = Vector2.Distance(e.mousePosition, rightGui);
                            if (dR < bestDist)
                            {
                                bestDist = dR;
                                bestIdx = i;
                                bestRight = true;
                            }
                        }

                        if (bestIdx >= 0)
                        {
                            if (e.control && pts.Count > 4 && bestIdx > 0 && bestIdx < pts.Count - 1)
                            {
                                Undo.RecordObject(_targetContainer, "Delete Glass Contour Point");
                                pts.RemoveAt(bestIdx);
                                _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                                _targetContainer.RebuildGlassMeshAndSettleParticles();
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
                    // Right-click to delete closest point
                    for (int i = 1; i < pts.Count - 1; i++)
                    {
                        var cp = pts[i];
                        float cx = (_targetContainer.symmetricMode ? 0f : cp.centerX) * wScale;
                        float hw = cp.halfWidth * wScale;
                        float y = cp.y * hScale;

                        Vector2 leftGui = ToCanvas(new Vector2(cx - hw, y));
                        Vector2 rightGui = ToCanvas(new Vector2(cx + hw, y));

                        if (Vector2.Distance(e.mousePosition, leftGui) < 12f ||
                            Vector2.Distance(e.mousePosition, rightGui) < 12f)
                        {
                            if (pts.Count > 4)
                            {
                                Undo.RecordObject(_targetContainer, "Delete Glass Contour Point");
                                pts.RemoveAt(i);
                                _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                                _targetContainer.RebuildGlassMeshAndSettleParticles();
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
                    Vector2 localPos = FromCanvas(e.mousePosition);
                    UpdateControlPointFromDrag(_draggingPointIndex, _draggingRightSide, localPos);
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
                        _targetContainer.RebuildContainer();
                        EditorUtility.SetDirty(_targetContainer);
                        SceneView.RepaintAll();
                    }
                    Repaint();
                }
            }

            Handles.EndGUI();
            EditorGUILayout.EndVertical();
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
                {
                    oldRight = Mathf.Max(oldLeft + 0.20f, draggedX);
                }
                else
                {
                    oldLeft = Mathf.Min(oldRight - 0.20f, draggedX);
                }

                cp.centerX = (oldLeft + oldRight) * 0.5f;
                cp.halfWidth = Mathf.Clamp((oldRight - oldLeft) * 0.5f, 0.10f, 1.85f);
            }

            pts[index] = cp;
        }

        private void DrawPresetsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🍾 Hazır Cam Şekli Şablonları (Tek Tıkla Dönüştür)", EditorStyles.boldLabel);

            int cols = 3;
            for (int i = 0; i < PresetLabels.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && (i + j) < PresetLabels.Length; j++)
                {
                    int presetIdx = i + j;
                    GlassShapePreset preset = (GlassShapePreset)presetIdx;
                    bool isActive = (_targetContainer.shapePreset == preset);

                    GUI.backgroundColor = isActive ? new Color(0.25f, 0.85f, 0.55f) : new Color(0.28f, 0.65f, 0.98f);
                    if (GUILayout.Button(PresetLabels[presetIdx], GUILayout.Height(30)))
                    {
                        Undo.RecordObject(_targetContainer, "Apply Glass Shape Preset");
                        _targetContainer.ApplyShapePreset(preset, true);
                        EditorUtility.SetDirty(_targetContainer);
                        SceneView.RepaintAll();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void DrawColorThemeAndAestheticsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("💎 Cam Rengi Temaları & Görünüm Ayarları", EditorStyles.boldLabel);

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

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();

            Color bodyCol = EditorGUILayout.ColorField("Cam Gövde Rengi", _targetContainer.glassBodyColor);
            Color outerRimCol = EditorGUILayout.ColorField("Dış Çerçeve Rengi", _targetContainer.glassOuterRimColor);
            Color innerRimCol = EditorGUILayout.ColorField("İç Kırılma Çerçevesi", _targetContainer.glassInnerRimColor);

            float rimRad = EditorGUILayout.Slider("Dış Çerçeve Kalınlığı", _targetContainer.glassRimRadius, 0.015f, 0.090f);
            float innerInset = EditorGUILayout.Slider("İç Çerçeve Mesafesi", _targetContainer.innerRimInset, 0.025f, 0.120f);
            float shellDepth = EditorGUILayout.Slider("3B Cam Derinliği", _targetContainer.glassShellDepth, 0.02f, 0.25f);
            int subdiv = EditorGUILayout.IntSlider("Eğri Yumuşaklığı", _targetContainer.curveSubdivisions, 4, 24);

            bool showEars = EditorGUILayout.Toggle("Yan Kulpları Göster (Ears)", _targetContainer.showShoulderHandles);
            float earScale = _targetContainer.shoulderHandleScale;
            Vector2 earOffset = _targetContainer.shoulderHandleOffset;
            if (showEars)
            {
                earScale = EditorGUILayout.Slider("Kulp Boyutu", _targetContainer.shoulderHandleScale, 0.4f, 1.8f);
                earOffset = EditorGUILayout.Vector2Field("Kulp Pozisyon Ofseti", _targetContainer.shoulderHandleOffset);
            }

            bool showSpec = EditorGUILayout.Toggle("Parlak Cam Yansımaları", _targetContainer.showSpecularReflections);
            float specInt = _targetContainer.specularIntensity;
            if (showSpec)
            {
                specInt = EditorGUILayout.Slider("Yansıma Parlaklığı", _targetContainer.specularIntensity, 0.1f, 1.0f);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Modify Glass Aesthetics");
                _targetContainer.glassBodyColor = bodyCol;
                _targetContainer.glassOuterRimColor = outerRimCol;
                _targetContainer.glassInnerRimColor = innerRimCol;
                _targetContainer.glassRimRadius = rimRad;
                _targetContainer.innerRimInset = innerInset;
                _targetContainer.glassShellDepth = shellDepth;
                _targetContainer.curveSubdivisions = subdiv;
                _targetContainer.showShoulderHandles = showEars;
                _targetContainer.shoulderHandleScale = earScale;
                _targetContainer.shoulderHandleOffset = earOffset;
                _targetContainer.showSpecularReflections = showSpec;
                _targetContainer.specularIntensity = specInt;

                _targetContainer.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDimensionsAndParticlesSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📐 Genel Ölçek & Partikül Doluluk Ayarları", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float wScale = EditorGUILayout.Slider("Genişlik Ölçeği (Width Scale)", _targetContainer.widthScale, 0.5f, 1.8f);
            float hScale = EditorGUILayout.Slider("Yükseklik Ölçeği (Height Scale)", _targetContainer.heightScale, 0.6f, 1.6f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Scale Glass Shape");
                _targetContainer.widthScale = wScale;
                _targetContainer.heightScale = hScale;
                _targetContainer.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            float pSize = EditorGUILayout.Slider("Partikül Boyutu", _targetContainer.particleSize, 0.08f, 0.22f);
            float pFill = EditorGUILayout.Slider("Cam Doluluk Oranı", _targetContainer.particleFillRatio, 0.15f, 0.85f);
            int pFloat = EditorGUILayout.IntSlider("Havada Süzülen Partiküller", _targetContainer.floatingBeadCount, 0, 20);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_targetContainer, "Change Particle Settings");
                _targetContainer.particleSize = pSize;
                _targetContainer.particleFillRatio = pFill;
                _targetContainer.floatingBeadCount = pFloat;
                _targetContainer.RebuildContainer();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.25f, 0.78f, 1.0f);
            if (GUILayout.Button("🔄 Partikülleri Bu Cam Şekline Göre Yeniden Doldur & Yerleştir", GUILayout.Height(32)))
            {
                _targetContainer.RebuildContainer();
                EditorUtility.SetDirty(_targetContainer);
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        private void DrawContourPointListAndSaveLoadSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📍 Kontrol Noktaları ({_targetContainer.contourPoints.Count} Nokta)", EditorStyles.boldLabel);

            if (GUILayout.Button("+ Nokta Ekle", GUILayout.Width(100)))
            {
                Undo.RecordObject(_targetContainer, "Add Glass Point");
                var pts = _targetContainer.contourPoints;
                int mid = pts.Count / 2;
                float y = (pts[mid - 1].y + pts[mid].y) * 0.5f;
                float hw = (pts[mid - 1].halfWidth + pts[mid].halfWidth) * 0.5f;
                pts.Insert(mid, new GlassContourPoint(y, hw, 0f, 0f));
                _targetContainer.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(_targetContainer);
            }
            EditorGUILayout.EndHorizontal();

            var list = _targetContainer.contourPoints;
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(28));

                EditorGUI.BeginChangeCheck();
                var cp = list[i];
                cp.y = EditorGUILayout.FloatField("Y", cp.y, GUILayout.MinWidth(80));
                cp.halfWidth = Mathf.Max(0.08f, EditorGUILayout.FloatField("Yarı Genişlik", cp.halfWidth, GUILayout.MinWidth(90)));
                if (!_targetContainer.symmetricMode)
                {
                    cp.centerX = EditorGUILayout.FloatField("Merkez X", cp.centerX, GUILayout.MinWidth(80));
                }
                cp.cornerTension = EditorGUILayout.Slider("Köşe Keskinliği", cp.cornerTension, 0f, 1f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_targetContainer, "Edit Contour Point");
                    list[i] = cp;
                    _targetContainer.shapePreset = GlassShapePreset.CustomFreeform;
                    _targetContainer.RebuildGlassMeshAndSettleParticles();
                    EditorUtility.SetDirty(_targetContainer);
                    SceneView.RepaintAll();
                }

                GUI.enabled = list.Count > 4 && i > 0 && i < list.Count - 1;
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    Undo.RecordObject(_targetContainer, "Remove Contour Point");
                    list.RemoveAt(i);
                    _targetContainer.RebuildGlassMeshAndSettleParticles();
                    EditorUtility.SetDirty(_targetContainer);
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.85f, 0.45f);
            if (GUILayout.Button("💾 Şekli JSON Olarak Kaydet", GUILayout.Height(28)))
            {
                SaveCurrentShapeToJson();
            }

            GUI.backgroundColor = new Color(0.95f, 0.75f, 0.25f);
            if (GUILayout.Button("📂 Kayıtlı Şekli Yükle", GUILayout.Height(28)))
            {
                LoadShapeFromJson();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
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
            Debug.Log($"<color=#2ED170><b>[Glass Shape Studio]</b></color> Cam şekli kaydedildi: {path}");
        }

        private void LoadShapeFromJson()
        {
            string dir = "Assets/GlassShapes";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = EditorUtility.OpenFilePanel("Cam Şekli Yükle", dir, "json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

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

            _targetContainer.RebuildContainer();
            EditorUtility.SetDirty(_targetContainer);
            SceneView.RepaintAll();
            Repaint();
        }
    }
}

