using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    [CustomEditor(typeof(PuzzleGlassGameManager))]
    public class PuzzleGlassGameManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PuzzleGlassGameManager manager = (PuzzleGlassGameManager)target;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Interactive Game Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUI.backgroundColor = new Color(0.25f, 0.65f, 1.0f);
            if (GUILayout.Button("⚡ Feed 1 Cube (Spacebar)", GUILayout.Height(36)))
            {
                if (Application.isPlaying)
                {
                    manager.FeedOneCube();
                }
                else
                {
                    Debug.LogWarning("[PuzzleGlass] Lütfen küp yedirme animasyonunu test etmek için Play moduna geçin.");
                }
            }

            string autoText = (manager.cubeDispenser != null && manager.cubeDispenser.isAutoFeeding)
                ? "⏹ Otomatik Yedirmeyi Durdur"
                : "▶ Otomatik Yedirmeyi Başlat (A)";

            GUI.backgroundColor = (manager.cubeDispenser != null && manager.cubeDispenser.isAutoFeeding)
                ? new Color(1.0f, 0.4f, 0.4f)
                : new Color(0.3f, 0.85f, 0.45f);

            if (GUILayout.Button(autoText, GUILayout.Height(32)))
            {
                if (Application.isPlaying)
                {
                    manager.ToggleAutoFeed();
                }
                else
                {
                    Debug.LogWarning("[PuzzleGlass] Lütfen Play modunda test edin.");
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Bölüm (Level) & Cam Tasarım Stüdyoları", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUI.backgroundColor = new Color(0.28f, 0.88f, 0.55f);
            if (GUILayout.Button("🎮 Level Design Studio'yu Aç (Bölüm Tasarım Aracı)", GUILayout.Height(36)))
            {
                LevelDesignerWindow.OpenWindow();
            }

            GUI.backgroundColor = new Color(0.30f, 0.85f, 0.95f);
            if (GUILayout.Button("✨ Cam Şekli Tasarım Stüdyosunu Aç (Glass Shape Studio)", GUILayout.Height(34)))
            {
                GlassShapeDesignerWindow.OpenForContainer(manager.glassContainer);
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("1. Referans Sahne (Mavi 10 + Sarı 20)", GUILayout.Height(28)))
            {
                manager.SetupReferenceScenario();
            }

            if (GUILayout.Button("2. Aynı Renk Testi (2x Mavi - İzole Kuyruk)", GUILayout.Height(28)))
            {
                manager.SetupSameColorScenario();
            }

            if (GUILayout.Button("3. 4 Renk Varyasyonu (Mavi, Sarı, Kırmızı, Yeşil)", GUILayout.Height(28)))
            {
                manager.SetupFourColorsScenario();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Sahneyi Sıfırla / Yeniden İnşa Et", GUILayout.Height(26)))
            {
                PuzzleGlassSceneSetup.BuildCompleteScene();
            }

            if (GUILayout.Button("Prefab ve Materyalleri Diske Kaydet", GUILayout.Height(26)))
            {
                PuzzleGlassPrefabGenerator.GenerateAllAssets();
            }
        }
    }

    [CustomEditor(typeof(MonsterCharacter))]
    public class MonsterCharacterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            MonsterCharacter monster = (MonsterCharacter)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Yaratık Testleri", EditorStyles.boldLabel);

            if (GUILayout.Button("Yaratık Görünümünü Yeniden Oluştur", GUILayout.Height(28)))
            {
                monster.RebuildMonsterVisuals();
            }

            if (Application.isPlaying)
            {
                if (GUILayout.Button("Küp Yutma Animasyonu Testi (1 Küp)", GUILayout.Height(28)))
                {
                    monster.FeedCube();
                }
            }
        }
    }

    [CustomEditor(typeof(ShapedGlassContainer))]
    public class ShapedGlassContainerEditor : UnityEditor.Editor
    {
        private static bool _enableSceneHandles = true;

        private static readonly string[] QuickPresetNames = new string[]
        {
            "🔵 Bombeli Flask",
            "🧪 Klasik Şişe",
            "⏳ Kum Saati",
            "💙 Kalp Şişe",
            "💎 Elmas Karaf",
            "📐 Konik Şişe",
            "⛄ Çift Boğum",
            "🏺 Amfora Vazo",
            "🐍 S-Kıvrım Tüp"
        };

        public override void OnInspectorGUI()
        {
            ShapedGlassContainer container = (ShapedGlassContainer)target;
            container.EnsureContourInitialized();

            // Prominent Studio Launchers at top of Inspector
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = new Color(0.28f, 0.88f, 0.55f);
            if (GUILayout.Button("🎮 Level Design Studio'yu Aç (Bölüm Tasarım Aracı)", GUILayout.Height(34)))
            {
                LevelDesignerWindow.OpenWindow();
            }

            GUI.backgroundColor = new Color(0.22f, 0.82f, 0.98f);
            if (GUILayout.Button("✨ Cam Şekli Tasarım Stüdyosunu Aç (Glass Shape Studio)", GUILayout.Height(36)))
            {
                GlassShapeDesignerWindow.OpenForContainer(container);
            }

            GUI.backgroundColor = _enableSceneHandles ? new Color(0.35f, 0.90f, 0.55f) : new Color(0.75f, 0.75f, 0.75f);
            if (GUILayout.Button(
                _enableSceneHandles
                    ? "🟢 Sahne Üzerinde Nokta Düzenleme: AÇIK (Scene View'da Sürükleyin)"
                    : "⚪ Sahne Üzerinde Nokta Düzenleme: KAPALI (Açmak İçin Tıklayın)",
                GUILayout.Height(28)))
            {
                _enableSceneHandles = !_enableSceneHandles;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            // Quick Shape Preset Grid right in Inspector
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("🍾 Hızlı Cam Şekli Şablonları", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            int cols = 3;
            for (int i = 0; i < QuickPresetNames.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && (i + j) < QuickPresetNames.Length; j++)
                {
                    int idx = i + j;
                    GlassShapePreset preset = (GlassShapePreset)idx;
                    GUI.backgroundColor = (container.shapePreset == preset)
                        ? new Color(0.28f, 0.88f, 0.55f)
                        : new Color(0.35f, 0.70f, 0.98f);
                    if (GUILayout.Button(QuickPresetNames[idx], GUILayout.Height(26)))
                    {
                        Undo.RecordObject(container, "Apply Glass Shape Preset");
                        container.ApplyShapePreset(preset, true);
                        EditorUtility.SetDirty(container);
                        SceneView.RepaintAll();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                container.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(container);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = new Color(0.25f, 0.75f, 1.0f);
            if (GUILayout.Button("🔄 Cam Şişeyi & Partikülleri Yeniden İnşa Et", GUILayout.Height(36)))
            {
                container.RebuildContainer();
                EditorUtility.SetDirty(container);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void OnSceneGUI()
        {
            ShapedGlassContainer container = (ShapedGlassContainer)target;
            if (container == null) return;
            container.EnsureContourInitialized();

            // Floating SceneView Overlay Toolbar
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(14, 14, 290, 145), EditorStyles.helpBox);
            GUILayout.Label("✨ Cam Şekli Tasarım Aracı (Scene View)", EditorStyles.boldLabel);

            _enableSceneHandles = GUILayout.Toggle(_enableSceneHandles, " Sahne Noktalarını Sürükleyerek Düzenle");

            EditorGUI.BeginChangeCheck();
            GlassShapePreset selectedPreset = (GlassShapePreset)EditorGUILayout.EnumPopup("Hızlı Şablon:", container.shapePreset);
            if (EditorGUI.EndChangeCheck() && selectedPreset != GlassShapePreset.CustomFreeform)
            {
                Undo.RecordObject(container, "Select Glass Preset");
                container.ApplyShapePreset(selectedPreset, true);
                EditorUtility.SetDirty(container);
            }

            EditorGUI.BeginChangeCheck();
            bool sym = GUILayout.Toggle(container.symmetricMode, " Simetrik Mod (Sol & Sağ Eşit)");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(container, "Toggle Symmetry");
                container.symmetricMode = sym;
                container.RebuildGlassMeshAndSettleParticles();
                EditorUtility.SetDirty(container);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎨 Stüdyo Penceresini Aç", GUILayout.Height(24)))
            {
                GlassShapeDesignerWindow.OpenForContainer(container);
            }
            if (GUILayout.Button("🔄 Partikülleri Doldur", GUILayout.Height(24)))
            {
                container.RebuildContainer();
                EditorUtility.SetDirty(container);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            Handles.EndGUI();

            if (!_enableSceneHandles) return;

            Transform tr = container.transform;
            var pts = container.contourPoints;
            float wScale = Mathf.Max(0.1f, container.widthScale);
            float hScale = Mathf.Max(0.1f, container.heightScale);

            // Draw smooth contour guide curves in Scene View
            if (container.cachedLeftOuterPath != null && container.cachedRightOuterPath != null)
            {
                Handles.color = new Color(0.25f, 0.95f, 1.0f, 0.85f);
                for (int i = 0; i < container.cachedLeftOuterPath.Length - 1; i++)
                {
                    Vector3 l0 = tr.TransformPoint(container.cachedLeftOuterPath[i] + new Vector3(0f, 0f, -0.18f));
                    Vector3 l1 = tr.TransformPoint(container.cachedLeftOuterPath[i + 1] + new Vector3(0f, 0f, -0.18f));
                    Handles.DrawAAPolyLine(3f, l0, l1);

                    Vector3 r0 = tr.TransformPoint(container.cachedRightOuterPath[i] + new Vector3(0f, 0f, -0.18f));
                    Vector3 r1 = tr.TransformPoint(container.cachedRightOuterPath[i + 1] + new Vector3(0f, 0f, -0.18f));
                    Handles.DrawAAPolyLine(3f, r0, r1);
                }
            }

            // Draw interactive draggable 3D handles for every control point
            for (int i = 0; i < pts.Count; i++)
            {
                var cp = pts[i];
                float cx = (container.symmetricMode ? 0f : cp.centerX) * wScale;
                float hw = cp.halfWidth * wScale;
                float y = cp.y * hScale;

                Vector3 leftLocal = new Vector3(cx - hw, y, -0.20f);
                Vector3 rightLocal = new Vector3(cx + hw, y, -0.20f);

                Vector3 leftWorld = tr.TransformPoint(leftLocal);
                Vector3 rightWorld = tr.TransformPoint(rightLocal);

                Handles.color = new Color(1f, 1f, 1f, 0.25f);
                Handles.DrawDottedLine(leftWorld, rightWorld, 2.5f);

                float handleSize = HandleUtility.GetHandleSize(rightWorld) * 0.085f;
                Handles.color = new Color(1.0f, 0.88f, 0.20f, 1.0f);

                // Right handle
                EditorGUI.BeginChangeCheck();
                Vector3 newRightWorld = Handles.Slider2D(
                    rightWorld,
                    Vector3.forward,
                    Vector3.right,
                    Vector3.up,
                    handleSize,
                    Handles.SphereHandleCap,
                    0f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(container, "Drag Right Glass Handle");
                    Vector3 newRightLocal = tr.InverseTransformPoint(newRightWorld);
                    ApplySceneHandleDrag(container, i, true, new Vector2(newRightLocal.x, newRightLocal.y));
                }

                // Left handle
                Handles.color = new Color(0.25f, 0.95f, 1.0f, 1.0f);
                EditorGUI.BeginChangeCheck();
                Vector3 newLeftWorld = Handles.Slider2D(
                    leftWorld,
                    Vector3.forward,
                    Vector3.right,
                    Vector3.up,
                    handleSize,
                    Handles.SphereHandleCap,
                    0f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(container, "Drag Left Glass Handle");
                    Vector3 newLeftLocal = tr.InverseTransformPoint(newLeftWorld);
                    ApplySceneHandleDrag(container, i, false, new Vector2(newLeftLocal.x, newLeftLocal.y));
                }
            }
        }

        private void ApplySceneHandleDrag(ShapedGlassContainer container, int index, bool isRight, Vector2 localPos)
        {
            var pts = container.contourPoints;
            var cp = pts[index];
            float wScale = Mathf.Max(0.1f, container.widthScale);
            float hScale = Mathf.Max(0.1f, container.heightScale);

            float targetY = localPos.y / hScale;
            if (index > 0) targetY = Mathf.Max(targetY, pts[index - 1].y + 0.04f);
            if (index < pts.Count - 1) targetY = Mathf.Min(targetY, pts[index + 1].y - 0.04f);
            cp.y = targetY;

            if (container.symmetricMode)
            {
                cp.centerX = 0f;
                cp.halfWidth = Mathf.Clamp(Mathf.Abs(localPos.x) / wScale, 0.10f, 1.85f);
            }
            else
            {
                float oldLeft = cp.centerX - cp.halfWidth;
                float oldRight = cp.centerX + cp.halfWidth;
                float draggedX = localPos.x / wScale;

                if (isRight) oldRight = Mathf.Max(oldLeft + 0.20f, draggedX);
                else oldLeft = Mathf.Min(oldRight - 0.20f, draggedX);

                cp.centerX = (oldLeft + oldRight) * 0.5f;
                cp.halfWidth = Mathf.Clamp((oldRight - oldLeft) * 0.5f, 0.10f, 1.85f);
            }

            pts[index] = cp;
            container.shapePreset = GlassShapePreset.CustomFreeform;
            container.RebuildGlassMeshAndSettleParticles();
            EditorUtility.SetDirty(container);
        }
    }
}
